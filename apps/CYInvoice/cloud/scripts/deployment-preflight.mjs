// Deployment audit only. Identity authorization stays in the existing Worker.
import fs from 'node:fs';
import path from 'node:path';
import { createHash } from 'node:crypto';
import { pathToFileURL } from 'node:url';

const root = path.resolve(import.meta.dirname, '..');
const digest = value => createHash('sha256').update(JSON.stringify(value)).digest('hex');
const fail = code => { throw new Error(code); };
const check = (condition, code) => { if (!condition) fail(code); };
const rowLimit = 10000;
const tables = {
  workspaces: 'workspace_id, status, employee_revision',
  devices: 'device_id, workspace_id, token_hash, status, revoked_at, employee_authority_state',
  cloud_employees: 'employee_id, workspace_id, employee_no, role, enabled, credential_version, revision',
  device_pairing_codes: 'pairing_id, workspace_id, code_hash, created_by_device_id, claimed_device_id, expires_at, used_at, created_at',
  device_invitations: 'invitation_id, workspace_id, code_hash, issued_by_device_id, issued_by_employee_id, delivery_state, expires_at, sent_at, revoked_at, consumed_at, consumed_by_device_id, created_at',
  security_audit_events: 'event_id, workspace_id, event_type, outcome, actor_device_id, actor_employee_id, target_device_id, pairing_id, invitation_id, reason_code, request_id, occurred_at',
};

export function checkSettings(settings, config) {
  const bindings = settings?.bindings;
  check(Array.isArray(bindings), 'SETTINGS_BINDINGS_INVALID');
  const named = name => bindings.filter(b => b.name === name);
  const plain = name => named(name).find(b => b.type === 'plain_text')?.text;
  check(plain('APP_ENV') === 'development', 'DEVELOPMENT_ENVIRONMENT_REQUIRED');
  check(plain('API_VERSION') === '1', 'API_COMPATIBILITY_REQUIRED');
  check(named('CYID_ENABLED').length === 0 ||
    (named('CYID_ENABLED').length === 1 && plain('CYID_ENABLED') === 'false'), 'BUILT_IN_AUTHORITY_REQUIRED');
  check(bindings.filter(b => b.type === 'd1').length === 1 &&
    named('DB').length === 1 && named('DB')[0].type === 'd1' &&
    named('DB')[0].id === config.d1_databases?.[0]?.database_id, 'ORIGINAL_DATABASE_BINDING_REQUIRED');
  // Unknown resources must not be silently removed by the additive deployment.
  check(bindings.every(b => ['plain_text', 'secret_text', 'd1', 'ratelimit', 'service'].includes(b.type)),
    'UNREVIEWED_RESOURCE_BINDING');
  const secrets = new Set(bindings.filter(b => b.type === 'secret_text').map(b => b.name));
  check(['BOOTSTRAP_KEY', 'OTP_PEPPER', 'EMAIL_FROM'].every(name => secrets.has(name)), 'RUNTIME_SECRETS_REQUIRED');
  const provider = plain('EMAIL_PROVIDER') ?? 'brevo';
  check(['brevo', 'resend'].includes(provider) && secrets.has(provider === 'brevo' ? 'BREVO_API_KEY' : 'RESEND_API_KEY'),
    'EMAIL_PROVIDER_SECRET_REQUIRED');
  for (const b of bindings.filter(b => b.type === 'ratelimit')) {
    const expected = config.ratelimits?.find(r => r.name === b.name);
    check(expected && String(expected.namespace_id) === String(b.namespace_id) &&
      expected.simple.limit === b.simple?.limit && expected.simple.period === b.simple?.period,
      'RATE_LIMIT_BINDING_CHANGED');
  }
  check(bindings.filter(b => b.type === 'ratelimit').length === config.ratelimits?.length,
    'RATE_LIMIT_BINDINGS_REQUIRED');
  return Object.fromEntries(bindings.filter(b => b.type === 'plain_text').map(b => [b.name, b.text]));
}

export function checkMigrations(applied, canonical, requireComplete = false) {
  check(applied.every((name, i) => canonical[i] === name), 'MIGRATION_HISTORY_NOT_CANONICAL_PREFIX');
  const pending = canonical.slice(applied.length);
  check(pending.length === 0 || (pending.length === 1 && pending[0] === '0013_cyid_invitation_actor.sql'),
    'UNREVIEWED_PENDING_MIGRATIONS');
  check(!requireComplete || pending.length === 0, 'MIGRATION_NOT_COMPLETE');
  return pending;
}

export function fingerprintRows(rows, idColumn) {
  check(Array.isArray(rows) && rows.length <= rowLimit, 'AUDIT_ROW_LIMIT_EXCEEDED');
  const result = {};
  for (const row of rows) {
    check(typeof row[idColumn] === 'string' && row[idColumn].length > 0, 'AUDIT_ID_INVALID');
    const key = digest(row[idColumn]);
    check(!Object.hasOwn(result, key), 'AUDIT_ID_DUPLICATED');
    result[key] = digest(Object.fromEntries(Object.keys(row).sort().map(k => [k, row[k]])));
  }
  return result;
}

