import {$, api, esc, toast} from './ui.js';
import {questionForms, running, taskAction, taskList, taskPath, tasksPath, taskStatus} from './grok.js';

const keys = ['name','model','attributes','packageQuantity','packageUnit','keyword','momoKeyword','feebeeKeyword'];
const form = $('tracked-form');
const fields = form.elements;
const dialog = $('tracked-dialog');
const send = $('grok-draft-send');
const status = $('grok-draft-state');
let taskId, applied, timer, listTimer, configured = false, markup;

function isCurrent(id) { return dialog.open && taskId === id; }
function hints() { form.querySelectorAll('.grok-origin').forEach(node => node.remove()); }
function drawDraftStatus(html) {
  if (markup !== html && !status.contains(document.activeElement)) {
    status.innerHTML = html;
    markup = html;
  }
}
export function prepareDraft(isNew) {
  clearTimeout(timer);
  taskId = applied = null;
  markup = status.innerHTML = '';
  hints();
  $('grok-draft-controls').hidden = !isNew;
  if (isNew) send.disabled = !configured;
  if (isNew) taskList().then(data => {
    configured = data.configured;
    $('grok-draft-unconfigured').hidden = configured;
    send.disabled = !configured || Boolean(taskId);
  }).catch(error => toast(error.message));
}
export async function refreshDrafts() {
  clearTimeout(listTimer);
  try {
    const {tasks} = await taskList();
    const drafts = tasks.filter(t => t.kind === 'draft');
    $('grok-drafts').hidden = !drafts.length;
    $('grok-draft-list').innerHTML = drafts.map(t => `<div class="grok-draft-row" data-draft="${esc(t.id)}">
      <strong>${esc(t.label || '新增追蹤商品')}</strong>${taskStatus(t)}
      <button class="btn small" type="button" data-draft-open>開啟</button>
      <button class="btn small" type="button" data-draft-discard>捨棄</button></div>`).join('');
    if (location.hash === '#tracked' && drafts.some(t => running(t.status))) listTimer = setTimeout(refreshDrafts, 5000);
  } catch (error) { toast(error.message); }
}

export function bindDraft(openForm, parseAttributes, attrLines) {
  async function updateDraft(id) {
    clearTimeout(timer);
    try {
      const task = await api('GET', taskPath(id));
      if (!isCurrent(id)) return;
      const {draft} = task;
      send.disabled = !configured || Boolean(taskId);
      const searches = draft?.searches.map(s => `<li>${esc(s.source === 'feebee' ? '飛比' : s.source)} · ${esc(s.keyword)} · ${esc(s.count ?? '受阻')}${s.count == null ? ' ' + esc(s.reason) : ' 筆'}</li>`).join('');
      drawDraftStatus((running(task.status) ? '<p>補完中…</p>' : taskStatus(task)) + questionForms(task) +
        (searches ? `<details><summary>關鍵字試搜結果</summary><ul>${searches}</ul></details>` : ''));
      if (draft && applied !== id) {
        for (const [key, value] of Object.entries(draft.fields)) {
          const input = fields[key];
          const toText = v => key === 'attributes' ? attrLines(v) : String(v ?? '');
          const changed = toText(task.conditions.fields[key]) !== toText(value);
          if (input.value === toText(task.conditions.fields[key])) input.value = toText(value);
          if (changed && input.value === toText(value)) {
            const hint = document.createElement('small');
            hint.className = 'grok-origin';
            hint.textContent = ['Grok 補完', draft.evidence[key]].filter(Boolean).join(' · ');
            input.after(hint);
          }
        }
        if (draft.fields.momoKeyword || draft.fields.feebeeKeyword) form.querySelector('.advanced').open = true;
        applied = id;
        await refreshDrafts();
      }
      if (isCurrent(id) && running(task.status)) timer = setTimeout(() => updateDraft(id), 5000);
    } catch (error) {
      if (isCurrent(id)) {
        $('tracked-error').textContent = error.message;
        $('tracked-error').hidden = false;
        timer = setTimeout(() => updateDraft(id), 5000);
      }
    }
  }
  send.addEventListener('click', async () => {
    send.disabled = true;
    $('tracked-error').hidden = true;
    try {
      const condition = fields.grokCondition.value.trim();
      if (!condition) throw new Error('請輸入商品條件');
      const snapshot = Object.fromEntries(keys.map(key => [key, fields[key].value]));
      snapshot.attributes = parseAttributes(snapshot.attributes);
      snapshot.packageQuantity = snapshot.packageQuantity ? Number(snapshot.packageQuantity) : '';
      const {id} = await api('POST', tasksPath, {kind:'draft',condition,fields:snapshot});
      await refreshDrafts();
      if (dialog.open && !taskId) { taskId = id; await updateDraft(id); }
    } catch (error) {
      $('tracked-error').textContent = error.message;
      $('tracked-error').hidden = false;
      send.disabled = false;
    }
  });
  status.addEventListener('click', async e => {
    const button = e.target.closest('button');
    if (!button) return;
    try {
      button.disabled = true;
      if (await taskAction(button)) { button.blur(); await updateDraft(taskId); await refreshDrafts(); }
    } catch (error) { toast(error.message); }
    finally { button.disabled = false; }
  });
  $('grok-draft-list').addEventListener('click', async e => {
    const button = e.target.closest('button'), row = button?.closest('[data-draft]');
    if (!row) return;
    const id = row.dataset.draft;
    try {
      button.disabled = true;
      if (await taskAction(button)) return await refreshDrafts();
      if (button.hasAttribute('data-draft-discard')) {
        await api('DELETE', taskPath(id));
        await refreshDrafts();
        toast('已捨棄補完');
        return;
      }
      const task = await api('GET', taskPath(id));
      openForm(null);
      taskId = id;
      fields.grokCondition.value = task.conditions.condition;
      for (const [key, value] of Object.entries(task.conditions.fields)) fields[key].value = key === 'attributes' ? attrLines(value) : value;
      await updateDraft(id);
    } catch (error) { toast(error.message); }
    finally { button.disabled = false; }
  });
  dialog.addEventListener('close', () => clearTimeout(timer));
  return () => applied;
}
