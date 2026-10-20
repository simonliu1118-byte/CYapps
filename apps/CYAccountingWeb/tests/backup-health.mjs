import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { DatabaseSync } from 'node:sqlite';
import { accountingFixture, memoryProvider, memoryR2Bucket, session, schemas } from './support/backup-fixtures.mjs';
import { verify } from './support/restore-fixture.mjs';
import { runTieredBackup, checkBackupHealth, readBackupHealth, handleBackupApi, runScheduledBackup, verifyStoredBackup } from '../src/backup-service.js';

const legacy=new DatabaseSync(':memory:');
try {
  for(const migration of schemas.slice(0,-1))legacy.exec(migration);
  legacy.exec("INSERT INTO transactions(tx_date,account_name,kind,category_name,summary,amount,created_at,updated_at) VALUES('2026-01-01','現金','income','一般收入','TEST_PRESERVE',99,'2026-01-01','2026-01-01')");
  const snapshot=()=>JSON.stringify(['accounts','categories','category_groups','transactions','opening_balance_overrides','opening_balance_audit','app_settings','excel_import_runs'].map(table=>legacy.prepare('SELECT * FROM '+table).all()));
  const before=snapshot();
  assert.ok(legacy.prepare("SELECT name FROM sqlite_master WHERE name='web_sessions'").get());
  legacy.exec(schemas.at(-1));
  assert.equal(legacy.prepare("SELECT name FROM sqlite_master WHERE name='web_sessions'").get(),undefined);
  assert.equal(snapshot(),before,'forward migration preserves every accounting/settings/audit row');
}finally{legacy.close();}

const R2='cloudflare_r2',GCS='google_cloud_storage';
const db=accountingFixture(),r2=memoryProvider(R2),gcs=memoryProvider(GCS);
let notifications=[], failMail=false;
const env={DB:db,BACKUP_R2:memoryR2Bucket(),BACKUP_TOPOLOGY:'parallel_dual_provider',CYID_WORKSPACE_ID:'workspace-test',CYID_APPLICATION_ID:'TEST_APP',
  BACKUP_NOTIFICATIONS:{async notifySuperAdmin(input){notifications.push(input);if(failMail)throw new Error('private diagnostic');return {status:'delivered'};}}};
