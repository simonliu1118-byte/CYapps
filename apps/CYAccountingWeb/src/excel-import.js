import readExcelFile, { readSheet } from 'read-excel-file/universal';
import { unzipSync, zipSync } from 'fflate';
import { runParallelBackup } from './backup-service.js';

const MAX_XLSX_BYTES = 8 * 1024 * 1024;
const MAX_IMPORT_ROWS = 5000;
const MAX_IMPORT_COLUMNS = 50;
const MAX_SUMMARY_LENGTH = 100;
const INSERT_CHUNK_SIZE = 50;

export async function handleExcelImportApi(request, env, session) {
  const url = new URL(request.url);

  if (url.pathname === '/api/import/xlsx/inspect' && request.method === 'POST') {
    return inspectWorkbook(request, url);
  }
  if (url.pathname === '/api/import/preview' && request.method === 'POST') {
    return previewImport(request, env.DB, session);
  }
  if (url.pathname === '/api/import/commit' && request.method === 'POST') {
    return commitImport(request, env, session);
  }
  return null;
}

async function inspectWorkbook(request, url) {
  const length = Number(request.headers.get('content-length') || 0);
  if (Number.isFinite(length) && length > MAX_XLSX_BYTES) {
    return json({ ok: false, error: 'Excel 檔案不可超過 8 MB。' }, 413);
  }

  const buffer = await request.arrayBuffer();
  if (!buffer.byteLength) return json({ ok: false, error: 'Excel 檔案內容為空。' }, 400);
  if (buffer.byteLength > MAX_XLSX_BYTES) return json({ ok: false, error: 'Excel 檔案不可超過 8 MB。' }, 413);

  try {
    // Workers cannot spawn the nested decompression workers used by the reader
    // for large deflated XML entries. Decode once with a bounded synchronous
    // unzip, then pass stored XML entries to the same canonical XLSX parser.
    const stored = prepareXlsxForWorker(buffer);
    const mode = url.searchParams.get('mode') || 'workbook';
    if (mode === 'sheet') {
      const sheetParam = url.searchParams.get('sheet');
      const sheet = sheetParam && /^\d+$/.test(sheetParam) ? Number(sheetParam) : (sheetParam || 1);
      const rows = await readSheet(stored, sheet);
      if (rows.length > MAX_IMPORT_ROWS + 100) {
        return json({ ok: false, error: `單一工作表最多可解析 ${MAX_IMPORT_ROWS} 筆資料；請先分割檔案。` }, 413);
      }
      return json({ ok: true, rows: serializeRows(rows, MAX_IMPORT_ROWS + 100) });
    }

    const sheets = await readExcelFile(stored);
    return json({
      ok: true,
      sheets: sheets.map((item, index) => ({
        index: index + 1,
        name: String(item.sheet || `Sheet${index + 1}`),
        rowCount: item.data?.length || 0,
        sample: serializeRows(item.data || [], 30)
      }))
    });
  } catch (error) {
    console.error('cyaccounting_xlsx_parse_failed');
    return json({ ok: false, error: '無法讀取這個 .xlsx 檔案。請確認檔案不是舊版 .xls、損毀或受密碼保護。' }, 400);
  }
}

export function prepareXlsxForWorker(buffer) {
  let expanded = 0;
  let entries = 0;
  const files = unzipSync(new Uint8Array(buffer), { filter: file => {
    expanded += file.originalSize;
    entries += 1;
    if (expanded > 32 * 1024 * 1024 || entries > 1000) throw new Error('XLSX_EXPANSION_LIMIT');
    return /\.(xml|rels)$/i.test(file.name);
  } });
  const stored = zipSync(files, { level: 0 });
  return stored.buffer.slice(stored.byteOffset, stored.byteOffset + stored.byteLength);
}

function serializeRows(rows, limit) {
  return rows.slice(0, limit).map(row => {
    const values = Array.isArray(row) ? row.slice(0, MAX_IMPORT_COLUMNS) : [];
    while (values.length && isEmptyCell(values[values.length - 1])) values.pop();
    return values.map(serializeCell);
  });
}

