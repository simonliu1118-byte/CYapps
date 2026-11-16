import test from 'node:test';
import assert from 'node:assert/strict';
import worker from '../src/index.js';
import {classifyListing} from '../src/grouping.js';
import {inRunWindow, randomRunTime, runScheduled} from '../src/tracking-run.js';
import {createTestEnv} from './support/env.js';
import {installFakeFetcher} from './support/fake-fetcher.js';

const ORIGIN = 'https://pricedesk.test';
const HOUR = 3600 * 1000, DAY = 24 * HOUR;
const tracked = {model: '1530-0', attributes: {尺寸: '0.5吋'}, packageQuantity: 12, packageUnit: '入'};

test('分組：同規格、其他包裝、待確認、無關', () => {
  const cases = [
    ['3M 1530-0 通氣膠帶 半吋 12入', 'same'],
    ['3M 1530-0 通氣膠帶 半吋 6入', 'other'],
    ['3M 1530-0 通氣膠帶 半吋 1盒', 'other'],
    ['3M 1530-0 通氣膠帶 0.5吋／1吋 12入', 'pending'],   // 標題同時出現多個規格
    ['3M 1530-0 通氣膠帶 半吋', 'pending'],               // 解析不出數量
    ['3M 1530-0 通氣膠帶 12入', 'pending'],               // 規格屬性不明
    ['3M 1530-0 通氣膠帶 1吋 12入', 'pending'],           // 規格屬性不同
    ['3M 1530-0 / 1530-1 膠帶 半吋 12入', 'pending'],     // 多個型號
    ['3M 15300 通氣膠帶 半吋 12入', 'unrelated'],         // 型號不同
    ['3M 1530-1 通氣膠帶 半吋 12入', 'unrelated'],
    ['醫療膠帶 半吋 12入', 'unrelated']                   // 不含型號
  ];
  for (const [title, group] of cases) assert.equal(classifyListing(tracked, title).group, group, title);
  assert.equal(classifyListing({...tracked, model: '1530'}, '3M 15301 膠帶 半吋 12入').group, 'unrelated');
  assert.equal(classifyListing({...tracked, model: '1530'}, '3M 1530-0 膠帶 半吋 12入').group, 'same');
});

test('人工判斷：覆蓋自動分組，標題變了回到待確認', () => {
  const title = '3M 1530-0 通氣膠帶 半吋 12入';
  const j = (verdict, extra = {}) => ({verdict, titleSnapshot: title, ...extra});
  assert.equal(classifyListing(tracked, title, j('not_same')).group, 'excluded');
  assert.equal(classifyListing(tracked, '3M 1530-0 半吋', j('same', {titleSnapshot: '3M 1530-0 半吋'})).group, 'same');
  const other = classifyListing(tracked, '3M 1530-0 半吋', j('other_package', {titleSnapshot: '3M 1530-0 半吋', packageQuantity: 24, packageUnit: '入'}));
  assert.deepEqual([other.group, other.quantity, other.unit], ['other', 24, '入']);
  const changed = classifyListing(tracked, title + ' 新包裝', j('not_same'));
  assert.equal(changed.group, 'pending');
});

test('排程時間：下次查價在 7–14 天後的 08:00–22:00（臺北）；新增為 0–7 天；窗外不執行', () => {
  const now = Date.parse('2026-11-16T03:00:00Z');
  for (let i = 0; i < 200; i++) {
    const t = randomRunTime(now, 7, 14);
    const taipei = new Date(t + 8 * HOUR);
    assert(t >= now + 6 * DAY && t <= now + 15 * DAY);
    assert(taipei.getUTCHours() >= 8 && taipei.getUTCHours() < 22);
    assert(randomRunTime(now, 0, 7) >= now);
  }
  assert.equal(inRunWindow(Date.parse('2026-11-16T00:00:00Z')), true);   // 08:00
  assert.equal(inRunWindow(Date.parse('2026-11-16T13:59:00Z')), true);   // 21:59
  assert.equal(inRunWindow(Date.parse('2026-11-16T14:00:00Z')), false);  // 22:00
  assert.equal(inRunWindow(Date.parse('2026-11-16T23:59:00Z')), false);  // 07:59
});

async function setup() {
  const shared = createTestEnv({acceptAnyLogin: true});
  const login = await worker.fetch(new Request(ORIGIN + '/api/auth/login', {method: 'POST', body: new URLSearchParams({employeeNo: '0001', password: 'password1'})}), shared.env);
  const cookie = login.headers.get('set-cookie').split(';')[0];
  const fetcher = installFakeFetcher();
  const call = (method, path, json) => worker.fetch(new Request(ORIGIN + path, {
    method, redirect: 'manual', headers: {cookie, ...(json ? {'content-type': 'application/json'} : {})}, body: json ? JSON.stringify(json) : undefined
  }), shared.env);
  const sql = shared.env.DB.sqlite;
  const open = () => sql.prepare('UPDATE upstream_state SET last_request_at=0, blocked_until=0').run();
  return {...shared, fetcher, call, sql, open};
}

