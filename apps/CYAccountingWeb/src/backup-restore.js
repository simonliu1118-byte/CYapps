import { buildBackupPackage } from './backup-package.js';
import { assertRecoveryContext, RECOVERY_TABLES } from './backup-recovery-context.js';
import buildMetadata from './build-metadata.json' with { type: 'json' };

// Only a newly created, empty SQLite database is accepted. This capability has
// no production D1 writer or provider mutation and returns evidence, never rows.
export const ACCOUNTING_RESTORE_TABLES = [
  ['accounts', 'accounts', ['id','name','sort_order','is_default','color_slot','created_at','archived_at'], ['id','name','sortOrder','isDefault','colorSlot','createdAt','archivedAt']],
  ['categoryGroups', 'category_groups', ['id','kind','name','sort_order','created_at'], ['id','kind','name','sortOrder','createdAt']],
  ['categories', 'categories', ['id','kind','group_id','name','sort_order','is_favorite','created_at'], ['id','kind','groupId','name','sortOrder','isFavorite','createdAt']],
  ['transactions', 'transactions', ['id','tx_date','account_name','kind','category_name','summary','amount','marker_color','marker_target','created_at','updated_at'], ['id','txDate','accountName','kind','categoryName','summary','amount','markerColor','markerTarget','createdAt','updatedAt']],
  ['openingBalanceOverrides', 'opening_balance_overrides', ['month','account_name','amount','reason','created_at','updated_at','updated_by_employee_id','updated_by_employee_no','updated_by_name','updated_by_role'], ['month','accountName','amount','reason','createdAt','updatedAt','updatedByEmployeeId','updatedByEmployeeNo','updatedByName','updatedByRole']],
  ['openingBalanceAudit', 'opening_balance_audit', ['id','month','account_name','action','previous_amount','new_amount','reason','actor_employee_id','actor_employee_no','actor_name','actor_role','created_at'], ['id','month','accountName','action','previousAmount','newAmount','reason','actorEmployeeId','actorEmployeeNo','actorName','actorRole','createdAt']],
  ['appSettings', 'app_settings', ['key','value'], ['key','value']],
  ['excelImportRuns', 'excel_import_runs', ['id','start_date','end_exclusive','reason','source_sha256','backup_id','deleted_count','inserted_count','actor_json','created_at'], ['id','startDate','endExclusive','reason','sourceSha256','backupId','deletedCount','insertedCount','actor','createdAt']]
];

// Portable schema compatibility is applied only after the immutable source bytes
// have passed the existing manifest/digest validator. Historical objects are not rewritten.
export function normalizeAccountingRestoreData(backup) {
  const schema = Number(backup.manifest?.schemaVersion);
  if (![6, 7, 8, 9, 10, 11, 12, 13, 14, 15].includes(schema)) throw restoreError('RESTORE_SCHEMA_UNSUPPORTED');
  const original = backup.data;
  if (!original || Object.keys(original).some(key => !ACCOUNTING_RESTORE_TABLES.some(([name]) => name === key))) throw restoreError('RESTORE_DATA_SHAPE');
  const data = { ...original };
  if (schema < 8 && !('excelImportRuns' in data)) data.excelImportRuns = [];
  // Historical schema 6–13 packages contain no marker column. Normalize only
  // after validation of the original package digest, without mutating source data.
  if (schema < 14 && Array.isArray(data.transactions)) {
    data.transactions = data.transactions.map(row => row && ({ ...row, markerColor: null }));
  }
  // Schema 6–14 never recorded placement: all existing highlights were summaries.
  if (schema < 15 && Array.isArray(data.transactions)) {
    data.transactions = data.transactions.map(row => row && ({ ...row, markerTarget: 'summary' }));
  }
  for (const [key] of ACCOUNTING_RESTORE_TABLES) if (!Array.isArray(data[key])) throw restoreError('RESTORE_DATA_SHAPE');
  if (schema < 8 && data.excelImportRuns.length) throw restoreError('RESTORE_LEGACY_IMPORT_AUDIT_CONFLICT');
  if (schema === 6) {
    // Canonical migration 0007 assigns ROW_NUMBER() OVER(ORDER BY created_at,id).
    // Reproduce that contract explicitly instead of relying on insertion order.
    if (data.accounts.some(row => !row || !Number.isSafeInteger(row.id) || typeof row.createdAt !== 'string' || row.colorSlot != null)) throw restoreError('RESTORE_LEGACY_ACCOUNT_SHAPE');
    const ranked = [...data.accounts].sort((a, b) => a.createdAt < b.createdAt ? -1 : a.createdAt > b.createdAt ? 1 : a.id - b.id);
    const slots = new Map(ranked.map((row, index) => [row.id, index + 1]));
    data.accounts = data.accounts.map(row => ({ ...row, colorSlot: slots.get(row.id) }));
  }
  return { data, normalization: schema < 8 ? {
    sourceSchemaVersion: schema,
    accountColorSlotsAssigned: schema === 6,
    emptyImportAuditAdded: !('excelImportRuns' in original)
  } : null };
}

