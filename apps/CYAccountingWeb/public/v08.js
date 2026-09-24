const CY_CONFIRMATION_DRAWER_KEY = 'cyaccounting.confirmationDrawerOpen';

window.addEventListener('load', () => {
  setupConfirmationDrawer();
});

function setupConfirmationDrawer() {
  const panel = document.querySelector('#inputConfirmationCard');
  const topbarActions = document.querySelector('.topbar-actions');
  const settingsButton = document.querySelector('#settingsButton');
  if (!panel || !topbarActions || document.querySelector('#confirmationToggle')) return;

  panel.className = 'confirmation-drawer';
  panel.setAttribute('role', 'complementary');
  panel.setAttribute('aria-label', '輸入確認');
  panel.setAttribute('aria-hidden', 'true');

  const title = panel.querySelector('.section-title');
  if (title) {
    title.classList.add('confirmation-drawer-header');
    const close = document.createElement('button');
    close.id = 'confirmationDrawerClose';
    close.className = 'icon-button confirmation-drawer-close';
    close.type = 'button';
    close.setAttribute('aria-label', '關閉輸入確認');
    close.textContent = '×';
    title.append(close);
  }

  document.body.append(panel);

  const toggle = document.createElement('button');
  toggle.id = 'confirmationToggle';
  toggle.className = 'secondary compact confirmation-toggle';
  toggle.type = 'button';
  toggle.setAttribute('aria-controls', 'inputConfirmationCard');
  toggle.setAttribute('aria-expanded', 'false');
  toggle.innerHTML = '輸入確認 <span id="confirmationCount" class="confirmation-count">0</span>';
  if (settingsButton) settingsButton.insertAdjacentElement('beforebegin', toggle);
  else topbarActions.prepend(toggle);

  toggle.addEventListener('click', () => {
    setConfirmationDrawer(!panel.classList.contains('open'));
  });
  panel.querySelector('#confirmationDrawerClose')?.addEventListener('click', () => setConfirmationDrawer(false));

  document.addEventListener('keydown', event => {
    if (event.key !== 'Escape' || !panel.classList.contains('open')) return;
    if (document.querySelector('dialog[open]')) return;
    setConfirmationDrawer(false);
    toggle.focus();
  });

  const list = panel.querySelector('#inputConfirmationList');
  if (list) {
    const observer = new MutationObserver(() => {
      updateConfirmationCount();
      if (list.querySelector('.confirmation-item.error')) setConfirmationDrawer(true);
    });
    observer.observe(list, { childList: true, subtree: true });
  }

  updateConfirmationCount();
  setConfirmationDrawer(localStorage.getItem(CY_CONFIRMATION_DRAWER_KEY) === '1', false);
}

function setConfirmationDrawer(open, persist = true) {
  const panel = document.querySelector('#inputConfirmationCard');
  const toggle = document.querySelector('#confirmationToggle');
  if (!panel || !toggle) return;

  panel.classList.toggle('open', open);
  panel.setAttribute('aria-hidden', open ? 'false' : 'true');
  toggle.classList.toggle('active', open);
  toggle.setAttribute('aria-expanded', open ? 'true' : 'false');
  if (persist) localStorage.setItem(CY_CONFIRMATION_DRAWER_KEY, open ? '1' : '0');
}

function updateConfirmationCount() {
  const count = document.querySelectorAll('#inputConfirmationList .confirmation-item').length;
  const badge = document.querySelector('#confirmationCount');
  if (!badge) return;
  badge.textContent = String(count);
  badge.classList.toggle('has-items', count > 0);
}
