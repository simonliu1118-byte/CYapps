const state = {
  kind: 'expense',
  accounts: [],
  groups: [],
  categories: [],
  transactions: [],
  lockedThrough: null,
  ledgerLocked: false,
  editingTx: null,
  settingsKind: 'expense',
  activeSettingsTab: 'accounts',
  openingData: null
};
const els = {};

document.addEventListener('DOMContentLoaded', async () => {
  Object.assign(els, {
    form: document.querySelector('#transactionForm'),
    txDate: document.querySelector('#txDate'),
    accountName: document.querySelector('#accountName'),
    categoryName: document.querySelector('#categoryName'),
    summary: document.querySelector('#summary'),
    amount: document.querySelector('#amount'),
    saveButton: document.querySelector('#saveButton'),
    saveMessage: document.querySelector('#saveMessage'),
    monthFilter: document.querySelector('#monthFilter'),
    monthSummary: document.querySelector('#monthSummary'),
    transactionRows: document.querySelector('#transactionRows'),
    connectionStatus: document.querySelector('#connectionStatus'),
    kindButtons: [...document.querySelectorAll('.kind-button[data-kind]')],
    entryLockBadge: document.querySelector('#entryLockBadge'),
    ledgerLockBadge: document.querySelector('#ledgerLockBadge'),
    settingsButton: document.querySelector('#settingsButton'),
    settingsDialog: document.querySelector('#settingsDialog'),
    settingsTabs: [...document.querySelectorAll('[data-settings-tab]')],
    settingsPanes: [...document.querySelectorAll('[data-settings-pane]')],
    settingsKindButtons: [...document.querySelectorAll('[data-settings-kind]')],
    settingsMessage: document.querySelector('#settingsMessage'),
    accountRows: document.querySelector('#accountRows'),
    newAccountName: document.querySelector('#newAccountName'),
    addAccountButton: document.querySelector('#addAccountButton'),
    categoryManager: document.querySelector('#categoryManager'),
    newGroupName: document.querySelector('#newGroupName'),
    addGroupButton: document.querySelector('#addGroupButton'),
    openingMonth: document.querySelector('#openingMonth'),
    openingRows: document.querySelector('#openingRows'),
    saveOpeningButton: document.querySelector('#saveOpeningButton'),
    openingDialog: document.querySelector('#openingDialog'),
    openingMessage: document.querySelector('#openingMessage'),
    lockedThrough: document.querySelector('#lockedThrough'),
    saveLockButton: document.querySelector('#saveLockButton'),
    clearLockButton: document.querySelector('#clearLockButton'),
    lockStatusText: document.querySelector('#lockStatusText'),
    editDialog: document.querySelector('#editDialog'),
    editForm: document.querySelector('#editTransactionForm'),
    editKindLabel: document.querySelector('#editKindLabel'),
    editDate: document.querySelector('#editDate'),
    editAccount: document.querySelector('#editAccount'),
    editCategory: document.querySelector('#editCategory'),
    editSummary: document.querySelector('#editSummary'),
    editAmount: document.querySelector('#editAmount'),
    editMessage: document.querySelector('#editMessage'),
    editSaveButton: document.querySelector('#editSaveButton')
  });

  const today = localDateString(new Date());
  els.txDate.value = today;
  els.monthFilter.value = today.slice(0, 7);
  els.openingMonth.value = today.slice(0, 7);
  bindEvents();
  await initialize();
});

