let cyCategoryTransferId = null;

window.addEventListener('load', () => {
  setupCategoryTransfer();
  setupSequentialLockControls();
});

function setupCategoryTransfer() {
  if (!els.categoryManager) return;
  ensureCategoryTransferDialog();

  els.categoryManager.addEventListener('click', event => {
    const button = event.target.closest('[data-v12-category-transfer]');
    if (!button) return;
    event.preventDefault();
    event.stopPropagation();
    openCategoryTransfer(Number(button.dataset.v12CategoryTransfer));
  });

  const observer = new MutationObserver(injectCategoryTransferButtons);
  observer.observe(els.categoryManager, { childList: true, subtree: true });
  injectCategoryTransferButtons();
}

function injectCategoryTransferButtons() {
  for (const itemElement of els.categoryManager?.querySelectorAll('.category-item') || []) {
    const rename = itemElement.querySelector('[data-category-rename]');
    const actions = rename?.parentElement;
    if (!rename || !actions || actions.querySelector('[data-v12-category-transfer]')) continue;

    const id = Number(rename.dataset.categoryRename);
    const category = state.categories.find(item => Number(item.id) === id);
    if (!category) continue;
    const targetGroups = state.groups.filter(group =>
      group.kind === category.kind && Number(group.id) !== Number(category.group_id)
    );
    if (!targetGroups.length) continue;

    const button = document.createElement('button');
    button.type = 'button';
    button.className = 'mini-button category-transfer-button';
    button.dataset.v12CategoryTransfer = String(id);
    button.textContent = '移動';
    button.title = '移動到其他大分類';
    actions.append(document.createTextNode(' '), button);
  }
}

function ensureCategoryTransferDialog() {
  if (document.querySelector('#categoryTransferDialog')) return;
  const dialog = document.createElement('dialog');
  dialog.id = 'categoryTransferDialog';
  dialog.className = 'modal small-modal category-transfer-dialog';
  dialog.innerHTML = `
    <div class="modal-header">
      <div><h2>移動科目</h2><p id="categoryTransferDescription"></p></div>
      <button class="icon-button" type="button" data-v12-transfer-close aria-label="關閉">×</button>
    </div>
    <div class="form-grid">
      <label><span>移動到大分類</span><select id="categoryTransferTarget"></select></label>
    </div>
    <p class="hint category-transfer-note">只可在相同收支類型的大分類之間移動；既有歷史記帳資料不會被改寫。</p>
    <div id="categoryTransferMessage" class="dialog-message"></div>
    <div class="modal-actions">
      <button class="secondary" type="button" data-v12-transfer-close>取消</button>
      <button id="categoryTransferConfirm" class="primary" type="button">確認移動</button>
    </div>
  `;
  document.body.append(dialog);
  dialog.querySelectorAll('[data-v12-transfer-close]').forEach(button =>
    button.addEventListener('click', () => dialog.close())
  );
  dialog.querySelector('#categoryTransferConfirm')?.addEventListener('click', confirmCategoryTransfer);
}

function openCategoryTransfer(id) {
  const category = state.categories.find(item => Number(item.id) === Number(id));
  if (!category) return;
  const targetGroups = state.groups.filter(group =>
    group.kind === category.kind && Number(group.id) !== Number(category.group_id)
  );
  if (!targetGroups.length) {
    setDialogMessage(els.settingsMessage, '目前沒有其他可移動的大分類。', true);
    return;
  }

  cyCategoryTransferId = Number(id);
  const dialog = document.querySelector('#categoryTransferDialog');
  const description = dialog?.querySelector('#categoryTransferDescription');
  const target = dialog?.querySelector('#categoryTransferTarget');
  const message = dialog?.querySelector('#categoryTransferMessage');
  if (!dialog || !target) return;

  if (description) description.textContent = `「${category.name}」目前位於「${category.group_name}」。`;
  target.innerHTML = targetGroups.map(group =>
    `<option value="${Number(group.id)}">${escapeHtml(group.name)}</option>`
  ).join('');
  if (message) setDialogMessage(message, '');
  dialog.showModal();
  target.focus();
}