const options={r2Provider:r2,gcsProvider:gcs,verifyRestore:verify};
const sunday=new Date('2026-10-18T19:30:00Z'); // Taipei Monday: R2 only after policy activation.
try {
  // Forward migration retires only the old local Session table.
  assert.equal(db.db.prepare("SELECT name FROM sqlite_master WHERE name='web_sessions'").get(),undefined);
  assert.equal((await readBackupHealth(db)).status,'unknown');
  const accounting=JSON.stringify(db.db.prepare('SELECT * FROM transactions ORDER BY id').all());
  const pair=await runTieredBackup(env,'manual',{...options,now:new Date(sunday.getTime()-86400000)});
  const checks=await verifyStoredBackup(env,session,pair.backupId,options);
  db.db.prepare("INSERT INTO backup_policy_events(target_phase,r2_check_id,gcs_check_id,acceptance_json,actor_json,created_at) VALUES('phase_d',?,?,'{}','{}','2026-10-18')").run(checks[0].id,checks[1].id);
  let result=await runScheduledBackup(env,{...options,now:sunday});
  assert.equal(result.gcsExpected,false);
  assert.equal(result.health.status,'healthy','unscheduled GCS validates its latest actual copy, never assumes a same-day pair');
  const completeSets = db.db.prepare('SELECT COUNT(*) AS n FROM backup_sets').get().n;
  const catchUp = await runScheduledBackup(env,{...options,now:new Date(sunday.getTime()+4.5*3600000),cron:'0 0 * * *'});
  assert.equal(catchUp.reason,'already_backed_up','Phase D R2-only days do not require a same-day GCS copy');
  assert.equal(db.db.prepare('SELECT COUNT(*) AS n FROM backup_sets').get().n,completeSets);
  assert.equal(notifications.length,0,'healthy checks never send mail');
  const ledger=()=>JSON.stringify(db.db.prepare('SELECT * FROM transactions ORDER BY id').all());
  assert.equal(ledger(),accounting,'real isolated SQLite verification never modifies live transactions');
  const actor=JSON.parse(db.db.prepare('SELECT actor_json FROM backup_restore_checks ORDER BY sequence DESC LIMIT 1').get().actor_json);
  assert.equal(actor.type,'scheduled_backup_health');assert.equal(actor.role,undefined,'automation does not impersonate a user');
  const key=`CYAccountingWeb/${pair.backupId}/data.json`,original=gcs.objects.get(key);
  gcs.objects.set(key,new TextEncoder().encode('{}'));
  result=await checkBackupHealth(env,{...options,now:sunday});
  assert.equal(result.status,'failed');assert.equal(result.issues[0].provider,GCS);assert.equal(result.emailSent,true);
  assert.equal(notifications.length,1);assert.equal(notifications[0].type,'backup_health_failed');
  assert.deepEqual(Object.keys(notifications[0]).sort(),['applicationId','eventId','type','workspaceId']);
  const incident=result.incidentId;
  await checkBackupHealth(env,{...options,now:sunday});assert.equal(notifications.length,1,'ongoing incident not resent');
  const denied=await handleBackupApi(new Request('https://example.invalid/api/backup/health'),env,{role:'ADMIN'});
  assert.equal(denied.status,403);
  const allowed=await (await handleBackupApi(new Request('https://example.invalid/api/backup/health'),env,session)).json();
  assert.doesNotMatch(JSON.stringify(allowed),/TEST_ARCHIVED|packageSha256|email_normalized|private diagnostic/);
  gcs.objects.set(key,original);
  result=await checkBackupHealth(env,{...options,now:sunday});
  assert.equal(result.status,'healthy');assert.equal(result.incidentId,null,'recovery clears active incident');
  gcs.objects.set(key,new TextEncoder().encode('{}'));failMail=true;
  result=await checkBackupHealth(env,{...options,now:sunday});
  assert.notEqual(result.incidentId,incident);assert.equal(result.emailPending,true);
  const retryIncident=result.incidentId;failMail=false;
  result=await checkBackupHealth(env,{...options,now:sunday});
  assert.equal(result.emailSent,true);assert.equal(notifications.at(-1).eventId,retryIncident,'mail retry retains incident identity');
  gcs.objects.set(key,original);
  // Leased concurrent invocations cannot start another provider scan or send.
  db.db.prepare("UPDATE backup_health_state SET lease_token='TEST',lease_until=?").run(new Date(sunday.getTime()+1000).toISOString());
  assert.equal((await checkBackupHealth(env,{...options,now:sunday})).reason,'check_in_progress');
  db.db.exec("UPDATE backup_health_state SET lease_token='',lease_until=''");
  result=await checkBackupHealth(env,{...options,now:new Date(sunday.getTime()+6*86400000)});
  assert.ok(result.issues.every(item=>item.code==='BACKUP_COPY_STALE'));
  assert.equal((await readBackupHealth(db,new Date(sunday.getTime()+8*86400000))).issues[0].code,'BACKUP_HEALTH_OVERDUE');
  // New failed backups cannot be hidden by choosing an older successful copy.
  const failing={...r2,async putObject(){throw new Error('TEST_STORAGE_DOWN');}};
  result=await runScheduledBackup(env,{...options,r2Provider:failing,now:new Date(sunday.getTime()+86400000)});
  assert.equal(result.ok,false);assert.equal(result.health.status,'failed');
  assert.ok(result.health.issues.some(item=>item.code==='BACKUP_COPY_FAILED'));
  assert.equal(ledger(),accounting);
  db.db.exec("INSERT INTO meta(key,value) VALUES('recovery_hold','1')");
  assert.equal((await runScheduledBackup(env,options)).reason,'recovery_workspace');
  assert.equal((await checkBackupHealth(env,options)).reason,'recovery_workspace');
  const outage=await runScheduledBackup({...env,BACKUP_R2:memoryR2Bucket(),DB:{prepare(){throw new Error('private database diagnostic');}}},options);
  assert.equal(outage.error,'BACKUP_DATABASE_UNAVAILABLE');assert.equal(outage.emailSent,true,'independent Identity mail still works during an accounting DB outage');
  assert.match(readFileSync(new URL('../wrangler.template.jsonc',import.meta.url),'utf8'),/"entrypoint": "ConsumerNotifications"/);
}finally{db.close();}
console.log('Backup health: real SQLite roundtrip, independent Phase D copies, corrupt/stale/failed sources, scheduled integration, mail incidents/retry/dedup, lease, Super Admin guard and recovery hold passed.');
