import { buildBackupPackage } from './v16-backup.js';
import { assertBackupStorageProvider } from './backup-storage-provider.js';
import { createGcsBackupStorageProvider, gcsConfigReady, GcsProviderError } from './gcs-backup-provider.js';

const BACKUP_PROVIDER = 'google_cloud_storage';
const APP_VERSION = '0.17.0';
const BACKUP_FORMAT = 'CYAccountingWebBackupSet';
const BACKUP_FORMAT_VERSION = 2;
const BACKUP_RETENTION_DAYS = 14;
const BACKUP_ROOT_PREFIX = 'CYAccountingWeb/';
const LEGACY_GCS_TOPOLOGY = 'legacy_gcs';
const encoder = new TextEncoder();
const decoder = new TextDecoder();

export async function handleV17Api(request, env, session) {
  const url = new URL(request.url);
  if (!url.pathname.startsWith('/api/backup/')) return null;
  if (String(session?.role || '') !== 'SUPER_ADMIN') {
    return json({ ok: false, error: '只有超級管理員可以管理備份與復原。', code: 'SUPER_ADMIN_REQUIRED' }, 403);
  }

  if (url.pathname === '/api/backup/status' && request.method === 'GET') {
    return backupStatus(env);
  }
  if (url.pathname === '/api/backup/run' && request.method === 'POST') {
    try {
      const result = await runBackup(env, 'manual');
      return json({ ok: true, backup: result });
    } catch (error) {
      return json({ ok: false, error: safeBackupMessage(error), code: safeErrorCode(error) }, 500);
    }
  }
  return null;
}

export async function runScheduledBackup(env) {
  try {
    if (!env?.DB || !gcsConfigReady(env)) return { skipped: true, reason: 'not_configured' };
    return await runBackup(env, 'scheduled');
  } catch (error) {
    console.error('cyaccounting_scheduled_backup_failed', safeErrorCode(error));
    return { ok: false, error: safeErrorCode(error) };
  }
}

async function backupStatus(env) {
  const configured = gcsConfigReady(env);
  const topology = resolveBackupTopology(env);
  const [latestSuccess, recentRuns] = await Promise.all([
    env.DB.prepare(`
      SELECT id, completed_at, file_name, data_sha256, file_sha256, row_count, byte_size
      FROM backup_runs
      WHERE provider = ? AND status = 'success'
      ORDER BY id DESC LIMIT 1
    `).bind(BACKUP_PROVIDER).first(),
    env.DB.prepare(`
      SELECT id, trigger_kind, status, started_at, completed_at, file_name, row_count, byte_size, error_message
      FROM backup_runs
      WHERE provider = ?
      ORDER BY id DESC LIMIT 8
    `).bind(BACKUP_PROVIDER).all()
  ]);

  return json({
    ok: true,
    provider: BACKUP_PROVIDER,
    topology,
    configured,
    schedule: { cron: '30 19 * * *', localTime: '每日 03:30（台灣時間）' },
    retentionDays: BACKUP_RETENTION_DAYS,
    latestSuccess: latestSuccess ? normalizeRunRow(latestSuccess) : null,
    recentRuns: (recentRuns.results || []).map(normalizeRunRow),
    restoreAvailable: false
  });
}

/**
 * Phase A/B safety switch. Until Phase C is explicitly introduced, production
 * stays on the accepted V0.17 GCS path. Future topology names are deliberately
 * rejected rather than silently changing behavior.
 */
export function resolveBackupTopology(env) {
  const value = String(env?.BACKUP_TOPOLOGY || '').trim();
  if (!value || value === LEGACY_GCS_TOPOLOGY) return LEGACY_GCS_TOPOLOGY;
  throw new BackupError('目前部署僅允許既有 GCS 備份拓撲。', 'BACKUP_TOPOLOGY_UNSUPPORTED');
}

