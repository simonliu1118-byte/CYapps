import assert from 'node:assert/strict';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { DatabaseSync } from 'node:sqlite';
import { checkSettings, checkMigrations, fingerprintRows, checkContinuity, checkHealth, snapshot, main } from '../scripts/deployment-preflight.mjs';

const config = { d1_databases: [{ database_id: 'synthetic-consumer-db' }],
  ratelimits: [{ name: 'LOGIN', namespace_id: '1', simple: { limit: 12, period: 60 } }] };
const settings = { bindings: [
  { name: 'APP_ENV', type: 'plain_text', text: 'development' },
  { name: 'API_VERSION', type: 'plain_text', text: '1' },
  { name: 'DB', type: 'd1', id: 'synthetic-consumer-db' },
  { name: 'LOGIN', type: 'ratelimit', namespace_id: 1, simple: { limit: 12, period: 60 } },
  ...['BOOTSTRAP_KEY', 'OTP_PEPPER', 'EMAIL_FROM', 'BREVO_API_KEY'].map(name => ({ name, type: 'secret_text' })),
] };
assert.equal(checkSettings(settings, config).APP_ENV, 'development');
for (const [replacement, error] of [
  [{ name: 'APP_ENV', type: 'plain_text', text: 'production' }, 'DEVELOPMENT_ENVIRONMENT_REQUIRED'],
  [{ name: 'DB', type: 'd1', id: 'another-db' }, 'ORIGINAL_DATABASE_BINDING_REQUIRED'],
  [{ name: 'LOGIN', type: 'ratelimit', namespace_id: 2, simple: { limit: 12, period: 60 } }, 'RATE_LIMIT_BINDING_CHANGED'],
]) {
  const changed = structuredClone(settings);
  changed.bindings[changed.bindings.findIndex(b => b.name === replacement.name)] = replacement;
  assert.throws(() => checkSettings(changed, config), new RegExp(error));
}
for (const flag of [{ type: 'plain_text', text: 'true' }, { type: 'plain_text', text: 'unknown' }, { type: 'secret_text' }])
  assert.throws(() => checkSettings({ bindings: [...settings.bindings, { name: 'CYID_ENABLED', ...flag }] }, config), /BUILT_IN_AUTHORITY_REQUIRED/);
assert.equal(checkSettings({ bindings: [...settings.bindings, { name: 'CYID_ENABLED', type: 'plain_text', text: 'false' }] }, config).CYID_ENABLED, 'false');
assert.throws(() => checkSettings({ bindings: settings.bindings.filter(b => b.name !== 'OTP_PEPPER') }, config), /RUNTIME_SECRETS_REQUIRED/);
assert.throws(() => checkSettings({ bindings: [...settings.bindings, { name: 'KV', type: 'kv_namespace' }] }, config), /UNREVIEWED_RESOURCE_BINDING/);
assert.throws(() => checkSettings({ bindings: [...settings.bindings, { name: 'SECOND_DB', type: 'd1', id: 'other' }] }, config), /ORIGINAL_DATABASE_BINDING_REQUIRED/);
assert.throws(() => checkSettings({ bindings: settings.bindings.filter(b => b.name !== 'LOGIN') }, config), /RATE_LIMIT_BINDINGS_REQUIRED/);

const migrations = Array.from({ length: 12 }, (_, i) => `${String(i + 1).padStart(4, '0')}_existing.sql`).concat('0013_cyid_invitation_actor.sql');
assert.deepEqual(checkMigrations(migrations.slice(0, 12), migrations), ['0013_cyid_invitation_actor.sql']);
assert.deepEqual(checkMigrations(migrations, migrations, true), []);
assert.throws(() => checkMigrations(migrations.slice(0, 11), migrations), /UNREVIEWED_PENDING_MIGRATIONS/);
assert.throws(() => checkMigrations([...migrations, '0014_unreviewed.sql'], migrations), /MIGRATION_HISTORY_NOT_CANONICAL_PREFIX/);
assert.throws(() => checkMigrations(migrations.slice(1), migrations), /MIGRATION_HISTORY_NOT_CANONICAL_PREFIX/);
assert.throws(() => checkMigrations(migrations.slice(0, 12), migrations, true), /MIGRATION_NOT_COMPLETE/);

const fingerprints = fingerprintRows([{ id: 'device_synthetic', token_hash: 'synthetic_hash', status: 'active' }], 'id');
assert.equal(JSON.stringify(fingerprints).includes('device_synthetic'), false);
assert.equal(JSON.stringify(fingerprints).includes('synthetic_hash'), false);
assert.deepEqual(fingerprints, fingerprintRows([{ status: 'active', token_hash: 'synthetic_hash', id: 'device_synthetic' }], 'id'));
assert.throws(() => fingerprintRows(Array.from({ length: 10001 }, (_, i) => ({ id: String(i) })), 'id'), /AUDIT_ROW_LIMIT_EXCEEDED/);
assert.throws(() => fingerprintRows([{ id: 'duplicate' }, { id: 'duplicate' }], 'id'), /AUDIT_ID_DUPLICATED/);

