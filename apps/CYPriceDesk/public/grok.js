import {api, esc, tag, toast} from './ui.js';

export const tasksPath = '/api/grok-tasks';
export const taskPath = id => tasksPath + '/' + encodeURIComponent(id);
export const taskList = () => api('GET', tasksPath);
export const running = status => ['queued', 'sent', 'working'].includes(status);
const labels = {queued:'派工中',sent:'等待 Grok 領取',working:'判讀中',done:'完成',partial:'部分完成',blocked:'受阻',
  send_failed:'派工失敗',cancelled:'已停止',delivery_timeout:'派工逾時',unclaimed:'領取逾時',stalled:'判讀逾時',question:'等待你的回答'};

export function judgePhase(task, now=Date.now()) {
  const idle = now - Date.parse(task.updatedAt);
  if (task.status==='queued' && idle>30_000) return 'delivery_timeout';
  if (task.status==='sent' && idle>10*60_000) return 'unclaimed';
  if (task.status==='working' && task.hasQuestions) return 'question';
  if (task.status==='working' && idle>20*60_000) return 'stalled';
  return task.status;
}
export const phaseLabel = phase => labels[phase] || phase;
const phaseHints = {
  delivery_timeout:'PriceDesk 未收到派工結果，可以重送同一任務。',
  unclaimed:'Webhook 已接受派工，Grok 在 10 分鐘內沒有領取任務。',
  stalled:'20 分鐘沒有收到新的判讀或進度。'
};

export function taskStatus(task, {phase=task.status, stopCount=0, canRetry=true}={}) {
  const retry = canRetry && ['send_failed','delivery_timeout'].includes(phase);
  return `<div class="grok-status"><span class="chip" role="status">Grok · ${esc(phaseLabel(phase))}</span>
    ${task.itemCount>0 ? `<small>${task.answeredCount} / ${task.itemCount} 筆已處理</small>` : ''}
    ${task.error || phaseHints[phase] ? `<small>${esc(task.error || phaseHints[phase])}</small>` : ''}
    ${retry ? `<button class="btn small" type="button" data-grok-resend="${esc(task.id)}">重送</button>` : ''}
    ${stopCount ? `<button class="btn small" type="button" data-grok-stop="${esc(task.id)}">${stopCount>1 ? `停止全部 ${stopCount} 筆` : '停止任務'}</button>` : ''}
    ${Object.entries(task.platforms || {}).filter(([, state]) => state.status === 'blocked').map(([source, state]) =>
      `<p>${tag(source)} 受阻 · ${esc(state.reason || '無法讀取商品頁')}${source === 'coupang' ? '（維持飛比參考價）' : ''}</p>`).join('')}</div>`;
}

export function questionForms(task) {
  return task.questions.filter(q => q.answer === null).map(q => `<fieldset class="grok-question" data-question="${esc(q.id)}" data-task="${esc(task.id)}">
    <legend>Grok 提問</legend>
    <p id="grok-question-${esc(q.id)}" class="grok-question-text">${esc(q.question)}</p>
    <div class="grok-answers" role="group" aria-labelledby="grok-question-${esc(q.id)}">
      ${q.options.map(value => `<label><input type="radio" name="answer-${esc(q.id)}" value="${esc(value)}"><span>${esc(value)}</span></label>`).join('')}
      ${q.options.length ? `<label><input type="radio" name="answer-${esc(q.id)}" data-custom-answer><span>自填答案</span></label>` : ''}
    </div>
    <label data-grok-custom ${q.options.length ? 'hidden' : ''}>輸入答案<textarea rows="2" aria-label="輸入答案"></textarea></label>
    <div class="grok-answer-footer"><span data-grok-feedback role="status"></span><button type="button" class="btn primary small" data-grok-answer disabled>送出回答</button></div>
  </fieldset>`).join('');
}

function questionAnswer(question) {
  const selected = question.querySelector('input[type=radio]:checked');
  return selected && !selected.hasAttribute('data-custom-answer') ? selected.value
    : question.querySelector('textarea').value.trim();
}
function updateQuestion(event) {
  const question = event.target.closest('[data-question]');
  if (!question) return;
  if (event.target.matches('input[type=radio]')) {
    const custom = question.querySelector('[data-grok-custom]');
    custom.hidden = !event.target.hasAttribute('data-custom-answer');
    if (!custom.hidden) custom.querySelector('textarea').focus();
  }
  question.querySelector('[data-grok-answer]').disabled = !questionAnswer(question);
  question.querySelector('[data-grok-feedback]').textContent = '';
}
document.addEventListener('change', updateQuestion);
document.addEventListener('input', updateQuestion);

export async function taskAction(button) {
  if (button.dataset.grokResend) {
    await api('POST', taskPath(button.dataset.grokResend) + '/resend');
  } else if (button.dataset.grokStop) {
    const {stopped} = await api('POST', taskPath(button.dataset.grokStop) + '/stop');
    toast(stopped ? '已停止 Grok 任務' : '任務已結束');
  } else if (button.hasAttribute('data-grok-answer')) {
    const question = button.closest('[data-question]');
    const answer = questionAnswer(question);
    if (!answer) return true;
    const feedback = question.querySelector('[data-grok-feedback]');
    feedback.textContent = '送出中…';
    try {
      const task = await api('POST', taskPath(question.dataset.task) + '/questions/' + encodeURIComponent(question.dataset.question) + '/answer', {answer});
      feedback.textContent = task.status==='send_failed' ? '已保存回答，可重送通知。' : '回答已送出';
      toast(feedback.textContent);
    } catch (error) { feedback.textContent = error.message; }
  } else return false;
  return true;
}
