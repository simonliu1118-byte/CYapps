const cyV15ImportState = {
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
  setupExcelImportV15();
});

function setupExcelImportV15() {
  const tools = document.querySelector('.ledger-view-tools');
  if (!tools || document.querySelector('#ledgerExcelImport')) return;

  const button = document.createElement('button');
  button.id = 'ledgerExcelImport';
  button.className = 'secondary compact';
  button.type = 'button';
  button.textContent = '匯入 Excel';
  button.title = '匯入 .xlsx 記帳資料';
  tools.prepend(button);

  document.body.insertAdjacentHTML('beforeend', importDialogHtmlV15());
  bindExcelImportDialogV15();
  button.addEventListener('click', openExcelImportV15);
}

function importDialogHtmlV15() {
  return `
  <dialog id="excelImportDialog" class="modal excel-import-modal">
    <div class="modal-header">
      <div><h2>匯入 Excel</h2><p>解析 → 欄位對應 → 預覽驗證 → 確認匯入</p></div>
      <button class="icon-button" type="button" data-v15-close aria-label="關閉">×</button>
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
      <button class="secondary" type="button" data-v15-close>關閉</button>
      <button id="excelImportCommitButton" class="primary" type="button" disabled>確認匯入</button>
    </div>
  </dialog>`;
}

function bindExcelImportDialogV15() {
  const dialog = document.querySelector('#excelImportDialog');
  document.querySelectorAll('[data-v15-close]').forEach(button => button.addEventListener('click', () => dialog?.close()));
  document.querySelector('#excelImportFile')?.addEventListener('change', handleExcelImportFileV15);
  document.querySelector('#excelImportSheet')?.addEventListener('change', loadExcelImportSheetV15);
  document.querySelector('#excelImportHeaderRow')?.addEventListener('change', renderExcelImportMappingV15);
  document.querySelector('#excelImportReloadMapping')?.addEventListener('click', () => {
    const best = detectImportHeaderV15(cyV15ImportState.rows);
    document.querySelector('#excelImportHeaderRow').value = String(best.row + 1);
    renderExcelImportMappingV15(true);
  });
  document.querySelector('#excelImportPreviewButton')?.addEventListener('click', buildExcelImportPreviewV15);
  document.querySelector('#excelImportCommitButton')?.addEventListener('click', commitExcelImportV15);
}

function openExcelImportV15() {
  setImportMessageV15('');
  document.querySelector('#excelImportDialog')?.showModal();
}

async function handleExcelImportFileV15(event) {
  const file = event.target.files?.[0] || null;
  resetImportAfterFileV15();
  cyV15ImportState.file = file;
  if (!file) return;

  if (!/\.xlsx$/i.test(file.name)) return setImportMessageV15('只支援 .xlsx 檔案。', true);
  if (file.size > 8 * 1024 * 1024) return setImportMessageV15('Excel 檔案不可超過 8 MB。', true);

  const status = document.querySelector('#excelImportFileStatus');
  if (status) status.textContent = '讀取工作表中…';
  try {
    const data = await uploadXlsxV15(file, 'mode=workbook');
    cyV15ImportState.sheets = data.sheets || [];
    if (!cyV15ImportState.sheets.length) throw new Error('Excel 檔案沒有可讀取的工作表。');
    renderImportSheetsV15();
    document.querySelector('#excelImportSheetSection')?.classList.remove('hidden');
    if (status) status.textContent = `${file.name}　${formatBytesV15(file.size)}`;
    await loadExcelImportSheetV15();
  } catch (error) {
    if (status) status.textContent = '';
    setImportMessageV15(error.message || 'Excel 解析失敗。', true);
  }
}

