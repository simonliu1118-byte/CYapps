const CY_V0212_VERSION = 'V0.21.2';
const CY_V0212_DESKTOP = '(min-width: 1024px)';
let cyV0212DialogState = null;
let cyV0212Rendering = false;

ensureV0212Stylesheet();

const runV0212 = () => {
  enforceV0212Version();
  if (!window.matchMedia(CY_V0212_DESKTOP).matches) return;
  installV0212CategoryRenderer();
  ensureV0212ManagerDialog();
  bindV0212ManagerActions();
  renderV0212CategoryManager();
};

if (document.readyState === 'loading') {
  document.addEventListener('DOMContentLoaded', () => setTimeout(runV0212, 0), { once: true });
} else {
  setTimeout(runV0212, 0);
}
window.addEventListener('load', () => {
  setTimeout(runV0212, 0);
  setTimeout(runV0212, 120);
  setTimeout(runV0212, 420);
}, { once: true });

function ensureV0212Stylesheet() {
  if (document.querySelector('link[href="/v0212.css"]')) return;
  const link = document.createElement('link');
  link.rel = 'stylesheet';
  link.href = '/v0212.css';
  document.head.appendChild(link);
}

function enforceV0212Version() {
  let version = document.querySelector('.version');
  if (!version) return;
  if (version.dataset.v0212Version !== '1') {
    const replacement = version.cloneNode(true);
    replacement.dataset.v0212Version = '1';
    version.replaceWith(replacement);
    version = replacement;
  }
  version.textContent = CY_V0212_VERSION;
}

function installV0212CategoryRenderer() {
  window.renderCategoryManager = renderV0212CategoryManager;
  if (typeof window.renderV21Build15CategoryManager === 'function') window.renderV21Build15CategoryManager = renderV0212CategoryManager;
  if (typeof window.renderV21Build16CategoryManager === 'function') window.renderV21Build16CategoryManager = renderV0212CategoryManager;
  if (typeof window.renderV0211CategoryManager === 'function') window.renderV0211CategoryManager = renderV0212CategoryManager;
}

function renderV0212CategoryManager() {
  if (!window.matchMedia(CY_V0212_DESKTOP).matches || typeof state !== 'object') return;
  const host = document.querySelector('#categoryManager');
  const pane = document.querySelector('[data-settings-pane="categories"]');
  if (!host || !pane || cyV0212Rendering) return;

  const kind = state.settingsKind === 'income' ? 'income' : 'expense';
  const groups = (state.groups || []).filter(group => group.kind === kind);
  pane.classList.toggle('v0211-category-income', kind === 'income');
  pane.classList.toggle('v0211-category-expense', kind === 'expense');
  pane.classList.toggle('v0212-category-income', kind === 'income');
  pane.classList.toggle('v0212-category-expense', kind === 'expense');

  const legacyGroupAdd = document.querySelector('#newGroupName')?.parentElement;
  legacyGroupAdd?.classList.add('v0212-legacy-group-add');

  const toolbar = `<div class="v0212-category-toolbar">
    <div class="entry-kind-switch v0212-kind-switch" role="group" aria-label="收入或支出">
      <button type="button" class="kind-button${kind === 'income' ? ' active' : ''}" data-kind="income" data-v0212-manager-kind="income">收入</button>
      <button type="button" class="kind-button${kind === 'expense' ? ' active' : ''}" data-kind="expense" data-v0212-manager-kind="expense">支出</button>
    </div>
    <button type="button" class="secondary compact v0212-toolbar-button" data-v0212-add-group>＋ 新增大分類</button>
    <span class="v0212-toolbar-spacer" aria-hidden="true"></span>
    <button type="button" class="secondary compact v0212-toolbar-button v0212-add-category-button" data-v0212-add-category${groups.length ? '' : ' disabled title="請先新增大分類"'}>＋ 新增科目</button>
  </div>`;

  const body = groups.length
    ? `<div class="v0212-category-list">${groups.map(group => v0212GroupHtml(group, kind)).join('')}</div>`
    : '<div class="v0212-category-list-empty">目前沒有大分類。請先使用上方「新增大分類」。</div>';

  cyV0212Rendering = true;
  try {
    host.innerHTML = `<div class="v0211-category-shell v0212-category-shell">${toolbar}${body}</div>`;
  } finally {
    cyV0212Rendering = false;
  }
}

