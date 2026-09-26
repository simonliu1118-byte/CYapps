const CY_V20_VERSION = 'V0.20.1';
const CY_V20_MOBILE_CONFIRMATION_INIT = 'cyaccounting.v20.mobileConfirmationInitialized';

ensureV201Stylesheet();

window.addEventListener('load', () => {
  syncV20Version();
  setupV20ViewportState();
  setupV20MobileConfirmationDefault();
  setupV20SettingsTabVisibility();
  setupV201MobileInlineEditVisibility();
});

function ensureV201Stylesheet() {
  if (document.querySelector('link[href="/v0201.css"]')) return;
  const link = document.createElement('link');
  link.rel = 'stylesheet';
  link.href = '/v0201.css';
  document.head.appendChild(link);
}

function syncV20Version() {
  const version = document.querySelector('.version');
  if (version) version.textContent = CY_V20_VERSION;
}

function setupV20ViewportState() {
  const sync = () => {
    const width = window.innerWidth;
    document.documentElement.dataset.viewport = width < 768 ? 'mobile' : width < 1024 ? 'tablet' : 'desktop';
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
