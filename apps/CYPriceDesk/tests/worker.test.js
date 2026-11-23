import test from 'node:test';
import assert from 'node:assert/strict';
import worker from '../src/index.js';
import {safeReturnPath} from '../src/sso.js';
import {createTestEnv} from './support/env.js';
import {APPLICATION_ID} from './support/fake-identity.js';

const ORIGIN = 'https://pricedesk.test';
const STATE_COOKIE = '__Host-cypd_sso_state';
const call = (env, method, path, {cookie, json, headers = {}} = {}) => worker.fetch(new Request(ORIGIN + path, {
  method,
  redirect: 'manual',
  headers: {...(cookie ? {cookie} : {}), ...(json ? {'content-type': 'application/json'} : {}), ...headers},
  body: json ? JSON.stringify(json) : undefined
}), env);

const setCookies = response => response.headers.getSetCookie();
const cookieNamed = (response, name) => setCookies(response).find(c => c.startsWith(name + '='));
const pairOf = setCookie => setCookie.split(';')[0];
function assertSsoHeaders(response) {
  assert.equal(response.headers.get('cache-control'), 'no-store');
  assert.equal(response.headers.get('referrer-policy'), 'no-referrer');
}

// Starts the flow like a browser: returns the portal redirect, the state, and the state cookie to send back.
async function startFlow(env, path = '/') {
  const response = await call(env, 'GET', path);
  const location = new URL(response.headers.get('location'));
  return {response, location, state: location.searchParams.get('state'), stateCookie: pairOf(cookieNamed(response, STATE_COOKIE))};
}
const callback = (env, {code, state, cookie}) => call(env, 'GET', `/auth/sso/callback?code=${encodeURIComponent(code)}&state=${encodeURIComponent(state)}`, {cookie});

test('未登入頁面請求：設定 state cookie 並 303 到 CY Web；API 仍回 401', async () => {
  const {env} = createTestEnv();
  for (const path of ['/', '/index.html']) {
    const {response, location, state} = await startFlow(env, path);
    assert.equal(response.status, 303);
    assert.equal(location.origin, 'https://portal.example.test');
    assert.equal(location.pathname, '/sso');
    assert.equal(location.searchParams.get('app'), APPLICATION_ID);
    assert.match(state, /^[A-Za-z0-9_-]{43}$/);   // 32 random bytes, base64url
    const cookie = cookieNamed(response, STATE_COOKIE);
    for (const part of ['HttpOnly', 'Secure', 'SameSite=Lax', 'Path=/', 'Max-Age=600']) assert(cookie.includes(part), part);
    assert(!/Domain=/i.test(cookie));
    assert(cookie.startsWith(`${STATE_COOKIE}=${state}.`));
    assertSsoHeaders(response);
  }
  assert.equal((await call(env, 'GET', '/api/tracked')).status, 401);
  assert.equal((await call(env, 'GET', '/api/audit')).status, 401);
  assert.equal((await call(env, 'GET', '/api/tracked')).headers.get('location'), null);
});

test('每次起手式都用新的 state', async () => {
  const {env} = createTestEnv();
  const a = await startFlow(env), b = await startFlow(env);
  assert.notEqual(a.state, b.state);
});

test('回跳成功：設定 cypd_session、清除 state cookie、303 回 /，網址不含 code', async () => {
  const {env, identity} = createTestEnv();
  const {state, stateCookie} = await startFlow(env);
  const code = identity.issueCode('0001');
  const response = await callback(env, {code, state, cookie: stateCookie});
  assert.equal(response.status, 303);
  assert.equal(response.headers.get('location'), '/');
  assert(!response.headers.get('location').includes(code));
  assertSsoHeaders(response);
  assert.deepEqual(identity.redeemHeaders, [APPLICATION_ID]);

  const session = cookieNamed(response, 'cypd_session');
  for (const part of ['cypd_session=cyid_', 'HttpOnly', 'Secure', 'SameSite=Lax', 'Path=/', 'Expires=', 'Max-Age=']) assert(session.includes(part), part);
  assert(!/Domain=/i.test(session));
  const cleared = cookieNamed(response, STATE_COOKIE);
  assert(cleared.includes('Max-Age=0') && cleared.startsWith(`${STATE_COOKIE}=;`));

  const home = await call(env, 'GET', '/', {cookie: pairOf(session)});
  assert.equal(home.status, 200);assert((await home.text()).includes('價格觀測室'));
  assert.deepEqual(await (await call(env, 'GET', '/api/me', {cookie: pairOf(session)})).json(), {employeeNo: '0001', displayName: '測試員工', workspaceRole: 'USER'});
});

