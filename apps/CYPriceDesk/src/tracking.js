import {candidateJoin,candidateCols,currentItem,stopJudges,taskById} from './grok-store.js';
// Tracked products API: CRUD with revision checks, per-platform own selling price (with history), manual judgments,
// list / detail with the per-platform comparison and trend chart data, and manual run. Every write is audited in the same D1 batch.
import {ApiError, json, readBody} from './http.js';
import {auditStmt} from './audit.js';
import {normalizeKeyword} from './search.js';
import {sanitizeAttributes, normalize} from '../public/catalog-core.js';
import {validateLink} from '../public/core.js';
import {PLATFORMS, buildChart, compareAll, attentionOf, dayOf, manualCounts} from '../public/compare.js';
import {classifyListing, normalizeUnit} from './grouping.js';
import {randomRunTime, runTracked, toTracked} from './tracking-run.js';
import {handleManual, toManual} from './manual.js';

const VERDICTS = ['same', 'other_package', 'not_same'];
const PRICE_COLUMN = {shopee: 'my_price_shopee', momo: 'my_price_momo', coupang: 'my_price_coupang'};
const conflict = () => new ApiError(409, '資料已被他人更新，請重新整理');

function price(value, label) {
  if (value === '' || value === null || value === undefined) return null;
  const n = Number(value);
  if (!Number.isFinite(n) || n < 0 || n > 1e9) throw new ApiError(400, `${label}需為 0 到 1,000,000,000 的數字`);
  return Math.round(n * 100) / 100;
}

function quantityAndUnit(body) {
  const quantity = Number(body.packageQuantity ?? body.quantity);
  const unit = normalizeUnit(String(body.packageUnit ?? body.unit ?? '').trim().slice(0, 20));
  if (!(quantity > 0) || quantity > 100000 || !unit) throw new ApiError(400, '請填入包裝數量與單位');
  return {quantity, unit};
}

const optionalKeyword = value => String(value ?? '').trim() ? normalizeKeyword(value) : '';

function validated(body) {
  const name = String(body.name ?? '').trim();
  const model = normalize(body.model).slice(0, 80);
  if (!name || name.length > 200) throw new ApiError(400, '請填入商品名稱（200 字內）');
  if (!model) throw new ApiError(400, '請填入型號');
  const {quantity, unit} = quantityAndUnit(body);
  const myPrices = {}, myUrls = {};
  for (const p of PLATFORMS) {
    myPrices[p] = price(body.myPrices?.[p], '自己的售價');
    try { myUrls[p] = validateLink(body.myUrls?.[p]); } catch (error) { throw new ApiError(400, error.message); }
  }
  return {
    name, model, attributes: sanitizeAttributes(body.attributes), packageQuantity: quantity, packageUnit: unit,
    cywebItemNo: String(body.cywebItemNo ?? '').trim().slice(0, 40),
    keyword: normalizeKeyword(body.keyword), momoKeyword: optionalKeyword(body.momoKeyword), feebeeKeyword: optionalKeyword(body.feebeeKeyword),
    myPrices, myUrls, active: body.active !== false
  };
}

const toChange = row => ({platform: row.platform, at: row.at, actorName: row.actor_name, oldPrice: row.old_price, newPrice: row.new_price, note: row.note});
export const toCandidate = row => ({runId:row.run_id, grok:row.grok_id ? {taskId:row.grok_id,status:row.grok_status,result:JSON.parse(row.grok_result)} : null, source: row.source, listingKey: row.listing_key, title: row.title, price: row.price, seller: row.seller, url: row.url, group: row.grp, parsed: JSON.parse(row.parsed_json)});
const auditFields = p => ({name: p.name, model: p.model, attributes: p.attributes, packageQuantity: p.packageQuantity, packageUnit: p.packageUnit, cywebItemNo: p.cywebItemNo, keyword: p.keyword, momoKeyword: p.momoKeyword, feebeeKeyword: p.feebeeKeyword, myPrices: p.myPrices, myUrls: p.myUrls, active: p.active});

