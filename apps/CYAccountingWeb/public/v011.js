let cyDateDigitBuffer = '';
let cyDateDigitAt = 0;
let cyQuickEntrySettingsLoaded = false;

window.addEventListener('load', () => {
  bindDateQuickEntry();
  setupQuickEntrySettingsPane();
  setupOrderingControls();
  updateKeyboardHintV11();
});

function bindDateQuickEntry() {
  if (!els.txDate || els.txDate.dataset.v11DateBound === '1') return;
  els.txDate.dataset.v11DateBound = '1';

  els.txDate.addEventListener('keydown', event => {
    if (event.isComposing) return;

    if (event.ctrlKey && !event.altKey && !event.metaKey && (event.key === 'ArrowUp' || event.key === 'ArrowDown')) {
      event.preventDefault();
      cyDateDigitBuffer = '';
      stepEntryDate(event.key === 'ArrowUp' ? 1 : -1);
      return;
    }

    if (/^\d$/.test(event.key) && !event.ctrlKey && !event.altKey && !event.metaKey) {
      event.preventDefault();
      const now = Date.now();
      if (now - cyDateDigitAt > 1800) cyDateDigitBuffer = '';
      cyDateDigitAt = now;
      cyDateDigitBuffer += event.key;
      if (cyDateDigitBuffer.length > 8) cyDateDigitBuffer = event.key;
      applyBufferedDateIfComplete();
      return;
    }

    if (event.key === 'Escape' || event.key === 'Backspace' || event.key === 'Delete' || event.key === 'Enter' || event.key === 'Tab') {
      cyDateDigitBuffer = '';
    }
  });

  els.txDate.addEventListener('blur', () => { cyDateDigitBuffer = ''; });
}

function applyBufferedDateIfComplete() {
  const raw = cyDateDigitBuffer;
  if (raw.length === 4) {
    const year = Number(String(els.txDate.value || localDateString(new Date())).slice(0, 4));
    const month = Number(raw.slice(0, 2));
    const day = Number(raw.slice(2, 4));
    const value = validDateValue(year, month, day);
    if (value) {
      setEntryDateValue(value);
      cyDateDigitBuffer = '';
    }
    return;
  }

  if (raw.length === 8) {
    const value = validDateValue(Number(raw.slice(0, 4)), Number(raw.slice(4, 6)), Number(raw.slice(6, 8)));
    if (value) setEntryDateValue(value);
    cyDateDigitBuffer = '';
  }
}

function validDateValue(year, month, day) {
  if (!Number.isInteger(year) || year < 1900 || year > 2200 || month < 1 || month > 12 || day < 1 || day > 31) return '';
  const date = new Date(year, month - 1, day);
  if (date.getFullYear() !== year || date.getMonth() !== month - 1 || date.getDate() !== day) return '';
  return localDateString(date);
}

function stepEntryDate(delta) {
  const current = /^\d{4}-\d{2}-\d{2}$/.test(els.txDate.value || '') ? els.txDate.value : localDateString(new Date());
  const [year, month, day] = current.split('-').map(Number);
  const date = new Date(year, month - 1, day + delta);
  setEntryDateValue(localDateString(date));
}

function setEntryDateValue(value) {
  els.txDate.value = value;
  els.txDate.dispatchEvent(new Event('change', { bubbles: true }));
}

function updateKeyboardHintV11() {
  const hint = document.querySelector('.keyboard-hint');
  if (!hint) return;
  hint.innerHTML = '鍵盤：日期 Enter → 帳戶 Enter → 科目 Enter → 摘要 Enter → 金額 Enter 儲存　｜　<kbd>F2</kbd> 切換收入／支出　｜　日期可輸入 <kbd>0924</kbd> / <kbd>20260924</kbd>，<kbd>Ctrl</kbd>+<kbd>↑↓</kbd> ±1 天';
}

