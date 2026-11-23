import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import app from '../src/app.js';
import { safeReturnPath, SSO_STATE_COOKIE } from '../src/sso.js';

const ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const token = 'cyid_' + 'b'.repeat(64);
const expiresAt = new Date(Date.now() + 60 * 60 * 1000).toISOString();
const principal = {
  workspaceId: 'workspace-ci-placeholder', employeeId: 'employee-ci', employeeNo: '0123', displayName: 'CI User',
  workspaceRole: 'ADMIN', isIdentityAdmin: false, emailVerified: true, isWorkspaceSuperAdmin: false,
  credentialVersion: 1, employeeRevision: 1
};

function makeEnv(redeem) {
  const calls = [];
  const env = {
    CYID_WORKSPACE_ID: 'workspace-ci-placeholder',
    CYID_APPLICATION_ID: 'cyacc_ci',
    CYWEB_PORTAL_URL: 'https://portal.example.test/',
    ASSETS: { async fetch() { return new Response('<!doctype html>APP', { headers: { 'content-type': 'text/html' } }); } },
    IDENTITY: {
      async fetch(request) {
        const url = new URL(request.url);
        const body = await request.json().catch(() => null);
        calls.push({ pathname: url.pathname, body, application: request.headers.get('x-identity-application') });
        if (url.pathname === '/v1/identity/session/resolve') {
          return request.headers.get('authorization') === `Bearer ${token}`
            ? Response.json({ principal, session: { expiresAt } })
            : Response.json({ error: { code: 'SESSION_INVALID' } }, { status: 401 });
        }
        if (url.pathname === '/v1/identity/sso/handoff/redeem') return redeem(body);
        return Response.json({ error: { code: 'NOT_FOUND' } }, { status: 404 });
      }
    }
  };
  return { env, calls };
}

const okRedeem = body => body?.code === 'good-code'
  ? Response.json({ principal, session: { token, expiresAt } })
  : Response.json({ error: { code: 'SSO_HANDOFF_INVALID' } }, { status: 401 });

const get = (env, pathAndQuery, cookie) => app.fetch(new Request('https://acc.example.test' + pathAndQuery, cookie ? { headers: { cookie } } : {}), env);
const setCookies = response => response.headers.getSetCookie();
const stateFrom = response => {
  const raw = setCookies(response).find(value => value.startsWith(SSO_STATE_COOKIE + '='));
  return { raw, value: raw.split(';', 1)[0].slice(SSO_STATE_COOKIE.length + 1) };
};
const assertSsoHeaders = (response, label) => {
  assert.match(response.headers.get('cache-control') || '', /no-store/, `${label}: no-store`);
  assert.equal(response.headers.get('referrer-policy'), 'no-referrer', `${label}: no-referrer`);
};

// 1. Unauthenticated page request: state cookie + 303 to the portal with this App's id and the same state.
{
  const { env, calls } = makeEnv(okRedeem);
  const response = await get(env, '/');
  assert.equal(response.status, 303);
  const location = new URL(response.headers.get('location'));
  assert.equal(location.origin, 'https://portal.example.test');
  assert.equal(location.pathname, '/sso');
  assert.equal(location.searchParams.get('app'), 'CYACC_CI');
  const state = location.searchParams.get('state');
  assert.match(state, /^[A-Za-z0-9_-]{43}$/, '32 random bytes as base64url');
  const { raw, value } = stateFrom(response);
  assert.match(raw, /; Path=\/; HttpOnly; Secure; SameSite=Lax; Max-Age=600$/);
  assert.doesNotMatch(raw, /Domain=/i);
  assert.ok(value.startsWith(state + '.'));
  assertSsoHeaders(response, 'start');
  assert.equal(calls.filter(call => call.pathname.includes('sso')).length, 0);

  const states = new Set();
  for (let i = 0; i < 5; i++) states.add(new URL((await get(env, '/')).headers.get('location')).searchParams.get('state'));
  assert.equal(states.size, 5, 'state is random per request');

  // A stale or invalid session cookie also starts the flow.
  const stale = await get(env, '/', 'cyaccounting_session=cyid_' + 'c'.repeat(64));
  assert.equal(stale.status, 303);
  assert.equal(new URL(stale.headers.get('location')).pathname, '/sso');

  // A valid session still opens the app.
  const authed = await get(env, '/', `cyaccounting_session=${token}`);
  assert.equal(authed.status, 200);
}

// API requests stay 401 JSON.
{
  const { env } = makeEnv(okRedeem);
  env.DB = {};
  const response = await get(env, '/api/transactions?month=2026-11');
  assert.equal(response.status, 401);
  assert.equal((await response.json()).code, 'AUTH_REQUIRED');
  assert.equal(response.headers.get('location'), null);
  const me = await get(env, '/api/auth/me');
  assert.equal(me.status, 401);
}

