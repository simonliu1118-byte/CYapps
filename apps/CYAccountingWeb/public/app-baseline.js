window.__CYACC_BASELINE_BUNDLE__ = true;


/* ---- baseline section ---- */
let cyaccAuthStarted = false;

if (document.readyState === 'loading') {
  document.addEventListener('DOMContentLoaded', startCyaccAuth, { once: true });
} else {
  startCyaccAuth();
}

function startCyaccAuth() {
  if (cyaccAuthStarted) return;
  cyaccAuthStarted = true;

  const currentUser = document.querySelector('#currentUser');
  const logoutButton = document.querySelector('#logoutButton');
  const readOnlyNotice = document.querySelector('#readOnlyNotice');

  logoutButton?.addEventListener('click', async () => {
    logoutButton.disabled = true;
    try {
      await fetchWithTimeout('/api/auth/logout', {
        method: 'POST',
        credentials: 'include',
        cache: 'no-store'
      }, 8_000);
    } finally {
      location.replace('/login');
    }
  });

  setBootStage('正在準備帳務資料…');
  const bootUser = validBootUser(window.__CYACC_BOOT_USER__) ? window.__CYACC_BOOT_USER__ : null;
  window.cyaccSessionPromise = bootUser ? Promise.resolve(bootUser) : checkSessionFallback();
  window.cyaccSessionPromise
    .then(user => {
      window.cyaccCurrentUser = user;
      applyUser(user);
      window.dispatchEvent(new CustomEvent('cyacc:session-ready', { detail: { user } }));
    })
    .catch(() => {});

  async function checkSessionFallback() {
    try {
      setBootStage('正在驗證帳號…');
      const response = await fetchWithTimeout('/api/auth/me', {
        cache: 'no-store',
        credentials: 'include'
      }, 8_000);
      const data = await response.json().catch(() => ({}));
      if (response.status === 401 || data.code === 'AUTH_REQUIRED') {
        location.replace('/login');
        throw new Error('AUTH_REQUIRED');
      }
      if (!response.ok || data.ok === false) {
        throw new Error(data.error || '帳號服務目前無法驗證登入狀態。');
      }
      if (!validBootUser(data.user)) throw new Error('帳號資料格式不正確。');
      return data.user;
    } catch (error) {
      if (String(error?.message || '') === 'AUTH_REQUIRED') throw error;
      const message = error?.name === 'AbortError'
        ? '帳號驗證逾時，請重新整理後再試。'
        : (error?.message || '帳號服務目前無法驗證登入狀態。');
      setBootFailure(message);
      throw new Error(message);
    }
  }

  function validBootUser(user) {
    return Boolean(
      user
      && /^\d{4}$/.test(String(user.employeeNo || ''))
      && ['USER', 'ADMIN', 'SUPER_ADMIN'].includes(String(user.role || ''))
    );
  }

  function applyUser(user) {
    const role = String(user.role || '');
    const roleLabels = {
      SUPER_ADMIN: '超級管理員',
      ADMIN: '管理員',
      USER: '使用者'
    };

    if (currentUser) {
      currentUser.replaceChildren();
      const main = document.createElement('span');
      main.className = 'current-user-main';
      main.textContent = [user.employeeNo, user.name].filter(Boolean).join(' ').trim();
      const roleBadge = document.createElement('span');
      roleBadge.className = 'current-user-role';
      roleBadge.textContent = roleLabels[role] || role;
      currentUser.append(main, roleBadge);
      currentUser.classList.remove('hidden', 'role-super-admin', 'role-admin', 'role-user');
      if (role === 'SUPER_ADMIN') currentUser.classList.add('role-super-admin');
      else if (role === 'ADMIN') currentUser.classList.add('role-admin');
      else currentUser.classList.add('role-user');
    }

    logoutButton?.classList.remove('hidden');
    document.body.dataset.cyaccRole = role;
    if (user.canWriteAccounting === false || role === 'USER') {
      document.body.dataset.cyaccReadOnly = 'true';
      readOnlyNotice?.classList.remove('hidden');
      activateReadOnlyMobileLedger();
    } else {
      delete document.body.dataset.cyaccReadOnly;
      readOnlyNotice?.classList.add('hidden');
    }
  }

  function activateReadOnlyMobileLedger(attempt = 0) {
    if (!window.matchMedia('(max-width: 767px)').matches) return;
    const ledgerButton = document.querySelector('#mobileMainNav [data-mobile-page="ledger"]');
    if (ledgerButton) {
      ledgerButton.click();
      return;
    }
    if (attempt < 60) window.setTimeout(() => activateReadOnlyMobileLedger(attempt + 1), 50);
  }
}

function setBootStage(message) {
  const status = document.querySelector('#cyaccBootStatus');
  if (status) status.textContent = message;
}

function setBootFailure(message) {
  const status = document.querySelector('#cyaccBootStatus');
  document.body.classList.remove('cyacc-booting');
  document.body.classList.add('cyacc-boot-failed');
  if (status) {
    status.hidden = false;
    status.textContent = message;
  }
}

async function fetchWithTimeout(input, init = {}, timeoutMs = 8_000) {
  const controller = new AbortController();
  const timeout = window.setTimeout(() => controller.abort(), timeoutMs);
  try {
    return await fetch(input, { ...init, signal: controller.signal });
  } finally {
    window.clearTimeout(timeout);
  }
}

/* ---- baseline section ---- */
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

let cyaccAppStarted = false;

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

    setCyaccBootStage('正在準備帳務資料…');
    if (window.cyaccSessionPromise) await window.cyaccSessionPromise;

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
      status.textContent = '載入失敗，請重新整理後再試。';
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

async function api(path, options = {}) {
  const controller = new AbortController();
  const timeout = window.setTimeout(() => controller.abort(), 12_000);
  try {
    const response = await fetch(path, {
      ...options,
      credentials: 'include',
      cache: 'no-store',
      signal: controller.signal
    });
    const data = await response.json().catch(() => ({}));
    if (!response.ok || data.ok === false) throw new Error(data.error || `HTTP ${response.status}`);
    return data;
  } catch (error) {
    if (error?.name === 'AbortError') throw new Error('連線逾時，請重新整理後再試。');
    throw error;
  } finally {
    window.clearTimeout(timeout);
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

/* ---- baseline section ---- */
let cyFastSummaryRequest = 0;
let cyFocusSummaryAfterSave = false;

window.addEventListener('load', () => {
  bindFastEntryKeys();
  bindQuickTools();
  bindFavoriteManager();
  observeAppRefreshes();
  refreshFastEntryUi();
});

function bindFastEntryKeys() {
  const enterStep = (element, action) => {
    element?.addEventListener('keydown', event => {
      if (event.key !== 'Enter' || event.isComposing || event.shiftKey || event.ctrlKey || event.altKey || event.metaKey) return;
      event.preventDefault();
      action();
    });
  };

  enterStep(els.txDate, () => els.summary?.focus());
  enterStep(els.summary, () => {
    els.amount?.focus();
    els.amount?.select();
  });
  enterStep(els.amount, () => {
    if (!els.saveButton.disabled) {
      cyFocusSummaryAfterSave = true;
      els.form.requestSubmit();
    }
  });

  els.form?.addEventListener('submit', () => {
    cyFocusSummaryAfterSave = true;
  });

  els.accountName?.addEventListener('change', loadFrequentSummaries);
  els.categoryName?.addEventListener('change', loadFrequentSummaries);
  els.kindButtons?.forEach(button => button.addEventListener('click', () => {
    setTimeout(() => {
      renderFavoriteCategories();
      loadFrequentSummaries();
    }, 0);
  }));
}

function bindQuickTools() {
  document.querySelector('#favoriteCategoryButtons')?.addEventListener('click', event => {
    const button = event.target.closest('[data-quick-category]');
    if (!button) return;
    const name = button.dataset.quickCategory;
    if (![...els.categoryName.options].some(option => option.value === name)) return;
    els.categoryName.value = name;
    loadFrequentSummaries();
    els.summary.focus();
  });

  document.querySelector('#summarySuggestions')?.addEventListener('click', event => {
    const button = event.target.closest('[data-summary-suggestion]');
    if (!button) return;
    els.summary.value = button.dataset.summarySuggestion || '';
    els.amount.focus();
  });
}

function bindFavoriteManager() {
  els.categoryManager?.addEventListener('click', async event => {
    const button = event.target.closest('[data-category-favorite]');
    if (!button) return;
    const id = Number(button.dataset.categoryFavorite);
    const category = state.categories.find(item => Number(item.id) === id);
    if (!category) return;
    button.disabled = true;
    setDialogMessage(els.settingsMessage, '');
    try {
      await api(`/api/categories/${id}/favorite`, {
        method: 'PUT',
        headers: jsonHeaders(),
        body: JSON.stringify({ favorite: Number(category.is_favorite) !== 1 })
      });
      await refreshBootstrap();
      renderFavoriteCategories();
      setDialogMessage(els.settingsMessage, Number(category.is_favorite) === 1 ? '已取消常用科目。' : '已加入常用科目。');
    } catch (error) {
      setDialogMessage(els.settingsMessage, error.message, true);
    }
  });
}

function observeAppRefreshes() {
  const connectionObserver = new MutationObserver(() => {
    if (els.connectionStatus?.textContent === '已連線') refreshFastEntryUi();
  });
  if (els.connectionStatus) connectionObserver.observe(els.connectionStatus, { childList: true, subtree: true, characterData: true });

  const categoryObserver = new MutationObserver(() => injectFavoriteButtons());
  if (els.categoryManager) categoryObserver.observe(els.categoryManager, { childList: true, subtree: true });

  const ledgerObserver = new MutationObserver(() => {
    if (cyFocusSummaryAfterSave && els.saveMessage?.textContent === '存檔成功') {
      cyFocusSummaryAfterSave = false;
      setTimeout(() => {
        els.summary.focus();
        loadFrequentSummaries();
      }, 0);
    }
  });
  if (els.transactionRows) ledgerObserver.observe(els.transactionRows, { childList: true, subtree: true });
}

function refreshFastEntryUi() {
  renderFavoriteCategories();
  injectFavoriteButtons();
  loadFrequentSummaries();
}

function renderFavoriteCategories() {
  const group = document.querySelector('#favoriteCategoryGroup');
  const container = document.querySelector('#favoriteCategoryButtons');
  if (!group || !container) return;
  const favorites = state.categories
    .filter(category => category.kind === state.kind && Number(category.is_favorite) === 1)
    .slice(0, 10);
  group.classList.toggle('hidden', favorites.length === 0);
  container.innerHTML = favorites.map(category =>
    `<button type="button" class="quick-chip" data-quick-category="${escapeHtml(category.name)}">${escapeHtml(category.name)}</button>`
  ).join('');
}

async function loadFrequentSummaries() {
  const group = document.querySelector('#summarySuggestionGroup');
  const container = document.querySelector('#summarySuggestions');
  if (!group || !container || !els.accountName?.value || !els.categoryName?.value) return;
  const requestId = ++cyFastSummaryRequest;
  group.classList.add('hidden');
  container.innerHTML = '';
  const query = new URLSearchParams({
    kind: state.kind,
    account: els.accountName.value,
    category: els.categoryName.value
  });
  try {
    const data = await api(`/api/summaries/frequent?${query.toString()}`);
    if (requestId !== cyFastSummaryRequest) return;
    const summaries = data.summaries || [];
    group.classList.toggle('hidden', summaries.length === 0);
    container.innerHTML = summaries.map(summary =>
      `<button type="button" class="quick-chip summary-chip" data-summary-suggestion="${escapeHtml(summary)}">${escapeHtml(summary)}</button>`
    ).join('');
  } catch {
    if (requestId !== cyFastSummaryRequest) return;
    group.classList.add('hidden');
  }
}

function injectFavoriteButtons() {
  for (const item of els.categoryManager?.querySelectorAll('.category-item') || []) {
    const rename = item.querySelector('[data-category-rename]');
    if (!rename || item.querySelector('[data-category-favorite]')) continue;
    const id = Number(rename.dataset.categoryRename);
    const category = state.categories.find(entry => Number(entry.id) === id);
    if (!category) continue;
    const actions = rename.parentElement;
    const button = document.createElement('button');
    button.type = 'button';
    button.className = `mini-button favorite-toggle${Number(category.is_favorite) === 1 ? ' active' : ''}`;
    button.dataset.categoryFavorite = String(id);
    button.title = Number(category.is_favorite) === 1 ? '取消常用科目' : '設為常用科目';
    button.textContent = Number(category.is_favorite) === 1 ? '★ 常用' : '☆ 常用';
    actions.prepend(button, document.createTextNode(' '));
  }
}

/* ---- baseline section ---- */
let cyLedgerGroupByAccount = false;
let cyLedgerSearch = '';
let cyLedgerOpeningData = null;
let cyLedgerRefreshTimer = null;
let cyLedgerObserver = null;
let cyLedgerRequestId = 0;
window.cyLedgerBalanceBreakdowns = new Map();

let cyV06Started = false;

if (document.readyState === 'loading') {
  document.addEventListener('DOMContentLoaded', startV06LedgerTools, { once: true });
} else {
  startV06LedgerTools();
}
window.addEventListener('load', startV06LedgerTools, { once: true });

function startV06LedgerTools() {
  if (cyV06Started) return;
  if (!document.querySelector('.ledger-card') || !document.querySelector('#monthFilter')) return;
  cyV06Started = true;
  setupLedgerDesktopTools();
  bindLedgerDesktopTools();
  observeLedgerRefreshes();
  if (window.cyaccCoreReady) scheduleLedgerDesktopRefresh();
  else window.addEventListener('cyacc:core-ready', scheduleLedgerDesktopRefresh, { once: true });
}

function setupLedgerDesktopTools() {
  const card = document.querySelector('.ledger-card');
  const title = card?.querySelector('.ledger-title');
  const monthPicker = title?.querySelector('.month-picker');
  if (!card || !title || !monthPicker || document.querySelector('#ledgerDesktopTools')) return;

  const tools = document.createElement('div');
  tools.id = 'ledgerDesktopTools';
  tools.className = 'ledger-desktop-tools';
  tools.innerHTML = `
    <div class="ledger-period-tools">
      <div class="ledger-month-tools">
        <button id="ledgerPrevMonth" class="secondary compact" type="button" title="上一個月">‹</button>
        <div id="ledgerMonthSlot"></div>
        <button id="ledgerNextMonth" class="secondary compact" type="button" title="下一個月">›</button>
        <span id="ledgerDisplayMonth" class="ledger-display-month"></span>
      </div>
      <button id="ledgerOpeningBalanceButton" class="secondary compact ledger-tool-button emphasis" type="button">期初餘額</button>
    </div>
    <form id="ledgerSearchForm" class="ledger-search" role="search">
      <span class="ledger-search-icon" aria-hidden="true">⌕</span>
      <input id="ledgerSummarySearch" type="search" maxlength="100" placeholder="搜尋摘要" autocomplete="off" inputmode="search" enterkeyhint="search">
      <button class="secondary compact ledger-search-submit" type="submit">搜尋</button>
      <button id="ledgerSearchClear" class="secondary compact" type="button" aria-label="清除搜尋"><span class="ledger-search-clear-desktop">清除</span><span class="ledger-search-clear-mobile" aria-hidden="true">×</span></button>
    </form>
    <div class="ledger-view-tools"></div>
  `;
  title.insertAdjacentElement('afterend', tools);
  document.querySelector('#ledgerMonthSlot')?.append(monthPicker);
}

function bindLedgerDesktopTools() {
  document.querySelector('#ledgerPrevMonth')?.addEventListener('click', () => moveLedgerMonth(-1));
  document.querySelector('#ledgerNextMonth')?.addEventListener('click', () => moveLedgerMonth(1));
  document.querySelector('#ledgerSearchForm')?.addEventListener('submit', event => {
    event.preventDefault();
    cyLedgerSearch = document.querySelector('#ledgerSummarySearch')?.value.trim() || '';
    renderDesktopLedger();
  });
  document.querySelector('#ledgerSearchClear')?.addEventListener('click', () => {
    const input = document.querySelector('#ledgerSummarySearch');
    if (input) input.value = '';
    cyLedgerSearch = '';
    renderDesktopLedger();
  });
  els.monthFilter?.addEventListener('change', () => {
    cyLedgerOpeningData = null;
    cyLedgerGroupByAccount = false;
    scheduleLedgerDesktopRefresh();
  });
}

function observeLedgerRefreshes() {
  if (!els.transactionRows) return;
  cyLedgerObserver = new MutationObserver(() => scheduleLedgerDesktopRefresh());
  cyLedgerObserver.observe(els.transactionRows, { childList: true, subtree: true });
}

function scheduleLedgerDesktopRefresh() {
  if (!window.cyaccCoreReady) return;
  clearTimeout(cyLedgerRefreshTimer);
  cyLedgerRefreshTimer = setTimeout(loadLedgerOpeningAndRender, 25);
}

async function loadLedgerOpeningAndRender() {
  const month = els.monthFilter?.value;
  if (!month) return;
  const requestId = ++cyLedgerRequestId;
  try {
    const data = await api(`/api/opening-balances?month=${encodeURIComponent(month)}`);
    if (requestId !== cyLedgerRequestId || month !== els.monthFilter.value) return;
    cyLedgerOpeningData = data;
  } catch {
    if (requestId !== cyLedgerRequestId) return;
    cyLedgerOpeningData = { month, accounts: [] };
  }
  renderDesktopLedger();
}

function moveLedgerMonth(delta) {
  const current = els.monthFilter?.value;
  if (!/^\d{4}-\d{2}$/.test(current || '')) return;
  const [year, month] = current.split('-').map(Number);
  const date = new Date(year, month - 1 + delta, 1);
  const next = `${date.getFullYear()}-${String(date.getMonth() + 1).padStart(2, '0')}`;
  els.monthFilter.value = next;
  els.monthFilter.dispatchEvent(new Event('change', { bubbles: true }));
}

function renderDesktopLedger() {
  if (!els.transactionRows || !els.monthFilter) return;
  const month = els.monthFilter.value;
  if (!/^\d{4}-\d{2}$/.test(month)) return;

  const allTransactions = Array.isArray(state.transactions) ? state.transactions : [];
  const openingMap = new Map();
  for (const item of cyLedgerOpeningData?.accounts || []) {
    const value = item.amount === null || item.amount === undefined || item.amount === '' ? 0 : Number(item.amount);
    openingMap.set(String(item.name), Number.isFinite(value) ? value : 0);
  }

  window.cyCloseLedgerBalancePopover?.();
  window.cyLedgerBalanceBreakdowns = new Map();
  const calculated = calculateLedgerBalances(allTransactions, openingMap);
  const income = allTransactions.reduce((sum, tx) => sum + (tx.kind === 'income' ? Number(tx.amount) || 0 : 0), 0);
  const expense = allTransactions.reduce((sum, tx) => sum + (tx.kind === 'expense' ? Number(tx.amount) || 0 : 0), 0);
  const openingTotal = [...openingMap.values()].reduce((sum, value) => sum + value, 0);
  const endingTotal = openingTotal + income - expense;
  const net = income - expense;
  const netLabel = net > 0 ? '淨利' : net < 0 ? '淨損' : '淨利損';
  const netClass = net > 0 ? 'profit' : net < 0 ? 'loss' : 'neutral';

  const query = cyLedgerSearch.toLocaleLowerCase('zh-Hant');
  const visible = (query
    ? allTransactions.filter(tx => String(tx.summary || '').toLocaleLowerCase('zh-Hant').includes(query))
    : [...allTransactions]
  ).sort(compareLedgerChronological);

  els.monthSummary.innerHTML = `<span class="ledger-summary-item opening"><span>期初</span><strong>${money(openingTotal)}</strong></span><span class="ledger-summary-item ending"><span>期末</span><strong>${money(endingTotal)}</strong></span><span class="ledger-summary-item net ${netClass}"><span>${netLabel}</span><strong>${money(Math.abs(net))}</strong></span><span class="ledger-summary-item income"><span>收入</span><strong>${money(income)}</strong></span><span class="ledger-summary-item expense"><span>支出</span><strong>${money(expense)}</strong></span>${query ? `<span class="ledger-summary-search">搜尋 ${visible.length}/${allTransactions.length} 筆</span>` : ''}`;
  const display = document.querySelector('#ledgerDisplayMonth');
  if (display) display.textContent = `目前顯示｜${month.replace('-', '/')}`;
  updateLedgerGroupButton();
  updateLedgerHeader();

  if (!visible.length) {
    const text = query ? '本月沒有符合摘要搜尋條件的資料。' : '本月尚無記帳資料。';
    writeLedgerRows(`<tr><td colspan="8" class="empty">${escapeHtml(text)}</td></tr>`);
    return;
  }

  if (cyLedgerGroupByAccount) {
    writeLedgerRows(renderGroupedLedgerRows(visible, allTransactions, openingMap, calculated));
  } else {
    writeLedgerRows(visible.map(tx => renderLedgerRow(
      tx,
      calculated.globalById.get(Number(tx.id)) ?? 0,
      calculated.balancesById.get(Number(tx.id)) || new Map(),
      false
    )).join(''));
  }
}

function calculateLedgerBalances(transactions, openingMap) {
  const chronological = [...transactions].sort(compareLedgerChronological);
  const accountBalances = new Map(openingMap);
  let globalBalance = [...openingMap.values()].reduce((sum, value) => sum + value, 0);
  const globalById = new Map();
  const accountById = new Map();
  const balancesById = new Map();

  for (const tx of chronological) {
    const account = String(tx.account_name || '');
    const amount = Number(tx.amount) || 0;
    const direction = tx.kind === 'income' ? 1 : -1;
    const nextAccount = (accountBalances.get(account) || 0) + direction * amount;
    globalBalance += direction * amount;
    accountBalances.set(account, nextAccount);
    globalById.set(Number(tx.id), globalBalance);
    accountById.set(Number(tx.id), nextAccount);
    balancesById.set(Number(tx.id), new Map(accountBalances));
  }
  return { globalById, accountById, balancesById, endingByAccount: accountBalances };
}

function compareLedgerChronological(left, right) {
  const dateCompare = String(left.tx_date).localeCompare(String(right.tx_date));
  if (dateCompare) return dateCompare;
  const kindCompare = (left.kind === 'income' ? 0 : 1) - (right.kind === 'income' ? 0 : 1);
  if (kindCompare) return kindCompare;
  const createdCompare = String(left.created_at || '').localeCompare(String(right.created_at || ''));
  if (createdCompare) return createdCompare;
  return Number(left.id) - Number(right.id);
}

function renderGroupedLedgerRows(visible, allTransactions, openingMap, calculated) {
  const collator = new Intl.Collator('zh-Hant-TW', { numeric: true, sensitivity: 'base' });
  const names = [...new Set(visible.map(tx => String(tx.account_name || '')))].sort((a, b) => collator.compare(a, b));

  return names.map(name => {
    const rows = visible.filter(tx => tx.account_name === name).sort(compareLedgerChronological);
    const opening = openingMap.get(name) || 0;
    const ending = calculated.endingByAccount.get(name) ?? opening;
    const heading = `<tr class="account-group-row"><td colspan="8"><strong>${escapeHtml(name)}</strong><span>期初 ${money(opening)}　期末 ${money(ending)}</span></td></tr>`;
    return heading + rows.map(tx => {
      const accountBalance = calculated.accountById.get(Number(tx.id)) ?? 0;
      return renderLedgerRow(tx, accountBalance, new Map([[name, accountBalance]]), true);
    }).join('');
  }).join('');
}

function splitLedgerAccountName(value) {
  const chars = Array.from(String(value || '').trim());
  if (chars.length <= 2) return [chars.join('')];
  const cut = Math.floor(chars.length / 2);
  return [chars.slice(0, cut).join(''), chars.slice(cut).join('')];
}

function renderLedgerRow(tx, balance, accountBalances = new Map(), accountOnly = false) {
  const locked = isLocked(String(tx.tx_date || '').slice(0, 7));
  const id = Number(tx.id);
  if (Number.isInteger(id) && id > 0) {
    const accounts = [...accountBalances.entries()].map(([name, value]) => ({
      name: String(name || ''),
      value: Number(value) || 0
    }));
    window.cyLedgerBalanceBreakdowns?.set(id, {
      accountOnly,
      activeAccount: String(tx.account_name || ''),
      accounts,
      total: Number(balance) || 0
    });
  }

  const fullDate = String(tx.tx_date || '').replaceAll('-', '/');
  const mobileDate = fullDate.length >= 10 ? fullDate.slice(5) : fullDate;
  const accountName = String(tx.account_name || '');
  const accountLines = splitLedgerAccountName(accountName);
  const mobileAccount = accountLines.map(line => `<span>${escapeHtml(line)}</span>`).join('');
  const kindClass = tx.kind === 'income' ? 'ledger-row-income' : 'ledger-row-expense';

  return `<tr class="ledger-row ${kindClass}" data-transaction-id="${id}">
    <td><span class="ledger-date-desktop">${escapeHtml(fullDate)}</span><span class="ledger-date-mobile">${escapeHtml(mobileDate)}</span></td>
    <td class="ledger-account-name"><span class="ledger-account-desktop">${escapeHtml(accountName)}</span><span class="ledger-account-mobile" aria-label="${escapeHtml(accountName)}">${mobileAccount}</span></td>
    <td><span class="kind-tag ${tx.kind}">${tx.kind === 'income' ? '收入' : '支出'}</span></td>
    <td>${escapeHtml(tx.category_name)}</td>
    <td class="summary">${escapeHtml(tx.summary || '')}</td>
    <td class="num ledger-amount">${money(tx.amount)}</td>
    <td class="num ledger-balance" data-balance-popover-id="${id}" tabindex="0" role="button" aria-haspopup="dialog" aria-expanded="false" aria-label="查看此筆後帳戶餘額">${money(balance)}</td>
    <td class="action-col"><button type="button" class="row-action" data-edit-id="${tx.id}" ${locked ? 'disabled' : ''}><span class="action-label-desktop">編輯</span><span class="action-label-mobile">編輯</span></button><button type="button" class="row-action delete" data-delete-id="${tx.id}" ${locked ? 'disabled' : ''}><span class="action-label-desktop">刪除</span><span class="action-label-mobile">刪除</span></button></td>
  </tr>`;
}

function updateLedgerHeader() {
  const row = document.querySelector('.ledger-card thead tr');
  if (!row) return;
  const accountLabel = cyLedgerGroupByAccount ? '帳戶 ▲' : '帳戶';
  const accountTitle = cyLedgerGroupByAccount ? '點擊取消帳戶排列' : '點擊依帳戶排列';
  row.innerHTML = `<th>日期</th><th id="ledgerAccountHeader" class="ledger-account-header${cyLedgerGroupByAccount ? ' v21-account-group-active' : ''}" title="${accountTitle}" aria-pressed="${cyLedgerGroupByAccount ? 'true' : 'false'}">${accountLabel}</th><th>收支</th><th>科目</th><th>摘要</th><th class="num">金額</th><th class="num">餘額</th><th class="action-col">操作</th>`;
  row.querySelector('#ledgerAccountHeader')?.addEventListener('click', () => {
    cyLedgerGroupByAccount = !cyLedgerGroupByAccount;
    renderDesktopLedger();
  });
}

function updateLedgerGroupButton() {
  const button = document.querySelector('#ledgerGroupToggle');
  if (!button) return;
  button.remove();
}

function writeLedgerRows(html) {
  if (!els.transactionRows) return;
  cyLedgerObserver?.disconnect();
  els.transactionRows.innerHTML = html;
  cyLedgerObserver?.observe(els.transactionRows, { childList: true, subtree: true });
}

/* ---- baseline section ---- */
const cyInputConfirmations = [];
let cyPendingConfirmation = null;
let cyConfirmationObserver = null;

window.addEventListener('load', () => {
  setupEntryWorkflowUi();
  bindEntryKindVisuals();
  bindEntryKindShortcut();
  bindInputConfirmation();
  updateEntryKindVisual();
  renderInputConfirmations();
});

function setupEntryWorkflowUi() {
  const card = document.querySelector('.entry-card');
  const title = card?.querySelector('.section-title .title-with-badge');
  if (title && !document.querySelector('#entryKindIndicator')) {
    const indicator = document.createElement('span');
    indicator.id = 'entryKindIndicator';
    indicator.className = 'entry-kind-indicator expense';
    indicator.textContent = '支出模式';
    title.append(indicator);
  }

  const hint = card?.querySelector('.keyboard-hint');
  if (hint) {
    hint.innerHTML = '鍵盤：日期 Enter → 帳戶 Enter → 科目 Enter → 摘要 Enter → 金額 Enter 儲存　｜　<kbd>Tab</kbd> 切換收入／支出';
  }

  const ledger = document.querySelector('.ledger-card');
  if (ledger && !document.querySelector('#inputConfirmationCard')) {
    const section = document.createElement('section');
    section.id = 'inputConfirmationCard';
    section.className = 'card confirmation-card';
    section.innerHTML = `
      <div class="section-title">
        <div><h2>輸入確認</h2></div>
        <span class="hint">本次使用最近 10 筆</span>
      </div>
      <div id="inputConfirmationList" class="confirmation-list" aria-live="polite"></div>
    `;
    ledger.insertAdjacentElement('beforebegin', section);
  }
}

function bindEntryKindVisuals() {
  els.kindButtons?.forEach(button => button.addEventListener('click', () => {
    setTimeout(updateEntryKindVisual, 0);
  }));
}

function bindEntryKindShortcut() {
  els.form?.addEventListener('keydown', event => {
    if (event.key !== 'Tab' || event.shiftKey || event.isComposing || event.ctrlKey || event.altKey || event.metaKey) return;
    if (document.body.classList.contains('auth-locked')) return;
    if (document.querySelector('dialog[open]')) return;
    if (!(event.target instanceof HTMLElement) || !event.target.matches('input, select')) return;

    event.preventDefault();
    const active = document.activeElement;
    const nextKind = state.kind === 'expense' ? 'income' : 'expense';
    setEntryKind(nextKind);
    updateEntryKindVisual();

    setTimeout(() => {
      renderFavoriteCategories();
      loadFrequentSummaries();
      if (active instanceof HTMLElement && document.contains(active)) active.focus();
    }, 0);
  });
}

function updateEntryKindVisual() {
  const card = document.querySelector('.entry-card');
  const indicator = document.querySelector('#entryKindIndicator');
  if (!card) return;

  const isIncome = state.kind === 'income';
  card.classList.toggle('entry-income', isIncome);
  card.classList.toggle('entry-expense', !isIncome);
  if (indicator) {
    indicator.textContent = isIncome ? '收入模式' : '支出模式';
    indicator.className = `entry-kind-indicator ${isIncome ? 'income' : 'expense'}`;
  }
}

function bindInputConfirmation() {
  if (!els.form || !els.saveMessage) return;

  els.form.addEventListener('submit', () => {
    cyPendingConfirmation = snapshotPendingConfirmation();
  });

  cyConfirmationObserver = new MutationObserver(() => {
    if (!cyPendingConfirmation) return;
    const text = String(els.saveMessage.textContent || '').trim();
    if (!text) return;

    if (text === '存檔成功') {
      pushInputConfirmation({ ...cyPendingConfirmation, ok: true, message: '存檔成功' });
      cyPendingConfirmation = null;
      return;
    }

    if (els.saveMessage.classList.contains('error')) {
      pushInputConfirmation({ ...cyPendingConfirmation, ok: false, message: text });
      cyPendingConfirmation = null;
    }
  });
  cyConfirmationObserver.observe(els.saveMessage, { childList: true, subtree: true, characterData: true, attributes: true, attributeFilter: ['class'] });
}

function snapshotPendingConfirmation() {
  return {
    txDate: String(els.txDate?.value || ''),
    accountName: String(els.accountName?.value || ''),
    kind: state.kind,
    categoryName: String(els.categoryName?.value || ''),
    summary: String(els.summary?.value || '').trim(),
    amount: String(els.amount?.value || '').trim()
  };
}

function pushInputConfirmation(entry) {
  cyInputConfirmations.unshift(entry);
  if (cyInputConfirmations.length > 10) cyInputConfirmations.length = 10;
  renderInputConfirmations();
}

function renderInputConfirmations() {
  const container = document.querySelector('#inputConfirmationList');
  if (!container) return;
  if (!cyInputConfirmations.length) {
    container.innerHTML = '<div class="confirmation-empty">本次使用尚無輸入紀錄。</div>';
    return;
  }

  container.innerHTML = cyInputConfirmations.map(entry => {
    const summary = entry.summary || '(空白)';
    const amountNumber = Number(entry.amount);
    const amountText = Number.isFinite(amountNumber) && amountNumber > 0 ? money(amountNumber) : `$${escapeHtml(entry.amount || '0')}`;
    const kindText = entry.kind === 'income' ? '收入' : '支出';
    const date = escapeHtml(String(entry.txDate || '').replaceAll('-', '/'));
    return `<div class="confirmation-item ${entry.ok ? 'success' : 'error'}">
      <div class="confirmation-main">
        ${date}　[${escapeHtml(entry.accountName)}]　<span class="confirmation-kind ${entry.kind}">${kindText}</span> ${escapeHtml(entry.categoryName)} - ${escapeHtml(summary)}　${amountText}
      </div>
      <span class="confirmation-status">${entry.ok ? '存檔成功' : escapeHtml(entry.message || '存檔失敗')}</span>
    </div>`;
  }).join('');
}

/* ---- baseline section ---- */
const CY_CONFIRMATION_DRAWER_KEY = 'cyaccounting.confirmationDrawerOpen';

window.addEventListener('load', () => {
  setupConfirmationDrawer();
});

function setupConfirmationDrawer() {
  const panel = document.querySelector('#inputConfirmationCard');
  const topbarActions = document.querySelector('.topbar-actions');
  const settingsButton = document.querySelector('#settingsButton');
  if (!panel || !topbarActions || document.querySelector('#confirmationToggle')) return;

  panel.className = 'confirmation-drawer';
  panel.setAttribute('role', 'complementary');
  panel.setAttribute('aria-label', '輸入確認');
  panel.setAttribute('aria-hidden', 'true');

  const title = panel.querySelector('.section-title');
  if (title) {
    title.classList.add('confirmation-drawer-header');
    const close = document.createElement('button');
    close.id = 'confirmationDrawerClose';
    close.className = 'icon-button confirmation-drawer-close';
    close.type = 'button';
    close.setAttribute('aria-label', '關閉輸入確認');
    close.textContent = '×';
    title.append(close);
  }

  document.body.append(panel);

  const toggle = document.createElement('button');
  toggle.id = 'confirmationToggle';
  toggle.className = 'secondary compact confirmation-toggle';
  toggle.type = 'button';
  toggle.setAttribute('aria-controls', 'inputConfirmationCard');
  toggle.setAttribute('aria-expanded', 'false');
  toggle.innerHTML = '輸入確認 <span id="confirmationCount" class="confirmation-count">0</span>';
  if (settingsButton) settingsButton.insertAdjacentElement('beforebegin', toggle);
  else topbarActions.prepend(toggle);

  toggle.addEventListener('click', () => {
    setConfirmationDrawer(!panel.classList.contains('open'));
  });
  panel.querySelector('#confirmationDrawerClose')?.addEventListener('click', () => setConfirmationDrawer(false));

  document.addEventListener('keydown', event => {
    if (event.key !== 'Escape' || !panel.classList.contains('open')) return;
    if (document.querySelector('dialog[open]')) return;
    setConfirmationDrawer(false);
    toggle.focus();
  });

  const list = panel.querySelector('#inputConfirmationList');
  if (list) {
    const observer = new MutationObserver(() => {
      updateConfirmationCount();
      if (list.querySelector('.confirmation-item.error')) setConfirmationDrawer(true);
    });
    observer.observe(list, { childList: true, subtree: true });
  }

  updateConfirmationCount();
  setConfirmationDrawer(localStorage.getItem(CY_CONFIRMATION_DRAWER_KEY) === '1', false);
}

function setConfirmationDrawer(open, persist = true) {
  const panel = document.querySelector('#inputConfirmationCard');
  const toggle = document.querySelector('#confirmationToggle');
  if (!panel || !toggle) return;

  panel.classList.toggle('open', open);
  panel.setAttribute('aria-hidden', open ? 'false' : 'true');
  toggle.classList.toggle('active', open);
  toggle.setAttribute('aria-expanded', open ? 'true' : 'false');
  if (persist) localStorage.setItem(CY_CONFIRMATION_DRAWER_KEY, open ? '1' : '0');
}

function updateConfirmationCount() {
  const count = document.querySelectorAll('#inputConfirmationList .confirmation-item').length;
  const badge = document.querySelector('#confirmationCount');
  if (!badge) return;
  badge.textContent = String(count);
  badge.classList.toggle('has-items', count > 0);
}

/* ---- baseline section ---- */
const CY_CONFIRMATION_DRAWER_STATE_KEY = 'cyaccounting.confirmationDrawerOpen';

window.addEventListener('load', () => {
  setupV09EntryKindSwitch();
  setupV09ConfirmationEdgeControls();
  setupV09OpeningBalanceAction();
});

function setupV09EntryKindSwitch() {
  document.querySelector('#entryKindIndicator')?.remove();
}

function setupV09ConfirmationEdgeControls() {
  const panel = document.querySelector('#inputConfirmationCard');
  const legacyToggle = document.querySelector('#confirmationToggle');
  if (!panel || !legacyToggle || document.querySelector('#confirmationEdgeOpen')) return;

  const edgeOpen = document.createElement('button');
  edgeOpen.id = 'confirmationEdgeOpen';
  edgeOpen.className = 'confirmation-edge-open';
  edgeOpen.type = 'button';
  edgeOpen.setAttribute('aria-label', '展開輸入確認');
  edgeOpen.setAttribute('aria-controls', 'inputConfirmationCard');
  edgeOpen.innerHTML = `&lt;&lt;<span id="confirmationEdgeCount" class="confirmation-count">0</span>`;
  document.body.append(edgeOpen);

  const collapse = document.createElement('button');
  collapse.id = 'confirmationDrawerCollapse';
  collapse.className = 'confirmation-drawer-collapse';
  collapse.type = 'button';
  collapse.setAttribute('aria-label', '收合輸入確認');
  collapse.textContent = '>>';
  panel.append(collapse);

  edgeOpen.addEventListener('click', () => setConfirmationDrawer(true));
  collapse.addEventListener('click', () => setConfirmationDrawer(false));

  const sync = () => {
    const open = panel.classList.contains('open');
    edgeOpen.classList.toggle('hidden-edge', open);
    collapse.classList.toggle('hidden-edge', !open);
    edgeOpen.setAttribute('aria-expanded', open ? 'true' : 'false');
    syncV09ConfirmationCount();
  };

  const classObserver = new MutationObserver(sync);
  classObserver.observe(panel, { attributes: true, attributeFilter: ['class'] });

  const list = panel.querySelector('#inputConfirmationList');
  if (list) {
    const countObserver = new MutationObserver(syncV09ConfirmationCount);
    countObserver.observe(list, { childList: true, subtree: true });
  }

  const stored = localStorage.getItem(CY_CONFIRMATION_DRAWER_STATE_KEY);
  setConfirmationDrawer(stored === null ? true : stored === '1', false);
  sync();
}

function syncV09ConfirmationCount() {
  const count = document.querySelectorAll('#inputConfirmationList .confirmation-item').length;
  const badge = document.querySelector('#confirmationEdgeCount');
  if (!badge) return;
  badge.textContent = String(count);
  badge.classList.toggle('has-items', count > 0);
}

function setupV09OpeningBalanceAction() {
  const button = document.querySelector('#ledgerOpeningBalanceButton');
  const dialog = document.querySelector('#openingDialog');
  if (!button || !dialog) return;

  button.addEventListener('click', async () => {
    if (els.openingMonth && els.monthFilter?.value) els.openingMonth.value = els.monthFilter.value;
    if (els.openingMessage) setDialogMessage(els.openingMessage, '');
    dialog.showModal();
    await loadOpeningBalances();
  });
}

/* ---- baseline section ---- */
window.addEventListener('load', () => {
  polishV091ConfirmationSidebar();
});

function polishV091ConfirmationSidebar() {
  const topbar = document.querySelector('.topbar');
  const edgeOpen = document.querySelector('#confirmationEdgeOpen');
  const collapse = document.querySelector('#confirmationDrawerCollapse');
  if (!edgeOpen || !collapse) return;

  const chevronLeft = `
    <svg viewBox="0 0 24 24" aria-hidden="true">
      <path d="M13.5 7.5 9 12l4.5 4.5"></path>
      <path d="M18 7.5 13.5 12l4.5 4.5"></path>
    </svg>`;
  const chevronRight = `
    <svg viewBox="0 0 24 24" aria-hidden="true">
      <path d="m10.5 7.5 4.5 4.5-4.5 4.5"></path>
      <path d="m6 7.5 4.5 4.5L6 16.5"></path>
    </svg>`;

  const existingBadge = edgeOpen.querySelector('#confirmationEdgeCount');
  edgeOpen.innerHTML = chevronLeft;
  if (existingBadge) edgeOpen.append(existingBadge);
  collapse.innerHTML = chevronRight;

  edgeOpen.title = '展開輸入確認';
  collapse.title = '收合輸入確認';

  const syncBounds = () => {
    if (!topbar) return;
    const height = Math.ceil(topbar.getBoundingClientRect().height);
    document.documentElement.style.setProperty('--cy-confirmation-top', `${height}px`);
  };

  syncBounds();
  window.addEventListener('resize', syncBounds, { passive: true });
}

/* ---- baseline section ---- */
let cyDateDigitBuffer = '';
let cyDateDigitAt = 0;
let cyQuickEntrySettingsLoaded = false;

window.addEventListener('load', () => {
  bindDateQuickEntry();
  setupQuickEntrySettingsPane();
  setupOrderingControls();
  updateKeyboardHintV11();
});

function bindDateQuickEntry() {
  if (!els.txDate || els.txDate.dataset.v11DateBound === '1') return;
  els.txDate.dataset.v11DateBound = '1';

  els.txDate.addEventListener('keydown', event => {
    if (event.isComposing) return;

    if (event.ctrlKey && !event.altKey && !event.metaKey && (event.key === 'ArrowUp' || event.key === 'ArrowDown')) {
      event.preventDefault();
      cyDateDigitBuffer = '';
      stepEntryDate(event.key === 'ArrowUp' ? 1 : -1);
      return;
    }

    if (/^\d$/.test(event.key) && !event.ctrlKey && !event.altKey && !event.metaKey) {
      event.preventDefault();
      const now = Date.now();
      if (now - cyDateDigitAt > 1800) cyDateDigitBuffer = '';
      cyDateDigitAt = now;
      cyDateDigitBuffer += event.key;
      if (cyDateDigitBuffer.length > 8) cyDateDigitBuffer = event.key;
      applyBufferedDateIfComplete();
      return;
    }

    if (event.key === 'Escape' || event.key === 'Backspace' || event.key === 'Delete' || event.key === 'Enter' || event.key === 'Tab') {
      cyDateDigitBuffer = '';
    }
  });

  els.txDate.addEventListener('blur', () => { cyDateDigitBuffer = ''; });
}

function applyBufferedDateIfComplete() {
  const raw = cyDateDigitBuffer;
  if (raw.length === 4) {
    const year = Number(String(els.txDate.value || localDateString(new Date())).slice(0, 4));
    const month = Number(raw.slice(0, 2));
    const day = Number(raw.slice(2, 4));
    const value = validDateValue(year, month, day);
    if (value) {
      setEntryDateValue(value);
      cyDateDigitBuffer = '';
    }
    return;
  }

  if (raw.length === 8) {
    const value = validDateValue(Number(raw.slice(0, 4)), Number(raw.slice(4, 6)), Number(raw.slice(6, 8)));
    if (value) setEntryDateValue(value);
    cyDateDigitBuffer = '';
  }
}

function validDateValue(year, month, day) {
  if (!Number.isInteger(year) || year < 1900 || year > 2200 || month < 1 || month > 12 || day < 1 || day > 31) return '';
  const date = new Date(year, month - 1, day);
  if (date.getFullYear() !== year || date.getMonth() !== month - 1 || date.getDate() !== day) return '';
  return localDateString(date);
}

function stepEntryDate(delta) {
  const current = /^\d{4}-\d{2}-\d{2}$/.test(els.txDate.value || '') ? els.txDate.value : localDateString(new Date());
  const [year, month, day] = current.split('-').map(Number);
  const date = new Date(year, month - 1, day + delta);
  setEntryDateValue(localDateString(date));
}

function setEntryDateValue(value) {
  els.txDate.value = value;
  els.txDate.dispatchEvent(new Event('change', { bubbles: true }));
}

function updateKeyboardHintV11() {
  const hint = document.querySelector('.keyboard-hint');
  if (!hint) return;
  hint.innerHTML = '鍵盤：日期 Enter → 帳戶 Enter → 科目 Enter → 摘要 Enter → 金額 Enter 儲存　｜　<kbd>Tab</kbd> 切換收入／支出　｜　日期可輸入 <kbd>0924</kbd> / <kbd>20260924</kbd>，<kbd>Ctrl</kbd>+<kbd>↑↓</kbd> ±1 天';
}

function setupQuickEntrySettingsPane() {
  const nav = document.querySelector('.settings-nav');
  const content = document.querySelector('.settings-content');
  if (!nav || !content || document.querySelector('[data-settings-tab="quick"]')) return;

  const tab = document.createElement('button');
  tab.type = 'button';
  tab.className = 'settings-tab';
  tab.dataset.settingsTab = 'quick';
  tab.textContent = '快速輸入';

  const pane = document.createElement('section');
  pane.className = 'settings-pane';
  pane.dataset.settingsPane = 'quick';
  pane.innerHTML = `
    <h3>快速輸入設定</h3>
    <p class="hint">調整「常用摘要」的統計方式。常用摘要仍依帳戶、收入／支出與科目分開計算，最多顯示 10 個。</p>
    <div class="quick-settings-grid">
      <label><span>統計依據</span><select id="frequentSummaryBasis"><option value="tx_date">帳務日期最近</option><option value="created_at">近期輸入最近</option></select></label>
      <label><span>統計最近筆數</span><input id="frequentSummaryRecentCount" type="number" min="10" max="1000" step="10" inputmode="numeric"></label>
      <label><span>最低出現次數</span><input id="frequentSummaryMinCount" type="number" min="2" max="20" step="1" inputmode="numeric"></label>
    </div>
    <p class="hint quick-settings-explain"><strong>帳務日期最近：</strong>依記帳日期選取最近資料。　<strong>近期輸入最近：</strong>依實際新增時間選取最近資料。</p>
    <div id="quickEntrySettingsMessage" class="dialog-message"></div>
    <div class="quick-settings-actions"><button id="saveQuickEntrySettings" class="primary" type="button">儲存快速輸入設定</button></div>
  `;

  const lockTab = nav.querySelector('[data-settings-tab="lock"]');
  if (lockTab) nav.insertBefore(tab, lockTab); else nav.append(tab);
  const lockPane = content.querySelector('[data-settings-pane="lock"]');
  if (lockPane) content.insertBefore(pane, lockPane); else content.prepend(pane);

  els.settingsTabs?.push(tab);
  els.settingsPanes?.push(pane);

  tab.addEventListener('click', async () => {
    setSettingsTab('quick');
    await loadQuickEntrySettings(true);
  });
  pane.querySelector('#saveQuickEntrySettings')?.addEventListener('click', saveQuickEntrySettings);
}

async function loadQuickEntrySettings(force = false) {
  if (cyQuickEntrySettingsLoaded && !force) return;
  const message = document.querySelector('#quickEntrySettingsMessage');
  try {
    const data = await api('/api/settings/quick-entry');
    const settings = data.settings || {};
    const basis = document.querySelector('#frequentSummaryBasis');
    const recent = document.querySelector('#frequentSummaryRecentCount');
    const minimum = document.querySelector('#frequentSummaryMinCount');
    if (basis) basis.value = settings.basis || 'tx_date';
    if (recent) recent.value = String(settings.recentCount ?? 100);
    if (minimum) minimum.value = String(settings.minCount ?? 3);
    cyQuickEntrySettingsLoaded = true;
    if (message) setDialogMessage(message, '');
  } catch (error) {
    if (message) setDialogMessage(message, error.message, true);
  }
}

async function saveQuickEntrySettings() {
  const basis = document.querySelector('#frequentSummaryBasis')?.value || '';
  const recentCount = Number(document.querySelector('#frequentSummaryRecentCount')?.value);
  const minCount = Number(document.querySelector('#frequentSummaryMinCount')?.value);
  const button = document.querySelector('#saveQuickEntrySettings');
  const message = document.querySelector('#quickEntrySettingsMessage');

  if (!Number.isInteger(recentCount) || recentCount < 10 || recentCount > 1000) {
    return setDialogMessage(message, '最近筆數必須為 10～1000。', true);
  }
  if (!Number.isInteger(minCount) || minCount < 2 || minCount > 20 || minCount > recentCount) {
    return setDialogMessage(message, '最低出現次數必須為 2～20，且不可大於最近筆數。', true);
  }

  if (button) button.disabled = true;
  try {
    await api('/api/settings/quick-entry', {
      method: 'PUT',
      headers: jsonHeaders(),
      body: JSON.stringify({ basis, recentCount, minCount })
    });
    cyQuickEntrySettingsLoaded = true;
    setDialogMessage(message, '快速輸入設定已儲存。');
    if (typeof loadFrequentSummaries === 'function') await loadFrequentSummaries();
  } catch (error) {
    setDialogMessage(message, error.message, true);
  } finally {
    if (button) button.disabled = false;
  }
}

function setupOrderingControls() {
  if (els.accountRows) {
    els.accountRows.addEventListener('click', handleV11OrderAction);
    const observer = new MutationObserver(injectAccountOrderButtons);
    observer.observe(els.accountRows, { childList: true, subtree: true });
  }
  if (els.categoryManager) {
    els.categoryManager.addEventListener('click', handleV11OrderAction);
    const observer = new MutationObserver(injectCategoryOrderButtons);
    observer.observe(els.categoryManager, { childList: true, subtree: true });
  }
  injectAccountOrderButtons();
  injectCategoryOrderButtons();
}

function injectAccountOrderButtons() {
  const rows = [...(els.accountRows?.querySelectorAll('.manager-row') || [])];
  rows.forEach((row, index) => {
    const rename = row.querySelector('[data-account-rename]');
    const actions = row.querySelector('.manager-row-actions');
    if (!rename || !actions || actions.querySelector('[data-v11-move-account]')) return;
    const id = rename.dataset.accountRename;
    actions.prepend(orderButton('account', id, 'up', index === 0), orderButton('account', id, 'down', index === rows.length - 1));
  });
}

function injectCategoryOrderButtons() {
  const groups = [...(els.categoryManager?.querySelectorAll('.category-group') || [])];
  groups.forEach((groupElement, groupIndex) => {
    const renameGroup = groupElement.querySelector('.category-group-head [data-group-rename]');
    const groupActions = renameGroup?.parentElement;
    if (renameGroup && groupActions && !groupActions.querySelector('[data-v11-move-group]')) {
      const id = renameGroup.dataset.groupRename;
      groupActions.prepend(orderButton('group', id, 'up', groupIndex === 0), orderButton('group', id, 'down', groupIndex === groups.length - 1));
    }

    const items = [...groupElement.querySelectorAll('.category-item')];
    items.forEach((item, index) => {
      const rename = item.querySelector('[data-category-rename]');
      const actions = rename?.parentElement;
      if (!rename || !actions || actions.querySelector('[data-v11-move-category]')) return;
      const id = rename.dataset.categoryRename;
      actions.prepend(orderButton('category', id, 'up', index === 0), orderButton('category', id, 'down', index === items.length - 1));
    });
  });
}

function orderButton(type, id, direction, disabled) {
  const button = document.createElement('button');
  button.type = 'button';
  button.className = 'mini-button order-button';
  button.dataset[`v11Move${type[0].toUpperCase()}${type.slice(1)}`] = String(id);
  button.dataset.direction = direction;
  button.disabled = disabled;
  button.title = direction === 'up' ? '往上移' : '往下移';
  button.setAttribute('aria-label', button.title);
  button.textContent = direction === 'up' ? '↑' : '↓';
  return button;
}

async function handleV11OrderAction(event) {
  const button = event.target.closest('[data-v11-move-account], [data-v11-move-group], [data-v11-move-category]');
  if (!button || button.disabled) return;
  event.preventDefault();
  event.stopPropagation();

  let path = '';
  if (button.dataset.v11MoveAccount) path = `/api/accounts/${button.dataset.v11MoveAccount}/move`;
  else if (button.dataset.v11MoveGroup) path = `/api/category-groups/${button.dataset.v11MoveGroup}/move`;
  else if (button.dataset.v11MoveCategory) path = `/api/categories/${button.dataset.v11MoveCategory}/move`;
  if (!path) return;

  button.disabled = true;
  setDialogMessage(els.settingsMessage, '');
  try {
    await api(path, {
      method: 'PUT',
      headers: jsonHeaders(),
      body: JSON.stringify({ direction: button.dataset.direction })
    });
    await refreshBootstrap();
    setDialogMessage(els.settingsMessage, '排序已更新。');
  } catch (error) {
    setDialogMessage(els.settingsMessage, error.message, true);
  }
}

/* ---- baseline section ---- */
let cyCategoryTransferId = null;

window.addEventListener('load', () => {
  setupCategoryTransfer();
  setupSequentialLockControls();
});

function setupCategoryTransfer() {
  if (!els.categoryManager) return;
  if (!window.__CYACC_BASELINE_BUNDLE__) ensureCategoryTransferDialog();
  els.categoryManager.addEventListener('click', event => {
    const button = event.target.closest('[data-v12-category-transfer]');
    if (!button) return;
    event.preventDefault();
    event.stopPropagation();
    openCategoryTransfer(Number(button.dataset.v12CategoryTransfer));
  });

  const observer = new MutationObserver(injectCategoryTransferButtons);
  observer.observe(els.categoryManager, { childList: true, subtree: true });
  injectCategoryTransferButtons();
}

function injectCategoryTransferButtons() {
  for (const itemElement of els.categoryManager?.querySelectorAll('.category-item') || []) {
    const rename = itemElement.querySelector('[data-category-rename]');
    const actions = rename?.parentElement;
    if (!rename || !actions || actions.querySelector('[data-v12-category-transfer]')) continue;

    const id = Number(rename.dataset.categoryRename);
    const category = state.categories.find(item => Number(item.id) === id);
    if (!category) continue;
    const targetGroups = state.groups.filter(group =>
      group.kind === category.kind && Number(group.id) !== Number(category.group_id)
    );
    if (!targetGroups.length) continue;

    const button = document.createElement('button');
    button.type = 'button';
    button.className = 'mini-button category-transfer-button';
    button.dataset.v12CategoryTransfer = String(id);
    button.textContent = '移動';
    button.title = '移動到其他大分類';
    actions.append(document.createTextNode(' '), button);
  }
}

function ensureCategoryTransferDialog() {
  if (document.querySelector('#categoryTransferDialog')) return;
  const dialog = document.createElement('dialog');
  dialog.id = 'categoryTransferDialog';
  dialog.className = 'modal small-modal category-transfer-dialog';
  dialog.innerHTML = `
    <div class="modal-header">
      <div><h2>移動科目</h2><p id="categoryTransferDescription"></p></div>
      <button class="icon-button" type="button" data-v12-transfer-close aria-label="關閉">×</button>
    </div>
    <div class="form-grid">
      <label><span>移動到大分類</span><select id="categoryTransferTarget"></select></label>
    </div>
    <p class="hint category-transfer-note">只可在相同收支類型的大分類之間移動；既有歷史記帳資料不會被改寫。</p>
    <div id="categoryTransferMessage" class="dialog-message"></div>
    <div class="modal-actions">
      <button class="secondary" type="button" data-v12-transfer-close>取消</button>
      <button id="categoryTransferConfirm" class="primary" type="button">確認移動</button>
    </div>
  `;
  document.body.append(dialog);
  dialog.querySelectorAll('[data-v12-transfer-close]').forEach(button =>
    button.addEventListener('click', () => dialog.close())
  );
  dialog.querySelector('#categoryTransferConfirm')?.addEventListener('click', confirmCategoryTransfer);
}

function openCategoryTransfer(id) {
  const category = state.categories.find(item => Number(item.id) === Number(id));
  if (!category) return;
  const targetGroups = state.groups.filter(group =>
    group.kind === category.kind && Number(group.id) !== Number(category.group_id)
  );
  if (!targetGroups.length) {
    setDialogMessage(els.settingsMessage, '目前沒有其他可移動的大分類。', true);
    return;
  }

  cyCategoryTransferId = Number(id);
  const dialog = document.querySelector('#categoryTransferDialog');
  const description = dialog?.querySelector('#categoryTransferDescription');
  const target = dialog?.querySelector('#categoryTransferTarget');
  const message = dialog?.querySelector('#categoryTransferMessage');
  if (!dialog || !target) return;

  if (description) description.textContent = `「${category.name}」目前位於「${category.group_name}」。`;
  target.innerHTML = targetGroups.map(group =>
    `<option value="${Number(group.id)}">${escapeHtml(group.name)}</option>`
  ).join('');
  if (message) setDialogMessage(message, '');
  dialog.showModal();
  target.focus();
}

async function confirmCategoryTransfer() {
  const dialog = document.querySelector('#categoryTransferDialog');
  const target = dialog?.querySelector('#categoryTransferTarget');
  const message = dialog?.querySelector('#categoryTransferMessage');
  const button = dialog?.querySelector('#categoryTransferConfirm');
  const groupId = Number(target?.value);
  if (!Number.isInteger(cyCategoryTransferId) || cyCategoryTransferId <= 0 || !Number.isInteger(groupId) || groupId <= 0) {
    return setDialogMessage(message, '移動資料不完整。', true);
  }

  if (button) button.disabled = true;
  try {
    const result = await api(`/api/categories/${cyCategoryTransferId}/group`, {
      method: 'PUT',
      headers: jsonHeaders(),
      body: JSON.stringify({ groupId })
    });
    await refreshBootstrap();
    dialog.close();
    setDialogMessage(els.settingsMessage, result.moved === false ? '科目已位於該大分類。' : `科目已移動到「${result.groupName || ''}」。`);
  } catch (error) {
    setDialogMessage(message, error.message, true);
  } finally {
    if (button) button.disabled = false;
  }
}

function setupSequentialLockControls() {
  const pane = document.querySelector('[data-settings-pane="lock"]');
  const form = pane?.querySelector('.lock-form');
  if (!pane || !form || document.querySelector('#lockStepControls')) return;

  const controls = document.createElement('div');
  controls.id = 'lockStepControls';
  controls.className = 'lock-step-panel';
  controls.innerHTML = `
    <div class="lock-step-copy">
      <strong>逐月鎖帳</strong>
      <span>日常操作建議使用逐月前進；上方直接指定月份仍保留給管理者調整。</span>
    </div>
    <div class="lock-step-actions">
      <button id="lockStepBackward" class="secondary compact" type="button">← 退回一個月</button>
      <button id="lockStepForward" class="primary compact" type="button">鎖定下一個月 →</button>
    </div>
  `;
  form.insertAdjacentElement('afterend', controls);

  controls.querySelector('#lockStepBackward')?.addEventListener('click', () => stepLock('backward'));
  controls.querySelector('#lockStepForward')?.addEventListener('click', () => stepLock('forward'));

  const observer = new MutationObserver(updateLockStepUi);
  if (els.lockStatusText) observer.observe(els.lockStatusText, { childList: true, subtree: true, characterData: true });
  updateLockStepUi();
}

function updateLockStepUi() {
  const backward = document.querySelector('#lockStepBackward');
  const forward = document.querySelector('#lockStepForward');
  if (backward) backward.disabled = !state.lockedThrough;

  const currentMonth = localDateString(new Date()).slice(0, 7);
  if (forward) {
    const atCurrentMonth = Boolean(state.lockedThrough && state.lockedThrough >= currentMonth);
    forward.disabled = atCurrentMonth;
    forward.title = atCurrentMonth ? '逐月鎖帳已到本月；若需特殊調整請使用上方直接指定月份。' : '';
  }
}

async function stepLock(direction) {
  const backward = document.querySelector('#lockStepBackward');
  const forward = document.querySelector('#lockStepForward');
  if (backward) backward.disabled = true;
  if (forward) forward.disabled = true;
  setDialogMessage(els.settingsMessage, '');

  try {
    const data = await api('/api/settings/lock/step', {
      method: 'PUT',
      headers: jsonHeaders(),
      body: JSON.stringify({ direction })
    });
    state.lockedThrough = data.lockedThrough || null;
    els.lockedThrough.value = state.lockedThrough || '';
    renderSettings();
    setDialogMessage(els.settingsMessage, data.message || '鎖帳設定已更新。');
    await loadTransactions();
  } catch (error) {
    setDialogMessage(els.settingsMessage, error.message, true);
  } finally {
    updateLockStepUi();
  }
}

/* ---- baseline section ---- */
window.addEventListener('load', () => {
  setupMonthlyExcelExport();
});

function setupMonthlyExcelExport() {
  const tools = document.querySelector('.ledger-view-tools');
  if (!tools || document.querySelector('#ledgerExcelExport')) return;

  const button = document.createElement('button');
  button.id = 'ledgerExcelExport';
  button.className = 'secondary compact';
  button.type = 'button';
  button.textContent = '匯出 Excel';
  button.title = '匯出目前月份完整帳簿（.xlsx）';

  const status = document.createElement('span');
  status.id = 'ledgerExcelExportStatus';
  status.className = 'ledger-export-status';
  status.setAttribute('aria-live', 'polite');

  tools.prepend(button, status);
  button.addEventListener('click', downloadMonthlyExcel);
}

async function downloadMonthlyExcel() {
  const button = document.querySelector('#ledgerExcelExport');
  const month = els.monthFilter?.value || '';
  if (!/^\d{4}-\d{2}$/.test(month)) {
    setExportStatus('請先選擇月份。', true);
    return;
  }

  const defaultLabel = '匯出 Excel';
  let failed = false;
  if (button) {
    button.disabled = true;
    button.textContent = '匯出中…';
    button.setAttribute('aria-busy', 'true');
  }
  setExportStatus('正在產生 Excel 檔案…');

  try {
    const response = await fetch(`/api/export/month.xlsx?month=${encodeURIComponent(month)}`, {
      method: 'GET',
      headers: { accept: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet' },
      cache: 'no-store'
    });

    if (!response.ok) {
      const data = await response.json().catch(() => null);
      throw new Error(data?.error || `匯出失敗（HTTP ${response.status}）。`);
    }

    const blob = await response.blob();
    if (!blob.size) throw new Error('匯出檔案內容為空。');

    const url = URL.createObjectURL(blob);
    const link = document.createElement('a');
    link.href = url;
    link.download = `CYAccounting_${month}.xlsx`;
    document.body.append(link);
    link.click();
    link.remove();
    setTimeout(() => URL.revokeObjectURL(url), 1000);
    setExportStatus('Excel 已下載。');
  } catch (error) {
    failed = true;
    setExportStatus(error?.message || 'Excel 匯出失敗。', true);
  } finally {
    if (button) {
      button.disabled = false;
      button.removeAttribute('aria-busy');
      button.textContent = failed ? '匯出失敗' : defaultLabel;
      if (failed) {
        setTimeout(() => {
          if (button.textContent === '匯出失敗') button.textContent = defaultLabel;
        }, 2200);
      }
    }
  }
}

function setExportStatus(message, isError = false) {
  const status = document.querySelector('#ledgerExcelExportStatus');
  if (!status) return;
  status.textContent = message || '';
  status.classList.toggle('error', Boolean(message && isError));
}

/* ---- baseline section ---- */
let cyV14InlineEdit = null;
let cyV14ObserverSuspended = false;

window.addEventListener('load', () => {
  setupInlineLedgerEditing();
});

function setupInlineLedgerEditing() {
  if (!els.transactionRows || els.transactionRows.dataset.v14InlineEdit === '1') return;
  els.transactionRows.dataset.v14InlineEdit = '1';

  els.transactionRows.addEventListener('click', handleInlineLedgerClick, true);
  els.monthFilter?.addEventListener('change', () => cancelInlineLedgerEdit(false), true);

  document.querySelector('#ledgerDesktopTools')?.addEventListener('click', () => {
    if (cyV14InlineEdit) cancelInlineLedgerEdit(true);
  }, true);
}

function handleInlineLedgerClick(event) {
  const editButton = event.target.closest('[data-edit-id]');
  if (editButton) {
    event.preventDefault();
    event.stopImmediatePropagation();
    beginInlineLedgerEdit(Number(editButton.dataset.editId), editButton.closest('tr'));
    return;
  }

  const saveButton = event.target.closest('[data-inline-save]');
  if (saveButton) {
    event.preventDefault();
    event.stopImmediatePropagation();
    saveInlineLedgerEdit();
    return;
  }

  const cancelButton = event.target.closest('[data-inline-cancel]');
  if (cancelButton) {
    event.preventDefault();
    event.stopImmediatePropagation();
    cancelInlineLedgerEdit(true);
    return;
  }

  const deleteButton = event.target.closest('[data-delete-id]');
  if (deleteButton && cyV14InlineEdit) cancelInlineLedgerEdit(true);
}

function beginInlineLedgerEdit(id, row) {
  if (!Number.isInteger(id) || !row) return;
  const tx = state.transactions.find(item => Number(item.id) === id);
  if (!tx || isLocked(String(tx.tx_date || '').slice(0, 7))) return;

  if (cyV14InlineEdit?.id === id) {
    row.querySelector('[data-inline-summary]')?.focus();
    return;
  }

  if (cyV14InlineEdit) restoreInlineLedgerRow(false);
  suspendLedgerRefreshObserver();

  cyV14InlineEdit = {
    id,
    tx,
    row,
    originalHtml: row.innerHTML
  };

  row.classList.add('inline-editing');
  row.innerHTML = inlineEditRowHtml(tx);
  row.addEventListener('keydown', handleInlineLedgerKeydown);
  row.querySelector('[data-inline-amount]')?.addEventListener('input', event => {
    event.target.value = String(event.target.value || '').replace(/[^0-9]/g, '').slice(0, 7);
  });
  row.querySelector('[data-inline-summary]')?.focus();
  row.querySelector('[data-inline-summary]')?.select();
}

function inlineEditRowHtml(tx) {
  return `
    <td><input class="inline-edit-control inline-edit-date" data-inline-date type="date" value="${v14Escape(tx.tx_date || '')}" aria-label="日期"></td>
    <td><select class="inline-edit-control" data-inline-account aria-label="帳戶">${inlineAccountOptions(tx.account_name)}</select></td>
    <td><span class="kind-tag ${tx.kind}">${tx.kind === 'income' ? '收入' : '支出'}</span></td>
    <td><select class="inline-edit-control" data-inline-category aria-label="科目">${inlineCategoryOptions(tx)}</select></td>
    <td class="summary"><input class="inline-edit-control inline-edit-summary" data-inline-summary type="text" maxlength="100" value="${v14Escape(tx.summary || '')}" aria-label="摘要"></td>
    <td class="num"><input class="inline-edit-control inline-edit-amount" data-inline-amount type="text" inputmode="numeric" maxlength="7" value="${v14Escape(String(tx.amount ?? ''))}" aria-label="金額"></td>
    <td class="num inline-edit-balance">儲存後重算</td>
    <td class="action-col inline-edit-action-cell">
      <div class="inline-edit-actions">
        <button type="button" class="row-action inline-save" data-inline-save>儲存</button>
        <button type="button" class="row-action" data-inline-cancel>取消</button>
      </div>
      <span class="inline-edit-message" data-inline-message aria-live="polite"></span>
    </td>`;
}

function inlineAccountOptions(current) {
  const names = [...new Set((state.accounts || []).map(item => String(item.name || '')).filter(Boolean))];
  if (current && !names.includes(current)) names.unshift(current);
  return names.map(name => {
    const historical = !state.accounts.some(item => item.name === name);
    return `<option value="${v14Escape(name)}" ${name === current ? 'selected' : ''}>${historical ? '（歷史）' : ''}${v14Escape(name)}</option>`;
  }).join('');
}

function inlineCategoryOptions(tx) {
  const categories = (state.categories || []).filter(item => item.kind === tx.kind);
  const names = new Set(categories.map(item => String(item.name || '')));
  const options = [];
  if (tx.category_name && !names.has(tx.category_name)) {
    options.push(`<option value="${v14Escape(tx.category_name)}" selected>（歷史）${v14Escape(tx.category_name)}</option>`);
  }
  for (const category of categories) {
    const name = String(category.name || '');
    const group = String(category.group_name || '');
    const label = group ? `${group}｜${name}` : name;
    options.push(`<option value="${v14Escape(name)}" ${name === tx.category_name ? 'selected' : ''}>${v14Escape(label)}</option>`);
  }
  return options.join('');
}

function handleInlineLedgerKeydown(event) {
  if (!cyV14InlineEdit || event.isComposing) return;
  if (event.key === 'Escape') {
    event.preventDefault();
    cancelInlineLedgerEdit(true);
    return;
  }
  if (event.key !== 'Enter') return;

  event.preventDefault();
  if (event.target.closest('[data-inline-cancel]')) {
    cancelInlineLedgerEdit(true);
    return;
  }
  saveInlineLedgerEdit();
}

async function saveInlineLedgerEdit() {
  const active = cyV14InlineEdit;
  if (!active?.row?.isConnected) {
    cancelInlineLedgerEdit(false);
    return;
  }

  const date = active.row.querySelector('[data-inline-date]')?.value || '';
  const accountName = active.row.querySelector('[data-inline-account]')?.value || '';
  const categoryName = active.row.querySelector('[data-inline-category]')?.value || '';
  const summary = active.row.querySelector('[data-inline-summary]')?.value.trim() || '';
  const amountInput = active.row.querySelector('[data-inline-amount]');
  const amount = Number(String(amountInput?.value || '').replace(/[^0-9]/g, ''));
  const message = active.row.querySelector('[data-inline-message]');

  if (!/^\d{4}-\d{2}-\d{2}$/.test(date)) return setInlineEditMessage(message, '日期不正確');
  if (!accountName) return setInlineEditMessage(message, '請選帳戶');
  if (!categoryName) return setInlineEditMessage(message, '請選科目');
  if (!Number.isInteger(amount) || amount < 1 || amount > 9_999_999) {
    amountInput?.focus();
    return setInlineEditMessage(message, '金額 1～9,999,999');
  }

  const saveButton = active.row.querySelector('[data-inline-save]');
  const cancelButton = active.row.querySelector('[data-inline-cancel]');
  if (saveButton) saveButton.disabled = true;
  if (cancelButton) cancelButton.disabled = true;
  setInlineEditMessage(message, '儲存中…', false);

  try {
    await api(`/api/transactions/${active.id}`, {
      method: 'PUT',
      headers: jsonHeaders(),
      body: JSON.stringify({ txDate: date, accountName, categoryName, summary, amount })
    });

    cyV14InlineEdit = null;
    resumeLedgerRefreshObserver();
    showMessage('修改成功');
    await loadTransactions();
    if (typeof scheduleLedgerDesktopRefresh === 'function') scheduleLedgerDesktopRefresh();
  } catch (error) {
    if (saveButton) saveButton.disabled = false;
    if (cancelButton) cancelButton.disabled = false;
    setInlineEditMessage(message, error?.message || '修改失敗');
  }
}

function cancelInlineLedgerEdit(restore = true) {
  if (!cyV14InlineEdit) {
    resumeLedgerRefreshObserver();
    return;
  }
  if (restore) restoreInlineLedgerRow(false);
  else cyV14InlineEdit = null;
  resumeLedgerRefreshObserver();
}

function restoreInlineLedgerRow(resume = true) {
  const active = cyV14InlineEdit;
  if (active?.row?.isConnected) {
    active.row.removeEventListener('keydown', handleInlineLedgerKeydown);
    active.row.classList.remove('inline-editing');
    active.row.innerHTML = active.originalHtml;
  }
  cyV14InlineEdit = null;
  if (resume) resumeLedgerRefreshObserver();
}

function suspendLedgerRefreshObserver() {
  if (cyV14ObserverSuspended) return;
  if (typeof cyLedgerObserver !== 'undefined' && cyLedgerObserver) {
    cyLedgerObserver.disconnect();
    cyV14ObserverSuspended = true;
  }
}

function resumeLedgerRefreshObserver() {
  if (!cyV14ObserverSuspended) return;
  if (typeof cyLedgerObserver !== 'undefined' && cyLedgerObserver && els.transactionRows) {
    cyLedgerObserver.observe(els.transactionRows, { childList: true, subtree: true });
  }
  cyV14ObserverSuspended = false;
}

function setInlineEditMessage(element, message, isError = true) {
  if (!element) return;
  element.textContent = message || '';
  element.classList.toggle('error', Boolean(message && isError));
}

function v14Escape(value) {
  return String(value ?? '')
    .replaceAll('&', '&amp;')
    .replaceAll('<', '&lt;')
    .replaceAll('>', '&gt;')
    .replaceAll('"', '&quot;')
    .replaceAll("'", '&#39;');
}

/* ---- baseline section ---- */
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
  document.querySelector('#excelImportMappingGrid')?.addEventListener('change', resetImportPreviewV15);
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
    if (target && normalized.includes(target)) return 2;
  }
  return 0;
}

function normalizeHeaderV15(value) {
  return String(value ?? '').trim().toLowerCase().replace(/[\s_\-()（）\[\]【】]/g, '');
}

async function buildExcelImportPreviewV15() {
  resetImportPreviewV15();
  const dataRowCount = Math.max(0, cyV15ImportState.rows.length - cyV15ImportState.headerRow);
  if (dataRowCount > 5000) return setImportMessageV15('單次最多匯入 5,000 筆；請先分割工作表。', true);

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
  const cleaned = text.replace(/[,$＄元\s]/g, '').replace(/^nt/i, '');
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
    if (cyV15ImportState.preview) cyV15ImportState.preview.canCommit = false;
    const commitButton = document.querySelector('#excelImportCommitButton');
    if (commitButton) { commitButton.disabled = true; commitButton.textContent = '匯入完成'; }
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
  if (!busy) {
    const commit = document.querySelector('#excelImportCommitButton');
    if (commit) commit.disabled = !cyV15ImportState.preview?.canCommit;
  }
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

/* ---- baseline section ---- */
window.addEventListener('load', () => {
  const version = document.querySelector('.version');
  if (version) version.textContent = 'V0.18.0';
  setupBackupSettingsV17();
});

let backupTopologyV18 = 'legacy_gcs';

async function setupBackupSettingsV17() {
  const user = await sharedSessionUserV17();
  if (user?.role !== 'SUPER_ADMIN') return;

  const nav = document.querySelector('.settings-nav');
  const content = document.querySelector('.settings-content');
  if (!nav || !content || document.querySelector('[data-settings-tab="backup"]')) return;

  const tab = document.createElement('button');
  tab.type = 'button';
  tab.className = 'settings-tab';
  tab.dataset.settingsTab = 'backup';
  tab.textContent = '備份／復原';
  nav.append(tab);

  const pane = document.createElement('section');
  pane.className = 'settings-pane backup-settings-pane';
  pane.dataset.settingsPane = 'backup';
  pane.innerHTML = backupSettingsHtmlV17();
  content.append(pane);

  els.settingsTabs?.push(tab);
  els.settingsPanes?.push(pane);

  tab.addEventListener('click', async () => {
    setSettingsTab('backup');
    await loadBackupStatusV17();
  });
  pane.querySelector('#backupRunNow')?.addEventListener('click', runBackupNowV17);
  pane.querySelector('#backupRefreshStatus')?.addEventListener('click', loadBackupStatusV17);
}

async function sharedSessionUserV17() {
  if (window.cyaccCurrentUser) return window.cyaccCurrentUser;
  try {
    return window.cyaccSessionPromise ? await window.cyaccSessionPromise : null;
  } catch {
    return null;
  }
}

function backupSettingsHtmlV17() {
  return `
    <div class="backup-heading">
      <div>
        <h3 id="backupHeadingTitle">自動備份</h3>
        <p id="backupHeadingHint" class="hint">Cloudflare D1 是正式資料來源；正在讀取備份拓撲。</p>
      </div>
      <button id="backupRefreshStatus" class="secondary compact" type="button">重新整理</button>
    </div>

    <div id="backupMessage" class="dialog-message"></div>

    <div class="backup-status-grid">
      <article class="backup-status-card">
        <span id="backupPrimaryLabel" class="backup-card-label">備份拓撲</span>
        <strong id="backupConnectionState">讀取中…</strong>
        <span id="backupAccountText" class="hint">—</span>
      </article>
      <article class="backup-status-card">
        <span class="backup-card-label">自動排程</span>
        <strong id="backupScheduleText">每日 03:30</strong>
        <span class="hint">台灣時間；無須人工按備份</span>
      </article>
      <article class="backup-status-card">
        <span class="backup-card-label">最近有效備份</span>
        <strong id="backupLastTime">尚無</strong>
        <span id="backupLastDetail" class="hint">—</span>
      </article>
    </div>

    <div id="backupProviderHealth" class="backup-provider-health"></div>

    <div class="backup-actions-row">
      <button id="backupRunNow" class="primary" type="button" disabled>立即執行測試備份</button>
      <span id="backupRetentionText" class="hint">讀取保留政策中…</span>
    </div>

    <div class="backup-security-note">
      <strong>安全設計</strong>
      <span>D1 只 export 一次，同一份 immutable backup-set bytes 交給各 storage provider；每個 copy 上傳後都必須回讀並通過 SHA-256 / byte size / manifest 驗證才記為成功。</span>
    </div>

    <div class="backup-history-block">
      <div class="backup-history-title"><strong>最近 logical backup</strong><span class="hint">一筆 logical backup 對應各 provider copy health</span></div>
      <div class="backup-history-table-wrap">
        <table class="backup-history-table">
          <thead><tr><th>時間</th><th>方式</th><th>整體</th><th>備份 ID／錯誤</th><th>R2</th><th>GCS</th><th class="num">資料筆數</th><th class="num">大小</th></tr></thead>
          <tbody id="backupHistoryRows"><tr><td colspan="8" class="empty">讀取中…</td></tr></tbody>
        </table>
      </div>
    </div>

    <div class="backup-restore-note">
      <strong>復原</strong>
      <span>復原功能尚未開放；後續只允許 <code>SUPER_ADMIN</code> 使用，採雙重確認並在覆蓋 D1 前再次驗證備份格式、版本與完整性。</span>
    </div>`;
}

async function loadBackupStatusV17() {
  const run = document.querySelector('#backupRunNow');
  if (run) run.disabled = true;
  setBackupMessageV17('');

  try {
    const data = await api('/api/backup/status');
    renderBackupStatusV17(data);
  } catch (error) {
    setBackupMessageV17(error.message || '無法讀取備份狀態。', true);
    const state = document.querySelector('#backupConnectionState');
    if (state) state.textContent = '狀態讀取失敗';
  }
}

function backupUiModelV18(data) {
  const tiered = data?.topology === 'parallel_dual_provider' || data?.provider === 'tiered';
  const providers = data?.providers || {};
  const r2 = providers.cloudflare_r2 || {};
  const gcs = providers.google_cloud_storage || {};
  const logicalBackups = Array.isArray(data?.logicalBackups) ? data.logicalBackups : [];
  const latestLogical = logicalBackups.find(item => item?.status === 'success') || logicalBackups[0] || null;

  if (tiered) {
    return {
      tiered: true,
      topology: 'parallel_dual_provider',
      configured: Boolean(data?.configured),
      heading: 'R2 + GCS 分層自動備份',
      hint: 'Cloudflare D1 是正式資料來源；R2 作日常 operational backup，GCS 在 Phase C 同步驗證 cross-cloud copy。',
      primaryLabel: '備份拓撲',
      state: data?.configured ? '雙 Provider 已啟用' : 'Provider 尚未完整設定',
      account: data?.configured ? 'R2 + GCS 均已完成設定' : '請檢查 R2 binding 與 GCS Secrets',
      schedule: data?.schedule?.localTime || '每日 03:30（台灣時間）',
      retention: `R2 ${Number(r2.retentionDays || 30)} 天｜GCS ${Number(gcs.retentionDays || 14)} 天（Phase C）`,
      latest: latestLogical,
      r2: {
        configured: Boolean(r2.configured),
        retentionDays: Number(r2.retentionDays || 30),
        role: String(r2.role || 'operational')
      },
      gcs: {
        configured: Boolean(gcs.configured),
        retentionDays: Number(gcs.retentionDays || 14),
        role: String(gcs.role || 'cross_cloud_validation')
      },
      logicalBackups
    };
  }

  return {
    tiered: false,
    topology: 'legacy_gcs',
    configured: Boolean(data?.configured),
    heading: 'Google Cloud Storage 自動備份',
    hint: 'Cloudflare D1 是正式資料來源；Cloud Storage 作異地／災難復原備份。',
    primaryLabel: 'Cloud Storage',
    state: data?.configured ? '已完成設定' : '尚未完成 Cloudflare Secrets',
    account: data?.configured ? 'Bucket 與專用 Service Account 已設定' : '需要 GCS_BUCKET、GCS_SERVICE_ACCOUNT_JSON',
    schedule: data?.schedule?.localTime || '每日 03:30（台灣時間）',
    retention: `GCS ${Number(data?.retentionDays || 14)} 天`,
    latest: data?.latestSuccess || null,
    recentRuns: Array.isArray(data?.recentRuns) ? data.recentRuns : []
  };
}

function renderBackupStatusV17(data) {
  const model = backupUiModelV18(data);
  backupTopologyV18 = model.topology;

  const heading = document.querySelector('#backupHeadingTitle');
  const hint = document.querySelector('#backupHeadingHint');
  const primaryLabel = document.querySelector('#backupPrimaryLabel');
  const state = document.querySelector('#backupConnectionState');
  const account = document.querySelector('#backupAccountText');
  const schedule = document.querySelector('#backupScheduleText');
  const lastTime = document.querySelector('#backupLastTime');
  const lastDetail = document.querySelector('#backupLastDetail');
  const retention = document.querySelector('#backupRetentionText');
  const run = document.querySelector('#backupRunNow');

  if (heading) heading.textContent = model.heading;
  if (hint) hint.textContent = model.hint;
  if (primaryLabel) primaryLabel.textContent = model.primaryLabel;
  if (state) state.textContent = model.state;
  if (account) account.textContent = model.account;
  if (schedule) schedule.textContent = model.schedule;
  if (retention) retention.textContent = model.retention;

  if (model.tiered) {
    const latest = model.latest;
    if (lastTime) lastTime.textContent = latest?.createdAt ? backupLocalDateTimeV17(latest.createdAt) : '尚無';
    if (lastDetail) {
      lastDetail.textContent = latest
        ? `${Number(latest.rowCount || 0).toLocaleString()} 筆 · ${backupBytesV17(latest.byteSize || 0)} · Package SHA ${String(latest.packageSha256 || '').slice(0, 10)}…`
        : (model.configured ? '可執行一次 paired backup 驗證 R2 + GCS' : '完成兩個 provider 設定後即可測試');
    }
    renderBackupProviderHealthV18(model, latest);
    renderTieredBackupHistoryV18(model.logicalBackups);
  } else {
    const latest = model.latest;
    if (lastTime) lastTime.textContent = latest?.completedAt ? backupLocalDateTimeV17(latest.completedAt) : '尚無';
    if (lastDetail) {
      lastDetail.textContent = latest
        ? `${Number(latest.rowCount || 0).toLocaleString()} 筆 · ${backupBytesV17(latest.byteSize || 0)} · SHA ${String(latest.fileSha256 || '').slice(0, 10)}…`
        : (model.configured ? '可先執行一次測試備份確認 GCS 權限與讀回驗證' : '完成 Cloudflare Secrets 後即可測試');
    }
    renderLegacyProviderHealthV18(model);
    renderBackupHistoryV17(model.recentRuns || []);
  }

  if (run) run.disabled = !model.configured;
}

function renderBackupProviderHealthV18(model, latest) {
  const container = document.querySelector('#backupProviderHealth');
  if (!container) return;
  const copies = Array.isArray(latest?.copies) ? latest.copies : [];
  const r2Copy = copies.find(copy => copy.provider === 'cloudflare_r2');
  const gcsCopy = copies.find(copy => copy.provider === 'google_cloud_storage');

  container.innerHTML = [
    providerHealthCardV18('Cloudflare R2', 'Operational backup', model.r2, r2Copy),
    providerHealthCardV18('Google Cloud Storage', 'Cross-cloud validation', model.gcs, gcsCopy)
  ].join('');
}

function renderLegacyProviderHealthV18(model) {
  const container = document.querySelector('#backupProviderHealth');
  if (!container) return;
  container.innerHTML = providerHealthCardV18(
    'Google Cloud Storage',
    'Legacy production / rollback path',
    { configured: model.configured, retentionDays: Number(String(model.retention).match(/\d+/)?.[0] || 14) },
    null
  );
}

function providerHealthCardV18(name, role, provider, copy) {
  const status = copy?.status || (provider?.configured ? 'configured' : 'not_configured');
  const badge = status === 'success' ? '成功' : status === 'failed' ? '失敗' : provider?.configured ? '已設定' : '未設定';
  const badgeClass = status === 'success' ? 'success' : status === 'failed' ? 'failed' : 'neutral';
  const verified = copy?.verifiedAt ? ` · 驗證 ${backupLocalDateTimeV17(copy.verifiedAt)}` : '';
  const detail = copy?.lastError || `${Number(provider?.retentionDays || 0)} 天${verified}`;
  return `<article class="backup-provider-card">
    <div class="backup-provider-card-head"><div><strong>${v17Escape(name)}</strong><span>${v17Escape(role)}</span></div><span class="backup-run-badge ${badgeClass}">${badge}</span></div>
    <div class="backup-provider-card-detail">${v17Escape(detail)}</div>
  </article>`;
}

function renderBackupHistoryV17(runs) {
  const tbody = document.querySelector('#backupHistoryRows');
  if (!tbody) return;
  if (!runs.length) {
    tbody.innerHTML = '<tr><td colspan="8" class="empty">尚無 Cloud Storage 備份執行紀錄。</td></tr>';
    return;
  }

  tbody.innerHTML = runs.map(run => {
    const success = run.status === 'success';
    const detail = success ? run.fileName : (run.errorMessage || '備份失敗');
    return `<tr>
      <td>${v17Escape(backupLocalDateTimeV17(run.completedAt || run.startedAt))}</td>
      <td>${run.trigger === 'scheduled' ? '自動' : '手動測試'}</td>
      <td><span class="backup-run-badge ${success ? 'success' : 'failed'}">${success ? '成功' : '失敗'}</span></td>
      <td class="backup-run-detail" title="${v17Escape(detail)}">${v17Escape(detail)}</td>
      <td class="backup-copy-cell">—</td>
      <td class="backup-copy-cell"><span class="backup-run-badge ${success ? 'success' : 'failed'}">${success ? '成功' : '失敗'}</span></td>
      <td class="num">${Number(run.rowCount || 0).toLocaleString()}</td>
      <td class="num">${run.byteSize ? backupBytesV17(run.byteSize) : '—'}</td>
    </tr>`;
  }).join('');
}

function renderTieredBackupHistoryV18(backups) {
  const tbody = document.querySelector('#backupHistoryRows');
  if (!tbody) return;
  if (!backups.length) {
    tbody.innerHTML = '<tr><td colspan="8" class="empty">尚無 paired backup 執行紀錄。</td></tr>';
    return;
  }

  tbody.innerHTML = backups.map(item => {
    const copies = Array.isArray(item.copies) ? item.copies : [];
    const r2 = copies.find(copy => copy.provider === 'cloudflare_r2');
    const gcs = copies.find(copy => copy.provider === 'google_cloud_storage');
    const overall = item.status === 'success';
    const failed = copies.find(copy => copy.status !== 'success');
    const detail = failed?.lastError || item.backupId || '—';
    return `<tr>
      <td>${v17Escape(backupLocalDateTimeV17(item.createdAt))}</td>
      <td>${item.trigger === 'scheduled' ? '自動' : '手動測試'}</td>
      <td><span class="backup-run-badge ${overall ? 'success' : 'failed'}">${overall ? '成功' : '部分失敗'}</span></td>
      <td class="backup-run-detail" title="${v17Escape(detail)}">${v17Escape(item.backupId || detail)}</td>
      <td class="backup-copy-cell">${copyBadgeV18(r2)}</td>
      <td class="backup-copy-cell">${copyBadgeV18(gcs)}</td>
      <td class="num">${Number(item.rowCount || 0).toLocaleString()}</td>
      <td class="num">${item.byteSize ? backupBytesV17(item.byteSize) : '—'}</td>
    </tr>`;
  }).join('');
}

function copyBadgeV18(copy) {
  if (!copy) return '<span class="backup-run-badge neutral">—</span>';
  const success = copy.status === 'success';
  return `<span class="backup-run-badge ${success ? 'success' : 'failed'}" title="${v17Escape(copy.lastError || copy.verifiedAt || '')}">${success ? '成功' : '失敗'}</span>`;
}

async function runBackupNowV17() {
  const tiered = backupTopologyV18 === 'parallel_dual_provider';
  const target = tiered ? 'R2 + GCS paired backup' : 'Google Cloud Storage 測試備份';
  if (!confirm(`現在立即執行一次 ${target}？\n正常每日備份仍會在排程時間自動執行。`)) return;
  const button = document.querySelector('#backupRunNow');
  if (button) button.disabled = true;
  setBackupMessageV17(tiered
    ? '正在建立單一 BackupSet、寫入 R2 + GCS 並逐一回讀驗證…'
    : '正在建立 data.json／manifest.json、上傳並回讀驗證…');
  try {
    const data = await api('/api/backup/run', {
      method: 'POST',
      headers: jsonHeaders(),
      body: '{}'
    });
    setBackupMessageV17(`備份完成：${data.backup?.backupId || data.backup?.fileName || ''}`);
    await loadBackupStatusV17();
  } catch (error) {
    setBackupMessageV17(error.message || '測試備份失敗。', true);
    await loadBackupStatusV17();
  }
}

function setBackupMessageV17(message, isError = false) {
  const element = document.querySelector('#backupMessage');
  if (!element) return;
  setDialogMessage(element, message || '', isError);
}

function backupLocalDateTimeV17(value) {
  if (!value) return '—';
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return String(value);
  return new Intl.DateTimeFormat('zh-TW', {
    year: 'numeric', month: '2-digit', day: '2-digit', hour: '2-digit', minute: '2-digit', second: '2-digit', hour12: false
  }).format(date);
}

function backupBytesV17(value) {
  const bytes = Number(value || 0);
  if (bytes < 1024) return `${bytes} B`;
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`;
  return `${(bytes / (1024 * 1024)).toFixed(2)} MB`;
}

function v17Escape(value) {
  return String(value ?? '')
    .replaceAll('&', '&amp;')
    .replaceAll('<', '&lt;')
    .replaceAll('>', '&gt;')
    .replaceAll('"', '&quot;')
    .replaceAll("'", '&#39;');
}

/* ---- baseline section ---- */
(() => {
  const PHASE_C_REQUIRED = 14;

  const baseBackupSettingsHtml = backupSettingsHtmlV17;
  backupSettingsHtmlV17 = function backupSettingsHtmlV181() {
    return baseBackupSettingsHtml()
      .replace(
        '<div id="backupProviderHealth" class="backup-provider-health"></div>\n\n    <div class="backup-actions-row">',
        '<div id="backupProviderHealth" class="backup-provider-health"></div>\n\n    <div id="backupAcceptance" class="backup-acceptance-strip" hidden></div>\n\n    <div class="backup-actions-row">'
      )
      .replace(
        '<thead><tr><th>時間</th><th>方式</th><th>整體</th><th>備份 ID／錯誤</th><th>R2</th><th>GCS</th><th class="num">資料筆數</th><th class="num">大小</th></tr></thead>',
        '<thead><tr><th>時間</th><th>方式</th><th>備份 ID</th><th>R2</th><th>GCS</th><th>資料</th></tr></thead>'
      )
      .replace('colspan="8" class="empty">讀取中…', 'colspan="6" class="empty">讀取中…');
  };

  const baseRenderBackupStatus = renderBackupStatusV17;
  renderBackupStatusV17 = function renderBackupStatusV181(data) {
    baseRenderBackupStatus(data);
    renderPhaseCAcceptanceV181(data);
  };

  renderTieredBackupHistoryV18 = function renderTieredBackupHistoryV181(backups) {
    const tbody = document.querySelector('#backupHistoryRows');
    if (!tbody) return;
    const items = Array.isArray(backups) ? backups.slice(0, 8) : [];
    if (!items.length) {
      tbody.innerHTML = '<tr><td colspan="6" class="empty">尚無 paired backup 執行紀錄。</td></tr>';
      return;
    }

    tbody.innerHTML = items.map(item => {
      const copies = Array.isArray(item.copies) ? item.copies : [];
      const r2 = copies.find(copy => copy.provider === 'cloudflare_r2');
      const gcs = copies.find(copy => copy.provider === 'google_cloud_storage');
      const title = item.status === 'success'
        ? `Package SHA ${String(item.packageSha256 || '')}`
        : copies.filter(copy => copy.status !== 'success').map(copy => copy.lastError || `${copy.provider} failed`).join(' · ');
      return `<tr>
        <td>${v17Escape(backupLocalDateTimeV17(item.createdAt))}</td>
        <td>${item.trigger === 'scheduled' ? '自動' : '手動'}</td>
        <td class="backup-run-detail" title="${v17Escape(title)}">${v17Escape(item.backupId || '—')}</td>
        <td class="backup-copy-cell">${copyBadgeV181(r2)}</td>
        <td class="backup-copy-cell">${copyBadgeV181(gcs)}</td>
        <td class="backup-data-summary">${Number(item.rowCount || 0).toLocaleString()} 筆 · ${backupBytesV17(item.byteSize || 0)}</td>
      </tr>`;
    }).join('');
  };

  renderBackupHistoryV17 = function renderBackupHistoryV181(runs) {
    const tbody = document.querySelector('#backupHistoryRows');
    if (!tbody) return;
    const items = Array.isArray(runs) ? runs.slice(0, 8) : [];
    if (!items.length) {
      tbody.innerHTML = '<tr><td colspan="6" class="empty">尚無 Cloud Storage 備份執行紀錄。</td></tr>';
      return;
    }

    tbody.innerHTML = items.map(run => {
      const success = run.status === 'success';
      const detail = success ? run.fileName : (run.errorMessage || '備份失敗');
      return `<tr>
        <td>${v17Escape(backupLocalDateTimeV17(run.completedAt || run.startedAt))}</td>
        <td>${run.trigger === 'scheduled' ? '自動' : '手動'}</td>
        <td class="backup-run-detail" title="${v17Escape(detail)}">${v17Escape(run.fileName || '—')}</td>
        <td class="backup-copy-cell">—</td>
        <td class="backup-copy-cell"><span class="backup-run-badge ${success ? 'success' : 'failed'}">${success ? '成功' : '失敗'}</span></td>
        <td class="backup-data-summary">${Number(run.rowCount || 0).toLocaleString()} 筆 · ${run.byteSize ? backupBytesV17(run.byteSize) : '—'}</td>
      </tr>`;
    }).join('');
  };

  function copyBadgeV181(copy) {
    if (!copy) return '<span class="backup-run-badge neutral">—</span>';
    const success = copy.status === 'success';
    const failed = copy.status === 'failed';
    const label = success ? '成功' : failed ? '失敗' : '處理中';
    const cls = success ? 'success' : failed ? 'failed' : 'neutral';
    return `<span class="backup-run-badge ${cls}" title="${v17Escape(copy.lastError || '')}">${label}</span>`;
  }

  function deriveAcceptanceFromLogicalBackupsV181(backups, required) {
    const scheduled = (Array.isArray(backups) ? backups : []).filter(item => item?.trigger === 'scheduled');
    let count = 0;
    for (const item of scheduled) {
      if (count >= required) break;
      const copies = Array.isArray(item?.copies) ? item.copies : [];
      const r2 = copies.find(copy => copy.provider === 'cloudflare_r2');
      const gcs = copies.find(copy => copy.provider === 'google_cloud_storage');
      const packageSha = String(item?.packageSha256 || '');
      if (!(r2?.status === 'success' && gcs?.status === 'success' && /^[0-9a-f]{64}$/i.test(packageSha))) break;
      count += 1;
    }
    return count;
  }

  function phaseCAcceptanceUiModelV181(data) {
    const tiered = data?.topology === 'parallel_dual_provider' || data?.provider === 'tiered';
    if (!tiered) return { visible: false, required: PHASE_C_REQUIRED, count: 0, remaining: PHASE_C_REQUIRED, completed: false };

    const server = data?.phaseCAcceptance || {};
    const required = Math.max(1, Number(server.requiredConsecutiveScheduled || PHASE_C_REQUIRED));
    const fallbackCount = deriveAcceptanceFromLogicalBackupsV181(data?.logicalBackups, required);
    const count = Math.max(0, Math.min(required, Number.isFinite(Number(server.consecutiveScheduledSuccesses))
      ? Number(server.consecutiveScheduledSuccesses)
      : fallbackCount));
    return {
      visible: true,
      required,
      count,
      remaining: Math.max(0, required - count),
      completed: Boolean(server.completed) || count >= required,
      latestScheduledAt: server.latestScheduledAt || null,
      latestScheduledBackupId: server.latestScheduledBackupId || null
    };
  }

  function renderPhaseCAcceptanceV181(data) {
    const container = document.querySelector('#backupAcceptance');
    if (!container) return;
    const model = phaseCAcceptanceUiModelV181(data);
    container.hidden = !model.visible;
    if (!model.visible) {
      container.innerHTML = '';
      return;
    }

    const state = model.completed ? 'Phase C gate 已完成' : `尚差 ${model.remaining} 次`;
    const latest = model.latestScheduledAt
      ? `最近排程：${backupLocalDateTimeV17(model.latestScheduledAt)}`
      : '尚未有 Phase C 排程備份';
    container.innerHTML = `
      <div class="backup-acceptance-main">
        <span>Phase C 排程驗收</span>
        <strong>${model.count} / ${model.required}</strong>
      </div>
      <progress class="backup-acceptance-progress" max="${model.required}" value="${model.count}"></progress>
      <div class="backup-acceptance-detail"><span>${v17Escape(state)}</span><span>${v17Escape(latest)}；手動測試不計</span></div>`;
  }

  window.phaseCAcceptanceUiModelV181 = phaseCAcceptanceUiModelV181;
  window.addEventListener('load', () => {
    const version = document.querySelector('.version');
    if (version) version.textContent = 'V0.18.1';
  });
})();

/* ---- baseline section ---- */
(() => {
  const MAX_SQLITE_BYTES = 20 * 1024 * 1024;
  const LIMITS = { accounts: 200, groups: 500, categories: 2000, transactions: 10000, openingBalances: 5000 };
  const REQUIRED_TABLES = ['meta', 'accounts', 'category_groups', 'categories', 'transactions', 'opening_balances', 'app_settings'];
  const SQLJS_SCRIPT = '/vendor/sqljs/sql-wasm.js';
  const SQLJS_WASM = '/vendor/sqljs/sql-wasm.wasm';

  const migrationState = {
    file: null,
    snapshot: null,
    preview: null,
    loadingSqlJs: null,
    committed: false
  };

  window.addEventListener('load', async () => {
    const version = document.querySelector('.version');
    if (version) version.textContent = 'V0.19.0';
    const role = await currentRoleV19();
    if (role === 'SUPER_ADMIN') installMigrationSettingsV19();
  });

  async function currentRoleV19() {
    try {
      const user = window.cyaccCurrentUser || (window.cyaccSessionPromise ? await window.cyaccSessionPromise : null);
      return String(user?.role || '');
    } catch {
      return '';
    }
  }

  function installMigrationSettingsV19() {
    if (document.querySelector('[data-settings-tab="migration"]')) return;
    const nav = document.querySelector('.settings-nav');
    const content = document.querySelector('.settings-content');
    if (!nav || !content || typeof setSettingsTab !== 'function') return;

    const tab = document.createElement('button');
    tab.type = 'button';
    tab.className = 'settings-tab';
    tab.dataset.settingsTab = 'migration';
    tab.textContent = '資料移轉';
    nav.appendChild(tab);

    const pane = document.createElement('section');
    pane.className = 'settings-pane migration-pane-v19';
    pane.dataset.settingsPane = 'migration';
    pane.innerHTML = migrationPaneHtmlV19();
    content.insertBefore(pane, document.querySelector('#settingsMessage'));

    if (typeof els === 'object' && Array.isArray(els.settingsTabs) && Array.isArray(els.settingsPanes)) {
      els.settingsTabs.push(tab);
      els.settingsPanes.push(pane);
    }
    tab.addEventListener('click', () => setSettingsTab('migration'));
    bindMigrationPaneV19(pane);
  }

  function migrationPaneHtmlV19() {
    return `
      <div class="migration-heading-v19">
        <div><h3>CYAccounting 桌面帳本移轉</h3><p class="hint">將既有桌面版 SQLite 帳本安全合併到目前 Web 帳本。只有超級管理員可執行。</p></div>
        <span class="migration-local-badge-v19">SQLite 本機解析</span>
      </div>
      <div class="migration-warning-v19">
        <strong>選檔前請先關閉桌面版 CYAccounting。</strong>
        <span>桌面版使用 SQLite WAL；若程式仍開啟，單獨讀取 <code>Data/CYaccounting.db</code> 可能尚未包含 WAL 中的最新資料。也可以選擇最近完成且已驗證的桌面備份檔。</span>
      </div>
      <div class="migration-source-v19">
        <label><span>SQLite 帳本</span><input id="desktopMigrationFileV19" type="file" accept=".db,.sqlite,.sqlite3,application/vnd.sqlite3,application/x-sqlite3"></label>
        <button id="desktopMigrationInspectV19" class="primary compact" type="button" disabled>解析並建立預覽</button>
      </div>
      <p class="hint migration-privacy-v19">原始 SQLite 檔只在你的瀏覽器中解析，不會上傳到伺服器；Worker 只接收解析後的帳戶、科目、交易與期初餘額資料。</p>
      <div id="desktopMigrationMessageV19" class="dialog-message"></div>
      <div id="desktopMigrationSourceV19" class="migration-source-summary-v19 hidden"></div>
      <div id="desktopMigrationPreviewV19" class="migration-preview-v19 hidden"></div>
      <div class="migration-actions-v19">
        <button id="desktopMigrationCommitV19" class="primary" type="button" disabled>確認執行移轉</button>
      </div>`;
  }

  function bindMigrationPaneV19(pane) {
    const fileInput = pane.querySelector('#desktopMigrationFileV19');
    const inspect = pane.querySelector('#desktopMigrationInspectV19');
    const commit = pane.querySelector('#desktopMigrationCommitV19');
    fileInput?.addEventListener('change', () => {
      migrationState.file = fileInput.files?.[0] || null;
      migrationState.snapshot = null;
      migrationState.preview = null;
      migrationState.committed = false;
      resetMigrationPreviewV19(pane);
      if (!migrationState.file) return;
      if (migrationState.file.size > MAX_SQLITE_BYTES) {
        setMigrationMessageV19(pane, 'SQLite 檔案不可超過 20 MB。', true);
        inspect.disabled = true;
        return;
      }
      inspect.disabled = false;
      setMigrationMessageV19(pane, `已選擇 ${migrationState.file.name}（${bytesV19(migrationState.file.size)}）。`);
    });
    inspect?.addEventListener('click', () => inspectDesktopSqliteV19(pane));
    commit?.addEventListener('click', () => commitDesktopMigrationV19(pane));
  }

  async function inspectDesktopSqliteV19(pane) {
    const file = migrationState.file;
    if (!file) return;
    setMigrationBusyV19(pane, true);
    resetMigrationPreviewV19(pane, false);
    setMigrationMessageV19(pane, '正在本機檢查 SQLite 完整性與資料內容…');
    try {
      const bytes = new Uint8Array(await file.arrayBuffer());
      const sha256 = await sha256V19(bytes);
      const snapshot = await readDesktopSqliteV19(bytes, file, sha256);
      migrationState.snapshot = snapshot;
      renderSourceSummaryV19(pane, snapshot);
      setMigrationMessageV19(pane, 'SQLite 本機解析完成，正在比對 Web 帳本…');
      const data = await jsonFetchV19('/api/migration/desktop/preview', {
        method: 'POST',
        headers: { 'content-type': 'application/json' },
        body: JSON.stringify({ snapshot })
      });
      migrationState.preview = data;
      renderMigrationPreviewV19(pane, data);
      setMigrationMessageV19(pane, data.plan?.canCommit ? '預覽完成。確認內容後即可執行移轉。' : '預覽完成，但目前有衝突或已移轉紀錄，不能提交。', !data.plan?.canCommit);
    } catch (error) {
      migrationState.snapshot = null;
      migrationState.preview = null;
      renderMigrationErrorV19(pane, error);
    } finally {
      setMigrationBusyV19(pane, false);
    }
  }

  async function readDesktopSqliteV19(bytes, file, sha256) {
    const SQL = await loadSqlJsV19();
    let db;
    try {
      db = new SQL.Database(bytes);
      const integrity = scalarV19(db, 'PRAGMA integrity_check');
      if (String(integrity || '').toLowerCase() !== 'ok') throw new Error(`SQLite 完整性檢查失敗：${integrity || 'unknown'}`);
      const foreignKeys = rowsV19(db, 'PRAGMA foreign_key_check');
      if (foreignKeys.length) throw new Error('SQLite 關聯完整性檢查失敗，請先修復桌面帳本。');

      const tables = new Set(rowsV19(db, "SELECT name FROM sqlite_master WHERE type='table'").map(row => String(row.name || '')));
      const missing = REQUIRED_TABLES.filter(name => !tables.has(name));
      if (missing.length) throw new Error(`不是支援的 CYAccounting 帳本；缺少資料表：${missing.join('、')}`);

      const schemaVersion = Number(scalarV19(db, "SELECT value FROM meta WHERE key='schema_version' LIMIT 1"));
      if (![1, 2].includes(schemaVersion)) throw new Error(`不支援的 CYAccounting SQLite schema version：${schemaVersion || 'unknown'}`);

      enforceCountV19(db, 'accounts', LIMITS.accounts, '帳戶');
      enforceCountV19(db, 'category_groups', LIMITS.groups, '大分類');
      enforceCountV19(db, 'categories', LIMITS.categories, '科目');
      enforceCountV19(db, 'transactions', LIMITS.transactions, '交易');
      enforceCountV19(db, 'opening_balances', LIMITS.openingBalances, '期初餘額');

      const categoryColumns = new Set(rowsV19(db, 'PRAGMA table_info(categories)').map(row => String(row.name || '')));
      const favoriteSelect = categoryColumns.has('is_favorite') ? 'c.is_favorite AS is_favorite' : '0 AS is_favorite';

      const accounts = rowsV19(db, 'SELECT name, sort_order, is_default, created_at FROM accounts ORDER BY sort_order, id').map(row => ({
        name: row.name, sortOrder: Number(row.sort_order), isDefault: Number(row.is_default), createdAt: row.created_at
      }));
      const groups = rowsV19(db, 'SELECT kind, name, sort_order, created_at FROM category_groups ORDER BY kind, sort_order, id').map(row => ({
        kind: row.kind, name: row.name, sortOrder: Number(row.sort_order), createdAt: row.created_at
      }));
      const categories = rowsV19(db, `
        SELECT c.kind, g.name AS group_name, c.name, c.sort_order, ${favoriteSelect}, c.created_at
        FROM categories c JOIN category_groups g ON g.id = c.group_id
        ORDER BY c.kind, g.sort_order, c.sort_order, c.id
      `).map(row => ({
        kind: row.kind, groupName: row.group_name, name: row.name, sortOrder: Number(row.sort_order),
        isFavorite: Number(row.is_favorite || 0), createdAt: row.created_at
      }));
      const transactions = rowsV19(db, `
        SELECT id, tx_date, account_name, kind, category_name, summary, amount, created_at, updated_at
        FROM transactions ORDER BY id
      `).map(row => ({
        sourceId: Number(row.id), txDate: row.tx_date, accountName: row.account_name, kind: row.kind,
        categoryName: row.category_name, summary: row.summary || '', amount: Number(row.amount),
        createdAt: row.created_at, updatedAt: row.updated_at
      }));
      const openingBalances = rowsV19(db, `
        SELECT month, account_name, amount, created_at, updated_at
        FROM opening_balances ORDER BY month, account_name
      `).map(row => ({
        month: row.month, accountName: row.account_name, amount: Number(row.amount),
        createdAt: row.created_at, updatedAt: row.updated_at
      }));
      const lockedThrough = String(scalarV19(db, "SELECT value FROM app_settings WHERE key='locked_through' LIMIT 1") || '').trim() || null;

      return {
        source: { schemaVersion, fileName: file.name, fileSize: file.size, fileSha256: sha256 },
        accounts, groups, categories, transactions, openingBalances, lockedThrough
      };
    } catch (error) {
      if (error?.message?.includes('file is not a database')) throw new Error('選取的檔案不是有效的 SQLite 資料庫。');
      throw error;
    } finally {
      try { db?.close(); } catch { /* no-op */ }
    }
  }

  function enforceCountV19(db, table, max, label) {
    const count = Number(scalarV19(db, `SELECT COUNT(*) FROM ${table}`) || 0);
    if (count > max) throw new Error(`${label}共有 ${count.toLocaleString()} 筆，超過單次移轉上限 ${max.toLocaleString()} 筆。`);
  }

  function rowsV19(db, sql) {
    const result = db.exec(sql)?.[0];
    if (!result) return [];
    return (result.values || []).map(values => Object.fromEntries(result.columns.map((column, index) => [column, values[index]])));
  }

  function scalarV19(db, sql) {
    const result = db.exec(sql)?.[0];
    return result?.values?.[0]?.[0] ?? null;
  }

  async function loadSqlJsV19() {
    if (window.SQL && typeof window.SQL.Database === 'function') return window.SQL;
    if (!migrationState.loadingSqlJs) {
      migrationState.loadingSqlJs = new Promise((resolve, reject) => {
        const existing = document.querySelector(`script[src="${SQLJS_SCRIPT}"]`);
        if (existing) {
          existing.addEventListener('load', initialize, { once: true });
          existing.addEventListener('error', () => reject(new Error('SQLite 解析元件載入失敗。')), { once: true });
          return;
        }
        const script = document.createElement('script');
        script.src = SQLJS_SCRIPT;
        script.onload = initialize;
        script.onerror = () => reject(new Error('SQLite 解析元件載入失敗。'));
        document.head.appendChild(script);

        async function initialize() {
          try {
            if (typeof window.initSqlJs !== 'function') throw new Error('SQLite 解析元件初始化失敗。');
            const SQL = await window.initSqlJs({ locateFile: () => SQLJS_WASM });
            window.SQL = SQL;
            resolve(SQL);
          } catch (error) {
            reject(error);
          }
        }
      });
    }
    return migrationState.loadingSqlJs;
  }

  function renderSourceSummaryV19(pane, snapshot) {
    const target = pane.querySelector('#desktopMigrationSourceV19');
    if (!target) return;
    target.classList.remove('hidden');
    target.innerHTML = `
      <div><span>檔案</span><strong>${escapeV19(snapshot.source.fileName)}</strong></div>
      <div><span>SQLite schema</span><strong>v${snapshot.source.schemaVersion}</strong></div>
      <div><span>SHA-256</span><code title="${escapeV19(snapshot.source.fileSha256)}">${escapeV19(snapshot.source.fileSha256.slice(0, 16))}…</code></div>
      <div><span>資料</span><strong>${snapshot.transactions.length.toLocaleString()} 筆交易</strong></div>`;
  }

  function renderMigrationPreviewV19(pane, data) {
    const box = pane.querySelector('#desktopMigrationPreviewV19');
    const commit = pane.querySelector('#desktopMigrationCommitV19');
    if (!box) return;
    const plan = data.plan || {};
    const source = data.source || {};
    const target = data.target || {};
    box.classList.remove('hidden');
    box.innerHTML = `
      <div class="migration-plan-head-v19">
        <div><span>移轉模式</span><strong>${plan.mode === 'pristine_merge' ? '空白 Web 帳本初始化合併' : '既有 Web 帳本保守合併'}</strong></div>
        <span class="migration-status-v19 ${plan.canCommit ? 'ok' : 'blocked'}">${plan.canCommit ? '可執行' : '暫停'}</span>
      </div>
      <div class="migration-grid-v19">
        ${statV19('帳戶', plan.accounts?.insert, `沿用 ${plan.accounts?.reuse || 0}`)}
        ${statV19('大分類', plan.groups?.insert, `沿用 ${plan.groups?.reuse || 0}`)}
        ${statV19('科目', plan.categories?.insert, `沿用 ${plan.categories?.reuse || 0} · 調整 ${plan.categories?.realign || 0}`)}
        ${statV19('交易', plan.transactions?.insert, `重複略過 ${plan.transactions?.duplicate || 0}`)}
        ${statV19('期初餘額', plan.openingBalances?.insert, `重複略過 ${plan.openingBalances?.duplicate || 0}`)}
        ${statV19('鎖帳至', plan.resultingLockedThrough || '—', `Web 原本 ${target.lockedThrough || '未鎖帳'}`, false)}
      </div>
      ${listBlockV19('阻擋衝突', plan.conflicts, 'error')}
      ${listBlockV19('注意事項', plan.warnings, 'warn')}
      <p class="hint">來源：${Number(source.counts?.accounts || 0)} 帳戶、${Number(source.counts?.categories || 0)} 科目、${Number(source.counts?.transactions || 0).toLocaleString()} 交易。Web 目前共有 ${Number(target.transactionCount || 0).toLocaleString()} 筆交易。</p>`;
    if (commit) commit.disabled = !plan.canCommit || migrationState.committed;
  }

  function statV19(label, value, detail, numeric = true) {
    const shown = numeric && Number.isFinite(Number(value)) ? Number(value).toLocaleString() : escapeV19(value ?? '—');
    return `<div class="migration-stat-v19"><span>${escapeV19(label)}</span><strong>${shown}</strong><small>${escapeV19(detail || '')}</small></div>`;
  }

  function listBlockV19(title, items, kind) {
    const values = Array.isArray(items) ? items : [];
    if (!values.length) return '';
    return `<div class="migration-list-v19 ${kind}"><strong>${escapeV19(title)}</strong><ul>${values.slice(0, 50).map(item => `<li>${escapeV19(item)}</li>`).join('')}</ul></div>`;
  }

  async function commitDesktopMigrationV19(pane) {
    const snapshot = migrationState.snapshot;
    const preview = migrationState.preview;
    if (!snapshot || !preview?.plan?.canCommit || migrationState.committed) return;
    const tx = Number(preview.plan.transactions?.insert || 0);
    const accounts = Number(preview.plan.accounts?.insert || 0);
    const categories = Number(preview.plan.categories?.insert || 0);
    const opening = Number(preview.plan.openingBalances?.insert || 0);
    const message = `確定執行桌面帳本移轉？\n\n將新增：\n- ${accounts} 個帳戶\n- ${categories} 個科目\n- ${tx} 筆交易\n- ${opening} 筆期初餘額\n\n既有 Web 交易不會被刪除。`;
    if (!confirm(message)) return;

    setMigrationBusyV19(pane, true);
    setMigrationMessageV19(pane, '正在寫入 D1；此步驟失敗會整批回滾…');
    try {
      const data = await jsonFetchV19('/api/migration/desktop/commit', {
        method: 'POST',
        headers: { 'content-type': 'application/json' },
        body: JSON.stringify({ snapshot, confirm: true, expectedMode: preview.plan.mode })
      });
      migrationState.committed = true;
      const commit = pane.querySelector('#desktopMigrationCommitV19');
      if (commit) commit.disabled = true;
      const result = data.migration || {};
      setMigrationMessageV19(pane, `移轉完成：新增 ${Number(result.insertedTransactions || 0).toLocaleString()} 筆交易，略過 ${Number(result.skippedDuplicateTransactions || 0).toLocaleString()} 筆既有重複資料。`);
      if (typeof refreshBootstrap === 'function') await refreshBootstrap();
      if (typeof loadTransactions === 'function') await loadTransactions();
      renderMigrationPreviewV19(pane, { ...preview, plan: { ...preview.plan, canCommit: false, warnings: [...(preview.plan.warnings || []), '本次移轉已完成；如來源資料之後有新增內容，請重新選取更新後的 SQLite 檔。'] } });
      window.cyShowMigrationComplete?.(result);
    } catch (error) {
      setMigrationMessageV19(pane, error.message || '移轉失敗。', true);
      if (error.preview) renderMigrationPreviewV19(pane, error.preview);
    } finally {
      setMigrationBusyV19(pane, false);
    }
  }

  async function jsonFetchV19(url, options) {
    const response = await fetch(url, options);
    const data = await response.json().catch(() => ({}));
    if (!response.ok || data.ok === false) {
      const error = new Error(data.error || `HTTP ${response.status}`);
      error.code = data.code;
      error.preview = data.preview;
      throw error;
    }
    return data;
  }

  function renderMigrationErrorV19(pane, error) {
    setMigrationMessageV19(pane, error?.message || 'SQLite 解析失敗。', true);
    const commit = pane.querySelector('#desktopMigrationCommitV19');
    if (commit) commit.disabled = true;
  }

  function resetMigrationPreviewV19(pane, clearMessage = true) {
    pane.querySelector('#desktopMigrationSourceV19')?.classList.add('hidden');
    pane.querySelector('#desktopMigrationPreviewV19')?.classList.add('hidden');
    const commit = pane.querySelector('#desktopMigrationCommitV19');
    if (commit) commit.disabled = true;
    if (clearMessage) setMigrationMessageV19(pane, '');
  }

  function setMigrationBusyV19(pane, busy) {
    const inspect = pane.querySelector('#desktopMigrationInspectV19');
    const commit = pane.querySelector('#desktopMigrationCommitV19');
    const file = pane.querySelector('#desktopMigrationFileV19');
    if (inspect) inspect.disabled = busy || !migrationState.file;
    if (commit) commit.disabled = busy || !migrationState.preview?.plan?.canCommit || migrationState.committed;
    if (file) file.disabled = busy;
  }

  function setMigrationMessageV19(pane, text, isError = false) {
    const element = pane.querySelector('#desktopMigrationMessageV19');
    if (!element) return;
    element.textContent = text || '';
    element.classList.toggle('error', Boolean(isError));
  }

  async function sha256V19(bytes) {
    const digest = new Uint8Array(await crypto.subtle.digest('SHA-256', bytes));
    return Array.from(digest, value => value.toString(16).padStart(2, '0')).join('');
  }

  function bytesV19(value) {
    const bytes = Number(value || 0);
    if (bytes < 1024) return `${bytes} B`;
    if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`;
    return `${(bytes / 1024 / 1024).toFixed(1)} MB`;
  }

  function escapeV19(value) {
    return String(value ?? '')
      .replaceAll('&', '&amp;')
      .replaceAll('<', '&lt;')
      .replaceAll('>', '&gt;')
      .replaceAll('"', '&quot;')
      .replaceAll("'", '&#39;');
  }
})();

/* ---- baseline section ---- */
const CY_V20_VERSION = 'V0.20.1';
const CY_V20_MOBILE_CONFIRMATION_INIT = 'cyaccounting.v20.mobileConfirmationInitialized';

if (!window.__CYACC_BASELINE_BUNDLE__) ensureV201Stylesheet();
let cyV20Started = false;

if (document.readyState === 'loading') {
  document.addEventListener('DOMContentLoaded', startV20, { once: true });
} else {
  startV20();
}
window.addEventListener('load', startV20, { once: true });

function startV20() {
  if (cyV20Started) return;
  cyV20Started = true;
  syncV20Version();
  setupV20ViewportState();
  setupV20MobileConfirmationDefault();
  setupV20SettingsTabVisibility();
  setupV201MobileInlineEditVisibility();
}

function ensureV201Stylesheet() {
  if (document.querySelector('link[href="/v0201.css"]')) return;
  const link = document.createElement('link');
  link.rel = 'stylesheet';
  link.href = '/v0201.css';
  document.head.appendChild(link);
}

function syncV20Version() {
  const version = document.querySelector('.version');
  if (version) version.textContent = CY_V20_VERSION;
}

function setupV20ViewportState() {
  const sync = () => {
    const width = window.innerWidth;
    document.documentElement.dataset.viewport = width < 768 ? 'mobile' : width < 1024 ? 'tablet' : 'desktop';
  };
  sync();
  window.addEventListener('resize', sync, { passive: true });
}

function setupV20MobileConfirmationDefault() {
  if (window.innerWidth >= 768) return;
  if (localStorage.getItem(CY_V20_MOBILE_CONFIRMATION_INIT) === '1') return;
  localStorage.setItem(CY_V20_MOBILE_CONFIRMATION_INIT, '1');
  if (typeof setConfirmationDrawer === 'function') setConfirmationDrawer(false, false);
}

function setupV20SettingsTabVisibility() {
  const nav = document.querySelector('.settings-nav');
  if (!nav) return;
  nav.addEventListener('click', event => {
    const tab = event.target.closest('.settings-tab');
    if (!tab || window.innerWidth >= 768) return;
    requestAnimationFrame(() => tab.scrollIntoView({ behavior: 'smooth', block: 'nearest', inline: 'center' }));
  });
}

function setupV201MobileInlineEditVisibility() {
  const rows = document.querySelector('#transactionRows');
  if (!rows || typeof MutationObserver !== 'function') return;
  const observer = new MutationObserver(mutations => {
    if (window.innerWidth >= 768) return;
    for (const mutation of mutations) {
      if (mutation.type !== 'attributes' || mutation.attributeName !== 'class') continue;
      const row = mutation.target;
      if (!(row instanceof HTMLElement) || !row.matches('tr.inline-editing')) continue;
      requestAnimationFrame(() => row.scrollIntoView({ behavior: 'smooth', block: 'nearest', inline: 'nearest' }));
      break;
    }
  });
  observer.observe(rows, { subtree: true, attributes: true, attributeFilter: ['class'] });
}

/* ---- baseline section ---- */
const CY_V21_VERSION = 'V0.21.0 Build 7';
const CY_V21_SPLIT_MEDIA = '(min-width: 1360px)';
const CY_V21_CONFIRMATION_STATE_KEY = 'cyaccounting.confirmationDrawerOpen';

if (!window.__CYACC_BASELINE_BUNDLE__) ensureV21Build1Stylesheet();
if (!window.__CYACC_BASELINE_BUNDLE__) ensureV21Build2Stylesheet();
if (!window.__CYACC_BASELINE_BUNDLE__) ensureV21Build3Stylesheet();
if (!window.__CYACC_BASELINE_BUNDLE__) ensureV21Build5Stylesheet();
if (!window.__CYACC_BASELINE_BUNDLE__) ensureV21Build6Stylesheet();
if (!window.__CYACC_BASELINE_BUNDLE__) ensureV21Build7Stylesheet();
let cyV21Started = false;

if (document.readyState === 'loading') {
  document.addEventListener('DOMContentLoaded', startV21, { once: true });
} else {
  startV21();
}
window.addEventListener('load', startV21, { once: true });

function startV21() {
  if (cyV21Started) return;
  cyV21Started = true;
  syncV21Version();
  updateV21KeyboardHint();
  setupV21HeaderLayout();
  setupV21DesktopSplitWorkspace();
  setupV21EntryHelp();
  setupV21LedgerContext();
  setupV21LedgerHeaderDecoration();
  setupV21LedgerEmptyState();
  setupV21ConfirmationCopy();
  setupV21DataSettings();
  cleanupV21InterfaceCopy();
  setupV21UserIdentity();
}

function ensureV21Build1Stylesheet() {
  if (document.querySelector('link[href="/v021b1.css"]')) return;
  const link = document.createElement('link');
  link.rel = 'stylesheet';
  link.href = '/v021b1.css';
  document.head.appendChild(link);
}

function ensureV21Build2Stylesheet() {
  if (document.querySelector('link[href="/v021b2.css"]')) return;
  const link = document.createElement('link');
  link.rel = 'stylesheet';
  link.href = '/v021b2.css';
  document.head.appendChild(link);
}

function ensureV21Build3Stylesheet() {
  if (document.querySelector('link[href="/v021b3.css"]')) return;
  const link = document.createElement('link');
  link.rel = 'stylesheet';
  link.href = '/v021b3.css';
  document.head.appendChild(link);
}

function ensureV21Build5Stylesheet() {
  if (document.querySelector('link[href="/v021b5.css"]')) return;
  const link = document.createElement('link');
  link.rel = 'stylesheet';
  link.href = '/v021b5.css';
  document.head.appendChild(link);
}

function ensureV21Build6Stylesheet() {
  if (document.querySelector('link[href="/v021b6.css"]')) return;
  const link = document.createElement('link');
  link.rel = 'stylesheet';
  link.href = '/v021b6.css';
  document.head.appendChild(link);
}

function ensureV21Build7Stylesheet() {
  if (document.querySelector('link[href="/v021b7.css"]')) return;
  const link = document.createElement('link');
  link.rel = 'stylesheet';
  link.href = '/v021b7.css';
  document.head.appendChild(link);
}

function syncV21Version() {
  const version = document.querySelector('.version');
  if (version) version.textContent = CY_V21_VERSION;
}

function updateV21KeyboardHint() {
  const hint = document.querySelector('.keyboard-hint');
  if (!hint) return;
  hint.innerHTML = '鍵盤：日期 Enter → 帳戶 Enter → 科目 Enter → 摘要 Enter → 金額 Enter 儲存　｜　<kbd>Tab</kbd> 切換收入／支出　｜　日期可輸入 <kbd>0924</kbd> / <kbd>20260924</kbd>，<kbd>Ctrl</kbd>+<kbd>↑↓</kbd> ±1 天';
}

function setupV21HeaderLayout() {
  const topbar = document.querySelector('.topbar');
  const brand = topbar?.firstElementChild;
  const heading = brand?.querySelector('h1');
  const status = document.querySelector('#connectionStatus');
  const actions = document.querySelector('.topbar-actions');
  const settings = document.querySelector('#settingsButton');
  const currentUser = document.querySelector('#currentUser');
  const logout = document.querySelector('#logoutButton');
  if (!topbar || !brand || !heading || !actions) return;

  let brandLine = brand.querySelector('.v21-brand-line');
  if (!brandLine) {
    brandLine = document.createElement('div');
    brandLine.className = 'v21-brand-line';
    heading.before(brandLine);
    brandLine.append(heading);
  }
  if (status && status.parentElement !== brandLine) brandLine.append(status);

  let accountCluster = actions.querySelector('.v21-account-cluster');
  if (!accountCluster) {
    accountCluster = document.createElement('div');
    accountCluster.className = 'v21-account-cluster';
  }

  if (currentUser && currentUser.parentElement !== accountCluster) accountCluster.append(currentUser);
  if (logout && logout.parentElement !== accountCluster) accountCluster.append(logout);
  if (accountCluster.parentElement !== actions) actions.append(accountCluster);
  if (settings) {
    if (settings.parentElement !== actions) actions.append(settings);
    actions.insertBefore(settings, accountCluster);
  }
}

function setupV21EntryHelp() {
  const card = document.querySelector('.entry-card');
  const title = card?.querySelector('.section-title .title-with-badge');
  const sectionTitle = card?.querySelector('.section-title');
  if (!card || !title || !sectionTitle || document.querySelector('#entryHelpButton')) return;

  const button = document.createElement('button');
  button.id = 'entryHelpButton';
  button.className = 'entry-help-button';
  button.type = 'button';
  button.textContent = '?';
  button.title = '快速輸入說明';
  button.setAttribute('aria-label', '開啟快速輸入說明');
  button.setAttribute('aria-expanded', 'false');
  button.setAttribute('aria-controls', 'entryHelpPopover');

  const popover = document.createElement('div');
  popover.id = 'entryHelpPopover';
  popover.className = 'entry-help-popover';
  popover.hidden = true;
  popover.innerHTML = `
    <strong>快速輸入說明</strong>
    <div class="entry-help-grid">
      <kbd>Enter</kbd><span>日期 → 帳戶 → 科目 → 摘要 → 金額 → 儲存</span>
      <kbd>Tab</kbd><span>切換收入／支出，游標留在目前欄位</span>
      <kbd>0924</kbd><span>輸入今年 09/24</span>
      <kbd>20260924</kbd><span>輸入完整日期</span>
      <kbd>Ctrl + ↑↓</kbd><span>日期 ±1 天</span>
    </div>`;

  title.append(button);
  sectionTitle.append(popover);

  const close = () => {
    popover.hidden = true;
    button.setAttribute('aria-expanded', 'false');
  };
  button.addEventListener('click', event => {
    event.stopPropagation();
    const open = popover.hidden;
    popover.hidden = !open;
    button.setAttribute('aria-expanded', open ? 'true' : 'false');
  });
  popover.addEventListener('click', event => event.stopPropagation());
  document.addEventListener('click', close);
  document.addEventListener('keydown', event => {
    if (event.key === 'Escape' && !popover.hidden) {
      close();
      button.focus();
    }
  });
}

function setupV21LedgerContext() {
  const ledgerTitle = document.querySelector('.ledger-card .ledger-title');
  const titleMain = ledgerTitle?.firstElementChild;
  const monthTools = document.querySelector('.ledger-month-tools');
  const summary = document.querySelector('#monthSummary');
  const openingButton = document.querySelector('#ledgerOpeningBalanceButton');
  if (!ledgerTitle || !titleMain || !monthTools || !summary) return;

  let context = titleMain.querySelector('.v21-ledger-context');
  if (!context) {
    context = document.createElement('div');
    context.className = 'v21-ledger-context';
    titleMain.insertBefore(context, summary);
  }
  if (monthTools.parentElement !== context) context.append(monthTools);

  let summaryBar = titleMain.querySelector('.v21-ledger-summary-bar');
  if (!summaryBar) {
    summaryBar = document.createElement('div');
    summaryBar.className = 'v21-ledger-summary-bar';
    context.insertAdjacentElement('afterend', summaryBar);
  }
  if (summary.parentElement !== summaryBar) summaryBar.append(summary);

  let summaryActions = summaryBar.querySelector('.v21-summary-actions');
  if (!summaryActions) {
    summaryActions = document.createElement('div');
    summaryActions.className = 'v21-summary-actions';
    summaryBar.append(summaryActions);
  }
  if (openingButton && openingButton.parentElement !== summaryActions) summaryActions.append(openingButton);

  let lockButton = document.querySelector('#ledgerLockSettingsButton');
  if (!lockButton) {
    lockButton = document.createElement('button');
    lockButton.id = 'ledgerLockSettingsButton';
    lockButton.className = 'secondary compact';
    lockButton.type = 'button';
    lockButton.textContent = '鎖定月份';
    lockButton.title = '開啟月份鎖帳設定';
    lockButton.addEventListener('click', () => {
      if (typeof openSettings === 'function') openSettings();
      if (typeof setSettingsTab === 'function') setSettingsTab('lock');
      setTimeout(() => document.querySelector('#lockedThrough')?.focus(), 0);
    });
  }
  if (lockButton.parentElement !== summaryActions) summaryActions.append(lockButton);

  document.querySelector('#ledgerGroupToggle')?.remove();
  const periodTools = document.querySelector('.ledger-period-tools');
  if (periodTools && !periodTools.children.length) periodTools.remove();
}

function setupV21LedgerHeaderDecoration() {
  const head = document.querySelector('.ledger-card thead');
  if (!head) return;
  const decorate = () => {
    const account = document.querySelector('#ledgerAccountHeader');
    if (!account) return;
    const active = typeof cyLedgerGroupByAccount !== 'undefined' && Boolean(cyLedgerGroupByAccount);
    const nextText = active ? '帳戶 ▲' : '帳戶';
    if (account.textContent !== nextText) account.textContent = nextText;
    account.classList.toggle('v21-account-group-active', active);
    account.setAttribute('aria-pressed', active ? 'true' : 'false');
    account.title = active ? '點擊取消帳戶排列' : '點擊依帳戶排列';
  };
  decorate();
  const observer = new MutationObserver(decorate);
  observer.observe(head, { childList: true, subtree: true });
}

function setupV21LedgerEmptyState() {
  const body = document.querySelector('#transactionRows');
  if (!body) return;
  const enhance = () => {
    const empty = body.querySelector('td.empty');
    if (!empty || empty.querySelector('.ledger-empty-state')) return;
    const text = String(empty.textContent || '').trim();
    if (!text) return;
    empty.innerHTML = `<div class="ledger-empty-state"><strong>${v21EscapeHtml(text)}</strong></div>`;
  };
  enhance();
  const observer = new MutationObserver(enhance);
  observer.observe(body, { childList: true, subtree: true });
}

function setupV21ConfirmationCopy() {
  const panel = document.querySelector('#inputConfirmationCard');
  const heading = panel?.querySelector('h2');
  const hint = panel?.querySelector('.section-title .hint');
  const list = panel?.querySelector('#inputConfirmationList');
  if (!panel) return;
  if (heading) heading.textContent = '最近輸入';
  if (hint) hint.textContent = '最近 10 筆';

  const syncEmpty = () => {
    const empty = list?.querySelector('.confirmation-empty');
    if (empty && empty.textContent !== '本次尚無輸入紀錄。') empty.textContent = '本次尚無輸入紀錄。';
  };
  syncEmpty();
  if (list) {
    const observer = new MutationObserver(syncEmpty);
    observer.observe(list, { childList: true, subtree: true });
  }
}

function setupV21DataSettings() {
  const nav = document.querySelector('.settings-nav');
  const content = document.querySelector('.settings-content');
  if (!nav || !content || typeof setSettingsTab !== 'function') return;

  let tab = nav.querySelector('[data-settings-tab="data"]');
  let pane = content.querySelector('[data-settings-pane="data"]');
  if (!tab) {
    tab = document.createElement('button');
    tab.type = 'button';
    tab.className = 'settings-tab';
    tab.dataset.settingsTab = 'data';
    tab.textContent = '資料管理';
    const lockTab = nav.querySelector('[data-settings-tab="lock"]');
    if (lockTab) nav.insertBefore(tab, lockTab); else nav.append(tab);
    tab.addEventListener('click', () => setSettingsTab('data'));
    if (typeof els === 'object' && Array.isArray(els.settingsTabs)) els.settingsTabs.push(tab);
  }

  if (!pane) {
    pane = document.createElement('section');
    pane.className = 'settings-pane v21-data-pane';
    pane.dataset.settingsPane = 'data';
    pane.innerHTML = `
      <h3>資料管理</h3>
      <section class="v21-data-section">
        <h4>Excel 匯入</h4>
        <div class="v21-data-actions" id="v21ExcelImportHost"></div>
      </section>`;
    const settingsMessage = document.querySelector('#settingsMessage');
    content.insertBefore(pane, settingsMessage || null);
    if (typeof els === 'object' && Array.isArray(els.settingsPanes)) els.settingsPanes.push(pane);
  }

  const moveImportButton = () => {
    const button = document.querySelector('#ledgerExcelImport');
    const host = document.querySelector('#v21ExcelImportHost');
    if (!button || !host) return false;
    if (button.parentElement !== host) host.append(button);
    button.className = 'secondary compact';
    button.textContent = '匯入 Excel';
    return true;
  };

  if (!moveImportButton()) setTimeout(moveImportButton, 50);

  const subtitle = document.querySelector('#settingsDialog .modal-header p');
  if (subtitle) subtitle.textContent = '';
}

function cleanupV21InterfaceCopy() {
  const removeNoise = () => {
    const selectors = [
      '.auth-note',
      '#settingsDialog > .modal-header p',
      '#settingsDialog [data-settings-pane="accounts"] > .hint',
      '#settingsDialog [data-settings-pane="categories"] .pane-heading .hint',
      '#settingsDialog [data-settings-pane="quick"] > .hint',
      '#settingsDialog [data-settings-pane="quick"] .quick-settings-explain',
      '#settingsDialog [data-settings-pane="lock"] > .hint',
      '#settingsDialog [data-settings-pane="data"] .hint',
      '#settingsDialog [data-settings-pane="data"] .v21-data-section > p',
      '#settingsDialog [data-settings-pane="backup"] #backupHeadingHint',
      '#settingsDialog [data-settings-pane="backup"] .backup-security-note',
      '#settingsDialog [data-settings-pane="backup"] .backup-restore-note',
      '#settingsDialog [data-settings-pane="migration"] .migration-heading-v19 .hint',
      '#settingsDialog [data-settings-pane="migration"] .migration-privacy-v19',
      '#openingDialog .opening-dialog-heading > .hint'
    ];
    for (const selector of selectors) {
      document.querySelectorAll(selector).forEach(node => node.remove());
    }
  };

  removeNoise();
  const settings = document.querySelector('#settingsDialog .settings-content');
  if (settings) {
    const observer = new MutationObserver(removeNoise);
    observer.observe(settings, { childList: true, subtree: true });
  }
}

async function setupV21UserIdentity() {
  const target = document.querySelector('#currentUser');
  if (!target) return;
  let user = null;

  const render = () => {
    if (!user) return;
    if (target.querySelector('.current-user-role')) return;
    const employeeNo = String(user.employeeNo || '').trim();
    const name = String(user.name || '').trim();
    const role = String(user.role || '').trim();
    const roleLabel = v21RoleLabel(role);
    target.innerHTML = `<span class="current-user-main">${v21EscapeHtml(`${employeeNo} ${name}`.trim())}</span><span class="current-user-role" title="權限組：${v21EscapeHtml(role || roleLabel)}">${v21EscapeHtml(roleLabel)}</span>`;
    target.classList.remove('hidden');
  };

  const observer = new MutationObserver(render);
  observer.observe(target, { childList: true, subtree: true, characterData: true });

  try {
    user = window.cyaccCurrentUser || (window.cyaccSessionPromise ? await window.cyaccSessionPromise : null);
    if (!user) return;
    target.innerHTML = '';
    render();
  } catch {
    // Authentication UI already owns connection/error handling; role display is optional presentation only.
  }
}

function v21RoleLabel(role) {
  if (role === 'SUPER_ADMIN') return '超級管理員';
  if (role === 'ADMIN') return '管理員';
  return role || '一般使用者';
}

function v21EscapeHtml(value) {
  return String(value ?? '')
    .replaceAll('&', '&amp;')
    .replaceAll('<', '&lt;')
    .replaceAll('>', '&gt;')
    .replaceAll('"', '&quot;')
    .replaceAll("'", '&#039;');
}

function setupV21DesktopSplitWorkspace() {
  const media = window.matchMedia(CY_V21_SPLIT_MEDIA);
  const sync = () => applyV21DesktopSplitWorkspace(media.matches);
  sync();
  if (typeof media.addEventListener === 'function') media.addEventListener('change', sync);
  else media.addListener?.(sync);
}

function applyV21DesktopSplitWorkspace(enabled) {
  const shell = document.querySelector('main.shell');
  const entry = shell?.querySelector('.entry-card') || document.querySelector('.entry-card');
  const ledger = shell?.querySelector('.ledger-card') || document.querySelector('.ledger-card');
  const confirmation = document.querySelector('#inputConfirmationCard');
  if (!shell || !entry || !ledger || !confirmation) return;

  let rail = shell.querySelector('.v21-entry-rail');

  if (enabled) {
    if (!rail) {
      rail = document.createElement('aside');
      rail.className = 'v21-entry-rail';
      rail.setAttribute('aria-label', '快速記帳工作區');
      shell.insertBefore(rail, ledger);
    }

    if (entry.parentElement !== rail) rail.prepend(entry);
    if (confirmation.parentElement !== rail) rail.append(confirmation);

    shell.classList.add('v21-split-layout');
    document.body.classList.add('v21-wide-split');
    confirmation.classList.add('v21-inline-confirmation');

    if (typeof setConfirmationDrawer === 'function') setConfirmationDrawer(true, false);
    else {
      confirmation.classList.add('open');
      confirmation.setAttribute('aria-hidden', 'false');
    }
    return;
  }

  shell.classList.remove('v21-split-layout');
  document.body.classList.remove('v21-wide-split');
  confirmation.classList.remove('v21-inline-confirmation');

  if (entry.parentElement === rail) shell.insertBefore(entry, ledger);
  if (confirmation.parentElement === rail) document.body.append(confirmation);
  rail?.remove();

  const shouldOpen = localStorage.getItem(CY_V21_CONFIRMATION_STATE_KEY) === '1';
  if (typeof setConfirmationDrawer === 'function') setConfirmationDrawer(shouldOpen, false);
  else {
    confirmation.classList.toggle('open', shouldOpen);
    confirmation.setAttribute('aria-hidden', shouldOpen ? 'false' : 'true');
  }
}

/* ---- baseline section ---- */
const CY_V21_BUILD8_VERSION = 'V0.21.0 Build 9';
const CY_V21_BUILD8_SUMMARY_UNITS = 40;
const CY_V21_BUILD9_MOBILE = '(max-width: 767px)';

if (!window.__CYACC_BASELINE_BUNDLE__) ensureV21Build8Stylesheet();
if (!window.__CYACC_BASELINE_BUNDLE__) ensureV21Build9Stylesheet();
let cyV21Build8Started = false;

if (document.readyState === 'loading') {
  document.addEventListener('DOMContentLoaded', startV21Build8, { once: true });
} else {
  startV21Build8();
}
window.addEventListener('load', syncV21Build8AfterLoad, { once: true });

function startV21Build8() {
  if (cyV21Build8Started) return;
  cyV21Build8Started = true;
  runV21Build8Step('mobile-pages', setupV21Build9MobilePages);
  runV21Build8Step('mobile-account-picker', setupV21Build9MobileAccountPicker);
  runV21Build8Step('account-choices', setupV21Build8AccountChoices);
  runV21Build8Step('summary-limit', setupV21Build8SummaryLimit);
  runV21Build8Step('role-medal', setupV21Build8RoleMedal);
  runV21Build8Step('enter-hints', setupV21Build9EnterHints);
  runV21Build8Step('help-copy', syncV21Build9HelpCopy);
  runV21Build8Step('version', syncV21Build8Version);
}

function runV21Build8Step(name, task) {
  try {
    task();
  } catch (error) {
    console.error('cyaccounting_mobile_build8_step_failed', name, error instanceof Error ? error.message : 'unknown_error');
  }
}

function syncV21Build8AfterLoad() {
  startV21Build8();
  syncV21Build8Version();
  syncV21Build8AccountChoices();
  syncV21Build8RoleMedal();
  syncV21Build9AccountPickerLabel();
  syncV21Build9HelpCopy();
}

function ensureV21Build8Stylesheet() {
  if (document.querySelector('link[href^="/v021b8.css"]')) return;
  const link = document.createElement('link');
  link.rel = 'stylesheet';
  link.href = '/v021b8.css?v=0216b6';
  document.head.appendChild(link);
}

function ensureV21Build9Stylesheet() {
  if (document.querySelector('link[href^="/v021b9.css"]')) return;
  const link = document.createElement('link');
  link.rel = 'stylesheet';
  link.href = '/v021b9.css?v=0216b6';
  document.head.appendChild(link);
}

function syncV21Build8Version() {
  const version = document.querySelector('.version');
  if (version) version.textContent = CY_V21_BUILD8_VERSION;
}

function setupV21Build8AccountChoices() {
  const select = document.querySelector('#accountName');
  const host = document.querySelector('#entryAccountButtons');
  const row = document.querySelector('#entryAccountChoiceRow');
  if (!select || !host || !row) return;

  const mobile = window.matchMedia(CY_V21_BUILD9_MOBILE);
  row.hidden = false;
  const observer = new MutationObserver(syncV21Build8AccountChoices);
  observer.observe(select, { childList: true, subtree: true });
  select.addEventListener('change', syncV21Build8AccountChoices);

  host.addEventListener('click', event => {
    const button = event.target.closest('[data-entry-account]');
    if (!button) return;
    selectV21Build8Account(button.dataset.entryAccount || '', !mobile.matches);
    if (mobile.matches) {
      setV21Build9AccountPickerOpen(false);
      document.querySelector('#entryAccountPickerButton')?.focus();
    }
  });

  host.addEventListener('keydown', event => {
    const button = event.target.closest('[data-entry-account]');
    if (!button) return;
    const buttons = [...host.querySelectorAll('[data-entry-account]')];
    const index = buttons.indexOf(button);
    if (index < 0 || !['ArrowLeft', 'ArrowRight', 'ArrowUp', 'ArrowDown'].includes(event.key)) return;
    event.preventDefault();
    const delta = event.key === 'ArrowLeft' || event.key === 'ArrowUp' ? -1 : 1;
    const next = buttons[(index + delta + buttons.length) % buttons.length];
    if (next) selectV21Build8Account(next.dataset.entryAccount || '', true);
  });

  syncV21Build8AccountChoices();
}

function syncV21Build8AccountChoices() {
  const select = document.querySelector('#accountName');
  const host = document.querySelector('#entryAccountButtons');
  if (!select || !host) return;

  const options = [...select.options].filter(option => option.value);
  const signature = options.map(option => option.value).join('\u001f');
  if (host.dataset.accountSignature !== signature) {
    host.dataset.accountSignature = signature;
    host.innerHTML = options.map(option => `
      <button type="button" class="entry-account-choice" role="radio" data-entry-account="${v21Build8Escape(option.value)}" aria-checked="false" tabindex="-1">${v21Build8Escape(option.textContent || option.value)}</button>
    `).join('');
  }

  const selected = select.value;
  for (const button of host.querySelectorAll('[data-entry-account]')) {
    const active = button.dataset.entryAccount === selected;
    button.classList.toggle('active', active);
    button.setAttribute('aria-checked', active ? 'true' : 'false');
    button.tabIndex = active ? 0 : -1;
    button.title = active ? '目前使用中的帳戶' : `切換至帳戶「${button.dataset.entryAccount}」`;
  }
  syncV21Build9AccountPickerLabel();
}

function selectV21Build8Account(name, focus = false) {
  const select = document.querySelector('#accountName');
  const host = document.querySelector('#entryAccountButtons');
  if (!select || !host || ![...select.options].some(option => option.value === name)) return;
  if (select.value !== name) {
    select.value = name;
    select.dispatchEvent(new Event('change', { bubbles: true }));
  }
  syncV21Build8AccountChoices();
  if (focus) host.querySelector(`[data-entry-account="${CSS.escape(name)}"]`)?.focus();
}

function setupV21Build9MobileAccountPicker() {
  const row = document.querySelector('#entryAccountChoiceRow');
  const host = document.querySelector('#entryAccountButtons');
  if (!row || !host) return;

  let trigger = document.querySelector('#entryAccountPickerButton');
  if (!trigger) {
    trigger = document.createElement('button');
    trigger.type = 'button';
    trigger.id = 'entryAccountPickerButton';
    trigger.className = 'entry-account-picker-trigger';
    trigger.setAttribute('aria-haspopup', 'true');
    trigger.setAttribute('aria-expanded', 'false');
    trigger.innerHTML = '<span class="entry-account-picker-value">選擇帳戶</span><span class="entry-account-picker-arrow" aria-hidden="true">▾</span>';
    row.insertBefore(trigger, host);
  }

  trigger.addEventListener('click', () => {
    if (!window.matchMedia(CY_V21_BUILD9_MOBILE).matches) return;
    setV21Build9AccountPickerOpen(!row.classList.contains('mobile-picker-open'));
  });

  document.addEventListener('pointerdown', event => {
    if (!window.matchMedia(CY_V21_BUILD9_MOBILE).matches || row.contains(event.target)) return;
    setV21Build9AccountPickerOpen(false);
  });

  document.addEventListener('keydown', event => {
    if (event.key !== 'Escape' || !row.classList.contains('mobile-picker-open')) return;
    setV21Build9AccountPickerOpen(false);
    trigger.focus();
  });

  const mobile = window.matchMedia(CY_V21_BUILD9_MOBILE);
  const syncMode = () => {
    setV21Build9AccountPickerOpen(false);
    syncV21Build9AccountPickerLabel();
  };
  if (typeof mobile.addEventListener === 'function') mobile.addEventListener('change', syncMode);
  else mobile.addListener?.(syncMode);
  syncMode();
}

function setV21Build9AccountPickerOpen(open) {
  const row = document.querySelector('#entryAccountChoiceRow');
  const trigger = document.querySelector('#entryAccountPickerButton');
  const host = document.querySelector('#entryAccountButtons');
  if (!row || !trigger || !host) return;

  const isMobile = window.matchMedia(CY_V21_BUILD9_MOBILE).matches;
  if (!isMobile) {
    row.classList.remove('mobile-picker-open');
    trigger.setAttribute('aria-expanded', 'false');
    host.removeAttribute('aria-hidden');
    return;
  }

  const next = Boolean(open);
  row.classList.toggle('mobile-picker-open', next);
  trigger.setAttribute('aria-expanded', next ? 'true' : 'false');
  host.setAttribute('aria-hidden', next ? 'false' : 'true');
  if (next) {
    const active = host.querySelector('.entry-account-choice.active') || host.querySelector('.entry-account-choice');
    requestAnimationFrame(() => active?.focus());
  }
}

function syncV21Build9AccountPickerLabel() {
  const select = document.querySelector('#accountName');
  const trigger = document.querySelector('#entryAccountPickerButton');
  const value = trigger?.querySelector('.entry-account-picker-value');
  if (!select || !value) return;
  const option = select.selectedOptions?.[0];
  value.textContent = option?.textContent?.trim() || select.value || '選擇帳戶';
}

function setupV21Build9MobilePages() {
  const topbar = document.querySelector('.topbar');
  const shell = document.querySelector('.shell');
  const entry = shell?.querySelector('.entry-card');
  const ledger = shell?.querySelector('.ledger-card');
  if (!topbar || !shell || !entry || !ledger) return;

  let nav = document.querySelector('#mobileMainNav');
  if (!nav) {
    nav = document.createElement('nav');
    nav.id = 'mobileMainNav';
    nav.className = 'v21-mobile-main-nav';
    nav.setAttribute('aria-label', '主要頁面');
    nav.innerHTML = `
      <button type="button" class="active" data-mobile-page="entry" aria-selected="true">新增記帳</button>
      <button type="button" data-mobile-page="ledger" aria-selected="false">記帳資料</button>`;
    topbar.insertAdjacentElement('afterend', nav);
  }

  let current = 'entry';
  const mobile = window.matchMedia(CY_V21_BUILD9_MOBILE);

  const apply = page => {
    current = page === 'ledger' ? 'ledger' : 'entry';
    const enabled = mobile.matches;
    nav.hidden = !enabled;
    entry.classList.toggle('v21-mobile-page-hidden', enabled && current !== 'entry');
    ledger.classList.toggle('v21-mobile-page-hidden', enabled && current !== 'ledger');
    shell.dataset.mobilePage = enabled ? current : '';
    for (const button of nav.querySelectorAll('[data-mobile-page]')) {
      const active = button.dataset.mobilePage === current;
      button.classList.toggle('active', active);
      button.setAttribute('aria-selected', active ? 'true' : 'false');
    }
  };

  nav.addEventListener('click', event => {
    const button = event.target.closest('[data-mobile-page]');
    if (!button || !mobile.matches) return;
    apply(button.dataset.mobilePage);
    window.scrollTo({ top: 0, behavior: 'smooth' });
  });

  const syncMode = () => apply(current);
  if (typeof mobile.addEventListener === 'function') mobile.addEventListener('change', syncMode);
  else mobile.addListener?.(syncMode);
  apply('entry');
}

function setupV21Build8SummaryLimit() {
  const composing = new WeakSet();
  const fields = [document.querySelector('#summary'), document.querySelector('#editSummary')].filter(Boolean);

  for (const input of fields) {
    input.maxLength = 40;
    input.addEventListener('compositionstart', () => composing.add(input));
    input.addEventListener('compositionend', () => {
      composing.delete(input);
      enforceV21Build8Summary(input);
    });
    input.addEventListener('input', () => {
      if (!composing.has(input)) enforceV21Build8Summary(input);
    });
  }

  document.querySelector('#summarySuggestions')?.addEventListener('click', () => {
    setTimeout(() => {
      const summary = document.querySelector('#summary');
      if (summary) enforceV21Build8Summary(summary);
    }, 0);
  });

  const entryForm = document.querySelector('#transactionForm');
  entryForm?.addEventListener('submit', event => {
    const summary = document.querySelector('#summary');
    if (!summary || v21Build8WeightedUnits(summary.value) <= CY_V21_BUILD8_SUMMARY_UNITS) return;
    event.preventDefault();
    event.stopImmediatePropagation();
    if (typeof showMessage === 'function') showMessage('摘要不可超過 20 個中文字或 40 個英數字元。', true);
    summary.focus();
  }, true);

  const editForm = document.querySelector('#editTransactionForm');
  editForm?.addEventListener('submit', event => {
    const summary = document.querySelector('#editSummary');
    if (!summary || v21Build8WeightedUnits(summary.value) <= CY_V21_BUILD8_SUMMARY_UNITS) return;
    event.preventDefault();
    event.stopImmediatePropagation();
    const message = document.querySelector('#editMessage');
    if (message && typeof setDialogMessage === 'function') setDialogMessage(message, '摘要不可超過 20 個中文字或 40 個英數字元。', true);
    summary.focus();
  }, true);
}

function enforceV21Build8Summary(input) {
  if (v21Build8WeightedUnits(input.value) <= CY_V21_BUILD8_SUMMARY_UNITS) return;
  const trimmed = v21Build8TrimWeighted(input.value, CY_V21_BUILD8_SUMMARY_UNITS);
  const cursor = input.selectionStart ?? trimmed.length;
  input.value = trimmed;
  try { input.setSelectionRange(Math.min(cursor, trimmed.length), Math.min(cursor, trimmed.length)); } catch { /* no-op */ }
}

function v21Build8WeightedUnits(value) {
  let units = 0;
  for (const char of String(value || '')) units += v21Build8CharUnits(char);
  return units;
}

function v21Build8TrimWeighted(value, maxUnits) {
  let units = 0;
  let result = '';
  for (const char of String(value || '')) {
    const next = v21Build8CharUnits(char);
    if (units + next > maxUnits) break;
    units += next;
    result += char;
  }
  return result;
}

function v21Build8CharUnits(char) {
  const code = char.codePointAt(0) || 0;
  if (code <= 0x7f) return 1;
  if (code >= 0xff61 && code <= 0xff9f) return 1;
  return 2;
}

function setupV21Build8RoleMedal() {
  const target = document.querySelector('#currentUser');
  if (!target) return;
  const observer = new MutationObserver(syncV21Build8RoleMedal);
  observer.observe(target, { childList: true, subtree: true, characterData: true });
  syncV21Build8RoleMedal();
}

function syncV21Build8RoleMedal() {
  const target = document.querySelector('#currentUser');
  const role = String(target?.querySelector('.current-user-role')?.textContent || '').trim();
  if (!target) return;
  target.classList.toggle('role-super-admin', role === '超級管理員');
  target.classList.toggle('role-admin', role === '管理員');
}

function setupV21Build9EnterHints() {
  const date = document.querySelector('#txDate');
  const summary = document.querySelector('#summary');
  const amount = document.querySelector('#amount');
  if (date) date.setAttribute('enterkeyhint', 'next');
  if (summary) summary.setAttribute('enterkeyhint', 'next');
  if (amount) amount.setAttribute('enterkeyhint', 'done');
}

function syncV21Build9HelpCopy() {
  const hint = document.querySelector('.keyboard-hint');
  if (hint) {
    hint.innerHTML = '鍵盤：日期 Enter → 摘要 Enter → 金額 Enter 儲存 → 回摘要　｜　<kbd>Tab</kbd> 切換收入／支出　｜　日期可輸入 <kbd>0924</kbd> / <kbd>20260924</kbd>，<kbd>Ctrl</kbd>+<kbd>↑↓</kbd> ±1 天';
  }

  const grid = document.querySelector('#entryHelpPopover .entry-help-grid');
  if (grid) {
    grid.innerHTML = `
      <kbd>Enter</kbd><span>日期 → 摘要 → 金額 → 儲存，成功後回摘要</span>
      <kbd>Tab</kbd><span>切換收入／支出，游標留在目前欄位</span>
      <kbd>0924</kbd><span>輸入今年 09/24</span>
      <kbd>20260924</kbd><span>輸入完整日期</span>
      <kbd>Ctrl + ↑↓</kbd><span>日期 ±1 天</span>`;
  }
}

function v21Build8Escape(value) {
  return String(value ?? '')
    .replaceAll('&', '&amp;')
    .replaceAll('<', '&lt;')
    .replaceAll('>', '&gt;')
    .replaceAll('"', '&quot;')
    .replaceAll("'", '&#039;');
}

/* ---- baseline section ---- */
const CY_V21_BUILD10_VERSION = 'V0.21.0 Build 10';
const CY_V21_BUILD10_MOBILE = '(max-width: 767px)';
const CY_V21_CONFIRMATION_KEY = 'cyaccounting.confirmationDrawerOpen';

if (!window.__CYACC_BASELINE_BUNDLE__) ensureV21Build10Stylesheet();
let cyV21Build10Started = false;

if (document.readyState === 'loading') {
  document.addEventListener('DOMContentLoaded', startV21Build10, { once: true });
} else {
  startV21Build10();
}
window.addEventListener('load', syncV21Build10AfterLoad, { once: true });

function startV21Build10() {
  if (cyV21Build10Started) return;
  cyV21Build10Started = true;
  runV21Build10Step('mobile-app-bar', setupV21Build10MobileAppBar);
  runV21Build10Step('mobile-navigation', setupV21Build10MobileNavigation);
  runV21Build10Step('account-sheet', setupV21Build10AccountSheet);
  runV21Build10Step('ledger-tools', setupV21Build10LedgerTools);
  runV21Build10Step('confirmation-policy', setupV21Build10ConfirmationPolicy);
  runV21Build10Step('mobile-form-copy', setupV21Build10MobileFormCopy);
  runV21Build10Step('version', syncV21Build10Version);
}

function runV21Build10Step(name, task) {
  try {
    task();
  } catch (error) {
    console.error('cyaccounting_mobile_build10_step_failed', name, error instanceof Error ? error.message : 'unknown_error');
  }
}

function syncV21Build10AfterLoad() {
  startV21Build10();
  syncV21Build10Version();
  syncV21Build10MobileIdentity();
  syncV21Build10MobileNavigation();
  syncV21Build10ConfirmationPolicy();
}

function ensureV21Build10Stylesheet() {
  if (document.querySelector('link[href^="/v021b10.css"]')) return;
  const link = document.createElement('link');
  link.rel = 'stylesheet';
  link.href = '/v021b10.css?v=0216b6';
  document.head.appendChild(link);
}

function syncV21Build10Version() {
  const version = document.querySelector('.version');
  if (version && version.textContent !== CY_V21_BUILD10_VERSION) version.textContent = CY_V21_BUILD10_VERSION;
}

function setupV21Build10MobileAppBar() {
  const topbar = document.querySelector('.topbar');
  const currentUser = document.querySelector('#currentUser');
  const logoutButton = document.querySelector('#logoutButton');
  if (!topbar || !currentUser || !logoutButton) return;

  let trigger = document.querySelector('#mobileAccountMenuButton');
  if (!trigger) {
    trigger = document.createElement('button');
    trigger.id = 'mobileAccountMenuButton';
    trigger.className = 'v21-mobile-account-menu-button';
    trigger.type = 'button';
    trigger.setAttribute('aria-haspopup', 'true');
    trigger.setAttribute('aria-expanded', 'false');
    trigger.innerHTML = '<span class="v21-mobile-account-name">帳號</span><span aria-hidden="true">›</span>';
    topbar.append(trigger);
  }

  let menu = document.querySelector('#mobileAccountMenu');
  if (!menu) {
    menu = document.createElement('div');
    menu.id = 'mobileAccountMenu';
    menu.className = 'v21-mobile-account-menu';
    menu.hidden = true;
    menu.innerHTML = `
      <div class="v21-mobile-account-menu-identity">
        <strong id="mobileAccountMenuName">帳號</strong>
        <span id="mobileAccountMenuRole"></span>
      </div>
      <button type="button" class="danger-lite" data-mobile-account-action="logout">登出</button>`;
    document.body.append(menu);
  }

  const close = () => {
    menu.hidden = true;
    trigger.setAttribute('aria-expanded', 'false');
    document.body.classList.remove('v21-mobile-account-menu-open');
  };

  trigger.addEventListener('click', event => {
    if (!window.matchMedia(CY_V21_BUILD10_MOBILE).matches) return;
    event.stopPropagation();
    const open = menu.hidden;
    menu.hidden = !open;
    trigger.setAttribute('aria-expanded', open ? 'true' : 'false');
    document.body.classList.toggle('v21-mobile-account-menu-open', open);
    if (open) syncV21Build10MobileIdentity();
  });

  menu.addEventListener('click', event => {
    const action = event.target.closest('[data-mobile-account-action]')?.dataset.mobileAccountAction;
    if (!action) return;
    close();
    if (action === 'logout') logoutButton.click();
  });

  document.addEventListener('pointerdown', event => {
    if (menu.hidden || trigger.contains(event.target) || menu.contains(event.target)) return;
    close();
  });

  document.addEventListener('keydown', event => {
    if (event.key !== 'Escape' || menu.hidden) return;
    close();
    trigger.focus();
  });

  const observer = new MutationObserver(syncV21Build10MobileIdentity);
  observer.observe(currentUser, { childList: true, subtree: true, characterData: true, attributes: true, attributeFilter: ['class'] });

  const mobile = window.matchMedia(CY_V21_BUILD10_MOBILE);
  const syncMode = () => {
    document.body.classList.toggle('v21-mobile-app', mobile.matches);
    if (!mobile.matches) close();
    syncV21Build10MobileIdentity();
  };
  if (typeof mobile.addEventListener === 'function') mobile.addEventListener('change', syncMode);
  else mobile.addListener?.(syncMode);
  syncMode();
}

function syncV21Build10MobileIdentity() {
  const source = document.querySelector('#currentUser');
  const trigger = document.querySelector('#mobileAccountMenuButton');
  const triggerName = trigger?.querySelector('.v21-mobile-account-name');
  const menu = document.querySelector('#mobileAccountMenu');
  const menuName = document.querySelector('#mobileAccountMenuName');
  const menuRole = document.querySelector('#mobileAccountMenuRole');
  if (!source || !trigger || !triggerName || !menu || !menuName || !menuRole) return;

  const main = String(source.querySelector('.current-user-main')?.textContent || source.textContent || '').trim() || '帳號';
  const role = String(source.querySelector('.current-user-role')?.textContent || '').trim();
  triggerName.textContent = main;
  menuName.textContent = main;
  menuRole.textContent = role;

  const superAdmin = source.classList.contains('role-super-admin') || role === '超級管理員';
  const admin = source.classList.contains('role-admin') || role === '管理員';
  trigger.classList.toggle('role-super-admin', superAdmin);
  trigger.classList.toggle('role-admin', admin);
  menu.classList.toggle('role-super-admin', superAdmin);
  menu.classList.toggle('role-admin', admin);
}

function setupV21Build10MobileNavigation() {
  const sync = () => {
    const nav = document.querySelector('#mobileMainNav');
    if (!nav) return false;
    nav.classList.add('v21-mobile-bottom-nav');
    if (nav.parentElement !== document.body) document.body.append(nav);

    const entryButton = nav.querySelector('[data-mobile-page="entry"]');
    const ledgerButton = nav.querySelector('[data-mobile-page="ledger"]');
    if (entryButton && !entryButton.querySelector('.v21-mobile-nav-icon')) {
      entryButton.innerHTML = '<span class="v21-mobile-nav-icon" aria-hidden="true">＋</span><span>新增記帳</span>';
    }
    if (ledgerButton && !ledgerButton.querySelector('.v21-mobile-nav-icon')) {
      ledgerButton.innerHTML = '<span class="v21-mobile-nav-icon" aria-hidden="true">≡</span><span>記帳資料</span>';
    }
    return true;
  };

  if (!sync()) setTimeout(sync, 0);
  const mobile = window.matchMedia(CY_V21_BUILD10_MOBILE);
  const syncMode = () => {
    sync();
    document.body.classList.toggle('v21-mobile-app', mobile.matches);
  };
  if (typeof mobile.addEventListener === 'function') mobile.addEventListener('change', syncMode);
  else mobile.addListener?.(syncMode);
}

function syncV21Build10MobileNavigation() {
  const nav = document.querySelector('#mobileMainNav');
  if (!nav) return;
  nav.classList.add('v21-mobile-bottom-nav');
  if (nav.parentElement !== document.body) document.body.append(nav);
}

function setupV21Build10AccountSheet() {
  const row = document.querySelector('#entryAccountChoiceRow');
  if (!row) return;

  let backdrop = document.querySelector('#mobileAccountSheetBackdrop');
  if (!backdrop) {
    backdrop = document.createElement('button');
    backdrop.id = 'mobileAccountSheetBackdrop';
    backdrop.className = 'v21-mobile-sheet-backdrop';
    backdrop.type = 'button';
    backdrop.setAttribute('aria-label', '關閉帳戶選單');
    backdrop.hidden = true;
    document.body.append(backdrop);
  }

  const sync = () => {
    const open = window.matchMedia(CY_V21_BUILD10_MOBILE).matches && row.classList.contains('mobile-picker-open');
    backdrop.hidden = !open;
    document.body.classList.toggle('v21-mobile-account-sheet-open', open);
  };

  backdrop.addEventListener('click', () => {
    if (typeof setV21Build9AccountPickerOpen === 'function') setV21Build9AccountPickerOpen(false);
  });

  const observer = new MutationObserver(sync);
  observer.observe(row, { attributes: true, attributeFilter: ['class'] });
  document.querySelector('#entryAccountButtons')?.addEventListener('click', () => setTimeout(sync, 0));
  sync();
}

function setupV21Build10LedgerTools() {
  const ledger = document.querySelector('.ledger-card');
  if (!ledger) return;

  let button = document.querySelector('#mobileLedgerMoreButton');
  if (!button) {
    button = document.createElement('button');
    button.id = 'mobileLedgerMoreButton';
    button.className = 'secondary compact v21-mobile-ledger-more';
    button.type = 'button';
    button.textContent = '更多';
    button.setAttribute('aria-haspopup', 'true');
    button.setAttribute('aria-expanded', 'false');
    const summaryBar = ledger.querySelector('.v21-ledger-summary-bar') || ledger.querySelector('.ledger-title');
    summaryBar?.append(button);
  }

  let backdrop = document.querySelector('#mobileLedgerToolsBackdrop');
  if (!backdrop) {
    backdrop = document.createElement('button');
    backdrop.id = 'mobileLedgerToolsBackdrop';
    backdrop.className = 'v21-mobile-sheet-backdrop';
    backdrop.type = 'button';
    backdrop.setAttribute('aria-label', '關閉記帳工具');
    backdrop.hidden = true;
    document.body.append(backdrop);
  }

  let sheet = document.querySelector('#mobileLedgerToolsSheet');
  if (!sheet) {
    sheet = document.createElement('section');
    sheet.id = 'mobileLedgerToolsSheet';
    sheet.className = 'v21-mobile-tools-sheet';
    sheet.hidden = true;
    sheet.innerHTML = `
      <div class="v21-mobile-sheet-handle" aria-hidden="true"></div>
      <h3>更多</h3>
      <button type="button" data-mobile-ledger-action="accounts">帳戶設定</button>
      <button type="button" data-mobile-ledger-action="categories">科目設定</button>
      <button type="button" data-mobile-ledger-action="lock">月份鎖帳</button>
      <button type="button" data-mobile-ledger-action="export">匯出 Excel</button>
      <button type="button" class="secondary" data-mobile-ledger-action="close">取消</button>`;
    document.body.append(sheet);
  }

  const close = () => {
    sheet.hidden = true;
    backdrop.hidden = true;
    button.setAttribute('aria-expanded', 'false');
    document.body.classList.remove('v21-mobile-ledger-tools-open');
  };

  const open = () => {
    if (!window.matchMedia(CY_V21_BUILD10_MOBILE).matches) return;
    sheet.hidden = false;
    backdrop.hidden = false;
    button.setAttribute('aria-expanded', 'true');
    document.body.classList.add('v21-mobile-ledger-tools-open');
  };

  button.addEventListener('click', open);
  backdrop.addEventListener('click', close);
  sheet.addEventListener('click', event => {
    const action = event.target.closest('[data-mobile-ledger-action]')?.dataset.mobileLedgerAction;
    if (!action) return;
    if (action === 'close') {
      close();
      return;
    }
    close();
    if (action === 'accounts') {
      if (typeof window.cyOpenMobileSettingsPane === 'function') window.cyOpenMobileSettingsPane('accounts');
      else {
        if (typeof openSettings === 'function') openSettings();
        if (typeof setSettingsTab === 'function') setSettingsTab('accounts');
      }
    }
    if (action === 'categories') {
      if (typeof window.cyOpenMobileSettingsPane === 'function') window.cyOpenMobileSettingsPane('categories');
      else {
        if (typeof openSettings === 'function') openSettings();
        if (typeof setSettingsTab === 'function') setSettingsTab('categories');
      }
    }
    if (action === 'lock') {
      if (typeof window.cyOpenMobileLedgerLock === 'function') window.cyOpenMobileLedgerLock();
      else document.querySelector('#ledgerLockSettingsButton')?.click();
    }
    if (action === 'export') document.querySelector('#ledgerExcelExport')?.click();
  });

  document.addEventListener('keydown', event => {
    if (event.key !== 'Escape' || sheet.hidden) return;
    close();
    button.focus();
  });
}

function setupV21Build10ConfirmationPolicy() {
  const mobile = window.matchMedia(CY_V21_BUILD10_MOBILE);
  const sync = () => {
    if (mobile.matches) {
      if (typeof setConfirmationDrawer === 'function') setConfirmationDrawer(false, false);
      return;
    }
    const shouldOpen = localStorage.getItem(CY_V21_CONFIRMATION_KEY) === '1';
    if (typeof setConfirmationDrawer === 'function') setConfirmationDrawer(shouldOpen, false);
  };
  if (typeof mobile.addEventListener === 'function') mobile.addEventListener('change', sync);
  else mobile.addListener?.(sync);
  sync();
}

function syncV21Build10ConfirmationPolicy() {
  if (!window.matchMedia(CY_V21_BUILD10_MOBILE).matches) return;
  if (typeof setConfirmationDrawer === 'function') setConfirmationDrawer(false, false);
}

function setupV21Build10MobileFormCopy() {
  const summary = document.querySelector('#summary');
  if (summary) summary.placeholder = '可留白，最多 20 個中文字';

  const trigger = document.querySelector('#entryAccountPickerButton');
  const arrow = trigger?.querySelector('.entry-account-picker-arrow');
  if (arrow) arrow.textContent = '›';
}

/* ---- baseline section ---- */
const CY_V21_BUILD11_VERSION = 'V0.21.0 Build 11';
const CY_V21_BUILD11_DESKTOP = '(min-width: 768px)';

if (!window.__CYACC_BASELINE_BUNDLE__) ensureV21Build11Stylesheet();
if (!window.__CYACC_BASELINE_BUNDLE__) ensureV21Build12Script();
if (!window.__CYACC_BASELINE_BUNDLE__) ensureV21Build13Script();
if (!window.__CYACC_BASELINE_BUNDLE__) ensureV21Build14Script();
if (!window.__CYACC_BASELINE_BUNDLE__) ensureV21Build15Script();
if (!window.__CYACC_BASELINE_BUNDLE__) ensureV21Build16Script();
if (!window.__CYACC_BASELINE_BUNDLE__) ensureV0211PatchScript();
if (!window.__CYACC_BASELINE_BUNDLE__) ensureV0211KeyboardScript();
if (!window.__CYACC_BASELINE_BUNDLE__) ensureV0212PatchScript();
if (!window.__CYACC_BASELINE_BUNDLE__) ensureV0214PatchScript();
if (!window.__CYACC_BASELINE_BUNDLE__) ensureV0215Stylesheet();
if (!window.__CYACC_BASELINE_BUNDLE__) ensureV0215Build3Script();
if (!window.__CYACC_BASELINE_BUNDLE__) ensureV0215Build4Script();
let cyV21Build11Started = false;

if (document.readyState === 'loading') {
  document.addEventListener('DOMContentLoaded', startV21Build11, { once: true });
} else {
  startV21Build11();
}
window.addEventListener('load', startV21Build11, { once: true });

function startV21Build11() {
  if (cyV21Build11Started) return;
  cyV21Build11Started = true;
  syncV21Build11Version();
  setupV21Build11DesktopIsolation();
}

function ensureV21Build11Stylesheet() {
  if (document.querySelector('link[href^="/v021b11.css"]')) return;
  const link = document.createElement('link');
  link.rel = 'stylesheet';
  link.href = '/v021b11.css?v=0216b6';
  document.head.appendChild(link);
}

function ensureV21Build12Script() {
  if (document.querySelector('script[src="/v021b12.js"]')) return;
  const script = document.createElement('script');
  script.src = '/v021b12.js';
  script.async = false;
  document.head.appendChild(script);
}

function ensureV21Build13Script() {
  if (document.querySelector('script[src="/v021b13.js"]')) return;
  const script = document.createElement('script');
  script.src = '/v021b13.js';
  script.async = false;
  document.head.appendChild(script);
}

function ensureV21Build14Script() {
  if (document.querySelector('script[src="/v021b14.js"]')) return;
  const script = document.createElement('script');
  script.src = '/v021b14.js';
  script.async = false;
  document.head.appendChild(script);
}

function ensureV21Build15Script() {
  if (document.querySelector('script[src="/v021b15.js"]')) return;
  const script = document.createElement('script');
  script.src = '/v021b15.js';
  script.async = false;
  document.head.appendChild(script);
}

function ensureV21Build16Script() {
  if (document.querySelector('script[src="/v021b16.js"]')) return;
  const script = document.createElement('script');
  script.src = '/v021b16.js';
  script.async = false;
  document.head.appendChild(script);
}

function ensureV0211PatchScript() {
  if (document.querySelector('script[src="/v0211.js"]')) return;
  const script = document.createElement('script');
  script.src = '/v0211.js';
  script.async = false;
  document.head.appendChild(script);
}

function ensureV0211KeyboardScript() {
  if (document.querySelector('script[src="/v0211-keyboard.js"]')) return;
  const script = document.createElement('script');
  script.src = '/v0211-keyboard.js';
  script.async = false;
  document.head.appendChild(script);
}

function ensureV0212PatchScript() {
  if (document.querySelector('script[src="/v0212.js"]')) return;
  const script = document.createElement('script');
  script.src = '/v0212.js';
  script.async = false;
  document.head.appendChild(script);
}

function ensureV0214PatchScript() {
  if (document.querySelector('script[src="/v0214.js"]')) return;
  const script = document.createElement('script');
  script.src = '/v0214.js';
  script.async = false;
  document.head.appendChild(script);
}

function ensureV0215Stylesheet() {
  if (document.querySelector('link[href^="/v0215.css"]')) return;
  const link = document.createElement('link');
  link.rel = 'stylesheet';
  link.href = '/v0215.css?v=0216b6';
  document.head.appendChild(link);
}

function ensureV0215Build3Script() {
  if (document.querySelector('script[src="/v0215b3.js"]')) return;
  const script = document.createElement('script');
  script.src = '/v0215b3.js';
  script.async = false;
  document.head.appendChild(script);
}

function ensureV0215Build4Script() {
  if (document.querySelector('script[src^="/v0215b4.js"]')) return;
  const script = document.createElement('script');
  script.src = '/v0215b4.js?v=0215b10';
  script.async = false;
  document.head.appendChild(script);
}

function syncV21Build11Version() {
  const version = document.querySelector('.version');
  if (version) version.textContent = CY_V21_BUILD11_VERSION;
}

function setupV21Build11DesktopIsolation() {
  const media = window.matchMedia(CY_V21_BUILD11_DESKTOP);
  const sync = () => syncV21Build11DesktopIsolation(media.matches);
  sync();
  if (typeof media.addEventListener === 'function') media.addEventListener('change', sync);
  else media.addListener?.(sync);
}

function syncV21Build11DesktopIsolation(desktop = window.matchMedia(CY_V21_BUILD11_DESKTOP).matches) {
  const accountTrigger = document.querySelector('#mobileAccountMenuButton');
  const ledgerMore = document.querySelector('#mobileLedgerMoreButton');

  if (accountTrigger) accountTrigger.hidden = Boolean(desktop);
  if (ledgerMore) ledgerMore.hidden = Boolean(desktop);

  if (!desktop) {
    document.body.classList.add('v21-mobile-app');
    return;
  }

  document.body.classList.remove(
    'v21-mobile-app',
    'v21-mobile-account-menu-open',
    'v21-mobile-account-sheet-open',
    'v21-mobile-ledger-tools-open'
  );

  const accountMenu = document.querySelector('#mobileAccountMenu');
  const accountBackdrop = document.querySelector('#mobileAccountSheetBackdrop');
  const ledgerBackdrop = document.querySelector('#mobileLedgerToolsBackdrop');
  const ledgerSheet = document.querySelector('#mobileLedgerToolsSheet');
  if (accountMenu) accountMenu.hidden = true;
  if (accountBackdrop) accountBackdrop.hidden = true;
  if (ledgerBackdrop) ledgerBackdrop.hidden = true;
  if (ledgerSheet) ledgerSheet.hidden = true;

  accountTrigger?.setAttribute('aria-expanded', 'false');
  ledgerMore?.setAttribute('aria-expanded', 'false');
}

/* ---- baseline section ---- */
const CY_V21_BUILD12_VERSION = 'V0.21.0 Build 12';
const CY_V21_BUILD12_DESKTOP = '(min-width: 1024px)';
const CY_V21_BUILD12_MONTHS = ['一月', '二月', '三月', '四月', '五月', '六月', '七月', '八月', '九月', '十月', '十一月', '十二月'];

if (!window.__CYACC_BASELINE_BUNDLE__) ensureV21Build12Stylesheet();
syncV21Build12Version();
syncV21Build12Copy();

window.addEventListener('load', () => {
  syncV21Build12Version();
  syncV21Build12Copy();
  setupV21Build12DesktopMonthPicker();
});

function ensureV21Build12Stylesheet() {
  if (document.querySelector('link[href="/v021b12.css"]')) return;
  const link = document.createElement('link');
  link.rel = 'stylesheet';
  link.href = '/v021b12.css';
  document.head.appendChild(link);
}

function syncV21Build12Version() {
  const version = document.querySelector('.version');
  if (version) version.textContent = CY_V21_BUILD12_VERSION;
}

function syncV21Build12Copy() {
  const summary = document.querySelector('#summary');
  const editSummary = document.querySelector('#editSummary');
  if (summary) summary.placeholder = '最多20個字';
  if (editSummary) editSummary.placeholder = '最多20個字';
}

function setupV21Build12DesktopMonthPicker() {
  const media = window.matchMedia(CY_V21_BUILD12_DESKTOP);
  const syncMode = () => {
    const root = document.querySelector('#ledgerMonthPickerCustom');
    if (root) {
      root.hidden = !media.matches;
      if (!media.matches) closeV21Build12MonthPicker(root);
    }
    if (media.matches) ensureV21Build12MonthPicker();
  };

  if (typeof media.addEventListener === 'function') media.addEventListener('change', syncMode);
  else media.addListener?.(syncMode);
  syncMode();
}

function ensureV21Build12MonthPicker() {
  const input = document.querySelector('#monthFilter');
  const slot = document.querySelector('#ledgerMonthSlot');
  if (!input || !slot) {
    setTimeout(ensureV21Build12MonthPicker, 60);
    return;
  }

  let root = document.querySelector('#ledgerMonthPickerCustom');
  if (root) {
    root.hidden = false;
    syncV21Build12MonthPickerLabel(root, input);
    return;
  }

  root = document.createElement('div');
  root.id = 'ledgerMonthPickerCustom';
  root.className = 'v21-month-picker-custom';
  root.innerHTML = `
    <button id="ledgerMonthPickerTrigger" class="v21-month-picker-trigger" type="button" aria-haspopup="dialog" aria-expanded="false">
      <span id="ledgerMonthPickerLabel">—</span><span class="v21-month-picker-caret" aria-hidden="true">▾</span>
    </button>
    <div id="ledgerMonthPickerPopover" class="v21-month-picker-popover" role="dialog" aria-label="選擇月份" hidden>
      <div class="v21-month-picker-head">
        <button type="button" class="v21-month-picker-nav" data-picker-nav="-1" aria-label="上一組">‹</button>
        <button id="ledgerMonthPickerYearButton" type="button" class="v21-month-picker-year" aria-label="切換年份選擇"></button>
        <button type="button" class="v21-month-picker-nav" data-picker-nav="1" aria-label="下一組">›</button>
      </div>
      <div id="ledgerMonthPickerGrid" class="v21-month-picker-grid"></div>
    </div>`;
  slot.append(root);

  const trigger = root.querySelector('#ledgerMonthPickerTrigger');
  const popover = root.querySelector('#ledgerMonthPickerPopover');
  const yearButton = root.querySelector('#ledgerMonthPickerYearButton');
  const grid = root.querySelector('#ledgerMonthPickerGrid');
  let view = 'months';
  let displayYear = v21Build12ReadMonth(input).year;
  let yearStart = displayYear - 5;

  const render = () => {
    const selected = v21Build12ReadMonth(input);
    if (view === 'months') {
      yearButton.textContent = String(displayYear);
      yearButton.title = '選擇年份';
      grid.className = 'v21-month-picker-grid month-view';
      grid.innerHTML = CY_V21_BUILD12_MONTHS.map((label, index) => {
        const month = index + 1;
        const active = selected.year === displayYear && selected.month === month;
        return `<button type="button" class="v21-month-choice${active ? ' active' : ''}" data-picker-month="${month}" aria-pressed="${active ? 'true' : 'false'}">${label}</button>`;
      }).join('');
      return;
    }

    yearButton.textContent = `${yearStart}–${yearStart + 11}`;
    yearButton.title = '返回月份選擇';
    grid.className = 'v21-month-picker-grid year-view';
    grid.innerHTML = Array.from({ length: 12 }, (_, index) => yearStart + index).map(year => {
      const active = year === selected.year;
      const current = year === new Date().getFullYear();
      return `<button type="button" class="v21-year-choice${active ? ' active' : ''}${current ? ' current' : ''}" data-picker-year="${year}" aria-pressed="${active ? 'true' : 'false'}">${year}</button>`;
    }).join('');
  };

  const open = () => {
    const selected = v21Build12ReadMonth(input);
    displayYear = selected.year;
    yearStart = displayYear - 5;
    view = 'months';
    render();
    popover.hidden = false;
    trigger.setAttribute('aria-expanded', 'true');
  };

  const close = focusTrigger => {
    popover.hidden = true;
    trigger.setAttribute('aria-expanded', 'false');
    if (focusTrigger) trigger.focus();
  };

  trigger.addEventListener('click', event => {
    event.stopPropagation();
    if (!window.matchMedia(CY_V21_BUILD12_DESKTOP).matches) return;
    if (popover.hidden) open(); else close(false);
  });

  root.querySelectorAll('[data-picker-nav]').forEach(button => {
    button.addEventListener('click', () => {
      const delta = Number(button.dataset.pickerNav) || 0;
      if (view === 'months') displayYear += delta;
      else yearStart += delta * 12;
      render();
    });
  });

  yearButton.addEventListener('click', () => {
    if (view === 'months') {
      view = 'years';
      yearStart = displayYear - 5;
    } else {
      view = 'months';
    }
    render();
  });

  grid.addEventListener('click', event => {
    const monthButton = event.target.closest('[data-picker-month]');
    if (monthButton) {
      const month = Number(monthButton.dataset.pickerMonth);
      if (month >= 1 && month <= 12) {
        input.value = `${displayYear}-${String(month).padStart(2, '0')}`;
        input.dispatchEvent(new Event('change', { bubbles: true }));
        syncV21Build12MonthPickerLabel(root, input);
        close(true);
      }
      return;
    }

    const yearButtonChoice = event.target.closest('[data-picker-year]');
    if (yearButtonChoice) {
      displayYear = Number(yearButtonChoice.dataset.pickerYear) || displayYear;
      view = 'months';
      render();
    }
  });

  input.addEventListener('change', () => {
    const selected = v21Build12ReadMonth(input);
    displayYear = selected.year;
    syncV21Build12MonthPickerLabel(root, input);
    if (!popover.hidden) render();
  });

  document.addEventListener('pointerdown', event => {
    if (popover.hidden || root.contains(event.target)) return;
    close(false);
  });

  document.addEventListener('keydown', event => {
    if (event.key !== 'Escape' || popover.hidden) return;
    close(true);
  });

  root._v21Build12Close = close;
  syncV21Build12MonthPickerLabel(root, input);
}

function syncV21Build12MonthPickerLabel(root, input) {
  const label = root?.querySelector('#ledgerMonthPickerLabel');
  if (!label || !input) return;
  const selected = v21Build12ReadMonth(input);
  label.textContent = `${selected.year}年${String(selected.month).padStart(2, '0')}月`;
}

function closeV21Build12MonthPicker(root) {
  const popover = root?.querySelector('#ledgerMonthPickerPopover');
  const trigger = root?.querySelector('#ledgerMonthPickerTrigger');
  if (popover) popover.hidden = true;
  trigger?.setAttribute('aria-expanded', 'false');
}

function v21Build12ReadMonth(input) {
  const value = String(input?.value || '');
  const match = /^(\d{4})-(\d{2})$/.exec(value);
  if (match) return { year: Number(match[1]), month: Number(match[2]) };
  const now = new Date();
  return { year: now.getFullYear(), month: now.getMonth() + 1 };
}

/* ---- baseline section ---- */
const CY_V21_BUILD13_VERSION = 'V0.21.0 Build 13';
const CY_V21_BUILD13_DESKTOP = '(min-width: 1024px)';

if (!window.__CYACC_BASELINE_BUNDLE__) ensureV21Build13Stylesheet();
syncV21Build13Version();
setupV21Build13ConnectionStatus();

const runV21Build13 = () => {
  syncV21Build13Version();
  setupV21Build13ConnectionStatus();
  setupV21Build13HeaderManagement();
  setupV21Build13OpeningDialog();
  syncV21Build13CrudCopy();
};

if (document.readyState === 'complete') setTimeout(runV21Build13, 0);
else window.addEventListener('load', () => setTimeout(runV21Build13, 0), { once: true });

function ensureV21Build13Stylesheet() {
  if (document.querySelector('link[href="/v021b13.css"]')) return;
  const attach = () => {
    if (document.querySelector('link[href="/v021b13.css"]')) return;
    if (!document.querySelector('link[href="/v021b12.css"]')) {
      setTimeout(attach, 20);
      return;
    }
    const link = document.createElement('link');
    link.rel = 'stylesheet';
    link.href = '/v021b13.css';
    document.head.appendChild(link);
  };
  attach();
}

function syncV21Build13Version() {
  const version = document.querySelector('.version');
  if (version) version.textContent = CY_V21_BUILD13_VERSION;
}

function setupV21Build13ConnectionStatus() {
  const status = document.querySelector('#connectionStatus');
  if (!status || status.dataset.v21Build13Bound === '1') return;
  status.dataset.v21Build13Bound = '1';

  const sync = () => {
    const warning = status.classList.contains('warn') || status.classList.contains('error');
    if (warning) {
      if (status.classList.contains('hidden')) status.classList.remove('hidden');
      return;
    }
    if (status.textContent) status.textContent = '';
    if (!status.classList.contains('hidden')) status.classList.add('hidden');
  };

  sync();
  const observer = new MutationObserver(sync);
  observer.observe(status, { attributes: true, childList: true, characterData: true, subtree: true });
}

function setupV21Build13HeaderManagement() {
  const media = window.matchMedia(CY_V21_BUILD13_DESKTOP);
  const actions = document.querySelector('.topbar-actions');
  const settings = document.querySelector('#settingsButton');
  const dialog = document.querySelector('#settingsDialog');
  const title = dialog?.querySelector('.modal-header h2');
  if (!actions || !settings || !dialog || !title) return;

  let accountsButton = document.querySelector('#headerAccountManagerButton');
  if (!accountsButton) {
    accountsButton = document.createElement('button');
    accountsButton.id = 'headerAccountManagerButton';
    accountsButton.className = 'secondary compact v21-header-management-button';
    accountsButton.type = 'button';
    accountsButton.textContent = '帳戶管理';
    actions.insertBefore(accountsButton, settings);
  }

  let categoriesButton = document.querySelector('#headerCategoryManagerButton');
  if (!categoriesButton) {
    categoriesButton = document.createElement('button');
    categoriesButton.id = 'headerCategoryManagerButton';
    categoriesButton.className = 'secondary compact v21-header-management-button';
    categoriesButton.type = 'button';
    categoriesButton.textContent = '科目管理';
    actions.insertBefore(categoriesButton, settings);
  }

  if (accountsButton.dataset.v21Build13Bound !== '1') {
    accountsButton.dataset.v21Build13Bound = '1';
    accountsButton.addEventListener('click', () => openV21Build13Management('accounts', '帳戶管理'));
  }
  if (categoriesButton.dataset.v21Build13Bound !== '1') {
    categoriesButton.dataset.v21Build13Bound = '1';
    categoriesButton.addEventListener('click', () => openV21Build13Management('categories', '科目管理'));
  }

  if (settings.dataset.v21Build13Bound !== '1') {
    settings.dataset.v21Build13Bound = '1';
    settings.addEventListener('click', () => {
      setTimeout(() => {
        dialog.classList.remove('v21-management-mode');
        delete dialog.dataset.managementPane;
        title.textContent = '設定';
        const current = typeof state === 'object' ? String(state.activeSettingsTab || '') : '';
        if (current === 'accounts' || current === 'categories' || !current) {
          const preferred = ['quick', 'data', 'lock', 'backup', 'migration']
            .find(name => document.querySelector(`[data-settings-tab="${name}"]`));
          if (preferred && typeof setSettingsTab === 'function') setSettingsTab(preferred);
        }
      }, 0);
    });
  }

  const syncDesktop = () => {
    const enabled = media.matches;
    accountsButton.hidden = !enabled;
    categoriesButton.hidden = !enabled;
    if (!enabled) {
      dialog.classList.remove('v21-management-mode');
      delete dialog.dataset.managementPane;
      title.textContent = '設定';
    }
  };
  syncDesktop();
  if (typeof media.addEventListener === 'function' && !actions.dataset.v21Build13MediaBound) {
    actions.dataset.v21Build13MediaBound = '1';
    media.addEventListener('change', syncDesktop);
  }
}

function openV21Build13Management(tab, label) {
  if (!window.matchMedia(CY_V21_BUILD13_DESKTOP).matches) return;
  const dialog = document.querySelector('#settingsDialog');
  const title = dialog?.querySelector('.modal-header h2');
  if (!dialog || !title) return;

  if (!dialog.open) {
    if (typeof openSettings === 'function') openSettings();
    else dialog.showModal();
  } else if (typeof renderSettings === 'function') {
    renderSettings();
  }

  dialog.classList.add('v21-management-mode');
  dialog.dataset.managementPane = tab;
  title.textContent = label;
  if (typeof setSettingsTab === 'function') setSettingsTab(tab);
}

function setupV21Build13OpeningDialog() {
  const dialog = document.querySelector('#openingDialog');
  const monthInput = document.querySelector('#openingMonth');
  const ledgerMonth = document.querySelector('#monthFilter');
  const title = dialog?.querySelector('.modal-header h2');
  if (!dialog || !monthInput || !ledgerMonth || !title) return;

  const sync = () => {
    if (!window.matchMedia(CY_V21_BUILD13_DESKTOP).matches) return;
    const month = /^\d{4}-\d{2}$/.test(ledgerMonth.value || '') ? ledgerMonth.value : monthInput.value;
    if (/^\d{4}-\d{2}$/.test(month || '')) {
      if (monthInput.value !== month) {
        monthInput.value = month;
        monthInput.dispatchEvent(new Event('change', { bubbles: true }));
      }
      title.textContent = `${month.replace('-', '/')}期初餘額`;
    }
  };

  sync();
  const observer = new MutationObserver(() => {
    if (dialog.open) sync();
  });
  observer.observe(dialog, { attributes: true, attributeFilter: ['open'] });
  document.querySelector('#ledgerOpeningBalanceButton')?.addEventListener('click', () => setTimeout(sync, 0));
}

function syncV21Build13CrudCopy() {
  const openingSave = document.querySelector('#saveOpeningButton');
  const editSave = document.querySelector('#editSaveButton');
  if (openingSave) openingSave.textContent = '儲存';
  if (editSave) editSave.textContent = '儲存';
}

/* ---- baseline section ---- */
const CY_V21_BUILD14_VERSION = 'V0.21.0 Build 14';
const CY_V21_BUILD14_DESKTOP = '(min-width: 1024px)';
const CY_V21_BUILD14_MONTHS = ['一月', '二月', '三月', '四月', '五月', '六月', '七月', '八月', '九月', '十月', '十一月', '十二月'];
let cyV21Build14InlineEdit = null;

if (!window.__CYACC_BASELINE_BUNDLE__) ensureV21Build14Stylesheet();
syncV21Build14Version();

const runV21Build14 = () => {
  syncV21Build14Version();
  setupV21Build14Managers();
  setupV21Build14InlineEditing();
  setupV21Build14AccountLimit();
  setupV21Build14MonthPickers();
  syncV21Build14LedgerMonthTrigger();
};

if (document.readyState === 'complete') setTimeout(runV21Build14, 0);
else window.addEventListener('load', () => setTimeout(runV21Build14, 0), { once: true });

function ensureV21Build14Stylesheet() {
  if (document.querySelector('link[href="/v021b14.css"]')) return;
  const link = document.createElement('link');
  link.rel = 'stylesheet';
  link.href = '/v021b14.css';
  document.head.appendChild(link);
}

function syncV21Build14Version() {
  const version = document.querySelector('.version');
  if (version) version.textContent = CY_V21_BUILD14_VERSION;
}

function setupV21Build14Managers() {
  if (typeof window.renderAccountManager === 'function') window.renderAccountManager = renderV21Build14AccountManager;
  if (typeof window.renderCategoryManager === 'function') window.renderCategoryManager = renderV21Build14CategoryManager;
  renderV21Build14AccountManager();
  renderV21Build14CategoryManager();
}

function renderV21Build14AccountManager() {
  const host = document.querySelector('#accountRows');
  if (!host || typeof state !== 'object') return;
  const accounts = Array.isArray(state.accounts) ? state.accounts : [];
  if (!accounts.length) {
    host.innerHTML = '<div class="empty">尚無帳戶。</div>';
    return;
  }

  host.innerHTML = accounts.map((account, index) => {
    const id = Number(account.id);
    const isDefault = Number(account.is_default) === 1;
    return `<div class="manager-row v21-account-manager-row" data-v21-account-row="${id}">
      <div class="manager-row-main v21-manager-name-cell">
        ${isDefault
          ? '<button type="button" class="v21-default-chip active" disabled title="預設帳戶" aria-label="預設帳戶">★ 預設</button>'
          : `<button type="button" class="v21-default-chip" data-account-default="${id}" title="設為預設帳戶" aria-label="設為預設帳戶">☆ 預設</button>`}
        <strong class="v21-editable-name" data-v21-account-name="${id}">${v21Build14Escape(account.name)}</strong>
        <button type="button" class="mini-button v21-edit-name-button" data-account-rename="${id}" title="編輯帳戶名稱" aria-label="編輯帳戶名稱">✎</button>
      </div>
      <div class="manager-row-actions">
        ${v21Build14OrderButton('account', id, 'up', index === 0)}
        ${v21Build14OrderButton('account', id, 'down', index === accounts.length - 1)}
        <button type="button" class="mini-button danger" data-account-delete="${id}">刪除</button>
      </div>
    </div>`;
  }).join('');
}

function renderV21Build14CategoryManager() {
  const host = document.querySelector('#categoryManager');
  if (!host || typeof state !== 'object') return;
  document.querySelectorAll('[data-settings-kind]').forEach(button =>
    button.classList.toggle('active', button.dataset.settingsKind === state.settingsKind)
  );

  const groups = (state.groups || []).filter(group => group.kind === state.settingsKind);
  if (!groups.length) {
    host.innerHTML = '<div class="empty">目前沒有大分類。</div>';
    return;
  }

  host.innerHTML = `<div class="v21-category-manager-list">${groups.map((group, groupIndex) => {
    const categories = (state.categories || []).filter(category => Number(category.group_id) === Number(group.id));
    const otherGroups = groups.filter(item => Number(item.id) !== Number(group.id));
    const groupId = Number(group.id);
    const items = categories.map((category, index) => {
      const id = Number(category.id);
      const favorite = Number(category.is_favorite) === 1;
      return `<div class="category-item v21-category-manager-row" data-v21-category-row="${id}">
        <button type="button" class="v21-favorite-chip${favorite ? ' active' : ''}" data-category-favorite="${id}" title="${favorite ? '取消常用科目' : '設為常用科目'}" aria-label="${favorite ? '取消常用科目' : '設為常用科目'}">${favorite ? '★' : '☆'}</button>
        <span class="v21-editable-name" data-v21-category-name="${id}">${v21Build14Escape(category.name)}</span>
        <button type="button" class="mini-button v21-edit-name-button" data-category-rename="${id}" title="編輯科目名稱" aria-label="編輯科目名稱">✎</button>
        ${v21Build14OrderButton('category', id, 'up', index === 0)}
        ${v21Build14OrderButton('category', id, 'down', index === categories.length - 1)}
        ${otherGroups.length ? `<button type="button" class="mini-button" data-v12-category-transfer="${id}">移動</button>` : ''}
        <button type="button" class="mini-button danger" data-category-delete="${id}">刪除</button>
      </div>`;
    }).join('');

    return `<section class="category-group v21-category-group" data-group-id="${groupId}">
      <div class="category-group-head v21-category-group-head">
        <strong class="category-group-title v21-editable-name" data-v21-group-name="${groupId}">${v21Build14Escape(group.name)}</strong>
        <button type="button" class="mini-button v21-edit-name-button" data-group-rename="${groupId}" title="編輯大分類名稱" aria-label="編輯大分類名稱">✎</button>
        <span class="v21-manager-action-spacer" aria-hidden="true"></span>
        ${v21Build14OrderButton('group', groupId, 'up', groupIndex === 0)}
        ${v21Build14OrderButton('group', groupId, 'down', groupIndex === groups.length - 1)}
        <button type="button" class="mini-button danger" data-group-delete="${groupId}">刪除</button>
      </div>
      <div class="category-items v21-category-items">
        ${items || '<div class="v21-category-empty">此分類尚無科目。</div>'}
        <div class="category-add v21-category-add-row">
          <input type="text" maxlength="60" placeholder="新增科目" data-new-category-group="${groupId}">
          <button type="button" class="mini-button" data-category-add="${groupId}">新增</button>
        </div>
      </div>
    </section>`;
  }).join('')}</div>`;
}

function v21Build14OrderButton(type, id, direction, disabled) {
  const attr = type === 'account' ? 'data-v11-move-account' : type === 'group' ? 'data-v11-move-group' : 'data-v11-move-category';
  const title = direction === 'up' ? '往上移' : '往下移';
  return `<button type="button" class="mini-button order-button" ${attr}="${id}" data-direction="${direction}" title="${title}" aria-label="${title}"${disabled ? ' disabled' : ''}>${direction === 'up' ? '↑' : '↓'}</button>`;
}

function setupV21Build14InlineEditing() {
  const accountHost = document.querySelector('#accountRows');
  const categoryHost = document.querySelector('#categoryManager');
  if (accountHost && accountHost.dataset.v21Build14EditBound !== '1') {
    accountHost.dataset.v21Build14EditBound = '1';
    accountHost.addEventListener('click', handleV21Build14RenameClick, true);
  }
  if (categoryHost && categoryHost.dataset.v21Build14EditBound !== '1') {
    categoryHost.dataset.v21Build14EditBound = '1';
    categoryHost.addEventListener('click', handleV21Build14RenameClick, true);
  }
  if (document.documentElement.dataset.v21Build14EditOutsideBound !== '1') {
    document.documentElement.dataset.v21Build14EditOutsideBound = '1';
    document.addEventListener('pointerdown', event => {
      if (!cyV21Build14InlineEdit) return;
      if (cyV21Build14InlineEdit.container?.contains(event.target)) return;
      cancelV21Build14InlineEdit();
    });
  }
}

function handleV21Build14RenameClick(event) {
  const save = event.target.closest('[data-v21-inline-name-save]');
  if (save) {
    event.preventDefault();
    event.stopImmediatePropagation();
    saveV21Build14InlineEdit();
    return;
  }
  const cancel = event.target.closest('[data-v21-inline-name-cancel]');
  if (cancel) {
    event.preventDefault();
    event.stopImmediatePropagation();
    cancelV21Build14InlineEdit();
    return;
  }

  const button = event.target.closest('[data-account-rename], [data-category-rename], [data-group-rename]');
  if (!button) return;
  event.preventDefault();
  event.stopImmediatePropagation();

  if (button.dataset.accountRename) beginV21Build14InlineEdit('account', Number(button.dataset.accountRename), button);
  else if (button.dataset.categoryRename) beginV21Build14InlineEdit('category', Number(button.dataset.categoryRename), button);
  else if (button.dataset.groupRename) beginV21Build14InlineEdit('group', Number(button.dataset.groupRename), button);
}

function beginV21Build14InlineEdit(type, id, button) {
  if (!Number.isInteger(id) || id <= 0) return;
  if (cyV21Build14InlineEdit) cancelV21Build14InlineEdit();

  const selector = type === 'account' ? `[data-v21-account-name="${id}"]` : type === 'category' ? `[data-v21-category-name="${id}"]` : `[data-v21-group-name="${id}"]`;
  const label = document.querySelector(selector);
  if (!label) return;
  const name = String(label.textContent || '').trim();
  const input = document.createElement('input');
  input.type = 'text';
  input.className = 'v21-inline-name-input';
  input.value = name;
  input.maxLength = type === 'account' ? 8 : 60;
  input.setAttribute('aria-label', type === 'account' ? '帳戶名稱' : type === 'category' ? '科目名稱' : '大分類名稱');

  const container = label.parentElement;
  label.hidden = true;
  label.insertAdjacentElement('afterend', input);
  button.hidden = true;

  const save = document.createElement('button');
  save.type = 'button';
  save.className = 'mini-button v21-inline-edit-confirm';
  save.dataset.v21InlineNameSave = '1';
  save.title = '儲存';
  save.setAttribute('aria-label', '儲存');
  save.textContent = '✓';

  const cancel = document.createElement('button');
  cancel.type = 'button';
  cancel.className = 'mini-button v21-inline-edit-cancel';
  cancel.dataset.v21InlineNameCancel = '1';
  cancel.title = '取消';
  cancel.setAttribute('aria-label', '取消');
  cancel.textContent = '×';

  input.insertAdjacentElement('afterend', save);
  save.insertAdjacentElement('afterend', cancel);

  cyV21Build14InlineEdit = { type, id, name, label, button, input, save, cancel, container };
  input.addEventListener('keydown', event => {
    if (event.isComposing) return;
    if (event.key === 'Enter') {
      event.preventDefault();
      saveV21Build14InlineEdit();
    } else if (event.key === 'Escape') {
      event.preventDefault();
      cancelV21Build14InlineEdit();
    }
  });
  input.focus();
  input.select();
}

async function saveV21Build14InlineEdit() {
  const active = cyV21Build14InlineEdit;
  if (!active) return;
  const name = String(active.input.value || '').trim().replace(/\s+/g, ' ');
  if (!name) return setDialogMessage(document.querySelector('#settingsMessage'), '名稱不可空白。', true);
  if (active.type === 'account' && v21Build14CharCount(name) > 8) {
    active.input.focus();
    return setDialogMessage(document.querySelector('#settingsMessage'), '帳戶名稱最多 8 個字。', true);
  }
  if (name === active.name) {
    cancelV21Build14InlineEdit();
    return;
  }

  const path = active.type === 'account' ? `/api/accounts/${active.id}` : active.type === 'category' ? `/api/categories/${active.id}` : `/api/category-groups/${active.id}`;
  active.input.disabled = true;
  active.save.disabled = true;
  active.cancel.disabled = true;
  cyV21Build14InlineEdit = null;
  const ok = await mutateSettings(path, { method: 'PUT', headers: jsonHeaders(), body: JSON.stringify({ name }) }, '名稱已更新。');
  if (!ok && active.container?.isConnected) {
    cyV21Build14InlineEdit = active;
    active.input.disabled = false;
    active.save.disabled = false;
    active.cancel.disabled = false;
    active.input.focus();
  }
}

function cancelV21Build14InlineEdit() {
  const active = cyV21Build14InlineEdit;
  if (!active) return;
  active.input?.remove();
  active.save?.remove();
  active.cancel?.remove();
  if (active.label) active.label.hidden = false;
  if (active.button) active.button.hidden = false;
  cyV21Build14InlineEdit = null;
}

function setupV21Build14AccountLimit() {
  const input = document.querySelector('#newAccountName');
  const button = document.querySelector('#addAccountButton');
  if (!input || !button) return;
  input.maxLength = 8;
  input.placeholder = '新增帳戶名稱（最多8字）';

  const validate = event => {
    const name = String(input.value || '').trim().replace(/\s+/g, ' ');
    if (!name || v21Build14CharCount(name) <= 8) return;
    event?.preventDefault();
    event?.stopImmediatePropagation();
    setDialogMessage(document.querySelector('#settingsMessage'), '帳戶名稱最多 8 個字。', true);
    input.focus();
  };
  if (button.dataset.v21Build14LimitBound !== '1') {
    button.dataset.v21Build14LimitBound = '1';
    button.addEventListener('click', validate, true);
  }
  if (input.dataset.v21Build14LimitBound !== '1') {
    input.dataset.v21Build14LimitBound = '1';
    input.addEventListener('keydown', event => {
      if (event.key === 'Enter') validate(event);
    }, true);
  }
}

function setupV21Build14MonthPickers() {
  const media = window.matchMedia(CY_V21_BUILD14_DESKTOP);
  const scan = () => {
    syncV21Build14LedgerMonthTrigger();
    if (!media.matches) return;
    for (const input of document.querySelectorAll('input[type="month"]')) {
      if (input.id === 'monthFilter' || input.id === 'openingMonth') continue;
      ensureV21Build14MonthPickerForInput(input);
    }
  };
  scan();
  if (typeof media.addEventListener === 'function') media.addEventListener('change', scan);
  else media.addListener?.(scan);

  if (document.body.dataset.v21Build14MonthObserver !== '1') {
    document.body.dataset.v21Build14MonthObserver = '1';
    const observer = new MutationObserver(scan);
    observer.observe(document.body, { childList: true, subtree: true });
  }
}

function syncV21Build14LedgerMonthTrigger() {
  document.querySelector('#ledgerMonthPickerCustom .v21-month-picker-caret')?.remove();
}

function ensureV21Build14MonthPickerForInput(input) {
  if (!input || input.dataset.v21Build14MonthPicker === '1') return;
  const label = input.closest('label') || input.parentElement;
  if (!label) return;
  input.dataset.v21Build14MonthPicker = '1';
  input.classList.add('v21-native-month-source');

  const root = document.createElement('div');
  root.className = 'v21-month-picker-custom v21-month-picker-field';
  root.innerHTML = `
    <button type="button" class="v21-month-picker-trigger v21-month-picker-field-trigger" aria-haspopup="dialog" aria-expanded="false"><span data-v21-month-label>—</span></button>
    <div class="v21-month-picker-popover" role="dialog" aria-label="選擇月份" hidden>
      <div class="v21-month-picker-head">
        <button type="button" class="v21-month-picker-nav" data-picker-nav="-1" aria-label="上一組">‹</button>
        <button type="button" class="v21-month-picker-year" data-picker-year-head aria-label="切換年份選擇"></button>
        <button type="button" class="v21-month-picker-nav" data-picker-nav="1" aria-label="下一組">›</button>
      </div>
      <div class="v21-month-picker-grid" data-picker-grid></div>
    </div>`;
  input.insertAdjacentElement('afterend', root);

  const trigger = root.querySelector('.v21-month-picker-trigger');
  const popover = root.querySelector('.v21-month-picker-popover');
  const yearHead = root.querySelector('[data-picker-year-head]');
  const grid = root.querySelector('[data-picker-grid]');
  let view = 'months';
  let displayYear = v21Build14ReadMonth(input).year;
  let yearStart = displayYear - 5;

  const render = () => {
    const selected = v21Build14ReadMonth(input);
    if (view === 'months') {
      yearHead.textContent = String(displayYear);
      grid.className = 'v21-month-picker-grid month-view';
      grid.innerHTML = CY_V21_BUILD14_MONTHS.map((monthLabel, index) => {
        const month = index + 1;
        const active = selected.valid && selected.year === displayYear && selected.month === month;
        return `<button type="button" class="v21-month-choice${active ? ' active' : ''}" data-picker-month="${month}">${monthLabel}</button>`;
      }).join('');
    } else {
      yearHead.textContent = `${yearStart}–${yearStart + 11}`;
      grid.className = 'v21-month-picker-grid year-view';
      grid.innerHTML = Array.from({ length: 12 }, (_, index) => yearStart + index).map(year =>
        `<button type="button" class="v21-year-choice${selected.valid && selected.year === year ? ' active' : ''}" data-picker-year="${year}">${year}</button>`
      ).join('');
    }
  };

  const syncLabel = () => {
    const selected = v21Build14ReadMonth(input);
    const target = root.querySelector('[data-v21-month-label]');
    if (target) target.textContent = selected.valid ? `${selected.year}年${String(selected.month).padStart(2, '0')}月` : '選擇月份';
  };

  const close = focusTrigger => {
    popover.hidden = true;
    trigger.setAttribute('aria-expanded', 'false');
    if (focusTrigger) trigger.focus();
  };

  trigger.addEventListener('click', event => {
    event.stopPropagation();
    if (!window.matchMedia(CY_V21_BUILD14_DESKTOP).matches) return;
    if (!popover.hidden) return close(false);
    const selected = v21Build14ReadMonth(input);
    displayYear = selected.year;
    yearStart = displayYear - 5;
    view = 'months';
    render();
    popover.hidden = false;
    trigger.setAttribute('aria-expanded', 'true');
  });

  root.querySelectorAll('[data-picker-nav]').forEach(button => button.addEventListener('click', () => {
    const delta = Number(button.dataset.pickerNav) || 0;
    if (view === 'months') displayYear += delta;
    else yearStart += delta * 12;
    render();
  }));

  yearHead.addEventListener('click', () => {
    if (view === 'months') {
      view = 'years';
      yearStart = displayYear - 5;
    } else view = 'months';
    render();
  });

  grid.addEventListener('click', event => {
    const monthButton = event.target.closest('[data-picker-month]');
    if (monthButton) {
      const month = Number(monthButton.dataset.pickerMonth);
      input.value = `${displayYear}-${String(month).padStart(2, '0')}`;
      input.dispatchEvent(new Event('change', { bubbles: true }));
      syncLabel();
      close(true);
      return;
    }
    const yearButton = event.target.closest('[data-picker-year]');
    if (yearButton) {
      displayYear = Number(yearButton.dataset.pickerYear) || displayYear;
      view = 'months';
      render();
    }
  });

  input.addEventListener('change', syncLabel);
  document.addEventListener('pointerdown', event => {
    if (popover.hidden || root.contains(event.target)) return;
    close(false);
  });
  document.addEventListener('keydown', event => {
    if (event.key === 'Escape' && !popover.hidden) close(true);
  });
  syncLabel();
}

function v21Build14ReadMonth(input) {
  const match = /^(\d{4})-(\d{2})$/.exec(String(input?.value || ''));
  if (match) return { valid: true, year: Number(match[1]), month: Number(match[2]) };
  const now = new Date();
  return { valid: false, year: now.getFullYear(), month: now.getMonth() + 1 };
}

function v21Build14CharCount(value) {
  return Array.from(String(value || '')).length;
}

function v21Build14Escape(value) {
  return String(value ?? '')
    .replaceAll('&', '&amp;')
    .replaceAll('<', '&lt;')
    .replaceAll('>', '&gt;')
    .replaceAll('"', '&quot;')
    .replaceAll("'", '&#39;');
}

/* ---- baseline section ---- */
const CY_V21_BUILD15_VERSION = 'V0.21.0 Build 15';
const CY_V21_BUILD15_DESKTOP = '(min-width: 1024px)';
let cyV21Build15Drag = null;

if (!window.__CYACC_BASELINE_BUNDLE__) ensureV21Build15Stylesheet();
syncV21Build15Version();

const runV21Build15 = () => {
  syncV21Build15Version();
  setupV21Build15Managers();
  setupV21Build15KindSwitch();
  setupV21Build15DragAndDrop();
};

if (document.readyState === 'complete') setTimeout(runV21Build15, 0);
else window.addEventListener('load', () => setTimeout(runV21Build15, 0), { once: true });

function ensureV21Build15Stylesheet() {
  if (document.querySelector('link[href="/v021b15.css"]')) return;
  const link = document.createElement('link');
  link.rel = 'stylesheet';
  link.href = '/v021b15.css';
  document.head.appendChild(link);
}

function syncV21Build15Version() {
  const version = document.querySelector('.version');
  if (version) version.textContent = CY_V21_BUILD15_VERSION;
}

function setupV21Build15Managers() {
  if (typeof window.renderAccountManager === 'function') window.renderAccountManager = renderV21Build15AccountManager;
  if (typeof window.renderCategoryManager === 'function') window.renderCategoryManager = renderV21Build15CategoryManager;
  renderV21Build15AccountManager();
  renderV21Build15CategoryManager();
}

function renderV21Build15AccountManager() {
  const host = document.querySelector('#accountRows');
  if (!host || typeof state !== 'object') return;
  const accounts = Array.isArray(state.accounts) ? state.accounts : [];
  if (!accounts.length) {
    host.innerHTML = '<div class="empty">尚無帳戶。</div>';
    return;
  }

  host.innerHTML = accounts.map(account => {
    const id = Number(account.id);
    const isDefault = Number(account.is_default) === 1;
    return `<div class="manager-row v21-account-manager-row" data-v21-account-row="${id}">
      <button type="button" class="v21-drag-handle" draggable="true" data-v21-drag-account="${id}" title="拖曳調整帳戶順序" aria-label="拖曳調整帳戶順序">⠿</button>
      ${isDefault
        ? '<button type="button" class="v21-default-tag active" disabled aria-label="目前預設帳戶">預設</button>'
        : `<button type="button" class="v21-default-tag" data-account-default="${id}" title="設為預設帳戶">設為預設</button>`}
      <div class="v21-manager-name-cell">
        <strong class="v21-editable-name" data-v21-account-name="${id}">${v21Build15Escape(account.name)}</strong>
        <button type="button" class="mini-button v21-edit-name-button" data-account-rename="${id}" title="編輯帳戶名稱" aria-label="編輯帳戶名稱">✎</button>
      </div>
      <button type="button" class="mini-button danger v21-manager-delete" data-account-delete="${id}">刪除</button>
    </div>`;
  }).join('');
}

function renderV21Build15CategoryManager() {
  const host = document.querySelector('#categoryManager');
  const pane = document.querySelector('[data-settings-pane="categories"]');
  if (!host || !pane || typeof state !== 'object') return;

  const kind = state.settingsKind === 'income' ? 'income' : 'expense';
  pane.classList.toggle('v21-category-kind-income', kind === 'income');
  pane.classList.toggle('v21-category-kind-expense', kind === 'expense');
  document.querySelectorAll('[data-settings-kind]').forEach(button =>
    button.classList.toggle('active', button.dataset.settingsKind === kind)
  );

  const groups = (state.groups || []).filter(group => group.kind === kind);
  const toolbar = `<div class="v21-category-toolbar">
    <div class="entry-kind-switch v21-category-kind-switch" role="group" aria-label="收入或支出">
      <button type="button" class="kind-button${kind === 'income' ? ' active' : ''}" data-kind="income" data-v21-manager-kind="income">收入</button>
      <button type="button" class="kind-button${kind === 'expense' ? ' active' : ''}" data-kind="expense" data-v21-manager-kind="expense">支出</button>
    </div>
  </div>`;

  if (!groups.length) {
    host.innerHTML = `${toolbar}<div class="empty v21-category-empty-state">目前沒有大分類。</div>`;
    return;
  }

  host.innerHTML = `${toolbar}<div class="v21-category-manager-list">${groups.map(group => {
    const groupId = Number(group.id);
    const categories = (state.categories || []).filter(category => Number(category.group_id) === groupId);
    const items = categories.map(category => {
      const id = Number(category.id);
      const favorite = Number(category.is_favorite) === 1;
      return `<div class="category-item v21-category-manager-row" data-v21-category-row="${id}" data-v21-category-group="${groupId}">
        <button type="button" class="v21-drag-handle" draggable="true" data-v21-drag-category="${id}" title="拖曳調整科目順序或分類" aria-label="拖曳調整科目順序或分類">⠿</button>
        <button type="button" class="v21-favorite-chip${favorite ? ' active' : ''}" data-category-favorite="${id}" title="${favorite ? '取消常用科目' : '設為常用科目'}" aria-label="${favorite ? '取消常用科目' : '設為常用科目'}">${favorite ? '★' : '☆'}</button>
        <span class="v21-editable-name" data-v21-category-name="${id}">${v21Build15Escape(category.name)}</span>
        <button type="button" class="mini-button v21-edit-name-button" data-category-rename="${id}" title="編輯科目名稱" aria-label="編輯科目名稱">✎</button>
        <button type="button" class="mini-button danger v21-manager-delete" data-category-delete="${id}">刪除</button>
      </div>`;
    }).join('');

    return `<section class="category-group v21-category-group" data-group-id="${groupId}">
      <div class="category-group-head v21-category-group-head">
        <button type="button" class="v21-drag-handle" draggable="true" data-v21-drag-group="${groupId}" title="拖曳調整大分類順序" aria-label="拖曳調整大分類順序">⠿</button>
        <strong class="category-group-title v21-editable-name" data-v21-group-name="${groupId}">${v21Build15Escape(group.name)}</strong>
        <button type="button" class="mini-button v21-edit-name-button" data-group-rename="${groupId}" title="編輯大分類名稱" aria-label="編輯大分類名稱">✎</button>
        <button type="button" class="mini-button danger v21-manager-delete" data-group-delete="${groupId}">刪除</button>
      </div>
      <div class="category-items v21-category-items" data-v21-category-dropzone="${groupId}">
        ${items || '<div class="v21-category-empty">拖曳科目到此分類，或在下方新增。</div>'}
        <div class="category-add v21-category-add-row">
          <input type="text" maxlength="60" placeholder="新增科目" data-new-category-group="${groupId}">
          <button type="button" class="mini-button" data-category-add="${groupId}">新增</button>
        </div>
      </div>
    </section>`;
  }).join('')}</div>`;
}

function setupV21Build15KindSwitch() {
  const host = document.querySelector('#categoryManager');
  if (!host || host.dataset.v21Build15KindBound === '1') return;
  host.dataset.v21Build15KindBound = '1';
  host.addEventListener('click', event => {
    const button = event.target.closest('[data-v21-manager-kind]');
    if (!button || typeof state !== 'object') return;
    const kind = button.dataset.v21ManagerKind;
    if (!['income', 'expense'].includes(kind) || state.settingsKind === kind) return;
    state.settingsKind = kind;
    if (cyV21Build15Drag) finishV21Build15Drag();
    renderV21Build15CategoryManager();
  });
}

function setupV21Build15DragAndDrop() {
  const accountHost = document.querySelector('#accountRows');
  const categoryHost = document.querySelector('#categoryManager');
  if (accountHost && accountHost.dataset.v21Build15DragBound !== '1') {
    accountHost.dataset.v21Build15DragBound = '1';
    accountHost.addEventListener('dragstart', handleV21Build15DragStart);
    accountHost.addEventListener('dragover', handleV21Build15AccountDragOver);
    accountHost.addEventListener('drop', handleV21Build15AccountDrop);
    accountHost.addEventListener('dragend', finishV21Build15Drag);
  }
  if (categoryHost && categoryHost.dataset.v21Build15DragBound !== '1') {
    categoryHost.dataset.v21Build15DragBound = '1';
    categoryHost.addEventListener('dragstart', handleV21Build15DragStart);
    categoryHost.addEventListener('dragover', handleV21Build15CategoryDragOver);
    categoryHost.addEventListener('drop', handleV21Build15CategoryDrop);
    categoryHost.addEventListener('dragend', finishV21Build15Drag);
  }
}

function handleV21Build15DragStart(event) {
  if (!window.matchMedia(CY_V21_BUILD15_DESKTOP).matches) return;
  const handle = event.target.closest('[data-v21-drag-account], [data-v21-drag-group], [data-v21-drag-category]');
  if (!handle) return;
  if (typeof cyV21Build14InlineEdit !== 'undefined' && cyV21Build14InlineEdit) {
    event.preventDefault();
    return;
  }

  let type = '';
  let id = 0;
  if (handle.dataset.v21DragAccount) { type = 'account'; id = Number(handle.dataset.v21DragAccount); }
  else if (handle.dataset.v21DragGroup) { type = 'group'; id = Number(handle.dataset.v21DragGroup); }
  else if (handle.dataset.v21DragCategory) { type = 'category'; id = Number(handle.dataset.v21DragCategory); }
  if (!type || !Number.isInteger(id) || id <= 0) return;

  cyV21Build15Drag = { type, id };
  event.dataTransfer.effectAllowed = 'move';
  event.dataTransfer.setData('text/plain', `${type}:${id}`);
  const row = handle.closest('[data-v21-account-row], [data-group-id], [data-v21-category-row]');
  row?.classList.add('v21-is-dragging');
  document.body.classList.add('v21-manager-dragging');
}

function handleV21Build15AccountDragOver(event) {
  if (cyV21Build15Drag?.type !== 'account') return;
  event.preventDefault();
  event.dataTransfer.dropEffect = 'move';
  clearV21Build15DropMarkers();
  const row = event.target.closest('[data-v21-account-row]');
  if (!row) return;
  row.classList.add(v21Build15AfterMidpoint(event, row) ? 'v21-drop-after' : 'v21-drop-before');
}

async function handleV21Build15AccountDrop(event) {
  if (cyV21Build15Drag?.type !== 'account' || typeof state !== 'object') return;
  event.preventDefault();
  const sourceId = cyV21Build15Drag.id;
  const row = event.target.closest('[data-v21-account-row]');
  const targetId = Number(row?.dataset.v21AccountRow || 0);
  const after = row ? v21Build15AfterMidpoint(event, row) : true;
  const ids = (state.accounts || []).map(item => Number(item.id));
  const next = v21Build15MoveId(ids, sourceId, targetId, after);
  finishV21Build15Drag();
  if (!next || next.every((id, index) => id === ids[index])) return;
  await mutateSettings('/api/accounts/reorder', {
    method: 'PUT', headers: jsonHeaders(), body: JSON.stringify({ ids: next })
  }, '帳戶順序已更新。');
}

function handleV21Build15CategoryDragOver(event) {
  if (!cyV21Build15Drag || !['group', 'category'].includes(cyV21Build15Drag.type)) return;
  event.preventDefault();
  event.dataTransfer.dropEffect = 'move';
  clearV21Build15DropMarkers();

  if (cyV21Build15Drag.type === 'group') {
    const group = event.target.closest('[data-group-id]');
    if (!group) return;
    const head = group.querySelector('.v21-category-group-head') || group;
    group.classList.add(v21Build15AfterMidpoint(event, head) ? 'v21-drop-after' : 'v21-drop-before');
    return;
  }

  const categoryRow = event.target.closest('[data-v21-category-row]');
  if (categoryRow) {
    categoryRow.classList.add(v21Build15AfterMidpoint(event, categoryRow) ? 'v21-drop-after' : 'v21-drop-before');
    categoryRow.closest('[data-group-id]')?.classList.add('v21-drop-group');
    return;
  }
  event.target.closest('[data-group-id]')?.classList.add('v21-drop-group');
}

async function handleV21Build15CategoryDrop(event) {
  if (!cyV21Build15Drag || typeof state !== 'object') return;
  const drag = { ...cyV21Build15Drag };
  if (drag.type === 'group') {
    event.preventDefault();
    const group = event.target.closest('[data-group-id]');
    const targetId = Number(group?.dataset.groupId || 0);
    const head = group?.querySelector('.v21-category-group-head') || group;
    const after = group && head ? v21Build15AfterMidpoint(event, head) : true;
    const ids = (state.groups || []).filter(item => item.kind === state.settingsKind).map(item => Number(item.id));
    const next = v21Build15MoveId(ids, drag.id, targetId, after);
    finishV21Build15Drag();
    if (!next || next.every((id, index) => id === ids[index])) return;
    await mutateSettings('/api/category-groups/reorder', {
      method: 'PUT', headers: jsonHeaders(), body: JSON.stringify({ kind: state.settingsKind, ids: next })
    }, '大分類順序已更新。');
    return;
  }

  if (drag.type !== 'category') return;
  event.preventDefault();
  const targetGroup = event.target.closest('[data-group-id]');
  const targetGroupId = Number(targetGroup?.dataset.groupId || 0);
  if (!Number.isInteger(targetGroupId) || targetGroupId <= 0) {
    finishV21Build15Drag();
    return;
  }

  const targetRow = event.target.closest('[data-v21-category-row]');
  const targetId = Number(targetRow?.dataset.v21CategoryRow || 0);
  if (targetId === drag.id) {
    finishV21Build15Drag();
    return;
  }
  const after = targetRow ? v21Build15AfterMidpoint(event, targetRow) : true;
  const payload = v21Build15CategoryOrderPayload(drag.id, targetGroupId, targetId, after);
  finishV21Build15Drag();
  if (!payload) return;
  await mutateSettings('/api/categories/reorder', {
    method: 'PUT', headers: jsonHeaders(), body: JSON.stringify({ kind: state.settingsKind, groups: payload })
  }, '科目順序已更新。');
}

function v21Build15MoveId(ids, sourceId, targetId, after) {
  if (!ids.includes(sourceId)) return null;
  if (targetId === sourceId) return [...ids];
  const next = ids.filter(id => id !== sourceId);
  if (!targetId || !next.includes(targetId)) {
    next.push(sourceId);
    return next;
  }
  let index = next.indexOf(targetId);
  if (after) index += 1;
  next.splice(index, 0, sourceId);
  return next;
}

function v21Build15CategoryOrderPayload(sourceId, targetGroupId, targetId, after) {
  if (targetId === sourceId) return null;
  const groups = (state.groups || []).filter(group => group.kind === state.settingsKind);
  if (!groups.some(group => Number(group.id) === targetGroupId)) return null;
  const payload = groups.map(group => ({
    groupId: Number(group.id),
    categoryIds: (state.categories || [])
      .filter(category => category.kind === state.settingsKind && Number(category.group_id) === Number(group.id))
      .map(category => Number(category.id))
  }));
  if (!payload.some(group => group.categoryIds.includes(sourceId))) return null;
  payload.forEach(group => { group.categoryIds = group.categoryIds.filter(id => id !== sourceId); });
  const target = payload.find(group => group.groupId === targetGroupId);
  let index = target.categoryIds.length;
  if (targetId && target.categoryIds.includes(targetId)) {
    index = target.categoryIds.indexOf(targetId) + (after ? 1 : 0);
  }
  target.categoryIds.splice(index, 0, sourceId);
  return payload;
}

function v21Build15AfterMidpoint(event, element) {
  const rect = element.getBoundingClientRect();
  return event.clientY > rect.top + rect.height / 2;
}

function clearV21Build15DropMarkers() {
  document.querySelectorAll('.v21-drop-before, .v21-drop-after, .v21-drop-group').forEach(element =>
    element.classList.remove('v21-drop-before', 'v21-drop-after', 'v21-drop-group')
  );
}

function finishV21Build15Drag() {
  document.querySelectorAll('.v21-is-dragging').forEach(element => element.classList.remove('v21-is-dragging'));
  clearV21Build15DropMarkers();
  document.body.classList.remove('v21-manager-dragging');
  cyV21Build15Drag = null;
}

function v21Build15Escape(value) {
  return String(value ?? '')
    .replaceAll('&', '&amp;')
    .replaceAll('<', '&lt;')
    .replaceAll('>', '&gt;')
    .replaceAll('"', '&quot;')
    .replaceAll("'", '&#039;');
}

/* ---- baseline section ---- */
const CY_V21_BUILD16_VERSION = 'V0.21.0 Build 16';
const CY_V21_BUILD16_DESKTOP = '(min-width: 1024px)';
let cyV21Build16Saving = false;

if (!window.__CYACC_BASELINE_BUNDLE__) ensureV21Build16Stylesheet();
syncV21Build16Version();

const runV21Build16 = () => {
  syncV21Build16Version();
  setupV21Build16CategoryManager();
  setupV21Build16OptimisticDrag();
};

if (document.readyState === 'complete') setTimeout(runV21Build16, 0);
else window.addEventListener('load', () => setTimeout(runV21Build16, 0), { once: true });

function ensureV21Build16Stylesheet() {
  if (document.querySelector('link[href="/v021b16.css"]')) return;
  const link = document.createElement('link');
  link.rel = 'stylesheet';
  link.href = '/v021b16.css';
  document.head.appendChild(link);
}

function syncV21Build16Version() {
  const version = document.querySelector('.version');
  if (version) version.textContent = CY_V21_BUILD16_VERSION;
}

function setupV21Build16CategoryManager() {
  if (typeof window.renderCategoryManager === 'function') window.renderCategoryManager = renderV21Build16CategoryManager;
  const host = document.querySelector('#categoryManager');
  if (!host) return;

  if (host.dataset.v21Build16KindBound !== '1') {
    host.dataset.v21Build16KindBound = '1';
    host.addEventListener('click', event => {
      const button = event.target.closest('[data-v21-manager-kind]');
      if (!button || typeof state !== 'object') return;
      event.preventDefault();
      event.stopImmediatePropagation();
      const kind = button.dataset.v21ManagerKind;
      if (!['income', 'expense'].includes(kind) || kind === state.settingsKind) return;
      state.settingsKind = kind;
      renderV21Build16CategoryManager();
    }, true);
  }

  renderV21Build16CategoryManager();
}

function renderV21Build16CategoryManager() {
  const host = document.querySelector('#categoryManager');
  const pane = document.querySelector('[data-settings-pane="categories"]');
  if (!host || !pane || typeof state !== 'object') return;

  const kind = state.settingsKind === 'income' ? 'income' : 'expense';
  pane.classList.toggle('v21-category-kind-income', kind === 'income');
  pane.classList.toggle('v21-category-kind-expense', kind === 'expense');

  const toolbar = `<div class="v21-category-toolbar">
    <div class="entry-kind-switch v21-category-kind-switch" role="group" aria-label="收入或支出">
      <button type="button" class="kind-button${kind === 'income' ? ' active' : ''}" data-kind="income" data-v21-manager-kind="income">收入</button>
      <button type="button" class="kind-button${kind === 'expense' ? ' active' : ''}" data-kind="expense" data-v21-manager-kind="expense">支出</button>
    </div>
  </div>`;

  const groups = (state.groups || []).filter(group => group.kind === kind);
  if (!groups.length) {
    host.innerHTML = `${toolbar}<div class="empty v21-category-empty-state">目前沒有大分類。</div>`;
    return;
  }

  const list = groups.map(group => {
    const groupId = Number(group.id);
    const categories = (state.categories || []).filter(category =>
      category.kind === kind && Number(category.group_id) === groupId
    );
    const items = categories.map(category => {
      const id = Number(category.id);
      const favorite = Number(category.is_favorite) === 1;
      return `<div class="category-item v21-category-manager-row" data-v21-category-row="${id}" data-v21-category-group="${groupId}">
        <button type="button" class="v21-drag-handle" draggable="true" data-v21-drag-category="${id}" title="拖曳調整科目順序或分類" aria-label="拖曳調整科目順序或分類">⠿</button>
        <button type="button" class="v21-favorite-chip${favorite ? ' active' : ''}" data-category-favorite="${id}" title="${favorite ? '取消常用科目' : '設為常用科目'}" aria-label="${favorite ? '取消常用科目' : '設為常用科目'}">${favorite ? '★' : '☆'}</button>
        <span class="v21-editable-name" data-v21-category-name="${id}">${v21Build16Escape(category.name)}</span>
        <button type="button" class="mini-button v21-edit-name-button" data-category-rename="${id}" title="編輯科目名稱" aria-label="編輯科目名稱">✎</button>
        <button type="button" class="mini-button danger v21-manager-delete" data-category-delete="${id}">刪除</button>
      </div>`;
    }).join('');

    return `<section class="category-group v21-category-group" data-group-id="${groupId}">
      <div class="category-group-head v21-category-group-head">
        <button type="button" class="v21-drag-handle" draggable="true" data-v21-drag-group="${groupId}" title="拖曳調整大分類順序" aria-label="拖曳調整大分類順序">⠿</button>
        <strong class="category-group-title v21-editable-name" data-v21-group-name="${groupId}">${v21Build16Escape(group.name)}</strong>
        <button type="button" class="mini-button v21-edit-name-button" data-group-rename="${groupId}" title="編輯大分類名稱" aria-label="編輯大分類名稱">✎</button>
        <button type="button" class="mini-button danger v21-manager-delete" data-group-delete="${groupId}">刪除</button>
      </div>
      <div class="category-items v21-category-items" data-v21-category-dropzone="${groupId}">
        ${items || '<div class="v21-category-empty">拖曳科目到此分類，或在下方新增。</div>'}
        <div class="category-add v21-category-add-row">
          <input type="text" maxlength="60" placeholder="新增科目" data-new-category-group="${groupId}">
          <button type="button" class="mini-button" data-category-add="${groupId}">新增</button>
        </div>
      </div>
    </section>`;
  }).join('');

  host.innerHTML = `${toolbar}<div class="v21-category-manager-list">${list}</div>`;
}

function setupV21Build16OptimisticDrag() {
  const accountHost = document.querySelector('#accountRows');
  const categoryHost = document.querySelector('#categoryManager');
  if (accountHost && accountHost.dataset.v21Build16DropBound !== '1') {
    accountHost.dataset.v21Build16DropBound = '1';
    accountHost.addEventListener('drop', handleV21Build16AccountDrop, true);
  }
  if (categoryHost && categoryHost.dataset.v21Build16DropBound !== '1') {
    categoryHost.dataset.v21Build16DropBound = '1';
    categoryHost.addEventListener('drop', handleV21Build16CategoryDrop, true);
  }
}

async function handleV21Build16AccountDrop(event) {
  if (!window.matchMedia(CY_V21_BUILD16_DESKTOP).matches || cyV21Build16Saving) return;
  if (typeof cyV21Build15Drag === 'undefined' || cyV21Build15Drag?.type !== 'account' || typeof state !== 'object') return;

  event.preventDefault();
  event.stopImmediatePropagation();
  const sourceId = Number(cyV21Build15Drag.id);
  const row = event.target.closest('[data-v21-account-row]');
  const targetId = Number(row?.dataset.v21AccountRow || 0);
  const after = row ? v21Build16AfterMidpoint(event, row) : true;
  const previous = [...(state.accounts || [])];
  const ids = previous.map(item => Number(item.id));
  const nextIds = v21Build16MoveId(ids, sourceId, targetId, after);
  finishV21Build15Drag?.();
  if (!nextIds || nextIds.every((id, index) => id === ids[index])) return;

  state.accounts = v21Build16OrderObjects(previous, nextIds);
  renderV21Build15AccountManager?.();
  const selectedAccount = document.querySelector('#accountName')?.value || '';
  if (typeof renderAccounts === 'function') renderAccounts(selectedAccount);
  if (typeof syncV21Build8AccountChoices === 'function') syncV21Build8AccountChoices();

  await persistV21Build16Optimistic(
    '/api/accounts/reorder',
    { ids: nextIds },
    '帳戶順序已更新。',
    () => {
      state.accounts = previous;
      renderV21Build15AccountManager?.();
      if (typeof renderAccounts === 'function') renderAccounts(selectedAccount);
      if (typeof syncV21Build8AccountChoices === 'function') syncV21Build8AccountChoices();
    }
  );
}

async function handleV21Build16CategoryDrop(event) {
  if (!window.matchMedia(CY_V21_BUILD16_DESKTOP).matches || cyV21Build16Saving) return;
  if (typeof cyV21Build15Drag === 'undefined' || !cyV21Build15Drag || typeof state !== 'object') return;
  if (!['group', 'category'].includes(cyV21Build15Drag.type)) return;

  event.preventDefault();
  event.stopImmediatePropagation();
  const drag = { type: cyV21Build15Drag.type, id: Number(cyV21Build15Drag.id) };
  const kind = state.settingsKind === 'income' ? 'income' : 'expense';

  if (drag.type === 'group') {
    const group = event.target.closest('[data-group-id]');
    const targetId = Number(group?.dataset.groupId || 0);
    const head = group?.querySelector('.v21-category-group-head') || group;
    const after = group && head ? v21Build16AfterMidpoint(event, head) : true;
    const previous = [...(state.groups || [])];
    const ids = previous.filter(item => item.kind === kind).map(item => Number(item.id));
    const nextIds = v21Build16MoveId(ids, drag.id, targetId, after);
    finishV21Build15Drag?.();
    if (!nextIds || nextIds.every((id, index) => id === ids[index])) return;

    state.groups = v21Build16ReplaceKindOrder(previous, kind, nextIds);
    renderV21Build16CategoryManager();
    await persistV21Build16Optimistic(
      '/api/category-groups/reorder',
      { kind, ids: nextIds },
      '大分類順序已更新。',
      () => {
        state.groups = previous;
        renderV21Build16CategoryManager();
      }
    );
    return;
  }

  const targetGroup = event.target.closest('[data-group-id]');
  const targetGroupId = Number(targetGroup?.dataset.groupId || 0);
  if (!Number.isInteger(targetGroupId) || targetGroupId <= 0) {
    finishV21Build15Drag?.();
    return;
  }
  const targetRow = event.target.closest('[data-v21-category-row]');
  const targetId = Number(targetRow?.dataset.v21CategoryRow || 0);
  const after = targetRow ? v21Build16AfterMidpoint(event, targetRow) : true;
  const previous = [...(state.categories || [])];
  const payload = v21Build16CategoryPayload(previous, kind, drag.id, targetGroupId, targetId, after);
  finishV21Build15Drag?.();
  if (!payload) return;

  state.categories = v21Build16ApplyCategoryPayload(previous, kind, payload);
  renderV21Build16CategoryManager();
  await persistV21Build16Optimistic(
    '/api/categories/reorder',
    { kind, groups: payload },
    '科目順序已更新。',
    () => {
      state.categories = previous;
      renderV21Build16CategoryManager();
    }
  );
}

async function persistV21Build16Optimistic(path, body, successMessage, rollback) {
  const message = document.querySelector('#settingsMessage');
  cyV21Build16Saving = true;
  document.body.classList.add('v21-reorder-saving');
  if (message) setDialogMessage(message, '');
  try {
    await api(path, { method: 'PUT', headers: jsonHeaders(), body: JSON.stringify(body) });
    if (message) setDialogMessage(message, successMessage);
    return true;
  } catch (error) {
    rollback?.();
    if (message) setDialogMessage(message, error.message || '排序儲存失敗。', true);
    return false;
  } finally {
    cyV21Build16Saving = false;
    document.body.classList.remove('v21-reorder-saving');
  }
}

function v21Build16MoveId(ids, sourceId, targetId, after) {
  if (!ids.includes(sourceId)) return null;
  if (targetId === sourceId) return [...ids];
  const next = ids.filter(id => id !== sourceId);
  if (!targetId || !next.includes(targetId)) {
    next.push(sourceId);
    return next;
  }
  let index = next.indexOf(targetId);
  if (after) index += 1;
  next.splice(index, 0, sourceId);
  return next;
}

function v21Build16OrderObjects(items, ids) {
  const byId = new Map(items.map(item => [Number(item.id), item]));
  return ids.map(id => byId.get(id)).filter(Boolean);
}

function v21Build16ReplaceKindOrder(items, kind, ids) {
  const byId = new Map(items.filter(item => item.kind === kind).map(item => [Number(item.id), item]));
  const ordered = ids.map((id, index) => ({ ...byId.get(id), sort_order: index + 1 })).filter(Boolean);
  let cursor = 0;
  return items.map(item => item.kind === kind ? ordered[cursor++] : item);
}

function v21Build16CategoryPayload(items, kind, sourceId, targetGroupId, targetId, after) {
  const groups = (state.groups || []).filter(group => group.kind === kind);
  if (!groups.some(group => Number(group.id) === targetGroupId)) return null;
  const payload = groups.map(group => ({
    groupId: Number(group.id),
    categoryIds: items
      .filter(category => category.kind === kind && Number(category.group_id) === Number(group.id))
      .map(category => Number(category.id))
  }));
  if (!payload.some(group => group.categoryIds.includes(sourceId))) return null;
  if (targetId === sourceId) return null;

  payload.forEach(group => { group.categoryIds = group.categoryIds.filter(id => id !== sourceId); });
  const target = payload.find(group => group.groupId === targetGroupId);
  let index = target.categoryIds.length;
  if (targetId && target.categoryIds.includes(targetId)) {
    index = target.categoryIds.indexOf(targetId) + (after ? 1 : 0);
  }
  target.categoryIds.splice(index, 0, sourceId);
  return payload;
}

function v21Build16ApplyCategoryPayload(items, kind, payload) {
  const byId = new Map(items.filter(item => item.kind === kind).map(item => [Number(item.id), item]));
  const ordered = [];
  for (const group of payload) {
    group.categoryIds.forEach((id, index) => {
      const item = byId.get(Number(id));
      if (item) ordered.push({ ...item, group_id: Number(group.groupId), sort_order: index + 1 });
    });
  }
  let cursor = 0;
  return items.map(item => item.kind === kind ? ordered[cursor++] : item);
}

function v21Build16AfterMidpoint(event, element) {
  const rect = element.getBoundingClientRect();
  return event.clientY > rect.top + rect.height / 2;
}

function v21Build16Escape(value) {
  return String(value ?? '')
    .replaceAll('&', '&amp;')
    .replaceAll('<', '&lt;')
    .replaceAll('>', '&gt;')
    .replaceAll('"', '&quot;')
    .replaceAll("'", '&#039;');
}

/* ---- baseline section ---- */
const CY_V0211_VERSION = 'V0.21.1';
const CY_V0211_DESKTOP = '(min-width: 1024px)';
const CY_V0211_MONTHS = ['一月', '二月', '三月', '四月', '五月', '六月', '七月', '八月', '九月', '十月', '十一月', '十二月'];
const CY_V0211_WEEKDAYS = ['日', '一', '二', '三', '四', '五', '六'];
let cyV0211RenderingManagers = false;
const cyV0211ConfirmBypass = new WeakSet();

if (!window.__CYACC_BASELINE_BUNDLE__) ensureV0211Stylesheet();
installV0211ConfirmDialog();
installV0211ConfirmInterceptors();
enforceV0211Version();

const runV0211Patch = () => {
  enforceV0211Version();
  installV0211ManagerOverrides();
  setupV0211ManagerGuards();
  renderV0211ManagersIfVisible();
  setupV0211DatePickers();
  auditV0211MonthPickers();
  refineV0211HeaderIdentity();
};

if (document.readyState === 'loading') {
  document.addEventListener('DOMContentLoaded', () => setTimeout(runV0211Patch, 0), { once: true });
} else {
  setTimeout(runV0211Patch, 0);
}
window.addEventListener('load', () => {
  runV0211Patch();
  setTimeout(runV0211Patch, 80);
  setTimeout(runV0211Patch, 300);
}, { once: true });

function ensureV0211Stylesheet() {
  if (document.querySelector('link[href="/v0211.css"]')) return;
  const link = document.createElement('link');
  link.rel = 'stylesheet';
  link.href = '/v0211.css';
  document.head.appendChild(link);
}

function enforceV0211Version() {
  const version = document.querySelector('.version');
  if (version && version.textContent !== CY_V0211_VERSION) version.textContent = CY_V0211_VERSION;
  if (version && version.dataset.v0211VersionGuard !== '1') {
    version.dataset.v0211VersionGuard = '1';
    const observer = new MutationObserver(() => {
      if (version.textContent !== CY_V0211_VERSION) version.textContent = CY_V0211_VERSION;
    });
    observer.observe(version, { childList: true, characterData: true, subtree: true });
  }
}

/* -------------------------------------------------------------------------- */
/* Managed confirmation dialog                                                */
/* -------------------------------------------------------------------------- */

function installV0211ConfirmDialog() {
  if (window.cyConfirm && document.querySelector('#cyConfirmDialog')) return;
  let dialog = document.querySelector('#cyConfirmDialog');
  if (!dialog) {
    dialog = document.createElement('dialog');
    dialog.id = 'cyConfirmDialog';
    dialog.className = 'cy-confirm-dialog';
    dialog.innerHTML = `
      <div class="cy-confirm-shell">
        <h2 class="cy-confirm-title">確認</h2>
        <p class="cy-confirm-message"></p>
        <p class="cy-confirm-detail" hidden></p>
        <div class="cy-confirm-actions">
          <button type="button" class="secondary cy-confirm-cancel">取消</button>
          <button type="button" class="primary cy-confirm-ok">確認</button>
        </div>
      </div>`;
    document.body.appendChild(dialog);
  }

  let pendingResolve = null;
  const finish = value => {
    const resolve = pendingResolve;
    pendingResolve = null;
    if (dialog.open) dialog.close();
    resolve?.(Boolean(value));
  };

  dialog.querySelector('.cy-confirm-cancel')?.addEventListener('click', () => finish(false));
  dialog.querySelector('.cy-confirm-ok')?.addEventListener('click', () => finish(true));
  dialog.addEventListener('cancel', event => {
    event.preventDefault();
    finish(false);
  });
  dialog.addEventListener('close', () => {
    if (pendingResolve) finish(false);
  });

  window.cyConfirm = options => new Promise(resolve => {
    if (pendingResolve) {
      const previous = pendingResolve;
      pendingResolve = null;
      previous(false);
    }
    const config = typeof options === 'string' ? { message: options } : (options || {});
    const title = dialog.querySelector('.cy-confirm-title');
    const message = dialog.querySelector('.cy-confirm-message');
    const detail = dialog.querySelector('.cy-confirm-detail');
    const ok = dialog.querySelector('.cy-confirm-ok');
    const cancel = dialog.querySelector('.cy-confirm-cancel');
    if (title) title.textContent = config.title || '確認';
    if (message) message.textContent = config.message || '';
    if (detail) {
      detail.textContent = config.detail || '';
      detail.hidden = !config.detail;
    }
    if (ok) {
      ok.textContent = config.confirmText || '確認';
      ok.classList.toggle('cy-confirm-danger', Boolean(config.danger));
    }
    if (cancel) cancel.textContent = config.cancelText || '取消';
    pendingResolve = resolve;
    if (!dialog.open) dialog.showModal();
    requestAnimationFrame(() => (config.danger ? cancel : ok)?.focus());
  });
}

function installV0211ConfirmInterceptors() {
  if (document.documentElement.dataset.v0211ConfirmBound === '1') return;
  document.documentElement.dataset.v0211ConfirmBound = '1';

  document.addEventListener('click', async event => {
    const target = event.target.closest([
      '[data-delete-id]',
      '[data-account-delete]',
      '[data-category-delete]',
      '[data-group-delete]',
      '#excelImportCommitButton',
      '#backupRunNow',
      '#desktopMigrationCommitV19'
    ].join(','));
    if (!target || cyV0211ConfirmBypass.has(target)) return;

    const spec = v0211ConfirmSpec(target);
    if (!spec) return;
    event.preventDefault();
    event.stopImmediatePropagation();

    const accepted = await window.cyConfirm(spec);
    if (!accepted || !target.isConnected) return;

    cyV0211ConfirmBypass.add(target);
    const nativeConfirm = window.confirm;
    window.confirm = () => true;
    try {
      target.click();
    } finally {
      window.confirm = nativeConfirm;
      queueMicrotask(() => cyV0211ConfirmBypass.delete(target));
    }
  }, true);

  /* Build 14 owns rename UX. Capture here so legacy prompt() paths can never win. */
  document.addEventListener('click', event => {
    const button = event.target.closest('[data-account-rename], [data-category-rename], [data-group-rename]');
    if (!button || typeof beginV21Build14InlineEdit !== 'function') return;
    let type = '';
    let id = 0;
    if (button.dataset.accountRename) { type = 'account'; id = Number(button.dataset.accountRename); }
    else if (button.dataset.categoryRename) { type = 'category'; id = Number(button.dataset.categoryRename); }
    else if (button.dataset.groupRename) { type = 'group'; id = Number(button.dataset.groupRename); }
    if (!type || !Number.isInteger(id) || id <= 0) return;
    event.preventDefault();
    event.stopImmediatePropagation();
    beginV21Build14InlineEdit(type, id, button);
  }, true);
}

function v0211ConfirmSpec(target) {
  if (target.matches('[data-delete-id]')) {
    return { title: '刪除記帳', message: '確定刪除這筆記帳？', confirmText: '刪除', danger: true };
  }
  if (target.matches('[data-account-delete]')) {
    const id = Number(target.dataset.accountDelete);
    const item = typeof state === 'object' ? (state.accounts || []).find(row => Number(row.id) === id) : null;
    return {
      title: '刪除帳戶',
      message: `確定刪除帳戶「${item?.name || ''}」？`,
      detail: '既有歷史記帳仍會保留原帳戶名稱。',
      confirmText: '刪除',
      danger: true
    };
  }
  if (target.matches('[data-category-delete]')) {
    const id = Number(target.dataset.categoryDelete);
    const item = typeof state === 'object' ? (state.categories || []).find(row => Number(row.id) === id) : null;
    return {
      title: '刪除科目',
      message: `確定刪除科目「${item?.name || ''}」？`,
      detail: '既有歷史記帳仍會保留原科目名稱。',
      confirmText: '刪除',
      danger: true
    };
  }
  if (target.matches('[data-group-delete]')) {
    const id = Number(target.dataset.groupDelete);
    const item = typeof state === 'object' ? (state.groups || []).find(row => Number(row.id) === id) : null;
    return { title: '刪除大分類', message: `確定刪除大分類「${item?.name || ''}」？`, confirmText: '刪除', danger: true };
  }
  if (target.id === 'excelImportCommitButton') {
    const ready = Number(typeof cyV15ImportState === 'object' ? cyV15ImportState.preview?.summary?.ready : 0) || 0;
    const duplicates = Number(typeof cyV15ImportState === 'object' ? cyV15ImportState.preview?.summary?.duplicates : 0) || 0;
    return {
      title: '匯入 Excel',
      message: `確定匯入 ${ready.toLocaleString()} 筆資料？`,
      detail: duplicates ? `另有 ${duplicates.toLocaleString()} 筆重複資料會自動略過。` : '',
      confirmText: '匯入'
    };
  }
  if (target.id === 'backupRunNow') {
    const tiered = typeof backupTopologyV18 !== 'undefined' && backupTopologyV18 === 'parallel_dual_provider';
    return {
      title: '立即執行備份',
      message: tiered ? '現在立即執行一次 R2 + GCS paired backup？' : '現在立即執行一次 Google Cloud Storage 測試備份？',
      detail: '正常每日備份仍會在排程時間自動執行。',
      confirmText: '開始備份'
    };
  }
  if (target.id === 'desktopMigrationCommitV19') {
    const preview = typeof migrationState !== 'undefined' ? migrationState.preview : null;
    const plan = preview?.plan || {};
    const tx = Number(plan.transactions?.insert || 0);
    const accounts = Number(plan.accounts?.insert || 0);
    const groups = Number(plan.groups?.insert || 0);
    const categories = Number(plan.categories?.insert || 0);
    const opening = Number(plan.openingBalances?.insert || 0);
    return {
      title: '確認資料移轉',
      message: `確定將預覽內容寫入 Web 帳本？`,
      detail: `新增：${accounts} 個帳戶、${groups} 個大分類、${categories} 個科目、${tx} 筆交易、${opening} 筆期初餘額。\n既有 Web 交易不會被刪除。`,
      confirmText: '執行移轉'
    };
  }
  return null;
}

/* -------------------------------------------------------------------------- */
/* Deterministic Desktop managers                                             */
/* -------------------------------------------------------------------------- */

function installV0211ManagerOverrides() {
  if (typeof window.renderAccountManager === 'function') window.renderAccountManager = renderV0211AccountManager;
  if (typeof window.renderV21Build15AccountManager === 'function') window.renderV21Build15AccountManager = renderV0211AccountManager;
  if (typeof window.renderCategoryManager === 'function') window.renderCategoryManager = renderV0211CategoryManager;
  if (typeof window.renderV21Build15CategoryManager === 'function') window.renderV21Build15CategoryManager = renderV0211CategoryManager;
  if (typeof window.renderV21Build16CategoryManager === 'function') window.renderV21Build16CategoryManager = renderV0211CategoryManager;
}

function renderV0211ManagersIfVisible() {
  if (!window.matchMedia(CY_V0211_DESKTOP).matches || typeof state !== 'object') return;
  if (document.querySelector('#accountRows')) renderV0211AccountManager();
  if (document.querySelector('#categoryManager')) renderV0211CategoryManager();
}

function renderV0211AccountManager() {
  if (!window.matchMedia(CY_V0211_DESKTOP).matches || typeof state !== 'object') return;
  const host = document.querySelector('#accountRows');
  if (!host) return;
  const accounts = Array.isArray(state.accounts) ? state.accounts : [];
  cyV0211RenderingManagers = true;
  try {
    if (!accounts.length) {
      host.innerHTML = '<div class="empty">尚無帳戶。</div>';
      return;
    }
    host.innerHTML = accounts.map(account => {
      const id = Number(account.id);
      const isDefault = Number(account.is_default) === 1;
      return `<div class="v0211-account-row" data-v21-account-row="${id}">
        <button type="button" class="v21-drag-handle" draggable="true" data-v21-drag-account="${id}" title="拖曳調整帳戶順序" aria-label="拖曳調整帳戶順序">⠿</button>
        ${isDefault
          ? '<button type="button" class="v0211-default-tag active" disabled aria-label="目前預設帳戶">預設</button>'
          : `<button type="button" class="v0211-default-tag" data-account-default="${id}" title="設為預設帳戶">設為預設</button>`}
        <div class="v0211-account-name-cell">
          <strong class="v21-editable-name" data-v21-account-name="${id}">${v0211Escape(account.name)}</strong>
          <button type="button" class="mini-button v21-edit-name-button" data-account-rename="${id}" title="編輯帳戶名稱" aria-label="編輯帳戶名稱">✎</button>
        </div>
        <button type="button" class="mini-button danger v21-manager-delete" data-account-delete="${id}">刪除</button>
      </div>`;
    }).join('');
  } finally {
    cyV0211RenderingManagers = false;
  }
}

function renderV0211CategoryManager() {
  if (!window.matchMedia(CY_V0211_DESKTOP).matches || typeof state !== 'object') return;
  const host = document.querySelector('#categoryManager');
  const pane = document.querySelector('[data-settings-pane="categories"]');
  if (!host || !pane) return;

  const kind = state.settingsKind === 'income' ? 'income' : 'expense';
  pane.classList.toggle('v0211-category-income', kind === 'income');
  pane.classList.toggle('v0211-category-expense', kind === 'expense');
  const addGroup = document.querySelector('#newGroupName')?.parentElement;
  addGroup?.classList.add('v0211-group-add');

  const groups = (state.groups || []).filter(group => group.kind === kind);
  const toolbar = `<div class="v0211-category-toolbar">
    <div class="entry-kind-switch" role="group" aria-label="收入或支出">
      <button type="button" class="kind-button${kind === 'income' ? ' active' : ''}" data-kind="income" data-v0211-manager-kind="income">收入</button>
      <button type="button" class="kind-button${kind === 'expense' ? ' active' : ''}" data-kind="expense" data-v0211-manager-kind="expense">支出</button>
    </div>
  </div>`;

  cyV0211RenderingManagers = true;
  try {
    const body = groups.length ? `<div class="v0211-category-list">${groups.map(group => {
      const groupId = Number(group.id);
      const categories = (state.categories || []).filter(category => category.kind === kind && Number(category.group_id) === groupId);
      const rows = categories.map(category => {
        const id = Number(category.id);
        const favorite = Number(category.is_favorite) === 1;
        return `<div class="v0211-category-row" data-v21-category-row="${id}" data-v21-category-group="${groupId}">
          <button type="button" class="v21-drag-handle" draggable="true" data-v21-drag-category="${id}" title="拖曳調整科目順序或分類" aria-label="拖曳調整科目順序或分類">⠿</button>
          <button type="button" class="v0211-category-favorite${favorite ? ' active' : ''}" data-category-favorite="${id}" title="${favorite ? '取消常用科目' : '設為常用科目'}" aria-label="${favorite ? '取消常用科目' : '設為常用科目'}">${favorite ? '★' : '☆'}</button>
          <span class="v21-editable-name" data-v21-category-name="${id}">${v0211Escape(category.name)}</span>
          <button type="button" class="mini-button v21-edit-name-button" data-category-rename="${id}" title="編輯科目名稱" aria-label="編輯科目名稱">✎</button>
          <button type="button" class="mini-button danger v21-manager-delete" data-category-delete="${id}">刪除</button>
        </div>`;
      }).join('');
      return `<section class="v0211-category-group" data-group-id="${groupId}">
        <div class="v0211-category-group-head">
          <button type="button" class="v21-drag-handle" draggable="true" data-v21-drag-group="${groupId}" title="拖曳調整大分類順序" aria-label="拖曳調整大分類順序">⠿</button>
          <strong class="v21-editable-name" data-v21-group-name="${groupId}">${v0211Escape(group.name)}</strong>
          <button type="button" class="mini-button v21-edit-name-button" data-group-rename="${groupId}" title="編輯大分類名稱" aria-label="編輯大分類名稱">✎</button>
          <button type="button" class="mini-button danger v21-manager-delete" data-group-delete="${groupId}">刪除</button>
        </div>
        <div class="v0211-category-items" data-v21-category-dropzone="${groupId}">
          ${rows || '<div class="v0211-category-empty">拖曳科目到此分類，或在下方新增。</div>'}
          <div class="v0211-category-add">
            <input type="text" maxlength="60" placeholder="新增科目" data-new-category-group="${groupId}">
            <button type="button" class="mini-button" data-category-add="${groupId}">新增</button>
          </div>
        </div>
      </section>`;
    }).join('')}</div>` : '<div class="empty v0211-category-empty">目前沒有大分類。</div>';
    host.innerHTML = `<div class="v0211-category-shell">${toolbar}${body}</div>`;
  } finally {
    cyV0211RenderingManagers = false;
  }
}

function setupV0211ManagerGuards() {
  const accountHost = document.querySelector('#accountRows');
  if (accountHost && accountHost.dataset.v0211Guard !== '1') {
    accountHost.dataset.v0211Guard = '1';
    const observer = new MutationObserver(() => {
      if (cyV0211RenderingManagers || !window.matchMedia(CY_V0211_DESKTOP).matches) return;
      const hasRows = accountHost.children.length > 0 && !accountHost.querySelector('.empty');
      if (hasRows && !accountHost.querySelector('.v0211-account-row')) queueMicrotask(renderV0211AccountManager);
    });
    observer.observe(accountHost, { childList: true, subtree: true });
  }

  const categoryHost = document.querySelector('#categoryManager');
  if (categoryHost && categoryHost.dataset.v0211Guard !== '1') {
    categoryHost.dataset.v0211Guard = '1';
    categoryHost.addEventListener('click', event => {
      const button = event.target.closest('[data-v0211-manager-kind]');
      if (!button || typeof state !== 'object') return;
      event.preventDefault();
      event.stopImmediatePropagation();
      const kind = button.dataset.v0211ManagerKind;
      if (!['income', 'expense'].includes(kind) || state.settingsKind === kind) return;
      state.settingsKind = kind;
      renderV0211CategoryManager();
    }, true);

    const observer = new MutationObserver(() => {
      if (cyV0211RenderingManagers || !window.matchMedia(CY_V0211_DESKTOP).matches) return;
      const polluted = categoryHost.querySelector('.category-item, .order-button, [data-v12-category-transfer], [data-v11-move-category], [data-v11-move-group]');
      const missingPatch = categoryHost.children.length > 0 && !categoryHost.querySelector('.v0211-category-shell');
      if (polluted || missingPatch) queueMicrotask(renderV0211CategoryManager);
    });
    observer.observe(categoryHost, { childList: true, subtree: true });
  }
}

/* -------------------------------------------------------------------------- */
/* Desktop date/month pickers                                                 */
/* -------------------------------------------------------------------------- */

function auditV0211MonthPickers() {
  if (!window.matchMedia(CY_V0211_DESKTOP).matches) return;
  if (typeof ensureV21Build14MonthPickerForInput === 'function') {
    document.querySelectorAll('input[type="month"]').forEach(input => ensureV21Build14MonthPickerForInput(input));
  }
  if (typeof syncV21Build14LedgerMonthTrigger === 'function') syncV21Build14LedgerMonthTrigger();
}

function setupV0211DatePickers() {
  const media = window.matchMedia(CY_V0211_DESKTOP);
  const scan = () => {
    if (!media.matches) return;
    document.querySelectorAll('input[type="date"]').forEach(ensureV0211DatePicker);
  };
  scan();
  if (document.body.dataset.v0211DateObserver !== '1') {
    document.body.dataset.v0211DateObserver = '1';
    const observer = new MutationObserver(scan);
    observer.observe(document.body, { childList: true, subtree: true });
  }
  if (typeof media.addEventListener === 'function' && document.body.dataset.v0211DateMedia !== '1') {
    document.body.dataset.v0211DateMedia = '1';
    media.addEventListener('change', scan);
  }
}

function ensureV0211DatePicker(input) {
  if (!input || input.dataset.v0211DatePicker === '1') return;
  input.dataset.v0211DatePicker = '1';
  input.classList.add('v0211-native-date-source');

  const root = document.createElement('div');
  root.className = 'v0211-date-picker';
  root.innerHTML = `
    <button type="button" class="v0211-date-trigger" aria-haspopup="dialog" aria-expanded="false">
      <span class="v0211-date-label">—</span><span class="v0211-date-calendar-icon" aria-hidden="true">▣</span>
    </button>
    <div class="v0211-date-popover" role="dialog" aria-label="選擇日期" hidden>
      <div class="v0211-date-head">
        <button type="button" data-v0211-date-nav="-1" aria-label="上一個">‹</button>
        <button type="button" class="v0211-date-title" aria-label="切換年月選擇"></button>
        <button type="button" data-v0211-date-nav="1" aria-label="下一個">›</button>
      </div>
      <div class="v0211-date-content"></div>
      <div class="v0211-date-footer"><button type="button" class="v0211-date-today-button">今天</button></div>
    </div>`;
  input.insertAdjacentElement('afterend', root);

  const trigger = root.querySelector('.v0211-date-trigger');
  const label = root.querySelector('.v0211-date-label');
  const popover = root.querySelector('.v0211-date-popover');
  const title = root.querySelector('.v0211-date-title');
  const content = root.querySelector('.v0211-date-content');
  let view = 'days';
  let selected = v0211ReadDate(input);
  let displayYear = selected.year;
  let displayMonth = selected.month;
  let yearStart = displayYear - 5;

  const syncLabel = () => {
    selected = v0211ReadDate(input);
    label.textContent = `${selected.year}/${String(selected.month).padStart(2, '0')}/${String(selected.day).padStart(2, '0')}`;
  };

  const render = () => {
    selected = v0211ReadDate(input);
    if (view === 'days') renderV0211Days();
    else if (view === 'months') renderV0211Months();
    else renderV0211Years();
  };

  const renderV0211Days = () => {
    title.textContent = `${displayYear}年${String(displayMonth).padStart(2, '0')}月`;
    const first = new Date(displayYear, displayMonth - 1, 1);
    const start = new Date(displayYear, displayMonth - 1, 1 - first.getDay());
    const today = v0211Today();
    const cells = Array.from({ length: 42 }, (_, index) => {
      const date = new Date(start.getFullYear(), start.getMonth(), start.getDate() + index);
      const y = date.getFullYear();
      const m = date.getMonth() + 1;
      const d = date.getDate();
      const value = `${y}-${String(m).padStart(2, '0')}-${String(d).padStart(2, '0')}`;
      const active = y === selected.year && m === selected.month && d === selected.day;
      const current = y === today.year && m === today.month && d === today.day;
      const other = m !== displayMonth;
      return `<button type="button" class="v0211-date-day${active ? ' active' : ''}${current ? ' today' : ''}${other ? ' other-month' : ''}" data-v0211-date-value="${value}">${d}</button>`;
    }).join('');
    content.innerHTML = `<div class="v0211-date-weekdays">${CY_V0211_WEEKDAYS.map(day => `<span>${day}</span>`).join('')}</div><div class="v0211-date-days">${cells}</div>`;
  };

  const renderV0211Months = () => {
    title.textContent = String(displayYear);
    content.innerHTML = `<div class="v0211-date-choice-grid">${CY_V0211_MONTHS.map((name, index) => {
      const month = index + 1;
      const active = selected.year === displayYear && selected.month === month;
      return `<button type="button" class="v0211-date-month-choice${active ? ' active' : ''}" data-v0211-date-month="${month}">${name}</button>`;
    }).join('')}</div>`;
  };

  const renderV0211Years = () => {
    title.textContent = `${yearStart}–${yearStart + 11}`;
    content.innerHTML = `<div class="v0211-date-choice-grid">${Array.from({ length: 12 }, (_, index) => yearStart + index).map(year => {
      const active = selected.year === year;
      return `<button type="button" class="v0211-date-year-choice${active ? ' active' : ''}" data-v0211-date-year="${year}">${year}</button>`;
    }).join('')}</div>`;
  };

  const open = () => {
    selected = v0211ReadDate(input);
    displayYear = selected.year;
    displayMonth = selected.month;
    yearStart = displayYear - 5;
    view = 'days';
    render();
    popover.hidden = false;
    trigger.setAttribute('aria-expanded', 'true');
  };

  const close = (focus = false) => {
    popover.hidden = true;
    trigger.setAttribute('aria-expanded', 'false');
    if (focus) trigger.focus();
  };

  trigger.addEventListener('click', event => {
    event.stopPropagation();
    if (popover.hidden) open(); else close(false);
  });

  root.querySelectorAll('[data-v0211-date-nav]').forEach(button => button.addEventListener('click', () => {
    const delta = Number(button.dataset.v0211DateNav) || 0;
    if (view === 'days') {
      const next = new Date(displayYear, displayMonth - 1 + delta, 1);
      displayYear = next.getFullYear();
      displayMonth = next.getMonth() + 1;
    } else if (view === 'months') {
      displayYear += delta;
    } else {
      yearStart += delta * 12;
    }
    render();
  }));

  title.addEventListener('click', () => {
    if (view === 'days') view = 'months';
    else if (view === 'months') {
      view = 'years';
      yearStart = displayYear - 5;
    } else view = 'months';
    render();
  });

  content.addEventListener('click', event => {
    const day = event.target.closest('[data-v0211-date-value]');
    if (day) {
      input.value = day.dataset.v0211DateValue;
      input.dispatchEvent(new Event('change', { bubbles: true }));
      syncLabel();
      close(true);
      return;
    }
    const month = event.target.closest('[data-v0211-date-month]');
    if (month) {
      displayMonth = Number(month.dataset.v0211DateMonth) || displayMonth;
      view = 'days';
      render();
      return;
    }
    const year = event.target.closest('[data-v0211-date-year]');
    if (year) {
      displayYear = Number(year.dataset.v0211DateYear) || displayYear;
      view = 'months';
      render();
    }
  });

  root.querySelector('.v0211-date-today-button')?.addEventListener('click', () => {
    const today = v0211Today();
    input.value = `${today.year}-${String(today.month).padStart(2, '0')}-${String(today.day).padStart(2, '0')}`;
    input.dispatchEvent(new Event('change', { bubbles: true }));
    syncLabel();
    close(true);
  });

  input.addEventListener('change', () => {
    syncLabel();
    if (!popover.hidden) render();
  });
  document.addEventListener('pointerdown', event => {
    if (!popover.hidden && !root.contains(event.target)) close(false);
  });
  document.addEventListener('keydown', event => {
    if (event.key === 'Escape' && !popover.hidden) close(true);
  });
  syncLabel();
}

function v0211ReadDate(input) {
  const match = /^(\d{4})-(\d{2})-(\d{2})$/.exec(String(input?.value || ''));
  if (match) return { year: Number(match[1]), month: Number(match[2]), day: Number(match[3]) };
  return v0211Today();
}

function v0211Today() {
  const now = new Date();
  return { year: now.getFullYear(), month: now.getMonth() + 1, day: now.getDate() };
}

function refineV0211HeaderIdentity() {
  const user = document.querySelector('#currentUser');
  if (!user) return;
  user.style.removeProperty('padding-top');
  user.style.removeProperty('padding-bottom');
}

function v0211Escape(value) {
  return String(value ?? '')
    .replaceAll('&', '&amp;')
    .replaceAll('<', '&lt;')
    .replaceAll('>', '&gt;')
    .replaceAll('"', '&quot;')
    .replaceAll("'", '&#039;');
}

/* ---- baseline section ---- */
/* V0.21.1 keyboard bridge for the Desktop custom date trigger. */

const CY_V0211_KEYBOARD_DESKTOP = '(min-width: 1024px)';

const setupV0211KeyboardBridge = () => {
  if (!window.matchMedia(CY_V0211_KEYBOARD_DESKTOP).matches) return;
  const input = document.querySelector('#txDate');
  const root = input?.nextElementSibling?.classList?.contains('v0211-date-picker') ? input.nextElementSibling : null;
  const trigger = root?.querySelector('.v0211-date-trigger');
  if (!input || !trigger || trigger.dataset.v0211KeyboardBound === '1') return;
  trigger.dataset.v0211KeyboardBound = '1';

  trigger.addEventListener('keydown', event => {
    if (event.isComposing) return;

    if (event.key === 'Enter' && !event.ctrlKey && !event.altKey && !event.metaKey && !event.shiftKey) {
      event.preventDefault();
      event.stopPropagation();
      document.querySelector('#summary')?.focus();
      return;
    }

    if (event.key === 'Tab' && !event.shiftKey && !event.ctrlKey && !event.altKey && !event.metaKey) {
      event.preventDefault();
      event.stopPropagation();
      if (typeof state === 'object' && typeof setEntryKind === 'function') {
        setEntryKind(state.kind === 'expense' ? 'income' : 'expense');
        if (typeof updateEntryKindVisual === 'function') updateEntryKindVisual();
        if (typeof renderFavoriteCategories === 'function') renderFavoriteCategories();
        if (typeof loadFrequentSummaries === 'function') loadFrequentSummaries();
      }
      trigger.focus();
      return;
    }

    const quickDigit = /^\d$/.test(event.key) && !event.ctrlKey && !event.altKey && !event.metaKey;
    const quickStep = event.ctrlKey && !event.altKey && !event.metaKey && (event.key === 'ArrowUp' || event.key === 'ArrowDown');
    if (!quickDigit && !quickStep) return;

    event.preventDefault();
    event.stopPropagation();
    input.dispatchEvent(new KeyboardEvent('keydown', {
      key: event.key,
      code: event.code,
      ctrlKey: event.ctrlKey,
      altKey: event.altKey,
      metaKey: event.metaKey,
      shiftKey: event.shiftKey,
      bubbles: true,
      cancelable: true
    }));
  });
};

if (document.readyState === 'complete') setTimeout(setupV0211KeyboardBridge, 0);
else window.addEventListener('load', () => setTimeout(setupV0211KeyboardBridge, 0), { once: true });
setTimeout(setupV0211KeyboardBridge, 350);

/* ---- baseline section ---- */
const CY_V0212_VERSION = 'V0.21.2';
const CY_V0212_DESKTOP = '(min-width: 1024px)';
let cyV0212DialogState = null;
let cyV0212Rendering = false;

if (!window.__CYACC_BASELINE_BUNDLE__) ensureV0212Stylesheet();
const runV0212 = () => {
  enforceV0212Version();
  if (!window.matchMedia(CY_V0212_DESKTOP).matches) return;
  installV0212CategoryRenderer();
  if (!window.__CYACC_BASELINE_BUNDLE__) ensureV0212ManagerDialog();
  bindV0212ManagerActions();
  renderV0212CategoryManager();
};

if (document.readyState === 'loading') {
  document.addEventListener('DOMContentLoaded', () => setTimeout(runV0212, 0), { once: true });
} else {
  setTimeout(runV0212, 0);
}
window.addEventListener('load', () => {
  setTimeout(runV0212, 0);
  setTimeout(runV0212, 120);
  setTimeout(runV0212, 420);
}, { once: true });

function ensureV0212Stylesheet() {
  if (document.querySelector('link[href="/v0212.css"]')) return;
  const link = document.createElement('link');
  link.rel = 'stylesheet';
  link.href = '/v0212.css';
  document.head.appendChild(link);
}

function enforceV0212Version() {
  let version = document.querySelector('.version');
  if (!version) return;
  if (version.dataset.v0212Version !== '1') {
    const replacement = version.cloneNode(true);
    replacement.dataset.v0212Version = '1';
    version.replaceWith(replacement);
    version = replacement;
  }
  version.textContent = CY_V0212_VERSION;
}

function installV0212CategoryRenderer() {
  window.renderCategoryManager = renderV0212CategoryManager;
  if (typeof window.renderV21Build15CategoryManager === 'function') window.renderV21Build15CategoryManager = renderV0212CategoryManager;
  if (typeof window.renderV21Build16CategoryManager === 'function') window.renderV21Build16CategoryManager = renderV0212CategoryManager;
  if (typeof window.renderV0211CategoryManager === 'function') window.renderV0211CategoryManager = renderV0212CategoryManager;
}

function renderV0212CategoryManager() {
  if (!window.matchMedia(CY_V0212_DESKTOP).matches || typeof state !== 'object') return;
  const host = document.querySelector('#categoryManager');
  const pane = document.querySelector('[data-settings-pane="categories"]');
  if (!host || !pane || cyV0212Rendering) return;

  const kind = state.settingsKind === 'income' ? 'income' : 'expense';
  const groups = (state.groups || []).filter(group => group.kind === kind);
  pane.classList.toggle('v0211-category-income', kind === 'income');
  pane.classList.toggle('v0211-category-expense', kind === 'expense');
  pane.classList.toggle('v0212-category-income', kind === 'income');
  pane.classList.toggle('v0212-category-expense', kind === 'expense');

  const legacyGroupAdd = document.querySelector('#newGroupName')?.parentElement;
  legacyGroupAdd?.classList.add('v0212-legacy-group-add');

  const toolbar = `<div class="v0212-category-toolbar">
    <div class="entry-kind-switch v0212-kind-switch" role="group" aria-label="收入或支出">
      <button type="button" class="kind-button${kind === 'income' ? ' active' : ''}" data-kind="income" data-v0212-manager-kind="income">收入</button>
      <button type="button" class="kind-button${kind === 'expense' ? ' active' : ''}" data-kind="expense" data-v0212-manager-kind="expense">支出</button>
    </div>
    <button type="button" class="secondary compact v0212-toolbar-button" data-v0212-add-group>＋ 新增大分類</button>
    <span class="v0212-toolbar-spacer" aria-hidden="true"></span>
    <button type="button" class="secondary compact v0212-toolbar-button v0212-add-category-button" data-v0212-add-category${groups.length ? '' : ' disabled title="請先新增大分類"'}>＋ 新增科目</button>
  </div>`;

  const body = groups.length
    ? `<div class="v0212-category-list">${groups.map(group => v0212GroupHtml(group, kind)).join('')}</div>`
    : '<div class="v0212-category-list-empty">目前沒有大分類。請先使用上方「新增大分類」。</div>';

  cyV0212Rendering = true;
  try {
    host.innerHTML = `<div class="v0211-category-shell v0212-category-shell">${toolbar}${body}</div>`;
  } finally {
    cyV0212Rendering = false;
  }
}

function v0212GroupHtml(group, kind) {
  const groupId = Number(group.id);
  const categories = (state.categories || []).filter(category =>
    category.kind === kind && Number(category.group_id) === groupId
  );
  const rows = categories.map(category => {
    const id = Number(category.id);
    const favorite = Number(category.is_favorite) === 1;
    return `<div class="v0212-category-row" data-v21-category-row="${id}" data-v21-category-group="${groupId}">
      <button type="button" class="v21-drag-handle" draggable="true" data-v21-drag-category="${id}" title="拖曳調整科目順序或分類" aria-label="拖曳調整科目順序或分類">⠿</button>
      <button type="button" class="v0212-favorite${favorite ? ' active' : ''}" data-category-favorite="${id}" title="${favorite ? '取消常用科目' : '設為常用科目'}" aria-label="${favorite ? '取消常用科目' : '設為常用科目'}">${favorite ? '★' : '☆'}</button>
      <span class="v0212-category-name" title="${v0212Escape(category.name)}">${v0212Escape(category.name)}</span>
      <button type="button" class="mini-button v0212-edit-button" data-v0212-rename="category" data-v0212-id="${id}" title="編輯科目名稱" aria-label="編輯科目名稱">✎</button>
      <button type="button" class="mini-button danger v21-manager-delete" data-category-delete="${id}">刪除</button>
    </div>`;
  }).join('');

  return `<section class="v0212-category-group" data-group-id="${groupId}">
    <div class="v21-category-group-head v0212-category-group-head">
      <button type="button" class="v21-drag-handle" draggable="true" data-v21-drag-group="${groupId}" title="拖曳調整大分類順序" aria-label="拖曳調整大分類順序">⠿</button>
      <strong class="v0212-group-name" title="${v0212Escape(group.name)}">${v0212Escape(group.name)}</strong>
      <button type="button" class="mini-button v0212-edit-button" data-v0212-rename="group" data-v0212-id="${groupId}" title="編輯大分類名稱" aria-label="編輯大分類名稱">✎</button>
      <button type="button" class="mini-button danger v21-manager-delete" data-group-delete="${groupId}">刪除</button>
    </div>
    <div class="v0212-category-items" data-v21-category-dropzone="${groupId}">
      ${rows || '<div class="v0212-empty-group">尚無科目</div>'}
    </div>
  </section>`;
}

function bindV0212ManagerActions() {
  if (window.__cyV0212ManagerActionsBound) return;
  window.__cyV0212ManagerActionsBound = true;

  window.addEventListener('click', event => {
    if (!window.matchMedia(CY_V0212_DESKTOP).matches) return;
    const rename = event.target.closest('[data-v0212-rename]');
    const legacyRename = event.target.closest('[data-account-rename], [data-category-rename], [data-group-rename]');
    const addGroup = event.target.closest('[data-v0212-add-group]');
    const addCategory = event.target.closest('[data-v0212-add-category]');
    if (!rename && !legacyRename && !addGroup && !addCategory) return;

    event.preventDefault();
    event.stopPropagation();
    event.stopImmediatePropagation();

    if (addGroup) return openV0212AddGroupDialog();
    if (addCategory) {
      if (addCategory.disabled) return;
      return openV0212AddCategoryDialog();
    }
    if (rename) return openV0212RenameDialog(rename.dataset.v0212Rename, Number(rename.dataset.v0212Id));

    if (legacyRename?.dataset.accountRename) return openV0212RenameDialog('account', Number(legacyRename.dataset.accountRename));
    if (legacyRename?.dataset.categoryRename) return openV0212RenameDialog('category', Number(legacyRename.dataset.categoryRename));
    if (legacyRename?.dataset.groupRename) return openV0212RenameDialog('group', Number(legacyRename.dataset.groupRename));
  }, true);

  document.querySelector('#categoryManager')?.addEventListener('click', event => {
    const button = event.target.closest('[data-v0212-manager-kind]');
    if (!button || typeof state !== 'object') return;
    const kind = button.dataset.v0212ManagerKind;
    if (!['income', 'expense'].includes(kind) || kind === state.settingsKind) return;
    state.settingsKind = kind;
    if (typeof cyV21Build15Drag !== 'undefined' && cyV21Build15Drag && typeof finishV21Build15Drag === 'function') finishV21Build15Drag();
    renderV0212CategoryManager();
  });

  const host = document.querySelector('#categoryManager');
  if (host && host.dataset.v0212Guard !== '1') {
    host.dataset.v0212Guard = '1';
    const observer = new MutationObserver(() => {
      if (cyV0212Rendering || !window.matchMedia(CY_V0212_DESKTOP).matches) return;
      if (!host.querySelector('.v0212-category-shell') && host.children.length) queueMicrotask(renderV0212CategoryManager);
    });
    observer.observe(host, { childList: true, subtree: false });
  }
}

function ensureV0212ManagerDialog() {
  if (document.querySelector('#v0212ManagerDialog')) return;
  const dialog = document.createElement('dialog');
  dialog.id = 'v0212ManagerDialog';
  dialog.className = 'v0212-manager-dialog';
  dialog.innerHTML = `<form method="dialog" class="v0212-manager-dialog-shell" id="v0212ManagerForm">
    <div class="v0212-manager-dialog-head">
      <div><h2 id="v0212ManagerTitle">新增科目</h2><p id="v0212ManagerSubtitle"></p></div>
      <button type="button" class="icon-button" data-v0212-dialog-close aria-label="關閉">×</button>
    </div>
    <label class="v0212-dialog-field" id="v0212GroupField" hidden>
      <span>大分類</span>
      <select id="v0212GroupSelect"></select>
    </label>
    <label class="v0212-dialog-field">
      <span id="v0212NameLabel">科目名稱</span>
      <input id="v0212NameInput" type="text" maxlength="60" autocomplete="off">
    </label>
    <div id="v0212ManagerMessage" class="dialog-message"></div>
    <div class="v0212-manager-dialog-actions">
      <button type="button" class="secondary" data-v0212-dialog-close>取消</button>
      <button type="submit" class="primary" id="v0212ManagerSave">新增</button>
    </div>
  </form>`;
  document.body.append(dialog);

  dialog.querySelectorAll('[data-v0212-dialog-close]').forEach(button => button.addEventListener('click', () => dialog.close()));
  dialog.querySelector('#v0212ManagerForm')?.addEventListener('submit', saveV0212ManagerDialog);
  dialog.addEventListener('close', () => { cyV0212DialogState = null; });
}

function openV0212AddGroupDialog() {
  if (typeof state !== 'object') return;
  openV0212ManagerDialog({
    mode: 'add-group',
    title: '新增大分類',
    subtitle: state.settingsKind === 'income' ? '新增到收入分類' : '新增到支出分類',
    label: '大分類名稱',
    maxLength: 60,
    value: '',
    showGroup: false,
    saveText: '新增'
  });
}

function openV0212AddCategoryDialog() {
  if (typeof state !== 'object') return;
  const kind = state.settingsKind === 'income' ? 'income' : 'expense';
  const groups = (state.groups || []).filter(group => group.kind === kind);
  if (!groups.length) return;
  openV0212ManagerDialog({
    mode: 'add-category',
    title: '新增科目',
    subtitle: kind === 'income' ? '新增收入科目' : '新增支出科目',
    label: '科目名稱',
    maxLength: 60,
    value: '',
    showGroup: true,
    groups,
    saveText: '新增'
  });
}

function openV0212RenameDialog(type, id) {
  if (typeof state !== 'object' || !Number.isInteger(id) || id <= 0) return;
  let item = null;
  let title = '';
  let label = '';
  let maxLength = 60;
  if (type === 'account') {
    item = (state.accounts || []).find(row => Number(row.id) === id);
    title = '編輯帳戶名稱';
    label = '帳戶名稱';
    maxLength = 8;
  } else if (type === 'group') {
    item = (state.groups || []).find(row => Number(row.id) === id);
    title = '編輯大分類';
    label = '大分類名稱';
  } else if (type === 'category') {
    item = (state.categories || []).find(row => Number(row.id) === id);
    title = '編輯科目';
    label = '科目名稱';
  }
  if (!item) return;
  openV0212ManagerDialog({
    mode: 'rename',
    type,
    id,
    title,
    subtitle: '編輯時不改變目前排序與分類位置',
    label,
    maxLength,
    value: item.name || '',
    showGroup: false,
    saveText: '儲存'
  });
}
window.openV0212RenameDialog = openV0212RenameDialog;

function openV0212ManagerDialog(config) {
  if (!window.__CYACC_BASELINE_BUNDLE__) ensureV0212ManagerDialog();
  const dialog = document.querySelector('#v0212ManagerDialog');
  if (!dialog) return;
  cyV0212DialogState = config;
  dialog.querySelector('#v0212ManagerTitle').textContent = config.title || '';
  dialog.querySelector('#v0212ManagerSubtitle').textContent = config.subtitle || '';
  dialog.querySelector('#v0212NameLabel').textContent = config.label || '名稱';
  const input = dialog.querySelector('#v0212NameInput');
  input.maxLength = Number(config.maxLength || 60);
  input.value = config.value || '';
  const groupField = dialog.querySelector('#v0212GroupField');
  const select = dialog.querySelector('#v0212GroupSelect');
  groupField.hidden = !config.showGroup;
  if (config.showGroup) {
    select.innerHTML = (config.groups || []).map(group => `<option value="${Number(group.id)}">${v0212Escape(group.name)}</option>`).join('');
  } else {
    select.innerHTML = '';
  }
  dialog.querySelector('#v0212ManagerSave').textContent = config.saveText || '儲存';
  setDialogMessage(dialog.querySelector('#v0212ManagerMessage'), '');
  if (!dialog.open) dialog.showModal();
  setTimeout(() => input.focus(), 0);
}

async function saveV0212ManagerDialog(event) {
  event.preventDefault();
  const config = cyV0212DialogState;
  const dialog = document.querySelector('#v0212ManagerDialog');
  if (!config || !dialog || typeof state !== 'object') return;
  const input = dialog.querySelector('#v0212NameInput');
  const select = dialog.querySelector('#v0212GroupSelect');
  const save = dialog.querySelector('#v0212ManagerSave');
  const message = dialog.querySelector('#v0212ManagerMessage');
  const name = String(input?.value || '').trim();
  if (!name) {
    setDialogMessage(message, '請輸入名稱。', true);
    input?.focus();
    return;
  }

  save.disabled = true;
  let ok = false;
  try {
    if (config.mode === 'add-group') {
      ok = await mutateSettings('/api/category-groups', {
        method: 'POST', headers: jsonHeaders(), body: JSON.stringify({ kind: state.settingsKind, name })
      }, '大分類已新增。');
    } else if (config.mode === 'add-category') {
      const groupId = Number(select?.value);
      if (!Number.isInteger(groupId) || groupId <= 0) {
        setDialogMessage(message, '請選擇大分類。', true);
        return;
      }
      ok = await mutateSettings('/api/categories', {
        method: 'POST', headers: jsonHeaders(), body: JSON.stringify({ kind: state.settingsKind, groupId, name })
      }, '科目已新增。');
    } else if (config.mode === 'rename') {
      const endpoints = {
        account: `/api/accounts/${config.id}`,
        group: `/api/category-groups/${config.id}`,
        category: `/api/categories/${config.id}`
      };
      const endpoint = endpoints[config.type];
      if (!endpoint) return;
      ok = await mutateSettings(endpoint, {
        method: 'PUT', headers: jsonHeaders(), body: JSON.stringify({ name })
      }, '名稱已更新。');
    }
    if (ok) {
      dialog.close();
      setTimeout(() => {
        installV0212CategoryRenderer();
        renderV0212CategoryManager();
      }, 0);
    }
  } finally {
    save.disabled = false;
  }
}

function v0212Escape(value) {
  return String(value ?? '')
    .replaceAll('&', '&amp;')
    .replaceAll('<', '&lt;')
    .replaceAll('>', '&gt;')
    .replaceAll('"', '&quot;')
    .replaceAll("'", '&#039;');
}

/* ---- baseline section ---- */
const CY_V0214_VERSION = 'V0.21.5 Build 11';
const CY_V0214_HOVER = '(hover: hover) and (pointer: fine)';
let cyV0214BalancePopover = null;
let cyV0214BalanceAnchor = null;
let cyV0214BalancePinned = false;
let cyV0214BalanceHideTimer = null;
const cyV0214PendingWrites = new Set();

if (!window.__CYACC_BASELINE_BUNDLE__) ensureV0214Stylesheet();
window.cyShowMigrationComplete = showMigrationCompleteV0214;
window.cyCloseLedgerBalancePopover = closeLedgerBalancePopoverV0214;

const runV0214 = () => {
  enforceV0214Version();
  if (!window.__CYACC_BASELINE_BUNDLE__) ensureMigrationCompleteDialogV0214();
  setupBalancePopoverV0214();
  setupOptimisticSettingsV0214();
};

if (document.readyState === 'loading') {
  document.addEventListener('DOMContentLoaded', () => setTimeout(runV0214, 0), { once: true });
} else {
  setTimeout(runV0214, 0);
}
window.addEventListener('load', () => {
  runV0214();
  setTimeout(runV0214, 160);
  setTimeout(runV0214, 520);
  setTimeout(runV0214, 900);
}, { once: true });

function ensureV0214Stylesheet() {
  if (document.querySelector('link[href="/v0214.css"]')) return;
  const link = document.createElement('link');
  link.rel = 'stylesheet';
  link.href = '/v0214.css';
  document.head.appendChild(link);
}

function enforceV0214Version() {
  let version = document.querySelector('.version');
  if (!version) return;
  if (version.dataset.v0214Version !== '1') {
    const replacement = version.cloneNode(true);
    replacement.dataset.v0214Version = '1';
    version.replaceWith(replacement);
    version = replacement;
  }
  version.textContent = CY_V0214_VERSION;
}

function ensureMigrationCompleteDialogV0214() {
  let dialog = document.querySelector('#migrationCompleteDialogV0214');
  if (dialog) return dialog;
  dialog = document.createElement('dialog');
  dialog.id = 'migrationCompleteDialogV0214';
  dialog.className = 'modal small-modal v0214-complete-dialog';
  dialog.innerHTML =
    '<div class="modal-header">' +
      '<div><span class="v0214-success-mark" aria-hidden="true">✓</span><h2>帳本移轉完成</h2><p>桌面帳本已成功複製到 Web。</p></div>' +
      '<button class="icon-button" type="button" data-v0214-close aria-label="關閉">×</button>' +
    '</div>' +
    '<div class="v0214-complete-grid">' +
      '<div><span>交易</span><strong data-v0214-result="transactions">—</strong></div>' +
      '<div><span>期初餘額</span><strong data-v0214-result="opening">—</strong></div>' +
      '<div><span>帳戶</span><strong data-v0214-result="accounts">—</strong></div>' +
      '<div><span>科目</span><strong data-v0214-result="categories">—</strong></div>' +
      '<div><span>重複交易</span><strong data-v0214-result="duplicates">—</strong></div>' +
      '<div><span>鎖帳至</span><strong data-v0214-result="locked">—</strong></div>' +
    '</div>' +
    '<p class="v0214-complete-note">原本電腦版的 SQLite 帳本不會被刪除或修改。</p>' +
    '<div class="modal-actions"><button class="primary" type="button" data-v0214-close>完成</button></div>';
  document.body.appendChild(dialog);
  dialog.addEventListener('click', event => {
    if (event.target.closest('[data-v0214-close]')) dialog.close();
  });
  return dialog;
}

function showMigrationCompleteV0214(result = {}) {
  const dialog = ensureMigrationCompleteDialogV0214();
  if (!dialog) return;
  const set = (key, value) => {
    const node = dialog.querySelector('[data-v0214-result="' + key + '"]');
    if (node) node.textContent = value;
  };
  const number = value => Number(value || 0).toLocaleString('zh-TW');
  set('transactions', '新增 ' + number(result.insertedTransactions) + ' 筆');
  set('opening', '新增 ' + number(result.insertedOpeningBalances) + ' 筆');
  set('accounts', '新增 ' + number(result.insertedAccounts) + ' 個');
  set('categories', '新增 ' + number(result.insertedCategories) + ' 個');
  set('duplicates', '略過 ' + number(result.skippedDuplicateTransactions) + ' 筆');
  set('locked', result.lockedThrough ? String(result.lockedThrough).replace('-', '/') : '未設定');
  if (dialog.open) dialog.close();
  dialog.showModal();
}

function ensureBalancePopoverV0214() {
  if (cyV0214BalancePopover?.isConnected) return cyV0214BalancePopover;
  const popover = document.createElement('div');
  popover.id = 'ledgerBalancePopoverV0214';
  popover.className = 'v0214-balance-popover';
  popover.setAttribute('role', 'dialog');
  popover.setAttribute('aria-label', '帳戶餘額明細');
  popover.hidden = true;
  document.body.appendChild(popover);
  popover.addEventListener('pointerenter', clearBalanceHideV0214);
  popover.addEventListener('pointerleave', () => scheduleBalanceHideV0214());
  cyV0214BalancePopover = popover;
  return popover;
}

function setupBalancePopoverV0214() {
  const body = document.body;
  if (!body || body.dataset.v0214BalanceBound === '1') return;
  body.dataset.v0214BalanceBound = '1';
  if (!window.__CYACC_BASELINE_BUNDLE__) ensureBalancePopoverV0214();
  document.addEventListener('pointerover', event => {
    if (!window.matchMedia(CY_V0214_HOVER).matches) return;
    const cell = event.target.closest('[data-balance-popover-id]');
    if (!cell || cell.contains(event.relatedTarget)) return;
    if (cyV0214BalancePinned && cyV0214BalanceAnchor !== cell) return;
    clearBalanceHideV0214();
    showLedgerBalancePopoverV0214(cell, false);
  });

  document.addEventListener('pointerout', event => {
    if (!window.matchMedia(CY_V0214_HOVER).matches) return;
    const cell = event.target.closest('[data-balance-popover-id]');
    if (!cell || cell.contains(event.relatedTarget)) return;
    if (cyV0214BalancePopover?.contains(event.relatedTarget)) return;
    scheduleBalanceHideV0214();
  });

  document.addEventListener('click', event => {
    const cell = event.target.closest('[data-balance-popover-id]');
    if (cell) {
      event.preventDefault();
      if (cyV0214BalancePinned && cyV0214BalanceAnchor === cell) {
        closeLedgerBalancePopoverV0214();
        return;
      }
      showLedgerBalancePopoverV0214(cell, true);
      return;
    }
    if (cyV0214BalancePinned && !cyV0214BalancePopover?.contains(event.target)) {
      closeLedgerBalancePopoverV0214();
    }
  });

  document.addEventListener('focusin', event => {
    const cell = event.target.closest('[data-balance-popover-id]');
    if (cell && !cyV0214BalancePinned) showLedgerBalancePopoverV0214(cell, false);
  });

  document.addEventListener('focusout', event => {
    const cell = event.target.closest('[data-balance-popover-id]');
    if (!cell || cyV0214BalancePinned) return;
    if (cyV0214BalancePopover?.contains(event.relatedTarget)) return;
    scheduleBalanceHideV0214();
  });

  document.addEventListener('keydown', event => {
    const cell = event.target.closest('[data-balance-popover-id]');
    if (cell && (event.key === 'Enter' || event.key === ' ')) {
      event.preventDefault();
      showLedgerBalancePopoverV0214(cell, true);
    } else if (event.key === 'Escape' && !cyV0214BalancePopover?.hidden) {
      closeLedgerBalancePopoverV0214();
      cyV0214BalanceAnchor?.focus?.();
    }
  });

  window.addEventListener('resize', closeLedgerBalancePopoverV0214);
  window.addEventListener('scroll', closeLedgerBalancePopoverV0214, true);
}

function showLedgerBalancePopoverV0214(cell, pinned) {
  const id = Number(cell?.dataset.balancePopoverId || 0);
  const detail = window.cyLedgerBalanceBreakdowns?.get(id);
  if (!cell || !detail) return;
  const popover = ensureBalancePopoverV0214();
  clearBalanceHideV0214();

  if (cyV0214BalanceAnchor && cyV0214BalanceAnchor !== cell) {
    cyV0214BalanceAnchor.setAttribute('aria-expanded', 'false');
  }
  cyV0214BalanceAnchor = cell;
  cyV0214BalancePinned = Boolean(pinned);
  cell.setAttribute('aria-expanded', 'true');

  popover.replaceChildren();
  const header = document.createElement('div');
  header.className = 'v0214-balance-popover-header';
  header.textContent = detail.accountOnly ? '此筆後帳戶餘額' : '此筆後各帳戶餘額';
  popover.appendChild(header);

  const list = document.createElement('div');
  list.className = 'v0214-balance-list';
  for (const item of detail.accounts || []) {
    const row = document.createElement('div');
    row.className = 'v0214-balance-row' + (item.name === detail.activeAccount ? ' active' : '');
    const name = document.createElement('span');
    const amount = document.createElement('strong');
    name.textContent = item.name || '未命名帳戶';
    amount.textContent = formatV0214Money(item.value);
    row.append(name, amount);
    list.appendChild(row);
  }
  popover.appendChild(list);

  if (!detail.accountOnly) {
    const total = document.createElement('div');
    total.className = 'v0214-balance-total';
    const label = document.createElement('span');
    const amount = document.createElement('strong');
    label.textContent = '總餘額';
    amount.textContent = formatV0214Money(detail.total);
    total.append(label, amount);
    popover.appendChild(total);
  }

  popover.hidden = false;
  positionBalancePopoverV0214(cell, popover);
}

function positionBalancePopoverV0214(cell, popover) {
  const anchor = cell.getBoundingClientRect();
  const box = popover.getBoundingClientRect();
  const margin = 12;
  let left = anchor.right - box.width;
  left = Math.max(margin, Math.min(left, window.innerWidth - box.width - margin));
  let top = anchor.bottom + 8;
  if (top + box.height > window.innerHeight - margin) top = anchor.top - box.height - 8;
  top = Math.max(margin, top);
  popover.style.left = Math.round(left) + 'px';
  popover.style.top = Math.round(top) + 'px';
}

function scheduleBalanceHideV0214() {
  clearBalanceHideV0214();
  if (cyV0214BalancePinned) return;
  cyV0214BalanceHideTimer = window.setTimeout(closeLedgerBalancePopoverV0214, 140);
}

function clearBalanceHideV0214() {
  if (cyV0214BalanceHideTimer) window.clearTimeout(cyV0214BalanceHideTimer);
  cyV0214BalanceHideTimer = null;
}

function closeLedgerBalancePopoverV0214() {
  clearBalanceHideV0214();
  if (cyV0214BalanceAnchor) cyV0214BalanceAnchor.setAttribute('aria-expanded', 'false');
  cyV0214BalanceAnchor = null;
  cyV0214BalancePinned = false;
  if (cyV0214BalancePopover) cyV0214BalancePopover.hidden = true;
}

function formatV0214Money(value) {
  if (typeof money === 'function') return money(Number(value) || 0);
  return (Number(value) || 0).toLocaleString('zh-TW');
}

function setupOptimisticSettingsV0214() {
  const accounts = document.querySelector('#accountRows');
  if (accounts && accounts.dataset.v0214OptimisticBound !== '1') {
    accounts.dataset.v0214OptimisticBound = '1';
    accounts.addEventListener('click', handleV0214AccountDefault, true);
  }
  const categories = document.querySelector('#categoryManager');
  if (categories && categories.dataset.v0214OptimisticBound !== '1') {
    categories.dataset.v0214OptimisticBound = '1';
    categories.addEventListener('click', handleV0214FavoriteToggle, true);
  }
}

async function handleV0214AccountDefault(event) {
  const button = event.target.closest('[data-account-default]');
  if (!button || typeof state !== 'object') return;
  event.preventDefault();
  event.stopImmediatePropagation();
  const id = Number(button.dataset.accountDefault || 0);
  const key = 'default:' + id;
  if (!Number.isInteger(id) || id <= 0 || cyV0214PendingWrites.has(key)) return;
  const previous = (state.accounts || []).map(item => ({ ...item }));
  if (!previous.some(item => Number(item.id) === id)) return;
  const selected = document.querySelector('#accountName')?.value || '';

  cyV0214PendingWrites.add(key);
  state.accounts = previous.map(item => ({ ...item, is_default: Number(item.id) === id ? 1 : 0 }));
  renderV0214AccountState(selected);
  setV0214SettingsMessage('正在儲存預設帳戶…');

  try {
    await api('/api/accounts/' + id + '/default', { method: 'POST' });
    setV0214SettingsMessage('已更新預設帳戶。');
  } catch (error) {
    state.accounts = previous;
    renderV0214AccountState(selected);
    setV0214SettingsMessage(error.message || '預設帳戶儲存失敗，已還原。', true);
  } finally {
    cyV0214PendingWrites.delete(key);
  }
}

async function handleV0214FavoriteToggle(event) {
  const button = event.target.closest('[data-category-favorite]');
  if (!button || typeof state !== 'object') return;
  event.preventDefault();
  event.stopImmediatePropagation();
  const id = Number(button.dataset.categoryFavorite || 0);
  const key = 'favorite:' + id;
  const current = (state.categories || []).find(item => Number(item.id) === id);
  if (!current || cyV0214PendingWrites.has(key)) return;
  const previous = (state.categories || []).map(item => ({ ...item }));
  const nextFavorite = Number(current.is_favorite) !== 1;

  cyV0214PendingWrites.add(key);
  state.categories = previous.map(item => Number(item.id) === id ? { ...item, is_favorite: nextFavorite ? 1 : 0 } : item);
  renderV0214CategoryState();
  setV0214SettingsMessage('正在儲存常用科目…');

  try {
    await api('/api/categories/' + id + '/favorite', {
      method: 'PUT',
      headers: jsonHeaders(),
      body: JSON.stringify({ favorite: nextFavorite })
    });
    setV0214SettingsMessage(nextFavorite ? '已加入常用科目。' : '已取消常用科目。');
  } catch (error) {
    state.categories = previous;
    renderV0214CategoryState();
    setV0214SettingsMessage(error.message || '常用科目儲存失敗，已還原。', true);
  } finally {
    cyV0214PendingWrites.delete(key);
  }
}

function renderV0214AccountState(selected) {
  if (typeof renderV0211AccountManager === 'function') renderV0211AccountManager();
  else if (typeof renderAccountManager === 'function') renderAccountManager();
  if (typeof renderAccounts === 'function') renderAccounts(selected);
  if (typeof syncV21Build8AccountChoices === 'function') syncV21Build8AccountChoices();
}

function renderV0214CategoryState() {
  if (typeof renderV0212CategoryManager === 'function' && window.matchMedia('(min-width: 1024px)').matches) {
    renderV0212CategoryManager();
  } else if (typeof renderCategoryManager === 'function') {
    renderCategoryManager();
  }
  if (typeof renderFavoriteCategories === 'function') renderFavoriteCategories();
}

function setV0214SettingsMessage(text, error = false) {
  const target = document.querySelector('#settingsMessage');
  if (target && typeof setDialogMessage === 'function') setDialogMessage(target, text, error);
}

/* ---- baseline section ---- */
const CY_V0215_BUILD3_MOBILE = '(max-width: 767px)';
const CY_V0215_BUILD3_EDGE_GUARD = 24;
const CY_V0215_BUILD3_ACTION_WIDTH = 72;
const CY_V0215_BUILD3_OPEN_THRESHOLD = 34;

let cyV0215Build3OpenRow = null;
let cyV0215Build3Gesture = null;
let cyV0215Build3SuppressClickUntil = 0;

if (document.readyState === 'loading') {
  document.addEventListener('DOMContentLoaded', setupV0215Build3SwipeActions, { once: true });
} else {
  window.setTimeout(setupV0215Build3SwipeActions, 0);
}
window.addEventListener('load', setupV0215Build3SwipeActions, { once: true });

function setupV0215Build3SwipeActions() {
  const rows = document.querySelector('#transactionRows');
  if (!rows || rows.dataset.v0215Build3SwipeBound === '1') return;
  rows.dataset.v0215Build3SwipeBound = '1';

  rows.addEventListener('pointerdown', event => {
    if (!window.matchMedia(CY_V0215_BUILD3_MOBILE).matches) return;
    if (event.pointerType === 'mouse' && event.button !== 0) return;
    if (event.target.closest('button, input, select, textarea, a')) return;

    const row = event.target.closest('tr.ledger-row:not(.inline-editing)');
    if (!row) return;

    if (event.clientX <= CY_V0215_BUILD3_EDGE_GUARD) {
      closeV0215Build3SwipeRow();
      return;
    }

    if (cyV0215Build3OpenRow && cyV0215Build3OpenRow !== row) {
      closeV0215Build3SwipeRow();
    }

    cyV0215Build3Gesture = {
      pointerId: event.pointerId,
      row,
      startX: event.clientX,
      startY: event.clientY,
      startOffset: readV0215Build3OpenOffset(row),
      horizontal: false,
      cancelled: false,
      moved: false
    };

    row.classList.remove('v0215-swipe-animate');
    row.setPointerCapture?.(event.pointerId);
  });

  rows.addEventListener('pointermove', event => {
    const gesture = cyV0215Build3Gesture;
    if (!gesture || gesture.pointerId !== event.pointerId || gesture.cancelled) return;

    const dx = event.clientX - gesture.startX;
    const dy = event.clientY - gesture.startY;
    const absX = Math.abs(dx);
    const absY = Math.abs(dy);

    if (!gesture.horizontal) {
      if (Math.max(absX, absY) < 7) return;
      if (absY > absX) {
        gesture.cancelled = true;
        return;
      }
      gesture.horizontal = true;
    }

    const row = gesture.row;
    let next = clampV0215Build3(
      gesture.startOffset + dx,
      -CY_V0215_BUILD3_ACTION_WIDTH,
      CY_V0215_BUILD3_ACTION_WIDTH
    );

    const edit = row.querySelector('[data-edit-id]');
    const remove = row.querySelector('[data-delete-id]');
    if (next > 0 && (!edit || edit.disabled)) next = 0;
    if (next < 0 && (!remove || remove.disabled)) next = 0;

    gesture.moved = gesture.moved || Math.abs(next - gesture.startOffset) > 7;
    setV0215Build3SwipeOffset(row, next);
    row.dataset.swipeDirection = next > 1 ? 'edit' : next < -1 ? 'delete' : '';
    event.preventDefault();
  });

  const finish = event => {
    const gesture = cyV0215Build3Gesture;
    if (!gesture || gesture.pointerId !== event.pointerId) return;
    cyV0215Build3Gesture = null;

    const row = gesture.row;
    row.releasePointerCapture?.(event.pointerId);

    if (gesture.cancelled || !gesture.horizontal) {
      row.classList.add('v0215-swipe-animate');
      return;
    }

    const current = readV0215Build3Offset(row);
    if (gesture.moved) cyV0215Build3SuppressClickUntil = Date.now() + 260;

    if (current >= CY_V0215_BUILD3_OPEN_THRESHOLD) {
      openV0215Build3SwipeRow(row, 'edit');
    } else if (current <= -CY_V0215_BUILD3_OPEN_THRESHOLD) {
      openV0215Build3SwipeRow(row, 'delete');
    } else {
      closeV0215Build3SwipeRow(row);
    }
  };

  rows.addEventListener('pointerup', finish);
  rows.addEventListener('pointercancel', finish);

  rows.addEventListener('click', event => {
    const action = event.target.closest('[data-edit-id], [data-delete-id]');
    if (action) {
      const row = action.closest('tr.ledger-row');
      window.setTimeout(() => closeV0215Build3SwipeRow(row), 0);
      return;
    }

    if (Date.now() < cyV0215Build3SuppressClickUntil) {
      event.preventDefault();
      event.stopPropagation();
      return;
    }

    const row = event.target.closest('tr.ledger-row:not(.inline-editing)');
    if (row && row === cyV0215Build3OpenRow) {
      event.preventDefault();
      event.stopPropagation();
      closeV0215Build3SwipeRow(row);
    }
  }, true);

  document.addEventListener('pointerdown', event => {
    if (!cyV0215Build3OpenRow) return;
    if (cyV0215Build3OpenRow.contains(event.target)) return;
    closeV0215Build3SwipeRow();
  }, true);

  window.addEventListener('scroll', () => closeV0215Build3SwipeRow(), { passive: true });
}

function openV0215Build3SwipeRow(row, mode) {
  if (!row?.isConnected) return;
  if (cyV0215Build3OpenRow && cyV0215Build3OpenRow !== row) {
    closeV0215Build3SwipeRow(cyV0215Build3OpenRow);
  }

  const action = mode === 'edit'
    ? row.querySelector('[data-edit-id]')
    : row.querySelector('[data-delete-id]');
  if (!action || action.disabled) {
    closeV0215Build3SwipeRow(row);
    return;
  }

  row.classList.add('v0215-swipe-animate');
  row.dataset.swipeOpen = mode;
  row.dataset.swipeDirection = mode;
  setV0215Build3SwipeOffset(
    row,
    mode === 'edit' ? CY_V0215_BUILD3_ACTION_WIDTH : -CY_V0215_BUILD3_ACTION_WIDTH
  );
  cyV0215Build3OpenRow = row;
}

function closeV0215Build3SwipeRow(row = cyV0215Build3OpenRow) {
  if (!row) return;
  row.classList.add('v0215-swipe-animate');
  row.dataset.swipeOpen = '';
  row.dataset.swipeDirection = '';
  setV0215Build3SwipeOffset(row, 0);
  if (cyV0215Build3OpenRow === row) cyV0215Build3OpenRow = null;
}

function readV0215Build3OpenOffset(row) {
  if (row?.dataset.swipeOpen === 'edit') return CY_V0215_BUILD3_ACTION_WIDTH;
  if (row?.dataset.swipeOpen === 'delete') return -CY_V0215_BUILD3_ACTION_WIDTH;
  return 0;
}

function readV0215Build3Offset(row) {
  const value = Number(row?.style.getPropertyValue('--v0215-swipe-x').replace('px', ''));
  return Number.isFinite(value) ? value : 0;
}

function setV0215Build3SwipeOffset(row, value) {
  row?.style.setProperty('--v0215-swipe-x', value + 'px');
}

function clampV0215Build3(value, min, max) {
  return Math.max(min, Math.min(max, value));
}

/* ---- baseline section ---- */
const CY_V0215_BUILD4_MOBILE = '(max-width: 767px)';
let cyV0215Build4Edit = null;
let cyV0215Build4SaveHideTimer = null;
let cyV0215Build4SaveClearTimer = null;
let cyV0215Build4SetupDone = false;

window.cyAfterSaveMessage = handleV0215Build4SaveMessage;
window.cyOpenMobileLedgerOpening = openV0215MobileLedgerOpening;
window.cyOpenMobileLedgerLock = openV0215MobileLedgerLock;
window.cyOpenMobileSettingsPane = openV0215MobileSettingsPane;

if (document.readyState === 'loading') {
  document.addEventListener('DOMContentLoaded', setupV0215Build4, { once: true });
} else {
  window.setTimeout(setupV0215Build4, 0);
}
window.addEventListener('load', setupV0215Build4, { once: true });

function setupV0215Build4() {
  if (cyV0215Build4SetupDone) return;
  if (!document.querySelector('#transactionRows') || !document.querySelector('#transactionForm') || !document.querySelector('#mobileMainNav')) {
    window.setTimeout(setupV0215Build4, 50);
    return;
  }
  cyV0215Build4SetupDone = true;
  setupV0215Build4Toolbar();
  setupV0215Build4Search();
  setupV0215Build4SaveMessage();
  setupV0215Build4EntrySecondaryAction();
  setupV0215Build4MobileEdit();
}

function setupV0215Build4Toolbar(attempt = 0) {
  if (!window.matchMedia(CY_V0215_BUILD4_MOBILE).matches) return;
  const monthTools = document.querySelector('.ledger-month-tools');
  const prev = document.querySelector('#ledgerPrevMonth');
  const next = document.querySelector('#ledgerNextMonth');
  const more = document.querySelector('#mobileLedgerMoreButton');
  const slot = document.querySelector('#ledgerMonthSlot');
  const picker = document.querySelector('.ledger-title .month-picker');
  if (!monthTools || !prev || !next || !more || !slot || !picker) {
    if (attempt < 60) window.setTimeout(() => setupV0215Build4Toolbar(attempt + 1), 50);
    return;
  }

  let balance = document.querySelector('#mobileLedgerBalanceButton');
  if (!balance) {
    balance = document.createElement('button');
    balance.id = 'mobileLedgerBalanceButton';
    balance.className = 'secondary compact v0215-mobile-balance-button';
    balance.type = 'button';
    balance.textContent = '餘額';
    balance.addEventListener('click', () => {
      openV0215MobileLedgerOpening();
    });
  }

  // Keep the month picker inside its original slot. Moving the label itself out of
  // the slot leaves an extra grid child and breaks the five-column mobile toolbar.
  if (picker.parentElement !== slot) slot.append(picker);
  monthTools.append(balance, prev, slot, next, more);
  setupV0215Build4MonthDisplay(slot);
  monthTools.classList.add('v0215-toolbar-ready');

  const displayMonth = document.querySelector('#ledgerDisplayMonth');
  if (displayMonth) displayMonth.hidden = true;

  const sheet = document.querySelector('#mobileLedgerToolsSheet');
  sheet?.querySelector('[data-mobile-ledger-action="opening"]')?.remove();
}

async function openV0215MobileLedgerOpening() {
  if (!window.matchMedia(CY_V0215_BUILD4_MOBILE).matches) return;
  const month = String(els.monthFilter?.value || '');
  if (!/^\d{4}-\d{2}$/.test(month)) return;
  if (els.openingMonth) els.openingMonth.value = month;
  if (typeof loadOpeningBalances === 'function') await loadOpeningBalances();
  if (els.openingDialog && !els.openingDialog.open) els.openingDialog.showModal();
}

function openV0215MobileSettingsPane(tab) {
  if (!window.matchMedia(CY_V0215_BUILD4_MOBILE).matches) return;
  if (!['accounts', 'categories'].includes(tab)) return;
  if (typeof openSettings === 'function') openSettings();
  if (typeof setSettingsTab === 'function') setSettingsTab(tab);

  const dialog = els.settingsDialog || document.querySelector('#settingsDialog');
  if (!dialog) return;
  dialog.classList.add('v0215-mobile-settings-focus');
  dialog.dataset.mobileSettingsFocus = tab;
  const title = dialog.querySelector('.modal-header h2');
  if (title) {
    if (!title.dataset.v0215OriginalTitle) title.dataset.v0215OriginalTitle = title.textContent || '設定';
    title.textContent = tab === 'accounts' ? '帳戶設定' : '科目設定';
  }
  if (!dialog.dataset.v0215FocusBound) {
    dialog.dataset.v0215FocusBound = '1';
    dialog.addEventListener('close', () => {
      dialog.classList.remove('v0215-mobile-settings-focus');
      delete dialog.dataset.mobileSettingsFocus;
      const heading = dialog.querySelector('.modal-header h2');
      if (heading?.dataset.v0215OriginalTitle) heading.textContent = heading.dataset.v0215OriginalTitle;
    });
  }
}

function ensureV0215MobileLockDialog() {
  let dialog = document.querySelector('#mobileLedgerLockDialogV0215');
  if (dialog) return dialog;

  dialog = document.createElement('dialog');
  dialog.id = 'mobileLedgerLockDialogV0215';
  dialog.className = 'modal v0215-mobile-lock-dialog';
  dialog.innerHTML = `
    <div class="v0215-mobile-lock-head">
      <div>
        <h2>月份鎖帳</h2>
        <p data-v0215-lock-month></p>
      </div>
      <button type="button" class="icon-button" data-v0215-lock-close aria-label="關閉">×</button>
    </div>
    <div class="v0215-mobile-lock-body">
      <p data-v0215-lock-status></p>
      <p class="v0215-mobile-lock-note" data-v0215-lock-note></p>
    </div>
    <div class="v0215-mobile-lock-actions">
      <button type="button" class="secondary" data-v0215-lock-close>取消</button>
      <button type="button" class="primary" data-v0215-lock-apply></button>
    </div>
    <p class="v0215-mobile-lock-message" data-v0215-lock-message></p>
  `;
  document.body.append(dialog);

  dialog.addEventListener('click', event => {
    if (event.target.closest('[data-v0215-lock-close]')) {
      dialog.close();
      return;
    }
    const apply = event.target.closest('[data-v0215-lock-apply]');
    if (apply) saveV0215MobileLedgerLock(dialog, apply);
  });
  return dialog;
}

function openV0215MobileLedgerLock() {
  if (!window.matchMedia(CY_V0215_BUILD4_MOBILE).matches) return;
  const month = String(els.monthFilter?.value || '');
  if (!/^\d{4}-\d{2}$/.test(month)) return;

  const dialog = ensureV0215MobileLockDialog();
  const lockedThrough = String(state.lockedThrough || '');
  const locked = Boolean(lockedThrough && month <= lockedThrough);
  const displayMonth = formatV0215MobileMonth(month);
  const status = dialog.querySelector('[data-v0215-lock-status]');
  const note = dialog.querySelector('[data-v0215-lock-note]');
  const apply = dialog.querySelector('[data-v0215-lock-apply]');
  const message = dialog.querySelector('[data-v0215-lock-message]');

  dialog.dataset.month = month;
  dialog.dataset.nextLockedThrough = locked ? previousV0215MobileMonth(month) : month;
  dialog.querySelector('[data-v0215-lock-month]').textContent = displayMonth;
  message.textContent = '';
  message.classList.remove('error');

  if (!locked) {
    status.textContent = '此月份目前未鎖帳。';
    note.textContent = '鎖定後，' + displayMonth + ' 以及更早月份都不可新增、修改或刪除記帳。';
    apply.textContent = '鎖定本月';
  } else if (lockedThrough === month) {
    status.textContent = '此月份目前已鎖帳。';
    note.textContent = '解除後，系統會保留鎖帳至 ' + formatV0215MobileMonth(previousV0215MobileMonth(month)) + '。';
    apply.textContent = '解除本月鎖帳';
  } else {
    status.textContent = '此月份包含在鎖帳範圍內，目前鎖帳至 ' + formatV0215MobileMonth(lockedThrough) + '。';
    note.textContent = '若解除，' + displayMonth + ' 到 ' + formatV0215MobileMonth(lockedThrough) + ' 都會一起解除鎖帳。';
    apply.textContent = '解除自本月起鎖帳';
  }

  if (!dialog.open) dialog.showModal();
}

async function saveV0215MobileLedgerLock(dialog, button) {
  const month = String(dialog?.dataset.month || '');
  const nextLockedThrough = String(dialog?.dataset.nextLockedThrough || '');
  const message = dialog?.querySelector('[data-v0215-lock-message]');
  if (!/^\d{4}-\d{2}$/.test(month) || !/^\d{4}-\d{2}$/.test(nextLockedThrough)) return;

  button.disabled = true;
  if (message) {
    message.textContent = '處理中…';
    message.classList.remove('error');
  }
  try {
    const data = await api('/api/settings/lock', {
      method: 'PUT',
      headers: jsonHeaders(),
      body: JSON.stringify({ lockedThrough: nextLockedThrough })
    });
    state.lockedThrough = data.lockedThrough || null;
    if (els.lockedThrough) els.lockedThrough.value = state.lockedThrough || '';
    updateEntryLockState();
    await loadTransactions();
    if (typeof scheduleLedgerDesktopRefresh === 'function') scheduleLedgerDesktopRefresh();
    dialog.close();
    showV0215Build4LedgerNotice(state.lockedThrough
      ? '已鎖帳至 ' + formatV0215MobileMonth(state.lockedThrough) + '。'
      : '已取消鎖帳。');
  } catch (error) {
    if (message) {
      message.textContent = error?.message || '鎖帳設定失敗。';
      message.classList.add('error');
    }
  } finally {
    button.disabled = false;
  }
}

function previousV0215MobileMonth(month) {
  const match = /^(\d{4})-(\d{2})$/.exec(String(month || ''));
  if (!match) return '';
  const date = new Date(Number(match[1]), Number(match[2]) - 2, 1);
  return date.getFullYear() + '-' + String(date.getMonth() + 1).padStart(2, '0');
}

function formatV0215MobileMonth(month) {
  const match = /^(\d{4})-(\d{2})$/.exec(String(month || ''));
  return match ? Number(match[1]) + '年' + Number(match[2]) + '月' : String(month || '');
}

function setupV0215Build4MonthDisplay(slot) {
  if (!slot || !els.monthFilter) return;

  let display = slot.querySelector('#mobileLedgerMonthDisplay');
  if (!display) {
    display = document.createElement('span');
    display.id = 'mobileLedgerMonthDisplay';
    display.className = 'v0215-mobile-month-display';
    display.setAttribute('aria-hidden', 'true');
    slot.append(display);
  }

  els.monthFilter.setAttribute('aria-label', '選擇月份');
  if (els.monthFilter.dataset.v0215MonthDisplayBound !== '1') {
    els.monthFilter.dataset.v0215MonthDisplayBound = '1';
    els.monthFilter.addEventListener('input', syncV0215Build4MonthDisplay);
    els.monthFilter.addEventListener('change', syncV0215Build4MonthDisplay);
  }
  syncV0215Build4MonthDisplay();
}

function syncV0215Build4MonthDisplay() {
  const display = document.querySelector('#mobileLedgerMonthDisplay');
  const value = String(els.monthFilter?.value || '');
  if (!display) return;
  const match = /^(\d{4})-(\d{2})$/.exec(value);
  display.textContent = match ? `${Number(match[1])}年${Number(match[2])}月` : '選擇月份';
}

function setupV0215Build4Search() {
  const form = document.querySelector('#ledgerSearchForm');
  const input = document.querySelector('#ledgerSummarySearch');
  if (!form || !input) return;
  input.setAttribute('enterkeyhint', 'search');
  input.setAttribute('inputmode', 'search');
  form.addEventListener('submit', () => {
    if (!window.matchMedia(CY_V0215_BUILD4_MOBILE).matches) return;
    window.setTimeout(() => input.blur(), 0);
  });
}

function setupV0215Build4SaveMessage() {
  const media = window.matchMedia(CY_V0215_BUILD4_MOBILE);
  const message = document.querySelector('#saveMessage');
  const saveButton = document.querySelector('#saveButton');
  if (!message || !saveButton) return;

  const originalParent = message.parentElement;
  const originalNext = message.nextSibling;

  const sync = () => {
    if (media.matches) {
      if (message.previousElementSibling !== saveButton) saveButton.insertAdjacentElement('afterend', message);
      message.classList.add('v0215-mobile-save-message');
    } else {
      message.classList.remove('v0215-mobile-save-message', 'is-visible', 'is-fading');
      if (originalParent && message.parentElement !== originalParent) {
        if (originalNext && originalNext.parentNode === originalParent) originalParent.insertBefore(message, originalNext);
        else originalParent.append(message);
      }
    }
  };

  if (typeof media.addEventListener === 'function') media.addEventListener('change', sync);
  else media.addListener?.(sync);
  sync();
}

function setupV0215Build4EntrySecondaryAction() {
  if (!window.matchMedia(CY_V0215_BUILD4_MOBILE).matches) return;
  const saveButton = document.querySelector('#saveButton');
  const message = document.querySelector('#saveMessage');
  if (!saveButton || !message) return;

  let button = document.querySelector('#mobileEntrySecondaryButton');
  if (!button) {
    button = document.createElement('button');
    button.id = 'mobileEntrySecondaryButton';
    button.className = 'secondary v0215-entry-secondary-button';
    button.type = 'button';
    button.textContent = '清空';
    button.addEventListener('click', () => {
      if (cyV0215Build4Edit) {
        cancelV0215Build4MobileEditAndReturn();
        return;
      }
      clearV0215Build4EntryForm();
    });
  }

  message.insertAdjacentElement('beforebegin', button);
  syncV0215Build4EntrySecondaryAction();
}

function syncV0215Build4EntrySecondaryAction() {
  const button = document.querySelector('#mobileEntrySecondaryButton');
  if (!button) return;
  button.textContent = cyV0215Build4Edit ? '取消' : '清空';
  button.classList.toggle('is-cancel', Boolean(cyV0215Build4Edit));
}

function clearV0215Build4EntryForm() {
  showMessage('');
  const today = typeof localDateString === 'function' ? localDateString(new Date()) : new Date().toISOString().slice(0, 10);
  els.txDate.value = today;
  renderAccounts();
  renderCategories();
  els.summary.value = '';
  els.amount.value = '';
  updateEntryLockState();
  els.summary.blur();
  els.amount.blur();
}

function cancelV0215Build4MobileEditAndReturn() {
  const context = cyV0215Build4Edit?.returnContext;
  if (!context) return false;
  cancelV0215Build4MobileEdit();
  switchV0215Build4MobilePage('ledger');
  restoreV0215Build4LedgerContext(context, false);
  return true;
}

function handleV0215Build4SaveMessage(message, text, isError) {
  if (!message) return;
  window.clearTimeout(cyV0215Build4SaveHideTimer);
  window.clearTimeout(cyV0215Build4SaveClearTimer);
  message.classList.remove('is-fading');
  message.classList.toggle('is-visible', Boolean(text));

  if (!text || isError || !window.matchMedia(CY_V0215_BUILD4_MOBILE).matches) return;

  cyV0215Build4SaveHideTimer = window.setTimeout(() => {
    message.classList.add('is-fading');
    cyV0215Build4SaveClearTimer = window.setTimeout(() => {
      if (message.classList.contains('is-fading')) {
        message.textContent = '';
        message.classList.remove('is-visible', 'is-fading');
      }
    }, 420);
  }, 2500);
}

function setupV0215Build4MobileEdit() {
  const rows = document.querySelector('#transactionRows');
  const form = document.querySelector('#transactionForm');
  const nav = document.querySelector('#mobileMainNav');
  if (!rows || !form || !nav) return;

  rows.addEventListener('click', event => {
    if (!window.matchMedia(CY_V0215_BUILD4_MOBILE).matches) return;
    const edit = event.target.closest('[data-edit-id]');
    if (!edit) return;
    const id = Number(edit.dataset.editId || 0);
    if (!Number.isInteger(id) || id <= 0 || edit.disabled) return;

    event.preventDefault();
    event.stopImmediatePropagation();
    beginV0215Build4MobileEdit(id);
  }, true);

  form.addEventListener('submit', event => {
    if (!cyV0215Build4Edit || !window.matchMedia(CY_V0215_BUILD4_MOBILE).matches) return;
    event.preventDefault();
    event.stopImmediatePropagation();
    saveV0215Build4MobileEdit();
  }, true);

  nav.addEventListener('click', event => {
    if (!cyV0215Build4Edit || !window.matchMedia(CY_V0215_BUILD4_MOBILE).matches) return;
    const button = event.target.closest('[data-mobile-page]');
    if (!button || button.dataset.mobilePage === 'entry') return;

    event.preventDefault();
    event.stopImmediatePropagation();
    cancelV0215Build4MobileEditAndReturn();
  }, true);

  window.addEventListener('pagehide', () => {
    if (!cyV0215Build4Edit) return;
    cancelV0215Build4MobileEdit({ restoreDraftOnly: true });
  });
}

function beginV0215Build4MobileEdit(id) {
  const tx = state.transactions.find(item => Number(item.id) === id);
  if (!tx || isLocked(String(tx.tx_date || '').slice(0, 7))) return;

  const row = document.querySelector('#transactionRows tr[data-transaction-id="' + id + '"]');
  cyV0215Build4Edit = {
    id,
    kind: tx.kind,
    originalMonth: String(tx.tx_date || '').slice(0, 7),
    draft: captureV0215Build4EntryDraft(),
    returnContext: {
      month: els.monthFilter?.value || '',
      search: document.querySelector('#ledgerSummarySearch')?.value || '',
      scrollY: window.scrollY,
      rowId: id,
      rowTop: row?.getBoundingClientRect().top ?? null
    }
  };

  setEntryKind(tx.kind);
  ensureV0215Build4Option(els.accountName, tx.account_name);
  els.accountName.value = tx.account_name;
  ensureV0215Build4Option(els.categoryName, tx.category_name);
  els.categoryName.value = tx.category_name;
  els.txDate.value = tx.tx_date;
  els.summary.value = tx.summary || '';
  els.amount.value = String(tx.amount || '');

  els.kindButtons.forEach(button => { button.disabled = true; });
  els.saveButton.textContent = '儲存修改';
  syncV0215Build4EntrySecondaryAction();
  document.querySelector('.entry-card')?.classList.add('v0215-mobile-editing');
  showMessage('');
  updateEntryLockState();

  switchV0215Build4MobilePage('entry');
  window.scrollTo({ top: 0, behavior: 'auto' });
}

async function saveV0215Build4MobileEdit() {
  const edit = cyV0215Build4Edit;
  if (!edit) return;

  const month = String(els.txDate.value || '').slice(0, 7);
  if (isLocked(month)) {
    showMessage('此月份已鎖帳，無法修改資料。', true);
    return;
  }

  const amount = Number(els.amount.value);
  if (!Number.isInteger(amount) || amount < 1 || amount > 9_999_999) {
    showMessage('金額必須為 1～9,999,999。', true);
    els.amount.focus();
    return;
  }

  els.saveButton.disabled = true;
  const destinationMonth = month;
  try {
    await api('/api/transactions/' + edit.id, {
      method: 'PUT',
      headers: jsonHeaders(),
      body: JSON.stringify({
        txDate: els.txDate.value,
        accountName: els.accountName.value,
        categoryName: els.categoryName.value,
        summary: els.summary.value.trim(),
        amount
      })
    });

    const context = edit.returnContext;
    const editedId = edit.id;
    cancelV0215Build4MobileEdit({ restoreDraftOnly: true });

    if (context.month && els.monthFilter.value !== context.month) {
      els.monthFilter.value = context.month;
    }
    const searchInput = document.querySelector('#ledgerSummarySearch');
    if (searchInput) searchInput.value = context.search || '';
    if (typeof cyLedgerSearch !== 'undefined') cyLedgerSearch = context.search || '';

    await loadTransactions();
    if (typeof renderDesktopLedger === 'function') renderDesktopLedger();

    switchV0215Build4MobilePage('ledger');
    restoreV0215Build4LedgerContext(context, destinationMonth === context.month ? editedId : false);

    if (destinationMonth !== context.month) {
      showV0215Build4LedgerNotice('修改完成，資料已移至 ' + destinationMonth.replace('-', '/') + '。');
    }
  } catch (error) {
    showMessage(error.message || '修改失敗。', true);
    updateEntryLockState();
  }
}

function cancelV0215Build4MobileEdit(options = {}) {
  const edit = cyV0215Build4Edit;
  if (!edit) return;
  cyV0215Build4Edit = null;

  clearV0215Build4TemporaryOptions();
  restoreV0215Build4EntryDraft(edit.draft);
  els.kindButtons.forEach(button => { button.disabled = false; });
  els.saveButton.textContent = '儲存';
  syncV0215Build4EntrySecondaryAction();
  document.querySelector('.entry-card')?.classList.remove('v0215-mobile-editing');
  showMessage('');
  updateEntryLockState();

  if (!options.restoreDraftOnly) window.cyCloseLedgerBalancePopover?.();
}

function captureV0215Build4EntryDraft() {
  return {
    kind: state.kind,
    txDate: els.txDate.value,
    accountName: els.accountName.value,
    categoryName: els.categoryName.value,
    summary: els.summary.value,
    amount: els.amount.value
  };
}

function restoreV0215Build4EntryDraft(draft) {
  if (!draft) return;
  setEntryKind(draft.kind);
  ensureV0215Build4Option(els.accountName, draft.accountName);
  els.accountName.value = draft.accountName || els.accountName.value;
  ensureV0215Build4Option(els.categoryName, draft.categoryName);
  els.categoryName.value = draft.categoryName || els.categoryName.value;
  els.txDate.value = draft.txDate || els.txDate.value;
  els.summary.value = draft.summary || '';
  els.amount.value = draft.amount || '';
}

function ensureV0215Build4Option(select, value) {
  const text = String(value || '').trim();
  if (!select || !text) return;
  if ([...select.options].some(option => option.value === text)) return;
  const option = document.createElement('option');
  option.value = text;
  option.textContent = text + '（歷史）';
  option.dataset.v0215EditTemporary = '1';
  select.append(option);
}

function clearV0215Build4TemporaryOptions() {
  for (const select of [els.accountName, els.categoryName]) {
    select?.querySelectorAll('[data-v0215-edit-temporary="1"]').forEach(option => option.remove());
  }
}

function switchV0215Build4MobilePage(page) {
  const shell = document.querySelector('.shell');
  const entry = shell?.querySelector('.entry-card');
  const ledger = shell?.querySelector('.ledger-card');
  const nav = document.querySelector('#mobileMainNav');
  if (!shell || !entry || !ledger || !nav) return;

  const current = page === 'ledger' ? 'ledger' : 'entry';
  entry.classList.toggle('v21-mobile-page-hidden', current !== 'entry');
  ledger.classList.toggle('v21-mobile-page-hidden', current !== 'ledger');
  shell.dataset.mobilePage = current;

  for (const button of nav.querySelectorAll('[data-mobile-page]')) {
    const active = button.dataset.mobilePage === current;
    button.classList.toggle('active', active);
    button.setAttribute('aria-selected', active ? 'true' : 'false');
  }
}

function restoreV0215Build4LedgerContext(context, highlightId) {
  if (!context) return;

  if (context.month && els.monthFilter.value !== context.month) {
    els.monthFilter.value = context.month;
  }
  syncV0215Build4MonthDisplay();
  const searchInput = document.querySelector('#ledgerSummarySearch');
  if (searchInput) searchInput.value = context.search || '';

  const restore = () => {
    const row = context.rowId
      ? document.querySelector('#transactionRows tr[data-transaction-id="' + context.rowId + '"]')
      : null;

    if (row && context.rowTop !== null) {
      const delta = row.getBoundingClientRect().top - context.rowTop;
      window.scrollBy({ top: delta, behavior: 'auto' });
    } else {
      window.scrollTo({ top: context.scrollY || 0, behavior: 'auto' });
    }

    if (highlightId) {
      const highlight = document.querySelector('#transactionRows tr[data-transaction-id="' + highlightId + '"]');
      if (highlight) {
        highlight.classList.add('v0215-edit-highlight');
        window.setTimeout(() => highlight.classList.remove('v0215-edit-highlight'), 1800);
      }
    }
  };

  window.requestAnimationFrame(() => window.requestAnimationFrame(restore));
}

function showV0215Build4LedgerNotice(text) {
  let notice = document.querySelector('#v0215LedgerNotice');
  if (!notice) {
    notice = document.createElement('div');
    notice.id = 'v0215LedgerNotice';
    notice.className = 'v0215-ledger-notice';
    document.body.append(notice);
  }
  notice.textContent = text;
  notice.classList.add('show');
  window.setTimeout(() => notice.classList.remove('show'), 2600);
}

/* ---- baseline section ---- */
const CY_V0216_VERSION = 'V0.21.6 Build 8';

if (document.readyState === 'loading') {
  document.addEventListener('DOMContentLoaded', setupV0216Version, { once: true });
} else {
  setupV0216Version();
}
window.addEventListener('load', setupV0216Version, { once: true });

function setupV0216Version() {
  syncV0216Version();
  window.setTimeout(syncV0216Version, 250);
  window.setTimeout(syncV0216Version, 1000);

  const version = document.querySelector('.version');
  if (!version || version.dataset.v0216Observed === 'true') return;
  version.dataset.v0216Observed = 'true';
  new MutationObserver(syncV0216Version).observe(version, {
    childList: true,
    characterData: true,
    subtree: true
  });
}

function syncV0216Version() {
  const version = document.querySelector('.version');
  if (version && version.textContent !== CY_V0216_VERSION) version.textContent = CY_V0216_VERSION;
}
