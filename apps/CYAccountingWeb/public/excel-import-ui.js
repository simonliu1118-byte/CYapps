/* CYAccountingWeb Excel import UI functional module. */

const cyImportState = {
  file: null,
  sheets: [],
  rows: [],
  headerRow: 1,
  normalizedRows: [],
  localErrors: [],
  preview: null
};

const CY_IMPORT_FIELDS = [
  { key: 'date', label: '日期 *' },
  { key: 'account', label: '帳戶 *' },
  { key: 'kind', label: '收支（合併模式）' },
  { key: 'category', label: '科目 *' },
  { key: 'summary', label: '摘要' },
  { key: 'amount', label: '金額（合併模式）' },
  { key: 'incomeAmount', label: '收入金額（分欄模式）' },
  { key: 'expenseAmount', label: '支出金額（分欄模式）' }
];

const CY_IMPORT_ALIASES = {
  date: ['日期', '記帳日期', '交易日期', 'date', 'txdate', 'tx_date'],
  account: ['帳戶', '帳戶名稱', 'account', 'accountname', 'account_name'],
  kind: ['收支', '收支類型', '類型', '收入支出', 'kind', 'type'],
  category: ['科目', '科目名稱', '類別', 'category', 'categoryname', 'category_name'],
  summary: ['摘要', '備註', '說明', 'description', 'summary', 'note', 'memo'],
  amount: ['金額', '交易金額', 'amount'],
  incomeAmount: ['收入金額', '收入', 'incomeamount', 'income_amount', 'income'],
  expenseAmount: ['支出金額', '支出', 'expenseamount', 'expense_amount', 'expense']
};

window.addEventListener('load', () => {
  setupExcelImport();
});

function setupExcelImport() {
  const button = document.querySelector('#ledgerExcelImport');
  if (!button || button.dataset.excelImportBound === '1') return;
  button.dataset.excelImportBound = '1';

  if (!document.querySelector('#excelImportDialog')) {
    document.body.insertAdjacentHTML('beforeend', importDialogHtml());
    bindExcelImportDialog();
  }
  button.addEventListener('click', openExcelImport);
}

function importDialogHtml() {
  return `
  <dialog id="excelImportDialog" class="modal excel-import-modal">
    <div class="modal-header">
      <div><h2>匯入 Excel</h2><p>解析 → 欄位對應 → 預覽驗證 → 確認匯入</p></div>
      <button class="icon-button" type="button" data-import-close aria-label="關閉">×</button>
    </div>

    <div class="excel-import-body">
      <section class="import-section">
        <div class="import-section-title"><strong>1. 選擇 Excel 檔案</strong><span class="hint">僅支援 .xlsx，最大 8 MB；檔案只送到 CYAccountingWeb Cloudflare Worker 解析。</span></div>
        <div class="import-file-row">
          <input id="excelImportFile" type="file" accept=".xlsx,application/vnd.openxmlformats-officedocument.spreadsheetml.sheet">
          <span id="excelImportFileStatus" class="hint"></span>
        </div>
      </section>

      <section id="excelImportSheetSection" class="import-section hidden">
        <div class="import-section-title"><strong>2. 工作表與標題列</strong><span id="excelImportSheetInfo" class="hint"></span></div>
        <div class="import-sheet-controls">
          <label><span>工作表</span><select id="excelImportSheet"></select></label>
          <label><span>標題列</span><input id="excelImportHeaderRow" type="number" min="1" step="1"></label>
          <button id="excelImportReloadMapping" class="secondary compact" type="button">重新判斷欄位</button>
        </div>
      </section>

      <section id="excelImportMappingSection" class="import-section hidden">
        <div class="import-section-title"><strong>3. 欄位對應</strong><span class="hint">日期／帳戶／科目必填；「收支＋金額」或「收入金額／支出金額」二擇一。</span></div>
        <div id="excelImportMappingGrid" class="import-mapping-grid"></div>
        <div class="import-preview-actions"><button id="excelImportPreviewButton" class="primary" type="button">建立匯入預覽</button></div>
      </section>

      <section id="excelImportPreviewSection" class="import-section hidden">
        <div class="import-section-title"><strong>4. 預覽與驗證</strong><span id="excelImportPreviewSummary" class="import-preview-summary"></span></div>
        <div id="excelImportPreviewNotice" class="dialog-message"></div>
        <div class="import-preview-table-wrap">
          <table class="import-preview-table">
            <thead><tr><th>原列</th><th>日期</th><th>帳戶</th><th>收支</th><th>科目</th><th>摘要</th><th class="num">金額</th><th>狀態</th></tr></thead>
            <tbody id="excelImportPreviewRows"></tbody>
          </table>
        </div>
        <p id="excelImportPreviewLimit" class="hint"></p>
      </section>
    </div>

    <div class="modal-actions excel-import-footer">
      <span id="excelImportMessage" class="dialog-message import-footer-message"></span>
      <button class="secondary" type="button" data-import-close>關閉</button>
      <button id="excelImportCommitButton" class="primary" type="button" disabled>確認匯入</button>
    </div>
  </dialog>`;
}