// Misconfiguration or provider outage never redirects (no loop).
{
  const { env } = makeEnv(okRedeem);
  delete env.CYWEB_PORTAL_URL;
  assert.equal((await get(env, '/')).status, 503);
  const down = makeEnv(okRedeem).env;
  down.IDENTITY = { async fetch() { return Response.json({}, { status: 503 }); } };
  const downResponse = await get(down, '/', `cyaccounting_session=${token}`);
  assert.equal(downResponse.status, 503);
  assert.equal(downResponse.headers.get('location'), null);
}

// 2. Callback success: cookie as the old login set it, redirect to the saved path, code not in Location.
{
  const { env, calls } = makeEnv(okRedeem);
  const start = await get(env, '/auth/sso/start?next=' + encodeURIComponent('/?month=2026-10'));
  const state = new URL(start.headers.get('location')).searchParams.get('state');
  const { value } = stateFrom(start);
  const response = await get(env, `/auth/sso/callback?code=good-code&state=${state}`, `${SSO_STATE_COOKIE}=${value}`);
  assert.equal(response.status, 303);
  assert.equal(response.headers.get('location'), '/?month=2026-10');
  assert.ok(!response.headers.get('location').includes('good-code'));
  assertSsoHeaders(response, 'callback');
  const cookies = setCookies(response);
  const clear = cookies.find(c => c.startsWith(SSO_STATE_COOKIE + '='));
  assert.match(clear, /Max-Age=0/);
  const session = cookies.find(c => c.startsWith('cyaccounting_session='));
  assert.match(session, /^cyaccounting_session=cyid_[0-9a-f]{64}; Path=\/; HttpOnly; Secure; SameSite=Lax; Max-Age=\d+; Expires=/);
  assert.doesNotMatch(session, /Domain=/i);
  const redeemCalls = calls.filter(call => call.pathname === '/v1/identity/sso/handoff/redeem');
  assert.equal(redeemCalls.length, 1);
  assert.deepEqual(redeemCalls[0].body, { code: 'good-code' });
  assert.equal(redeemCalls[0].application, 'CYACC_CI');

  // Default return path is '/'.
  const plain = await get(env, '/');
  const plainState = new URL(plain.headers.get('location')).searchParams.get('state');
  const plainDone = await get(env, `/auth/sso/callback?code=good-code&state=${plainState}`, `${SSO_STATE_COOKIE}=${stateFrom(plain).value}`);
  assert.equal(plainDone.headers.get('location'), '/');
}

// State missing / mismatched / malformed cookie / missing code: error page, no redeem, state cookie removed.
{
  const { env, calls } = makeEnv(okRedeem);
  const start = await get(env, '/');
  const state = new URL(start.headers.get('location')).searchParams.get('state');
  const { value } = stateFrom(start);
  const otherState = 'A'.repeat(43);
  const cases = {
    'no cookie': [`/auth/sso/callback?code=good-code&state=${state}`, undefined],
    'no query state': ['/auth/sso/callback?code=good-code', `${SSO_STATE_COOKIE}=${value}`],
    'mismatch': [`/auth/sso/callback?code=good-code&state=${otherState}`, `${SSO_STATE_COOKIE}=${value}`],
    'garbled cookie': [`/auth/sso/callback?code=good-code&state=${state}`, `${SSO_STATE_COOKIE}=garbage`],
    'no code': [`/auth/sso/callback?state=${state}`, `${SSO_STATE_COOKIE}=${value}`]
  };
  for (const [label, [route, cookie]] of Object.entries(cases)) {
    const response = await get(env, route, cookie);
    assert.equal(response.status, 400, label);
    assertSsoHeaders(response, label);
    assert.match(setCookies(response).find(c => c.startsWith(SSO_STATE_COOKIE + '=')) || '', /Max-Age=0/, label);
    assert.equal(setCookies(response).some(c => c.startsWith('cyaccounting_session=')), false, label);
    const html = await response.text();
    assert.match(html, /重新登入/);
    assert.match(html, /href="\/auth\/sso\/start"/);
  }
  assert.equal(calls.filter(call => call.pathname.includes('redeem')).length, 0, 'no redeem without a matching state');
}

