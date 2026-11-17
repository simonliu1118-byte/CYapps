// 查價 page: one keyword, one source selector (all / momo / shopee / coupang), one merged result table.
// Spec parsing and manual 設定規格 (/api/classifications, shared and audited), exclusion, and 加入追蹤.
import {displayTime, money} from './core.js';
import {classification, groups, sanitizeAttributes} from './catalog-core.js';
import {PLATFORM_LABEL} from './compare.js';
import {$, api, esc, showError, tag, toast} from './ui.js';
import {openTrackedForm} from './tracked.js';

const SOURCES = ['momo', 'shopee', 'coupang'];
const USABLE = ['ok', 'no_match', 'no_price'];
const ruleKey = (source, key) => `${source}\u001f${key}`;

let keyword = '', products = [], effective = [], statuses = {}, rules = new Map(), momoPage = 0, momoNext = false;
let sortKey = 'price', sortDir = 1, filter = 'all', editing = null, busy = false, sourceUrl = '';

// ---- recent searches ----
async function loadRecent() {
  try {
    const {keywords} = await api('GET', '/api/search/recent');
    $('s-recent').innerHTML = keywords.length ? `<span class="muted">最近查價</span>${keywords.map(k => `<button type="button" class="btn small" data-keyword="${esc(k)}">${esc(k)}</button>`).join('')}` : '';
  } catch { $('s-recent').innerHTML = ''; }
}
$('s-recent').addEventListener('click', e => {
  const button = e.target.closest('[data-keyword]');
  if (!button) return;
  $('s-keyword').value = button.dataset.keyword;
  run();
});

// ---- fetching ----
const toProducts = (source, list) => list.map(r => source === 'momo'
  ? {source, key: r.code, title: r.title, price: r.price, url: r.url, seller: ''}
  : {source, key: r.url, title: r.title, price: r.price, url: r.url, seller: r.seller || r.merchant || ''});

async function fetchSource(source, page = 1) {
  try {
    const res = await api('GET', `/api/search?${new URLSearchParams({source, keyword, page})}`);
    statuses[source] = res;
    if (res.sourceUrl) sourceUrl = res.sourceUrl;
    if (source === 'momo') { momoPage = page; momoNext = Boolean(res.hasNext); }
    if (USABLE.includes(res.status)) {
      const have = new Set(products.filter(p => p.source === source).map(p => p.key));
      products.push(...toProducts(source, res.results).filter(p => !have.has(p.key)));
    }
  } catch (error) { statuses[source] = {status: 'error', message: error.message}; }
}

async function run() {
  if (busy) return;
  keyword = $('s-keyword').value.trim();
  if (!keyword) return;
  busy = true;
  $('s-submit').disabled = true;
  products = []; effective = []; statuses = {}; momoNext = false; sourceUrl = ''; filter = 'all';
  $('s-recent').innerHTML = '';
  $('s-result').hidden = false;
  $('s-status').textContent = '查詢中…';
  $('s-body').innerHTML = '';
  try {
    const wanted = $('s-source').value === 'all' ? SOURCES : [$('s-source').value];
    // Shopee and coupang come from one Feebee fetch, so coupang runs after shopee (it then hits the shared cache).
    await Promise.all([
      wanted.includes('momo') ? fetchSource('momo') : null,
      (async () => { for (const s of wanted.filter(s => s !== 'momo')) await fetchSource(s); })()
    ]);
    await loadRules([...new Set(products.map(p => p.source))]);
    classify();
    render();
  } finally { busy = false; $('s-submit').disabled = false; }
}
$('s-form').addEventListener('submit', e => { e.preventDefault(); run(); });

$('s-more').onclick = async () => {
  $('s-more').disabled = true;
  await fetchSource('momo', momoPage + 1);
  await loadRules(['momo']);
  classify();
  render();
  $('s-more').disabled = false;
};

async function loadRules(sources) {
  for (const source of sources) {
    for (const c of (await api('GET', `/api/classifications?source=${source}`)).classifications) rules.set(ruleKey(source, c.listingKey), c);
  }
}
function classify() { effective = products.map((p, i) => ({...classification(p, rules.get(ruleKey(p.source, p.key))), i})); }