export async function runBackup(env, triggerKind = 'scheduled', options = {}) {
  if (!['scheduled', 'manual'].includes(triggerKind)) {
    throw new BackupError('備份觸發類型錯誤。', 'INVALID_TRIGGER');
  }
  if (!env?.DB) throw new BackupError('D1 尚未綁定。', 'DB_NOT_CONFIGURED');
  resolveBackupTopology(env);

  const provider = assertBackupStorageProvider(options.provider || createGcsBackupStorageProvider(env, options.providerOptions));
  const now = options.now instanceof Date ? options.now : new Date();
  const startedAt = now.toISOString();
  let backupSet = null;

  try {
    // Phase B invariant: one logical backup event performs the D1 export once.
    backupSet = await buildV17BackupSet(env.DB, now);
    await storeV17BackupSet(provider, backupSet, {
      now,
      retentionDays: BACKUP_RETENTION_DAYS,
      cleanup: true
    });

    const completedAt = new Date().toISOString();
    await env.DB.prepare(`
      INSERT INTO backup_runs(
        provider, trigger_kind, status, started_at, completed_at, file_id, file_name,
        data_sha256, file_sha256, row_count, byte_size, error_message
      ) VALUES (?, ?, 'success', ?, ?, ?, ?, ?, ?, ?, ?, '')
    `).bind(
      BACKUP_PROVIDER,
      triggerKind,
      startedAt,
      completedAt,
      backupSet.prefix,
      backupSet.backupId,
      backupSet.dataSha256,
      backupSet.manifestSha256,
      backupSet.totalRowCount,
      backupSet.totalByteSize
    ).run();

    return {
      ok: true,
      backupId: backupSet.backupId,
      fileName: backupSet.backupId,
      completedAt,
      dataSha256: backupSet.dataSha256,
      manifestSha256: backupSet.manifestSha256,
      rowCount: backupSet.totalRowCount,
      byteSize: backupSet.totalByteSize
    };
  } catch (error) {
    const completedAt = new Date().toISOString();
    try {
      await env.DB.prepare(`
        INSERT INTO backup_runs(
          provider, trigger_kind, status, started_at, completed_at, file_id, file_name,
          data_sha256, file_sha256, row_count, byte_size, error_message
        ) VALUES (?, ?, 'failed', ?, ?, ?, ?, ?, ?, ?, ?, ?)
      `).bind(
        BACKUP_PROVIDER,
        triggerKind,
        startedAt,
        completedAt,
        backupSet?.prefix || '',
        backupSet?.backupId || '',
        backupSet?.dataSha256 || '',
        backupSet?.manifestSha256 || '',
        backupSet?.totalRowCount || 0,
        backupSet?.totalByteSize || 0,
        truncateError(safeBackupMessage(error))
      ).run();
    } catch (logError) {
      console.error('cyaccounting_backup_failure_log_failed', safeErrorCode(logError));
    }
    throw error;
  }
}

/**
 * Storage execution is intentionally independent from D1 export/package build.
 * The same immutable BackupSet can be passed to more than one provider in Phase C
 * without calling buildBackupPackage() again.
 */
export async function storeV17BackupSet(provider, backupSet, options = {}) {
  assertBackupStorageProvider(provider);
  assertBackupSetShape(backupSet);
  const now = options.now instanceof Date ? options.now : new Date();
  const retentionDays = Number(options.retentionDays ?? BACKUP_RETENTION_DAYS);
  const cleanup = options.cleanup !== false;

  const dataUpload = await provider.putObject(backupSet.dataKey, backupSet.dataBytes, {
    backupId: backupSet.backupId,
    role: 'data',
    sha256: backupSet.dataSha256,
    app: 'CYAccountingWeb'
  });
  const manifestUpload = await provider.putObject(backupSet.manifestKey, backupSet.manifestBytes, {
    backupId: backupSet.backupId,
    role: 'manifest',
    sha256: backupSet.manifestSha256,
    app: 'CYAccountingWeb'
  });

  try {
    await verifyBackupSet(provider, backupSet);
  } catch (verifyError) {
    await bestEffortDelete(provider, backupSet.dataKey, dataUpload?.versionToken);
    await bestEffortDelete(provider, backupSet.manifestKey, manifestUpload?.versionToken);
    throw verifyError;
  }

  if (cleanup && Number.isFinite(retentionDays) && retentionDays > 0) {
    try {
      await cleanupExpiredBackups(provider, retentionDays, now);
    } catch (cleanupError) {
      console.warn('cyaccounting_backup_retention_cleanup_failed', safeErrorCode(cleanupError));
    }
  }

  return {
    ok: true,
    backupId: backupSet.backupId,
    dataVersionToken: String(dataUpload?.versionToken || ''),
    manifestVersionToken: String(manifestUpload?.versionToken || '')
  };
}