// Redeem failures render the error page with a retry button and never set a session.
for (const [status, code, expected] of [[401, 'SSO_HANDOFF_INVALID', 401], [429, 'AUTH_RATE_LIMITED', 429], [503, 'AUTH_RATE_LIMITER_UNAVAILABLE', 503], [500, 'X', 503]]) {
  const { env } = makeEnv(() => Response.json({ error: { code } }, { status, headers: status === 429 ? { 'retry-after': '60' } : {} }));
  const start = await get(env, '/');
  const state = new URL(start.headers.get('location')).searchParams.get('state');
  const response = await get(env, `/auth/sso/callback?code=bad-code&state=${state}`, `${SSO_STATE_COOKIE}=${stateFrom(start).value}`);
  assert.equal(response.status, expected);
  assertSsoHeaders(response, 'redeem failure ' + status);
  assert.equal(setCookies(response).some(c => c.startsWith('cyaccounting_session=')), false);
  assert.match(setCookies(response).find(c => c.startsWith(SSO_STATE_COOKIE + '=')), /Max-Age=0/);
  const html = await response.text();
  assert.match(html, /重新登入/);
  assert.ok(!html.includes('bad-code') && !html.includes(state));
  if (status === 429) assert.equal(response.headers.get('retry-after'), '60');
}
{
  // A malformed provider success is rejected too.
  const { env } = makeEnv(() => Response.json({ principal, session: { token: 'nope', expiresAt } }));
  const start = await get(env, '/');
  const state = new URL(start.headers.get('location')).searchParams.get('state');
  const response = await get(env, `/auth/sso/callback?code=x&state=${state}`, `${SSO_STATE_COOKIE}=${stateFrom(start).value}`);
  assert.equal(response.status, 503);
  assert.equal(setCookies(response).some(c => c.startsWith('cyaccounting_session=')), false);
}

// Unsafe return paths fall back to '/'.
for (const unsafe of ['//evil.example', 'https://evil.example/', 'javascript:alert(1)', '/\\evil.example', '/ok\r\nset-cookie: x=1', '', 'relative/path', '/auth/sso/callback?code=1', '/api/transactions', '/login', '/a b', '/' + 'a'.repeat(600)]) {
  assert.equal(safeReturnPath(unsafe), '/', JSON.stringify(unsafe).slice(0, 60));
}
assert.equal(safeReturnPath('/'), '/');
assert.equal(safeReturnPath('/recovery'), '/recovery');
assert.equal(safeReturnPath('/?month=2026-10'), '/?month=2026-10');
{
  const { env } = makeEnv(okRedeem);
  const start = await get(env, '/auth/sso/start?next=' + encodeURIComponent('//evil.example/x'));
  const state = new URL(start.headers.get('location')).searchParams.get('state');
  const done = await get(env, `/auth/sso/callback?code=good-code&state=${state}`, `${SSO_STATE_COOKIE}=${stateFrom(start).value}`);
  assert.equal(done.headers.get('location'), '/');
  // A tampered return path inside the cookie is re-validated at callback time.
  const forged = `${state}.${Buffer.from('//evil.example').toString('base64url')}`;
  const forgedDone = await get(env, `/auth/sso/callback?code=good-code&state=${state}`, `${SSO_STATE_COOKIE}=${forged}`);
  assert.equal(forgedDone.headers.get('location'), '/');
}

// Signed-out page shows a button and never redirects by itself.
{
  const { env } = makeEnv(okRedeem);
  const response = await get(env, '/auth/sso/signed-out');
  assert.equal(response.status, 200);
  assert.equal(response.headers.get('location'), null);
  assertSsoHeaders(response, 'signed out');
  const html = await response.text();
  assert.match(html, /已登出/);
  assert.match(html, /<a class="button" href="\/auth\/sso\/start">重新登入<\/a>/);
  assert.doesNotMatch(html, /http-equiv|location\.(?:replace|href)|<script/i);
  assert.equal((await get(env, '/auth/sso/unknown')).status, 404);
  const post = await app.fetch(new Request('https://acc.example.test/auth/sso/callback', { method: 'POST' }), env);
  assert.equal(post.status, 405);
}

// Logout still revokes at the provider and clears only this App's cookie.
{
  const { env, calls } = makeEnv(okRedeem);
  env.IDENTITY = { async fetch(request) { calls.push(new URL(request.url).pathname); return Response.json({ ok: true }); } };
  const response = await app.fetch(new Request('https://acc.example.test/api/auth/logout', { method: 'POST', headers: { cookie: `cyaccounting_session=${token}` } }), env);
  assert.equal(response.status, 200);
  assert.deepEqual(calls, ['/v1/identity/logout']);
  assert.match(response.headers.get('set-cookie'), /^cyaccounting_session=; Path=\/; HttpOnly; Secure; SameSite=Lax; Max-Age=0/);
}

// Front end: expiry and 401 start the portal flow; logout lands on the signed-out page.
const auth = fs.readFileSync(path.join(ROOT, 'public/auth.js'), 'utf8');
assert.match(auth, /CYACC_SSO_START_URL = '\/auth\/sso\/start'/);
assert.match(auth, /location\.replace\(CYACC_SIGNED_OUT_URL\)/);
assert.doesNotMatch(auth, /['"]\/login['"]/);

console.log('SSO login tests passed.');
