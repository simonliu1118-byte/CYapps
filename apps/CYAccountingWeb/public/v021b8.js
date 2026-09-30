const CY_V21_BUILD8_VERSION = 'V0.21.0 Build 9';
const CY_V21_BUILD8_SUMMARY_UNITS = 40;
const CY_V21_BUILD9_MOBILE = '(max-width: 767px)';

ensureV21Build8Stylesheet();
ensureV21Build9Stylesheet();

let cyV21Build8Started = false;

if (document.readyState === 'loading') {
  document.addEventListener('DOMContentLoaded', startV21Build8, { once: true });
} else {
  startV21Build8();
}
window.addEventListener('load', syncV21Build8AfterLoad, { once: true });

function startV21Build8() {
  if (cyV21Build8Started) return;
  cyV21Build8Started = true;
  runV21Build8Step('mobile-pages', setupV21Build9MobilePages);
  runV21Build8Step('mobile-account-picker', setupV21Build9MobileAccountPicker);
  runV21Build8Step('account-choices', setupV21Build8AccountChoices);
  runV21Build8Step('summary-limit', setupV21Build8SummaryLimit);
  runV21Build8Step('role-medal', setupV21Build8RoleMedal);
  runV21Build8Step('enter-hints', setupV21Build9EnterHints);
  runV21Build8Step('help-copy', syncV21Build9HelpCopy);
  runV21Build8Step('version', syncV21Build8Version);
}

function runV21Build8Step(name, task) {
  try {
    task();
  } catch (error) {
    console.error('cyaccounting_mobile_build8_step_failed', name, error instanceof Error ? error.message : 'unknown_error');
  }
}

function syncV21Build8AfterLoad() {
  startV21Build8();
  syncV21Build8Version();
  syncV21Build8AccountChoices();
  syncV21Build8RoleMedal();
  syncV21Build9AccountPickerLabel();
  syncV21Build9HelpCopy();
}

function ensureV21Build8Stylesheet() {
  if (document.querySelector('link[href^="/v021b8.css"]')) return;
  const link = document.createElement('link');
  link.rel = 'stylesheet';
  link.href = '/v021b8.css?v=0216b6';
  document.head.appendChild(link);
}

function ensureV21Build9Stylesheet() {
  if (document.querySelector('link[href^="/v021b9.css"]')) return;
  const link = document.createElement('link');
  link.rel = 'stylesheet';
  link.href = '/v021b9.css?v=0216b6';
  document.head.appendChild(link);
}

function syncV21Build8Version() {
  const version = document.querySelector('.version');
  if (version) version.textContent = CY_V21_BUILD8_VERSION;
}

function setupV21Build8AccountChoices() {
  const select = document.querySelector('#accountName');
  const host = document.querySelector('#entryAccountButtons');
  const row = document.querySelector('#entryAccountChoiceRow');
  if (!select || !host || !row) return;

  const mobile = window.matchMedia(CY_V21_BUILD9_MOBILE);
  row.hidden = false;
  const observer = new MutationObserver(syncV21Build8AccountChoices);
  observer.observe(select, { childList: true, subtree: true });
  select.addEventListener('change', syncV21Build8AccountChoices);

  host.addEventListener('click', event => {
    const button = event.target.closest('[data-entry-account]');
    if (!button) return;
    selectV21Build8Account(button.dataset.entryAccount || '', !mobile.matches);
    if (mobile.matches) {
      setV21Build9AccountPickerOpen(false);
      document.querySelector('#entryAccountPickerButton')?.focus();
    }
  });

  host.addEventListener('keydown', event => {
    const button = event.target.closest('[data-entry-account]');
    if (!button) return;
    const buttons = [...host.querySelectorAll('[data-entry-account]')];
    const index = buttons.indexOf(button);
    if (index < 0 || !['ArrowLeft', 'ArrowRight', 'ArrowUp', 'ArrowDown'].includes(event.key)) return;
    event.preventDefault();
    const delta = event.key === 'ArrowLeft' || event.key === 'ArrowUp' ? -1 : 1;
    const next = buttons[(index + delta + buttons.length) % buttons.length];
    if (next) selectV21Build8Account(next.dataset.entryAccount || '', true);
  });

  syncV21Build8AccountChoices();
}

