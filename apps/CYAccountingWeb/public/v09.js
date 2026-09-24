const CY_CONFIRMATION_DRAWER_STATE_KEY = 'cyaccounting.confirmationDrawerOpen';

window.addEventListener('load', () => {
  setupV09EntryKindSwitch();
  setupV09ConfirmationEdgeControls();
  setupV09OpeningBalanceAction();
});

function setupV09EntryKindSwitch() {
  document.querySelector('#entryKindIndicator')?.remove();
}

function setupV09ConfirmationEdgeControls() {
  const panel = document.querySelector('#inputConfirmationCard');
  const legacyToggle = document.querySelector('#confirmationToggle');
  if (!panel || !legacyToggle || document.querySelector('#confirmationEdgeOpen')) return;

  const edgeOpen = document.createElement('button');
  edgeOpen.id = 'confirmationEdgeOpen';
  edgeOpen.className = 'confirmation-edge-open';
  edgeOpen.type = 'button';
  edgeOpen.setAttribute('aria-label', '展開輸入確認');
  edgeOpen.setAttribute('aria-controls', 'inputConfirmationCard');
  edgeOpen.innerHTML = `&lt;&lt;<span id="confirmationEdgeCount" class="confirmation-count">0</span>`;
  document.body.append(edgeOpen);

  const collapse = document.createElement('button');
  collapse.id = 'confirmationDrawerCollapse';
  collapse.className = 'confirmation-drawer-collapse';
  collapse.type = 'button';
  collapse.setAttribute('aria-label', '收合輸入確認');
  collapse.textContent = '>>';
  panel.append(collapse);

  edgeOpen.addEventListener('click', () => setConfirmationDrawer(true));
  collapse.addEventListener('click', () => setConfirmationDrawer(false));

  const sync = () => {
    const open = panel.classList.contains('open');
    edgeOpen.classList.toggle('hidden-edge', open);
    collapse.classList.toggle('hidden-edge', !open);
    edgeOpen.setAttribute('aria-expanded', open ? 'true' : 'false');
    syncV09ConfirmationCount();
  };

  const classObserver = new MutationObserver(sync);
  classObserver.observe(panel, { attributes: true, attributeFilter: ['class'] });

  const list = panel.querySelector('#inputConfirmationList');
  if (list) {
    const countObserver = new MutationObserver(syncV09ConfirmationCount);
    countObserver.observe(list, { childList: true, subtree: true });
  }

  const stored = localStorage.getItem(CY_CONFIRMATION_DRAWER_STATE_KEY);
  setConfirmationDrawer(stored === null ? true : stored === '1', false);
  sync();
}

function syncV09ConfirmationCount() {
  const count = document.querySelectorAll('#inputConfirmationList .confirmation-item').length;
  const badge = document.querySelector('#confirmationEdgeCount');
  if (!badge) return;
  badge.textContent = String(count);
  badge.classList.toggle('has-items', count > 0);
}

function setupV09OpeningBalanceAction() {
  const button = document.querySelector('#ledgerOpeningBalanceButton');
  const dialog = document.querySelector('#openingDialog');
  if (!button || !dialog) return;

  button.addEventListener('click', async () => {
    if (els.openingMonth && els.monthFilter?.value) els.openingMonth.value = els.monthFilter.value;
    if (els.openingMessage) setDialogMessage(els.openingMessage, '');
    dialog.showModal();
    await loadOpeningBalances();
  });
}