// ---- rendering ----
function statusText(source, res) {
  switch (res.status) {
    case 'error': return res.message;
    case 'blocked': case 'blocked_cooldown': case 'provider_refused': return '被站方限制，已暫停查詢';
    case 'rate_limited': return `查詢過於頻繁，約 ${res.retryAfterSeconds || 15} 秒後可再查`;
    case 'no_match': case 'no_price': return '無結果';
    case 'ok': return `${res.results.length} 筆${res.fromCache ? '（使用快取）' : ''}`;
    default: return '暫時無法取得';
  }
}
function renderStatus() {
  const items = SOURCES.filter(s => statuses[s]).map(s => `<span class="${statuses[s].status === 'ok' ? '' : 'bad'}">${tag(s)} ${esc(statusText(s, statuses[s]))}</span>`);
  const checked = Object.values(statuses).map(r => r.checkedAt).filter(Boolean).sort().at(-1);
  $('s-status').innerHTML = items.join('') + (checked ? `<span class="muted">查價時間 ${esc(displayTime(checked))}</span>` : '')
    + (sourceUrl ? `<a href="${esc(sourceUrl)}" target="_blank" rel="noopener noreferrer">飛比原始搜尋結果</a>` : '');
}
const sorters = {
  source: (a, b) => SOURCES.indexOf(a.source) - SOURCES.indexOf(b.source),
  title: (a, b) => a.title.localeCompare(b.title, 'zh-TW'),
  price: (a, b) => (a.price ?? Infinity) - (b.price ?? Infinity)
};
function visibleRows() {
  const rows = effective.filter(p => filter === 'all' || p.source === filter);
  // Rows without a price always sort last, whichever direction is chosen.
  return rows.sort((a, b) => sortKey === 'price' && (a.price === null || b.price === null) ? (a.price === null) - (b.price === null) : (sorters[sortKey](a, b) || a.i - b.i) * sortDir);
}
const attrText = attrs => Object.entries(attrs || {}).map(([k, v]) => `${esc(k)}：${esc(v)}`).join('、');
function specCell(p) {
  const chip = p.excluded ? '<span class="chip">不使用</span>' : p.status === '人工確認' ? '<span class="chip accent">人工設定</span>' : p.ready ? '' : '<span class="chip warning">待確認</span>';
  const parts = [p.model ? `<strong>${esc(p.model)}</strong>` : '', p.quantity ? `${p.quantity} ${esc(p.unit)}` : '', attrText(p.attributes)].filter(Boolean);
  return `${parts.join(' · ') || '<span class="muted">未辨識</span>'} ${chip}`;
}
function render() {
  renderStatus();
  $('s-filter').innerHTML = [['all', '全部'], ...SOURCES.map(s => [s, PLATFORM_LABEL[s]])]
    .map(([k, label]) => `<button type="button" data-filter="${k}" class="${filter === k ? 'selected' : ''}">${label} ${k === 'all' ? effective.length : effective.filter(p => p.source === k).length}</button>`).join('');
  const rows = visibleRows();
  $('s-body').innerHTML = rows.length ? rows.map(p => `<tr class="${p.excluded ? 'is-excluded' : ''}" data-i="${p.i}">
    <td>${tag(p.source)}</td>
    <td><a href="${esc(p.url)}" target="_blank" rel="noopener noreferrer">${esc(p.title)}</a></td>
    <td>${esc(p.seller) || '—'}</td>
    <td class="num">${p.price === null ? '<span class="muted">未取得</span>' : money(p.price)}</td>
    <td>${specCell(p)}</td>
    <td><div class="row-actions"><button type="button" class="btn small" data-act="spec">設定規格</button><button type="button" class="btn small" data-act="exclude">${p.excluded ? '取消排除' : '不使用'}</button><button type="button" class="btn small" data-act="track">加入追蹤</button></div></td></tr>`).join('')
    : `<tr><td colspan="6" class="muted">${effective.length ? '沒有符合篩選的商品。' : '沒有查到商品。'}</td></tr>`;
  for (const b of document.querySelectorAll('#s-table .sort')) b.parentElement.setAttribute('aria-sort', b.dataset.sort === sortKey ? (sortDir > 0 ? 'ascending' : 'descending') : 'none');
  renderSummary(rows);
  $('s-more').hidden = !momoNext;
  $('s-disclaimer').hidden = !effective.some(p => p.source !== 'momo');
}
// One line: per-unit price summary of the largest spec groups among the rows shown.
function renderSummary(rows) {
  const summary = groups(rows.filter(p => !p.excluded));
  $('s-summary').textContent = summary.length
    ? '同規格摘要（每單位）：' + summary.slice(0, 2).map(g => `${g.model} 每${g.unit} ${g.statistics.count} 筆，最低 ${money(g.statistics.min)}／中位 ${money(g.statistics.median)}／最高 ${money(g.statistics.max)}`).join('；') + (summary.length > 2 ? `；另有 ${summary.length - 2} 組` : '')
    : '尚無可歸類的同規格商品，可用「設定規格」補齊。';
}