export function checkContinuity(before, after) {
  check(before && after, 'AUDIT_SNAPSHOT_INVALID');
  for (const table of Object.keys(tables)) {
    check(before[table] && after[table], 'AUDIT_SNAPSHOT_INVALID');
    for (const [key, hash] of Object.entries(before[table]))
      check(after[table][key] === hash, `HISTORY_CHANGED_${table.toUpperCase()}`);
  }
}

export function checkHealth(health, { version, schema }) {
  check(health?.ok === true && health.storage === 'ok' && health.service === 'cyinvoice-cloud' &&
    health.environment === 'development' && String(health.apiVersion) === '1' &&
    String(health.schemaVersion) === '8' && String(health.storageSchemaVersion) === String(schema),
    'CLOUD_HEALTH_CONTRACT_INVALID');
  if (version) check(health.cloudVersion === version, 'CLOUD_SOURCE_VERSION_MISMATCH');
  const required = ['device-revoke-v1', 'device-self-status-v1', 'device-usage-v1', 'device-rename-v1'];
  if (version) required.push('cyid-consumer-v1', 'runtime-sync-v1');
  check(required.every(name => health.capabilities?.includes(name)), 'CLOUD_CAPABILITIES_MISSING');
}

// Only these read-only SQL statements are transported. No arbitrary query input.
export async function snapshot(query) {
  const state = {};
  for (const [table, columns] of Object.entries(tables)) {
    const id = columns.split(',')[0];
    const rows = await query(`SELECT ${columns} FROM ${table} ORDER BY ${id} LIMIT ${rowLimit + 1}`);
    state[table] = fingerprintRows(rows, id);
  }
  const fk = await query('PRAGMA foreign_key_check');
  check(fk.length === 0, 'FOREIGN_KEY_CHECK_FAILED');
  return state;
}

