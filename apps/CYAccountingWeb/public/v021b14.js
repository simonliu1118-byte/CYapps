const CY_V21_BUILD14_VERSION = 'V0.21.0 Build 14';
const CY_V21_BUILD14_DESKTOP = '(min-width: 1024px)';
const CY_V21_BUILD14_MONTHS = ['一月', '二月', '三月', '四月', '五月', '六月', '七月', '八月', '九月', '十月', '十一月', '十二月'];
let cyV21Build14InlineEdit = null;

ensureV21Build14Stylesheet();
syncV21Build14Version();

const runV21Build14 = () => {
  syncV21Build14Version();
  setupV21Build14Managers();
  setupV21Build14InlineEditing();
  setupV21Build14AccountLimit();
  setupV21Build14MonthPickers();
  syncV21Build14LedgerMonthTrigger();
};

if (document.readyState === 'complete') setTimeout(runV21Build14, 0);
else window.addEventListener('load', () => setTimeout(runV21Build14, 0), { once: true });

function ensureV21Build14Stylesheet() {
  if (document.querySelector('link[href="/v021b14.css"]')) return;
  const link = document.createElement('link');
  link.rel = 'stylesheet';
  link.href = '/v021b14.css';
  document.head.appendChild(link);
}

function syncV21Build14Version() {
  const version = document.querySelector('.version');
  if (version) version.textContent = CY_V21_BUILD14_VERSION;
}

function setupV21Build14Managers() {
  if (typeof window.renderAccountManager === 'function') window.renderAccountManager = renderV21Build14AccountManager;
  if (typeof window.renderCategoryManager === 'function') window.renderCategoryManager = renderV21Build14CategoryManager;
  renderV21Build14AccountManager();
  renderV21Build14CategoryManager();
}

function renderV21Build14AccountManager() {
  const host = document.querySelector('#accountRows');
  if (!host || typeof state !== 'object') return;
  const accounts = Array.isArray(state.accounts) ? state.accounts : [];
  if (!accounts.length) {
    host.innerHTML = '<div class="empty">尚無帳戶。</div>';
    return;
  }

  host.innerHTML = accounts.map((account, index) => {
    const id = Number(account.id);
    const isDefault = Number(account.is_default) === 1;
    return `<div class="manager-row v21-account-manager-row" data-v21-account-row="${id}">
      <div class="manager-row-main v21-manager-name-cell">
        ${isDefault
          ? '<button type="button" class="v21-default-chip active" disabled title="預設帳戶" aria-label="預設帳戶">★ 預設</button>'
          : `<button type="button" class="v21-default-chip" data-account-default="${id}" title="設為預設帳戶" aria-label="設為預設帳戶">☆ 預設</button>`}
        <strong class="v21-editable-name" data-v21-account-name="${id}">${v21Build14Escape(account.name)}</strong>
        <button type="button" class="mini-button v21-edit-name-button" data-account-rename="${id}" title="編輯帳戶名稱" aria-label="編輯帳戶名稱">✎</button>
      </div>
      <div class="manager-row-actions">
        ${v21Build14OrderButton('account', id, 'up', index === 0)}
        ${v21Build14OrderButton('account', id, 'down', index === accounts.length - 1)}
        <button type="button" class="mini-button danger" data-account-delete="${id}">刪除</button>
      </div>
    </div>`;
  }).join('');
}

