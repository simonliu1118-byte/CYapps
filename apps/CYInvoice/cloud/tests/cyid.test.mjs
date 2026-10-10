import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { readFileSync, readdirSync } from 'node:fs';
import { DatabaseSync } from 'node:sqlite';
import { build } from 'esbuild';
const versions = ['../../CYCloudIdentity/CONSUMER_MIN_COMPATIBLE_VERSION', '../CYID_CONSUMER_VERSION', '../../CYCloudIdentity/CONSUMER_CONTRACT_VERSION'];
const parsedVersions = versions.map(path => readFileSync(path, 'utf8').trim().split('.').map(Number));
const compare = (a,b) => a[0]-b[0] || a[1]-b[1] || a[2]-b[2];
assert.ok(compare(parsedVersions[0], parsedVersions[1]) <= 0 && compare(parsedVersions[1], parsedVersions[2]) <= 0, 'consumer version must be inside canonical support window');

async function moduleFrom(path) {
  const result = await build({ entryPoints: [path], bundle: true, write: false, format: 'esm', platform: 'node' });
  return import(`data:text/javascript;base64,${Buffer.from(result.outputFiles[0].text).toString('base64')}`);
}
const { default: invoice } = await moduleFrom('src/app.ts');
const { default: identity } = await moduleFrom('../../CYCloudIdentity/src/index.ts');
const { createCredentialVerifier } = await moduleFrom('../../CYCloudIdentity/src/crypto.ts');

function database(migrations, beforeMigration) {
  const db = new DatabaseSync(':memory:');
  for (const name of readdirSync(migrations).filter(x => x.endsWith('.sql')).sort()) {
    if (beforeMigration) beforeMigration(db, name);
    db.exec(readFileSync(`${migrations}/${name}`, 'utf8'));
  }
  class Statement {
    constructor(sql) { this.sql = sql; this.args = []; }
    bind(...args) { this.args = args; return this; }
    async first(column) { const row = db.prepare(this.sql).get(...this.args); return column ? row?.[column] ?? null : row ?? null; }
    async all() { return { success: true, results: db.prepare(this.sql).all(...this.args) }; }
    async run() { return { success: true, meta: { changes: db.prepare(this.sql).run(...this.args).changes } }; }
  }
  return { db, d1: { prepare: sql => new Statement(sql), batch: async statements => {
    db.exec('BEGIN');
    try { const results = []; for (const stmt of statements) results.push(await stmt.run()); db.exec('COMMIT'); return results; }
    catch (error) { db.exec('ROLLBACK'); throw error; }
  } } };
}
const consumer = database('migrations', (db, name) => {
  if (!name.startsWith('0013')) return;
  db.exec(`INSERT INTO workspaces (workspace_id, display_name) VALUES ('ws_invoice', 'Historical');
    INSERT INTO devices (device_id, workspace_id, display_name, token_hash, status) VALUES ('dev_history', 'ws_invoice', 'Historical', 'old_hash', 'revoked');
    INSERT INTO cloud_employees (employee_id, workspace_id, employee_no, name, email_normalized, role)
      VALUES ('emp_history', 'ws_invoice', '0001', 'Historical', 'history@example.test', 'SUPER_ADMIN');
    INSERT INTO device_invitations (invitation_id, workspace_id, code_hash, issued_by_device_id, issued_by_employee_id,
      delivery_state, expires_at, created_at) VALUES ('inv_history', 'ws_invoice', 'old_code', 'dev_history', 'emp_history',
      'sent', '2026-09-29', '2026-09-01');`);
});
assert.equal(consumer.db.prepare("SELECT issued_by_employee_id FROM device_invitations WHERE invitation_id='inv_history'").get().issued_by_employee_id, 'emp_history');
const provider = database('../../CYCloudIdentity/migrations');
const pass = 'SyntheticPass1';
const verifier = await createCredentialVerifier(pass);
provider.db.prepare("INSERT INTO workspaces (workspace_id, workspace_code, display_name) VALUES ('ws_identity', 'TEST', 'Synthetic Identity')").run();
for (const [id, no, role] of [['emp_owner', '0001', 'ADMIN'], ['emp_user', '0002', 'USER']]) {
  provider.db.prepare(`INSERT INTO employees (employee_id, workspace_id, employee_no, name, email_normalized,
    email_verified_at, activated_at, role_key) VALUES (?, 'ws_identity', ?, ?, ?, '2026-10-01', '2026-10-01', ?)`)
    .run(id, no, id, `${id}@example.test`, role);
  provider.db.prepare('INSERT INTO employee_credentials (employee_id, algorithm, verifier) VALUES (?, ?, ?)')
    .run(id, "scrypt", verifier);
}
provider.db.prepare("UPDATE workspaces SET status='active', super_admin_employee_id='emp_owner' WHERE workspace_id='ws_identity'").run();
provider.db.prepare("INSERT INTO applications (application_id, display_name) VALUES ('CYINVOICE', 'Synthetic Invoice')").run();
provider.db.prepare("INSERT INTO workspace_applications (workspace_id, application_id) VALUES ('ws_identity', 'CYINVOICE')").run();
provider.db.prepare("INSERT INTO employee_application_access (workspace_id, employee_id, application_id) VALUES ('ws_identity', 'emp_user', 'CYINVOICE')").run();

