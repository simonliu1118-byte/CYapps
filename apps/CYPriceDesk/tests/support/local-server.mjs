// Local server for browser checks: the real Worker with a fake IDENTITY (accepts any login as 「測試員工」)
// and an in-memory node:sqlite D1. The price fetcher is a fake returning fixture data (no network).
// Usage: PORT=4173 SEED=1 [NO_GAP=1] node tests/support/local-server.mjs   (NO_GAP drops the 15 s upstream spacing, for screenshots)
import http from 'node:http';
import worker from '../../src/index.js';
import {createTestEnv} from './env.js';
import {installFakeFetcher} from './fake-fetcher.js';

const {env} = createTestEnv({acceptAnyLogin: true});
const fetcher = installFakeFetcher();
if (process.env.SEED) {
  const ago = hours => new Date(Date.now() - hours * 3600_000).toISOString();
  const rows = [
    ['示範耳機', 'A1／黑色', '蝦皮', '示範商城 A', 2490, 0, ago(2), ''],
    ['示範耳機', 'A1／黑色', 'momo', '示範商城 B', 2690, 0, ago(3), ''],
    ['示範耳機', 'A1／黑色', 'PChome', '示範商城 C', 2790, null, ago(5), '運費待確認'],
    ['示範充電器', '65W／單入', '蝦皮', '示範配件館', 690, 60, ago(24 * 9), ''],
    ['示範充電器', '65W／單入', 'momo', '示範通路', 729, 0, ago(30), '']
  ];
  rows.forEach(([product, spec, platform, seller, price, shipping, checkedAt, note], i) => env.DB.sqlite.prepare(
    "INSERT INTO price_records (id,product,spec,platform,seller,price,shipping,checked_at,note,created_at,created_by,updated_at,updated_by) VALUES (?,?,?,?,?,?,?,?,?,?,'seed',?,'seed')"
  ).run(`seed-${i}`, product, spec, platform, seller, price, shipping, checkedAt, note, `2026-01-01T00:00:0${i}Z`, `2026-01-01T00:00:0${i}Z`));
  const audit = env.DB.sqlite.prepare('INSERT INTO audit_log (at,actor_employee_id,actor_name,action,entity_id,after_json) VALUES (?,?,?,?,?,?)');
  audit.run(ago(30), 'seed', '測試員工', 'record.create', 'seed-0', JSON.stringify({product: '示範耳機', spec: 'A1／黑色', platform: '蝦皮'}));
  audit.run(ago(3), 'seed', '測試員工', 'record.update', 'seed-1', JSON.stringify({product: '示範耳機', spec: 'A1／黑色', platform: 'momo'}));
  audit.run(ago(1), 'seed', '測試員工', 'record.import', null, JSON.stringify({added: 2, updated: 1, skipped: 0}));
}

if (process.env.SEED) seedTracked();

