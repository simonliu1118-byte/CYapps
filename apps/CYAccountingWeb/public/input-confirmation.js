/* CYAccountingWeb input confirmation functional module. */

const cyInputConfirmations = [];
let cyPendingConfirmation = null;
let cyConfirmationObserver = null;

window.addEventListener('cyacc:entry-kind-changed', updateEntryKindVisual);

window.addEventListener('load', () => {
  setupEntryWorkflowUi();
  bindEntryKindShortcut();
  bindInputConfirmation();
  updateEntryKindVisual();
  renderInputConfirmations();
  setupConfirmationDrawer();
});

function setupEntryWorkflowUi() {
  const card = document.querySelector('.entry-card');
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
    if (active instanceof HTMLElement && document.contains(active)) active.focus();
  });
}

function updateEntryKindVisual() {
  const card = document.querySelector('.entry-card');
  if (!card) return;

  const isIncome = state.kind === 'income';
  card.classList.toggle('entry-income', isIncome);
  card.classList.toggle('entry-expense', !isIncome);
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

function setupConfirmationDrawer() {
  const panel = document.querySelector('#inputConfirmationCard');
  if (!panel || document.querySelector('#confirmationEdgeOpen')) return;

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

  const edgeOpen = document.createElement('button');
  edgeOpen.id = 'confirmationEdgeOpen';
  edgeOpen.className = 'confirmation-edge-open';
  edgeOpen.type = 'button';
  edgeOpen.setAttribute('aria-label', '展開輸入確認');
  edgeOpen.setAttribute('aria-controls', 'inputConfirmationCard');
  edgeOpen.title = '展開輸入確認';
  edgeOpen.innerHTML = `
    <svg viewBox="0 0 24 24" aria-hidden="true">
      <path d="M13.5 7.5 9 12l4.5 4.5"></path>
      <path d="M18 7.5 13.5 12l4.5 4.5"></path>
    </svg>
    <span id="confirmationEdgeCount" class="confirmation-count">0</span>`;
  document.body.append(edgeOpen);

  const collapse = document.createElement('button');
  collapse.id = 'confirmationDrawerCollapse';
  collapse.className = 'confirmation-drawer-collapse';
  collapse.type = 'button';
  collapse.setAttribute('aria-label', '收合輸入確認');
  collapse.title = '收合輸入確認';
  collapse.innerHTML = `
    <svg viewBox="0 0 24 24" aria-hidden="true">
      <path d="m10.5 7.5 4.5 4.5-4.5 4.5"></path>
      <path d="m6 7.5 4.5 4.5L6 16.5"></path>
    </svg>`;
  panel.append(collapse);

  edgeOpen.addEventListener('click', () => setConfirmationDrawer(true));
  collapse.addEventListener('click', () => setConfirmationDrawer(false));
  panel.querySelector('#confirmationDrawerClose')?.addEventListener('click', () => setConfirmationDrawer(false));

  document.addEventListener('keydown', event => {
    if (event.key !== 'Escape' || !panel.classList.contains('open')) return;
    if (document.querySelector('dialog[open]')) return;
    setConfirmationDrawer(false);
    edgeOpen.focus();
  });

  const list = panel.querySelector('#inputConfirmationList');
  if (list) {
    const observer = new MutationObserver(() => {
      syncConfirmationCount();
      if (list.querySelector('.confirmation-item.error')) setConfirmationDrawer(true);
    });
    observer.observe(list, { childList: true, subtree: true });
  }

  const topbar = document.querySelector('.topbar');
  const syncBounds = () => {
    if (!topbar) return;
    const height = Math.ceil(topbar.getBoundingClientRect().height);
    document.documentElement.style.setProperty('--cy-confirmation-top', `${height}px`);
  };
  syncBounds();
  window.addEventListener('resize', syncBounds, { passive: true });

  syncConfirmationCount();
  const stored = localStorage.getItem(CY_CONFIRMATION_DRAWER_KEY);
  setConfirmationDrawer(stored === null ? true : stored === '1', false);
  // The confirmation component owns its creation; dependent layouts must wait
  // until the drawer exists instead of relying on DOMContentLoaded timing.
  window.dispatchEvent(new CustomEvent('cyacc:confirmation-ready'));
}

function setConfirmationDrawer(open, persist = true) {
  const panel = document.querySelector('#inputConfirmationCard');
  const edgeOpen = document.querySelector('#confirmationEdgeOpen');
  const collapse = document.querySelector('#confirmationDrawerCollapse');
  if (!panel) return;

  panel.classList.toggle('open', open);
  panel.setAttribute('aria-hidden', open ? 'false' : 'true');
  edgeOpen?.classList.toggle('hidden-edge', open);
  collapse?.classList.toggle('hidden-edge', !open);
  edgeOpen?.setAttribute('aria-expanded', open ? 'true' : 'false');
  if (persist) localStorage.setItem(CY_CONFIRMATION_DRAWER_KEY, open ? '1' : '0');
}

function syncConfirmationCount() {
  const count = document.querySelectorAll('#inputConfirmationList .confirmation-item').length;
  const badge = document.querySelector('#confirmationEdgeCount');
  if (!badge) return;
  badge.textContent = String(count);
  badge.classList.toggle('has-items', count > 0);
}