function setupQuickEntrySettingsPane() {
  const nav = document.querySelector('.settings-nav');
  const content = document.querySelector('.settings-content');
  if (!nav || !content || document.querySelector('[data-settings-tab="quick"]')) return;

  const tab = document.createElement('button');
  tab.type = 'button';
  tab.className = 'settings-tab';
  tab.dataset.settingsTab = 'quick';
  tab.textContent = '快速輸入';

  const pane = document.createElement('section');
  pane.className = 'settings-pane';
  pane.dataset.settingsPane = 'quick';
  pane.innerHTML = `
    <h3>快速輸入設定</h3>
    <p class="hint">調整「常用摘要」的統計方式。常用摘要仍依帳戶、收入／支出與科目分開計算，最多顯示 10 個。</p>
    <div class="quick-settings-grid">
      <label><span>統計依據</span><select id="frequentSummaryBasis"><option value="tx_date">帳務日期最近</option><option value="created_at">近期輸入最近</option></select></label>
      <label><span>統計最近筆數</span><input id="frequentSummaryRecentCount" type="number" min="10" max="1000" step="10" inputmode="numeric"></label>
      <label><span>最低出現次數</span><input id="frequentSummaryMinCount" type="number" min="2" max="20" step="1" inputmode="numeric"></label>
    </div>
    <p class="hint quick-settings-explain"><strong>帳務日期最近：</strong>依記帳日期選取最近資料。　<strong>近期輸入最近：</strong>依實際新增時間選取最近資料。</p>
    <div id="quickEntrySettingsMessage" class="dialog-message"></div>
    <div class="quick-settings-actions"><button id="saveQuickEntrySettings" class="primary" type="button">儲存快速輸入設定</button></div>
  `;

  const lockTab = nav.querySelector('[data-settings-tab="lock"]');
  if (lockTab) nav.insertBefore(tab, lockTab); else nav.append(tab);
  const lockPane = content.querySelector('[data-settings-pane="lock"]');
  if (lockPane) content.insertBefore(pane, lockPane); else content.prepend(pane);

  els.settingsTabs?.push(tab);
  els.settingsPanes?.push(pane);

  tab.addEventListener('click', async () => {
    setSettingsTab('quick');
    await loadQuickEntrySettings();
  });
  pane.querySelector('#saveQuickEntrySettings')?.addEventListener('click', saveQuickEntrySettings);
}

async function loadQuickEntrySettings(force = false) {
  if (cyQuickEntrySettingsLoaded && !force) return;
  const message = document.querySelector('#quickEntrySettingsMessage');
  try {
    const data = await api('/api/settings/quick-entry');
    const settings = data.settings || {};
    const basis = document.querySelector('#frequentSummaryBasis');
    const recent = document.querySelector('#frequentSummaryRecentCount');
    const minimum = document.querySelector('#frequentSummaryMinCount');
    if (basis) basis.value = settings.basis || 'tx_date';
    if (recent) recent.value = String(settings.recentCount ?? 100);
    if (minimum) minimum.value = String(settings.minCount ?? 3);
    cyQuickEntrySettingsLoaded = true;
    if (message) setDialogMessage(message, '');
  } catch (error) {
    if (message) setDialogMessage(message, error.message, true);
  }
}

async function saveQuickEntrySettings() {
  const basis = document.querySelector('#frequentSummaryBasis')?.value || '';
  const recentCount = Number(document.querySelector('#frequentSummaryRecentCount')?.value);
  const minCount = Number(document.querySelector('#frequentSummaryMinCount')?.value);
  const button = document.querySelector('#saveQuickEntrySettings');
  const message = document.querySelector('#quickEntrySettingsMessage');

  if (!Number.isInteger(recentCount) || recentCount < 10 || recentCount > 1000) {
    return setDialogMessage(message, '最近筆數必須為 10～1000。', true);
  }
  if (!Number.isInteger(minCount) || minCount < 2 || minCount > 20 || minCount > recentCount) {
    return setDialogMessage(message, '最低出現次數必須為 2～20，且不可大於最近筆數。', true);
  }

  if (button) button.disabled = true;
  try {
    await api('/api/settings/quick-entry', {
      method: 'PUT',
      headers: jsonHeaders(),
      body: JSON.stringify({ basis, recentCount, minCount })
    });
    cyQuickEntrySettingsLoaded = true;
    setDialogMessage(message, '快速輸入設定已儲存。');
    if (typeof loadFrequentSummaries === 'function') await loadFrequentSummaries();
  } catch (error) {
    setDialogMessage(message, error.message, true);
  } finally {
    if (button) button.disabled = false;
  }
}