const ids = ['dev_11111111-1111-4111-8111-111111111111', 'dev_22222222-2222-4222-8222-222222222222'];
const tokens = ['a', 'b'].map(x => `cydev_${x.repeat(64)}`);
for (let i = 0; i < 2; i++) consumer.db.prepare(`INSERT INTO devices (device_id, workspace_id, display_name,
  token_hash, employee_authority_state) VALUES (?, 'ws_invoice', ?, ?, 'cloud')`)
  .run(ids[i], `Device ${i}`, createHash('sha256').update(tokens[i]).digest('hex'));
const rate = { limit: async () => ({ success: true }) };
const identityEnv = { DB: provider.d1, APP_ENV: 'test', API_VERSION: '1', LOGIN_RATE_LIMITER: rate };
let providerBehavior;
const paths = [];
const env = { DB: consumer.d1, APP_ENV: 'test', API_VERSION: '1', SCHEMA_VERSION: '13', CYID_ENABLED: 'true',
  IDENTITY_APPLICATION_ID: 'CYINVOICE', IDENTITY_WORKSPACE_ID: 'ws_identity', IDENTITY_CYINVOICE_WORKSPACE_ID: 'ws_invoice',
  WEB_LOGIN_EMPLOYEE_RATE_LIMIT: rate, WEB_LOGIN_IP_RATE_LIMIT: rate, WEB_PASSWORD_RESET_EMPLOYEE_RATE_LIMIT: rate,
  WEB_PASSWORD_RESET_IP_RATE_LIMIT: rate, IDENTITY: { fetch: async request => {
    paths.push(new URL(request.url).pathname);
    if (providerBehavior) { const response = await providerBehavior(request); if (response) return response; }
    return identity.fetch(request, identityEnv);
  } } };
async function call(path, body, token = tokens[0], overrides = {}) {
  const response = await invoice.fetch(new Request(`https://invoice.example.test${path}`, {
    method: body === undefined ? 'GET' : 'POST', headers: { Authorization: `Bearer ${token}`, 'Content-Type': 'application/json' },
    body: body === undefined ? undefined : JSON.stringify(body),
  }), { ...env, ...overrides });
  return { status: response.status, body: await response.json() };
}
const credentials = { employeeNo: '0001', password: pass };
const auth = await call('/v1/cyid/authenticate', credentials);
assert.equal(auth.status, 200);
assert.equal(auth.body.principal.workspaceRole, 'SUPER_ADMIN');
assert.equal(auth.body.workspaceId, 'ws_invoice');
assert.equal(auth.body.principal.workspaceId, 'ws_identity');
assert.equal(auth.body.principal.employeeId, 'emp_owner');
assert.deepEqual(paths, ['/v1/identity/login', '/v1/identity/session/resolve', '/v1/identity/logout']);
assert.equal(provider.db.prepare('SELECT count(*) n FROM identity_sessions WHERE revoked_at IS NULL').get().n, 0);
assert.ok(!JSON.stringify(auth.body).includes('cyid_'));
assert.ok(!JSON.stringify(auth.body).includes(verifier));
assert.equal((await call('/v1/identity-provider')).body.provider, 'CYID');
assert.equal((await call('/v1/identity-provider', undefined, tokens[0], { CYID_ENABLED: 'false' })).body.provider, 'BUILT_IN');
assert.equal((await call('/v1/identity-provider', undefined, tokens[0], { IDENTITY_WORKSPACE_ID: undefined })).status, 503);
assert.equal((await call('/v1/identity-provider', undefined, tokens[0], { IDENTITY_CYINVOICE_WORKSPACE_ID: 'ws_other' })).status, 409);
assert.equal((await call('/v1/cyid/authenticate', credentials, `cydev_${'f'.repeat(64)}`)).status, 401);
for (const path of ['/v1/employee-authority/snapshot', '/v1/employees', '/v1/web-auth/login', '/v1/bootstrap'])
  assert.equal((await call(path, path.endsWith('snapshot') || path.endsWith('employees') ? undefined : credentials)).status, 409);
