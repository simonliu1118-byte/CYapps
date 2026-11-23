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
  const shared = createTestEnv();
  const cookie = shared.identity.cookieFor('0001');
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
const product = {name: '3M 紙膠帶 半吋 12入', model: '1530-0', attributes: {尺寸: '0.5吋'}, packageQuantity: 12, packageUnit: '入', cywebItemNo: 'A-100', keyword: '3M 1530-0',
  myPrices: {shopee: 320, momo: 300, coupang: ''}, myUrls: {shopee: 'https://shopee.tw/example'}};
const TITLES = [['3M 1530-0 通氣膠帶 半吋 12入', 'A1', 259], ['3M 1530-0 通氣膠帶 半吋 6入', 'A2', 140], ['3M 1530-0 通氣膠帶 12入', 'A3', 250], ['3M 15301 膠帶 半吋 12入', 'A4', 99], ['3M 1530-0 通氣膠帶 半吋 12入 限量', 'A5', 275]];

test('追蹤商品：建立、修改、改售價（分平台）都寫紀錄與 audit，且與資料同一個 batch；revision 衝突回 409', async t => {
  const a = await setup();
  t.after(a.fetcher.restore);
  const batches = [];
  const batch = a.env.DB.batch;
  a.env.DB.batch = async list => {batches.push(list.length);return batch(list);};
  assert.equal((await a.call('POST', '/api/tracked', {...product, keyword: ''})).status, 400);
  const created = await (await a.call('POST', '/api/tracked', product)).json();
  const id = created.product.id;
  assert.deepEqual(created.product.myPrices, {shopee: 320, momo: 300, coupang: null});
  assert.equal(created.product.cywebItemNo, 'A-100');
  const soon = created.product.nextRunAt - Date.now();
  assert(soon >= -1000 && soon <= 8 * DAY);
  const edited = await a.call('PUT', `/api/tracked/${id}`, {...product, name: '改名', myPrices: {shopee: 1}, momoKeyword: 'momo 專用', revision: 1});
  assert.equal(edited.status, 200);
  const body = await edited.json();
  assert.equal(body.product.name, '改名');assert.equal(body.product.myPrices.shopee, 320); // 售價不能由編輯改
  assert.equal(body.product.momoKeyword, 'momo 專用');
  assert.equal((await a.call('PUT', `/api/tracked/${id}`, {...product, revision: 1})).status, 409);
  assert.equal((await a.call('PUT', `/api/tracked/${id}/price`, {price: 350, revision: 2})).status, 400); // 缺平台
  const priced = await a.call('PUT', `/api/tracked/${id}/price`, {platform: 'coupang', price: 280, note: '新增酷澎', revision: 2});
  assert.equal(priced.status, 200);
  assert.equal((await a.call('PUT', `/api/tracked/${id}/price`, {platform: 'shopee', price: 360, revision: 2})).status, 409);
  const d = await (await a.call('GET', `/api/tracked/${id}`)).json();
  assert.deepEqual(d.priceChanges.map(c => [c.platform, c.oldPrice, c.newPrice, c.note, c.actorName]),
    [['coupang', null, 280, '新增酷澎', '測試員工'], ['momo', null, 300, '建立追蹤商品', '測試員工'], ['shopee', null, 320, '建立追蹤商品', '測試員工']]);
  assert.deepEqual(batches, [4, 2, 3]);   // 建立：商品＋兩筆售價＋audit，修改，改售價
  assert.deepEqual(a.sql.prepare("SELECT action FROM audit_log WHERE action LIKE 'tracked.%' ORDER BY id").all().map(x => x.action), ['tracked.create', 'tracked.update', 'tracked.price']);
  const entries = (await (await a.call('GET', '/api/audit')).json()).entries;
  assert(entries.some(e => e.action === 'tracked.price' && e.detail.includes('酷澎：未設定 → 280')));
});

