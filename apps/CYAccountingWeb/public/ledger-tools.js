/* CYAccountingWeb ledger tools functional module. */

let cyLedgerGroupByAccount = false;
let cyLedgerSearch = '';
let cyLedgerOpeningData = null;
let cyLedgerRequestId = 0;
let cyLedgerRenderedMonth = '';
window.cyLedgerBalanceBreakdowns = new Map();
window.cyaccRefreshLedgerView = loadLedgerOpeningAndRender;
window.cyaccRenderLedgerMessage = renderLedgerMessage;

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
}

function scheduleLedgerDesktopRefresh() {
  void loadLedgerOpeningAndRender();
}

async function loadLedgerOpeningAndRender(expectedMonth = els.monthFilter?.value) {
  const month = String(expectedMonth || '');
  if (!month || month !== els.monthFilter?.value) return;
  if (month !== cyLedgerRenderedMonth) {
    cyLedgerRenderedMonth = month;
    cyLedgerOpeningData = null;
    cyLedgerGroupByAccount = false;
  }
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
    renderLedgerMessage(text, query ? 'search-empty' : 'empty');
    return;
  }

  const rowsHtml = cyLedgerGroupByAccount
    ? renderGroupedLedgerRows(visible, allTransactions, openingMap, calculated)
    : visible.map(tx => renderLedgerRow(
        tx,
        calculated.globalById.get(Number(tx.id)) ?? 0,
        calculated.balancesById.get(Number(tx.id)) || new Map(),
        false
      )).join('');
  writeLedgerRows(rowsHtml, {
    reason: 'transactions',
    month,
    visibleCount: visible.length,
    totalCount: allTransactions.length
  });
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

function renderLedgerMessage(message, reason = 'status') {
  writeLedgerRows(
    `<tr><td colspan="8" class="empty"><div class="ledger-empty-state"><strong>${escapeHtml(String(message || ''))}</strong></div></td></tr>`,
    { reason }
  );
}

function writeLedgerRows(html, detail = {}) {
  if (!els.transactionRows) return;
  els.transactionRows.innerHTML = html;
  window.dispatchEvent(new CustomEvent('cyacc:ledger-rendered', {
    detail: {
      month: els.monthFilter?.value || '',
      ...detail
    }
  }));
}