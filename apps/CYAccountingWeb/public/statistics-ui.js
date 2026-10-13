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

function operatingGroups(data) {
  return data.groups.map(group => {
    const subjects = group.subjects.filter(subject => !subject.isTransfer);
    return { ...group, subjects, amount: subjects.reduce((sum, subject) => sum + subject.amount, 0) };
  }).filter(group => group.subjects.length);
}
export function statisticsItems(data, kind, level = 'group', groupKey = '') {
  const groups = operatingGroups(data).filter(group => group.kind === kind && (!groupKey || group.key === groupKey));
  if (level === 'group' && !groupKey) return groups.map(group => ({ key: group.key, name: group.name, amount: group.amount }));
  return groups.flatMap(group => group.subjects.map(subject => ({ key: `${group.key}:${subject.id ?? subject.name}`, name: groupKey ? subject.name : `${group.name}／${subject.name}`, amount: subject.amount })));
}

export function renderStatistics(data, options = {}) {
  const level = options.level || 'group';
  const filters = options.filters || {};
  const tab = options.tab === 'totals' ? 'totals' : 'charts';
  const id = options.id || 'statistics';
  const groups = operatingGroups(data);
  return `<p class="statistics-scope-note">收入、支出、淨利損及圖表均排除指定移轉科目${options.compactScope ? '' : '（設定 → 統計／損益）'}。${data.transferCategoryIds.length ? '' : '目前尚未指定移轉科目。'}</p><div class="statistics-totals">${totalCell('收入', data.operating.income, 'income')}${totalCell('支出', data.operating.expense, 'expense')}${totalCell('淨利損', data.operating.net, data.operating.net < 0 ? 'expense' : 'income')}</div>
    <div class="statistics-view-tabs" role="tablist" aria-label="統計內容">${[['charts', '圖表'], ['totals', '統計']].map(([value, label]) => `<button type="button" role="tab" id="${id}-${value}-tab" aria-controls="${id}-${value}" aria-selected="${tab === value}" tabindex="${tab === value ? 0 : -1}" data-statistics-tab="${value}">${label}</button>`).join('')}</div>
    <div id="${id}-charts" role="tabpanel" aria-labelledby="${id}-charts-tab" ${tab !== 'charts' ? 'hidden' : ''}>
    <div class="statistics-level"><label>圖表依據 <select data-statistics-level><option value="group" ${level === 'group' ? 'selected' : ''}>分類</option><option value="subject" ${level === 'subject' ? 'selected' : ''}>科目</option></select></label></div>
    <div class="statistics-charts">${['income', 'expense'].map(kind => chart(data, kind, level, filters[kind])).join('')}</div></div>
    <div id="${id}-totals" class="statistics-breakdown" role="tabpanel" aria-labelledby="${id}-totals-tab" ${tab !== 'totals' ? 'hidden' : ''}><h3>分類／科目合計</h3>${data.operating.count ? groups.map(group => `<details open><summary><span>${group.kind === 'income' ? '收入' : '支出'}・${esc(group.name)}</span><b>${money(group.amount)}</b></summary><ul>${group.subjects.map(subject => `<li><span>${esc(subject.name)}${subject.isTransfer ? '<small class="statistics-transfer-tag">移轉</small>' : ''}</span><b>${money(subject.amount)}</b></li>`).join('')}</ul></details>`).join('') : '<p class="statistics-empty">此期間尚無記帳資料。</p>'}</div>
    <p class="statistics-note">依現行分類彙總；非移轉記帳共 ${data.operating.count.toLocaleString('zh-TW')} 筆。</p>`;
}
function totalCell(label, amount, kind) {
  return `<div><span>${label}</span><strong class="statistics-${kind}">${money(amount)}</strong></div>`;
}

const textWidth = text => [...String(text)].reduce((width, char) => width + (char.charCodeAt(0) > 255 ? 12 : 6.5), 0);
function wrapLabel(text, width = 84) {
  const lines = [''];
  for (const char of String(text)) {
    if (lines.at(-1) && textWidth(lines.at(-1) + char) > width) lines.push('');
    lines[lines.length - 1] += char;
  }
  return lines;
}

