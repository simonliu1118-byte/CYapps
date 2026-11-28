import {money} from './core.js';
import {$, api, esc, showError, tag, toast} from './ui.js';
import {PLATFORM_LABEL} from './compare.js';
import {questionForms, running, taskAction, taskList, taskPath, tasksPath, taskStatus} from './grok.js';

const verdicts = {same:'同規格', other_package:'其他包裝', not_same:'不是同一商品', insufficient:'依據不足'};
const eligible = c => ['momo','coupang'].includes(c.source);
const judgmentPath = id => `/api/tracked/${encodeURIComponent(id)}/judgments`;
const candidatePath = (product, source, key) => `/api/tracked/${encodeURIComponent(product)}/candidates/${source}/${encodeURIComponent(key)}`;
const priceText = price => price == null ? '—' : money(price);

const button = (action, label) => `<button type="button" class="btn small" data-candidate-action="${action}">${label}</button>`;
function candidateActions(c) {
  if (c.group === 'excluded') return button('restore', '恢復') + button('delete', '刪除');
  const manual = Object.entries(verdicts).filter(([v]) => v !== 'insufficient' &&
    !(v === 'same' && c.group === 'same') && !(v === 'other_package' && c.group === 'other'))
    .map(([v, label]) => button(v, label)).join('');
  const review = c.grok?.status === 'answered'
    ? (c.grok.result.verdict !== 'insufficient' ? button('approve', '同意') : '') +
      button('correct', '修正並採用') + button('recheck', '請 Grok 再查')
    : '';
  return review + manual + button('delete', '刪除');
}
function resultHtml(item) {
  if (!item) return '';
  if (item.status === 'waiting') return '<p class="muted">Grok 判讀中…</p>';
  if (item.status === 'expired') return '<p class="muted">Grok 判讀已過期</p>';
  const r = item.result;
  if (!r) return '';
  const spec = [r.spec.model, ...Object.entries(r.spec.attributes || {}).map(([key, value]) => key + ' ' + value),
    r.spec.packageQuantity && [r.spec.packageQuantity, r.spec.packageUnit].join(' ')].filter(Boolean).join(' · ');
  return `<div class="grok-result"><span class="chip">Grok：${esc(verdicts[r.verdict])}${item.status === 'reviewed' ? ' · 已複審' : ''}</span>
    <p>${esc(r.option)} ${priceText(r.price)}${r.priceConditions ? ' · ' + esc(r.priceConditions) : ''}</p>
    ${spec ? `<p>${esc(spec)}</p>` : ''}
    <details><summary>依據 · ${esc(r.evidence.slice(0, 32))}${r.evidence.length > 32 ? '…' : ''}</summary><p>${esc(r.evidence)}</p></details>
    ${r.url ? `<a href="${esc(r.url)}" target="_blank" rel="noopener noreferrer">Grok 查看頁面</a>` : ''}</div>`;
}
export const candidateTable = (rows, productId) => `<div class="table-wrap"><table class="data-table wide candidate-table"><thead><tr><th>來源</th><th>商品／賣家</th><th class="num">價格</th><th>說明</th><th>操作</th></tr></thead><tbody>${rows.map(c => `<tr data-candidate="${esc(JSON.stringify(c))}" data-product="${esc(productId)}" class="${c.group === 'excluded' ? 'muted-row' : ''}">
  <td>${tag(c.source)}${eligible(c) ? '<label class="grok-selection" hidden><input type="checkbox" aria-label="勾選給 Grok"></label>' : ''}</td>
  <td><a href="${esc(c.url)}" target="_blank" rel="noopener noreferrer">${esc(c.title)}</a>${c.seller ? `<small>${esc(c.seller)}</small>` : ''}</td>
  <td class="num">${priceText(c.price)}</td>
  <td>${c.group === 'other' ? `${c.parsed.quantity} ${esc(c.parsed.unit)}` : ''}${c.parsed.reason ? ` <span class="chip ${c.group === 'pending' ? 'warning' : 'info'}">${esc(c.parsed.reason)}</span>` : ''}${resultHtml(c.grok)}</td>
  <td><div class="row-actions">${candidateActions(c)}</div></td></tr>`).join('')}</tbody></table></div>`;