function bindEvents() {
  els.kindButtons.forEach(button => button.addEventListener('click', () => setEntryKind(button.dataset.kind)));
  els.amount.addEventListener('input', numericInput);
  els.editAmount.addEventListener('input', numericInput);
  els.txDate.addEventListener('change', updateEntryLockState);
  els.form.addEventListener('submit', saveTransaction);
  els.monthFilter.addEventListener('change', loadTransactions);

  els.transactionRows.addEventListener('click', async event => {
    const edit = event.target.closest('[data-edit-id]');
    if (edit) return openEditTransaction(Number(edit.dataset.editId));
    const del = event.target.closest('[data-delete-id]');
    if (del) {
      const id = Number(del.dataset.deleteId);
      if (Number.isInteger(id) && confirm('確定刪除這筆記帳嗎？')) await deleteTransaction(id);
    }
  });

  els.editForm.addEventListener('submit', saveTransactionEdit);
  els.settingsButton.addEventListener('click', openSettings);
  document.addEventListener('click', event => {
    const close = event.target.closest('[data-close-dialog]');
    if (!close) return;
    document.querySelector(`#${CSS.escape(close.dataset.closeDialog)}`)?.close();
  });

  els.settingsTabs.forEach(button => button.addEventListener('click', () => setSettingsTab(button.dataset.settingsTab)));
  els.settingsKindButtons.forEach(button => button.addEventListener('click', () => {
    state.settingsKind = button.dataset.settingsKind;
    renderCategoryManager();
  }));

  els.addAccountButton.addEventListener('click', addAccount);
  els.newAccountName.addEventListener('keydown', event => { if (event.key === 'Enter') { event.preventDefault(); addAccount(); } });
  els.accountRows.addEventListener('click', handleAccountAction);

  els.addGroupButton.addEventListener('click', addGroup);
  els.newGroupName.addEventListener('keydown', event => { if (event.key === 'Enter') { event.preventDefault(); addGroup(); } });
  els.categoryManager.addEventListener('click', handleCategoryAction);
  els.categoryManager.addEventListener('keydown', event => {
    if (event.key !== 'Enter') return;
    const input = event.target.closest('[data-new-category-group]');
    if (!input) return;
    event.preventDefault();
    addCategory(Number(input.dataset.newCategoryGroup), input);
  });

  els.openingMonth.addEventListener('change', loadOpeningBalances);
  els.saveOpeningButton.addEventListener('click', saveOpeningBalances);
  els.saveLockButton.addEventListener('click', () => saveLock(els.lockedThrough.value));
  els.clearLockButton.addEventListener('click', () => saveLock(''));
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
    await refreshBootstrap();
    setConnection('已連線', 'ok');
    await loadTransactions();
  } catch (error) {
    setConnection('連線失敗', 'warn');
    showMessage(error.message, true);
  }
}

async function refreshBootstrap() {
  const bootstrap = await api('/api/bootstrap');
  const accountValue = els.accountName.value;
  const categoryValue = els.categoryName.value;
  state.accounts = bootstrap.accounts || [];
  state.groups = bootstrap.groups || [];
  state.categories = bootstrap.categories || [];
  state.lockedThrough = bootstrap.lockedThrough || null;
  renderAccounts(accountValue);
  renderCategories(categoryValue);
  renderSettings();
  updateEntryLockState();
}

function renderAccounts(preferred) {
  els.accountName.innerHTML = state.accounts.map(account => `<option value="${escapeHtml(account.name)}">${escapeHtml(account.name)}</option>`).join('');
  if (preferred && state.accounts.some(a => a.name === preferred)) els.accountName.value = preferred;
  else {
    const defaultAccount = state.accounts.find(account => Number(account.is_default) === 1) || state.accounts[0];
    if (defaultAccount) els.accountName.value = defaultAccount.name;
  }
}

function renderCategories(preferred) {
  const categories = state.categories.filter(category => category.kind === state.kind);
  els.categoryName.innerHTML = categories.map(category => `<option value="${escapeHtml(category.name)}">${escapeHtml(category.name)}</option>`).join('');
  if (preferred && categories.some(c => c.name === preferred)) els.categoryName.value = preferred;
}

function setEntryKind(kind) {
  if (!['income', 'expense'].includes(kind)) return;
  state.kind = kind;
  els.kindButtons.forEach(button => button.classList.toggle('active', button.dataset.kind === kind));
  renderCategories();
}

function updateEntryLockState() {
  const month = String(els.txDate.value || '').slice(0, 7);
  const locked = isLocked(month);
  els.entryLockBadge.classList.toggle('hidden', !locked);
  els.saveButton.disabled = locked;
}

async function saveTransaction(event) {
  event.preventDefault();
  showMessage('');
  if (isLocked(els.txDate.value.slice(0, 7))) return showMessage('此月份已鎖帳，無法新增資料。', true);
  const amount = Number(els.amount.value);
  if (!Number.isInteger(amount) || amount < 1 || amount > 9_999_999) {
    showMessage('金額必須為 1～9,999,999。', true);
    els.amount.focus();
    return;
  }
  els.saveButton.disabled = true;
  try {
    await api('/api/transactions', {
      method: 'POST', headers: jsonHeaders(),
      body: JSON.stringify({ txDate: els.txDate.value, accountName: els.accountName.value, kind: state.kind, categoryName: els.categoryName.value, summary: els.summary.value.trim(), amount })
    });
    showMessage('存檔成功');
    els.summary.value = '';
    els.amount.value = '';
    els.monthFilter.value = els.txDate.value.slice(0, 7);
    await loadTransactions();
    els.amount.focus();
  } catch (error) {
    showMessage(error.message, true);
  } finally {
    updateEntryLockState();
  }
}

