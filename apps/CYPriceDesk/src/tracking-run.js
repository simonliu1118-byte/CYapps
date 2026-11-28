// One price check of a tracked product, and the scheduled entry point (WO-1.3.0 §2.6, §2.7; keywords per WO-1.4.0 §2.3).
import {querySource} from './search.js';
import {classifyListing} from './grouping.js';
import {calcStats} from '../public/catalog-core.js';
import {auditStmt} from './audit.js';
import {fetcherConfigured} from './fetcher-client.js';

const HOUR_MS = 3600 * 1000;
const DAY_MS = 24 * HOUR_MS;
const TAIPEI_MS = 8 * HOUR_MS;
const USABLE = ['ok', 'no_match', 'no_price'];

export const toTracked = row => ({
  id: row.id, name: row.name, model: row.model, attributes: JSON.parse(row.attributes_json),
  packageQuantity: row.package_quantity, packageUnit: row.package_unit, cywebItemNo: row.cyweb_item_no,
  keyword: row.keyword, momoKeyword: row.momo_keyword, feebeeKeyword: row.feebee_keyword,
  myPrices: {shopee: row.my_price_shopee, momo: row.my_price_momo, coupang: row.my_price_coupang},
  myUrls: {shopee: row.my_url_shopee, momo: row.my_url_momo, coupang: row.my_url_coupang},
  active: Boolean(row.active), nextRunAt: row.next_run_at, lastRunAt: row.last_run_at, revision: row.revision
});

// An override wins; otherwise the shared keyword is used. shopee and coupang share the Feebee query.
export const effectiveKeywords = p => ({momo: p.momoKeyword || p.keyword, feebee: p.feebeeKeyword || p.keyword});

// A random moment 08:00-22:00 Taipei time, a whole number of days (minDays..maxDays) after `now`, never before `now`.
export function randomRunTime(now, minDays, maxDays, rand = Math.random) {
  const days = minDays + Math.floor(rand() * (maxDays - minDays + 1));
  const day = new Date(now + TAIPEI_MS + days * DAY_MS);
  const minutes = 8 * 60 + Math.floor(rand() * 14 * 60);
  return Math.max(now, Date.UTC(day.getUTCFullYear(), day.getUTCMonth(), day.getUTCDate(), 0, minutes) - TAIPEI_MS);
}

export const inRunWindow = now => {
  const hour = new Date(now + TAIPEI_MS).getUTCHours();
  return hour >= 8 && hour < 22;
};

const listingsOf = (source, result) => result.results.map(r => source === 'momo'
  ? {source, key: r.code, title: r.title, price: r.price, url: r.url, seller: ''}
  : {source, key: r.url, title: r.title, price: r.price, url: r.url, seller: r.seller || r.merchant || ''});

const summaryOf = (key, label, prices) => {
  const stats = calcStats(prices);
  return stats && {key, label, count: stats.count, min: stats.min, median: stats.median, max: stats.max};
};

