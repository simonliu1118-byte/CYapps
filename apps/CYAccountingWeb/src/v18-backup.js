import { handleV17Api, runScheduledBackup as runLegacyScheduledBackup, buildV17BackupSet, storeV17BackupSet } from './v17-backup.js';
import { createGcsBackupStorageProvider, gcsConfigReady } from './gcs-backup-provider.js';
import { createR2BackupStorageProvider, r2ConfigReady } from './r2-backup-provider.js';

const LEGACY_GCS = 'legacy_gcs';
const PARALLEL_DUAL_PROVIDER = 'parallel_dual_provider';
const APP_ID = 'CYAccountingWeb';
const R2_PROVIDER = 'cloudflare_r2';
const GCS_PROVIDER = 'google_cloud_storage';
const R2_RETENTION_DAYS = 30;
const GCS_PHASE_C_RETENTION_DAYS = 14;
const PHASE_C_REQUIRED_SCHEDULED_BACKUPS = 14;

export function resolveTieredBackupTopology(env) {
  const value = String(env?.BACKUP_TOPOLOGY || '').trim();
  if (!value || value === LEGACY_GCS) return LEGACY_GCS;
  if (value === PARALLEL_DUAL_PROVIDER) return PARALLEL_DUAL_PROVIDER;
  throw new TieredBackupError('不支援的備份拓撲設定。', 'BACKUP_TOPOLOGY_UNSUPPORTED');
}

export async function handleV18Api(request, env, session) {
  const topology = resolveTieredBackupTopology(env);
  if (topology === LEGACY_GCS) return handleV17Api(request, env, session);

  const url = new URL(request.url);
  if (!url.pathname.startsWith('/api/backup/')) return null;
  if (String(session?.role || '') !== 'SUPER_ADMIN') {
    return json({ ok: false, error: '只有超級管理員可以管理備份與復原。', code: 'SUPER_ADMIN_REQUIRED' }, 403);
  }
  if (url.pathname === '/api/backup/status' && request.method === 'GET') {
    return parallelBackupStatus(env);
  }
  if (url.pathname === '/api/backup/run' && request.method === 'POST') {
    try {
      const result = await runParallelBackup(env, 'manual');
      return json({ ok: true, backup: result });
    } catch (error) {
      return json({
        ok: false,
        error: safeMessage(error),
        code: safeCode(error),
        backup: error?.backupResult || null
      }, 500);
    }
  }
  return null;
}

export async function runScheduledTieredBackup(env) {
  try {
    const topology = resolveTieredBackupTopology(env);
    if (topology === LEGACY_GCS) return runLegacyScheduledBackup(env);
    if (!env?.DB || !gcsConfigReady(env) || !r2ConfigReady(env)) {
      return { skipped: true, reason: 'not_configured' };
    }
    return await runParallelBackup(env, 'scheduled');
  } catch (error) {
    console.error('cyaccounting_tiered_backup_failed', safeCode(error));
    return { ok: false, error: safeCode(error) };
  }
}

