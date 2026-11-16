// Price record API on D1. Validation, grouping and the "keep the newer check" rule all come from
// public/core.js, the single implementation shared with the browser.
import {validateOffer, upsertOffers, groupKey} from '../public/core.js';
import {ApiError, json, readBody} from './http.js';

const FIELDS = ['product', 'spec', 'platform', 'seller', 'url', 'price', 'shipping', 'checkedAt', 'note'];

const toOffer = row => ({
  id: row.id, product: row.product, spec: row.spec, platform: row.platform, seller: row.seller, url: row.url,
  price: row.price, shipping: row.shipping, checkedAt: row.checked_at, note: row.note, revision: row.revision
});
const fieldsOf = offer => Object.fromEntries(FIELDS.map(k => [k, offer[k]]));
const same = (a, b) => FIELDS.every(k => a[k] === b[k]);
const params = o => [o.product, o.spec, o.platform, o.seller, o.url, o.price, o.shipping, o.checkedAt, o.note];

function validated(raw) {
  try { return validateOffer(raw); } catch (error) { throw new ApiError(400, error.message); }
}

const allRecords = async DB => (await DB.prepare('SELECT * FROM price_records ORDER BY created_at, id').all()).results.map(toOffer);

const insertStmt = (DB, o, now, actor) => DB.prepare(
  'INSERT INTO price_records (product,spec,platform,seller,url,price,shipping,checked_at,note,id,revision,created_at,created_by,updated_at,updated_by) VALUES (?,?,?,?,?,?,?,?,?,?,1,?,?,?,?)'
).bind(...params(o), o.id, now, actor.employeeId, now, actor.employeeId);

const updateStmt = (DB, o, now, actor, revision) => DB.prepare(
  `UPDATE price_records SET product=?,spec=?,platform=?,seller=?,url=?,price=?,shipping=?,checked_at=?,note=?,revision=revision+1,updated_at=?,updated_by=? WHERE id=?${revision === undefined ? '' : ' AND revision=?'}`
).bind(...params(o), now, actor.employeeId, o.id, ...(revision === undefined ? [] : [revision]));

const deleteStmt = (DB, id, revision) => DB.prepare('DELETE FROM price_records WHERE id=? AND revision=?').bind(id, revision);

// The audit row is written in the same batch as its change. With `afterChange` it is only inserted when the
// preceding revision-guarded statement really changed a row.
export const auditStmt = (DB, actor, now, action, entityId, before, after, afterChange = false) => DB.prepare(
  `INSERT INTO audit_log (at,actor_employee_id,actor_name,action,entity_id,before_json,after_json) SELECT ?,?,?,?,?,?,?${afterChange ? ' WHERE changes() > 0' : ''}`
).bind(now, actor.employeeId, actor.displayName, action, entityId, before && JSON.stringify(before), after && JSON.stringify(after));

// Applies core.js upsert rules and returns the rows that must be inserted or updated.
function planUpsert(existing, incoming) {
  const result = upsertOffers(existing.map(fieldsOfWithId), incoming, () => crypto.randomUUID());
  const previous = new Map(existing.map(o => [o.id, o]));
  const inserts = [], updates = [];
  for (const o of result.offers) {
    const before = previous.get(o.id);
    if (!before) inserts.push(o);
    else if (!same(before, o)) updates.push({before, after: o});
  }
  return {inserts, updates, added: result.added, updated: result.updated, skipped: result.skipped};
}
const fieldsOfWithId = o => ({...fieldsOf(o), id: o.id});