export async function main(mode, statePath, env = process.env) {
  check(['before', 'after'].includes(mode) && statePath, 'DEPLOYMENT_AUDIT_ARGUMENTS_INVALID');
  check(env.CLOUDFLARE_API_TOKEN && env.CLOUDFLARE_ACCOUNT_ID, 'CLOUDFLARE_CREDENTIALS_REQUIRED');
  const config = JSON.parse(fs.readFileSync(path.join(root, 'wrangler.jsonc'), 'utf8'));
  const canonical = fs.readdirSync(path.join(root, 'migrations')).filter(n => /^\d{4}_.*\.sql$/.test(n)).sort();
  const expectedVersion = JSON.parse(fs.readFileSync(path.join(root, 'package.json'), 'utf8')).version;
  const sourceIdentity = digest(canonical.map(name => [name, fs.readFileSync(path.join(root, 'migrations', name), 'utf8')]));
  const api = `https://api.cloudflare.com/client/v4/accounts/${encodeURIComponent(env.CLOUDFLARE_ACCOUNT_ID)}`;
  async function request(suffix, body) {
    const response = await fetch(api + suffix, {
      method: body ? 'POST' : 'GET',
      headers: { authorization: `Bearer ${env.CLOUDFLARE_API_TOKEN}`, 'content-type': 'application/json' },
      ...(body ? { body: JSON.stringify(body) } : {}), signal: AbortSignal.timeout(30000),
    });
    const data = await response.json();
    check(response.ok && data.success === true, 'REMOTE_READ_FAILED');
    return data.result;
  }
  const settings = await request(`/workers/scripts/${encodeURIComponent(config.name)}/settings`);
  // Reuse the existing deployment's D1, not a new database or a public concrete ID.
  const databaseId = settings?.bindings?.find(b => b.name === 'DB' && b.type === 'd1')?.id;
  check(/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(databaseId ?? ''),
    'ORIGINAL_DATABASE_BINDING_REQUIRED');
  const database = await request(`/d1/database/${encodeURIComponent(databaseId)}`);
  check(database?.uuid === databaseId && database.name === config.d1_databases[0].database_name &&
    databaseId !== env.CYID_D1_DATABASE_ID, 'ORIGINAL_DEVELOPMENT_DATABASE_REQUIRED');
  const runtimeConfig = { ...config, d1_databases: [{ ...config.d1_databases[0], database_id: databaseId }] };
  const vars = checkSettings(settings, runtimeConfig);
  const databasePath = `/d1/database/${encodeURIComponent(databaseId)}`;
  async function query(sql) {
    const results = await request(databasePath + '/query', { sql });
    check(Array.isArray(results) && results.length === 1 && results[0].success === true &&
      Array.isArray(results[0].results), 'D1_READ_RESULT_INVALID');
    return results[0].results;
  }
  const applied = (await query('SELECT name FROM d1_migrations ORDER BY id')).map(r => r.name);
  const pending = checkMigrations(applied, canonical, mode === 'after');
  const state = await snapshot(query);
  const subdomain = await request('/workers/subdomain');
  check(/^[a-z0-9-]+$/.test(subdomain?.subdomain ?? ''), 'DEVELOPMENT_ENDPOINT_UNAVAILABLE');
  const origin = `https://${config.name}.${subdomain.subdomain}.workers.dev`;
  let health, healthOk = false;
  for (let attempt = 0; attempt < (mode === 'after' ? 30 : 1); attempt++) {
    const response = await fetch(origin + '/v1/health', { signal: AbortSignal.timeout(15000) });
    health = await response.json();
    healthOk = response.ok;
    if (response.ok && (mode === 'before' || health.cloudVersion === expectedVersion)) break;
    if (mode === 'after') await new Promise(resolve => setTimeout(resolve, 1500));
  }
  check(healthOk, 'CLOUD_HEALTH_HTTP_FAILED');
  checkHealth(health, { schema: applied.length, ...(mode === 'after' ? { version: expectedVersion } : {}) });
  if (mode === 'before') {
    const capturedAt = new Date().toISOString();
    const bookmark = await request(databasePath + '/time_travel/bookmark?timestamp=' + encodeURIComponent(capturedAt));
    check(typeof bookmark?.bookmark === 'string' && bookmark.bookmark.length > 0, 'RESTORE_BOOKMARK_REQUIRED');
    const deployments = await request(`/workers/scripts/${encodeURIComponent(config.name)}/deployments`);
    check(Array.isArray(deployments?.deployments) && deployments.deployments.length > 0,
      'WORKER_ROLLBACK_VERSION_REQUIRED');
    const services = settings.bindings.filter(b => b.type === 'service').map(b => ({
      binding: b.name, service: b.service, ...(b.environment ? { environment: b.environment } : {}),
      ...(b.entrypoint ? { entrypoint: b.entrypoint } : {}),
    }));
    // Preserve runtime vars/services rather than deleting them with a static template.
    const deployConfig = { ...runtimeConfig, vars: { ...vars, ...config.vars, CYID_ENABLED: 'false' }, ...(services.length ? { services } : {}) };
    const configPath = path.join(root, 'wrangler.deploy.generated.jsonc');
    fs.writeFileSync(configPath, JSON.stringify(deployConfig, null, 2), { mode: 0o600 });
    fs.writeFileSync(statePath, JSON.stringify({ sourceIdentity, databaseId, worker: config.name, state,
      bookmark, deployments, settings, origin, capturedAt }), { mode: 0o600 });
    console.log(`Private recovery checkpoint captured at ${capturedAt}; retrieve its bookmark by timestamp from the original D1 if recovery is needed. Never restore automatically.`);
    console.log(`Preflight passed: Cloud ${health.cloudVersion}; storage ${applied.length}; pending ${pending.length}; ` +
      `Workspace ${Object.keys(state.workspaces).length}; Devices ${Object.keys(state.devices).length}; ` +
      'Built-in retained; original D1 verified; FK clean; restore bookmark and prior Worker version captured privately.');
    if (env.CYID_WORKER_NAME) {
      const response = await fetch(`https://${env.CYID_WORKER_NAME}.${subdomain.subdomain}.workers.dev/v1/health`,
        { signal: AbortSignal.timeout(15000) });
      const identity = await response.json();
      check(response.ok && identity.ok === true && identity.environment === 'development', 'CYID_DEVELOPMENT_HEALTH_INVALID');
      console.log(`CYID development provider health: ${identity.serviceVersion ?? 'unknown'}; private binding and Application/Workspace readiness still require staging acceptance.`);
    }
  } else {
    const before = JSON.parse(fs.readFileSync(statePath, 'utf8'));
    check(before.worker === config.name && before.databaseId === databaseId && before.sourceIdentity === sourceIdentity,
      'AUDIT_SCOPE_MISMATCH');
    check(Date.now() - Date.parse(before.capturedAt) < 30 * 60 * 1000 &&
      Date.now() >= Date.parse(before.capturedAt), 'AUDIT_SNAPSHOT_EXPIRED');
    checkContinuity(before.state, state);
    console.log(`Deployment verified: Cloud ${expectedVersion}; API 1; marker 8; storage ${applied.length}; ` +
      'runtime-sync capability; original Workspace/Device token hashes/employee/history retained; FK clean; CYID disabled.');
  }
}

if (process.argv[1] && import.meta.url === pathToFileURL(path.resolve(process.argv[1])).href) {
  try { await main(process.argv[2], process.argv[3]); }
  catch (error) {
    // Never print remote response bodies, resource identifiers, secrets or stack traces.
    const code = /^[A-Z][A-Z0-9_]+$/.test(error?.message ?? '') ? error.message : 'DEPLOYMENT_AUDIT_FAILED';
    console.error(code); process.exitCode = 1;
  }
}
