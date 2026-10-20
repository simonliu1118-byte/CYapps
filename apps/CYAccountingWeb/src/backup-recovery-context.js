// Operational evidence is separate from the portable accounting dataset. The
// source backup engine owns this allow-list; sessions/credentials are excluded.
export const RECOVERY_TABLES = {
  backup_sets: 'backup_id app_id created_at app_version schema_version format format_version data_sha256 manifest_sha256 package_sha256 data_byte_length manifest_byte_length total_byte_length record_count trigger_kind policy_phase gcs_expected'.split(' '),
  backup_copies: 'backup_id provider status started_at completed_at verified_at storage_prefix version_token last_error'.split(' '),
  backup_restore_checks: 'sequence id attempt_id backup_id provider package_sha256 source_schema_version target_schema_version status result_json actor_json created_at'.split(' '),
  backup_policy_events: 'id target_phase r2_check_id gcs_check_id acceptance_json actor_json created_at'.split(' '),
  backup_runs: 'id provider trigger_kind status started_at completed_at file_id file_name data_sha256 file_sha256 row_count byte_size error_message'.split(' ')
};
export async function buildRecoveryContext(db) {
  const tables = {};
  let remaining = 20000;
  for (const [table,columns] of Object.entries(RECOVERY_TABLES)) {
    const order = table==='backup_copies' ? 'backup_id,provider' : table==='backup_sets' ? 'backup_id' : table==='backup_restore_checks' ? 'sequence' : 'id';
    const result=await db.prepare(`SELECT ${columns.join(',')} FROM ${table} ORDER BY ${order} LIMIT ?`).bind(remaining + 1).all();
    if ((result.results || []).length > remaining) throw recoveryError('BACKUP_RECOVERY_ROW_LIMIT');
    remaining -= (result.results || []).length;
    tables[table]=(result.results||[]).map(row=>columns.map(column=>row[column]));
  }
  return { format:'CYAccountingRecoveryContext',formatVersion:1,tables };
}
export function assertRecoveryContext(context) {
  if(context?.format!=='CYAccountingRecoveryContext'||context.formatVersion!==1||!context.tables||Object.keys(context.tables).length!==Object.keys(RECOVERY_TABLES).length) throw recoveryError('RECOVERY_CONTEXT_INVALID');
  for(const [table,columns] of Object.entries(RECOVERY_TABLES)) {
    if(!Array.isArray(context.tables[table])||context.tables[table].some(row=>!Array.isArray(row)||row.length!==columns.length||row.some(value=>value!==null&&!['string','number'].includes(typeof value)))) throw recoveryError('RECOVERY_CONTEXT_INVALID');
  }
}
export async function recoveryDigest(value) {
  const bytes=typeof value==='string'?new TextEncoder().encode(value):new TextEncoder().encode(JSON.stringify(value));
  return Array.from(new Uint8Array(await crypto.subtle.digest('SHA-256',bytes)),byte=>byte.toString(16).padStart(2,'0')).join('');
}
export function recoveryError(code) { const error=new Error('還原未完成，請查看狀態代碼；目前正式帳本未更動。');error.code=code;return error; }