export async function runParallelBackup(env, triggerKind = 'scheduled', options = {}) {
  if (!['scheduled', 'manual'].includes(triggerKind)) {
    throw new TieredBackupError('備份觸發類型錯誤。', 'INVALID_TRIGGER');
  }
  if (!env?.DB) throw new TieredBackupError('D1 尚未綁定。', 'DB_NOT_CONFIGURED');
  if ((options.topology || resolveTieredBackupTopology(env)) !== PARALLEL_DUAL_PROVIDER) {
    throw new TieredBackupError('Phase C 雙 provider 尚未啟用。', 'PARALLEL_TOPOLOGY_REQUIRED');
  }

  const now = options.now instanceof Date ? options.now : new Date();
  const backupSet = await buildV17BackupSet(env.DB, now);
  const packageSha256 = await backupPackageDigest(backupSet);
  await insertLogicalBackup(env.DB, backupSet, triggerKind, packageSha256);

  const specs = [
    {
      provider: R2_PROVIDER,
      retentionDays: R2_RETENTION_DAYS,
      create: () => options.r2Provider || createR2BackupStorageProvider(env)
    },
    {
      provider: GCS_PROVIDER,
      retentionDays: GCS_PHASE_C_RETENTION_DAYS,
      create: () => options.gcsProvider || createGcsBackupStorageProvider(env, options.gcsProviderOptions)
    }
  ];

  const copies = [];
  for (const spec of specs) {
    copies.push(await executeProviderCopy(env.DB, backupSet, spec, now));
  }

  const gcsCopy = copies.find(copy => copy.provider === GCS_PROVIDER);
  await writeLegacyGcsRun(env.DB, backupSet, triggerKind, now, gcsCopy);

  const completedAt = new Date().toISOString();
  const result = {
    ok: copies.every(copy => copy.status === 'success'),
    backupId: backupSet.backupId,
    completedAt,
    dataSha256: backupSet.dataSha256,
    manifestSha256: backupSet.manifestSha256,
    packageSha256,
    rowCount: backupSet.totalRowCount,
    byteSize: backupSet.totalByteSize,
    copies
  };

  if (!result.ok) {
    const error = new TieredBackupError('備份只有部分 provider 成功，請查看 R2／GCS 個別狀態。', 'BACKUP_COPY_PARTIAL');
    error.backupResult = result;
    throw error;
  }
  return result;
}

async function executeProviderCopy(db, backupSet, spec, now) {
  const startedAt = new Date().toISOString();
  await db.prepare(`
    INSERT INTO backup_copies(
      backup_id, provider, status, started_at, completed_at, verified_at,
      storage_prefix, version_token, last_error
    ) VALUES (?, ?, 'pending', ?, NULL, NULL, ?, '', '')
    ON CONFLICT(backup_id, provider) DO UPDATE SET
      status = 'pending', started_at = excluded.started_at, completed_at = NULL,
      verified_at = NULL, storage_prefix = excluded.storage_prefix,
      version_token = '', last_error = ''
  `).bind(backupSet.backupId, spec.provider, startedAt, backupSet.prefix).run();

  try {
    const provider = spec.create();
    const stored = await storeV17BackupSet(provider, backupSet, {
      now,
      retentionDays: spec.retentionDays,
      cleanup: true
    });
    const completedAt = new Date().toISOString();
    const versionToken = String(stored?.manifestVersionToken || stored?.dataVersionToken || '');
    await db.prepare(`
      UPDATE backup_copies
      SET status = 'success', completed_at = ?, verified_at = ?, version_token = ?, last_error = ''
      WHERE backup_id = ? AND provider = ?
    `).bind(completedAt, completedAt, versionToken, backupSet.backupId, spec.provider).run();
    return { provider: spec.provider, status: 'success', verifiedAt: completedAt, versionToken };
  } catch (error) {
    const completedAt = new Date().toISOString();
    const lastError = truncate(safeMessage(error));
    await db.prepare(`
      UPDATE backup_copies
      SET status = 'failed', completed_at = ?, verified_at = NULL, last_error = ?
      WHERE backup_id = ? AND provider = ?
    `).bind(completedAt, lastError, backupSet.backupId, spec.provider).run();
    return { provider: spec.provider, status: 'failed', error: lastError };
  }
}

async function insertLogicalBackup(db, backupSet, triggerKind, packageSha256) {
  await db.prepare(`
    INSERT INTO backup_sets(
      backup_id, app_id, created_at, app_version, schema_version, format, format_version,
      data_sha256, manifest_sha256, package_sha256, data_byte_length,
      manifest_byte_length, total_byte_length, record_count, trigger_kind
    ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
  `).bind(
    backupSet.backupId,
    APP_ID,
    String(backupSet.manifest?.createdAt || new Date().toISOString()),
    String(backupSet.manifest?.appVersion || ''),
    Number(backupSet.manifest?.schemaVersion || 0),
    String(backupSet.manifest?.format || ''),
    Number(backupSet.manifest?.formatVersion || 0),
    backupSet.dataSha256,
    backupSet.manifestSha256,
    packageSha256,
    backupSet.dataBytes.byteLength,
    backupSet.manifestBytes.byteLength,
    backupSet.totalByteSize,
    backupSet.totalRowCount,
    triggerKind
  ).run();
}

