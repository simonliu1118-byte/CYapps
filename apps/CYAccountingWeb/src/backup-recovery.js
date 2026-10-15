import { ACCOUNTING_RESTORE_TABLES, stableJson } from './backup-restore.js';
import { buildBackupPackage } from './backup-package.js';
import { RECOVERY_TABLES, assertRecoveryContext, buildRecoveryContext, recoveryDigest, recoveryError } from './backup-recovery-context.js';
import metadata from './build-metadata.json' with {type:'json'};

export const RECOVERY_SEED_SNAPSHOT_SQL = 'SELECT json_array(' + ACCOUNTING_RESTORE_TABLES.map(([,table,columns]) => `(SELECT json_group_array(json_array(${columns.join(',')})) FROM (SELECT ${columns.join(',')} FROM ${table} ORDER BY ${columns.join(',')}))`).join(',') + ') AS snapshot';
export const RECOVERY_CONFIRMATION='重建獨立帳本';
export async function recoveryWorkspace(db) {
  if(!db) return {configured:false,ready:false};
  const row=await db.prepare(`SELECT
    (SELECT value FROM meta WHERE key='recovery_workspace_id') AS workspace_id,
    (SELECT value FROM meta WHERE key='schema_version') AS schema_version,
    (SELECT COUNT(*) FROM backup_recovery_runs) AS runs,
    (SELECT COUNT(*) FROM transactions)+(SELECT COUNT(*) FROM opening_balance_overrides)+(SELECT COUNT(*) FROM opening_balance_audit)+(SELECT COUNT(*) FROM excel_import_runs)+(SELECT COUNT(*) FROM app_settings) AS financial_rows,
    (SELECT COUNT(*) FROM accounts) AS accounts,(SELECT COUNT(*) FROM category_groups) AS groups_count,(SELECT COUNT(*) FROM categories) AS categories,
    ((${RECOVERY_SEED_SNAPSHOT_SQL})=(SELECT value FROM meta WHERE key='recovery_seed_snapshot')) AS seed_matches`).first();
  const ready=Boolean(row?.workspace_id)&&row.runs===0&&row.financial_rows===0&&row.accounts===1&&row.groups_count===2&&row.categories===2&&row.seed_matches===1;
  return {configured:true,ready,workspaceId:String(row?.workspace_id||''),schemaVersion:Number(row?.schema_version||0)};
}

