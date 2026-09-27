const CY_V21_BUILD15_VERSION = 'V0.21.0 Build 15';
const CY_V21_BUILD15_DESKTOP = '(min-width: 1024px)';
let cyV21Build15Drag = null;

ensureV21Build15Stylesheet();
syncV21Build15Version();

const runV21Build15 = () => {
  syncV21Build15Version();
  setupV21Build15Managers();
  setupV21Build15KindSwitch();
  setupV21Build15DragAndDrop();
};

if (document.readyState === 'complete') setTimeout(runV21Build15, 0);
else window.addEventListener('load', () => setTimeout(runV21Build15, 0), { once: true });

function ensureV21Build15Stylesheet() {
  if (document.querySelector('link[href="/v021b15.css"]')) return;
  const link = document.createElement('link');
  link.rel = 'stylesheet';
  link.href = '/v021b15.css';
  document.head.appendChild(link);
}

function syncV21Build15Version() {
  const version = document.querySelector('.version');
  if (version) version.textContent = CY_V21_BUILD15_VERSION;
}

function setupV21Build15Managers() {
  if (typeof window.renderAccountManager === 'function') window.renderAccountManager = renderV21Build15AccountManager;
  if (typeof window.renderCategoryManager === 'function') window.renderCategoryManager = renderV21Build15CategoryManager;
  renderV21Build15AccountManager();
  renderV21Build15CategoryManager();
}

function renderV21Build15AccountManager() {
  const host = document.querySelector('#accountRows');
  if (!host || typeof state !== 'object') return;
  const accounts = Array.isArray(state.accounts) ? state.accounts : [];
  if (!accounts.length) {
    host.innerHTML = '<div class="empty">尚無帳戶。</div>';
    return;
  }

  host.innerHTML = accounts.map(account => {
    const id = Number(account.id);
    const isDefault = Number(account.is_default) === 1;
    return `<div class="manager-row v21-account-manager-row" data-v21-account-row="${id}">
      <button type="button" class="v21-drag-handle" draggable="true" data-v21-drag-account="${id}" title="拖曳調整帳戶順序" aria-label="拖曳調整帳戶順序">⠿</button>
      ${isDefault
        ? '<button type="button" class="v21-default-tag active" disabled aria-label="目前預設帳戶">預設</button>'
        : `<button type="button" class="v21-default-tag" data-account-default="${id}" title="設為預設帳戶">設為預設</button>`}
      <div class="v21-manager-name-cell">
        <strong class="v21-editable-name" data-v21-account-name="${id}">${v21Build15Escape(account.name)}</strong>
        <button type="button" class="mini-button v21-edit-name-button" data-account-rename="${id}" title="編輯帳戶名稱" aria-label="編輯帳戶名稱">✎</button>
      </div>
      <button type="button" class="mini-button danger v21-manager-delete" data-account-delete="${id}">刪除</button>
    </div>`;
  }).join('');
}

