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

test('未登入：首頁 302 到 /login，API 回 401，登入頁可開啟', async () => {
  const {env} = createTestEnv();
  const home = await call(env, 'GET', '/');
  assert.equal(home.status, 302);assert.equal(home.headers.get('location'), '/login');
  assert.equal((await call(env, 'GET', '/index.html')).status, 302);
  assert.equal((await call(env, 'GET', '/api/tracked')).status, 401);
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
  assert.equal((await call(env, 'GET', '/api/tracked', {cookie})).status, 200);
  identity.sessions.clear();
  const api = await call(env, 'GET', '/api/tracked', {cookie});
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
