// Fake CYID provider behind the IDENTITY service binding.
import {randomBytes} from 'node:crypto';

export const WORKSPACE_ID = 'workspace-test';
export const APPLICATION_ID = 'CYPD_TEST';

// Handoff codes are 43-character base64url strings; `issueCode` registers one that redeem accepts exactly once.
export function createFakeIdentity() {
  const sessions = new Map();
  const codes = new Map();
  const calls = [];
  const redeemHeaders = [];
  const state = {rateLimited: false, unavailable: false};
  const principal = employeeNo => ({
    workspaceId: WORKSPACE_ID, employeeId: `emp-${employeeNo}`, employeeNo, displayName: '測試員工',
    workspaceRole: 'USER', isIdentityAdmin: false, emailVerified: true, isWorkspaceSuperAdmin: false,
    credentialVersion: 1, employeeRevision: 1
  });
  const reply = (status, data) => new Response(JSON.stringify(data), {status, headers: {'content-type': 'application/json'}});
  return {
    calls,
    sessions,
    redeemHeaders,
    state,
    // A ready-made Session for tests that are not about sign-in; returns the cookie header value.
    cookieFor(employeeNo = '0001') {
      const token = `cyid_${randomBytes(32).toString('hex')}`;
      sessions.set(token, principal(employeeNo));
      return `cypd_session=${token}`;
    },
    issueCode(employeeNo = '0001') {
      const code = randomBytes(32).toString('base64url');
      codes.set(code, employeeNo);
      return code;
    },
    async fetch(request) {
      const path = new URL(request.url).pathname;
      calls.push(path);
      if (path === '/v1/identity/sso/handoff/redeem') {
        redeemHeaders.push(request.headers.get('x-identity-application'));
        if (state.rateLimited) return reply(429, {error: {code: 'AUTH_RATE_LIMITED'}});
        if (state.unavailable) return reply(503, {error: {code: 'IDENTITY_UNAVAILABLE'}});
        const {code} = await request.json();
        const employeeNo = codes.get(code);
        if (!employeeNo || request.headers.get('x-identity-application') !== APPLICATION_ID) return reply(401, {error: {code: 'SSO_HANDOFF_INVALID'}});
        codes.delete(code);
        const token = `cyid_${randomBytes(32).toString('hex')}`;
        sessions.set(token, principal(employeeNo));
        return reply(200, {ok: true, principal: principal(employeeNo), session: {token, expiresAt: new Date(Date.now() + 8 * 3600_000).toISOString()}});
      }
      const token = (request.headers.get('authorization') || '').replace(/^Bearer /, '');
      if (request.headers.get('x-identity-application') !== APPLICATION_ID) return reply(403, {error: {code: 'APPLICATION_ACCESS_DENIED'}});
      if (path === '/v1/identity/session/resolve') {
        const found = sessions.get(token);
        return found ? reply(200, {principal: found, session: {expiresAt: new Date(Date.now() + 8 * 3600_000).toISOString()}}) : reply(401, {error: {code: 'SESSION_INVALID'}});
      }
      if (path === '/v1/identity/logout') {
        sessions.delete(token);
        return reply(200, {ok: true});
      }
      return reply(404, {});
    }
  };
}
