import assert from 'node:assert/strict';
import { createHash, pbkdf2Sync } from 'node:crypto';
import { readFileSync, readdirSync } from 'node:fs';
import { DatabaseSync } from 'node:sqlite';
import { build } from 'esbuild';

// Exercise the real handler and SQL against all forward migrations, with a
// transactional D1 adapter and controlled concurrent state changes.
const bundle = await build({ entryPoints: ['src/device-lifecycle.ts'], bundle: true, write: false, format: 'esm', platform: 'node' });
const { handleDeviceLifecycle } = await import(`data:text/javascript;base64,${Buffer.from(bundle.outputFiles[0].text).toString('base64')}`);
const db = new DatabaseSync(':memory:');
const migrations = readdirSync('migrations').filter(x => x.endsWith('.sql')).sort();
for (const name of migrations.filter(x => x < '0012')) db.exec(readFileSync(`migrations/${name}`, 'utf8'));
db.prepare("INSERT INTO workspaces (workspace_id, display_name) VALUES ('ws_test', 'Synthetic Workspace')").run();
db.prepare("INSERT INTO security_audit_events (event_id, workspace_id, event_type, outcome, occurred_at) VALUES ('evt_history', 'ws_test', 'device_revoked', 'success', '2026-09-29T00:00:00Z')").run();
for (const name of migrations.filter(x => x >= '0012')) db.exec(readFileSync(`migrations/${name}`, 'utf8'));
assert.equal(db.prepare("SELECT event_type FROM security_audit_events WHERE event_id='evt_history'").get().event_type, 'device_revoked');
assert.equal(db.prepare('PRAGMA foreign_key_check').all().length, 0);
const ids = ['11111111-1111-4111-8111-111111111111', '22222222-2222-4222-8222-222222222222', '33333333-3333-4333-8333-333333333333'].map(x => `dev_${x}`);
const tokens = ['a', 'b', 'c'].map(x => `cydev_${x.repeat(64)}`);
const oldTime = '2026-09-29T00:00:00.000Z';
for (let i = 0; i < ids.length; i++) db.prepare(`INSERT INTO devices
  (device_id, workspace_id, display_name, token_hash, client_version, employee_authority_state, paired_at, created_at, updated_at)
  VALUES (?, 'ws_test', ?, ?, '2.6.13', 'cloud', ?, ?, ?)`).run(ids[i], `Device ${i}`, createHash('sha256').update(tokens[i]).digest('hex'), oldTime, oldTime, oldTime);
const salt = '11'.repeat(16);
const verifier = `pbkdf2-sha256$100000$${salt}$${pbkdf2Sync('SyntheticPass1', Buffer.from(salt, 'hex'), 100000, 32, 'sha256').toString('hex')}`;
db.prepare(`INSERT INTO cloud_employees (employee_id, workspace_id, employee_no, name, email_normalized, role,
  enabled, credential_algorithm, credential_verifier) VALUES ('emp_test', 'ws_test', '0001', 'Synthetic Owner', 'owner@example.test', 'SUPER_ADMIN', 1, 'pbkdf2-sha256', ?)`).run(verifier);
