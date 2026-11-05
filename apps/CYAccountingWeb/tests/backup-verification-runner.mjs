import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { readFileSync } from 'node:fs';

// Execute the actual production runner against in-memory synthetic HTTP sources,
// including a corrupt source containing a distinctive canary. Public output must
// remain fixed text on success, source failure and an unexpected request failure.
for (const mode of ['success','corrupt','request-failed']) {
  const bootstrap = `
    import {accountingFixture} from './tests/support/backup-fixtures.mjs';
    import {buildBackupSet} from './src/gcs-backup.js';
    import {backupPackageDigest} from './src/backup-package.js';
    const db=accountingFixture();
    db.db.prepare('UPDATE transactions SET summary=? WHERE id=1').run('PRIVATE_SYNTHETIC_CANARY_DO_NOT_LOG');
    const backup=await buildBackupSet(db);db.close();
    const source={backupId:backup.backupId,packageSha256:await backupPackageDigest(backup),dataSha256:backup.dataSha256,
      manifestSha256:backup.manifestSha256};
    process.env.CF_CYACCOUNTINGWEB_CUSTOM_DOMAIN='backup.example.test';
    process.env.ACTIONS_ID_TOKEN_REQUEST_URL='https://oidc.example.test/token';
    process.env.ACTIONS_ID_TOKEN_REQUEST_TOKEN='SYNTHETIC_SECRET_DO_NOT_LOG';
    globalThis.fetch=async (url,init)=>{
      const path=new URL(url).pathname;
      if(path==='/token')return Response.json({value:'SYNTHETIC_TOKEN_DO_NOT_LOG'});
      if(${JSON.stringify(mode)}==='request-failed')throw new Error('PRIVATE_SYNTHETIC_CANARY_DO_NOT_LOG');
      if(path.endsWith('/start'))return Response.json({jobId:'SYNTHETIC_JOB_DO_NOT_LOG',sources:[
        {...source,provider:'cloudflare_r2'},{...source,provider:'google_cloud_storage'}]});
      if(path.endsWith('/source'))return new Response(new URL(url).searchParams.get('part')==='manifest'
        ? backup.manifestBytes : ${JSON.stringify(mode)}==='corrupt' ? 'PRIVATE_SYNTHETIC_CANARY_DO_NOT_LOG' : backup.dataBytes);
      if(path.endsWith('/complete')){
        const {results}=JSON.parse(init.body);
        if(JSON.stringify(results).includes('CANARY'))throw new Error('Evidence must not contain rows');
        return Response.json({status:results.every(result=>result.status==='success')?'success':'failed'});
      }
      throw new Error('unexpected');
    };
    await import('./scripts/verify-backups-external.mjs');
  `;
  const child=spawnSync(process.execPath,['--no-warnings','--input-type=module','-e',bootstrap],
    {cwd:new URL('../',import.meta.url),encoding:'utf8',timeout:30000});
  assert.equal(child.error,undefined);
  assert.equal(child.status,mode==='success'?0:1);
  assert.equal(child.stderr,'');
  assert.match(child.stdout,/^Backup verification (passed\.|incomplete; fallback will continue\.)\n$/);
  assert.doesNotMatch(child.stdout,/CANARY|SYNTHETIC|TEST_|[a-f0-9]{64}/);
}
const workflow=readFileSync(new URL('../../../.github/workflows/cyaccountingweb-backup-verify.yml',import.meta.url),'utf8');
assert.doesNotMatch(workflow,/upload-artifact|cache:\s*npm|secrets\.|pull_request:/);
assert.match(workflow,/persist-credentials: false/);
assert.match(workflow,/if: github.ref == 'refs\/heads\/main'/);
console.log('External runner public output: success/corruption/request failure leak no synthetic accounting data, tokens or identifiers; no backup artifacts/cache configured.');