test('手動比價：新增、修改、刪除各一筆 audit 且同一 batch；日期與 revision 檢查；30 天內才計入平台比較', async t => {
  const a = await setup();
  t.after(a.fetcher.restore);
  const id = (await (await a.call('POST', '/api/tracked', product)).json()).product.id;
  const batches = [];
  const batch = a.env.DB.batch;
  a.env.DB.batch = async list => {batches.push(list.length);return batch(list);};
  const day = offset => new Date(Date.now() + 8 * HOUR - offset * DAY).toISOString().slice(0, 10);
  const manual = {platform: 'shopee', seller: '示範店', url: 'https://shopee.tw/x', price: 250, checkedAt: day(0), note: '現場看到'};
  assert.equal((await a.call('POST', `/api/tracked/${id}/manual`, {...manual, platform: 'pchome'})).status, 400);
  assert.equal((await a.call('POST', `/api/tracked/${id}/manual`, {...manual, checkedAt: day(-1)})).status, 400);   // 明天
  const created = await a.call('POST', `/api/tracked/${id}/manual`, manual);
  assert.equal(created.status, 201);
  const mid = (await created.json()).manual.id;
  const listed = async () => (await (await a.call('GET', '/api/tracked')).json()).products[0];
  assert.deepEqual([(await listed()).platforms.shopee.min, (await listed()).platforms.shopee.warn], [250, true]);
  assert.equal((await a.call('PUT', `/api/tracked/${id}/manual/${mid}`, {...manual, price: 255, revision: 1})).status, 200);
  assert.equal((await a.call('PUT', `/api/tracked/${id}/manual/${mid}`, {...manual, price: 1, revision: 1})).status, 409);
  assert.equal((await a.call('PUT', `/api/tracked/${id}/manual/${mid}`, {...manual, price: 255, checkedAt: day(31), revision: 2})).status, 200);
  const detail = await (await a.call('GET', `/api/tracked/${id}`)).json();
  assert.deepEqual(detail.manual.map(m => [m.price, m.counted]), [[255, false]]);            // 超過 30 天，不計入
  assert.equal(detail.platforms.shopee.min, null);
  assert.equal((await a.call('DELETE', `/api/tracked/${id}/manual/${mid}?revision=2`)).status, 409);
  assert.equal((await a.call('DELETE', `/api/tracked/${id}/manual/${mid}?revision=3`)).status, 200);
  assert.deepEqual(batches, [2, 2, 2, 2]);
  assert.deepEqual(a.sql.prepare("SELECT action FROM audit_log WHERE action LIKE 'manual.%' ORDER BY id").all().map(x => x.action), ['manual.create', 'manual.update', 'manual.update', 'manual.delete']);
});

test('列表：三平台各自比較與「最需要注意」資料；待確認清單與導覽數字；最近查價', async t => {
  const a = await setup();
  t.after(a.fetcher.restore);
  a.fetcher.respond = ({path}) => path === '/v1/momo' ? momoResults(TITLES) : feebeeResults(['3M 1530-0 通氣膠帶 半吋 12入', 262, '示範藥局']);
  const id = (await (await a.call('POST', '/api/tracked', product)).json()).product.id;
  await a.call('POST', `/api/tracked/${id}/run`);
  const p = (await (await a.call('GET', '/api/tracked')).json()).products[0];
  assert.deepEqual([p.platforms.shopee.min, p.platforms.momo.min, p.platforms.coupang.min], [262, 259, null]);
  assert.deepEqual([p.platforms.shopee.warn, p.platforms.momo.warn, p.platforms.coupang.warn], [true, true, false]);   // 320 vs 262、300 vs 259
  assert.equal(Math.round(p.attention * 1000), 221);
  assert.equal(p.pendingCount, 1);
  const pending = await (await a.call('GET', '/api/pending')).json();
  assert.deepEqual([pending.count, pending.products.length, pending.products[0].candidates[0].listingKey], [1, 1, 'A3']);
  await a.call('PUT', `/api/tracked/${id}/judgments`, {source: 'momo', listingKey: 'A3', verdict: 'not_same'});
  assert.equal((await (await a.call('GET', '/api/pending')).json()).count, 0);
  assert.deepEqual((await (await a.call('GET', '/api/search/recent')).json()).keywords.sort(), ['3M 1530-0'].sort());
  const detail = await (await a.call('GET', `/api/tracked/${id}`)).json();
  assert.equal(detail.chart.platforms.momo.min.length, 1);
  assert.deepEqual(detail.chart.platforms.shopee.mine.map(x => x.value), [320]);
});

test('查詢關鍵字：共用關鍵字為預設，個別覆寫優先；蝦皮與酷澎共用飛比關鍵字', async t => {
  const a = await setup();
  t.after(a.fetcher.restore);
  a.fetcher.respond = ({path}) => path === '/v1/momo' ? momoResults(TITLES) : feebeeResults();
  const shared = (await (await a.call('POST', '/api/tracked', product)).json()).product.id;
  await a.call('POST', `/api/tracked/${shared}/run`);
  assert.deepEqual(a.fetcher.calls.map(c => [c.path, c.payload.keyword]), [['/v1/momo', '3M 1530-0'], ['/v1/feebee', '3M 1530-0']]);
  a.open();
  a.fetcher.calls.length = 0;
  const override = (await (await a.call('POST', '/api/tracked', {...product, name: '覆寫', momoKeyword: 'momo 專用', feebeeKeyword: '飛比專用'})).json()).product.id;
  await a.call('POST', `/api/tracked/${override}/run`);
  assert.deepEqual(a.fetcher.calls.map(c => [c.path, c.payload.keyword]), [['/v1/momo', 'momo 專用'], ['/v1/feebee', '飛比專用']]);
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
  assert.deepEqual([listed.platforms.momo.count, listed.platforms.momo.min, listed.pendingCount], [2, 250, 0]);

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
