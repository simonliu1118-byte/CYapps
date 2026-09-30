const CY_V21_BUILD10_VERSION = 'V0.21.0 Build 10';
const CY_V21_BUILD10_MOBILE = '(max-width: 767px)';
const CY_V21_CONFIRMATION_KEY = 'cyaccounting.confirmationDrawerOpen';

ensureV21Build10Stylesheet();

document.addEventListener('DOMContentLoaded', () => {
  syncV21Build10Version();
  setupV21Build10MobileAppBar();
  setupV21Build10MobileNavigation();
  setupV21Build10AccountSheet();
  setupV21Build10LedgerTools();
  setupV21Build10ConfirmationPolicy();
  setupV21Build10MobileFormCopy();
});

window.addEventListener('load', () => {
  syncV21Build10Version();
  syncV21Build10MobileIdentity();
  syncV21Build10MobileNavigation();
  syncV21Build10ConfirmationPolicy();
});

function ensureV21Build10Stylesheet() {
  if (document.querySelector('link[href="/v021b10.css"]')) return;
  const link = document.createElement('link');
  link.rel = 'stylesheet';
  link.href = '/v021b10.css';
  document.head.appendChild(link);
}

function syncV21Build10Version() {
  const version = document.querySelector('.version');
  if (version && version.textContent !== CY_V21_BUILD10_VERSION) version.textContent = CY_V21_BUILD10_VERSION;
}