function v0212GroupHtml(group, kind) {
  const groupId = Number(group.id);
  const categories = (state.categories || []).filter(category =>
    category.kind === kind && Number(category.group_id) === groupId
  );
  const rows = categories.map(category => {
    const id = Number(category.id);
    const favorite = Number(category.is_favorite) === 1;
    return `<div class="v0212-category-row" data-v21-category-row="${id}" data-v21-category-group="${groupId}">
      <button type="button" class="v21-drag-handle" draggable="true" data-v21-drag-category="${id}" title="拖曳調整科目順序或分類" aria-label="拖曳調整科目順序或分類">⠿</button>
      <button type="button" class="v0212-favorite${favorite ? ' active' : ''}" data-category-favorite="${id}" title="${favorite ? '取消常用科目' : '設為常用科目'}" aria-label="${favorite ? '取消常用科目' : '設為常用科目'}">${favorite ? '★' : '☆'}</button>
      <span class="v0212-category-name" title="${v0212Escape(category.name)}">${v0212Escape(category.name)}</span>
      <button type="button" class="mini-button v0212-edit-button" data-v0212-rename="category" data-v0212-id="${id}" title="編輯科目名稱" aria-label="編輯科目名稱">✎</button>
      <button type="button" class="mini-button danger v21-manager-delete" data-category-delete="${id}">刪除</button>
    </div>`;
  }).join('');

  return `<section class="v0212-category-group" data-group-id="${groupId}">
    <div class="v21-category-group-head v0212-category-group-head">
      <button type="button" class="v21-drag-handle" draggable="true" data-v21-drag-group="${groupId}" title="拖曳調整大分類順序" aria-label="拖曳調整大分類順序">⠿</button>
      <strong class="v0212-group-name" title="${v0212Escape(group.name)}">${v0212Escape(group.name)}</strong>
      <button type="button" class="mini-button v0212-edit-button" data-v0212-rename="group" data-v0212-id="${groupId}" title="編輯大分類名稱" aria-label="編輯大分類名稱">✎</button>
      <button type="button" class="mini-button danger v21-manager-delete" data-group-delete="${groupId}">刪除</button>
    </div>
    <div class="v0212-category-items" data-v21-category-dropzone="${groupId}">
      ${rows || '<div class="v0212-empty-group">尚無科目</div>'}
    </div>
  </section>`;
}

function bindV0212ManagerActions() {
  if (window.__cyV0212ManagerActionsBound) return;
  window.__cyV0212ManagerActionsBound = true;

  window.addEventListener('click', event => {
    if (!window.matchMedia(CY_V0212_DESKTOP).matches) return;
    const rename = event.target.closest('[data-v0212-rename]');
    const legacyRename = event.target.closest('[data-account-rename], [data-category-rename], [data-group-rename]');
    const addGroup = event.target.closest('[data-v0212-add-group]');
    const addCategory = event.target.closest('[data-v0212-add-category]');
    if (!rename && !legacyRename && !addGroup && !addCategory) return;

    event.preventDefault();
    event.stopPropagation();
    event.stopImmediatePropagation();

    if (addGroup) return openV0212AddGroupDialog();
    if (addCategory) {
      if (addCategory.disabled) return;
      return openV0212AddCategoryDialog();
    }
    if (rename) return openV0212RenameDialog(rename.dataset.v0212Rename, Number(rename.dataset.v0212Id));

    if (legacyRename?.dataset.accountRename) return openV0212RenameDialog('account', Number(legacyRename.dataset.accountRename));
    if (legacyRename?.dataset.categoryRename) return openV0212RenameDialog('category', Number(legacyRename.dataset.categoryRename));
    if (legacyRename?.dataset.groupRename) return openV0212RenameDialog('group', Number(legacyRename.dataset.groupRename));
  }, true);

  document.querySelector('#categoryManager')?.addEventListener('click', event => {
    const button = event.target.closest('[data-v0212-manager-kind]');
    if (!button || typeof state !== 'object') return;
    const kind = button.dataset.v0212ManagerKind;
    if (!['income', 'expense'].includes(kind) || kind === state.settingsKind) return;
    state.settingsKind = kind;
    if (typeof cyV21Build15Drag !== 'undefined' && cyV21Build15Drag && typeof finishV21Build15Drag === 'function') finishV21Build15Drag();
    renderV0212CategoryManager();
  });

  const host = document.querySelector('#categoryManager');
  if (host && host.dataset.v0212Guard !== '1') {
    host.dataset.v0212Guard = '1';
    const observer = new MutationObserver(() => {
      if (cyV0212Rendering || !window.matchMedia(CY_V0212_DESKTOP).matches) return;
      if (!host.querySelector('.v0212-category-shell') && host.children.length) queueMicrotask(renderV0212CategoryManager);
    });
    observer.observe(host, { childList: true, subtree: false });
  }
}