function serializeCell(value) {
  if (value instanceof Date && !Number.isNaN(value.getTime())) {
    return { __cyType: 'date', value: formatUtcDate(value) };
  }
  if (value === null || value === undefined) return null;
  if (typeof value === 'number' || typeof value === 'boolean' || typeof value === 'string') return value;
  return String(value);
}

function isEmptyCell(value) {
  return value === null || value === undefined || value === '';
}

async function previewImport(request, db, session) {
  const body = await request.json().catch(() => null);
  const rows = Array.isArray(body?.rows) ? body.rows : null;
  if (!rows) return json({ ok: false, error: '匯入資料格式錯誤。' }, 400);
  if (!rows.length) return json({ ok: false, error: '沒有可預覽的資料。' }, 400);
  if (rows.length > MAX_IMPORT_ROWS) return json({ ok: false, error: `單次最多匯入 ${MAX_IMPORT_ROWS} 筆。` }, 413);

  if (body.mode === 'replace_period') return previewPeriodReplacement(body, db, session);
  if (body.mode && body.mode !== 'append') return json({ ok: false, error: '匯入模式錯誤。' }, 400);
  const analysis = await analyzeImportRows(rows, db);
  return json({ ok: true, ...analysis });
}

async function commitImport(request, env, session) {
  const db = env.DB;
  const body = await request.json().catch(() => null);
  const rows = Array.isArray(body?.rows) ? body.rows : null;
  if (!rows || body?.confirm !== true) return json({ ok: false, error: '必須先完成預覽並確認匯入。' }, 400);
  if (!rows.length) return json({ ok: false, error: '沒有可匯入的資料。' }, 400);
  if (rows.length > MAX_IMPORT_ROWS) return json({ ok: false, error: `單次最多匯入 ${MAX_IMPORT_ROWS} 筆。` }, 413);

  if (body.mode === 'replace_period') return commitPeriodReplacement(body, env, session);
  if (body.mode && body.mode !== 'append') return json({ ok: false, error: '匯入模式錯誤。' }, 400);

  const analysis = await analyzeImportRows(rows, db, true);
  if (analysis.summary.errors || analysis.summary.locked) {
    return json({
      ok: false,
      error: '資料狀態已變更或仍有錯誤，未寫入任何資料。請重新檢查預覽。',
      preview: analysis
    }, 409);
  }

  const ready = analysis._readyRows || [];
  if (!ready.length) {
    return json({ ok: true, inserted: 0, skippedDuplicates: analysis.summary.duplicates, message: '沒有新資料需要匯入。' });
  }

  const now = new Date().toISOString();
  const statements = [];
  for (let start = 0; start < ready.length; start += INSERT_CHUNK_SIZE) {
    const chunk = ready.slice(start, start + INSERT_CHUNK_SIZE);
    const placeholders = chunk.map(() => '(?, ?, ?, ?, ?, ?, ?, ?)').join(', ');
    const values = [];
    for (const row of chunk) {
      values.push(row.txDate, row.accountName, row.kind, row.categoryName, row.summary, row.amount, now, now);
    }
    statements.push(db.prepare(`
      INSERT INTO transactions(tx_date, account_name, kind, category_name, summary, amount, created_at, updated_at)
      VALUES ${placeholders}
    `).bind(...values));
  }

  try {
    await db.batch(statements);
  } catch (error) {
    console.error('cyaccounting_excel_import_write_failed', error instanceof Error ? error.message : String(error));
    return json({ ok: false, error: 'Excel 匯入寫入失敗，未完成匯入。請重新整理後再試。' }, 500);
  }
  return json({
    ok: true,
    inserted: ready.length,
    skippedDuplicates: analysis.summary.duplicates,
    message: `已匯入 ${ready.length} 筆資料。`
  });
}

