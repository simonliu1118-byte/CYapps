const CY_V21_VERSION = 'V0.21.0 Build 2';
const CY_V21_SPLIT_MEDIA = '(min-width: 1360px)';
const CY_V21_CONFIRMATION_STATE_KEY = 'cyaccounting.confirmationDrawerOpen';

ensureV21Build1Stylesheet();
ensureV21Build2Stylesheet();

window.addEventListener('load', () => {
  syncV21Version();
  updateV21KeyboardHint();
  setupV21DesktopSplitWorkspace();
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

function syncV21Version() {
  const version = document.querySelector('.version');
  if (version) version.textContent = CY_V21_VERSION;
}

function updateV21KeyboardHint() {
  const hint = document.querySelector('.keyboard-hint');
  if (!hint) return;
  hint.innerHTML = '鍵盤：日期 Enter → 帳戶 Enter → 科目 Enter → 摘要 Enter → 金額 Enter 儲存　｜　<kbd>Tab</kbd> 切換收入／支出　｜　日期可輸入 <kbd>0924</kbd> / <kbd>20260924</kbd>，<kbd>Ctrl</kbd>+<kbd>↑↓</kbd> ±1 天';
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