export const candidateTasks = (id, canSend) => `<div class="candidate-grok" data-grok-product="${esc(id)}" data-can-send="${canSend}">
  <button class="btn" type="button" data-grok-judge disabled>交給 Grok 判讀</button>
  <small data-grok-config hidden>Grok 尚未設定</small><div data-grok-tasks></div></div>`;

async function renderTasks(root) {
  const boxes = [...root.querySelectorAll('[data-grok-product]')];
  if (!boxes.length || !root.getClientRects().length) return false;
  const {configured, tasks} = await taskList();
  let polling = false;
  await Promise.all(boxes.map(async box => {
    const id = box.dataset.grokProduct;
    const list = tasks.filter(t => t.kind === 'judge' && t.productId === id);
    polling ||= list.some(t => running(t.status));
    const panel = box.closest('.panel');
    box.dataset.configured = configured;
    enableDispatch(box);
    box.querySelector('[data-grok-config]').hidden = configured;
    panel.querySelectorAll('.grok-selection').forEach(label => { label.hidden = !configured; });
    const details = await Promise.all(list.filter(t => t.hasQuestions).map(t => api('GET', taskPath(t.id))));
    const markup = list.map(t => taskStatus(t) + (details.find(d => d.id === t.id) ? questionForms(details.find(d => d.id === t.id)) : '')).join('');
    const container = box.querySelector('[data-grok-tasks]');
    if (container.dataset.markup !== markup && !container.contains(document.activeElement)) {
      container.innerHTML = markup;
      container.dataset.markup = markup;
    }
  }));
  return polling;
}

let pending;
const packForm = $('package-form');
function syncPackage() {
  const f = packForm.elements, other = f.verdict.value === 'other_package';
  $('judge-quantity').hidden = $('judge-unit').hidden = !other;
  f.quantity.required = f.unit.required = other;
  $('judge-price').hidden = !(pending.grok && f.verdict.value === 'same');
}
packForm.elements.verdict.addEventListener('change', syncPackage);
function openJudgment(context, grok) {
  pending = {...context, grok};
  const r = grok ? pending.c.grok.result : null, f = packForm.elements;
  packForm.reset();
  $('package-title').textContent = grok ? '修正並採用' : '其他包裝';
  $('judge-verdict').hidden = !grok;
  f.verdict.value = r?.verdict === 'insufficient' ? '' : r?.verdict || 'other_package';
  f.quantity.value = r?.spec.packageQuantity ?? '';
  f.unit.value = r?.spec.packageUnit ?? '';
  f.price.value = r?.price ?? '';
  syncPackage();
  $('package-error').hidden = true;
  $('package-dialog').showModal();
}
packForm.addEventListener('submit', async e => {
  e.preventDefault();
  const {product, c, onDone, grok} = pending, f = packForm.elements;
  const body = {source:c.source, listingKey:c.listingKey, verdict:f.verdict.value,
    packageQuantity:Number(f.quantity.value), packageUnit:f.unit.value};
  if (grok) Object.assign(body, {grokTaskId:c.grok.taskId, price:f.price.value === '' ? null : Number(f.price.value)});
  try {
    await api('PUT', judgmentPath(product), body);
    $('package-dialog').close();
    await onDone();
    toast('已記錄判斷');
  } catch (error) { showError('package-error', error); }
});

