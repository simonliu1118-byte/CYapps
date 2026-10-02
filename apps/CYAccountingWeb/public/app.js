const state = {
  kind: 'expense',
  accounts: [],
  archivedAccounts: [],
  groups: [],
  categories: [],
  transactions: [],
  lockedThrough: null,
  ledgerLocked: false,
  editingTx: null,
  settingsKind: 'income',
  activeSettingsTab: 'accounts',
  openingData: null
};
const els = {};

let cyaccAppStarted = false;
let cyTransactionRequestId = 0;
let cyOpeningManualEdit = false;
let cyOpeningOriginalValues = new Map();

if (document.readyState === 'loading') {
  document.addEventListener('DOMContentLoaded', startCyaccApp, { once: true });
} else {
  void startCyaccApp();
}

async function startCyaccApp() {
  if (cyaccAppStarted) return;
  cyaccAppStarted = true;
  let startupError = null;

  try {
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

    if (window.cyaccSessionPromise) await window.cyaccSessionPromise;
    setCyaccBootStage('正在準備帳務資料…');

    await initialize();
    window.cyaccCoreReady = true;
    window.dispatchEvent(new CustomEvent('cyacc:core-ready'));
  } catch (error) {
    startupError = error instanceof Error ? error : new Error('APP_STARTUP_FAILED');
    console.error('cyaccounting_app_start_failed', startupError.message);
    if (els.connectionStatus) setConnection('載入失敗', 'warn');
    if (els.saveMessage) showMessage(startupError.message || '載入失敗，請重新整理。', true);
  } finally {
    finishCyaccBoot(startupError);
  }
}

function setCyaccBootStage(message) {
  const status = document.querySelector('#cyaccBootStatus');
  if (status) status.textContent = message;
}

function finishCyaccBoot(error = null) {
  if (window.__cyaccBootWatchdog) {
    window.clearTimeout(window.__cyaccBootWatchdog);
    window.__cyaccBootWatchdog = null;
  }
  const status = document.querySelector('#cyaccBootStatus');
  document.body.classList.remove('cyacc-booting');
  if (error) {
    document.body.classList.add('cyacc-boot-failed');
    if (status) {
      status.hidden = false;
      status.textContent = error?.message || '載入失敗，請重新整理後再試。';
    }
    return;
  }
  document.body.classList.remove('cyacc-boot-failed');
  status?.remove();
}

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
    window.cySettingsManager?.renderCategoryManager?.();
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
    setCyaccBootStage('正在檢查服務…');
    const health = await api('/api/health');
    if (!health.database) {
      setConnection('網站已啟動，等待 D1 設定', 'warn');
      els.transactionRows.innerHTML = '<tr><td colspan="7" class="empty">D1 尚未綁定，完成 Cloudflare 設定後即可開始記帳。</td></tr>';
      els.saveButton.disabled = true;
      return;
    }
    setCyaccBootStage('正在載入帳戶與科目…');
    await refreshBootstrap();
    setConnection('已連線', 'ok');
    setCyaccBootStage('正在載入本月資料…');
    await loadTransactions();
  } catch (error) {
    setConnection('連線失敗', 'warn');
    showMessage(error.message, true);
    throw error;
  }
}

async function refreshBootstrap() {
  const bootstrap = await api('/api/bootstrap');
  const accountValue = els.accountName.value;
  const categoryValue = els.categoryName.value;
  state.accounts = bootstrap.accounts || [];
  state.archivedAccounts = bootstrap.archivedAccounts || [];
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
  window.cySyncMobileCanvasContinuation?.();
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
  const month = String(els.monthFilter.value || '');
  if (!month) return;
  const requestId = ++cyTransactionRequestId;
  setLedgerLoadingState(true);
  try {
    const data = await api(`/api/transactions?month=${encodeURIComponent(month)}`);
    if (requestId !== cyTransactionRequestId || month !== els.monthFilter.value) return;
    state.transactions = data.transactions || [];
    state.ledgerLocked = Boolean(data.locked);
    state.lockedThrough = data.lockedThrough || state.lockedThrough;
    if (typeof window.cyaccRefreshLedgerView === 'function') await window.cyaccRefreshLedgerView(month);
    else renderTransactions();
    updateEntryLockState();
  } catch (error) {
    if (requestId !== cyTransactionRequestId || month !== els.monthFilter.value) return;
    els.transactionRows.innerHTML = `<tr><td colspan="7" class="empty">${escapeHtml(error.message)}</td></tr>`;
  } finally {
    if (requestId === cyTransactionRequestId) setLedgerLoadingState(false);
  }
}