function renderV21Build14CategoryManager() {
  const host = document.querySelector('#categoryManager');
  if (!host || typeof state !== 'object') return;
  document.querySelectorAll('[data-settings-kind]').forEach(button =>
    button.classList.toggle('active', button.dataset.settingsKind === state.settingsKind)
  );

  const groups = (state.groups || []).filter(group => group.kind === state.settingsKind);
  if (!groups.length) {
    host.innerHTML = '<div class="empty">目前沒有大分類。</div>';
    return;
  }

  host.innerHTML = `<div class="v21-category-manager-list">${groups.map((group, groupIndex) => {
    const categories = (state.categories || []).filter(category => Number(category.group_id) === Number(group.id));
    const otherGroups = groups.filter(item => Number(item.id) !== Number(group.id));
    const groupId = Number(group.id);
    const items = categories.map((category, index) => {
      const id = Number(category.id);
      const favorite = Number(category.is_favorite) === 1;
      return `<div class="category-item v21-category-manager-row" data-v21-category-row="${id}">
        <button type="button" class="v21-favorite-chip${favorite ? ' active' : ''}" data-category-favorite="${id}" title="${favorite ? '取消常用科目' : '設為常用科目'}" aria-label="${favorite ? '取消常用科目' : '設為常用科目'}">${favorite ? '★' : '☆'}</button>
        <span class="v21-editable-name" data-v21-category-name="${id}">${v21Build14Escape(category.name)}</span>
        <button type="button" class="mini-button v21-edit-name-button" data-category-rename="${id}" title="編輯科目名稱" aria-label="編輯科目名稱">✎</button>
        ${v21Build14OrderButton('category', id, 'up', index === 0)}
        ${v21Build14OrderButton('category', id, 'down', index === categories.length - 1)}
        ${otherGroups.length ? `<button type="button" class="mini-button" data-v12-category-transfer="${id}">移動</button>` : ''}
        <button type="button" class="mini-button danger" data-category-delete="${id}">刪除</button>
      </div>`;
    }).join('');

    return `<section class="category-group v21-category-group" data-group-id="${groupId}">
      <div class="category-group-head v21-category-group-head">
        <strong class="category-group-title v21-editable-name" data-v21-group-name="${groupId}">${v21Build14Escape(group.name)}</strong>
        <button type="button" class="mini-button v21-edit-name-button" data-group-rename="${groupId}" title="編輯大分類名稱" aria-label="編輯大分類名稱">✎</button>
        <span class="v21-manager-action-spacer" aria-hidden="true"></span>
        ${v21Build14OrderButton('group', groupId, 'up', groupIndex === 0)}
        ${v21Build14OrderButton('group', groupId, 'down', groupIndex === groups.length - 1)}
        <button type="button" class="mini-button danger" data-group-delete="${groupId}">刪除</button>
      </div>
      <div class="category-items v21-category-items">
        ${items || '<div class="v21-category-empty">此分類尚無科目。</div>'}
        <div class="category-add v21-category-add-row">
          <input type="text" maxlength="60" placeholder="新增科目" data-new-category-group="${groupId}">
          <button type="button" class="mini-button" data-category-add="${groupId}">新增</button>
        </div>
      </div>
    </section>`;
  }).join('')}</div>`;
}

function v21Build14OrderButton(type, id, direction, disabled) {
  const attr = type === 'account' ? 'data-v11-move-account' : type === 'group' ? 'data-v11-move-group' : 'data-v11-move-category';
  const title = direction === 'up' ? '往上移' : '往下移';
  return `<button type="button" class="mini-button order-button" ${attr}="${id}" data-direction="${direction}" title="${title}" aria-label="${title}"${disabled ? ' disabled' : ''}>${direction === 'up' ? '↑' : '↓'}</button>`;
}

function setupV21Build14InlineEditing() {
  const accountHost = document.querySelector('#accountRows');
  const categoryHost = document.querySelector('#categoryManager');
  if (accountHost && accountHost.dataset.v21Build14EditBound !== '1') {
    accountHost.dataset.v21Build14EditBound = '1';
    accountHost.addEventListener('click', handleV21Build14RenameClick, true);
  }
  if (categoryHost && categoryHost.dataset.v21Build14EditBound !== '1') {
    categoryHost.dataset.v21Build14EditBound = '1';
    categoryHost.addEventListener('click', handleV21Build14RenameClick, true);
  }
  if (document.documentElement.dataset.v21Build14EditOutsideBound !== '1') {
    document.documentElement.dataset.v21Build14EditOutsideBound = '1';
    document.addEventListener('pointerdown', event => {
      if (!cyV21Build14InlineEdit) return;
      if (cyV21Build14InlineEdit.container?.contains(event.target)) return;
      cancelV21Build14InlineEdit();
    });
  }
}