const renamed = await call('/v1/devices/rename', { ...credentials, targetDeviceId: ids[1], displayName: 'CYID Device' });
assert.equal(renamed.status, 200);
assert.equal(consumer.db.prepare('SELECT display_name FROM devices WHERE device_id=?').get(ids[1]).display_name, 'CYID Device');
assert.equal(consumer.db.prepare("SELECT actor_employee_id FROM security_audit_events WHERE event_type='device_renamed'").get().actor_employee_id, 'emp_owner');
// A USER has App Access but cannot administer devices.
assert.equal((await call('/v1/devices/rename', { ...credentials, employeeNo: '0002', targetDeviceId: ids[1], displayName: 'Denied' })).status, 401);
assert.equal((await call('/v1/cyid/authenticate', { employeeNo: '0002', password: pass })).body.principal.workspaceRole, 'USER');
// Existing pairing OTP / invitation / direct join keep their UX, with CYID authority.
const originalFetch = globalThis.fetch;
const emails = [];
globalThis.fetch = async (url, init) => {
  assert.equal(String(url), 'https://api.resend.com/emails');
  emails.push(JSON.parse(init.body));
  return Response.json({ id: 'synthetic_delivery' });
};
const emailEnv = { EMAIL_PROVIDER: 'resend', EMAIL_FROM: 'sender@example.test',
  RESEND_API_KEY: crypto.randomUUID(), OTP_PEPPER: crypto.randomUUID() };
