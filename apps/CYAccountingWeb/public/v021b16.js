const CY_V21_BUILD16_VERSION = 'V0.21.0 Build 16';
const CY_V21_BUILD16_DESKTOP = '(min-width: 1024px)';
let cyV21Build16Saving = false;

ensureV21Build16Stylesheet();
syncV21Build16Version();

const runV21Build16 = () => {
  syncV21Build16Version();
  setupV21Build16CategoryManager();
  setupV21Build16OptimisticDrag();
};

if (document.readyState === 'complete') setTimeout(runV21Build16, 0);
else window.addEventListener('load', () => setTimeout(runV21Build16, 0), { once: true });

function ensureV21Build16Stylesheet() {
  if (document.querySelector('link[href="/v021b16.css"]')) return;
  const link = document.createElement('link');
  link.rel = 'stylesheet';
  link.href = '/v021b16.css';
  document.head.appendChild(link);
}

function syncV21Build16Version() {
  const version = document.querySelector('.version');
  if (version) version.textContent = CY_V21_BUILD16_VERSION;
}

function setupV21Build16CategoryManager() {
  if (typeof window.renderCategoryManager === 'function') window.renderCategoryManager = renderV21Build16CategoryManager;
  const host = document.querySelector('#categoryManager');
  if (!host) return;

  if (host.dataset.v21Build16KindBound !== '1') {
    host.dataset.v21Build16KindBound = '1';
    host.addEventListener('click', event => {
      const button = event.target.closest('[data-v21-manager-kind]');
      if (!button || typeof state !== 'object') return;
      event.preventDefault();
      event.stopImmediatePropagation();
      const kind = button.dataset.v21ManagerKind;
      if (!['income', 'expense'].includes(kind) || kind === state.settingsKind) return;
      state.settingsKind = kind;
      renderV21Build16CategoryManager();
    }, true);
  }

  renderV21Build16CategoryManager();
}

