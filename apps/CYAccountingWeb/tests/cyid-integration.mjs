import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import vm from 'node:vm';
import { fileURLToPath } from 'node:url';
import {
  appUserFromPrincipal,
  canWriteAccounting,
  cyWebPortalUrl,
  redeemSsoHandoff,
  resolveIdentitySession
} from '../src/identity-adapter.js';
import app from '../src/app.js';

const HERE = path.dirname(fileURLToPath(import.meta.url));
const ROOT = path.resolve(HERE, '..');
const token = 'cyid_' + 'a'.repeat(64);
const expiresAt = new Date(Date.now() + 60 * 60 * 1000).toISOString();
const basePrincipal = {
  workspaceId: 'workspace-ci-placeholder',
  employeeId: 'employee-ci',
  employeeNo: '0123',
  displayName: 'CI User',
  workspaceRole: 'USER',
  isIdentityAdmin: false,
  emailVerified: true,
  isWorkspaceSuperAdmin: false,
  credentialVersion: 3,
  employeeRevision: 7
};

const calls = [];
const env = {
  CYID_WORKSPACE_ID: 'workspace-ci-placeholder',
  CYID_APPLICATION_ID: 'CYACC_CI',
  IDENTITY: {
    async fetch(request) {
      const url = new URL(request.url);
      const body = await request.json().catch(() => null);
      calls.push({
        pathname: url.pathname,
        body,
        authorization: request.headers.get('authorization'),
        application: request.headers.get('x-identity-application')
      });
      if (url.pathname === '/v1/identity/sso/handoff/redeem') {
        return Response.json({ principal: basePrincipal, session: { token, expiresAt } });
      }
      if (url.pathname === '/v1/identity/session/resolve') {
        return Response.json({ principal: basePrincipal, session: { expiresAt } });
      }
      return Response.json({ error: { code: 'NOT_FOUND' } }, { status: 404 });
    }
  }
};

assert.equal(cyWebPortalUrl({ CYWEB_PORTAL_URL: 'https://portal.example.test/start' }), 'https://portal.example.test/');
assert.equal(cyWebPortalUrl({ CYWEB_PORTAL_URL: 'http://portal.example.test/' }), null);
assert.equal(cyWebPortalUrl({ CYWEB_PORTAL_URL: 'https://portal.example.test/?token=secret' }), null);
const redeemRequest = new Request('https://acc.example.com/auth/sso/callback', {
  headers: { 'cf-connecting-ip': '127.0.0.1' }
});
const redeemed = await redeemSsoHandoff(redeemRequest, env, 'handoff-code-ci');
assert.equal(redeemed.ok, true);
assert.equal(redeemed.user.role, 'USER');
assert.equal(redeemed.user.canWriteAccounting, false);
assert.match(redeemed.cookie, /^cyaccounting_session=cyid_[0-9a-f]{64}; Path=\/; HttpOnly; Secure; SameSite=Lax; Max-Age=\d+; Expires=/);
assert.doesNotMatch(redeemed.cookie, /Domain=/i);
assert.equal(calls[0].pathname, '/v1/identity/sso/handoff/redeem');
assert.deepEqual(calls[0].body, { code: 'handoff-code-ci' });
assert.equal(calls[0].application, 'CYACC_CI', 'redeem is made as this App');
assert.equal(calls[0].authorization, null);

const resolveRequest = new Request('https://acc.example.com/api/auth/me', {
  headers: { cookie: `cyaccounting_session=${token}` }
});
const resolved = await resolveIdentitySession(resolveRequest, env);
assert.equal(resolved.ok, true);
assert.equal(resolved.session.role, 'USER');
assert.equal(calls[1].authorization, `Bearer ${token}`);
assert.equal(calls[1].application, 'CYACC_CI');
assert.equal(canWriteAccounting(resolved.principal), false);
assert.equal(canWriteAccounting({ workspaceRole: 'ADMIN' }), true);
assert.equal(appUserFromPrincipal({ ...basePrincipal, workspaceRole: 'SUPER_ADMIN' }).canWriteAccounting, true);

let hangingRequestSignal = null;
const hangingEnv = {
  CYID_WORKSPACE_ID: 'workspace-ci-placeholder',
  CYID_APPLICATION_ID: 'CYACC_CI',
  CYID_PROVIDER_TIMEOUT_MS: '100',
  IDENTITY: {
    async fetch(request) {
      hangingRequestSignal = request.signal;
      return await new Promise(() => {});
    }
  }
};
const timeoutRequest = new Request('https://acc.example.com/api/auth/me', {
  headers: { cookie: `cyaccounting_session=${token}` }
});
const timeoutStarted = Date.now();
const timeoutResolved = await resolveIdentitySession(timeoutRequest, hangingEnv);
const timeoutElapsed = Date.now() - timeoutStarted;
assert.equal(timeoutResolved.ok, false, 'IDENTITY_TIMEOUT must fail closed');
assert.equal(timeoutResolved.status, 504);
assert.equal(timeoutResolved.code, 'IDENTITY_TIMEOUT');
assert.ok(timeoutElapsed >= 80 && timeoutElapsed < 1000, `identity timeout should be bounded even when the Service Binding ignores abort, got ${timeoutElapsed}ms`);
assert.equal(hangingRequestSignal?.aborted, true, 'timeout should still signal cancellation to the provider request');


