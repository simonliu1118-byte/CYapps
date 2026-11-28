// Signed-in user, the 操作紀錄 page and the initial 待確認 badge.
import {displayTime} from './core.js';
import {$, api, esc} from './ui.js';
import {loadPending} from './pending.js';

const ACTION_LABELS = {'classification.update': '設定規格', 'classification.delete': '清除規格', 'tracked.create': '追蹤新增', 'tracked.update': '追蹤修改', 'tracked.price': '調整售價', 'tracked.run': '立即查價',
  'manual.create': '手動比價新增', 'manual.update': '手動比價修改', 'manual.delete': '手動比價刪除', 'judgment.update': '判斷', 'judgment.delete': '恢復判斷',
  'candidate.delete':'刪除當輪候選','grok.dispatch':'Grok 派工結果','grok.create':'Grok 派工','grok.claim':'Grok 領取','grok.progress':'Grok 進度',
  'grok.draft':'Grok 補完','grok.results':'Grok 判讀','grok.questions':'Grok 提問','grok.answer':'回答 Grok',
  'grok.review':'複審 Grok 判讀','grok.expired':'Grok 過期','grok.recheck':'Grok 再查','grok.discard':'捨棄補完','grok.resend':'重送 Grok'};

async function loadAudit() {
  $('audit-list').innerHTML = '<p class="muted">載入中…</p>';
  try {
    const {entries} = await api('GET', '/api/audit?limit=100');
    $('audit-list').innerHTML = entries.length
      ? `<div class="table-wrap"><table class="data-table wide"><thead><tr><th>時間</th><th>操作者</th><th>動作</th><th>內容</th></tr></thead><tbody>${entries.map(x => `<tr><td>${esc(displayTime(x.at))}</td><td>${esc(x.actor)}</td><td><span class="chip">${esc(ACTION_LABELS[x.action] || x.action)}</span></td><td>${esc(x.detail)}</td></tr>`).join('')}</tbody></table></div>`
      : '<div class="empty"><strong>尚無操作紀錄</strong></div>';
  } catch (error) { $('audit-list').innerHTML = `<div class="notice error">${esc(error.message)}</div>`; }
}
const route = () => { if (location.hash === '#audit') loadAudit(); };
window.addEventListener('hashchange', route);
route();

(async () => {
  try {
    const me = await api('GET', '/api/me');
    const name = me.displayName || me.employeeNo;
    document.querySelectorAll('[data-user-name]').forEach(x => { x.textContent = name; });
    document.querySelectorAll('[data-user-avatar]').forEach(x => { x.textContent = [...name][0] || ''; });
    await loadPending();
  } catch { /* api() already sends the browser back to / on 401 (the Worker then starts the CY Web sign-in) */ }
})();