export async function buildV17BackupSet(db, now = new Date()) {
  const base = await buildBackupPackage(db, now);
  const createdAt = now.toISOString();
  const backupId = createdAt.replace(/[-:]/g, '').replace(/\.\d{3}Z$/, 'Z');
  const prefix = `${BACKUP_ROOT_PREFIX}${backupId}/`;
  const dataKey = `${prefix}data.json`;
  const manifestKey = `${prefix}manifest.json`;

  const dataText = JSON.stringify(base.data, null, 2) + '\n';
  const dataBytes = encoder.encode(dataText);
  const dataSha256 = await sha256HexBytes(dataBytes);
  const counts = Object.fromEntries(Object.entries(base.data).map(([key, rows]) => [key, Array.isArray(rows) ? rows.length : 0]));
  const totalRowCount = Object.values(counts).reduce((sum, value) => sum + Number(value || 0), 0);

  const manifest = {
    format: BACKUP_FORMAT,
    formatVersion: BACKUP_FORMAT_VERSION,
    app: 'CYAccountingWeb',
    appVersion: APP_VERSION,
    schemaVersion: Number(base.manifest?.schemaVersion || 0),
    backupId,
    createdAt,
    counts,
    totalRowCount,
    files: {
      data: {
        name: 'data.json',
        sha256: dataSha256,
        byteSize: dataBytes.byteLength
      }
    }
  };
  const manifestText = JSON.stringify(manifest, null, 2) + '\n';
  const manifestBytes = encoder.encode(manifestText);
  const manifestSha256 = await sha256HexBytes(manifestBytes);

  return Object.freeze({
    backupId,
    prefix,
    dataKey,
    manifestKey,
    manifest,
    data: base.data,
    dataBytes,
    manifestBytes,
    dataSha256,
    manifestSha256,
    totalRowCount,
    totalByteSize: dataBytes.byteLength + manifestBytes.byteLength
  });
}

/**
 * Compatibility reader for accepted V0.17 backup objects. This remains available
 * when a future common CYBackupSet outer format is introduced.
 */
export async function validateV17BackupSetBytes(manifestBytes, dataBytes, expectedBackupId = '') {
  const [dataSha256, manifestSha256] = await Promise.all([
    sha256HexBytes(dataBytes),
    sha256HexBytes(manifestBytes)
  ]);

  let manifest;
  let data;
  try {
    manifest = JSON.parse(decoder.decode(manifestBytes));
    data = JSON.parse(decoder.decode(dataBytes));
  } catch {
    throw new BackupError('備份 JSON 無法解析。', 'BACKUP_JSON_INVALID');
  }

  const backupId = String(manifest?.backupId || '');
  if (manifest?.format !== BACKUP_FORMAT || Number(manifest?.formatVersion) !== BACKUP_FORMAT_VERSION
      || manifest?.app !== 'CYAccountingWeb' || !/^\d{8}T\d{6}Z$/.test(backupId)
      || (expectedBackupId && backupId !== String(expectedBackupId))
      || String(manifest?.files?.data?.name || '') !== 'data.json'
      || String(manifest?.files?.data?.sha256 || '') !== dataSha256
      || Number(manifest?.files?.data?.byteSize || -1) !== dataBytes.byteLength) {
    throw new BackupError('備份 manifest 內容驗證失敗。', 'MANIFEST_CONTENT_INVALID');
  }

  const actualCount = Object.values(data || {}).reduce((sum, rows) => sum + (Array.isArray(rows) ? rows.length : 0), 0);
  if (actualCount !== Number(manifest.totalRowCount || -1)) {
    throw new BackupError('備份資料筆數驗證失敗。', 'BACKUP_COUNT_MISMATCH');
  }

  return {
    manifest,
    data,
    backupId,
    dataSha256,
    manifestSha256,
    totalRowCount: actualCount,
    dataByteSize: dataBytes.byteLength,
    manifestByteSize: manifestBytes.byteLength
  };
}