const httpsAssetEnv = {
  ASSETS: {
    async fetch() {
      return new Response('<!doctype html>', { headers: { 'content-type': 'text/html; charset=utf-8' } });
    }
  }
};

const httpGet = await app.fetch(new Request('http://acc.example.com/'), httpsAssetEnv);
assert.equal(httpGet.status, 308, 'HTTP entry must redirect to HTTPS before any login step');
assert.equal(httpGet.headers.get('location'), 'https://acc.example.com/');

const httpCallback = await app.fetch(new Request('http://acc.example.com/auth/sso/callback?code=c&state=s'), httpsAssetEnv);
assert.equal(httpCallback.status, 308, 'HTTP callback must redirect before the code is processed');
assert.equal(httpCallback.headers.get('location'), 'https://acc.example.com/auth/sso/callback?code=c&state=s');

const forwardedHttp = await app.fetch(new Request('https://acc.example.com/', {
  headers: { 'x-forwarded-proto': 'http' }
}), httpsAssetEnv);
assert.equal(forwardedHttp.status, 308, 'forwarded HTTP entry must redirect to HTTPS');
assert.equal(forwardedHttp.headers.get('location'), 'https://acc.example.com/');

const cfVisitorHttp = await app.fetch(new Request('https://acc.example.com/', {
  headers: { 'cf-visitor': '{"scheme":"http"}' }
}), httpsAssetEnv);
assert.equal(cfVisitorHttp.status, 308, 'Cloudflare HTTP visitor entry must redirect to HTTPS');

// The retired password login surface is gone: no credential route, no API login, no recovery API.
const oldLoginGet = await app.fetch(new Request('https://acc.example.com/login'), { ...env, ASSETS: httpsAssetEnv.ASSETS });
assert.equal(oldLoginGet.status, 303);
assert.equal(oldLoginGet.headers.get('location'), '/');
const oldLoginPostCalls = calls.length;
for (const [route, method] of [['/login', 'POST'], ['/api/auth/login', 'POST'], ['/api/auth/password-recovery/start', 'POST'], ['/api/auth/password-recovery/confirm', 'POST'], ['/api/auth/account-portal', 'GET']]) {
  const response = await app.fetch(new Request('https://acc.example.com' + route, {
    method,
    headers: { 'content-type': 'application/json' },
    body: method === 'POST' ? JSON.stringify({ employeeNo: '0123', password: '12345678' }) : undefined
  }), { ...env, ASSETS: { async fetch() { return new Response('missing', { status: 404 }); } } });
  assert.ok([401, 404, 405, 503].includes(response.status), `${method} ${route} must not be a login route (got ${response.status})`);
  assert.equal(response.headers.get('set-cookie'), null);
}
assert.equal(calls.length, oldLoginPostCalls, 'removed routes never reach the identity provider');

const appEntryEnv = {
  ...env,
  ASSETS: {
    async fetch() {
      return new Response('<!doctype html><html><head></head><body>APP</body></html>', {
        headers: { 'content-type': 'text/html; charset=utf-8' }
      });
    }
  }
};
const appEntry = await app.fetch(new Request('https://acc.example.com/', {
  headers: { cookie: `cyaccounting_session=${token}` }
}), appEntryEnv);
assert.equal(appEntry.status, 200);
const appEntryHtml = await appEntry.text();
assert.equal(appEntryHtml, '<!doctype html><html><head></head><body>APP</body></html>');
assert.doesNotMatch(appEntryHtml, /cyaccBootContext|cyid_[0-9a-f]{64}|employee-ci/);

const hangingAssetsEnv = {
  ...env,
  CYACC_ASSET_TIMEOUT_MS: '100',
  ASSETS: { async fetch() { return await new Promise(() => {}); } }
};
const assetTimeoutStarted = Date.now();
const assetTimeoutEntry = await app.fetch(new Request('https://acc.example.com/', {
  headers: { cookie: `cyaccounting_session=${token}` }
}), hangingAssetsEnv);
const assetTimeoutElapsed = Date.now() - assetTimeoutStarted;
assert.equal(assetTimeoutEntry.status, 504);
assert.ok(assetTimeoutElapsed >= 80 && assetTimeoutElapsed < 1000, `protected static entry must be bounded, got ${assetTimeoutElapsed}ms`);