// Both device presentations use the same slices; only the annotation detail differs.
// Outside labels are packed per side, keeping every item readable without tooltips.
export function pieLabelLayout(items, compact = false, height = 280) {
  const sum = items.reduce((total, item) => total + item.amount, 0);
  let offset = 0;
  const labels = items.map((item, index) => {
    const fraction = sum ? item.amount / sum : 0;
    const angle = (offset + fraction / 2) * Math.PI * 2 - Math.PI / 2;
    offset += fraction;
    const lines = compact ? [item.name, `${(fraction * 100).toFixed(1)}%`] : [item.name, money(item.amount), `${(fraction * 100).toFixed(1)}%`];
    const chord = 2 * 75 * Math.sin(Math.min(fraction * Math.PI, Math.PI / 2));
    let inside = fraction >= (compact ? .14 : .18) && Math.max(...lines.map(textWidth)) <= chord * .85 && textWidth(item.name) <= 88;
    if (inside) {
      const width = Math.max(...lines.map(textWidth)), lineHeight = lines.length * 14;
      const x = Math.cos(angle) * 75, y = Math.sin(angle) * 75;
      inside = [-1, 1].every(dx => [-1, 1].every(dy => {
        const cornerX = x + dx * width / 2, cornerY = y + dy * lineHeight / 2;
        const radius = Math.hypot(cornerX, cornerY);
        const delta = Math.atan2(Math.sin(Math.atan2(cornerY, cornerX) - angle), Math.cos(Math.atan2(cornerY, cornerX) - angle));
        return radius >= 41 && radius <= 109 && Math.abs(delta) <= fraction * Math.PI;
      }));
    }
    const displayLines = inside ? lines : [...wrapLabel(item.name), ...(compact ? [] : [money(item.amount)]), `${(fraction * 100).toFixed(1)}%`];
    return { index, angle, inside, side: Math.cos(angle) < 0 ? 'left' : 'right', lines: displayLines, height: displayLines.length * 14 + 4 };
  });
  height = Math.max(height, ...['left', 'right'].map(side => labels.filter(label => !label.inside && label.side === side).reduce((total, label) => total + label.height + 6, 24)));
  for (const side of ['left', 'right']) {
    const outside = labels.filter(label => !label.inside && label.side === side).sort((a, b) => Math.sin(a.angle) - Math.sin(b.angle));
    let edge = 8;
    for (const label of outside) {
      label.y = Math.max(edge + label.height / 2, height / 2 + Math.sin(label.angle) * 111);
      edge = label.y + label.height / 2 + 6;
    }
    edge = height - 8;
    for (const label of outside.toReversed()) {
      label.y = Math.min(label.y, edge - label.height / 2);
      edge = label.y - label.height / 2 - 6;
    }
  }
  for (const label of labels) {
    label.x = label.inside ? 200 + Math.cos(label.angle) * 75 : label.side === 'left' ? 5 : 314;
    if (label.inside) label.y = height / 2 + Math.sin(label.angle) * 75;
  }
  return { width: 400, height, labels };
}
function pie(items, title, kind, drill) {
  let desktop = pieLabelLayout(items), compact = pieLabelLayout(items, true);
  const height = Math.max(desktop.height, compact.height);
  desktop = pieLabelLayout(items, false, height); compact = pieLabelLayout(items, true, height);
  const sum = items.reduce((total, item) => total + item.amount, 0);
  let offset = 0;
  const circumference = Math.PI * 2 * 75;
  const circles = items.map((item, index) => {
    const length = item.amount / sum * circumference;
    const circle = `<circle cx="200" cy="${height / 2}" r="75" fill="none" stroke="${palette[index % palette.length]}" stroke-width="72" stroke-dasharray="${length} ${circumference - length}" stroke-dashoffset="${-offset}" transform="rotate(-90 200 ${height / 2})"/>`;
    offset += length; return circle;
  }).join('');
  const annotations = (layout, variant) => `<g class="statistics-pie-labels statistics-pie-labels-${variant}">${layout.labels.map(label => {
    const item = items[label.index], color = palette[label.index % palette.length];
    const rgb = color.slice(1).match(/../g).map(hex => parseInt(hex, 16) / 255).map(value => value <= .04045 ? value / 12.92 : ((value + .055) / 1.055) ** 2.4);
    const ink = label.inside ? (rgb[0] * .2126 + rgb[1] * .7152 + rgb[2] * .0722 > .179 ? '#000' : '#fff') : '#344054';
    const fromX = 200 + Math.cos(label.angle) * 111, fromY = height / 2 + Math.sin(label.angle) * 111;
    const elbowX = label.side === 'left' ? 91 : 309;
    return `<g ${drill ? `role="button" tabindex="0" aria-label="查看${esc(item.name)}科目" data-statistics-group="${esc(item.key)}" data-statistics-kind="${kind}"` : ''}>${label.inside ? '' : `<polyline points="${fromX},${fromY} ${elbowX},${label.y} ${label.side === 'left' ? 89 : 312},${label.y}" fill="none" stroke="${color}" stroke-width="1"/>`}<text x="${label.x}" y="${label.y - (label.lines.length - 1) * 7}" text-anchor="${label.inside ? 'middle' : 'start'}" fill="${ink}">${label.lines.map((line, index) => `<tspan x="${label.x}" dy="${index ? 14 : 0}">${esc(line)}</tspan>`).join('')}</text></g>`;
  }).join('')}</g>`;
  return `<svg viewBox="0 0 400 ${height}" class="statistics-pie" role="img" aria-label="${title}合計 ${money(sum)}，各項名稱與比率詳見圖上及下方清單">${circles}<text x="200" y="${height / 2 - 4}" text-anchor="middle" class="statistics-pie-center">${title}</text><text x="200" y="${height / 2 + 14}" text-anchor="middle" class="statistics-pie-total">${money(sum)}</text>${annotations(desktop, 'desktop')}${annotations(compact, 'compact')}</svg>`;
}
function chart(data, kind, level, groupKey) {
  const items = statisticsItems(data, kind, level, groupKey).filter(item => item.amount > 0);
  const sum = items.reduce((total, item) => total + item.amount, 0);
  const title = kind === 'income' ? '收入' : '支出';
  const filter = data.groups.find(group => group.key === groupKey);
  return `<section class="statistics-chart"><h3>${title}${filter ? `・${esc(filter.name)} <button type="button" class="statistics-reset" data-statistics-reset="${kind}">全部分類</button>` : ''}</h3>${sum ? `${pie(items, title, kind, level === 'group' && !filter)}<ul class="statistics-legend">${items.map((item, index) => `<li><span class="statistics-swatch" style="background:${palette[index % palette.length]}"></span>${level === 'group' && !filter ? `<button type="button" data-statistics-group="${esc(item.key)}" data-statistics-kind="${kind}">${esc(item.name)}</button>` : `<span>${esc(item.name)}</span>`}<b>${money(item.amount)}</b><small>${(item.amount / sum * 100).toFixed(1)}%</small></li>`).join('')}</ul>` : `<p class="statistics-empty">此期間無${title}資料。</p>`}</section>`;
}

