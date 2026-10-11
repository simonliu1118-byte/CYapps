import assert from 'node:assert/strict';
import test from 'node:test';
import fs from 'node:fs';
import path from 'node:path';
import os from 'node:os';
import { DatabaseSync } from 'node:sqlite';
import { buildDevelopmentConfig, checkMigrations, snapshot, checkContinuity, main } from '../scripts/deployment-preflight.mjs';

const root = path.resolve(import.meta.dirname, '..');
const databaseId = '11111111-1111-4111-8111-111111111111';
const canonical = fs.readdirSync(path.join(root, 'migrations')).filter(name => /^\d{4}_.*\.sql$/.test(name)).sort();
function fixture() {
  const values = { APP_ENV: 'development', API_VERSION: 'v1', SESSION_TTL_SECONDS: '28800', EMAIL_PROVIDER: 'brevo',
    EMAIL_FROM: 'Synthetic <test@example.test>', EMAIL_DAILY_BUDGET: '50', CORE_ACCOUNT_APPLICATION_ID: 'APP_TEST',
    ACCOUNT_PORTAL_URL: 'https://portal.example.test/', EXISTING_APP_SETTING: 'retained' };
  return { settings: { bindings: [
    ...Object.entries(values).map(([name, text]) => ({ name, text, type: 'plain_text' })),
    { type: 'd1', name: 'DB', id: databaseId },
    { type: 'ratelimit', name: 'LOGIN_RATE_LIMITER', namespace_id: '900101', simple: { limit: 17, period: 60 } },
    ...['OTP_PEPPER', 'BOOTSTRAP_SECRET', 'BREVO_API_KEY'].map(name => ({ type: 'secret_text', name })),
    { type: 'service', name: 'EXISTING_SERVICE', service: 'synthetic-service', entrypoint: 'ExistingEntry' },
  ] }, database: { uuid: databaseId, name: 'identity_test_dev' } };
}
const config = value => buildDevelopmentConfig(value.settings, value.database, 'identity-test-dev', databaseId);

test('development upgrade preserves vars, resources, custom limits and secret names without material', () => {
  const result = config(fixture());
  assert.equal(result.vars.SESSION_TTL_SECONDS, '28800');
  assert.equal(result.vars.EXISTING_APP_SETTING, 'retained');
  assert.equal(result.d1_databases[0].database_id, databaseId);
  assert.equal(result.ratelimits[0].simple.limit, 17);
  assert.equal(result.services[0].entrypoint, 'ExistingEntry');
  assert.deepEqual(result.secrets.required, ['BOOTSTRAP_SECRET', 'BREVO_API_KEY', 'OTP_PEPPER']);
  assert.equal(Object.hasOwn(result.vars, 'OTP_PEPPER'), false);
});
for (const [name, change] of [
  ['production scope', value => { value.settings.bindings.find(b => b.name === 'APP_ENV').text = 'production'; }],
  ['other database', value => { value.settings.bindings.find(b => b.name === 'DB').id = '22222222-2222-4222-8222-222222222222'; }],
  ['missing secret', value => { value.settings.bindings = value.settings.bindings.filter(b => b.name !== 'OTP_PEPPER'); }],
  ['plaintext secret', value => { value.settings.bindings.push({ type: 'plain_text', name: 'RESEND_API_KEY', text: 'synthetic-value' }); }],
  ['unknown resource', value => { value.settings.bindings.push({ type: 'kv_namespace', name: 'UNKNOWN' }); }],
  ['duplicate binding', value => { value.settings.bindings.push(value.settings.bindings[0]); }],
  ['missing limiter', value => { value.settings.bindings = value.settings.bindings.filter(b => b.name !== 'LOGIN_RATE_LIMITER'); }],
  ['unsafe portal', value => { value.settings.bindings.find(b => b.name === 'ACCOUNT_PORTAL_URL').text = 'http://portal.example.test'; }],
]) test(`development upgrade refuses ${name}`, () => { const value = fixture(); change(value); assert.throws(() => config(value)); });

test('no missing, reordered, extra or pending authority migration is accepted', () => {
  assert.doesNotThrow(() => checkMigrations(canonical, canonical));
  for (const applied of [canonical.slice(0, -1), [...canonical].reverse(), [...canonical, '9999.sql']])
    assert.throws(() => checkMigrations(applied, canonical), /UNREVIEWED_MIGRATION_STATE/);
});

function database() {
  const db = new DatabaseSync(':memory:');
  for (const name of canonical) db.exec(fs.readFileSync(path.join(root, 'migrations', name), 'utf8'));
  db.exec("CREATE TABLE d1_migrations(id INTEGER PRIMARY KEY, name TEXT); INSERT INTO workspaces(workspace_id,workspace_code,display_name) VALUES ('workspace-test','TEST','Synthetic');");
  for (const [i, name] of canonical.entries()) db.prepare('INSERT INTO d1_migrations(id,name) VALUES (?,?)').run(i + 1, name);
  return db;
}

