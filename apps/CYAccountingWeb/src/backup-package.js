import buildMetadata from './build-metadata.json' with { type: 'json' };
const BACKUP_FORMAT = 'CYAccountingWebBackup';
const BACKUP_FORMAT_VERSION = 2;
const APP_VERSION = buildMetadata.appVersion;
const BACKUP_PREFIX = 'CYAccountingWeb_backup_';
const PAGE_SIZE = 1000;
const encoder = new TextEncoder();

export async function buildBackupPackage(db, now = new Date()) {
  // A failed query stops the export; no parallel reads continue after quota exhaustion.
  const budget = { remaining: 20000 };
  const schemaRow = await db.prepare("SELECT value FROM meta WHERE key = 'schema_version'").first();
  const accounts = await readPaged(db, budget, 'SELECT id, name, sort_order, is_default, color_slot, created_at, archived_at FROM accounts ORDER BY sort_order, id', mapAccount);
  const groups = await readPaged(db, budget, 'SELECT id, kind, name, sort_order, created_at FROM category_groups ORDER BY kind, sort_order, id', mapGroup);
  const categories = await readPaged(db, budget, 'SELECT id, kind, group_id, name, sort_order, is_favorite, created_at FROM categories ORDER BY kind, group_id, sort_order, id', mapCategory);
  const transactions = await readPaged(db, budget, 'SELECT id, tx_date, account_name, kind, category_name, summary, amount, marker_color, created_at, updated_at FROM transactions ORDER BY id', mapTransaction);
  const openingOverrides = await readPaged(db, budget, 'SELECT month, account_name, amount, reason, created_at, updated_at, updated_by_employee_id, updated_by_employee_no, updated_by_name, updated_by_role FROM opening_balance_overrides ORDER BY month, account_name', mapOpeningOverride);
  const openingAudit = await readPaged(db, budget, 'SELECT id, month, account_name, action, previous_amount, new_amount, reason, actor_employee_id, actor_employee_no, actor_name, actor_role, created_at FROM opening_balance_audit ORDER BY id', mapOpeningAudit);
  const settings = await readPaged(db, budget, 'SELECT key, value FROM app_settings ORDER BY key', mapSetting);
  const importRuns = Number(schemaRow?.value || 0) >= 8 ? await readPaged(db, budget,
    'SELECT id, start_date, end_exclusive, reason, source_sha256, backup_id, deleted_count, inserted_count, actor_json, created_at FROM excel_import_runs ORDER BY created_at, id', row => ({
      id: String(row.id), startDate: String(row.start_date), endExclusive: String(row.end_exclusive),
      reason: String(row.reason), sourceSha256: String(row.source_sha256), backupId: String(row.backup_id),
      deletedCount: Number(row.deleted_count), insertedCount: Number(row.inserted_count),
      actor: JSON.parse(row.actor_json), createdAt: String(row.created_at)
    })) : [];

  const data = {
    accounts,
    categoryGroups: groups,
    categories,
    openingBalanceOverrides: openingOverrides,
    openingBalanceAudit: openingAudit,
    transactions,
    appSettings: settings,
    excelImportRuns: importRuns
  };
  const counts = Object.fromEntries(Object.entries(data).map(([key, rows]) => [key, rows.length]));
  const totalRowCount = Object.values(counts).reduce((sum, count) => sum + count, 0);
  const canonicalData = JSON.stringify(data);
  const dataSha256 = await sha256HexBytes(encoder.encode(canonicalData));
  const createdAt = now.toISOString();
  const manifest = {
    format: BACKUP_FORMAT,
    formatVersion: BACKUP_FORMAT_VERSION,
    appVersion: APP_VERSION,
    schemaVersion: Number(schemaRow?.value || 0),
    createdAt,
    counts,
    totalRowCount,
    dataSha256
  };
  const payload = JSON.stringify({ manifest, data }, null, 2) + '\n';
  const bytes = encoder.encode(payload);
  const fileSha256 = await sha256HexBytes(bytes);
  const stamp = createdAt.replace(/[-:]/g, '').replace(/\.\d{3}Z$/, 'Z');
  const fileName = `${BACKUP_PREFIX}${stamp}.json`;
  return { manifest, data, bytes, dataSha256, fileSha256, totalRowCount, fileName };
}