function renderV21Build15CategoryManager() {
  const host = document.querySelector('#categoryManager');
  const pane = document.querySelector('[data-settings-pane="categories"]');
  if (!host || !pane || typeof state !== 'object') return;

  const kind = state.settingsKind === 'income' ? 'income' : 'expense';
  pane.classList.toggle('v21-category-kind-income', kind === 'income');
  pane.classList.toggle('v21-category-kind-expense', kind === 'expense');
  document.querySelectorAll('[data-settings-kind]').forEach(button =>
    button.classList.toggle('active', button.dataset.settingsKind === kind)
  );

  const groups = (state.groups || []).filter(group => group.kind === kind);
  const toolbar = `<div class="v21-category-toolbar">
    <div class="entry-kind-switch v21-category-kind-switch" role="group" aria-label="收入或支出">
      <button type="button" class="kind-button${kind === 'income' ? ' active' : ''}" data-kind="income" data-v21-manager-kind="income">收入</button>
      <button type="button" class="kind-button${kind === 'expense' ? ' active' : ''}" data-kind="expense" data-v21-manager-kind="expense">支出</button>
    </div>
  </div>`;

  if (!groups.length) {
    host.innerHTML = `${toolbar}<div class="empty v21-category-empty-state">目前沒有大分類。</div>`;
    return;
  }

  host.innerHTML = `${toolbar}<div class="v21-category-manager-list">${groups.map(group => {
    const groupId = Number(group.id);
    const categories = (state.categories || []).filter(category => Number(category.group_id) === groupId);
    const items = categories.map(category => {
      const id = Number(category.id);
      const favorite = Number(category.is_favorite) === 1;
      return `<div class="category-item v21-category-manager-row" data-v21-category-row="${id}" data-v21-category-group="${groupId}">
        <button type="button" class="v21-drag-handle" draggable="true" data-v21-drag-category="${id}" title="拖曳調整科目順序或分類" aria-label="拖曳調整科目順序或分類">⠿</button>
        <button type="button" class="v21-favorite-chip${favorite ? ' active' : ''}" data-category-favorite="${id}" title="${favorite ? '取消常用科目' : '設為常用科目'}" aria-label="${favorite ? '取消常用科目' : '設為常用科目'}">${favorite ? '★' : '☆'}</button>
        <span class="v21-editable-name" data-v21-category-name="${id}">${v21Build15Escape(category.name)}</span>
        <button type="button" class="mini-button v21-edit-name-button" data-category-rename="${id}" title="編輯科目名稱" aria-label="編輯科目名稱">✎</button>
        <button type="button" class="mini-button danger v21-manager-delete" data-category-delete="${id}">刪除</button>
      </div>`;
    }).join('');

    return `<section class="category-group v21-category-group" data-group-id="${groupId}">
      <div class="category-group-head v21-category-group-head">
        <button type="button" class="v21-drag-handle" draggable="true" data-v21-drag-group="${groupId}" title="拖曳調整大分類順序" aria-label="拖曳調整大分類順序">⠿</button>
        <strong class="category-group-title v21-editable-name" data-v21-group-name="${groupId}">${v21Build15Escape(group.name)}</strong>
        <button type="button" class="mini-button v21-edit-name-button" data-group-rename="${groupId}" title="編輯大分類名稱" aria-label="編輯大分類名稱">✎</button>
        <button type="button" class="mini-button danger v21-manager-delete" data-group-delete="${groupId}">刪除</button>
      </div>
      <div class="category-items v21-category-items" data-v21-category-dropzone="${groupId}">
        ${items || '<div class="v21-category-empty">拖曳科目到此分類，或在下方新增。</div>'}
        <div class="category-add v21-category-add-row">
          <input type="text" maxlength="60" placeholder="新增科目" data-new-category-group="${groupId}">
          <button type="button" class="mini-button" data-category-add="${groupId}">新增</button>
        </div>
      </div>
    </section>`;
  }).join('')}</div>`;
}

function setupV21Build15KindSwitch() {
  const host = document.querySelector('#categoryManager');
  if (!host || host.dataset.v21Build15KindBound === '1') return;
  host.dataset.v21Build15KindBound = '1';
  host.addEventListener('click', event => {
    const button = event.target.closest('[data-v21-manager-kind]');
    if (!button || typeof state !== 'object') return;
    const kind = button.dataset.v21ManagerKind;
    if (!['income', 'expense'].includes(kind) || state.settingsKind === kind) return;
    state.settingsKind = kind;
    if (cyV21Build15Drag) finishV21Build15Drag();
    renderV21Build15CategoryManager();
  });
}

function setupV21Build15DragAndDrop() {
  const accountHost = document.querySelector('#accountRows');
  const categoryHost = document.querySelector('#categoryManager');
  if (accountHost && accountHost.dataset.v21Build15DragBound !== '1') {
    accountHost.dataset.v21Build15DragBound = '1';
    accountHost.addEventListener('dragstart', handleV21Build15DragStart);
    accountHost.addEventListener('dragover', handleV21Build15AccountDragOver);
    accountHost.addEventListener('drop', handleV21Build15AccountDrop);
    accountHost.addEventListener('dragend', finishV21Build15Drag);
  }
  if (categoryHost && categoryHost.dataset.v21Build15DragBound !== '1') {
    categoryHost.dataset.v21Build15DragBound = '1';
    categoryHost.addEventListener('dragstart', handleV21Build15DragStart);
    categoryHost.addEventListener('dragover', handleV21Build15CategoryDragOver);
    categoryHost.addEventListener('drop', handleV21Build15CategoryDrop);
    categoryHost.addEventListener('dragend', finishV21Build15Drag);
  }
}

