import assert from 'node:assert/strict';
import { accountingFixture, memoryProvider, memoryR2Bucket } from './support/backup-fixtures.mjs';
import { verify } from './support/restore-fixture.mjs';
import { runScheduledBackup, runTieredBackup, readScheduledBackupAttempts } from '../src/backup-service.js';
import { buildBackupPackage } from '../src/backup-package.js';
import { buildRecoveryContext } from '../src/backup-recovery-context.js';

const primary = '30 19 * * *', retry = '0 0 * * *';
const atNight = new Date('2026-10-19T19:30:00Z'), atMorning = new Date('2026-10-20T00:00:00Z');
const db = accountingFixture(), bucket = memoryR2Bucket();
const r2 = memoryProvider('cloudflare_r2'), gcs = memoryProvider('google_cloud_storage');
const notifications = [];
const env = { DB: db, BACKUP_R2: bucket, BACKUP_TOPOLOGY: 'parallel_dual_provider',
  BACKUP_NOTIFICATIONS: { async notifySuperAdmin(input) { notifications.push(input); return { status: 'delivered' }; } } };
const options = { r2Provider: r2, gcsProvider: gcs, verifyRestore: verify };
const journal = async (date = atMorning) => readScheduledBackupAttempts(env, date);
let quotaReads = 0;
const quotaDb = { prepare() { quotaReads++; throw new Error("Your account has exceeded D1's free tier daily row read limit. private diagnostic"); } };
try {
  const ledger = JSON.stringify(db.db.prepare('SELECT * FROM transactions ORDER BY id').all());
  // D1 cannot even read the recovery flag. Durable failure evidence still exists.
  let result = await runScheduledBackup({ ...env, DB: quotaDb }, { ...options, now: atNight, cron: primary });
  assert.equal(result.error, 'D1_DAILY_LIMIT_EXCEEDED');
  assert.equal(quotaReads, 1, 'quota stops further D1 work and health scans');
  assert.equal(result.emailSent, true);
  assert.match(notifications[0].eventId, /^[0-9a-f]{8}(-[0-9a-f]{4}){3}-[0-9a-f]{12}$/);
  let attempts = await journal();
  assert.equal(attempts.length, 1);
  assert.equal(attempts[0].status, 'failed');
  assert.equal(attempts[0].day, '2026-10-20', '03:30 belongs to the next Taipei date');
  assert.doesNotMatch(JSON.stringify(attempts), /private diagnostic|TEST_INCOME|SELECT|session/i);
  const first = JSON.stringify(attempts[0]);
  result = await runScheduledBackup({ ...env, DB: quotaDb }, { ...options, now: atNight, cron: primary });
  assert.equal(result.reason, 'attempt_already_recorded');
  assert.equal(JSON.stringify((await journal())[0]), first, 'redelivery preserves the failure');
  assert.equal(notifications.length, 1);
  // At 08:00 the quota has reset. Exactly one normal canonical backup is made.
  result = await runScheduledBackup(env, { ...options, now: atMorning, cron: retry });
  assert.equal(result.ok, true);
  assert.equal(db.db.prepare('SELECT COUNT(*) AS n FROM backup_sets').get().n, 1);
  attempts = await journal();
  assert.deepEqual(attempts.map(row => row.status), ['success', 'failed']);
  const writes = r2.objects.size + gcs.objects.size;
  result = await runScheduledBackup(env, { ...options, now: atMorning, cron: retry });
  assert.equal(result.reason, 'attempt_already_recorded');
  assert.equal(r2.objects.size + gcs.objects.size, writes);
  assert.equal(JSON.stringify(db.db.prepare('SELECT * FROM transactions ORDER BY id').all()), ledger);

  // A completed primary run (even with a health-only error) must not be exported twice.
  const nextNight = new Date('2026-10-20T19:30:00Z'), nextMorning = new Date('2026-10-21T00:00:00Z');
  result = await runScheduledBackup(env, { ...options, now: nextNight, cron: primary });
  assert.equal(result.ok, true);
  const count = db.db.prepare('SELECT COUNT(*) AS n FROM backup_sets').get().n;
  const [one, two] = await Promise.all([
    runScheduledBackup(env, { ...options, now: nextMorning, cron: retry }),
    runScheduledBackup(env, { ...options, now: nextMorning, cron: retry })
  ]);
  assert.deepEqual([one.reason, two.reason].sort(), ['already_backed_up', 'attempt_already_recorded'].sort());
  assert.equal(db.db.prepare('SELECT COUNT(*) AS n FROM backup_sets').get().n, count);

  // Partial provider failure requires another paired backup on required GCS days.
  const failDay = new Date('2026-10-21T19:30:00Z'), retryDay = new Date('2026-10-22T00:00:00Z');
  const failingGcs = { ...gcs, async putObject() { throw Object.assign(new Error('private provider failure'), { code: 'TEST_GCS_DOWN' }); } };
  result = await runScheduledBackup(env, { ...options, gcsProvider: failingGcs, now: failDay, cron: primary });
  assert.equal(result.ok, false);
  result = await runScheduledBackup(env, { ...options, now: retryDay, cron: retry });
  assert.equal(result.ok, true);
  assert.ok((await journal(retryDay)).some(row => row.slot === 'primary' && row.status === 'failed'));

  // Manual success in the same Taipei day is sufficient; yesterday's is not.
  const manualDay = new Date('2026-10-22T23:55:00Z');
  await runTieredBackup(env, 'manual', { ...options, now: manualDay });
  result = await runScheduledBackup(env, { ...options, now: new Date('2026-10-23T00:00:00Z'), cron: retry });
  assert.equal(result.reason, 'already_backed_up');
  result = await runScheduledBackup(env, { ...options, now: new Date('2026-10-24T00:00:00Z'), cron: retry });
  assert.equal(result.ok, true, 'missing primary and older success do not suppress the catch-up');

  // If quota is still exhausted at 08:00, fail once, record it and stop.
  const stillDown = new Date('2026-10-25T00:00:00Z');
  result = await runScheduledBackup({ ...env, DB: quotaDb }, { ...options, now: stillDown, cron: retry });
  assert.equal(result.error, 'D1_DAILY_LIMIT_EXCEEDED');
  assert.equal((await journal(stillDown)).find(row => row.day === '2026-10-25').status, 'failed');
  result = await runScheduledBackup({ ...env, DB: quotaDb }, { ...options, now: stillDown, cron: retry });
  assert.equal(result.reason, 'attempt_already_recorded');

  // A quota failure during package construction never continues other table reads.
  const queries = [];
  const failMidExport = { prepare(sql) {
    queries.push(sql);
    if (sql.includes('FROM transactions')) throw new Error("Your account has exceeded D1's free tier daily row read limit.");
    return db.prepare(sql);
  } };
  result = await runScheduledBackup({ ...env, DB: failMidExport }, { ...options, now: new Date('2026-10-25T19:30:00Z'), cron: primary });
  assert.equal(result.error, 'D1_DAILY_LIMIT_EXCEEDED');
  assert.match(queries.at(-1), /FROM transactions/);
  assert.ok(!queries.some(sql => sql.includes('FROM opening_balance_overrides')));

  // An R2 journal outage must not suppress the independent GCS backup copy.
  let gcsUploads = 0;
  const journalDown = { async put() { throw new Error('TEST_JOURNAL_DOWN'); }, async delete() { throw new Error('TEST_JOURNAL_DOWN'); } };
  const failedR2 = { ...r2, async putObject() { throw new Error('TEST_R2_DOWN'); } };
  const countedGcs = { ...gcs, async putObject(...args) { gcsUploads++; return gcs.putObject(...args); } };
  result = await runScheduledBackup({ ...env, BACKUP_R2: journalDown }, { ...options, r2Provider: failedR2,
    gcsProvider: countedGcs, now: new Date('2026-10-26T19:30:00Z'), cron: primary });
  assert.equal(result.ok, false);
  assert.equal(result.attemptLogError, 'BACKUP_ATTEMPT_LOG_FAILED');
  assert.equal(gcsUploads, 2, 'GCS data and manifest still upload while R2 is down');

  // No unexpected Cron, recovery-workspace write, or legacy double-run.
  assert.equal((await runScheduledBackup(env, { cron: 'bad' })).reason, 'unknown_schedule');
  assert.equal((await runScheduledBackup({ BACKUP_TOPOLOGY: 'legacy_gcs' }, { cron: retry })).reason, 'legacy_retry_not_enabled');
  db.db.exec("INSERT INTO meta(key,value) VALUES('recovery_hold','1')");
  const journalWrites = bucket.calls.filter(call => call[0] === 'put').length;
  assert.equal((await runScheduledBackup(env, { ...options, now: new Date('2026-10-27T00:00:00Z'), cron: retry })).reason, 'recovery_workspace');
  assert.equal(bucket.calls.filter(call => call[0] === 'put').length, journalWrites);
  // The log read is bounded; packages are never selected as journal entries.
  const beforeReads = bucket.calls.length;
  await journal();
  assert.equal(bucket.calls.slice(beforeReads).filter(call => call[0] === 'get').length, 16);
} finally { db.close(); }

// Export limits fail closed, rather than publishing a truncated backup.
let packageCalls = 0;
const huge = { prepare(sql) { return { bind() { return this; }, async first() { return { value: 11 }; },
  async all() { packageCalls++; return { results: Array.from({ length: 1000 }, (_, id) => ({ id })) }; } }; } };
await assert.rejects(buildBackupPackage(huge), error => error.code === 'BACKUP_EXPORT_ROW_LIMIT');
assert.equal(packageCalls, 21);
await assert.rejects(buildRecoveryContext({ prepare() { return { bind() { return this; }, async all() { return { results: Array(20001).fill({}) }; } }; } }), error => error.code === 'BACKUP_RECOVERY_ROW_LIMIT');
console.log('Backup scheduling: independent outage journal, 08:00 retry, atomic dedup, quota stop, partial copies, manual/day boundary, recovery hold and bounded export passed.');