export async function handleRecordsApi(request, env, url, actor) {
  const {DB} = env;
  const path = url.pathname;
  const method = request.method;
  try {
    if (path === '/api/records' && method === 'GET') return json({records: await allRecords(DB)});

    if (path === '/api/records' && method === 'POST') {
      const offer = validated(await readBody(request));
      const plan = planUpsert(await allRecords(DB), [offer]);
      if (plan.skipped) throw new ApiError(422, '此連結已有較新的查價時間，請更新時間或編輯原紀錄。');
      const now = new Date().toISOString();
      if (plan.inserts.length) {
        const record = {...plan.inserts[0], revision: 1};
        await DB.batch([insertStmt(DB, record, now, actor), auditStmt(DB, actor, now, 'record.create', record.id, null, record)]);
        return json({result: 'created', record}, 201);
      }
      if (!plan.updates.length) return json({result: 'unchanged'});
      const {before, after} = plan.updates[0];
      const record = {...after, revision: before.revision + 1};
      await DB.batch([updateStmt(DB, after, now, actor), auditStmt(DB, actor, now, 'record.update', record.id, before, record)]);
      return json({result: 'updated', record});
    }

    if (path === '/api/records/import' && method === 'POST') {
      const body = await readBody(request);
      if (!Array.isArray(body.offers)) throw new ApiError(400, '資料格式不正確');
      const offers = body.offers.map((raw, i) => {
        try { return validateOffer(raw); } catch (error) { throw new ApiError(400, `第 ${i + 1} 筆：${error.message}`); }
      });
      const plan = planUpsert(await allRecords(DB), offers);
      const summary = {added: plan.added, updated: plan.updated, skipped: plan.skipped};
      if (!plan.inserts.length && !plan.updates.length) return json(summary);
      const now = new Date().toISOString();
      await DB.batch([
        ...plan.inserts.map(o => insertStmt(DB, o, now, actor)),
        ...plan.updates.map(u => updateStmt(DB, u.after, now, actor)),
        auditStmt(DB, actor, now, 'record.import', null, null, {...summary, addedIds: plan.inserts.map(o => o.id), updatedIds: plan.updates.map(u => u.after.id)})
      ]);
      return json(summary);
    }

    if (path === '/api/audit' && method === 'GET') {
      const limit = Math.min(Math.max(Math.trunc(Number(url.searchParams.get('limit'))) || 50, 1), 200);
      const rows = (await DB.prepare('SELECT * FROM audit_log ORDER BY at DESC, id DESC LIMIT ?').bind(limit).all()).results;
      return json({entries: rows.map(auditEntry)});
    }

    const match = path.match(/^\/api\/records\/([^/]+)$/);
    if (match && (method === 'PUT' || method === 'DELETE')) {
      const id = decodeURIComponent(match[1]);
      const row = await DB.prepare('SELECT * FROM price_records WHERE id=?').bind(id).first();
      if (!row) throw new ApiError(404, '找不到這筆紀錄，可能已被刪除，請重新整理');
      const before = toOffer(row);
      const body = method === 'PUT' ? await readBody(request) : {revision: url.searchParams.get('revision')};
      const revision = Number(body.revision);
      const conflict = new ApiError(409, '資料已被他人更新，請重新整理');
      if (revision !== before.revision) throw conflict;
      const now = new Date().toISOString();

      if (method === 'DELETE') {
        const [result] = await DB.batch([deleteStmt(DB, id, revision), auditStmt(DB, actor, now, 'record.delete', id, before, null, true)]);
        if (!result.meta.changes) throw conflict;
        return json({ok: true});
      }

      const offer = {...validated(body), id};
      const duplicate = (await allRecords(DB)).find(x => x.id !== id && groupKey(x) === groupKey(offer) && offer.url && x.url === offer.url);
      if (duplicate) throw new ApiError(422, '此商品規格已有相同連結，請改為編輯那筆查價。');
      const record = {...offer, revision: revision + 1};
      const [result] = await DB.batch([updateStmt(DB, offer, now, actor, revision), auditStmt(DB, actor, now, 'record.update', id, before, record, true)]);
      if (!result.meta.changes) throw conflict;
      return json({record});
    }
  } catch (error) {
    if (error instanceof ApiError) return json({error: error.message}, error.status);
    throw error;
  }
  return null;
}

function auditEntry(row) {
  const after = row.after_json ? JSON.parse(row.after_json) : null;
  const before = row.before_json ? JSON.parse(row.before_json) : null;
  const record = after || before;
  const trackedLabels = {'tracked.create': () => `${record.name}（${record.model}）`, 'tracked.update': () => `${record.name}（${record.model}）`, 'tracked.run': () => `${record.name}（${record.model}）`,
    'tracked.price': () => `${record.name}：${before?.myPrice ?? '未設定'} → ${after.myPrice}`,
    'judgment.update': () => `${record.name} · ${record.source} · ${{same: '同規格', other_package: '其他包裝', not_same: '不是同一商品'}[record.verdict]} · ${record.title}`,
    'judgment.delete': () => `${record.name} · ${record.source} · 恢復 ${record.title}`};
  const detail = row.action === 'record.import'
    ? `新增 ${after.added} 筆、更新 ${after.updated} 筆、略過 ${after.skipped} 筆`
    : trackedLabels[row.action]
      ? trackedLabels[row.action]()
    : row.action.startsWith('classification.')
      ? [record.source, record.model, record.listingKey].filter(Boolean).join(' · ')
      : [record.product, record.spec, record.platform].filter(Boolean).join(' · ');
  return {id: row.id, at: row.at, actor: row.actor_name, action: row.action, detail};
}
