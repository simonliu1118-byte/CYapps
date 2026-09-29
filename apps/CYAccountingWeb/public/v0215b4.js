const CY_V0215_BUILD4_MOBILE = '(max-width: 767px)';
let cyV0215Build4Edit = null;
let cyV0215Build4SaveHideTimer = null;
let cyV0215Build4SaveClearTimer = null;
let cyV0215Build4SetupDone = false;
let cyV0215MobileBalancePopover = null;
let cyV0215MobileBalanceAnchor = null;

window.cyAfterSaveMessage = handleV0215Build4SaveMessage;
window.cyOpenMobileLedgerBalance = openV0215MobileLedgerBalance;
window.cyOpenMobileLedgerLock = openV0215MobileLedgerLock;

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
      openV0215MobileLedgerBalance();
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

function ensureV0215MobileBalancePopover() {
  if (cyV0215MobileBalancePopover?.isConnected) return cyV0215MobileBalancePopover;
  const popover = document.createElement('div');
  popover.id = 'mobileLedgerBalancePopoverV0215';
  popover.className = 'v0215-mobile-balance-popover';
  popover.setAttribute('role', 'dialog');
  popover.setAttribute('aria-label', '各帳戶餘額');
  popover.hidden = true;
  document.body.append(popover);
  cyV0215MobileBalancePopover = popover;

  document.addEventListener('click', event => {
    if (popover.hidden) return;
    if (popover.contains(event.target) || cyV0215MobileBalanceAnchor?.contains(event.target)) return;
    closeV0215MobileLedgerBalance();
  }, true);
  window.addEventListener('resize', closeV0215MobileLedgerBalance);
  window.addEventListener('scroll', closeV0215MobileLedgerBalance, true);
  return popover;
}

async function openV0215MobileLedgerBalance() {
  if (!window.matchMedia(CY_V0215_BUILD4_MOBILE).matches) return;
  const anchor = document.querySelector('#mobileLedgerBalanceButton');
  const month = String(els.monthFilter?.value || '');
  if (!anchor || !/^\d{4}-\d{2}$/.test(month)) return;

  const popover = ensureV0215MobileBalancePopover();
  cyV0215MobileBalanceAnchor = anchor;
  popover.innerHTML = '<div class="v0215-mobile-balance-title">各帳戶餘額</div><div class="v0215-mobile-balance-loading">讀取中…</div>';
  popover.hidden = false;
  positionV0215MobileBalancePopover(anchor, popover);

  try {
    let opening = cyLedgerOpeningData;
    if (!opening || opening.month !== month) {
      opening = await api('/api/opening-balances?month=' + encodeURIComponent(month));
      if (month !== String(els.monthFilter?.value || '')) return closeV0215MobileLedgerBalance();
      cyLedgerOpeningData = opening;
    }

    const openingMap = new Map();
    for (const item of opening?.accounts || []) {
      const value = item.amount === null || item.amount === undefined || item.amount === '' ? 0 : Number(item.amount);
      openingMap.set(String(item.name || ''), Number.isFinite(value) ? value : 0);
    }
    const calculated = calculateLedgerBalances(Array.isArray(state.transactions) ? state.transactions : [], openingMap);
    const names = (state.accounts || []).map(account => String(account.name || '')).filter(Boolean);

    popover.replaceChildren();
    const title = document.createElement('div');
    title.className = 'v0215-mobile-balance-title';
    title.textContent = '各帳戶餘額';
    popover.append(title);

    const list = document.createElement('div');
    list.className = 'v0215-mobile-balance-list';
    if (!names.length) {
      const empty = document.createElement('div');
      empty.className = 'v0215-mobile-balance-empty';
      empty.textContent = '尚無帳戶';
      list.append(empty);
    } else {
      for (const name of names) {
        const row = document.createElement('div');
        row.className = 'v0215-mobile-balance-row';
        const label = document.createElement('span');
        label.textContent = name;
        const value = document.createElement('strong');
        value.textContent = money(calculated.endingByAccount.get(name) ?? openingMap.get(name) ?? 0);
        row.append(label, value);
        list.append(row);
      }
    }
    popover.append(list);
    positionV0215MobileBalancePopover(anchor, popover);
  } catch (error) {
    popover.innerHTML = '<div class="v0215-mobile-balance-title">各帳戶餘額</div><div class="v0215-mobile-balance-error"></div>';
    const target = popover.querySelector('.v0215-mobile-balance-error');
    if (target) target.textContent = error?.message || '餘額讀取失敗。';
    positionV0215MobileBalancePopover(anchor, popover);
  }
}

function positionV0215MobileBalancePopover(anchor, popover) {
  if (!anchor || !popover || popover.hidden) return;
  const anchorBox = anchor.getBoundingClientRect();
  const popoverBox = popover.getBoundingClientRect();
  const left = Math.max(8, Math.min(anchorBox.left, window.innerWidth - popoverBox.width - 8));
  const top = Math.min(anchorBox.bottom + 6, window.innerHeight - popoverBox.height - 8);
  popover.style.left = Math.round(left) + 'px';
  popover.style.top = Math.round(Math.max(8, top)) + 'px';
}

function closeV0215MobileLedgerBalance() {
  cyV0215MobileBalanceAnchor = null;
  if (cyV0215MobileBalancePopover) cyV0215MobileBalancePopover.hidden = true;
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

  closeV0215MobileLedgerBalance();
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
