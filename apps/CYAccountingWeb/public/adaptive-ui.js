/* CYAccountingWeb adaptive UI functional module. */

const CY_V20_MOBILE_CONFIRMATION_INIT = 'cyaccounting.v20.mobileConfirmationInitialized';
let cyV20Started = false;

if (document.readyState === 'loading') {
  document.addEventListener('DOMContentLoaded', startV20, { once: true });
} else {
  startV20();
}
window.addEventListener('load', startV20, { once: true });

function startV20() {
  if (cyV20Started) return;
  cyV20Started = true;
  setupV20ViewportState();
  setupV20MobileConfirmationDefault();
  setupV20SettingsTabVisibility();
  setupV201MobileInlineEditVisibility();
}



function setupV20ViewportState() {
  const sync = () => {
    const width = window.innerWidth;
    document.documentElement.dataset.viewport = width < 768 ? 'mobile' : isTabletWorkspace() ? 'tablet' : 'desktop';
  };
  sync();
  window.addEventListener('resize', sync, { passive: true });
}

function setupV20MobileConfirmationDefault() {
  if (window.innerWidth >= 768) return;
  if (localStorage.getItem(CY_V20_MOBILE_CONFIRMATION_INIT) === '1') return;
  localStorage.setItem(CY_V20_MOBILE_CONFIRMATION_INIT, '1');
  if (typeof setConfirmationDrawer === 'function') setConfirmationDrawer(false, false);
}

function setupV20SettingsTabVisibility() {
  const nav = document.querySelector('.settings-nav');
  if (!nav) return;
  nav.addEventListener('click', event => {
    const tab = event.target.closest('.settings-tab');
    if (!tab || window.innerWidth >= 768) return;
    requestAnimationFrame(() => tab.scrollIntoView({ behavior: 'smooth', block: 'nearest', inline: 'center' }));
  });
}

function setupV201MobileInlineEditVisibility() {
  const rows = document.querySelector('#transactionRows');
  if (!rows || typeof MutationObserver !== 'function') return;
  const observer = new MutationObserver(mutations => {
    if (window.innerWidth >= 768) return;
    for (const mutation of mutations) {
      if (mutation.type !== 'attributes' || mutation.attributeName !== 'class') continue;
      const row = mutation.target;
      if (!(row instanceof HTMLElement) || !row.matches('tr.inline-editing')) continue;
      requestAnimationFrame(() => row.scrollIntoView({ behavior: 'smooth', block: 'nearest', inline: 'nearest' }));
      break;
    }
  });
  observer.observe(rows, { subtree: true, attributes: true, attributeFilter: ['class'] });
}

const CY_V21_SPLIT_MEDIA = '(min-width: 1360px)';
const CY_V21_CONFIRMATION_STATE_KEY = 'cyaccounting.confirmationDrawerOpen';
let cyV21Started = false;

if (document.readyState === 'loading') {
  document.addEventListener('DOMContentLoaded', startV21, { once: true });
} else {
  startV21();
}
window.addEventListener('load', startV21, { once: true });

function startV21() {
  if (cyV21Started) return;
  cyV21Started = true;
  updateV21KeyboardHint();
  setupV21HeaderLayout();
  setupV21DesktopSplitWorkspace();
  setupV21EntryHelp();
  setupV21LedgerContext();
  setupV21LedgerHeaderDecoration();
  setupV21LedgerEmptyState();
  setupV21ConfirmationCopy();
  setupV21DataSettings();
  cleanupV21InterfaceCopy();
  setupV21UserIdentity();
}








function updateV21KeyboardHint() {
  const hint = document.querySelector('.keyboard-hint');
  if (!hint) return;
  hint.innerHTML = '鍵盤：日期 Enter → 帳戶 Enter → 科目 Enter → 摘要 Enter → 金額 Enter 儲存　｜　<kbd>Tab</kbd> 切換收入／支出　｜　日期可輸入 <kbd>0924</kbd> / <kbd>20260924</kbd>，<kbd>Ctrl</kbd>+<kbd>↑↓</kbd> ±1 天';
}

function setupV21HeaderLayout() {
  const topbar = document.querySelector('.topbar');
  const brand = topbar?.firstElementChild;
  const heading = brand?.querySelector('h1');
  const status = document.querySelector('#connectionStatus');
  const actions = document.querySelector('.topbar-actions');
  const settings = document.querySelector('#settingsButton');
  const currentUser = document.querySelector('#currentUser');
  const logout = document.querySelector('#logoutButton');
  if (!topbar || !brand || !heading || !actions) return;

  let brandLine = brand.querySelector('.cy-brand-line');
  if (!brandLine) {
    brandLine = document.createElement('div');
    brandLine.className = 'cy-brand-line';
    heading.before(brandLine);
    brandLine.append(heading);
  }
  if (status && status.parentElement !== brandLine) brandLine.append(status);

  let accountCluster = actions.querySelector('.cy-account-cluster');
  if (!accountCluster) {
    accountCluster = document.createElement('div');
    accountCluster.className = 'cy-account-cluster';
  }

  if (currentUser && currentUser.parentElement !== accountCluster) accountCluster.append(currentUser);
  if (logout && logout.parentElement !== accountCluster) accountCluster.append(logout);
  if (accountCluster.parentElement !== actions) actions.append(accountCluster);
  if (settings) {
    if (settings.parentElement !== actions) actions.append(settings);
    actions.insertBefore(settings, accountCluster);
  }
}

function setupV21EntryHelp() {
  const card = document.querySelector('.entry-card');
  const title = card?.querySelector('.section-title .title-with-badge');
  const sectionTitle = card?.querySelector('.section-title');
  if (!card || !title || !sectionTitle || document.querySelector('#entryHelpButton')) return;

  const button = document.createElement('button');
  button.id = 'entryHelpButton';
  button.className = 'entry-help-button';
  button.type = 'button';
  button.textContent = '?';
  button.title = '快速輸入說明';
  button.setAttribute('aria-label', '開啟快速輸入說明');
  button.setAttribute('aria-expanded', 'false');
  button.setAttribute('aria-controls', 'entryHelpPopover');

  const popover = document.createElement('div');
  popover.id = 'entryHelpPopover';
  popover.className = 'entry-help-popover';
  popover.hidden = true;
  popover.innerHTML = `
    <strong>快速輸入說明</strong>
    <div class="entry-help-grid">
      <kbd>Enter</kbd><span>日期 → 帳戶 → 科目 → 摘要 → 金額 → 儲存</span>
      <kbd>Tab</kbd><span>切換收入／支出，游標留在目前欄位</span>
      <kbd>0924</kbd><span>輸入今年 09/24</span>
      <kbd>20260924</kbd><span>輸入完整日期</span>
      <kbd>Ctrl + ↑↓</kbd><span>日期 ±1 天</span>
    </div>`;

  title.append(button);
  sectionTitle.append(popover);

  const close = () => {
    popover.hidden = true;
    button.setAttribute('aria-expanded', 'false');
  };
  button.addEventListener('click', event => {
    event.stopPropagation();
    const open = popover.hidden;
    popover.hidden = !open;
    button.setAttribute('aria-expanded', open ? 'true' : 'false');
  });
  popover.addEventListener('click', event => event.stopPropagation());
  document.addEventListener('click', close);
  document.addEventListener('keydown', event => {
    if (event.key === 'Escape' && !popover.hidden) {
      close();
      button.focus();
    }
  });
}

function setupV21LedgerContext() {
  const ledgerTitle = document.querySelector('.ledger-card .ledger-title');
  const titleMain = ledgerTitle?.firstElementChild;
  const monthTools = document.querySelector('.ledger-month-tools');
  const summary = document.querySelector('#monthSummary');
  const openingButton = document.querySelector('#ledgerOpeningBalanceButton');
  if (!ledgerTitle || !titleMain || !monthTools || !summary) return;

  let context = titleMain.querySelector('.cy-ledger-context');
  if (!context) {
    context = document.createElement('div');
    context.className = 'cy-ledger-context';
    titleMain.insertBefore(context, summary);
  }
  if (monthTools.parentElement !== context) context.append(monthTools);

  let summaryBar = titleMain.querySelector('.cy-ledger-summary-bar');
  if (!summaryBar) {
    summaryBar = document.createElement('div');
    summaryBar.className = 'cy-ledger-summary-bar';
    context.insertAdjacentElement('afterend', summaryBar);
  }
  if (summary.parentElement !== summaryBar) summaryBar.append(summary);

  let summaryActions = summaryBar.querySelector('.cy-summary-actions');
  if (!summaryActions) {
    summaryActions = document.createElement('div');
    summaryActions.className = 'cy-summary-actions';
    summaryBar.append(summaryActions);
  }
  if (openingButton && openingButton.parentElement !== summaryActions) summaryActions.append(openingButton);

  let lockButton = document.querySelector('#ledgerLockSettingsButton');
  if (!lockButton) {
    lockButton = document.createElement('button');
    lockButton.id = 'ledgerLockSettingsButton';
    lockButton.className = 'secondary compact';
    lockButton.type = 'button';
    lockButton.textContent = '鎖定月份';
    lockButton.title = '開啟月份鎖帳設定';
    lockButton.addEventListener('click', () => {
      if (typeof openSettings === 'function') openSettings();
      if (typeof setSettingsTab === 'function') setSettingsTab('lock');
      setTimeout(() => document.querySelector('#lockedThrough')?.focus(), 0);
    });
  }
  if (lockButton.parentElement !== summaryActions) summaryActions.append(lockButton);

  document.querySelector('#ledgerGroupToggle')?.remove();
  const periodTools = document.querySelector('.ledger-period-tools');
  if (periodTools && !periodTools.children.length) periodTools.remove();
}

function setupV21LedgerHeaderDecoration() {
  const head = document.querySelector('.ledger-card thead');
  if (!head) return;
  const decorate = () => {
    const account = document.querySelector('#ledgerAccountHeader');
    if (!account) return;
    const active = typeof cyLedgerGroupByAccount !== 'undefined' && Boolean(cyLedgerGroupByAccount);
    const nextText = active ? '帳戶 ▲' : '帳戶';
    if (account.textContent !== nextText) account.textContent = nextText;
    account.classList.toggle('cy-account-group-active', active);
    account.setAttribute('aria-pressed', active ? 'true' : 'false');
    account.title = active ? '點擊取消帳戶排列' : '點擊依帳戶排列';
  };
  decorate();
  const observer = new MutationObserver(decorate);
  observer.observe(head, { childList: true, subtree: true });
}

function setupV21LedgerEmptyState() {
  const body = document.querySelector('#transactionRows');
  if (!body) return;
  const enhance = () => {
    const empty = body.querySelector('td.empty');
    if (!empty || empty.querySelector('.ledger-empty-state')) return;
    const text = String(empty.textContent || '').trim();
    if (!text) return;
    empty.innerHTML = `<div class="ledger-empty-state"><strong>${v21EscapeHtml(text)}</strong></div>`;
  };
  enhance();
  const observer = new MutationObserver(enhance);
  observer.observe(body, { childList: true, subtree: true });
}

function setupV21ConfirmationCopy() {
  const panel = document.querySelector('#inputConfirmationCard');
  const heading = panel?.querySelector('h2');
  const hint = panel?.querySelector('.section-title .hint');
  const list = panel?.querySelector('#inputConfirmationList');
  if (!panel) return;
  if (heading) heading.textContent = '最近輸入';
  if (hint) hint.textContent = '最近 10 筆';

  const syncEmpty = () => {
    const empty = list?.querySelector('.confirmation-empty');
    if (empty && empty.textContent !== '本次尚無輸入紀錄。') empty.textContent = '本次尚無輸入紀錄。';
  };
  syncEmpty();
  if (list) {
    const observer = new MutationObserver(syncEmpty);
    observer.observe(list, { childList: true, subtree: true });
  }
}

function setupV21DataSettings() {
  const nav = document.querySelector('.settings-nav');
  const content = document.querySelector('.settings-content');
  if (!nav || !content || typeof setSettingsTab !== 'function') return;

  let tab = nav.querySelector('[data-settings-tab="data"]');
  let pane = content.querySelector('[data-settings-pane="data"]');
  if (!tab) {
    tab = document.createElement('button');
    tab.type = 'button';
    tab.className = 'settings-tab';
    tab.dataset.settingsTab = 'data';
    tab.textContent = '資料管理';
    const lockTab = nav.querySelector('[data-settings-tab="lock"]');
    if (lockTab) nav.insertBefore(tab, lockTab); else nav.append(tab);
    tab.addEventListener('click', () => setSettingsTab('data'));
    if (typeof els === 'object' && Array.isArray(els.settingsTabs)) els.settingsTabs.push(tab);
  }

  if (!pane) {
    pane = document.createElement('section');
    pane.className = 'settings-pane cy-data-pane';
    pane.dataset.settingsPane = 'data';
    pane.innerHTML = `
      <h3>資料管理</h3>
      <section class="cy-data-section">
        <h4>Excel 匯入</h4>
        <div class="cy-data-actions" id="v21ExcelImportHost"></div>
      </section>`;
    const settingsMessage = document.querySelector('#settingsMessage');
    content.insertBefore(pane, settingsMessage || null);
    if (typeof els === 'object' && Array.isArray(els.settingsPanes)) els.settingsPanes.push(pane);
  }

  const moveImportButton = () => {
    const button = document.querySelector('#ledgerExcelImport');
    const host = document.querySelector('#v21ExcelImportHost');
    if (!button || !host) return false;
    if (button.parentElement !== host) host.append(button);
    button.className = 'secondary compact';
    button.textContent = '匯入 Excel';
    return true;
  };

  if (!moveImportButton()) setTimeout(moveImportButton, 50);

  const subtitle = document.querySelector('#settingsDialog .modal-header p');
  if (subtitle) subtitle.textContent = '';
}

function cleanupV21InterfaceCopy() {
  const removeNoise = () => {
    const selectors = [
      '.auth-note',
      '#settingsDialog > .modal-header p',
      '#settingsDialog [data-settings-pane="accounts"] > .hint',
      '#settingsDialog [data-settings-pane="categories"] .pane-heading .hint',
      '#settingsDialog [data-settings-pane="quick"] > .hint',
      '#settingsDialog [data-settings-pane="quick"] .quick-settings-explain',
      '#settingsDialog [data-settings-pane="lock"] > .hint',
      '#settingsDialog [data-settings-pane="data"] .hint',
      '#settingsDialog [data-settings-pane="data"] .cy-data-section > p',
      '#settingsDialog [data-settings-pane="backup"] #backupHeadingHint',
      '#settingsDialog [data-settings-pane="backup"] .backup-security-note',
      '#settingsDialog [data-settings-pane="backup"] .backup-restore-note',
      '#settingsDialog [data-settings-pane="migration"] .migration-heading-v19 .hint',
      '#settingsDialog [data-settings-pane="migration"] .migration-privacy-v19',
      '#openingDialog .opening-dialog-heading > .hint'
    ];
    for (const selector of selectors) {
      document.querySelectorAll(selector).forEach(node => node.remove());
    }
  };

  removeNoise();
  const settings = document.querySelector('#settingsDialog .settings-content');
  if (settings) {
    const observer = new MutationObserver(removeNoise);
    observer.observe(settings, { childList: true, subtree: true });
  }
}

async function setupV21UserIdentity() {
  const target = document.querySelector('#currentUser');
  if (!target) return;
  let user = null;

  const render = () => {
    if (!user) return;
    if (target.querySelector('.current-user-role')) return;
    const employeeNo = String(user.employeeNo || '').trim();
    const name = String(user.name || '').trim();
    const role = String(user.role || '').trim();
    const roleLabel = v21RoleLabel(role);
    target.innerHTML = `<span class="current-user-main">${v21EscapeHtml(`${employeeNo} ${name}`.trim())}</span><span class="current-user-role" title="權限組：${v21EscapeHtml(role || roleLabel)}">${v21EscapeHtml(roleLabel)}</span>`;
    target.classList.remove('hidden');
  };

  const observer = new MutationObserver(render);
  observer.observe(target, { childList: true, subtree: true, characterData: true });

  try {
    user = window.cyaccCurrentUser || (window.cyaccSessionPromise ? await window.cyaccSessionPromise : null);
    if (!user) return;
    target.innerHTML = '';
    render();
  } catch {
    // Authentication UI already owns connection/error handling; role display is optional presentation only.
  }
}

function v21RoleLabel(role) {
  if (role === 'SUPER_ADMIN') return '超級管理員';
  if (role === 'ADMIN') return '管理員';
  return role || '一般使用者';
}

function v21EscapeHtml(value) {
  return String(value ?? '')
    .replaceAll('&', '&amp;')
    .replaceAll('<', '&lt;')
    .replaceAll('>', '&gt;')
    .replaceAll('"', '&quot;')
    .replaceAll("'", '&#039;');
}

function setupV21DesktopSplitWorkspace() {
  const media = window.matchMedia(CY_V21_SPLIT_MEDIA);
  const sync = () => applyV21DesktopSplitWorkspace(media.matches);
  sync();
  if (typeof media.addEventListener === 'function') media.addEventListener('change', sync);
  else media.addListener?.(sync);
}

function applyV21DesktopSplitWorkspace(enabled) {
  const tablet = isTabletWorkspace();
  enabled = enabled || tablet;
  const shell = document.querySelector('main.shell');
  const entry = shell?.querySelector('.entry-card') || document.querySelector('.entry-card');
  const ledger = shell?.querySelector('.ledger-card') || document.querySelector('.ledger-card');
  const confirmation = document.querySelector('#inputConfirmationCard');
  if (!shell || !entry || !ledger || !confirmation) return;

  let rail = shell.querySelector('.cy-entry-rail');

  if (enabled) {
    if (!rail) {
      rail = document.createElement('aside');
      rail.className = 'cy-entry-rail';
      rail.setAttribute('aria-label', '快速記帳工作區');
      shell.insertBefore(rail, ledger);
    }

    if (entry.parentElement !== rail) rail.prepend(entry);
    if (confirmation.parentElement !== rail) rail.append(confirmation);

    shell.classList.add('cy-split-layout');
    document.body.classList.add('cy-wide-split');
    confirmation.classList.add('cy-inline-confirmation');

    if (typeof setConfirmationDrawer === 'function') setConfirmationDrawer(!tablet, false);
    else {
      confirmation.classList.add('open');
      confirmation.setAttribute('aria-hidden', 'false');
    }
    return;
  }

  shell.classList.remove('cy-split-layout');
  document.body.classList.remove('cy-wide-split');
  confirmation.classList.remove('cy-inline-confirmation');

  if (entry.parentElement === rail) shell.insertBefore(entry, ledger);
  if (confirmation.parentElement === rail) document.body.append(confirmation);
  rail?.remove();

  const shouldOpen = localStorage.getItem(CY_V21_CONFIRMATION_STATE_KEY) === '1';
  if (typeof setConfirmationDrawer === 'function') setConfirmationDrawer(shouldOpen, false);
  else {
    confirmation.classList.toggle('open', shouldOpen);
    confirmation.setAttribute('aria-hidden', shouldOpen ? 'false' : 'true');
  }
}

const CY_ENTRY_UI_SUMMARY_UNITS = 40;
const CY_MOBILE_ENTRY_MOBILE = '(max-width: 767px)';
let cyEntryUiStarted = false;

if (document.readyState === 'loading') {
  document.addEventListener('DOMContentLoaded', startEntryUi, { once: true });
} else {
  startEntryUi();
}
window.addEventListener('load', syncEntryUiAfterLoad, { once: true });

function startEntryUi() {
  if (cyEntryUiStarted) return;
  cyEntryUiStarted = true;
  runEntryUiStep('mobile-pages', setupMobileEntryMobilePages);
  runEntryUiStep('mobile-account-picker', setupMobileEntryMobileAccountPicker);
  runEntryUiStep('account-choices', setupEntryUiAccountChoices);
  runEntryUiStep('summary-limit', setupEntryUiSummaryLimit);
  runEntryUiStep('role-medal', setupEntryUiRoleMedal);
  runEntryUiStep('enter-hints', setupMobileEntryEnterHints);
  runEntryUiStep('help-copy', syncMobileEntryHelpCopy);
}

function runEntryUiStep(name, task) {
  try {
    task();
  } catch (error) {
    console.error('cyaccounting_mobile_build8_step_failed', name, error instanceof Error ? error.message : 'unknown_error');
  }
}

function syncEntryUiAfterLoad() {
  startEntryUi();
  syncEntryUiAccountChoices();
  syncEntryUiRoleMedal();
  syncMobileEntryAccountPickerLabel();
  syncMobileEntryHelpCopy();
}