// Only a provisioned, unclaimed, seed-only recovery D1 can be written. Production
// binding changes are a separate deployment action, never an HTTP side effect.
export async function restoreIntoRecoveryD1(target, backup, context, preview, actor) {
  assertRecoveryContext(context);
  const workspace=await recoveryWorkspace(target);
  if(!workspace.ready||workspace.workspaceId!==preview.workspaceId||workspace.schemaVersion!==metadata.schemaVersion) throw recoveryError('RECOVERY_TARGET_NOT_EMPTY');
  const runId=crypto.randomUUID(), now=new Date().toISOString();
  const statements=[target.prepare(`INSERT INTO backup_recovery_runs(id,preview_id,backup_id,provider,package_sha256,reason,actor_json,created_at)
    SELECT ?,?,?,?,?,?,?,? WHERE NOT EXISTS(SELECT 1 FROM backup_recovery_runs)
    AND (SELECT value FROM meta WHERE key='recovery_workspace_id')=?
    AND NOT EXISTS(SELECT 1 FROM transactions) AND NOT EXISTS(SELECT 1 FROM opening_balance_audit)
    AND NOT EXISTS(SELECT 1 FROM excel_import_runs) AND NOT EXISTS(SELECT 1 FROM opening_balance_overrides)
    AND NOT EXISTS(SELECT 1 FROM app_settings)
    AND (${RECOVERY_SEED_SNAPSHOT_SQL})=(SELECT value FROM meta WHERE key='recovery_seed_snapshot')
    AND (SELECT COUNT(*) FROM accounts)=1 AND (SELECT COUNT(*) FROM categories)=2 AND (SELECT COUNT(*) FROM category_groups)=2`)
    .bind(runId,preview.id,preview.backupId,preview.provider,preview.packageSha256,preview.reason,JSON.stringify(actor),now,workspace.workspaceId)];
  const guard='EXISTS(SELECT 1 FROM backup_recovery_runs WHERE id=?)';
  for(const table of ['categories','category_groups','accounts']) statements.push(target.prepare(`DELETE FROM ${table} WHERE ${guard}`).bind(runId));
  for(const [key,table,columns,fields] of ACCOUNTING_RESTORE_TABLES) {
    const rows=backup.data[key].map(row=>fields.map(field=>field==='actor'?JSON.stringify(row[field]):row[field]));
    appendRows(statements,target,table,columns,rows,guard,runId);
  }
  for(const [table,columns] of Object.entries(RECOVERY_TABLES)) appendRows(statements,target,table,columns,context.tables[table],guard,runId);
  statements.push(target.prepare(`INSERT INTO meta(key,value) SELECT 'accounting_epoch',? WHERE ${guard}`).bind(crypto.randomUUID(),runId));
  statements.push(target.prepare(`INSERT INTO meta(key,value) SELECT 'recovery_hold','1' WHERE ${guard}`).bind(runId));
  if(statements.length>40) throw recoveryError('RECOVERY_BATCH_SIZE_LIMIT');
  try { await target.batch(statements); } catch { throw recoveryError('RECOVERY_BATCH_ROLLED_BACK'); }
  const claimed=await target.prepare('SELECT id FROM backup_recovery_runs WHERE id=?').bind(runId).first();
  if(!claimed) throw recoveryError('RECOVERY_TARGET_CHANGED');
  try {
    const restored=await buildBackupPackage(target);
    if(stableJson(restored.data)!==stableJson(backup.data)) throw recoveryError('RECOVERY_ROUNDTRIP_MISMATCH');
    const restoredContext=await buildRecoveryContext(target);
    if(stableJson(restoredContext)!==stableJson(context)) throw recoveryError('RECOVERY_POLICY_MISMATCH');
    const fk=await target.prepare('PRAGMA foreign_key_check').all();
    const integrity=await target.prepare('PRAGMA quick_check').all();
    if(fk.results?.length||integrity.results?.length!==1||Object.values(integrity.results[0])[0]!=='ok') throw recoveryError('RECOVERY_INTEGRITY_FAILED');
    const evidence={runId,liveAccountingModified:false,isolated:true,target:'recovery_d1',fullRowRoundtrip:true,integrityCheck:true,databaseCheck:'quick_check',foreignKeyCheck:true,operationalContext:true,counts:restored.manifest.counts,restoredDataSha256:await recoveryDigest(stableJson(restored.data)),targetSchemaVersion:metadata.schemaVersion,promoted:false};
    await recoveryEvent(target,runId,'verified',evidence,actor);
    return evidence;
  } catch(error) {
    await recoveryEvent(target,runId,'failed',{code:error.code||'RECOVERY_CHECK_FAILED',promoted:false},actor);
    const failure=recoveryError(error.code||'RECOVERY_CHECK_FAILED');failure.cause=error;throw failure;
  }
}
function appendRows(statements,db,table,columns,rows,guard,runId) {
  for(let start=0;start<rows.length;start+=400) statements.push(db.prepare(`INSERT INTO ${table}(${columns.join(',')}) SELECT ${columns.map((_,i)=>`json_extract(value,'$[${i}]')`).join(',')} FROM json_each(?) WHERE ${guard}`).bind(JSON.stringify(rows.slice(start,start+400)),runId));
}
export async function recoveryEvent(db,runId,status,result,actor) {
  await db.prepare('INSERT INTO backup_recovery_events(id,run_id,status,result_json,actor_json,created_at) VALUES(?,?,?,?,?,?)').bind(crypto.randomUUID(),runId,status,JSON.stringify(result),JSON.stringify(actor),new Date().toISOString()).run();
}
