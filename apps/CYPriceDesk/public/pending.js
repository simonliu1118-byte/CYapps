// 待確認 page: every tracked product's pending candidates, judged in place; keeps the nav badge in step.
import {$, api, esc, setBadge} from './ui.js';
import {candidateTable, bindJudging} from './candidates.js';

export async function loadPending() {
  const {count, products} = await api('GET', '/api/pending');
  setBadge(count);
  $('pending-list').innerHTML = products.length
    ? products.map(p => `<section class="panel"><div class="panel-head"><h2><a href="#tracked/${esc(p.id)}">${esc(p.name)}</a> <small>型號 ${esc(p.model)} · ${p.packageQuantity} ${esc(p.packageUnit)} · ${p.candidates.length} 筆待確認</small></h2></div>${candidateTable(p.candidates, p.id)}</section>`).join('')
    : '<div class="empty"><strong>沒有待確認的商品</strong><p>新的查價結果若有程式無法判斷的商品，會出現在這裡。</p></div>';
}

bindJudging($('pending-list'), loadPending);
window.addEventListener('hashchange', () => { if (location.hash === '#pending') loadPending().catch(() => {}); });
