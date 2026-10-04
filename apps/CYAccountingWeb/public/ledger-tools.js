/* CYAccountingWeb ledger tools functional module. */

let cyLedgerGroupByAccount = false;
let cyLedgerSearch = '';
let cyLedgerOpeningData = null;
let cyLedgerRequestId = 0;
let cyLedgerRenderedMonth = '';
window.cyLedgerBalanceBreakdowns = new Map();
window.cyaccRefreshLedgerView = loadLedgerOpeningAndRender;
window.cyaccRenderLedgerMessage = renderLedgerMessage;

let cyLedgerToolsStarted = false;

if (document.readyState === 'loading') {
  document.addEventListener('DOMContentLoaded', startLedgerTools, { once: true });
} else {
  startLedgerTools();
}
window.addEventListener('load', startLedgerTools, { once: true });

function startLedgerTools() {
  if (cyLedgerToolsStarted) return;
  if (!document.querySelector('.ledger-card') || !document.querySelector('#monthFilter')) return;
  cyLedgerToolsStarted = true;
  setupLedgerToolbar();
  bindLedgerToolbar();
}

function setupLedgerToolbar() {
  const card = document.querySelector('.ledger-card');
  const title = card?.querySelector('.ledger-title');
  const titleMain = title?.firstElementChild;
  const monthPicker = title?.querySelector('.month-picker');
  const summary = document.querySelector('#monthSummary');
  if (!card || !title || !titleMain || !monthPicker || !summary || document.querySelector('#ledgerToolbar')) return;

  const context = document.createElement('div');
  context.className = 'cy-ledger-context';
  context.innerHTML = `
    <div class="ledger-month-tools">
      <button id="ledgerBalanceButton" class="secondary compact cy-ledger-balance-button" type="button">餘額</button>
      <button id="ledgerPrevMonth" class="secondary compact" type="button" title="上一個月">‹</button>
      <div id="ledgerMonthSlot"><span id="ledgerMonthDisplay" class="cy-mobile-month-display" aria-hidden="true"></span></div>
      <button id="ledgerNextMonth" class="secondary compact" type="button" title="下一個月">›</button>
      <button id="ledgerMoreButton" class="secondary compact cy-ledger-more-button" type="button" aria-haspopup="true" aria-expanded="false">更多</button>
      <span id="ledgerDisplayMonth" class="ledger-display-month"></span>
    </div>`;
  titleMain.insertBefore(context, summary);
  context.querySelector('#ledgerMonthSlot')?.prepend(monthPicker);

  const summaryBar = document.createElement('div');
  summaryBar.className = 'cy-ledger-summary-bar';
  summaryBar.append(summary);

  const summaryActions = document.createElement('div');
  summaryActions.className = 'cy-summary-actions';
  summaryActions.innerHTML = `
    <button id="ledgerOpeningBalanceButton" class="secondary compact ledger-tool-button emphasis" type="button">期初餘額</button>
    <button id="ledgerLockSettingsButton" class="secondary compact" type="button" title="開啟月份鎖帳設定">鎖定月份</button>`;
  summaryBar.append(summaryActions);
  context.insertAdjacentElement('afterend', summaryBar);

  const toolbar = document.createElement('div');
  toolbar.id = 'ledgerToolbar';
  toolbar.className = 'ledger-toolbar';
  toolbar.innerHTML = `
    <form id="ledgerSearchForm" class="ledger-search" role="search">
      <span class="ledger-search-icon" aria-hidden="true">⌕</span>
      <input id="ledgerSummarySearch" type="search" maxlength="100" placeholder="搜尋摘要" autocomplete="off" inputmode="search" enterkeyhint="search">
      <button class="secondary compact ledger-search-submit" type="submit">搜尋</button>
      <button id="ledgerSearchClear" class="secondary compact" type="button" aria-label="清除搜尋"><span class="ledger-search-clear-desktop">清除</span><span class="ledger-search-clear-mobile" aria-hidden="true">×</span></button>
    </form>
    <div class="ledger-view-tools">
      <button id="ledgerExcelExport" class="secondary compact" type="button" title="匯出目前月份完整帳簿（.xlsx）">匯出 Excel</button>
      <span id="ledgerExcelExportStatus" class="ledger-export-status" aria-live="polite"></span>
    </div>`;
  title.insertAdjacentElement('afterend', toolbar);

  setupLedgerUtilityMenu();
  syncLedgerMonthDisplay();
}

