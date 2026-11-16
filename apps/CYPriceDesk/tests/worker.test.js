import test from 'node:test';
import assert from 'node:assert/strict';
import worker from '../src/index.js';
import {createTestEnv} from './support/env.js';

const ORIGIN = 'https://pricedesk.test';
const call = (env, method, path, {cookie, json, form} = {}) => worker.fetch(new Request(ORIGIN + path, {
  method,
  redirect: 'manual',
  headers: {...(cookie ? {cookie} : {}), ...(json ? {'content-type': 'application/json'} : {})},
  body: json ? JSON.stringify(json) : form ? new URLSearchParams(form) : undefined
}), env);

async function login(env, employeeNo = '0001', password = 'password1') {
  const response = await call(env, 'POST', '/api/auth/login', {form: {employeeNo, password}});
  return {response, cookie: (response.headers.get('set-cookie') || '').split(';')[0]};
}
const offer = {product: '測試耳機', spec: 'A1', platform: 'momo', seller: '測試店', url: 'https://www.momoshop.com.tw/goods/1', price: 990, shipping: 60, checkedAt: '2026-01-01T02:00:00.000Z', note: ''};

test('未登入：首頁 302 到 /login，API 回 401，登入頁可開啟', async () => {
  const {env} = createTestEnv();
  const home = await call(env, 'GET', '/');
  assert.equal(home.status, 302);assert.equal(home.headers.get('location'), '/login');
  assert.equal((await call(env, 'GET', '/index.html')).status, 302);
  assert.equal((await call(env, 'GET', '/api/records')).status, 401);
  assert.equal((await call(env, 'GET', '/api/audit')).status, 401);
  const page = await call(env, 'GET', '/login');
  assert.equal(page.status, 200);assert((await page.text()).includes('/api/auth/login'));
});

test('登入成功設定 cypd_session；first-login 與錯誤密碼不建立 Session', async () => {
  const {env, identity} = createTestEnv();
  const {response, cookie} = await login(env);
  assert.equal(response.status, 302);assert.equal(response.headers.get('location'), '/');
  const setCookie = response.headers.get('set-cookie');
  for (const part of ['cypd_session=cyid_', 'HttpOnly', 'Secure', 'SameSite=Lax', 'Path=/', 'Expires=']) assert(setCookie.includes(part), part);
  const home = await call(env, 'GET', '/', {cookie});
  assert.equal(home.status, 200);assert((await home.text()).includes('價格觀測室'));
  assert.equal((await call(env, 'GET', '/login', {cookie})).headers.get('location'), '/');
  assert.deepEqual(await (await call(env, 'GET', '/api/me', {cookie})).json(), {employeeNo: '0001', displayName: '測試員工', workspaceRole: 'USER'});

  const first = await login(env, '0002');
  assert.equal(first.response.headers.get('location'), '/login?error=first-login');
  assert.equal(first.response.headers.get('set-cookie'), null);
  assert.equal(identity.sessions.size, 1);
  assert.equal(await (await call(env, 'GET', '/api/auth/portal')).json().then(x => x.url), 'https://portal.example.test/');

  const wrong = await login(env, '0001', 'wrongpass1');
  assert.equal(wrong.response.headers.get('location'), '/login?error=failed');
  assert.equal(wrong.response.headers.get('set-cookie'), null);
});

test('resolve 被拒後下一個請求即 401 並清除 cookie', async () => {
  const {env, identity} = createTestEnv();
  const {cookie} = await login(env);
  assert.equal((await call(env, 'GET', '/api/records', {cookie})).status, 200);
  identity.sessions.clear();
  const api = await call(env, 'GET', '/api/records', {cookie});
  assert.equal(api.status, 401);assert(api.headers.get('set-cookie').includes('Max-Age=0'));
  const home = await call(env, 'GET', '/', {cookie});
  assert.equal(home.status, 302);assert.equal(home.headers.get('location'), '/login');
});

test('登出先呼叫 provider logout，再清除 cookie 並 302 /login', async () => {
  const {env, identity} = createTestEnv();
  const {cookie} = await login(env);
  const response = await call(env, 'POST', '/api/auth/logout', {cookie});
  assert.equal(identity.calls.at(-1), '/v1/identity/logout');
  assert.equal(identity.sessions.size, 0);
  assert.equal(response.status, 302);assert.equal(response.headers.get('location'), '/login');
  assert(response.headers.get('set-cookie').includes('Max-Age=0'));
});

test('新增、修改（含 409）、刪除、匯入各寫一筆 audit，且與寫入同一個 batch', async () => {
  const {env} = createTestEnv();
  const {cookie} = await login(env);
  const batches = [];
  const batch = env.DB.batch;
  env.DB.batch = async list => {batches.push(list.length);return batch(list);};
  const send = (method, path, json) => call(env, method, path, {cookie, json});

  const created = await send('POST', '/api/records', offer);
  assert.equal(created.status, 201);
  const record = (await created.json()).record;
  assert.equal(record.revision, 1);

  const updated = await send('PUT', `/api/records/${record.id}`, {...offer, price: 950, revision: 1});
  assert.equal(updated.status, 200);
  assert.equal((await updated.json()).record.revision, 2);
  const stale = await send('PUT', `/api/records/${record.id}`, {...offer, price: 900, revision: 1});
  assert.equal(stale.status, 409);
  assert.equal((await stale.json()).error, '資料已被他人更新，請重新整理');
  assert.equal((await send('DELETE', `/api/records/${record.id}?revision=1`)).status, 409);

  const imported = await send('POST', '/api/records/import', {offers: [
    {...offer, price: 900, checkedAt: '2026-01-02T02:00:00.000Z'},
    {...offer, spec: 'A2', url: 'https://www.momoshop.com.tw/goods/2'},
    {...offer, spec: 'A3', url: 'https://www.momoshop.com.tw/goods/3', checkedAt: '2025-01-01T00:00:00.000Z', price: 1}
  ]});
  assert.deepEqual(await imported.json(), {added: 2, updated: 1, skipped: 0});
  const {records} = await (await send('GET', '/api/records')).json();
  assert.equal(records.length, 3);
  assert.equal(records.find(r => r.id === record.id).price, 900);

  assert.equal((await send('DELETE', `/api/records/${record.id}?revision=3`)).status, 200);
  assert.equal((await (await send('GET', '/api/records')).json()).records.length, 2);
  assert.equal((await send('POST', '/api/records', {product: '', platform: 'momo', price: 1})).status, 400);

  assert.deepEqual(batches, [2, 2, 4, 2]);
  const {entries} = await (await send('GET', '/api/audit?limit=50')).json();
  assert.deepEqual(entries.map(e => e.action).reverse(), ['record.create', 'record.update', 'record.import', 'record.delete']);
  assert(entries.every(e => e.actor === '測試員工'));
  assert.equal(entries[1].detail, '新增 2 筆、更新 1 筆、略過 0 筆');
  assert(!JSON.stringify(env.DB.sqlite.prepare('SELECT * FROM audit_log').all()).includes('cyid_'));
});
