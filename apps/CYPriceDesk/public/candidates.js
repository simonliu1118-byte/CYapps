import {displayTime,money} from './core.js';
import {$, api, esc, showError, tag, toast} from './ui.js';
import {PLATFORM_LABEL} from './compare.js';
import {judgePhase, phaseLabel, questionForms, running, taskAction, taskList, taskPath, tasksPath, taskStatus} from './grok.js';

const verdicts = {same:'同規格', other_package:'其他包裝', not_same:'不是同一商品', insufficient:'依據不足'};
const eligible = c => ['momo','coupang'].includes(c.source);
const judgmentPath = id => `/api/tracked/${encodeURIComponent(id)}/judgments`;
const candidatePath = (product, source, key) => `/api/tracked/${encodeURIComponent(product)}/candidates/${source}/${encodeURIComponent(key)}`;
const priceText = price => price == null ? '—' : money(price);

const button = (action, label) => `<button type="button" class="btn small" data-candidate-action="${action}">${label}</button>`;
function candidateActions(c) {
  let options;
  const review = c.grok?.status==='answered';
  const approve = review && c.grok.result.verdict!=='insufficient';
  if (c.group==='excluded') {
    options = button('restore','恢復');
  } else {
    const manual = Object.entries(verdicts)
      .filter(([v]) => v!=='insufficient' && !(v==='same' && c.group==='same') && !(v==='other_package' && c.group==='other'))
      .map(([value,label]) => button(value,label)).join('');
    options = (review ? button('correct','修正並採用')+button('recheck','請 Grok 再查') : '')+manual;
  }
  const primary = approve ? '<button type="button" class="btn primary small" data-candidate-action="approve">同意</button>' : '';
  const menuLabel = review ? '其他操作' : '操作';
  return `<div class="candidate-actions">${primary}<details class="candidate-menu">
    <summary class="btn small">${menuLabel}</summary><div class="candidate-menu-items">${options+button('delete','刪除')}</div></details></div>`;
}
function resultHtml(item) {
  if (!item) return '<span class="muted">—</span>';
  if (item.status==='expired') return '<span class="grok-result muted">判讀已過期</span>';
  const r = item.result;
  if (!r) return '<span class="muted">—</span>';
  const pack = r.spec.packageQuantity ? r.spec.packageQuantity+' '+r.spec.packageUnit : '';
  const spec = [r.spec.model,...Object.entries(r.spec.attributes || {}).map(([key,value]) => key+' '+value),pack].filter(Boolean).join(' · ');
  return `<div class="grok-result"><div class="grok-result-line">
    <span class="chip">${esc(verdicts[r.verdict])}${item.status==='reviewed' ? ' · 已複審' : ''}</span>
    ${r.price===null ? '' : `<strong>${priceText(r.price)}</strong>`}</div>
    ${spec ? `<small class="grok-result-spec">${esc(spec)}</small>` : ''}
    <details><summary>查看依據</summary>
      ${r.option ? `<p>${esc(r.option)}</p>` : ''}
      ${r.priceConditions ? `<p>${esc(r.priceConditions)}</p>` : ''}
      <p>${esc(r.evidence)}</p>
      ${r.url ? `<a href="${esc(r.url)}" target="_blank" rel="noopener noreferrer">查看商品頁</a>` : ''}
    </details></div>`;
}
export const candidateTable = (rows, productId) => `<div class="table-wrap"><table class="data-table wide candidate-table"><thead><tr>
  <th class="grok-pick" hidden>選送</th><th>候選商品</th><th class="num">搜尋價</th><th>Grok 判讀</th><th>操作</th></tr></thead>
  <tbody>${rows.map(c => `<tr data-candidate="${esc(JSON.stringify(c))}" data-product="${esc(productId)}" class="${c.group==='excluded' ? 'muted-row' : ''}">
    <td class="grok-pick" hidden>${eligible(c) ? `<label class="grok-selection"><input type="checkbox" aria-label="選送 ${esc(c.title)} 給 Grok"><span>選送</span></label>` : ''}</td>
    <td class="candidate-title"><div class="candidate-merchant">${tag(c.source)}${c.seller ? `<small>${esc(c.seller)}</small>` : ''}</div>
      <a href="${esc(c.url)}" target="_blank" rel="noopener noreferrer">${esc(c.title)}</a>
      ${c.parsed.reason ? `<small class="candidate-reason">${esc(c.parsed.reason)}</small>` : ''}</td>
    <td class="num candidate-price">${priceText(c.price)}</td>
    <td class="candidate-result">${resultHtml(c.grok)}</td>
    <td class="candidate-operation">${candidateActions(c)}</td></tr>`).join('')}</tbody></table></div>`;

