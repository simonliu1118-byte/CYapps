// Provider-owned deployment audit. Consumers never read Identity D1 through this tool.
import fs from 'node:fs';
import path from 'node:path';
import { createHash, randomBytes } from 'node:crypto';
import { pathToFileURL } from 'node:url';

const root = path.resolve(import.meta.dirname, '..');
const requireValue = (condition, code) => { if (!condition) throw new Error(code); };
const stable = value => Array.isArray(value) ? value.map(stable) : value && typeof value === 'object'
  ? Object.fromEntries(Object.keys(value).sort().map(key => [key, stable(value[key])])) : value;
const digest = value => createHash('sha256').update(JSON.stringify(stable(value))).digest('hex');
const tables = {
  workspaces: ['workspace_id', 'workspace_id, status, super_admin_employee_id, revision, recovery_email_verified_at'],
  employees: ['employee_id', 'employee_id, workspace_id, employee_no, enabled, revision, role_key, identity_admin, activated_at, email_verified_at'],
  employee_credentials: ['employee_id', 'employee_id, algorithm, credential_version, updated_at'],
  employee_initial_credentials: ['employee_id', 'employee_id, algorithm, issued_at, expires_at, verified_at, revision'],
  applications: ['application_id', 'application_id, status'],
  workspace_applications: ['workspace_id, application_id', 'workspace_id, application_id, enabled'],
  employee_application_access: ['workspace_id, employee_id, application_id', 'workspace_id, employee_id, application_id, enabled'],
  identity_security_policies: ['workspace_id', 'workspace_id, otp_resend_cooldown_seconds, otp_max_attempts, otp_max_sent_per_email_purpose_hour, email_daily_limit, revision'],
};
const historyTables = ['identity_sessions', 'identity_audit_events', 'email_otp_challenges', 'bootstrap_requests'];