function syncV21Build8AccountChoices() {
  const select = document.querySelector('#accountName');
  const host = document.querySelector('#entryAccountButtons');
  if (!select || !host) return;

  const options = [...select.options].filter(option => option.value);
  const signature = options.map(option => option.value).join('\u001f');
  if (host.dataset.accountSignature !== signature) {
    host.dataset.accountSignature = signature;
    host.innerHTML = options.map(option => `
      <button type="button" class="entry-account-choice" role="radio" data-entry-account="${v21Build8Escape(option.value)}" aria-checked="false" tabindex="-1">${v21Build8Escape(option.textContent || option.value)}</button>
    `).join('');
  }

  const selected = select.value;
  for (const button of host.querySelectorAll('[data-entry-account]')) {
    const active = button.dataset.entryAccount === selected;
    button.classList.toggle('active', active);
    button.setAttribute('aria-checked', active ? 'true' : 'false');
    button.tabIndex = active ? 0 : -1;
    button.title = active ? '目前使用中的帳戶' : `切換至帳戶「${button.dataset.entryAccount}」`;
  }
  syncV21Build9AccountPickerLabel();
}

function selectV21Build8Account(name, focus = false) {
  const select = document.querySelector('#accountName');
  const host = document.querySelector('#entryAccountButtons');
  if (!select || !host || ![...select.options].some(option => option.value === name)) return;
  if (select.value !== name) {
    select.value = name;
    select.dispatchEvent(new Event('change', { bubbles: true }));
  }
  syncV21Build8AccountChoices();
  if (focus) host.querySelector(`[data-entry-account="${CSS.escape(name)}"]`)?.focus();
}

function setupV21Build9MobileAccountPicker() {
  const row = document.querySelector('#entryAccountChoiceRow');
  const host = document.querySelector('#entryAccountButtons');
  if (!row || !host) return;

  let trigger = document.querySelector('#entryAccountPickerButton');
  if (!trigger) {
    trigger = document.createElement('button');
    trigger.type = 'button';
    trigger.id = 'entryAccountPickerButton';
    trigger.className = 'entry-account-picker-trigger';
    trigger.setAttribute('aria-haspopup', 'true');
    trigger.setAttribute('aria-expanded', 'false');
    trigger.innerHTML = '<span class="entry-account-picker-value">選擇帳戶</span><span class="entry-account-picker-arrow" aria-hidden="true">▾</span>';
    row.insertBefore(trigger, host);
  }

  trigger.addEventListener('click', () => {
    if (!window.matchMedia(CY_V21_BUILD9_MOBILE).matches) return;
    setV21Build9AccountPickerOpen(!row.classList.contains('mobile-picker-open'));
  });

  document.addEventListener('pointerdown', event => {
    if (!window.matchMedia(CY_V21_BUILD9_MOBILE).matches || row.contains(event.target)) return;
    setV21Build9AccountPickerOpen(false);
  });

  document.addEventListener('keydown', event => {
    if (event.key !== 'Escape' || !row.classList.contains('mobile-picker-open')) return;
    setV21Build9AccountPickerOpen(false);
    trigger.focus();
  });

  const mobile = window.matchMedia(CY_V21_BUILD9_MOBILE);
  const syncMode = () => {
    setV21Build9AccountPickerOpen(false);
    syncV21Build9AccountPickerLabel();
  };
  if (typeof mobile.addEventListener === 'function') mobile.addEventListener('change', syncMode);
  else mobile.addListener?.(syncMode);
  syncMode();
}

function setV21Build9AccountPickerOpen(open) {
  const row = document.querySelector('#entryAccountChoiceRow');
  const trigger = document.querySelector('#entryAccountPickerButton');
  const host = document.querySelector('#entryAccountButtons');
  if (!row || !trigger || !host) return;

  const isMobile = window.matchMedia(CY_V21_BUILD9_MOBILE).matches;
  if (!isMobile) {
    row.classList.remove('mobile-picker-open');
    trigger.setAttribute('aria-expanded', 'false');
    host.removeAttribute('aria-hidden');
    return;
  }

  const next = Boolean(open);
  row.classList.toggle('mobile-picker-open', next);
  trigger.setAttribute('aria-expanded', next ? 'true' : 'false');
  host.setAttribute('aria-hidden', next ? 'false' : 'true');
  if (next) {
    const active = host.querySelector('.entry-account-choice.active') || host.querySelector('.entry-account-choice');
    requestAnimationFrame(() => active?.focus());
  }
}

