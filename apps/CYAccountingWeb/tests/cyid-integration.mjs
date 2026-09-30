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

const recovery = await startPasswordRecovery(new Request('https://acc.example.com/api/auth/password-recovery/start'), env, '0123');
assert.equal(recovery.ok, true);
assert.equal(recovery.recovery.challengeId, 'challenge-ci');
assert.equal('maskedEmail' in recovery.recovery, false);

for (const file of ['src/app.js', 'src/app-v17.js', 'src/app-v18.js', 'src/app-v19.js']) {
  const source = fs.readFileSync(path.join(ROOT, file), 'utf8');
  assert.doesNotMatch(source, /web_sessions/);
  assert.match(source, /identity-adapter|resolveIdentitySession/);
}

const indexHtml = fs.readFileSync(path.join(ROOT, 'public/index.html'), 'utf8');
const loginHtml = fs.readFileSync(path.join(ROOT, 'public/login.html'), 'utf8');
const authJs = fs.readFileSync(path.join(ROOT, 'public/auth.js'), 'utf8');
const authCss = fs.readFileSync(path.join(ROOT, 'public/auth.css'), 'utf8');
const versionPatch = fs.readFileSync(path.join(ROOT, 'public/v0216.js'), 'utf8');
const workerApp = fs.readFileSync(path.join(ROOT, 'src/app.js'), 'utf8');
assert.doesNotMatch(indexHtml, /authOverlay|loginForm/);
assert.match(loginHtml, /action="\/login"/);
assert.doesNotMatch(loginHtml, /src="\/app\.js"/);
assert.match(workerApp, /url\.pathname === '\/login\.html'[\s\S]*?redirect\('\/login'/);
assert.match(authJs, /activateReadOnlyMobileLedger/);
assert.match(authJs, /data-mobile-page="ledger"/);
assert.match(authCss, /data-mobile-ledger-action="accounts"/);
assert.match(authCss, /data-mobile-ledger-action="categories"/);
assert.match(authCss, /data-mobile-ledger-action="lock"/);
assert.doesNotMatch(authCss, /data-mobile-ledger-action="export"[\s\S]*?display:\s*none/);
assert.match(versionPatch, /MutationObserver/);
assert.match(versionPatch, /CY_V0216_VERSION = 'V0\.21\.6'/);

const migration = fs.readFileSync(path.join(ROOT, 'migrations/0005_retire_web_sessions.sql'), 'utf8');
assert.match(migration, /DROP TABLE IF EXISTS web_sessions/i);

console.log('CYID integration tests passed.');