function setupV21Build10MobileAppBar() {
  const topbar = document.querySelector('.topbar');
  const currentUser = document.querySelector('#currentUser');
  const logoutButton = document.querySelector('#logoutButton');
  if (!topbar || !currentUser || !logoutButton) return;

  let trigger = document.querySelector('#mobileAccountMenuButton');
  if (!trigger) {
    trigger = document.createElement('button');
    trigger.id = 'mobileAccountMenuButton';
    trigger.className = 'v21-mobile-account-menu-button';
    trigger.type = 'button';
    trigger.setAttribute('aria-haspopup', 'true');
    trigger.setAttribute('aria-expanded', 'false');
    trigger.innerHTML = '<span class="v21-mobile-account-name">帳號</span><span aria-hidden="true">›</span>';
    topbar.append(trigger);
  }

  let menu = document.querySelector('#mobileAccountMenu');
  if (!menu) {
    menu = document.createElement('div');
    menu.id = 'mobileAccountMenu';
    menu.className = 'v21-mobile-account-menu';
    menu.hidden = true;
    menu.innerHTML = `
      <div class="v21-mobile-account-menu-identity">
        <strong id="mobileAccountMenuName">帳號</strong>
        <span id="mobileAccountMenuRole"></span>
      </div>
      <button type="button" class="danger-lite" data-mobile-account-action="logout">登出</button>`;
    document.body.append(menu);
  }

  const close = () => {
    menu.hidden = true;
    trigger.setAttribute('aria-expanded', 'false');
    document.body.classList.remove('v21-mobile-account-menu-open');
  };

  trigger.addEventListener('click', event => {
    if (!window.matchMedia(CY_V21_BUILD10_MOBILE).matches) return;
    event.stopPropagation();
    const open = menu.hidden;
    menu.hidden = !open;
    trigger.setAttribute('aria-expanded', open ? 'true' : 'false');
    document.body.classList.toggle('v21-mobile-account-menu-open', open);
    if (open) syncV21Build10MobileIdentity();
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

  const observer = new MutationObserver(syncV21Build10MobileIdentity);
  observer.observe(currentUser, { childList: true, subtree: true, characterData: true, attributes: true, attributeFilter: ['class'] });

  const mobile = window.matchMedia(CY_V21_BUILD10_MOBILE);
  const syncMode = () => {
    document.body.classList.toggle('v21-mobile-app', mobile.matches);
    if (!mobile.matches) close();
    syncV21Build10MobileIdentity();
  };
  if (typeof mobile.addEventListener === 'function') mobile.addEventListener('change', syncMode);
  else mobile.addListener?.(syncMode);
  syncMode();
}

function syncV21Build10MobileIdentity() {
  const source = document.querySelector('#currentUser');
  const trigger = document.querySelector('#mobileAccountMenuButton');
  const triggerName = trigger?.querySelector('.v21-mobile-account-name');
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

function setupV21Build10MobileNavigation() {
  const sync = () => {
    const nav = document.querySelector('#mobileMainNav');
    if (!nav) return false;
    nav.classList.add('v21-mobile-bottom-nav');
    if (nav.parentElement !== document.body) document.body.append(nav);

    const entryButton = nav.querySelector('[data-mobile-page="entry"]');
    const ledgerButton = nav.querySelector('[data-mobile-page="ledger"]');
    if (entryButton && !entryButton.querySelector('.v21-mobile-nav-icon')) {
      entryButton.innerHTML = '<span class="v21-mobile-nav-icon" aria-hidden="true">＋</span><span>新增記帳</span>';
    }
    if (ledgerButton && !ledgerButton.querySelector('.v21-mobile-nav-icon')) {
      ledgerButton.innerHTML = '<span class="v21-mobile-nav-icon" aria-hidden="true">≡</span><span>記帳資料</span>';
    }
    return true;
  };

  if (!sync()) setTimeout(sync, 0);
  const mobile = window.matchMedia(CY_V21_BUILD10_MOBILE);
  const syncMode = () => {
    sync();
    document.body.classList.toggle('v21-mobile-app', mobile.matches);
  };
  if (typeof mobile.addEventListener === 'function') mobile.addEventListener('change', syncMode);
  else mobile.addListener?.(syncMode);
}

function syncV21Build10MobileNavigation() {
  const nav = document.querySelector('#mobileMainNav');
  if (!nav) return;
  nav.classList.add('v21-mobile-bottom-nav');
  if (nav.parentElement !== document.body) document.body.append(nav);
}

function setupV21Build10AccountSheet() {
  const row = document.querySelector('#entryAccountChoiceRow');
  if (!row) return;

  let backdrop = document.querySelector('#mobileAccountSheetBackdrop');
  if (!backdrop) {
    backdrop = document.createElement('button');
    backdrop.id = 'mobileAccountSheetBackdrop';
    backdrop.className = 'v21-mobile-sheet-backdrop';
    backdrop.type = 'button';
    backdrop.setAttribute('aria-label', '關閉帳戶選單');
    backdrop.hidden = true;
    document.body.append(backdrop);
  }

  const sync = () => {
    const open = window.matchMedia(CY_V21_BUILD10_MOBILE).matches && row.classList.contains('mobile-picker-open');
    backdrop.hidden = !open;
    document.body.classList.toggle('v21-mobile-account-sheet-open', open);
  };

  backdrop.addEventListener('click', () => {
    if (typeof setV21Build9AccountPickerOpen === 'function') setV21Build9AccountPickerOpen(false);
  });

  const observer = new MutationObserver(sync);
  observer.observe(row, { attributes: true, attributeFilter: ['class'] });
  document.querySelector('#entryAccountButtons')?.addEventListener('click', () => setTimeout(sync, 0));
  sync();
}

function setupV21Build10LedgerTools() {
  const ledger = document.querySelector('.ledger-card');
  if (!ledger) return;

  let button = document.querySelector('#mobileLedgerMoreButton');
  if (!button) {
    button = document.createElement('button');
    button.id = 'mobileLedgerMoreButton';
    button.className = 'secondary compact v21-mobile-ledger-more';
    button.type = 'button';
    button.textContent = '更多';
    button.setAttribute('aria-haspopup', 'true');
    button.setAttribute('aria-expanded', 'false');
    const summaryBar = ledger.querySelector('.v21-ledger-summary-bar') || ledger.querySelector('.ledger-title');
    summaryBar?.append(button);
  }

  let backdrop = document.querySelector('#mobileLedgerToolsBackdrop');
  if (!backdrop) {
    backdrop = document.createElement('button');
    backdrop.id = 'mobileLedgerToolsBackdrop';
    backdrop.className = 'v21-mobile-sheet-backdrop';
    backdrop.type = 'button';
    backdrop.setAttribute('aria-label', '關閉記帳工具');
    backdrop.hidden = true;
    document.body.append(backdrop);
  }

  let sheet = document.querySelector('#mobileLedgerToolsSheet');
  if (!sheet) {
    sheet = document.createElement('section');
    sheet.id = 'mobileLedgerToolsSheet';
    sheet.className = 'v21-mobile-tools-sheet';
    sheet.hidden = true;
    sheet.innerHTML = `
      <div class="v21-mobile-sheet-handle" aria-hidden="true"></div>
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
    document.body.classList.remove('v21-mobile-ledger-tools-open');
  };

  const open = () => {
    if (!window.matchMedia(CY_V21_BUILD10_MOBILE).matches) return;
    sheet.hidden = false;
    backdrop.hidden = false;
    button.setAttribute('aria-expanded', 'true');
    document.body.classList.add('v21-mobile-ledger-tools-open');
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

function setupV21Build10ConfirmationPolicy() {
  const mobile = window.matchMedia(CY_V21_BUILD10_MOBILE);
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

function syncV21Build10ConfirmationPolicy() {
  if (!window.matchMedia(CY_V21_BUILD10_MOBILE).matches) return;
  if (typeof setConfirmationDrawer === 'function') setConfirmationDrawer(false, false);
}

function setupV21Build10MobileFormCopy() {
  const summary = document.querySelector('#summary');
  if (summary) summary.placeholder = '可留白，最多 20 個中文字';

  const trigger = document.querySelector('#entryAccountPickerButton');
  const arrow = trigger?.querySelector('.entry-account-picker-arrow');
  if (arrow) arrow.textContent = '›';
}
