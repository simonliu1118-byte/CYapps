import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import {
  appUserFromPrincipal,
  canWriteAccounting,
  loginWithCyid,
  passwordWithinCyidBounds,
  resolveIdentitySession,
  startPasswordRecovery
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
      if (url.pathname === '/v1/identity/login') {
        return Response.json({ principal: basePrincipal, session: { token, expiresAt } });
      }
      if (url.pathname === '/v1/identity/session/resolve') {
        return Response.json({ principal: basePrincipal, session: { expiresAt } });
      }
      if (url.pathname === '/v1/identity/password-recovery/start') {
        return Response.json({
          recovery: {
            challengeId: 'challenge-ci',
            expiresAt,
            resendAfter: new Date(Date.now() + 60_000).toISOString()
          }
        }, { status: 202 });
      }
      return Response.json({ error: { code: 'NOT_FOUND' } }, { status: 404 });
    }
  }
};

assert.equal(passwordWithinCyidBounds('12345678'), true);
assert.equal(passwordWithinCyidBounds('1234567'), false);
assert.equal(passwordWithinCyidBounds('😀😀😀😀😀😀😀😀'), true);
assert.equal(passwordWithinCyidBounds('😀'.repeat(17)), false);

const loginRequest = new Request('https://acc.example.com/login', {
  method: 'POST',
  headers: { 'cf-connecting-ip': '127.0.0.1' }
});
const login = await loginWithCyid(loginRequest, env, '0123', '12345678');
assert.equal(login.ok, true);
assert.equal(login.user.role, 'USER');
assert.equal(login.user.canWriteAccounting, false);
assert.match(login.cookie, /HttpOnly/);
assert.match(login.cookie, /Secure/);
assert.match(login.cookie, /SameSite=Lax/);
assert.match(login.cookie, /Expires=/);
assert.equal(calls[0].body.applicationId, 'CYACC_CI');
assert.equal(calls[0].body.workspaceId, 'workspace-ci-placeholder');

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
    async fetch(request) {
      return new Response('<!doctype html><form action="/login" method="post"></form>', {
        headers: { 'content-type': 'text/html; charset=utf-8' }
      });
    }
  }
};

const httpGet = await app.fetch(new Request('http://acc.example.com/login'), httpsAssetEnv);
assert.equal(httpGet.status, 308, 'HTTP login GET must redirect to HTTPS before serving credentials form');
assert.equal(httpGet.headers.get('location'), 'https://acc.example.com/login');

const httpPost = await app.fetch(new Request('http://acc.example.com/login', {
  method: 'POST',
  headers: { 'content-type': 'application/x-www-form-urlencoded' },
  body: new URLSearchParams({ employeeNo: '0123', password: '12345678' })
}), httpsAssetEnv);
assert.equal(httpPost.status, 308, 'HTTP login POST must redirect before credential processing');
assert.equal(httpPost.headers.get('location'), 'https://acc.example.com/login');

const forwardedHttp = await app.fetch(new Request('https://acc.example.com/login', {
  headers: { 'x-forwarded-proto': 'http' }
}), httpsAssetEnv);
assert.equal(forwardedHttp.status, 308, 'forwarded HTTP login must redirect to HTTPS');
assert.equal(forwardedHttp.headers.get('location'), 'https://acc.example.com/login');

const cfVisitorHttp = await app.fetch(new Request('https://acc.example.com/login', {
  headers: { 'cf-visitor': '{"scheme":"http"}' }
}), httpsAssetEnv);
assert.equal(cfVisitorHttp.status, 308, 'Cloudflare HTTP visitor login must redirect to HTTPS');

const apiLoginCallStart = calls.length;
const apiLogin = await app.fetch(new Request('https://acc.example.com/api/auth/login', {
  method: 'POST',
  headers: { 'content-type': 'application/json' },
  body: JSON.stringify({ employeeNo: '0123', password: '12345678' })
}), env);
assert.equal(apiLogin.status, 200);
assert.match(apiLogin.headers.get('set-cookie') || '', /cyaccounting_session=/);
const apiLoginPayload = await apiLogin.json();
assert.equal(apiLoginPayload.ok, true);
assert.equal(apiLoginPayload.user.employeeNo, '0123');
assert.equal('token' in apiLoginPayload, false);
assert.deepEqual(
  calls.slice(apiLoginCallStart).map(call => call.pathname),
  ['/v1/identity/login', '/v1/identity/session/resolve'],
  'login success must be returned only after the newly created CYID Session resolves successfully'
);