async function writeLegacyGcsRun(db, backupSet, triggerKind, now, copy) {
  const success = copy?.status === 'success';
  const completedAt = new Date().toISOString();
  await db.prepare(`
    INSERT INTO backup_runs(
      provider, trigger_kind, status, started_at, completed_at, file_id, file_name,
      data_sha256, file_sha256, row_count, byte_size, error_message
    ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
  `).bind(
    GCS_PROVIDER,
    triggerKind,
    success ? 'success' : 'failed',
    now.toISOString(),
    completedAt,
    backupSet.prefix,
    backupSet.backupId,
    backupSet.dataSha256,
    backupSet.manifestSha256,
    backupSet.totalRowCount,
    backupSet.totalByteSize,
    success ? '' : truncate(copy?.error || 'GCS copy failed')
  ).run();
}

export async function parallelBackupStatus(env) {
  const configured = gcsConfigReady(env) && r2ConfigReady(env);
  const result = await env.DB.prepare(`
    SELECT backup_id, created_at, app_version, schema_version, format, format_version,
           data_sha256, manifest_sha256, package_sha256, total_byte_length,
           record_count, trigger_kind
    FROM backup_sets
    ORDER BY created_at DESC
    LIMIT 8
  `).all();

  const logicalBackups = [];
  for (const row of result.results || []) {
    const copiesResult = await env.DB.prepare(`
      SELECT provider, status, started_at, completed_at, verified_at,
             storage_prefix, version_token, last_error
      FROM backup_copies
      WHERE backup_id = ?
      ORDER BY provider
    `).bind(row.backup_id).all();
    const copies = (copiesResult.results || []).map(normalizeCopyRow);
    logicalBackups.push({
      backupId: String(row.backup_id),
      createdAt: String(row.created_at),
      appVersion: String(row.app_version),
      schemaVersion: Number(row.schema_version || 0),
      format: String(row.format),
      formatVersion: Number(row.format_version || 0),
      dataSha256: String(row.data_sha256),
      manifestSha256: String(row.manifest_sha256),
      packageSha256: String(row.package_sha256),
      byteSize: Number(row.total_byte_length || 0),
      rowCount: Number(row.record_count || 0),
      trigger: String(row.trigger_kind || ''),
      status: copies.length >= 2 && copies.every(copy => copy.status === 'success') ? 'success' : 'failed',
      copies
    });
  }

  const acceptanceResult = await env.DB.prepare(`
    SELECT bs.backup_id, bs.created_at, bs.package_sha256,
           MAX(CASE WHEN bc.provider = ? AND bc.status = 'success' THEN 1 ELSE 0 END) AS r2_success,
           MAX(CASE WHEN bc.provider = ? AND bc.status = 'success' THEN 1 ELSE 0 END) AS gcs_success
    FROM backup_sets bs
    LEFT JOIN backup_copies bc ON bc.backup_id = bs.backup_id
    WHERE bs.trigger_kind = 'scheduled'
    GROUP BY bs.backup_id, bs.created_at, bs.package_sha256
    ORDER BY bs.created_at DESC
    LIMIT 14
  `).bind(R2_PROVIDER, GCS_PROVIDER).all();
  const phaseCAcceptance = summarizePhaseCAcceptance(acceptanceResult.results || []);

  const latestSuccess = logicalBackups.find(item => item.status === 'success') || null;
  return json({
    ok: true,
    provider: 'tiered',
    topology: PARALLEL_DUAL_PROVIDER,
    configured,
    providers: {
      [R2_PROVIDER]: { configured: r2ConfigReady(env), retentionDays: R2_RETENTION_DAYS, role: 'operational' },
      [GCS_PROVIDER]: { configured: gcsConfigReady(env), retentionDays: GCS_PHASE_C_RETENTION_DAYS, role: 'cross_cloud_validation' }
    },
    schedule: { cron: '30 19 * * *', localTime: '每日 03:30（台灣時間）' },
    retentionDays: GCS_PHASE_C_RETENTION_DAYS,
    phaseCAcceptance,
    latestSuccess: latestSuccess ? logicalToLegacyRun(latestSuccess) : null,
    recentRuns: logicalBackups.map(logicalToLegacyRun),
    logicalBackups,
    restoreAvailable: false
  });
}

