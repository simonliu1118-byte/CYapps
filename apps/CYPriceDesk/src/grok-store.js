import {ApiError} from './http.js';
import {auditStmt} from './audit.js';
export const bot = {employeeId:'grok-bot',displayName:'Grok Bot'};
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
export function eventUpdate(db,event) {
  return db.prepare("UPDATE grok_tasks SET status='queued',event_json=?,event_id=?,error='',updated_at=? WHERE id=?")
    .bind(JSON.stringify(event),event.eventId,new Date().toISOString(),event.taskId);
}
export const candidateJoin = `LEFT JOIN grok_task_items gi ON gi.rowid=(
  SELECT i.rowid FROM grok_task_items i WHERE i.product_id=c.product_id AND i.source=c.source
  AND i.listing_key=c.listing_key AND i.run_id=c.run_id ORDER BY i.rowid DESC LIMIT 1)`;
export const candidateCols='gi.task_id AS grok_id,gi.status AS grok_status,gi.result_json AS grok_result';
export async function currentItem(db,t,source,key) {
  const item=await db.prepare('SELECT * FROM grok_task_items WHERE task_id=? AND source=? AND listing_key=?').bind(t.id,source,key).first();
  const c=await db.prepare('SELECT * FROM latest_candidates WHERE product_id=? AND source=? AND listing_key=?').bind(t.product_id,source,key).first();
  if (!item || !c) throw new ApiError(404,'找不到本輪候選');
  const product=await db.prepare('SELECT * FROM tracked_products WHERE id=?').bind(t.product_id).first();
  const saved=JSON.parse(t.conditions_json), live=conditions(product);
  const sameAttrs=Object.keys(saved.attributes).length===Object.keys(live.attributes).length && Object.entries(saved.attributes).every(([k,v])=>live.attributes[k]===v);
  if (item.run_id!==c.run_id || !sameAttrs || live.model!==saved.model || live.packageUnit!==saved.packageUnit || live.packageQuantity!==saved.packageQuantity) {
    throw new ApiError(409,'商品條件或清單已更新，請重新整理');
  }
  return {item,c,product};
}
export const audit=(db,actor,action,id,after)=>auditStmt(db,actor,new Date().toISOString(),action,id,null,after);
