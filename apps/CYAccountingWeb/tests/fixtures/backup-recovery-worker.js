import coreWorker from '../../src/index.js';
import { verifyRestoreInMemory } from '../../src/backup-restore-runtime.js';
import { restoreIntoRecoveryD1, RECOVERY_SEED_SNAPSHOT_SQL } from '../../src/backup-recovery.js';
export default {
  async fetch(request,env) {
    try {
      const body=await request.json();
      const workspaceId=crypto.randomUUID();
      await env.RECOVERY_DB.prepare("INSERT INTO meta(key,value) VALUES('recovery_workspace_id',?)").bind(workspaceId).run();
      const seed=await env.RECOVERY_DB.prepare(RECOVERY_SEED_SNAPSHOT_SQL).first();
      await env.RECOVERY_DB.prepare("INSERT INTO meta(key,value) VALUES('recovery_seed_snapshot',?)").bind(seed.snapshot).run();
      await verifyRestoreInMemory(body.backup);
      const result=await restoreIntoRecoveryD1(env.RECOVERY_DB,body.backup,body.backup.manifest.recoveryContext,{id:crypto.randomUUID(),backupId:body.backup.manifest.backupId,provider:'google_cloud_storage',packageSha256:body.packageSha256,workspaceId,reason:'TEST_LOCAL_WORKER_DR'}, {employeeNo:'TEST',role:'SUPER_ADMIN'});
      const bootstrap=await (await coreWorker.fetch(new Request('https://test.invalid/api/bootstrap'),{DB:env.RECOVERY_DB})).json();
      const ledger=await (await coreWorker.fetch(new Request('https://test.invalid/api/transactions?month=2026-01'),{DB:env.RECOVERY_DB})).json();
      return Response.json({...result,readService:{accounts:bootstrap.accounts.length+bootstrap.archivedAccounts.length,transactions:ledger.transactions.length}});
    } catch(error) { return Response.json({code:error.code||'TEST_WORKER_RECOVERY_FAILED',message:error.message,cause:error.cause?.message},{status:500}); }
  }
};
