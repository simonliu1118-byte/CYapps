import {ApiError} from './http.js';
import {auditStmt} from './audit.js';
export const bot = {employeeId:'grok-bot',displayName:'Grok Bot'};
// One outstanding judge task owns a product until delivery/review work ends or a user stops it.
export const openJudgeStates = "('queued','sent','working','send_failed')";
export const noOtherJudge = (states=openJudgeStates) => `NOT EXISTS (
  SELECT 1 FROM grok_tasks other WHERE other.product_id=grok_tasks.product_id
  AND other.id<>grok_tasks.id AND other.status IN ${states}
)`;
export const deliveryExpired = task => task.status==='queued' && Date.now()-Date.parse(task.updated_at)>30_000;
export const judgeBusy = () => new ApiError(409,'這個商品已有 Grok 任務，請先重送或停止目前的任務');
export const draftFields = ['name','model','attributes','packageQuantity','packageUnit','keyword','momoKeyword','feebeeKeyword'];
export function conditions(row) {
  return {model:row.model,attributes:JSON.parse(row.attributes_json),packageQuantity:row.package_quantity,packageUnit:row.package_unit};
}
export async function taskById(db,id) {
  const t = await db.prepare('SELECT * FROM grok_tasks WHERE id=?').bind(id).first();
  if (!t) throw new ApiError(404,'找不到任務');
  return t;
}
export async function taskDetail(db,t) {
  const items=(await db.prepare('SELECT * FROM grok_task_items WHERE task_id=? ORDER BY source,listing_key').bind(t.id).all()).results;
  const questions=(await db.prepare('SELECT * FROM grok_questions WHERE task_id=? ORDER BY created_at').bind(t.id).all()).results;
  return {id:t.id,kind:t.kind,productId:t.product_id,status:t.status,conditions:JSON.parse(t.conditions_json),
    draft:JSON.parse(t.draft_json),platforms:JSON.parse(t.platform_status_json),error:t.error,
    items:items.map(i=>({...JSON.parse(i.snapshot_json),status:i.status,reason:i.recheck_reason,result:JSON.parse(i.result_json)})),
    questions:questions.map(q=>({id:q.id,question:q.question,options:JSON.parse(q.options_json),answer:q.answer,source:q.source,listingKey:q.listing_key}))};
}
export function eventUpdate(db,event,task,questionId=null) {
  return db.prepare(`UPDATE grok_tasks SET status='queued',event_json=?,event_id=?,error='',updated_at=?
    WHERE id=? AND event_id=? AND status=? AND (kind='draft' OR ${noOtherJudge()})
      AND (? IS NULL OR EXISTS(SELECT 1 FROM grok_questions WHERE id=? AND task_id=grok_tasks.id AND answer IS NULL))`)
    .bind(JSON.stringify(event),event.eventId,new Date().toISOString(),event.taskId,task.event_id,task.status,questionId,questionId);
}
export function stopJudges(db,productId,now,{deletedRevision,emptyOnly=false}={}) {
  const deleted = deletedRevision === undefined ? '' : 'AND EXISTS(SELECT 1 FROM tracked_products WHERE id=grok_tasks.product_id AND deleted_at=? AND revision=?)';
  const params = deleted ? [now,deletedRevision] : [];
  const empty = emptyOnly ? `AND changes()>0 AND NOT EXISTS(
    SELECT 1 FROM grok_task_items i JOIN latest_candidates c
      ON c.product_id=i.product_id AND c.source=i.source AND c.listing_key=i.listing_key AND c.run_id=i.run_id
    WHERE i.task_id=grok_tasks.id AND i.status='waiting'
  )` : '';
  return db.prepare(`UPDATE grok_tasks SET status='cancelled',error='',updated_at=?
    WHERE product_id=? AND kind='judge' AND status IN ${openJudgeStates} ${deleted} ${empty}`).bind(now,productId,...params);
}
export const candidateJoin = `LEFT JOIN grok_task_items gi ON gi.rowid=(
  SELECT i.rowid FROM grok_task_items i JOIN grok_tasks t ON t.id=i.task_id AND t.status<>'discarded' WHERE i.product_id=c.product_id AND i.source=c.source
  AND i.listing_key=c.listing_key AND i.run_id=c.run_id AND i.status<>'waiting' ORDER BY i.rowid DESC LIMIT 1)`;
export const candidateCols='gi.task_id AS grok_id,gi.status AS grok_status,gi.result_json AS grok_result';
export async function currentItem(db,t,source,key) {
  const item=await db.prepare('SELECT * FROM grok_task_items WHERE task_id=? AND source=? AND listing_key=?').bind(t.id,source,key).first();
  const c=await db.prepare('SELECT * FROM latest_candidates WHERE product_id=? AND source=? AND listing_key=?').bind(t.product_id,source,key).first();
  if (!item || !c) throw new ApiError(404,'找不到本輪候選');
  const product=await db.prepare('SELECT * FROM tracked_products WHERE id=? AND deleted_at IS NULL').bind(t.product_id).first();
  if (!product) throw new ApiError(404,'找不到這個追蹤商品');
  const saved=JSON.parse(t.conditions_json), live=conditions(product);
  const sameAttrs=Object.keys(saved.attributes).length===Object.keys(live.attributes).length && Object.entries(saved.attributes).every(([k,v])=>live.attributes[k]===v);
  if (item.run_id!==c.run_id || !sameAttrs || live.model!==saved.model || live.packageUnit!==saved.packageUnit || live.packageQuantity!==saved.packageQuantity) {
    throw new ApiError(409,'商品條件或清單已更新，請重新整理');
  }
  return {item,c,product};
}
export const audit=(db,actor,action,id,after)=>auditStmt(db,actor,new Date().toISOString(),action,id,null,after);
