import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { execFileSync } from 'node:child_process';
import { RECOVERY_SEED_SNAPSHOT_SQL } from '../src/backup-recovery.js';

// Runs only in the protected deployment job. No database content, identifiers,
// credentials, response bodies or generated configuration are logged/uploaded.
const env=process.env;
const account=env.CLOUDFLARE_ACCOUNT_ID,token=env.CLOUDFLARE_API_TOKEN;
const primary=env.CF_D1_DATABASE_ID,worker=env.CF_WORKER_NAME;
if(!account||!token||!primary||!worker||!env.GITHUB_ENV) throw new Error('RECOVERY_PROVISION_ENV_REQUIRED');
const api='https://api.cloudflare.com/client/v4/accounts/'+encodeURIComponent(account)+'/d1/database';
async function call(suffix='',method='GET',body) {
  const response=await fetch(api+suffix,{method,headers:{authorization:'Bearer '+token,'content-type':'application/json'},...(body?{body:JSON.stringify(body)}:{})});
  const data=await response.json().catch(()=>({}));
  if(!response.ok||data.success===false) throw new Error('RECOVERY_CLOUDFLARE_'+response.status+'_'+String(data.errors?.[0]?.code||'REQUEST'));
  return data;
}
try {
  const databases=[];
  for(let page=1;page<=100;page++) {
    const data=await call('?per_page=100&page='+page);
    databases.push(...(data.result||[]));
    if(!data.result_info?.total_pages||page>=data.result_info.total_pages) break;
    if(page===100) throw new Error('RECOVERY_INVENTORY_LIMIT');
  }
  const prefix=worker+'-recovery';
  let target=databases.filter(row=>row.uuid!==primary&&(row.name===prefix||row.name.startsWith(prefix+'-'))).sort((a,b)=>String(b.created_at||'').localeCompare(String(a.created_at||'')))[0];
  if(!target) {
    const name=databases.some(row=>row.name===prefix)?prefix+'-'+crypto.randomUUID().slice(0,8):prefix;
    target=(await call('','POST',{name})).result;
  }
  if(!target?.uuid||target.uuid===primary) throw new Error('RECOVERY_TARGET_EQUALS_PRIMARY');
  console.log('::add-mask::'+target.uuid);console.log('::add-mask::'+target.name);
  const folder=fs.mkdtempSync(path.join(os.tmpdir(),'cyacc-recovery-'));
  try {
    const config=path.join(folder,'wrangler.json');
    fs.writeFileSync(config,JSON.stringify({name:'cyacc-recovery-provision',compatibility_date:'2026-09-22',d1_databases:[{binding:'RECOVERY_DB',database_id:target.uuid,database_name:target.name,migrations_dir:path.resolve('migrations')}]}),{mode:0o600});
    try { execFileSync(process.execPath,['node_modules/wrangler/bin/wrangler.js','d1','migrations','apply','RECOVERY_DB','--remote','--config',config],{stdio:'pipe',env,timeout:180000}); }
    catch { throw new Error('RECOVERY_MIGRATION_FAILED'); }
    const query=async(sql,params=[])=>call('/'+encodeURIComponent(target.uuid)+'/query','POST',{sql,params});
    await query("INSERT OR IGNORE INTO meta(key,value) VALUES('recovery_workspace_id',?)",[crypto.randomUUID()]);
    const snapshot=(await query(RECOVERY_SEED_SNAPSHOT_SQL)).result?.[0]?.results?.[0]?.snapshot;
    if(typeof snapshot!=='string') throw new Error('RECOVERY_SEED_SNAPSHOT_FAILED');
    await query("INSERT OR IGNORE INTO meta(key,value) VALUES('recovery_seed_snapshot',?)",[snapshot]);
    fs.appendFileSync(env.GITHUB_ENV,'CF_RECOVERY_D1_DATABASE_ID='+target.uuid+'\nCF_RECOVERY_D1_DATABASE_NAME='+target.name+'\n');
    console.log('Isolated recovery D1 binding prepared; primary D1 unchanged.');
  } finally { fs.rmSync(folder,{recursive:true,force:true}); }
} catch(error) { console.error(error.code||(/^RECOVERY_[A-Z0-9_]+$/.test(error.message)?error.message:'RECOVERY_PROVISION_FAILED'));process.exitCode=1; }