test('回跳失敗：state 缺少、不符、cookie 缺少都不兌換，並清除 state cookie', async () => {
  const {env, identity} = createTestEnv();
  const {state, stateCookie} = await startFlow(env);
  const other = await startFlow(env);
  const code = identity.issueCode();
  const cases = [
    {name: 'state 不符', state: other.state, cookie: stateCookie},
    {name: 'query 沒有 state', state: '', cookie: stateCookie},
    {name: '沒有 state cookie', state, cookie: undefined},
    {name: 'cookie 內容毀損', state, cookie: `${STATE_COOKIE}=garbage`}
  ];
  for (const item of cases) {
    const response = await callback(env, {code, state: item.state, cookie: item.cookie});
    assert.equal(response.status, 400, item.name);
    assertSsoHeaders(response);
    assert(cookieNamed(response, STATE_COOKIE).includes('Max-Age=0'), item.name);
    assert.equal(cookieNamed(response, 'cypd_session'), undefined, item.name);
    const html = await response.text();
    assert(html.includes('重新登入') && html.includes('/auth/sso/start'), item.name);
    assert(!html.includes(code) && !html.includes(state), item.name);
  }
  const noCode = await call(env, 'GET', '/auth/sso/callback?state=' + state, {cookie: stateCookie});
  assert.equal(noCode.status, 401);
  assert.equal(identity.calls.includes('/v1/identity/sso/handoff/redeem'), false);
  assert.equal(identity.sessions.size, 0);
});

test('兌換失敗頁：無效碼 401、重放、被限流 429、provider 故障 503；都有重新登入按鈕且不建立 Session', async () => {
  const {env, identity} = createTestEnv();
  const attempt = async code => {
    const {state, stateCookie} = await startFlow(env);
    return callback(env, {code, state, cookie: stateCookie});
  };
  const unknown = await attempt('A'.repeat(43));
  assert.equal(unknown.status, 401);
  assert((await unknown.text()).includes('登入連結已失效'));

  const good = identity.issueCode();
  assert.equal((await attempt(good)).status, 303);
  const replay = await attempt(good);
  assert.equal(replay.status, 401);
  assert.equal(cookieNamed(replay, 'cypd_session'), undefined);
  assertSsoHeaders(replay);
  assert((await replay.text()).includes('重新登入'));

  identity.state.rateLimited = true;
  const limited = await attempt(identity.issueCode());
  assert.equal(limited.status, 429);assertSsoHeaders(limited);
  assert((await limited.text()).includes('重新登入'));
  identity.state.rateLimited = false;

  identity.state.unavailable = true;
  const down = await attempt(identity.issueCode());
  assert.equal(down.status, 503);
  assert.equal(cookieNamed(down, 'cypd_session'), undefined);
  assert.equal(identity.sessions.size, 1);
});

test('回到使用者原本要去的路徑；不安全的路徑一律退回 /', async () => {
  const {env, identity} = createTestEnv();
  // /auth/sso/start?next= is the only place a path can be supplied; the page request itself is always "/".
  const start = next => call(env, 'GET', '/auth/sso/start?next=' + encodeURIComponent(next));
  const via = async next => {
    const response = await start(next);
    const location = new URL(response.headers.get('location'));
    const stateCookie = pairOf(cookieNamed(response, STATE_COOKIE));
    return (await callback(env, {code: identity.issueCode(), state: location.searchParams.get('state'), cookie: stateCookie})).headers.get('location');
  };
  assert.equal(await via('/?view=audit'), '/?view=audit');
  assert.equal(await via('/some/page?x=1'), '/some/page?x=1');
  for (const bad of ['https://evil.example/', '//evil.example/', '/\\evil.example', 'javascript:alert(1)', 'evil', '', '/auth/sso/callback?code=x', '/api/audit', '/a\r\nset-cookie: x=1']) {
    assert.equal(await via(bad), '/', JSON.stringify(bad));
  }
  assert.equal((await call(env, 'GET', '/auth/sso/start')).status, 303);

  assert.equal(safeReturnPath(undefined), '/');
  assert.equal(safeReturnPath('/' + 'a'.repeat(600)), '/');
  assert.equal(safeReturnPath('/%2f%2fevil'), '/%2f%2fevil');   // still a same-origin path, never a host
});