const statements = [];
const before = await snapshot(async sql => {
  statements.push(sql);
  if (sql === 'PRAGMA foreign_key_check') return [];
  const id = sql.match(/^SELECT ([a-z_]+)/)[1];
  return [{ [id]: 'synthetic_original', workspace_id: 'synthetic_workspace' }];
});
assert.ok(statements.every(sql => sql.startsWith('SELECT ') || sql === 'PRAGMA foreign_key_check'));
assert.ok(statements.every(sql => !sql.includes('credential_verifier') && !sql.includes('email_normalized')));
checkContinuity(before, structuredClone(before));
const appended = structuredClone(before);
appended.security_audit_events.newEvent = 'newHash';
checkContinuity(before, appended); // Ordinary new audit events do not erase history.
for (const table of Object.keys(before)) {
  const changed = structuredClone(before);
  changed[table][Object.keys(changed[table])[0]] = 'changed';
  assert.throws(() => checkContinuity(before, changed), /HISTORY_CHANGED_/);
}
await assert.rejects(snapshot(async sql => sql === 'PRAGMA foreign_key_check' ? [{ table: 'broken' }] : []), /FOREIGN_KEY_CHECK_FAILED/);

const health = { ok: true, storage: 'ok', service: 'cyinvoice-cloud', environment: 'development', apiVersion: '1',
  schemaVersion: '8', storageSchemaVersion: '13', cloudVersion: '0.9.2',
  capabilities: ['device-revoke-v1', 'device-self-status-v1', 'device-usage-v1', 'device-rename-v1', 'cyid-consumer-v1', 'runtime-sync-v1'] };
checkHealth(health, { version: '0.9.2', schema: 13 });
assert.throws(() => checkHealth({ ...health, cloudVersion: '0.9.0' }, { version: '0.9.2', schema: 13 }), /CLOUD_SOURCE_VERSION_MISMATCH/);
assert.throws(() => checkHealth({ ...health, capabilities: health.capabilities.slice(0, -1) }, { version: '0.9.2', schema: 13 }), /CLOUD_CAPABILITIES_MISSING/);
for (const field of ['environment', 'apiVersion', 'schemaVersion', 'storageSchemaVersion', 'storage'])
  assert.throws(() => checkHealth({ ...health, [field]: 'wrong' }, { version: '0.9.2', schema: 13 }), /CLOUD_HEALTH_CONTRACT_INVALID/);

// Exercise the deployment comparison across the actual table-rebuilding migration.
const db = new DatabaseSync(':memory:');
const canonical = fs.readdirSync('migrations').filter(n => n.endsWith('.sql')).sort();
for (const name of canonical.slice(0, 12)) db.exec(fs.readFileSync(`migrations/${name}`, 'utf8'));
db.exec(`
  INSERT INTO workspaces (workspace_id, display_name) VALUES ('workspace_synthetic', 'Synthetic');
  INSERT INTO devices (device_id, workspace_id, display_name, token_hash, status)
    VALUES ('device_synthetic', 'workspace_synthetic', 'Synthetic', 'synthetic_token_hash', 'active');
  INSERT INTO cloud_employees (employee_id, workspace_id, employee_no, name, email_normalized, role)
    VALUES ('employee_synthetic', 'workspace_synthetic', '0001', 'Synthetic', 'owner@example.test', 'SUPER_ADMIN');
  INSERT INTO device_invitations (invitation_id, workspace_id, code_hash, issued_by_device_id, issued_by_employee_id,
    delivery_state, expires_at, created_at) VALUES ('invitation_synthetic', 'workspace_synthetic', 'synthetic_code_hash',
    'device_synthetic', 'employee_synthetic', 'sent', '2026-10-12', '2026-10-11');
`);
const query = async sql => db.prepare(sql).all();
const schema12 = await snapshot(query);
db.exec(fs.readFileSync(`migrations/${canonical[12]}`, 'utf8'));
const schema13 = await snapshot(query);
checkContinuity(schema12, schema13);
assert.equal(db.prepare('SELECT issued_by_cyid_employee_id FROM device_invitations').get().issued_by_cyid_employee_id, null);
// Built-in issuance remains valid after 0013; external actors never overwrite history.
db.exec(`INSERT INTO device_invitations (invitation_id, workspace_id, code_hash, issued_by_device_id, issued_by_employee_id,
  delivery_state, expires_at, created_at) VALUES ('invitation_after', 'workspace_synthetic', 'after_code_hash',
  'device_synthetic', 'employee_synthetic', 'pending', '2026-10-12', '2026-10-11')`);
checkContinuity(schema12, await snapshot(query));
db.exec("UPDATE devices SET token_hash='unexpected_replacement' WHERE device_id='device_synthetic'");
assert.throws(() => checkContinuity(schema12, undefined), /AUDIT_SNAPSHOT_INVALID/);
assert.throws(() => checkContinuity(schema12, { ...schema13, devices: fingerprintRows(db.prepare('SELECT device_id, workspace_id, token_hash, status, revoked_at, employee_authority_state FROM devices').all(), 'device_id') }), /HISTORY_CHANGED_DEVICES/);