function bindExcelImportDialog() {
  const dialog = document.querySelector('#excelImportDialog');
  document.querySelectorAll('[data-import-close]').forEach(button => button.addEventListener('click', () => dialog?.close()));
  document.querySelector('#excelImportFile')?.addEventListener('change', handleExcelImportFile);
  document.querySelector('#excelImportSheet')?.addEventListener('change', loadExcelImportSheet);
  document.querySelector('#excelImportHeaderRow')?.addEventListener('change', renderExcelImportMapping);
  document.querySelector('#excelImportReloadMapping')?.addEventListener('click', () => {
    const best = detectImportHeader(cyImportState.rows);
    document.querySelector('#excelImportHeaderRow').value = String(best.row + 1);
    renderExcelImportMapping(true);
  });
  document.querySelector('#excelImportMappingGrid')?.addEventListener('change', resetImportPreview);
  document.querySelector('#excelImportPreviewButton')?.addEventListener('click', buildExcelImportPreview);
  document.querySelector('#excelImportCommitButton')?.addEventListener('click', commitExcelImport);
}

function openExcelImport() {
  setImportMessage('');
  document.querySelector('#excelImportDialog')?.showModal();
}

async function handleExcelImportFile(event) {
  const file = event.target.files?.[0] || null;
  resetImportAfterFile();
  cyImportState.file = file;
  if (!file) return;

  if (!/\.xlsx$/i.test(file.name)) return setImportMessage('只支援 .xlsx 檔案。', true);
  if (file.size > 8 * 1024 * 1024) return setImportMessage('Excel 檔案不可超過 8 MB。', true);

  const status = document.querySelector('#excelImportFileStatus');
  if (status) status.textContent = '讀取工作表中…';
  try {
    const data = await uploadXlsx(file, 'mode=workbook');
    cyImportState.sheets = data.sheets || [];
    if (!cyImportState.sheets.length) throw new Error('Excel 檔案沒有可讀取的工作表。');
    renderImportSheets();
    document.querySelector('#excelImportSheetSection')?.classList.remove('hidden');
    if (status) status.textContent = `${file.name}　${formatImportBytes(file.size)}`;
    await loadExcelImportSheet();
  } catch (error) {
    if (status) status.textContent = '';
    setImportMessage(error.message || 'Excel 解析失敗。', true);
  }
}

function renderImportSheets() {
  const select = document.querySelector('#excelImportSheet');
  if (!select) return;

  let bestIndex = 0;
  let bestScore = -1;
  cyImportState.sheets.forEach((sheet, index) => {
    const score = detectImportHeader(sheet.sample || []).score;
    if (score > bestScore) {
      bestScore = score;
      bestIndex = index;
    }
  });

  select.innerHTML = cyImportState.sheets.map(sheet =>
    `<option value="${sheet.index}">${escapeImportHtml(sheet.name)}（${Number(sheet.rowCount || 0).toLocaleString()} 列）</option>`
  ).join('');
  select.value = String(cyImportState.sheets[bestIndex]?.index || 1);
}

