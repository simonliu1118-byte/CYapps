import {api, esc, tag, toast} from './ui.js';

export const tasksPath = '/api/grok-tasks';
export const taskPath = id => tasksPath + '/' + encodeURIComponent(id);
export const taskList = () => api('GET', tasksPath);
export const running = status => ['queued', 'sent', 'working'].includes(status);
const labels = {queued:'待派工',sent:'已派工',working:'處理中',done:'完成',partial:'部分完成',blocked:'受阻',send_failed:'派工失敗'};

export function taskStatus(task) {
  return `<div class="grok-status"><span class="chip">Grok · ${esc(labels[task.status] || task.status)}</span>
    ${task.error ? `<small>${esc(task.error)}</small>` : ''}
    ${task.status === 'send_failed' ? `<button class="btn small" type="button" data-grok-resend="${esc(task.id)}">重送</button>` : ''}
    ${Object.entries(task.platforms || {}).filter(([, state]) => state.status === 'blocked').map(([source, state]) =>
      `<p>${tag(source)} 受阻 · ${esc(state.reason || '無法讀取商品頁')}${source === 'coupang' ? '（維持飛比參考價）' : ''}</p>`).join('')}</div>`;
}

export function questionForms(task) {
  return task.questions.filter(q => q.answer === null).map(q => `<div class="grok-question" data-question="${esc(q.id)}" data-task="${esc(task.id)}">
    <p><strong>Grok 提問</strong> ${esc(q.question)}</p>
    ${q.options.length ? `<label>選擇答案<select aria-label="選擇答案"><option value="">請選擇</option>${q.options.map(value => `<option>${esc(value)}</option>`).join('')}</select></label>` : ''}
    <label>自填答案<input aria-label="自填答案"></label><button type="button" class="btn small" data-grok-answer>送出答案</button></div>`).join('');
}

export async function taskAction(button) {
  if (button.dataset.grokResend) {
    await api('POST', taskPath(button.dataset.grokResend) + '/resend');
  } else if (button.hasAttribute('data-grok-answer')) {
    const question = button.closest('[data-question]');
    const answer = question.querySelector('input').value.trim() || question.querySelector('select')?.value;
    if (!answer) throw new Error('請選擇或輸入答案');
    await api('POST', taskPath(question.dataset.task) + '/questions/' + encodeURIComponent(question.dataset.question) + '/answer', {answer});
    toast('已送出答案');
  } else return false;
  return true;
}
