import assert from 'node:assert/strict';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { execFileSync, spawnSync } from 'node:child_process';
import { DatabaseSync } from 'node:sqlite';
import { RECOVERY_SEED_SNAPSHOT_SQL } from '../src/backup-recovery.js';

const folder=fs.mkdtempSync(path.join(os.tmpdir(),'cyacc-provision-test-'));
const primary='11111111-1111-4111-8111-111111111111',target='22222222-2222-4222-8222-222222222222';
const schemas=fs.readdirSync('migrations').filter(name=>name.endsWith('.sql')).sort().map(name=>fs.readFileSync(path.join('migrations',name),'utf8'));
const mock=path.join(folder,'mock.mjs');
try {
  fs.writeFileSync(mock,String.raw`import fs from 'node:fs';import childProcess from 'node:child_process';import {syncBuiltinESMExports} from 'node:module';import {DatabaseSync} from 'node:sqlite';
const calls=[],db=new DatabaseSync(process.env.TEST_DB);process.on('exit',()=>{db.close();fs.writeFileSync(process.env.TEST_TRACE,JSON.stringify(calls));});
childProcess.execFileSync=(binary,args)=>{const config=JSON.parse(fs.readFileSync(args[args.indexOf('--config')+1],'utf8'));calls.push({migration:config.d1_databases[0]});if(process.env.TEST_SCENARIO==='migration-failed')throw new Error('PRIVATE_DIAGNOSTIC');if(!db.prepare("SELECT name FROM sqlite_master WHERE name='meta'").get())for(const sql of JSON.parse(fs.readFileSync(process.env.TEST_SCHEMAS,'utf8')))db.exec(sql);return Buffer.from('');};syncBuiltinESMExports();
globalThis.fetch=async(url,options={})=>{const body=options.body?JSON.parse(options.body):null;calls.push({method:options.method,url,body});const scenario=process.env.TEST_SCENARIO;
if(scenario==='denied')return Response.json({success:false,errors:[{code:10000,message:'PRIVATE_DIAGNOSTIC'}]},{status:403});
if(scenario==='quota')return Response.json({success:false,errors:[{code:10064,message:'PRIVATE_DIAGNOSTIC'}]},{status:429});
if(options.method==='GET'){
 let rows=[{uuid:'${primary}',name:'test-primary'},{uuid:'33333333-3333-4333-8333-333333333333',name:'unrelated'}];
 if(['existing','occupied','unmarked','modified-seed','partial-seed'].includes(scenario))rows.push({uuid:'${target}',name:'test-worker-recovery'});
 if(scenario==='ambiguous')rows.push({uuid:'${target}',name:'test-worker-recovery'},{uuid:'44444444-4444-4444-8444-444444444444',name:'test-worker-recovery-old'});
 if(scenario==='primary-collision')rows=[{uuid:'${primary}',name:'test-worker-recovery'}];
 return Response.json({success:true,result:rows,result_info:{total_pages:scenario==='inventory-limit'?3:1}});
}
if(!String(url).includes('/query'))return scenario==='full'?Response.json({success:false,errors:[{code:7502,message:'PRIVATE_DIAGNOSTIC'}]},{status:400}):Response.json({success:true,result:{uuid:'${target}',name:'test-worker-recovery'}});
if(!String(url).includes('/${target}/query'))throw new Error('PRIMARY_OR_UNRELATED_DB_ACCESSED');
let results=[];if(/^\s*SELECT/i.test(body.sql))results=db.prepare(body.sql).all(...body.params);else db.prepare(body.sql).run(...body.params);
return Response.json({success:true,result:[{results}]});};`);
  const schemaFile=path.join(folder,'schemas.json');fs.writeFileSync(schemaFile,JSON.stringify(schemas));
  let sequence=0;
  function run(scenario,override={}) {
    const stem=String(++sequence),database=path.join(folder,stem+'.sqlite'),trace=path.join(folder,stem+'.trace.json'),githubEnv=path.join(folder,stem+'.env');
    if(['existing','occupied','unmarked','modified-seed','partial-seed'].includes(scenario)) {
      const db=new DatabaseSync(database);for(const sql of schemas)db.exec(sql);
      if(scenario!=='unmarked') {
        db.prepare("INSERT INTO meta(key,value) VALUES('recovery_workspace_id','TEST_WORKSPACE')").run();
        db.prepare("INSERT INTO meta(key,value) VALUES('recovery_seed_snapshot',?)").run(db.prepare(RECOVERY_SEED_SNAPSHOT_SQL).get().snapshot);
      }
      if(scenario==='occupied')db.exec("INSERT INTO app_settings(key,value) VALUES('TEST_RESTORED','PRIVATE_TEST_VALUE')");
      if(scenario==='modified-seed')db.exec("UPDATE accounts SET name='TEST_CHANGED_SEED'");
      if(scenario==='partial-seed')db.exec('DELETE FROM categories');
      db.close();
    }
    const token=crypto.randomUUID(),env={...process.env,CLOUDFLARE_ACCOUNT_ID:'test-account',CLOUDFLARE_API_TOKEN:token,CF_D1_DATABASE_ID:primary,CF_WORKER_NAME:'test-worker',GITHUB_ENV:githubEnv,TEST_TRACE:trace,TEST_DB:database,TEST_SCHEMAS:schemaFile,TEST_SCENARIO:scenario,RECOVERY_D1_MODE:'prepare',GITHUB_EVENT_NAME:'workflow_dispatch',GITHUB_REF:'refs/heads/main',...override};
    const result=spawnSync(process.execPath,['--import',mock,'scripts/provision-recovery-d1.mjs'],{env,encoding:'utf8'});
    const calls=JSON.parse(fs.readFileSync(trace,'utf8')),binding=fs.existsSync(githubEnv)?fs.readFileSync(githubEnv,'utf8'):'';
    assert.doesNotMatch(result.stdout+result.stderr,new RegExp(token+'|PRIVATE_DIAGNOSTIC|PRIVATE_TEST_VALUE|TEST_CHANGED_SEED'));
    assert.ok(!calls.some(call=>call.url?.includes('/'+primary+'/query')),'never queries or mutates primary D1');
    assert.ok(!calls.some(call=>call.method==='DELETE'),'never deletes a cloud database');
    return {...result,calls,binding,database};
  }
  // Default publishing performs zero Cloudflare calls and clears stale binding inputs.
  for(const override of [{RECOVERY_D1_MODE:undefined},{RECOVERY_D1_MODE:'disabled'}]) {
    const r=run('create',{...override,CLOUDFLARE_ACCOUNT_ID:'',CLOUDFLARE_API_TOKEN:'',CF_D1_DATABASE_ID:'',CF_WORKER_NAME:''});
    assert.equal(r.status,0,r.stderr);assert.equal(r.calls.length,0);
    assert.equal(r.binding,'CF_RECOVERY_D1_DATABASE_ID=\nCF_RECOVERY_D1_DATABASE_NAME=\n');
  }
  for(const override of [{RECOVERY_D1_MODE:'bad'},{GITHUB_EVENT_NAME:'push'},{GITHUB_REF:'refs/heads/feature/test'}]) {
    const r=run('create',override);assert.notEqual(r.status,0);assert.equal(r.calls.length,0);
  }
  let r=run('create');assert.equal(r.status,0,r.stderr);
  assert.equal(r.calls.filter(call=>call.method==='GET').length,1);
  assert.equal(r.calls.filter(call=>call.method==='POST'&&!call.url.includes('/query')).length,1);
  assert.equal(r.calls.find(call=>call.migration).migration.database_id,target);
  assert.match(r.binding,/CF_RECOVERY_D1_DATABASE_ID=22222222/);
  r=run('existing');assert.equal(r.status,0,r.stderr);
  assert.equal(r.calls.filter(call=>call.method==='POST'&&!call.url.includes('/query')).length,0,'reuse never creates another D1');
  for(const scenario of ['occupied','unmarked','modified-seed','partial-seed']) {
    r=run(scenario);assert.notEqual(r.status,0);assert.match(r.stderr,/RECOVERY_TARGET_NOT_EMPTY/);
    assert.ok(!r.calls.some(call=>call.migration),'refusal happens before migration');
    assert.ok(r.calls.filter(call=>call.body?.sql).every(call=>/^SELECT/.test(call.body.sql)),'refusal performs read-only bounded checks');
    assert.doesNotMatch(r.binding,/CF_RECOVERY_D1_DATABASE_ID=2222/);
  }
  for(const [scenario,code,count] of [['ambiguous','RECOVERY_TARGET_AMBIGUOUS',1],['primary-collision','RECOVERY_TARGET_EQUALS_PRIMARY',1],['inventory-limit','RECOVERY_INVENTORY_LIMIT',2],['denied','RECOVERY_CLOUDFLARE_403_10000',1],['quota','RECOVERY_CLOUDFLARE_429_10064',1],['full','RECOVERY_CLOUDFLARE_400_7502',2]]) {
    r=run(scenario);assert.notEqual(r.status,0);assert.match(r.stderr,new RegExp(code));
    assert.equal(r.calls.length,count,'failure stops without retry');assert.ok(!r.calls.some(call=>call.migration));
  }
  r=run('migration-failed');assert.notEqual(r.status,0);assert.match(r.stderr,/RECOVERY_MIGRATION_FAILED/);
  assert.doesNotMatch(r.binding,/CF_RECOVERY_D1_DATABASE_ID=2222/);
  console.log('On-demand recovery: zero-call normal publishing, protected opt-in, actual SQLite seed/reuse guards, occupied/ambiguous/primary rejection, bounded inventory, quota stop, migration failure, no deletion or primary D1 access passed.');
} finally {fs.rmSync(folder,{recursive:true,force:true});}