async function loadExcelImportSheet() {
  const file = cyImportState.file;
  const select = document.querySelector('#excelImportSheet');
  if (!file || !select?.value) return;

  setImportMessage('讀取工作表…');
  setImportBusy(true);
  try {
    const data = await uploadXlsx(file, `mode=sheet&sheet=${encodeURIComponent(select.value)}`);
    cyImportState.rows = data.rows || [];
    if (!cyImportState.rows.length) throw new Error('這個工作表沒有資料。');

    const best = detectImportHeader(cyImportState.rows);
    cyImportState.headerRow = best.row + 1;
    const headerInput = document.querySelector('#excelImportHeaderRow');
    headerInput.max = String(cyImportState.rows.length);
    headerInput.value = String(cyImportState.headerRow);

    const sheetMeta = cyImportState.sheets.find(item => String(item.index) === String(select.value));
    const info = document.querySelector('#excelImportSheetInfo');
    if (info) info.textContent = `${sheetMeta?.name || ''}，已讀取 ${cyImportState.rows.length.toLocaleString()} 列；自動判斷標題列為第 ${cyImportState.headerRow} 列。`;

    renderExcelImportMapping(true);
    document.querySelector('#excelImportMappingSection')?.classList.remove('hidden');
    setImportMessage('');
  } catch (error) {
    setImportMessage(error.message || '工作表讀取失敗。', true);
  } finally {
    setImportBusy(false);
  }
}

function renderExcelImportMapping(autoMap = false) {
  const grid = document.querySelector('#excelImportMappingGrid');
  const headerInput = document.querySelector('#excelImportHeaderRow');
  if (!grid || !headerInput || !cyImportState.rows.length) return;

  const headerIndex = Math.max(0, Math.min(cyImportState.rows.length - 1, Number(headerInput.value || 1) - 1));
  cyImportState.headerRow = headerIndex + 1;
  const headers = (cyImportState.rows[headerIndex] || []).map((cell, index) => importCellLabel(cell) || `欄 ${index + 1}`);
  const suggested = autoMap ? autoMapImportColumns(headers) : readCurrentImportMapping();

  grid.innerHTML = CY_IMPORT_FIELDS.map(field => {
    const options = [`<option value="">— 不對應 —</option>`].concat(headers.map((header, index) =>
      `<option value="${index}" ${String(suggested[field.key]) === String(index) ? 'selected' : ''}>${index + 1}. ${escapeImportHtml(header)}</option>`
    ));
    return `<label><span>${field.label}</span><select data-import-map="${field.key}">${options.join('')}</select></label>`;
  }).join('');

  resetImportPreview();
}

function readCurrentImportMapping() {
  const mapping = {};
  document.querySelectorAll('[data-import-map]').forEach(select => {
    mapping[select.dataset.importMap] = select.value === '' ? '' : Number(select.value);
  });
  return mapping;
}

function autoMapImportColumns(headers) {
  const result = {};
  const used = new Set();
  for (const field of CY_IMPORT_FIELDS) {
    let bestIndex = '';
    let bestScore = 0;
    headers.forEach((header, index) => {
      if (used.has(index)) return;
      const score = importHeaderAliasScore(field.key, header);
      if (score > bestScore) {
        bestScore = score;
        bestIndex = index;
      }
    });
    result[field.key] = bestScore > 0 ? bestIndex : '';
    if (bestScore > 0) used.add(bestIndex);
  }
  return result;
}

function detectImportHeader(rows) {
  let best = { row: 0, score: -1 };
  rows.slice(0, 30).forEach((row, rowIndex) => {
    const headers = (row || []).map(importCellLabel);
    const matched = new Set();
    let score = 0;
    for (const header of headers) {
      for (const field of CY_IMPORT_FIELDS) {
        if (matched.has(field.key)) continue;
        const fieldScore = importHeaderAliasScore(field.key, header);
        if (fieldScore > 0) {
          matched.add(field.key);
          score += fieldScore;
          break;
        }
      }
    }
    if (matched.has('date')) score += 3;
    if (matched.has('account')) score += 2;
    if (matched.has('category')) score += 2;
    if ((matched.has('kind') && matched.has('amount')) || matched.has('incomeAmount') || matched.has('expenseAmount')) score += 3;
    if (score > best.score) best = { row: rowIndex, score };
  });
  return best;
}