function setupLedgerUtilityMenu() {
  if (document.querySelector('#ledgerToolsSheet')) return;

  const backdrop = document.createElement('button');
  backdrop.id = 'ledgerToolsBackdrop';
  backdrop.className = 'cy-mobile-sheet-backdrop';
  backdrop.type = 'button';
  backdrop.setAttribute('aria-label', '關閉記帳工具');
  backdrop.hidden = true;

  const sheet = document.createElement('section');
  sheet.id = 'ledgerToolsSheet';
  sheet.className = 'cy-mobile-tools-sheet';
  sheet.hidden = true;
  sheet.innerHTML = `
    <div class="cy-mobile-sheet-handle" aria-hidden="true"></div>
    <h3>更多</h3>
    <button type="button" data-mobile-ledger-action="accounts">帳戶設定</button>
    <button type="button" data-mobile-ledger-action="categories">科目設定</button>
    <button type="button" data-mobile-ledger-action="lock">月份鎖帳</button>
    <button type="button" data-mobile-ledger-action="export">匯出 Excel</button>
    <button type="button" class="secondary" data-mobile-ledger-action="close">取消</button>`;

  document.body.append(backdrop, sheet);
}

function bindLedgerToolbar() {
  document.querySelector('#ledgerPrevMonth')?.addEventListener('click', () => moveLedgerMonth(-1));
  document.querySelector('#ledgerNextMonth')?.addEventListener('click', () => moveLedgerMonth(1));
  els.monthFilter?.addEventListener('change', syncLedgerMonthDisplay);
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

  document.querySelector('#ledgerOpeningBalanceButton')?.addEventListener('click', async () => {
    const dialog = document.querySelector('#openingDialog');
    if (!dialog) return;
    if (els.openingMonth && els.monthFilter?.value) els.openingMonth.value = els.monthFilter.value;
    if (els.openingMessage) setDialogMessage(els.openingMessage, '');
    dialog.showModal();
    await loadOpeningBalances();
  });

  document.querySelector('#ledgerLockSettingsButton')?.addEventListener('click', () => {
    if (typeof openSettings === 'function') openSettings();
    if (typeof setSettingsTab === 'function') setSettingsTab('lock');
    setTimeout(() => document.querySelector('#lockedThrough')?.focus(), 0);
  });

  document.querySelector('#ledgerBalanceButton')?.addEventListener('click', () => {
    if (typeof window.cyOpenMobileLedgerOpening === 'function') window.cyOpenMobileLedgerOpening();
    else document.querySelector('#ledgerOpeningBalanceButton')?.click();
  });

  const more = document.querySelector('#ledgerMoreButton');
  const backdrop = document.querySelector('#ledgerToolsBackdrop');
  const sheet = document.querySelector('#ledgerToolsSheet');
  if (!more || !backdrop || !sheet) return;

  const close = () => {
    sheet.hidden = true;
    backdrop.hidden = true;
    more.setAttribute('aria-expanded', 'false');
    document.body.classList.remove('cy-mobile-ledger-tools-open');
  };
  const open = () => {
    sheet.hidden = false;
    backdrop.hidden = false;
    more.setAttribute('aria-expanded', 'true');
    document.body.classList.add('cy-mobile-ledger-tools-open');
  };

  more.addEventListener('click', open);
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
    more.focus();
  });
}

function syncLedgerMonthDisplay() {
  const display = document.querySelector('#ledgerMonthDisplay');
  const value = String(els.monthFilter?.value || '');
  if (!display) return;
  const match = /^(\d{4})-(\d{2})$/.exec(value);
  display.textContent = match ? `${Number(match[1])}年${Number(match[2])}月` : '選擇月份';
}

window.cySyncLedgerMonthDisplay = syncLedgerMonthDisplay;

