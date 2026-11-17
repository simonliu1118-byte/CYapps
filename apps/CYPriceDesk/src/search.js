// GET /api/search and the shared lookup used by tracked runs: D1 cache (10 min), per-upstream spacing (15 s) and block cooldown (30 min) in front of the fetcher.
import {ApiError, json} from './http.js';
import {callFetcher, fetcherConfigured} from './fetcher-client.js';

const CACHE_MS = 10 * 60 * 1000;
const GAP_MS = 15 * 1000;
const COOLDOWN_MS = 30 * 60 * 1000;
const CACHE_KEEP_MS = 24 * 60 * 60 * 1000;
const MAX_MOMO_PAGE = 5;
const SOURCES = {
  momo: {upstream: 'momo', path: '/v1/momo'},
  shopee: {upstream: 'feebee', path: '/v1/feebee', market: 'shopee'},
  coupang: {upstream: 'feebee', path: '/v1/feebee', market: 'coupang'}
};

export function normalizeKeyword(raw) {
  const keyword = String(raw ?? '').split(/\s+/).filter(Boolean).join(' ');
  if ([...keyword].length < 2 || [...keyword].length > 80 || [...keyword].some(c => c.charCodeAt(0) < 32)) {
    throw new ApiError(400, '關鍵字必須為 2～80 個字元');
  }
  return keyword;
}

const seconds = ms => Math.max(1, Math.ceil(ms / 1000));

// Turns one fetcher payload into the response for the requested source.
function view(source, keyword, page, payload, fromCache) {
  const base = {source, keyword, fromCache, checkedAt: payload.checked_at ?? null};
  if (source === 'momo') {
    const rawCount = payload.raw_count ?? 0;
    return {...base, page, status: payload.status, rawCount, results: payload.results ?? [], hasNext: page < MAX_MOMO_PAGE && rawCount >= 20};
  }
  const market = payload.status === 'ok' ? payload.markets?.[SOURCES[source].market] : null;
  return {...base, status: market ? market.status : payload.status, results: market?.results ?? [], sourceUrl: payload.source_url ?? ''};
}

const waiting = (source, keyword, page, status, ms) => ({source, keyword, page, status, results: [], fromCache: false, retryAfterSeconds: seconds(ms)});

// One cached / throttled lookup for a source. Returns the response view; throws ApiError when the fetcher is not configured.
// Shared by GET /api/search and the tracked-product runs, so there is a single cache, spacing and cooldown.
export async function querySource(env, source, keyword, page = 1) {
  const config = SOURCES[source];
  if (!fetcherConfigured(env)) throw new ApiError(503, '自動查價服務尚未設定');
  const {DB} = env;
  const now = Date.now();

  const cached = await DB.prepare('SELECT payload_json, fetched_at FROM search_cache WHERE source=? AND keyword=? AND page=?')
    .bind(config.upstream, keyword, page).first();
  if (cached && now - cached.fetched_at < CACHE_MS) return view(source, keyword, page, JSON.parse(cached.payload_json), true);

  const state = await DB.prepare('SELECT last_request_at, blocked_until FROM upstream_state WHERE upstream=?').bind(config.upstream).first();
  if (state.blocked_until > now) return waiting(source, keyword, page, 'blocked_cooldown', state.blocked_until - now);
  if (now - state.last_request_at < GAP_MS) return waiting(source, keyword, page, 'rate_limited', GAP_MS - (now - state.last_request_at));
  // Conditional claim so two simultaneous requests cannot both pass the spacing check.
  const claim = await DB.prepare('UPDATE upstream_state SET last_request_at=? WHERE upstream=? AND last_request_at<=? AND blocked_until<=?')
    .bind(now, config.upstream, now - GAP_MS, now).run();
  if (!claim.meta.changes) return waiting(source, keyword, page, 'rate_limited', GAP_MS);

  let payload;
  try {
    payload = await callFetcher(env, config.path, source === 'momo' ? {keyword, page} : {keyword});
  } catch {
    return view(source, keyword, page, {status: 'network_error'}, false);
  }
  if (payload.status === 'blocked' || payload.status === 'provider_refused') {
    await DB.prepare('UPDATE upstream_state SET blocked_until=? WHERE upstream=?').bind(Date.now() + COOLDOWN_MS, config.upstream).run();
  } else if (['ok', 'no_match', 'no_price'].includes(payload.status)) {
    const fetchedAt = Date.now();
    await DB.batch([
      DB.prepare('INSERT OR REPLACE INTO search_cache (source,keyword,page,payload_json,fetched_at) VALUES (?,?,?,?,?)')
        .bind(config.upstream, keyword, page, JSON.stringify(payload), fetchedAt),
      DB.prepare('DELETE FROM search_cache WHERE fetched_at<?').bind(fetchedAt - CACHE_KEEP_MS)
    ]);
  }
  return view(source, keyword, page, payload, false);
}

export async function handleSearchApi(request, env, url) {
  if (request.method !== 'GET') return null;
  // The last 10 distinct keywords still in the shared cache (kept for one day), newest first.
  if (url.pathname === '/api/search/recent') {
    const rows = (await env.DB.prepare('SELECT keyword FROM search_cache GROUP BY keyword ORDER BY MAX(fetched_at) DESC LIMIT 10').all()).results;
    return json({keywords: rows.map(r => r.keyword)});
  }
  if (url.pathname !== '/api/search') return null;
  try {
    const source = url.searchParams.get('source');
    if (!SOURCES[source]) throw new ApiError(400, '不支援的查詢來源');
    const keyword = normalizeKeyword(url.searchParams.get('keyword'));
    const requested = Math.trunc(Number(url.searchParams.get('page'))) || 1;
    const page = source === 'momo' ? Math.min(Math.max(requested, 1), MAX_MOMO_PAGE) : 1;
    return json(await querySource(env, source, keyword, page));
  } catch (error) {
    if (error instanceof ApiError) return json({error: error.message}, error.status);
    throw error;
  }
}