// ---- events ----
$('s-filter').addEventListener('click', e => { const b = e.target.closest('[data-filter]'); if (b) { filter = b.dataset.filter; render(); } });
$('s-table').querySelector('thead').addEventListener('click', e => {
  const b = e.target.closest('[data-sort]');
  if (!b) return;
  sortDir = b.dataset.sort === sortKey ? -sortDir : 1;
  sortKey = b.dataset.sort;
  render();
});
$('s-body').addEventListener('click', async e => {
  const button = e.target.closest('button[data-act]');
  if (!button) return;
  const p = effective[Number(button.closest('tr').dataset.i)];
  try {
    if (button.dataset.act === 'spec') openEditor(p);
    else if (button.dataset.act === 'track') openTrackedForm({name: p.title, keyword, model: p.model, quantity: p.quantity, unit: p.unit});
    else await saveRule(p, {model: p.model, quantity: p.quantity, unit: p.unit, attributes: p.attributes, decision: p.excluded ? 'include' : 'exclude'});
  } catch (error) { toast(error.message); }
});

// ---- 設定規格 ----
const attributesText = attrs => Object.entries(attrs || {}).map(([k, v]) => `${k}=${v}`).join('\n');
function parseAttributes(text) {
  const attrs = {};
  for (const line of text.split(/\r?\n/).slice(0, 12)) {
    if (!line.trim()) continue;
    const at = line.indexOf('=');
    if (at < 1) throw new Error('額外規格請使用「名稱=內容」，每行一項');
    const key = line.slice(0, at).trim(), value = line.slice(at + 1).trim();
    if (!key || !value) throw new Error('額外規格不可留空');
    attrs[key] = value;
  }
  return sanitizeAttributes(attrs);
}
function openEditor(p) {
  editing = p;
  const f = $('class-form').elements;
  $('class-source').textContent = `來源：${PLATFORM_LABEL[p.source]}`;
  $('class-listing').textContent = p.title;
  f.model.value = p.model || ''; f.quantity.value = p.quantity ?? ''; f.unit.value = p.unit || '';
  f.attributes.value = attributesText(p.attributes); f.decision.value = p.excluded ? 'exclude' : 'include';
  $('class-error').hidden = true;
  $('class-dialog').showModal();
}
async function saveRule(p, fields) {
  const body = {source: p.source, listingKey: p.key, ...fields};
  await api('PUT', '/api/classifications', body);
  rules.set(ruleKey(p.source, p.key), body);
  classify();
  render();
}
$('class-form').addEventListener('submit', async e => {
  e.preventDefault();
  const f = e.target.elements;
  try {
    const model = f.model.value.trim(), quantity = Number(f.quantity.value), unit = f.unit.value.trim(), decision = f.decision.value;
    const attributes = parseAttributes(f.attributes.value);
    if (decision !== 'exclude' && (!model || !unit || !Number.isFinite(quantity) || quantity <= 0 || quantity > 100000))
      throw new Error('要納入比較，請確認型號、包裝數量及單位；不確定可先標記不使用。');
    await saveRule(editing, {model, quantity: quantity > 0 ? quantity : null, unit, attributes, decision});
    $('class-dialog').close();
  } catch (error) { showError('class-error', error); }
});
$('class-reset').onclick = async () => {
  try {
    const key = ruleKey(editing.source, editing.key);
    if (rules.has(key)) {
      await api('DELETE', `/api/classifications?source=${editing.source}&listingKey=${encodeURIComponent(editing.key)}`);
      rules.delete(key);
      classify();
      render();
    }
    $('class-dialog').close();
  } catch (error) { showError('class-error', error); }
};

loadRecent();
