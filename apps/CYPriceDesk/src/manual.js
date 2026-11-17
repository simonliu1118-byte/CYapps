// Manual comparison records of a tracked product (WO-1.4.0 §2.4): create, edit, delete with revision checks.
// Every write is audited in the same D1 batch as the change.
import {ApiError, json, readBody} from './http.js';
import {auditStmt} from './audit.js';
import {validateLink} from '../public/core.js';
import {PLATFORMS, dayOf, dayNumber} from '../public/compare.js';

const conflict = () => new ApiError(409, '資料已被他人更新，請重新整理');

export const toManual = row => ({id: row.id, platform: row.platform, seller: row.seller, url: row.url, price: row.price, checkedAt: row.checked_at, note: row.note, revision: row.revision});

function validated(body, today) {
  if (![...PLATFORMS, 'other'].includes(body.platform)) throw new ApiError(400, '請選擇平台');
  const price = Number(body.price);
  if (body.price === '' || body.price === null || !Number.isFinite(price) || price < 0 || price > 1e9) throw new ApiError(400, '價格需為 0 到 1,000,000,000 的數字');
  const checkedAt = String(body.checkedAt || today);
  if (!/^\d{4}-\d{2}-\d{2}$/.test(checkedAt) || Number.isNaN(dayNumber(checkedAt)) || new Date(`${checkedAt}T00:00:00Z`).toISOString().slice(0, 10) !== checkedAt) throw new ApiError(400, '查價日期不正確');
  if (dayNumber(checkedAt) > dayNumber(today)) throw new ApiError(400, '查價日期不能晚於今天');
  let url;
  try { url = validateLink(body.url); } catch (error) { throw new ApiError(400, error.message); }
  return {platform: body.platform, seller: String(body.seller ?? '').trim().slice(0, 200), url, price: Math.round(price * 100) / 100, checkedAt, note: String(body.note ?? '').trim().slice(0, 500)};
}

// Routes /api/tracked/:id/manual and /api/tracked/:id/manual/:manualId; `productRow` is the already loaded product.
export async function handleManual(request, env, actor, productRow, manualId) {
  const {DB} = env;
  const method = request.method;
  const today = dayOf(new Date().toISOString());
  const label = {name: productRow.name};
  const now = new Date().toISOString();

  if (!manualId && method === 'POST') {
    const input = validated(await readBody(request), today);
    const id = crypto.randomUUID();
    await DB.batch([
      DB.prepare('INSERT INTO manual_comparisons (id,product_id,platform,seller,url,price,checked_at,note,revision,created_at,created_by,updated_at,updated_by) VALUES (?,?,?,?,?,?,?,?,1,?,?,?,?)')
        .bind(id, productRow.id, input.platform, input.seller, input.url, input.price, input.checkedAt, input.note, now, actor.employeeId, now, actor.employeeId),
      auditStmt(DB, actor, now, 'manual.create', id, null, {...label, ...input})
    ]);
    return json({manual: toManual(await DB.prepare('SELECT * FROM manual_comparisons WHERE id=?').bind(id).first())}, 201);
  }

  if (manualId && (method === 'PUT' || method === 'DELETE')) {
    const row = await DB.prepare('SELECT * FROM manual_comparisons WHERE id=? AND product_id=?').bind(manualId, productRow.id).first();
    if (!row) throw new ApiError(404, '找不到這筆手動比價，可能已被刪除，請重新整理');
    const before = {...label, ...toManual(row)};
    const body = method === 'PUT' ? await readBody(request) : {revision: new URL(request.url).searchParams.get('revision')};
    if (Number(body.revision) !== row.revision) throw conflict();
    if (method === 'DELETE') {
      const [result] = await DB.batch([
        DB.prepare('DELETE FROM manual_comparisons WHERE id=? AND revision=?').bind(manualId, row.revision),
        auditStmt(DB, actor, now, 'manual.delete', manualId, before, null, true)
      ]);
      if (!result.meta.changes) throw conflict();
      return json({ok: true});
    }
    const input = validated(body, today);
    const [result] = await DB.batch([
      DB.prepare('UPDATE manual_comparisons SET platform=?,seller=?,url=?,price=?,checked_at=?,note=?,revision=revision+1,updated_at=?,updated_by=? WHERE id=? AND revision=?')
        .bind(input.platform, input.seller, input.url, input.price, input.checkedAt, input.note, now, actor.employeeId, manualId, row.revision),
      auditStmt(DB, actor, now, 'manual.update', manualId, before, {...label, ...input}, true)
    ]);
    if (!result.meta.changes) throw conflict();
    return json({manual: toManual(await DB.prepare('SELECT * FROM manual_comparisons WHERE id=?').bind(manualId).first())});
  }
  return null;
}