export async function analyzeImportRows(rows, db, includeReadyRows = false, options = {}) {
  const normalized = rows.map((row, index) => normalizeImportRow(row, index));
  const [accountsResult, categoriesResult, lockedRow] = options.target ? [
    { results: options.target.accounts.map(row => ({ name: row[1] })) },
    { results: options.target.categories.map(row => ({ kind: row[1], name: row[3] })) },
    { value: options.target.lockedThrough }
  ] : await Promise.all([
    db.prepare('SELECT name FROM accounts').all(),
    db.prepare('SELECT kind, name FROM categories').all(),
    db.prepare("SELECT value FROM app_settings WHERE key = 'locked_through'").first()
  ]);

  const accounts = new Set((accountsResult.results || []).map(row => String(row.name || '')));
  const categories = new Set((categoriesResult.results || []).map(row => `${row.kind}\u0000${row.name}`));
  const lockedThrough = isMonth(String(lockedRow?.value || '')) ? String(lockedRow.value) : null;
  const validDates = normalized.filter(item => !item.fieldError && isDate(item.row.txDate)).map(item => item.row.txDate);
  const minDate = validDates.length ? validDates.reduce((a, b) => a < b ? a : b) : null;
  const maxDate = validDates.length ? validDates.reduce((a, b) => a > b ? a : b) : null;

  let existingRows = [];
  if (minDate && maxDate && !options.replacePeriod) {
    const result = await db.prepare(`
      SELECT tx_date, account_name, kind, category_name, summary, amount
      FROM transactions
      WHERE tx_date BETWEEN ? AND ?
    `).bind(minDate, maxDate).all();
    existingRows = result.results || [];
  }

  const existingCounts = new Map();
  for (const row of existingRows) {
    const key = fingerprint({
      txDate: row.tx_date,
      accountName: row.account_name,
      kind: row.kind,
      categoryName: row.category_name,
      summary: row.summary || '',
      amount: Number(row.amount)
    });
    existingCounts.set(key, (existingCounts.get(key) || 0) + 1);
  }

  const incomingCounts = new Map();
  const results = [];
  const readyRows = [];
  const seenSourceRows = new Set();

  for (const item of normalized) {
    const row = item.row;
    let status = 'ready';
    let message = '';

    if (seenSourceRows.has(row.sourceRow)) {
      status = 'error';
      message = `來源列 ${row.sourceRow} 重複。`;
    } else {
      seenSourceRows.add(row.sourceRow);
    }

    if (status === 'ready' && item.fieldError) {
      status = 'error';
      message = item.fieldError;
    }
    if (status === 'ready' && !accounts.has(row.accountName)) {
      status = 'error';
      message = `帳戶「${row.accountName}」不存在。`;
    }
    if (status === 'ready' && !categories.has(`${row.kind}\u0000${row.categoryName}`) && !options.createCategories) {
      status = 'error';
      message = `${row.kind === 'income' ? '收入' : '支出'}科目「${row.categoryName}」不存在。`;
    }
    if (status === 'ready' && lockedThrough && row.txDate.slice(0, 7) <= lockedThrough) {
      status = 'locked';
      message = `${row.txDate.slice(0, 7)} 已鎖帳。`;
    }

    if (status === 'ready') {
      const key = fingerprint(row);
      const occurrence = (incomingCounts.get(key) || 0) + 1;
      incomingCounts.set(key, occurrence);
      if (occurrence <= (existingCounts.get(key) || 0)) {
        status = 'duplicate';
        message = '資料庫已有相同資料，將略過。';
      } else {
        readyRows.push(row);
      }
    }

    results.push({
      sourceRow: row.sourceRow,
      status,
      message,
      txDate: row.txDate,
      accountName: row.accountName,
      kind: row.kind,
      categoryName: row.categoryName,
      summary: row.summary,
      amount: row.amount
    });
  }

  const summary = {
    total: results.length,
    ready: results.filter(row => row.status === 'ready').length,
    duplicates: results.filter(row => row.status === 'duplicate').length,
    locked: results.filter(row => row.status === 'locked').length,
    errors: results.filter(row => row.status === 'error').length
  };

  const response = {
    results,
    summary,
    canCommit: summary.ready > 0 && summary.locked === 0 && summary.errors === 0
  };
  if (includeReadyRows) response._readyRows = readyRows;
  return response;
}