export const candidateTasks = (id, name, canSend, canClear) => `<div class="candidate-grok" data-grok-product="${esc(id)}" data-product-name="${esc(name)}" data-can-send="${canSend}">
  <div class="grok-toolbar">
    <div class="toolbar-group">
      <button class="btn primary" type="button" data-grok-judge disabled>交給 Grok 判讀</button>
      <button class="btn" type="button" data-grok-select hidden aria-pressed="false">選擇候選</button>
      <small data-grok-config hidden>Grok 尚未設定</small>
    </div>
    <div class="toolbar-group">
      <button class="btn quiet" type="button" data-grok-batches hidden>管理批次</button>
      ${canClear ? '<button class="btn quiet clear-pending" type="button" data-clear-pending>清除全部待確認</button>' : ''}
    </div>
  </div>
  <small data-grok-selection-hint hidden>勾選要送的 momo、酷澎候選。全不勾時，送該商品待確認與其他包裝的候選。</small>
  <div data-grok-tasks></div></div>`;

const open = t => running(t.status) || t.status==='send_failed';

async function renderTasks(root) {
  const boxes = [...root.querySelectorAll('[data-grok-product]')];
  if (!boxes.length || !root.getClientRects().length) return {active:false, results:null};
  const {configured, tasks} = await taskList();
  const results = [];
  let active = false;
  await Promise.all(boxes.map(async box => {
    const list = tasks.filter(t => t.kind==='judge' && t.productId===box.dataset.grokProduct);
    const outstanding = list.filter(open);
    const current = outstanding.find(t => running(t.status)) || outstanding[0] || list[0];
    const phase = current && judgePhase(current);
    const panel = box.closest('.panel');
    active ||= outstanding.length>0;
    results.push(...list.filter(t => t.answeredCount>0).map(t => t.id+':'+t.answeredCount));

    box.dataset.configured = configured;
    box.dataset.locked = outstanding.length>0;
    box.querySelector('[data-grok-config]').hidden = configured;
    box.querySelector('[data-grok-batches]').hidden = !list.length;
    panel.querySelectorAll('[data-candidate-action="recheck"]').forEach(button => { button.disabled = !configured || !!outstanding.length; });
    renderSelection(box);

    let markup = '';
    if (current) {
      const detail = current.hasQuestions && !['cancelled','send_failed'].includes(current.status)
        ? await api('GET',taskPath(current.id)) : null;
      markup = taskStatus(current,{phase,stopCount:outstanding.length,
        canRetry:configured && !outstanding.some(t => t.id!==current.id && running(t.status))}) +
        (detail ? questionForms(detail) : '');
    }
    const container = box.querySelector('[data-grok-tasks]');
    if (container.dataset.markup !== markup && !container.contains(document.activeElement)) {
      container.innerHTML = markup;
      container.dataset.markup = markup;
    }
  }));
  return {active,results:results.sort().join('|')};
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
  const clear = action==='clear-pending';
  $('candidate-action-title').textContent = clear ? '清除全部待確認？' : action==='delete' ? '刪除這筆候選？' : '請 Grok 再查';
  $('candidate-action-summary').textContent = clear
    ? `${context.name} · 移除目前 ${context.items.length} 筆待確認候選，既有價格紀錄將保留。`
    : [PLATFORM_LABEL[c.source],c.title,priceText(c.price)].join(' · ');
  const submit = $('candidate-action-submit');
  submit.textContent = clear ? '確認清除' : action==='delete' ? '刪除' : '送出';
  submit.className = 'btn '+(clear || action==='delete' ? 'danger' : 'primary');
  $('candidate-reason').hidden = action !== 'recheck';
  actionForm.elements.reason.required = action === 'recheck';
  $('candidate-action-error').hidden = true;
  $('candidate-action-dialog').showModal();
}
const itemPath = c => taskPath(c.grok.taskId) + '/items/' + c.source + '/' + encodeURIComponent(c.listingKey);
actionForm.addEventListener('submit', async e => {
  e.preventDefault();
  const {c, product, onDone, action, items} = pending;
  const submit = e.submitter;
  submit.disabled = true;
  try {
    if (action==='clear-pending') await api('DELETE',`/api/tracked/${encodeURIComponent(product)}/candidates?group=pending`,{items});
    else if (action === 'delete') await api('DELETE', candidatePath(product, c.source, c.listingKey), {runId:c.runId});
    else await api('POST', itemPath(c) + '/recheck', {reason:actionForm.elements.reason.value});
    $('candidate-action-dialog').close();
    await onDone();
    toast(action==='clear-pending' ? `已清除 ${items.length} 筆待確認候選` : action==='delete' ? '已刪除候選' : '已提交再查');
  } catch (error) { showError('candidate-action-error',error); }
  finally { submit.disabled = false; }
});

