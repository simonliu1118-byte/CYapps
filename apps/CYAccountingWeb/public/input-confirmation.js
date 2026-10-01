/* CYAccountingWeb input confirmation functional module. */

const cyInputConfirmations = [];
let cyPendingConfirmation = null;
let cyConfirmationObserver = null;

window.addEventListener('load', () => {
  setupEntryWorkflowUi();
  bindEntryKindVisuals();
  bindEntryKindShortcut();
  bindInputConfirmation();
  updateEntryKindVisual();
  renderInputConfirmations();
});

function setupEntryWorkflowUi() {
  const card = document.querySelector('.entry-card');
  const title = card?.querySelector('.section-title .title-with-badge');
  if (title && !document.querySelector('#entryKindIndicator')) {
    const indicator = document.createElement('span');
    indicator.id = 'entryKindIndicator';
    indicator.className = 'entry-kind-indicator expense';
    indicator.textContent = '支出模式';
    title.append(indicator);
  }

  const hint = card?.querySelector('.keyboard-hint');
  if (hint) {
    hint.innerHTML = '鍵盤：日期 Enter → 帳戶 Enter → 科目 Enter → 摘要 Enter → 金額 Enter 儲存　｜　<kbd>Tab</kbd> 切換收入／支出';
  }

  const ledger = document.querySelector('.ledger-card');
  if (ledger && !document.querySelector('#inputConfirmationCard')) {
    const section = document.createElement('section');
    section.id = 'inputConfirmationCard';
    section.className = 'card confirmation-card';
    section.innerHTML = `
      <div class="section-title">
        <div><h2>輸入確認</h2></div>
        <span class="hint">本次使用最近 10 筆</span>
      </div>
      <div id="inputConfirmationList" class="confirmation-list" aria-live="polite"></div>
    `;
    ledger.insertAdjacentElement('beforebegin', section);
  }
}

function bindEntryKindVisuals() {
  els.kindButtons?.forEach(button => button.addEventListener('click', () => {
    setTimeout(updateEntryKindVisual, 0);
  }));
}

function bindEntryKindShortcut() {
  els.form?.addEventListener('keydown', event => {
    if (event.key !== 'Tab' || event.shiftKey || event.isComposing || event.ctrlKey || event.altKey || event.metaKey) return;
    if (document.body.classList.contains('auth-locked')) return;
    if (document.querySelector('dialog[open]')) return;
    if (!(event.target instanceof HTMLElement) || !event.target.matches('input, select')) return;

    event.preventDefault();
    const active = document.activeElement;
    const nextKind = state.kind === 'expense' ? 'income' : 'expense';
    setEntryKind(nextKind);
    updateEntryKindVisual();

    setTimeout(() => {
      renderFavoriteCategories();
      loadFrequentSummaries();
      if (active instanceof HTMLElement && document.contains(active)) active.focus();
    }, 0);
  });
}

function updateEntryKindVisual() {
  const card = document.querySelector('.entry-card');
  const indicator = document.querySelector('#entryKindIndicator');
  if (!card) return;

  const isIncome = state.kind === 'income';
  card.classList.toggle('entry-income', isIncome);
  card.classList.toggle('entry-expense', !isIncome);
  if (indicator) {
    indicator.textContent = isIncome ? '收入模式' : '支出模式';
    indicator.className = `entry-kind-indicator ${isIncome ? 'income' : 'expense'}`;
  }
}

function bindInputConfirmation() {
  if (!els.form || !els.saveMessage) return;

  els.form.addEventListener('submit', () => {
    cyPendingConfirmation = snapshotPendingConfirmation();
  });

  cyConfirmationObserver = new MutationObserver(() => {
    if (!cyPendingConfirmation) return;
    const text = String(els.saveMessage.textContent || '').trim();
    if (!text) return;

    if (text === '存檔成功') {
      pushInputConfirmation({ ...cyPendingConfirmation, ok: true, message: '存檔成功' });
      cyPendingConfirmation = null;
      return;
    }

    if (els.saveMessage.classList.contains('error')) {
      pushInputConfirmation({ ...cyPendingConfirmation, ok: false, message: text });
      cyPendingConfirmation = null;
    }
  });
  cyConfirmationObserver.observe(els.saveMessage, { childList: true, subtree: true, characterData: true, attributes: true, attributeFilter: ['class'] });
}