function scheduleLedgerRefresh() {
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
    const accountVisual = ledgerAccountVisual(name);
    const heading = `<tr class="account-group-row"><td colspan="8" data-account-color-slot="${accountVisual.slot || ''}" style="--ledger-account-bg:${accountVisual.background};--ledger-account-fg:${accountVisual.foreground}"><strong class="ledger-account-color">${escapeHtml(name)}</strong><span>期初 ${money(opening)}　期末 ${money(ending)}</span></td></tr>`;
    return heading + rows.map(tx => {
      const accountBalance = calculated.accountById.get(Number(tx.id)) ?? 0;
      return renderLedgerRow(tx, accountBalance, new Map([[name, accountBalance]]), true);
    }).join('');
  }).join('');
}

const LEDGER_ACCOUNT_LIGHT_PALETTE = [
  '#dce9ff', '#ffe4c2', '#ddf1e2', '#f8dce6', '#e9ddfc',
  '#d7f1ee', '#fff0b8', '#ffd8cf', '#dfe3ff', '#e9f0c9',
  '#d7eff8', '#f2daf0', '#eee7c8', '#f7dec8', '#d8efdf',
  '#e7ddf0', '#dceaf1', '#f3d8d7', '#e9ddd4', '#d5ece8'
];

const LEDGER_ACCOUNT_DARK_PALETTE = [
  '#315c99', '#a55d18', '#397447', '#9a4561', '#684a95',
  '#27736a', '#806615', '#9e4a36', '#4b5da2', '#60752c',
  '#34778a', '#88467f', '#746930', '#955732', '#3b7355',
  '#71528a', '#416d82', '#934844', '#75513d', '#376f69'
];

function ledgerAccountVisual(value) {
  const name = String(value || '').trim();
  const account = [
    ...(Array.isArray(state.accounts) ? state.accounts : []),
    ...(Array.isArray(state.archivedAccounts) ? state.archivedAccounts : [])
  ].find(item => String(item?.name || '') === name);
  const slot = Number(account?.color_slot || 0);
  if (!Number.isInteger(slot) || slot <= 0) {
    return { slot: null, background: '#edf1f4', foreground: '#344454' };
  }

  const cycle = (slot - 1) % 40;
  const paletteIndex = cycle % 20;
  const dark = cycle >= 20;
  return {
    slot,
    background: dark ? LEDGER_ACCOUNT_DARK_PALETTE[paletteIndex] : LEDGER_ACCOUNT_LIGHT_PALETTE[paletteIndex],
    foreground: dark ? '#fffaf4' : '#263647'
  };
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
  const accountVisual = ledgerAccountVisual(accountName);
  const mobileAccount = accountLines.map(line => `<span>${escapeHtml(line)}</span>`).join('');
  const kindClass = tx.kind === 'income' ? 'ledger-row-income' : 'ledger-row-expense';

  return `<tr class="ledger-row ${kindClass}" data-transaction-id="${id}">
    <td><span class="ledger-date-desktop">${escapeHtml(fullDate)}</span><span class="ledger-date-mobile">${escapeHtml(mobileDate)}</span></td>
    <td class="ledger-account-name" data-account-color-slot="${accountVisual.slot || ''}" style="--ledger-account-bg:${accountVisual.background};--ledger-account-fg:${accountVisual.foreground}"><span class="ledger-account-desktop ledger-account-color">${escapeHtml(accountName)}</span><span class="ledger-account-mobile ledger-account-color" aria-label="${escapeHtml(accountName)}">${mobileAccount}</span></td>
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
  row.innerHTML = `<th>日期</th><th id="ledgerAccountHeader" class="ledger-account-header${cyLedgerGroupByAccount ? ' cy-account-group-active' : ''}" title="${accountTitle}" aria-pressed="${cyLedgerGroupByAccount ? 'true' : 'false'}">${accountLabel}</th><th>收支</th><th>科目</th><th>摘要</th><th class="num">金額</th><th class="num">餘額</th><th class="action-col">操作</th>`;
  row.querySelector('#ledgerAccountHeader')?.addEventListener('click', () => {
    cyLedgerGroupByAccount = !cyLedgerGroupByAccount;
    renderDesktopLedger();
  });
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