// Queries momo page 1 and Feebee once (shopee + coupang come from the same cached fetch), groups every listing with the
// stored judgments and writes the run, its observations, summaries and the new latest_candidates in one batch.
// Any source that is not usable (throttled, blocked, failed) postpones the product by one hour; usable sources are still stored.
export async function runTracked(env, productRow, trigger, {actor = null, now = Date.now(), rand = Math.random} = {}) {
  const {DB} = env;
  const product = toTracked(productRow);
  const started = new Date().toISOString();
  const judgments = new Map((await DB.prepare('SELECT * FROM listing_judgments WHERE product_id=?').bind(product.id).all()).results
    .map(j => [`${j.source}\u001f${j.listing_key}`, {verdict: j.verdict, packageQuantity: j.package_quantity, packageUnit: j.package_unit, titleSnapshot: j.title_snapshot}]));

  const keywords = effectiveKeywords(product);
  const statuses = {};
  const usable = {};
  const momo = await querySource(env, 'momo', keywords.momo, 1);
  statuses.momo = momo.status;
  if (USABLE.includes(momo.status)) usable.momo = momo;
  const shopee = await querySource(env, 'shopee', keywords.feebee, 1);
  statuses.feebee = shopee.status;
  if (USABLE.includes(shopee.status)) {
    usable.shopee = shopee;
    usable.coupang = await querySource(env, 'coupang', keywords.feebee, 1);
  }

  const runId = crypto.randomUUID();
  const observedAt = new Date().toISOString();
  const sources = Object.keys(usable);
  const candidates = [];
  for (const source of sources) {
    const seen = new Set();
    for (const listing of listingsOf(source, usable[source])) {
      if (seen.has(listing.key)) continue;
      seen.add(listing.key);
      const c = classifyListing(product, listing.title, judgments.get(`${source}\u001f${listing.key}`));
      candidates.push({...listing, ...c});
    }
  }

  const priced = group => candidates.filter(c => c.group === group && c.price !== null);
  const summaries = [];
  const same = summaryOf('same', '同規格', priced('same').map(c => c.price));
  if (same) summaries.push(same);
  const others = new Map();
  for (const c of priced('other')) others.set(`${c.quantity}:${c.unit}`, [...(others.get(`${c.quantity}:${c.unit}`) || []), c]);
  for (const [key, list] of others) summaries.push(summaryOf(`other:${key}`, `${list[0].quantity} ${list[0].unit}`, list.map(c => c.price)));

  const finished = new Date().toISOString();
  const nextRunAt = Object.values(statuses).every(s => USABLE.includes(s))
    ? randomRunTime(now, 7, 14, rand) : now + HOUR_MS;
  const statements = [
    DB.prepare('INSERT INTO tracked_runs (id,product_id,started_at,finished_at,momo_status,feebee_status,trigger) VALUES (?,?,?,?,?,?,?)')
      .bind(runId, product.id, started, finished, statuses.momo, statuses.feebee, trigger),
    ...sources.map(source => DB.prepare('DELETE FROM latest_candidates WHERE product_id=? AND source=?').bind(product.id, source)),
    ...candidates.map(c => DB.prepare('INSERT OR REPLACE INTO latest_candidates (product_id,source,listing_key,title,price,seller,url,grp,parsed_json,run_id) VALUES (?,?,?,?,?,?,?,?,?,?)')
      .bind(product.id, c.source, c.key, c.title, c.price, c.seller, c.url, c.group, JSON.stringify({...c.parsed, quantity: c.quantity, unit: c.unit, reason: c.reason}),runId)),
    ...priced('same').map(c => DB.prepare('INSERT INTO price_observations (run_id,product_id,source,listing_key,seller,title,price,url,observed_at) VALUES (?,?,?,?,?,?,?,?,?)')
      .bind(runId, product.id, c.source, c.key, c.seller, c.title, c.price, c.url, observedAt)),
    ...summaries.map(s => DB.prepare('INSERT INTO group_summaries (run_id,product_id,group_key,label,count,min_price,median_price,max_price) VALUES (?,?,?,?,?,?,?,?)')
      .bind(runId, product.id, s.key, s.label, s.count, s.min, s.median, s.max)),
    DB.prepare('UPDATE tracked_products SET last_run_at=?, next_run_at=? WHERE id=?').bind(now, nextRunAt, product.id)
  ];
  if (actor) statements.push(auditStmt(DB, actor, finished, 'tracked.run', product.id, null, {name: product.name, model: product.model, momo: statuses.momo, feebee: statuses.feebee}));
  await DB.batch(statements);
  return {runId, momoStatus: statuses.momo, feebeeStatus: statuses.feebee, nextRunAt, sameCount: priced('same').length};
}

// Cron entry (*/10): only 08:00-22:00 Taipei, at most one due product per tick. The provisional next_run_at both claims
// the product against an overlapping tick and retries it in an hour if the run dies midway.
export async function runScheduled(env, {now = Date.now(), rand = Math.random} = {}) {
  if (!inRunWindow(now) || !fetcherConfigured(env)) return null;
  const row = await env.DB.prepare('SELECT * FROM tracked_products WHERE active=1 AND next_run_at<=? ORDER BY next_run_at LIMIT 1').bind(now).first();
  if (!row) return null;
  const claim = await env.DB.prepare('UPDATE tracked_products SET next_run_at=? WHERE id=? AND next_run_at=?').bind(now + HOUR_MS, row.id, row.next_run_at).run();
  if (!claim.meta.changes) return null;
  return {id: row.id, ...await runTracked(env, row, 'schedule', {now, rand})};
}