function importHeaderAliasScore(field, header) {
  const normalized = normalizeImportHeader(header);
  if (!normalized) return 0;
  const aliases = CY_IMPORT_ALIASES[field] || [];
  for (const alias of aliases) {
    const target = normalizeImportHeader(alias);
    if (normalized === target) return 5;
  }
  for (const alias of aliases) {
    const target = normalizeImportHeader(alias);
    if (target && normalized.includes(target)) return 2;
  }
  return 0;
}

function normalizeImportHeader(value) {
  return String(value ?? '').trim().toLowerCase().replace(/[\s_\-()（）\[\]【】]/g, '');
}

async function buildExcelImportPreview() {
  resetImportPreview();
  const dataRowCount = Math.max(0, cyImportState.rows.length - cyImportState.headerRow);
  if (dataRowCount > 5000) return setImportMessage('單次最多匯入 5,000 筆；請先分割工作表。', true);

  const mapping = readCurrentImportMapping();
  const mappingError = validateImportMapping(mapping);
  if (mappingError) return setImportMessage(mappingError, true);

  const built = normalizeMappedImportRows(mapping);
  cyImportState.normalizedRows = built.validRows;
  cyImportState.localErrors = built.errors;
  if (!built.validRows.length && !built.errors.length) return setImportMessage('標題列下方沒有資料。', true);

  setImportBusy(true);
  setImportMessage('驗證匯入資料…');
  try {
    let server = { results: [], summary: { total: 0, ready: 0, duplicates: 0, locked: 0, errors: 0 }, canCommit: false };
    if (built.validRows.length) {
      server = await api('/api/import/preview', {
        method: 'POST', headers: jsonHeaders(), body: JSON.stringify({ rows: built.validRows })
      });
    }

    const mergedResults = [...server.results, ...built.errors].sort((a, b) => a.sourceRow - b.sourceRow);
    const summary = {
      total: mergedResults.length,
      ready: mergedResults.filter(row => row.status === 'ready').length,
      duplicates: mergedResults.filter(row => row.status === 'duplicate').length,
      locked: mergedResults.filter(row => row.status === 'locked').length,
      errors: mergedResults.filter(row => row.status === 'error').length
    };
    cyImportState.preview = { results: mergedResults, summary, canCommit: summary.ready > 0 && summary.locked === 0 && summary.errors === 0 };
    renderExcelImportPreview();
    setImportMessage('');
  } catch (error) {
    setImportMessage(error.message || '匯入預覽失敗。', true);
  } finally {
    setImportBusy(false);
  }
}

function validateImportMapping(mapping) {
  if (mapping.date === '' || mapping.date === undefined) return '請對應「日期」欄位。';
  if (mapping.account === '' || mapping.account === undefined) return '請對應「帳戶」欄位。';
  if (mapping.category === '' || mapping.category === undefined) return '請對應「科目」欄位。';

  const combined = mapping.kind !== '' && mapping.kind !== undefined && mapping.amount !== '' && mapping.amount !== undefined;
  const split = (mapping.incomeAmount !== '' && mapping.incomeAmount !== undefined) || (mapping.expenseAmount !== '' && mapping.expenseAmount !== undefined);
  if (!combined && !split) return '請使用「收支＋金額」合併模式，或至少對應一個「收入金額／支出金額」欄位。';

  const used = new Map();
  for (const [key, value] of Object.entries(mapping)) {
    if (value === '' || value === undefined) continue;
    if (used.has(value)) return `同一來源欄位不可同時對應「${importMappingFieldLabel(used.get(value))}」與「${importMappingFieldLabel(key)}」。`;
    used.set(value, key);
  }
  return '';
}

