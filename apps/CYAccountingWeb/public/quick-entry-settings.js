/* CYAccountingWeb quick entry settings functional module. */

let cyDateDigitBuffer = '';
let cyDateDigitAt = 0;
let cyQuickEntrySettingsLoaded = false;

window.addEventListener('load', () => {
  bindDateQuickEntry();
  setupQuickEntrySettingsPane();
  updateQuickEntryKeyboardHint();
});

function bindDateQuickEntry() {
  if (!els.txDate || els.txDate.dataset.quickDateBound === '1') return;
  els.txDate.dataset.quickDateBound = '1';

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

function updateQuickEntryKeyboardHint() {
  const hint = document.querySelector('.keyboard-hint');
  if (!hint) return;
  hint.innerHTML = '鍵盤：日期 Enter → 帳戶 Enter → 科目 Enter → 摘要 Enter → 金額 Enter 儲存　｜　<kbd>Tab</kbd> 切換收入／支出　｜　日期可輸入 <kbd>0924</kbd> / <kbd>20260924</kbd>，<kbd>Ctrl</kbd>+<kbd>↑↓</kbd> ±1 天';
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
    await loadQuickEntrySettings(true);
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
