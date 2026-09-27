const CY_V21_BUILD8_VERSION = 'V0.21.0 Build 8';
const CY_V21_BUILD8_DESKTOP = '(min-width: 1024px)';
const CY_V21_BUILD8_SUMMARY_UNITS = 40;

ensureV21Build8Stylesheet();

document.addEventListener('DOMContentLoaded', () => {
  syncV21Build8Version();
  setupV21Build8AccountChoices();
  setupV21Build8SummaryLimit();
  setupV21Build8RoleMedal();
});

window.addEventListener('load', () => {
  syncV21Build8Version();
  syncV21Build8AccountChoices();
  syncV21Build8RoleMedal();
});

function ensureV21Build8Stylesheet() {
  if (document.querySelector('link[href="/v021b8.css"]')) return;
  const link = document.createElement('link');
  link.rel = 'stylesheet';
  link.href = '/v021b8.css';
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
  const form = document.querySelector('#transactionForm');
  if (!select || !host || !row || !form) return;

  const media = window.matchMedia(CY_V21_BUILD8_DESKTOP);
  const applyMode = () => {
    row.hidden = !media.matches;
    syncV21Build8AccountChoices();
  };

  const observer = new MutationObserver(syncV21Build8AccountChoices);
  observer.observe(select, { childList: true, subtree: true });
  select.addEventListener('change', syncV21Build8AccountChoices);

  host.addEventListener('click', event => {
    const button = event.target.closest('[data-entry-account]');
    if (!button || !media.matches) return;
    selectV21Build8Account(button.dataset.entryAccount || '', true);
  });

  host.addEventListener('keydown', event => {
    const button = event.target.closest('[data-entry-account]');
    if (!button || !media.matches) return;
    const buttons = [...host.querySelectorAll('[data-entry-account]')];
    const index = buttons.indexOf(button);
    if (index < 0) return;

    if (event.key === 'Enter' && !event.isComposing && !event.shiftKey && !event.ctrlKey && !event.altKey && !event.metaKey) {
      event.preventDefault();
      event.stopPropagation();
      document.querySelector('#categoryName')?.focus();
      return;
    }

    if (!['ArrowLeft', 'ArrowRight', 'ArrowUp', 'ArrowDown'].includes(event.key)) return;
    event.preventDefault();
    const delta = event.key === 'ArrowLeft' || event.key === 'ArrowUp' ? -1 : 1;
    const next = buttons[(index + delta + buttons.length) % buttons.length];
    if (next) selectV21Build8Account(next.dataset.entryAccount || '', true);
  });

  form.addEventListener('keydown', event => {
    if (!media.matches || event.key !== 'Enter' || event.isComposing || event.shiftKey || event.ctrlKey || event.altKey || event.metaKey) return;
    if (event.target !== document.querySelector('#txDate')) return;
    event.preventDefault();
    event.stopImmediatePropagation();
    const active = host.querySelector('.entry-account-choice.active') || host.querySelector('.entry-account-choice');
    if (active) active.focus();
    else document.querySelector('#categoryName')?.focus();
  }, true);

  if (typeof media.addEventListener === 'function') media.addEventListener('change', applyMode);
  else media.addListener?.(applyMode);
  applyMode();
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

function v21Build8Escape(value) {
  return String(value ?? '')
    .replaceAll('&', '&amp;')
    .replaceAll('<', '&lt;')
    .replaceAll('>', '&gt;')
    .replaceAll('"', '&quot;')
    .replaceAll("'", '&#039;');
}