function ensureV0212ManagerDialog() {
  if (document.querySelector('#v0212ManagerDialog')) return;
  const dialog = document.createElement('dialog');
  dialog.id = 'v0212ManagerDialog';
  dialog.className = 'v0212-manager-dialog';
  dialog.innerHTML = `<form method="dialog" class="v0212-manager-dialog-shell" id="v0212ManagerForm">
    <div class="v0212-manager-dialog-head">
      <div><h2 id="v0212ManagerTitle">新增科目</h2><p id="v0212ManagerSubtitle"></p></div>
      <button type="button" class="icon-button" data-v0212-dialog-close aria-label="關閉">×</button>
    </div>
    <label class="v0212-dialog-field" id="v0212GroupField" hidden>
      <span>大分類</span>
      <select id="v0212GroupSelect"></select>
    </label>
    <label class="v0212-dialog-field">
      <span id="v0212NameLabel">科目名稱</span>
      <input id="v0212NameInput" type="text" maxlength="60" autocomplete="off">
    </label>
    <div id="v0212ManagerMessage" class="dialog-message"></div>
    <div class="v0212-manager-dialog-actions">
      <button type="button" class="secondary" data-v0212-dialog-close>取消</button>
      <button type="submit" class="primary" id="v0212ManagerSave">新增</button>
    </div>
  </form>`;
  document.body.append(dialog);

  dialog.querySelectorAll('[data-v0212-dialog-close]').forEach(button => button.addEventListener('click', () => dialog.close()));
  dialog.querySelector('#v0212ManagerForm')?.addEventListener('submit', saveV0212ManagerDialog);
  dialog.addEventListener('close', () => { cyV0212DialogState = null; });
}

function openV0212AddGroupDialog() {
  if (typeof state !== 'object') return;
  openV0212ManagerDialog({
    mode: 'add-group',
    title: '新增大分類',
    subtitle: state.settingsKind === 'income' ? '新增到收入分類' : '新增到支出分類',
    label: '大分類名稱',
    maxLength: 60,
    value: '',
    showGroup: false,
    saveText: '新增'
  });
}

function openV0212AddCategoryDialog() {
  if (typeof state !== 'object') return;
  const kind = state.settingsKind === 'income' ? 'income' : 'expense';
  const groups = (state.groups || []).filter(group => group.kind === kind);
  if (!groups.length) return;
  openV0212ManagerDialog({
    mode: 'add-category',
    title: '新增科目',
    subtitle: kind === 'income' ? '新增收入科目' : '新增支出科目',
    label: '科目名稱',
    maxLength: 60,
    value: '',
    showGroup: true,
    groups,
    saveText: '新增'
  });
}

function openV0212RenameDialog(type, id) {
  if (typeof state !== 'object' || !Number.isInteger(id) || id <= 0) return;
  let item = null;
  let title = '';
  let label = '';
  let maxLength = 60;
  if (type === 'account') {
    item = (state.accounts || []).find(row => Number(row.id) === id);
    title = '編輯帳戶名稱';
    label = '帳戶名稱';
    maxLength = 8;
  } else if (type === 'group') {
    item = (state.groups || []).find(row => Number(row.id) === id);
    title = '編輯大分類';
    label = '大分類名稱';
  } else if (type === 'category') {
    item = (state.categories || []).find(row => Number(row.id) === id);
    title = '編輯科目';
    label = '科目名稱';
  }
  if (!item) return;
  openV0212ManagerDialog({
    mode: 'rename',
    type,
    id,
    title,
    subtitle: '編輯時不改變目前排序與分類位置',
    label,
    maxLength,
    value: item.name || '',
    showGroup: false,
    saveText: '儲存'
  });
}
window.openV0212RenameDialog = openV0212RenameDialog;

