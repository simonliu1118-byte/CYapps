let cyLedgerGroupByAccount = false;
let cyLedgerSearch = '';
let cyLedgerOpeningData = null;
let cyLedgerRefreshTimer = null;
let cyLedgerObserver = null;
let cyLedgerRequestId = 0;

window.addEventListener('load', () => {
  setupLedgerDesktopTools();
  bindLedgerDesktopTools();
  observeLedgerRefreshes();
  scheduleLedgerDesktopRefresh();
});

function setupLedgerDesktopTools() {
  const card = document.querySelector('.ledger-card');
  const title = card?.querySelector('.ledger-title');
  const monthPicker = title?.querySelector('.month-picker');
  if (!card || !title || !monthPicker || document.querySelector('#ledgerDesktopTools')) return;

  const tools = document.createElement('div');
  tools.id = 'ledgerDesktopTools';
  tools.className = 'ledger-desktop-tools';
  tools.innerHTML = `
    <div class="ledger-month-tools">
      <button id="ledgerPrevMonth" class="secondary compact" type="button" title="上一個月">‹</button>
      <div id="ledgerMonthSlot"></div>
      <button id="ledgerNextMonth" class="secondary compact" type="button" title="下一個月">›</button>
      <span id="ledgerDisplayMonth" class="ledger-display-month"></span>
    </div>
    <form id="ledgerSearchForm" class="ledger-search" role="search">
      <input id="ledgerSummarySearch" type="search" maxlength="100" placeholder="搜尋摘要">
      <button class="secondary compact" type="submit">搜尋</button>
      <button id="ledgerSearchClear" class="secondary compact" type="button">清除</button>
    </form>
    <button id="ledgerGroupToggle" class="secondary compact" type="button" aria-pressed="false">帳戶分組</button>
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
  document.querySelector('#ledgerGroupToggle')?.addEventListener('click', () => {
    cyLedgerGroupByAccount = !cyLedgerGroupByAccount;
    updateLedgerGroupButton();
    renderDesktopLedger();
  });
  els.monthFilter?.addEventListener('change', () => {
    cyLedgerOpeningData = null;
    scheduleLedgerDesktopRefresh();
  });
}

function observeLedgerRefreshes() {
  if (!els.transactionRows) return;
  cyLedgerObserver = new MutationObserver(() => scheduleLedgerDesktopRefresh());
  cyLedgerObserver.observe(els.transactionRows, { childList: true, subtree: true });
}

function scheduleLedgerDesktopRefresh() {
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

  const calculated = calculateLedgerBalances(allTransactions, openingMap);
  const income = allTransactions.reduce((sum, tx) => sum + (tx.kind === 'income' ? Number(tx.amount) || 0 : 0), 0);
  const expense = allTransactions.reduce((sum, tx) => sum + (tx.kind === 'expense' ? Number(tx.amount) || 0 : 0), 0);
  const openingTotal = [...openingMap.values()].reduce((sum, value) => sum + value, 0);
  const endingTotal = openingTotal + income - expense;

  const query = cyLedgerSearch.toLocaleLowerCase('zh-Hant');
  const visible = query
    ? allTransactions.filter(tx => String(tx.summary || '').toLocaleLowerCase('zh-Hant').includes(query))
    : [...allTransactions];

  els.monthSummary.textContent = `期初 ${money(openingTotal)}　收入 ${money(income)}　支出 ${money(expense)}　淨利損 ${money(income - expense)}　期末 ${money(endingTotal)}${query ? `　｜搜尋顯示 ${visible.length}/${allTransactions.length} 筆` : ''}`;
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
    writeLedgerRows(visible.map(tx => renderLedgerRow(tx, calculated.globalById.get(Number(tx.id)) ?? 0)).join(''));
  }
}

function calculateLedgerBalances(transactions, openingMap) {
  const chronological = [...transactions].sort(compareLedgerChronological);
  const accountBalances = new Map(openingMap);
  let globalBalance = [...openingMap.values()].reduce((sum, value) => sum + value, 0);
  const globalById = new Map();
  const accountById = new Map();

  for (const tx of chronological) {
    const account = String(tx.account_name || '');
    const amount = Number(tx.amount) || 0;
    const direction = tx.kind === 'income' ? 1 : -1;
    const nextAccount = (accountBalances.get(account) || 0) + direction * amount;
    globalBalance += direction * amount;
    accountBalances.set(account, nextAccount);
    globalById.set(Number(tx.id), globalBalance);
    accountById.set(Number(tx.id), nextAccount);
  }
  return { globalById, accountById, endingByAccount: accountBalances };
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
  const currentOrder = new Map((state.accounts || []).map((account, index) => [account.name, index]));
  const names = [...new Set(visible.map(tx => String(tx.account_name || '')))].sort((a, b) => {
    const ai = currentOrder.has(a) ? currentOrder.get(a) : Number.MAX_SAFE_INTEGER;
    const bi = currentOrder.has(b) ? currentOrder.get(b) : Number.MAX_SAFE_INTEGER;
    return ai - bi || a.localeCompare(b, 'zh-Hant');
  });

  return names.map(name => {
    const rows = visible.filter(tx => tx.account_name === name).sort(compareLedgerChronological);
    const opening = openingMap.get(name) || 0;
    const ending = calculated.endingByAccount.get(name) ?? opening;
    const heading = `<tr class="account-group-row"><td colspan="8"><strong>${escapeHtml(name)}</strong><span>期初 ${money(opening)}　期末 ${money(ending)}</span></td></tr>`;
    return heading + rows.map(tx => renderLedgerRow(tx, calculated.accountById.get(Number(tx.id)) ?? 0)).join('');
  }).join('');
}

function renderLedgerRow(tx, balance) {
  const locked = isLocked(String(tx.tx_date || '').slice(0, 7));
  return `<tr>
    <td>${escapeHtml(String(tx.tx_date || '').replaceAll('-', '/'))}</td>
    <td>${escapeHtml(tx.account_name)}</td>
    <td><span class="kind-tag ${tx.kind}">${tx.kind === 'income' ? '收入' : '支出'}</span></td>
    <td>${escapeHtml(tx.category_name)}</td>
    <td class="summary">${escapeHtml(tx.summary || '')}</td>
    <td class="num">${money(tx.amount)}</td>
    <td class="num ledger-balance">${money(balance)}</td>
    <td class="action-col"><button type="button" class="row-action" data-edit-id="${tx.id}" ${locked ? 'disabled' : ''}>編輯</button><button type="button" class="row-action delete" data-delete-id="${tx.id}" ${locked ? 'disabled' : ''}>刪除</button></td>
  </tr>`;
}

function updateLedgerHeader() {
  const row = document.querySelector('.ledger-card thead tr');
  if (!row) return;
  row.innerHTML = '<th>日期</th><th id="ledgerAccountHeader" class="ledger-account-header" title="點擊切換帳戶分組">帳戶</th><th>收支</th><th>科目</th><th>摘要</th><th class="num">金額</th><th class="num">餘額</th><th class="action-col">操作</th>';
  row.querySelector('#ledgerAccountHeader')?.addEventListener('click', () => {
    cyLedgerGroupByAccount = !cyLedgerGroupByAccount;
    updateLedgerGroupButton();
    renderDesktopLedger();
  });
}

function updateLedgerGroupButton() {
  const button = document.querySelector('#ledgerGroupToggle');
  if (!button) return;
  button.classList.toggle('active', cyLedgerGroupByAccount);
  button.setAttribute('aria-pressed', cyLedgerGroupByAccount ? 'true' : 'false');
  button.textContent = cyLedgerGroupByAccount ? '帳戶分組：開' : '帳戶分組';
}

function writeLedgerRows(html) {
  if (!els.transactionRows) return;
  cyLedgerObserver?.disconnect();
  els.transactionRows.innerHTML = html;
  cyLedgerObserver?.observe(els.transactionRows, { childList: true, subtree: true });
}