const IMPORT_TARGET_SQL = `SELECT json_object(
  'transactions', json((SELECT json_group_array(json_array(id, tx_date, account_name, kind, category_name, summary, amount, created_at, updated_at)) FROM (SELECT * FROM transactions ORDER BY id))),
  'accounts', json((SELECT json_group_array(json_array(id, name, sort_order, is_default, archived_at)) FROM (SELECT * FROM accounts ORDER BY id))),
  'groups', json((SELECT json_group_array(json_array(id, kind, name, sort_order)) FROM (SELECT * FROM category_groups ORDER BY id))),
  'categories', json((SELECT json_group_array(json_array(id, kind, group_id, name, sort_order, is_favorite)) FROM (SELECT * FROM categories ORDER BY id))),
  'openings', json((SELECT json_group_array(json_array(month, account_name, amount, reason, updated_at)) FROM (SELECT * FROM opening_balance_overrides ORDER BY month, account_name))),
  'lockedThrough', (SELECT value FROM app_settings WHERE key = 'locked_through')
) AS snapshot`;

function replacementError(message, status = 400) {
  return json({ ok: false, error: message }, status);
}

function validateReplacement(body, session) {
  if (session?.role !== 'SUPER_ADMIN') return replacementError('只有超級管理員可以替換指定期間。', 403);
  if (!isMonth(body.startMonth) || !isMonth(body.endMonth) || body.startMonth > body.endMonth) {
    return replacementError('請指定正確的起訖月份。');
  }
  if (!String(body.reason || '').trim() || String(body.reason).length > 200) {
    return replacementError('期間替換必須填寫理由，最多 200 字。');
  }
  if (body.createCategories !== undefined && typeof body.createCategories !== 'boolean') {
    return replacementError('科目建立選項格式錯誤。');
  }
  return null;
}

async function replacementAnalysis(body, db) {
  const current = await db.prepare(IMPORT_TARGET_SQL).first();
  const targetJson = current.snapshot;
  const target = JSON.parse(targetJson);
  const analysis = await analyzeImportRows(body.rows, db, true, {
    replacePeriod: true, createCategories: body.createCategories === true, target
  });
  for (const row of analysis.results) {
    if (row.status === 'ready' && (row.txDate.slice(0, 7) < body.startMonth || row.txDate.slice(0, 7) > body.endMonth)) {
      row.status = 'error';
      row.message = '日期超出指定替換期間。';
      analysis.summary.ready -= 1;
      analysis.summary.errors += 1;
    }
  }
  analysis._readyRows = analysis.results.filter(row => row.status === 'ready').map(({ status, message, ...row }) => row);
  analysis.canCommit = analysis.summary.ready > 0 && !analysis.summary.errors && !analysis.summary.locked;
  const lockedRange = Boolean(target.lockedThrough && body.startMonth <= target.lockedThrough);
  analysis.canCommit = analysis.canCommit && !lockedRange;
  const existing = target.transactions.filter(row => row[1].slice(0, 7) >= body.startMonth && row[1].slice(0, 7) <= body.endMonth);
  const knownCategories = new Set(target.categories.map(row => `${row[1]}\u0000${row[3]}`));
  const missing = new Map();
  for (const row of analysis._readyRows) {
    const key = `${row.kind}\u0000${row.categoryName}`;
    if (!knownCategories.has(key)) missing.set(key, { kind: row.kind, name: row.categoryName });
  }
  const endExclusive = nextMonth(body.endMonth) + '-01';
  const range = { start: body.startMonth + '-01', endExclusive };
  const sourceSha256 = await digest(JSON.stringify(analysis.results.map(({ status, message, sourceRow, ...row }) => row)));
  const previewToken = await digest(JSON.stringify({
    targetJson, sourceSha256, range, reason: String(body.reason).trim(), createCategories: body.createCategories === true
  }));
  const openingOverrides = target.openings.map(row => ({ month: row[0], accountName: row[1], amount: row[2], reason: row[3] }));
  const plan = {
    mode: 'replace_period', startMonth: body.startMonth, endMonth: body.endMonth,
    deleteCount: existing.length, insertCount: analysis.summary.ready,
    preservedCount: target.transactions.length - existing.length,
    missingCategories: [...missing.values()], openingOverrides,
    lockedThrough: target.lockedThrough || null, lockedRange, previewToken, sourceSha256
  };
  return { analysis, targetJson, range, plan };
}