export function statementRows(data, kind, transfer = false) {
  return data.groups.filter(group => group.kind === kind).flatMap(group => {
    const subjects = group.subjects.filter(subject => subject.isTransfer === transfer);
    if (!subjects.length) return [];
    return [{ type: 'group', name: group.name, amount: subjects.reduce((sum, subject) => sum + subject.amount, 0) },
      ...subjects.map(subject => ({ type: 'subject', name: subject.name, group: group.name, groupAmount: subjects.reduce((sum, item) => sum + item.amount, 0), amount: subject.amount }))];
  });
}
export function statementPage(data, options = {}) {
  const operatingRows = Math.max(1, options.operatingRows || Infinity);
  const transferRows = Math.max(1, options.transferRows || Infinity);
  const lists = [statementRows(data, 'income'), statementRows(data, 'expense'), statementRows(data, 'income', true), statementRows(data, 'expense', true)];
  const capacities = [operatingRows, operatingRows, transferRows, transferRows];
  const pages = Math.max(1, ...lists.map((list, i) => Math.ceil(list.length / capacities[i])));
  const page = Math.min(pages - 1, Math.max(0, options.page || 0));
  return { page, pages, lists: lists.map((list, i) => {
    const rows = Number.isFinite(capacities[i]) ? list.slice(page * capacities[i], (page + 1) * capacities[i]) : list;
    if (page > 0 && rows[0]?.type === 'subject') rows.unshift({ type: 'group', name: rows[0].group, amount: rows[0].groupAmount });
    return rows;
  }) };
}
export function renderIncomeStatement(data, options = {}) {
  const paged = statementPage(data, options);
  const cells = (row, right = false) => row ? `<th scope="row" class="statement-${row.type}${right ? ' statement-half-start' : ''}">${esc(row.name)}</th><td class="statement-${row.type}">${money(row.amount)}</td>` : `<td colspan="2" class="statement-blank${right ? ' statement-half-start' : ''}"></td>`;
  const table = (left, right, totals, transfer = false) => `<table class="statement-paired-table ${transfer ? 'statement-transfers' : 'statement-operating'}"><colgroup><col class="statement-name-col"><col class="statement-amount-col"><col class="statement-name-col"><col class="statement-amount-col"></colgroup><thead><tr><th scope="col">${transfer ? '移轉收入' : '收入'}</th><th scope="col">金額</th><th scope="col" class="statement-half-start">${transfer ? '移轉支出' : '支出'}</th><th scope="col">金額</th></tr></thead><tbody>${Array.from({ length: Math.max(left.length, right.length, 1) }, (_, i) => `<tr>${cells(left[i])}${cells(right[i], true)}</tr>`).join('')}</tbody><tfoot><tr><th scope="row">${transfer ? '移轉收入合計' : '總收入'}</th><td>${money(totals.income)}</td><th scope="row" class="statement-half-start">${transfer ? '移轉支出合計' : '總支出'}</th><td>${money(totals.expense)}</td></tr></tfoot></table>`;
  return `<article class="income-statement"><header class="statement-heading"><h3>志遠記帳系統・單月損益表</h3><p class="statement-period">${monthLabel(data.period.start)}　單位：新臺幣元</p></header>
    ${table(paged.lists[0], paged.lists[1], data.operating)}
    <div class="statement-net"><span>本月${data.operating.net < 0 ? '淨損' : '淨利'}</span><strong class="statistics-${data.operating.net < 0 ? 'expense' : 'income'}">${money(Math.abs(data.operating.net))}</strong></div>
    <section class="statement-transfer-section"><h4>帳戶移轉（另列，不計入上述損益）</h4>${data.transfer.count ? table(paged.lists[2], paged.lists[3], data.transfer, true) : '<p class="statement-no-transfers">本月無指定科目的移轉紀錄。</p>'}<p class="statement-transfer-net">移轉收入 ${money(data.transfer.income)}　移轉支出 ${money(data.transfer.expense)}　移轉差額 ${money(data.transfer.net)}</p></section>
    <footer class="statement-footer"><p class="statistics-note">${data.transferCategoryIds.length ? '指定移轉科目不計入損益。' : '尚未指定移轉科目，全部收支計入損益。'}期初餘額不計入損益。合計為整月金額；科目較多時分頁顯示。</p>${paged.pages > 1 ? `<nav class="statement-pagination" aria-label="損益表頁次"><button type="button" class="secondary compact" data-statement-page="${paged.page - 1}" ${paged.page === 0 ? 'disabled' : ''}>上一頁</button><span>${paged.page + 1}／${paged.pages}</span><button type="button" class="secondary compact" data-statement-page="${paged.page + 1}" ${paged.page + 1 === paged.pages ? 'disabled' : ''}>下一頁</button></nav>` : ''}</footer></article>`;
}

