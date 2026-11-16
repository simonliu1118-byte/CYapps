// Tracked products API: CRUD with revision checks, own selling price history, manual judgments, detail and manual run.
// Every write is audited in the same D1 batch as the change.
import {ApiError, json, readBody} from './http.js';
import {auditStmt} from './records.js';
import {normalizeKeyword} from './search.js';
import {sanitizeAttributes, calcStats, normalize} from '../public/catalog-core.js';
import {validateLink} from '../public/core.js';
import {classifyListing, normalizeUnit} from './grouping.js';
import {randomRunTime, runTracked, toTracked} from './tracking-run.js';

const SOURCES = ['momo', 'shopee', 'coupang'];
const VERDICTS = ['same', 'other_package', 'not_same'];
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

function validated(body) {
  const name = String(body.name ?? '').trim();
  const model = normalize(body.model).slice(0, 80);
  if (!name || name.length > 200) throw new ApiError(400, '請填入商品名稱（200 字內）');
  if (!model) throw new ApiError(400, '請填入型號');
  const {quantity, unit} = quantityAndUnit(body);
  const momoKeyword = String(body.momoKeyword ?? '').trim() ? normalizeKeyword(body.momoKeyword) : '';
  const feebeeKeyword = String(body.feebeeKeyword ?? '').trim() ? normalizeKeyword(body.feebeeKeyword) : '';
  if (!momoKeyword && !feebeeKeyword) throw new ApiError(400, '至少填入一個查詢關鍵字');
  let myUrl;
  try { myUrl = validateLink(body.myUrl); } catch (error) { throw new ApiError(400, error.message); }
  return {name, model, attributes: sanitizeAttributes(body.attributes), packageQuantity: quantity, packageUnit: unit, myPrice: price(body.myPrice, '自己的售價'), myUrl, momoKeyword, feebeeKeyword, active: body.active !== false};
}

const toChange = row => ({at: row.at, actorName: row.actor_name, oldPrice: row.old_price, newPrice: row.new_price, note: row.note});
const toCandidate = row => ({source: row.source, listingKey: row.listing_key, title: row.title, price: row.price, seller: row.seller, url: row.url, group: row.grp, parsed: JSON.parse(row.parsed_json)});
const auditFields = p => ({name: p.name, model: p.model, attributes: p.attributes, packageQuantity: p.packageQuantity, packageUnit: p.packageUnit, myPrice: p.myPrice, myUrl: p.myUrl, momoKeyword: p.momoKeyword, feebeeKeyword: p.feebeeKeyword, active: p.active});

async function getProduct(DB, id) {
  const row = await DB.prepare('SELECT * FROM tracked_products WHERE id=?').bind(id).first();
  if (!row) throw new ApiError(404, '找不到這個追蹤商品');
  return row;
}

async function list(DB) {
  const products = (await DB.prepare('SELECT * FROM tracked_products ORDER BY name, id').all()).results.map(toTracked);
  const samePrices = new Map();
  for (const r of (await DB.prepare("SELECT product_id, price FROM latest_candidates WHERE grp='same' AND price IS NOT NULL").all()).results) {
    samePrices.set(r.product_id, [...(samePrices.get(r.product_id) || []), r.price]);
  }
  const pending = new Map((await DB.prepare("SELECT product_id, COUNT(*) n FROM latest_candidates WHERE grp='pending' GROUP BY product_id").all()).results.map(r => [r.product_id, r.n]));
  return products.map(p => {
    const stats = calcStats(samePrices.get(p.id) || []);
    return {...p, sameCount: stats?.count ?? 0, sameMin: stats?.min ?? null, sameMedian: stats?.median ?? null, pendingCount: pending.get(p.id) || 0};
  });
}