async function previewPeriodReplacement(body, db, session) {
  const error = validateReplacement(body, session);
  if (error) return error;
  const { analysis, plan } = await replacementAnalysis(body, db);
  const { _readyRows, ...publicAnalysis } = analysis;
  return json({ ok: true, ...publicAnalysis, replacement: plan });
}

// A single guarded D1 batch owns the audit, optional historical masters,
// deletion and inserts. If anything changes after preview, every write is a
// no-op. No lock, opening-balance override or out-of-range transaction is changed.
export function buildReplacementStatements(db, replacement, body, session, backupId, now) {
  const { analysis, targetJson, range, plan } = replacement;
  const runId = plan.previewToken;
  const actor = JSON.stringify({ employeeId: String(session.employee_id || ''), employeeNo: String(session.employee_no || ''), name: String(session.employee_name || ''), role: session.role });
  const statements = [db.prepare(`
    INSERT INTO excel_import_runs(id, start_date, end_exclusive, reason, source_sha256, backup_id, deleted_count, inserted_count, actor_json, created_at)
    SELECT ?, ?, ?, ?, ?, ?, ?, ?, ?, ?
    WHERE (${IMPORT_TARGET_SQL}) = ?
  `).bind(runId, range.start, range.endExclusive, String(body.reason).trim(), plan.sourceSha256, backupId, plan.deleteCount, plan.insertCount, actor, now, targetJson)];
  const guard = 'EXISTS (SELECT 1 FROM excel_import_runs WHERE id = ?)';
  if (plan.missingCategories.length) {
    const kinds = [...new Set(plan.missingCategories.map(row => row.kind))];
    statements.push(db.prepare(`
      INSERT OR IGNORE INTO category_groups(kind, name, sort_order, created_at)
      SELECT value, '歷史科目', (SELECT COALESCE(MAX(sort_order), -1) + 1 FROM category_groups WHERE kind = j.value), ?
      FROM json_each(?) j WHERE ${guard}
    `).bind(now, JSON.stringify(kinds), runId));
    statements.push(db.prepare(`
      INSERT INTO categories(kind, group_id, name, sort_order, is_favorite, created_at)
      SELECT json_extract(j.value, '$.kind'), g.id, json_extract(j.value, '$.name'),
             (SELECT COALESCE(MAX(sort_order), -1) + 1 FROM categories WHERE group_id = g.id) + CAST(j.key AS INTEGER), 0, ?
      FROM json_each(?) j JOIN category_groups g ON g.kind = json_extract(j.value, '$.kind') AND g.name = '歷史科目'
      WHERE ${guard}
    `).bind(now, JSON.stringify(plan.missingCategories), runId));
  }
  statements.push(db.prepare(`DELETE FROM transactions WHERE tx_date >= ? AND tx_date < ? AND ${guard}`).bind(range.start, range.endExclusive, runId));
  for (let start = 0; start < analysis._readyRows.length; start += 400) {
    const rows = analysis._readyRows.slice(start, start + 400);
    statements.push(db.prepare(`
      INSERT INTO transactions(tx_date, account_name, kind, category_name, summary, amount, created_at, updated_at)
      SELECT json_extract(value, '$.txDate'), json_extract(value, '$.accountName'), json_extract(value, '$.kind'),
             json_extract(value, '$.categoryName'), json_extract(value, '$.summary'), CAST(json_extract(value, '$.amount') AS INTEGER), ?, ?
      FROM json_each(?) WHERE ${guard}
    `).bind(now, now, JSON.stringify(rows), runId));
  }
  return statements;
}

