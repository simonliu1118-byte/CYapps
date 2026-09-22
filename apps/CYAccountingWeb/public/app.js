const state = { kind: 'expense', accounts: [], categories: [], transactions: [] };
const els = {};

document.addEventListener('DOMContentLoaded', async () => {
  Object.assign(els, {
    form: document.querySelector('#transactionForm'), txDate: document.querySelector('#txDate'), accountName: document.querySelector('#accountName'), categoryName: document.querySelector('#categoryName'), summary: document.querySelector('#summary'), amount: document.querySelector('#amount'), saveButton: document.querySelector('#saveButton'), saveMessage: document.querySelector('#saveMessage'), monthFilter: document.querySelector('#monthFilter'), monthSummary: document.querySelector('#monthSummary'), transactionRows: document.querySelector('#transactionRows'), connectionStatus: document.querySelector('#connectionStatus'), kindButtons: [...document.querySelectorAll('.kind-button')]
  });
  const today = localDateString(new Date());
  els.txDate.value = today;
  els.monthFilter.value = today.slice(0, 7);
  bindEvents();
  await initialize();
});

function bindEvents() {
  els.kindButtons.forEach(button => button.addEventListener('click', () => {
    state.kind = button.dataset.kind;
    els.kindButtons.forEach(b => b.classList.toggle('active', b === button));
    renderCategories();
  }));
  els.amount.addEventListener('input', () => { els.amount.value = els.amount.value.replace(/\D/g, '').slice(0, 7); });
  els.form.addEventListener('submit', saveTransaction);
  els.monthFilter.addEventListener('change', loadTransactions);
  els.transactionRows.addEventListener('click', async event => {
    const button = event.target.closest('[data-delete-id]');
    if (!button) return;
    const id = Number(button.dataset.deleteId);
    if (!Number.isInteger(id) || !confirm('確定刪除這筆記帳嗎？')) return;
    await deleteTransaction(id);
  });
}

async function initialize() {
  try {
    const health = await api('/api/health');
    if (!health.database) {
      setConnection('網站已啟動，等待 D1 設定', 'warn');
      els.transactionRows.innerHTML = '<tr><td colspan="7" class="empty">D1 尚未綁定，完成 Cloudflare 設定後即可開始記帳。</td></tr>';
      els.saveButton.disabled = true;
      return;
    }
    const bootstrap = await api('/api/bootstrap');
    state.accounts = bootstrap.accounts || [];
    state.categories = bootstrap.categories || [];
    renderAccounts(); renderCategories(); setConnection('已連線', 'ok');
    await loadTransactions();
  } catch (error) { setConnection('連線失敗', 'warn'); showMessage(error.message, true); }
}

function renderAccounts() {
  els.accountName.innerHTML = state.accounts.map(account => `<option value="${escapeHtml(account.name)}">${escapeHtml(account.name)}</option>`).join('');
  const defaultAccount = state.accounts.find(account => Number(account.is_default) === 1);
  if (defaultAccount) els.accountName.value = defaultAccount.name;
}
function renderCategories() {
  const categories = state.categories.filter(category => category.kind === state.kind);
  els.categoryName.innerHTML = categories.map(category => `<option value="${escapeHtml(category.name)}">${escapeHtml(category.name)}</option>`).join('');
}

async function saveTransaction(event) {
  event.preventDefault(); showMessage('');
  const amount = Number(els.amount.value);
  if (!Number.isInteger(amount) || amount < 1 || amount > 9_999_999) { showMessage('金額必須為 1～9,999,999。', true); els.amount.focus(); return; }
  els.saveButton.disabled = true;
  try {
    await api('/api/transactions', { method: 'POST', headers: { 'content-type': 'application/json' }, body: JSON.stringify({ txDate: els.txDate.value, accountName: els.accountName.value, kind: state.kind, categoryName: els.categoryName.value, summary: els.summary.value.trim(), amount }) });
    showMessage('存檔成功'); els.summary.value = ''; els.amount.value = ''; els.amount.focus(); els.monthFilter.value = els.txDate.value.slice(0, 7); await loadTransactions();
  } catch (error) { showMessage(error.message, true); } finally { els.saveButton.disabled = false; }
}

async function loadTransactions() {
  if (!els.monthFilter.value) return;
  try { const data = await api(`/api/transactions?month=${encodeURIComponent(els.monthFilter.value)}`); state.transactions = data.transactions || []; renderTransactions(); }
  catch (error) { els.transactionRows.innerHTML = `<tr><td colspan="7" class="empty">${escapeHtml(error.message)}</td></tr>`; }
}

function renderTransactions() {
  let income = 0, expense = 0;
  for (const tx of state.transactions) { if (tx.kind === 'income') income += Number(tx.amount) || 0; else expense += Number(tx.amount) || 0; }
  els.monthSummary.textContent = `收入 ${money(income)}　支出 ${money(expense)}　收支 ${money(income - expense)}`;
  if (!state.transactions.length) { els.transactionRows.innerHTML = '<tr><td colspan="7" class="empty">本月尚無記帳資料。</td></tr>'; return; }
  els.transactionRows.innerHTML = state.transactions.map(tx => `<tr><td>${escapeHtml(tx.tx_date.replaceAll('-', '/'))}</td><td>${escapeHtml(tx.account_name)}</td><td><span class="kind-tag ${tx.kind}">${tx.kind === 'income' ? '收入' : '支出'}</span></td><td>${escapeHtml(tx.category_name)}</td><td class="summary">${escapeHtml(tx.summary || '')}</td><td class="num">${money(tx.amount)}</td><td class="action-col"><button type="button" class="delete-button" data-delete-id="${tx.id}">刪除</button></td></tr>`).join('');
}

async function deleteTransaction(id) { try { await api(`/api/transactions/${id}`, { method: 'DELETE' }); showMessage('已刪除'); await loadTransactions(); } catch (error) { showMessage(error.message, true); } }
async function api(path, options) { const response = await fetch(path, options); const data = await response.json().catch(() => ({})); if (!response.ok || data.ok === false) throw new Error(data.error || `HTTP ${response.status}`); return data; }
function setConnection(text, type) { els.connectionStatus.textContent = text; els.connectionStatus.className = `status ${type || ''}`.trim(); }
function showMessage(text, isError = false) { els.saveMessage.textContent = text; els.saveMessage.classList.toggle('error', isError); }
function money(value) { return `$${Number(value || 0).toLocaleString('zh-TW')}`; }
function localDateString(date) { const y = date.getFullYear(), m = String(date.getMonth() + 1).padStart(2, '0'), d = String(date.getDate()).padStart(2, '0'); return `${y}-${m}-${d}`; }
function escapeHtml(value) { return String(value ?? '').replaceAll('&', '&amp;').replaceAll('<', '&lt;').replaceAll('>', '&gt;').replaceAll('"', '&quot;').replaceAll("'", '&#039;'); }