async function loadTransactions() {
  if (!els.monthFilter.value) return;
  try {
    const data = await api(`/api/transactions?month=${encodeURIComponent(els.monthFilter.value)}`);
    state.transactions = data.transactions || [];
    state.ledgerLocked = Boolean(data.locked);
    state.lockedThrough = data.lockedThrough || state.lockedThrough;
    renderTransactions();
    updateEntryLockState();
  } catch (error) {
    els.transactionRows.innerHTML = `<tr><td colspan="7" class="empty">${escapeHtml(error.message)}</td></tr>`;
  }
}

function renderTransactions() {
  let income = 0, expense = 0;
  for (const tx of state.transactions) {
    if (tx.kind === 'income') income += Number(tx.amount) || 0;
    else expense += Number(tx.amount) || 0;
  }
  els.monthSummary.textContent = `收入 ${money(income)}　支出 ${money(expense)}　收支 ${money(income - expense)}`;
  els.ledgerLockBadge.classList.toggle('hidden', !state.ledgerLocked);
  if (!state.transactions.length) {
    els.transactionRows.innerHTML = '<tr><td colspan="7" class="empty">本月尚無記帳資料。</td></tr>';
    return;
  }
  els.transactionRows.innerHTML = state.transactions.map(tx => {
    const locked = isLocked(tx.tx_date.slice(0, 7));
    return `<tr>
      <td>${escapeHtml(tx.tx_date.replaceAll('-', '/'))}</td>
      <td>${escapeHtml(tx.account_name)}</td>
      <td><span class="kind-tag ${tx.kind}">${tx.kind === 'income' ? '收入' : '支出'}</span></td>
      <td>${escapeHtml(tx.category_name)}</td>
      <td class="summary">${escapeHtml(tx.summary || '')}</td>
      <td class="num">${money(tx.amount)}</td>
      <td class="action-col"><button type="button" class="row-action" data-edit-id="${tx.id}" ${locked ? 'disabled' : ''}>編輯</button><button type="button" class="row-action delete" data-delete-id="${tx.id}" ${locked ? 'disabled' : ''}>刪除</button></td>
    </tr>`;
  }).join('');
}

function openEditTransaction(id) {
  const tx = state.transactions.find(item => Number(item.id) === id);
  if (!tx || isLocked(tx.tx_date.slice(0, 7))) return;
  state.editingTx = tx;
  els.editKindLabel.textContent = tx.kind === 'income' ? '收入' : '支出';
  els.editDate.value = tx.tx_date;
  els.editSummary.value = tx.summary || '';
  els.editAmount.value = String(tx.amount);
  els.editAccount.innerHTML = optionsWithHistorical(state.accounts.map(a => a.name), tx.account_name);
  els.editAccount.value = tx.account_name;
  const categories = state.categories.filter(c => c.kind === tx.kind).map(c => c.name);
  els.editCategory.innerHTML = optionsWithHistorical(categories, tx.category_name);
  els.editCategory.value = tx.category_name;
  setDialogMessage(els.editMessage, '');
  els.editDialog.showModal();
}

async function saveTransactionEdit(event) {
  event.preventDefault();
  if (!state.editingTx) return;
  const amount = Number(els.editAmount.value);
  if (!Number.isInteger(amount) || amount < 1 || amount > 9_999_999) return setDialogMessage(els.editMessage, '金額必須為 1～9,999,999。', true);
  els.editSaveButton.disabled = true;
  try {
    await api(`/api/transactions/${state.editingTx.id}`, {
      method: 'PUT', headers: jsonHeaders(),
      body: JSON.stringify({ txDate: els.editDate.value, accountName: els.editAccount.value, categoryName: els.editCategory.value, summary: els.editSummary.value.trim(), amount })
    });
    els.editDialog.close();
    showMessage('修改成功');
    await loadTransactions();
  } catch (error) {
    setDialogMessage(els.editMessage, error.message, true);
  } finally {
    els.editSaveButton.disabled = false;
  }
}