async function detail(DB, id) {
  const product = toTracked(await getProduct(DB, id));
  const run = await DB.prepare('SELECT * FROM tracked_runs WHERE product_id=? ORDER BY started_at DESC LIMIT 1').bind(id).first();
  return {
    product,
    priceChanges: (await DB.prepare('SELECT * FROM my_price_changes WHERE product_id=? ORDER BY at DESC, id DESC').bind(id).all()).results.map(toChange),
    candidates: (await DB.prepare('SELECT * FROM latest_candidates WHERE product_id=? ORDER BY grp, source, price, listing_key').bind(id).all()).results.map(toCandidate),
    history: (await DB.prepare("SELECT r.id runId, r.started_at at, s.count, s.min_price min, s.median_price median, s.max_price max FROM group_summaries s JOIN tracked_runs r ON r.id=s.run_id WHERE s.product_id=? AND s.group_key='same' ORDER BY r.started_at DESC LIMIT 100").bind(id).all()).results,
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
      const input = validated(await readBody(request));
      const id = crypto.randomUUID();
      const now = new Date().toISOString();
      const nextRunAt = randomRunTime(Date.now(), 0, 7);
      const statements = [
        DB.prepare('INSERT INTO tracked_products (id,name,model,attributes_json,package_quantity,package_unit,my_price,my_url,momo_keyword,feebee_keyword,active,next_run_at,revision,created_at,created_by,updated_at,updated_by) VALUES (?,?,?,?,?,?,?,?,?,?,?,?,1,?,?,?,?)')
          .bind(id, input.name, input.model, JSON.stringify(input.attributes), input.packageQuantity, input.packageUnit, input.myPrice, input.myUrl, input.momoKeyword, input.feebeeKeyword, input.active ? 1 : 0, nextRunAt, now, actor.employeeId, now, actor.employeeId)
      ];
      if (input.myPrice !== null) statements.push(DB.prepare('INSERT INTO my_price_changes (product_id,at,actor_employee_id,actor_name,old_price,new_price,note) VALUES (?,?,?,?,NULL,?,?)').bind(id, now, actor.employeeId, actor.displayName, input.myPrice, '建立追蹤商品'));
      statements.push(auditStmt(DB, actor, now, 'tracked.create', id, null, auditFields(input)));
      await DB.batch(statements);
      return json({product: toTracked(await getProduct(DB, id))}, 201);
    }

    const match = path.match(/^\/api\/tracked\/([^/]+)(?:\/(price|run|judgments))?$/);
    if (!match) return null;
    const id = decodeURIComponent(match[1]);
    const part = match[2];

    if (!part && method === 'GET') return json(await detail(DB, id));

    if (!part && method === 'PUT') {
      const body = await readBody(request);
      const before = toTracked(await getProduct(DB, id));
      if (Number(body.revision) !== before.revision) throw conflict();
      // The selling price changes only through /price so every change is recorded.
      const input = {...validated(body), myPrice: before.myPrice};
      const now = new Date().toISOString();
      const [result] = await DB.batch([
        DB.prepare('UPDATE tracked_products SET name=?,model=?,attributes_json=?,package_quantity=?,package_unit=?,my_url=?,momo_keyword=?,feebee_keyword=?,active=?,revision=revision+1,updated_at=?,updated_by=? WHERE id=? AND revision=?')
          .bind(input.name, input.model, JSON.stringify(input.attributes), input.packageQuantity, input.packageUnit, input.myUrl, input.momoKeyword, input.feebeeKeyword, input.active ? 1 : 0, now, actor.employeeId, id, before.revision),
        auditStmt(DB, actor, now, 'tracked.update', id, auditFields(before), auditFields(input), true)
      ]);
      if (!result.meta.changes) throw conflict();
      return json({product: toTracked(await getProduct(DB, id))});
    }

    if (part === 'price' && method === 'PUT') {
      const body = await readBody(request);
      const before = toTracked(await getProduct(DB, id));
      if (Number(body.revision) !== before.revision) throw conflict();
      const newPrice = price(body.price, '售價');
      if (newPrice === null) throw new ApiError(400, '請填入售價');
      const note = String(body.note ?? '').trim().slice(0, 200);
      const now = new Date().toISOString();
      const [result] = await DB.batch([
        DB.prepare('UPDATE tracked_products SET my_price=?,revision=revision+1,updated_at=?,updated_by=? WHERE id=? AND revision=?').bind(newPrice, now, actor.employeeId, id, before.revision),
        DB.prepare('INSERT INTO my_price_changes (product_id,at,actor_employee_id,actor_name,old_price,new_price,note) SELECT ?,?,?,?,?,?,? WHERE changes() > 0')
          .bind(id, now, actor.employeeId, actor.displayName, before.myPrice, newPrice, note),
        auditStmt(DB, actor, now, 'tracked.price', id, {myPrice: before.myPrice}, {myPrice: newPrice, note, name: before.name}, true)
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
      if (!SOURCES.includes(body.source)) throw new ApiError(400, '不支援的查詢來源');
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
      await DB.batch([
        DB.prepare(`INSERT INTO listing_judgments (product_id,source,listing_key,verdict,package_quantity,package_unit,title_snapshot,decided_at,decided_by) VALUES (?,?,?,?,?,?,?,?,?)
          ON CONFLICT(product_id,source,listing_key) DO UPDATE SET verdict=excluded.verdict,package_quantity=excluded.package_quantity,package_unit=excluded.package_unit,title_snapshot=excluded.title_snapshot,decided_at=excluded.decided_at,decided_by=excluded.decided_by`)
          .bind(id, body.source, candidate.listing_key, judgment.verdict, judgment.packageQuantity, judgment.packageUnit, judgment.titleSnapshot, now, actor.employeeId),
        regroup,
        auditStmt(DB, actor, now, 'judgment.update', entity, before, {...label, verdict: judgment.verdict, quantity: judgment.packageQuantity, unit: judgment.packageUnit})
      ]);
      return json({ok: true, group});
    }
  } catch (error) {
    if (error instanceof ApiError) return json({error: error.message}, error.status);
    throw error;
  }
  return null;
}
