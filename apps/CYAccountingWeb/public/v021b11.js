const CY_V21_BUILD11_VERSION = 'V0.21.0 Build 11';
const CY_V21_BUILD11_DESKTOP = '(min-width: 768px)';

ensureV21Build11Stylesheet();
ensureV21Build12Script();
ensureV21Build13Script();
ensureV21Build14Script();
ensureV21Build15Script();
ensureV21Build16Script();

document.addEventListener('DOMContentLoaded', () => {
  syncV21Build11Version();
  setupV21Build11DesktopIsolation();
});

window.addEventListener('load', () => {
  syncV21Build11Version();
  syncV21Build11DesktopIsolation();
});

function ensureV21Build11Stylesheet() {
  if (document.querySelector('link[href="/v021b11.css"]')) return;
  const link = document.createElement('link');
  link.rel = 'stylesheet';
  link.href = '/v021b11.css';
  document.head.appendChild(link);
}

function ensureV21Build12Script() {
  if (document.querySelector('script[src="/v021b12.js"]')) return;
  const script = document.createElement('script');
  script.src = '/v021b12.js';
  script.async = false;
  document.head.appendChild(script);
}

function ensureV21Build13Script() {
  if (document.querySelector('script[src="/v021b13.js"]')) return;
  const script = document.createElement('script');
  script.src = '/v021b13.js';
  script.async = false;
  document.head.appendChild(script);
}

function ensureV21Build14Script() {
  if (document.querySelector('script[src="/v021b14.js"]')) return;
  const script = document.createElement('script');
  script.src = '/v021b14.js';
  script.async = false;
  document.head.appendChild(script);
}

function ensureV21Build15Script() {
  if (document.querySelector('script[src="/v021b15.js"]')) return;
  const script = document.createElement('script');
  script.src = '/v021b15.js';
  script.async = false;
  document.head.appendChild(script);
}

function ensureV21Build16Script() {
  if (document.querySelector('script[src="/v021b16.js"]')) return;
  const script = document.createElement('script');
  script.src = '/v021b16.js';
  script.async = false;
  document.head.appendChild(script);
}

function syncV21Build11Version() {
  const version = document.querySelector('.version');
  if (version) version.textContent = CY_V21_BUILD11_VERSION;
}

function setupV21Build11DesktopIsolation() {
  const media = window.matchMedia(CY_V21_BUILD11_DESKTOP);
  const sync = () => syncV21Build11DesktopIsolation(media.matches);
  sync();
  if (typeof media.addEventListener === 'function') media.addEventListener('change', sync);
  else media.addListener?.(sync);
}

function syncV21Build11DesktopIsolation(desktop = window.matchMedia(CY_V21_BUILD11_DESKTOP).matches) {
  const accountTrigger = document.querySelector('#mobileAccountMenuButton');
  const ledgerMore = document.querySelector('#mobileLedgerMoreButton');

  if (accountTrigger) accountTrigger.hidden = Boolean(desktop);
  if (ledgerMore) ledgerMore.hidden = Boolean(desktop);

  if (!desktop) {
    document.body.classList.add('v21-mobile-app');
    return;
  }

  document.body.classList.remove(
    'v21-mobile-app',
    'v21-mobile-account-menu-open',
    'v21-mobile-account-sheet-open',
    'v21-mobile-ledger-tools-open'
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