async function getProduct(DB, id) {
  const row = await DB.prepare('SELECT * FROM tracked_products WHERE id=? AND deleted_at IS NULL').bind(id).first();
  if (!row) throw new ApiError(404, '找不到這個追蹤商品');
  return row;
}

// {platform: [price]} of the same-spec candidates of the latest check.
const samePrices = rows => {
  const by = {};
  for (const r of rows) (by[r.source] ||= []).push(r.price);
  return by;
};

async function list(DB) {
  const products = (await DB.prepare('SELECT * FROM tracked_products WHERE deleted_at IS NULL ORDER BY name, id').all()).results.map(toTracked);
  const same = new Map();
  for (const r of (await DB.prepare("SELECT product_id, source, price FROM latest_candidates WHERE grp='same' AND price IS NOT NULL").all()).results) {
    same.set(r.product_id, [...(same.get(r.product_id) || []), r]);
  }
  const manual = new Map();
  for (const r of (await DB.prepare('SELECT * FROM manual_comparisons').all()).results) manual.set(r.product_id, [...(manual.get(r.product_id) || []), toManual(r)]);
  const pending = new Map((await DB.prepare("SELECT product_id, COUNT(*) n FROM latest_candidates WHERE grp='pending' GROUP BY product_id").all()).results.map(r => [r.product_id, r.n]));
  const today = dayOf(new Date().toISOString());
  return products.map(p => {
    const platforms = compareAll({myPrices: p.myPrices, pricesBy: samePrices(same.get(p.id) || []), manual: manual.get(p.id) || [], today});
    return {...p, platforms, attention: attentionOf(platforms), pendingCount: pending.get(p.id) || 0};
  });
}

async function detail(DB, id) {
  const product = toTracked(await getProduct(DB, id));
  const today = dayOf(new Date().toISOString());
  const run = await DB.prepare('SELECT * FROM tracked_runs WHERE product_id=? ORDER BY started_at DESC LIMIT 1').bind(id).first();
  const candidates = (await DB.prepare(`SELECT c.*, ${candidateCols} FROM latest_candidates c ${candidateJoin} WHERE c.product_id=? ORDER BY c.grp,c.source,c.price,c.listing_key`).bind(id).all()).results.map(toCandidate);
  const manual = (await DB.prepare('SELECT * FROM manual_comparisons WHERE product_id=? ORDER BY checked_at DESC, created_at DESC').bind(id).all()).results.map(toManual);
  const priceChanges = (await DB.prepare('SELECT * FROM my_price_changes WHERE product_id=? ORDER BY at DESC, id DESC').bind(id).all()).results.map(toChange);
  const observations = (await DB.prepare('SELECT run_id, source, price, observed_at FROM price_observations WHERE product_id=? ORDER BY observed_at').bind(id).all()).results
    .map(r => ({runId: r.run_id, source: r.source, at: r.observed_at, price: r.price}));
  const sameRows = candidates.filter(c => c.group === 'same' && c.price !== null).map(c => ({source: c.source, price: c.price}));
  return {
    product,
    platforms: compareAll({myPrices: product.myPrices, pricesBy: samePrices(sameRows), manual, today}),
    chart: buildChart({observations, changes: priceChanges.map(c => ({platform: c.platform, at: c.at, newPrice: c.newPrice})), manual}),
    today,
    priceChanges,
    candidates,
    manual: manual.map(m => ({...m, counted: manualCounts(m, today)})),
    lastRun: run ? {startedAt: run.started_at, momoStatus: run.momo_status, feebeeStatus: run.feebee_status, trigger: run.trigger} : null
  };
}