function setupEntryUiAccountChoices() {
  const select = document.querySelector('#accountName');
  const host = document.querySelector('#entryAccountButtons');
  const row = document.querySelector('#entryAccountChoiceRow');
  if (!select || !host || !row) return;

  const mobile = window.matchMedia(CY_MOBILE_ENTRY_MOBILE);
  row.hidden = false;
  const observer = new MutationObserver(syncEntryUiAccountChoices);
  observer.observe(select, { childList: true, subtree: true });
  select.addEventListener('change', syncEntryUiAccountChoices);

  host.addEventListener('click', event => {
    const button = event.target.closest('[data-entry-account]');
    if (!button) return;
    selectEntryUiAccount(button.dataset.entryAccount || '', !mobile.matches);
    if (mobile.matches) {
      setMobileEntryAccountPickerOpen(false);
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
    if (next) selectEntryUiAccount(next.dataset.entryAccount || '', true);
  });

  syncEntryUiAccountChoices();
}

function syncEntryUiAccountChoices() {
  const select = document.querySelector('#accountName');
  const host = document.querySelector('#entryAccountButtons');
  if (!select || !host) return;

  const options = [...select.options].filter(option => option.value);
  const signature = options.map(option => option.value).join('\u001f');
  if (host.dataset.accountSignature !== signature) {
    host.dataset.accountSignature = signature;
    host.innerHTML = options.map(option => `
      <button type="button" class="entry-account-choice" role="radio" data-entry-account="${entryUiEscape(option.value)}" aria-checked="false" tabindex="-1">${entryUiEscape(option.textContent || option.value)}</button>
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
  syncMobileEntryAccountPickerLabel();
}

function selectEntryUiAccount(name, focus = false) {
  const select = document.querySelector('#accountName');
  const host = document.querySelector('#entryAccountButtons');
  if (!select || !host || ![...select.options].some(option => option.value === name)) return;
  if (select.value !== name) {
    select.value = name;
    select.dispatchEvent(new Event('change', { bubbles: true }));
  }
  syncEntryUiAccountChoices();
  if (focus) host.querySelector(`[data-entry-account="${CSS.escape(name)}"]`)?.focus();
}

function setupMobileEntryMobileAccountPicker() {
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
    if (!window.matchMedia(CY_MOBILE_ENTRY_MOBILE).matches) return;
    setMobileEntryAccountPickerOpen(!row.classList.contains('mobile-picker-open'));
  });

  document.addEventListener('pointerdown', event => {
    if (!window.matchMedia(CY_MOBILE_ENTRY_MOBILE).matches || row.contains(event.target)) return;
    setMobileEntryAccountPickerOpen(false);
  });

  document.addEventListener('keydown', event => {
    if (event.key !== 'Escape' || !row.classList.contains('mobile-picker-open')) return;
    setMobileEntryAccountPickerOpen(false);
    trigger.focus();
  });

  const mobile = window.matchMedia(CY_MOBILE_ENTRY_MOBILE);
  const syncMode = () => {
    setMobileEntryAccountPickerOpen(false);
    syncMobileEntryAccountPickerLabel();
  };
  if (typeof mobile.addEventListener === 'function') mobile.addEventListener('change', syncMode);
  else mobile.addListener?.(syncMode);
  syncMode();
}

function setMobileEntryAccountPickerOpen(open) {
  const row = document.querySelector('#entryAccountChoiceRow');
  const trigger = document.querySelector('#entryAccountPickerButton');
  const host = document.querySelector('#entryAccountButtons');
  if (!row || !trigger || !host) return;

  const isMobile = window.matchMedia(CY_MOBILE_ENTRY_MOBILE).matches;
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

function syncMobileEntryAccountPickerLabel() {
  const select = document.querySelector('#accountName');
  const trigger = document.querySelector('#entryAccountPickerButton');
  const value = trigger?.querySelector('.entry-account-picker-value');
  if (!select || !value) return;
  const option = select.selectedOptions?.[0];
  value.textContent = option?.textContent?.trim() || select.value || '選擇帳戶';
}

function setupMobileEntryMobilePages() {
  const topbar = document.querySelector('.topbar');
  const shell = document.querySelector('.shell');
  const entry = shell?.querySelector('.entry-card');
  const ledger = shell?.querySelector('.ledger-card');
  if (!topbar || !shell || !entry || !ledger) return;

  let nav = document.querySelector('#mobileMainNav');
  if (!nav) {
    nav = document.createElement('nav');
    nav.id = 'mobileMainNav';
    nav.className = 'cy-mobile-main-nav';
    nav.setAttribute('aria-label', '主要頁面');
    nav.innerHTML = `
      <button type="button" class="active" data-mobile-page="entry" aria-selected="true">新增記帳</button>
      <button type="button" data-mobile-page="ledger" aria-selected="false">記帳資料</button>`;
    topbar.insertAdjacentElement('afterend', nav);
  }

  let current = 'entry';
  const mobile = window.matchMedia(CY_MOBILE_ENTRY_MOBILE);

  const apply = page => {
    current = page === 'ledger' ? 'ledger' : 'entry';
    const enabled = mobile.matches;
    nav.hidden = !enabled;
    entry.classList.toggle('cy-mobile-page-hidden', enabled && current !== 'entry');
    ledger.classList.toggle('cy-mobile-page-hidden', enabled && current !== 'ledger');
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

function setupEntryUiSummaryLimit() {
  const composing = new WeakSet();
  const fields = [document.querySelector('#summary'), document.querySelector('#editSummary')].filter(Boolean);

  for (const input of fields) {
    input.maxLength = 40;
    input.addEventListener('compositionstart', () => composing.add(input));
    input.addEventListener('compositionend', () => {
      composing.delete(input);
      enforceEntryUiSummary(input);
    });
    input.addEventListener('input', () => {
      if (!composing.has(input)) enforceEntryUiSummary(input);
    });
  }

  document.querySelector('#summarySuggestions')?.addEventListener('click', () => {
    setTimeout(() => {
      const summary = document.querySelector('#summary');
      if (summary) enforceEntryUiSummary(summary);
    }, 0);
  });

  const entryForm = document.querySelector('#transactionForm');
  entryForm?.addEventListener('submit', event => {
    const summary = document.querySelector('#summary');
    if (!summary || summaryWeightedUnits(summary.value) <= CY_ENTRY_UI_SUMMARY_UNITS) return;
    event.preventDefault();
    event.stopImmediatePropagation();
    if (typeof showMessage === 'function') showMessage('摘要不可超過 20 個中文字或 40 個英數字元。', true);
    summary.focus();
  }, true);

  const editForm = document.querySelector('#editTransactionForm');
  editForm?.addEventListener('submit', event => {
    const summary = document.querySelector('#editSummary');
    if (!summary || summaryWeightedUnits(summary.value) <= CY_ENTRY_UI_SUMMARY_UNITS) return;
    event.preventDefault();
    event.stopImmediatePropagation();
    const message = document.querySelector('#editMessage');
    if (message && typeof setDialogMessage === 'function') setDialogMessage(message, '摘要不可超過 20 個中文字或 40 個英數字元。', true);
    summary.focus();
  }, true);
}

function enforceEntryUiSummary(input) {
  if (summaryWeightedUnits(input.value) <= CY_ENTRY_UI_SUMMARY_UNITS) return;
  const trimmed = entryUiTrimWeighted(input.value, CY_ENTRY_UI_SUMMARY_UNITS);
  const cursor = input.selectionStart ?? trimmed.length;
  input.value = trimmed;
  try { input.setSelectionRange(Math.min(cursor, trimmed.length), Math.min(cursor, trimmed.length)); } catch { /* no-op */ }
}

function entryUiTrimWeighted(value, maxUnits) {
  let units = 0;
  let result = '';
  for (const char of String(value || '')) {
    const next = summaryCharacterUnits(char);
    if (units + next > maxUnits) break;
    units += next;
    result += char;
  }
  return result;
}



function setupEntryUiRoleMedal() {
  const target = document.querySelector('#currentUser');
  if (!target) return;
  const observer = new MutationObserver(syncEntryUiRoleMedal);
  observer.observe(target, { childList: true, subtree: true, characterData: true });
  syncEntryUiRoleMedal();
}

function syncEntryUiRoleMedal() {
  const target = document.querySelector('#currentUser');
  const role = String(target?.querySelector('.current-user-role')?.textContent || '').trim();
  if (!target) return;
  target.classList.toggle('role-super-admin', role === '超級管理員');
  target.classList.toggle('role-admin', role === '管理員');
}

function setupMobileEntryEnterHints() {
  const date = document.querySelector('#txDate');
  const summary = document.querySelector('#summary');
  const amount = document.querySelector('#amount');
  if (date) date.setAttribute('enterkeyhint', 'next');
  if (summary) summary.setAttribute('enterkeyhint', 'next');
  if (amount) amount.setAttribute('enterkeyhint', 'done');
}

function syncMobileEntryHelpCopy() {
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

function entryUiEscape(value) {
  return String(value ?? '')
    .replaceAll('&', '&amp;')
    .replaceAll('<', '&lt;')
    .replaceAll('>', '&gt;')
    .replaceAll('"', '&quot;')
    .replaceAll("'", '&#039;');
}

const CY_MOBILE_WORKSPACE_MOBILE = '(max-width: 767px)';
const CY_V21_CONFIRMATION_KEY = 'cyaccounting.confirmationDrawerOpen';
let cyMobileWorkspaceStarted = false;

if (document.readyState === 'loading') {
  document.addEventListener('DOMContentLoaded', startMobileWorkspace, { once: true });
} else {
  startMobileWorkspace();
}
window.addEventListener('load', syncMobileWorkspaceAfterLoad, { once: true });

function startMobileWorkspace() {
  if (cyMobileWorkspaceStarted) return;
  cyMobileWorkspaceStarted = true;
  runMobileWorkspaceStep('mobile-app-bar', setupMobileWorkspaceMobileAppBar);
  runMobileWorkspaceStep('mobile-navigation', setupMobileWorkspaceMobileNavigation);
  runMobileWorkspaceStep('account-sheet', setupMobileWorkspaceAccountSheet);
  runMobileWorkspaceStep('ledger-tools', setupMobileWorkspaceLedgerTools);
  runMobileWorkspaceStep('confirmation-policy', setupMobileWorkspaceConfirmationPolicy);
  runMobileWorkspaceStep('mobile-form-copy', setupMobileWorkspaceMobileFormCopy);
}

function runMobileWorkspaceStep(name, task) {
  try {
    task();
  } catch (error) {
    console.error('cyaccounting_mobile_build10_step_failed', name, error instanceof Error ? error.message : 'unknown_error');
  }
}

function syncMobileWorkspaceAfterLoad() {
  startMobileWorkspace();
  syncMobileWorkspaceMobileIdentity();
  syncMobileWorkspaceMobileNavigation();
  syncMobileWorkspaceConfirmationPolicy();
}



function setupMobileWorkspaceMobileAppBar() {
  const topbar = document.querySelector('.topbar');
  const currentUser = document.querySelector('#currentUser');
  const logoutButton = document.querySelector('#logoutButton');
  if (!topbar || !currentUser || !logoutButton) return;

  let trigger = document.querySelector('#mobileAccountMenuButton');
  if (!trigger) {
    trigger = document.createElement('button');
    trigger.id = 'mobileAccountMenuButton';
    trigger.className = 'cy-mobile-account-menu-button';
    trigger.type = 'button';
    trigger.setAttribute('aria-haspopup', 'true');
    trigger.setAttribute('aria-expanded', 'false');
    trigger.innerHTML = '<span class="cy-mobile-account-name">帳號</span><span aria-hidden="true">›</span>';
    topbar.append(trigger);
  }

  let menu = document.querySelector('#mobileAccountMenu');
  if (!menu) {
    menu = document.createElement('div');
    menu.id = 'mobileAccountMenu';
    menu.className = 'cy-mobile-account-menu';
    menu.hidden = true;
    menu.innerHTML = `
      <div class="cy-mobile-account-menu-identity">
        <strong id="mobileAccountMenuName">帳號</strong>
        <span id="mobileAccountMenuRole"></span>
      </div>
      <button type="button" class="danger-lite" data-mobile-account-action="logout">登出</button>`;
    document.body.append(menu);
  }

  const close = () => {
    menu.hidden = true;
    trigger.setAttribute('aria-expanded', 'false');
    document.body.classList.remove('cy-mobile-account-menu-open');
  };

  trigger.addEventListener('click', event => {
    if (!window.matchMedia(CY_MOBILE_WORKSPACE_MOBILE).matches) return;
    event.stopPropagation();
    const open = menu.hidden;
    menu.hidden = !open;
    trigger.setAttribute('aria-expanded', open ? 'true' : 'false');
    document.body.classList.toggle('cy-mobile-account-menu-open', open);
    if (open) syncMobileWorkspaceMobileIdentity();
  });

  menu.addEventListener('click', event => {
    const action = event.target.closest('[data-mobile-account-action]')?.dataset.mobileAccountAction;
    if (!action) return;
    close();
    if (action === 'logout') logoutButton.click();
  });

  document.addEventListener('pointerdown', event => {
    if (menu.hidden || trigger.contains(event.target) || menu.contains(event.target)) return;
    close();
  });

  document.addEventListener('keydown', event => {
    if (event.key !== 'Escape' || menu.hidden) return;
    close();
    trigger.focus();
  });

  const observer = new MutationObserver(syncMobileWorkspaceMobileIdentity);
  observer.observe(currentUser, { childList: true, subtree: true, characterData: true, attributes: true, attributeFilter: ['class'] });

  const mobile = window.matchMedia(CY_MOBILE_WORKSPACE_MOBILE);
  const syncMode = () => {
    document.body.classList.toggle('cy-mobile-app', mobile.matches);
    if (!mobile.matches) close();
    syncMobileWorkspaceMobileIdentity();
  };
  if (typeof mobile.addEventListener === 'function') mobile.addEventListener('change', syncMode);
  else mobile.addListener?.(syncMode);
  syncMode();
}

function syncMobileWorkspaceMobileIdentity() {
  const source = document.querySelector('#currentUser');
  const trigger = document.querySelector('#mobileAccountMenuButton');
  const triggerName = trigger?.querySelector('.cy-mobile-account-name');
  const menu = document.querySelector('#mobileAccountMenu');
  const menuName = document.querySelector('#mobileAccountMenuName');
  const menuRole = document.querySelector('#mobileAccountMenuRole');
  if (!source || !trigger || !triggerName || !menu || !menuName || !menuRole) return;

  const main = String(source.querySelector('.current-user-main')?.textContent || source.textContent || '').trim() || '帳號';
  const role = String(source.querySelector('.current-user-role')?.textContent || '').trim();
  triggerName.textContent = main;
  menuName.textContent = main;
  menuRole.textContent = role;

  const superAdmin = source.classList.contains('role-super-admin') || role === '超級管理員';
  const admin = source.classList.contains('role-admin') || role === '管理員';
  trigger.classList.toggle('role-super-admin', superAdmin);
  trigger.classList.toggle('role-admin', admin);
  menu.classList.toggle('role-super-admin', superAdmin);
  menu.classList.toggle('role-admin', admin);
}

function setupMobileWorkspaceMobileNavigation() {
  const sync = () => {
    const nav = document.querySelector('#mobileMainNav');
    if (!nav) return false;
    nav.classList.add('cy-mobile-bottom-nav');
    if (nav.parentElement !== document.body) document.body.append(nav);

    const entryButton = nav.querySelector('[data-mobile-page="entry"]');
    const ledgerButton = nav.querySelector('[data-mobile-page="ledger"]');
    if (entryButton && !entryButton.querySelector('.cy-mobile-nav-icon')) {
      entryButton.innerHTML = '<span class="cy-mobile-nav-icon" aria-hidden="true">＋</span><span>新增記帳</span>';
    }
    if (ledgerButton && !ledgerButton.querySelector('.cy-mobile-nav-icon')) {
      ledgerButton.innerHTML = '<span class="cy-mobile-nav-icon" aria-hidden="true">≡</span><span>記帳資料</span>';
    }
    return true;
  };

  if (!sync()) setTimeout(sync, 0);
  const mobile = window.matchMedia(CY_MOBILE_WORKSPACE_MOBILE);
  const syncMode = () => {
    sync();
    document.body.classList.toggle('cy-mobile-app', mobile.matches);
  };
  if (typeof mobile.addEventListener === 'function') mobile.addEventListener('change', syncMode);
  else mobile.addListener?.(syncMode);
}

function syncMobileWorkspaceMobileNavigation() {
  const nav = document.querySelector('#mobileMainNav');
  if (!nav) return;
  nav.classList.add('cy-mobile-bottom-nav');
  if (nav.parentElement !== document.body) document.body.append(nav);
}

function setupMobileWorkspaceAccountSheet() {
  const row = document.querySelector('#entryAccountChoiceRow');
  if (!row) return;

  let backdrop = document.querySelector('#mobileAccountSheetBackdrop');
  if (!backdrop) {
    backdrop = document.createElement('button');
    backdrop.id = 'mobileAccountSheetBackdrop';
    backdrop.className = 'cy-mobile-sheet-backdrop';
    backdrop.type = 'button';
    backdrop.setAttribute('aria-label', '關閉帳戶選單');
    backdrop.hidden = true;
    document.body.append(backdrop);
  }

  const sync = () => {
    const open = window.matchMedia(CY_MOBILE_WORKSPACE_MOBILE).matches && row.classList.contains('mobile-picker-open');
    backdrop.hidden = !open;
    document.body.classList.toggle('cy-mobile-account-sheet-open', open);
  };

  backdrop.addEventListener('click', () => {
    if (typeof setMobileEntryAccountPickerOpen === 'function') setMobileEntryAccountPickerOpen(false);
  });

  const observer = new MutationObserver(sync);
  observer.observe(row, { attributes: true, attributeFilter: ['class'] });
  document.querySelector('#entryAccountButtons')?.addEventListener('click', () => setTimeout(sync, 0));
  sync();
}

function setupMobileWorkspaceLedgerTools() {
  const ledger = document.querySelector('.ledger-card');
  if (!ledger) return;

  let button = document.querySelector('#mobileLedgerMoreButton');
  if (!button) {
    button = document.createElement('button');
    button.id = 'mobileLedgerMoreButton';
    button.className = 'secondary compact cy-mobile-ledger-more';
    button.type = 'button';
    button.textContent = '更多';
    button.setAttribute('aria-haspopup', 'true');
    button.setAttribute('aria-expanded', 'false');
    const summaryBar = ledger.querySelector('.cy-ledger-summary-bar') || ledger.querySelector('.ledger-title');
    summaryBar?.append(button);
  }

  let backdrop = document.querySelector('#mobileLedgerToolsBackdrop');
  if (!backdrop) {
    backdrop = document.createElement('button');
    backdrop.id = 'mobileLedgerToolsBackdrop';
    backdrop.className = 'cy-mobile-sheet-backdrop';
    backdrop.type = 'button';
    backdrop.setAttribute('aria-label', '關閉記帳工具');
    backdrop.hidden = true;
    document.body.append(backdrop);
  }

  let sheet = document.querySelector('#mobileLedgerToolsSheet');
  if (!sheet) {
    sheet = document.createElement('section');
    sheet.id = 'mobileLedgerToolsSheet';
    sheet.className = 'cy-mobile-tools-sheet';
    sheet.hidden = true;
    sheet.innerHTML = `
      <div class="cy-mobile-sheet-handle" aria-hidden="true"></div>
      <h3>更多</h3>
      <button type="button" data-mobile-ledger-action="accounts">帳戶設定</button>
      <button type="button" data-mobile-ledger-action="categories">科目設定</button>
      <button type="button" data-mobile-ledger-action="lock">月份鎖帳</button>
      <button type="button" data-mobile-ledger-action="export">匯出 Excel</button>
      <button type="button" class="secondary" data-mobile-ledger-action="close">取消</button>`;
    document.body.append(sheet);
  }

  const close = () => {
    sheet.hidden = true;
    backdrop.hidden = true;
    button.setAttribute('aria-expanded', 'false');
    document.body.classList.remove('cy-mobile-ledger-tools-open');
  };

  const open = () => {
    if (!window.matchMedia(CY_MOBILE_WORKSPACE_MOBILE).matches) return;
    sheet.hidden = false;
    backdrop.hidden = false;
    button.setAttribute('aria-expanded', 'true');
    document.body.classList.add('cy-mobile-ledger-tools-open');
  };

  button.addEventListener('click', open);
  backdrop.addEventListener('click', close);
  sheet.addEventListener('click', event => {
    const action = event.target.closest('[data-mobile-ledger-action]')?.dataset.mobileLedgerAction;
    if (!action) return;
    if (action === 'close') {
      close();
      return;
    }
    close();
    if (action === 'accounts') {
      if (typeof window.cyOpenMobileSettingsPane === 'function') window.cyOpenMobileSettingsPane('accounts');
      else {
        if (typeof openSettings === 'function') openSettings();
        if (typeof setSettingsTab === 'function') setSettingsTab('accounts');
      }
    }
    if (action === 'categories') {
      if (typeof window.cyOpenMobileSettingsPane === 'function') window.cyOpenMobileSettingsPane('categories');
      else {
        if (typeof openSettings === 'function') openSettings();
        if (typeof setSettingsTab === 'function') setSettingsTab('categories');
      }
    }
    if (action === 'lock') {
      if (typeof window.cyOpenMobileLedgerLock === 'function') window.cyOpenMobileLedgerLock();
      else document.querySelector('#ledgerLockSettingsButton')?.click();
    }
    if (action === 'export') document.querySelector('#ledgerExcelExport')?.click();
  });

  document.addEventListener('keydown', event => {
    if (event.key !== 'Escape' || sheet.hidden) return;
    close();
    button.focus();
  });
}

function setupMobileWorkspaceConfirmationPolicy() {
  const mobile = window.matchMedia(CY_MOBILE_WORKSPACE_MOBILE);
  const sync = () => {
    if (mobile.matches) {
      if (typeof setConfirmationDrawer === 'function') setConfirmationDrawer(false, false);
      return;
    }
    const shouldOpen = localStorage.getItem(CY_V21_CONFIRMATION_KEY) === '1';
    if (typeof setConfirmationDrawer === 'function') setConfirmationDrawer(shouldOpen, false);
  };
  if (typeof mobile.addEventListener === 'function') mobile.addEventListener('change', sync);
  else mobile.addListener?.(sync);
  sync();
}

function syncMobileWorkspaceConfirmationPolicy() {
  if (!window.matchMedia(CY_MOBILE_WORKSPACE_MOBILE).matches) return;
  if (typeof setConfirmationDrawer === 'function') setConfirmationDrawer(false, false);
}

function setupMobileWorkspaceMobileFormCopy() {
  const summary = document.querySelector('#summary');
  if (summary) summary.placeholder = '可留白，最多 20 個中文字';

  const trigger = document.querySelector('#entryAccountPickerButton');
  const arrow = trigger?.querySelector('.entry-account-picker-arrow');
  if (arrow) arrow.textContent = '›';
}

const CY_DESKTOP_ISOLATION_DESKTOP = '(min-width: 768px)';

function isDesktopInteractionWorkspace() {
  return window.innerWidth >= 768 && !isTabletWorkspace();
}
window.cyIsDesktopInteractionWorkspace = isDesktopInteractionWorkspace;
let cyDesktopIsolationStarted = false;

if (document.readyState === 'loading') {
  document.addEventListener('DOMContentLoaded', startDesktopIsolation, { once: true });
} else {
  startDesktopIsolation();
}
window.addEventListener('load', startDesktopIsolation, { once: true });

function startDesktopIsolation() {
  if (cyDesktopIsolationStarted) return;
  cyDesktopIsolationStarted = true;
  setupDesktopIsolationDesktopIsolation();
}















function setupDesktopIsolationDesktopIsolation() {
  const media = window.matchMedia(CY_DESKTOP_ISOLATION_DESKTOP);
  const sync = () => syncDesktopIsolationDesktopIsolation(media.matches);
  sync();
  if (typeof media.addEventListener === 'function') media.addEventListener('change', sync);
  else media.addListener?.(sync);
}

function syncDesktopIsolationDesktopIsolation(desktop = window.matchMedia(CY_DESKTOP_ISOLATION_DESKTOP).matches) {
  const accountTrigger = document.querySelector('#mobileAccountMenuButton');
  const ledgerMore = document.querySelector('#mobileLedgerMoreButton');

  if (accountTrigger) accountTrigger.hidden = Boolean(desktop);
  if (ledgerMore) ledgerMore.hidden = Boolean(desktop);

  if (!desktop) {
    document.body.classList.add('cy-mobile-app');
    return;
  }

  document.body.classList.remove(
    'cy-mobile-app',
    'cy-mobile-account-menu-open',
    'cy-mobile-account-sheet-open',
    'cy-mobile-ledger-tools-open'
  );

  const accountMenu = document.querySelector('#mobileAccountMenu');
  const accountBackdrop = document.querySelector('#mobileAccountSheetBackdrop');
  const ledgerBackdrop = document.querySelector('#mobileLedgerToolsBackdrop');
  const ledgerSheet = document.querySelector('#mobileLedgerToolsSheet');
  if (accountMenu) accountMenu.hidden = true;
  if (accountBackdrop) accountBackdrop.hidden = true;
  if (ledgerBackdrop) ledgerBackdrop.hidden = true;
  if (ledgerSheet) ledgerSheet.hidden = true;

  accountTrigger?.setAttribute('aria-expanded', 'false');
  ledgerMore?.setAttribute('aria-expanded', 'false');
}

const CY_DESKTOP_LEDGER_MONTH_MONTHS = ['一月', '二月', '三月', '四月', '五月', '六月', '七月', '八月', '九月', '十月', '十一月', '十二月'];
syncDesktopLedgerMonthCopy();

window.addEventListener('load', () => {
  syncDesktopLedgerMonthCopy();
  setupDesktopLedgerMonthDesktopMonthPicker();
});



function syncDesktopLedgerMonthCopy() {
  const summary = document.querySelector('#summary');
  const editSummary = document.querySelector('#editSummary');
  if (summary) summary.placeholder = '最多20個字';
  if (editSummary) editSummary.placeholder = '最多20個字';
}

function setupDesktopLedgerMonthDesktopMonthPicker() {
  const media = window.matchMedia('(any-pointer: coarse)');
  const syncMode = () => {
    const desktop = isDesktopInteractionWorkspace();
    const root = document.querySelector('#ledgerMonthPickerCustom');
    if (root) {
      root.hidden = !desktop;
      if (!desktop) closeDesktopLedgerMonthMonthPicker(root);
    }
    if (desktop) ensureDesktopLedgerMonthMonthPicker();
  };

  if (typeof media.addEventListener === 'function') media.addEventListener('change', syncMode);
  else media.addListener?.(syncMode);
  syncMode();
}

function ensureDesktopLedgerMonthMonthPicker() {
  const input = document.querySelector('#monthFilter');
  const slot = document.querySelector('#ledgerMonthSlot');
  if (!input || !slot) {
    setTimeout(ensureDesktopLedgerMonthMonthPicker, 60);
    return;
  }

  let root = document.querySelector('#ledgerMonthPickerCustom');
  if (root) {
    root.hidden = false;
    syncDesktopLedgerMonthMonthPickerLabel(root, input);
    return;
  }

  root = document.createElement('div');
  root.id = 'ledgerMonthPickerCustom';
  root.className = 'cy-month-picker-custom';
  root.innerHTML = `
    <button id="ledgerMonthPickerTrigger" class="cy-month-picker-trigger" type="button" aria-haspopup="dialog" aria-expanded="false">
      <span id="ledgerMonthPickerLabel">—</span><span class="cy-month-picker-caret" aria-hidden="true">▾</span>
    </button>
    <div id="ledgerMonthPickerPopover" class="cy-month-picker-popover" role="dialog" aria-label="選擇月份" hidden>
      <div class="cy-month-picker-head">
        <button type="button" class="cy-month-picker-nav" data-picker-nav="-1" aria-label="上一組">‹</button>
        <button id="ledgerMonthPickerYearButton" type="button" class="cy-month-picker-year" aria-label="切換年份選擇"></button>
        <button type="button" class="cy-month-picker-nav" data-picker-nav="1" aria-label="下一組">›</button>
      </div>
      <div id="ledgerMonthPickerGrid" class="cy-month-picker-grid"></div>
    </div>`;
  slot.append(root);

  const trigger = root.querySelector('#ledgerMonthPickerTrigger');
  const popover = root.querySelector('#ledgerMonthPickerPopover');
  const yearButton = root.querySelector('#ledgerMonthPickerYearButton');
  const grid = root.querySelector('#ledgerMonthPickerGrid');
  let view = 'months';
  let displayYear = desktopLedgerMonthReadMonth(input).year;
  let yearStart = displayYear - 5;

  const render = () => {
    const selected = desktopLedgerMonthReadMonth(input);
    if (view === 'months') {
      yearButton.textContent = String(displayYear);
      yearButton.title = '選擇年份';
      grid.className = 'cy-month-picker-grid month-view';
      grid.innerHTML = CY_DESKTOP_LEDGER_MONTH_MONTHS.map((label, index) => {
        const month = index + 1;
        const active = selected.year === displayYear && selected.month === month;
        return `<button type="button" class="cy-month-choice${active ? ' active' : ''}" data-picker-month="${month}" aria-pressed="${active ? 'true' : 'false'}">${label}</button>`;
      }).join('');
      return;
    }

    yearButton.textContent = `${yearStart}–${yearStart + 11}`;
    yearButton.title = '返回月份選擇';
    grid.className = 'cy-month-picker-grid year-view';
    grid.innerHTML = Array.from({ length: 12 }, (_, index) => yearStart + index).map(year => {
      const active = year === selected.year;
      const current = year === new Date().getFullYear();
      return `<button type="button" class="cy-year-choice${active ? ' active' : ''}${current ? ' current' : ''}" data-picker-year="${year}" aria-pressed="${active ? 'true' : 'false'}">${year}</button>`;
    }).join('');
  };

  const open = () => {
    const selected = desktopLedgerMonthReadMonth(input);
    displayYear = selected.year;
    yearStart = displayYear - 5;
    view = 'months';
    render();
    popover.hidden = false;
    trigger.setAttribute('aria-expanded', 'true');
  };

  const close = focusTrigger => {
    popover.hidden = true;
    trigger.setAttribute('aria-expanded', 'false');
    if (focusTrigger) trigger.focus();
  };

  trigger.addEventListener('click', event => {
    event.stopPropagation();
    if (!isDesktopInteractionWorkspace()) return;
    if (popover.hidden) open(); else close(false);
  });

  root.querySelectorAll('[data-picker-nav]').forEach(button => {
    button.addEventListener('click', () => {
      const delta = Number(button.dataset.pickerNav) || 0;
      if (view === 'months') displayYear += delta;
      else yearStart += delta * 12;
      render();
    });
  });

  yearButton.addEventListener('click', () => {
    if (view === 'months') {
      view = 'years';
      yearStart = displayYear - 5;
    } else {
      view = 'months';
    }
    render();
  });

  grid.addEventListener('click', event => {
    const monthButton = event.target.closest('[data-picker-month]');
    if (monthButton) {
      const month = Number(monthButton.dataset.pickerMonth);
      if (month >= 1 && month <= 12) {
        input.value = `${displayYear}-${String(month).padStart(2, '0')}`;
        input.dispatchEvent(new Event('change', { bubbles: true }));
        syncDesktopLedgerMonthMonthPickerLabel(root, input);
        close(true);
      }
      return;
    }

    const yearButtonChoice = event.target.closest('[data-picker-year]');
    if (yearButtonChoice) {
      displayYear = Number(yearButtonChoice.dataset.pickerYear) || displayYear;
      view = 'months';
      render();
    }
  });

  input.addEventListener('change', () => {
    const selected = desktopLedgerMonthReadMonth(input);
    displayYear = selected.year;
    syncDesktopLedgerMonthMonthPickerLabel(root, input);
    if (!popover.hidden) render();
  });

  document.addEventListener('pointerdown', event => {
    if (popover.hidden || root.contains(event.target)) return;
    close(false);
  });

  document.addEventListener('keydown', event => {
    if (event.key !== 'Escape' || popover.hidden) return;
    close(true);
  });

  root._desktopLedgerMonthClose = close;
  syncDesktopLedgerMonthMonthPickerLabel(root, input);
}

function syncDesktopLedgerMonthMonthPickerLabel(root, input) {
  const label = root?.querySelector('#ledgerMonthPickerLabel');
  if (!label || !input) return;
  const selected = desktopLedgerMonthReadMonth(input);
  label.textContent = `${selected.year}年${String(selected.month).padStart(2, '0')}月`;
}

function closeDesktopLedgerMonthMonthPicker(root) {
  const popover = root?.querySelector('#ledgerMonthPickerPopover');
  const trigger = root?.querySelector('#ledgerMonthPickerTrigger');
  if (popover) popover.hidden = true;
  trigger?.setAttribute('aria-expanded', 'false');
}

function desktopLedgerMonthReadMonth(input) {
  const value = String(input?.value || '');
  const match = /^(\d{4})-(\d{2})$/.exec(value);
  if (match) return { year: Number(match[1]), month: Number(match[2]) };
  const now = new Date();
  return { year: now.getFullYear(), month: now.getMonth() + 1 };
}

setupDesktopManagementConnectionStatus();

const runDesktopManagement = () => {
  setupDesktopManagementConnectionStatus();
  setupDesktopManagementHeaderManagement();
  setupDesktopManagementOpeningDialog();
  syncDesktopManagementCrudCopy();
};

if (document.readyState === 'complete') setTimeout(runDesktopManagement, 0);
else window.addEventListener('load', () => setTimeout(runDesktopManagement, 0), { once: true });



function setupDesktopManagementConnectionStatus() {
  const status = document.querySelector('#connectionStatus');
  if (!status || status.dataset.desktopManagementBound === '1') return;
  status.dataset.desktopManagementBound = '1';

  const sync = () => {
    const warning = status.classList.contains('warn') || status.classList.contains('error');
    if (warning) {
      if (status.classList.contains('hidden')) status.classList.remove('hidden');
      return;
    }
    if (status.textContent) status.textContent = '';
    if (!status.classList.contains('hidden')) status.classList.add('hidden');
  };

  sync();
  const observer = new MutationObserver(sync);
  observer.observe(status, { attributes: true, childList: true, characterData: true, subtree: true });
}

function setupDesktopManagementHeaderManagement() {
  const media = window.matchMedia('(any-pointer: coarse)');
  const actions = document.querySelector('.topbar-actions');
  const settings = document.querySelector('#settingsButton');
  const dialog = document.querySelector('#settingsDialog');
  const title = dialog?.querySelector('.modal-header h2');
  if (!actions || !settings || !dialog || !title) return;

  let accountsButton = document.querySelector('#headerAccountManagerButton');
  if (!accountsButton) {
    accountsButton = document.createElement('button');
    accountsButton.id = 'headerAccountManagerButton';
    accountsButton.className = 'secondary compact cy-header-management-button';
    accountsButton.type = 'button';
    accountsButton.textContent = '帳戶管理';
    actions.insertBefore(accountsButton, settings);
  }

  let categoriesButton = document.querySelector('#headerCategoryManagerButton');
  if (!categoriesButton) {
    categoriesButton = document.createElement('button');
    categoriesButton.id = 'headerCategoryManagerButton';
    categoriesButton.className = 'secondary compact cy-header-management-button';
    categoriesButton.type = 'button';
    categoriesButton.textContent = '科目管理';
    actions.insertBefore(categoriesButton, settings);
  }

  if (accountsButton.dataset.desktopManagementBound !== '1') {
    accountsButton.dataset.desktopManagementBound = '1';
    accountsButton.addEventListener('click', () => openDesktopManagementManagement('accounts', '帳戶管理'));
  }
  if (categoriesButton.dataset.desktopManagementBound !== '1') {
    categoriesButton.dataset.desktopManagementBound = '1';
    categoriesButton.addEventListener('click', () => openDesktopManagementManagement('categories', '科目管理'));
  }

  if (settings.dataset.desktopManagementBound !== '1') {
    settings.dataset.desktopManagementBound = '1';
    settings.addEventListener('click', () => {
      setTimeout(() => {
        dialog.classList.remove('cy-management-mode');
        delete dialog.dataset.managementPane;
        title.textContent = '設定';
        const current = typeof state === 'object' ? String(state.activeSettingsTab || '') : '';
        if (current === 'accounts' || current === 'categories' || !current) {
          const preferred = ['quick', 'data', 'lock', 'backup', 'migration']
            .find(name => document.querySelector(`[data-settings-tab="${name}"]`));
          if (preferred && typeof setSettingsTab === 'function') setSettingsTab(preferred);
        }
      }, 0);
    });
  }

  const syncDesktop = () => {
    const enabled = isDesktopInteractionWorkspace();
    accountsButton.hidden = !enabled;
    categoriesButton.hidden = !enabled;
    if (!enabled) {
      dialog.classList.remove('cy-management-mode');
      delete dialog.dataset.managementPane;
      title.textContent = '設定';
    }
  };
  syncDesktop();
  if (typeof media.addEventListener === 'function' && !actions.dataset.desktopManagementMediaBound) {
    actions.dataset.desktopManagementMediaBound = '1';
    media.addEventListener('change', syncDesktop);
  }
}

function openDesktopManagementManagement(tab, label) {
  if (!isDesktopInteractionWorkspace()) return;
  const dialog = document.querySelector('#settingsDialog');
  const title = dialog?.querySelector('.modal-header h2');
  if (!dialog || !title) return;

  if (!dialog.open) {
    if (typeof openSettings === 'function') openSettings();
    else dialog.showModal();
  } else if (typeof renderSettings === 'function') {
    renderSettings();
  }

  dialog.classList.add('cy-management-mode');
  dialog.dataset.managementPane = tab;
  title.textContent = label;
  if (typeof setSettingsTab === 'function') setSettingsTab(tab);
}

function setupDesktopManagementOpeningDialog() {
  const dialog = document.querySelector('#openingDialog');
  const monthInput = document.querySelector('#openingMonth');
  const ledgerMonth = document.querySelector('#monthFilter');
  const title = dialog?.querySelector('.modal-header h2');
  if (!dialog || !monthInput || !ledgerMonth || !title) return;

  const sync = () => {
    if (!isDesktopInteractionWorkspace()) return;
    const month = /^\d{4}-\d{2}$/.test(ledgerMonth.value || '') ? ledgerMonth.value : monthInput.value;
    if (/^\d{4}-\d{2}$/.test(month || '')) {
      if (monthInput.value !== month) {
        monthInput.value = month;
        monthInput.dispatchEvent(new Event('change', { bubbles: true }));
      }
      title.textContent = `${month.replace('-', '/')}期初餘額`;
    }
  };

  sync();
  const observer = new MutationObserver(() => {
    if (dialog.open) sync();
  });
  observer.observe(dialog, { attributes: true, attributeFilter: ['open'] });
  document.querySelector('#ledgerOpeningBalanceButton')?.addEventListener('click', () => setTimeout(sync, 0));
}

function syncDesktopManagementCrudCopy() {
  const editSave = document.querySelector('#editSaveButton');
  if (editSave) editSave.textContent = '儲存';
}

const CY_DESKTOP_MANAGERS_MONTHS = ['一月', '二月', '三月', '四月', '五月', '六月', '七月', '八月', '九月', '十月', '十一月', '十二月'];

const runDesktopManagers = () => {
  setupDesktopManagersAccountLimit();
  setupDesktopManagersMonthPickers();
  syncDesktopManagersLedgerMonthTrigger();
};

if (document.readyState === 'complete') setTimeout(runDesktopManagers, 0);
else window.addEventListener('load', () => setTimeout(runDesktopManagers, 0), { once: true });



function setupDesktopManagersAccountLimit() {
  const input = document.querySelector('#newAccountName');
  const button = document.querySelector('#addAccountButton');
  if (!input || !button) return;
  input.maxLength = 8;
  input.placeholder = '帳戶名稱最多八字';

  const validate = event => {
    const name = String(input.value || '').trim().replace(/\s+/g, ' ');
    if (!name || desktopManagersCharCount(name) <= 8) return;
    event?.preventDefault();
    event?.stopImmediatePropagation();
    setDialogMessage(document.querySelector('#settingsMessage'), '帳戶名稱最多 8 個字。', true);
    input.focus();
  };
  if (button.dataset.desktopManagersLimitBound !== '1') {
    button.dataset.desktopManagersLimitBound = '1';
    button.addEventListener('click', validate, true);
  }
  if (input.dataset.desktopManagersLimitBound !== '1') {
    input.dataset.desktopManagersLimitBound = '1';
    input.addEventListener('keydown', event => {
      if (event.key === 'Enter') validate(event);
    }, true);
  }
}

function setupDesktopManagersMonthPickers() {
  const media = window.matchMedia('(any-pointer: coarse)');
  const scan = () => {
    syncDesktopManagersLedgerMonthTrigger();
    if (!isDesktopInteractionWorkspace()) return;
    for (const input of document.querySelectorAll('input[type="month"]')) {
      if (input.id === 'monthFilter' || input.id === 'openingMonth') continue;
      ensureDesktopManagersMonthPickerForInput(input);
    }
  };
  scan();
  if (typeof media.addEventListener === 'function') media.addEventListener('change', scan);
  else media.addListener?.(scan);

  if (document.body.dataset.desktopManagersMonthObserver !== '1') {
    document.body.dataset.desktopManagersMonthObserver = '1';
    const observer = new MutationObserver(scan);
    observer.observe(document.body, { childList: true, subtree: true });
  }
}

function syncDesktopManagersLedgerMonthTrigger() {
  document.querySelector('#ledgerMonthPickerCustom .cy-month-picker-caret')?.remove();
}

function ensureDesktopManagersMonthPickerForInput(input) {
  if (!input || input.dataset.desktopManagersMonthPicker === '1') return;
  const label = input.closest('label') || input.parentElement;
  if (!label) return;
  input.dataset.desktopManagersMonthPicker = '1';
  input.classList.add('cy-native-month-source');

  const root = document.createElement('div');
  root.className = 'cy-month-picker-custom cy-month-picker-field';
  root.innerHTML = `
    <button type="button" class="cy-month-picker-trigger cy-month-picker-field-trigger" aria-haspopup="dialog" aria-expanded="false"><span data-cy-month-label>—</span></button>
    <div class="cy-month-picker-popover" role="dialog" aria-label="選擇月份" hidden>
      <div class="cy-month-picker-head">
        <button type="button" class="cy-month-picker-nav" data-picker-nav="-1" aria-label="上一組">‹</button>
        <button type="button" class="cy-month-picker-year" data-picker-year-head aria-label="切換年份選擇"></button>
        <button type="button" class="cy-month-picker-nav" data-picker-nav="1" aria-label="下一組">›</button>
      </div>
      <div class="cy-month-picker-grid" data-picker-grid></div>
    </div>`;
  input.insertAdjacentElement('afterend', root);

  const trigger = root.querySelector('.cy-month-picker-trigger');
  const popover = root.querySelector('.cy-month-picker-popover');
  const yearHead = root.querySelector('[data-picker-year-head]');
  const grid = root.querySelector('[data-picker-grid]');
  let view = 'months';
  let displayYear = desktopManagersReadMonth(input).year;
  let yearStart = displayYear - 5;

  const render = () => {
    const selected = desktopManagersReadMonth(input);
    if (view === 'months') {
      yearHead.textContent = String(displayYear);
      grid.className = 'cy-month-picker-grid month-view';
      grid.innerHTML = CY_DESKTOP_MANAGERS_MONTHS.map((monthLabel, index) => {
        const month = index + 1;
        const active = selected.valid && selected.year === displayYear && selected.month === month;
        return `<button type="button" class="cy-month-choice${active ? ' active' : ''}" data-picker-month="${month}">${monthLabel}</button>`;
      }).join('');
    } else {
      yearHead.textContent = `${yearStart}–${yearStart + 11}`;
      grid.className = 'cy-month-picker-grid year-view';
      grid.innerHTML = Array.from({ length: 12 }, (_, index) => yearStart + index).map(year =>
        `<button type="button" class="cy-year-choice${selected.valid && selected.year === year ? ' active' : ''}" data-picker-year="${year}">${year}</button>`
      ).join('');
    }
  };

  const syncLabel = () => {
    const selected = desktopManagersReadMonth(input);
    const target = root.querySelector('[data-cy-month-label]');
    if (target) target.textContent = selected.valid ? `${selected.year}年${String(selected.month).padStart(2, '0')}月` : '選擇月份';
  };

  const close = focusTrigger => {
    popover.hidden = true;
    trigger.setAttribute('aria-expanded', 'false');
    if (focusTrigger) trigger.focus();
  };

  trigger.addEventListener('click', event => {
    event.stopPropagation();
    if (!isDesktopInteractionWorkspace()) return;
    if (!popover.hidden) return close(false);
    const selected = desktopManagersReadMonth(input);
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

function desktopManagersReadMonth(input) {
  const match = /^(\d{4})-(\d{2})$/.exec(String(input?.value || ''));
  if (match) return { valid: true, year: Number(match[1]), month: Number(match[2]) };
  const now = new Date();
  return { valid: false, year: now.getFullYear(), month: now.getMonth() + 1 };
}

function desktopManagersCharCount(value) {
  return Array.from(String(value || '')).length;
}

function desktopManagersEscape(value) {
  return String(value ?? '')
    .replaceAll('&', '&amp;')
    .replaceAll('<', '&lt;')
    .replaceAll('>', '&gt;')
    .replaceAll('"', '&quot;')
    .replaceAll("'", '&#39;');
}

const CY_DesktopUi_MONTHS = ['一月', '二月', '三月', '四月', '五月', '六月', '七月', '八月', '九月', '十月', '十一月', '十二月'];
const CY_DesktopUi_WEEKDAYS = ['日', '一', '二', '三', '四', '五', '六'];
let cyDesktopUiRenderingManagers = false;
const cyDesktopUiConfirmBypass = new WeakSet();
installDesktopUiConfirmDialog();
installDesktopUiConfirmInterceptors();

const runDesktopUiPatch = () => {
  setupDesktopUiDatePickers();
  auditDesktopUiMonthPickers();
  refineDesktopUiHeaderIdentity();
};

if (document.readyState === 'loading') {
  document.addEventListener('DOMContentLoaded', () => setTimeout(runDesktopUiPatch, 0), { once: true });
} else {
  setTimeout(runDesktopUiPatch, 0);
}
window.addEventListener('load', () => {
  runDesktopUiPatch();
  setTimeout(runDesktopUiPatch, 80);
  setTimeout(runDesktopUiPatch, 300);
}, { once: true });



/* -------------------------------------------------------------------------- */
/* Managed confirmation dialog                                                */
/* -------------------------------------------------------------------------- */

function installDesktopUiConfirmDialog() {
  if (window.cyConfirm && document.querySelector('#cyConfirmDialog')) return;
  let dialog = document.querySelector('#cyConfirmDialog');
  if (!dialog) {
    dialog = document.createElement('dialog');
    dialog.id = 'cyConfirmDialog';
    dialog.className = 'cy-confirm-dialog';
    dialog.innerHTML = `
      <div class="cy-confirm-shell">
        <h2 class="cy-confirm-title">確認</h2>
        <p class="cy-confirm-message"></p>
        <p class="cy-confirm-detail" hidden></p>
        <div class="cy-confirm-actions">
          <button type="button" class="secondary cy-confirm-cancel">取消</button>
          <button type="button" class="primary cy-confirm-ok">確認</button>
        </div>
      </div>`;
    document.body.appendChild(dialog);
  }

  let pendingResolve = null;
  const finish = value => {
    const resolve = pendingResolve;
    pendingResolve = null;
    if (dialog.open) dialog.close();
    resolve?.(Boolean(value));
  };

  dialog.querySelector('.cy-confirm-cancel')?.addEventListener('click', () => finish(false));
  dialog.querySelector('.cy-confirm-ok')?.addEventListener('click', () => finish(true));
  dialog.addEventListener('cancel', event => {
    event.preventDefault();
    finish(false);
  });
  dialog.addEventListener('close', () => {
    if (pendingResolve) finish(false);
  });

  window.cyConfirm = options => new Promise(resolve => {
    if (pendingResolve) {
      const previous = pendingResolve;
      pendingResolve = null;
      previous(false);
    }
    const config = typeof options === 'string' ? { message: options } : (options || {});
    const title = dialog.querySelector('.cy-confirm-title');
    const message = dialog.querySelector('.cy-confirm-message');
    const detail = dialog.querySelector('.cy-confirm-detail');
    const ok = dialog.querySelector('.cy-confirm-ok');
    const cancel = dialog.querySelector('.cy-confirm-cancel');
    if (title) title.textContent = config.title || '確認';
    if (message) message.textContent = config.message || '';
    if (detail) {
      detail.textContent = config.detail || '';
      detail.hidden = !config.detail;
    }
    if (ok) {
      ok.textContent = config.confirmText || '確認';
      ok.classList.toggle('cy-confirm-danger', Boolean(config.danger));
    }
    if (cancel) cancel.textContent = config.cancelText || '取消';
    pendingResolve = resolve;
    if (!dialog.open) dialog.showModal();
    requestAnimationFrame(() => (config.danger ? cancel : ok)?.focus());
  });
}

function installDesktopUiConfirmInterceptors() {
  if (document.documentElement.dataset.desktopUiConfirmBound === '1') return;
  document.documentElement.dataset.desktopUiConfirmBound = '1';

  document.addEventListener('click', async event => {
    const target = event.target.closest([
      '[data-delete-id]',
      '[data-account-delete]',
      '[data-category-delete]',
      '[data-group-delete]',
      '#excelImportCommitButton',
      '#backupRunNow',
      '#desktopMigrationCommitV19'
    ].join(','));
    if (!target || cyDesktopUiConfirmBypass.has(target)) return;

    const spec = desktopUiConfirmSpec(target);
    if (!spec) return;
    event.preventDefault();
    event.stopImmediatePropagation();

    const accepted = await window.cyConfirm(spec);
    if (!accepted || !target.isConnected) return;

    cyDesktopUiConfirmBypass.add(target);
    const nativeConfirm = window.confirm;
    window.confirm = () => true;
    try {
      target.click();
    } finally {
      window.confirm = nativeConfirm;
      queueMicrotask(() => cyDesktopUiConfirmBypass.delete(target));
    }
  }, true);

  /* Build 14 owns rename UX. Capture here so legacy prompt() paths can never win. */
  document.addEventListener('click', event => {
    const button = event.target.closest('[data-account-rename], [data-category-rename], [data-group-rename]');
    if (!button || typeof beginDesktopManagersInlineEdit !== 'function') return;
    let type = '';
    let id = 0;
    if (button.dataset.accountRename) { type = 'account'; id = Number(button.dataset.accountRename); }
    else if (button.dataset.categoryRename) { type = 'category'; id = Number(button.dataset.categoryRename); }
    else if (button.dataset.groupRename) { type = 'group'; id = Number(button.dataset.groupRename); }
    if (!type || !Number.isInteger(id) || id <= 0) return;
    event.preventDefault();
    event.stopImmediatePropagation();
    beginDesktopManagersInlineEdit(type, id, button);
  }, true);
}

function desktopUiConfirmSpec(target) {
  if (target.matches('[data-delete-id]')) {
    return { title: '刪除記帳', message: '確定刪除這筆記帳？', confirmText: '刪除', danger: true };
  }
  if (target.matches('[data-account-delete]')) {
    const id = Number(target.dataset.accountDelete);
    const item = typeof state === 'object' ? (state.accounts || []).find(row => Number(row.id) === id) : null;
    return {
      title: '刪除帳戶',
      message: `確定刪除帳戶「${item?.name || ''}」？`,
      detail: '既有歷史記帳仍會保留原帳戶名稱。',
      confirmText: '刪除',
      danger: true
    };
  }
  if (target.matches('[data-category-delete]')) {
    const id = Number(target.dataset.categoryDelete);
    const item = typeof state === 'object' ? (state.categories || []).find(row => Number(row.id) === id) : null;
    return {
      title: '刪除科目',
      message: `確定刪除科目「${item?.name || ''}」？`,
      detail: '既有歷史記帳仍會保留原科目名稱。',
      confirmText: '刪除',
      danger: true
    };
  }
  if (target.matches('[data-group-delete]')) {
    const id = Number(target.dataset.groupDelete);
    const item = typeof state === 'object' ? (state.groups || []).find(row => Number(row.id) === id) : null;
    return { title: '刪除大分類', message: `確定刪除大分類「${item?.name || ''}」？`, confirmText: '刪除', danger: true };
  }
  if (target.id === 'excelImportCommitButton') {
    const ready = Number(typeof cyV15ImportState === 'object' ? cyV15ImportState.preview?.summary?.ready : 0) || 0;
    const duplicates = Number(typeof cyV15ImportState === 'object' ? cyV15ImportState.preview?.summary?.duplicates : 0) || 0;
    return {
      title: '匯入 Excel',
      message: `確定匯入 ${ready.toLocaleString()} 筆資料？`,
      detail: duplicates ? `另有 ${duplicates.toLocaleString()} 筆重複資料會自動略過。` : '',
      confirmText: '匯入'
    };
  }
  if (target.id === 'backupRunNow') {
    const tiered = typeof backupTopologyV18 !== 'undefined' && backupTopologyV18 === 'parallel_dual_provider';
    return {
      title: '立即執行備份',
      message: tiered ? '現在立即執行一次 R2 + GCS paired backup？' : '現在立即執行一次 Google Cloud Storage 測試備份？',
      detail: '正常每日備份仍會在排程時間自動執行。',
      confirmText: '開始備份'
    };
  }
  if (target.id === 'desktopMigrationCommitV19') {
    const preview = typeof migrationState !== 'undefined' ? migrationState.preview : null;
    const plan = preview?.plan || {};
    const tx = Number(plan.transactions?.insert || 0);
    const accounts = Number(plan.accounts?.insert || 0);
    const groups = Number(plan.groups?.insert || 0);
    const categories = Number(plan.categories?.insert || 0);
    const opening = Number(plan.openingBalances?.insert || 0);
    return {
      title: '確認資料移轉',
      message: `確定將預覽內容寫入 Web 帳本？`,
      detail: `新增：${accounts} 個帳戶、${groups} 個大分類、${categories} 個科目、${tx} 筆交易、${opening} 筆期初餘額。\n既有 Web 交易不會被刪除。`,
      confirmText: '執行移轉'
    };
  }
  return null;
}

/* -------------------------------------------------------------------------- */
/* Deterministic Desktop managers                                             */
/* -------------------------------------------------------------------------- */

function auditDesktopUiMonthPickers() {
  if (!isDesktopInteractionWorkspace()) return;
  if (typeof ensureDesktopManagersMonthPickerForInput === 'function') {
    document.querySelectorAll('input[type="month"]').forEach(input => ensureDesktopManagersMonthPickerForInput(input));
  }
  if (typeof syncDesktopManagersLedgerMonthTrigger === 'function') syncDesktopManagersLedgerMonthTrigger();
}

function setupDesktopUiDatePickers() {
  const media = window.matchMedia('(any-pointer: coarse)');
  const scan = () => {
    if (!isDesktopInteractionWorkspace()) return;
    document.querySelectorAll('input[type="date"]').forEach(ensureDesktopUiDatePicker);
  };
  scan();
  if (document.body.dataset.desktopUiDateObserver !== '1') {
    document.body.dataset.desktopUiDateObserver = '1';
    const observer = new MutationObserver(scan);
    observer.observe(document.body, { childList: true, subtree: true });
  }
  if (typeof media.addEventListener === 'function' && document.body.dataset.desktopUiDateMedia !== '1') {
    document.body.dataset.desktopUiDateMedia = '1';
    media.addEventListener('change', scan);
  }
}

function ensureDesktopUiDatePicker(input) {
  if (!input || input.dataset.desktopUiDatePicker === '1') return;
  input.dataset.desktopUiDatePicker = '1';
  input.classList.add('desktopUi-native-date-source');

  const root = document.createElement('div');
  root.className = 'desktopUi-date-picker';
  root.innerHTML = `
    <button type="button" class="desktopUi-date-trigger" aria-haspopup="dialog" aria-expanded="false">
      <span class="desktopUi-date-label">—</span><span class="desktopUi-date-calendar-icon" aria-hidden="true">▣</span>
    </button>
    <div class="desktopUi-date-popover" role="dialog" aria-label="選擇日期" hidden>
      <div class="desktopUi-date-head">
        <button type="button" data-desktopUi-date-nav="-1" aria-label="上一個">‹</button>
        <button type="button" class="desktopUi-date-title" aria-label="切換年月選擇"></button>
        <button type="button" data-desktopUi-date-nav="1" aria-label="下一個">›</button>
      </div>
      <div class="desktopUi-date-content"></div>
      <div class="desktopUi-date-footer"><button type="button" class="desktopUi-date-today-button">今天</button></div>
    </div>`;
  input.insertAdjacentElement('afterend', root);

  const trigger = root.querySelector('.desktopUi-date-trigger');
  const label = root.querySelector('.desktopUi-date-label');
  const popover = root.querySelector('.desktopUi-date-popover');
  const title = root.querySelector('.desktopUi-date-title');
  const content = root.querySelector('.desktopUi-date-content');
  let view = 'days';
  let selected = desktopUiReadDate(input);
  let displayYear = selected.year;
  let displayMonth = selected.month;
  let yearStart = displayYear - 5;

  const syncLabel = () => {
    selected = desktopUiReadDate(input);
    label.textContent = `${selected.year}/${String(selected.month).padStart(2, '0')}/${String(selected.day).padStart(2, '0')}`;
  };

  const render = () => {
    selected = desktopUiReadDate(input);
    if (view === 'days') renderDesktopUiDays();
    else if (view === 'months') renderDesktopUiMonths();
    else renderDesktopUiYears();
  };

  const renderDesktopUiDays = () => {
    title.textContent = `${displayYear}年${String(displayMonth).padStart(2, '0')}月`;
    const first = new Date(displayYear, displayMonth - 1, 1);
    const start = new Date(displayYear, displayMonth - 1, 1 - first.getDay());
    const today = desktopUiToday();
    const cells = Array.from({ length: 42 }, (_, index) => {
      const date = new Date(start.getFullYear(), start.getMonth(), start.getDate() + index);
      const y = date.getFullYear();
      const m = date.getMonth() + 1;
      const d = date.getDate();
      const value = `${y}-${String(m).padStart(2, '0')}-${String(d).padStart(2, '0')}`;
      const active = y === selected.year && m === selected.month && d === selected.day;
      const current = y === today.year && m === today.month && d === today.day;
      const other = m !== displayMonth;
      return `<button type="button" class="desktopUi-date-day${active ? ' active' : ''}${current ? ' today' : ''}${other ? ' other-month' : ''}" data-desktopUi-date-value="${value}">${d}</button>`;
    }).join('');
    content.innerHTML = `<div class="desktopUi-date-weekdays">${CY_DesktopUi_WEEKDAYS.map(day => `<span>${day}</span>`).join('')}</div><div class="desktopUi-date-days">${cells}</div>`;
  };

  const renderDesktopUiMonths = () => {
    title.textContent = String(displayYear);
    content.innerHTML = `<div class="desktopUi-date-choice-grid">${CY_DesktopUi_MONTHS.map((name, index) => {
      const month = index + 1;
      const active = selected.year === displayYear && selected.month === month;
      return `<button type="button" class="desktopUi-date-month-choice${active ? ' active' : ''}" data-desktopUi-date-month="${month}">${name}</button>`;
    }).join('')}</div>`;
  };

  const renderDesktopUiYears = () => {
    title.textContent = `${yearStart}–${yearStart + 11}`;
    content.innerHTML = `<div class="desktopUi-date-choice-grid">${Array.from({ length: 12 }, (_, index) => yearStart + index).map(year => {
      const active = selected.year === year;
      return `<button type="button" class="desktopUi-date-year-choice${active ? ' active' : ''}" data-desktopUi-date-year="${year}">${year}</button>`;
    }).join('')}</div>`;
  };

  const open = () => {
    selected = desktopUiReadDate(input);
    displayYear = selected.year;
    displayMonth = selected.month;
    yearStart = displayYear - 5;
    view = 'days';
    render();
    popover.hidden = false;
    trigger.setAttribute('aria-expanded', 'true');
  };

  const close = (focus = false) => {
    popover.hidden = true;
    trigger.setAttribute('aria-expanded', 'false');
    if (focus) trigger.focus();
  };

  trigger.addEventListener('click', event => {
    event.stopPropagation();
    if (popover.hidden) open(); else close(false);
  });

  root.querySelectorAll('[data-desktopUi-date-nav]').forEach(button => button.addEventListener('click', () => {
    const delta = Number(button.dataset.desktopUiDateNav) || 0;
    if (view === 'days') {
      const next = new Date(displayYear, displayMonth - 1 + delta, 1);
      displayYear = next.getFullYear();
      displayMonth = next.getMonth() + 1;
    } else if (view === 'months') {
      displayYear += delta;
    } else {
      yearStart += delta * 12;
    }
    render();
  }));

  title.addEventListener('click', () => {
    if (view === 'days') view = 'months';
    else if (view === 'months') {
      view = 'years';
      yearStart = displayYear - 5;
    } else view = 'months';
    render();
  });

  content.addEventListener('click', event => {
    const day = event.target.closest('[data-desktopUi-date-value]');
    if (day) {
      input.value = day.dataset.desktopUiDateValue;
      input.dispatchEvent(new Event('change', { bubbles: true }));
      syncLabel();
      close(true);
      return;
    }
    const month = event.target.closest('[data-desktopUi-date-month]');
    if (month) {
      displayMonth = Number(month.dataset.desktopUiDateMonth) || displayMonth;
      view = 'days';
      render();
      return;
    }
    const year = event.target.closest('[data-desktopUi-date-year]');
    if (year) {
      displayYear = Number(year.dataset.desktopUiDateYear) || displayYear;
      view = 'months';
      render();
    }
  });

  root.querySelector('.desktopUi-date-today-button')?.addEventListener('click', () => {
    const today = desktopUiToday();
    input.value = `${today.year}-${String(today.month).padStart(2, '0')}-${String(today.day).padStart(2, '0')}`;
    input.dispatchEvent(new Event('change', { bubbles: true }));
    syncLabel();
    close(true);
  });

  input.addEventListener('change', () => {
    syncLabel();
    if (!popover.hidden) render();
  });
  document.addEventListener('pointerdown', event => {
    if (!popover.hidden && !root.contains(event.target)) close(false);
  });
  document.addEventListener('keydown', event => {
    if (event.key === 'Escape' && !popover.hidden) close(true);
  });
  syncLabel();
}

function desktopUiReadDate(input) {
  const match = /^(\d{4})-(\d{2})-(\d{2})$/.exec(String(input?.value || ''));
  if (match) return { year: Number(match[1]), month: Number(match[2]), day: Number(match[3]) };
  return desktopUiToday();
}

function desktopUiToday() {
  const now = new Date();
  return { year: now.getFullYear(), month: now.getMonth() + 1, day: now.getDate() };
}

function refineDesktopUiHeaderIdentity() {
  const user = document.querySelector('#currentUser');
  if (!user) return;
  user.style.removeProperty('padding-top');
  user.style.removeProperty('padding-bottom');
}

/* Desktop custom date keyboard bridge. */


const setupDesktopUiKeyboardBridge = () => {
  if (!isDesktopInteractionWorkspace()) return;
  const input = document.querySelector('#txDate');
  const root = input?.nextElementSibling?.classList?.contains('desktopUi-date-picker') ? input.nextElementSibling : null;
  const trigger = root?.querySelector('.desktopUi-date-trigger');
  if (!input || !trigger || trigger.dataset.desktopUiKeyboardBound === '1') return;
  trigger.dataset.desktopUiKeyboardBound = '1';

  trigger.addEventListener('keydown', event => {
    if (event.isComposing) return;

    if (event.key === 'Enter' && !event.ctrlKey && !event.altKey && !event.metaKey && !event.shiftKey) {
      event.preventDefault();
      event.stopPropagation();
      document.querySelector('#summary')?.focus();
      return;
    }

    if (event.key === 'Tab' && !event.shiftKey && !event.ctrlKey && !event.altKey && !event.metaKey) {
      event.preventDefault();
      event.stopPropagation();
      if (typeof state === 'object' && typeof setEntryKind === 'function') {
        setEntryKind(state.kind === 'expense' ? 'income' : 'expense');
        if (typeof updateEntryKindVisual === 'function') updateEntryKindVisual();
        if (typeof renderFavoriteCategories === 'function') renderFavoriteCategories();
        if (typeof loadFrequentSummaries === 'function') loadFrequentSummaries();
      }
      trigger.focus();
      return;
    }

    const quickDigit = /^\d$/.test(event.key) && !event.ctrlKey && !event.altKey && !event.metaKey;
    const quickStep = event.ctrlKey && !event.altKey && !event.metaKey && (event.key === 'ArrowUp' || event.key === 'ArrowDown');
    if (!quickDigit && !quickStep) return;

    event.preventDefault();
    event.stopPropagation();
    input.dispatchEvent(new KeyboardEvent('keydown', {
      key: event.key,
      code: event.code,
      ctrlKey: event.ctrlKey,
      altKey: event.altKey,
      metaKey: event.metaKey,
      shiftKey: event.shiftKey,
      bubbles: true,
      cancelable: true
    }));
  });
};

if (document.readyState === 'complete') setTimeout(setupDesktopUiKeyboardBridge, 0);
else window.addEventListener('load', () => setTimeout(setupDesktopUiKeyboardBridge, 0), { once: true });
setTimeout(setupDesktopUiKeyboardBridge, 350);

let settingsManagerDialogState = null;
let settingsManagerDrag = null;
let settingsManagerPointer = null;
let settingsManagerSaving = false;

window.cySettingsManager = {
  renderAccountManager: renderSettingsAccountManager,
  renderCategoryManager: renderSettingsCategoryManager,
  openAddGroupDialog: openSettingsAddGroupDialog,
  openAddCategoryDialog: openSettingsAddCategoryDialog,
  openRenameDialog: openSettingsRenameDialog
};

const setupSettingsManager = () => {
  ensureSettingsManagerDialog();
  bindSettingsManagerActions();
  setupSettingsManagerDragAndDrop();
  setupArchivedAccountDialog();
  renderSettingsAccountManager();
  renderSettingsCategoryManager();
};

if (document.readyState === 'loading') {
  document.addEventListener('DOMContentLoaded', () => setTimeout(setupSettingsManager, 0), { once: true });
} else {
  setTimeout(setupSettingsManager, 0);
}
window.addEventListener('load', () => setTimeout(setupSettingsManager, 0), { once: true });

function renderSettingsAccountManager() {
  if (typeof state !== 'object') return;
  renderArchivedAccountManager();
  if (!isDesktopInteractionWorkspace()) return renderMobileAccountManager();

  const host = document.querySelector('#accountRows');
  if (!host) return;
  const accounts = Array.isArray(state.accounts) ? state.accounts : [];

  const activeHtml = accounts.length
    ? accounts.map(account => {
        const id = Number(account.id);
        const isDefault = Number(account.is_default) === 1;
        return `<div class="settings-account-row" data-settings-account-row="${id}">
          <button type="button" class="settings-drag-handle" draggable="true" data-settings-drag-account="${id}" title="拖曳調整帳戶順序" aria-label="拖曳調整帳戶順序">⠿</button>
          ${isDefault
            ? '<button type="button" class="settings-default-tag active" disabled aria-label="目前預設帳戶">預設</button>'
            : `<button type="button" class="settings-default-tag" data-account-default="${id}" title="設為預設帳戶">設為預設</button>`}
          <div class="settings-account-name-cell">
            <strong class="settings-editable-name">${settingsManagerEscape(account.name)}</strong>
            <button type="button" class="mini-button settings-edit-button" data-account-rename="${id}" title="編輯帳戶名稱" aria-label="編輯帳戶名稱">${settingsActionIcon('edit')}</button>
          </div>
          <button type="button" class="mini-button settings-archive-button" data-account-archive="${id}" title="封存帳戶" aria-label="封存帳戶">${settingsActionIcon('archive')}</button>
        </div>`;
      }).join('')
    : '<div class="empty">尚無可用帳戶。</div>';

  host.innerHTML = `
    <section class="settings-account-section">
      <div class="settings-account-active-list">${activeHtml}</div>
    </section>`;
}

function setupArchivedAccountDialog() {
  const dialog = document.querySelector('#archivedAccountsDialog');
  const button = document.querySelector('#openArchivedAccountsButton');
  if (!dialog || !button || dialog.dataset.bound === '1') return;
  dialog.dataset.bound = '1';
  button.addEventListener('click', () => {
    renderArchivedAccountManager();
    setDialogMessage(dialog.querySelector('#archivedAccountsMessage'), '');
    if (!dialog.open) dialog.showModal();
  });
  dialog.querySelector('[data-close-archived-accounts]').addEventListener('click', () => dialog.close());
  dialog.addEventListener('click', handleAccountAction);
}

function renderArchivedAccountManager() {
  const host = document.querySelector('#archivedAccountRows');
  if (!host || typeof state !== 'object') return;
  const archived = Array.isArray(state.archivedAccounts) ? state.archivedAccounts : [];
  const isSuperAdmin = String(window.cyaccCurrentUser?.role || '') === 'SUPER_ADMIN';
  host.innerHTML = archived.length
    ? archived.map(account => settingsArchivedAccountHtml(account, isSuperAdmin)).join('')
    : '<div class="empty">沒有已封存帳戶。</div>';
}

function settingsActionIcon(action) {
  const path = action === 'edit'
    ? 'M15 5l4 4M4 20l4-1L20 7a2.1 2.1 0 0 0-3-3L5 16z'
    : action === 'archive'
      ? 'M3 3h18v5H3zM5 8v13h14V8M9 12h6'
      : 'M3 6h18M9 6V4h6v2M5 6l1 14h12l1-14M10 10v6M14 10v6';
  return `<svg class="settings-action-icon" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true" focusable="false"><path d="${path}"/></svg>`;
}

function settingsArchivedAccountHtml(account, isSuperAdmin) {
  const id = Number(account.id);
  const txCount = Number(account.transaction_count || 0);
  const latestOpening = Number(account.latest_opening_amount || 0);
  const cannotDelete = txCount > 0 || latestOpening !== 0;
  const permanent = isSuperAdmin
    ? `<button type="button" class="mini-button danger settings-delete-button" data-account-permanent-delete="${id}" aria-label="永久刪除帳戶" title="${cannotDelete ? '仍有歷史記帳或目前期初餘額非 0' : '永久刪除帳戶'}"${cannotDelete ? ' disabled' : ''}>${settingsActionIcon('delete')}</button>`
    : '';

  return `<div class="settings-archived-account-row">
    <div class="settings-archived-account-main">
      <strong>${settingsManagerEscape(account.name)}</strong>
    </div>
    <div class="settings-archived-account-actions">
      <button type="button" class="mini-button" data-account-restore="${id}">解封</button>
      ${permanent}
    </div>
  </div>`;
}

function renderSettingsCategoryManager() {
  if (typeof state !== 'object') return;

  const host = document.querySelector('#categoryManager');
  const pane = document.querySelector('[data-settings-pane="categories"]');
  if (!host || !pane) return;

  const desktop = isDesktopInteractionWorkspace();
  const kind = state.settingsKind === 'income' ? 'income' : 'expense';
  pane.classList.toggle('settings-kind-income', kind === 'income');
  pane.classList.toggle('settings-kind-expense', kind === 'expense');
  document.querySelectorAll('[data-settings-kind]').forEach(button =>
    button.classList.toggle('active', button.dataset.settingsKind === kind)
  );

  const groups = (state.groups || []).filter(group => group.kind === kind);
  const toolbar = desktop
    ? `<div class="settings-category-toolbar">
        <div class="entry-kind-switch settings-kind-switch" role="group" aria-label="收入或支出">
          <button type="button" class="kind-button${kind === 'income' ? ' active' : ''}" data-settings-kind-choice="income">收入</button>
          <button type="button" class="kind-button${kind === 'expense' ? ' active' : ''}" data-settings-kind-choice="expense">支出</button>
        </div>
        <button type="button" class="secondary compact settings-toolbar-button" data-settings-add-group>＋ 新增分類</button>
        <span class="settings-toolbar-spacer" aria-hidden="true"></span>
        <button type="button" class="secondary compact settings-toolbar-button settings-add-category-button" data-settings-add-category${groups.some(group => Number(group.id) > 0) ? '' : ' disabled title="請先新增分類"'}>＋ 新增科目</button>
      </div>`
    : '';

  const tree = groups.length
    ? `<div class="settings-category-tree">${groups.map(group => settingsCategoryGroupHtml(group, kind)).join('')}</div>`
    : '<div class="settings-category-list-empty">目前沒有分類。請先新增分類。</div>';

  host.innerHTML = `<div class="settings-category-shell">${toolbar}${tree}</div>`;

  if (desktop) {
    pane.querySelector('#mobileCategoryActions')?.remove();
  } else {
    ensureMobileCategoryActions(pane, groups.some(group => Number(group.id) > 0));
  }
}

function settingsCategoryGroupHtml(group, kind) {
  const groupId = Number(group.id);
  const pendingGroup = groupId < 0;
  const categories = (state.categories || []).filter(category =>
    category.kind === kind && Number(category.group_id) === groupId
  );

  const rows = categories.length
    ? categories.map(category => {
        const id = Number(category.id);
        const pending = id < 0;
        const favorite = Number(category.is_favorite) === 1;
        return `<div class="settings-category-leaf${pending ? ' is-pending' : ''}" data-settings-category-row="${id}" data-settings-category-group="${groupId}">
          ${pending
            ? '<span class="settings-tree-spacer" aria-hidden="true"></span>'
            : `<button type="button" class="settings-drag-handle" draggable="true" data-settings-drag-category="${id}" title="拖曳調整科目順序或分類" aria-label="拖曳調整科目順序或分類">⠿</button>`}
          ${pending
            ? '<span class="settings-tree-spacer" aria-hidden="true"></span>'
            : `<button type="button" class="settings-favorite${favorite ? ' active' : ''}" data-category-favorite="${id}" title="${favorite ? '取消常用科目' : '設為常用科目'}" aria-label="${favorite ? '取消常用科目' : '設為常用科目'}">${favorite ? '★' : '☆'}</button>`}
          <span class="settings-category-name" title="${settingsManagerEscape(category.name)}">${settingsManagerEscape(category.name)}</span>
          <span class="settings-tree-actions">
            ${pending
              ? '<span class="settings-pending-label">儲存中…</span>'
              : `<button type="button" class="mini-button settings-edit-button" data-settings-rename="category" data-settings-id="${id}" title="編輯科目名稱" aria-label="編輯科目名稱">${settingsActionIcon('edit')}</button>
                 <button type="button" class="mini-button danger settings-delete-button" data-category-delete="${id}" title="刪除科目" aria-label="刪除科目">${settingsActionIcon('delete')}</button>`}
          </span>
        </div>`;
      }).join('')
    : '<div class="settings-category-empty">尚無科目</div>';

  return `<section class="settings-category-branch${pendingGroup ? ' is-pending' : ''}" data-group-id="${groupId}">
    <div class="settings-category-parent">
      <strong class="settings-group-name" title="${settingsManagerEscape(group.name)}">${settingsManagerEscape(group.name)}</strong>
      <span class="settings-tree-actions">
        ${pendingGroup
          ? '<span class="settings-pending-label">儲存中…</span>'
          : `${settingsGroupOrderButtons(groupId, kind)}<button type="button" class="mini-button settings-edit-button" data-settings-rename="group" data-settings-id="${groupId}" title="編輯大分類名稱" aria-label="編輯大分類名稱">${settingsActionIcon('edit')}</button>
             <button type="button" class="mini-button danger settings-delete-button" data-group-delete="${groupId}" title="刪除大分類" aria-label="刪除大分類">${settingsActionIcon('delete')}</button>`}
      </span>
    </div>
    <div class="settings-category-children" data-settings-category-dropzone="${groupId}">
      ${rows}
    </div>
  </section>`;
}

function settingsGroupOrderButtons(groupId, kind) {
  const groups = (state.groups || []).filter(group => group.kind === kind);
  const index = groups.findIndex(group => Number(group.id) === groupId);
  const pending = groups.some(group => Number(group.id) <= 0);
  return `<button type="button" class="mini-button settings-group-order" data-settings-group-move="${groupId}" data-direction="up" aria-label="分類上移" title="分類上移"${pending || index <= 0 ? ' disabled' : ''}>↑</button>
    <button type="button" class="mini-button settings-group-order" data-settings-group-move="${groupId}" data-direction="down" aria-label="分類下移" title="分類下移"${pending || index < 0 || index >= groups.length - 1 ? ' disabled' : ''}>↓</button>`;
}

function restoreSettingsOrderState(current, previous, fields) {
  const before = new Map(previous.map((item, index) => [Number(item.id), { item, index }]));
  return current.map(item => {
    const prior = before.get(Number(item.id));
    if (!prior) return item;
    return { ...item, ...Object.fromEntries(fields.map(field => [field, prior.item[field]])) };
  }).sort((a, b) => (before.get(Number(a.id))?.index ?? 999999) - (before.get(Number(b.id))?.index ?? 999999));
}

function renderSettingsCategorySurfaces() {
  const selected = document.querySelector('#categoryName')?.value || '';
  renderSettingsCategoryManager();
  if (typeof renderCategories === 'function') renderCategories(selected);
  if (typeof renderFavoriteCategories === 'function') renderFavoriteCategories();
}

async function moveSettingsGroup(id, direction) {
  if (settingsManagerSaving || !['up', 'down'].includes(direction)) return;
  const kind = state.settingsKind === 'income' ? 'income' : 'expense';
  const previous = [...(state.groups || [])];
  const groups = previous.filter(group => group.kind === kind);
  if (groups.some(group => Number(group.id) <= 0)) return;
  const ids = groups.map(group => Number(group.id));
  const index = ids.indexOf(id);
  const targetIndex = index + (direction === 'up' ? -1 : 1);
  if (index < 0 || targetIndex < 0 || targetIndex >= ids.length) return;
  [ids[index], ids[targetIndex]] = [ids[targetIndex], ids[index]];
  const byId = new Map(groups.map(group => [Number(group.id), group]));
  let cursor = 0;
  state.groups = previous.map(group => group.kind === kind
    ? { ...byId.get(ids[cursor]), sort_order: cursor++ + 1 }
    : group);
  renderSettingsCategorySurfaces();
  await persistSettingsOrder('/api/category-groups/reorder', { kind, ids }, '分類順序已更新。', () => {
    state.groups = restoreSettingsOrderState(state.groups, previous, ['sort_order']);
    renderSettingsCategorySurfaces();
  });
}

function bindSettingsManagerActions() {
  if (document.documentElement.dataset.settingsManagerBound === '1') return;
  document.documentElement.dataset.settingsManagerBound = '1';

  window.addEventListener('click', event => {
    const move = event.target.closest('[data-settings-group-move]');
    if (move) {
      event.preventDefault();
      event.stopImmediatePropagation();
      if (!move.disabled) void moveSettingsGroup(Number(move.dataset.settingsGroupMove), move.dataset.direction);
      return;
    }
    const kind = event.target.closest('[data-settings-kind-choice]');
    if (kind && typeof state === 'object') {
      const value = kind.dataset.settingsKindChoice;
      if (['income', 'expense'].includes(value) && value !== state.settingsKind) {
        event.preventDefault();
        event.stopImmediatePropagation();
        state.settingsKind = value;
        finishSettingsManagerDrag();
        renderSettingsCategoryManager();
      }
      return;
    }

    const favorite = event.target.closest('[data-category-favorite]');
    if (favorite && typeof state === 'object') {
      event.preventDefault();
      event.stopImmediatePropagation();
      const id = Number(favorite.dataset.categoryFavorite || 0);
      const current = (state.categories || []).find(item => Number(item.id) === id);
      if (current) {
        void mutateSettings(`/api/categories/${id}/favorite`, {
          method: 'PUT',
          headers: jsonHeaders(),
          body: JSON.stringify({ favorite: Number(current.is_favorite) !== 1 })
        }, Number(current.is_favorite) === 1 ? '已取消常用科目。' : '已加入常用科目。');
      }
      return;
    }

    const addGroup = event.target.closest('[data-settings-add-group], [data-mobile-group-add]');
    const addCategory = event.target.closest('[data-settings-add-category], [data-mobile-category-add]');
    const rename = event.target.closest('[data-settings-rename], [data-account-rename], [data-category-rename], [data-group-rename]');
    if (!addGroup && !addCategory && !rename) return;

    event.preventDefault();
    event.stopImmediatePropagation();

    if (addGroup) return openSettingsAddGroupDialog();
    if (addCategory) {
      if (addCategory.disabled) return;
      return openSettingsAddCategoryDialog();
    }

    if (rename.dataset.settingsRename) {
      return openSettingsRenameDialog(rename.dataset.settingsRename, Number(rename.dataset.settingsId));
    }
    if (rename.dataset.accountRename) return openSettingsRenameDialog('account', Number(rename.dataset.accountRename));
    if (rename.dataset.categoryRename) return openSettingsRenameDialog('category', Number(rename.dataset.categoryRename));
    if (rename.dataset.groupRename) return openSettingsRenameDialog('group', Number(rename.dataset.groupRename));
  }, true);
}

function ensureSettingsManagerDialog() {
  if (document.querySelector('#settingsManagerDialog')) return;
  const dialog = document.createElement('dialog');
  dialog.id = 'settingsManagerDialog';
  dialog.className = 'settings-manager-dialog';
  dialog.innerHTML = `<form method="dialog" class="settings-manager-dialog-shell" id="settingsManagerForm">
    <div class="settings-manager-dialog-head">
      <div><h2 id="settingsManagerTitle">新增科目</h2><p id="settingsManagerSubtitle"></p></div>
      <button type="button" class="icon-button" data-settings-dialog-close aria-label="關閉">×</button>
    </div>
    <label class="settings-dialog-field" id="settingsGroupField" hidden>
      <span>大分類</span>
      <select id="settingsGroupSelect"></select>
    </label>
    <label class="settings-dialog-field">
      <span id="settingsNameLabel">科目名稱</span>
      <input id="settingsNameInput" type="text" maxlength="60" autocomplete="off">
    </label>
    <div id="settingsManagerMessage" class="dialog-message"></div>
    <div class="settings-manager-dialog-actions">
      <button type="button" class="secondary" data-settings-dialog-close>取消</button>
      <button type="submit" class="primary" id="settingsManagerSave">新增</button>
    </div>
  </form>`;
  document.body.append(dialog);

  dialog.querySelectorAll('[data-settings-dialog-close]').forEach(button => button.addEventListener('click', () => dialog.close()));
  dialog.querySelector('#settingsManagerForm')?.addEventListener('submit', saveSettingsManagerDialog);
  dialog.addEventListener('close', () => { settingsManagerDialogState = null; });
}

function openSettingsAddGroupDialog() {
  if (typeof state !== 'object') return;
  openSettingsManagerDialog({
    mode: 'add-group',
    title: '新增分類',
    subtitle: state.settingsKind === 'income' ? '新增收入分類' : '新增支出分類',
    label: '分類名稱',
    maxLength: 60,
    value: '',
    showGroup: false,
    saveText: '新增'
  });
}

function openSettingsAddCategoryDialog() {
  if (typeof state !== 'object') return;
  const kind = state.settingsKind === 'income' ? 'income' : 'expense';
  const groups = (state.groups || []).filter(group => group.kind === kind && Number(group.id) > 0);
  if (!groups.length) return;
  openSettingsManagerDialog({
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

function openSettingsRenameDialog(type, id) {
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
    title = '編輯分類';
    label = '分類名稱';
  } else if (type === 'category') {
    item = (state.categories || []).find(row => Number(row.id) === id);
    title = '編輯科目';
    label = '科目名稱';
  }
  if (!item) return;
  openSettingsManagerDialog({
    mode: 'rename',
    type,
    id,
    title,
    subtitle: '',
    label,
    maxLength,
    value: item.name || '',
    showGroup: false,
    saveText: '儲存'
  });
}

function openSettingsManagerDialog(config) {
  ensureSettingsManagerDialog();
  const dialog = document.querySelector('#settingsManagerDialog');
  if (!dialog) return;
  settingsManagerDialogState = config;

  dialog.querySelector('#settingsManagerTitle').textContent = config.title || '';
  dialog.querySelector('#settingsManagerSubtitle').textContent = config.subtitle || '';
  dialog.querySelector('#settingsManagerSubtitle').hidden = !config.subtitle;
  dialog.querySelector('#settingsNameLabel').textContent = config.label || '名稱';

  const input = dialog.querySelector('#settingsNameInput');
  input.maxLength = Number(config.maxLength || 60);
  input.value = config.value || '';

  const groupField = dialog.querySelector('#settingsGroupField');
  const select = dialog.querySelector('#settingsGroupSelect');
  groupField.hidden = !config.showGroup;
  if (config.showGroup) {
    select.innerHTML = (config.groups || []).map(group =>
      `<option value="${Number(group.id)}">${settingsManagerEscape(group.name)}</option>`
    ).join('');
  } else {
    select.innerHTML = '';
  }

  dialog.querySelector('#settingsManagerSave').textContent = config.saveText || '儲存';
  setDialogMessage(dialog.querySelector('#settingsManagerMessage'), '');
  if (!dialog.open) dialog.showModal();
  setTimeout(() => input.focus(), 0);
}

async function saveSettingsManagerDialog(event) {
  event.preventDefault();
  const config = settingsManagerDialogState;
  const dialog = document.querySelector('#settingsManagerDialog');
  if (!config || !dialog || typeof state !== 'object') return;

  const input = dialog.querySelector('#settingsNameInput');
  const select = dialog.querySelector('#settingsGroupSelect');
  const save = dialog.querySelector('#settingsManagerSave');
  const message = dialog.querySelector('#settingsManagerMessage');
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
      dialog.close();
      await optimisticAddSettingsGroup(name);
      return;
    } else if (config.mode === 'add-category') {
      const groupId = Number(select?.value);
      if (!Number.isInteger(groupId) || groupId <= 0) {
        setDialogMessage(message, '請選擇大分類。', true);
        return;
      }
      dialog.close();
      await optimisticAddSettingsCategory(name, groupId);
      return;
    } else if (config.mode === 'rename') {
      const endpoints = {
        account: `/api/accounts/${config.id}`,
        group: `/api/category-groups/${config.id}`,
        category: `/api/categories/${config.id}`
      };
      const endpoint = endpoints[config.type];
      if (!endpoint) return;
      dialog.close();
      ok = await mutateSettings(endpoint, {
        method: 'PUT', headers: jsonHeaders(), body: JSON.stringify({ name })
      }, '名稱已更新。');
    }

    if (ok) {
      dialog.close();
      renderSettingsAccountManager();
      renderSettingsCategoryManager();
    }
  } finally {
    save.disabled = false;
  }
}

function setupSettingsManagerDragAndDrop() {
  const accountHost = document.querySelector('#accountRows');
  const categoryHost = document.querySelector('#categoryManager');

  if (accountHost && accountHost.dataset.settingsDragBound !== '1') {
    accountHost.dataset.settingsDragBound = '1';
    accountHost.addEventListener('dragstart', handleSettingsManagerDragStart);
    accountHost.addEventListener('dragover', handleSettingsAccountDragOver);
    accountHost.addEventListener('drop', handleSettingsAccountDrop);
    accountHost.addEventListener('dragend', finishSettingsManagerDrag);
  }

  if (categoryHost && categoryHost.dataset.settingsDragBound !== '1') {
    categoryHost.dataset.settingsDragBound = '1';
    categoryHost.addEventListener('dragstart', handleSettingsManagerDragStart);
    categoryHost.addEventListener('dragover', handleSettingsCategoryDragOver);
    categoryHost.addEventListener('drop', handleSettingsCategoryDrop);
    categoryHost.addEventListener('dragend', finishSettingsManagerDrag);
    categoryHost.addEventListener('pointerdown', startSettingsCategoryPointerDrag);
    categoryHost.addEventListener('pointermove', moveSettingsCategoryPointerDrag);
    categoryHost.addEventListener('pointerup', endSettingsCategoryPointerDrag);
    categoryHost.addEventListener('pointercancel', cancelSettingsCategoryPointerDrag);
    categoryHost.addEventListener('lostpointercapture', cancelSettingsCategoryPointerDrag);
  }
}

function startSettingsCategoryPointerDrag(event) {
  if (event.pointerType === 'mouse' || !event.isPrimary || settingsManagerSaving || settingsManagerPointer) return;
  const handle = event.target.closest('[data-settings-drag-category]');
  const id = Number(handle?.dataset.settingsDragCategory);
  if (!Number.isInteger(id) || id <= 0) return;
  event.preventDefault();
  const host = event.currentTarget;
  settingsManagerDrag = { type: 'category', id };
  settingsManagerPointer = { pointerId: event.pointerId, host, x: event.clientX, y: event.clientY, frame: null };
  handle.closest('[data-settings-category-row]')?.classList.add('settings-is-dragging');
  host.setPointerCapture(event.pointerId);
  settingsManagerPointer.frame = requestAnimationFrame(scrollSettingsCategoryDrag);
}

function settingsCategoryPointerTarget() {
  const pointer = settingsManagerPointer;
  if (!pointer) return null;
  const target = document.elementFromPoint(pointer.x, pointer.y);
  return target && pointer.host.contains(target) ? target : null;
}

function moveSettingsCategoryPointerDrag(event) {
  const pointer = settingsManagerPointer;
  if (!pointer || pointer.pointerId !== event.pointerId) return;
  event.preventDefault();
  pointer.x = event.clientX;
  pointer.y = event.clientY;
  updateSettingsCategoryPointerMarker();
}

function updateSettingsCategoryPointerMarker() {
  const target = settingsCategoryPointerTarget();
  if (target) handleSettingsCategoryDragOver({ target, clientY: settingsManagerPointer.y, preventDefault() {} });
  else clearSettingsDropMarkers();
}

function scrollSettingsCategoryDrag() {
  const pointer = settingsManagerPointer;
  if (!pointer) return;
  const rect = pointer.host.getBoundingClientRect();
  if (pointer.x >= rect.left && pointer.x <= rect.right) {
    const distance = pointer.y < rect.top + 36 ? pointer.y - rect.top - 36
      : pointer.y > rect.bottom - 36 ? pointer.y - rect.bottom + 36 : 0;
    pointer.host.scrollTop += Math.max(-10, Math.min(10, distance / 4));
  }
  updateSettingsCategoryPointerMarker();
  pointer.frame = requestAnimationFrame(scrollSettingsCategoryDrag);
}

function endSettingsCategoryPointerDrag(event) {
  if (settingsManagerPointer?.pointerId !== event.pointerId) return;
  event.preventDefault();
  settingsManagerPointer.x = event.clientX;
  settingsManagerPointer.y = event.clientY;
  const target = settingsCategoryPointerTarget();
  if (target?.closest('[data-group-id]')) {
    void handleSettingsCategoryDrop({ target, clientY: event.clientY, preventDefault() {} });
  } else finishSettingsManagerDrag();
}

function cancelSettingsCategoryPointerDrag(event) {
  if (settingsManagerPointer?.pointerId === event.pointerId) finishSettingsManagerDrag();
}

function handleSettingsManagerDragStart(event) {
  if (settingsManagerSaving || settingsManagerPointer) { event.preventDefault(); return; }
  const handle = event.target.closest('[data-settings-drag-account], [data-settings-drag-category]');
  if (!handle) return;

  let type = '';
  let id = 0;
  if (handle.dataset.settingsDragAccount) { type = 'account'; id = Number(handle.dataset.settingsDragAccount); }
  else if (handle.dataset.settingsDragCategory) { type = 'category'; id = Number(handle.dataset.settingsDragCategory); }
  if (!type || !Number.isInteger(id) || id <= 0 || (type === 'account' && !isDesktopInteractionWorkspace())) return;

  settingsManagerDrag = { type, id };
  event.dataTransfer.effectAllowed = 'move';
  event.dataTransfer.setData('text/plain', `${type}:${id}`);
  handle.closest('[data-settings-account-row], [data-group-id], [data-settings-category-row]')?.classList.add('settings-is-dragging');
}

function handleSettingsAccountDragOver(event) {
  if (settingsManagerDrag?.type !== 'account') return;
  event.preventDefault();
  clearSettingsDropMarkers();
  const row = event.target.closest('[data-settings-account-row]');
  if (!row) return;
  row.classList.add(settingsAfterMidpoint(event, row) ? 'settings-drop-after' : 'settings-drop-before');
}

async function handleSettingsAccountDrop(event) {
  if (settingsManagerDrag?.type !== 'account' || settingsManagerSaving || typeof state !== 'object') return;
  event.preventDefault();

  const sourceId = settingsManagerDrag.id;
  const row = event.target.closest('[data-settings-account-row]');
  const targetId = Number(row?.dataset.settingsAccountRow || 0);
  const after = row ? settingsAfterMidpoint(event, row) : true;
  const previous = [...(state.accounts || [])];
  const ids = previous.map(item => Number(item.id));
  const nextIds = settingsMoveId(ids, sourceId, targetId, after);
  finishSettingsManagerDrag();
  if (!nextIds || nextIds.every((id, index) => id === ids[index])) return;

  await applySettingsAccountOrder(previous, nextIds);
}

function handleSettingsCategoryDragOver(event) {
  if (!settingsManagerDrag || settingsManagerDrag.type !== 'category') return;
  event.preventDefault();
  clearSettingsDropMarkers();

  const row = event.target.closest('[data-settings-category-row]');
  if (row) {
    row.classList.add(settingsAfterMidpoint(event, row) ? 'settings-drop-after' : 'settings-drop-before');
    row.closest('[data-group-id]')?.classList.add('settings-drop-group');
    return;
  }
  event.target.closest('[data-group-id]')?.classList.add('settings-drop-group');
}

async function handleSettingsCategoryDrop(event) {
  if (!settingsManagerDrag || settingsManagerSaving || typeof state !== 'object') return;
  event.preventDefault();

  const drag = { ...settingsManagerDrag };
  const kind = state.settingsKind === 'income' ? 'income' : 'expense';

  if (drag.type !== 'category') {
    finishSettingsManagerDrag();
    return;
  }

  const targetGroup = event.target.closest('[data-group-id]');
  const targetGroupId = Number(targetGroup?.dataset.groupId || 0);
  const targetRow = event.target.closest('[data-settings-category-row]');
  const targetId = Number(targetRow?.dataset.settingsCategoryRow || 0);
  const after = targetRow ? settingsAfterMidpoint(event, targetRow) : true;
  const previous = [...(state.categories || [])];
  const payload = settingsCategoryPayload(previous, kind, drag.id, targetGroupId, targetId, after);
  finishSettingsManagerDrag();
  if (!payload) return;

  const byId = new Map(previous.filter(item => item.kind === kind).map(item => [Number(item.id), item]));
  const ordered = [];
  for (const group of payload) {
    group.categoryIds.forEach((id, index) => {
      const item = byId.get(Number(id));
      if (item) ordered.push({ ...item, group_id: Number(group.groupId), group_name: state.groups.find(entry => Number(entry.id) === Number(group.groupId))?.name || '', sort_order: index + 1 });
    });
  }
  let cursor = 0;
  state.categories = previous.map(item => item.kind === kind ? ordered[cursor++] : item);
  renderSettingsCategorySurfaces();

  await persistSettingsOrder('/api/categories/reorder', { kind, groups: payload }, '科目順序已更新。', () => {
    state.categories = restoreSettingsOrderState(state.categories, previous, ['group_id', 'sort_order']);
    state.categories = state.categories.map(item => ({ ...item, group_name: state.groups.find(group => Number(group.id) === Number(item.group_id))?.name || item.group_name }));
    renderSettingsCategorySurfaces();
  });
}

async function persistSettingsOrder(path, body, successMessage, rollback) {
  settingsManagerSaving = true;
  document.querySelector('#categoryManager')?.setAttribute('aria-busy', 'true');
  setDialogMessage(els.settingsMessage, '');
  try {
    await api(path, { method: 'PUT', headers: jsonHeaders(), body: JSON.stringify(body) });
    setDialogMessage(els.settingsMessage, successMessage);
    return true;
  } catch (error) {
    rollback?.();
    setDialogMessage(els.settingsMessage, error.message || '排序儲存失敗。', true);
    return false;
  } finally {
    settingsManagerSaving = false;
    document.querySelector('#categoryManager')?.removeAttribute('aria-busy');
  }
}

function settingsCategoryPayload(items, kind, sourceId, targetGroupId, targetId, after) {
  if (!Number.isInteger(targetGroupId) || targetGroupId <= 0 || targetId === sourceId) return null;
  const groups = (state.groups || []).filter(group => group.kind === kind);
  if (!groups.some(group => Number(group.id) === targetGroupId) || groups.some(group => Number(group.id) <= 0)
      || items.some(category => category.kind === kind && Number(category.id) <= 0)) return null;

  const payload = groups.map(group => ({
    groupId: Number(group.id),
    categoryIds: items
      .filter(category => category.kind === kind && Number(category.group_id) === Number(group.id))
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

function settingsMoveId(ids, sourceId, targetId, after) {
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

function settingsAfterMidpoint(event, element) {
  const rect = element.getBoundingClientRect();
  return event.clientY > rect.top + rect.height / 2;
}

function clearSettingsDropMarkers() {
  document.querySelectorAll('.settings-drop-before, .settings-drop-after, .settings-drop-group').forEach(element =>
    element.classList.remove('settings-drop-before', 'settings-drop-after', 'settings-drop-group')
  );
}

function finishSettingsManagerDrag() {
  const pointer = settingsManagerPointer;
  settingsManagerPointer = null;
  if (pointer) {
    cancelAnimationFrame(pointer.frame);
    if (pointer.host.hasPointerCapture(pointer.pointerId)) pointer.host.releasePointerCapture(pointer.pointerId);
  }
  document.querySelectorAll('.settings-is-dragging').forEach(element => element.classList.remove('settings-is-dragging'));
  clearSettingsDropMarkers();
  settingsManagerDrag = null;
}

function settingsManagerEscape(value) {
  return String(value ?? '')
    .replaceAll('&', '&amp;')
    .replaceAll('<', '&lt;')
    .replaceAll('>', '&gt;')
    .replaceAll('"', '&quot;')
    .replaceAll("'", '&#039;');
}

const CY_LEDGER_BALANCE_HOVER = '(hover: hover) and (pointer: fine)';
let cyLedgerBalancePopover = null;
let cyLedgerBalanceAnchor = null;
let cyLedgerBalancePinned = false;
let cyLedgerBalanceHideTimer = null;

window.cyShowMigrationComplete = showMigrationComplete;
window.cyCloseLedgerBalancePopover = closeLedgerBalancePopover;

function setupLedgerBalancePopover() {
  const body = document.body;
  if (!body || body.dataset.ledgerBalancePopoverBound === '1') return;
  body.dataset.ledgerBalancePopoverBound = '1';

  document.addEventListener('pointerover', event => {
    if (!window.matchMedia(CY_LEDGER_BALANCE_HOVER).matches) return;
    const cell = event.target.closest('[data-balance-popover-id]');
    if (!cell || cell.contains(event.relatedTarget)) return;
    if (cyLedgerBalancePinned && cyLedgerBalanceAnchor !== cell) return;
    clearLedgerBalanceHide();
    showLedgerBalancePopover(cell, false);
  });

  document.addEventListener('pointerout', event => {
    if (!window.matchMedia(CY_LEDGER_BALANCE_HOVER).matches) return;
    const cell = event.target.closest('[data-balance-popover-id]');
    if (!cell || cell.contains(event.relatedTarget)) return;
    if (cyLedgerBalancePopover?.contains(event.relatedTarget)) return;
    scheduleLedgerBalanceHide();
  });

  document.addEventListener('click', event => {
    const cell = event.target.closest('[data-balance-popover-id]');
    if (cell) {
      event.preventDefault();
      if (cyLedgerBalancePinned && cyLedgerBalanceAnchor === cell) {
        closeLedgerBalancePopover();
        return;
      }
      showLedgerBalancePopover(cell, true);
      return;
    }
    if (cyLedgerBalancePinned && !cyLedgerBalancePopover?.contains(event.target)) {
      closeLedgerBalancePopover();
    }
  });

  document.addEventListener('focusin', event => {
    const cell = event.target.closest('[data-balance-popover-id]');
    if (cell && !cyLedgerBalancePinned) showLedgerBalancePopover(cell, false);
  });

  document.addEventListener('focusout', event => {
    const cell = event.target.closest('[data-balance-popover-id]');
    if (!cell || cyLedgerBalancePinned) return;
    if (cyLedgerBalancePopover?.contains(event.relatedTarget)) return;
    scheduleLedgerBalanceHide();
  });

  document.addEventListener('keydown', event => {
    const cell = event.target.closest('[data-balance-popover-id]');
    if (cell && (event.key === 'Enter' || event.key === ' ')) {
      event.preventDefault();
      showLedgerBalancePopover(cell, true);
    } else if (event.key === 'Escape' && !cyLedgerBalancePopover?.hidden) {
      closeLedgerBalancePopover();
      cyLedgerBalanceAnchor?.focus?.();
    }
  });

  window.addEventListener('resize', closeLedgerBalancePopover);
  window.addEventListener('scroll', closeLedgerBalancePopover, true);
}

if (document.readyState === 'loading') {
  document.addEventListener('DOMContentLoaded', setupLedgerBalancePopover, { once: true });
} else {
  setupLedgerBalancePopover();
}

function ensureMigrationCompleteDialog() {
  let dialog = document.querySelector('#migrationCompleteDialog');
  if (dialog) return dialog;
  dialog = document.createElement('dialog');
  dialog.id = 'migrationCompleteDialog';
  dialog.className = 'modal small-modal migration-complete-dialog';
  dialog.innerHTML =
    '<div class="modal-header">' +
      '<div><span class="migration-complete-success-mark" aria-hidden="true">✓</span><h2>帳本移轉完成</h2><p>桌面帳本已成功複製到 Web。</p></div>' +
      '<button class="icon-button" type="button" data-migration-complete-close aria-label="關閉">×</button>' +
    '</div>' +
    '<div class="migration-complete-grid">' +
      '<div><span>交易</span><strong data-migration-result="transactions">—</strong></div>' +
      '<div><span>期初餘額</span><strong data-migration-result="opening">—</strong></div>' +
      '<div><span>帳戶</span><strong data-migration-result="accounts">—</strong></div>' +
      '<div><span>科目</span><strong data-migration-result="categories">—</strong></div>' +
      '<div><span>重複交易</span><strong data-migration-result="duplicates">—</strong></div>' +
      '<div><span>鎖帳至</span><strong data-migration-result="locked">—</strong></div>' +
    '</div>' +
    '<p class="migration-complete-note">原本電腦版的 SQLite 帳本不會被刪除或修改。</p>' +
    '<div class="modal-actions"><button class="primary" type="button" data-migration-complete-close>完成</button></div>';
  document.body.appendChild(dialog);
  dialog.addEventListener('click', event => {
    if (event.target.closest('[data-migration-complete-close]')) dialog.close();
  });
  return dialog;
}

function showMigrationComplete(result = {}) {
  const dialog = ensureMigrationCompleteDialog();
  if (!dialog) return;
  const set = (key, value) => {
    const node = dialog.querySelector('[data-migration-result="' + key + '"]');
    if (node) node.textContent = value;
  };
  const number = value => Number(value || 0).toLocaleString('zh-TW');
  set('transactions', '新增 ' + number(result.insertedTransactions) + ' 筆');
  set('opening', '新增 ' + number(result.insertedOpeningBalances) + ' 筆');
  set('accounts', '新增 ' + number(result.insertedAccounts) + ' 個');
  set('categories', '新增 ' + number(result.insertedCategories) + ' 個');
  set('duplicates', '略過 ' + number(result.skippedDuplicateTransactions) + ' 筆');
  set('locked', result.lockedThrough ? String(result.lockedThrough).replace('-', '/') : '未設定');
  if (dialog.open) dialog.close();
  dialog.showModal();
}

function ensureLedgerBalancePopover() {
  if (cyLedgerBalancePopover?.isConnected) return cyLedgerBalancePopover;
  const popover = document.createElement('div');
  popover.id = 'ledgerBalancePopover';
  popover.className = 'ledger-balance-popover';
  popover.setAttribute('role', 'dialog');
  popover.setAttribute('aria-label', '帳戶餘額明細');
  popover.hidden = true;
  document.body.appendChild(popover);
  popover.addEventListener('pointerenter', clearLedgerBalanceHide);
  popover.addEventListener('pointerleave', scheduleLedgerBalanceHide);
  cyLedgerBalancePopover = popover;
  return popover;
}

function showLedgerBalancePopover(cell, pinned) {
  const id = Number(cell?.dataset.balancePopoverId || 0);
  const detail = window.cyLedgerBalanceBreakdowns?.get(id);
  if (!cell || !detail) return;
  const popover = ensureLedgerBalancePopover();
  clearLedgerBalanceHide();

  if (cyLedgerBalanceAnchor && cyLedgerBalanceAnchor !== cell) {
    cyLedgerBalanceAnchor.setAttribute('aria-expanded', 'false');
  }
  cyLedgerBalanceAnchor = cell;
  cyLedgerBalancePinned = Boolean(pinned);
  cell.setAttribute('aria-expanded', 'true');

  popover.replaceChildren();
  const header = document.createElement('div');
  header.className = 'ledger-balance-popover-header';
  header.textContent = detail.accountOnly ? '此筆後帳戶餘額' : '此筆後各帳戶餘額';
  popover.appendChild(header);

  const list = document.createElement('div');
  list.className = 'ledger-balance-list';
  for (const item of detail.accounts || []) {
    const row = document.createElement('div');
    row.className = 'ledger-balance-row' + (item.name === detail.activeAccount ? ' active' : '');
    const name = document.createElement('span');
    const amount = document.createElement('strong');
    name.textContent = item.name || '未命名帳戶';
    amount.textContent = formatLedgerBalanceMoney(item.value);
    row.append(name, amount);
    list.appendChild(row);
  }
  popover.appendChild(list);

  if (!detail.accountOnly) {
    const total = document.createElement('div');
    total.className = 'ledger-balance-total';
    const label = document.createElement('span');
    const amount = document.createElement('strong');
    label.textContent = '總餘額';
    amount.textContent = formatLedgerBalanceMoney(detail.total);
    total.append(label, amount);
    popover.appendChild(total);
  }

  popover.hidden = false;
  positionLedgerBalancePopover(cell, popover);
}

function positionLedgerBalancePopover(cell, popover) {
  const anchor = cell.getBoundingClientRect();
  const box = popover.getBoundingClientRect();
  const margin = 12;
  let left = anchor.right - box.width;
  left = Math.max(margin, Math.min(left, window.innerWidth - box.width - margin));
  let top = anchor.bottom + 8;
  if (top + box.height > window.innerHeight - margin) top = anchor.top - box.height - 8;
  top = Math.max(margin, top);
  popover.style.left = Math.round(left) + 'px';
  popover.style.top = Math.round(top) + 'px';
}

function scheduleLedgerBalanceHide() {
  clearLedgerBalanceHide();
  if (cyLedgerBalancePinned) return;
  cyLedgerBalanceHideTimer = window.setTimeout(closeLedgerBalancePopover, 140);
}

function clearLedgerBalanceHide() {
  if (cyLedgerBalanceHideTimer) window.clearTimeout(cyLedgerBalanceHideTimer);
  cyLedgerBalanceHideTimer = null;
}

function closeLedgerBalancePopover() {
  clearLedgerBalanceHide();
  if (cyLedgerBalanceAnchor) cyLedgerBalanceAnchor.setAttribute('aria-expanded', 'false');
  cyLedgerBalanceAnchor = null;
  cyLedgerBalancePinned = false;
  if (cyLedgerBalancePopover) cyLedgerBalancePopover.hidden = true;
}

function formatLedgerBalanceMoney(value) {
  if (typeof money === 'function') return money(Number(value) || 0);
  return (Number(value) || 0).toLocaleString('zh-TW');
}

const CY_TRANSACTION_SWIPE_MOBILE = '(max-width: 767px)';
const CY_TRANSACTION_SWIPE_EDGE_GUARD = 24;
const CY_TRANSACTION_SWIPE_ACTION_WIDTH = 72;
const CY_TRANSACTION_SWIPE_OPEN_THRESHOLD = 34;

let cyTransactionSwipeOpenRow = null;
let cyTransactionSwipeGesture = null;
let cyTransactionSwipeSuppressClickUntil = 0;

if (document.readyState === 'loading') {
  document.addEventListener('DOMContentLoaded', setupTransactionSwipeSwipeActions, { once: true });
} else {
  window.setTimeout(setupTransactionSwipeSwipeActions, 0);
}
window.addEventListener('load', setupTransactionSwipeSwipeActions, { once: true });

function setupTransactionSwipeSwipeActions() {
  const rows = document.querySelector('#transactionRows');
  if (!rows || rows.dataset.transactionSwipeSwipeBound === '1') return;
  rows.dataset.transactionSwipeSwipeBound = '1';

  rows.addEventListener('pointerdown', event => {
    if (!window.matchMedia(CY_TRANSACTION_SWIPE_MOBILE).matches) return;
    if (event.pointerType === 'mouse' && event.button !== 0) return;
    if (event.target.closest('button, input, select, textarea, a')) return;

    const row = event.target.closest('tr.ledger-row:not(.inline-editing)');
    if (!row) return;

    if (event.clientX <= CY_TRANSACTION_SWIPE_EDGE_GUARD) {
      closeTransactionSwipeSwipeRow();
      return;
    }

    if (cyTransactionSwipeOpenRow && cyTransactionSwipeOpenRow !== row) {
      closeTransactionSwipeSwipeRow();
    }

    cyTransactionSwipeGesture = {
      pointerId: event.pointerId,
      row,
      startX: event.clientX,
      startY: event.clientY,
      startOffset: readTransactionSwipeOpenOffset(row),
      horizontal: false,
      cancelled: false,
      moved: false
    };

    row.classList.remove('cy-swipe-animate');
    row.setPointerCapture?.(event.pointerId);
  });

  rows.addEventListener('pointermove', event => {
    const gesture = cyTransactionSwipeGesture;
    if (!gesture || gesture.pointerId !== event.pointerId || gesture.cancelled) return;

    const dx = event.clientX - gesture.startX;
    const dy = event.clientY - gesture.startY;
    const absX = Math.abs(dx);
    const absY = Math.abs(dy);

    if (!gesture.horizontal) {
      if (Math.max(absX, absY) < 7) return;
      if (absY > absX) {
        gesture.cancelled = true;
        return;
      }
      gesture.horizontal = true;
    }

    const row = gesture.row;
    let next = clampTransactionSwipe(
      gesture.startOffset + dx,
      -CY_TRANSACTION_SWIPE_ACTION_WIDTH,
      CY_TRANSACTION_SWIPE_ACTION_WIDTH
    );

    const edit = row.querySelector('[data-edit-id]');
    const remove = row.querySelector('[data-delete-id]');
    if (next > 0 && (!edit || edit.disabled)) next = 0;
    if (next < 0 && (!remove || remove.disabled)) next = 0;

    gesture.moved = gesture.moved || Math.abs(next - gesture.startOffset) > 7;
    setTransactionSwipeSwipeOffset(row, next);
    row.dataset.swipeDirection = next > 1 ? 'edit' : next < -1 ? 'delete' : '';
    event.preventDefault();
  });

  const finish = event => {
    const gesture = cyTransactionSwipeGesture;
    if (!gesture || gesture.pointerId !== event.pointerId) return;
    cyTransactionSwipeGesture = null;

    const row = gesture.row;
    row.releasePointerCapture?.(event.pointerId);

    if (gesture.cancelled || !gesture.horizontal) {
      row.classList.add('cy-swipe-animate');
      return;
    }

    const current = readTransactionSwipeOffset(row);
    if (gesture.moved) cyTransactionSwipeSuppressClickUntil = Date.now() + 260;

    if (current >= CY_TRANSACTION_SWIPE_OPEN_THRESHOLD) {
      openTransactionSwipeSwipeRow(row, 'edit');
    } else if (current <= -CY_TRANSACTION_SWIPE_OPEN_THRESHOLD) {
      openTransactionSwipeSwipeRow(row, 'delete');
    } else {
      closeTransactionSwipeSwipeRow(row);
    }
  };

  rows.addEventListener('pointerup', finish);
  rows.addEventListener('pointercancel', finish);

  rows.addEventListener('click', event => {
    const action = event.target.closest('[data-edit-id], [data-delete-id]');
    if (action) {
      const row = action.closest('tr.ledger-row');
      window.setTimeout(() => closeTransactionSwipeSwipeRow(row), 0);
      return;
    }

    if (Date.now() < cyTransactionSwipeSuppressClickUntil) {
      event.preventDefault();
      event.stopPropagation();
      return;
    }

    const row = event.target.closest('tr.ledger-row:not(.inline-editing)');
    if (row && row === cyTransactionSwipeOpenRow) {
      event.preventDefault();
      event.stopPropagation();
      closeTransactionSwipeSwipeRow(row);
    }
  }, true);

  document.addEventListener('pointerdown', event => {
    if (!cyTransactionSwipeOpenRow) return;
    if (cyTransactionSwipeOpenRow.contains(event.target)) return;
    closeTransactionSwipeSwipeRow();
  }, true);

  window.addEventListener('scroll', () => closeTransactionSwipeSwipeRow(), { passive: true });
}

function openTransactionSwipeSwipeRow(row, mode) {
  if (!row?.isConnected) return;
  if (cyTransactionSwipeOpenRow && cyTransactionSwipeOpenRow !== row) {
    closeTransactionSwipeSwipeRow(cyTransactionSwipeOpenRow);
  }

  const action = mode === 'edit'
    ? row.querySelector('[data-edit-id]')
    : row.querySelector('[data-delete-id]');
  if (!action || action.disabled) {
    closeTransactionSwipeSwipeRow(row);
    return;
  }

  row.classList.add('cy-swipe-animate');
  row.dataset.swipeOpen = mode;
  row.dataset.swipeDirection = mode;
  setTransactionSwipeSwipeOffset(
    row,
    mode === 'edit' ? CY_TRANSACTION_SWIPE_ACTION_WIDTH : -CY_TRANSACTION_SWIPE_ACTION_WIDTH
  );
  cyTransactionSwipeOpenRow = row;
}

function closeTransactionSwipeSwipeRow(row = cyTransactionSwipeOpenRow) {
  if (!row) return;
  row.classList.add('cy-swipe-animate');
  row.dataset.swipeOpen = '';
  row.dataset.swipeDirection = '';
  setTransactionSwipeSwipeOffset(row, 0);
  if (cyTransactionSwipeOpenRow === row) cyTransactionSwipeOpenRow = null;
}

function readTransactionSwipeOpenOffset(row) {
  if (row?.dataset.swipeOpen === 'edit') return CY_TRANSACTION_SWIPE_ACTION_WIDTH;
  if (row?.dataset.swipeOpen === 'delete') return -CY_TRANSACTION_SWIPE_ACTION_WIDTH;
  return 0;
}

function readTransactionSwipeOffset(row) {
  const value = Number(row?.style.getPropertyValue('--cy-swipe-x').replace('px', ''));
  return Number.isFinite(value) ? value : 0;
}

function setTransactionSwipeSwipeOffset(row, value) {
  row?.style.setProperty('--cy-swipe-x', value + 'px');
}

function clampTransactionSwipe(value, min, max) {
  return Math.max(min, Math.min(max, value));
}

const CY_TOUCH_WORKSPACE_MOBILE = '(max-width: 767px)';
let cyTouchWorkspaceEdit = null;
let cyTouchWorkspaceSaveHideTimer = null;
let cyTouchWorkspaceSaveClearTimer = null;
let cyTouchWorkspaceSetupDone = false;
let cyMobileAccountDragId = 0;

window.cyAfterSaveMessage = handleTouchWorkspaceSaveMessage;
window.cyOpenMobileUtility = openMobileUtility;
window.cyOpenMobileLedgerOpening = () => openMobileUtility('opening');
window.cyOpenMobileLedgerLock = () => openMobileUtility('lock');
window.cyOpenMobileSettingsPane = tab => openMobileUtility(tab);

let cyMobileOpeningMount = null;

if (document.readyState === 'loading') {
  document.addEventListener('DOMContentLoaded', setupTouchWorkspace, { once: true });
} else {
  window.setTimeout(setupTouchWorkspace, 0);
}
window.addEventListener('load', setupTouchWorkspace, { once: true });

function setupTouchWorkspace() {
  if (cyTouchWorkspaceSetupDone) return;
  if (!document.querySelector('#transactionRows') || !document.querySelector('#transactionForm') || !document.querySelector('#mobileMainNav')) {
    window.setTimeout(setupTouchWorkspace, 50);
    return;
  }
  cyTouchWorkspaceSetupDone = true;
  setupTouchWorkspaceToolbar();
  setupMobileCanvasContinuation();
  setupTouchWorkspaceSearch();
  setupTouchWorkspaceSaveMessage();
  setupTouchWorkspaceEntrySecondaryAction();
  setupTouchWorkspaceMobileEdit();
}

function setupTouchWorkspaceToolbar(attempt = 0) {
  if (!window.matchMedia(CY_TOUCH_WORKSPACE_MOBILE).matches) return;
  const monthTools = document.querySelector('.ledger-month-tools');
  const prev = document.querySelector('#ledgerPrevMonth');
  const next = document.querySelector('#ledgerNextMonth');
  const more = document.querySelector('#mobileLedgerMoreButton');
  const slot = document.querySelector('#ledgerMonthSlot');
  const picker = document.querySelector('.ledger-title .month-picker');
  if (!monthTools || !prev || !next || !more || !slot || !picker) {
    if (attempt < 60) window.setTimeout(() => setupTouchWorkspaceToolbar(attempt + 1), 50);
    return;
  }

  let balance = document.querySelector('#mobileLedgerBalanceButton');
  if (!balance) {
    balance = document.createElement('button');
    balance.id = 'mobileLedgerBalanceButton';
    balance.className = 'secondary compact cy-mobile-balance-button';
    balance.type = 'button';
    balance.textContent = '餘額';
    balance.addEventListener('click', () => {
      openMobileUtility('opening');
    });
  }

  // Keep the month picker inside its original slot. Moving the label itself out of
  // the slot leaves an extra grid child and breaks the five-column mobile toolbar.
  if (picker.parentElement !== slot) slot.append(picker);
  monthTools.append(balance, prev, slot, next, more);
  setupTouchWorkspaceMonthDisplay(slot);
  monthTools.classList.add('cy-toolbar-ready');

  const displayMonth = document.querySelector('#ledgerDisplayMonth');
  if (displayMonth) displayMonth.hidden = true;

  const sheet = document.querySelector('#mobileLedgerToolsSheet');
  sheet?.querySelector('[data-mobile-ledger-action="opening"]')?.remove();
}

async function openMobileUtility(type) {
  if (!window.matchMedia(CY_TOUCH_WORKSPACE_MOBILE).matches) return;
  if (!['opening', 'accounts', 'categories', 'lock'].includes(type)) return;

  const dialog = els.settingsDialog || document.querySelector('#settingsDialog');
  if (!dialog) return;

  restoreMobileOpeningMount();
  if (typeof setDialogMessage === 'function' && els.settingsMessage) setDialogMessage(els.settingsMessage, '');
  if (typeof renderSettings === 'function') renderSettings();

  dialog.classList.remove('cy-mobile-settings-focus');
  dialog.classList.add('mobile-utility-dialog', 'mobile-settings-dialog');
  dialog.dataset.mobileUtility = type;

  const title = dialog.querySelector('.modal-header h2');
  const subtitle = dialog.querySelector('.modal-header p');
  if (title && !title.dataset.mobileUtilityOriginal) title.dataset.mobileUtilityOriginal = title.textContent || '設定';
  if (subtitle && !subtitle.dataset.mobileUtilityOriginal) subtitle.dataset.mobileUtilityOriginal = subtitle.textContent || '';

  if (!dialog.dataset.mobileUtilityBound) {
    dialog.dataset.mobileUtilityBound = '1';
    dialog.addEventListener('close', () => {
      restoreMobileOpeningMount();
      dialog.classList.remove('mobile-utility-dialog', 'mobile-settings-dialog', 'mobile-opening-dialog');
      delete dialog.dataset.mobileUtility;
      const heading = dialog.querySelector('.modal-header h2');
      const description = dialog.querySelector('.modal-header p');
      if (heading?.dataset.mobileUtilityOriginal) heading.textContent = heading.dataset.mobileUtilityOriginal;
      if (description?.dataset.mobileUtilityOriginal !== undefined) description.textContent = description.dataset.mobileUtilityOriginal;
    });
  }

  if (type === 'opening') {
    const month = String(els.monthFilter?.value || '');
    if (!/^\d{4}-\d{2}$/.test(month) || !els.openingRows || !els.openingMessage || !els.saveOpeningButton) return;
    if (els.openingMonth) els.openingMonth.value = month;

    dialog.classList.add('mobile-opening-dialog');
    if (title) title.textContent = '期初餘額';
    if (subtitle) subtitle.textContent = formatMobileMonth(month);

    const content = dialog.querySelector('.settings-content');
    if (!content) return;

    const pane = document.createElement('section');
    pane.className = 'settings-pane active mobile-utility-opening-pane';
    const actions = document.createElement('div');
    actions.className = 'mobile-utility-actions';

    const close = document.createElement('button');
    close.type = 'button';
    close.className = 'secondary';
    close.textContent = '關閉';
    close.addEventListener('click', () => dialog.close());
    actions.append(close);

    cyMobileOpeningMount = {
      rows: mountMobileUtilityNode(els.openingRows, pane),
      message: mountMobileUtilityNode(els.openingMessage, pane),
      save: mountMobileUtilityNode(els.saveOpeningButton, actions),
      pane
    };
    pane.append(actions);
    content.append(pane);

    dialog.querySelectorAll('.settings-pane:not(.mobile-utility-opening-pane)').forEach(item => item.classList.remove('active'));
    if (!dialog.open) dialog.showModal();
    if (typeof loadOpeningBalances === 'function') await loadOpeningBalances();
    return;
  }

  dialog.classList.remove('mobile-opening-dialog');
  if (typeof setSettingsTab === 'function') setSettingsTab(type);
  if (type === 'accounts') {
    if (title) title.textContent = '帳戶設定';
    if (subtitle) subtitle.textContent = '管理帳戶與預設帳戶';
    renderMobileAccountManager();
  } else if (type === 'categories') {
    if (title) title.textContent = '科目設定';
    if (subtitle) subtitle.textContent = '管理收入／支出大分類與科目';
    renderSettingsCategoryManager();
  } else {
    if (title) title.textContent = '月份鎖帳';
    if (subtitle) subtitle.textContent = '設定鎖帳月份';
    setupMobileLockMonthControls(dialog);
  }

  if (!dialog.open) dialog.showModal();
}

function mountMobileUtilityNode(node, target) {
  if (!node?.parentNode || !target) return null;
  const marker = document.createComment('cyacc-mobile-utility');
  node.parentNode.insertBefore(marker, node);
  target.append(node);
  return { node, marker };
}

function restoreMobileOpeningMount() {
  if (!cyMobileOpeningMount) return;
  for (const mount of [cyMobileOpeningMount.rows, cyMobileOpeningMount.message, cyMobileOpeningMount.save]) {
    if (mount?.marker?.parentNode) mount.marker.parentNode.insertBefore(mount.node, mount.marker);
    mount?.marker?.remove();
  }
  cyMobileOpeningMount.pane?.remove();
  cyMobileOpeningMount = null;
}

function renderMobileAccountManager() {
  if (typeof state !== 'object') return;
  const host = document.querySelector('#accountRows');
  if (!host) return;
  const accounts = Array.isArray(state.accounts) ? state.accounts : [];

  const activeHtml = accounts.length
    ? accounts.map(account => {
        const id = Number(account.id);
        const isDefault = Number(account.is_default) === 1;
        return `<div class="mobile-account-card" data-mobile-account-row="${id}" draggable="true">
          <button type="button" class="mobile-account-drag" data-mobile-account-drag="${id}" aria-label="拖曳調整 ${settingsManagerEscape(account.name)} 順序">⋮⋮</button>
          <strong class="mobile-account-name">${settingsManagerEscape(account.name)}</strong>
          ${isDefault ? '<span class="mobile-manager-badge">預設</span>' : `<button type="button" class="mini-button" data-account-default="${id}">設為預設</button>`}
          <button type="button" class="mini-button settings-edit-button" data-account-rename="${id}" title="編輯帳戶名稱" aria-label="編輯帳戶名稱">${settingsActionIcon('edit')}</button>
          <button type="button" class="mini-button settings-archive-button" data-account-archive="${id}" title="封存帳戶" aria-label="封存帳戶">${settingsActionIcon('archive')}</button>
        </div>`;
      }).join('')
    : '<div class="empty mobile-manager-empty">尚無可用帳戶。</div>';

  host.innerHTML = `
    <div class="mobile-account-active-list">${activeHtml}</div>`;

  bindMobileAccountReorder(host);
}

function bindMobileAccountReorder(host) {
  if (!host || host.dataset.mobileAccountReorderBound === '1') return;
  host.dataset.mobileAccountReorderBound = '1';

  let pointerId = null;
  let targetId = 0;
  let after = false;

  const updatePointerTarget = (clientX, clientY) => {
    const row = document.elementFromPoint(clientX, clientY)?.closest?.('[data-mobile-account-row]');
    host.querySelectorAll('.mobile-account-card').forEach(card => card.classList.remove('drop-before', 'drop-after'));
    if (!row || !host.contains(row)) {
      targetId = 0;
      return;
    }
    targetId = Number(row.dataset.mobileAccountRow || 0);
    const rect = row.getBoundingClientRect();
    after = clientY >= rect.top + rect.height / 2;
    row.classList.add(after ? 'drop-after' : 'drop-before');
  };

  host.addEventListener('pointerdown', event => {
    const handle = event.target.closest('[data-mobile-account-drag]');
    if (!handle || settingsManagerSaving) return;
    const row = handle.closest('[data-mobile-account-row]');
    const id = Number(row?.dataset.mobileAccountRow || 0);
    if (!row || !Number.isInteger(id) || id <= 0) return;

    cyMobileAccountDragId = id;
    pointerId = event.pointerId;
    targetId = id;
    after = false;
    row.classList.add('is-dragging');
    handle.setPointerCapture?.(event.pointerId);
    event.preventDefault();
  });

  host.addEventListener('pointermove', event => {
    if (!cyMobileAccountDragId || pointerId !== event.pointerId || settingsManagerSaving) return;
    updatePointerTarget(event.clientX, event.clientY);
    event.preventDefault();
  });

  const finishPointerReorder = async event => {
    if (!cyMobileAccountDragId || pointerId !== event.pointerId || settingsManagerSaving) return;
    const sourceId = cyMobileAccountDragId;
    const dropTargetId = targetId;
    const dropAfter = after;
    pointerId = null;
    finishMobileAccountDrag(host);

    const previous = [...(state.accounts || [])];
    const ids = previous.map(account => Number(account.id));
    const nextIds = settingsMoveId(ids, sourceId, dropTargetId, dropAfter);
    if (!nextIds || nextIds.every((id, index) => id === ids[index])) return;

    await applySettingsAccountOrder(previous, nextIds);
  };

  host.addEventListener('pointerup', finishPointerReorder);
  host.addEventListener('pointercancel', event => {
    if (pointerId !== event.pointerId) return;
    pointerId = null;
    finishMobileAccountDrag(host);
  });

  // Mouse drag remains as a desktop-browser fallback when the phone view is emulated.
  host.addEventListener('dragstart', event => {
    if (event.pointerType === 'touch' || settingsManagerSaving) {
      event.preventDefault();
      return;
    }
    const row = event.target.closest('[data-mobile-account-row]');
    if (!row) return;
    const id = Number(row.dataset.mobileAccountRow || 0);
    if (!Number.isInteger(id) || id <= 0) return;
    cyMobileAccountDragId = id;
    row.classList.add('is-dragging');
    event.dataTransfer.effectAllowed = 'move';
    event.dataTransfer.setData('text/plain', String(id));
  });

  host.addEventListener('dragover', event => {
    if (!cyMobileAccountDragId || settingsManagerSaving) return;
    const row = event.target.closest('[data-mobile-account-row]');
    if (!row) return;
    event.preventDefault();
    event.dataTransfer.dropEffect = 'move';
    host.querySelectorAll('.mobile-account-card').forEach(card => card.classList.remove('drop-before', 'drop-after'));
    const rect = row.getBoundingClientRect();
    row.classList.add(event.clientY >= rect.top + rect.height / 2 ? 'drop-after' : 'drop-before');
  });

  host.addEventListener('drop', async event => {
    if (!cyMobileAccountDragId || settingsManagerSaving || typeof state !== 'object') return;
    const row = event.target.closest('[data-mobile-account-row]');
    if (!row) return;
    event.preventDefault();

    const sourceId = cyMobileAccountDragId;
    const dropTargetId = Number(row.dataset.mobileAccountRow || 0);
    const rect = row.getBoundingClientRect();
    const dropAfter = event.clientY >= rect.top + rect.height / 2;
    finishMobileAccountDrag(host);

    const previous = [...(state.accounts || [])];
    const ids = previous.map(account => Number(account.id));
    const nextIds = settingsMoveId(ids, sourceId, dropTargetId, dropAfter);
    if (!nextIds || nextIds.every((id, index) => id === ids[index])) return;

    await applySettingsAccountOrder(previous, nextIds);
  });

  host.addEventListener('dragend', () => finishMobileAccountDrag(host));
}

async function applySettingsAccountOrder(previous, nextIds) {
  if (settingsManagerSaving) return;
  const byId = new Map(previous.map(account => [Number(account.id), account]));
  if (nextIds.some(id => Number(id) <= 0) || nextIds.length !== byId.size || new Set(nextIds).size !== byId.size || nextIds.some(id => !byId.has(id))) return;
  const selectedAccount = document.querySelector('#accountName')?.value || '';
  const render = () => {
    renderSettingsAccountManager();
    if (typeof renderAccounts === 'function') renderAccounts(selectedAccount);
    if (typeof syncEntryUiAccountChoices === 'function') syncEntryUiAccountChoices();
  };
  state.accounts = nextIds.map((id, index) => ({ ...byId.get(id), sort_order: index + 1 }));
  render();
  return persistSettingsOrder('/api/accounts/reorder', { ids: nextIds }, '帳戶順序已更新。', () => {
    state.accounts = restoreSettingsOrderState(state.accounts, previous, ['sort_order']);
    render();
  });
}

function finishMobileAccountDrag(host) {
  cyMobileAccountDragId = 0;
  host?.querySelectorAll('.mobile-account-card').forEach(card =>
    card.classList.remove('is-dragging', 'drop-before', 'drop-after')
  );
}


function ensureMobileCategoryActions(pane, hasPersistedGroup) {
  let actions = pane.querySelector('#mobileCategoryActions');
  if (!actions) {
    actions = document.createElement('div');
    actions.id = 'mobileCategoryActions';
    actions.className = 'mobile-category-footer';
    actions.innerHTML = `
      <button type="button" class="primary" data-mobile-category-add>新增科目</button>
      <button type="button" class="secondary" data-mobile-group-add>新增分類</button>`;
    pane.append(actions);

  }
  const addCategory = actions.querySelector('[data-mobile-category-add]');
  if (addCategory) {
    addCategory.disabled = !hasPersistedGroup;
    addCategory.title = hasPersistedGroup ? '' : '請先新增分類';
  }
}

let settingsTempId = -1;

function nextSettingsTempId() {
  return settingsTempId--;
}

async function optimisticAddSettingsGroup(rawName) {
  const name = String(rawName || '').trim().replace(/\s+/g, ' ');
  const kind = state.settingsKind === 'income' ? 'income' : 'expense';
  if (!name) return;

  const tempId = nextSettingsTempId();
  const sortOrder = Math.max(-1, ...(state.groups || [])
    .filter(group => group.kind === kind)
    .map(group => Number(group.sort_order ?? -1))) + 1;
  state.groups = [...(state.groups || []), { id: tempId, kind, name, sort_order: sortOrder }];
  renderSettingsCategoryManager();
  setDialogMessage(els.settingsMessage, '');

  try {
    const result = await api('/api/category-groups', {
      method: 'POST',
      headers: jsonHeaders(),
      body: JSON.stringify({ kind, name })
    });
    const pending = (state.groups || []).find(group => Number(group.id) === tempId);
    if (pending) pending.id = Number(result.id);
    renderSettingsCategoryManager();
    setDialogMessage(els.settingsMessage, '大分類已新增。');
    return true;
  } catch (error) {
    state.groups = (state.groups || []).filter(group => Number(group.id) !== tempId);
    renderSettingsCategoryManager();
    setDialogMessage(els.settingsMessage, error?.message || '大分類新增失敗。', true);
    return false;
  }
}

async function optimisticAddSettingsCategory(rawName, groupId) {
  const name = String(rawName || '').trim().replace(/\s+/g, ' ');
  const kind = state.settingsKind === 'income' ? 'income' : 'expense';
  const group = (state.groups || []).find(item =>
    Number(item.id) === Number(groupId) && Number(item.id) > 0 && item.kind === kind
  );
  if (!name || !group) return;

  const tempId = nextSettingsTempId();
  const sortOrder = Math.max(-1, ...(state.categories || [])
    .filter(category => category.kind === kind && Number(category.group_id) === Number(groupId))
    .map(category => Number(category.sort_order ?? -1))) + 1;
  state.categories = [...(state.categories || []), {
    id: tempId,
    kind,
    name,
    group_id: Number(groupId),
    group_name: group.name,
    group_sort_order: Number(group.sort_order || 0),
    sort_order: sortOrder,
    is_favorite: 0
  }];
  renderSettingsCategoryManager();
  if (typeof renderCategories === 'function') renderCategories(els.categoryName?.value);
  setDialogMessage(els.settingsMessage, '');

  try {
    const result = await api('/api/categories', {
      method: 'POST',
      headers: jsonHeaders(),
      body: JSON.stringify({ kind, groupId: Number(groupId), name })
    });
    const pending = (state.categories || []).find(category => Number(category.id) === tempId);
    if (pending) pending.id = Number(result.id);
    renderSettingsCategoryManager();
    if (typeof renderCategories === 'function') renderCategories(els.categoryName?.value);
    setDialogMessage(els.settingsMessage, '科目已新增。');
    return true;
  } catch (error) {
    state.categories = (state.categories || []).filter(category => Number(category.id) !== tempId);
    renderSettingsCategoryManager();
    if (typeof renderCategories === 'function') renderCategories(els.categoryName?.value);
    setDialogMessage(els.settingsMessage, error?.message || '科目新增失敗。', true);
    return false;
  }
}

function setupMobileLockMonthControls(dialog) {
  if (!dialog || !els.lockedThrough) return;
  const nativeField = els.lockedThrough.closest('label');
  const form = nativeField?.closest('.lock-form');
  if (!nativeField || !form) return;

  nativeField.classList.add('mobile-lock-native-field');

  let controls = form.querySelector('#mobileLockMonthControls');
  if (!controls) {
    controls = document.createElement('div');
    controls.id = 'mobileLockMonthControls';
    controls.className = 'mobile-lock-month-controls';
    controls.innerHTML = `
      <span class="mobile-lock-month-label">鎖帳至</span>
      <div class="mobile-lock-month-selects">
        <label><span>年份</span><select id="mobileLockYear" aria-label="鎖帳年份"></select></label>
        <label><span>月份</span><select id="mobileLockMonth" aria-label="鎖帳月份"></select></label>
      </div>`;
    form.insertBefore(controls, nativeField);

    const year = controls.querySelector('#mobileLockYear');
    const month = controls.querySelector('#mobileLockMonth');
    year.innerHTML = Array.from({ length: 100 }, (_, index) => 2000 + index)
      .map(value => `<option value="${value}">${value} 年</option>`).join('');
    month.innerHTML = Array.from({ length: 12 }, (_, index) => index + 1)
      .map(value => `<option value="${String(value).padStart(2, '0')}">${value} 月</option>`).join('');

    const writeCanonical = () => {
      if (!year.value || !month.value) return;
      els.lockedThrough.value = year.value + '-' + month.value;
    };
    year.addEventListener('change', writeCanonical);
    month.addEventListener('change', writeCanonical);
  }

  syncMobileLockMonthControls();
}

function syncMobileLockMonthControls() {
  const controls = document.querySelector('#mobileLockMonthControls');
  if (!controls || !els.lockedThrough) return;

  let value = String(els.lockedThrough.value || '');
  if (!/^\d{4}-\d{2}$/.test(value)) value = String(els.monthFilter?.value || '');
  if (!/^\d{4}-\d{2}$/.test(value)) value = localDateString(new Date()).slice(0, 7);

  const [yearValue, monthValue] = value.split('-');
  const year = controls.querySelector('#mobileLockYear');
  const month = controls.querySelector('#mobileLockMonth');
  if (year && !year.querySelector(`option[value="${yearValue}"]`)) {
    year.insertAdjacentHTML('beforeend', `<option value="${yearValue}">${Number(yearValue)} 年</option>`);
  }
  if (year) year.value = yearValue;
  if (month) month.value = monthValue;
  els.lockedThrough.value = value;
}
window.cySyncMobileLockMonthControls = syncMobileLockMonthControls;

function setupMobileCanvasContinuation() {
  const shell = document.querySelector('.shell');
  const entry = document.querySelector('.entry-card');
  if (!shell || !entry || shell.dataset.mobileCanvasBound === '1') return;
  shell.dataset.mobileCanvasBound = '1';

  const observer = new MutationObserver(syncMobileCanvasContinuation);
  observer.observe(shell, { attributes: true, attributeFilter: ['data-mobile-page'] });
  observer.observe(entry, { attributes: true, attributeFilter: ['class'] });
  syncMobileCanvasContinuation();
}

window.cySyncMobileCanvasContinuation = syncMobileCanvasContinuation;

function syncMobileCanvasContinuation() {
  const shell = document.querySelector('.shell');
  const entry = document.querySelector('.entry-card');
  const mobile = window.matchMedia(CY_TOUCH_WORKSPACE_MOBILE).matches;
  const entryActive = mobile && shell?.dataset.mobilePage !== 'ledger';
  const income = entryActive && (entry?.classList.contains('entry-income') || state?.kind === 'income');
  const expense = entryActive && !income;

  document.documentElement.classList.toggle('mobile-entry-income-canvas', income);
  document.documentElement.classList.toggle('mobile-entry-expense-canvas', expense);
}

function previousMobileMonth(month) {
  const match = /^(\d{4})-(\d{2})$/.exec(String(month || ''));
  if (!match) return '';
  const date = new Date(Number(match[1]), Number(match[2]) - 2, 1);
  return date.getFullYear() + '-' + String(date.getMonth() + 1).padStart(2, '0');
}

function formatMobileMonth(month) {
  const match = /^(\d{4})-(\d{2})$/.exec(String(month || ''));
  return match ? Number(match[1]) + '年' + Number(match[2]) + '月' : String(month || '');
}

function setupTouchWorkspaceMonthDisplay(slot) {
  if (!slot || !els.monthFilter) return;

  let display = slot.querySelector('#mobileLedgerMonthDisplay');
  if (!display) {
    display = document.createElement('span');
    display.id = 'mobileLedgerMonthDisplay';
    display.className = 'cy-mobile-month-display';
    display.setAttribute('aria-hidden', 'true');
    slot.append(display);
  }

  els.monthFilter.setAttribute('aria-label', '選擇月份');
  if (els.monthFilter.dataset.touchMonthDisplayBound !== '1') {
    els.monthFilter.dataset.touchMonthDisplayBound = '1';
    els.monthFilter.addEventListener('input', syncTouchWorkspaceMonthDisplay);
    els.monthFilter.addEventListener('change', syncTouchWorkspaceMonthDisplay);
  }
  syncTouchWorkspaceMonthDisplay();
}

function syncTouchWorkspaceMonthDisplay() {
  const display = document.querySelector('#mobileLedgerMonthDisplay');
  const value = String(els.monthFilter?.value || '');
  if (!display) return;
  const match = /^(\d{4})-(\d{2})$/.exec(value);
  display.textContent = match ? `${Number(match[1])}年${Number(match[2])}月` : '選擇月份';
}

function setupTouchWorkspaceSearch() {
  const form = document.querySelector('#ledgerSearchForm');
  const input = document.querySelector('#ledgerSummarySearch');
  if (!form || !input) return;
  input.setAttribute('enterkeyhint', 'search');
  input.setAttribute('inputmode', 'search');
  form.addEventListener('submit', () => {
    if (!window.matchMedia(CY_TOUCH_WORKSPACE_MOBILE).matches) return;
    window.setTimeout(() => input.blur(), 0);
  });
}

function setupTouchWorkspaceSaveMessage() {
  const media = window.matchMedia(CY_TOUCH_WORKSPACE_MOBILE);
  const message = document.querySelector('#saveMessage');
  const saveButton = document.querySelector('#saveButton');
  if (!message || !saveButton) return;

  const originalParent = message.parentElement;
  const originalNext = message.nextSibling;

  const sync = () => {
    if (usesEntryTransactionEditor()) {
      if (message.previousElementSibling !== saveButton) saveButton.insertAdjacentElement('afterend', message);
      message.classList.add('cy-mobile-save-message');
    } else {
      message.classList.remove('cy-mobile-save-message', 'is-visible', 'is-fading');
      if (originalParent && message.parentElement !== originalParent) {
        if (originalNext && originalNext.parentNode === originalParent) originalParent.insertBefore(message, originalNext);
        else originalParent.append(message);
      }
    }
  };

  if (typeof media.addEventListener === 'function') media.addEventListener('change', sync);
  else media.addListener?.(sync);
  window.addEventListener('resize', sync, { passive: true });
  sync();
}

function setupTouchWorkspaceEntrySecondaryAction() {
  if (!usesEntryTransactionEditor()) return;
  const saveButton = document.querySelector('#saveButton');
  const message = document.querySelector('#saveMessage');
  if (!saveButton || !message) return;

  let button = document.querySelector('#mobileEntrySecondaryButton');
  if (!button) {
    button = document.createElement('button');
    button.id = 'mobileEntrySecondaryButton';
    button.className = 'secondary cy-entry-secondary-button';
    button.type = 'button';
    button.textContent = '清空';
    button.addEventListener('click', () => {
      if (cyTouchWorkspaceEdit) {
        cancelTouchWorkspaceMobileEditAndReturn();
        return;
      }
      clearTouchWorkspaceEntryForm();
    });
  }

  message.insertAdjacentElement('beforebegin', button);
  syncTouchWorkspaceEntrySecondaryAction();
}

function syncTouchWorkspaceEntrySecondaryAction() {
  const button = document.querySelector('#mobileEntrySecondaryButton');
  if (!button) return;
  button.textContent = cyTouchWorkspaceEdit ? '取消' : '清空';
  button.classList.toggle('is-cancel', Boolean(cyTouchWorkspaceEdit));
}

function clearTouchWorkspaceEntryForm() {
  showMessage('');
  const today = typeof localDateString === 'function' ? localDateString(new Date()) : new Date().toISOString().slice(0, 10);
  els.txDate.value = today;
  renderAccounts();
  renderCategories();
  els.summary.value = '';
  els.amount.value = '';
  updateEntryLockState();
  els.summary.blur();
  els.amount.blur();
}

function cancelTouchWorkspaceMobileEditAndReturn() {
  const context = cyTouchWorkspaceEdit?.returnContext;
  if (!context) return false;
  cancelTouchWorkspaceMobileEdit();
  switchTouchWorkspaceMobilePage('ledger');
  restoreTouchWorkspaceLedgerContext(context, false);
  return true;
}

function handleTouchWorkspaceSaveMessage(message, text, isError) {
  if (!message) return;
  window.clearTimeout(cyTouchWorkspaceSaveHideTimer);
  window.clearTimeout(cyTouchWorkspaceSaveClearTimer);
  message.classList.remove('is-fading');
  message.classList.toggle('is-visible', Boolean(text));

  if (!text || isError || !window.matchMedia(CY_TOUCH_WORKSPACE_MOBILE).matches) return;

  cyTouchWorkspaceSaveHideTimer = window.setTimeout(() => {
    message.classList.add('is-fading');
    cyTouchWorkspaceSaveClearTimer = window.setTimeout(() => {
      if (message.classList.contains('is-fading')) {
        message.textContent = '';
        message.classList.remove('is-visible', 'is-fading');
      }
    }, 420);
  }, 2500);
}

function setupTouchWorkspaceMobileEdit() {
  const rows = document.querySelector('#transactionRows');
  const form = document.querySelector('#transactionForm');
  const nav = document.querySelector('#mobileMainNav');
  if (!rows || !form || !nav) return;

  rows.addEventListener('click', event => {
    if (!usesEntryTransactionEditor()) return;
    const edit = event.target.closest('[data-edit-id]');
    if (!edit) return;
    const id = Number(edit.dataset.editId || 0);
    if (!Number.isInteger(id) || id <= 0 || edit.disabled) return;

    event.preventDefault();
    event.stopImmediatePropagation();
    beginTouchWorkspaceMobileEdit(id);
  }, true);

  form.addEventListener('submit', event => {
    if (!cyTouchWorkspaceEdit || !usesEntryTransactionEditor()) return;
    event.preventDefault();
    event.stopImmediatePropagation();
    saveTouchWorkspaceMobileEdit();
  }, true);

  nav.addEventListener('click', event => {
    if (!cyTouchWorkspaceEdit || !usesEntryTransactionEditor()) return;
    const button = event.target.closest('[data-mobile-page]');
    if (!button || button.dataset.mobilePage === 'entry') return;

    event.preventDefault();
    event.stopImmediatePropagation();
    cancelTouchWorkspaceMobileEditAndReturn();
  }, true);

  const phone = window.matchMedia(CY_TOUCH_WORKSPACE_MOBILE);
  phone.addEventListener?.('change', () => {
    if (!usesEntryTransactionEditor() && cyTouchWorkspaceEdit) cancelTouchWorkspaceMobileEdit({ restoreDraftOnly: true });
  });

  els.monthFilter?.addEventListener('change', () => {
    if (cyTouchWorkspaceEdit && usesEntryTransactionEditor()) cancelTouchWorkspaceMobileEdit({ restoreDraftOnly: true });
  });

  window.addEventListener('pagehide', () => {
    if (!cyTouchWorkspaceEdit) return;
    cancelTouchWorkspaceMobileEdit({ restoreDraftOnly: true });
  });
}

function beginTouchWorkspaceMobileEdit(id) {
  if (cyTouchWorkspaceEdit?.id === id) {
    if (isTabletWorkspace()) setTabletEntryExpanded(true);
    els.summary?.focus();
    return;
  }
  const tx = state.transactions.find(item => Number(item.id) === id);
  if (!tx || isLocked(String(tx.tx_date || '').slice(0, 7))) return;

  if (cyTouchWorkspaceEdit) cancelTouchWorkspaceMobileEdit({ restoreDraftOnly: true });
  const row = document.querySelector('#transactionRows tr[data-transaction-id="' + id + '"]');
  cyTouchWorkspaceEdit = {
    id,
    kind: tx.kind,
    originalMonth: String(tx.tx_date || '').slice(0, 7),
    draft: captureTouchWorkspaceEntryDraft(),
    returnContext: {
      month: els.monthFilter?.value || '',
      search: document.querySelector('#ledgerSummarySearch')?.value || '',
      scrollY: window.scrollY,
      ledgerScrollTop: document.querySelector('.ledger-card .table-wrap')?.scrollTop || 0,
      rowId: id,
      rowTop: row?.getBoundingClientRect().top ?? null
    }
  };

  setEntryKind(tx.kind);
  ensureTouchWorkspaceOption(els.accountName, tx.account_name);
  els.accountName.value = tx.account_name;
  ensureTouchWorkspaceOption(els.categoryName, tx.category_name);
  els.categoryName.value = tx.category_name;
  els.txDate.value = tx.tx_date;
  els.summary.value = tx.summary || '';
  els.amount.value = String(tx.amount || '');

  els.kindButtons.forEach(button => { button.disabled = true; });
  els.saveButton.textContent = '儲存修改';
  syncTouchWorkspaceEntrySecondaryAction();
  document.querySelector('.entry-card')?.classList.add('cy-mobile-editing');
  showMessage('');
  updateEntryLockState();

  switchTouchWorkspaceMobilePage('entry');
  window.scrollTo({ top: 0, behavior: 'auto' });
}

async function saveTouchWorkspaceMobileEdit() {
  const edit = cyTouchWorkspaceEdit;
  if (!edit) return;

  const month = String(els.txDate.value || '').slice(0, 7);
  if (isLocked(month)) {
    showMessage('此月份已鎖帳，無法修改資料。', true);
    return;
  }

  const amount = Number(els.amount.value);
  if (!Number.isInteger(amount) || amount < 1 || amount > 9_999_999) {
    showMessage('金額必須為 1～9,999,999。', true);
    els.amount.focus();
    return;
  }

  els.saveButton.disabled = true;
  const destinationMonth = month;
  try {
    const context = edit.returnContext;
    const editedId = edit.id;
    await persistTransactionUpdate(edit.id, {
      txDate: els.txDate.value, accountName: els.accountName.value,
      categoryName: els.categoryName.value, summary: els.summary.value, amount
    }, () => {
      cancelTouchWorkspaceMobileEdit({ restoreDraftOnly: true });
      const searchInput = document.querySelector('#ledgerSummarySearch');
      if (searchInput) searchInput.value = context.search || '';
      if (typeof cyLedgerSearch !== 'undefined') cyLedgerSearch = context.search || '';
      switchTouchWorkspaceMobilePage('ledger');
      restoreTouchWorkspaceLedgerContext(context, destinationMonth === context.month ? editedId : false);
    });
    showTouchWorkspaceLedgerNotice(destinationMonth === context.month ? '修改成功。' : '修改完成，資料已移至 ' + destinationMonth.replace('-', '/') + '。');

  } catch (error) {
    showMessage(error.message || '修改失敗。', true);
    showTouchWorkspaceLedgerNotice((error.message || '修改失敗。') + ' 已還原原資料。');
    updateEntryLockState();
  }
}

function cancelTouchWorkspaceMobileEdit(options = {}) {
  const edit = cyTouchWorkspaceEdit;
  if (!edit) return;
  cyTouchWorkspaceEdit = null;

  clearTouchWorkspaceTemporaryOptions();
  restoreTouchWorkspaceEntryDraft(edit.draft);
  els.kindButtons.forEach(button => { button.disabled = false; });
  els.saveButton.textContent = '儲存';
  syncTouchWorkspaceEntrySecondaryAction();
  document.querySelector('.entry-card')?.classList.remove('cy-mobile-editing');
  showMessage('');
  updateEntryLockState();

  if (!options.restoreDraftOnly) window.cyCloseLedgerBalancePopover?.();
}

function captureTouchWorkspaceEntryDraft() {
  return {
    kind: state.kind,
    txDate: els.txDate.value,
    accountName: els.accountName.value,
    categoryName: els.categoryName.value,
    summary: els.summary.value,
    amount: els.amount.value
  };
}

function restoreTouchWorkspaceEntryDraft(draft) {
  if (!draft) return;
  setEntryKind(draft.kind);
  ensureTouchWorkspaceOption(els.accountName, draft.accountName);
  els.accountName.value = draft.accountName || els.accountName.value;
  ensureTouchWorkspaceOption(els.categoryName, draft.categoryName);
  els.categoryName.value = draft.categoryName || els.categoryName.value;
  els.txDate.value = draft.txDate || els.txDate.value;
  els.summary.value = draft.summary || '';
  els.amount.value = draft.amount || '';
}

function ensureTouchWorkspaceOption(select, value) {
  const text = String(value || '').trim();
  if (!select || !text) return;
  if ([...select.options].some(option => option.value === text)) return;
  const option = document.createElement('option');
  option.value = text;
  option.textContent = text + '（歷史）';
  option.dataset.entryEditTemporary = '1';
  select.append(option);
}

function clearTouchWorkspaceTemporaryOptions() {
  for (const select of [els.accountName, els.categoryName]) {
    select?.querySelectorAll('[data-cy-edit-temporary="1"]').forEach(option => option.remove());
  }
}

function switchTouchWorkspaceMobilePage(page) {
  if (isTabletWorkspace()) {
    if (page === 'entry') setTabletEntryExpanded(true);
    return;
  }
  const shell = document.querySelector('.shell');
  const entry = shell?.querySelector('.entry-card');
  const ledger = shell?.querySelector('.ledger-card');
  const nav = document.querySelector('#mobileMainNav');
  if (!shell || !entry || !ledger || !nav) return;

  const current = page === 'ledger' ? 'ledger' : 'entry';
  entry.classList.toggle('cy-mobile-page-hidden', current !== 'entry');
  ledger.classList.toggle('cy-mobile-page-hidden', current !== 'ledger');
  shell.dataset.mobilePage = current;

  for (const button of nav.querySelectorAll('[data-mobile-page]')) {
    const active = button.dataset.mobilePage === current;
    button.classList.toggle('active', active);
    button.setAttribute('aria-selected', active ? 'true' : 'false');
  }
}

function restoreTouchWorkspaceLedgerContext(context, highlightId) {
  if (!context) return;

  if (context.month && els.monthFilter.value !== context.month) {
    els.monthFilter.value = context.month;
  }
  syncTouchWorkspaceMonthDisplay();
  const searchInput = document.querySelector('#ledgerSummarySearch');
  if (searchInput) searchInput.value = context.search || '';

  const restore = () => {
    if (isTabletWorkspace()) {
      const scroller = document.querySelector('.ledger-card .table-wrap');
      if (scroller) scroller.scrollTop = context.ledgerScrollTop || 0;
    }
    const row = context.rowId
      ? document.querySelector('#transactionRows tr[data-transaction-id="' + context.rowId + '"]')
      : null;

    if (isTabletWorkspace()) {
      // The ledger scroll container, rather than the page, owns the return position.
    } else if (row && context.rowTop !== null) {
      const delta = row.getBoundingClientRect().top - context.rowTop;
      window.scrollBy({ top: delta, behavior: 'auto' });
    } else {
      window.scrollTo({ top: context.scrollY || 0, behavior: 'auto' });
    }

    if (highlightId) {
      const highlight = document.querySelector('#transactionRows tr[data-transaction-id="' + highlightId + '"]');
      if (highlight) {
        highlight.classList.add('cy-edit-highlight');
        window.setTimeout(() => highlight.classList.remove('cy-edit-highlight'), 1800);
      }
    }
  };

  window.requestAnimationFrame(() => window.requestAnimationFrame(restore));
}

function showTouchWorkspaceLedgerNotice(text) {
  let notice = document.querySelector('#touchLedgerNotice');
  if (!notice) {
    notice = document.createElement('div');
    notice.id = 'touchLedgerNotice';
    notice.className = 'cy-ledger-notice';
    document.body.append(notice);
  }
  notice.textContent = text;
  notice.classList.add('show');
  window.setTimeout(() => notice.classList.remove('show'), 2600);
}

/* Tablet presentation reuses the same entry form, ledger and entry-edit owner. */
function isTabletWorkspace() {
  const width = window.innerWidth;
  return width >= 768 && (width < 1024 ||
    (width <= 1366 && window.matchMedia('(any-pointer: coarse)').matches));
}

function usesEntryTransactionEditor() {
  return window.matchMedia('(max-width: 767px)').matches || isTabletWorkspace();
}
window.cyUsesEntryTransactionEditor = usesEntryTransactionEditor;

function tabletWorkspaceOrientation() {
  const orientation = window.screen?.orientation?.type;
  if (orientation) return orientation.startsWith('landscape') ? 'landscape' : 'portrait';
  if (typeof window.orientation === 'number') return Math.abs(window.orientation) === 90 ? 'landscape' : 'portrait';
  return window.matchMedia('(orientation: landscape)').matches ? 'landscape' : 'portrait';
}

function setTabletEntryExpanded(expanded) {
  const rail = document.querySelector('.cy-entry-rail');
  if (!rail) return;
  rail.dataset.entryExpanded = expanded ? 'true' : 'false';
  const button = document.querySelector('#tabletEntryToggle');
  if (button) {
    button.setAttribute('aria-expanded', String(Boolean(expanded)));
    button.setAttribute('aria-label', expanded ? '收合記帳' : '展開記帳');
  }
}

function syncTabletPickerOwnership(orientation) {
  const tablet = isTabletWorkspace();

  if (tablet) {
    document.querySelectorAll('.desktopUi-date-picker').forEach(root => root.remove());
    document.querySelectorAll('input[type="date"]').forEach(input => {
      input.classList.remove('desktopUi-native-date-source');
      delete input.dataset.desktopUiDatePicker;
      input.removeAttribute('style');
    });
  } else if (isDesktopInteractionWorkspace() && typeof ensureDesktopUiDatePicker === 'function') {
    document.querySelectorAll('input[type="date"]').forEach(ensureDesktopUiDatePicker);
  }

  const customMonth = document.querySelector('#ledgerMonthPickerCustom');
  if (tablet && customMonth) {
    customMonth.remove();
  }

  const slot = document.querySelector('#ledgerMonthSlot');
  if (tablet && orientation === 'landscape') {
    if (slot) setupTouchWorkspaceMonthDisplay(slot);
    else window.setTimeout(() => {
      if (isTabletWorkspace() && tabletWorkspaceOrientation() === 'landscape') {
        const readySlot = document.querySelector('#ledgerMonthSlot');
        if (readySlot) setupTouchWorkspaceMonthDisplay(readySlot);
      }
    }, 60);
  } else if (tablet) {
    document.querySelector('#mobileLedgerMonthDisplay')?.remove();
  }
}

function setupTabletWorkspace() {
  const shell = document.querySelector('main.shell');
  const entry = document.querySelector('.entry-card');
  if (!shell || !entry || shell.dataset.tabletBound === '1') return;
  shell.dataset.tabletBound = '1';
  const controls = document.createElement('div');
  controls.className = 'tablet-entry-controls';
  controls.innerHTML = '<button id="tabletEntryToggle" type="button" class="tablet-entry-handle" aria-label="展開記帳" aria-expanded="false" aria-controls="transactionForm"><span aria-hidden="true"></span></button><label><input id="tabletEntryPinned" type="checkbox">保持展開</label>';
  entry.prepend(controls);
  controls.querySelector('#tabletEntryToggle').addEventListener('click', () => {
    if (suppressClick) { suppressClick = false; return; }
    if (controls.querySelector('#tabletEntryPinned').checked) return;
    const rail = document.querySelector('.cy-entry-rail');
    const expanded = rail?.dataset.entryExpanded !== 'true';
    setTabletEntryExpanded(expanded);
    if (!expanded) controls.querySelector('#tabletEntryPinned').checked = false;
  });
  const handle = controls.querySelector('#tabletEntryToggle');
  let drag = null;
  let suppressClick = false;
  handle.addEventListener('pointerdown', event => {
    if (event.button !== 0 || !isTabletWorkspace() || tabletWorkspaceOrientation() !== 'portrait') return;
    drag = { id: event.pointerId, y: event.clientY };
    suppressClick = false;
    handle.setPointerCapture?.(event.pointerId);
  });
  handle.addEventListener('pointerup', event => {
    if (!drag || event.pointerId !== drag.id) return;
    const distance = event.clientY - drag.y;
    drag = null;
    if (Math.abs(distance) < 24) return;
    suppressClick = true;
    if (distance > 0) controls.querySelector('#tabletEntryPinned').checked = false;
    setTabletEntryExpanded(distance < 0);
  });
  handle.addEventListener('pointercancel', () => { drag = null; suppressClick = false; });
  controls.querySelector('#tabletEntryPinned').addEventListener('change', event => {
    if (event.target.checked) setTabletEntryExpanded(true);
  });

  let active = false;
  const sync = () => {
    const tablet = isTabletWorkspace();
    const wasTablet = active;
    active = tablet;
    const orientation = tablet ? tabletWorkspaceOrientation() : '';
    document.documentElement.dataset.tabletLayout = orientation;
    document.documentElement.dataset.viewport = window.innerWidth < 768 ? 'mobile' : tablet ? 'tablet' : 'desktop';
    syncTabletPickerOwnership(orientation);
    if (!tablet && !wasTablet) return;
    const quickHost = document.querySelector('.quick-entry-tools');
    const grid = document.querySelector('.entry-grid');
    for (const id of ['favoriteCategoryGroup', 'summarySuggestionGroup']) {
      const group = document.querySelector('#' + id);
      const host = tablet ? grid : quickHost;
      if (group && host && group.parentElement !== host) host.append(group);
    }
    applyV21DesktopSplitWorkspace(window.matchMedia(CY_V21_SPLIT_MEDIA).matches);
    if (tablet) {
      const rail = document.querySelector('.cy-entry-rail');
      if (rail && !rail.dataset.entryExpanded) rail.dataset.entryExpanded = 'false';
      document.querySelector('.entry-card')?.classList.remove('cy-mobile-page-hidden');
      document.querySelector('.ledger-card')?.classList.remove('cy-mobile-page-hidden');
      setupTouchWorkspaceEntrySecondaryAction();
      // The original date/month controls are native and keep their existing listeners.
      document.querySelectorAll('.desktopUi-date-popover, .cy-month-picker-popover').forEach(node => { node.hidden = true; });
    } else if (!usesEntryTransactionEditor() && cyTouchWorkspaceEdit) {
      cancelTouchWorkspaceMobileEdit({ restoreDraftOnly: true });
    }
    if (wasTablet !== tablet && typeof renderSettingsAccountManager === 'function') renderSettingsAccountManager();
  };
  const syncHeight = () => {
    const height = window.visualViewport?.height || window.innerHeight;
    document.documentElement.style.setProperty('--tablet-visible-height', `${height}px`);
    const topbar = document.querySelector('.topbar');
    const notice = document.querySelector('#readOnlyNotice');
    const headerHeight = (topbar?.getBoundingClientRect().height || 0) +
      (notice && !notice.classList.contains('hidden') ? notice.getBoundingClientRect().height : 0);
    document.documentElement.style.setProperty('--tablet-header-height', `${headerHeight}px`);
  };
  window.addEventListener('resize', () => { sync(); syncHeight(); }, { passive: true });
  window.addEventListener('orientationchange', sync, { passive: true });
  window.screen?.orientation?.addEventListener?.('change', sync);
  window.matchMedia('(any-pointer: coarse)').addEventListener?.('change', sync);
  window.visualViewport?.addEventListener('resize', syncHeight, { passive: true });
  if (typeof ResizeObserver === 'function') {
    const observer = new ResizeObserver(syncHeight);
    for (const node of document.querySelectorAll('.topbar, #readOnlyNotice')) observer.observe(node);
  }
  sync();
  syncHeight();
}
window.addEventListener('load', setupTabletWorkspace, { once: true });
