import assert from 'node:assert/strict';
import initSqlJs from 'sql.js';
import { accountingFixture, memoryProvider, memoryR2Bucket, schemas } from './support/backup-fixtures.mjs';
import { runTieredBackup, runScheduledBackup, readBackupHealth, handleBackupApi, verifyStoredBackup } from '../src/backup-service.js';
import { verify } from './support/restore-fixture.mjs';
import { advanceRestoreSqlite, verifyRestoreIntoEmptySqlite } from '../src/backup-restore.js';
import { handleVerificationApi, runBackupVerification, VERIFICATION_STATE_KEY } from '../src/backup-verification.js';
import { validateBackupSetBytes } from '../src/gcs-backup.js';
import { backupPackageDigest } from '../src/backup-package.js';

const SQL = await initSqlJs();
const pair = await crypto.subtle.generateKey({name:'RSASSA-PKCS1-v1_5',modulusLength:2048,publicExponent:new Uint8Array([1,0,1]),hash:'SHA-256'},true,['sign','verify']);
const jwk = {...await crypto.subtle.exportKey('jwk',pair.publicKey),kid:'TEST_VERIFIER',use:'sig'};
const origin='https://backup.example.test', start=new Date('2026-11-04T19:30:00Z');
const encode = value => Buffer.from(typeof value === 'string' ? value : JSON.stringify(value)).toString('base64url');
async function signedRequest(path,body,now,claims={}) {
  const timestamp=Math.floor(now.getTime()/1000);
  const tokenClaims={iss:'https://token.actions.githubusercontent.com',aud:origin,repository:'simonliu1118-byte/CYapps',repository_id:'1',
    ref:'refs/heads/main',workflow_ref:'simonliu1118-byte/CYapps/.github/workflows/cyaccountingweb-backup-verify.yml@refs/heads/main',
    runner_environment:'github-hosted',event_name:'schedule',run_id:'123',run_attempt:'1',iat:timestamp,nbf:timestamp-5,exp:timestamp+300,...claims};
  const unsigned=encode({alg:'RS256',kid:jwk.kid})+'.'+encode(tokenClaims);
  const signature=await crypto.subtle.sign('RSASSA-PKCS1-v1_5',pair.privateKey,new TextEncoder().encode(unsigned));
  return new Request(origin+path,{method:body===undefined?'GET':'POST',headers:{authorization:'Bearer '+unsigned+'.'+Buffer.from(signature).toString('base64url'),'content-type':'application/json'},body:body===undefined?undefined:JSON.stringify(body)});
}
async function advance(snapshot,backup,cursor) {
  const db=snapshot?new SQL.Database(snapshot):new SQL.Database();
  try { return await advanceRestoreSqlite(db,schemas,backup,cursor); } finally { db.close(); }
}
function fixture() {
  const db=accountingFixture(),bucket=memoryR2Bucket(),r2=memoryProvider('cloudflare_r2'),gcs=memoryProvider('google_cloud_storage'),mails=[];
  const env={DB:db,BACKUP_R2:bucket,BACKUP_TOPOLOGY:'parallel_dual_provider',BACKUP_VERIFY_REPOSITORY_ID:'1',
    BACKUP_NOTIFICATIONS:{async notifySuperAdmin(value){mails.push(value);return {status:'delivered'};}}};
  const options={now:start,r2Provider:r2,gcsProvider:gcs,advanceRestore:advance,
    fetchImpl:async url=>{assert.equal(url,'https://token.actions.githubusercontent.com/.well-known/jwks');return Response.json({keys:[jwk]});}};
  return {db,bucket,r2,gcs,mails,env,options};
}
async function api(f,path,body,now,claims) {
  return handleVerificationApi(await signedRequest(path,body,now,claims),f.env,{...f.options,now});
}
async function state(f) {return JSON.parse(await (await f.bucket.get(VERIFICATION_STATE_KEY)).text());}
async function externalResults(f,job) {
  const results=[];
  for(const source of job.sources) {
    const provider=source.provider==='cloudflare_r2'?f.r2:f.gcs;
    const prefix=`CYAccountingWeb/${source.backupId}/`;
    const manifestBytes=await provider.getObject(prefix+'manifest.json'),dataBytes=await provider.getObject(prefix+'data.json');
    const backup=await validateBackupSetBytes(manifestBytes,dataBytes,source.backupId);
    const db=new SQL.Database();let proof;
    try {proof=await verifyRestoreIntoEmptySqlite(db,schemas,backup);} finally{db.close();}
    results.push({provider:source.provider,status:'success',packageSha256:await backupPackageDigest({manifestBytes,dataBytes}),
      dataSha256:backup.dataSha256,manifestSha256:backup.manifestSha256,proof});
  }
  return results;
}