function handleV21Build14RenameClick(event) {
  const save = event.target.closest('[data-v21-inline-name-save]');
  if (save) {
    event.preventDefault();
    event.stopImmediatePropagation();
    saveV21Build14InlineEdit();
    return;
  }
  const cancel = event.target.closest('[data-v21-inline-name-cancel]');
  if (cancel) {
    event.preventDefault();
    event.stopImmediatePropagation();
    cancelV21Build14InlineEdit();
    return;
  }

  const button = event.target.closest('[data-account-rename], [data-category-rename], [data-group-rename]');
  if (!button) return;
  event.preventDefault();
  event.stopImmediatePropagation();

  if (button.dataset.accountRename) beginV21Build14InlineEdit('account', Number(button.dataset.accountRename), button);
  else if (button.dataset.categoryRename) beginV21Build14InlineEdit('category', Number(button.dataset.categoryRename), button);
  else if (button.dataset.groupRename) beginV21Build14InlineEdit('group', Number(button.dataset.groupRename), button);
}

function beginV21Build14InlineEdit(type, id, button) {
  if (!Number.isInteger(id) || id <= 0) return;
  if (cyV21Build14InlineEdit) cancelV21Build14InlineEdit();

  const selector = type === 'account' ? `[data-v21-account-name="${id}"]` : type === 'category' ? `[data-v21-category-name="${id}"]` : `[data-v21-group-name="${id}"]`;
  const label = document.querySelector(selector);
  if (!label) return;
  const name = String(label.textContent || '').trim();
  const input = document.createElement('input');
  input.type = 'text';
  input.className = 'v21-inline-name-input';
  input.value = name;
  input.maxLength = type === 'account' ? 8 : 60;
  input.setAttribute('aria-label', type === 'account' ? '帳戶名稱' : type === 'category' ? '科目名稱' : '大分類名稱');

  const container = label.parentElement;
  label.hidden = true;
  label.insertAdjacentElement('afterend', input);
  button.hidden = true;

  const save = document.createElement('button');
  save.type = 'button';
  save.className = 'mini-button v21-inline-edit-confirm';
  save.dataset.v21InlineNameSave = '1';
  save.title = '儲存';
  save.setAttribute('aria-label', '儲存');
  save.textContent = '✓';

  const cancel = document.createElement('button');
  cancel.type = 'button';
  cancel.className = 'mini-button v21-inline-edit-cancel';
  cancel.dataset.v21InlineNameCancel = '1';
  cancel.title = '取消';
  cancel.setAttribute('aria-label', '取消');
  cancel.textContent = '×';

  input.insertAdjacentElement('afterend', save);
  save.insertAdjacentElement('afterend', cancel);

  cyV21Build14InlineEdit = { type, id, name, label, button, input, save, cancel, container };
  input.addEventListener('keydown', event => {
    if (event.isComposing) return;
    if (event.key === 'Enter') {
      event.preventDefault();
      saveV21Build14InlineEdit();
    } else if (event.key === 'Escape') {
      event.preventDefault();
      cancelV21Build14InlineEdit();
    }
  });
  input.focus();
  input.select();
}

async function saveV21Build14InlineEdit() {
  const active = cyV21Build14InlineEdit;
  if (!active) return;
  const name = String(active.input.value || '').trim().replace(/\s+/g, ' ');
  if (!name) return setDialogMessage(document.querySelector('#settingsMessage'), '名稱不可空白。', true);
  if (active.type === 'account' && v21Build14CharCount(name) > 8) {
    active.input.focus();
    return setDialogMessage(document.querySelector('#settingsMessage'), '帳戶名稱最多 8 個字。', true);
  }
  if (name === active.name) {
    cancelV21Build14InlineEdit();
    return;
  }

  const path = active.type === 'account' ? `/api/accounts/${active.id}` : active.type === 'category' ? `/api/categories/${active.id}` : `/api/category-groups/${active.id}`;
  active.input.disabled = true;
  active.save.disabled = true;
  active.cancel.disabled = true;
  cyV21Build14InlineEdit = null;
  const ok = await mutateSettings(path, { method: 'PUT', headers: jsonHeaders(), body: JSON.stringify({ name }) }, '名稱已更新。');
  if (!ok && active.container?.isConnected) {
    cyV21Build14InlineEdit = active;
    active.input.disabled = false;
    active.save.disabled = false;
    active.cancel.disabled = false;
    active.input.focus();
  }
}