async function confirmCategoryTransfer() {
  const dialog = document.querySelector('#categoryTransferDialog');
  const target = dialog?.querySelector('#categoryTransferTarget');
  const message = dialog?.querySelector('#categoryTransferMessage');
  const button = dialog?.querySelector('#categoryTransferConfirm');
  const groupId = Number(target?.value);
  if (!Number.isInteger(cyCategoryTransferId) || cyCategoryTransferId <= 0 || !Number.isInteger(groupId) || groupId <= 0) {
    return setDialogMessage(message, '移動資料不完整。', true);
  }

  if (button) button.disabled = true;
  try {
    const result = await api(`/api/categories/${cyCategoryTransferId}/group`, {
      method: 'PUT',
      headers: jsonHeaders(),
      body: JSON.stringify({ groupId })
    });
    await refreshBootstrap();
    dialog.close();
    setDialogMessage(els.settingsMessage, result.moved === false ? '科目已位於該大分類。' : `科目已移動到「${result.groupName || ''}」。`);
  } catch (error) {
    setDialogMessage(message, error.message, true);
  } finally {
    if (button) button.disabled = false;
  }
}

function setupSequentialLockControls() {
  const pane = document.querySelector('[data-settings-pane="lock"]');
  const form = pane?.querySelector('.lock-form');
  if (!pane || !form || document.querySelector('#lockStepControls')) return;

  const controls = document.createElement('div');
  controls.id = 'lockStepControls';
  controls.className = 'lock-step-panel';
  controls.innerHTML = `
    <div class="lock-step-copy">
      <strong>逐月鎖帳</strong>
      <span>日常操作建議使用逐月前進；上方直接指定月份仍保留給管理者調整。</span>
    </div>
    <div class="lock-step-actions">
      <button id="lockStepBackward" class="secondary compact" type="button">← 退回一個月</button>
      <button id="lockStepForward" class="primary compact" type="button">鎖定下一個月 →</button>
    </div>
  `;
  form.insertAdjacentElement('afterend', controls);

  controls.querySelector('#lockStepBackward')?.addEventListener('click', () => stepLock('backward'));
  controls.querySelector('#lockStepForward')?.addEventListener('click', () => stepLock('forward'));

  const observer = new MutationObserver(updateLockStepUi);
  if (els.lockStatusText) observer.observe(els.lockStatusText, { childList: true, subtree: true, characterData: true });
  updateLockStepUi();
}

function updateLockStepUi() {
  const backward = document.querySelector('#lockStepBackward');
  const forward = document.querySelector('#lockStepForward');
  if (backward) backward.disabled = !state.lockedThrough;

  const currentMonth = localDateString(new Date()).slice(0, 7);
  if (forward) {
    const atCurrentMonth = Boolean(state.lockedThrough && state.lockedThrough >= currentMonth);
    forward.disabled = atCurrentMonth;
    forward.title = atCurrentMonth ? '逐月鎖帳已到本月；若需特殊調整請使用上方直接指定月份。' : '';
  }
}

async function stepLock(direction) {
  const backward = document.querySelector('#lockStepBackward');
  const forward = document.querySelector('#lockStepForward');
  if (backward) backward.disabled = true;
  if (forward) forward.disabled = true;
  setDialogMessage(els.settingsMessage, '');

  try {
    const data = await api('/api/settings/lock/step', {
      method: 'PUT',
      headers: jsonHeaders(),
      body: JSON.stringify({ direction })
    });
    state.lockedThrough = data.lockedThrough || null;
    els.lockedThrough.value = state.lockedThrough || '';
    renderSettings();
    setDialogMessage(els.settingsMessage, data.message || '鎖帳設定已更新。');
    await loadTransactions();
  } catch (error) {
    setDialogMessage(els.settingsMessage, error.message, true);
  } finally {
    updateLockStepUi();
  }
}