// Run the actual CLI orchestration against synthetic Cloudflare HTTP responses.
const deploymentConfig = JSON.parse(fs.readFileSync('wrangler.jsonc', 'utf8'));
const tmp = fs.mkdtempSync(path.join(os.tmpdir(), 'cyinvoice-deployment-test-'));
const generatedPath = path.resolve('wrangler.deploy.generated.jsonc');
const priorGenerated = fs.existsSync(generatedPath) ? fs.readFileSync(generatedPath) : null;
const originalFetch = globalThis.fetch;
const originalLog = console.log;
let applied = canonical.slice(0, 12), currentDatabase = '11111111-1111-4111-8111-111111111111';
const calls = [], logs = [];
console.log = value => logs.push(value);
globalThis.fetch = async (url, options = {}) => {
  const u = new URL(url);
  calls.push([u.pathname, options.method ?? 'GET']);
  let result;
  if (u.pathname.endsWith('/settings')) result = { bindings: [
    ...settings.bindings.filter(b => b.name !== 'DB' && b.name !== 'LOGIN'),
    { name: 'DB', type: 'd1', id: currentDatabase },
    { name: 'SCHEMA_VERSION', type: 'plain_text', text: String(applied.length) },
    { name: 'CUSTOM_POLICY', type: 'plain_text', text: 'preserve-synthetic-policy' },
    ...deploymentConfig.ratelimits.map(r => ({ ...r, type: 'ratelimit' })),
    { name: 'IDENTITY', type: 'service', service: 'synthetic-provider' },
    { name: 'IDENTITY_AUTHORITY', type: 'service', service: 'synthetic-provider', entrypoint: 'ConsumerAuthoritySync' },
  ] };
  else if (u.pathname.endsWith('/query')) {
    const { sql } = JSON.parse(options.body);
    assert.ok(sql.startsWith('SELECT ') || sql === 'PRAGMA foreign_key_check');
    const results = sql.startsWith('SELECT name FROM d1_migrations') ? applied.map(name => ({ name })) : db.prepare(sql).all();
    result = [{ success: true, results }];
  } else if (u.pathname.endsWith('/time_travel/bookmark')) {
    assert.ok(u.searchParams.get('timestamp'));
    result = { bookmark: 'synthetic-bookmark' };
  } else if (u.pathname.endsWith('/deployments')) result = { deployments: [{ id: 'synthetic-deployment', versions: [{ version_id: 'synthetic-version', percentage: 100 }] }] };
  else if (u.pathname.endsWith('/subdomain')) result = { subdomain: 'synthetic-account' };
  else if (u.pathname.endsWith('/v1/health')) return Response.json({ ...health, storageSchemaVersion: String(applied.length), cloudVersion: applied.length === 12 ? '0.8.9' : '0.9.2' });
  else if (u.pathname.endsWith('/' + currentDatabase)) result = { uuid: currentDatabase, name: deploymentConfig.d1_databases[0].database_name };
  else throw new Error('UNEXPECTED_TEST_REQUEST');
  return Response.json({ success: true, result });
};
try {
  const env = { CLOUDFLARE_API_TOKEN: 'synthetic-token', CLOUDFLARE_ACCOUNT_ID: 'synthetic-account' };
  const statePath = path.join(tmp, 'checkpoint.json');
  await main('before', statePath, env);
  const generated = JSON.parse(fs.readFileSync(generatedPath, 'utf8'));
  assert.equal(generated.d1_databases[0].database_id, currentDatabase);
  assert.equal(generated.vars.CYID_ENABLED, 'false');
  assert.equal(generated.vars.CUSTOM_POLICY, 'preserve-synthetic-policy');
  assert.equal(generated.vars.SCHEMA_VERSION, '13');
  assert.equal(generated.services[1].entrypoint, 'ConsumerAuthoritySync');
  assert.equal(fs.statSync(statePath).mode & 0o777, 0o600);
  assert.equal(fs.statSync(generatedPath).mode & 0o777, 0o600);
  applied = canonical;
  await main('after', statePath, env);
  currentDatabase = '22222222-2222-4222-8222-222222222222';
  await assert.rejects(main('after', statePath, env), /AUDIT_SCOPE_MISMATCH/);
  assert.ok(calls.every(([url, method]) => method === 'GET' || (method === 'POST' && url.endsWith('/query'))));
  assert.ok(!logs.join('\n').includes('synthetic-token') && !logs.join('\n').includes('synthetic-bookmark') &&
    !logs.join('\n').includes('11111111-1111-4111-8111-111111111111'));
} finally {
  globalThis.fetch = originalFetch; console.log = originalLog;
  if (priorGenerated) fs.writeFileSync(generatedPath, priorGenerated, { mode: 0o600 });
  else fs.rmSync(generatedPath, { force: true });
  fs.rmSync(tmp, { recursive: true, force: true });
  db.close();
}
console.log('Deployment audit: scope/authority/migrations/continuity/privacy/health regressions passed.');
