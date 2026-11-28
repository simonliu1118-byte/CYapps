import {ApiError,json,readBody} from './http.js';
import {configured,eventFor,dispatch} from './grok-client.js';
import {auditStmt} from './audit.js';
import {audit,conditions,currentItem,deliveryExpired,draftFields,eventUpdate,judgeBusy,noOtherJudge,openJudgeStates,stopJudges,taskById,taskDetail} from './grok-store.js';
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
    const product=await db.prepare('SELECT * FROM tracked_products WHERE id=? AND deleted_at IS NULL').bind(data.productId).first();
    if(!product) throw new ApiError(404,'找不到商品');
    productId=product.id;snapshot=conditions(product);
    rows=(await db.prepare("SELECT * FROM latest_candidates WHERE product_id=? AND source IN ('momo','coupang')").bind(productId).all()).results;
    rows=rows.filter(c=>data.items?.length ? data.items.some(i=>i.source===c.source && i.listingKey===c.listing_key && i.runId===c.run_id) : ['pending','other'].includes(c.grp));
    if (!rows.length || (data.items?.length && rows.length!==data.items.length)) throw new ApiError(409,'沒有可送出的候選，請更新清單');
  } else throw new ApiError(400,'任務類型不正確');
  const platforms=Object.fromEntries(rows.map(c=>[c.source,{status:'working'}]));
  const available = productId ? `WHERE EXISTS(SELECT 1 FROM tracked_products WHERE id=? AND deleted_at IS NULL)
    AND NOT EXISTS(SELECT 1 FROM grok_tasks WHERE product_id=? AND status IN ${openJudgeStates})` : '';
  const guard = productId ? [productId,productId] : [];
  const [created] = await db.batch([
    db.prepare(`INSERT INTO grok_tasks(id,kind,product_id,status,event_id,event_json,conditions_json,platform_status_json,created_at,created_by,updated_at)
      SELECT ?,?,?,'queued',?,?,?,?,?,?,? ${available}`)
      .bind(id,data.kind,productId,event.eventId,JSON.stringify(event),JSON.stringify(snapshot),JSON.stringify(platforms),now,actor.employeeId,now,...guard),
    auditStmt(db,actor,now,'grok.create',id,null,{kind:data.kind,count:rows.length},true),
    ...rows.map(c => db.prepare(`INSERT INTO grok_task_items(task_id,product_id,source,listing_key,run_id,snapshot_json)
      SELECT ?,?,?,?,?,? WHERE EXISTS(SELECT 1 FROM grok_tasks WHERE id=?)`)
      .bind(id,productId,c.source,c.listing_key,c.run_id,JSON.stringify({source:c.source,listingKey:c.listing_key,runId:c.run_id,title:c.title,url:c.url,seller:c.seller,price:c.price}),id))
  ]);
  if (!created.meta.changes) throw judgeBusy();
  return dispatch(env,event,actor);
}
export async function handleGrokTasks(req,env,url,actor) {
  if (!/^\/api\/grok-tasks(?:\/|$)/.test(url.pathname)) return null;
  const [, , ,id,part,a,b,op]=url.pathname.split('/').map(decodeURIComponent), db=env.DB;
  try {
    if (!id && req.method==='POST') return json(await createTask(env,actor,await readBody(req)),201);
    if (!id && req.method==='GET') {
      const productId = url.searchParams.get('productId');
      const scope = productId ? "t.kind='judge' AND t.product_id=?" : `(t.kind='draft' OR
        t.status IN ${openJudgeStates} OR t.rowid=(
          SELECT last.rowid FROM grok_tasks last WHERE last.product_id=t.product_id
            AND last.kind='judge' AND last.status<>'discarded' ORDER BY last.rowid DESC LIMIT 1))`;
      const params = productId ? [productId] : [];
      const tasks=(await db.prepare(`SELECT t.id,t.kind,t.status,t.product_id AS productId,t.updated_at AS updatedAt,t.created_at AS createdAt,t.error,
        t.platform_status_json AS platforms, COALESCE(json_extract(t.draft_json,'$.fields.name'),json_extract(t.conditions_json,'$.condition')) AS label,
        EXISTS(SELECT 1 FROM grok_questions q WHERE q.task_id=t.id AND q.answer IS NULL) AS hasQuestions,
        (SELECT count(*) FROM grok_task_items i WHERE i.task_id=t.id AND i.status<>'waiting') AS answeredCount,
        (SELECT count(*) FROM grok_task_items i WHERE i.task_id=t.id) AS itemCount
        FROM grok_tasks t WHERE t.status NOT IN ('discarded','applied') AND ${scope} AND
          (t.kind='draft' OR EXISTS(SELECT 1 FROM tracked_products p WHERE p.id=t.product_id AND p.deleted_at IS NULL))
        ORDER BY t.created_at DESC,t.rowid DESC`).bind(...params).all()).results;
      return json({configured:configured(env),tasks:tasks.map(t=>({...t,platforms:JSON.parse(t.platforms)}))});
    }
    if (!id && req.method==='DELETE') {
      const productId = url.searchParams.get('productId');
      if (!productId) throw new ApiError(400,'請指定追蹤商品');
      return json(await discardJudges(db,actor,productId));
    }
    const t=await taskById(db,id);
    if (!part && req.method==='GET') return json(await taskDetail(db,t));
    if (!part && req.method==='DELETE') {
      if (t.kind==='judge') return json(await discardJudges(db,actor,t.product_id,id));
      const now = new Date().toISOString();
      await db.batch([db.prepare("UPDATE grok_tasks SET status='discarded' WHERE id=? AND status NOT IN ('applied','discarded')").bind(id),
        auditStmt(db,actor,now,'grok.discard',id,null,{kind:t.kind},true)]);
      return json({ok:true});
    }
    if(req.method!=='POST') throw new ApiError(404,'找不到接口');
    if (part==='stop' && t.kind==='judge' && !a) {
      const now = new Date().toISOString();
      const [stopped] = await db.batch([
        stopJudges(db,t.product_id,now),
        auditStmt(db,actor,now,'grok.stop',t.product_id,null,{taskId:id},true)
      ]);
      return json({ok:true,stopped:stopped.meta.changes});
    }
    if (part==='resend' && (t.status==='send_failed' || deliveryExpired(t))) {
      if (!configured(env)) throw new ApiError(503,'Grok 尚未設定');
      const now = new Date().toISOString();
      const [queued] = await db.batch([
        db.prepare(`UPDATE grok_tasks SET status='queued',error='',updated_at=?
          WHERE id=? AND status=? AND updated_at=? AND event_id=? AND (kind='draft' OR ${noOtherJudge("('queued','sent','working')")})`)
          .bind(now,id,t.status,t.updated_at,t.event_id),
        auditStmt(db,actor,now,'grok.resend',id,null,{kind:t.kind},true),
        db.prepare(`UPDATE grok_tasks SET status='cancelled',error='',updated_at=?
          WHERE product_id=? AND id<>? AND status='send_failed' AND changes()>0`).bind(now,t.product_id,id)
      ]);
      if (!queued.meta.changes) throw judgeBusy();
      return json(await dispatch(env,JSON.parse(t.event_json),actor));
    }
    if (part==='questions' && b==='answer' && !['applied','discarded','cancelled'].includes(t.status)) {
      if (!configured(env)) throw new ApiError(503,'Grok 尚未設定');
      const {answer}=await readBody(req);
      if(!answer?.trim?.()) throw new ApiError(400,'請填答案');
      const now = new Date().toISOString();
      const event=eventFor(id,'pricedesk.question.answered',{questionId:a});
      const [queued] = await db.batch([
        eventUpdate(db,event,t,a),
        db.prepare('UPDATE grok_questions SET answer=?,answered_at=?,answered_by=? WHERE id=? AND task_id=? AND changes()>0')
          .bind(answer,now,actor.employeeId,a,id),
        auditStmt(db,actor,now,'grok.answer',id,null,{answer},true)
      ]);
      if (!queued.meta.changes) throw new ApiError(409,'問題或任務已更新，請重新整理');
      return json(await dispatch(env,event,actor));
    }
    if(part==='items' && t.kind==='judge' && t.status!=='discarded') {
      const {item}=await currentItem(db,t,a,b);
      if(item.status!=='answered') throw new ApiError(409,'候選尚未回填或已處理');
      if(op==='approve') {
        const result=JSON.parse(item.result_json);
        return await writeJudgment(db,actor,t.product_id,'PUT',{source:a,listingKey:b,verdict:result.verdict,
          packageQuantity:result.spec.packageQuantity,packageUnit:result.spec.packageUnit,price:result.price,grokTaskId:id},item.result_json);
      }
      if(op==='recheck') {
        if (!configured(env)) throw new ApiError(503,'Grok 尚未設定');
        if (!['done','partial','blocked'].includes(t.status)) throw judgeBusy();
        const {reason}=await readBody(req);
        if(!reason?.trim?.())throw new ApiError(400,'請填再查原因');
        const event=eventFor(id,'pricedesk.review.recheck',{resultId:{source:a,listingKey:b}});
        const now = new Date().toISOString();
        const [queued] = await db.batch([
          eventUpdate(db,event,t),
          db.prepare("UPDATE grok_task_items SET status='waiting',recheck_reason=? WHERE task_id=? AND source=? AND listing_key=? AND status='answered' AND changes()>0")
            .bind(reason,id,a,b),
          db.prepare("UPDATE grok_tasks SET platform_status_json=json_set(platform_status_json,?,json(?)) WHERE id=? AND changes()>0").bind('$.'+a,'{"status":"working"}',id),
          auditStmt(db,actor,now,'grok.recheck',id,null,{source:a,reason},true)
        ]);
        if (!queued.meta.changes) throw judgeBusy();
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

async function discardJudges(db,actor,productId,taskId=null) {
  const now = new Date().toISOString();
  const [discarded] = await db.batch([
    db.prepare(`UPDATE grok_tasks SET status='discarded',error='',updated_at=?
      WHERE product_id=? AND kind='judge' AND status<>'discarded' AND (? IS NULL OR id=?)`)
      .bind(now,productId,taskId,taskId),
    auditStmt(db,actor,now,'grok.discard_judge',productId,null,{taskId},true)
  ]);
  return {discarded:discarded.meta.changes};
}
