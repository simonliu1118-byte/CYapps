import {ApiError} from './http.js';
import {auditStmt} from './audit.js';
export const configured = env => Boolean(env.GROK_WEBHOOK_URL && env.GROK_WEBHOOK_TOKEN && env.GROK_API_TOKEN);
export function eventFor(taskId, event, extra = {}) {
  return {schemaVersion:1, event, eventId:crypto.randomUUID(), taskId, taskVersion:1, ...extra};
}
export async function dispatch(env, event, actor) {
  if (!configured(env)) throw new ApiError(503,'Grok 尚未設定');
  let ok = false;
  try {
    const response = await fetch(env.GROK_WEBHOOK_URL, {method:'POST', redirect:'error',
      headers:{authorization:'Bearer ' + env.GROK_WEBHOOK_TOKEN,'content-type':'application/json'},
      body:JSON.stringify(event)});
    ok = response.ok;
  } catch { /* An uncertain delivery is retried with its original event ID. */ }
  const status = ok ? 'sent' : 'send_failed';
  const now = new Date().toISOString(), db = env.DB;
  await db.batch([
    db.prepare("UPDATE grok_tasks SET status=?,error=?,updated_at=? WHERE id=? AND event_id=? AND status IN ('queued','send_failed')")
      .bind(status,ok ? '' : '派工未確認，請重送',now,event.taskId,event.eventId),
    auditStmt(db,actor,now,'grok.dispatch',event.taskId,null,{status},true)
  ]);
  return {id:event.taskId,status};
}