function importMappingFieldLabel(key) {
  return CY_IMPORT_FIELDS.find(field => field.key === key)?.label.replace(' *', '') || key;
}

function normalizeMappedImportRows(mapping) {
  const validRows = [];
  const errors = [];
  const startIndex = cyImportState.headerRow;
  const combined = mapping.kind !== '' && mapping.kind !== undefined && mapping.amount !== '' && mapping.amount !== undefined;

  for (let index = startIndex; index < cyImportState.rows.length; index += 1) {
    const source = cyImportState.rows[index] || [];
    if (source.every(cell => importCellLabel(cell).trim() === '')) continue;
    const sourceRow = index + 1;

    const txDate = normalizeImportDate(source[mapping.date]);
    const accountName = importCellLabel(source[mapping.account]).trim();
    const categoryName = importCellLabel(source[mapping.category]).trim();
    const summary = mapping.summary === '' || mapping.summary === undefined ? '' : importCellLabel(source[mapping.summary]).trim();
    let kind = '';
    let amount = NaN;
    let error = '';

    if (combined) {
      kind = normalizeImportKind(source[mapping.kind]);
      amount = normalizeImportAmount(source[mapping.amount]);
      if (!kind) error = '無法辨識收支類型。';
    } else {
      const income = mapping.incomeAmount === '' || mapping.incomeAmount === undefined ? null : normalizeOptionalImportAmount(source[mapping.incomeAmount]);
      const expense = mapping.expenseAmount === '' || mapping.expenseAmount === undefined ? null : normalizeOptionalImportAmount(source[mapping.expenseAmount]);
      if (income?.error) error = '收入金額格式錯誤。';
      else if (expense?.error) error = '支出金額格式錯誤。';
      else if ((income?.value || 0) > 0 && (expense?.value || 0) > 0) error = '同一列同時有收入與支出金額。';
      else if ((income?.value || 0) > 0) { kind = 'income'; amount = income.value; }
      else if ((expense?.value || 0) > 0) { kind = 'expense'; amount = expense.value; }
      else error = '收入／支出金額皆為空白或 0。';
    }

    if (!error && !txDate) error = '日期格式無法辨識。';
    else if (!error && !accountName) error = '帳戶不可空白。';
    else if (!error && !categoryName) error = '科目不可空白。';
    else if (!error && summary.length > 100) error = '摘要超過 100 字。';
    else if (!error && (!Number.isInteger(amount) || amount < 1 || amount > 9_999_999)) error = '金額必須為 1～9,999,999 的整數。';

    const normalized = { sourceRow, txDate, accountName, kind, categoryName, summary, amount: Number.isFinite(amount) ? amount : 0 };
    if (error) errors.push({ ...normalized, status: 'error', message: error });
    else validRows.push(normalized);
  }
  return { validRows, errors };
}

function normalizeImportDate(cell) {
  if (cell && typeof cell === 'object' && cell.__cyType === 'date') return validImportIsoDate(String(cell.value || '')) ? String(cell.value) : '';
  if (typeof cell === 'number' && Number.isFinite(cell)) {
    const digits = String(Math.trunc(cell));
    if (/^\d{8}$/.test(digits)) return buildImportDate(Number(digits.slice(0, 4)), Number(digits.slice(4, 6)), Number(digits.slice(6, 8)));
  }

  const text = String(cell ?? '').trim();
  if (!text) return '';
  if (/^\d{4}-\d{2}-\d{2}T/.test(text)) return validImportIsoDate(text.slice(0, 10)) ? text.slice(0, 10) : '';
  if (/^\d{8}$/.test(text)) return buildImportDate(Number(text.slice(0, 4)), Number(text.slice(4, 6)), Number(text.slice(6, 8)));

  const match = /^(\d{3,4})[\/\.\-](\d{1,2})[\/\.\-](\d{1,2})$/.exec(text);
  if (!match) return '';
  let year = Number(match[1]);
  if (match[1].length === 3) year += 1911;
  return buildImportDate(year, Number(match[2]), Number(match[3]));
}

