import assert from 'node:assert/strict';
import { accountingFixture, memoryProvider, session } from './support/backup-fixtures.mjs';
import { verify } from './support/restore-fixture.mjs';
import { backupExecutionPlan, runTieredBackup, readBackupPolicy, readPhaseCAcceptance, verifyStoredBackup, activatePhaseD, parallelBackupStatus, handleBackupApi } from '../src/backup-service.js';
const R2 = 'cloudflare_r2', GCS = 'google_cloud_storage';
const db = accountingFixture(), r2 = memoryProvider(R2), gcs = memoryProvider(GCS);
const options = { r2Provider:r2, gcsProvider:gcs, verifyRestore:verify };
// Configuration markers are synthetic; all provider I/O uses in-memory doubles.
const env = { DB:db, BACKUP_TOPOLOGY:'parallel_dual_provider', BACKUP_R2:{put(){},get(){},list(){},delete(){}}, GCS_BUCKET:'test-only',
  GCS_SERVICE_ACCOUNT_JSON:JSON.stringify({type:'service_account',client_email:'test@example.invalid',private_key:'-----BEGIN PRIVATE KEY-----\nTEST_ONLY\n-----END PRIVATE KEY-----'}) };
const status = async () => (await parallelBackupStatus(env)).json();
const rejectCode = (promise,code) => assert.rejects(promise,error=>error.code===code);
for (const [date,expected] of [['2026-10-13T15:59:59Z',false],['2026-10-13T16:00:00Z',true],['2026-10-14T16:00:00Z',false],['2026-10-17T19:30:00Z',true]]) {
  assert.equal(backupExecutionPlan('phase_d','scheduled',new Date(date)).gcsExpected,expected,'schedule uses Taipei day');
  assert.equal(backupExecutionPlan('phase_d','manual',new Date(date)).gcsExpected,true,'manual and pre-import safety backups remain dual');
  assert.equal(backupExecutionPlan('phase_c','scheduled',new Date(date)).gcsExpected,true);
}
assert.equal((await readBackupPolicy(db)).phase,'phase_c');
await rejectCode(activatePhaseD(env,session),'PHASE_C_INCOMPLETE');
await rejectCode(activatePhaseD(env,{role:'ADMIN'}),'SUPER_ADMIN_REQUIRED');
const unchanged = JSON.stringify(db.db.prepare('SELECT * FROM transactions ORDER BY id').all());
const start = new Date('2026-09-20T19:30:00Z'); let last;
for(let day=0;day<14;day++) {
  if (day===13) assert.equal((await readPhaseCAcceptance(db)).completed,false,'13 successes are insufficient');
  last=await runTieredBackup(env,'scheduled',{...options,now:new Date(start.getTime()+day*86400000)});
}
assert.equal((await readPhaseCAcceptance(db)).completed,true);
await rejectCode(activatePhaseD(env,session),'RESTORE_PAIR_REQUIRED');
await rejectCode(verifyStoredBackup(env,session,'../unsafe',options),'BACKUP_ID_INVALID');
await rejectCode(verifyStoredBackup(env,session,'20200101T000000Z',options),'BACKUP_SOURCE_MISSING');
await rejectCode(verifyStoredBackup(env,{role:'ADMIN'},'',options),'SUPER_ADMIN_REQUIRED');
let checks=await verifyStoredBackup(env,session,last.backupId,options);
assert.ok(checks.every(check=>check.status==='success' && check.result.fullRowRoundtrip));
assert.equal((await status()).phaseDReady,true);
assert.equal(JSON.stringify(db.db.prepare('SELECT * FROM transactions ORDER BY id').all()),unchanged,'verification cannot write live accounting');
assert.equal(JSON.parse(db.db.prepare('SELECT actor_json FROM backup_restore_checks LIMIT 1').get().actor_json).employeeNo,'TEST');
// Latest failed attempt supersedes older successful evidence.
const key=`CYAccountingWeb/${last.backupId}/data.json`, original=gcs.objects.get(key);
gcs.objects.set(key,new TextEncoder().encode('{}'));
checks=await verifyStoredBackup(env,session,last.backupId,options);
assert.equal(checks.find(check=>check.provider===GCS).status,'failed');
assert.equal((await status()).phaseDReady,false);
await rejectCode(activatePhaseD(env,session),'RESTORE_PAIR_REQUIRED');
gcs.objects.set(key,original);
await verifyStoredBackup(env,session,last.backupId,options);
// Successful provider bytes still must match immutable catalog evidence.
const swapped=memoryProvider(GCS);
for(const [name,bytes] of gcs.objects) swapped.objects.set(name,bytes);
const otherPrefix='CYAccountingWeb/20260920T193000Z/';
swapped.objects.set(key,gcs.objects.get(otherPrefix+'data.json'));
// Manifests from a different logical backup fail regardless of identical row data.
swapped.objects.set(`CYAccountingWeb/${last.backupId}/manifest.json`,gcs.objects.get(otherPrefix+'manifest.json'));
checks=await verifyStoredBackup(env,session,last.backupId,{...options,gcsProvider:swapped});
assert.equal(checks.find(check=>check.provider===GCS).status,'failed');
await verifyStoredBackup(env,session,last.backupId,options);
// A new attempt with only one successful copy cannot reuse its predecessor's other copy.
const latestCheck=db.db.prepare('SELECT * FROM backup_restore_checks ORDER BY sequence DESC LIMIT 1').get();
db.db.prepare(`INSERT INTO backup_restore_checks(id,attempt_id,backup_id,provider,package_sha256,source_schema_version,target_schema_version,status,result_json,actor_json,created_at) VALUES('TEST_PARTIAL','TEST_NEW_ATTEMPT',?,?,?,9,9,'success',?,'{}',?)`).run(last.backupId,R2,last.packageSha256,latestCheck.result_json,new Date().toISOString());
assert.equal((await status()).phaseDReady,false);
await rejectCode(activatePhaseD(env,session),'RESTORE_PAIR_REQUIRED');
await verifyStoredBackup(env,session,last.backupId,options);
const beforeDeletes=[r2.deleted.length,gcs.deleted.length];
assert.equal((await activatePhaseD(env,session)).phase,'phase_d');
assert.deepEqual([r2.deleted.length,gcs.deleted.length],beforeDeletes,'activation preserves all existing objects');
await activatePhaseD(env,session);
assert.equal(db.db.prepare('SELECT COUNT(*) AS n FROM backup_policy_events').get().n,1,'activation is idempotent');
for(const table of ['backup_policy_events','backup_restore_checks']) {
  assert.throws(()=>db.db.exec(`DELETE FROM ${table}`),/append.only/i);
  assert.throws(()=>db.db.exec(`UPDATE ${table} SET created_at='TEST'`),/append.only/i);
}
const monday=new Date('2026-10-18T19:30:00Z');
const result=await runTieredBackup(env,'scheduled',{...options,now:monday});
assert.equal(result.gcsExpected,false); assert.equal(result.copies.length,1);
assert.equal((await status()).logicalBackups[0].status,'success','unscheduled GCS does not produce false failure');
// Operational journals are optional: a stuck R2 read cannot hide valid D1 history.
const degraded = await (await handleBackupApi(new Request('https://example.invalid/api/backup/status'),
  { ...env, BACKUP_R2: { ...env.BACKUP_R2, get() { return new Promise(() => {}); } } }, session,
  { attemptLogTimeoutMs: 10 })).json();
