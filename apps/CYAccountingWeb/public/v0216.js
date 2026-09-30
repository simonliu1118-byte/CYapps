const CY_V0216_VERSION = 'V0.21.6';

document.addEventListener('DOMContentLoaded', syncV0216Version);
window.addEventListener('load', syncV0216Version);

function syncV0216Version() {
  const version = document.querySelector('.version');
  if (version) version.textContent = CY_V0216_VERSION;
}