export async function handleTrackingApi(request, env, url, actor) {
  const path = url.pathname;
  if (path !== '/api/tracked' && !path.startsWith('/api/tracked/')) return null;
  const {DB} = env;
  const method = request.method;
  try {
    if (path === '/api/tracked' && method === 'GET') return json({products: await list(DB)});

    if (path === '/api/tracked' && method === 'POST') {
      const body = await readBody(request);
      const input = validated(body);
      const id = crypto.randomUUID();
      const now = new Date().toISOString();
      const nextRunAt = randomRunTime(Date.now(), 0, 7);
      const statements = [
        DB.prepare('INSERT INTO tracked_products (id,name,model,attributes_json,package_quantity,package_unit,keyword,momo_keyword,feebee_keyword,my_price_shopee,my_price_momo,my_price_coupang,my_url_shopee,my_url_momo,my_url_coupang,cyweb_item_no,active,next_run_at,revision,created_at,created_by,updated_at,updated_by) VALUES (?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,1,?,?,?,?)')
          .bind(id, input.name, input.model, JSON.stringify(input.attributes), input.packageQuantity, input.packageUnit, input.keyword, input.momoKeyword, input.feebeeKeyword,
            input.myPrices.shopee, input.myPrices.momo, input.myPrices.coupang, input.myUrls.shopee, input.myUrls.momo, input.myUrls.coupang, input.cywebItemNo,
            input.active ? 1 : 0, nextRunAt, now, actor.employeeId, now, actor.employeeId)
      ];
      if (body.grokDraftId) {
        statements.unshift(DB.prepare("UPDATE grok_tasks SET status='applied' WHERE id=? AND kind='draft' AND status='done'").bind(body.grokDraftId));
        // Abort this batch if another user has already used the same completion.
        statements.splice(1,0,DB.prepare("SELECT CASE WHEN changes()=0 THEN json('draft_conflict') ELSE 1 END"));
      }
      for (const p of PLATFORMS) {
        if (input.myPrices[p] !== null) statements.push(DB.prepare('INSERT INTO my_price_changes (product_id,platform,at,actor_employee_id,actor_name,old_price,new_price,note) VALUES (?,?,?,?,?,NULL,?,?)').bind(id, p, now, actor.employeeId, actor.displayName, input.myPrices[p], '建立追蹤商品'));
      }
      statements.push(auditStmt(DB, actor, now, 'tracked.create', id, null, auditFields(input)));
      await DB.batch(statements);
      return json({product: toTracked(await getProduct(DB, id))}, 201);
    }

    const pendingGroup = path.match(/^\/api\/tracked\/([^/]+)\/candidates$/);
    if (pendingGroup && method==='DELETE' && url.searchParams.get('group')==='pending') {
      const id = decodeURIComponent(pendingGroup[1]);
      const product = await getProduct(DB,id);
      const {items} = await readBody(request);
      if (!Array.isArray(items) || !items.length) throw new ApiError(400,'請提供待確認候選清單');
      const now = new Date().toISOString();
      const [removed] = await DB.batch([
        DB.prepare(`DELETE FROM latest_candidates WHERE product_id=? AND grp='pending'
          AND EXISTS(SELECT 1 FROM tracked_products WHERE id=? AND deleted_at IS NULL)
          AND (SELECT count(*) FROM latest_candidates WHERE product_id=? AND grp='pending')=?
          AND NOT EXISTS(
            SELECT 1 FROM latest_candidates c WHERE c.product_id=? AND c.grp='pending' AND NOT EXISTS(
              SELECT 1 FROM json_each(?) i WHERE json_extract(i.value,'$.source')=c.source
                AND json_extract(i.value,'$.listingKey')=c.listing_key AND json_extract(i.value,'$.runId')=c.run_id
            )
          )`).bind(id,id,id,items.length,id,JSON.stringify(items)),
        auditStmt(DB,actor,now,'candidate.clear_pending',id,null,{name:product.name,count:items.length},true),
        stopJudges(DB,id,now,{emptyOnly:true}),
        auditStmt(DB,actor,now,'grok.stop',id,null,{reason:'已無本輪待判讀候選'},true)
      ]);
      if (!removed.meta.changes) throw new ApiError(409,'待確認清單已更新，請重新整理後再清除');
      return json({removed:removed.meta.changes});
    }

    const deletion = path.match(/^\/api\/tracked\/([^/]+)\/candidates\/([^/]+)\/([^/]+)$/);
    if (deletion && method === 'DELETE') {
      const [id,source,key] = deletion.slice(1).map(decodeURIComponent);
      await getProduct(DB,id);
      const {runId} = await readBody(request);
      const [removed] = await DB.batch([
        DB.prepare('DELETE FROM latest_candidates WHERE product_id=? AND source=? AND listing_key=? AND run_id=?').bind(id,source,key,runId??null),
        auditStmt(DB,actor,new Date().toISOString(),'candidate.delete',id,null,{source,listingKey:key},true)
      ]);
      if (!removed.meta.changes) throw new ApiError(409,'清單已更新，請重新整理');
      return json({ok:true});
    }
    const match = path.match(/^\/api\/tracked\/([^/]+)(?:\/(price|run|judgments|manual)(?:\/([^/]+))?)?$/);
    if (!match) return null;
    const id = decodeURIComponent(match[1]);
    const part = match[2];

    if (part === 'manual') return await handleManual(request, env, actor, await getProduct(DB, id), match[3] && decodeURIComponent(match[3]));

    if (!part && method === 'GET') return json(await detail(DB, id));
    if (!part && method === 'DELETE') {
      const before = toTracked(await getProduct(DB, id));
      if (Number(url.searchParams.get('revision')) !== before.revision) throw conflict();
      const now = new Date().toISOString();
      const [removed] = await DB.batch([
        DB.prepare('UPDATE tracked_products SET deleted_at=?,active=0,revision=revision+1,updated_at=?,updated_by=? WHERE id=? AND revision=? AND deleted_at IS NULL')
          .bind(now,now,actor.employeeId,id,before.revision),
        auditStmt(DB,actor,now,'tracked.delete',id,auditFields(before),null,true),
        stopJudges(DB,id,now,{deletedRevision:before.revision + 1})
      ]);
      if (!removed.meta.changes) throw conflict();
      return json({ok:true});
    }

    if (!part && method === 'PUT') {
      const body = await readBody(request);
      const before = toTracked(await getProduct(DB, id));
      if (Number(body.revision) !== before.revision) throw conflict();
      // The selling price changes only through /price so every change is recorded.
      const input = {...validated(body), myPrices: before.myPrices};
      const now = new Date().toISOString();
      const [result] = await DB.batch([
        DB.prepare('UPDATE tracked_products SET name=?,model=?,attributes_json=?,package_quantity=?,package_unit=?,keyword=?,momo_keyword=?,feebee_keyword=?,my_url_shopee=?,my_url_momo=?,my_url_coupang=?,cyweb_item_no=?,active=?,revision=revision+1,updated_at=?,updated_by=? WHERE id=? AND revision=?')
          .bind(input.name, input.model, JSON.stringify(input.attributes), input.packageQuantity, input.packageUnit, input.keyword, input.momoKeyword, input.feebeeKeyword,
            input.myUrls.shopee, input.myUrls.momo, input.myUrls.coupang, input.cywebItemNo, input.active ? 1 : 0, now, actor.employeeId, id, before.revision),
        auditStmt(DB, actor, now, 'tracked.update', id, auditFields(before), auditFields(input), true)
      ]);
      if (!result.meta.changes) throw conflict();
      return json({product: toTracked(await getProduct(DB, id))});
    }

    if (part === 'price' && method === 'PUT') {
      const body = await readBody(request);
      if (!PLATFORMS.includes(body.platform)) throw new ApiError(400, '請選擇平台');
      const before = toTracked(await getProduct(DB, id));
      if (Number(body.revision) !== before.revision) throw conflict();
      const newPrice = price(body.price, '售價');
      const note = String(body.note ?? '').trim().slice(0, 200);
      const now = new Date().toISOString();
      const oldPrice = before.myPrices[body.platform];
      // A blank price stops comparing on that platform; the change is still recorded.
      if (newPrice === null && oldPrice === null) throw new ApiError(400, '請填入售價');
      const [result] = await DB.batch([
        DB.prepare(`UPDATE tracked_products SET ${PRICE_COLUMN[body.platform]}=?,revision=revision+1,updated_at=?,updated_by=? WHERE id=? AND revision=?`).bind(newPrice, now, actor.employeeId, id, before.revision),
        DB.prepare('INSERT INTO my_price_changes (product_id,platform,at,actor_employee_id,actor_name,old_price,new_price,note) SELECT ?,?,?,?,?,?,?,? WHERE changes() > 0')
          .bind(id, body.platform, now, actor.employeeId, actor.displayName, oldPrice, newPrice, note),
        auditStmt(DB, actor, now, 'tracked.price', id, {platform: body.platform, myPrice: oldPrice}, {platform: body.platform, myPrice: newPrice, note, name: before.name}, true)
      ]);
      if (!result.meta.changes) throw conflict();
      return json({product: toTracked(await getProduct(DB, id))});
    }

    if (part === 'run' && method === 'POST') {
      const row = await getProduct(DB, id);
      return json(await runTracked(env, row, 'manual', {actor}));
    }

    if (part === 'judgments' && (method === 'PUT' || method === 'DELETE')) {
      const body = method === 'PUT' ? await readBody(request) : {source: url.searchParams.get('source'), listingKey: url.searchParams.get('listingKey')};
      return await writeJudgment(DB, actor, id, method, body);
    }
  } catch (error) {
    if (String(error.message).includes('malformed JSON')) return json({error:'資料已更新，請重新整理'},409);
    if (error instanceof ApiError) return json({error: error.message}, error.status);
    throw error;
  }
  return null;
}

