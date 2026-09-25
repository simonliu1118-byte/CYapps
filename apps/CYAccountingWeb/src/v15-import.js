import readExcelFile, { readSheet } from 'read-excel-file/universal';

const MAX_XLSX_BYTES = 8 * 1024 * 1024;
const MAX_IMPORT_ROWS = 5000;
const MAX_IMPORT_COLUMNS = 50;
const MAX_SUMMARY_LENGTH = 100;
const INSERT_CHUNK_SIZE = 50;

export async function handleV15Api(request, env) {
  const url = new URL(request.url);

  if (url.pathname === '/api/import/xlsx/inspect' && request.method === 'POST') {
    return inspectWorkbook(request, url);
  }
  if (url.pathname === '/api/import/preview' && request.method === 'POST') {
    return previewImport(request, env.DB);
  }
  if (url.pathname === '/api/import/commit' && request.method === 'POST') {
    return commitImport(request, env.DB);
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
    const mode = url.searchParams.get('mode') || 'workbook';
    if (mode === 'sheet') {
      const sheetParam = url.searchParams.get('sheet');
      const sheet = sheetParam && /^\d+$/.test(sheetParam) ? Number(sheetParam) : (sheetParam || 1);
      const rows = await readSheet(buffer, sheet);
      if (rows.length > MAX_IMPORT_ROWS + 100) {
        return json({ ok: false, error: `單一工作表最多可解析 ${MAX_IMPORT_ROWS} 筆資料；請先分割檔案。` }, 413);
      }
      return json({ ok: true, rows: serializeRows(rows, MAX_IMPORT_ROWS + 100) });
    }

    const sheets = await readExcelFile(buffer);
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
    console.error('cyaccounting_xlsx_parse_failed', error instanceof Error ? error.message : String(error));
    return json({ ok: false, error: '無法讀取這個 .xlsx 檔案。請確認檔案不是舊版 .xls、損毀或受密碼保護。' }, 400);
  }
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

async function previewImport(request, db) {
  const body = await request.json().catch(() => null);
  const rows = Array.isArray(body?.rows) ? body.rows : null;
  if (!rows) return json({ ok: false, error: '匯入資料格式錯誤。' }, 400);
  if (!rows.length) return json({ ok: false, error: '沒有可預覽的資料。' }, 400);
  if (rows.length > MAX_IMPORT_ROWS) return json({ ok: false, error: `單次最多匯入 ${MAX_IMPORT_ROWS} 筆。` }, 413);

  const analysis = await analyzeImportRows(rows, db);
  return json({ ok: true, ...analysis });
}

async function commitImport(request, db) {
  const body = await request.json().catch(() => null);
  const rows = Array.isArray(body?.rows) ? body.rows : null;
  if (!rows || body?.confirm !== true) return json({ ok: false, error: '必須先完成預覽並確認匯入。' }, 400);
  if (!rows.length) return json({ ok: false, error: '沒有可匯入的資料。' }, 400);
  if (rows.length > MAX_IMPORT_ROWS) return json({ ok: false, error: `單次最多匯入 ${MAX_IMPORT_ROWS} 筆。` }, 413);

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

export async function analyzeImportRows(rows, db, includeReadyRows = false) {
  const normalized = rows.map((row, index) => normalizeImportRow(row, index));
  const [accountsResult, categoriesResult, lockedRow] = await Promise.all([
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
  if (minDate && maxDate) {
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
    if (status === 'ready' && !categories.has(`${row.kind}\u0000${row.categoryName}`)) {
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
