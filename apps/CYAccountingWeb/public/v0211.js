const CY_V0211_VERSION = 'V0.21.1';
const CY_V0211_DESKTOP = '(min-width: 1024px)';
const CY_V0211_MONTHS = ['一月', '二月', '三月', '四月', '五月', '六月', '七月', '八月', '九月', '十月', '十一月', '十二月'];
const CY_V0211_WEEKDAYS = ['日', '一', '二', '三', '四', '五', '六'];
let cyV0211RenderingManagers = false;
const cyV0211ConfirmBypass = new WeakSet();

ensureV0211Stylesheet();
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
