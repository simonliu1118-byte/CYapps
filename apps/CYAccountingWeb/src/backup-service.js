import { recoveryPage } from './backup-recovery-page.js';
import { buildRecoveryContext, assertRecoveryContext, recoveryDigest } from './backup-recovery-context.js';
import { recoveryWorkspace, restoreIntoRecoveryD1, recoveryEvent, RECOVERY_CONFIRMATION } from './backup-recovery.js';
import { handleGcsBackupApi, runScheduledGcsBackup as runLegacyScheduledBackup, buildBackupSet, storeBackupSet, validateBackupSetBytes } from './gcs-backup.js';
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
const GCS_PHASE_D_RETENTION_DAYS = 182;
const MAX_RESTORE_BYTES = 8 * 1024 * 1024;
const PRIMARY_CRON = '30 19 * * *';
const RETRY_CRON = '0 0 * * *';
const ATTEMPT_PREFIX = 'CYAccountingWebOperations/scheduled-backups/';

export function resolveTieredBackupTopology(env) {
  const value = String(env?.BACKUP_TOPOLOGY || '').trim();
  if (!value || value === LEGACY_GCS) return LEGACY_GCS;
  if (value === PARALLEL_DUAL_PROVIDER) return PARALLEL_DUAL_PROVIDER;
  throw new TieredBackupError('不支援的備份拓撲設定。', 'BACKUP_TOPOLOGY_UNSUPPORTED');
}

