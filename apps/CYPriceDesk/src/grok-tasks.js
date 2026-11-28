import {ApiError,json,readBody} from './http.js';
import {configured,eventFor,dispatch} from './grok-client.js';
import {audit,conditions,currentItem,draftFields,eventUpdate,taskById,taskDetail} from './grok-store.js';
import {writeJudgment} from './tracking.js';

async function createTask(env,actor,data) {
  if (!configured(env)) throw new ApiError(503,'Grok 尚未設定');
  const db=env.DB, id=crypto.randomUUID(), event=eventFor(id,'pricedesk.task.ready'), now=new Date().toISOString();
  let snapshot, productId=null, rows=[];
  if (data.kind==='draft') {
    if (!data.condition?.trim?.()) throw new ApiError(400,'請輸入商品條件');
    const fields=Object.fromEntries(draftFields.map(k=>[k,data.fields?.[k]??'']));
    snapshot={condition:data.condition,fields};
  } else if(data.kind==='judge') {
    const product=await db.prepare('SELECT * FROM tracked_products WHERE id=?').bind(data.productId).first();
    if(!product) throw new ApiError(404,'找不到商品');
    productId=product.id;snapshot=conditions(product);
    rows=(await db.prepare("SELECT * FROM latest_candidates WHERE product_id=? AND source IN ('momo','coupang')").bind(productId).all()).results;
    rows=rows.filter(c=>data.items?.length ? data.items.some(i=>i.source===c.source && i.listingKey===c.listing_key && i.runId===c.run_id) : ['pending','other'].includes(c.grp));
    if (!rows.length || (data.items?.length && rows.length!==data.items.length)) throw new ApiError(409,'沒有可送出的候選，請更新清單');
  } else throw new ApiError(400,'任務類型不正確');
  const platforms=Object.fromEntries(rows.map(c=>[c.source,{status:'working'}]));
  await db.batch([db.prepare(`INSERT INTO grok_tasks(id,kind,product_id,status,event_id,event_json,conditions_json,platform_status_json,created_at,created_by,updated_at)
    VALUES(?,?,?,'queued',?,?,?,?,?,?,?)`).bind(id,data.kind,productId,event.eventId,JSON.stringify(event),JSON.stringify(snapshot),JSON.stringify(platforms),now,actor.employeeId,now),
    ...rows.map(c=>db.prepare('INSERT INTO grok_task_items(task_id,product_id,source,listing_key,run_id,snapshot_json) VALUES(?,?,?,?,?,?)')
      .bind(id,productId,c.source,c.listing_key,c.run_id,JSON.stringify({source:c.source,listingKey:c.listing_key,runId:c.run_id,title:c.title,url:c.url,seller:c.seller,price:c.price}))),
    audit(db,actor,'grok.create',id,{kind:data.kind,count:rows.length})]);
  return dispatch(env,event,actor);
}
export async function handleGrokTasks(req,env,url,actor) {
  if (!/^\/api\/grok-tasks(?:\/|$)/.test(url.pathname)) return null;
  const [, , ,id,part,a,b,op]=url.pathname.split('/').map(decodeURIComponent), db=env.DB;
  try {
    if (!id && req.method==='POST') return json(await createTask(env,actor,await readBody(req)),201);
    if (!id && req.method==='GET') {
      const tasks=(await db.prepare("SELECT t.id,t.kind,t.status,t.product_id AS productId,t.error,t.platform_status_json AS platforms,         COALESCE(json_extract(t.draft_json,'$.fields.name'),json_extract(t.conditions_json,'$.condition')) AS label,         EXISTS(SELECT 1 FROM grok_questions q WHERE q.task_id=t.id AND q.answer IS NULL) AS hasQuestions         FROM grok_tasks t WHERE t.status NOT IN ('discarded','applied') AND         (t.kind='draft' OR t.status NOT IN ('done','partial','blocked') OR t.rowid=(           SELECT last.rowid FROM grok_tasks last WHERE last.product_id=t.product_id AND last.kind='judge' ORDER BY last.rowid DESC LIMIT 1))         ORDER BY t.created_at DESC").all()).results;
      return json({configured:configured(env),tasks:tasks.map(t=>({...t,platforms:JSON.parse(t.platforms)}))});
    }
    const t=await taskById(db,id);
    if (!part && req.method==='GET') return json(await taskDetail(db,t));
    if (!part && req.method==='DELETE' && t.kind==='draft' && !['applied','discarded'].includes(t.status)) {
      await db.batch([db.prepare("UPDATE grok_tasks SET status='discarded' WHERE id=? AND status NOT IN ('applied','discarded')").bind(id),audit(db,actor,'grok.discard',id,{kind:t.kind})]);
      return json({ok:true});
    }
    if(req.method!=='POST') throw new ApiError(404,'找不到接口');
    if(part==='resend' && t.status==='send_failed') {
      await db.batch([audit(db,actor,'grok.resend',id,{kind:t.kind})]);
      return json(await dispatch(env,JSON.parse(t.event_json),actor));
    }
    if(part==='questions' && b==='answer' && !['applied','discarded'].includes(t.status)) {
      const {answer}=await readBody(req);
      if(!answer?.trim?.()) throw new ApiError(400,'請填答案');
      const q=await db.prepare('SELECT id FROM grok_questions WHERE id=? AND task_id=? AND answer IS NULL').bind(a,id).first();
      if(!q)throw new ApiError(409,'問題已更新');
      const event=eventFor(id,'pricedesk.question.answered',{questionId:a});
      await db.batch([db.prepare('UPDATE grok_questions SET answer=?,answered_at=?,answered_by=? WHERE id=? AND task_id=?').bind(answer,new Date().toISOString(),actor.employeeId,a,id),
        eventUpdate(db,event),audit(db,actor,'grok.answer',id,{answer})]);
      return json(await dispatch(env,event,actor));
    }
    if(part==='items' && t.kind==='judge') {
      const {item}=await currentItem(db,t,a,b);
      if(item.status!=='answered') throw new ApiError(409,'候選尚未回填或已處理');
      if(op==='approve') {
        const result=JSON.parse(item.result_json);
        return await writeJudgment(db,actor,t.product_id,'PUT',{source:a,listingKey:b,verdict:result.verdict,
          packageQuantity:result.spec.packageQuantity,packageUnit:result.spec.packageUnit,price:result.price,grokTaskId:id},item.result_json);
      }
      if(op==='recheck') {
        const {reason}=await readBody(req);
        if(!reason?.trim?.())throw new ApiError(400,'請填再查原因');
        const event=eventFor(id,'pricedesk.review.recheck',{resultId:{source:a,listingKey:b}});
        await db.batch([db.prepare("UPDATE grok_task_items SET status='waiting',recheck_reason=? WHERE task_id=? AND source=? AND listing_key=?").bind(reason,id,a,b),
          db.prepare("UPDATE grok_tasks SET platform_status_json=json_set(platform_status_json,?,json(?)) WHERE id=?").bind('$.'+a,'{"status":"working"}',id),
          eventUpdate(db,event),audit(db,actor,'grok.recheck',id,{source:a,reason})]);
        return json(await dispatch(env,event,actor));
      }
    }
    throw new ApiError(404,'找不到接口');
  } catch (error) {
    if (error instanceof ApiError) return json({error:error.message},error.status);
    if (String(error.message).includes('malformed JSON')) return json({error:'資料已更新，請重新整理'},409);
    throw error;
  }
}