function renderImportSheetsV15() {
  const select = document.querySelector('#excelImportSheet');
  if (!select) return;

  let bestIndex = 0;
  let bestScore = -1;
  cyV15ImportState.sheets.forEach((sheet, index) => {
    const score = detectImportHeaderV15(sheet.sample || []).score;
    if (score > bestScore) {
      bestScore = score;
      bestIndex = index;
    }
  });

  select.innerHTML = cyV15ImportState.sheets.map(sheet =>
    `<option value="${sheet.index}">${v15Escape(sheet.name)}（${Number(sheet.rowCount || 0).toLocaleString()} 列）</option>`
  ).join('');
  select.value = String(cyV15ImportState.sheets[bestIndex]?.index || 1);
}

async function loadExcelImportSheetV15() {
  const file = cyV15ImportState.file;
  const select = document.querySelector('#excelImportSheet');
  if (!file || !select?.value) return;

  setImportMessageV15('讀取工作表…');
  setImportBusyV15(true);
  try {
    const data = await uploadXlsxV15(file, `mode=sheet&sheet=${encodeURIComponent(select.value)}`);
    cyV15ImportState.rows = data.rows || [];
    if (!cyV15ImportState.rows.length) throw new Error('這個工作表沒有資料。');

    const best = detectImportHeaderV15(cyV15ImportState.rows);
    cyV15ImportState.headerRow = best.row + 1;
    const headerInput = document.querySelector('#excelImportHeaderRow');
    headerInput.max = String(cyV15ImportState.rows.length);
    headerInput.value = String(cyV15ImportState.headerRow);

    const sheetMeta = cyV15ImportState.sheets.find(item => String(item.index) === String(select.value));
    const info = document.querySelector('#excelImportSheetInfo');
    if (info) info.textContent = `${sheetMeta?.name || ''}，已讀取 ${cyV15ImportState.rows.length.toLocaleString()} 列；自動判斷標題列為第 ${cyV15ImportState.headerRow} 列。`;

    renderExcelImportMappingV15(true);
    document.querySelector('#excelImportMappingSection')?.classList.remove('hidden');
    setImportMessageV15('');
  } catch (error) {
    setImportMessageV15(error.message || '工作表讀取失敗。', true);
  } finally {
    setImportBusyV15(false);
  }
}

function renderExcelImportMappingV15(autoMap = false) {
  const grid = document.querySelector('#excelImportMappingGrid');
  const headerInput = document.querySelector('#excelImportHeaderRow');
  if (!grid || !headerInput || !cyV15ImportState.rows.length) return;

  const headerIndex = Math.max(0, Math.min(cyV15ImportState.rows.length - 1, Number(headerInput.value || 1) - 1));
  cyV15ImportState.headerRow = headerIndex + 1;
  const headers = (cyV15ImportState.rows[headerIndex] || []).map((cell, index) => cellLabelV15(cell) || `欄 ${index + 1}`);
  const suggested = autoMap ? autoMapColumnsV15(headers) : readCurrentMappingV15();

  grid.innerHTML = CY_IMPORT_FIELDS.map(field => {
    const options = [`<option value="">— 不對應 —</option>`].concat(headers.map((header, index) =>
      `<option value="${index}" ${String(suggested[field.key]) === String(index) ? 'selected' : ''}>${index + 1}. ${v15Escape(header)}</option>`
    ));
    return `<label><span>${field.label}</span><select data-import-map="${field.key}">${options.join('')}</select></label>`;
  }).join('');

  resetImportPreviewV15();
}

function readCurrentMappingV15() {
  const mapping = {};
  document.querySelectorAll('[data-import-map]').forEach(select => {
    mapping[select.dataset.importMap] = select.value === '' ? '' : Number(select.value);
  });
  return mapping;
}