async function deleteTransaction(id) {
  try {
    await api(`/api/transactions/${id}`, { method: 'DELETE' });
    showMessage('已刪除');
    await loadTransactions();
  } catch (error) {
    showMessage(error.message, true);
  }
}

function openSettings() {
  setDialogMessage(els.settingsMessage, '');
  renderSettings();
  els.settingsDialog.showModal();
}

function setSettingsTab(tab) {
  state.activeSettingsTab = tab;
  els.settingsTabs.forEach(button => button.classList.toggle('active', button.dataset.settingsTab === tab));
  els.settingsPanes.forEach(pane => pane.classList.toggle('active', pane.dataset.settingsPane === tab));
  setDialogMessage(els.settingsMessage, '');
}

function renderSettings() {
  renderAccountManager();
  renderCategoryManager();
  els.lockedThrough.value = state.lockedThrough || '';
  els.lockStatusText.textContent = state.lockedThrough ? `目前已鎖帳至 ${formatMonth(state.lockedThrough)}，更早月份也一併鎖定。` : '目前未鎖帳。';
}

function renderAccountManager() {
  if (!state.accounts.length) {
    els.accountRows.innerHTML = '<div class="empty">尚無帳戶。</div>';
    return;
  }
  els.accountRows.innerHTML = state.accounts.map(account => `<div class="manager-row">
    <div class="manager-row-main"><strong>${escapeHtml(account.name)}</strong>${Number(account.is_default) === 1 ? '<span class="badge">預設</span>' : ''}</div>
    <div class="manager-row-actions">
      ${Number(account.is_default) === 1 ? '' : `<button type="button" class="mini-button" data-account-default="${account.id}">設為預設</button>`}
      <button type="button" class="mini-button" data-account-rename="${account.id}">改名</button>
      <button type="button" class="mini-button danger" data-account-delete="${account.id}">刪除</button>
    </div>
  </div>`).join('');
}

async function handleAccountAction(event) {
  const defaultButton = event.target.closest('[data-account-default]');
  if (defaultButton) return mutateSettings(`/api/accounts/${defaultButton.dataset.accountDefault}/default`, { method: 'POST' }, '已更新預設帳戶。');
  const renameButton = event.target.closest('[data-account-rename]');
  if (renameButton) {
    const account = state.accounts.find(a => String(a.id) === renameButton.dataset.accountRename);
    const name = prompt('新的帳戶名稱：', account?.name || '');
    if (name === null) return;
    return mutateSettings(`/api/accounts/${renameButton.dataset.accountRename}`, { method: 'PUT', headers: jsonHeaders(), body: JSON.stringify({ name }) }, '帳戶名稱已更新。');
  }
  const deleteButton = event.target.closest('[data-account-delete]');
  if (deleteButton) {
    const account = state.accounts.find(a => String(a.id) === deleteButton.dataset.accountDelete);
    if (!confirm(`確定刪除帳戶「${account?.name || ''}」？\n既有歷史記帳仍會保留原帳戶名稱。`)) return;
    return mutateSettings(`/api/accounts/${deleteButton.dataset.accountDelete}`, { method: 'DELETE' }, '帳戶已刪除。');
  }
}

async function addAccount() {
  const name = els.newAccountName.value.trim();
  if (!name) return;
  const ok = await mutateSettings('/api/accounts', { method: 'POST', headers: jsonHeaders(), body: JSON.stringify({ name }) }, '帳戶已新增。');
  if (ok) els.newAccountName.value = '';
}

function renderCategoryManager() {
  els.settingsKindButtons.forEach(button => button.classList.toggle('active', button.dataset.settingsKind === state.settingsKind));
  const groups = state.groups.filter(group => group.kind === state.settingsKind);
  if (!groups.length) {
    els.categoryManager.innerHTML = '<div class="empty">目前沒有大分類，請先新增。</div>';
    return;
  }
  els.categoryManager.innerHTML = groups.map(group => {
    const categories = state.categories.filter(category => Number(category.group_id) === Number(group.id));
    const categoryHtml = categories.length ? categories.map(category => `<div class="category-item"><span>${escapeHtml(category.name)}</span><span><button type="button" class="mini-button" data-category-rename="${category.id}">改名</button> <button type="button" class="mini-button danger" data-category-delete="${category.id}">刪除</button></span></div>`).join('') : '<div class="hint">此分類尚無科目。</div>';
    return `<div class="category-group" data-group-id="${group.id}">
      <div class="category-group-head"><span class="category-group-title">${escapeHtml(group.name)}</span><span><button type="button" class="mini-button" data-group-rename="${group.id}">改名</button> <button type="button" class="mini-button danger" data-group-delete="${group.id}">刪除</button></span></div>
      <div class="category-items">${categoryHtml}<div class="category-add"><input type="text" maxlength="60" placeholder="新增科目" data-new-category-group="${group.id}"><button type="button" class="mini-button" data-category-add="${group.id}">新增</button></div></div>
    </div>`;
  }).join('');
}