const momoResults = titles => ({status: 'ok', raw_count: titles.length, checked_at: '2026-11-16T02:00:00+00:00',
  results: titles.map(([title, code, price]) => ({title, code, price, relevance: 1, match_label: '型號相關', url: 'https://www.momoshop.com.tw/product/' + code}))});
const feebeeResults = (...items) => ({status: 'ok', checked_at: '2026-11-16T02:00:00+00:00', source_url: 'https://feebee.com.tw/s/x/',
  markets: {shopee: {status: 'ok', results: items.map(([title, price, seller], i) => ({title, price, url: 'https://feebee.com.tw/p/' + i, merchant: '蝦皮購物', seller}))}, coupang: {status: 'ok', results: []}}});
const product = {name: '3M 紙膠帶 半吋 12入', model: '1530-0', attributes: {尺寸: '0.5吋'}, packageQuantity: 12, packageUnit: '入', myPrice: 320, momoKeyword: '3M 1530-0', feebeeKeyword: '3M 1530-0 紙膠帶'};
const TITLES = [['3M 1530-0 通氣膠帶 半吋 12入', 'A1', 259], ['3M 1530-0 通氣膠帶 半吋 6入', 'A2', 140], ['3M 1530-0 通氣膠帶 12入', 'A3', 250], ['3M 15301 膠帶 半吋 12入', 'A4', 99], ['3M 1530-0 通氣膠帶 半吋 12入 限量', 'A5', 275]];

test('追蹤商品：建立、修改、改售價都寫紀錄與 audit；revision 衝突回 409', async t => {
  const a = await setup();
  t.after(a.fetcher.restore);
  assert.equal((await a.call('POST', '/api/tracked', {...product, momoKeyword: '', feebeeKeyword: ''})).status, 400);
  const created = await (await a.call('POST', '/api/tracked', product)).json();
  const id = created.product.id;
  assert.equal(created.product.myPrice, 320);
  const soon = created.product.nextRunAt - Date.now();
  assert(soon >= -1000 && soon <= 8 * DAY);
  const edited = await a.call('PUT', `/api/tracked/${id}`, {...product, name: '改名', myPrice: 1, revision: 1});
  assert.equal(edited.status, 200);
  const body = await edited.json();
  assert.equal(body.product.name, '改名');assert.equal(body.product.myPrice, 320); // 售價不能由編輯改
  assert.equal((await a.call('PUT', `/api/tracked/${id}`, {...product, revision: 1})).status, 409);
  const priced = await a.call('PUT', `/api/tracked/${id}/price`, {price: 350, note: '調價', revision: 2});
  assert.equal(priced.status, 200);
  assert.equal((await a.call('PUT', `/api/tracked/${id}/price`, {price: 360, revision: 2})).status, 409);
  const d = await (await a.call('GET', `/api/tracked/${id}`)).json();
  assert.deepEqual(d.priceChanges.map(c => [c.oldPrice, c.newPrice, c.note, c.actorName]), [[320, 350, '調價', '測試員工'], [null, 320, '建立追蹤商品', '測試員工']]);
  assert.deepEqual(a.sql.prepare("SELECT action FROM audit_log WHERE action LIKE 'tracked.%' ORDER BY id").all().map(x => x.action), ['tracked.create', 'tracked.update', 'tracked.price']);
  const entries = (await (await a.call('GET', '/api/audit')).json()).entries;
  assert(entries.some(e => e.action === 'tracked.price' && e.detail.includes('320 → 350')));
});