async function readPaged(db, budget, selectSql, mapper) {
  const rows = [];
  let offset = 0;
  for (let batch = 0; batch <= 20; batch++) {
    const limit = Math.min(PAGE_SIZE, budget.remaining + 1);
    const result = await db.prepare(`${selectSql} LIMIT ? OFFSET ?`).bind(limit, offset).all();
    const page = result.results || [];
    if (page.length > budget.remaining) {
      const error = new Error('備份資料超過本次讀取上限，請由維運人員評估額度後調整。');
      error.code = 'BACKUP_EXPORT_ROW_LIMIT';
      throw error;
    }
    budget.remaining -= page.length;
    for (const row of page) rows.push(mapper(row));
    if (page.length < limit) break;
    offset += page.length;
  }
  return rows;
}

function mapAccount(row) {
  return {
    id: Number(row.id),
    name: String(row.name),
    sortOrder: Number(row.sort_order),
    isDefault: Number(row.is_default),
    colorSlot: Number(row.color_slot || 0) || null,
    createdAt: String(row.created_at),
    archivedAt: row.archived_at ? String(row.archived_at) : null
  };
}

function mapGroup(row) {
  return {
    id: Number(row.id),
    kind: String(row.kind),
    name: String(row.name),
    sortOrder: Number(row.sort_order),
    createdAt: String(row.created_at)
  };
}

function mapCategory(row) {
  return {
    id: Number(row.id),
    kind: String(row.kind),
    groupId: Number(row.group_id),
    name: String(row.name),
    sortOrder: Number(row.sort_order),
    isFavorite: Number(row.is_favorite),
    createdAt: String(row.created_at)
  };
}

function mapTransaction(row) {
  return {
    id: Number(row.id),
    txDate: String(row.tx_date),
    accountName: String(row.account_name),
    kind: String(row.kind),
    categoryName: String(row.category_name),
    summary: String(row.summary || ''),
    amount: Number(row.amount),
    markerColor: row.marker_color ?? null,
    createdAt: String(row.created_at),
    updatedAt: String(row.updated_at)
  };
}

function mapOpeningOverride(row) {
  return {
    month: String(row.month),
    accountName: String(row.account_name),
    amount: Number(row.amount),
    reason: String(row.reason || ''),
    createdAt: String(row.created_at),
    updatedAt: String(row.updated_at),
    updatedByEmployeeId: String(row.updated_by_employee_id || ''),
    updatedByEmployeeNo: String(row.updated_by_employee_no || ''),
    updatedByName: String(row.updated_by_name || ''),
    updatedByRole: String(row.updated_by_role || '')
  };
}

function mapOpeningAudit(row) {
  return {
    id: Number(row.id),
    month: String(row.month),
    accountName: String(row.account_name),
    action: String(row.action),
    previousAmount: row.previous_amount === null ? null : Number(row.previous_amount),
    newAmount: row.new_amount === null ? null : Number(row.new_amount),
    reason: String(row.reason || ''),
    actorEmployeeId: String(row.actor_employee_id || ''),
    actorEmployeeNo: String(row.actor_employee_no || ''),
    actorName: String(row.actor_name || ''),
    actorRole: String(row.actor_role || ''),
    createdAt: String(row.created_at)
  };
}
function mapSetting(row) {
  return { key: String(row.key), value: String(row.value) };
}

async function sha256HexBytes(bytes) {
  const digest = new Uint8Array(await crypto.subtle.digest('SHA-256', bytes));
  return Array.from(digest, byte => byte.toString(16).padStart(2, '0')).join('');
}