export async function handleBackupApi(request, env, session, options = {}) {
  const topology = resolveTieredBackupTopology(env);
  if (topology === LEGACY_GCS) return handleGcsBackupApi(request, env, session);

  const url = new URL(request.url);
  if (!url.pathname.startsWith('/api/backup/')) return null;
  if (String(session?.role || '') !== 'SUPER_ADMIN') {
    return json({ ok: false, error: '只有超級管理員可以管理備份與復原。', code: 'SUPER_ADMIN_REQUIRED' }, 403);
  }
  if (url.pathname.startsWith('/api/backup/recovery/') && request.method === 'POST') {
    try {
      if(request.headers.get('origin')&&request.headers.get('origin')!==url.origin) throw new TieredBackupError('來源網站不符。','RECOVERY_ORIGIN_INVALID');
      const html=request.headers.get('accept')?.includes('text/html');
      const body=request.headers.get('content-type')?.includes('application/x-www-form-urlencoded')?Object.fromEntries(await request.formData()):await request.json();
      if(html)body.reviewed=body.reviewed==='on';
      const result=await handleRecoveryAction(url.pathname,body,env,session,options);
      return html?recoveryPage(result):json({ok:true,...result});
    } catch(error) { return request.headers.get('accept')?.includes('text/html')?recoveryPage({error:safeMessage(error),code:safeCode(error)},409):json({ok:false,error:safeMessage(error),code:safeCode(error)},409); }
  }
  if (url.pathname === '/api/backup/health' && request.method === 'GET') return json({ ok: true, health: await readBackupHealth(env.DB) });
  if (url.pathname === '/api/backup/status' && request.method === 'GET') {
    return parallelBackupStatus(env);
  }
  if (request.method === 'POST' && ['/api/backup/verify-restore', '/api/backup/phase-d/activate'].includes(url.pathname)) {
    try {
      const body = await request.json();
      if (url.pathname === '/api/backup/verify-restore') {
        const checks = await verifyStoredBackup(env, session, String(body.backupId || ''), options);
        return json({ ok: checks.every(check => check.status === 'success'), checks });
      }
      if (body.confirm !== 'phase_d') throw new TieredBackupError('請明確確認啟用 Phase D。', 'PHASE_D_CONFIRM_REQUIRED');
      return json({ ok: true, policy: await activatePhaseD(env, session) });
    } catch (error) {
      return json({ ok: false, error: safeMessage(error), code: safeCode(error) }, 409);
    }
  }
  if (url.pathname === '/api/backup/run' && request.method === 'POST') {
    try {
      const result = await runTieredBackup(env, 'manual');
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

export async function runScheduledBackup(env, options = {}) {
  const cron = options.cron || PRIMARY_CRON;
  if (![PRIMARY_CRON, RETRY_CRON].includes(cron)) return { skipped: true, reason: 'unknown_schedule' };
  if (resolveTieredBackupTopology(env) === LEGACY_GCS) return cron === PRIMARY_CRON
    ? runLegacyScheduledBackup(env) : { skipped: true, reason: 'legacy_retry_not_enabled' };
  if (!env?.DB) return { skipped: true, reason: 'not_configured' };
  const now = options.now instanceof Date ? options.now : new Date();
  const day = taipeiBackupDay(now), slot = cron === RETRY_CRON ? 'retry' : 'primary';
  const hex = await recoveryDigest(`scheduled-backup:${day}:${slot}`);
  const incident = `${hex.slice(0,8)}-${hex.slice(8,12)}-${hex.slice(12,16)}-${hex.slice(16,20)}-${hex.slice(20,32)}`;
  let databaseError;
  try {
    const hold = await env.DB.prepare("SELECT value FROM meta WHERE key='recovery_hold'").first();
    if (hold?.value === '1') return { skipped: true, reason: 'recovery_workspace' };
  } catch (error) { databaseError = error; }
  const key = `${ATTEMPT_PREFIX}${day}/${slot}.json`;
  // This journal must survive a D1 quota outage. Each day has two atomic slots;
  // a duplicate delivery cannot claim the same slot, even after a failure.
  const attempt = { day, slot, createdAt: now.toISOString(), completedAt: null, status: 'pending', backupId: '', errorCode: '' };
  let claim;
  try {
    claim = await env.BACKUP_R2.put(key, JSON.stringify(attempt), { onlyIf: { etagDoesNotMatch: '*' } });
    if (!claim) return { skipped: true, reason: 'attempt_already_recorded' };
  } catch {
    console.error('cyaccounting_backup_attempt_log_unavailable');
    // A journal outage must not suppress the other provider. The canonical D1
    // backup ID is also unique for this ScheduledController timestamp.
  }
  let backup, databaseReady = false;
  try {
    if (databaseError) throw databaseError;
    databaseReady = true;
    if (slot === 'retry' && await successfulBackupToday(env.DB, now)) backup = { skipped: true, reason: 'already_backed_up' };
    else backup = await runTieredBackup(env, 'scheduled', { ...options, now });
  }
  catch (error) {
    const quota = /exceeded D1's free tier daily|daily.*row.*limit/i.test(`${error?.message || ''} ${error?.cause?.message || ''}`);
    const code = quota ? 'D1_DAILY_LIMIT_EXCEEDED' : !databaseReady ? 'BACKUP_DATABASE_UNAVAILABLE' : safeCode(error);
    console.error('cyaccounting_tiered_backup_failed', /^[A-Z][A-Z0-9_]{0,63}$/.test(code) ? code : 'BACKUP_FAILED');
    backup = { ok: false, error: code, backupId: error?.backupResult?.backupId || '' };
    // Stop all further D1 work on a quota error; notification uses CYID instead.
    if (quota) databaseReady = false;
  }
  Object.assign(attempt, { status: backup.skipped ? 'skipped' : backup.ok ? 'success' : 'failed',
    completedAt: new Date().toISOString(), backupId: backup.backupId || '',
    errorCode: /^[A-Z][A-Z0-9_]{0,63}$/.test(backup.error || '') ? backup.error : backup.ok || backup.skipped ? '' : 'BACKUP_FAILED',
    reason: backup.reason || '' });
  try {
    if (!claim) throw new Error('attempt journal unavailable');
    const saved = await env.BACKUP_R2.put(key, JSON.stringify(attempt), { onlyIf: { etagMatches: claim.etag } });
    if (!saved) throw new Error('attempt changed');
  } catch {
    console.error('cyaccounting_backup_attempt_log_failed');
    const delivery = await notifyBackupIncident(env, incident);
    backup = { ...backup, attemptLogError: 'BACKUP_ATTEMPT_LOG_FAILED', emailSent: delivery.sent };
  }
  // Fixed-key cleanup only: never scan the bucket or remove backup packages.
  try {
    const expired = taipeiBackupDay(new Date(now.getTime() - 31 * 86400000));
    await env.BACKUP_R2.delete(['primary', 'retry'].map(name => `${ATTEMPT_PREFIX}${expired}/${name}.json`));
  } catch { console.warn('cyaccounting_backup_attempt_cleanup_failed'); }
  if (backup.skipped) return backup;
  if (!databaseReady) {
    const delivery = await notifyBackupIncident(env, incident);
    return { ...backup, emailSent: delivery.sent };
  }
  // A failed new backup must not suppress verification / incident notification.
  try { return { ...backup, health: await checkBackupHealth(env, { ...options, backupFailed: backup.ok === false }) }; }
  catch (error) {
    console.error('cyaccounting_backup_health_failed', safeCode(error));
    const delivery = await notifyBackupIncident(env, crypto.randomUUID());
    return { ...backup, health: { status: 'failed', code: 'BACKUP_HEALTH_CHECK_FAILED', emailSent: delivery.sent } };
  }
}

function taipeiBackupDay(now) {
  return new Date(now.getTime() + 8 * 3600000).toISOString().slice(0, 10);
}

async function successfulBackupToday(db, now) {
  const day = taipeiBackupDay(now);
  const start = new Date(`${day}T00:00:00+08:00`);
  const end = new Date(start.getTime() + 86400000);
  const policy = await readBackupPolicy(db);
  const { gcsExpected } = backupExecutionPlan(policy.phase, 'scheduled', now);
  return Boolean(await db.prepare(`SELECT bs.backup_id FROM backup_sets bs
    WHERE bs.created_at >= ? AND bs.created_at < ?
      AND EXISTS (SELECT 1 FROM backup_copies bc WHERE bc.backup_id = bs.backup_id
        AND bc.provider = 'cloudflare_r2' AND bc.status = 'success' AND bc.verified_at IS NOT NULL)
      AND (? = 0 OR EXISTS (SELECT 1 FROM backup_copies bc WHERE bc.backup_id = bs.backup_id
        AND bc.provider = 'google_cloud_storage' AND bc.status = 'success' AND bc.verified_at IS NOT NULL))
    ORDER BY bs.created_at DESC LIMIT 1`).bind(start.toISOString(), end.toISOString(), Number(gcsExpected)).first());
}

export async function readScheduledBackupAttempts(env, now = new Date()) {
  const attempts = [];
  // The existing history shows eight rows. Read at most eight days / 16 keys,
  // independently of the number of historical objects in the bucket.
  for (let age = 0; age < 8; age++) {
    const day = taipeiBackupDay(new Date(now.getTime() - age * 86400000));
    for (const slot of ['retry', 'primary']) {
      const object = await env.BACKUP_R2.get(`${ATTEMPT_PREFIX}${day}/${slot}.json`);
      if (!object) continue;
      const record = JSON.parse(await object.text());
      if (record.day === day && record.slot === slot && ['pending', 'failed', 'success', 'skipped'].includes(record.status)) attempts.push(record);
    }
  }
  return attempts;
}

export async function runTieredBackup(env, triggerKind = 'scheduled', options = {}) {
  if (!['scheduled', 'manual'].includes(triggerKind)) {
    throw new TieredBackupError('備份觸發類型錯誤。', 'INVALID_TRIGGER');
  }
  if (!env?.DB) throw new TieredBackupError('D1 尚未綁定。', 'DB_NOT_CONFIGURED');
  if ((options.topology || resolveTieredBackupTopology(env)) !== PARALLEL_DUAL_PROVIDER) {
    throw new TieredBackupError('Phase C 雙 provider 尚未啟用。', 'PARALLEL_TOPOLOGY_REQUIRED');
  }

  const now = options.now instanceof Date ? options.now : new Date();
  const policy = await readBackupPolicy(env.DB);
  const plan = backupExecutionPlan(policy.phase, triggerKind, now);
  const backupSet = await buildBackupSet(env.DB, now);
  const packageSha256 = await backupPackageDigest(backupSet);
  await insertLogicalBackup(env.DB, backupSet, triggerKind, packageSha256, plan);

  const specs = [
    {
      provider: R2_PROVIDER,
      retentionDays: R2_RETENTION_DAYS,
      create: () => options.r2Provider || createR2BackupStorageProvider(env)
    },
    {
      provider: GCS_PROVIDER,
      retentionDays: plan.gcsRetentionDays,
      create: () => options.gcsProvider || createGcsBackupStorageProvider(env, options.gcsProviderOptions)
    }
  ].filter(spec => spec.provider === R2_PROVIDER || plan.gcsExpected);

  const copies = [];
  for (const spec of specs) {
    copies.push(await executeProviderCopy(env.DB, backupSet, spec, now));
  }

  const gcsCopy = copies.find(copy => copy.provider === GCS_PROVIDER);
  if (plan.gcsExpected) await writeLegacyGcsRun(env.DB, backupSet, triggerKind, now, gcsCopy);

  const completedAt = new Date().toISOString();
  const result = {
    ok: copies.every(copy => copy.status === 'success'),
    backupId: backupSet.backupId,
    policyPhase: plan.phase, gcsExpected: plan.gcsExpected,
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
    const stored = await storeBackupSet(provider, backupSet, {
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

async function insertLogicalBackup(db, backupSet, triggerKind, packageSha256, plan) {
  await db.prepare(`
    INSERT INTO backup_sets(
      backup_id, app_id, created_at, app_version, schema_version, format, format_version,
      data_sha256, manifest_sha256, package_sha256, data_byte_length,
      manifest_byte_length, total_byte_length, record_count, trigger_kind, policy_phase, gcs_expected
    ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
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
    triggerKind, plan.phase, Number(plan.gcsExpected)
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
  const policy = await readBackupPolicy(env.DB);
  const result = await env.DB.prepare(`
    SELECT backup_id, created_at, app_version, schema_version, format, format_version,
           data_sha256, manifest_sha256, package_sha256, total_byte_length,
           record_count, trigger_kind, policy_phase, gcs_expected
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
      policyPhase: row.policy_phase || 'phase_c', gcsExpected: Number(row.gcs_expected ?? 1) === 1,
      status: requiredCopiesSuccessful(copies, Number(row.gcs_expected ?? 1) === 1) ? 'success' : 'failed',
      copies
    });
  }

  const phaseCAcceptance = await readPhaseCAcceptance(env.DB);
  const restoreChecks = await recentRestoreChecks(env.DB);
  const latestSuccess = logicalBackups.find(item => item.status === 'success') || null;
  let scheduledAttempts = [], attemptLogAvailable = true;
  try { scheduledAttempts = await readScheduledBackupAttempts(env); }
  catch { attemptLogAvailable = false; }
  const workspace = await recoveryWorkspace(env.RECOVERY_DB);
  return json({
    ok: true,
    provider: 'tiered',
    topology: PARALLEL_DUAL_PROVIDER,
    policy, restoreChecks,
    recoveryWorkspace: workspace,
    recoveryAvailable: workspace.configured,
    recoveryLatest: await latestRecoveryEvent(env.RECOVERY_DB),
    phaseDReady: configured && phaseCAcceptance.completed && restorationPairReady(restoreChecks),
    configured,
    providers: {
      [R2_PROVIDER]: { configured: r2ConfigReady(env), retentionDays: R2_RETENTION_DAYS, role: 'operational' },
      [GCS_PROVIDER]: { configured: gcsConfigReady(env), retentionDays: policy.gcsRetentionDays, role: policy.phase === 'phase_d' ? 'cross_cloud_long_term' : 'cross_cloud_validation' }
    },
    schedule: { cron: PRIMARY_CRON, retryCron: RETRY_CRON, localTime: policy.phase === 'phase_d'
      ? 'R2 每日；GCS 週三、週日 03:30；失敗於 08:00 補跑一次（台灣時間）'
      : '每日 03:30；失敗於 08:00 補跑一次（台灣時間）' },
    retentionDays: policy.gcsRetentionDays,
    phaseCAcceptance,
    latestSuccess: latestSuccess ? logicalToLegacyRun(latestSuccess) : null,
    recentRuns: logicalBackups.map(logicalToLegacyRun),
    logicalBackups,
    scheduledAttempts, attemptLogAvailable,
    restoreAvailable: false
  });
}

export function backupExecutionPlan(phase, triggerKind, now) {
  const localDay = new Date(now.getTime() + 8 * 60 * 60 * 1000).getUTCDay();
  const phaseD = phase === 'phase_d';
  return { phase: phaseD ? 'phase_d' : 'phase_c',
    gcsExpected: !phaseD || triggerKind === 'manual' || [0, 3].includes(localDay),
    gcsRetentionDays: phaseD ? GCS_PHASE_D_RETENTION_DAYS : GCS_PHASE_C_RETENTION_DAYS };
}

export async function readBackupPolicy(db) {
  const event = await db.prepare('SELECT target_phase, created_at FROM backup_policy_events ORDER BY id DESC LIMIT 1').first();
  const phase = event?.target_phase === 'phase_d' ? 'phase_d' : 'phase_c';
  return { phase, activatedAt: event?.created_at || null, r2RetentionDays: R2_RETENTION_DAYS,
    gcsRetentionDays: phase === 'phase_d' ? GCS_PHASE_D_RETENTION_DAYS : GCS_PHASE_C_RETENTION_DAYS,
    gcsDaysTaipei: phase === 'phase_d' ? [0, 3] : [0, 1, 2, 3, 4, 5, 6] };
}

export async function readPhaseCAcceptance(db) {
  const result = await db.prepare(`
    SELECT bs.backup_id, bs.created_at, bs.package_sha256,
      MAX(CASE WHEN bc.provider = ? AND bc.status = 'success' AND bc.verified_at IS NOT NULL THEN 1 ELSE 0 END) AS r2_success,
      MAX(CASE WHEN bc.provider = ? AND bc.status = 'success' AND bc.verified_at IS NOT NULL THEN 1 ELSE 0 END) AS gcs_success
    FROM backup_sets bs LEFT JOIN backup_copies bc ON bc.backup_id = bs.backup_id
    WHERE bs.trigger_kind = 'scheduled' AND bs.policy_phase = 'phase_c'
    GROUP BY bs.backup_id, bs.created_at, bs.package_sha256
    ORDER BY bs.created_at DESC LIMIT 14
  `).bind(R2_PROVIDER, GCS_PROVIDER).all();
  return summarizePhaseCAcceptance(result.results || []);
}

function requiredCopiesSuccessful(copies, gcsExpected) {
  return [R2_PROVIDER, ...(gcsExpected ? [GCS_PROVIDER] : [])].every(provider => copies.some(copy => copy.provider === provider && copy.status === 'success'));
}

export async function verifyStoredBackup(env, session, backupId = '', options = {}) {
  assertSuperAdmin(session);
  if (backupId && !/^\d{8}T\d{6}Z$/.test(backupId)) throw new TieredBackupError('備份 ID 不正確。', 'BACKUP_ID_INVALID');
  const source = await env.DB.prepare(`SELECT bs.* FROM backup_sets bs
    WHERE ((? = '' AND bs.gcs_expected = 1) OR bs.backup_id = ?) AND EXISTS (
      SELECT 1 FROM backup_copies bc WHERE bc.backup_id = bs.backup_id AND bc.status = 'success')
    ORDER BY bs.created_at DESC LIMIT 1`).bind(backupId, backupId).first();
  if (!source) throw new TieredBackupError('尚無可驗證的備份。', 'BACKUP_SOURCE_MISSING');
  const checks = [];
  const attemptId = crypto.randomUUID();
  for (const providerName of [R2_PROVIDER, GCS_PROVIDER]) {
    checks.push(await verifyStoredBackupCopy(env, source, providerName, backupActor(session), attemptId, options));
  }
  return checks;
}

async function verifyStoredBackupCopy(env, source, providerName, actor, attemptId, options) {
  const id = crypto.randomUUID(), createdAt = new Date().toISOString();
  let result, status = 'failed';
  try {
    const verify = options.verifyRestore || (await import('./backup-restore-runtime.js')).verifyRestoreInMemory;
    const provider = providerName === R2_PROVIDER ? options.r2Provider || createR2BackupStorageProvider(env)
      : options.gcsProvider || createGcsBackupStorageProvider(env, options.gcsProviderOptions);
    const prefix = `CYAccountingWeb/${source.backup_id}/`;
    const [manifestBytes, dataBytes] = await Promise.all([provider.getObject(prefix + 'manifest.json'), provider.getObject(prefix + 'data.json')]);
    if (!(manifestBytes instanceof Uint8Array) || !(dataBytes instanceof Uint8Array) || dataBytes.byteLength > MAX_RESTORE_BYTES) throw new TieredBackupError('備份超出隔離驗證大小上限或副本不存在。', 'RESTORE_SIZE_OR_SOURCE');
    const validated = await validateBackupSetBytes(manifestBytes, dataBytes, source.backup_id);
    const packageSha256 = await backupPackageDigest({ manifestBytes, dataBytes });
    if (packageSha256 !== source.package_sha256 || validated.dataSha256 !== source.data_sha256 || validated.manifestSha256 !== source.manifest_sha256) throw new TieredBackupError('副本與備份紀錄不符。', 'RESTORE_CATALOG_MISMATCH');
    result = await verify(validated);
    if (!result.fullRowRoundtrip || !result.integrityCheck || !result.foreignKeyCheck) throw new TieredBackupError('還原核對未完整通過。', 'RESTORE_PROOF_INCOMPLETE');
    status = 'success';
  } catch (error) { result = { code: safeCode(error), isolated: true, liveAccountingModified: false }; }
  await env.DB.prepare(`INSERT INTO backup_restore_checks(id, attempt_id, backup_id, provider, package_sha256,
    source_schema_version, target_schema_version, status, result_json, actor_json, created_at)
    VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)`)
    .bind(id, attemptId, source.backup_id, providerName, source.package_sha256, Number(source.schema_version), Number(result.targetSchemaVersion || 0),
      status, JSON.stringify(result), JSON.stringify(actor), createdAt).run();
  return { id, attemptId, backupId: source.backup_id, provider: providerName, packageSha256: source.package_sha256, status, result, createdAt };
}


// One scheduled health owner: same immutable-byte + fresh SQLite verifier as
// controlled restore, with independent newest R2/GCS sources under Phase D.
export async function checkBackupHealth(env, options = {}) {
  const now = options.now instanceof Date ? options.now : new Date();
  const timestamp = now.toISOString(), lease = crypto.randomUUID();
  const hold = await env.DB.prepare("SELECT value FROM meta WHERE key='recovery_hold'").first();
  if (hold?.value === '1') return { skipped: true, reason: 'recovery_workspace' };
  const claimed = await env.DB.prepare(`UPDATE backup_health_state SET lease_token = ?, lease_until = ?
    WHERE id = 1 AND lease_until <= ? RETURNING id`)
    .bind(lease, new Date(now.getTime() + 600_000).toISOString(), timestamp).first();
  if (!claimed) return { skipped: true, reason: 'check_in_progress' };
  try {
    const previous = await env.DB.prepare('SELECT * FROM backup_health_state WHERE id=1').first();
    const attemptId = crypto.randomUUID(), details = options.backupFailed
      ? [{ provider: '', status: 'failed', code: 'BACKUP_JOB_FAILED' }] : [];
    for (const provider of [R2_PROVIDER, GCS_PROVIDER]) {
      const source = await env.DB.prepare(`SELECT bs.*, bc.status AS copy_status FROM backup_sets bs
        LEFT JOIN backup_copies bc ON bc.backup_id = bs.backup_id AND bc.provider = ?
        WHERE (? = 'cloudflare_r2' OR bs.gcs_expected = 1)
        ORDER BY bs.created_at DESC LIMIT 1`).bind(provider, provider).first();
      let code = '', check = null;
      if (!source) code = 'BACKUP_SOURCE_MISSING';
      else if (source.copy_status !== 'success') code = 'BACKUP_COPY_FAILED';
      else if (now.getTime() - Date.parse(source.created_at) > (provider === R2_PROVIDER ? 36 : 120) * 3_600_000
        || Date.parse(source.created_at) > now.getTime() + 300_000) code = 'BACKUP_COPY_STALE';
      else {
        check = await verifyStoredBackupCopy(env, source, provider, { type: 'scheduled_backup_health' }, attemptId, options);
        if (check.status !== 'success') code = check.result.code || 'RESTORE_VERIFY_FAILED';
      }
      details.push({ provider, status: code ? 'failed' : 'success', code,
        backupId: source?.backup_id || null, checkId: check?.id || null });
    }
    const failed = details.some(detail => detail.status === 'failed');
    const incident = failed ? previous.incident_id || crypto.randomUUID() : null;
    const emailSentAt = failed && previous.incident_id === incident ? previous.email_sent_at : null;
    await env.DB.prepare(`UPDATE backup_health_state SET status=?, checked_at=?, details_json=?,
      incident_id=?, email_sent_at=?, email_error_code='' WHERE id=1 AND lease_token=?`)
      .bind(failed ? 'failed' : 'healthy', timestamp, JSON.stringify(details), incident, emailSentAt, lease).run();
    if (failed && !emailSentAt) {
      const delivery = await notifyBackupIncident(env, incident);
      const sentAt = delivery.sent ? timestamp : null, code = delivery.code;
      await env.DB.prepare(`UPDATE backup_health_state SET email_sent_at=?, email_error_code=?
        WHERE id=1 AND lease_token=? AND incident_id=?`).bind(sentAt, code, lease, incident).run();
    }
    return await readBackupHealth(env.DB, now);
  } finally {
    await env.DB.prepare("UPDATE backup_health_state SET lease_token='',lease_until='' WHERE id=1 AND lease_token=?").bind(lease).run();
  }
}

async function notifyBackupIncident(env, incident) {
  try {
    if (typeof env.BACKUP_NOTIFICATIONS?.notifySuperAdmin !== 'function') throw new Error('missing binding');
    const result = await env.BACKUP_NOTIFICATIONS.notifySuperAdmin({
      workspaceId: String(env.CYID_WORKSPACE_ID || ''), applicationId: String(env.CYID_APPLICATION_ID || ''),
      type: 'backup_health_failed', eventId: incident
    });
    return ['delivered','already_delivered'].includes(result?.status)
      ? { sent: true, code: '' } : { sent: false, code: 'NOTIFICATION_DEFERRED' };
  } catch { return { sent: false, code: 'NOTIFICATION_DELIVERY_FAILED' }; }
}

export async function readBackupHealth(db, now = new Date()) {
  const row = await db.prepare('SELECT status,checked_at,details_json,incident_id,email_sent_at,email_error_code FROM backup_health_state WHERE id=1').first();
  const stale = row?.checked_at && now.getTime() - Date.parse(row.checked_at) > 36 * 3_600_000;
  return { status: stale ? 'failed' : row?.status || 'unknown', checkedAt: row?.checked_at || null,
    incidentId: row?.incident_id || (stale ? 'check-overdue' : null),
    issues: stale ? [{ provider: '', status: 'failed', code: 'BACKUP_HEALTH_OVERDUE' }]
      : JSON.parse(row?.details_json || '[]').filter(detail => detail.status === 'failed')
        .map(({provider, code}) => ({ provider, code })),
    emailSent: Boolean(row?.email_sent_at), emailPending: Boolean(row?.incident_id && !row?.email_sent_at) };
}

async function recentRestoreChecks(db) {
  const result = await db.prepare('SELECT id, attempt_id, backup_id, provider, package_sha256, status, result_json, created_at FROM backup_restore_checks ORDER BY sequence DESC LIMIT 16').all();
  return (result.results || []).map(row => ({ id: row.id, attemptId: row.attempt_id, backupId: row.backup_id, provider: row.provider,
    packageSha256: row.package_sha256, status: row.status, result: JSON.parse(row.result_json), createdAt: row.created_at }));
}
function restorationPair(checks, now = Date.now()) {
  const backupId = checks[0]?.backupId;
  const r2 = checks.find(check => check.backupId === backupId && check.provider === R2_PROVIDER);
  const gcs = checks.find(check => check.backupId === backupId && check.provider === GCS_PROVIDER);
  if (!r2 || !gcs || r2.attemptId !== gcs.attemptId || r2.packageSha256 !== gcs.packageSha256) return null;
  return [r2, gcs].every(check => check.status === 'success' && check.result.fullRowRoundtrip && check.result.integrityCheck && check.result.foreignKeyCheck && now - Date.parse(check.createdAt) <= 86400000 && now >= Date.parse(check.createdAt)) ? [r2, gcs] : null;
}
function restorationPairReady(checks) { return Boolean(restorationPair(checks)); }

export async function activatePhaseD(env, session) {
  assertSuperAdmin(session);
  if (resolveTieredBackupTopology(env) !== PARALLEL_DUAL_PROVIDER || !gcsConfigReady(env) || !r2ConfigReady(env)) throw new TieredBackupError('兩個備份 provider 尚未設定完整。', 'PHASE_D_PROVIDER_REQUIRED');
  const policy = await readBackupPolicy(env.DB);
  if (policy.phase === 'phase_d') return policy;
  const acceptance = await readPhaseCAcceptance(env.DB);
  if (!acceptance.completed) throw new TieredBackupError('需先完成 14 次連續雙副本排程驗收。', 'PHASE_C_INCOMPLETE');
  const pair = restorationPair(await recentRestoreChecks(env.DB));
  if (!pair) throw new TieredBackupError('需先完成同一備份 R2 與 GCS 的隔離還原驗證。', 'RESTORE_PAIR_REQUIRED');
  await env.DB.prepare(`INSERT INTO backup_policy_events(target_phase, r2_check_id, gcs_check_id, acceptance_json, actor_json, created_at)
    SELECT 'phase_d', ?, ?, ?, ?, ? WHERE NOT EXISTS (SELECT 1 FROM backup_policy_events)
    AND (SELECT backup_id FROM backup_sets WHERE trigger_kind = 'scheduled' AND policy_phase = 'phase_c' ORDER BY created_at DESC LIMIT 1) = ?`)
    .bind(pair[0].id, pair[1].id, JSON.stringify(acceptance), JSON.stringify(backupActor(session)), new Date().toISOString(), acceptance.latestScheduledBackupId).run();
  const updated = await readBackupPolicy(env.DB);
  if (updated.phase !== 'phase_d') throw new TieredBackupError('排程紀錄已更新，請重新驗證後再切換。', 'BACKUP_POLICY_CONFLICT');
  return updated;
}
function assertSuperAdmin(session) { if (session?.role !== 'SUPER_ADMIN') throw new TieredBackupError('只有超級管理員可以管理備份。', 'SUPER_ADMIN_REQUIRED'); }
function backupActor(session) { return { employeeId: String(session.employee_id || ''), employeeNo: String(session.employee_no || ''), name: String(session.employee_name || ''), role: session.role }; }

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


async function readRecoverySource(env,backupId,preferred,options={}) {
  if(!/^\d{8}T\d{6}Z$/.test(backupId)||!['auto',R2_PROVIDER,GCS_PROVIDER].includes(preferred)) throw new TieredBackupError('請選擇正確的備份與來源。','RECOVERY_SOURCE_INVALID');
  let catalog=null;
  try { catalog=await env.DB.prepare('SELECT * FROM backup_sets WHERE backup_id=?').bind(backupId).first(); } catch { /* Old D1 may be unavailable in a disaster. GCS context is independently verified. */ }
  for(const provider of preferred==='auto'?[R2_PROVIDER,GCS_PROVIDER]:[preferred]) {
    try {
      const adapter=provider===R2_PROVIDER?options.r2Provider||createR2BackupStorageProvider(env):options.gcsProvider||createGcsBackupStorageProvider(env);
      const prefix=`CYAccountingWeb/${backupId}/`;
      const [manifestBytes,dataBytes]=await Promise.all([adapter.getObject(prefix+'manifest.json'),adapter.getObject(prefix+'data.json')]);
      if(dataBytes.byteLength>MAX_RESTORE_BYTES||manifestBytes.byteLength>MAX_RESTORE_BYTES) throw new Error('size');
      const backup=await validateBackupSetBytes(manifestBytes,dataBytes,backupId);
      const packageSha256=await backupPackageDigest({manifestBytes,dataBytes});
      if(catalog&&(packageSha256!==catalog.package_sha256||backup.dataSha256!==catalog.data_sha256||backup.manifestSha256!==catalog.manifest_sha256)) throw new Error('catalog');
      if(!catalog&&!backup.manifest.recoveryContext) throw new Error('offline_context_required');
      return {backup,provider,packageSha256};
    } catch { if(preferred!=='auto'||provider===GCS_PROVIDER) throw new TieredBackupError('備份回讀或完整性驗證失敗，未更動任何帳本。','RECOVERY_SOURCE_VERIFY_FAILED'); }
  }
}
async function handleRecoveryAction(path,body,env,session,options) {
  assertSuperAdmin(session);
  if(!env.RECOVERY_DB||env.RECOVERY_DB===env.DB) throw new TieredBackupError('獨立還原工作區尚未設定。','RECOVERY_TARGET_UNAVAILABLE');
  const actor=backupActor(session);
  const verify=options.verifyRestore||(await import('./backup-restore-runtime.js')).verifyRestoreInMemory;
  if(path==='/api/backup/recovery/preview') {
    const reason=String(body.reason||'').trim();
    if(!reason||reason.length>200) throw new TieredBackupError('請填寫還原理由，最多 200 字。','RECOVERY_REASON_REQUIRED');
    const workspace=await recoveryWorkspace(env.RECOVERY_DB);
    if(!workspace.ready) throw new TieredBackupError('獨立還原工作區不是空白，請先由維運人員處理。','RECOVERY_TARGET_NOT_EMPTY');
    const source=await readRecoverySource(env,String(body.backupId||''),String(body.provider||'auto'),options);
    const context=source.backup.manifest.recoveryContext||await buildRecoveryContext(env.DB);
    assertRecoveryContext(context);
    const verification=await verify({...source.backup,manifest:{...source.backup.manifest,recoveryContext:context}});
    const id=crypto.randomUUID(),expiresAt=Math.floor(Date.now()/1000)+600;
    const contextSha256=await recoveryDigest(context);
    await env.RECOVERY_DB.prepare(`INSERT INTO backup_recovery_previews(id,backup_id,provider,package_sha256,context_sha256,workspace_id,reason,actor_json,expires_at,created_at) VALUES(?,?,?,?,?,?,?,?,?,?)`)
      .bind(id,body.backupId,source.provider,source.packageSha256,contextSha256,workspace.workspaceId,reason,JSON.stringify(actor),expiresAt,new Date().toISOString()).run();
    return {preview:{id,backupId:body.backupId,provider:source.provider,expiresAt,counts:verification.counts,normalization:verification.normalization||null,target:'獨立空白 D1',productionModified:false,confirmation:RECOVERY_CONFIRMATION,reason}};
  }
  if(path!=='/api/backup/recovery/commit') throw new TieredBackupError('找不到此還原操作。','RECOVERY_ROUTE_NOT_FOUND');
  if(body.reviewed!==true||body.confirmation!==RECOVERY_CONFIRMATION) throw new TieredBackupError('請勾選來源確認並輸入指定確認文字。','RECOVERY_CONFIRM_REQUIRED');
  const row=await env.RECOVERY_DB.prepare('SELECT * FROM backup_recovery_previews WHERE id=?').bind(String(body.previewId||'')).first();
  if(!row||row.expires_at<Math.floor(Date.now()/1000)||row.actor_json!==JSON.stringify(actor)) throw new TieredBackupError('還原預覽已失效，請重新預覽。','RECOVERY_PREVIEW_EXPIRED');
  const source=await readRecoverySource(env,row.backup_id,row.provider,options);
  const context=source.backup.manifest.recoveryContext||await buildRecoveryContext(env.DB);
  if(source.packageSha256!==row.package_sha256||await recoveryDigest(context)!==row.context_sha256) throw new TieredBackupError('來源或維運狀態已更新，請重新預覽。','RECOVERY_SOURCE_CHANGED');
  await verify({...source.backup,manifest:{...source.backup.manifest,recoveryContext:context}});
  const preview={id:row.id,backupId:row.backup_id,provider:row.provider,packageSha256:row.package_sha256,workspaceId:row.workspace_id,reason:row.reason};
  const report=await restoreIntoRecoveryD1(env.RECOVERY_DB,source.backup,context,preview,actor);
  try { await recoveryEvent(env.DB,report.runId,'verified',report,actor); } catch { /* The authoritative completed recovery audit is already append-only in the target D1. */ }
  return {recovery:report};
}

async function latestRecoveryEvent(db) {
  if(!db) return null;
  const row=await db.prepare("SELECT status,result_json,created_at FROM backup_recovery_events ORDER BY sequence DESC LIMIT 1").first();
  return row?{status:row.status,result:JSON.parse(row.result_json),createdAt:row.created_at}:null;
}


export async function recoveryLanding(env,session,options={}) {
  if(session?.role!=='SUPER_ADMIN')return recoveryPage({error:'只有超級管理員可以使用帳本復原。'},403);
  try {
    const provider=options.gcsProvider||createGcsBackupStorageProvider(env);
    const objects=await provider.listObjects('CYAccountingWeb/');
    const ids=[...new Set(objects.map(object=>/^CYAccountingWeb\/(\d{8}T\d{6}Z)\/manifest\.json$/.exec(object.key)?.[1]).filter(Boolean))].sort().reverse().slice(0,30);
    return recoveryPage({sources:ids,workspace:await recoveryWorkspace(env.RECOVERY_DB)});
  } catch {return recoveryPage({error:'無法讀取復原來源或獨立工作區，請由維運人員確認設定。'},503);}
}
