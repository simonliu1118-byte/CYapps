const CY_V0214_VERSION = 'V0.21.5 Build 8';
const CY_V0214_HOVER = '(hover: hover) and (pointer: fine)';
let cyV0214BalancePopover = null;
let cyV0214BalanceAnchor = null;
let cyV0214BalancePinned = false;
let cyV0214BalanceHideTimer = null;
const cyV0214PendingWrites = new Set();

ensureV0214Stylesheet();
window.cyShowMigrationComplete = showMigrationCompleteV0214;
window.cyCloseLedgerBalancePopover = closeLedgerBalancePopoverV0214;

const runV0214 = () => {
  enforceV0214Version();
  ensureMigrationCompleteDialogV0214();
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
  ensureBalancePopoverV0214();

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