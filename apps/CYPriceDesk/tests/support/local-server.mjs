// Local server for browser checks: the real Worker with a fake IDENTITY (accepts any login as 「測試員工」)
// and an in-memory node:sqlite D1. The price fetcher is a fake returning fixture data (no network).
// Usage: PORT=4173 SEED=1 [NO_GAP=1] node tests/support/local-server.mjs   (NO_GAP drops the 15 s upstream spacing, for screenshots)
import http from 'node:http';
import worker from '../../src/index.js';
import {createTestEnv} from './env.js';
import {installFakeFetcher} from './fake-fetcher.js';

const {env} = createTestEnv({acceptAnyLogin: true});
installFakeFetcher();
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