function cancelV21Build14InlineEdit() {
  const active = cyV21Build14InlineEdit;
  if (!active) return;
  active.input?.remove();
  active.save?.remove();
  active.cancel?.remove();
  if (active.label) active.label.hidden = false;
  if (active.button) active.button.hidden = false;
  cyV21Build14InlineEdit = null;
}

function setupV21Build14AccountLimit() {
  const input = document.querySelector('#newAccountName');
  const button = document.querySelector('#addAccountButton');
  if (!input || !button) return;
  input.maxLength = 8;
  input.placeholder = '新增帳戶名稱（最多8字）';

  const validate = event => {
    const name = String(input.value || '').trim().replace(/\s+/g, ' ');
    if (!name || v21Build14CharCount(name) <= 8) return;
    event?.preventDefault();
    event?.stopImmediatePropagation();
    setDialogMessage(document.querySelector('#settingsMessage'), '帳戶名稱最多 8 個字。', true);
    input.focus();
  };
  if (button.dataset.v21Build14LimitBound !== '1') {
    button.dataset.v21Build14LimitBound = '1';
    button.addEventListener('click', validate, true);
  }
  if (input.dataset.v21Build14LimitBound !== '1') {
    input.dataset.v21Build14LimitBound = '1';
    input.addEventListener('keydown', event => {
      if (event.key === 'Enter') validate(event);
    }, true);
  }
}

function setupV21Build14MonthPickers() {
  const media = window.matchMedia(CY_V21_BUILD14_DESKTOP);
  const scan = () => {
    syncV21Build14LedgerMonthTrigger();
    if (!media.matches) return;
    for (const input of document.querySelectorAll('input[type="month"]')) {
      if (input.id === 'monthFilter' || input.id === 'openingMonth') continue;
      ensureV21Build14MonthPickerForInput(input);
    }
  };
  scan();
  if (typeof media.addEventListener === 'function') media.addEventListener('change', scan);
  else media.addListener?.(scan);

  if (document.body.dataset.v21Build14MonthObserver !== '1') {
    document.body.dataset.v21Build14MonthObserver = '1';
    const observer = new MutationObserver(scan);
    observer.observe(document.body, { childList: true, subtree: true });
  }
}

function syncV21Build14LedgerMonthTrigger() {
  document.querySelector('#ledgerMonthPickerCustom .v21-month-picker-caret')?.remove();
}