function renderSelection(box) {
  const panel = box.closest('.panel');
  const allowed = box.dataset.configured==='true' && box.dataset.locked!=='true' && box.dataset.dispatching!=='true';
  const options = box.querySelector('[data-grok-select]');
  const selecting = allowed && box.dataset.selecting==='true';
  options.hidden = !allowed || !panel.querySelector('.grok-selection');
  options.setAttribute('aria-pressed',String(selecting));
  options.textContent = selecting ? '取消選擇' : '選擇候選';
  box.querySelector('[data-grok-selection-hint]').hidden = !selecting;
  panel.querySelectorAll('.grok-pick').forEach(cell => { cell.hidden = !selecting; });
  const count = selecting ? panel.querySelectorAll('.grok-selection input:checked').length : 0;
  const send = box.querySelector('[data-grok-judge]');
  send.disabled = !allowed || (box.dataset.canSend!=='true' && !count);
  send.textContent = count ? `交給 Grok 判讀（${count} 筆）` : '交給 Grok 判讀';
}

const batchesDialog = $('grok-batches-dialog');
const batchList = $('grok-batches-list'), batchConfirm = $('grok-batches-confirm');
const batchClear = $('grok-batches-clear'), batchSubmit = $('grok-batches-submit'), batchBack = $('grok-batches-back');
let batchContext;
function batchScope() { return tasksPath+'?productId='+encodeURIComponent(batchContext.product); }

function confirmBatches(taskId, count) {
  batchContext.target = taskId;
  batchConfirm.textContent = taskId ? '刪除這筆批次及其 Grok 回填？既有人工判斷及採用的價格會保留。'
    : `清除全部 ${count} 筆 Grok 批次及回填？既有人工判斷及採用的價格會保留。`;
  batchList.hidden = batchClear.hidden = true;
  batchConfirm.hidden = batchSubmit.hidden = batchBack.hidden = false;
  batchSubmit.focus();
}
async function showBatches() {
  const {tasks} = await api('GET',batchScope());
  batchContext.count = tasks.length;
  batchList.innerHTML = tasks.length ? `<ul class="grok-batch-list">${tasks.map(t => `<li>
    <div><strong>${esc(phaseLabel(judgePhase(t)))}</strong> <span class="muted">${esc(displayTime(t.createdAt))} · ${t.itemCount} 筆候選</span>
      ${t.error ? `<small>${esc(t.error)}</small>` : ''}</div>
    <button class="btn small" type="button" data-remove-batch="${esc(t.id)}">刪除</button></li>`).join('')}</ul>` : '<p class="muted">目前沒有 Grok 批次。</p>';
  batchConfirm.hidden = batchSubmit.hidden = batchBack.hidden = true;
  batchList.hidden = false;
  batchClear.hidden = tasks.length===0;
}
async function openBatches(box,onDone) {
  batchContext = {product:box.dataset.grokProduct,onDone};
  $('grok-batches-product').textContent = box.dataset.productName;
  $('grok-batches-error').hidden = true;
  await showBatches();
  batchesDialog.showModal();
}
batchList.addEventListener('click',e => {
  const button = e.target.closest('[data-remove-batch]');
  if (button) confirmBatches(button.dataset.removeBatch,1);
});
batchClear.onclick = () => confirmBatches(null,batchContext.count);
batchBack.onclick = () => showBatches().catch(error => showError('grok-batches-error',error));
batchSubmit.onclick = async () => {
  batchSubmit.disabled = true;
  $('grok-batches-error').hidden = true;
  try {
    const {target,onDone} = batchContext;
    const {discarded} = await api('DELETE',target ? taskPath(target) : batchScope());
    await onDone();
    await showBatches();
    toast(`已清除 ${discarded} 筆 Grok 批次`);
  } catch (error) { showError('grok-batches-error',error); }
  finally { batchSubmit.disabled = false; }
};