function handleV21Build15DragStart(event) {
  if (!window.matchMedia(CY_V21_BUILD15_DESKTOP).matches) return;
  const handle = event.target.closest('[data-v21-drag-account], [data-v21-drag-group], [data-v21-drag-category]');
  if (!handle) return;
  if (typeof cyV21Build14InlineEdit !== 'undefined' && cyV21Build14InlineEdit) {
    event.preventDefault();
    return;
  }

  let type = '';
  let id = 0;
  if (handle.dataset.v21DragAccount) { type = 'account'; id = Number(handle.dataset.v21DragAccount); }
  else if (handle.dataset.v21DragGroup) { type = 'group'; id = Number(handle.dataset.v21DragGroup); }
  else if (handle.dataset.v21DragCategory) { type = 'category'; id = Number(handle.dataset.v21DragCategory); }
  if (!type || !Number.isInteger(id) || id <= 0) return;

  cyV21Build15Drag = { type, id };
  event.dataTransfer.effectAllowed = 'move';
  event.dataTransfer.setData('text/plain', `${type}:${id}`);
  const row = handle.closest('[data-v21-account-row], [data-group-id], [data-v21-category-row]');
  row?.classList.add('v21-is-dragging');
  document.body.classList.add('v21-manager-dragging');
}

function handleV21Build15AccountDragOver(event) {
  if (cyV21Build15Drag?.type !== 'account') return;
  event.preventDefault();
  event.dataTransfer.dropEffect = 'move';
  clearV21Build15DropMarkers();
  const row = event.target.closest('[data-v21-account-row]');
  if (!row) return;
  row.classList.add(v21Build15AfterMidpoint(event, row) ? 'v21-drop-after' : 'v21-drop-before');
}

async function handleV21Build15AccountDrop(event) {
  if (cyV21Build15Drag?.type !== 'account' || typeof state !== 'object') return;
  event.preventDefault();
  const sourceId = cyV21Build15Drag.id;
  const row = event.target.closest('[data-v21-account-row]');
  const targetId = Number(row?.dataset.v21AccountRow || 0);
  const after = row ? v21Build15AfterMidpoint(event, row) : true;
  const ids = (state.accounts || []).map(item => Number(item.id));
  const next = v21Build15MoveId(ids, sourceId, targetId, after);
  finishV21Build15Drag();
  if (!next || next.every((id, index) => id === ids[index])) return;
  await mutateSettings('/api/accounts/reorder', {
    method: 'PUT', headers: jsonHeaders(), body: JSON.stringify({ ids: next })
  }, '帳戶順序已更新。');
}

function handleV21Build15CategoryDragOver(event) {
  if (!cyV21Build15Drag || !['group', 'category'].includes(cyV21Build15Drag.type)) return;
  event.preventDefault();
  event.dataTransfer.dropEffect = 'move';
  clearV21Build15DropMarkers();

  if (cyV21Build15Drag.type === 'group') {
    const group = event.target.closest('[data-group-id]');
    if (!group) return;
    const head = group.querySelector('.v21-category-group-head') || group;
    group.classList.add(v21Build15AfterMidpoint(event, head) ? 'v21-drop-after' : 'v21-drop-before');
    return;
  }

  const categoryRow = event.target.closest('[data-v21-category-row]');
  if (categoryRow) {
    categoryRow.classList.add(v21Build15AfterMidpoint(event, categoryRow) ? 'v21-drop-after' : 'v21-drop-before');
    categoryRow.closest('[data-group-id]')?.classList.add('v21-drop-group');
    return;
  }
  event.target.closest('[data-group-id]')?.classList.add('v21-drop-group');
}

