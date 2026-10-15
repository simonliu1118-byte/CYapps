import assert from 'node:assert/strict';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { execFileSync } from 'node:child_process';
const folder=fs.mkdtempSync(path.join(os.tmpdir(),'cyacc-provision-test-'));
try {
  const mock=path.join(folder,'mock.mjs'),trace=path.join(folder,'trace.json'),githubEnv=path.join(folder,'env');
  const primary='11111111-1111-4111-8111-111111111111',target='22222222-2222-4222-8222-222222222222';
  fs.writeFileSync(mock,`import fs from 'node:fs';import childProcess from 'node:child_process';import {syncBuiltinESMExports} from 'node:module';
const calls=[];process.on('exit',()=>fs.writeFileSync(process.env.TEST_TRACE,JSON.stringify(calls)));
childProcess.execFileSync=(binary,args)=>{const config=JSON.parse(fs.readFileSync(args[args.indexOf('--config')+1],'utf8'));calls.push({migration:config.d1_databases[0]});return Buffer.from('');};syncBuiltinESMExports();
globalThis.fetch=async(url,options={})=>{calls.push({method:options.method,url,body:options.body?JSON.parse(options.body):null});
if(process.env.TEST_DENIED)return Response.json({success:false,errors:[{code:10000}]},{status:403});
if(options.method==='GET')return Response.json({success:true,result:[{uuid:'${primary}',name:'test-primary'},{uuid:'33333333-3333-4333-8333-333333333333',name:'unrelated'}],result_info:{total_pages:1}});
if(!String(url).includes('/query'))return Response.json({success:true,result:{uuid:'${target}',name:'test-worker-recovery'}});
return Response.json({success:true,result:[{results:[{snapshot:'TEST_SEED_SNAPSHOT'}]}]});};`);
  const token=crypto.randomUUID();const env={...process.env,CLOUDFLARE_ACCOUNT_ID:'test-account',CLOUDFLARE_API_TOKEN:token,CF_D1_DATABASE_ID:primary,CF_WORKER_NAME:'test-worker',GITHUB_ENV:githubEnv,TEST_TRACE:trace};
  const stdout=execFileSync(process.execPath,['--import',mock,'scripts/provision-recovery-d1.mjs'],{env,encoding:'utf8'});
  assert.ok(!stdout.includes(token));assert.ok(!stdout.includes('TEST_SEED_SNAPSHOT'));
  const calls=JSON.parse(fs.readFileSync(trace));
  assert.equal(calls.find(call=>call.migration).migration.database_id,target);
  assert.ok(!calls.some(call=>call.url?.includes('/'+primary+'/query')),'never queries or mutates the primary D1');
  assert.ok(!calls.some(call=>call.method==='DELETE'),'no automatic resource deletion');
  assert.match(fs.readFileSync(githubEnv,'utf8'),/CF_RECOVERY_D1_DATABASE_ID=22222222/);
  try {execFileSync(process.execPath,['--import',mock,'scripts/provision-recovery-d1.mjs'],{env:{...env,TEST_DENIED:'1'},stdio:'pipe'});assert.fail('permission denial must fail closed');}
  catch(error){assert.match(String(error.stderr),/RECOVERY_CLOUDFLARE_403_10000/);assert.ok(!String(error.stderr).includes(token));}
  console.log('Recovery provisioning: isolated binding, current D1 preservation, protected env output, no content/credential logs, no deletes and permission failure handling passed.');
} finally {fs.rmSync(folder,{recursive:true,force:true});}