async function handleCategoryAction(event) {
  const add = event.target.closest('[data-category-add]');
  if (add) {
    const group = add.closest('.category-group');
    const input = group?.querySelector('[data-new-category-group]');
    return addCategory(Number(add.dataset.categoryAdd), input);
  }
  const renameCategory = event.target.closest('[data-category-rename]');
  if (renameCategory) {
    const item = state.categories.find(c => String(c.id) === renameCategory.dataset.categoryRename);
    const name = prompt('新的科目名稱：', item?.name || '');
    if (name === null) return;
    return mutateSettings(`/api/categories/${renameCategory.dataset.categoryRename}`, { method: 'PUT', headers: jsonHeaders(), body: JSON.stringify({ name }) }, '科目名稱已更新。');
  }
  const deleteCategory = event.target.closest('[data-category-delete]');
  if (deleteCategory) {
    const item = state.categories.find(c => String(c.id) === deleteCategory.dataset.categoryDelete);
    if (!confirm(`確定刪除科目「${item?.name || ''}」？\n既有歷史記帳仍會保留原科目名稱。`)) return;
    return mutateSettings(`/api/categories/${deleteCategory.dataset.categoryDelete}`, { method: 'DELETE' }, '科目已刪除。');
  }
  const renameGroup = event.target.closest('[data-group-rename]');
  if (renameGroup) {
    const item = state.groups.find(g => String(g.id) === renameGroup.dataset.groupRename);
    const name = prompt('新的大分類名稱：', item?.name || '');
    if (name === null) return;
    return mutateSettings(`/api/category-groups/${renameGroup.dataset.groupRename}`, { method: 'PUT', headers: jsonHeaders(), body: JSON.stringify({ name }) }, '大分類名稱已更新。');
  }
  const deleteGroup = event.target.closest('[data-group-delete]');
  if (deleteGroup) {
    const item = state.groups.find(g => String(g.id) === deleteGroup.dataset.groupDelete);
    if (!confirm(`確定刪除大分類「${item?.name || ''}」？`)) return;
    return mutateSettings(`/api/category-groups/${deleteGroup.dataset.groupDelete}`, { method: 'DELETE' }, '大分類已刪除。');
  }
}

async function addGroup() {
  const name = els.newGroupName.value.trim();
  if (!name) return;
  const ok = await mutateSettings('/api/category-groups', { method: 'POST', headers: jsonHeaders(), body: JSON.stringify({ kind: state.settingsKind, name }) }, '大分類已新增。');
  if (ok) els.newGroupName.value = '';
}

async function addCategory(groupId, input) {
  const name = input?.value.trim();
  if (!name || !Number.isInteger(groupId) || groupId <= 0) return;
  const ok = await mutateSettings('/api/categories', { method: 'POST', headers: jsonHeaders(), body: JSON.stringify({ kind: state.settingsKind, groupId, name }) }, '科目已新增。');
  if (ok && input) input.value = '';
}

async function mutateSettings(path, options, successMessage) {
  setDialogMessage(els.settingsMessage, '');
  try {
    await api(path, options);
    await refreshBootstrap();
    setDialogMessage(els.settingsMessage, successMessage);
    await loadTransactions();
    return true;
  } catch (error) {
    setDialogMessage(els.settingsMessage, error.message, true);
    return false;
  }
}