const pollers = new WeakMap();
export function refreshCandidateTasks(root) { pollers.get(root)?.(); }
export function bindJudging(root, onDone) {
  let previousResults, timer, polling = false, active = false;
  const check = async () => {
    if (polling) return;
    polling = true;
    clearTimeout(timer);
    try {
      const state = await renderTasks(root);
      active = state.active;
      if (state.results===null) { previousResults=undefined; return; }
      if (previousResults!==undefined && previousResults!==state.results && !root.querySelector('.grok-question:focus-within')) {
        previousResults = state.results;
        await onDone();
        await renderTasks(root);
      } else if (previousResults===undefined || !root.querySelector('.grok-question:focus-within')) {
        previousResults = state.results;
      }
    } catch { /* Keep the candidate table usable when task status is unavailable. */ }
    finally {
      polling = false;
      if (active) timer = setTimeout(check, 5000);
    }
  };
  pollers.set(root, check);
  root.addEventListener('change', e => {
    if (e.target.closest('.grok-selection')) renderSelection(e.target.closest('.panel').querySelector('[data-grok-product]'));
  });
  root.addEventListener('click', async e => {
    const b = e.target.closest('button[data-candidate-action], button[data-grok-judge], button[data-grok-answer], button[data-grok-resend], button[data-grok-stop], button[data-grok-select], button[data-grok-batches], button[data-clear-pending]');
    if (!b) return;
    const box = b.closest('[data-grok-product]');
    if (b.hasAttribute('data-grok-select')) {
      box.dataset.selecting = box.dataset.selecting!=='true';
      if (box.dataset.selecting!=='true') box.closest('.panel').querySelectorAll('.grok-selection input').forEach(input => { input.checked=false; });
      renderSelection(box);
      return;
    }
    if (b.hasAttribute('data-grok-judge')) box.dataset.dispatching='true';
    b.disabled = true;
    try {
      if (b.hasAttribute('data-grok-batches')) return await openBatches(box,onDone);
      if (b.hasAttribute('data-clear-pending')) {
        const items = [...box.closest('.panel').querySelectorAll('tr[data-candidate]')].map(row => {
          const {source,listingKey,runId} = JSON.parse(row.dataset.candidate);
          return {source,listingKey,runId};
        });
        return openCandidateAction({product:box.dataset.grokProduct,name:box.dataset.productName,items,onDone},'clear-pending');
      }
      if (await taskAction(b)) { b.blur(); await check(); return; }
      if (b.hasAttribute('data-grok-judge')) {
        const panel = b.closest('.panel');
        const items = [...panel.querySelectorAll('.grok-selection input:checked')].map(input => {
          const {source,listingKey,runId} = JSON.parse(input.closest('tr').dataset.candidate);
          return {source,listingKey,runId};
        });
        const task = await api('POST',tasksPath,{kind:'judge',productId:box.dataset.grokProduct,items});
        box.dataset.locked = open(task);
        box.dataset.selecting = 'false';
        panel.querySelectorAll('.grok-selection input:checked').forEach(input => { input.checked = false; });
        await check();
        toast(task.status === 'send_failed' ? '派工失敗，可重送' : '已交給 Grok 判讀');
        return;
      }
      const row = b.closest('[data-candidate]'), c = JSON.parse(row.dataset.candidate), product = row.dataset.product;
      const context = {c, product, onDone};
      const action = b.dataset.candidateAction;
      const menu = b.closest('.candidate-menu');
      if (menu) menu.open = false;
      if (action === 'delete' || action === 'recheck') return openCandidateAction(context, action);
      if (action === 'correct' || action === 'other_package') return openJudgment(context, action === 'correct');
      if (action === 'approve') await api('POST', itemPath(c) + '/approve');
      else if (action === 'restore') await api('DELETE', judgmentPath(product) + `?source=${c.source}&listingKey=${encodeURIComponent(c.listingKey)}`);
      else await api('PUT', judgmentPath(product), {source:c.source, listingKey:c.listingKey, verdict:action});
      await onDone();
      toast(action === 'restore' ? '已恢復' : '已記錄判斷');
    } catch (error) {
      toast(error.message);
      if (error.status===409 && box) await check();
    } finally {
      if (b.hasAttribute('data-grok-judge')) {
        delete box.dataset.dispatching;
        renderSelection(box);
      } else b.disabled = false;
    }
  });
}
