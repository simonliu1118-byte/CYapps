// GET /api/pending: every tracked product's 待確認 candidates (latest_candidates.grp = 'pending'), grouped by product.
import {candidateJoin,candidateCols} from './grok-store.js';
import {json} from './http.js';
import {toCandidate} from './tracking.js';

export async function handlePendingApi(request, env, url) {
  if (url.pathname !== '/api/pending' || request.method !== 'GET') return null;
  const rows = (await env.DB.prepare(
    `SELECT c.*, ${candidateCols}, p.name, p.model, p.package_quantity, p.package_unit,
      EXISTS(SELECT 1 FROM latest_candidates eligible WHERE eligible.product_id=p.id
        AND eligible.source IN ('momo','coupang') AND eligible.grp IN ('pending','other')) AS can_judge FROM latest_candidates c ${candidateJoin} JOIN tracked_products p ON p.id=c.product_id WHERE c.grp='pending' AND p.deleted_at IS NULL ORDER BY p.name, p.id, c.source, c.price, c.listing_key`
  ).all()).results;
  const products = [];
  for (const row of rows) {
    let product = products.at(-1);
    if (product?.id !== row.product_id) {
      product = {id: row.product_id, name: row.name, model: row.model, packageQuantity: row.package_quantity, packageUnit: row.package_unit, canJudge:Boolean(row.can_judge), candidates: []};
      products.push(product);
    }
    product.candidates.push(toCandidate(row));
  }
  return json({count: rows.length, products});
}