function ensureV21Build14MonthPickerForInput(input) {
  if (!input || input.dataset.v21Build14MonthPicker === '1') return;
  const label = input.closest('label') || input.parentElement;
  if (!label) return;
  input.dataset.v21Build14MonthPicker = '1';
  input.classList.add('v21-native-month-source');

  const root = document.createElement('div');
  root.className = 'v21-month-picker-custom v21-month-picker-field';
  root.innerHTML = `
    <button type="button" class="v21-month-picker-trigger v21-month-picker-field-trigger" aria-haspopup="dialog" aria-expanded="false"><span data-v21-month-label>—</span></button>
    <div class="v21-month-picker-popover" role="dialog" aria-label="選擇月份" hidden>
      <div class="v21-month-picker-head">
        <button type="button" class="v21-month-picker-nav" data-picker-nav="-1" aria-label="上一組">‹</button>
        <button type="button" class="v21-month-picker-year" data-picker-year-head aria-label="切換年份選擇"></button>
        <button type="button" class="v21-month-picker-nav" data-picker-nav="1" aria-label="下一組">›</button>
      </div>
      <div class="v21-month-picker-grid" data-picker-grid></div>
    </div>`;
  input.insertAdjacentElement('afterend', root);

  const trigger = root.querySelector('.v21-month-picker-trigger');
  const popover = root.querySelector('.v21-month-picker-popover');
  const yearHead = root.querySelector('[data-picker-year-head]');
  const grid = root.querySelector('[data-picker-grid]');
  let view = 'months';
  let displayYear = v21Build14ReadMonth(input).year;
  let yearStart = displayYear - 5;

  const render = () => {
    const selected = v21Build14ReadMonth(input);
    if (view === 'months') {
      yearHead.textContent = String(displayYear);
      grid.className = 'v21-month-picker-grid month-view';
      grid.innerHTML = CY_V21_BUILD14_MONTHS.map((monthLabel, index) => {
        const month = index + 1;
        const active = selected.valid && selected.year === displayYear && selected.month === month;
        return `<button type="button" class="v21-month-choice${active ? ' active' : ''}" data-picker-month="${month}">${monthLabel}</button>`;
      }).join('');
    } else {
      yearHead.textContent = `${yearStart}–${yearStart + 11}`;
      grid.className = 'v21-month-picker-grid year-view';
      grid.innerHTML = Array.from({ length: 12 }, (_, index) => yearStart + index).map(year =>
        `<button type="button" class="v21-year-choice${selected.valid && selected.year === year ? ' active' : ''}" data-picker-year="${year}">${year}</button>`
      ).join('');
    }
  };

  const syncLabel = () => {
    const selected = v21Build14ReadMonth(input);
    const target = root.querySelector('[data-v21-month-label]');
    if (target) target.textContent = selected.valid ? `${selected.year}年${String(selected.month).padStart(2, '0')}月` : '選擇月份';
  };

  const close = focusTrigger => {
    popover.hidden = true;
    trigger.setAttribute('aria-expanded', 'false');
    if (focusTrigger) trigger.focus();
  };

  trigger.addEventListener('click', event => {
    event.stopPropagation();
    if (!window.matchMedia(CY_V21_BUILD14_DESKTOP).matches) return;
    if (!popover.hidden) return close(false);
    const selected = v21Build14ReadMonth(input);
    displayYear = selected.year;
    yearStart = displayYear - 5;
    view = 'months';
    render();
    popover.hidden = false;
    trigger.setAttribute('aria-expanded', 'true');
  });

  root.querySelectorAll('[data-picker-nav]').forEach(button => button.addEventListener('click', () => {
    const delta = Number(button.dataset.pickerNav) || 0;
    if (view === 'months') displayYear += delta;
    else yearStart += delta * 12;
    render();
  }));

  yearHead.addEventListener('click', () => {
    if (view === 'months') {
      view = 'years';
      yearStart = displayYear - 5;
    } else view = 'months';
    render();
  });

  grid.addEventListener('click', event => {
    const monthButton = event.target.closest('[data-picker-month]');
    if (monthButton) {
      const month = Number(monthButton.dataset.pickerMonth);
      input.value = `${displayYear}-${String(month).padStart(2, '0')}`;
      input.dispatchEvent(new Event('change', { bubbles: true }));
      syncLabel();
      close(true);
      return;
    }
    const yearButton = event.target.closest('[data-picker-year]');
    if (yearButton) {
      displayYear = Number(yearButton.dataset.pickerYear) || displayYear;
      view = 'months';
      render();
    }
  });

  input.addEventListener('change', syncLabel);
  document.addEventListener('pointerdown', event => {
    if (popover.hidden || root.contains(event.target)) return;
    close(false);
  });
  document.addEventListener('keydown', event => {
    if (event.key === 'Escape' && !popover.hidden) close(true);
  });
  syncLabel();
}

function v21Build14ReadMonth(input) {
  const match = /^(\d{4})-(\d{2})$/.exec(String(input?.value || ''));
  if (match) return { valid: true, year: Number(match[1]), month: Number(match[2]) };
  const now = new Date();
  return { valid: false, year: now.getFullYear(), month: now.getMonth() + 1 };
}

function v21Build14CharCount(value) {
  return Array.from(String(value || '')).length;
}

function v21Build14Escape(value) {
  return String(value ?? '')
    .replaceAll('&', '&amp;')
    .replaceAll('<', '&lt;')
    .replaceAll('>', '&gt;')
    .replaceAll('"', '&quot;')
    .replaceAll("'", '&#39;');
}
