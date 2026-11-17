// GET /api/pending: every tracked product's 待確認 candidates (latest_candidates.grp = 'pending'), grouped by product.
import {json} from './http.js';
import {toCandidate} from './tracking.js';

export async function handlePendingApi(request, env, url) {
  if (url.pathname !== '/api/pending' || request.method !== 'GET') return null;
  const rows = (await env.DB.prepare(
    "SELECT c.*, p.name, p.model, p.package_quantity, p.package_unit FROM latest_candidates c JOIN tracked_products p ON p.id=c.product_id WHERE c.grp='pending' ORDER BY p.name, p.id, c.source, c.price, c.listing_key"
  ).all()).results;
  const products = [];
  for (const row of rows) {
    let product = products.at(-1);
    if (product?.id !== row.product_id) {
      product = {id: row.product_id, name: row.name, model: row.model, packageQuantity: row.package_quantity, packageUnit: row.package_unit, candidates: []};
      products.push(product);
    }
    product.candidates.push(toCandidate(row));
  }
  return json({count: rows.length, products});
}