export function buildDevelopmentConfig(settings, database, workerName, databaseId) {
  requireValue(/^[a-z0-9][a-z0-9-]{1,62}$/.test(workerName ?? ''), 'INVALID_DEVELOPMENT_WORKER');
  const bindings = settings?.bindings;
  requireValue(Array.isArray(bindings) && bindings.every(b => ['plain_text', 'secret_text', 'd1', 'ratelimit', 'service'].includes(b.type)), 'UNREVIEWED_BINDING');
  requireValue(new Set(bindings.map(b => b.name)).size === bindings.length, 'DUPLICATE_BINDING');
  const vars = Object.fromEntries(bindings.filter(b => b.type === 'plain_text').map(b => [b.name, b.text]));
  requireValue(vars.APP_ENV === 'development' && vars.API_VERSION === 'v1', 'DEVELOPMENT_ENVIRONMENT_REQUIRED');
  requireValue(/^\d+$/.test(vars.SESSION_TTL_SECONDS ?? '') && Number(vars.SESSION_TTL_SECONDS) >= 900 && Number(vars.SESSION_TTL_SECONDS) <= 86400, 'INVALID_SESSION_TTL');
  requireValue(/^\d+$/.test(vars.EMAIL_DAILY_BUDGET ?? '') && Number(vars.EMAIL_DAILY_BUDGET) >= 1 && Number(vars.EMAIL_DAILY_BUDGET) <= 10000, 'INVALID_EMAIL_BUDGET');
  requireValue(['brevo', 'resend'].includes(vars.EMAIL_PROVIDER) && typeof vars.EMAIL_FROM === 'string' && vars.EMAIL_FROM.trim().length > 0 && !/[\r\n]/.test(vars.EMAIL_FROM), 'INVALID_EMAIL_CONFIG');
  requireValue(/^[A-Z0-9_-]{2,64}$/.test(vars.CORE_ACCOUNT_APPLICATION_ID ?? '') && /^https:\/\//.test(vars.ACCOUNT_PORTAL_URL ?? ''), 'INVALID_CORE_APPLICATION_CONFIG');
  requireValue(new URL(vars.ACCOUNT_PORTAL_URL).protocol === 'https:', 'INVALID_CORE_APPLICATION_CONFIG');
  const databases = bindings.filter(b => b.type === 'd1');
  requireValue(databases.length === 1 && databases[0].name === 'DB' && databases[0].id === databaseId &&
    /^[0-9a-f]{8}-[0-9a-f]{4}-[1-5][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i.test(databaseId ?? '') &&
    database?.uuid === databaseId && /^[A-Za-z0-9_-]{1,64}$/.test(database?.name ?? ''), 'ORIGINAL_DEVELOPMENT_DATABASE_REQUIRED');
  const secrets = bindings.filter(b => b.type === 'secret_text').map(b => b.name).sort();
  requireValue(['OTP_PEPPER', 'BOOTSTRAP_SECRET', vars.EMAIL_PROVIDER === 'brevo' ? 'BREVO_API_KEY' : 'RESEND_API_KEY'].every(name => secrets.includes(name)), 'RUNTIME_SECRETS_REQUIRED');
  requireValue(!['OTP_PEPPER', 'BOOTSTRAP_SECRET', 'BREVO_API_KEY', 'RESEND_API_KEY'].some(name => Object.hasOwn(vars, name)), 'PLAINTEXT_SECRET_BINDING');
  const rates = bindings.filter(b => b.type === 'ratelimit');
  requireValue(rates.some(b => b.name === 'LOGIN_RATE_LIMITER') && rates.every(b => /^\d{1,20}$/.test(String(b.namespace_id)) &&
    Number.isSafeInteger(b.simple?.limit) && b.simple.limit > 0 && [10, 60].includes(b.simple?.period)), 'LOGIN_RATE_LIMIT_REQUIRED');
  return {
    name: workerName, main: 'src/index.ts', compatibility_date: '2026-09-25', workers_dev: true,
    compatibility_flags: [...new Set([...(settings.compatibility_flags ?? []), 'nodejs_compat'])].sort(),
    d1_databases: [{ binding: 'DB', database_id: databaseId, database_name: database.name, migrations_dir: 'migrations' }],
    vars, secrets: { required: secrets },
    ratelimits: rates.map(b => ({ name: b.name, namespace_id: String(b.namespace_id), simple: b.simple })).sort((a, b) => a.name.localeCompare(b.name)),
    services: bindings.filter(b => b.type === 'service').map(b => ({ binding: b.name, service: b.service,
      ...(b.environment ? { environment: b.environment } : {}), ...(b.entrypoint ? { entrypoint: b.entrypoint } : {}) })).sort((a, b) => a.binding.localeCompare(b.binding)),
  };
}

export function checkMigrations(applied, canonical) {
  // This release adds a private entrypoint only; it must never replay authority migrations.
  requireValue(Array.isArray(applied) && applied.length === canonical.length && applied.every((name, i) => name === canonical[i]), 'UNREVIEWED_MIGRATION_STATE');
}

export async function snapshot(query) {
  const state = { authority: {}, history: {} };
  for (const [table, [keys, columns]] of Object.entries(tables)) {
    const rows = await query(`SELECT ${columns} FROM ${table} ORDER BY ${keys} LIMIT 10001`);
    requireValue(Array.isArray(rows) && rows.length <= 10000, 'AUDIT_ROW_LIMIT_EXCEEDED');
    state.authority[table] = {};
    for (const row of rows) {
      const id = digest(keys.split(', ').map(key => row[key]));
      requireValue(keys.split(', ').every(key => typeof row[key] === 'string' && row[key].length > 0) && !Object.hasOwn(state.authority[table], id), 'AUDIT_ROW_KEY_INVALID');
      state.authority[table][id] = digest(row);
    }
  }
  for (const table of historyTables) {
    const rows = await query(`SELECT count(*) AS total FROM ${table}`);
    requireValue(rows.length === 1 && Number.isSafeInteger(rows[0].total) && rows[0].total >= 0, 'AUDIT_COUNT_INVALID');
    state.history[table] = rows[0].total;
  }
  requireValue((await query('PRAGMA foreign_key_check')).length === 0, 'FOREIGN_KEY_CHECK_FAILED');
  return state;
}

export function checkContinuity(before, after) {
  for (const table of Object.keys(tables)) {
    requireValue(before?.authority?.[table] && after?.authority?.[table] && digest(before.authority[table]) === digest(after.authority[table]), 'AUTHORITY_CHANGED_DURING_UPGRADE');
  }
  for (const table of historyTables)
    requireValue(Number.isSafeInteger(before?.history?.[table]) && after?.history?.[table] >= before.history[table], 'IDENTITY_HISTORY_LOST');
}

export async function main(mode, statePath, env = process.env, fetcher = fetch) {
  requireValue(['before', 'after'].includes(mode) && statePath, 'INVALID_AUDIT_ARGUMENTS');
  requireValue(env.CLOUDFLARE_ACCOUNT_ID && env.CLOUDFLARE_API_TOKEN && env.CYID_WORKER_NAME && env.CYID_D1_DATABASE_ID, 'DEVELOPMENT_INPUTS_REQUIRED');
  const worker = encodeURIComponent(env.CYID_WORKER_NAME);
  const dbId = encodeURIComponent(env.CYID_D1_DATABASE_ID);
  const api = `https://api.cloudflare.com/client/v4/accounts/${encodeURIComponent(env.CLOUDFLARE_ACCOUNT_ID)}`;
  async function read(suffix, body) {
    const response = await fetcher(api + suffix, { method: body ? 'POST' : 'GET',
      headers: { authorization: `Bearer ${env.CLOUDFLARE_API_TOKEN}`, 'content-type': 'application/json' },
      ...(body ? { body: JSON.stringify(body) } : {}), signal: AbortSignal.timeout(30000) });
    const result = await response.json();
    requireValue(response.ok && result.success === true, 'REMOTE_READ_FAILED');
    return result.result;
  }
  async function query(sql) {
    const result = await read(`/d1/database/${dbId}/query`, { sql });
    requireValue(Array.isArray(result) && result.length === 1 && result[0].success === true && Array.isArray(result[0].results), 'D1_READ_RESULT_INVALID');
    return result[0].results;
  }
  const settings = await read(`/workers/scripts/${worker}/settings`);
  const database = await read(`/d1/database/${dbId}`);
  const config = buildDevelopmentConfig(settings, database, env.CYID_WORKER_NAME, env.CYID_D1_DATABASE_ID);
  const canonical = fs.readdirSync(path.join(root, 'migrations')).filter(name => /^\d{4}_.*\.sql$/.test(name)).sort();
  const applied = (await query('SELECT name FROM d1_migrations ORDER BY id')).map(row => row.name);
  checkMigrations(applied, canonical);
  const state = await snapshot(query);
  const expected = fs.readFileSync(path.join(root, 'VERSION'), 'utf8').trim();
  const contract = fs.readFileSync(path.join(root, 'CONSUMER_CONTRACT_VERSION'), 'utf8').trim();
  const minimum = fs.readFileSync(path.join(root, 'CONSUMER_MIN_COMPATIBLE_VERSION'), 'utf8').trim();
  const sourceIdentity = digest([expected, contract, minimum,
    canonical.map(name => [name, fs.readFileSync(path.join(root, 'migrations', name), 'utf8')]),
    fs.readdirSync(path.join(root, 'src'), { recursive: true }).filter(name => name.endsWith('.ts')).sort()
      .map(name => [name, fs.readFileSync(path.join(root, 'src', name), 'utf8')])]);
  const subdomain = await read('/workers/subdomain');
  requireValue(/^[a-z0-9-]+$/.test(subdomain?.subdomain ?? ''), 'DEVELOPMENT_ENDPOINT_UNAVAILABLE');
  const origin = `https://${env.CYID_WORKER_NAME}.${subdomain.subdomain}.workers.dev`;
  let health;
  for (let attempt = 0; attempt < (mode === 'after' ? 20 : 1); attempt++) {
    const response = await fetcher(origin + '/v1/health', { signal: AbortSignal.timeout(15000) });
    health = await response.json();
    requireValue(response.ok && health.ok === true && health.service === 'cycloud-identity' && health.environment === 'development' && health.apiVersion === 'v1', 'PROVIDER_HEALTH_INVALID');
    if (mode === 'before' || health.serviceVersion === expected) break;
    await new Promise(resolve => setTimeout(resolve, 1500));
  }
  if (mode === 'before') {
    const capturedAt = new Date().toISOString();
    const bookmark = await read(`/d1/database/${dbId}/time_travel/bookmark?timestamp=${encodeURIComponent(capturedAt)}`);
    const deployments = await read(`/workers/scripts/${worker}/deployments`);
    requireValue(typeof bookmark?.bookmark === 'string' && bookmark.bookmark.length > 0 && deployments?.deployments?.length > 0, 'RECOVERY_CHECKPOINT_REQUIRED');
    fs.writeFileSync(path.join(root, 'wrangler.deploy.generated.jsonc'), JSON.stringify(config, null, 2), { mode: 0o600 });
    fs.writeFileSync(statePath, JSON.stringify({ capturedAt, sourceIdentity, config, state, bookmark, deployments, origin }), { mode: 0o600 });
    console.log(`Development preflight passed: provider ${health.serviceVersion}; ${applied.length} applied migrations; no pending migrations; runtime settings/secrets preserved; FK clean.`);
    console.log(`Private recovery checkpoint captured at ${capturedAt}; no automatic restore, bootstrap, secret replacement or authority switch.`);
  } else {
    const before = JSON.parse(fs.readFileSync(statePath, 'utf8'));
    requireValue(before.sourceIdentity === sourceIdentity && digest(before.config) === digest(config) && before.origin === origin, 'AUDIT_SCOPE_OR_RUNTIME_CONFIG_CHANGED');
    requireValue(Date.now() >= Date.parse(before.capturedAt) && Date.now() - Date.parse(before.capturedAt) < 30 * 60 * 1000, 'AUDIT_SNAPSHOT_EXPIRED');
    requireValue(health.serviceVersion === expected, 'PROVIDER_VERSION_NOT_READY');
    checkContinuity(before.state, state);
    const response = await fetcher(origin + '/v1/identity/session/resolve', { method: 'POST',
      headers: { authorization: `Bearer cyid_${randomBytes(32).toString('hex')}`, 'x-identity-application': config.vars.CORE_ACCOUNT_APPLICATION_ID }, signal: AbortSignal.timeout(15000) });
    const denied = await response.json();
    requireValue(response.status === 401 && denied.error?.code === 'SESSION_INVALID', 'INVALID_SESSION_NOT_REJECTED');
    console.log(`Development upgrade verified: provider ${expected}; canonical contract ${contract}/minimum ${minimum}; original authority metadata and history counts preserved; unchanged runtime settings/secrets; invalid Session rejected. Private RPC and original-device acceptance remain separate gates.`);
  }
}

if (process.argv[1] && import.meta.url === pathToFileURL(path.resolve(process.argv[1])).href) {
  try { await main(process.argv[2], process.argv[3]); }
  catch (error) {
    console.error(/^[A-Z][A-Z0-9_]+$/.test(error?.message ?? '') ? error.message : 'DEPLOYMENT_AUDIT_FAILED');
    process.exitCode = 1;
  }
}