function syncV21Build9AccountPickerLabel() {
  const select = document.querySelector('#accountName');
  const trigger = document.querySelector('#entryAccountPickerButton');
  const value = trigger?.querySelector('.entry-account-picker-value');
  if (!select || !value) return;
  const option = select.selectedOptions?.[0];
  value.textContent = option?.textContent?.trim() || select.value || '選擇帳戶';
}

function setupV21Build9MobilePages() {
  const topbar = document.querySelector('.topbar');
  const shell = document.querySelector('.shell');
  const entry = shell?.querySelector('.entry-card');
  const ledger = shell?.querySelector('.ledger-card');
  if (!topbar || !shell || !entry || !ledger) return;

  let nav = document.querySelector('#mobileMainNav');
  if (!nav) {
    nav = document.createElement('nav');
    nav.id = 'mobileMainNav';
    nav.className = 'v21-mobile-main-nav';
    nav.setAttribute('aria-label', '主要頁面');
    nav.innerHTML = `
      <button type="button" class="active" data-mobile-page="entry" aria-selected="true">新增記帳</button>
      <button type="button" data-mobile-page="ledger" aria-selected="false">記帳資料</button>`;
    topbar.insertAdjacentElement('afterend', nav);
  }

  let current = 'entry';
  const mobile = window.matchMedia(CY_V21_BUILD9_MOBILE);

  const apply = page => {
    current = page === 'ledger' ? 'ledger' : 'entry';
    const enabled = mobile.matches;
    nav.hidden = !enabled;
    entry.classList.toggle('v21-mobile-page-hidden', enabled && current !== 'entry');
    ledger.classList.toggle('v21-mobile-page-hidden', enabled && current !== 'ledger');
    shell.dataset.mobilePage = enabled ? current : '';
    for (const button of nav.querySelectorAll('[data-mobile-page]')) {
      const active = button.dataset.mobilePage === current;
      button.classList.toggle('active', active);
      button.setAttribute('aria-selected', active ? 'true' : 'false');
    }
  };

  nav.addEventListener('click', event => {
    const button = event.target.closest('[data-mobile-page]');
    if (!button || !mobile.matches) return;
    apply(button.dataset.mobilePage);
    window.scrollTo({ top: 0, behavior: 'smooth' });
  });

  const syncMode = () => apply(current);
  if (typeof mobile.addEventListener === 'function') mobile.addEventListener('change', syncMode);
  else mobile.addListener?.(syncMode);
  apply('entry');
}

function setupV21Build8SummaryLimit() {
  const composing = new WeakSet();
  const fields = [document.querySelector('#summary'), document.querySelector('#editSummary')].filter(Boolean);

  for (const input of fields) {
    input.maxLength = 40;
    input.addEventListener('compositionstart', () => composing.add(input));
    input.addEventListener('compositionend', () => {
      composing.delete(input);
      enforceV21Build8Summary(input);
    });
    input.addEventListener('input', () => {
      if (!composing.has(input)) enforceV21Build8Summary(input);
    });
  }

  document.querySelector('#summarySuggestions')?.addEventListener('click', () => {
    setTimeout(() => {
      const summary = document.querySelector('#summary');
      if (summary) enforceV21Build8Summary(summary);
    }, 0);
  });

  const entryForm = document.querySelector('#transactionForm');
  entryForm?.addEventListener('submit', event => {
    const summary = document.querySelector('#summary');
    if (!summary || v21Build8WeightedUnits(summary.value) <= CY_V21_BUILD8_SUMMARY_UNITS) return;
    event.preventDefault();
    event.stopImmediatePropagation();
    if (typeof showMessage === 'function') showMessage('摘要不可超過 20 個中文字或 40 個英數字元。', true);
    summary.focus();
  }, true);

  const editForm = document.querySelector('#editTransactionForm');
  editForm?.addEventListener('submit', event => {
    const summary = document.querySelector('#editSummary');
    if (!summary || v21Build8WeightedUnits(summary.value) <= CY_V21_BUILD8_SUMMARY_UNITS) return;
    event.preventDefault();
    event.stopImmediatePropagation();
    const message = document.querySelector('#editMessage');
    if (message && typeof setDialogMessage === 'function') setDialogMessage(message, '摘要不可超過 20 個中文字或 40 個英數字元。', true);
    summary.focus();
  }, true);
}