test('立即查價：分組、儲存方式 B；判斷下次查價自動套用；標題變了回待確認', async t => {
  const a = await setup();
  t.after(a.fetcher.restore);
  const id = (await (await a.call('POST', '/api/tracked', product)).json()).product.id;
  a.fetcher.respond = ({path}) => path === '/v1/momo' ? momoResults(TITLES) : feebeeResults(['3M 1530-0 通氣膠帶 半吋 12入', 262, '示範藥局']);
  const run = await (await a.call('POST', `/api/tracked/${id}/run`)).json();
  assert.deepEqual([run.momoStatus, run.feebeeStatus, run.sameCount], ['ok', 'ok', 3]);
  const groups = async () => Object.fromEntries((await (await a.call('GET', `/api/tracked/${id}`)).json()).candidates.map(c => [c.listingKey.slice(-2), c.group]));
  assert.deepEqual(await groups(), {A1: 'same', A2: 'other', A3: 'pending', A4: 'unrelated', A5: 'same', '/0': 'same'});
  assert.equal(a.sql.prepare('SELECT COUNT(*) n FROM price_observations').get().n, 3);
  const summaries = a.sql.prepare('SELECT group_key, count, min_price, median_price, max_price FROM group_summaries ORDER BY group_key').all();
  assert.deepEqual(summaries.map(s => [s.group_key, s.count, s.min_price, s.max_price]), [['other:6:入', 1, 140, 140], ['same', 3, 259, 275]]);
  assert.equal(a.sql.prepare("SELECT COUNT(*) n FROM audit_log WHERE action='tracked.run'").get().n, 1);

  // Judgments: A3 -> same, A5 -> not the same product.
  assert.equal((await a.call('PUT', `/api/tracked/${id}/judgments`, {source: 'momo', listingKey: 'A3', verdict: 'same'})).status, 200);
  assert.equal((await a.call('PUT', `/api/tracked/${id}/judgments`, {source: 'momo', listingKey: 'A5', verdict: 'not_same'})).status, 200);
  assert.equal((await a.call('PUT', `/api/tracked/${id}/judgments`, {source: 'momo', listingKey: 'A2', verdict: 'other_package'})).status, 400); // 缺數量與單位
  assert.equal((await a.call('PUT', `/api/tracked/${id}/judgments`, {source: 'momo', listingKey: 'nope', verdict: 'same'})).status, 404);
  assert.equal((await groups()).A5, 'excluded');
  const listed = (await (await a.call('GET', '/api/tracked')).json()).products[0];
  assert.deepEqual([listed.sameCount, listed.sameMin, listed.pendingCount], [3, 250, 0]);

  // Next run: the judgments apply without asking again; A3's title changes, so it is pending again.
  a.open();
  const changed = TITLES.map(x => x[1] === 'A3' ? ['3M 1530-0 通氣膠帶 12入 新包裝', 'A3', 250] : x);
  a.fetcher.respond = ({path}) => path === '/v1/momo' ? momoResults(changed) : feebeeResults();
  a.sql.exec('DELETE FROM search_cache');
  await a.call('POST', `/api/tracked/${id}/run`);
  assert.deepEqual(await groups(), {A1: 'same', A2: 'other', A3: 'pending', A4: 'unrelated', A5: 'excluded'});
  assert.equal(a.sql.prepare('SELECT COUNT(*) n FROM latest_candidates').get().n, 5); // latest_candidates 每次覆蓋
  assert.equal(a.sql.prepare('SELECT COUNT(*) n FROM tracked_runs').get().n, 2);        // 其他表只增不刪

  // Restore A5.
  assert.equal((await a.call('DELETE', `/api/tracked/${id}/judgments?source=momo&listingKey=A5`)).status, 200);
  assert.equal((await groups()).A5, 'same');
  assert.deepEqual(a.sql.prepare("SELECT action FROM audit_log WHERE action LIKE 'judgment.%' ORDER BY id").all().map(x => x.action), ['judgment.update', 'judgment.update', 'judgment.delete']);
});

test('排程：只在時間窗內、一次只處理 1 個到期商品、查完 7–14 天後；被節流延後 1 小時', async t => {
  const a = await setup();
  t.after(a.fetcher.restore);
  a.fetcher.respond = ({path}) => path === '/v1/momo' ? momoResults(TITLES) : feebeeResults();
  const ids = [];
  for (const name of ['甲', '乙', '丙']) ids.push((await (await a.call('POST', '/api/tracked', {...product, name})).json()).product.id);
  const base = Date.parse('2026-11-16T02:00:00Z'); // 10:00 Taipei
  ids.forEach((id, i) => a.sql.prepare('UPDATE tracked_products SET next_run_at=? WHERE id=?').run(base - (3 - i) * 1000, id));
  a.sql.prepare('UPDATE tracked_products SET next_run_at=? WHERE id=?').run(base + DAY, ids[2]); // 丙 not due

  assert.equal(await runScheduled(a.env, {now: Date.parse('2026-11-15T15:00:00Z')}), null); // 23:00 Taipei: outside the window
  assert.equal(a.fetcher.calls.length, 0);

  const first = await runScheduled(a.env, {now: base});
  assert.equal(first.id, ids[0]);                       // earliest next_run_at first
  assert.equal(a.sql.prepare('SELECT COUNT(*) n FROM tracked_runs').get().n, 1);
  const next = a.sql.prepare('SELECT next_run_at FROM tracked_products WHERE id=?').get(ids[0]).next_run_at;
  assert(next >= base + 6 * DAY && next <= base + 15 * DAY);
  const hour = new Date(next + 8 * HOUR).getUTCHours();
  assert(hour >= 8 && hour < 22);
  assert.equal(a.sql.prepare("SELECT trigger FROM tracked_runs").get().trigger, 'schedule');

  a.open();
  assert.equal((await runScheduled(a.env, {now: base + 1000})).id, ids[1]);
  assert.equal(await runScheduled(a.env, {now: base + 2000}), null); // 丙 is not due

  // Throttled momo: the product waits one hour, the Feebee result is still stored.
  a.sql.prepare('UPDATE tracked_products SET next_run_at=? WHERE id=?').run(base, ids[2]);
  a.sql.exec('DELETE FROM search_cache');
  a.sql.prepare("UPDATE upstream_state SET last_request_at=? WHERE upstream='momo'").run(Date.now());
  const throttled = await runScheduled(a.env, {now: base + 3000});
  assert.equal(throttled.momoStatus, 'rate_limited');
  assert.equal(a.sql.prepare('SELECT next_run_at FROM tracked_products WHERE id=?').get(ids[2]).next_run_at, base + 3000 + HOUR);
  const run = a.sql.prepare('SELECT * FROM tracked_runs WHERE product_id=?').get(ids[2]);
  assert.deepEqual([run.momo_status, run.feebee_status], ['rate_limited', 'ok']);
});