export function summarizePhaseCAcceptance(rows, required = PHASE_C_REQUIRED_SCHEDULED_BACKUPS) {
  const requiredCount = Math.max(1, Number(required) || PHASE_C_REQUIRED_SCHEDULED_BACKUPS);
  const candidates = Array.isArray(rows) ? rows : [];
  let consecutive = 0;

  for (const row of candidates) {
    if (consecutive >= requiredCount) break;
    const r2Success = Number(row?.r2_success ?? row?.r2Success ?? 0) === 1;
    const gcsSuccess = Number(row?.gcs_success ?? row?.gcsSuccess ?? 0) === 1;
    const packageSha256 = String(row?.package_sha256 ?? row?.packageSha256 ?? '');
    if (!(r2Success && gcsSuccess && /^[0-9a-f]{64}$/i.test(packageSha256))) break;
    consecutive += 1;
  }

  const latest = candidates[0] || null;
  return {
    requiredConsecutiveScheduled: requiredCount,
    consecutiveScheduledSuccesses: consecutive,
    remaining: Math.max(0, requiredCount - consecutive),
    completed: consecutive >= requiredCount,
    latestScheduledBackupId: latest ? String(latest.backup_id ?? latest.backupId ?? '') : null,
    latestScheduledAt: latest ? String(latest.created_at ?? latest.createdAt ?? '') : null,
    manualRunsCount: false
  };
}

function logicalToLegacyRun(item) {
  const failed = item.copies.find(copy => copy.status !== 'success');
  return {
    trigger: item.trigger,
    status: item.status,
    completedAt: item.createdAt,
    fileName: item.backupId,
    dataSha256: item.dataSha256,
    fileSha256: item.manifestSha256,
    rowCount: item.rowCount,
    byteSize: item.byteSize,
    errorMessage: failed?.lastError || ''
  };
}

function normalizeCopyRow(row) {
  return {
    provider: String(row.provider || ''),
    status: String(row.status || ''),
    startedAt: row.started_at || null,
    completedAt: row.completed_at || null,
    verifiedAt: row.verified_at || null,
    storagePrefix: String(row.storage_prefix || ''),
    versionToken: String(row.version_token || ''),
    lastError: String(row.last_error || '')
  };
}

export async function backupPackageDigest(backupSet) {
  const combined = new Uint8Array(backupSet.manifestBytes.byteLength + backupSet.dataBytes.byteLength);
  combined.set(backupSet.manifestBytes, 0);
  combined.set(backupSet.dataBytes, backupSet.manifestBytes.byteLength);
  const digest = new Uint8Array(await crypto.subtle.digest('SHA-256', combined));
  return Array.from(digest, byte => byte.toString(16).padStart(2, '0')).join('');
}

function safeMessage(error) {
  return error instanceof Error && error.message ? error.message : '備份處理失敗，請稍後再試。';
}

function safeCode(error) {
  return String(error?.code || 'BACKUP_FAILED');
}

function truncate(value) {
  return String(value || '').slice(0, 500);
}

export class TieredBackupError extends Error {
  constructor(message, code) {
    super(message);
    this.name = 'TieredBackupError';
    this.code = code;
  }
}

function json(data, status = 200) {
  return new Response(JSON.stringify(data), {
    status,
    headers: {
      'content-type': 'application/json; charset=utf-8',
      'cache-control': 'no-store',
      'x-content-type-options': 'nosniff'
    }
  });
}
