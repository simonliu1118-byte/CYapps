const CY_V0216_VERSION = 'V0.21.6 Build 8';

if (document.readyState === 'loading') {
  document.addEventListener('DOMContentLoaded', setupV0216Version, { once: true });
} else {
  setupV0216Version();
}
window.addEventListener('load', setupV0216Version, { once: true });

function setupV0216Version() {
  syncV0216Version();
  window.setTimeout(syncV0216Version, 250);
  window.setTimeout(syncV0216Version, 1000);

  const version = document.querySelector('.version');
  if (!version || version.dataset.v0216Observed === 'true') return;
  version.dataset.v0216Observed = 'true';
  new MutationObserver(syncV0216Version).observe(version, {
    childList: true,
    characterData: true,
    subtree: true
  });
}

function syncV0216Version() {
  const version = document.querySelector('.version');
  if (version && version.textContent !== CY_V0216_VERSION) version.textContent = CY_V0216_VERSION;
}