assert.equal(degraded.attemptLogAvailable, false);
assert.equal(degraded.logicalBackups[0].backupId, result.backupId);
assert.deepEqual(degraded.logicalBackups, (await status()).logicalBackups);
assert.ok(degraded.logicalBackups.every(row => row.copies.every(copy =>
  db.db.prepare('SELECT status FROM backup_copies WHERE backup_id=? AND provider=?').get(row.backupId, copy.provider)?.status === copy.status)));
assert.equal((await readPhaseCAcceptance(db)).completed,true,'Phase D does not erase Phase C evidence');
// Retention boundaries: old GCS validation copies survive the new 182-day policy.
assert.equal(gcs.deleted.length,0);
await runTieredBackup(env,'manual',{...options,now:new Date('2026-10-19T12:00:00Z')});
assert.equal(gcs.deleted.length,0);
const expireNow=new Date('2027-04-20T19:30:00Z');
await runTieredBackup(env,'manual',{...options,now:expireNow});
assert.ok(gcs.deleted.some(key=>key.includes('20260920T193000Z')));
assert.ok(r2.deleted.some(key=>key.includes('20261019T120000Z')));
// GCS failure on a required day never suppresses the independent R2 copy.
const failing={...gcs,async putObject(){throw new Error('TEST_GCS_DOWN');}};
await assert.rejects(runTieredBackup(env,'scheduled',{...options,gcsProvider:failing,now:new Date('2027-04-21T00:00:00Z')}),error=>{
  assert.equal(error.backupResult.copies.find(copy=>copy.provider===R2).status,'success');
  assert.equal(error.backupResult.copies.find(copy=>copy.provider===GCS).status,'failed');return error.code==='BACKUP_COPY_PARTIAL';
});
const request = (path,body) => new Request('https://example.invalid/api/backup/'+path,{method:'POST',headers:{'content-type':'application/json'},body:JSON.stringify(body)});
assert.equal((await handleBackupApi(request('verify-restore',{}),env,{role:'ADMIN'},options)).status,403);
assert.equal((await handleBackupApi(request('phase-d/activate',{}),env,session,options)).status,409);
assert.equal((await handleBackupApi(request('phase-d/activate',{confirm:'phase_d'}),env,session,options)).status,200);
// Freshness is enforced server-side; old success cannot activate a new policy.
const stale=accountingFixture();
const staleEnv={...env,DB:stale};
for(let day=0;day<14;day++) await runTieredBackup(staleEnv,'scheduled',{...options,now:new Date(start.getTime()+day*86400000)});
for(const provider of [R2,GCS]) stale.db.prepare(`INSERT INTO backup_restore_checks(id,attempt_id,backup_id,provider,package_sha256,source_schema_version,target_schema_version,status,result_json,actor_json,created_at) VALUES(?,'TEST_ATTEMPT',?,?,?,9,9,'success',?,'{}','2000-01-01')`).run(provider,last.backupId,provider,'a'.repeat(64),JSON.stringify({fullRowRoundtrip:true,integrityCheck:true,foreignKeyCheck:true}));
await rejectCode(activatePhaseD(staleEnv,session),'RESTORE_PAIR_REQUIRED');
stale.close();db.close();
console.log('Phase D: Taipei schedule, independent providers, 30/182 retention, 14-run gate, real isolated restore proofs, tamper/stale rejection, append-only audit, authorization and idempotent activation passed.');
