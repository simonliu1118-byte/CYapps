import { buildBackupPackage, ACCOUNTING_EXPORT_TABLES } from './backup-package.js';
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

// The same insert/read-back contract is used in a single external job and in
// restartable Worker batches. Only an isolated SQLite database is accepted.
export function initializeRestoreSqlite(db, schemas) {
  if (db.exec("SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%'")[0]?.values?.length) throw restoreError('SANDBOX_NOT_EMPTY');
  for (const sql of schemas) db.exec(sql);
  const actual = db.exec("SELECT value FROM meta WHERE key='schema_version'")[0]?.values[0]?.[0];
  if (Number(actual) !== buildMetadata.schemaVersion) throw restoreError('RESTORE_TARGET_SCHEMA_MISMATCH');
  for (const table of ['categories','category_groups','accounts']) db.exec('DELETE FROM ' + table);
}

export function restoreAccountingBatch(db, data, tableIndex, offset, limit = 500) {
  const [key, table, columns, fields] = ACCOUNTING_RESTORE_TABLES[tableIndex];
  const rows = data[key].slice(offset, offset + limit);
  const values = rows.map(row => {
    if (!row || fields.some(field => !(field in row))) throw restoreError('RESTORE_ROW_SHAPE');
    const values = fields.map(field => field === 'actor' ? JSON.stringify(row[field]) : row[field]);
    if (values.some(value => value !== null && !['string','number'].includes(typeof value))) throw restoreError('RESTORE_ROW_SHAPE');
    return values;
  });
  insertRows(db, table, columns, values);
  const [, sql, mapper] = ACCOUNTING_EXPORT_TABLES.find(([name]) => name === key);
  const result = sqliteReadAdapter(db).prepare(sql + ' LIMIT ? OFFSET ?').bind(rows.length, offset).rows().map(mapper);
  if (stableJson(rows) !== stableJson(result)) throw restoreError('RESTORE_ROUNDTRIP_MISMATCH');
  return rows.length;
}

export function restoreContextBatch(db, context, tableIndex, offset, limit = 500) {
  assertRecoveryContext(context);
  const [table, columns] = Object.entries(RECOVERY_TABLES)[tableIndex];
  const rows = context.tables[table].slice(offset, offset + limit);
  insertRows(db, table, columns, rows);
  return rows.length;
}

function insertRows(db, table, columns, rows) {
  db.exec('PRAGMA foreign_keys=ON; BEGIN TRANSACTION');
  const statement = db.prepare('INSERT INTO ' + table + '(' + columns.join(',') + ') VALUES(' + columns.map(() => '?').join(',') + ')');
  try {
    for (const row of rows) statement.run(row);
    db.exec('COMMIT');
  } catch (error) {
    db.exec('ROLLBACK');
    throw error?.code ? error : restoreError('RESTORE_CONSTRAINT_FAILED');
  } finally { statement.free(); }
}

export async function finishRestoreSqlite(db, backup) {
  const { data, normalization } = normalizeAccountingRestoreData(backup);
  const integrity = db.exec('PRAGMA integrity_check')?.[0]?.values;
  if (integrity?.length !== 1 || integrity[0][0] !== 'ok' || db.exec('PRAGMA foreign_key_check')?.[0]?.values?.length) throw restoreError('RESTORE_INTEGRITY_FAILED');
  const counts = {};
  for (const [key, table] of ACCOUNTING_RESTORE_TABLES) {
    counts[key] = Number(db.exec('SELECT COUNT(*) FROM ' + table)[0].values[0][0]);
    if (counts[key] !== data[key].length) throw restoreError('RESTORE_ROUNDTRIP_MISMATCH');
  }
  // Every source row was read back with the canonical export mapper in its batch.
  return { engine: 'SQLite sql.js 1.14.2', isolated: true, liveAccountingModified: false,
    sourceSchemaVersion: Number(backup.manifest.schemaVersion), normalization,
    targetSchemaVersion: buildMetadata.schemaVersion, counts,
    rowCount: Object.values(counts).reduce((a,b) => a+b, 0),
    integrityCheck: true, foreignKeyCheck: true, fullRowRoundtrip: true };
}

export async function verifyRestoreIntoEmptySqlite(db, schemas, backup) {
  const { data } = normalizeAccountingRestoreData(backup);
  initializeRestoreSqlite(db, schemas);
  for (let table = 0; table < ACCOUNTING_RESTORE_TABLES.length; table++) {
    const rows = data[ACCOUNTING_RESTORE_TABLES[table][0]];
    for (let offset = 0; offset < rows.length; offset += 500) restoreAccountingBatch(db, data, table, offset);
  }
  if (backup.manifest.recoveryContext) {
    for (let table = 0; table < Object.keys(RECOVERY_TABLES).length; table++) {
      const rows = backup.manifest.recoveryContext.tables[Object.keys(RECOVERY_TABLES)[table]];
      for (let offset = 0; offset < rows.length; offset += 500) restoreContextBatch(db, backup.manifest.recoveryContext, table, offset);
    }
  }
  const proof = await finishRestoreSqlite(db, backup);
  const restored = await buildBackupPackage(sqliteReadAdapter(db), new Date(backup.manifest.createdAt));
  if (stableJson(data) !== stableJson(restored.data)) throw restoreError('RESTORE_ROUNDTRIP_MISMATCH');
  return { ...proof, restoredDataSha256: restored.dataSha256 };
}

export async function advanceRestoreSqlite(db, schemas, backup, cursor) {
  if (!cursor) {
    initializeRestoreSqlite(db, schemas);
    return { snapshot: db.export(), cursor: { phase: 'accounting', table: 0, offset: 0 } };
  }
  const { data } = normalizeAccountingRestoreData(backup);
  if (cursor.phase === 'finish') return { proof: await finishRestoreSqlite(db, backup), cursor };
  const context = cursor.phase === 'context';
  const names = context ? Object.keys(RECOVERY_TABLES) : ACCOUNTING_RESTORE_TABLES.map(([key]) => key);
  const rows = context ? backup.manifest.recoveryContext.tables[names[cursor.table]] : data[names[cursor.table]];
  const written = context
    ? restoreContextBatch(db, backup.manifest.recoveryContext, cursor.table, cursor.offset)
    : restoreAccountingBatch(db, data, cursor.table, cursor.offset);
  let next = { ...cursor, offset: cursor.offset + written };
  if (next.offset >= rows.length) next = { ...next, table: cursor.table + 1, offset: 0 };
  if (next.table >= names.length) next = !context && backup.manifest.recoveryContext
    ? { phase: 'context', table: 0, offset: 0 } : { phase: 'finish', table: 0, offset: 0 };
  return { snapshot: db.export(), cursor: next, written };
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
    return { bind(...values) { args = values; return this; }, rows, async all() { return { results: rows() }; }, async first() { return rows()[0] || null; } };
  } };
}
function restoreError(code) { const error = new Error('隔離還原驗證失敗，請查看驗證代碼。'); error.code = code; return error; }