let beforeWrite;
class Statement {
  constructor(sql) { this.sql = sql; this.args = []; }
  bind(...args) { this.args = args; return this; }
  async first() { return db.prepare(this.sql).get(...this.args) ?? null; }
  async all() { return { results: db.prepare(this.sql).all(...this.args) }; }
  async run() {
    if (this.sql.startsWith('UPDATE devices') && beforeWrite) { const action = beforeWrite; beforeWrite = null; action(); }
    return { meta: { changes: db.prepare(this.sql).run(...this.args).changes } };
  }
}
const env = { APP_ENV: 'test', API_VERSION: '1', SCHEMA_VERSION: '12', DB: {
  prepare: sql => new Statement(sql),
  batch: async statements => {
    if (beforeWrite) { const action = beforeWrite; beforeWrite = null; action(); }
    db.exec('BEGIN');
    try { const results = []; for (const stmt of statements) results.push(await stmt.run()); db.exec('COMMIT'); return results; }
    catch (error) { db.exec('ROLLBACK'); throw error; }
  },
} };
async function call(path, body, token = tokens[0]) {
  const response = await handleDeviceLifecycle(new Request(`https://cloud.example.test/v1/devices${path}`, {
    method: body === undefined ? 'GET' : 'POST', headers: { Authorization: `Bearer ${token}`, 'Content-Type': 'application/json' },
    body: body === undefined ? undefined : JSON.stringify(body),
  }), env);
  return { status: response.status, body: await response.json() };
}
const usage = await call('/usage', { clientVersion: ' 2.6.14 Build 1 ', targetDeviceId: ids[1], lastSeenAt: '2099-01-01T00:00:00Z' });
assert.equal(usage.status, 200);
assert.equal(usage.body.device.deviceId, ids[0]);
assert.equal(usage.body.device.clientVersion, '2.6.14 Build 1');
assert.ok(Date.parse(usage.body.device.lastSeenAt) <= Date.now());
assert.ok(Date.parse(usage.body.device.lastSeenAt) > Date.parse(oldTime));
assert.equal(db.prepare('SELECT client_version FROM devices WHERE device_id=?').get(ids[1]).client_version, '2.6.13');
for (const clientVersion of ['', 'x'.repeat(65), '2.6.14\nBad']) assert.equal((await call('/usage', { clientVersion })).status, 400);
const creds = { employeeNo: '0001', password: 'SyntheticPass1' };
const renamed = await call('/rename', { targetDeviceId: ids[1], displayName: '  新裝置 B  ', ...creds });
assert.equal(renamed.status, 200);
assert.equal(renamed.body.device.displayName, '新裝置 B');
assert.equal(renamed.body.device.clientVersion, '2.6.13');
assert.equal(renamed.body.device.lastSeenAt, '');
assert.equal(db.prepare("SELECT COUNT(*) AS n FROM security_audit_events WHERE event_type='device_renamed' AND outcome='success'").get().n, 1);
assert.equal((await call('/rename', { targetDeviceId: ids[1], displayName: 'Bad', ...creds, password: 'Wrong' })).status, 401);
for (const displayName of ['', 'x'.repeat(121), 'Bad\nName']) assert.equal((await call('/rename', { targetDeviceId: ids[1], displayName, ...creds })).status, 400);
assert.equal((await call('/rename', { targetDeviceId: 'dev_44444444-4444-4444-8444-444444444444', displayName: 'Missing', ...creds })).status, 409);
beforeWrite = () => db.prepare("UPDATE cloud_employees SET credential_verifier=? WHERE employee_id='emp_test'").run(verifier + "changed");
assert.equal((await call('/rename', { targetDeviceId: ids[1], displayName: 'Race', ...creds })).status, 409);
db.prepare("UPDATE cloud_employees SET credential_verifier=? WHERE employee_id='emp_test'").run(verifier);
beforeWrite = () => db.prepare("UPDATE devices SET status='revoked', revoked_at=? WHERE device_id=?").run(oldTime, ids[2]);
assert.equal((await call('/usage', { clientVersion: '2.6.14' }, tokens[2])).status, 401);
assert.equal((await call('/rename', { targetDeviceId: ids[2], displayName: 'Revoked', ...creds })).status, 409);
assert.equal((await call('/usage', { clientVersion: '2.6.14' }, tokens[2])).status, 401);
// An audit failure must roll back the name update with it.
db.exec("CREATE TRIGGER reject_rename_audit BEFORE INSERT ON security_audit_events WHEN NEW.event_type='device_renamed' BEGIN SELECT RAISE(ABORT, 'synthetic audit failure'); END;");
assert.equal((await call('/rename', { targetDeviceId: ids[1], displayName: 'Rollback', ...creds })).status, 500);
assert.equal(db.prepare('SELECT display_name FROM devices WHERE device_id=?').get(ids[1]).display_name, '新裝置 B');
db.exec('DROP TRIGGER reject_rename_audit');
const list = await call('');
assert.equal(list.body.activeDeviceCount, 2);
assert.equal(list.body.devices.length, 3); // retained history; desktop alone filters it
assert.equal(list.body.devices.find(x => x.deviceId === ids[2]).status, 'revoked');
db.prepare("UPDATE workspaces SET status='disabled' WHERE workspace_id='ws_test'").run();
assert.equal((await call('/usage', { clientVersion: '2.6.14' })).status, 401);
db.close();
console.log('Device usage, rename, auth/race checks, history and atomic audit regression checks passed.');