function renderV21Build16CategoryManager() {
  const host = document.querySelector('#categoryManager');
  const pane = document.querySelector('[data-settings-pane="categories"]');
  if (!host || !pane || typeof state !== 'object') return;

  const kind = state.settingsKind === 'income' ? 'income' : 'expense';
  pane.classList.toggle('v21-category-kind-income', kind === 'income');
  pane.classList.toggle('v21-category-kind-expense', kind === 'expense');

  const toolbar = `<div class="v21-category-toolbar">
    <div class="entry-kind-switch v21-category-kind-switch" role="group" aria-label="收入或支出">
      <button type="button" class="kind-button${kind === 'income' ? ' active' : ''}" data-kind="income" data-v21-manager-kind="income">收入</button>
      <button type="button" class="kind-button${kind === 'expense' ? ' active' : ''}" data-kind="expense" data-v21-manager-kind="expense">支出</button>
    </div>
  </div>`;

  const groups = (state.groups || []).filter(group => group.kind === kind);
  if (!groups.length) {
    host.innerHTML = `${toolbar}<div class="empty v21-category-empty-state">目前沒有大分類。</div>`;
    return;
  }

  const list = groups.map(group => {
    const groupId = Number(group.id);
    const categories = (state.categories || []).filter(category =>
      category.kind === kind && Number(category.group_id) === groupId
    );
    const items = categories.map(category => {
      const id = Number(category.id);
      const favorite = Number(category.is_favorite) === 1;
      return `<div class="category-item v21-category-manager-row" data-v21-category-row="${id}" data-v21-category-group="${groupId}">
        <button type="button" class="v21-drag-handle" draggable="true" data-v21-drag-category="${id}" title="拖曳調整科目順序或分類" aria-label="拖曳調整科目順序或分類">⠿</button>
        <button type="button" class="v21-favorite-chip${favorite ? ' active' : ''}" data-category-favorite="${id}" title="${favorite ? '取消常用科目' : '設為常用科目'}" aria-label="${favorite ? '取消常用科目' : '設為常用科目'}">${favorite ? '★' : '☆'}</button>
        <span class="v21-editable-name" data-v21-category-name="${id}">${v21Build16Escape(category.name)}</span>
        <button type="button" class="mini-button v21-edit-name-button" data-category-rename="${id}" title="編輯科目名稱" aria-label="編輯科目名稱">✎</button>
        <button type="button" class="mini-button danger v21-manager-delete" data-category-delete="${id}">刪除</button>
      </div>`;
    }).join('');

    return `<section class="category-group v21-category-group" data-group-id="${groupId}">
      <div class="category-group-head v21-category-group-head">
        <button type="button" class="v21-drag-handle" draggable="true" data-v21-drag-group="${groupId}" title="拖曳調整大分類順序" aria-label="拖曳調整大分類順序">⠿</button>
        <strong class="category-group-title v21-editable-name" data-v21-group-name="${groupId}">${v21Build16Escape(group.name)}</strong>
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
  }).join('');

  host.innerHTML = `${toolbar}<div class="v21-category-manager-list">${list}</div>`;
}

function setupV21Build16OptimisticDrag() {
  const accountHost = document.querySelector('#accountRows');
  const categoryHost = document.querySelector('#categoryManager');
  if (accountHost && accountHost.dataset.v21Build16DropBound !== '1') {
    accountHost.dataset.v21Build16DropBound = '1';
    accountHost.addEventListener('drop', handleV21Build16AccountDrop, true);
  }
  if (categoryHost && categoryHost.dataset.v21Build16DropBound !== '1') {
    categoryHost.dataset.v21Build16DropBound = '1';
    categoryHost.addEventListener('drop', handleV21Build16CategoryDrop, true);
  }
}

async function handleV21Build16AccountDrop(event) {
  if (!window.matchMedia(CY_V21_BUILD16_DESKTOP).matches || cyV21Build16Saving) return;
  if (typeof cyV21Build15Drag === 'undefined' || cyV21Build15Drag?.type !== 'account' || typeof state !== 'object') return;

  event.preventDefault();
  event.stopImmediatePropagation();
  const sourceId = Number(cyV21Build15Drag.id);
  const row = event.target.closest('[data-v21-account-row]');
  const targetId = Number(row?.dataset.v21AccountRow || 0);
  const after = row ? v21Build16AfterMidpoint(event, row) : true;
  const previous = [...(state.accounts || [])];
  const ids = previous.map(item => Number(item.id));
  const nextIds = v21Build16MoveId(ids, sourceId, targetId, after);
  finishV21Build15Drag?.();
  if (!nextIds || nextIds.every((id, index) => id === ids[index])) return;

  state.accounts = v21Build16OrderObjects(previous, nextIds);
  renderV21Build15AccountManager?.();
  const selectedAccount = document.querySelector('#accountName')?.value || '';
  if (typeof renderAccounts === 'function') renderAccounts(selectedAccount);
  if (typeof syncV21Build8AccountChoices === 'function') syncV21Build8AccountChoices();

  await persistV21Build16Optimistic(
    '/api/accounts/reorder',
    { ids: nextIds },
    '帳戶順序已更新。',
    () => {
      state.accounts = previous;
      renderV21Build15AccountManager?.();
      if (typeof renderAccounts === 'function') renderAccounts(selectedAccount);
      if (typeof syncV21Build8AccountChoices === 'function') syncV21Build8AccountChoices();
    }
  );
}

async function handleV21Build16CategoryDrop(event) {
  if (!window.matchMedia(CY_V21_BUILD16_DESKTOP).matches || cyV21Build16Saving) return;
  if (typeof cyV21Build15Drag === 'undefined' || !cyV21Build15Drag || typeof state !== 'object') return;
  if (!['group', 'category'].includes(cyV21Build15Drag.type)) return;

  event.preventDefault();
  event.stopImmediatePropagation();
  const drag = { type: cyV21Build15Drag.type, id: Number(cyV21Build15Drag.id) };
  const kind = state.settingsKind === 'income' ? 'income' : 'expense';

  if (drag.type === 'group') {
    const group = event.target.closest('[data-group-id]');
    const targetId = Number(group?.dataset.groupId || 0);
    const head = group?.querySelector('.v21-category-group-head') || group;
    const after = group && head ? v21Build16AfterMidpoint(event, head) : true;
    const previous = [...(state.groups || [])];
    const ids = previous.filter(item => item.kind === kind).map(item => Number(item.id));
    const nextIds = v21Build16MoveId(ids, drag.id, targetId, after);
    finishV21Build15Drag?.();
    if (!nextIds || nextIds.every((id, index) => id === ids[index])) return;

    state.groups = v21Build16ReplaceKindOrder(previous, kind, nextIds);
    renderV21Build16CategoryManager();
    await persistV21Build16Optimistic(
      '/api/category-groups/reorder',
      { kind, ids: nextIds },
      '大分類順序已更新。',
      () => {
        state.groups = previous;
        renderV21Build16CategoryManager();
      }
    );
    return;
  }

  const targetGroup = event.target.closest('[data-group-id]');
  const targetGroupId = Number(targetGroup?.dataset.groupId || 0);
  if (!Number.isInteger(targetGroupId) || targetGroupId <= 0) {
    finishV21Build15Drag?.();
    return;
  }
  const targetRow = event.target.closest('[data-v21-category-row]');
  const targetId = Number(targetRow?.dataset.v21CategoryRow || 0);
  const after = targetRow ? v21Build16AfterMidpoint(event, targetRow) : true;
  const previous = [...(state.categories || [])];
  const payload = v21Build16CategoryPayload(previous, kind, drag.id, targetGroupId, targetId, after);
  finishV21Build15Drag?.();
  if (!payload) return;

  state.categories = v21Build16ApplyCategoryPayload(previous, kind, payload);
  renderV21Build16CategoryManager();
  await persistV21Build16Optimistic(
    '/api/categories/reorder',
    { kind, groups: payload },
    '科目順序已更新。',
    () => {
      state.categories = previous;
      renderV21Build16CategoryManager();
    }
  );
}

async function persistV21Build16Optimistic(path, body, successMessage, rollback) {
  const message = document.querySelector('#settingsMessage');
  cyV21Build16Saving = true;
  document.body.classList.add('v21-reorder-saving');
  if (message) setDialogMessage(message, '');
  try {
    await api(path, { method: 'PUT', headers: jsonHeaders(), body: JSON.stringify(body) });
    if (message) setDialogMessage(message, successMessage);
    return true;
  } catch (error) {
    rollback?.();
    if (message) setDialogMessage(message, error.message || '排序儲存失敗。', true);
    return false;
  } finally {
    cyV21Build16Saving = false;
    document.body.classList.remove('v21-reorder-saving');
  }
}

function v21Build16MoveId(ids, sourceId, targetId, after) {
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

function v21Build16OrderObjects(items, ids) {
  const byId = new Map(items.map(item => [Number(item.id), item]));
  return ids.map(id => byId.get(id)).filter(Boolean);
}

function v21Build16ReplaceKindOrder(items, kind, ids) {
  const byId = new Map(items.filter(item => item.kind === kind).map(item => [Number(item.id), item]));
  const ordered = ids.map((id, index) => ({ ...byId.get(id), sort_order: index + 1 })).filter(Boolean);
  let cursor = 0;
  return items.map(item => item.kind === kind ? ordered[cursor++] : item);
}

function v21Build16CategoryPayload(items, kind, sourceId, targetGroupId, targetId, after) {
  const groups = (state.groups || []).filter(group => group.kind === kind);
  if (!groups.some(group => Number(group.id) === targetGroupId)) return null;
  const payload = groups.map(group => ({
    groupId: Number(group.id),
    categoryIds: items
      .filter(category => category.kind === kind && Number(category.group_id) === Number(group.id))
      .map(category => Number(category.id))
  }));
  if (!payload.some(group => group.categoryIds.includes(sourceId))) return null;
  if (targetId === sourceId) return null;

  payload.forEach(group => { group.categoryIds = group.categoryIds.filter(id => id !== sourceId); });
  const target = payload.find(group => group.groupId === targetGroupId);
  let index = target.categoryIds.length;
  if (targetId && target.categoryIds.includes(targetId)) {
    index = target.categoryIds.indexOf(targetId) + (after ? 1 : 0);
  }
  target.categoryIds.splice(index, 0, sourceId);
  return payload;
}

function v21Build16ApplyCategoryPayload(items, kind, payload) {
  const byId = new Map(items.filter(item => item.kind === kind).map(item => [Number(item.id), item]));
  const ordered = [];
  for (const group of payload) {
    group.categoryIds.forEach((id, index) => {
      const item = byId.get(Number(id));
      if (item) ordered.push({ ...item, group_id: Number(group.groupId), sort_order: index + 1 });
    });
  }
  let cursor = 0;
  return items.map(item => item.kind === kind ? ordered[cursor++] : item);
}

function v21Build16AfterMidpoint(event, element) {
  const rect = element.getBoundingClientRect();
  return event.clientY > rect.top + rect.height / 2;
}

function v21Build16Escape(value) {
  return String(value ?? '')
    .replaceAll('&', '&amp;')
    .replaceAll('<', '&lt;')
    .replaceAll('>', '&gt;')
    .replaceAll('"', '&quot;')
    .replaceAll("'", '&#039;');
}
