const CY_V21_VERSION = 'V0.21.0 Build 6';
const CY_V21_SPLIT_MEDIA = '(min-width: 1360px)';
const CY_V21_CONFIRMATION_STATE_KEY = 'cyaccounting.confirmationDrawerOpen';

ensureV21Build1Stylesheet();
ensureV21Build2Stylesheet();
ensureV21Build3Stylesheet();
ensureV21Build5Stylesheet();
ensureV21Build6Stylesheet();

window.addEventListener('load', () => {
  syncV21Version();
  updateV21KeyboardHint();
  setupV21HeaderLayout();
  setupV21DesktopSplitWorkspace();
  setupV21EntryHelp();
  setupV21LedgerContext();
  setupV21LedgerHeaderDecoration();
  setupV21LedgerEmptyState();
  setupV21ConfirmationCopy();
  setupV21DataSettings();
  setupV21UserIdentity();
});

function ensureV21Build1Stylesheet() {
  if (document.querySelector('link[href="/v021b1.css"]')) return;
  const link = document.createElement('link');
  link.rel = 'stylesheet';
  link.href = '/v021b1.css';
  document.head.appendChild(link);
}

function ensureV21Build2Stylesheet() {
  if (document.querySelector('link[href="/v021b2.css"]')) return;
  const link = document.createElement('link');
  link.rel = 'stylesheet';
  link.href = '/v021b2.css';
  document.head.appendChild(link);
}

function ensureV21Build3Stylesheet() {
  if (document.querySelector('link[href="/v021b3.css"]')) return;
  const link = document.createElement('link');
  link.rel = 'stylesheet';
  link.href = '/v021b3.css';
  document.head.appendChild(link);
}

function ensureV21Build5Stylesheet() {
  if (document.querySelector('link[href="/v021b5.css"]')) return;
  const link = document.createElement('link');
  link.rel = 'stylesheet';
  link.href = '/v021b5.css';
  document.head.appendChild(link);
}

function ensureV21Build6Stylesheet() {
  if (document.querySelector('link[href="/v021b6.css"]')) return;
  const link = document.createElement('link');
  link.rel = 'stylesheet';
  link.href = '/v021b6.css';
  document.head.appendChild(link);
}

function syncV21Version() {
  const version = document.querySelector('.version');
  if (version) version.textContent = CY_V21_VERSION;
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

  let brandLine = brand.querySelector('.v21-brand-line');
  if (!brandLine) {
    brandLine = document.createElement('div');
    brandLine.className = 'v21-brand-line';
    heading.before(brandLine);
    brandLine.append(heading);
  }
  if (status && status.parentElement !== brandLine) brandLine.append(status);

  let accountCluster = actions.querySelector('.v21-account-cluster');
  if (!accountCluster) {
    accountCluster = document.createElement('div');
    accountCluster.className = 'v21-account-cluster';
  }

  if (settings && settings.parentElement !== actions) actions.append(settings);
  if (currentUser && currentUser.parentElement !== accountCluster) accountCluster.append(currentUser);
  if (logout && logout.parentElement !== accountCluster) accountCluster.append(logout);
  if (accountCluster.parentElement !== actions) actions.append(accountCluster);
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

  let context = titleMain.querySelector('.v21-ledger-context');
  if (!context) {
    context = document.createElement('div');
    context.className = 'v21-ledger-context';
    titleMain.insertBefore(context, summary);
  }
  if (monthTools.parentElement !== context) context.append(monthTools);

  let summaryBar = titleMain.querySelector('.v21-ledger-summary-bar');
  if (!summaryBar) {
    summaryBar = document.createElement('div');
    summaryBar.className = 'v21-ledger-summary-bar';
    context.insertAdjacentElement('afterend', summaryBar);
  }
  if (summary.parentElement !== summaryBar) summaryBar.append(summary);

  let summaryActions = summaryBar.querySelector('.v21-summary-actions');
  if (!summaryActions) {
    summaryActions = document.createElement('div');
    summaryActions.className = 'v21-summary-actions';
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
    account.classList.toggle('v21-account-group-active', active);
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
    const searchEmpty = text.includes('搜尋條件');
    empty.innerHTML = `<div class="ledger-empty-state"><strong>${v21EscapeHtml(text)}</strong><span>${searchEmpty ? '請調整搜尋文字或清除搜尋條件。' : '新增記帳後，資料會顯示在這裡。'}</span></div>`;
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
    pane.className = 'settings-pane v21-data-pane';
    pane.dataset.settingsPane = 'data';
    pane.innerHTML = `
      <div>
        <h3>資料管理</h3>
        <p class="hint">低頻的資料匯入工具集中在這裡，避免佔用日常記帳工作區。</p>
      </div>
      <section class="v21-data-section">
        <h4>Excel 匯入</h4>
        <p>匯入 .xlsx 記帳資料。請先確認工作表內容，再使用預覽驗證後提交。</p>
        <div class="v21-data-actions" id="v21ExcelImportHost"></div>
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
  if (subtitle) subtitle.textContent = '帳戶、科目、資料與鎖帳';
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
    const response = await fetch('/api/auth/me', { cache: 'no-store' });
    const data = await response.json().catch(() => ({}));
    if (!response.ok || data.ok === false || !data.user) return;
    user = data.user;
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
  const shell = document.querySelector('main.shell');
  const entry = shell?.querySelector('.entry-card') || document.querySelector('.entry-card');
  const ledger = shell?.querySelector('.ledger-card') || document.querySelector('.ledger-card');
  const confirmation = document.querySelector('#inputConfirmationCard');
  if (!shell || !entry || !ledger || !confirmation) return;

  let rail = shell.querySelector('.v21-entry-rail');

  if (enabled) {
    if (!rail) {
      rail = document.createElement('aside');
      rail.className = 'v21-entry-rail';
      rail.setAttribute('aria-label', '快速記帳工作區');
      shell.insertBefore(rail, ledger);
    }

    if (entry.parentElement !== rail) rail.prepend(entry);
    if (confirmation.parentElement !== rail) rail.append(confirmation);

    shell.classList.add('v21-split-layout');
    document.body.classList.add('v21-wide-split');
    confirmation.classList.add('v21-inline-confirmation');

    if (typeof setConfirmationDrawer === 'function') setConfirmationDrawer(true, false);
    else {
      confirmation.classList.add('open');
      confirmation.setAttribute('aria-hidden', 'false');
    }
    return;
  }

  shell.classList.remove('v21-split-layout');
  document.body.classList.remove('v21-wide-split');
  confirmation.classList.remove('v21-inline-confirmation');

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
