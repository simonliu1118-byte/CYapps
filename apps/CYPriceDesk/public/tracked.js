// 追蹤商品: list (per-platform comparison, 最需要注意 ordering), detail (three platform cards, trend chart, candidates,
// manual comparisons, price history), add / edit, 調整售價 and "check now". All data comes from /api/tracked (audited on the server).
import {displayTime, money} from './core.js';
import {sanitizeAttributes} from './catalog-core.js';
import {PLATFORMS, PLATFORM_COLOR, PLATFORM_LABEL, byAttention, needsAttention} from './compare.js';
import {$, api, esc, showError, tag, toast} from './ui.js';
import {createChart} from './chart.js';
import {candidateTable, bindJudging} from './candidates.js';
import {loadPending} from './pending.js';

const STATUS = {ok: '完成', no_match: '無符合結果', no_price: '無價格', rate_limited: '查詢間隔未到，1 小時後重試', blocked_cooldown: '冷卻中，1 小時後重試', blocked: '被限制，1 小時後重試', provider_refused: '被拒絕，1 小時後重試', network_error: '連線失敗，1 小時後重試'};
const TABS = [['pending', '待確認'], ['same', '同規格'], ['other', '其他包裝'], ['unrelated', '不相關'], ['excluded', '不是同一商品']];

let products = [], filter = 'all', detail = null, candTab = 'pending', chartState = {}, editing = null, priceFor = null, manualEditing = null, deleting = null, busy = false;

const attrText = attrs => Object.entries(attrs || {}).map(([k, v]) => `${esc(k)}：${esc(v)}`).join('、') || '—';
const attrLines = attrs => Object.entries(attrs || {}).map(([k, v]) => `${k}=${v}`).join('\n');
const packText = p => `${p.packageQuantity} ${esc(p.packageUnit)}`;
const when = value => value ? esc(displayTime(value)) : '—';
const stateChip = p => p.active ? '<span class="chip success">啟用</span>' : '<span class="chip">停用</span>';
const gapHtml = c => c.gap === null ? '<span class="muted">—</span>' : `<span class="gap ${c.warn ? 'warn' : 'ok'}">${c.gap > 0 ? '+' : ''}${(c.gap * 100).toFixed(1)}%</span>`;
const money0 = value => value === null ? '—' : money(value);

// ---- list ----
async function loadList() {
  try { products = (await api('GET', '/api/tracked')).products; renderList(); }
  catch (error) { $('tr-list').innerHTML = `<div class="notice error">${esc(error.message)}</div>`; }
}
function platformCell(p, platform) {
  const c = p.platforms[platform];
  if (c.min === null && c.mine === null) return '<span class="muted">—</span>';
  return `<div class="pcell">${c.mine === null ? '' : `<span>我的 <b>${money(c.mine)}</b></span>`}<span class="${c.mine === null ? '' : 'muted'}">市場最低 ${money0(c.min)}</span>${c.mine === null ? '' : gapHtml(c)}</div>`;
}
function renderList() {
  const q = $('tr-search').value.trim().toLocaleLowerCase(), sort = $('tr-sort').value;
  const keep = {all: () => true, attention: p => needsAttention(p.platforms), pending: p => p.pendingCount > 0, inactive: p => !p.active}[filter];
  const rows = products.filter(p => (!q || `${p.name} ${p.model}`.toLocaleLowerCase().includes(q)) && keep(p))
    .sort(sort === 'attention' ? byAttention : sort === 'name' ? (a, b) => a.name.localeCompare(b.name, 'zh-TW') : (a, b) => (b.lastRunAt ?? 0) - (a.lastRunAt ?? 0));
  if (!rows.length) {
    $('tr-list').innerHTML = `<div class="empty"><strong>${products.length ? '沒有符合條件的追蹤商品' : '尚無追蹤商品'}</strong><p>${products.length ? '換個搜尋詞或篩選條件。' : '在「查價」頁加入追蹤，或按右上角「新增追蹤商品」。'}</p></div>`;
    return;
  }
  $('tr-list').innerHTML = `<div class="table-wrap"><table class="data-table wide"><thead><tr><th>商品</th>${PLATFORMS.map(p => `<th>${tag(p)}</th>`).join('')}<th class="num">待確認</th><th>上次查價</th><th>下次查價</th></tr></thead><tbody>${rows.map(p => `<tr class="clickable" data-id="${esc(p.id)}" tabindex="0">
    <td><strong>${esc(p.name)}</strong> ${p.active ? '' : '<span class="chip">停用</span>'}<small>型號 ${esc(p.model)} · ${packText(p)}${Object.keys(p.attributes).length ? ` · ${attrText(p.attributes)}` : ''}</small></td>
    ${PLATFORMS.map(pl => `<td>${platformCell(p, pl)}</td>`).join('')}
    <td class="num">${p.pendingCount ? `<span class="chip warning">${p.pendingCount}</span>` : '0'}</td>
    <td>${when(p.lastRunAt)}</td><td>${p.active ? when(p.nextRunAt) : '—'}</td></tr>`).join('')}</tbody></table></div>`;
}
$('tr-search').addEventListener('input', renderList);
$('tr-sort').addEventListener('change', renderList);
for (const b of document.querySelectorAll('[data-tr-filter]')) b.onclick = () => {
  filter = b.dataset.trFilter;
  document.querySelectorAll('[data-tr-filter]').forEach(x => x.classList.toggle('selected', x === b));
  renderList();
};
const openFromRow = e => { const row = e.target.closest('tr[data-id]'); if (row) location.hash = `#tracked/${row.dataset.id}`; };
$('tr-list').addEventListener('click', openFromRow);
$('tr-list').addEventListener('keydown', e => { if (e.key === 'Enter') openFromRow(e); });