const actionForm = $('candidate-action-form');
function openCandidateAction(context, action) {
  pending = {...context, action};
  const {c} = context;
  actionForm.reset();
  $('candidate-action-title').textContent = action === 'delete' ? '刪除這筆候選？' : '請 Grok 再查';
  $('candidate-action-summary').textContent = [PLATFORM_LABEL[c.source], c.title, priceText(c.price)].join(' · ');
  const submit = $('candidate-action-submit');
  submit.textContent = action === 'delete' ? '刪除' : '送出';
  submit.className = 'btn ' + (action === 'delete' ? 'danger' : 'primary');
  $('candidate-reason').hidden = action !== 'recheck';
  actionForm.elements.reason.required = action === 'recheck';
  $('candidate-action-error').hidden = true;
  $('candidate-action-dialog').showModal();
}
const itemPath = c => taskPath(c.grok.taskId) + '/items/' + c.source + '/' + encodeURIComponent(c.listingKey);
actionForm.addEventListener('submit', async e => {
  e.preventDefault();
  const {c, product, onDone, action} = pending;
  try {
    if (action === 'delete') await api('DELETE', candidatePath(product, c.source, c.listingKey), {runId:c.runId});
    else await api('POST', itemPath(c) + '/recheck', {reason:actionForm.elements.reason.value});
    $('candidate-action-dialog').close();
    await onDone();
    toast(action === 'delete' ? '已刪除候選' : '已提交再查');
  } catch (error) { showError('candidate-action-error', error); }
});

function enableDispatch(box) {
  const selected = box.closest('.panel').querySelector('.grok-selection input:checked');
  box.querySelector('[data-grok-judge]').disabled = box.dataset.configured !== 'true' || (box.dataset.canSend !== 'true' && !selected);
}

const pollers = new WeakMap();
export function refreshCandidateTasks(root) { pollers.get(root)?.(); }
export function bindJudging(root, onDone) {
  let wasRunning = false, timer, polling = false;
  const check = async () => {
    if (polling) return;
    polling = true;
    clearTimeout(timer);
    try {
      const active = await renderTasks(root);
      if (wasRunning && !active && root.getClientRects().length) {
        await onDone();
        await renderTasks(root);
      }
      wasRunning = active;
    } catch { /* The candidate table remains usable when task status is unavailable. */ }
    finally {
      polling = false;
      if (wasRunning) timer = setTimeout(check, 5000);
    }
  };
  pollers.set(root, check);
  root.addEventListener('change', e => {
    if (e.target.closest('.grok-selection')) enableDispatch(e.target.closest('.panel').querySelector('[data-grok-product]'));
  });
  root.addEventListener('click', async e => {
    const b = e.target.closest('button[data-candidate-action], button[data-grok-judge], button[data-grok-answer], button[data-grok-resend]');
    if (!b) return;
    b.disabled = true;
    try {
      if (await taskAction(b)) { b.blur(); await check(); return; }
      if (b.hasAttribute('data-grok-judge')) {
        const panel = b.closest('.panel');
        const items = [...panel.querySelectorAll('.grok-selection input:checked')].map(input => {
          const {source,listingKey,runId} = JSON.parse(input.closest('tr').dataset.candidate);
          return {source,listingKey,runId};
        });
        const task = await api('POST', tasksPath, {kind:'judge',productId:b.closest('[data-grok-product]').dataset.grokProduct,items});
        wasRunning = running(task.status);
        await onDone();
        await check();
        toast(task.status === 'send_failed' ? '派工失敗，可重送' : '已交給 Grok 判讀');
        return;
      }
      const row = b.closest('[data-candidate]'), c = JSON.parse(row.dataset.candidate), product = row.dataset.product;
      const context = {c, product, onDone};
      const action = b.dataset.candidateAction;
      if (action === 'delete' || action === 'recheck') return openCandidateAction(context, action);
      if (action === 'correct' || action === 'other_package') return openJudgment(context, action === 'correct');
      if (action === 'approve') await api('POST', itemPath(c) + '/approve');
      else if (action === 'restore') await api('DELETE', judgmentPath(product) + `?source=${c.source}&listingKey=${encodeURIComponent(c.listingKey)}`);
      else await api('PUT', judgmentPath(product), {source:c.source, listingKey:c.listingKey, verdict:action});
      await onDone();
      toast(action === 'restore' ? '已恢復' : '已記錄判斷');
    } catch (error) { toast(error.message); }
    finally { b.disabled = false; }
  });
}
