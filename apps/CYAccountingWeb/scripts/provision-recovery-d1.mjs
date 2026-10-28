import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { execFileSync } from 'node:child_process';
import { RECOVERY_SEED_SNAPSHOT_SQL, recoveryWorkspace } from '../src/backup-recovery.js';

// Runs only in the protected deployment job. No database content, identifiers,
// credentials, response bodies or generated configuration are logged/uploaded.
const env=process.env;
try {
  const mode=env.RECOVERY_D1_MODE||'disabled';
  if(!['disabled','prepare'].includes(mode)) throw new Error('RECOVERY_MODE_INVALID');
  if(!env.GITHUB_ENV) throw new Error('RECOVERY_PROVISION_ENV_REQUIRED');
  // Normal publishing neither discovers, creates nor migrates a recovery D1.
  // Clearing the generated binding does not delete any existing database.
  fs.appendFileSync(env.GITHUB_ENV,'CF_RECOVERY_D1_DATABASE_ID=\nCF_RECOVERY_D1_DATABASE_NAME=\n');
  if(mode==='disabled') {
    console.log('Recovery workspace disabled; no recovery resources accessed.');
  } else {
    if(env.GITHUB_EVENT_NAME!=='workflow_dispatch'||env.GITHUB_REF!=='refs/heads/main') throw new Error('RECOVERY_PREPARE_REQUIRES_MAIN_DISPATCH');
    const account=env.CLOUDFLARE_ACCOUNT_ID,token=env.CLOUDFLARE_API_TOKEN;
    const primary=env.CF_D1_DATABASE_ID,worker=env.CF_WORKER_NAME;
    if(!account||!token||!primary||!worker) throw new Error('RECOVERY_PROVISION_ENV_REQUIRED');
    const api='https://api.cloudflare.com/client/v4/accounts/'+encodeURIComponent(account)+'/d1/database';
    async function call(suffix='',method='GET',body) {
      const response=await fetch(api+suffix,{method,signal:AbortSignal.timeout(15000),headers:{authorization:'Bearer '+token,'content-type':'application/json'},...(body?{body:JSON.stringify(body)}:{})});
      const data=await response.json().catch(()=>({}));
      if(!response.ok||data.success===false) throw new Error('RECOVERY_CLOUDFLARE_'+response.status+'_'+String(data.errors?.[0]?.code||'REQUEST'));
      return data;
    }
    const databases=[];
    for(let page=1;page<=2;page++) {
      const data=await call('?per_page=100&page='+page);
      databases.push(...(data.result||[]));
      if(data.result_info?.total_pages ? page>=data.result_info.total_pages : (data.result||[]).length<100) break;
      if(page===2) throw new Error('RECOVERY_INVENTORY_LIMIT');
    }
    const prefix=worker+'-recovery';
    const candidates=databases.filter(row=>row.name===prefix||row.name.startsWith(prefix+'-'));
    if(candidates.some(row=>row.uuid===primary)) throw new Error('RECOVERY_TARGET_EQUALS_PRIMARY');
    if(candidates.length>1) throw new Error('RECOVERY_TARGET_AMBIGUOUS');
    let target=candidates[0];
    const reused=Boolean(target);
    if(!target) {
      target=(await call('','POST',{name:prefix})).result;
    }
    if(!target?.uuid||target.uuid===primary) throw new Error('RECOVERY_TARGET_EQUALS_PRIMARY');
    console.log('::add-mask::'+target.uuid);console.log('::add-mask::'+target.name);
    const query=async(sql,params=[])=>call('/'+encodeURIComponent(target.uuid)+'/query','POST',{sql,params});
    const targetDb={prepare:sql=>({first:async()=>(await query(sql)).result?.[0]?.results?.[0]||null})};
    if(reused) {
      // Reject a claimed/non-seed workspace before migrations or seed writes.
      // LIMIT/OFFSET checks read at most the seed rows plus one per table.
      const occupied=(await query(`SELECT EXISTS(SELECT 1 FROM backup_recovery_runs LIMIT 1)
        OR EXISTS(SELECT 1 FROM transactions LIMIT 1)
        OR EXISTS(SELECT 1 FROM opening_balance_overrides LIMIT 1)
        OR EXISTS(SELECT 1 FROM opening_balance_audit LIMIT 1)
        OR EXISTS(SELECT 1 FROM excel_import_runs LIMIT 1)
        OR EXISTS(SELECT 1 FROM app_settings LIMIT 1)
        OR EXISTS(SELECT 1 FROM accounts LIMIT 1 OFFSET 1)
        OR EXISTS(SELECT 1 FROM category_groups LIMIT 1 OFFSET 2)
        OR EXISTS(SELECT 1 FROM categories LIMIT 1 OFFSET 2) AS occupied`)).result?.[0]?.results?.[0];
      if(!occupied||occupied.occupied!==0||!(await recoveryWorkspace(targetDb)).ready) throw new Error('RECOVERY_TARGET_NOT_EMPTY');
    }
    const folder=fs.mkdtempSync(path.join(os.tmpdir(),'cyacc-recovery-'));
    try {
      const config=path.join(folder,'wrangler.json');
      fs.writeFileSync(config,JSON.stringify({name:'cyacc-recovery-provision',compatibility_date:'2026-09-22',d1_databases:[{binding:'RECOVERY_DB',database_id:target.uuid,database_name:target.name,migrations_dir:path.resolve('migrations')}]}),{mode:0o600});
      try { execFileSync(process.execPath,['node_modules/wrangler/bin/wrangler.js','d1','migrations','apply','RECOVERY_DB','--remote','--config',config],{stdio:'pipe',env,timeout:180000}); }
      catch { throw new Error('RECOVERY_MIGRATION_FAILED'); }
      await query("INSERT OR IGNORE INTO meta(key,value) VALUES('recovery_workspace_id',?)",[crypto.randomUUID()]);
      const snapshot=(await query(RECOVERY_SEED_SNAPSHOT_SQL)).result?.[0]?.results?.[0]?.snapshot;
      if(typeof snapshot!=='string') throw new Error('RECOVERY_SEED_SNAPSHOT_FAILED');
      await query("INSERT OR IGNORE INTO meta(key,value) VALUES('recovery_seed_snapshot',?)",[snapshot]);
      if(!(await recoveryWorkspace(targetDb)).ready) throw new Error('RECOVERY_TARGET_NOT_READY');
      fs.appendFileSync(env.GITHUB_ENV,'CF_RECOVERY_D1_DATABASE_ID='+target.uuid+'\nCF_RECOVERY_D1_DATABASE_NAME='+target.name+'\n');
      console.log('Isolated recovery D1 binding prepared; primary D1 unchanged.');
    } finally { fs.rmSync(folder,{recursive:true,force:true}); }
  }
} catch(error) { console.error(error.code||(/^RECOVERY_[A-Z0-9_]+$/.test(error.message)?error.message:'RECOVERY_PROVISION_FAILED'));process.exitCode=1; }