function setupOrderingControls() {
  if (els.accountRows) {
    els.accountRows.addEventListener('click', handleV11OrderAction);
    const observer = new MutationObserver(injectAccountOrderButtons);
    observer.observe(els.accountRows, { childList: true, subtree: true });
  }
  if (els.categoryManager) {
    els.categoryManager.addEventListener('click', handleV11OrderAction);
    const observer = new MutationObserver(injectCategoryOrderButtons);
    observer.observe(els.categoryManager, { childList: true, subtree: true });
  }
  injectAccountOrderButtons();
  injectCategoryOrderButtons();
}

function injectAccountOrderButtons() {
  const rows = [...(els.accountRows?.querySelectorAll('.manager-row') || [])];
  rows.forEach((row, index) => {
    const rename = row.querySelector('[data-account-rename]');
    const actions = row.querySelector('.manager-row-actions');
    if (!rename || !actions || actions.querySelector('[data-v11-move-account]')) return;
    const id = rename.dataset.accountRename;
    actions.prepend(orderButton('account', id, 'down', index === rows.length - 1), orderButton('account', id, 'up', index === 0));
  });
}

function injectCategoryOrderButtons() {
  const groups = [...(els.categoryManager?.querySelectorAll('.category-group') || [])];
  groups.forEach((groupElement, groupIndex) => {
    const renameGroup = groupElement.querySelector('.category-group-head [data-group-rename]');
    const groupActions = renameGroup?.parentElement;
    if (renameGroup && groupActions && !groupActions.querySelector('[data-v11-move-group]')) {
      const id = renameGroup.dataset.groupRename;
      groupActions.prepend(orderButton('group', id, 'down', groupIndex === groups.length - 1), orderButton('group', id, 'up', groupIndex === 0));
    }

    const items = [...groupElement.querySelectorAll('.category-item')];
    items.forEach((item, index) => {
      const rename = item.querySelector('[data-category-rename]');
      const actions = rename?.parentElement;
      if (!rename || !actions || actions.querySelector('[data-v11-move-category]')) return;
      const id = rename.dataset.categoryRename;
      actions.prepend(orderButton('category', id, 'down', index === items.length - 1), orderButton('category', id, 'up', index === 0));
    });
  });
}

function orderButton(type, id, direction, disabled) {
  const button = document.createElement('button');
  button.type = 'button';
  button.className = 'mini-button order-button';
  button.dataset[`v11Move${type[0].toUpperCase()}${type.slice(1)}`] = String(id);
  button.dataset.direction = direction;
  button.disabled = disabled;
  button.title = direction === 'up' ? '往上移' : '往下移';
  button.setAttribute('aria-label', button.title);
  button.textContent = direction === 'up' ? '↑' : '↓';
  return button;
}

async function handleV11OrderAction(event) {
  const button = event.target.closest('[data-v11-move-account], [data-v11-move-group], [data-v11-move-category]');
  if (!button || button.disabled) return;
  event.preventDefault();
  event.stopPropagation();

  let path = '';
  if (button.dataset.v11MoveAccount) path = `/api/accounts/${button.dataset.v11MoveAccount}/move`;
  else if (button.dataset.v11MoveGroup) path = `/api/category-groups/${button.dataset.v11MoveGroup}/move`;
  else if (button.dataset.v11MoveCategory) path = `/api/categories/${button.dataset.v11MoveCategory}/move`;
  if (!path) return;

  button.disabled = true;
  setDialogMessage(els.settingsMessage, '');
  try {
    await api(path, {
      method: 'PUT',
      headers: jsonHeaders(),
      body: JSON.stringify({ direction: button.dataset.direction })
    });
    await refreshBootstrap();
    setDialogMessage(els.settingsMessage, '排序已更新。');
  } catch (error) {
    setDialogMessage(els.settingsMessage, error.message, true);
  }
}
