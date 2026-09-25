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
