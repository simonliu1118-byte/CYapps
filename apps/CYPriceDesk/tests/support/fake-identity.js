// Fake CYID provider behind the IDENTITY service binding.
import {randomBytes} from 'node:crypto';

export const WORKSPACE_ID = 'workspace-test';
export const APPLICATION_ID = 'CYPD_TEST';

export function createFakeIdentity({acceptAnyLogin = false} = {}) {
  const sessions = new Map();
  const calls = [];
  const principal = employeeNo => ({
    workspaceId: WORKSPACE_ID, employeeId: `emp-${employeeNo}`, employeeNo, displayName: '測試員工',
    workspaceRole: 'USER', isIdentityAdmin: false, emailVerified: true, isWorkspaceSuperAdmin: false,
    credentialVersion: 1, employeeRevision: 1
  });
  const reply = (status, data) => new Response(JSON.stringify(data), {status, headers: {'content-type': 'application/json'}});
  return {
    calls,
    sessions,
    async fetch(request) {
      const path = new URL(request.url).pathname;
      calls.push(path);
      if (path === '/v1/identity/login') {
        const body = await request.json();
        const known = acceptAnyLogin || (['0001', '0002'].includes(body.employeeNo) && body.password === 'password1');
        if (!known) return reply(401, {error: {code: 'INVALID_CREDENTIALS'}});
        if (!acceptAnyLogin && body.employeeNo === '0002') return reply(200, {passwordChangeRequired: true, principal: principal('0002')});
        const token = `cyid_${randomBytes(32).toString('hex')}`;
        sessions.set(token, principal(body.employeeNo));
        return reply(200, {principal: principal(body.employeeNo), session: {token, expiresAt: new Date(Date.now() + 8 * 3600_000).toISOString()}});
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