test('real canonical SQLite queries exclude verifiers and tolerate only increasing history', async () => {
  const db = database();
  try {
    const queries = [];
    const query = async sql => { queries.push(sql); return db.prepare(sql).all(); };
    const before = await snapshot(query);
    assert.equal(Object.keys(before.authority.workspaces).length, 1);
    assert.equal(queries.some(sql => /\bverifier\b|session_hash|exchange_token_digest|email_normalized/.test(sql)), false);
    assert.equal(queries.every(sql => /^(SELECT |PRAGMA foreign_key_check$)/.test(sql)), true);
    checkContinuity(before, structuredClone(before));
    const authorityChanged = structuredClone(before);
    authorityChanged.authority.workspaces[Object.keys(before.authority.workspaces)[0]] = 'different';
    assert.throws(() => checkContinuity(before, authorityChanged), /AUTHORITY_CHANGED/);
    const deleted = structuredClone(before); deleted.authority.workspaces = {};
    assert.throws(() => checkContinuity(before, deleted), /AUTHORITY_CHANGED/);
    const priorHistory = structuredClone(before); priorHistory.history.identity_sessions = 1;
    assert.throws(() => checkContinuity(priorHistory, before), /IDENTITY_HISTORY_LOST/);
    const newHistory = structuredClone(before); newHistory.history.identity_sessions = 1;
    checkContinuity(before, newHistory);
  } finally { db.close(); }
});

test('remote audit orchestration is read-only, private and refuses stale config or changed authority', async () => {
  const directory = fs.mkdtempSync(path.join(os.tmpdir(), 'cyid-upgrade-audit-'));
  const statePath = path.join(directory, 'private.json');
  const generated = path.join(root, 'wrangler.deploy.generated.jsonc');
  const prior = fs.existsSync(generated) ? fs.readFileSync(generated) : null;
  const db = database();
  const value = fixture();
  let upgraded = false;
  const requests = [];
  const fetcher = async (url, args = {}) => {
    requests.push({ url, method: args.method ?? 'GET' });
    const endpoint = new URL(url).pathname;
    let result;
    if (endpoint.endsWith('/settings')) result = value.settings;
    else if (endpoint.endsWith('/' + databaseId)) result = value.database;
    else if (endpoint.endsWith('/query')) {
      const sql = JSON.parse(args.body).sql;
      assert.match(sql, /^(SELECT |PRAGMA foreign_key_check$)/);
      result = [{ success: true, results: db.prepare(sql).all() }];
    } else if (endpoint.endsWith('/workers/subdomain')) result = { subdomain: 'synthetic' };
    else if (endpoint.endsWith('/bookmark')) result = { bookmark: 'synthetic-private-checkpoint' };
    else if (endpoint.endsWith('/deployments')) result = { deployments: [{ id: 'synthetic-prior-version' }] };
    else if (endpoint === '/v1/health') return Response.json({ ok: true, service: 'cycloud-identity', environment: 'development', apiVersion: 'v1', serviceVersion: upgraded ? fs.readFileSync(path.join(root, 'VERSION'), 'utf8').trim() : '0.3.5' });
    else if (endpoint === '/v1/identity/session/resolve') return Response.json({ ok: false, error: { code: 'SESSION_INVALID' } }, { status: 401 });
    else throw new Error('Unexpected remote operation');
    return Response.json({ success: true, result });
  };
  const env = { CLOUDFLARE_ACCOUNT_ID: 'synthetic-account', CLOUDFLARE_API_TOKEN: 'synthetic-token', CYID_WORKER_NAME: 'identity-test-dev', CYID_D1_DATABASE_ID: databaseId };
  try {
    await main('before', statePath, env, fetcher);
    assert.equal(fs.statSync(statePath).mode & 0o777, 0o600);
    const saved = fs.readFileSync(statePath, 'utf8');
    assert.equal(saved.includes('synthetic-token'), false);
    assert.equal(saved.includes('workspace-test'), false);
    assert.equal(requests.every(request => request.method === 'GET' || request.url.endsWith('/query')), true);
    upgraded = true;
    await main('after', statePath, env, fetcher);
    value.settings.bindings.find(b => b.name === 'SESSION_TTL_SECONDS').text = '3600';
    await assert.rejects(main('after', statePath, env, fetcher), /AUDIT_SCOPE_OR_RUNTIME_CONFIG_CHANGED/);
    value.settings.bindings.find(b => b.name === 'SESSION_TTL_SECONDS').text = '28800';
    db.exec("UPDATE workspaces SET revision = revision + 1;");
    await assert.rejects(main('after', statePath, env, fetcher), /AUTHORITY_CHANGED/);
    assert.equal(requests.some(request => /\/secrets|\/bootstrap|\/identity\/login/.test(request.url)), false);
  } finally {
    db.close();
    fs.rmSync(directory, { recursive: true, force: true });
    if (prior) fs.writeFileSync(generated, prior); else fs.rmSync(generated, { force: true });
  }
});

test('workflow has no original-provider secret, migration or authority write and defaults to preflight', () => {
  const workflow = fs.readFileSync(path.join(root, '../..', '.github/workflows/cycloudidentity-deploy.yml'), 'utf8');
  const deployment = workflow.split('  deploy-development:')[1];
  assert.match(workflow, /default: preflight/);
  assert.match(workflow, /group: cycloudidentity-development\n  cancel-in-progress: false/);
  assert.match(deployment, /CYID_D1_DATABASE_ID: \$\{\{ vars\.CYID_D1_DATABASE_ID \}\}/);
  assert.doesNotMatch(deployment, /secret put|migrations apply|CYID_OTP_PEPPER|CYID_BOOTSTRAP_SECRET|CYID_BREVO_API_KEY|CYID_RESEND_API_KEY/);
  const guards = deployment.match(/if: \(github\.event_name == 'push' && github\.ref == 'refs\/heads\/deploy\/cycloudidentity-development'\) \|\| \(github\.event_name == 'workflow_dispatch' && inputs\.mode == 'upgrade'\)/g);
  assert.equal(guards.length, 2);
  assert.match(deployment, /if: always\(\)/);
});