// Screenshot data: one tracked 3M 1530-0 product with same-spec, other-package, pending, unrelated and excluded candidates.
function seedTracked() {
  const day = 86400000, db = env.DB.sqlite, now = Date.now();
  const iso = ms => new Date(ms).toISOString();
  const momo = [['3M 1530-0 通氣膠帶 半吋 12入', 'M1', 259], ['3M 1530-0 通氣膠帶 半吋 12入 單盒', 'M2', 275], ['3M 1530-0 通氣膠帶 半吋 6入', 'M3', 140],
    ['3M 1530-0 通氣膠帶 0.5吋／1吋 12入', 'M4', 255], ['3M 15301 家用膠帶 12入', 'M5', 99], ['3M 1530-0 通氣膠帶 半吋 12入 展示品', 'M6', 88]];
  fetcher.respond = ({path}) => path === '/v1/momo'
    ? {status: 'ok', raw_count: momo.length, checked_at: iso(now), results: momo.map(([title, code, price]) => ({title, code, price, relevance: 3, match_label: '型號相關', url: 'https://www.momoshop.com.tw/product/' + code}))}
    : {status: 'ok', checked_at: iso(now), source_url: 'https://feebee.com.tw/s/x/', markets: {
      shopee: {status: 'ok', results: [{title: '3M 1530-0 通氣膠帶 半吋 12入', price: 262, url: 'https://feebee.com.tw/p/s1', merchant: '蝦皮商城', seller: '示範藥局'}, {title: '3M 1530-0 通氣膠帶 半吋 24入', price: 480, url: 'https://feebee.com.tw/p/s2', merchant: '蝦皮購物', seller: '示範生活'}, {title: '3M 1530-0 通氣膠帶 半吋', price: 230, url: 'https://feebee.com.tw/p/s3', merchant: '蝦皮購物', seller: '示範百貨'}]},
      coupang: {status: 'ok', results: [{title: '3M 1530-0 通氣膠帶 半吋 12入', price: 285, url: 'https://feebee.com.tw/p/c1', merchant: 'Coupang 酷澎', seller: ''}]}}};
  const product = db.prepare("INSERT INTO tracked_products (id,name,model,attributes_json,package_quantity,package_unit,my_price,my_url,momo_keyword,feebee_keyword,active,next_run_at,last_run_at,revision,created_at,created_by,updated_at,updated_by) VALUES (?,?,?,?,?,?,?,?,?,?,?,?,?,3,?,'seed',?,'seed')");
  product.run('t-1', '3M 紙膠帶 半吋 12入', '1530-0', '{"尺寸":"0.5吋"}', 12, '入', 320, 'https://shopee.tw/example', '3M 1530-0', '3M 1530-0 紙膠帶', 1, now + 9 * day, now - 2 * day, iso(now - 60 * day), iso(now));
  product.run('t-2', '羅技 M331 靜音滑鼠', 'M331', '{}', 1, '個', 450, '', '羅技 M331', '羅技 M331', 1, now + 3 * day, now - 5 * day, iso(now - 30 * day), iso(now));
  product.run('t-3', '舊款膠台', 'TC-100', '{}', 1, '個', null, '', 'TC-100', '', 0, now + 30 * day, null, iso(now - 30 * day), iso(now));
  const change = db.prepare("INSERT INTO my_price_changes (product_id,at,actor_employee_id,actor_name,old_price,new_price,note) VALUES ('t-1',?,'seed','測試員工',?,?,?)");
  change.run(iso(now - 60 * day), null, 300, '建立追蹤商品'); change.run(iso(now - 31 * day), 300, 310, '成本調整'); change.run(iso(now - 3 * day), 310, 320, '跟進市場價');
  const run = db.prepare("INSERT INTO tracked_runs VALUES (?,'t-1',?,?,'ok','ok','schedule')"), summary = db.prepare("INSERT INTO group_summaries VALUES (?,'t-1','same','同規格',?,?,?,?)");
  [[48, 4, 269, 280, 299], [41, 5, 262, 275, 299], [34, 5, 255, 270, 295], [27, 4, 259, 272, 285], [20, 6, 259, 268, 285], [13, 5, 259, 265, 285], [2, 5, 259, 268, 285]].forEach(([ago, n, min, med, max], i) => {
    run.run('r' + i, iso(now - ago * day), iso(now - ago * day + 20000)); summary.run('r' + i, n, min, med, max);
  });
  const cand = db.prepare("INSERT INTO latest_candidates VALUES ('t-1',?,?,?,?,?,?,?,?)");
  const parsed = (quantity, unit, reason = '') => JSON.stringify({model: '1530-0', attributes: {}, quantity, unit, issues: [], reason});
  const rows = [['momo', 'M1', momo[0][0], 259, '', 'same', parsed(12, '入')], ['momo', 'M2', momo[1][0], 275, '', 'same', parsed(12, '入', '人工確認')],
    ['shopee', 'https://feebee.com.tw/p/s1', '3M 1530-0 通氣膠帶 半吋 12入', 262, '示範藥局', 'same', parsed(12, '入')], ['coupang', 'https://feebee.com.tw/p/c1', '3M 1530-0 通氣膠帶 半吋 12入', 285, '', 'same', parsed(12, '入')],
    ['momo', 'M3', momo[2][0], 140, '', 'other', parsed(6, '入')], ['shopee', 'https://feebee.com.tw/p/s2', '3M 1530-0 通氣膠帶 半吋 24入', 480, '示範生活', 'other', parsed(24, '入')],
    ['momo', 'M4', momo[3][0], 255, '', 'pending', parsed(12, '入', '尺寸出現多種選項')], ['shopee', 'https://feebee.com.tw/p/s3', '3M 1530-0 通氣膠帶 半吋', 230, '示範百貨', 'pending', parsed(null, '', '無法辨識包裝數量')],
    ['momo', 'M5', momo[4][0], 99, '', 'unrelated', parsed(12, '入')], ['momo', 'M6', momo[5][0], 88, '', 'excluded', parsed(12, '入')]];
  rows.forEach(([source, key, title, price, seller, grp, json]) => cand.run(source, key, title, price, seller, source === 'momo' ? 'https://www.momoshop.com.tw/product/' + key : key, grp, json));
  db.prepare("INSERT INTO listing_judgments VALUES ('t-1','momo','M6','not_same',NULL,'',?,?,'seed')").run(momo[5][0], iso(now - 2 * day));
  db.prepare("INSERT INTO listing_judgments VALUES ('t-1','momo','M2','same',NULL,'',?,?,'seed')").run(momo[1][0], iso(now - 20 * day));
}

const port = Number(process.env.PORT) || 4173;
http.createServer(async (req, res) => {
  const chunks = [];
  for await (const chunk of req) chunks.push(chunk);
  if (process.env.NO_GAP) env.DB.sqlite.exec('UPDATE upstream_state SET last_request_at=0');
  const body = ['GET', 'HEAD'].includes(req.method) ? undefined : Buffer.concat(chunks);
  const response = await worker.fetch(new Request(`http://localhost:${port}${req.url}`, {method: req.method, headers: req.headers, body, redirect: 'manual'}), env);
  const headers = {};
  for (const [key, value] of response.headers) headers[key] = value;
  const cookies = response.headers.getSetCookie?.() ?? [];
  if (cookies.length) headers['set-cookie'] = cookies;
  res.writeHead(response.status, headers);
  res.end(Buffer.from(await response.arrayBuffer()));
}).listen(port, '127.0.0.1');
console.log(`CYPriceDesk local server on http://localhost:${port}`);