function snapshotPendingConfirmation() {
  return {
    txDate: String(els.txDate?.value || ''),
    accountName: String(els.accountName?.value || ''),
    kind: state.kind,
    categoryName: String(els.categoryName?.value || ''),
    summary: String(els.summary?.value || '').trim(),
    amount: String(els.amount?.value || '').trim()
  };
}

function pushInputConfirmation(entry) {
  cyInputConfirmations.unshift(entry);
  if (cyInputConfirmations.length > 10) cyInputConfirmations.length = 10;
  renderInputConfirmations();
}

function renderInputConfirmations() {
  const container = document.querySelector('#inputConfirmationList');
  if (!container) return;
  if (!cyInputConfirmations.length) {
    container.innerHTML = '<div class="confirmation-empty">本次使用尚無輸入紀錄。</div>';
    return;
  }

  container.innerHTML = cyInputConfirmations.map(entry => {
    const summary = entry.summary || '(空白)';
    const amountNumber = Number(entry.amount);
    const amountText = Number.isFinite(amountNumber) && amountNumber > 0 ? money(amountNumber) : `$${escapeHtml(entry.amount || '0')}`;
    const kindText = entry.kind === 'income' ? '收入' : '支出';
    const date = escapeHtml(String(entry.txDate || '').replaceAll('-', '/'));
    return `<div class="confirmation-item ${entry.ok ? 'success' : 'error'}">
      <div class="confirmation-main">
        ${date}　[${escapeHtml(entry.accountName)}]　<span class="confirmation-kind ${entry.kind}">${kindText}</span> ${escapeHtml(entry.categoryName)} - ${escapeHtml(summary)}　${amountText}
      </div>
      <span class="confirmation-status">${entry.ok ? '存檔成功' : escapeHtml(entry.message || '存檔失敗')}</span>
    </div>`;
  }).join('');
}

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

window.addEventListener('load', () => {
  polishV091ConfirmationSidebar();
});

function polishV091ConfirmationSidebar() {
  const topbar = document.querySelector('.topbar');
  const edgeOpen = document.querySelector('#confirmationEdgeOpen');
  const collapse = document.querySelector('#confirmationDrawerCollapse');
  if (!edgeOpen || !collapse) return;

  const chevronLeft = `
    <svg viewBox="0 0 24 24" aria-hidden="true">
      <path d="M13.5 7.5 9 12l4.5 4.5"></path>
      <path d="M18 7.5 13.5 12l4.5 4.5"></path>
    </svg>`;
  const chevronRight = `
    <svg viewBox="0 0 24 24" aria-hidden="true">
      <path d="m10.5 7.5 4.5 4.5-4.5 4.5"></path>
      <path d="m6 7.5 4.5 4.5L6 16.5"></path>
    </svg>`;

  const existingBadge = edgeOpen.querySelector('#confirmationEdgeCount');
  edgeOpen.innerHTML = chevronLeft;
  if (existingBadge) edgeOpen.append(existingBadge);
  collapse.innerHTML = chevronRight;

  edgeOpen.title = '展開輸入確認';
  collapse.title = '收合輸入確認';

  const syncBounds = () => {
    if (!topbar) return;
    const height = Math.ceil(topbar.getBoundingClientRect().height);
    document.documentElement.style.setProperty('--cy-confirmation-top', `${height}px`);
  };

  syncBounds();
  window.addEventListener('resize', syncBounds, { passive: true });
}