const routeEnv = { ...env, DB: {} };
for (const [method, route, body] of [
  ['POST', '/api/transactions', '{}'],
  ['PUT', '/api/opening-balances', '{}']
]) {
  const response = await app.fetch(new Request('https://acc.example.com' + route, {
    method,
    headers: { cookie: `cyaccounting_session=${token}`, 'content-type': 'application/json' },
    body
  }), routeEnv);
  assert.equal(response.status, 403, `${method} ${route} must reject USER before D1 mutation`);
  const payload = await response.json();
  assert.equal(payload.code, 'READ_ONLY_USER');
}

const workerSource = fs.readFileSync(path.join(ROOT, 'src/app.js'), 'utf8');
assert.doesNotMatch(workerSource, /web_sessions/);
assert.match(workerSource, /identity-adapter|resolveIdentitySession/);
assert.doesNotMatch(workerSource, /app-v\d+|handleV\d+Api/);

const indexHtml = fs.readFileSync(path.join(ROOT, 'public/index.html'), 'utf8');
const authJs = fs.readFileSync(path.join(ROOT, 'public/auth.js'), 'utf8');
const authCss = fs.readFileSync(path.join(ROOT, 'public/auth.css'), 'utf8');
const adaptiveUi = fs.readFileSync(path.join(ROOT, 'public/adaptive-ui.js'), 'utf8');
const workerApp = fs.readFileSync(path.join(ROOT, 'src/app.js'), 'utf8');
assert.doesNotMatch(indexHtml, /authOverlay|loginForm/);
for (const retired of ['public/login.html', 'public/login.js', 'public/login.css', 'scripts/smoke-cyid-development.mjs']) {
  assert.equal(fs.existsSync(path.join(ROOT, retired)), false, `${retired} must stay removed`);
}
assert.doesNotMatch(workerApp, /loginWithCyid|startPasswordRecovery|confirmPasswordRecovery|handleApiLogin|handleNavigationLogin|secureLoginDocument/);
assert.match(workerApp, /startSsoLogin\(env, '\/'\)/);
assert.doesNotMatch(authJs, /'\/login'/);
assert.match(authJs, /activateReadOnlyMobileLedger/);
assert.doesNotMatch(authJs, /window\.__CYACC_BOOT_USER__/);
assert.match(authJs, /window\.cyaccSessionPromise = checkSessionFallback\(\)/);
assert.match(workerApp, /fetchAsset\(request, env, '\/index\.html'\)/);
assert.doesNotMatch(workerApp, /fetchAppEntry|cyaccBootContext|safeJsonForScript/);
assert.doesNotMatch(workerApp, /session\.token/);
assert.match(authJs, /data-mobile-page="ledger"/);
assert.match(authCss, /data-mobile-ledger-action="accounts"/);
assert.match(authCss, /data-mobile-ledger-action="categories"/);
assert.match(authCss, /data-mobile-ledger-action="lock"/);
assert.match(authCss, /data-cyacc-read-only="true"\] \.shell[\s\S]*?grid-template-columns:\s*minmax\(0, 1fr\)/);
assert.doesNotMatch(authCss, /data-mobile-ledger-action="export"[\s\S]*?display:\s*none/);
assert.match(adaptiveUi, /MutationObserver/);
assert.doesNotMatch(adaptiveUi, /querySelector\(['"]\.version['"]\)|CY_[A-Z0-9_]*VERSION|(?:sync|enforce)[A-Za-z0-9_]*Version/);
const version = fs.readFileSync(path.join(ROOT, 'VERSION'), 'utf8').trim();
const build = Number(fs.readFileSync(path.join(ROOT, 'BUILD'), 'utf8').trim());
const displayVersion = `V${version}${build > 0 ? ` Build ${build}` : ''}`;
assert.ok(indexHtml.includes(`<span class="version">${displayVersion}</span>`), 'frontend version must match canonical VERSION and BUILD files');

const migrationDir = path.join(ROOT, 'migrations');
const readFileSyncSafe = name => fs.readFileSync(path.join(migrationDir, name), 'utf8');
const migrationTexts = fs.readdirSync(migrationDir)
  .filter(name => name.endsWith('.sql'))
  .map(name => fs.readFileSync(path.join(migrationDir, name), 'utf8'))
  .join('\n');
assert.doesNotMatch(
  fs.readdirSync(migrationDir).filter(name => /^000[1-9]_|^0010_/.test(name)).map(name => readFileSyncSafe(name)).join('\n'),
  /DROP\s+TABLE\s+(?:IF\s+EXISTS\s+)?web_sessions/i,
  '0.21.6 cutover must not drop the legacy table before post-cutover acceptance'
);

console.log('CYID integration tests passed.');

assert.match(fs.readFileSync(path.join(migrationDir, '0011_backup_health_retire_sessions.sql'), 'utf8'), /DROP TABLE IF EXISTS web_sessions/);
