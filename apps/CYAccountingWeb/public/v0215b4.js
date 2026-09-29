const CY_V0215_BUILD4_MOBILE = '(max-width: 767px)';
let cyV0215Build4Edit = null;
let cyV0215Build4SaveHideTimer = null;
let cyV0215Build4SaveClearTimer = null;
let cyV0215Build4SetupDone = false;

window.cyAfterSaveMessage = handleV0215Build4SaveMessage;

if (document.readyState === 'loading') {
  document.addEventListener('DOMContentLoaded', setupV0215Build4, { once: true });
} else {
  window.setTimeout(setupV0215Build4, 0);
}
window.addEventListener('load', setupV0215Build4, { once: true });

function setupV0215Build4() {
  if (cyV0215Build4SetupDone) return;
  if (!document.querySelector('#transactionRows') || !document.querySelector('#transactionForm') || !document.querySelector('#mobileMainNav')) return;
  cyV0215Build4SetupDone = true;
  setupV0215Build4Toolbar();
  setupV0215Build4Search();
  setupV0215Build4SaveMessage();
  setupV0215Build4MobileEdit();
}

function setupV0215Build4Toolbar() {
  if (!window.matchMedia(CY_V0215_BUILD4_MOBILE).matches) return;
  const monthTools = document.querySelector('.ledger-month-tools');
  const prev = document.querySelector('#ledgerPrevMonth');
  const next = document.querySelector('#ledgerNextMonth');
  const more = document.querySelector('#mobileLedgerMoreButton');
  if (!monthTools || !prev || !next || !more) return;

  let balance = document.querySelector('#mobileLedgerBalanceButton');
  if (!balance) {
    balance = document.createElement('button');
    balance.id = 'mobileLedgerBalanceButton';
    balance.className = 'secondary compact v0215-mobile-balance-button';
    balance.type = 'button';
    balance.textContent = '餘額';
    balance.addEventListener('click', () => {
      document.querySelector('#ledgerOpeningBalanceButton')?.click();
    });
  }

  monthTools.insertBefore(balance, prev);
  monthTools.append(more);

  const sheet = document.querySelector('#mobileLedgerToolsSheet');
  sheet?.querySelector('[data-mobile-ledger-action="opening"]')?.remove();
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
    const context = cyV0215Build4Edit.returnContext;
    cancelV0215Build4MobileEdit();
    switchV0215Build4MobilePage('ledger');
    restoreV0215Build4LedgerContext(context, false);
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
