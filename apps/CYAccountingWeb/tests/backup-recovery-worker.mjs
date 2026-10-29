import assert from 'node:assert/strict';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { execFileSync } from 'node:child_process';
import { unstable_dev } from 'wrangler';
import { accountingFixture } from './support/backup-fixtures.mjs';
import { buildBackupSet, validateBackupSetBytes } from '../src/gcs-backup.js';
import { backupPackageDigest } from '../src/backup-service.js';
const folder=fs.mkdtempSync(path.join(os.tmpdir(),'cyacc-dr-worker-'));
let worker;
try {
  const config=path.join(folder,'wrangler.json');const state=path.join(folder,'state');
  fs.writeFileSync(config,JSON.stringify({name:'cyacc-recovery-local-test',main:path.resolve('tests/fixtures/backup-recovery-worker.js'),compatibility_date:'2026-09-22',define:{'globalThis.WorkerGlobalScope':'undefined'},rules:[{type:'Text',globs:['**/*.sql'],fallthrough:true}],d1_databases:[{binding:'RECOVERY_DB',database_name:'test-only-recovery',database_id:'22222222-2222-4222-8222-222222222222',migrations_dir:path.resolve('migrations')}]}));
  execFileSync(process.execPath,['node_modules/wrangler/bin/wrangler.js','d1','migrations','apply','RECOVERY_DB','--local','--persist-to',state,'--config',config],{stdio:'pipe',timeout:60000});
  const source=accountingFixture();const set=await buildBackupSet(source);source.close();
  const backup=await validateBackupSetBytes(set.manifestBytes,set.dataBytes,set.backupId);
  worker=await unstable_dev('tests/fixtures/backup-recovery-worker.js',{config,local:true,persist:true,persistTo:state,port:0,ip:'127.0.0.1',logLevel:'error',experimental:{disableExperimentalWarning:true,disableDevRegistry:true,watch:false}});
  const response=await worker.fetch('/',{method:'POST',headers:{'content-type':'application/json'},body:JSON.stringify({backup,packageSha256:await backupPackageDigest(set)})});
  const report=await response.json();assert.equal(response.status,200,JSON.stringify(report));
  assert.equal(report.fullRowRoundtrip,true);assert.equal(report.operationalContext,true);assert.equal(report.liveAccountingModified,false);assert.equal(report.counts.transactions,3);assert.equal(report.readService.accounts,2);assert.equal(report.readService.transactions,3);
  assert.equal(report.readService.openingCached,true,'restored D1 rebuilds and shares its disposable monthly snapshot in workerd');
  console.log('Actual workerd + fresh D1 binding: canonical migrations, real WASM validation, bound JSON batch restore, complete row/policy/audit roundtrip and integrity checks passed.');
} finally {if(worker)await worker.stop();fs.rmSync(folder,{recursive:true,force:true});}
