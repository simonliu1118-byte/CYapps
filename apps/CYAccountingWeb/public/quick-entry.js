/* CYAccountingWeb quick entry functional module. */

let cyFastSummaryRequest = 0;
let cyFocusSummaryAfterSave = false;

window.cyPrepareQuickEntryUi = prepareQuickEntryUi;
window.addEventListener('cyacc:ledger-rendered', handleFastEntryLedgerRendered);
window.addEventListener('cyacc:entry-kind-changed', handleQuickEntryKindChanged);

window.addEventListener('load', () => {
  bindFastEntryKeys();
  bindQuickTools();
  bindFavoriteManager();
  if (window.cyaccCoreReady) void prepareQuickEntryUi();
});

function bindFastEntryKeys() {
  const enterStep = (element, action) => {
    element?.addEventListener('keydown', event => {
      if (event.key !== 'Enter' || event.isComposing || event.shiftKey || event.ctrlKey || event.altKey || event.metaKey) return;
      event.preventDefault();
      action();
    });
  };

  enterStep(els.txDate, () => els.summary?.focus());
  enterStep(els.summary, () => {
    els.amount?.focus();
    els.amount?.select();
  });
  enterStep(els.amount, () => {
    if (!els.saveButton.disabled) {
      cyFocusSummaryAfterSave = true;
      els.form.requestSubmit();
    }
  });

  els.form?.addEventListener('submit', () => {
    cyFocusSummaryAfterSave = true;
  });

  els.accountName?.addEventListener('change', loadFrequentSummaries);
  els.categoryName?.addEventListener('change', loadFrequentSummaries);
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
    await mutateSettings(`/api/categories/${id}/favorite`, {
      method: 'PUT', headers: jsonHeaders(), body: JSON.stringify({ favorite: Number(category.is_favorite) !== 1 })
    }, Number(category.is_favorite) === 1 ? '已取消常用科目。' : '已加入常用科目。');
  });
}

function handleQuickEntryKindChanged() {
  renderFavoriteCategories();
  void loadFrequentSummaries();
}

function handleFastEntryLedgerRendered() {
  if (!cyFocusSummaryAfterSave || els.saveMessage?.textContent !== '存檔成功') return;
  cyFocusSummaryAfterSave = false;
  setTimeout(() => {
    els.summary.focus();
    loadFrequentSummaries();
  }, 0);
}

async function prepareQuickEntryUi() {
  renderFavoriteCategories();
  await loadFrequentSummaries();
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
