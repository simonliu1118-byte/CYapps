const views = new WeakMap();
const money = value => '$' + Number(value || 0).toLocaleString('zh-TW');
const esc = value => String(value ?? '').replace(/[&<>"']/g, char => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[char]));
const monthLabel = month => String(month).replace('-', '／');
const palette = ['#407fb0', '#e4a64e', '#6b9d7b', '#bf6d77', '#8c78ac', '#67a5a7', '#ae875d', '#87959e'];
let sidebarData = null;
let sidebarRequest = 0;
let sidebarPendingMonth = '';
let dialogRequest = 0;
let dialogMode = 'period';
let settingsRequest = 0;
let settingsSaving = false;
let transferSettingsIds = [];

export function statisticsItems(data, kind, level = 'group', groupKey = '') {
  const groups = data.groups.filter(group => group.kind === kind && (!groupKey || group.key === groupKey));
  if (level === 'group' && !groupKey) return groups.map(group => ({ key: group.key, name: group.name, amount: group.amount }));
  return groups.flatMap(group => group.subjects.map(subject => ({ key: `${group.key}:${subject.id ?? subject.name}`, name: groupKey ? subject.name : `${group.name}／${subject.name}`, amount: subject.amount })));
}

export function renderStatistics(data, options = {}) {
  const level = options.level || 'group';
  const filters = options.filters || {};
  return `<div class="statistics-totals">${totalCell('收入', data.cash.income, 'income')}${totalCell('支出', data.cash.expense, 'expense')}${totalCell('淨利損', data.cash.net, data.cash.net < 0 ? 'expense' : 'income')}</div>
    <div class="statistics-level"><label>圖表依據 <select data-statistics-level><option value="group" ${level === 'group' ? 'selected' : ''}>分類</option><option value="subject" ${level === 'subject' ? 'selected' : ''}>科目</option></select></label></div>
    <div class="statistics-charts">${['income', 'expense'].map(kind => chart(data, kind, level, filters[kind])).join('')}</div>
    <div class="statistics-breakdown"><h3>分類／科目合計</h3>${data.cash.count ? data.groups.map(group => `<details><summary><span>${group.kind === 'income' ? '收入' : '支出'}・${esc(group.name)}</span><b>${money(group.amount)}</b></summary><ul>${group.subjects.map(subject => `<li><span>${esc(subject.name)}${subject.isTransfer ? '<small class="statistics-transfer-tag">移轉</small>' : ''}</span><b>${money(subject.amount)}</b></li>`).join('')}</ul></details>`).join('') : '<p class="statistics-empty">此期間尚無記帳資料。</p>'}</div>
    <p class="statistics-note">全帳戶收支，包含移轉；依現行分類彙總。共 ${data.cash.count.toLocaleString('zh-TW')} 筆。</p>`;
}
function totalCell(label, amount, kind) {
  return `<div><span>${label}</span><strong class="statistics-${kind}">${money(amount)}</strong></div>`;
}
function chart(data, kind, level, groupKey) {
  const items = statisticsItems(data, kind, level, groupKey).filter(item => item.amount > 0);
  const sum = items.reduce((total, item) => total + item.amount, 0);
  const title = kind === 'income' ? '收入' : '支出';
  const filter = data.groups.find(group => group.key === groupKey);
  let offset = 0;
  const circles = items.map((item, index) => {
    const percent = item.amount / sum * 100;
    const circle = `<circle cx="60" cy="60" r="44" fill="none" stroke="${palette[index % palette.length]}" stroke-width="28" pathLength="100" stroke-dasharray="${percent} ${100 - percent}" stroke-dashoffset="${-offset}" transform="rotate(-90 60 60)"><title>${esc(item.name)} ${money(item.amount)} ${(percent).toFixed(1)}%</title></circle>`;
    offset += percent;
    return circle;
  }).join('');
  return `<section class="statistics-chart"><h3>${title}${filter ? `・${esc(filter.name)} <button type="button" class="statistics-reset" data-statistics-reset="${kind}">全部分類</button>` : ''}</h3>${sum ? `<svg viewBox="0 0 120 120" class="statistics-pie" role="img" aria-label="${title}組成圓餅圖"><title>${title}合計 ${money(sum)}</title>${circles}<text x="60" y="58" text-anchor="middle">${title}</text><text x="60" y="72" text-anchor="middle" class="statistics-pie-total">${money(sum)}</text></svg><ul class="statistics-legend">${items.map((item, index) => `<li><span class="statistics-swatch" style="background:${palette[index % palette.length]}"></span>${level === 'group' && !filter ? `<button type="button" data-statistics-group="${esc(item.key)}" data-statistics-kind="${kind}">${esc(item.name)}</button>` : `<span>${esc(item.name)}</span>`}<b>${money(item.amount)}</b><small>${(item.amount / sum * 100).toFixed(1)}%</small></li>`).join('')}</ul>` : `<p class="statistics-empty">此期間無${title}資料。</p>`}</section>`;
}

export function renderIncomeStatement(data) {
  const rows = (kind, transfer) => data.groups.filter(group => group.kind === kind).map(group => {
    const subjects = group.subjects.filter(subject => subject.isTransfer === transfer);
    if (!subjects.length) return '';
    const sum = subjects.reduce((total, subject) => total + subject.amount, 0);
    return `<tr class="statement-group"><th scope="row">${esc(group.name)}</th><td>${money(sum)}</td></tr>${subjects.map(subject => `<tr class="statement-subject"><th scope="row">${esc(subject.name)}</th><td>${money(subject.amount)}</td></tr>`).join('')}`;
  }).join('') || '<tr><td colspan="2" class="statistics-empty">無資料</td></tr>';
  return `<article class="income-statement"><h3>志遠記帳系統・單月損益表</h3><p class="statement-period">${monthLabel(data.period.start)}　單位：新臺幣元</p>
    <table><thead><tr><th scope="col">分類／科目</th><th scope="col">金額</th></tr></thead><tbody>
    <tr class="statement-section"><th colspan="2">收入</th></tr>${rows('income', false)}
    <tr class="statement-subtotal"><th scope="row">收入合計</th><td>${money(data.operating.income)}</td></tr>
    <tr class="statement-section"><th colspan="2">支出</th></tr>${rows('expense', false)}
    <tr class="statement-subtotal"><th scope="row">支出合計</th><td>${money(data.operating.expense)}</td></tr>
    <tr class="statement-net"><th scope="row">本月${data.operating.net < 0 ? '淨損' : '淨利'}</th><td class="statistics-${data.operating.net < 0 ? 'expense' : 'income'}">${money(data.operating.net)}</td></tr>
    </tbody></table>
    <h4>帳戶移轉（另列，不計入上述損益）</h4><table><tbody><tr class="statement-section"><th colspan="2">移轉收入</th></tr>${rows('income', true)}<tr class="statement-subtotal"><th scope="row">移轉收入合計</th><td>${money(data.transfer.income)}</td></tr><tr class="statement-section"><th colspan="2">移轉支出</th></tr>${rows('expense', true)}<tr class="statement-subtotal"><th scope="row">移轉支出合計</th><td>${money(data.transfer.expense)}</td></tr><tr class="statement-subtotal"><th scope="row">移轉差額</th><td>${money(data.transfer.net)}</td></tr></tbody></table>
    <p class="statistics-note">移轉由「設定 → 統計／損益」指定；${data.transferCategoryIds.length ? '依指定科目排除移轉。' : '目前尚未指定移轉科目，全部收支均計入損益。'}期初餘額不計入損益。</p>
    </article>`;
}

function paint(host, data) {
  const previous = views.get(host) || { level: 'group', filters: {} };
  const view = { ...previous, data };
  views.set(host, view);
  host.innerHTML = (host.id === 'statisticsDialogResults' ? `<p class="statistics-range">${monthLabel(data.period.start)} ～ ${monthLabel(data.period.end)}</p>` : '') + renderStatistics(data, view);
  host.removeAttribute('aria-busy');
}
function currentMonth() { return document.querySelector('#monthFilter')?.value || new Date().toISOString().slice(0, 7); }
function sidebarVisible() {
  const host = document.querySelector('#statisticsCard');
  return Boolean(host?.getClientRects().length);
}
function busy(host) {
  host.setAttribute('aria-busy', 'true');
  host.innerHTML = '<p class="statistics-empty"><span class="statistics-spinner" aria-hidden="true"></span>載入統計中…</p>';
}
function failure(host, error) {
  host.removeAttribute('aria-busy');
  host.innerHTML = `<p class="statistics-error">${esc(error.message)}</p><button type="button" class="secondary" data-statistics-retry>重新載入</button>`;
}
async function query(start, end) {
  return window.cyaccApi(`/api/statistics?start=${encodeURIComponent(start)}&end=${encodeURIComponent(end)}`);
}
async function refresh(force = false) {
  const month = currentMonth();
  document.querySelector('#statisticsMonthLabel').textContent = monthLabel(month);
  document.querySelector('#statisticsMobileMonth').value = month;
  if (force) { sidebarData = null; sidebarRequest += 1; sidebarPendingMonth = ''; }
  if (!sidebarVisible() || sidebarData?.period.start === month || sidebarPendingMonth === month) return;
  const host = document.querySelector('#statisticsSidebarResults');
  const request = ++sidebarRequest;
  sidebarPendingMonth = month;
  busy(host);
  try {
    const data = await query(month, month);
    if (request !== sidebarRequest || currentMonth() !== month) return;
    if (sidebarData?.period.start !== month) views.delete(host);
    sidebarData = data;
    paint(host, data);
  } catch (error) {
    if (request === sidebarRequest) failure(host, error);
  } finally {
    if (request === sidebarRequest) sidebarPendingMonth = '';
  }
}
async function openReport(mode) {
  const dialog = document.querySelector('#statisticsDialog');
  const month = currentMonth();
  dialogMode = mode === 'income' ? 'income' : 'period';
  document.querySelector('#statisticsDialogTitle').textContent = dialogMode === 'income' ? '單月損益表' : '多月統計圖表';
  document.querySelector('#statisticsStartLabel').textContent = dialogMode === 'income' ? '月份' : '起始月份';
  document.querySelector('#statisticsEndField').hidden = dialogMode === 'income';
  document.querySelector('#statisticsEnd').required = dialogMode !== 'income';
  document.querySelector('#statisticsStart').value = month;
  document.querySelector('#statisticsEnd').value = month;
  dialog.querySelectorAll('[data-statistics-open]').forEach(button => {
    button.classList.toggle('active', button.dataset.statisticsOpen === dialogMode);
    button.setAttribute('aria-pressed', String(button.dataset.statisticsOpen === dialogMode));
  });
  views.delete(document.querySelector('#statisticsDialogResults'));
  if (!dialog.open) dialog.showModal();
  await refreshReport();
}
async function refreshReport() {
  const host = document.querySelector('#statisticsDialogResults');
  const start = document.querySelector('#statisticsStart').value;
  const end = dialogMode === 'income' ? start : document.querySelector('#statisticsEnd').value;
  const request = ++dialogRequest;
  const mode = dialogMode;
  views.delete(host);
  busy(host);
  try {
    const data = await query(start, end);
    if (request !== dialogRequest || !document.querySelector('#statisticsDialog').open) return;
    if (mode === 'income') { host.innerHTML = renderIncomeStatement(data); host.removeAttribute('aria-busy'); }
    else paint(host, data);
  } catch (error) { if (request === dialogRequest) failure(host, error); }
}
async function loadSettings() {
  if (settingsSaving) return;
  const host = document.querySelector('#statisticsTransferSettings');
  const message = document.querySelector('#statisticsSettingsMessage');
  const request = ++settingsRequest;
  message.textContent = '';
  busy(host);
  try {
    const data = await window.cyaccApi('/api/settings/statistics');
    if (request !== settingsRequest) return;
    transferSettingsIds = data.transferCategoryIds;
    const selected = new Set(transferSettingsIds);
    const readOnly = document.body.dataset.cyaccReadOnly === 'true';
    host.innerHTML = `<form id="statisticsTransferForm">${['income', 'expense'].map(kind => `<fieldset><legend>${kind === 'income' ? '收入' : '支出'}移轉科目</legend>${data.categories.filter(category => category.kind === kind).map(category => `<label class="statistics-transfer-choice"><input type="checkbox" name="transfer" value="${category.id}" ${selected.has(category.id) ? 'checked' : ''} ${readOnly ? 'disabled' : ''}><span>${esc(category.group_name)}／${esc(category.name)}</span></label>`).join('') || '<p class="hint">尚無科目</p>'}</fieldset>`).join('')}<button type="submit" class="primary" ${readOnly ? 'disabled' : ''}>儲存移轉科目</button></form>`;
    host.removeAttribute('aria-busy');
  } catch (error) { if (request === settingsRequest) failure(host, error); }
}
async function saveSettings(form) {
  if (settingsSaving) return;
  const message = document.querySelector('#statisticsSettingsMessage');
  const inputs = [...form.querySelectorAll('input')];
  const ids = inputs.filter(input => input.checked).map(input => Number(input.value));
  const previous = new Set(transferSettingsIds);
  const controls = [...form.elements];
  settingsSaving = true;
  // The report-setting change is reversible: reflect it immediately, restore the canonical setting on failure.
  message.textContent = '已更新，正在儲存…';
  const cached = sidebarData;
  if (cached) { sidebarData = null; }
  controls.forEach(control => { control.disabled = true; });
  try {
    await window.cyaccApi('/api/settings/statistics', { method: 'PUT', headers: { 'content-type': 'application/json' }, body: JSON.stringify({ transferCategoryIds: ids }) });
    transferSettingsIds = ids;
    message.textContent = '移轉科目已儲存。';
    await refresh(true);
  } catch (error) {
    sidebarData = cached;
    inputs.forEach(input => { input.checked = previous.has(Number(input.value)); });
    message.textContent = error.message + '；已還原儲存前的設定。';
  } finally { settingsSaving = false; controls.forEach(control => { control.disabled = false; }); }
}
function start() {
  window.cyStatistics = { refresh, loadSettings };
  document.addEventListener('click', event => {
    const open = event.target.closest('[data-statistics-open]');
    if (open) { void openReport(open.dataset.statisticsOpen); return; }
    const host = event.target.closest('.statistics-results');
    if (!host) return;
    const retry = event.target.closest('[data-statistics-retry]');
    if (retry) { void (host.id === 'statisticsTransferSettings' ? loadSettings() : host.id === 'statisticsDialogResults' ? refreshReport() : refresh(true)); return; }
    const view = views.get(host);
    if (!view) return;
    const group = event.target.closest('[data-statistics-group]');
    const reset = event.target.closest('[data-statistics-reset]');
    if (group) { view.filters[group.dataset.statisticsKind] = group.dataset.statisticsGroup; paint(host, view.data); }
    if (reset) { delete view.filters[reset.dataset.statisticsReset]; paint(host, view.data); }
  });
  document.addEventListener('change', event => {
    if (event.target.matches('[data-statistics-level]')) {
      const host = event.target.closest('.statistics-results');
      const view = views.get(host);
      view.level = event.target.value; view.filters = {}; paint(host, view.data);
    }
  });
  document.querySelector('#statisticsPeriodForm').addEventListener('submit', event => { event.preventDefault(); void refreshReport(); });
  document.querySelector('#statisticsTransferSettings').addEventListener('submit', event => { event.preventDefault(); void saveSettings(event.target); });
  document.querySelector('#statisticsMobileMonth').addEventListener('change', event => {
    if (!event.target.value) return;
    const input = document.querySelector('#monthFilter');
    input.value = event.target.value;
    input.dispatchEvent(new Event('change', { bubbles: true }));
  });
  document.querySelector('#monthFilter').addEventListener('change', () => {
    sidebarData = null; sidebarRequest += 1; sidebarPendingMonth = '';
    busy(document.querySelector('#statisticsSidebarResults'));
    // Statistics query is independent of the ledger's loading success.
    void refresh();
  });
  window.addEventListener('cyacc:transactions-loaded', () => void refresh(true));
  window.addEventListener('cyacc:master-data-changed', () => void refresh(true));
  window.addEventListener('cyacc:core-ready', () => void refresh());
  window.addEventListener('resize', () => requestAnimationFrame(() => void refresh()), { passive: true });
  if (window.cyaccCoreReady) void refresh();
}
if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', start, { once: true });
else start();