try {
  const email = await call('/v1/device-pairings/authorization-email', credentials, tokens[0], emailEnv);
  assert.equal(email.status, 201);
  const otp = emails.at(-1).text.match(/\b\d{6}\b/)[0];
  const pairing = await call('/v1/device-pairings', { ...credentials, emailChallengeId: email.body.challenge.challengeId,
    emailOtp: otp }, tokens[0], emailEnv);
  assert.equal(pairing.status, 201);
  const pairedToken = `cydev_${'c'.repeat(64)}`;
  const claim = await call('/v1/device-pairings/claim', { code: pairing.body.pairing.code,
    directJoin: true, deviceDisplayName: 'Paired', clientVersion: '2.6.15', deviceToken: pairedToken }, tokens[0], emailEnv);
  assert.equal(claim.status, 201);
  assert.equal((await call('/v1/identity-provider', undefined, pairedToken)).body.provider, 'CYID');
  const invitation = await call('/v1/device-invitations', credentials, tokens[0], emailEnv);
  assert.equal(invitation.status, 201);
  const code = emails.at(-1).text.match(/[0-9a-f]{40}/)[0];
  const inviter = consumer.db.prepare('SELECT issued_by_employee_id, issued_by_cyid_employee_id FROM device_invitations WHERE invitation_id=?')
    .get(invitation.body.invitation.invitationId);
  assert.equal(inviter.issued_by_employee_id, null);
  assert.equal(inviter.issued_by_cyid_employee_id, 'emp_owner');
  assert.equal((await call('/v1/device-invitations/preview', { ...credentials, code }, tokens[0], emailEnv)).status, 200);
  const invitedToken = `cydev_${'d'.repeat(64)}`;
  const joined = await call('/v1/device-invitations/claim', { ...credentials, code, deviceDisplayName: 'Invited',
    clientVersion: '2.6.15', deviceToken: invitedToken }, tokens[0], emailEnv);
  assert.equal(joined.status, 201);
  assert.equal((await call('/v1/identity-provider', undefined, invitedToken)).body.provider, 'CYID');
  const revoked = await call('/v1/device-invitations', credentials, tokens[0], emailEnv);
  assert.equal((await call('/v1/device-invitations/revoke', { ...credentials, invitationId: revoked.body.invitation.invitationId }, tokens[0], emailEnv)).status, 200);
  // Return to two active devices for last-active-device acceptance below.
  consumer.db.prepare("UPDATE devices SET status='revoked' WHERE token_hash IN (?,?)")
    .run(createHash('sha256').update(pairedToken).digest('hex'), createHash('sha256').update(invitedToken).digest('hex'));
} finally { globalThis.fetch = originalFetch; }
provider.db.prepare("UPDATE employee_application_access SET enabled=0 WHERE employee_id='emp_user'").run();
assert.equal((await call('/v1/cyid/authenticate', { employeeNo: '0002', password: pass })).status, 403);
provider.db.prepare("UPDATE employee_application_access SET enabled=1 WHERE employee_id='emp_user'").run();
provider.db.prepare("UPDATE employees SET role_key='ADMIN', revision=revision+1 WHERE employee_id='emp_user'").run();
assert.equal((await call('/v1/cyid/authenticate', { employeeNo: '0002', password: pass })).body.principal.workspaceRole, 'ADMIN');
provider.db.prepare("UPDATE employees SET enabled=0, revision=revision+1 WHERE employee_id='emp_user'").run();
assert.equal((await call('/v1/cyid/authenticate', { employeeNo: '0002', password: pass })).status, 401);
provider.db.prepare("UPDATE employee_credentials SET credential_version=credential_version+1 WHERE employee_id='emp_owner'").run();
assert.equal((await call('/v1/cyid/authenticate', credentials)).body.principal.credentialVersion, 2);
let mutateOnResolve = true;
providerBehavior = async request => {
  if (mutateOnResolve && new URL(request.url).pathname.endsWith('/resolve')) {
    mutateOnResolve = false;
    provider.db.prepare("UPDATE employee_credentials SET credential_version=credential_version+1 WHERE employee_id='emp_owner'").run();
  }
};
assert.equal((await call('/v1/cyid/authenticate', credentials)).status, 401);
assert.equal(provider.db.prepare('SELECT count(*) n FROM identity_sessions WHERE revoked_at IS NULL').get().n, 0);
providerBehavior = async request => {
  if (new URL(request.url).pathname.endsWith('/resolve')) return Response.json({ ok: true, principal: {
    ...auth.body.principal, workspaceId: 'ws_other' } });
};
assert.equal((await call('/v1/cyid/authenticate', credentials)).status, 502);
assert.equal(provider.db.prepare('SELECT count(*) n FROM identity_sessions WHERE revoked_at IS NULL').get().n, 0);
providerBehavior = async request => {
  if (new URL(request.url).pathname.endsWith('/login')) return Response.json({ ok: true, passwordChangeRequired: true,
    firstLogin: { token: 'first_login_ticket' } });
};
assert.equal((await call('/v1/cyid/authenticate', credentials)).status, 502);
providerBehavior = async () => { throw new Error('synthetic transport outage'); };
assert.equal((await call('/v1/cyid/authenticate', credentials)).status, 503);
providerBehavior = undefined;
// Logout loss must neither replay the completed mutation nor pretend the provider
// Session was revoked. Only CYID owns expiry/revocation of an orphaned Session.
providerBehavior = async request => {
  if (new URL(request.url).pathname.endsWith('/logout')) throw new Error('synthetic logout response loss');
};
const auditBefore = consumer.db.prepare("SELECT count(*) n FROM security_audit_events WHERE event_type='device_renamed'").get().n;
assert.equal((await call('/v1/devices/rename', { ...credentials, targetDeviceId: ids[1], displayName: 'Once only' })).status, 200);
assert.equal(consumer.db.prepare("SELECT count(*) n FROM security_audit_events WHERE event_type='device_renamed'").get().n, auditBefore + 1);
assert.equal(provider.db.prepare('SELECT count(*) n FROM identity_sessions WHERE revoked_at IS NULL').get().n, 1);
provider.db.prepare("UPDATE identity_sessions SET revoked_at=datetime('now') WHERE revoked_at IS NULL").run();
providerBehavior = undefined;
assert.equal((await call('/v1/devices/revoke', { ...credentials, targetDeviceId: ids[1] })).status, 200);
assert.equal((await call('/v1/cyid/authenticate', credentials, tokens[1])).status, 401);
assert.equal((await call('/v1/devices/revoke', { ...credentials, targetDeviceId: ids[0] })).status, 409);
assert.equal(consumer.db.prepare('PRAGMA foreign_key_check').all().length, 0);
assert.equal(provider.db.prepare('PRAGMA foreign_key_check').all().length, 0);
console.log('PASS CYID real-provider gateway/session/access/device contracts');
