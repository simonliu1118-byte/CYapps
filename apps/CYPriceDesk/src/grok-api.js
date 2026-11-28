import {ApiError,json,readBody} from './http.js';
import {bot,audit,currentItem,taskById,taskDetail,draftFields} from './grok-store.js';
import {validateLink} from '../public/core.js';

async function authenticated(request,env) {
  if (!env.GROK_API_TOKEN) return false;
  const encoder=new TextEncoder();
  const digest=s=>crypto.subtle.digest('SHA-256',encoder.encode(s));
  const [a,b]=await Promise.all([digest(request.headers.get('authorization')||''),digest('Bearer '+env.GROK_API_TOKEN)]);
  const left=new Uint8Array(a), right=new Uint8Array(b);
  let different=0;
  for (let i=0;i<left.length;i++) different |= left[i]^right[i];
  return different===0;
}
function invalid(){throw new ApiError(400,'回填資料不完整');}
function validateResult(data) {
  if (!['same','other_package','not_same','insufficient'].includes(data.verdict) || !data.evidence?.trim?.() ||
      !data.spec || !Number.isFinite(Date.parse(data.checkedAt)) ||
      !(data.price===null || (Number.isFinite(data.price) && data.price>=0))) invalid();
  if (data.verdict==='other_package' && (!(data.spec.packageQuantity>0) || !data.spec.packageUnit)) invalid();
  try { data.url = validateLink(data.url); } catch { invalid(); }
}
export async function handleGrokApi(request,env,url) {
  if (!await authenticated(request,env)) return json({error:'未授權'},401);
  const [id,action,source,key,...extra]=url.pathname.slice('/api/grok/tasks/'.length).split('/').map(decodeURIComponent);
  const db=env.DB, now=new Date().toISOString();
  try {
    const t=await taskById(db,id);
    if (!action && !source && !key && !extra.length && request.method==='GET') {
      if (['sent','queued','send_failed'].includes(t.status)) {
        const [changed]=await db.batch([db.prepare("UPDATE grok_tasks SET status='working',updated_at=? WHERE id=? AND status IN ('sent','queued','send_failed')").bind(now,id),
          auditStmtAfter(db,now,'grok.claim',id)]);
        if (changed.meta.changes) t.status='working';
      }
      return json(await taskDetail(db,t));
    }
    if (['applied','discarded'].includes(t.status)) throw new ApiError(409,'任務已結束');
    const data=await readBody(request);
    let stmt;
    if (action==='draft' && request.method==='PUT' && t.kind==='draft' && !source) {
      if (!data.fields || typeof data.fields!=='object' || !data.evidence || typeof data.evidence!=='object' || !Array.isArray(data.searches)) invalid();
      const draft={fields:Object.fromEntries(draftFields.filter(k=>k in data.fields).map(k=>[k,data.fields[k]])),evidence:data.evidence,searches:data.searches};
      stmt=db.prepare("UPDATE grok_tasks SET draft_json=?,status='done',updated_at=? WHERE id=? AND status NOT IN ('applied','discarded')").bind(JSON.stringify(draft),now,id);
    } else if (action==='results' && request.method==='PUT' && t.kind==='judge' && key && !extra.length) {
      const current = await readCurrentItem(db,t,source,key,now);
      validateResult(data);
      if(current.item.status==='reviewed') throw new ApiError(409,'候選已複審');
      stmt=db.prepare(`UPDATE grok_task_items SET result_json=?,status='answered' WHERE task_id=? AND source=? AND listing_key=? AND status IN ('waiting','answered')
        AND EXISTS(SELECT 1 FROM latest_candidates c WHERE c.product_id=grok_task_items.product_id AND c.source=grok_task_items.source AND c.listing_key=grok_task_items.listing_key AND c.run_id=grok_task_items.run_id)
        AND EXISTS(SELECT 1 FROM tracked_products p WHERE p.id=grok_task_items.product_id AND p.model=? AND p.attributes_json=? AND p.package_quantity=? AND p.package_unit=?)`)
        .bind(JSON.stringify({verdict:data.verdict,option:data.option??'',spec:data.spec,price:data.price,
          priceConditions:data.priceConditions??'',seller:data.seller??'',url:data.url,checkedAt:data.checkedAt,evidence:data.evidence}),id,source,key,current.product.model,current.product.attributes_json,current.product.package_quantity,current.product.package_unit);
    } else if (action==='questions' && request.method==='POST' && !source) {
      if (!data.question?.trim?.() || !Array.isArray(data.options??[])) invalid();
      stmt=db.prepare('INSERT INTO grok_questions(id,task_id,source,listing_key,question,options_json,created_at) VALUES(?,?,?,?,?,?,?)')
        .bind(crypto.randomUUID(),id,data.source??null,data.listingKey??null,data.question,JSON.stringify(data.options??[]),now);
    } else if (action==='progress' && request.method==='POST' && t.kind==='judge' && !source) {
      const platforms=JSON.parse(t.platform_status_json);
      if (!Object.hasOwn(platforms,data.source) || !['working','done','blocked'].includes(data.status)) invalid();
      const update=db.prepare('UPDATE grok_tasks SET platform_status_json=json_set(platform_status_json,?,json(?)),updated_at=? WHERE id=?')
        .bind('$.'+data.source,JSON.stringify({status:data.status,reason:data.reason||''}),now,id);
      const finish=db.prepare(`UPDATE grok_tasks SET status=CASE
        WHEN EXISTS(SELECT 1 FROM json_each(platform_status_json) WHERE json_extract(value,'$.status')='working') THEN 'working'
        WHEN NOT EXISTS(SELECT 1 FROM json_each(platform_status_json) WHERE json_extract(value,'$.status')='blocked') THEN 'done'
        WHEN NOT EXISTS(SELECT 1 FROM json_each(platform_status_json) WHERE json_extract(value,'$.status')='done') THEN 'blocked' ELSE 'partial' END WHERE id=?`).bind(id);
      await db.batch([update,finish,audit(db,bot,'grok.progress',id,data)]);return json({ok:true});
    } else return json({error:'找不到接口'},404);
    const [change]=await db.batch([stmt,auditStmtAfter(db,now,'grok.'+action,id)]);
    if (!change.meta.changes) {
      if (action==='results') await readCurrentItem(db,t,source,key,now);
      throw new ApiError(409,'任務或候選已更新');
    }
    return json({ok:true});
  } catch(e){if(e instanceof ApiError)return json({error:e.message},e.status);throw e;}
}
import {auditStmt} from './audit.js';
const auditStmtAfter=(db,now,action,id)=>auditStmt(db,bot,now,action,id,null,{name:'Grok 任務'},true);

async function readCurrentItem(db, task, source, key, now) {
  try {
    return await currentItem(db,task,source,key);
  } catch (error) {
    if (error.status===409) {
      await db.batch([
        db.prepare("UPDATE grok_task_items SET status='expired' WHERE task_id=? AND source=? AND listing_key=? AND status NOT IN ('reviewed','expired')").bind(task.id,source,key),
        auditStmtAfter(db,now,'grok.expired',task.id)
      ]);
    }
    throw error;
  }
}