function buildImportDate(year, month, day) {
  if (!Number.isInteger(year) || year < 1900 || year > 2200 || month < 1 || month > 12 || day < 1 || day > 31) return '';
  const date = new Date(Date.UTC(year, month - 1, day));
  if (date.getUTCFullYear() !== year || date.getUTCMonth() !== month - 1 || date.getUTCDate() !== day) return '';
  return `${year}-${String(month).padStart(2, '0')}-${String(day).padStart(2, '0')}`;
}

function validImportIsoDate(value) {
  return Boolean(/^\d{4}-\d{2}-\d{2}$/.test(value) && buildImportDate(...value.split('-').map(Number)) === value);
}

function normalizeImportKind(cell) {
  const text = String(cell ?? '').trim().toLowerCase().replace(/\s+/g, '');
  if (['收入', '收', 'income', 'in', '+'].includes(text)) return 'income';
  if (['支出', '支', 'expense', 'out', '-'].includes(text)) return 'expense';
  return '';
}

function normalizeImportAmount(cell) {
  if (typeof cell === 'number') return Number.isFinite(cell) ? cell : NaN;
  const text = String(cell ?? '').trim();
  if (!text) return NaN;
  const cleaned = text.replace(/[,$＄元\s]/g, '').replace(/^nt/i, '');
  const value = Number(cleaned);
  return Number.isFinite(value) ? value : NaN;
}

function normalizeOptionalImportAmount(cell) {
  if (cell === null || cell === undefined || String(cell).trim() === '') return { value: 0, error: false };
  const value = normalizeImportAmount(cell);
  if (!Number.isFinite(value) || value < 0) return { value: 0, error: true };
  return { value, error: false };
}

function renderExcelImportPreview() {
  const preview = cyImportState.preview;
  if (!preview) return;
  document.querySelector('#excelImportPreviewSection')?.classList.remove('hidden');

  const summary = preview.summary;
  const summaryEl = document.querySelector('#excelImportPreviewSummary');
  if (summaryEl) summaryEl.innerHTML = `可匯入 <strong>${summary.ready}</strong>　重複 <strong>${summary.duplicates}</strong>　鎖帳 <strong>${summary.locked}</strong>　錯誤 <strong>${summary.errors}</strong>`;

  const notice = document.querySelector('#excelImportPreviewNotice');
  if (summary.errors || summary.locked) {
    setDialogMessage(notice, '有錯誤或鎖帳資料時不允許部分匯入；請修正檔案或欄位對應後重新預覽。', true);
  } else if (!summary.ready && summary.duplicates) {
    setDialogMessage(notice, '所有資料都已存在，沒有新資料需要匯入。');
  } else {
    setDialogMessage(notice, `確認後會新增 ${summary.ready} 筆，並略過 ${summary.duplicates} 筆重複資料。`);
  }

  const rows = preview.results.slice(0, 300);
  const tbody = document.querySelector('#excelImportPreviewRows');
  tbody.innerHTML = rows.map(row => `<tr class="import-status-${row.status}">
    <td>${row.sourceRow}</td>
    <td>${escapeImportHtml(row.txDate || '')}</td>
    <td>${escapeImportHtml(row.accountName || '')}</td>
    <td>${row.kind === 'income' ? '收入' : row.kind === 'expense' ? '支出' : '—'}</td>
    <td>${escapeImportHtml(row.categoryName || '')}</td>
    <td class="summary">${escapeImportHtml(row.summary || '')}</td>
    <td class="num">${Number(row.amount || 0).toLocaleString()}</td>
    <td><span class="import-status-badge ${row.status}">${importStatusLabel(row.status)}</span>${row.message ? `<small>${escapeImportHtml(row.message)}</small>` : ''}</td>
  </tr>`).join('');

  const limit = document.querySelector('#excelImportPreviewLimit');
  if (limit) limit.textContent = preview.results.length > 300 ? `預覽只顯示前 300 筆；實際驗證共 ${preview.results.length.toLocaleString()} 筆。` : '';

  const commit = document.querySelector('#excelImportCommitButton');
  commit.disabled = !preview.canCommit;
  commit.textContent = preview.canCommit ? `確認匯入 ${summary.ready} 筆` : '確認匯入';
}