// ---- routing: #tracked is the list, #tracked/<id> a product ----
function route() {
  const match = location.hash.match(/^#tracked\/(.+)$/);
  if (match) openDetail(decodeURIComponent(match[1]));
  else if (location.hash === '#tracked') showList();
}
function showList() {
  detail = null;
  $('tracked-detail-view').hidden = true;
  $('tracked-list-view').hidden = false;
  loadList();
}
window.addEventListener('hashchange', route);
route();

// ---- detail ----
async function openDetail(id) {
  try { detail = await api('GET', `/api/tracked/${id}`); }
  catch (error) { toast(error.message); location.hash = '#tracked'; return; }
  chartState = {};
  candTab = detail.candidates.some(c => c.group === 'pending') ? 'pending' : 'same';
  $('tracked-list-view').hidden = true;
  $('tracked-detail-view').hidden = false;
  renderDetail();
}
async function refreshDetail() { detail = await api('GET', `/api/tracked/${detail.product.id}`); renderDetail(); }

function platformCard(platform) {
  const {product: p, platforms} = detail, c = platforms[platform];
  return `<div class="pcard" style="--c:${PLATFORM_COLOR[platform]}">
    <div class="pcard-head"><strong>${PLATFORM_LABEL[platform]}${platform === 'momo' ? '（MO店+）' : ''}</strong>${p.myUrls[platform] ? `<a href="${esc(p.myUrls[platform])}" target="_blank" rel="noopener noreferrer">我的商品頁</a>` : ''}</div>
    <div class="pcard-mine"><span>我的售價</span><b>${c.mine === null ? '未設定' : money(c.mine)}</b><button class="btn small" type="button" data-price="${platform}">調整售價</button></div>
    <dl class="pcard-stats"><div><dt>同規格最低</dt><dd>${money0(c.min)}</dd></div><div><dt>中位數</dt><dd>${money0(c.median)}</dd></div><div><dt>差距</dt><dd>${gapHtml(c)}</dd></div><div><dt>計入筆數</dt><dd>${c.count}</dd></div></dl>
    ${c.mine === null ? '<p class="hint">未設定售價，不與此平台比價。</p>' : ''}</div>`;
}

const manualStatus = m => ['shopee', 'momo', 'coupang'].includes(m.platform) ? (m.counted ? '<span class="chip success">計入</span>' : '<span class="chip">已過期，不計入</span>') : '<span class="chip">僅列出</span>';

function renderDetail() {
  const {product: p, candidates, manual, priceChanges, lastRun} = detail;
  const groupOf = g => candidates.filter(c => c.group === g);
  const keywords = [`共用：${esc(p.keyword)}`, p.momoKeyword && `momo：${esc(p.momoKeyword)}`, p.feebeeKeyword && `飛比：${esc(p.feebeeKeyword)}`].filter(Boolean).join('　');
  const shown = groupOf(candTab);
  $('tracked-detail-view').innerHTML = `
  <div class="detail-head"><div class="toolbar-group"><a class="back" href="#tracked">← 追蹤商品</a><h2>${esc(p.name)}</h2>${stateChip(p)}</div>
    <div class="toolbar-group"><button class="btn" id="tr-edit" type="button">編輯</button><button class="btn primary" id="tr-run" type="button">立即查價</button></div></div>
  <section class="panel"><div class="kv">
    <div><span>型號</span>${esc(p.model)}</div><div><span>規格屬性</span>${attrText(p.attributes)}</div><div><span>包裝</span>${packText(p)}</div><div><span>CY Web 品號</span>${esc(p.cywebItemNo) || '—'}</div>
    <div class="wide2"><span>查詢關鍵字</span>${keywords}</div>
    <div><span>上次查價</span>${when(p.lastRunAt)}${lastRun ? `<small class="muted run-status">momo：${esc(STATUS[lastRun.momoStatus] || lastRun.momoStatus)}；飛比：${esc(STATUS[lastRun.feebeeStatus] || lastRun.feebeeStatus)}</small>` : ''}</div>
    <div><span>下次查價</span>${p.active ? when(p.nextRunAt) : '已停用'}</div></div></section>
  <section class="pcards">${PLATFORMS.map(platformCard).join('')}</section>
  <section class="panel"><div class="panel-head"><h2>價格走勢 <small>市場為同規格商品；虛線為中位數，粗線為我的售價</small></h2></div><div id="chart-box"></div></section>
  <section class="panel"><div class="panel-head"><h2>候選商品</h2><div class="segmented" role="group" aria-label="候選分類">${TABS.map(([k, label]) => `<button type="button" data-tab="${k}" class="${candTab === k ? 'selected' : ''}">${label} ${groupOf(k).length}</button>`).join('')}</div></div>
    ${shown.length ? candidateTable(shown, p.id) : '<p class="muted">這個分類目前沒有商品。</p>'}</section>
  <section class="panel"><div class="panel-head"><h2>手動比價 <small>平台為蝦皮、momo、酷澎者，30 天內計入該平台比較</small></h2><button class="btn" id="manual-add" type="button">新增手動比價</button></div>
    ${manual.length ? `<div class="table-wrap"><table class="data-table wide"><thead><tr><th>平台</th><th>賣家</th><th class="num">價格</th><th>查價日期</th><th>狀態</th><th>備註</th><th>操作</th></tr></thead><tbody>${manual.map(m => `<tr class="${m.counted || !['shopee', 'momo', 'coupang'].includes(m.platform) ? '' : 'muted-row'}">
      <td>${m.platform === 'other' ? '<span class="chip">其他</span>' : tag(m.platform)}</td><td>${m.url ? `<a href="${esc(m.url)}" target="_blank" rel="noopener noreferrer">${esc(m.seller) || '連結'}</a>` : esc(m.seller) || '—'}</td>
      <td class="num">${money(m.price)}</td><td>${esc(m.checkedAt)}</td><td>${manualStatus(m)}</td><td>${esc(m.note) || '—'}</td>
      <td><div class="row-actions"><button class="btn small" type="button" data-manual-edit="${esc(m.id)}">修改</button><button class="btn small" type="button" data-manual-del="${esc(m.id)}">刪除</button></div></td></tr>`).join('')}</tbody></table></div>` : '<p class="muted">尚無手動比價。看到其他賣場的價格，可以手動記一筆。</p>'}</section>
  <section class="panel"><div class="panel-head"><h2>售價調整紀錄</h2></div>
    ${priceChanges.length ? `<div class="table-wrap"><table class="data-table wide"><thead><tr><th>時間</th><th>平台</th><th>操作者</th><th class="num">改前</th><th class="num">改後</th><th>備註</th></tr></thead><tbody>${priceChanges.map(c => `<tr><td>${when(c.at)}</td><td>${tag(c.platform)}</td><td>${esc(c.actorName)}</td><td class="num">${money0(c.oldPrice)}</td><td class="num">${c.newPrice === null ? '停止比價' : money(c.newPrice)}</td><td>${esc(c.note) || '—'}</td></tr>`).join('')}</tbody></table></div>` : '<p class="muted">尚無售價紀錄。</p>'}</section>`;
  createChart($('chart-box'), detail.chart, detail.today, chartState);
}

bindJudging($('tracked-detail-view'), async () => { await refreshDetail(); await loadPending(); });
$('tracked-detail-view').addEventListener('click', async e => {
  const button = e.target.closest('button');
  if (!button || busy) return;
  try {
    if (button.id === 'tr-edit') openEdit(detail.product);
    else if (button.id === 'tr-run') await runNow(button);
    else if (button.dataset.price) openPrice(button.dataset.price);
    else if (button.dataset.tab) { candTab = button.dataset.tab; renderDetail(); }
    else if (button.id === 'manual-add') openManual(null);
    else if (button.dataset.manualEdit) openManual(detail.manual.find(m => m.id === button.dataset.manualEdit));
    else if (button.dataset.manualDel) {
      deleting = detail.manual.find(m => m.id === button.dataset.manualDel);
      $('delete-description').textContent = `${PLATFORM_LABEL[deleting.platform]} · ${deleting.seller || '（未填賣家）'} · ${money(deleting.price)}（${deleting.checkedAt}）`;
      $('delete-dialog').showModal();
    }
  } catch (error) { toast(error.message); }
});
async function runNow(button) {
  busy = true; button.disabled = true; button.textContent = '查價中…';
  try {
    const r = await api('POST', `/api/tracked/${detail.product.id}/run`);
    await refreshDetail();
    await loadPending();
    toast(`查價完成。momo：${STATUS[r.momoStatus] || r.momoStatus}；飛比：${STATUS[r.feebeeStatus] || r.feebeeStatus}`);
  } finally { busy = false; if ($('tr-run')) { $('tr-run').disabled = false; $('tr-run').textContent = '立即查價'; } }
}

// ---- add / edit ----
function parseAttributes(text) {
  const attrs = {};
  for (const line of text.split(/\r?\n/).slice(0, 12)) {
    if (!line.trim()) continue;
    const at = line.indexOf('=');
    if (at < 1 || !line.slice(at + 1).trim()) throw new Error('規格屬性請使用「名稱=內容」，每行一項');
    attrs[line.slice(0, at).trim()] = line.slice(at + 1).trim();
  }
  return sanitizeAttributes(attrs);
}
function openForm(p, prefill = {}) {
  editing = p;
  const f = $('tracked-form').elements;
  $('tracked-form').reset();
  $('tracked-error').hidden = true;
  $('tracked-title').textContent = p ? '編輯追蹤商品' : '新增追蹤商品';
  $('tr-price-hint').hidden = !p;
  for (const platform of PLATFORMS) { f[`price_${platform}`].disabled = Boolean(p); f[`price_${platform}`].value = p?.myPrices[platform] ?? ''; f[`url_${platform}`].value = p?.myUrls[platform] ?? ''; }
  f.name.value = p?.name ?? prefill.name ?? '';
  f.model.value = p?.model ?? prefill.model ?? '';
  f.cywebItemNo.value = p?.cywebItemNo ?? '';
  f.attributes.value = attrLines(p?.attributes);
  f.packageQuantity.value = p?.packageQuantity ?? prefill.quantity ?? '';
  f.packageUnit.value = p?.packageUnit ?? prefill.unit ?? '';
  f.keyword.value = p?.keyword ?? prefill.keyword ?? '';
  f.momoKeyword.value = p?.momoKeyword ?? '';
  f.feebeeKeyword.value = p?.feebeeKeyword ?? '';
  f.active.checked = p?.active ?? true;
  document.querySelector('#tracked-form .advanced').open = Boolean(p?.momoKeyword || p?.feebeeKeyword);
  $('tracked-dialog').showModal();
}
const openEdit = p => openForm(p);
export const openTrackedForm = prefill => openForm(null, prefill);   // used by 查價 → 加入追蹤
$('tr-add').onclick = () => openForm(null);
$('tracked-form').addEventListener('submit', async e => {
  e.preventDefault();
  $('tracked-error').hidden = true;
  const f = e.target.elements;
  try {
    const body = {name: f.name.value, model: f.model.value, cywebItemNo: f.cywebItemNo.value, attributes: parseAttributes(f.attributes.value), packageQuantity: Number(f.packageQuantity.value), packageUnit: f.packageUnit.value,
      keyword: f.keyword.value, momoKeyword: f.momoKeyword.value, feebeeKeyword: f.feebeeKeyword.value, active: f.active.checked,
      myPrices: Object.fromEntries(PLATFORMS.map(p => [p, f[`price_${p}`].value])), myUrls: Object.fromEntries(PLATFORMS.map(p => [p, f[`url_${p}`].value]))};
    if (editing) { await api('PUT', `/api/tracked/${editing.id}`, {...body, revision: editing.revision}); await refreshDetail(); }
    else { await api('POST', '/api/tracked', body); await loadList(); }
    $('tracked-dialog').close();
    toast(editing ? '已更新' : '已新增追蹤商品，系統會自動安排查價時間');
  } catch (error) {
    showError('tracked-error', error);
    if (error.status === 409 && detail) await refreshDetail().catch(() => {});
  }
});

// ---- 調整售價 (one platform) ----
function openPrice(platform) {
  priceFor = platform;
  $('price-form').reset();
  $('price-error').hidden = true;
  $('price-platform').innerHTML = `${tag(platform)} 目前售價 ${money0(detail.product.myPrices[platform])}`;
  $('price-form').elements.price.value = detail.product.myPrices[platform] ?? '';
  $('price-dialog').showModal();
}
$('price-form').addEventListener('submit', async e => {
  e.preventDefault();
  $('price-error').hidden = true;
  const f = e.target.elements;
  try {
    await api('PUT', `/api/tracked/${detail.product.id}/price`, {platform: priceFor, price: f.price.value, note: f.note.value, revision: detail.product.revision});
    await refreshDetail();
    $('price-dialog').close();
    toast('售價已更新');
  } catch (error) { showError('price-error', error); if (error.status === 409) await refreshDetail().catch(() => {}); }
});

// ---- manual comparisons ----
function openManual(m) {
  manualEditing = m;
  const f = $('manual-form').elements;
  $('manual-form').reset();
  $('manual-error').hidden = true;
  $('manual-title').textContent = m ? '修改手動比價' : '新增手動比價';
  f.checkedAt.max = detail.today;
  f.platform.value = m?.platform ?? 'shopee';
  f.seller.value = m?.seller ?? '';
  f.price.value = m?.price ?? '';
  f.checkedAt.value = m?.checkedAt ?? detail.today;
  f.url.value = m?.url ?? '';
  f.note.value = m?.note ?? '';
  $('manual-dialog').showModal();
}
$('manual-form').addEventListener('submit', async e => {
  e.preventDefault();
  $('manual-error').hidden = true;
  const f = e.target.elements;
  const body = {platform: f.platform.value, seller: f.seller.value, price: f.price.value, checkedAt: f.checkedAt.value, url: f.url.value, note: f.note.value};
  const base = `/api/tracked/${detail.product.id}/manual`;
  try {
    if (manualEditing) await api('PUT', `${base}/${manualEditing.id}`, {...body, revision: manualEditing.revision});
    else await api('POST', base, body);
    await refreshDetail();
    $('manual-dialog').close();
    toast(manualEditing ? '已更新手動比價' : '已新增手動比價');
  } catch (error) { showError('manual-error', error); if (error.status === 409) await refreshDetail().catch(() => {}); }
});
$('confirm-delete').onclick = async () => {
  try {
    await api('DELETE', `/api/tracked/${detail.product.id}/manual/${deleting.id}?revision=${deleting.revision}`);
    $('delete-dialog').close();
    await refreshDetail();
    toast('已刪除手動比價');
  } catch (error) { $('delete-dialog').close(); toast(error.message); await refreshDetail().catch(() => {}); }
};