function autoMapColumnsV15(headers) {
  const result = {};
  const used = new Set();
  for (const field of CY_IMPORT_FIELDS) {
    let bestIndex = '';
    let bestScore = 0;
    headers.forEach((header, index) => {
      if (used.has(index)) return;
      const score = headerAliasScoreV15(field.key, header);
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

function detectImportHeaderV15(rows) {
  let best = { row: 0, score: -1 };
  rows.slice(0, 30).forEach((row, rowIndex) => {
    const headers = (row || []).map(cellLabelV15);
    const matched = new Set();
    let score = 0;
    for (const header of headers) {
      for (const field of CY_IMPORT_FIELDS) {
        if (matched.has(field.key)) continue;
        const fieldScore = headerAliasScoreV15(field.key, header);
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

function headerAliasScoreV15(field, header) {
  const normalized = normalizeHeaderV15(header);
  if (!normalized) return 0;
  const aliases = CY_IMPORT_ALIASES[field] || [];
  for (const alias of aliases) {
    const target = normalizeHeaderV15(alias);
    if (normalized === target) return 5;
  }
  for (const alias of aliases) {
    const target = normalizeHeaderV15(alias);
    if (target && (normalized.includes(target) || target.includes(normalized))) return 2;
  }
  return 0;
}

function normalizeHeaderV15(value) {
  return String(value ?? '').trim().toLowerCase().replace(/[\s_\-()（）\[\]【】]/g, '');
}

async function buildExcelImportPreviewV15() {
  resetImportPreviewV15();
  const mapping = readCurrentMappingV15();
  const mappingError = validateImportMappingV15(mapping);
  if (mappingError) return setImportMessageV15(mappingError, true);

  const built = normalizeMappedRowsV15(mapping);
  cyV15ImportState.normalizedRows = built.validRows;
  cyV15ImportState.localErrors = built.errors;
  if (!built.validRows.length && !built.errors.length) return setImportMessageV15('標題列下方沒有資料。', true);

  setImportBusyV15(true);
  setImportMessageV15('驗證匯入資料…');
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
    cyV15ImportState.preview = { results: mergedResults, summary, canCommit: summary.ready > 0 && summary.locked === 0 && summary.errors === 0 };
    renderExcelImportPreviewV15();
    setImportMessageV15('');
  } catch (error) {
    setImportMessageV15(error.message || '匯入預覽失敗。', true);
  } finally {
    setImportBusyV15(false);
  }
}

function validateImportMappingV15(mapping) {
  if (mapping.date === '' || mapping.date === undefined) return '請對應「日期」欄位。';
  if (mapping.account === '' || mapping.account === undefined) return '請對應「帳戶」欄位。';
  if (mapping.category === '' || mapping.category === undefined) return '請對應「科目」欄位。';

  const combined = mapping.kind !== '' && mapping.kind !== undefined && mapping.amount !== '' && mapping.amount !== undefined;
  const split = (mapping.incomeAmount !== '' && mapping.incomeAmount !== undefined) || (mapping.expenseAmount !== '' && mapping.expenseAmount !== undefined);
  if (!combined && !split) return '請使用「收支＋金額」合併模式，或至少對應一個「收入金額／支出金額」欄位。';

  const used = new Map();
  for (const [key, value] of Object.entries(mapping)) {
    if (value === '' || value === undefined) continue;
    if (used.has(value)) return `同一來源欄位不可同時對應「${mappingFieldLabelV15(used.get(value))}」與「${mappingFieldLabelV15(key)}」。`;
    used.set(value, key);
  }
  return '';
}

function mappingFieldLabelV15(key) {
  return CY_IMPORT_FIELDS.find(field => field.key === key)?.label.replace(' *', '') || key;
}

function normalizeMappedRowsV15(mapping) {
  const validRows = [];
  const errors = [];
  const startIndex = cyV15ImportState.headerRow;
  const combined = mapping.kind !== '' && mapping.kind !== undefined && mapping.amount !== '' && mapping.amount !== undefined;

  for (let index = startIndex; index < cyV15ImportState.rows.length; index += 1) {
    const source = cyV15ImportState.rows[index] || [];
    if (source.every(cell => cellLabelV15(cell).trim() === '')) continue;
    const sourceRow = index + 1;

    const txDate = normalizeImportDateV15(source[mapping.date]);
    const accountName = cellLabelV15(source[mapping.account]).trim();
    const categoryName = cellLabelV15(source[mapping.category]).trim();
    const summary = mapping.summary === '' || mapping.summary === undefined ? '' : cellLabelV15(source[mapping.summary]).trim();
    let kind = '';
    let amount = NaN;
    let error = '';

    if (combined) {
      kind = normalizeImportKindV15(source[mapping.kind]);
      amount = normalizeImportAmountV15(source[mapping.amount]);
      if (!kind) error = '無法辨識收支類型。';
    } else {
      const income = mapping.incomeAmount === '' || mapping.incomeAmount === undefined ? null : normalizeOptionalAmountV15(source[mapping.incomeAmount]);
      const expense = mapping.expenseAmount === '' || mapping.expenseAmount === undefined ? null : normalizeOptionalAmountV15(source[mapping.expenseAmount]);
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

function normalizeImportDateV15(cell) {
  if (cell && typeof cell === 'object' && cell.__cyType === 'date') return validIsoDateV15(String(cell.value || '')) ? String(cell.value) : '';
  if (typeof cell === 'number' && Number.isFinite(cell)) {
    const digits = String(Math.trunc(cell));
    if (/^\d{8}$/.test(digits)) return buildDateV15(Number(digits.slice(0, 4)), Number(digits.slice(4, 6)), Number(digits.slice(6, 8)));
  }

  const text = String(cell ?? '').trim();
  if (!text) return '';
  if (/^\d{4}-\d{2}-\d{2}T/.test(text)) return validIsoDateV15(text.slice(0, 10)) ? text.slice(0, 10) : '';
  if (/^\d{8}$/.test(text)) return buildDateV15(Number(text.slice(0, 4)), Number(text.slice(4, 6)), Number(text.slice(6, 8)));

  const match = /^(\d{3,4})[\/\.\-](\d{1,2})[\/\.\-](\d{1,2})$/.exec(text);
  if (!match) return '';
  let year = Number(match[1]);
  if (match[1].length === 3) year += 1911;
  return buildDateV15(year, Number(match[2]), Number(match[3]));
}

function buildDateV15(year, month, day) {
  if (!Number.isInteger(year) || year < 1900 || year > 2200 || month < 1 || month > 12 || day < 1 || day > 31) return '';
  const date = new Date(Date.UTC(year, month - 1, day));
  if (date.getUTCFullYear() !== year || date.getUTCMonth() !== month - 1 || date.getUTCDate() !== day) return '';
  return `${year}-${String(month).padStart(2, '0')}-${String(day).padStart(2, '0')}`;
}

function validIsoDateV15(value) {
  return Boolean(/^\d{4}-\d{2}-\d{2}$/.test(value) && buildDateV15(...value.split('-').map(Number)) === value);
}

function normalizeImportKindV15(cell) {
  const text = String(cell ?? '').trim().toLowerCase().replace(/\s+/g, '');
  if (['收入', '收', 'income', 'in', '+'].includes(text)) return 'income';
  if (['支出', '支', 'expense', 'out', '-'].includes(text)) return 'expense';
  return '';
}

function normalizeImportAmountV15(cell) {
  if (typeof cell === 'number') return Number.isFinite(cell) ? cell : NaN;
  const text = String(cell ?? '').trim();
  if (!text) return NaN;
  const cleaned = text.replace(/[,$＄NTnt元\s]/g, '');
  const value = Number(cleaned);
  return Number.isFinite(value) ? value : NaN;
}

function normalizeOptionalAmountV15(cell) {
  if (cell === null || cell === undefined || String(cell).trim() === '') return { value: 0, error: false };
  const value = normalizeImportAmountV15(cell);
  if (!Number.isFinite(value) || value < 0) return { value: 0, error: true };
  return { value, error: false };
}

function renderExcelImportPreviewV15() {
  const preview = cyV15ImportState.preview;
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
    <td>${v15Escape(row.txDate || '')}</td>
    <td>${v15Escape(row.accountName || '')}</td>
    <td>${row.kind === 'income' ? '收入' : row.kind === 'expense' ? '支出' : '—'}</td>
    <td>${v15Escape(row.categoryName || '')}</td>
    <td class="summary">${v15Escape(row.summary || '')}</td>
    <td class="num">${Number(row.amount || 0).toLocaleString()}</td>
    <td><span class="import-status-badge ${row.status}">${importStatusLabelV15(row.status)}</span>${row.message ? `<small>${v15Escape(row.message)}</small>` : ''}</td>
  </tr>`).join('');

  const limit = document.querySelector('#excelImportPreviewLimit');
  if (limit) limit.textContent = preview.results.length > 300 ? `預覽只顯示前 300 筆；實際驗證共 ${preview.results.length.toLocaleString()} 筆。` : '';

  const commit = document.querySelector('#excelImportCommitButton');
  commit.disabled = !preview.canCommit;
  commit.textContent = preview.canCommit ? `確認匯入 ${summary.ready} 筆` : '確認匯入';
}

async function commitExcelImportV15() {
  const preview = cyV15ImportState.preview;
  if (!preview?.canCommit) return;
  const ready = preview.summary.ready;
  const duplicates = preview.summary.duplicates;
  if (!confirm(`確定匯入 ${ready} 筆資料嗎？${duplicates ? `\n另有 ${duplicates} 筆重複資料會自動略過。` : ''}`)) return;

  setImportBusyV15(true);
  setImportMessageV15('正在寫入 D1…');
  try {
    const data = await api('/api/import/commit', {
      method: 'POST', headers: jsonHeaders(), body: JSON.stringify({ rows: cyV15ImportState.normalizedRows, confirm: true })
    });
    setImportMessageV15(`${data.message || '匯入完成'}${data.skippedDuplicates ? `　略過重複 ${data.skippedDuplicates} 筆。` : ''}`);
    document.querySelector('#excelImportCommitButton').disabled = true;
    await loadTransactions();
    if (typeof scheduleLedgerDesktopRefresh === 'function') scheduleLedgerDesktopRefresh();
  } catch (error) {
    setImportMessageV15(error.message || 'Excel 匯入失敗。', true);
  } finally {
    setImportBusyV15(false);
  }
}

async function uploadXlsxV15(file, query) {
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

function resetImportAfterFileV15() {
  cyV15ImportState.sheets = [];
  cyV15ImportState.rows = [];
  cyV15ImportState.normalizedRows = [];
  cyV15ImportState.localErrors = [];
  cyV15ImportState.preview = null;
  document.querySelector('#excelImportSheetSection')?.classList.add('hidden');
  document.querySelector('#excelImportMappingSection')?.classList.add('hidden');
  resetImportPreviewV15();
  setImportMessageV15('');
}

function resetImportPreviewV15() {
  cyV15ImportState.preview = null;
  document.querySelector('#excelImportPreviewSection')?.classList.add('hidden');
  const commit = document.querySelector('#excelImportCommitButton');
  if (commit) { commit.disabled = true; commit.textContent = '確認匯入'; }
}

function setImportBusyV15(busy) {
  document.querySelectorAll('#excelImportDialog button, #excelImportDialog select, #excelImportDialog input').forEach(element => {
    if (element.hasAttribute('data-v15-close')) return;
    element.disabled = busy;
  });
}

function setImportMessageV15(message, error = false) {
  const element = document.querySelector('#excelImportMessage');
  if (!element) return;
  setDialogMessage(element, message || '', error);
}

function importStatusLabelV15(status) {
  return ({ ready: '可匯入', duplicate: '重複略過', locked: '鎖帳', error: '錯誤' })[status] || status;
}

function cellLabelV15(cell) {
  if (cell && typeof cell === 'object' && cell.__cyType === 'date') return String(cell.value || '');
  if (cell === null || cell === undefined) return '';
  return String(cell);
}

function formatBytesV15(bytes) {
  if (bytes < 1024) return `${bytes} B`;
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`;
  return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
}

function v15Escape(value) {
  return String(value ?? '')
    .replaceAll('&', '&amp;')
    .replaceAll('<', '&lt;')
    .replaceAll('>', '&gt;')
    .replaceAll('"', '&quot;')
    .replaceAll("'", '&#39;');
}