async function commitExcelImport() {
  const preview = cyImportState.preview;
  if (!preview?.canCommit) return;
  const ready = preview.summary.ready;
  const duplicates = preview.summary.duplicates;
  if (!confirm(`確定匯入 ${ready} 筆資料嗎？${duplicates ? `\n另有 ${duplicates} 筆重複資料會自動略過。` : ''}`)) return;

  setImportBusy(true);
  setImportMessage('正在寫入 D1…');
  try {
    const data = await api('/api/import/commit', {
      method: 'POST', headers: jsonHeaders(), body: JSON.stringify({ rows: cyImportState.normalizedRows, confirm: true })
    });
    setImportMessage(`${data.message || '匯入完成'}${data.skippedDuplicates ? `　略過重複 ${data.skippedDuplicates} 筆。` : ''}`);
    if (cyImportState.preview) cyImportState.preview.canCommit = false;
    const commitButton = document.querySelector('#excelImportCommitButton');
    if (commitButton) { commitButton.disabled = true; commitButton.textContent = '匯入完成'; }
    await loadTransactions();
    if (typeof scheduleLedgerRefresh === 'function') scheduleLedgerRefresh();
  } catch (error) {
    setImportMessage(error.message || 'Excel 匯入失敗。', true);
  } finally {
    setImportBusy(false);
  }
}

async function uploadXlsx(file, query) {
  const response = await fetch(`/api/import/xlsx/inspect?${query}`, {
    method: 'POST',
    headers: { 'content-type': 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet' },
    body: file,
    cache: 'no-store'
  });
  const data = await response.json().catch(() => null);
  if (!response.ok) throw new Error(data?.error || `Excel 解析失敗（HTTP ${response.status}）。`);
  return data;
}

function resetImportAfterFile() {
  cyImportState.sheets = [];
  cyImportState.rows = [];
  cyImportState.normalizedRows = [];
  cyImportState.localErrors = [];
  cyImportState.preview = null;
  document.querySelector('#excelImportSheetSection')?.classList.add('hidden');
  document.querySelector('#excelImportMappingSection')?.classList.add('hidden');
  resetImportPreview();
  setImportMessage('');
}

function resetImportPreview() {
  cyImportState.preview = null;
  document.querySelector('#excelImportPreviewSection')?.classList.add('hidden');
  const commit = document.querySelector('#excelImportCommitButton');
  if (commit) { commit.disabled = true; commit.textContent = '確認匯入'; }
}

function setImportBusy(busy) {
  document.querySelectorAll('#excelImportDialog button, #excelImportDialog select, #excelImportDialog input').forEach(element => {
    if (element.hasAttribute('data-import-close')) return;
    element.disabled = busy;
  });
  if (!busy) {
    const commit = document.querySelector('#excelImportCommitButton');
    if (commit) commit.disabled = !cyImportState.preview?.canCommit;
  }
}

function setImportMessage(message, error = false) {
  const element = document.querySelector('#excelImportMessage');
  if (!element) return;
  setDialogMessage(element, message || '', error);
}

function importStatusLabel(status) {
  return ({ ready: '可匯入', duplicate: '重複略過', locked: '鎖帳', error: '錯誤' })[status] || status;
}

function importCellLabel(cell) {
  if (cell && typeof cell === 'object' && cell.__cyType === 'date') return String(cell.value || '');
  if (cell === null || cell === undefined) return '';
  return String(cell);
}

function formatImportBytes(bytes) {
  if (bytes < 1024) return `${bytes} B`;
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`;
  return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
}

function escapeImportHtml(value) {
  return String(value ?? '')
    .replaceAll('&', '&amp;')
    .replaceAll('<', '&lt;')
    .replaceAll('>', '&gt;')
    .replaceAll('"', '&quot;')
    .replaceAll("'", '&#39;');
}