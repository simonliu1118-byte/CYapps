const CY_V21_BUILD13_VERSION = 'V0.21.0 Build 13';
const CY_V21_BUILD13_DESKTOP = '(min-width: 1024px)';

ensureV21Build13Stylesheet();
syncV21Build13Version();
setupV21Build13ConnectionStatus();

const runV21Build13 = () => {
  syncV21Build13Version();
  setupV21Build13ConnectionStatus();
  setupV21Build13HeaderManagement();
  setupV21Build13OpeningDialog();
  syncV21Build13CrudCopy();
};

if (document.readyState === 'complete') setTimeout(runV21Build13, 0);
else window.addEventListener('load', () => setTimeout(runV21Build13, 0), { once: true });

function ensureV21Build13Stylesheet() {
  if (document.querySelector('link[href="/v021b13.css"]')) return;
  const attach = () => {
    if (document.querySelector('link[href="/v021b13.css"]')) return;
    if (!document.querySelector('link[href="/v021b12.css"]')) {
      setTimeout(attach, 20);
      return;
    }
    const link = document.createElement('link');
    link.rel = 'stylesheet';
    link.href = '/v021b13.css';
    document.head.appendChild(link);
  };
  attach();
}

function syncV21Build13Version() {
  const version = document.querySelector('.version');
  if (version) version.textContent = CY_V21_BUILD13_VERSION;
}

function setupV21Build13ConnectionStatus() {
  const status = document.querySelector('#connectionStatus');
  if (!status || status.dataset.v21Build13Bound === '1') return;
  status.dataset.v21Build13Bound = '1';

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

function setupV21Build13HeaderManagement() {
  const media = window.matchMedia(CY_V21_BUILD13_DESKTOP);
  const actions = document.querySelector('.topbar-actions');
  const settings = document.querySelector('#settingsButton');
  const dialog = document.querySelector('#settingsDialog');
  const title = dialog?.querySelector('.modal-header h2');
  if (!actions || !settings || !dialog || !title) return;

  let accountsButton = document.querySelector('#headerAccountManagerButton');
  if (!accountsButton) {
    accountsButton = document.createElement('button');
    accountsButton.id = 'headerAccountManagerButton';
    accountsButton.className = 'secondary compact v21-header-management-button';
    accountsButton.type = 'button';
    accountsButton.textContent = '帳戶管理';
    actions.insertBefore(accountsButton, settings);
  }

  let categoriesButton = document.querySelector('#headerCategoryManagerButton');
  if (!categoriesButton) {
    categoriesButton = document.createElement('button');
    categoriesButton.id = 'headerCategoryManagerButton';
    categoriesButton.className = 'secondary compact v21-header-management-button';
    categoriesButton.type = 'button';
    categoriesButton.textContent = '科目管理';
    actions.insertBefore(categoriesButton, settings);
  }

  if (accountsButton.dataset.v21Build13Bound !== '1') {
    accountsButton.dataset.v21Build13Bound = '1';
    accountsButton.addEventListener('click', () => openV21Build13Management('accounts', '帳戶管理'));
  }
  if (categoriesButton.dataset.v21Build13Bound !== '1') {
    categoriesButton.dataset.v21Build13Bound = '1';
    categoriesButton.addEventListener('click', () => openV21Build13Management('categories', '科目管理'));
  }

  if (settings.dataset.v21Build13Bound !== '1') {
    settings.dataset.v21Build13Bound = '1';
    settings.addEventListener('click', () => {
      setTimeout(() => {
        dialog.classList.remove('v21-management-mode');
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
    const enabled = media.matches;
    accountsButton.hidden = !enabled;
    categoriesButton.hidden = !enabled;
    if (!enabled) {
      dialog.classList.remove('v21-management-mode');
      delete dialog.dataset.managementPane;
      title.textContent = '設定';
    }
  };
  syncDesktop();
  if (typeof media.addEventListener === 'function' && !actions.dataset.v21Build13MediaBound) {
    actions.dataset.v21Build13MediaBound = '1';
    media.addEventListener('change', syncDesktop);
  }
}

function openV21Build13Management(tab, label) {
  if (!window.matchMedia(CY_V21_BUILD13_DESKTOP).matches) return;
  const dialog = document.querySelector('#settingsDialog');
  const title = dialog?.querySelector('.modal-header h2');
  if (!dialog || !title) return;

  if (!dialog.open) {
    if (typeof openSettings === 'function') openSettings();
    else dialog.showModal();
  } else if (typeof renderSettings === 'function') {
    renderSettings();
  }

  dialog.classList.add('v21-management-mode');
  dialog.dataset.managementPane = tab;
  title.textContent = label;
  if (typeof setSettingsTab === 'function') setSettingsTab(tab);
}

function setupV21Build13OpeningDialog() {
  const dialog = document.querySelector('#openingDialog');
  const monthInput = document.querySelector('#openingMonth');
  const ledgerMonth = document.querySelector('#monthFilter');
  const title = dialog?.querySelector('.modal-header h2');
  if (!dialog || !monthInput || !ledgerMonth || !title) return;

  const sync = () => {
    if (!window.matchMedia(CY_V21_BUILD13_DESKTOP).matches) return;
    const month = /^\d{4}-\d{2}$/.test(ledgerMonth.value || '') ? ledgerMonth.value : monthInput.value;
    if (/^\d{4}-\d{2}$/.test(month || '')) {
      if (monthInput.value !== month) {
        monthInput.value = month;
        monthInput.dispatchEvent(new Event('change', { bubbles: true }));
      }
      title.textContent = `${month.replace('-', '/')} 期初餘額`;
    }
  };

  sync();
  const observer = new MutationObserver(() => {
    if (dialog.open) sync();
  });
  observer.observe(dialog, { attributes: true, attributeFilter: ['open'] });
  document.querySelector('#ledgerOpeningBalanceButton')?.addEventListener('click', () => setTimeout(sync, 0));
}

function syncV21Build13CrudCopy() {
  const openingSave = document.querySelector('#saveOpeningButton');
  const editSave = document.querySelector('#editSaveButton');
  if (openingSave) openingSave.textContent = '儲存';
  if (editSave) editSave.textContent = '儲存';
}