// Real SQLite snapshots, many batches, exact row read-back, no live accounting writes.
const f=fixture();
try {
  const insert=f.db.db.prepare("INSERT INTO transactions(tx_date,account_name,kind,category_name,summary,amount,created_at,updated_at) VALUES('2026-01-01','現金','income','一般收入',?,10,'2026-01-01','2026-01-01')");
  f.db.db.exec('BEGIN');for(let i=0;i<1800;i++)insert.run('SYNTHETIC_'+i);f.db.db.exec('COMMIT');
  f.db.db.exec(`UPDATE transactions SET pending_confirmation=1 WHERE id=1;
    INSERT INTO transaction_confirmation_audit(transaction_id,previous_category,previous_summary,category_name,summary,actor_json,created_at)
    VALUES(2,'一般支出','TEST_BEFORE','一般支出','TEST_AFTER','{"employeeNo":"TEST"}','2026-11-05');`);
  const ledger=JSON.stringify(f.db.db.prepare('SELECT * FROM transactions').all());
  const scheduled=await runScheduledBackup(f.env,{...f.options,now:start,cron:'*/5 * * * *'});
  assert.equal(scheduled.health.status,'unknown');
  assert.equal(f.db.db.prepare('SELECT COUNT(*) AS n FROM backup_restore_checks').get().n,0,'export invocation never starts WASM verification');
  const early=new Date('2026-11-04T20:15:00Z');
  await runScheduledBackup(f.env,{...f.options,now:early,cron:'*/5 * * * *'});assert.equal((await state(f)).fallback.status,'waiting');
  assert.equal(f.db.db.prepare('SELECT COUNT(*) AS n FROM backup_sets').get().n,1,'five-minute ticks do not export another backup');
  await runScheduledBackup(f.env,{...f.options,now:new Date('2026-11-05T03:55:00Z'),cron:'*/5 * * * *'});assert.equal((await state(f)).fallback.status,'waiting','external may still claim until 12:00 Taipei');
  let now=new Date('2026-11-05T04:00:00Z'),ticks=0,result;
  do {
    result=await runBackupVerification(f.env,{...f.options,now});
    now=new Date(now.getTime()+300000);ticks++;
    assert.ok(ticks<100);
  } while(!result.verified);
  assert.ok(ticks>10,'fallback continues for as many batches as required, not two slots');
  assert.equal(result.status,'warning','external unfinished remains visible even when fallback passes');
  assert.equal(result.emailSent,false);assert.equal(f.mails.length,0);
  assert.equal(f.db.db.prepare('SELECT COUNT(*) AS n FROM backup_restore_checks').get().n,2);
  assert.equal(JSON.stringify(f.db.db.prepare('SELECT * FROM transactions').all()),ledger);
  const reads=f.bucket.calls.length;
  await runBackupVerification(f.env,{...f.options,now});
  assert.deepEqual(f.bucket.calls.slice(reads).map(call=>call[0]),['get'],'healthy idle tick has no export, D1 scan, WASM or R2 writes');
  assert.equal((await handleBackupApi(new Request(origin+'/api/backup/health'),f.env,{role:'ADMIN'})).status,403);
  assert.doesNotMatch(JSON.stringify(await readBackupHealth(f.env,now)),/SYNTHETIC|TEST_|packageSha|backupId/);
}finally{f.db.close();}

