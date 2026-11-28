// Local server for browser checks: the real Worker with a fake IDENTITY (a stand-in CY Web portal below hands out a code for 「測試員工」)
// and an in-memory node:sqlite D1. The price fetcher is a fake returning fixture data (no network).
// Usage: PORT=4173 SEED=1 [NO_GAP=1] node tests/support/local-server.mjs   (NO_GAP drops the 15 s upstream spacing, for screenshots)
import http from 'node:http';
import worker from '../../src/index.js';
import {createTestEnv} from './env.js';
import {installFakeFetcher} from './fake-fetcher.js';

const {env, identity} = createTestEnv();
const fetcher = installFakeFetcher();
if (process.env.FAKE_GROK) {
  Object.assign(env, {GROK_WEBHOOK_URL:'https://grok.example.test/hook', GROK_WEBHOOK_TOKEN:'ui-hook', GROK_API_TOKEN:'ui-grok'});
  const fakeFetcher = globalThis.fetch;
  globalThis.fetch = (url, options) => url === env.GROK_WEBHOOK_URL
    ? Promise.resolve(new Response(null, {status:204}))
    : fakeFetcher(url, options);
}
if (process.env.SEED) seedTracked();

// Screenshot / UI-test data: tracked products with same-spec, other-package, pending, unrelated and excluded candidates,
// per-platform own prices and price changes, a year-ish of observations for the trend chart, manual comparisons and recent searches.
function seedTracked() {
  const day = 86400000, db = env.DB.sqlite, now = Date.now();
  const iso = ms => new Date(ms).toISOString();
  const ymd = ms => new Date(ms + 8 * 3600000).toISOString().slice(0, 10);
  const momo = [['3M 1530-0 通氣膠帶 半吋 12入', 'M1', 259], ['3M 1530-0 通氣膠帶 半吋 12入 單盒', 'M2', 275], ['3M 1530-0 通氣膠帶 半吋 6入', 'M3', 140],
    ['3M 1530-0 通氣膠帶 0.5吋／1吋 12入', 'M4', 255], ['3M 15301 家用膠帶 12入', 'M5', 99], ['3M 1530-0 通氣膠帶 半吋 12入 展示品', 'M6', 88]];
  fetcher.respond = ({path}) => path === '/v1/momo'
    ? {status: 'ok', raw_count: momo.length, checked_at: iso(now), results: momo.map(([title, code, price]) => ({title, code, price, relevance: 3, match_label: '型號相關', url: 'https://www.momoshop.com.tw/product/' + code}))}
    : {status: 'ok', checked_at: iso(now), source_url: 'https://feebee.com.tw/s/x/', markets: {
      shopee: {status: 'ok', results: [{title: '3M 1530-0 通氣膠帶 半吋 12入', price: 262, url: 'https://feebee.com.tw/p/s1', merchant: '蝦皮商城', seller: '示範藥局'}, {title: '3M 1530-0 通氣膠帶 半吋 24入', price: 480, url: 'https://feebee.com.tw/p/s2', merchant: '蝦皮購物', seller: '示範生活'}, {title: '3M 1530-0 通氣膠帶 半吋', price: 230, url: 'https://feebee.com.tw/p/s3', merchant: '蝦皮購物', seller: '示範百貨'}]},
      coupang: {status: 'ok', results: [{title: '3M 1530-0 通氣膠帶 半吋 12入', price: 285, url: 'https://feebee.com.tw/p/c1', merchant: 'Coupang 酷澎', seller: ''}]}}};

  const product = db.prepare("INSERT INTO tracked_products (id,name,model,attributes_json,package_quantity,package_unit,keyword,momo_keyword,feebee_keyword,my_price_shopee,my_price_momo,my_price_coupang,my_url_shopee,my_url_momo,my_url_coupang,cyweb_item_no,active,next_run_at,last_run_at,revision,created_at,created_by,updated_at,updated_by) VALUES (?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,5,?,'seed',?,'seed')");
  product.run('t-1', '3M 紙膠帶 半吋 12入', '1530-0', '{"尺寸":"0.5吋"}', 12, '入', '3M 1530-0', '', '3M 1530-0 紙膠帶', 320, 300, 270, 'https://shopee.tw/example', 'https://www.momoshop.com.tw/example', '', 'A-1530', 1, now + 9 * day, now - 2 * day, iso(now - 60 * day), iso(now));
  product.run('t-2', '羅技 M331 靜音滑鼠', 'M331', '{}', 1, '個', '羅技 M331', '', '', null, 450, null, '', '', '', '', 1, now + 3 * day, now - 5 * day, iso(now - 30 * day), iso(now));
  product.run('t-3', '舊款膠台', 'TC-100', '{}', 1, '個', 'TC-100', '', '', null, null, null, '', '', '', '', 0, now + 30 * day, null, iso(now - 30 * day), iso(now));

  const change = db.prepare("INSERT INTO my_price_changes (product_id,platform,at,actor_employee_id,actor_name,old_price,new_price,note) VALUES (?,?,?,'seed','測試員工',?,?,?)");
  [['shopee', 60, null, 300, '建立追蹤商品'], ['shopee', 31, 300, 310, '成本調整'], ['shopee', 3, 310, 320, '跟進市場價'],
    ['momo', 60, null, 290, '建立追蹤商品'], ['momo', 20, 290, 300, ''], ['coupang', 60, null, 280, '建立追蹤商品'], ['coupang', 10, 280, 270, '酷澎促銷']]
    .forEach(([platform, ago, old, now_, note]) => change.run('t-1', platform, iso(now - ago * day), old, now_, note));
  change.run('t-2', 'momo', iso(now - 30 * day), null, 450, '建立追蹤商品');

  const run = db.prepare("INSERT INTO tracked_runs VALUES (?,'t-1',?,?,'ok','ok','schedule')"), obs = db.prepare("INSERT INTO price_observations (run_id,product_id,source,listing_key,seller,title,price,url,observed_at) VALUES (?,'t-1',?,?,'',?,?,'',?)");
  [[48, [269, 280, 299], [275, 290], [300]], [41, [262, 275, 299], [280], [295, 310]], [34, [255, 270, 295], [270, 285], [290]], [27, [259, 272, 285], [268], [285, 300]],
    [20, [259, 268, 285], [262, 270], [285]], [13, [259, 265, 285], [262, 264], [285, 290]], [2, [259, 275], [262], [285]]].forEach(([ago, momoPrices, shopeePrices, coupangPrices], i) => {
    run.run('r' + i, iso(now - ago * day), iso(now - ago * day + 20000));
    [['momo', momoPrices], ['shopee', shopeePrices], ['coupang', coupangPrices]].forEach(([source, prices]) => prices.forEach((price, j) => obs.run('r' + i, source, `${source}${j}`, '3M 1530-0 通氣膠帶 半吋 12入', price, iso(now - ago * day))));
  });

  const manual = db.prepare("INSERT INTO manual_comparisons (id,product_id,platform,seller,url,price,checked_at,note,revision,created_at,created_by,updated_at,updated_by) VALUES (?,'t-1',?,?,?,?,?,?,1,?,'seed',?,'seed')");
  [['m-1', 'shopee', '示範特賣店', 'https://shopee.tw/shop/demo', 255, 3, '促銷價'], ['m-2', 'momo', '示範旗艦店', '', 249, 40, '舊價格'], ['m-3', 'other', 'PChome 24h', 'https://24h.pchome.com.tw/prod/demo', 230, 2, '']]
    .forEach(([id, platform, seller, url, price, ago, note]) => manual.run(id, platform, seller, url, price, ymd(now - ago * day), note, iso(now - ago * day), iso(now - ago * day)));

  const cand = db.prepare("INSERT INTO latest_candidates(product_id,source,listing_key,title,price,seller,url,grp,parsed_json,run_id) VALUES (?,?,?,?,?,?,?,?,?,?)");
  const parsed = (quantity, unit, reason = '') => JSON.stringify({model: '1530-0', attributes: {}, quantity, unit, issues: [], reason});
  const rows = [['momo', 'M1', momo[0][0], 259, '', 'same', parsed(12, '入')], ['momo', 'M2', momo[1][0], 275, '', 'same', parsed(12, '入', '人工確認')],
    ['shopee', 'https://feebee.com.tw/p/s1', '3M 1530-0 通氣膠帶 半吋 12入', 262, '示範藥局', 'same', parsed(12, '入')], ['coupang', 'https://feebee.com.tw/p/c1', '3M 1530-0 通氣膠帶 半吋 12入', 285, '', 'same', parsed(12, '入')],
    ['momo', 'M3', momo[2][0], 140, '', 'other', parsed(6, '入')], ['shopee', 'https://feebee.com.tw/p/s2', '3M 1530-0 通氣膠帶 半吋 24入', 480, '示範生活', 'other', parsed(24, '入')],
    ['momo', 'M4', momo[3][0], 255, '', 'pending', parsed(12, '入', '尺寸出現多種選項')], ['shopee', 'https://feebee.com.tw/p/s3', '3M 1530-0 通氣膠帶 半吋', 230, '示範百貨', 'pending', parsed(null, '', '無法辨識包裝數量')],
    ['momo', 'M5', momo[4][0], 99, '', 'unrelated', parsed(12, '入')], ['momo', 'M6', momo[5][0], 88, '', 'excluded', parsed(12, '入')]];
  rows.forEach(([source, key, title, price, seller, grp, json]) => cand.run('t-1', source, key, title, price, seller, source === 'momo' ? 'https://www.momoshop.com.tw/product/' + key : key, grp, json, 'r6'));
  if (process.env.FAKE_GROK) cand.run('t-1','coupang','https://market.example.test/other',
    '3M 1530-0 通氣膠帶 半吋 6入',149,'酷澎','https://market.example.test/other','other',parsed(6,'入'),'r6');
  const m331 = JSON.stringify({model: 'M331', attributes: {}, quantity: 1, unit: '個', issues: [], reason: ''});
  cand.run('t-2', 'momo', 'L1', '羅技 M331 靜音滑鼠 黑色', 429, '', 'https://www.momoshop.com.tw/product/L1', 'same', m331, 'r-l2');
  cand.run('t-2', 'momo', 'L2', '羅技 M331 靜音滑鼠 黑色／藍色 任選', 399, '', 'https://www.momoshop.com.tw/product/L2', 'pending', JSON.stringify({model: 'M331', attributes: {}, quantity: 1, unit: '個', issues: [], reason: '可能有多規格或多種商品選項'}), 'r-l2');
  db.prepare("INSERT INTO listing_judgments VALUES ('t-1','momo','M6','not_same',NULL,'',?,?,'seed')").run(momo[5][0], iso(now - 2 * day));
  db.prepare("INSERT INTO listing_judgments VALUES ('t-1','momo','M2','same',NULL,'',?,?,'seed')").run(momo[1][0], iso(now - 20 * day));

  // Recent searches (older than the 10 minute cache, so choosing one queries the fake fetcher again).
  const cache = db.prepare("INSERT INTO search_cache VALUES ('momo',?,1,'{}',?)");
  [['3M 1530-0', 1], ['羅技 M331', 3], ['醫療膠帶 半吋', 5]].forEach(([keyword, hours]) => cache.run(keyword, now - hours * 3600000));
  const audit = db.prepare('INSERT INTO audit_log (at,actor_employee_id,actor_name,action,entity_id,before_json,after_json) VALUES (?,?,?,?,?,?,?)');
  audit.run(iso(now - 3 * day), 'seed', '測試員工', 'tracked.price', 't-1', JSON.stringify({platform: 'shopee', myPrice: 310}), JSON.stringify({platform: 'shopee', myPrice: 320, note: '跟進市場價', name: '3M 紙膠帶 半吋 12入'}));
  audit.run(iso(now - 3 * day), 'seed', '測試員工', 'manual.create', 'm-1', null, JSON.stringify({name: '3M 紙膠帶 半吋 12入', platform: 'shopee', seller: '示範特賣店', price: 255}));
}

