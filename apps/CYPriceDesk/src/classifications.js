// Manual listing classifications: shared by everyone, isolated per source, every change audited.
import {ApiError, json, readBody} from './http.js';
import {auditStmt} from './audit.js';
import {sanitizeAttributes} from '../public/catalog-core.js';

const SOURCES = ['momo', 'shopee', 'coupang'];

const toClassification = row => ({
  source: row.source, listingKey: row.listing_key, model: row.model, quantity: row.quantity, unit: row.unit,
  attributes: JSON.parse(row.attributes_json), decision: row.decision
});

function sourceOf(value) {
  if (!SOURCES.includes(value)) throw new ApiError(400, '不支援的查詢來源');
  return value;
}

function listingKeyOf(value) {
  if (typeof value !== 'string' || !value || value.length > 2048) throw new ApiError(400, '商品識別不正確');
  return value;
}

function validated(body) {
  const decision = body.decision;
  if (decision !== 'include' && decision !== 'exclude') throw new ApiError(400, '使用狀態不正確');
  const model = String(body.model ?? '').trim().slice(0, 80);
  const unit = String(body.unit ?? '').trim().slice(0, 20);
  const quantity = Number(body.quantity) > 0 ? Number(body.quantity) : null;
  if (quantity > 100000) throw new ApiError(400, '包裝數量不正確');
  return {source: sourceOf(body.source), listingKey: listingKeyOf(body.listingKey), model, quantity, unit, attributes: sanitizeAttributes(body.attributes), decision};
}

export async function handleClassificationsApi(request, env, url, actor) {
  if (url.pathname !== '/api/classifications') return null;
  const {DB} = env;
  const method = request.method;
  try {
    if (method === 'GET') {
      const rows = (await DB.prepare('SELECT * FROM listing_classifications WHERE source=? ORDER BY listing_key').bind(sourceOf(url.searchParams.get('source'))).all()).results;
      return json({classifications: rows.map(toClassification)});
    }
    if (method === 'PUT' || method === 'DELETE') {
      const input = method === 'PUT' ? validated(await readBody(request)) : {source: sourceOf(url.searchParams.get('source')), listingKey: listingKeyOf(url.searchParams.get('listingKey'))};
      const entityId = `${input.source}:${input.listingKey}`;
      const row = await DB.prepare('SELECT * FROM listing_classifications WHERE source=? AND listing_key=?').bind(input.source, input.listingKey).first();
      const before = row ? toClassification(row) : null;
      const now = new Date().toISOString();
      if (method === 'DELETE') {
        if (!before) throw new ApiError(404, '找不到這筆分類');
        await DB.batch([
          DB.prepare('DELETE FROM listing_classifications WHERE source=? AND listing_key=?').bind(input.source, input.listingKey),
          auditStmt(DB, actor, now, 'classification.delete', entityId, before, null)
        ]);
        return json({ok: true});
      }
      await DB.batch([
        DB.prepare(`INSERT INTO listing_classifications (source,listing_key,model,quantity,unit,attributes_json,decision,updated_at,updated_by) VALUES (?,?,?,?,?,?,?,?,?)
          ON CONFLICT(source,listing_key) DO UPDATE SET model=excluded.model,quantity=excluded.quantity,unit=excluded.unit,attributes_json=excluded.attributes_json,decision=excluded.decision,updated_at=excluded.updated_at,updated_by=excluded.updated_by`)
          .bind(input.source, input.listingKey, input.model, input.quantity, input.unit, JSON.stringify(input.attributes), input.decision, now, actor.employeeId),
        auditStmt(DB, actor, now, 'classification.update', entityId, before, input)
      ]);
      return json({classification: input});
    }
  } catch (error) {
    if (error instanceof ApiError) return json({error: error.message}, error.status);
    throw error;
  }
  return null;
}