export async function writeJudgment(DB, actor, id, method, body, expectedResult) {
  if (!PLATFORMS.includes(body.source)) throw new ApiError(400, '不支援的查詢來源');
  const row = await getProduct(DB, id);
  const product = toTracked(row);
  const candidate = await DB.prepare('SELECT * FROM latest_candidates WHERE product_id=? AND source=? AND listing_key=?').bind(id, body.source, String(body.listingKey ?? '')).first();
  if (!candidate) throw new ApiError(404, '找不到這筆候選商品，請重新查價');
  const old = await DB.prepare('SELECT * FROM listing_judgments WHERE product_id=? AND source=? AND listing_key=?').bind(id, body.source, candidate.listing_key).first();
  const entity = `${id}:${body.source}:${candidate.listing_key}`;
  const now = new Date().toISOString();
  const label = {name: product.name, source: body.source, title: candidate.title};
  const before = old ? {...label, verdict: old.verdict, quantity: old.package_quantity, unit: old.package_unit} : null;
  let judgment = null, group, parsed;
  if (method === 'PUT') {
    if (!VERDICTS.includes(body.verdict)) throw new ApiError(400, '判斷結果不正確');
    const other = body.verdict === 'other_package' ? quantityAndUnit(body) : {quantity: null, unit: ''};
    judgment = {verdict: body.verdict, packageQuantity: other.quantity, packageUnit: other.unit, titleSnapshot: candidate.title};
  } else if (!old) throw new ApiError(404, '找不到這筆判斷');
  const result = classifyListing(product, candidate.title, judgment);
  group = result.group;
  parsed = {...result.parsed, quantity: result.quantity, unit: result.unit, reason: result.reason};
  const regroup = DB.prepare('UPDATE latest_candidates SET grp=?, parsed_json=? WHERE product_id=? AND source=? AND listing_key=?').bind(group, JSON.stringify(parsed), id, body.source, candidate.listing_key);
  if (method === 'DELETE') {
    await DB.batch([
      DB.prepare('DELETE FROM listing_judgments WHERE product_id=? AND source=? AND listing_key=?').bind(id, body.source, candidate.listing_key), regroup,
      auditStmt(DB, actor, now, 'judgment.delete', entity, before, null)
    ]);
    return json({ok: true, group});
  }
  const reviewWrites=[];
  let reviewedPrice;
  if (body.grokTaskId) {
    const task=await taskById(DB,body.grokTaskId);
    if(task.product_id!==id || task.status==='discarded') throw new ApiError(404,'任務商品不符');
    const {item}=await currentItem(DB,task,body.source,body.listingKey);
    if(item.status!=='answered' || (expectedResult!==undefined && item.result_json!==expectedResult)) throw conflict();
    reviewedPrice=body.price==null ? null : price(body.price,'選項價格');
    const review={verdict:body.verdict,packageQuantity:judgment.packageQuantity,packageUnit:judgment.packageUnit,price:reviewedPrice};
    reviewWrites.push(DB.prepare(`UPDATE grok_task_items SET status='reviewed',review_json=?,reviewed_at=?,reviewed_by=?
      WHERE task_id=? AND source=? AND listing_key=? AND status='answered' AND result_json=?
      AND EXISTS(SELECT 1 FROM grok_tasks WHERE id=grok_task_items.task_id AND status<>'discarded')
      AND EXISTS(SELECT 1 FROM latest_candidates WHERE product_id=? AND source=? AND listing_key=? AND run_id=?)
      AND EXISTS(SELECT 1 FROM tracked_products WHERE id=? AND deleted_at IS NULL AND model=? AND attributes_json=? AND package_quantity=? AND package_unit=?)`)
      .bind(JSON.stringify(review),now,actor.employeeId,task.id,body.source,body.listingKey,item.result_json,id,body.source,body.listingKey,item.run_id,id,row.model,row.attributes_json,row.package_quantity,row.package_unit),
      DB.prepare("SELECT CASE WHEN changes()=0 THEN json('review_conflict') ELSE 1 END"));
    if(body.verdict==='same') {
      reviewWrites.push(DB.prepare('UPDATE latest_candidates SET price=? WHERE product_id=? AND source=? AND listing_key=?').bind(reviewedPrice,id,body.source,body.listingKey));
      if(reviewedPrice!==null) {
        const original=JSON.parse(item.result_json);
        const run = await DB.prepare('SELECT finished_at FROM tracked_runs WHERE product_id=? AND id=?').bind(id,item.run_id).first();
        reviewWrites.push(DB.prepare('DELETE FROM price_observations WHERE run_id=? AND product_id=? AND source=? AND listing_key=?').bind(item.run_id,id,body.source,body.listingKey),
          DB.prepare('INSERT INTO price_observations(run_id,product_id,source,listing_key,seller,title,price,url,observed_at) VALUES(?,?,?,?,?,?,?,?,?)')
            .bind(item.run_id,id,body.source,body.listingKey,original.seller||candidate.seller,candidate.title,reviewedPrice,original.url||candidate.url,run?.finished_at || original.checkedAt));
      }
    }
    reviewWrites.push(auditStmt(DB,actor,now,'grok.review',task.id,null,{...label,...review}));
  }
  await DB.batch([...reviewWrites,
    DB.prepare(`INSERT INTO listing_judgments (product_id,source,listing_key,verdict,package_quantity,package_unit,title_snapshot,decided_at,decided_by) VALUES (?,?,?,?,?,?,?,?,?)
      ON CONFLICT(product_id,source,listing_key) DO UPDATE SET verdict=excluded.verdict,package_quantity=excluded.package_quantity,package_unit=excluded.package_unit,title_snapshot=excluded.title_snapshot,decided_at=excluded.decided_at,decided_by=excluded.decided_by`)
      .bind(id, body.source, candidate.listing_key, judgment.verdict, judgment.packageQuantity, judgment.packageUnit, judgment.titleSnapshot, now, actor.employeeId),
    regroup,
    auditStmt(DB, actor, now, 'judgment.update', entity, before, {...label, verdict: judgment.verdict, quantity: judgment.packageQuantity, unit: judgment.packageUnit})
  ]);
  return json({ok: true, group});
}