const port = Number(process.env.PORT) || 4173;
http.createServer(async (req, res) => {
  const chunks = [];
  for await (const chunk of req) chunks.push(chunk);
  if (process.env.NO_GAP) env.DB.sqlite.exec('UPDATE upstream_state SET last_request_at=0');
  const body = ['GET', 'HEAD'].includes(req.method) ? undefined : Buffer.concat(chunks);
  // Stand-in for the CY Web portal: /__portal/sso immediately sends the browser back with a fresh one-time code.
  const portalUrl = new URL(req.url, `http://localhost:${port}`);
  if (portalUrl.pathname === '/__portal/sso') {
    const back = new URL('/auth/sso/callback', `http://localhost:${port}`);
    back.searchParams.set('code', identity.issueCode('0001'));
    back.searchParams.set('state', portalUrl.searchParams.get('state') || '');
    res.writeHead(303, {location: back.pathname + back.search});
    return res.end();
  }
  const response = await worker.fetch(new Request(`http://localhost:${port}${req.url}`, {method: req.method, headers: req.headers, body, redirect: 'manual'}), env);
  const headers = {};
  for (const [key, value] of response.headers) headers[key] = value;
  const cookies = response.headers.getSetCookie?.() ?? [];
  if (cookies.length) headers['set-cookie'] = cookies;
  if (headers.location?.startsWith(env.CYWEB_PORTAL_URL)) headers.location = headers.location.replace(env.CYWEB_PORTAL_URL.replace(/\/$/, ''), '/__portal');
  res.writeHead(response.status, headers);
  res.end(Buffer.from(await response.arrayBuffer()));
}).listen(port, '127.0.0.1');
console.log(`CYPriceDesk local server on http://localhost:${port}`);