export async function verifyRestoreIntoEmptySqlite(db, schemas, backup) {
  const existing = db.exec("SELECT name FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite_%'");
  if (existing[0]?.values?.length) throw restoreError('SANDBOX_NOT_EMPTY');
  const schema = Number(backup.manifest?.schemaVersion);
  const { data, normalization } = normalizeAccountingRestoreData(backup);

  for (const sql of schemas) db.exec(sql);
  const actualSchema = db.exec("SELECT value FROM meta WHERE key='schema_version'")[0]?.values[0]?.[0];
  if(Number(actualSchema)!==buildMetadata.schemaVersion) throw restoreError('RESTORE_TARGET_SCHEMA_MISMATCH');
  // Initial migration seeds are empty-sandbox defaults, not restored data.
  for (const table of ['categories', 'category_groups', 'accounts']) db.exec(`DELETE FROM ${table}`);
  db.exec('BEGIN TRANSACTION');
  try {
    for (const [key, table, columns, fields] of ACCOUNTING_RESTORE_TABLES) {
      const statement = db.prepare(`INSERT INTO ${table}(${columns.join(',')}) VALUES (${columns.map(() => '?').join(',')})`);
      try {
        for (const row of data[key]) {
          if (!row || fields.some(field => !(field in row))) throw restoreError('RESTORE_ROW_SHAPE');
          const values = fields.map(field => field === 'actor' ? JSON.stringify(row[field]) : row[field]);
          if (values.some(value => value !== null && !['string','number'].includes(typeof value))) throw restoreError('RESTORE_ROW_SHAPE');
          statement.run(values);
        }
      } finally { statement.free(); }
    }
    if (backup.manifest.recoveryContext) {
      assertRecoveryContext(backup.manifest.recoveryContext);
      for(const [table,columns] of Object.entries(RECOVERY_TABLES)) {
        const statement=db.prepare(`INSERT INTO ${table}(${columns.join(',')}) VALUES(${columns.map(()=>'?').join(',')})`);
        try { for(const row of backup.manifest.recoveryContext.tables[table]) statement.run(row); } finally { statement.free(); }
      }
    }
    db.exec('COMMIT');
  } catch (error) { db.exec('ROLLBACK'); throw error?.code ? error : restoreError('RESTORE_CONSTRAINT_FAILED'); }
  const integrity = db.exec('PRAGMA integrity_check')?.[0]?.values;
  const foreignKeys = db.exec('PRAGMA foreign_key_check')?.[0]?.values || [];
  if (integrity?.length !== 1 || integrity[0][0] !== 'ok' || foreignKeys.length) throw restoreError('RESTORE_INTEGRITY_FAILED');
  const restored = await buildBackupPackage(sqliteReadAdapter(db), new Date(backup.manifest.createdAt));
  const source = stableJson(data), result = stableJson(restored.data);
  if (source !== result) throw restoreError('RESTORE_ROUNDTRIP_MISMATCH');
  return {
    engine: 'SQLite sql.js 1.14.2', isolated: true, liveAccountingModified: false,
    sourceSchemaVersion: schema, normalization, targetSchemaVersion: buildMetadata.schemaVersion,
    counts: restored.manifest.counts, rowCount: restored.totalRowCount,
    integrityCheck: true, foreignKeyCheck: true, fullRowRoundtrip: true,
    restoredDataSha256: restored.dataSha256
  };
}

export function stableJson(value) {
  if (Array.isArray(value)) return '[' + value.map(stableJson).join(',') + ']';
  if (value && typeof value === 'object') return '{' + Object.keys(value).sort().map(key => JSON.stringify(key) + ':' + stableJson(value[key])).join(',') + '}';
  return JSON.stringify(value);
}
function sqliteReadAdapter(db) {
  return { prepare(sql) {
    let args = [];
    const rows = () => {
      const statement = db.prepare(sql);
      try { statement.bind(args); const result = []; while (statement.step()) result.push(statement.getAsObject()); return result; }
      finally { statement.free(); }
    };
    return { bind(...values) { args = values; return this; }, async all() { return { results: rows() }; }, async first() { return rows()[0] || null; } };
  } };
}
function restoreError(code) { const error = new Error('隔離還原驗證失敗，請查看驗證代碼。'); error.code = code; return error; }