test('resolve 被拒後下一個請求即 401 並清除 cookie；頁面請求重新走 CY Web', async () => {
  const {env, identity} = createTestEnv();
  const cookie = identity.cookieFor();
  assert.equal((await call(env, 'GET', '/api/tracked', {cookie})).status, 200);
  identity.sessions.clear();
  const api = await call(env, 'GET', '/api/tracked', {cookie});
  assert.equal(api.status, 401);assert(api.headers.get('set-cookie').includes('Max-Age=0'));
  const home = await call(env, 'GET', '/', {cookie});
  assert.equal(home.status, 303);
  assert.equal(new URL(home.headers.get('location')).pathname, '/sso');
  assert(cookieNamed(home, 'cypd_session').includes('Max-Age=0'));
});

test('已登入的頁面請求直接顯示；provider 故障時頁面 503 且不導向', async () => {
  const {env, identity} = createTestEnv();
  assert.equal((await call(env, 'GET', '/', {cookie: identity.cookieFor()})).status, 200);
  const original = identity.fetch;
  identity.fetch = async request => new URL(request.url).pathname === '/v1/identity/session/resolve' ? new Response('{}', {status: 503}) : original.call(identity, request);
  const down = await call(env, 'GET', '/', {cookie: identity.cookieFor()});
  assert.equal(down.status, 503);assert.equal(down.headers.get('location'), null);assertSsoHeaders(down);
});

test('舊登入入口已移除', async () => {
  const {env, identity} = createTestEnv();
  for (const path of ['/login', '/login.html']) assert.equal((await call(env, 'GET', path)).status, 404, path);
  const post = await call(env, 'POST', '/api/auth/login', {headers: {'content-type': 'application/x-www-form-urlencoded'}});
  assert.notEqual(post.status, 200);assert.notEqual(post.status, 302);assert.notEqual(post.status, 303);
  assert(!(post.headers.get('set-cookie') || '').includes('cypd_session=cyid_'));
  assert.equal(identity.sessions.size, 0);
  assert.equal((await call(env, 'GET', '/api/auth/portal')).status, 401);
  assert.equal(identity.calls.includes('/v1/identity/login'), false);
});

test('登出先呼叫 provider logout，清除 cookie 並 303 到「已登出」頁；該頁不自動導向', async () => {
  const {env, identity} = createTestEnv();
  const cookie = identity.cookieFor();
  const response = await call(env, 'POST', '/api/auth/logout', {cookie});
  assert.equal(identity.calls.at(-1), '/v1/identity/logout');
  assert.equal(identity.sessions.size, 0);
  assert.equal(response.status, 303);assert.equal(response.headers.get('location'), '/auth/signed-out');
  assert(response.headers.get('set-cookie').includes('Max-Age=0'));

  const page = await call(env, 'GET', '/auth/signed-out');
  assert.equal(page.status, 200);assertSsoHeaders(page);
  assert.equal(page.headers.get('location'), null);
  const html = await page.text();
  assert(html.includes('已登出') && html.includes('重新登入') && html.includes('href="/auth/sso/start"'));
  assert(!/http-equiv=["']?refresh/i.test(html) && !/location\s*[.=]/.test(html) && !html.includes('<script'));
  assert.equal(cookieNamed(page, STATE_COOKIE), undefined);
  // The sign-in button is a plain link: following it (and only that) starts the flow.
  assert.equal((await call(env, 'GET', '/auth/sso/start')).status, 303);
});

test('未設定 CY Web 入口或 Application ID 時，起手式回 503 錯誤頁而不是導向', async () => {
  const {env} = createTestEnv();
  const response = await call({...env, CYWEB_PORTAL_URL: ''}, 'GET', '/');
  assert.equal(response.status, 503);assert.equal(response.headers.get('location'), null);assertSsoHeaders(response);
  assert.equal(cookieNamed(response, STATE_COOKIE), undefined);
});