function paintIncome(host, data, page = 0, refit = false) {
  const previous = views.get(host);
  let operatingRows = previous?.data === data && !refit ? previous.operatingRows : null;
  let transferRows = previous?.data === data && !refit ? previous.transferRows : null;
  if (!operatingRows) {
    host.innerHTML = renderIncomeStatement(data);
    const article = host.querySelector('.income-statement');
    const operatingBody = host.querySelector('.statement-operating tbody');
    const transferBody = host.querySelector('.statement-transfers tbody');
    const operatingMax = Math.max(statementRows(data, 'income').length, statementRows(data, 'expense').length, 1);
    const transferMax = Math.max(statementRows(data, 'income', true).length, statementRows(data, 'expense', true).length, 1);
    operatingRows = operatingMax; transferRows = transferMax;
    if (article.scrollHeight > host.clientHeight) {
      const operatingHeight = operatingBody.getBoundingClientRect().height;
      const transferHeight = transferBody?.getBoundingClientRect().height || 0;
      const operatingRowHeight = Math.max(18, ...[...operatingBody.rows].map(row => row.getBoundingClientRect().height));
      const transferRowHeight = transferBody ? Math.max(18, ...[...transferBody.rows].map(row => row.getBoundingClientRect().height)) : 0;
      // Reserve page controls and one repeated category heading per table. Measure
      // the longest real row, so page changes keep identical capacities and never skip subjects.
      const fixed = article.scrollHeight - operatingHeight - transferHeight + 34 + operatingRowHeight + transferRowHeight;
      const available = Math.max(0, host.clientHeight - fixed);
      const need = operatingMax * operatingRowHeight + transferMax * transferRowHeight;
      operatingRows = Math.max(1, Math.min(operatingMax, Math.floor(available * operatingMax / need)));
      transferRows = transferRowHeight ? Math.max(1, Math.min(transferMax, Math.floor((available - operatingRows * operatingRowHeight) / transferRowHeight))) : 1;
    }
  }
  host.innerHTML = renderIncomeStatement(data, { operatingRows, transferRows, page });
  views.set(host, { mode: 'income', data, operatingRows, transferRows, page: statementPage(data, { operatingRows, transferRows, page }).page });
  host.removeAttribute('aria-busy');
}
function paint(host, data) {
  const previous = views.get(host) || { level: 'group', filters: {}, tab: 'charts' };
  const view = { ...previous, data, id: host.id, compactScope: host.id !== 'statisticsDialogResults' && Boolean(window.cyIsDesktopInteractionWorkspace?.()) };
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
  dialog.classList.toggle('statistics-income-dialog', dialogMode === 'income');
  dialog.querySelector('.statistics-dialog-tabs').hidden = dialogMode === 'income';
  document.querySelector('#statisticsDialogTitle').textContent = dialogMode === 'income' ? '單月損益表' : '多月統計圖表';
  document.querySelector('#statisticsStartLabel').textContent = dialogMode === 'income' ? '月份' : '起始月份';
  document.querySelector('#statisticsEndField').hidden = dialogMode === 'income';
  document.querySelector('#statisticsEnd').required = dialogMode !== 'income';
  document.querySelector('#statisticsStart').value = month;
  document.querySelector('#statisticsEnd').value = month;
  for (const id of ['statisticsStart', 'statisticsEnd']) document.querySelector('#' + id).dispatchEvent(new Event('change', { bubbles: true }));
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
    if (mode === 'income') paintIncome(host, data);
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
    const page = event.target.closest('[data-statement-page]');
    if (page && view.mode === 'income') { paintIncome(host, view.data, Number(page.dataset.statementPage)); return; }
    const tab = event.target.closest('[data-statistics-tab]');
    if (tab) { view.tab = tab.dataset.statisticsTab; paint(host, view.data); host.querySelector(`[data-statistics-tab="${view.tab}"]`).focus(); return; }
    const group = event.target.closest('[data-statistics-group]');
    const reset = event.target.closest('[data-statistics-reset]');
    if (group) { view.filters[group.dataset.statisticsKind] = group.dataset.statisticsGroup; paint(host, view.data); }
    if (reset) { delete view.filters[reset.dataset.statisticsReset]; paint(host, view.data); }
  });
  document.addEventListener('keydown', event => {
    const tab = event.target.closest('[data-statistics-tab]');
    if (tab && ['ArrowLeft', 'ArrowRight', 'Home', 'End'].includes(event.key)) {
      event.preventDefault();
      const host = tab.closest('.statistics-results');
      const next = event.key === 'Home' ? 'charts' : event.key === 'End' ? 'totals' : tab.dataset.statisticsTab === 'charts' ? 'totals' : 'charts';
      host.querySelector(`[data-statistics-tab="${next}"]`).click();
    }
    if (event.target.closest('svg [data-statistics-group]') && ['Enter', ' '].includes(event.key)) { event.preventDefault(); event.target.closest('[data-statistics-group]').dispatchEvent(new MouseEvent('click', { bubbles: true })); }
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
  window.addEventListener('resize', () => requestAnimationFrame(() => {
    void refresh();
    const host = document.querySelector('#statisticsDialogResults');
    const view = views.get(host);
    if (document.querySelector('#statisticsDialog').open && view?.mode === 'income') paintIncome(host, view.data, view.page, true);
  }), { passive: true });
  if (window.cyaccCoreReady) void refresh();
}
if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', start, { once: true });
else start();