function enforceV21Build8Summary(input) {
  if (v21Build8WeightedUnits(input.value) <= CY_V21_BUILD8_SUMMARY_UNITS) return;
  const trimmed = v21Build8TrimWeighted(input.value, CY_V21_BUILD8_SUMMARY_UNITS);
  const cursor = input.selectionStart ?? trimmed.length;
  input.value = trimmed;
  try { input.setSelectionRange(Math.min(cursor, trimmed.length), Math.min(cursor, trimmed.length)); } catch { /* no-op */ }
}

function v21Build8WeightedUnits(value) {
  let units = 0;
  for (const char of String(value || '')) units += v21Build8CharUnits(char);
  return units;
}

function v21Build8TrimWeighted(value, maxUnits) {
  let units = 0;
  let result = '';
  for (const char of String(value || '')) {
    const next = v21Build8CharUnits(char);
    if (units + next > maxUnits) break;
    units += next;
    result += char;
  }
  return result;
}

function v21Build8CharUnits(char) {
  const code = char.codePointAt(0) || 0;
  if (code <= 0x7f) return 1;
  if (code >= 0xff61 && code <= 0xff9f) return 1;
  return 2;
}

function setupV21Build8RoleMedal() {
  const target = document.querySelector('#currentUser');
  if (!target) return;
  const observer = new MutationObserver(syncV21Build8RoleMedal);
  observer.observe(target, { childList: true, subtree: true, characterData: true });
  syncV21Build8RoleMedal();
}

function syncV21Build8RoleMedal() {
  const target = document.querySelector('#currentUser');
  const role = String(target?.querySelector('.current-user-role')?.textContent || '').trim();
  if (!target) return;
  target.classList.toggle('role-super-admin', role === '超級管理員');
  target.classList.toggle('role-admin', role === '管理員');
}

function setupV21Build9EnterHints() {
  const date = document.querySelector('#txDate');
  const summary = document.querySelector('#summary');
  const amount = document.querySelector('#amount');
  if (date) date.setAttribute('enterkeyhint', 'next');
  if (summary) summary.setAttribute('enterkeyhint', 'next');
  if (amount) amount.setAttribute('enterkeyhint', 'done');
}

function syncV21Build9HelpCopy() {
  const hint = document.querySelector('.keyboard-hint');
  if (hint) {
    hint.innerHTML = '鍵盤：日期 Enter → 摘要 Enter → 金額 Enter 儲存 → 回摘要　｜　<kbd>Tab</kbd> 切換收入／支出　｜　日期可輸入 <kbd>0924</kbd> / <kbd>20260924</kbd>，<kbd>Ctrl</kbd>+<kbd>↑↓</kbd> ±1 天';
  }

  const grid = document.querySelector('#entryHelpPopover .entry-help-grid');
  if (grid) {
    grid.innerHTML = `
      <kbd>Enter</kbd><span>日期 → 摘要 → 金額 → 儲存，成功後回摘要</span>
      <kbd>Tab</kbd><span>切換收入／支出，游標留在目前欄位</span>
      <kbd>0924</kbd><span>輸入今年 09/24</span>
      <kbd>20260924</kbd><span>輸入完整日期</span>
      <kbd>Ctrl + ↑↓</kbd><span>日期 ±1 天</span>`;
  }
}

function v21Build8Escape(value) {
  return String(value ?? '')
    .replaceAll('&', '&amp;')
    .replaceAll('<', '&lt;')
    .replaceAll('>', '&gt;')
    .replaceAll('"', '&quot;')
    .replaceAll("'", '&#039;');
}
