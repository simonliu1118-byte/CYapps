/* CYAccountingWeb category management functional module. */

window.addEventListener('load', () => {
  setupSequentialLockControls();
});

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