async function commitPeriodReplacement(body, env, session) {
  const error = validateReplacement(body, session);
  if (error) return error;
  const db = env.DB;
  const replacement = await replacementAnalysis(body, db);
  if (!replacement.analysis.canCommit || body.previewToken !== replacement.plan.previewToken) {
    return replacementError('預覽已失效、月份仍鎖帳或資料有錯誤，未替換任何資料。請重新預覽。', 409);
  }
  const previous = await db.prepare('SELECT id FROM excel_import_runs WHERE id = ?').bind(body.previewToken).first();
  if (previous) return replacementError('這次期間替換已完成，請重新整理確認結果。', 409);
  let backup;
  try {
    backup = await runParallelBackup(env, 'manual');
  } catch {
    return replacementError('替換前備份未完成雙 Provider 驗證，未修改任何帳目。', 503);
  }
  try {
    await db.batch(buildReplacementStatements(db, replacement, body, session, backup.backupId, new Date().toISOString()));
  } catch {
    console.error('cyaccounting_period_replacement_failed');
    return replacementError('期間替換失敗，D1 transaction 已回滾。請重新預覽。', 500);
  }
  const completed = await db.prepare('SELECT id FROM excel_import_runs WHERE id = ?').bind(body.previewToken).first();
  if (!completed) return replacementError('備份期間帳本有變動，未替換任何資料。請重新預覽。', 409);
  return json({ ok: true, inserted: replacement.plan.insertCount, deleted: replacement.plan.deleteCount,
    preserved: replacement.plan.preservedCount, backupId: backup.backupId,
    message: `已替換 ${body.startMonth}～${body.endMonth}：移除 ${replacement.plan.deleteCount} 筆，寫入 ${replacement.plan.insertCount} 筆，保留期間外 ${replacement.plan.preservedCount} 筆。` });
}

function nextMonth(month) {
  const [year, value] = month.split('-').map(Number);
  return value === 12 ? `${year + 1}-01` : `${year}-${String(value + 1).padStart(2, '0')}`;
}

async function digest(value) {
  const bytes = new Uint8Array(await crypto.subtle.digest('SHA-256', new TextEncoder().encode(value)));
  return Array.from(bytes, byte => byte.toString(16).padStart(2, '0')).join('');
}

function normalizeImportRow(value, index) {
  const sourceRow = Number.isInteger(Number(value?.sourceRow)) && Number(value.sourceRow) > 0 ? Number(value.sourceRow) : index + 1;
  const row = {
    sourceRow,
    txDate: String(value?.txDate || '').trim(),
    accountName: normalizeName(value?.accountName),
    kind: String(value?.kind || '').trim(),
    categoryName: normalizeName(value?.categoryName),
    summary: String(value?.summary || '').trim(),
    amount: Number(value?.amount)
  };

  let fieldError = '';
  if (!isDate(row.txDate)) fieldError = '日期格式或日期內容不正確。';
  else if (!row.accountName) fieldError = '帳戶不可空白。';
  else if (!['income', 'expense'].includes(row.kind)) fieldError = '收支必須是收入或支出。';
  else if (!row.categoryName) fieldError = '科目不可空白。';
  else if (row.summary.length > MAX_SUMMARY_LENGTH) fieldError = `摘要不可超過 ${MAX_SUMMARY_LENGTH} 字。`;
  else if (!Number.isInteger(row.amount) || row.amount < 1 || row.amount > 9_999_999) fieldError = '金額必須為 1～9,999,999 的整數。';
  return { row, fieldError };
}

function fingerprint(row) {
  return JSON.stringify([
    row.txDate,
    row.accountName,
    row.kind,
    row.categoryName,
    row.summary || '',
    Number(row.amount)
  ]);
}

function normalizeName(value) {
  return String(value ?? '').trim().replace(/\s+/g, ' ');
}

function isMonth(value) {
  return /^\d{4}-(0[1-9]|1[0-2])$/.test(String(value || ''));
}

function isDate(value) {
  if (!/^\d{4}-\d{2}-\d{2}$/.test(String(value || ''))) return false;
  const [y, m, d] = String(value).split('-').map(Number);
  const date = new Date(Date.UTC(y, m - 1, d));
  return date.getUTCFullYear() === y && date.getUTCMonth() === m - 1 && date.getUTCDate() === d;
}

function formatUtcDate(date) {
  return `${date.getUTCFullYear()}-${String(date.getUTCMonth() + 1).padStart(2, '0')}-${String(date.getUTCDate()).padStart(2, '0')}`;
}

function json(data, status = 200) {
  return new Response(JSON.stringify(data), {
    status,
    headers: { 'content-type': 'application/json; charset=utf-8', 'cache-control': 'no-store' }
  });
}
