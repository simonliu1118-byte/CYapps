const CY_MOBILE_ENTRY_MEDIA = '(max-width: 767px)';

function initialEntryKind() {
  return typeof window !== 'undefined' && window.matchMedia?.(CY_MOBILE_ENTRY_MEDIA)?.matches
    ? 'income'
    : 'expense';
}

const state = {
  kind: initialEntryKind(),
  accounts: [],
  archivedAccounts: [],
  groups: [],
  categories: [],
  transactions: [],
  transactionMonth: '',
  lockedThrough: null,
  ledgerLocked: false,
  settingsKind: 'income',
  activeSettingsTab: 'accounts',
  openingData: null
};
const els = {};

let cyaccAppStarted = false;
let cyTransactionRequestId = 0;
let cyTransactionMutationRevision = 0;
const cyPendingTransactionUpdates = new Map();
const cyPendingTransactionCreates = new Map();
let cyTransactionCreateId = 0;
let cyEntryDraftRevision = 0;
const cyPendingSettingsMutations = new Set();
let cySettingsTempId = -1;
let cyOpeningManualEdit = false;
let cyOpeningDesktopPresentation = false;
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
  });

    const today = localDateString(new Date());
    els.txDate.value = today;
    els.monthFilter.value = today.slice(0, 7);
    els.openingMonth.value = today.slice(0, 7);
    bindEvents();
    setEntryKind(state.kind);

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
  els.txDate.addEventListener('change', updateEntryLockState);
  els.form.addEventListener('submit', saveTransaction);
  const markEntryDraftChanged = () => { cyEntryDraftRevision += 1; };
  els.form.addEventListener('input', markEntryDraftChanged, true);
  els.form.addEventListener('change', markEntryDraftChanged, true);
  window.addEventListener('cyacc:entry-kind-changed', markEntryDraftChanged);
  window.addEventListener('cyacc:entry-draft-reset', markEntryDraftChanged);
  window.addEventListener('beforeunload', event => {
    if (!cyPendingTransactionCreates.size) return;
    event.preventDefault();
    event.returnValue = '';
  });
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

  els.settingsButton.addEventListener('click', openSettings);
  document.addEventListener('click', event => {
    const close = event.target.closest('[data-close-dialog]');
    if (!close) return;
    if (close.id === 'openingCloseButton' && cyOpeningDesktopPresentation && cyOpeningManualEdit) {
      cancelOpeningManualEdit();
      return;
    }
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

  els.openingDialog?.addEventListener('close', () => {
    if (cyOpeningDesktopPresentation && cyOpeningManualEdit) cancelOpeningManualEdit();
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
      window.cyaccRenderLedgerMessage('D1 尚未綁定，完成 Cloudflare 設定後即可開始記帳。', 'database-unbound');
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
  const epoch=String(bootstrap.accountingEpoch||'');
  if(window.cyaccBookEpoch!==undefined&&window.cyaccBookEpoch!==epoch) { window.location.reload();throw new Error('帳本已切換，正在重新整理。'); }
  window.cyaccBookEpoch=epoch;
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
  await window.cyPrepareQuickEntryUi?.();
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
  const groupOrder = new Map(state.groups.filter(group => group.kind === state.kind).map((group, index) => [Number(group.id), index]));
  const categories = state.categories.filter(category => category.kind === state.kind).sort((a, b) =>
    (groupOrder.get(Number(a.group_id)) ?? 999999) - (groupOrder.get(Number(b.group_id)) ?? 999999) || Number(a.sort_order || 0) - Number(b.sort_order || 0));
  els.categoryName.innerHTML = categories.map(category => {
    const groupName = category.group_name || state.groups.find(group => String(group.id) === String(category.group_id))?.name;
    const label = groupName ? `${groupName}／${category.name}` : category.name;
    return `<option value="${escapeHtml(category.name)}">${escapeHtml(label)}</option>`;
  }).join('');
  if (preferred && categories.some(c => c.name === preferred)) els.categoryName.value = preferred;
}

function setEntryKind(kind) {
  if (!['income', 'expense'].includes(kind)) return;
  const previousKind = state.kind;
  state.kind = kind;
  els.kindButtons.forEach(button => button.classList.toggle('active', button.dataset.kind === kind));
  renderCategories();
  window.cySyncMobileCanvasContinuation?.();
  window.dispatchEvent(new CustomEvent('cyacc:entry-kind-changed', {
    detail: { kind, previousKind }
  }));
}

function updateEntryLockState() {
  const month = String(els.txDate.value || '').slice(0, 7);
  const locked = isLocked(month);
  els.entryLockBadge.classList.toggle('hidden', !locked);
  els.saveButton.disabled = locked;
}

async function saveTransaction(event) {
  event.preventDefault();
  const entry = {
    txDate: els.txDate.value, accountName: els.accountName.value,
    categoryName: els.categoryName.value, kind: state.kind,
    summary: els.summary.value, amount: els.amount.value
  };
  let body;
  try {
    if (isLocked(entry.txDate.slice(0, 7))) throw new Error('此月份已鎖帳，無法新增資料。');
    body = { ...manualTransactionValues(entry), kind: entry.kind };
  } catch (error) {
    showMessage(error.message, true);
    window.dispatchEvent(new CustomEvent('cyacc:entry-result', { detail: { entry, ok: false, message: error.message } }));
    if (!Number.isInteger(Number(entry.amount)) || Number(entry.amount) < 1 || Number(entry.amount) > 9_999_999) els.amount.focus();
    return;
  }

  const id = --cyTransactionCreateId;
  const month = body.txDate.slice(0, 7);
  const row = { id, tx_date: body.txDate, account_name: body.accountName, category_name: body.categoryName,
    kind: body.kind, summary: body.summary, amount: body.amount, created_at: new Date().toISOString() };
  let finish;
  const settled = new Promise(resolve => { finish = resolve; });
  cyPendingTransactionCreates.set(id, { row, settled });
  cyTransactionMutationRevision += 1;
  els.summary.value = '';
  els.amount.value = '';
  const draftRevision = ++cyEntryDraftRevision;
  let saved = false;
  try {
    showMessage('儲存中…');
    window.dispatchEvent(new CustomEvent('cyacc:entry-submitted', { detail: { id, entry } }));
    if (els.monthFilter.value !== month) {
      els.monthFilter.value = month;
      els.monthFilter.dispatchEvent(new Event('change', { bubbles: true }));
    }
    if (state.transactionMonth === month && els.monthFilter.value === month) {
      state.transactions.push(row);
      renderOptimisticTransactionState();
    }

    const result = await api('/api/transactions', { method: 'POST', headers: jsonHeaders(), body: JSON.stringify(body) });
    const confirmed = result.transaction;
    if (!Number.isSafeInteger(confirmed?.id) || confirmed.id <= 0) throw new Error('尚未確認存檔結果，請重新整理確認後再操作。');
    // Never replace another request's row or navigate after an asynchronous write.
    state.transactions = state.transactions.filter(item => Number(item.id) !== id && Number(item.id) !== confirmed.id);
    if (state.transactionMonth === month) state.transactions.push(confirmed);
    saved = true;
    if (id === cyTransactionCreateId) showMessage('存檔成功');
    window.dispatchEvent(new CustomEvent('cyacc:entry-result', { detail: { id, entry, ok: true, message: '存檔成功' } }));
  } catch (error) {
    state.transactions = state.transactions.filter(item => Number(item.id) !== id);
    if (id === cyTransactionCreateId && draftRevision === cyEntryDraftRevision
      && els.summary.value === '' && els.amount.value === '' && state.kind === entry.kind
      && els.txDate.value === entry.txDate && els.accountName.value === entry.accountName
      && els.categoryName.value === entry.categoryName) {
      els.summary.value = entry.summary;
      els.amount.value = entry.amount;
      cyEntryDraftRevision += 1;
    }
    showMessage(error.message, true);
    window.dispatchEvent(new CustomEvent('cyacc:entry-result', { detail: { id, entry, ok: false, message: error.message } }));
  } finally {
    cyPendingTransactionCreates.delete(id);
    cyTransactionMutationRevision += 1;
    finish();
    if (state.transactionMonth === els.monthFilter.value) renderOptimisticTransactionState();
    updateEntryLockState();
  }
  if (saved) {
    // A prior-month write can change the currently displayed opening balance.
    if (month < els.monthFilter.value) {
      try { await window.cyaccRefreshLedgerView(els.monthFilter.value); }
      catch { showMessage('存檔成功，餘額載入失敗，請重新整理。', true); }
    }
    window.dispatchEvent(new CustomEvent('cyacc:transactions-loaded', { detail: { month: els.monthFilter.value } }));
  }
}

async function loadTransactions() {
  const month = String(els.monthFilter.value || '');
  if (!month) return;
  const requestId = ++cyTransactionRequestId;
  const mutationRevision = cyTransactionMutationRevision;
  setLedgerLoadingState(true);
  try {
    const data = await api(`/api/transactions?month=${encodeURIComponent(month)}`);
    // A GET may see a committed row before its POST acknowledgement arrives.
    // Wait for this month's outstanding creates before accepting the snapshot,
    // then use the same revision guard; identical legitimate entries stay distinct.
    await Promise.all([...cyPendingTransactionCreates.values()]
      .filter(item => item.row.tx_date.slice(0, 7) === month).map(item => item.settled));
    if (requestId !== cyTransactionRequestId || month !== els.monthFilter.value) return;
    if (mutationRevision !== cyTransactionMutationRevision) return await loadTransactions();
    state.transactionMonth = month;
    state.transactions = mergePendingTransactionUpdates(data.transactions || [], month);
    state.ledgerLocked = Boolean(data.locked);
    state.lockedThrough = data.lockedThrough || state.lockedThrough;
    els.ledgerLockBadge?.classList.toggle('hidden', !state.ledgerLocked);
    window.cySyncLedgerQuickLock?.();
    await window.cyaccRefreshLedgerView(month);
    updateEntryLockState();
    window.dispatchEvent(new CustomEvent('cyacc:transactions-loaded', { detail: { month } }));
  } catch (error) {
    if (requestId !== cyTransactionRequestId || month !== els.monthFilter.value) return;
    window.cyaccRenderLedgerMessage(error.message, 'load-error');
  } finally {
    if (requestId === cyTransactionRequestId) setLedgerLoadingState(false);
  }
}

function setLedgerLoadingState(loading) {
  const busy = Boolean(loading);
  const ledger = document.querySelector('.ledger-card');
  ledger?.classList.toggle('is-loading', busy);
  ledger?.setAttribute('aria-busy', busy ? 'true' : 'false');
  const loadingLabel = document.querySelector('#ledgerMonthLoading');
  if (loadingLabel) loadingLabel.hidden = !busy;
  const loadingOverlay = document.querySelector('#ledgerLoadingOverlay');
  if (loadingOverlay) loadingOverlay.hidden = !busy;
  ledger?.querySelector('.table-wrap table')?.toggleAttribute('inert', busy && Boolean(window.cyIsDesktopInteractionWorkspace?.()));
  for (const control of document.querySelectorAll(
    '#ledgerPrevMonth, #ledgerNextMonth, #monthFilter, #ledgerMoreButton, #ledgerBalanceButton, #ledgerQuickLockButton, #ledgerOpeningBalanceButton, #ledgerLockSettingsButton, #ledgerExcelExport, #ledgerSearchForm input, #ledgerSearchForm button'
  )) {
    if ('disabled' in control) control.disabled = busy;
  }
  if (!busy) window.cySyncLedgerQuickLock?.();
}

function openEditTransaction(id) {
  window.cyBeginEntryTransactionEdit?.(id);
}

function summaryCharacterUnits(char) {
  const code = char.codePointAt(0) || 0;
  return code <= 0x7f || (code >= 0xff61 && code <= 0xff9f) ? 1 : 2;
}

function summaryWeightedUnits(value) {
  return Array.from(String(value || '')).reduce((units, char) => units + summaryCharacterUnits(char), 0);
}

function manualTransactionValues(values, original = null) {
  const body = {
    txDate: String(values.txDate || ''),
    accountName: String(values.accountName || ''),
    categoryName: String(values.categoryName || ''),
    summary: String(values.summary || '').trim(),
    amount: Number(values.amount)
  };
  if (isLocked(body.txDate.slice(0, 7)) || (original && isLocked(String(original.tx_date || '').slice(0, 7)))) {
    throw new Error('此月份已鎖帳，無法修改資料。');
  }
  if (!Number.isInteger(body.amount) || body.amount < 1 || body.amount > 9_999_999) {
    throw new Error('金額必須為 1～9,999,999。');
  }
  if (summaryWeightedUnits(body.summary) > 40) {
    throw new Error('摘要不可超過 20 個中文字或 40 個英數字元。');
  }
  return body;
}

function mergePendingTransactionUpdates(rows, month) {
  let merged = [...rows];
  for (const { updated } of cyPendingTransactionUpdates.values()) {
    merged = merged.filter(item => Number(item.id) !== Number(updated.id));
    if (String(updated.tx_date).slice(0, 7) === month) merged.push(updated);
  }
  return merged;
}

function renderOptimisticTransactionState() {
  renderDesktopLedger();
}

async function persistTransactionUpdate(id, values, onOptimistic) {
  id = Number(id);
  if (!Number.isSafeInteger(id) || id <= 0) throw new Error('此筆交易仍在儲存中。');
  const original = state.transactions.find(item => Number(item.id) === id);
  if (!original) throw new Error('找不到交易。');
  if (cyPendingTransactionUpdates.has(id)) throw new Error('此筆交易仍在儲存中。');
  const body = manualTransactionValues(values, original);
  const updated = { ...original, tx_date: body.txDate, account_name: body.accountName, category_name: body.categoryName, summary: body.summary, amount: body.amount };
  cyPendingTransactionUpdates.set(id, { original, updated });
  cyTransactionMutationRevision += 1;
  state.transactions = mergePendingTransactionUpdates(state.transactions, els.monthFilter.value);
  try {
    onOptimistic?.();
    renderOptimisticTransactionState();
    await api(`/api/transactions/${id}`, { method: 'PUT', headers: jsonHeaders(), body: JSON.stringify(body) });
    if (String(original.tx_date).slice(0, 7) !== body.txDate.slice(0, 7)) {
      try { await window.cyaccRefreshLedgerView(els.monthFilter.value); }
      catch { showMessage('修改已儲存，餘額載入失敗，請重新整理。', true); }
    }
    window.dispatchEvent(new CustomEvent('cyacc:transactions-loaded', { detail: { month: els.monthFilter.value } }));
    return true;
  } catch (error) {
    state.transactions = state.transactions.filter(item => Number(item.id) !== id);
    if (String(original.tx_date).slice(0, 7) === els.monthFilter.value) state.transactions.push(original);
    renderOptimisticTransactionState();
    throw error;
  } finally {
    cyPendingTransactionUpdates.delete(id);
    cyTransactionMutationRevision += 1;
  }
}

async function deleteTransaction(id) {
  if (!Number.isSafeInteger(Number(id)) || Number(id) <= 0) return;
  try {
    await api(`/api/transactions/${id}`, { method: 'DELETE' });
    showMessage('已刪除');
    await loadTransactions();
  } catch (error) {
    showMessage(error.message, true);
  }
}

function openSettings(initialTab) {
  setDialogMessage(els.settingsMessage, '');
  window.cySettingsManager?.prepareSettings?.(typeof initialTab === 'string' ? initialTab : null);
  renderSettings();
  els.settingsDialog.showModal();
}

function setSettingsTab(tab) {
  state.activeSettingsTab = tab;
  els.settingsTabs.forEach(button => button.classList.toggle('active', button.dataset.settingsTab === tab));
  els.settingsPanes.forEach(pane => pane.classList.toggle('active', pane.dataset.settingsPane === tab));
  setDialogMessage(els.settingsMessage, '');
  if (tab === 'statistics') window.cyStatistics?.loadSettings();
  if (tab === 'quick' && typeof loadQuickEntrySettings === 'function') loadQuickEntrySettings(true);
}

function renderSettings() {
  window.cySettingsManager?.renderAccountManager?.();
  window.cySettingsManager?.renderCategoryManager?.();
  els.lockedThrough.value = state.lockedThrough || '';
  els.lockStatusText.textContent = state.lockedThrough ? `目前已鎖帳至 ${formatMonth(state.lockedThrough)}，更早月份也一併鎖定。` : '目前未鎖帳。';
  window.cySyncMobileLockMonthControls?.();
  els.lockStatusText.hidden = Boolean(window.cyIsDesktopInteractionWorkspace?.() && state.lockedThrough);
  els.lockedThrough.dispatchEvent(new Event('change', { bubbles: true }));
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

function accountArchiveMessage() {
  const dialog = document.querySelector('#archivedAccountsDialog');
  return dialog?.open ? dialog.querySelector('#archivedAccountsMessage') : els.settingsMessage;
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
    is_default: 0,
    color_slot: Number(account.color_slot || 0) || null
  }];
  renderAccountSurfaces(account.name);
  setDialogMessage(accountArchiveMessage(), '');

  try {
    await api(`/api/accounts/${id}/restore`, { method: 'POST' });
    await refreshBootstrap();
    setDialogMessage(accountArchiveMessage(), '帳戶已解封。');
  } catch (error) {
    state.accounts = previousAccounts;
    state.archivedAccounts = previousArchived;
    renderAccountSurfaces();
    setDialogMessage(accountArchiveMessage(), error.message || '帳戶解封失敗，已還原。', true);
  }
}

async function permanentlyDeleteArchivedAccount(id) {
  if (!Number.isInteger(id) || id <= 0) return;
  const account = state.archivedAccounts.find(item => Number(item.id) === id);
  if (!account) return;
  const transactions = Number(account.transaction_count || 0);
  const latestOpening = Number(account.latest_opening_amount || 0);
  if (transactions > 0 || latestOpening !== 0) {
    setDialogMessage(accountArchiveMessage(), '此帳戶仍有歷史記帳或目前期初餘額非 0，不能永久刪除。', true);
    return;
  }
  if (!confirm(`永久刪除帳戶「${account.name}」？\n此操作無法復原。`)) return;

  try {
    await api(`/api/accounts/${id}/permanent`, { method: 'DELETE' });
    await refreshBootstrap();
    setDialogMessage(accountArchiveMessage(), '帳戶已永久刪除。');
  } catch (error) {
    setDialogMessage(accountArchiveMessage(), error.message || '帳戶永久刪除失敗。', true);
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
  if (ok && els.newAccountName.value.trim() === name) els.newAccountName.value = '';
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
  const ok = await optimisticAddSettingsGroup(name);
  if (ok && els.newGroupName.value.trim() === name) els.newGroupName.value = '';
}

async function addCategory(groupId, input) {
  const name = input?.value.trim();
  if (!name || !Number.isInteger(groupId) || groupId <= 0) return;
  const ok = await optimisticAddSettingsCategory(name, groupId);
  if (ok && input?.value.trim() === name) input.value = '';
}

function renderSettingsMutationState(account = els.accountName.value, category = els.categoryName.value) {
  renderAccounts(account);
  renderCategories(category);
  renderSettings();
  if (typeof renderFavoriteCategories === 'function') renderFavoriteCategories();
  if (typeof syncEntryUiAccountChoices === 'function') syncEntryUiAccountChoices();
}

function beginSettingsOptimisticMutation(path, options) {
  const body = options.body ? JSON.parse(options.body) : {};
  const method = options.method || 'GET';
  const match = path.match(/^\/api\/(accounts|category-groups|categories)\/(\d+)(?:\/(default|favorite))?$/);
  const accountCreate = path === '/api/accounts' && method === 'POST';
  if (!accountCreate && !(match && ((method === 'PUT' && !match[3]) || (match[3] === 'default' && method === 'POST') || (match[3] === 'favorite' && method === 'PUT')))) return null;
  const key = match?.[3] === 'default' ? 'account-default' : path;
  if (cyPendingSettingsMutations.has(key)) throw new Error('此項設定仍在儲存中。');
  const account = els.accountName.value;
  const category = els.categoryName.value;
  const accountId = state.accounts.find(item => item.name === account)?.id;
  const categoryId = state.categories.find(item => item.name === category)?.id;
  let rollback;
  let commit = () => {};
  if (accountCreate) {
    const name = String(body.name || '').trim().replace(/\s+/g, ' ');
    if (!name || Array.from(name).length > 8) throw new Error('帳戶名稱需為 1～8 個字。');
    const id = cySettingsTempId--;
    state.accounts = [...state.accounts, { id, name, sort_order: Math.max(-1, ...state.accounts.map(item => Number(item.sort_order || 0))) + 1, is_default: 0 }];
    rollback = () => { state.accounts = state.accounts.filter(item => item.id !== id); };
    commit = result => {
      const item = state.accounts.find(item => item.id === id);
      if (!item) return;
      item.id = Number(result.id);
      item.color_slot = Number(result.color_slot || 0) || null;
    };
  } else {
    const list = match[1] === 'accounts' ? 'accounts' : match[1] === 'categories' ? 'categories' : 'groups';
    const id = Number(match[2]);
    const original = state[list].find(item => Number(item.id) === id);
    if (!original) throw new Error('找不到此項設定。');
    const field = match[3] === 'default' ? 'is_default' : match[3] === 'favorite' ? 'is_favorite' : 'name';
    const before = new Map(state[list].filter(item => field === 'is_default' || Number(item.id) === id).map(item => [Number(item.id), item[field]]));
    const value = field === 'name' ? String(body.name || '').trim().replace(/\s+/g, ' ') : body.favorite ? 1 : 0;
    if (field === 'name' && (!value || (list === 'accounts' && Array.from(value).length > 8))) throw new Error('名稱不可空白，帳戶名稱最多 8 個字。');
    state[list] = state[list].map(item => field === 'is_default' ? { ...item, is_default: Number(item.id) === id ? 1 : 0 } : Number(item.id) === id ? { ...item, [field]: value } : item);
    if (list === 'groups' && field === 'name') state.categories = state.categories.map(item => Number(item.group_id) === id ? { ...item, group_name: value } : item);
    rollback = () => {
      state[list] = state[list].map(item => before.has(Number(item.id)) ? { ...item, [field]: before.get(Number(item.id)) } : item);
      if (list === 'groups' && field === 'name') state.categories = state.categories.map(item => Number(item.group_id) === id ? { ...item, group_name: before.get(id) } : item);
    };
  }
  cyPendingSettingsMutations.add(key);
  return { key, rollback, commit, account, category, preferredAccount: state.accounts.find(item => item.id === accountId)?.name || account, preferredCategory: state.categories.find(item => item.id === categoryId)?.name || category };
}

async function mutateSettings(path, options, successMessage) {
  setDialogMessage(els.settingsMessage, '');
  let optimistic;
  try {
    optimistic = beginSettingsOptimisticMutation(path, options);
    if (optimistic) renderSettingsMutationState(optimistic.preferredAccount, optimistic.preferredCategory);
    const result = await api(path, options);
    if (optimistic) {
      optimistic.commit(result);
      renderSettingsMutationState();
    } else {
      await refreshBootstrap();
      await loadTransactions();
    }
    window.dispatchEvent(new CustomEvent('cyacc:master-data-changed'));
    setDialogMessage(els.settingsMessage, successMessage);
    return true;
  } catch (error) {
    if (optimistic) {
      optimistic.rollback();
      renderSettingsMutationState(els.accountName.value === optimistic.preferredAccount ? optimistic.account : els.accountName.value, els.categoryName.value === optimistic.preferredCategory ? optimistic.category : els.categoryName.value);
    }
    setDialogMessage(els.settingsMessage, error.message, true);
    return false;
  } finally {
    if (optimistic) cyPendingSettingsMutations.delete(optimistic.key);
  }
}

function openingAccountRowHtml(item, audit = []) {
  const manual = item.source === 'override';
  const badge = manual ? '<span class="opening-source manual">調整</span>' : '';
  const amount = cyOpeningDesktopPresentation && !cyOpeningManualEdit
    ? `<span class="opening-amount">${Number(item.amount || 0).toLocaleString('zh-TW')}</span>`
    : `<input type="number" step="1" value="${Number(item.amount) || 0}"
      aria-label="${escapeHtml(item.name)}期初餘額"
      data-opening-account="${escapeHtml(item.name)}"
      data-opening-automatic="${Number(item.automaticAmount) || 0}"${cyOpeningManualEdit ? '' : ' disabled'}>`;
  const row = `<label class="opening-row" data-opening-source="${manual ? 'manual' : 'automatic'}">
    <span class="opening-account-copy">
      <span class="opening-account-title">${badge}<strong>${escapeHtml(item.name)}</strong></span>
    </span>
    ${amount}
  </label>`;
  return cyOpeningDesktopPresentation
    ? `<section class="opening-account-entry">${row}${renderOpeningAccountAudit(item, audit)}</section>` : row;
}

function openingAccountAuditEntryHtml(entry) {
  const date = entry.createdAt ? new Date(entry.createdAt).toLocaleString('zh-TW', {
    timeZone: 'Asia/Taipei', year: 'numeric', month: '2-digit', day: '2-digit',
    hour: '2-digit', minute: '2-digit', hour12: false
  }) : '';
  const actor = [entry.actorEmployeeNo, entry.actorName].filter(Boolean).join(' ');
  const action = entry.action === 'clear' ? '恢復自動 · ' : entry.action === 'migration' ? '舊資料移轉 · ' : '';
  const amount = `${entry.previousAmount === null ? '—' : money(entry.previousAmount)} → ${entry.newAmount === null ? '—' : money(entry.newAmount)}`;
  return `<div class="opening-account-audit-meta"><time>${escapeHtml(date)}</time>${actor ? ` · ${escapeHtml(actor)}` : ''}
    <span class="opening-account-audit-change">${escapeHtml(action + amount)}</span></div>
    ${entry.reason ? `<p class="opening-account-audit-reason">${escapeHtml(entry.reason)}</p>` : ''}`;
}

function renderOpeningAccountAudit(item, audit) {
  const rows = audit.filter(entry => entry.accountName === item.name);
  const latest = rows[0] || (item.source === 'override' ? {
    action: 'set', previousAmount: null, newAmount: item.amount, reason: item.overrideReason,
    createdAt: item.overrideUpdatedAt, actorEmployeeNo: item.overrideUpdatedByEmployeeNo,
    actorName: item.overrideUpdatedByName
  } : null);
  if (!latest) return '';
  // The initial shared response contains at most 30 monthly records. Only an
  // explicit expansion needs the existing account-specific audit endpoint.
  const truncated = audit.length >= 30;
  const history = rows.length > 1 || truncated
    ? `<details class="opening-account-history"${truncated ? ` data-opening-history-account="${escapeHtml(item.name)}"` : ''}>
      <summary>查看全部紀錄</summary><div class="opening-account-history-list">${rows.slice(1).map(openingAccountAuditEntryHtml).join('')}</div></details>` : '';
  return `<div class="opening-account-audit"><div class="opening-account-audit-latest">${openingAccountAuditEntryHtml(latest)}</div>${history}</div>`;
}

function renderOpeningBalanceRows() {
  const data = state.openingData;
  const rows = data.accounts.map(item => openingAccountRowHtml(item, data.audit || [])).join('');
  els.openingRows.innerHTML = `<div class="opening-account-column">${rows}
    <section id="openingManualPanel" class="opening-manual-panel"${cyOpeningManualEdit ? '' : ' hidden'}>
      <label class="opening-reason-field">
        <span>手動調整理由 <strong>必填</strong></span>
        <textarea id="openingAdjustmentReason" maxlength="200" rows="2" placeholder="例如：銀行對帳差異調整"></textarea>
      </label>
      ${cyOpeningDesktopPresentation ? '' : `<div class="opening-manual-actions">
        <button id="cancelOpeningManualButton" class="secondary" type="button">取消手動調整</button>
      </div>`}
    </section></div>${cyOpeningDesktopPresentation ? '' : renderOpeningAudit(data.audit || [])}`;
  document.querySelector('#cancelOpeningManualButton')?.addEventListener('click', cancelOpeningManualEdit);
  els.openingDialog?.querySelector('#openingCloseButton')?.replaceChildren(cyOpeningDesktopPresentation && cyOpeningManualEdit ? '取消' : '關閉');
  els.openingRows.querySelectorAll('[data-opening-history-account]').forEach(details => {
    details.addEventListener('toggle', async () => {
      if (!details.open || details.dataset.loaded || details.dataset.loading) return;
      details.dataset.loading = '1';
      const list = details.querySelector('.opening-account-history-list');
      list.textContent = '讀取中…';
      try {
        const result = await api(`/api/opening-balance-audit?month=${encodeURIComponent(data.month || els.openingMonth.value)}&account=${encodeURIComponent(details.dataset.openingHistoryAccount)}`);
        if (!details.isConnected) return;
        const entries = result.entries || [];
        const latest = details.parentElement.querySelector('.opening-account-audit-latest');
        if (entries[0]) latest.innerHTML = openingAccountAuditEntryHtml(entries[0]);
        list.innerHTML = entries.slice(1).map(openingAccountAuditEntryHtml).join('') || '<p>沒有其他調整紀錄。</p>';
        if (entries.length === 100) list.insertAdjacentHTML('beforeend', '<p>顯示最近 100 筆紀錄。</p>');
        details.dataset.loaded = '1';
      } catch (error) { list.textContent = error.message || '紀錄讀取失敗，請重新展開再試。'; }
      finally { delete details.dataset.loading; }
    });
  });
}

async function loadOpeningBalances() {
  const month = els.openingMonth.value;
  if (!month) return;
  cyOpeningManualEdit = false;
  cyOpeningDesktopPresentation = Boolean(window.cyIsDesktopInteractionWorkspace?.());
  if (els.openingDialog) {
    els.openingDialog.dataset.openingPresentation = cyOpeningDesktopPresentation ? 'desktop' : 'touch';
    const title = els.openingDialog.querySelector('.modal-header h2');
    if (title && cyOpeningDesktopPresentation) title.textContent = `${month.replace('-', '/')} 期初餘額`;
    const close = els.openingDialog.querySelector('#openingCloseButton');
    if (close) close.textContent = '關閉';
  }
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
    renderOpeningBalanceRows();
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
    if (cyOpeningDesktopPresentation) renderOpeningBalanceRows();
    else els.openingRows.querySelectorAll('[data-opening-account]').forEach(input => { input.disabled = false; });
    const panel = document.querySelector('#openingManualPanel');
    if (panel) panel.hidden = false;
    els.saveOpeningButton.textContent = '儲存調整';
    setDialogMessage(els.openingMessage, cyOpeningDesktopPresentation ? '' : '手動調整會留下理由、操作者與時間；未修改的帳戶不會寫入。');
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
    await loadOpeningBalances();
    setDialogMessage(els.openingMessage, '手動調整已儲存並留下調整紀錄。');
    if (typeof scheduleLedgerRefresh === 'function') scheduleLedgerRefresh();
  } catch (error) {
    setDialogMessage(els.openingMessage, error.message, true);
  } finally {
    if (!state.openingData?.locked) els.saveOpeningButton.disabled = false;
  }
}

function cancelOpeningManualEdit() {
  cyOpeningManualEdit = false;
  if (cyOpeningDesktopPresentation && state.openingData?.accounts?.length) renderOpeningBalanceRows();
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
    return `<details class="opening-audit"><summary>調整紀錄</summary><div class="opening-audit-empty">目前沒有手動調整紀錄。</div></details>`;
  }
  const items = rows.slice(0, 12).map(entry => {
    const action = entry.action === 'clear' ? '恢復自動' : entry.action === 'migration' ? '舊資料移轉' : '手動設定';
    const actor = [entry.actorEmployeeNo, entry.actorName].filter(Boolean).join(' ');
    const amount = `${entry.previousAmount === null ? '—' : money(entry.previousAmount)} → ${entry.newAmount === null ? '—' : money(entry.newAmount)}`;
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
    window.cySyncLedgerQuickLock?.();
    setDialogMessage(els.settingsMessage, state.lockedThrough ? `已鎖帳至 ${formatMonth(state.lockedThrough)}。` : '已取消鎖帳。');
    await loadTransactions();
    return true;
  } catch (error) {
    setDialogMessage(els.settingsMessage, error.message, true);
    return false;
  }
}
window.cyaccSaveLock = saveLock;


function isLocked(month) {
  return Boolean(state.lockedThrough && /^\d{4}-\d{2}$/.test(month) && month <= state.lockedThrough);
}

function numericInput(event) {
  event.target.value = event.target.value.replace(/\D/g, '').slice(0, 7);
}

window.cyaccApi = api;

// Only share concurrent reads in this browser document. Settled responses are
// never cached, and any attempted write invalidates reads that started before it.
const cyAccountingReadFlights = new Map();
let cyAccountingReadRevision = 0;

function invalidateAccountingReads() {
  cyAccountingReadRevision += 1;
  cyAccountingReadFlights.clear();
}

async function api(path, options = {}, timeoutMs = 12_000, readAttempt = 0) {
  const method = String(options.method || 'GET').toUpperCase();
  const shareRead = method === 'GET' && Object.keys(options).length === 0
    && /^\/api\/(?:opening-balances|summaries\/frequent)\?/.test(path);
  if (shareRead) {
    const epoch = window.cyaccBookEpoch || '';
    const key = `${epoch}:${timeoutMs}:${path}`;
    if (cyAccountingReadFlights.has(key)) return cyAccountingReadFlights.get(key);
    const revision = cyAccountingReadRevision;
    const pending = requestApi(path, options, timeoutMs).then(data => {
      if (revision === cyAccountingReadRevision && epoch === (window.cyaccBookEpoch || '')) return data;
      if (readAttempt >= 1) throw new Error('資料已更新，請重新整理後再試。');
      return api(path, options, timeoutMs, readAttempt + 1);
    }).finally(() => {
      if (cyAccountingReadFlights.get(key) === pending) cyAccountingReadFlights.delete(key);
    });
    cyAccountingReadFlights.set(key, pending);
    return pending;
  }
  try {
    return await requestApi(path, options, timeoutMs);
  } finally {
    if (!['GET', 'HEAD'].includes(method)) invalidateAccountingReads();
  }
}

async function requestApi(path, options, timeoutMs) {
  const controller = new AbortController();
  let timeoutId;
  const request = Promise.resolve().then(async () => {
    const response = await fetch(path, {
      ...options,
      headers:{...options.headers,'x-cyacc-book-epoch':window.cyaccBookEpoch||''},
      credentials: 'include',
      cache: 'no-store',
      signal: controller.signal
    });
    const data = await response.json().catch(() => ({}));
    if (window.cyaccHandleAuthResponse?.(response, data)) throw new Error('AUTH_REQUIRED');
    if(data.code==='ACCOUNTING_BOOK_CHANGED') window.location.reload();
    if (!response.ok || data.ok === false) throw new Error(data.error || `HTTP ${response.status}`);
    return data;
  });
  const timeout = new Promise((_, reject) => {
    timeoutId = window.setTimeout(() => {
      controller.abort();
      const error = new Error('CYACC_BROWSER_TIMEOUT');
      error.name = 'TimeoutError';
      reject(error);
    }, timeoutMs);
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
  els.saveMessage.title = text;
  els.saveMessage.classList.toggle('error', isError);
  window.cyAfterSaveMessage?.(els.saveMessage, text, isError);
  window.dispatchEvent(new CustomEvent('cyacc:entry-message', { detail: { text, isError } }));
}
function setDialogMessage(element, text, isError = false) { element.textContent = text; element.classList.toggle('error', isError); }
function money(value) { return `$${Number(value || 0).toLocaleString('zh-TW')}`; }
function formatMonth(value) { const [year, month] = String(value || '').split('-'); return year && month ? `${year}年${month}月` : ''; }
function localDateString(date) { const y = date.getFullYear(), m = String(date.getMonth() + 1).padStart(2, '0'), d = String(date.getDate()).padStart(2, '0'); return `${y}-${m}-${d}`; }
function escapeHtml(value) { return String(value ?? '').replaceAll('&', '&amp;').replaceAll('<', '&lt;').replaceAll('>', '&gt;').replaceAll('"', '&quot;').replaceAll("'", '&#039;'); }