const loginThenHangEnv = {
  CYID_WORKSPACE_ID: 'workspace-ci-placeholder',
  CYID_APPLICATION_ID: 'CYACC_CI',
  CYID_PROVIDER_TIMEOUT_MS: '100',
  IDENTITY: {
    async fetch(request) {
      const url = new URL(request.url);
      if (url.pathname === '/v1/identity/login') {
        return Response.json({ principal: basePrincipal, session: { token, expiresAt } });
      }
      return await new Promise(() => {});
    }
  }
};
const loginResolveStarted = Date.now();
const loginResolveFailure = await app.fetch(new Request('https://acc.example.com/api/auth/login', {
  method: 'POST',
  headers: { 'content-type': 'application/json' },
  body: JSON.stringify({ employeeNo: '0123', password: '12345678' })
}), loginThenHangEnv);
const loginResolveElapsed = Date.now() - loginResolveStarted;
assert.equal(loginResolveFailure.status, 503);
assert.equal(loginResolveFailure.headers.get('set-cookie'), null, 'unverified Session must not be committed to browser');
assert.ok(loginResolveElapsed >= 80 && loginResolveElapsed < 1000, `post-login Session verification must be bounded, got ${loginResolveElapsed}ms`);

const secureLoginPage = await app.fetch(new Request('https://acc.example.com/login'), httpsAssetEnv);
assert.equal(secureLoginPage.status, 200);
assert.equal(secureLoginPage.headers.get('strict-transport-security'), 'max-age=31536000');
assert.equal(
  secureLoginPage.headers.get('content-security-policy'),
  'upgrade-insecure-requests; form-action https:'
);

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

const recovery = await startPasswordRecovery(new Request('https://acc.example.com/api/auth/password-recovery/start'), env, '0123');
assert.equal(recovery.ok, true);
assert.equal(recovery.recovery.challengeId, 'challenge-ci');
assert.equal('maskedEmail' in recovery.recovery, false);

const workerSource = fs.readFileSync(path.join(ROOT, 'src/app.js'), 'utf8');
assert.doesNotMatch(workerSource, /web_sessions/);
assert.match(workerSource, /identity-adapter|resolveIdentitySession/);
assert.doesNotMatch(workerSource, /app-v17|app-v18|app-v19|handleV\d+Api/);

const indexHtml = fs.readFileSync(path.join(ROOT, 'public/index.html'), 'utf8');
const loginHtml = fs.readFileSync(path.join(ROOT, 'public/login.html'), 'utf8');
const authJs = fs.readFileSync(path.join(ROOT, 'public/auth.js'), 'utf8');
const authCss = fs.readFileSync(path.join(ROOT, 'public/auth.css'), 'utf8');
const adaptiveUi = fs.readFileSync(path.join(ROOT, 'public/adaptive-ui.js'), 'utf8');
const workerApp = fs.readFileSync(path.join(ROOT, 'src/app.js'), 'utf8');
assert.doesNotMatch(indexHtml, /authOverlay|loginForm/);
assert.match(loginHtml, /action="\/login" method="post"/);
assert.match(loginHtml, /id="loginForm"/);
assert.match(workerApp, /url\.pathname === '\/api\/auth\/login'/);
assert.match(loginHtml, /window\.location\.protocol === 'http:'/);
assert.match(loginHtml, /window\.location\.replace/);
assert.doesNotMatch(loginHtml, /src="\/app\.js"/);
assert.match(workerApp, /url\.pathname === '\/login\.html'[\s\S]*?redirect\('\/login'/);
assert.match(workerApp, /upgrade-insecure-requests; form-action https:/);
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
assert.match(indexHtml, /<span class="version">V0\.22\.6 Build 0<\/span>/);

const migrationDir = path.join(ROOT, 'migrations');
const migrationTexts = fs.readdirSync(migrationDir)
  .filter(name => name.endsWith('.sql'))
  .map(name => fs.readFileSync(path.join(migrationDir, name), 'utf8'))
  .join('\n');
assert.doesNotMatch(
  migrationTexts,
  /DROP\s+TABLE\s+(?:IF\s+EXISTS\s+)?web_sessions/i,
  '0.21.6 cutover must not drop the legacy table before post-cutover acceptance'
);

console.log('CYID integration tests passed.');
