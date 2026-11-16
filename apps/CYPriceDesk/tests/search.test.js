import test from 'node:test';
import assert from 'node:assert/strict';
import worker from '../src/index.js';
import {signBody} from '../src/fetcher-client.js';
import {createTestEnv, FETCHER_SECRET} from './support/env.js';
import {installFakeFetcher} from './support/fake-fetcher.js';

const ORIGIN = 'https://pricedesk.test';

async function setup() {
  const shared = createTestEnv({acceptAnyLogin: true});
  const login = await worker.fetch(new Request(ORIGIN + '/api/auth/login', {method: 'POST', body: new URLSearchParams({employeeNo: '0001', password: 'password1'})}), shared.env);
  const cookie = login.headers.get('set-cookie').split(';')[0];
  const fetcher = installFakeFetcher();
  const call = (method, path, json) => worker.fetch(new Request(ORIGIN + path, {
    method, redirect: 'manual',
    headers: {cookie, ...(json ? {'content-type': 'application/json'} : {})}, body: json ? JSON.stringify(json) : undefined
  }), shared.env);
  const search = async (source, keyword, page = 1) => (await call('GET', `/api/search?source=${source}&keyword=${encodeURIComponent(keyword)}&page=${page}`)).json();
  // Moves the upstream's last request far enough back that the 15 second spacing no longer applies.
  const waitOut = upstream => shared.env.DB.sqlite.prepare('UPDATE upstream_state SET last_request_at=0 WHERE upstream=?').run(upstream);
  return {...shared, fetcher, call, search, waitOut, sql: shared.env.DB.sqlite};
}

test('簽章格式：同一組 secret／timestamp／body 與 fetcher 端算出相同簽章', async () => {
  // Same vector as fetcher/tests/test_api.py.
  const body = JSON.stringify({keyword: '羅技 M331', page: 1});
  assert.equal(await signBody('test-secret', 1700000000, body), '8812112ccbd89f91d690718ca58c7b05960d5cda9471f8a99bcc2b1aaf6dc91d');
});

test('快取：10 分鐘內命中不呼叫 fetcher；送出的簽章有效；寫入新快取時刪除超過 1 天的舊列', async t => {
  const a = await setup();
  t.after(a.fetcher.restore);
  a.sql.prepare("INSERT INTO search_cache VALUES ('momo','舊關鍵字',1,'{}',?)").run(Date.now() - 2 * 86400000);
  const first = await a.search('momo', '3m  1530');
  assert.equal(first.status, 'ok');assert.equal(first.fromCache, false);assert.equal(first.keyword, '3m 1530');
  assert.equal(first.results.length, 3);assert.equal(first.hasNext, true);
  assert.equal(a.fetcher.calls.length, 1);assert.equal(a.fetcher.calls[0].valid, true);
  assert.deepEqual(a.fetcher.calls[0].payload, {keyword: '3m 1530', page: 1});
  assert.equal(a.sql.prepare("SELECT COUNT(*) n FROM search_cache WHERE keyword='舊關鍵字'").get().n, 0);
  const second = await a.search('momo', '3m 1530');
  assert.equal(second.fromCache, true);assert.equal(second.results.length, 3);
  assert.equal(a.fetcher.calls.length, 1);
});

test('同一上游 15 秒內第二次實際請求回 rate_limited，間隔後可再查', async t => {
  const a = await setup();
  t.after(a.fetcher.restore);
  await a.search('momo', '羅技 M331');
  const limited = await a.search('momo', '另一個關鍵字');
  assert.equal(limited.status, 'rate_limited');assert(limited.retryAfterSeconds >= 1 && limited.retryAfterSeconds <= 15);
  assert.equal(a.fetcher.calls.length, 1);
  assert.equal((await a.search('shopee', '羅技 M331')).status, 'ok'); // feebee is a different upstream
  a.waitOut('momo');
  assert.equal((await a.search('momo', '另一個關鍵字')).status, 'ok');
  assert.equal(a.fetcher.calls.length, 3);
});

test('blocked 後該上游冷卻 30 分鐘，不寫快取', async t => {
  const a = await setup();
  t.after(a.fetcher.restore);
  a.fetcher.respond = () => ({status: 'blocked', raw_count: 0, results: [], checked_at: 'x'});
  assert.equal((await a.search('momo', '羅技 M331')).status, 'blocked');
  const state = a.sql.prepare("SELECT blocked_until FROM upstream_state WHERE upstream='momo'").get();
  const left = state.blocked_until - Date.now();
  assert(left > 29 * 60000 && left <= 30 * 60000);
  assert.equal(a.sql.prepare('SELECT COUNT(*) n FROM search_cache').get().n, 0);
  a.waitOut('momo');
  const cooling = await a.search('momo', '羅技 M331');
  assert.equal(cooling.status, 'blocked_cooldown');assert(cooling.retryAfterSeconds > 1700);
  assert.equal(a.fetcher.calls.length, 1);
});

test('飛比一次抓取供蝦皮與酷澎兩個來源使用，各自只回自己的結果', async t => {
  const a = await setup();
  t.after(a.fetcher.restore);
  const shopee = await a.search('shopee', '3m 1530-1');
  assert.deepEqual(shopee.results.map(r => r.price), [300, 230]);assert.equal(shopee.fromCache, false);
  assert(shopee.sourceUrl.startsWith('https://feebee.com.tw/s/'));
  const coupang = await a.search('coupang', '3m 1530-1');
  assert.deepEqual(coupang.results.map(r => r.price), [175]);assert.equal(coupang.fromCache, true);
  assert.equal(a.fetcher.calls.length, 1);assert.equal(a.fetcher.calls[0].path, '/v1/feebee');
});

test('分類：全站共用、依來源隔離、新增／修改／刪除各寫 audit', async t => {
  const a = await setup();
  t.after(a.fetcher.restore);
  const rule = {source: 'momo', listingKey: '14011391', model: '1530-0', quantity: 12, unit: '入', attributes: {尺寸: '0.5吋'}, decision: 'include'};
  assert.equal((await a.call('PUT', '/api/classifications', rule)).status, 200);
  assert.equal((await a.call('PUT', '/api/classifications', {...rule, quantity: 24})).status, 200);
  assert.equal((await a.call('PUT', '/api/classifications', {...rule, source: 'shopee', listingKey: 'https://feebee.com.tw/p/shopee-1', decision: 'exclude'})).status, 200);
  assert.equal((await a.call('PUT', '/api/classifications', {...rule, decision: 'maybe'})).status, 400);
  const momo = (await (await a.call('GET', '/api/classifications?source=momo')).json()).classifications;
  assert.deepEqual(momo, [{...rule, quantity: 24}]);
  assert.equal((await (await a.call('GET', '/api/classifications?source=coupang')).json()).classifications.length, 0);
  assert.equal((await a.call('DELETE', '/api/classifications?source=momo&listingKey=14011391')).status, 200);
  assert.equal((await (await a.call('GET', '/api/classifications?source=momo')).json()).classifications.length, 0);
  const actions = a.sql.prepare("SELECT action, actor_name FROM audit_log WHERE action LIKE 'classification.%' ORDER BY id").all();
  assert.deepEqual(actions.map(x => x.action), ['classification.update', 'classification.update', 'classification.update', 'classification.delete']);
  assert.equal(actions[0].actor_name, '測試員工');
  const entries = (await (await a.call('GET', '/api/audit')).json()).entries;
  assert.equal(entries.length, 4);assert(entries[0].detail.includes('14011391'));
});