function openV0212ManagerDialog(config) {
  ensureV0212ManagerDialog();
  const dialog = document.querySelector('#v0212ManagerDialog');
  if (!dialog) return;
  cyV0212DialogState = config;
  dialog.querySelector('#v0212ManagerTitle').textContent = config.title || '';
  dialog.querySelector('#v0212ManagerSubtitle').textContent = config.subtitle || '';
  dialog.querySelector('#v0212NameLabel').textContent = config.label || '名稱';
  const input = dialog.querySelector('#v0212NameInput');
  input.maxLength = Number(config.maxLength || 60);
  input.value = config.value || '';
  const groupField = dialog.querySelector('#v0212GroupField');
  const select = dialog.querySelector('#v0212GroupSelect');
  groupField.hidden = !config.showGroup;
  if (config.showGroup) {
    select.innerHTML = (config.groups || []).map(group => `<option value="${Number(group.id)}">${v0212Escape(group.name)}</option>`).join('');
  } else {
    select.innerHTML = '';
  }
  dialog.querySelector('#v0212ManagerSave').textContent = config.saveText || '儲存';
  setDialogMessage(dialog.querySelector('#v0212ManagerMessage'), '');
  if (!dialog.open) dialog.showModal();
  setTimeout(() => input.focus(), 0);
}

async function saveV0212ManagerDialog(event) {
  event.preventDefault();
  const config = cyV0212DialogState;
  const dialog = document.querySelector('#v0212ManagerDialog');
  if (!config || !dialog || typeof state !== 'object') return;
  const input = dialog.querySelector('#v0212NameInput');
  const select = dialog.querySelector('#v0212GroupSelect');
  const save = dialog.querySelector('#v0212ManagerSave');
  const message = dialog.querySelector('#v0212ManagerMessage');
  const name = String(input?.value || '').trim();
  if (!name) {
    setDialogMessage(message, '請輸入名稱。', true);
    input?.focus();
    return;
  }

  save.disabled = true;
  let ok = false;
  try {
    if (config.mode === 'add-group') {
      ok = await mutateSettings('/api/category-groups', {
        method: 'POST', headers: jsonHeaders(), body: JSON.stringify({ kind: state.settingsKind, name })
      }, '大分類已新增。');
    } else if (config.mode === 'add-category') {
      const groupId = Number(select?.value);
      if (!Number.isInteger(groupId) || groupId <= 0) {
        setDialogMessage(message, '請選擇大分類。', true);
        return;
      }
      ok = await mutateSettings('/api/categories', {
        method: 'POST', headers: jsonHeaders(), body: JSON.stringify({ kind: state.settingsKind, groupId, name })
      }, '科目已新增。');
    } else if (config.mode === 'rename') {
      const endpoints = {
        account: `/api/accounts/${config.id}`,
        group: `/api/category-groups/${config.id}`,
        category: `/api/categories/${config.id}`
      };
      const endpoint = endpoints[config.type];
      if (!endpoint) return;
      ok = await mutateSettings(endpoint, {
        method: 'PUT', headers: jsonHeaders(), body: JSON.stringify({ name })
      }, '名稱已更新。');
    }
    if (ok) {
      dialog.close();
      setTimeout(() => {
        installV0212CategoryRenderer();
        renderV0212CategoryManager();
      }, 0);
    }
  } finally {
    save.disabled = false;
  }
}

function v0212Escape(value) {
  return String(value ?? '')
    .replaceAll('&', '&amp;')
    .replaceAll('<', '&lt;')
    .replaceAll('>', '&gt;')
    .replaceAll('"', '&quot;')
    .replaceAll("'", '&#039;');
}
