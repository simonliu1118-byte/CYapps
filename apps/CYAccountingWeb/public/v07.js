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