function setLedgerLoadingState(loading) {
  const busy = Boolean(loading);
  const ledger = document.querySelector('.ledger-card');
  ledger?.classList.toggle('is-loading', busy);
  ledger?.setAttribute('aria-busy', busy ? 'true' : 'false');
  for (const control of document.querySelectorAll(
    '#ledgerPrevMonth, #ledgerNextMonth, #monthFilter, #mobileLedgerMoreButton, #mobileLedgerBalanceButton, #ledgerSearchForm input, #ledgerSearchForm button'
  )) {
    if ('disabled' in control) control.disabled = busy;
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
  window.cySettingsManager?.renderAccountManager?.();
  window.cySettingsManager?.renderCategoryManager?.();
  els.lockedThrough.value = state.lockedThrough || '';
  els.lockStatusText.textContent = state.lockedThrough ? `目前已鎖帳至 ${formatMonth(state.lockedThrough)}，更早月份也一併鎖定。` : '目前未鎖帳。';
  window.cySyncMobileLockMonthControls?.();
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

  const archiveButton = event.target.closest('[data-account-archive]');
  if (archiveButton) return archiveAccountOptimistically(Number(archiveButton.dataset.accountArchive));

  const restoreButton = event.target.closest('[data-account-restore]');
  if (restoreButton) return restoreAccountOptimistically(Number(restoreButton.dataset.accountRestore));

  const permanentButton = event.target.closest('[data-account-permanent-delete]');
  if (permanentButton) return permanentlyDeleteArchivedAccount(Number(permanentButton.dataset.accountPermanentDelete));
}

async function archiveAccountOptimistically(id) {
  if (!Number.isInteger(id) || id <= 0) return;
  const account = state.accounts.find(item => Number(item.id) === id);
  if (!account || state.accounts.length <= 1) return;

  const previousAccounts = state.accounts.map(item => ({ ...item }));
  const previousArchived = state.archivedAccounts.map(item => ({ ...item }));
  const nextAccounts = state.accounts.filter(item => Number(item.id) !== id).map(item => ({ ...item }));
  if (Number(account.is_default) === 1 && nextAccounts.length) {
    nextAccounts.forEach((item, index) => { item.is_default = index === 0 ? 1 : 0; });
  }

  state.accounts = nextAccounts;
  state.archivedAccounts = [{
    ...account,
    is_default: 0,
    archived_at: new Date().toISOString(),
    transaction_count: 0,
    opening_balance_count: 0
  }, ...state.archivedAccounts];
  renderAccountSurfaces(account.name);
  setDialogMessage(els.settingsMessage, '');

  try {
    await api(`/api/accounts/${id}/archive`, { method: 'POST' });
    await refreshBootstrap();
    setDialogMessage(els.settingsMessage, '帳戶已封存。');
  } catch (error) {
    state.accounts = previousAccounts;
    state.archivedAccounts = previousArchived;
    renderAccountSurfaces(account.name);
    setDialogMessage(els.settingsMessage, error.message || '帳戶封存失敗，已還原。', true);
  }
}

async function restoreAccountOptimistically(id) {
  if (!Number.isInteger(id) || id <= 0) return;
  const account = state.archivedAccounts.find(item => Number(item.id) === id);
  if (!account) return;

  const previousAccounts = state.accounts.map(item => ({ ...item }));
  const previousArchived = state.archivedAccounts.map(item => ({ ...item }));
  state.archivedAccounts = state.archivedAccounts.filter(item => Number(item.id) !== id);
  state.accounts = [...state.accounts, {
    id: account.id,
    name: account.name,
    sort_order: state.accounts.length,
    is_default: 0
  }];
  renderAccountSurfaces(account.name);
  setDialogMessage(els.settingsMessage, '');

  try {
    await api(`/api/accounts/${id}/restore`, { method: 'POST' });
    await refreshBootstrap();
    setDialogMessage(els.settingsMessage, '帳戶已解封。');
  } catch (error) {
    state.accounts = previousAccounts;
    state.archivedAccounts = previousArchived;
    renderAccountSurfaces();
    setDialogMessage(els.settingsMessage, error.message || '帳戶解封失敗，已還原。', true);
  }
}

async function permanentlyDeleteArchivedAccount(id) {
  if (!Number.isInteger(id) || id <= 0) return;
  const account = state.archivedAccounts.find(item => Number(item.id) === id);
  if (!account) return;
  const transactions = Number(account.transaction_count || 0);
  const openings = Number(account.opening_balance_count || 0);
  if (transactions > 0 || openings > 0) {
    setDialogMessage(els.settingsMessage, '此帳戶仍有歷史記帳或期初餘額，不能永久刪除。', true);
    return;
  }
  if (!confirm(`永久刪除帳戶「${account.name}」？\n此操作無法復原。`)) return;

  try {
    await api(`/api/accounts/${id}/permanent`, { method: 'DELETE' });
    await refreshBootstrap();
    setDialogMessage(els.settingsMessage, '帳戶已永久刪除。');
  } catch (error) {
    setDialogMessage(els.settingsMessage, error.message || '帳戶永久刪除失敗。', true);
  }
}

function renderAccountSurfaces(preferred) {
  renderAccounts(preferred);
  window.cySettingsManager?.renderAccountManager?.();
}

async function addAccount() {
  const name = els.newAccountName.value.trim();
  if (!name) return;
  const ok = await mutateSettings('/api/accounts', { method: 'POST', headers: jsonHeaders(), body: JSON.stringify({ name }) }, '帳戶已新增。');
  if (ok) els.newAccountName.value = '';
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
  cyOpeningManualEdit = false;
  cyOpeningOriginalValues = new Map();
  setDialogMessage(els.openingMessage, '');
  els.openingRows.innerHTML = '<div class="empty">載入中…</div>';

  try {
    const data = await api(`/api/opening-balances?month=${encodeURIComponent(month)}&audit=1`);
    state.openingData = data;
    if (!data.accounts?.length) {
      els.openingRows.innerHTML = '<div class="empty">沒有可計算的帳戶。</div>';
      els.saveOpeningButton.disabled = true;
      els.saveOpeningButton.textContent = '手動調整';
      return;
    }

    cyOpeningOriginalValues = new Map(data.accounts.map(item => [String(item.name), Number(item.amount) || 0]));
    const rows = data.accounts.map(item => {
      const source = item.source === 'override' ? 'manual' : 'automatic';
      const sourceLabel = source === 'manual' ? '手動調整' : '自動';
      const historical = item.isCurrent ? '' : '<span class="historical">歷史帳戶</span>';
      const sourceBadge = `<span class="opening-source ${source}">${sourceLabel}</span>`;
      const detail = source === 'manual'
        ? `自動值 ${money(item.automaticAmount)}${item.overrideReason ? ` · ${escapeHtml(item.overrideReason)}` : ''}`
        : (item.automaticAnchorMonth ? `承接 ${escapeHtml(item.automaticAnchorMonth)} 手動基準後自動計算` : '依歷史收支自動計算');
      return `<label class="opening-row" data-opening-source="${source}">
        <span class="opening-account-copy">
          <span class="opening-account-title"><strong>${escapeHtml(item.name)}</strong>${historical}${sourceBadge}</span>
          <small>${detail}</small>
        </span>
        <input type="number" step="1" value="${Number(item.amount) || 0}"
          data-opening-account="${escapeHtml(item.name)}"
          data-opening-automatic="${Number(item.automaticAmount) || 0}"
          disabled>
      </label>`;
    }).join('');

    const audit = renderOpeningAudit(data.audit || []);
    els.openingRows.innerHTML = rows + `
      <section id="openingManualPanel" class="opening-manual-panel" hidden>
        <label class="opening-reason-field">
          <span>手動調整理由 <strong>必填</strong></span>
          <textarea id="openingAdjustmentReason" maxlength="200" rows="2" placeholder="例如：銀行對帳差異調整"></textarea>
        </label>
        <div class="opening-manual-actions">
          <button id="cancelOpeningManualButton" class="secondary" type="button">取消手動調整</button>
          <span>若輸入值等於自動值，會清除該月人工覆寫並保留 audit。</span>
        </div>
      </section>
      ${audit}`;

    document.querySelector('#cancelOpeningManualButton')?.addEventListener('click', cancelOpeningManualEdit);
    els.saveOpeningButton.disabled = Boolean(data.locked);
    els.saveOpeningButton.textContent = data.locked ? '已鎖帳' : '手動調整';
    if (data.locked) {
      setDialogMessage(els.openingMessage, `${month} 已鎖帳，期初餘額僅供檢視。`, true);
    }
  } catch (error) {
    els.openingRows.innerHTML = `<div class="empty">${escapeHtml(error.message)}</div>`;
    els.saveOpeningButton.disabled = true;
  }
}

async function saveOpeningBalances() {
  if (!els.openingMonth.value || state.openingData?.locked) return;

  if (!cyOpeningManualEdit) {
    cyOpeningManualEdit = true;
    els.openingRows.querySelectorAll('[data-opening-account]').forEach(input => { input.disabled = false; });
    const panel = document.querySelector('#openingManualPanel');
    if (panel) panel.hidden = false;
    els.saveOpeningButton.textContent = '儲存調整';
    setDialogMessage(els.openingMessage, '手動調整會留下理由、操作者與時間；未修改的帳戶不會寫入。');
    document.querySelector('#openingAdjustmentReason')?.focus();
    return;
  }

  const reason = String(document.querySelector('#openingAdjustmentReason')?.value || '').trim();
  if (!reason) {
    setDialogMessage(els.openingMessage, '請填寫手動調整理由。', true);
    document.querySelector('#openingAdjustmentReason')?.focus();
    return;
  }

  const values = {};
  for (const input of els.openingRows.querySelectorAll('[data-opening-account]')) {
    const raw = input.value.trim();
    const amount = Number(raw);
    const name = String(input.dataset.openingAccount || '');
    if (!raw || !Number.isSafeInteger(amount)) {
      return setDialogMessage(els.openingMessage, `${name} 的期初餘額必須是整數。`, true);
    }
    if (amount !== Number(cyOpeningOriginalValues.get(name) || 0)) values[name] = amount;
  }

  if (!Object.keys(values).length) {
    setDialogMessage(els.openingMessage, '沒有期初餘額變更。');
    return;
  }

  els.saveOpeningButton.disabled = true;
  try {
    await api('/api/opening-balance-overrides', {
      method: 'PUT',
      headers: jsonHeaders(),
      body: JSON.stringify({ month: els.openingMonth.value, values, reason })
    });
    setDialogMessage(els.openingMessage, '手動調整已儲存並寫入 audit。');
    await loadOpeningBalances();
    if (typeof scheduleLedgerDesktopRefresh === 'function') scheduleLedgerDesktopRefresh();
  } catch (error) {
    setDialogMessage(els.openingMessage, error.message, true);
  } finally {
    if (!state.openingData?.locked) els.saveOpeningButton.disabled = false;
  }
}

function cancelOpeningManualEdit() {
  cyOpeningManualEdit = false;
  for (const input of els.openingRows.querySelectorAll('[data-opening-account]')) {
    const name = String(input.dataset.openingAccount || '');
    input.value = String(Number(cyOpeningOriginalValues.get(name) || 0));
    input.disabled = true;
  }
  const panel = document.querySelector('#openingManualPanel');
  if (panel) panel.hidden = true;
  els.saveOpeningButton.textContent = '手動調整';
  setDialogMessage(els.openingMessage, '');
}

function renderOpeningAudit(entries) {
  const rows = Array.isArray(entries) ? entries : [];
  if (!rows.length) {
    return '<details class="opening-audit"><summary>調整紀錄</summary><div class="opening-audit-empty">目前沒有手動調整紀錄。</div></details>';
  }
  const items = rows.slice(0, 12).map(entry => {
    const action = entry.action === 'clear' ? '恢復自動' : entry.action === 'migration' ? '舊資料移轉' : '手動設定';
    const actor = [entry.actorEmployeeNo, entry.actorName].filter(Boolean).join(' ');
    const amount = entry.newAmount === null ? '—' : money(entry.newAmount);
    return `<div class="opening-audit-row">
      <div><strong>${escapeHtml(entry.accountName)}</strong><span>${escapeHtml(action)}</span><b>${escapeHtml(amount)}</b></div>
      <small>${escapeHtml(entry.reason || '')}${actor ? ` · ${escapeHtml(actor)}` : ''}${entry.createdAt ? ` · ${escapeHtml(entry.createdAt.replace('T', ' ').replace('Z', ' UTC'))}` : ''}</small>
    </div>`;
  }).join('');
  return `<details class="opening-audit"><summary>調整紀錄（${rows.length}）</summary><div class="opening-audit-list">${items}</div></details>`;
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

async function api(path, options = {}) {
  const controller = new AbortController();
  let timeoutId;
  const request = Promise.resolve().then(async () => {
    const response = await fetch(path, {
      ...options,
      credentials: 'include',
      cache: 'no-store',
      signal: controller.signal
    });
    const data = await response.json().catch(() => ({}));
    if (!response.ok || data.ok === false) throw new Error(data.error || `HTTP ${response.status}`);
    return data;
  });
  const timeout = new Promise((_, reject) => {
    timeoutId = window.setTimeout(() => {
      controller.abort();
      const error = new Error('CYACC_BROWSER_TIMEOUT');
      error.name = 'TimeoutError';
      reject(error);
    }, 12_000);
  });

  try {
    return await Promise.race([request, timeout]);
  } catch (error) {
    if (['AbortError', 'TimeoutError'].includes(error?.name)) {
      throw new Error('連線逾時，請重新整理後再試。');
    }
    throw error;
  } finally {
    if (timeoutId !== undefined) window.clearTimeout(timeoutId);
  }
}

function jsonHeaders() { return { 'content-type': 'application/json' }; }
function setConnection(text, type) { els.connectionStatus.textContent = text; els.connectionStatus.className = `status ${type || ''}`.trim(); }
function showMessage(text, isError = false) {
  els.saveMessage.textContent = text;
  els.saveMessage.classList.toggle('error', isError);
  window.cyAfterSaveMessage?.(els.saveMessage, text, isError);
}
function setDialogMessage(element, text, isError = false) { element.textContent = text; element.classList.toggle('error', isError); }
function money(value) { return `$${Number(value || 0).toLocaleString('zh-TW')}`; }
function formatMonth(value) { const [year, month] = String(value || '').split('-'); return year && month ? `${year}年${month}月` : ''; }
function localDateString(date) { const y = date.getFullYear(), m = String(date.getMonth() + 1).padStart(2, '0'), d = String(date.getDate()).padStart(2, '0'); return `${y}-${m}-${d}`; }
function escapeHtml(value) { return String(value ?? '').replaceAll('&', '&amp;').replaceAll('<', '&lt;').replaceAll('>', '&gt;').replaceAll('"', '&quot;').replaceAll("'", '&#039;'); }