export async function verifyBackupSet(provider, expected) {
  assertBackupStorageProvider(provider);
  assertBackupSetShape(expected);
  const [dataBytes, manifestBytes] = await Promise.all([
    provider.getObject(expected.dataKey),
    provider.getObject(expected.manifestKey)
  ]);

  const validated = await validateV17BackupSetBytes(manifestBytes, dataBytes, expected.backupId);
  if (dataBytes.byteLength !== expected.dataBytes.byteLength || validated.dataSha256 !== expected.dataSha256) {
    throw new BackupError('Cloud Storage data.json 回讀驗證失敗。', 'DATA_VERIFY_FAILED');
  }
  if (manifestBytes.byteLength !== expected.manifestBytes.byteLength || validated.manifestSha256 !== expected.manifestSha256) {
    throw new BackupError('Cloud Storage manifest.json 回讀驗證失敗。', 'MANIFEST_VERIFY_FAILED');
  }
  if (validated.totalRowCount !== expected.totalRowCount) {
    throw new BackupError('備份資料筆數驗證失敗。', 'BACKUP_COUNT_MISMATCH');
  }
  return true;
}

export async function cleanupExpiredBackups(provider, retentionDays, now = new Date()) {
  assertBackupStorageProvider(provider);
  const cutoff = now.getTime() - Number(retentionDays) * 24 * 60 * 60 * 1000;
  const objects = await provider.listObjects(BACKUP_ROOT_PREFIX);
  for (const object of objects) {
    const key = String(object?.key || '');
    if (!isManagedBackupObject(key)) continue;
    const created = Date.parse(String(object?.timeCreated || ''));
    if (!Number.isFinite(created) || created >= cutoff) continue;
    try {
      await provider.deleteObject(key, object?.versionToken || '');
    } catch {
      // Retention cleanup is best effort and must not invalidate a verified backup.
    }
  }
}

function assertBackupSetShape(backupSet) {
  if (!backupSet || typeof backupSet !== 'object'
      || !backupSet.backupId || !backupSet.dataKey || !backupSet.manifestKey
      || !(backupSet.dataBytes instanceof Uint8Array)
      || !(backupSet.manifestBytes instanceof Uint8Array)) {
    throw new BackupError('BackupSet 結構不完整。', 'BACKUP_SET_INVALID');
  }
}

function isManagedBackupObject(key) {
  if (!key.startsWith(BACKUP_ROOT_PREFIX)) return false;
  const relative = key.slice(BACKUP_ROOT_PREFIX.length);
  const parts = relative.split('/');
  if (parts.length !== 2 || !/^\d{8}T\d{6}Z$/.test(parts[0])) return false;
  return parts[1] === 'data.json' || parts[1] === 'manifest.json';
}

async function bestEffortDelete(provider, key, versionToken) {
  try { await provider.deleteObject(key, versionToken || ''); } catch { /* best effort */ }
}

async function sha256HexBytes(bytes) {
  const digest = new Uint8Array(await crypto.subtle.digest('SHA-256', bytes));
  return Array.from(digest, byte => byte.toString(16).padStart(2, '0')).join('');
}

function normalizeRunRow(row) {
  return {
    id: Number(row.id),
    trigger: row.trigger_kind || undefined,
    status: row.status || 'success',
    startedAt: row.started_at || undefined,
    completedAt: row.completed_at || null,
    fileName: row.file_name || '',
    dataSha256: row.data_sha256 || '',
    fileSha256: row.file_sha256 || '',
    rowCount: Number(row.row_count || 0),
    byteSize: Number(row.byte_size || 0),
    errorMessage: row.error_message || ''
  };
}

function truncateError(value) {
  return String(value || '').slice(0, 500);
}

function safeBackupMessage(error) {
  if (error instanceof BackupError || error instanceof GcsProviderError) return error.message;
  return 'Google Cloud Storage 備份處理失敗，請稍後再試。';
}

function safeErrorCode(error) {
  if (error instanceof BackupError || error instanceof GcsProviderError) return error.code;
  return 'BACKUP_FAILED';
}

export class BackupError extends Error {
  constructor(message, code) {
    super(message);
    this.name = 'BackupError';
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
