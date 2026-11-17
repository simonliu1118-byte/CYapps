// Candidate listings of a tracked product (same spec / other package / pending / unrelated / excluded) and the manual
// judgments on them. Shared by the product detail page and the 待確認 page.
import {money} from './core.js';
import {$, api, esc, showError, tag, toast} from './ui.js';

const judgeButtons = (c, productId) => [['same', '同規格'], ['other', '其他包裝'], ['not_same', '不是同一商品']]
  .filter(([k]) => !(k === 'same' && c.group === 'same') && !(k === 'other' && c.group === 'other'))
  .map(([k, label]) => `<button type="button" class="btn small" data-judge="${k}" data-product="${esc(productId)}" data-source="${esc(c.source)}" data-key="${esc(c.listingKey)}">${label}</button>`).join('');
const restoreButton = (c, productId) => `<button type="button" class="btn small" data-restore="1" data-product="${esc(productId)}" data-source="${esc(c.source)}" data-key="${esc(c.listingKey)}">恢復</button>`;

export const candidateTable = (rows, productId) => `<div class="table-wrap"><table class="data-table wide"><thead><tr><th>來源</th><th>商品／賣家</th><th class="num">價格</th><th>說明</th><th>操作</th></tr></thead><tbody>${rows.map(c => `<tr class="${c.group === 'excluded' ? 'muted-row' : ''}">
  <td>${tag(c.source)}</td>
  <td><a href="${esc(c.url)}" target="_blank" rel="noopener noreferrer">${esc(c.title)}</a>${c.seller ? `<small>${esc(c.seller)}</small>` : ''}</td>
  <td class="num">${c.price === null ? '—' : money(c.price)}</td>
  <td>${c.group === 'other' ? `${c.parsed.quantity} ${esc(c.parsed.unit)}` : ''}${c.parsed.reason ? ` <span class="chip ${c.group === 'pending' ? 'warning' : 'info'}">${esc(c.parsed.reason)}</span>` : ''}</td>
  <td><div class="row-actions">${c.group === 'excluded' ? restoreButton(c, productId) : judgeButtons(c, productId)}</div></td></tr>`).join('')}</tbody></table></div>`;

const judgmentPath = id => `/api/tracked/${id}/judgments`;
let pendingJudge = null;

// `onDone` runs after every successful judgment (reload the page's data and the nav badge).
export function bindJudging(root, onDone) {
  root.addEventListener('click', async e => {
    const button = e.target.closest('button[data-judge], button[data-restore]');
    if (!button) return;
    const {product, source, key} = button.dataset;
    try {
      if (button.dataset.restore) {
        await api('DELETE', `${judgmentPath(product)}?source=${source}&listingKey=${encodeURIComponent(key)}`);
        await onDone();
        toast('已恢復');
      } else if (button.dataset.judge === 'other') {
        pendingJudge = {product, source, key, onDone};
        $('package-form').reset();
        $('package-error').hidden = true;
        $('package-dialog').showModal();
      } else {
        await api('PUT', judgmentPath(product), {source, listingKey: key, verdict: button.dataset.judge});
        await onDone();
        toast('已記錄判斷，之後查價自動套用');
      }
    } catch (error) { toast(error.message); }
  });
}

$('package-form').addEventListener('submit', async e => {
  e.preventDefault();
  $('package-error').hidden = true;
  const f = e.target.elements;
  try {
    await api('PUT', judgmentPath(pendingJudge.product), {source: pendingJudge.source, listingKey: pendingJudge.key, verdict: 'other_package', packageQuantity: Number(f.quantity.value), packageUnit: f.unit.value});
    $('package-dialog').close();
    await pendingJudge.onDone();
    toast('已記錄判斷，之後查價自動套用');
  } catch (error) { showError('package-error', error); }
});
