let cyFastSummaryRequest = 0;
let cyFocusSummaryAfterSave = false;

window.addEventListener('load', () => {
  bindFastEntryKeys();
  bindQuickTools();
  bindFavoriteManager();
  observeAppRefreshes();
  refreshFastEntryUi();
});

function bindFastEntryKeys() {
  const flow = [els.txDate, els.accountName, els.categoryName, els.summary, els.amount];
  flow.forEach((element, index) => {
    element?.addEventListener('keydown', event => {
      if (event.key !== 'Enter' || event.isComposing) return;
      event.preventDefault();
      if (element === els.amount) {
        if (!els.saveButton.disabled) {
          cyFocusSummaryAfterSave = true;
          els.form.requestSubmit();
        }
        return;
      }
      flow[index + 1]?.focus();
    });
  });

  els.form?.addEventListener('submit', () => {
    cyFocusSummaryAfterSave = true;
  });

  els.accountName?.addEventListener('change', loadFrequentSummaries);
  els.categoryName?.addEventListener('change', loadFrequentSummaries);
  els.kindButtons?.forEach(button => button.addEventListener('click', () => {
    setTimeout(() => {
      renderFavoriteCategories();
      loadFrequentSummaries();
    }, 0);
  }));
}

function bindQuickTools() {
  document.querySelector('#favoriteCategoryButtons')?.addEventListener('click', event => {
    const button = event.target.closest('[data-quick-category]');
    if (!button) return;
    const name = button.dataset.quickCategory;
    if (![...els.categoryName.options].some(option => option.value === name)) return;
    els.categoryName.value = name;
    loadFrequentSummaries();
    els.summary.focus();
  });

  document.querySelector('#summarySuggestions')?.addEventListener('click', event => {
    const button = event.target.closest('[data-summary-suggestion]');
    if (!button) return;
    els.summary.value = button.dataset.summarySuggestion || '';
    els.amount.focus();
  });
}

function bindFavoriteManager() {
  els.categoryManager?.addEventListener('click', async event => {
    const button = event.target.closest('[data-category-favorite]');
    if (!button) return;
    const id = Number(button.dataset.categoryFavorite);
    const category = state.categories.find(item => Number(item.id) === id);
    if (!category) return;
    button.disabled = true;
    setDialogMessage(els.settingsMessage, '');
    try {
      await api(`/api/categories/${id}/favorite`, {
        method: 'PUT',
        headers: jsonHeaders(),
        body: JSON.stringify({ favorite: Number(category.is_favorite) !== 1 })
      });
      await refreshBootstrap();
      renderFavoriteCategories();
      setDialogMessage(els.settingsMessage, Number(category.is_favorite) === 1 ? '已取消常用科目。' : '已加入常用科目。');
    } catch (error) {
      setDialogMessage(els.settingsMessage, error.message, true);
    }
  });
}

function observeAppRefreshes() {
  const connectionObserver = new MutationObserver(() => {
    if (els.connectionStatus?.textContent === '已連線') refreshFastEntryUi();
  });
  if (els.connectionStatus) connectionObserver.observe(els.connectionStatus, { childList: true, subtree: true, characterData: true });

  const categoryObserver = new MutationObserver(() => injectFavoriteButtons());
  if (els.categoryManager) categoryObserver.observe(els.categoryManager, { childList: true, subtree: true });

  const ledgerObserver = new MutationObserver(() => {
    if (cyFocusSummaryAfterSave && els.saveMessage?.textContent === '存檔成功') {
      cyFocusSummaryAfterSave = false;
      setTimeout(() => {
        els.summary.focus();
        loadFrequentSummaries();
      }, 0);
    }
  });
  if (els.transactionRows) ledgerObserver.observe(els.transactionRows, { childList: true, subtree: true });
}

function refreshFastEntryUi() {
  renderFavoriteCategories();
  injectFavoriteButtons();
  loadFrequentSummaries();
}

function renderFavoriteCategories() {
  const group = document.querySelector('#favoriteCategoryGroup');
  const container = document.querySelector('#favoriteCategoryButtons');
  if (!group || !container) return;
  const favorites = state.categories
    .filter(category => category.kind === state.kind && Number(category.is_favorite) === 1)
    .slice(0, 10);
  group.classList.toggle('hidden', favorites.length === 0);
  container.innerHTML = favorites.map(category =>
    `<button type="button" class="quick-chip" data-quick-category="${escapeHtml(category.name)}">${escapeHtml(category.name)}</button>`
  ).join('');
}

async function loadFrequentSummaries() {
  const group = document.querySelector('#summarySuggestionGroup');
  const container = document.querySelector('#summarySuggestions');
  if (!group || !container || !els.accountName?.value || !els.categoryName?.value) return;
  const requestId = ++cyFastSummaryRequest;
  group.classList.add('hidden');
  container.innerHTML = '';
  const query = new URLSearchParams({
    kind: state.kind,
    account: els.accountName.value,
    category: els.categoryName.value
  });
  try {
    const data = await api(`/api/summaries/frequent?${query.toString()}`);
    if (requestId !== cyFastSummaryRequest) return;
    const summaries = data.summaries || [];
    group.classList.toggle('hidden', summaries.length === 0);
    container.innerHTML = summaries.map(summary =>
      `<button type="button" class="quick-chip summary-chip" data-summary-suggestion="${escapeHtml(summary)}">${escapeHtml(summary)}</button>`
    ).join('');
  } catch {
    if (requestId !== cyFastSummaryRequest) return;
    group.classList.add('hidden');
  }
}

function injectFavoriteButtons() {
  for (const item of els.categoryManager?.querySelectorAll('.category-item') || []) {
    const rename = item.querySelector('[data-category-rename]');
    if (!rename || item.querySelector('[data-category-favorite]')) continue;
    const id = Number(rename.dataset.categoryRename);
    const category = state.categories.find(entry => Number(entry.id) === id);
    if (!category) continue;
    const actions = rename.parentElement;
    const button = document.createElement('button');
    button.type = 'button';
    button.className = `mini-button favorite-toggle${Number(category.is_favorite) === 1 ? ' active' : ''}`;
    button.dataset.categoryFavorite = String(id);
    button.title = Number(category.is_favorite) === 1 ? '取消常用科目' : '設為常用科目';
    button.textContent = Number(category.is_favorite) === 1 ? '★ 常用' : '☆ 常用';
    actions.prepend(button, document.createTextNode(' '));
  }
}