async function loadOpeningBalances() {
  const month = els.openingMonth.value;
  if (!month) return;
  setDialogMessage(els.openingMessage, '');
  els.openingRows.innerHTML = '<div class="empty">載入中…</div>';
  try {
    const data = await api(`/api/opening-balances?month=${encodeURIComponent(month)}`);
    state.openingData = data;
    if (!data.accounts?.length) {
      els.openingRows.innerHTML = '<div class="empty">沒有可設定的帳戶。</div>';
      els.saveOpeningButton.disabled = true;
      return;
    }
    els.openingRows.innerHTML = data.accounts.map(item => `<label class="opening-row"><span>${escapeHtml(item.name)}${item.isCurrent ? '' : '<span class="historical">歷史帳戶</span>'}</span><input type="number" step="1" value="${item.amount ?? ''}" data-opening-account="${escapeHtml(item.name)}" ${data.locked ? 'disabled' : ''}></label>`).join('');
    els.saveOpeningButton.disabled = Boolean(data.locked);
    if (data.locked) setDialogMessage(els.openingMessage, `${month} 已鎖帳，期初餘額僅供檢視。`, true);
  } catch (error) {
    els.openingRows.innerHTML = `<div class="empty">${escapeHtml(error.message)}</div>`;
  }
}

async function saveOpeningBalances() {
  if (!els.openingMonth.value) return;
  const values = {};
  for (const input of els.openingRows.querySelectorAll('[data-opening-account]')) {
    const raw = input.value.trim();
    if (raw !== '' && !Number.isSafeInteger(Number(raw))) return setDialogMessage(els.openingMessage, `${input.dataset.openingAccount} 的期初餘額必須是整數。`, true);
    values[input.dataset.openingAccount] = raw === '' ? null : Number(raw);
  }
  els.saveOpeningButton.disabled = true;
  try {
    await api('/api/opening-balances', { method: 'PUT', headers: jsonHeaders(), body: JSON.stringify({ month: els.openingMonth.value, values }) });
    setDialogMessage(els.openingMessage, '期初餘額已儲存。');
    await loadOpeningBalances();
    if (typeof scheduleLedgerDesktopRefresh === 'function') scheduleLedgerDesktopRefresh();
  } catch (error) {
    setDialogMessage(els.openingMessage, error.message, true);
  } finally {
    els.saveOpeningButton.disabled = Boolean(state.openingData?.locked);
  }
}

async function saveLock(lockedThrough) {
  try {
    const data = await api('/api/settings/lock', { method: 'PUT', headers: jsonHeaders(), body: JSON.stringify({ lockedThrough }) });
    state.lockedThrough = data.lockedThrough || null;
    els.lockedThrough.value = state.lockedThrough || '';
    renderSettings();
    setDialogMessage(els.settingsMessage, state.lockedThrough ? `已鎖帳至 ${formatMonth(state.lockedThrough)}。` : '已取消鎖帳。');
    await loadTransactions();
  } catch (error) {
    setDialogMessage(els.settingsMessage, error.message, true);
  }
}

function optionsWithHistorical(currentNames, selected) {
  const names = [...currentNames];
  if (selected && !names.includes(selected)) names.unshift(selected);
  return names.map(name => `<option value="${escapeHtml(name)}">${escapeHtml(name)}${currentNames.includes(name) ? '' : '（歷史）'}</option>`).join('');
}

function isLocked(month) {
  return Boolean(state.lockedThrough && /^\d{4}-\d{2}$/.test(month) && month <= state.lockedThrough);
}

function numericInput(event) {
  event.target.value = event.target.value.replace(/\D/g, '').slice(0, 7);
}

async function api(path, options) {
  const response = await fetch(path, options);
  const data = await response.json().catch(() => ({}));
  if (!response.ok || data.ok === false) throw new Error(data.error || `HTTP ${response.status}`);
  return data;
}

function jsonHeaders() { return { 'content-type': 'application/json' }; }
function setConnection(text, type) { els.connectionStatus.textContent = text; els.connectionStatus.className = `status ${type || ''}`.trim(); }
function showMessage(text, isError = false) { els.saveMessage.textContent = text; els.saveMessage.classList.toggle('error', isError); }
function setDialogMessage(element, text, isError = false) { element.textContent = text; element.classList.toggle('error', isError); }
function money(value) { return `$${Number(value || 0).toLocaleString('zh-TW')}`; }
function formatMonth(value) { const [year, month] = String(value || '').split('-'); return year && month ? `${year}年${month}月` : ''; }
function localDateString(date) { const y = date.getFullYear(), m = String(date.getMonth() + 1).padStart(2, '0'), d = String(date.getDate()).padStart(2, '0'); return `${y}-${m}-${d}`; }
function escapeHtml(value) { return String(value ?? '').replaceAll('&', '&amp;').replaceAll('<', '&lt;').replaceAll('>', '&gt;').replaceAll('"', '&quot;').replaceAll("'", '&#039;'); }