async function handleV21Build15CategoryDrop(event) {
  if (!cyV21Build15Drag || typeof state !== 'object') return;
  const drag = { ...cyV21Build15Drag };
  if (drag.type === 'group') {
    event.preventDefault();
    const group = event.target.closest('[data-group-id]');
    const targetId = Number(group?.dataset.groupId || 0);
    const head = group?.querySelector('.v21-category-group-head') || group;
    const after = group && head ? v21Build15AfterMidpoint(event, head) : true;
    const ids = (state.groups || []).filter(item => item.kind === state.settingsKind).map(item => Number(item.id));
    const next = v21Build15MoveId(ids, drag.id, targetId, after);
    finishV21Build15Drag();
    if (!next || next.every((id, index) => id === ids[index])) return;
    await mutateSettings('/api/category-groups/reorder', {
      method: 'PUT', headers: jsonHeaders(), body: JSON.stringify({ kind: state.settingsKind, ids: next })
    }, '大分類順序已更新。');
    return;
  }

  if (drag.type !== 'category') return;
  event.preventDefault();
  const targetGroup = event.target.closest('[data-group-id]');
  const targetGroupId = Number(targetGroup?.dataset.groupId || 0);
  if (!Number.isInteger(targetGroupId) || targetGroupId <= 0) {
    finishV21Build15Drag();
    return;
  }

  const targetRow = event.target.closest('[data-v21-category-row]');
  const targetId = Number(targetRow?.dataset.v21CategoryRow || 0);
  if (targetId === drag.id) {
    finishV21Build15Drag();
    return;
  }
  const after = targetRow ? v21Build15AfterMidpoint(event, targetRow) : true;
  const payload = v21Build15CategoryOrderPayload(drag.id, targetGroupId, targetId, after);
  finishV21Build15Drag();
  if (!payload) return;
  await mutateSettings('/api/categories/reorder', {
    method: 'PUT', headers: jsonHeaders(), body: JSON.stringify({ kind: state.settingsKind, groups: payload })
  }, '科目順序已更新。');
}

function v21Build15MoveId(ids, sourceId, targetId, after) {
  if (!ids.includes(sourceId)) return null;
  if (targetId === sourceId) return [...ids];
  const next = ids.filter(id => id !== sourceId);
  if (!targetId || !next.includes(targetId)) {
    next.push(sourceId);
    return next;
  }
  let index = next.indexOf(targetId);
  if (after) index += 1;
  next.splice(index, 0, sourceId);
  return next;
}

function v21Build15CategoryOrderPayload(sourceId, targetGroupId, targetId, after) {
  if (targetId === sourceId) return null;
  const groups = (state.groups || []).filter(group => group.kind === state.settingsKind);
  if (!groups.some(group => Number(group.id) === targetGroupId)) return null;
  const payload = groups.map(group => ({
    groupId: Number(group.id),
    categoryIds: (state.categories || [])
      .filter(category => category.kind === state.settingsKind && Number(category.group_id) === Number(group.id))
      .map(category => Number(category.id))
  }));
  if (!payload.some(group => group.categoryIds.includes(sourceId))) return null;
  payload.forEach(group => { group.categoryIds = group.categoryIds.filter(id => id !== sourceId); });
  const target = payload.find(group => group.groupId === targetGroupId);
  let index = target.categoryIds.length;
  if (targetId && target.categoryIds.includes(targetId)) {
    index = target.categoryIds.indexOf(targetId) + (after ? 1 : 0);
  }
  target.categoryIds.splice(index, 0, sourceId);
  return payload;
}

function v21Build15AfterMidpoint(event, element) {
  const rect = element.getBoundingClientRect();
  return event.clientY > rect.top + rect.height / 2;
}

function clearV21Build15DropMarkers() {
  document.querySelectorAll('.v21-drop-before, .v21-drop-after, .v21-drop-group').forEach(element =>
    element.classList.remove('v21-drop-before', 'v21-drop-after', 'v21-drop-group')
  );
}

function finishV21Build15Drag() {
  document.querySelectorAll('.v21-is-dragging').forEach(element => element.classList.remove('v21-is-dragging'));
  clearV21Build15DropMarkers();
  document.body.classList.remove('v21-manager-dragging');
  cyV21Build15Drag = null;
}

function v21Build15Escape(value) {
  return String(value ?? '')
    .replaceAll('&', '&amp;')
    .replaceAll('<', '&lt;')
    .replaceAll('>', '&gt;')
    .replaceAll('"', '&quot;')
    .replaceAll("'", '&#039;');
}
