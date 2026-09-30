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