// External success, authenticated streaming, source identity and replay boundaries.
const g=fixture();
try {
  const previous=await runTieredBackup(g.env,'manual',{...g.options,now:new Date(start.getTime()-86400000)});
  const checks=await verifyStoredBackup(g.env,{role:'SUPER_ADMIN'},previous.backupId,{...g.options,verifyRestore:verify});
  g.db.db.prepare("INSERT INTO backup_policy_events(target_phase,r2_check_id,gcs_check_id,acceptance_json,actor_json,created_at) VALUES('phase_d',?,?,'{}','{}','2026-11-04')").run(checks[0].id,checks[1].id);
  await runScheduledBackup(g.env,g.options);
  const now=new Date('2026-11-04T20:10:00Z');
  assert.equal((await handleVerificationApi(new Request(origin+'/api/backup/verification/start',{method:'POST'}),g.env,g.options)).status,403);
  for(const claims of [{repository_id:'2'},{ref:'refs/heads/feature'},{event_name:'pull_request'},{workflow_ref:'simonliu1118-byte/CYapps/.github/workflows/other.yml@refs/heads/main'}]) {
    assert.equal((await api(g,'/api/backup/verification/start',{},now,claims)).status,403);
  }
  const forged=await signedRequest('/api/backup/verification/start',{},now);
  const pieces=forged.headers.get('authorization').split('.');
  pieces[2]=(pieces[2][0]==='A'?'B':'A')+pieces[2].slice(1);
  forged.headers.set('authorization',pieces.join('.'));
  assert.equal((await handleVerificationApi(forged,g.env,{...g.options,now})).status,403,'a forged signature cannot fetch backup data');
  const job=await (await api(g,'/api/backup/verification/start',{},now)).json();
  assert.notEqual(job.sources[0].backupId,job.sources[1].backupId,'Phase D verifies today R2 and the older expected GCS independently');
  const source=await api(g,`/api/backup/verification/source?job=${job.jobId}&provider=cloudflare_r2&part=data`,undefined,now);
  assert.equal(source.status,200);assert.equal(source.headers.get('cache-control'),'no-store');await source.body.cancel();
  assert.equal((await api(g,`/api/backup/verification/source?job=${job.jobId}&provider=cloudflare_r2&part=data`,undefined,now,{run_id:'456'})).status,409);
  const results=await externalResults(g,job);
  const response=await api(g,`/api/backup/verification/complete?job=${job.jobId}`,{results},now);
  assert.equal((await response.json()).status,'success');
  assert.equal((await readBackupHealth(g.env,now)).status,'healthy');assert.equal(g.mails.length,0);
  assert.equal((await (await api(g,`/api/backup/verification/complete?job=${job.jobId}`,{results:[]},now)).json()).complete,true,'late failure cannot erase a completed proof');
  assert.equal((await readBackupHealth(g.env,new Date(now.getTime()+37*3600000))).verified,false,'old successful evidence does not stay healthy forever');
  g.bucket.get=async()=>{throw new Error('TEST_R2_OUTAGE');};
  const alreadyPassed=await runBackupVerification(g.env,{...g.options,now:new Date(now.getTime()+3600000)});
  assert.equal(alreadyPassed.verified,true,'a coordinator outage does not invent two failed checks after today already passed');
  assert.equal(g.mails.length,0);
}finally{g.db.close();}

// Neither external-only failure nor normal progress emails the administrator.
// Three consecutive interrupted/failed fallback attempts do; one incident only.
const h=fixture();
try {
  await runScheduledBackup(h.env,h.options);
  let now=new Date('2026-11-04T20:10:00Z');
  const job=await (await api(h,'/api/backup/verification/start',{},now)).json();
  await api(h,`/api/backup/verification/complete?job=${job.jobId}`,{results:[]},now);
  assert.equal(h.mails.length,0);
  for(let i=0;i<3;i++) {
    const active=await state(h);
    active.lease={kind:'fallback',token:'TEST_TERMINATED',until:new Date(now.getTime()-1).toISOString()};
    // Simulate runtime termination, which does not run catch/finally.
    await h.bucket.put(VERIFICATION_STATE_KEY,JSON.stringify(active));
    // The next invocation also cannot make progress; cumulative lease losses matter.
    const provider={...h.r2,async getObjectStream(){throw new Error('PRIVATE_DATA_MUST_NOT_ESCAPE');}};
    await runBackupVerification(h.env,{...h.options,r2Provider:provider,now});
    now=new Date(now.getTime()+300000);
    if((await state(h)).fallback.status==='failed')break;
  }
  assert.equal((await readBackupHealth(h.env,now)).status,'failed');assert.equal(h.mails.length,1);
  await runBackupVerification(h.env,{...h.options,now});assert.equal(h.mails.length,1);
  assert.deepEqual(Object.keys(h.mails[0]).sort(),['applicationId','eventId','type','workspaceId']);
}finally{h.db.close();}

// R2's coordinator can itself fail. A small existing D1 status projection
// keeps the independent notification route alive without storing another job.
const outage=fixture();
try {
  await runScheduledBackup(outage.env,outage.options);
  outage.bucket.get=async()=>{throw new Error('TEST_R2_OUTAGE');};
  let now=new Date('2026-11-05T04:00:00Z');
  for(let i=0;i<3;i++) {
    await runBackupVerification(outage.env,{...outage.options,now});
    now=new Date(now.getTime()+300000);
  }
  assert.equal(outage.mails.length,1);
  assert.equal((await readBackupHealth(outage.env,now)).status,'failed');
  await runBackupVerification(outage.env,{...outage.options,now});assert.equal(outage.mails.length,1);
}finally{outage.db.close();}
console.log('Backup verification: external OIDC/streaming, real restartable SQLite batches, dual-channel notifications, idle cost and live-ledger isolation passed.');
