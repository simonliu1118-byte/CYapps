import {ApiError} from './http.js';
import {auditStmt} from './audit.js';
import {taskById} from './grok-store.js';
export const configured = env => Boolean(env.GROK_WEBHOOK_URL && env.GROK_WEBHOOK_TOKEN && env.GROK_API_TOKEN);
export function eventFor(taskId, event, extra = {}) {
  return {schemaVersion:1, event, eventId:crypto.randomUUID(), taskId, taskVersion:1, ...extra};
}
export async function dispatch(env, event, actor) {
  if (!configured(env)) throw new ApiError(503,'Grok 尚未設定');
  const task = await taskById(env.DB,event.taskId);
  if (task.status!=='queued' || task.event_id!==event.eventId) return {id:task.id,status:task.status};
  // An uncertain delivery is retried with its original event ID; the reason is shown so a wrong URL or key can be told apart.
  let ok = false, reason = '';
  try {
    const response = await fetch(env.GROK_WEBHOOK_URL, {method:'POST', redirect:'manual', signal:AbortSignal.timeout(15000),
      headers:{authorization:'Bearer ' + env.GROK_WEBHOOK_TOKEN,'content-type':'application/json'},
      body:JSON.stringify(event)});
    ok = response.ok;
    reason = 'HTTP ' + response.status;
    response.body?.cancel().catch(() => {});
  } catch (error) {
    reason = error?.name==='TimeoutError' || error?.name==='AbortError' ? 'Webhook 回應逾時' : '連線失敗：' + (error?.name || 'Error');
  }
  const status = ok ? 'sent' : 'send_failed';
  const now = new Date().toISOString(), db = env.DB;
  const [updated] = await db.batch([
    db.prepare("UPDATE grok_tasks SET status=?,error=?,updated_at=? WHERE id=? AND event_id=? AND status IN ('queued','send_failed')")
      .bind(status,ok ? '' : `派工未確認（${reason}），請重送`,now,event.taskId,event.eventId),
    auditStmt(db,actor,now,'grok.dispatch',event.taskId,null,{status},true)
  ]);
  return {id:event.taskId,status:updated.meta.changes ? status : (await taskById(db,event.taskId)).status};
}
