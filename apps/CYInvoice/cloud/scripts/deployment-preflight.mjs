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
const uuid = value => /^[a-f0-9]{8}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{12}$/i.test(value ?? '');
const tables = {
  workspaces: 'workspace_id, status, employee_revision',
  devices: 'device_id, workspace_id, token_hash, status, revoked_at, employee_authority_state',
  cloud_employees: 'employee_id, workspace_id, employee_no, role, enabled, credential_version, revision',
  device_pairing_codes: 'pairing_id, workspace_id, code_hash, created_by_device_id, claimed_device_id, expires_at, used_at, created_at',
  device_invitations: 'invitation_id, workspace_id, code_hash, issued_by_device_id, issued_by_employee_id, delivery_state, expires_at, sent_at, revoked_at, consumed_at, consumed_by_device_id, created_at',
  security_audit_events: 'event_id, workspace_id, event_type, outcome, actor_device_id, actor_employee_id, target_device_id, pairing_id, invitation_id, reason_code, request_id, occurred_at',
};

// /settings follows the latest upload, which may never have served traffic.
// Deployment preservation must inspect the version actually receiving traffic.
export async function activeDeployment(request, worker) {
  const path = `/workers/scripts/${encodeURIComponent(worker)}/deployments`;
  const deployments = await request(path);
  const versions = deployments?.deployments?.[0]?.versions;
  check(Array.isArray(versions) && versions.length === 1 && versions[0].percentage === 100 &&
    uuid(versions[0].version_id), 'SINGLE_ACTIVE_DEPLOYMENT_REQUIRED');
  const version = await request(`/workers/workers/${encodeURIComponent(worker)}/versions/${versions[0].version_id}`);
  check(version?.id === versions[0].version_id && Array.isArray(version.bindings), 'ACTIVE_VERSION_BINDINGS_REQUIRED');
  const observed = await request(path);
  check(JSON.stringify(observed?.deployments?.[0]?.versions) === JSON.stringify(versions), 'ACTIVE_DEPLOYMENT_CHANGED');
  const bindings = version.bindings.map(b => b.type === 'secret_text' ? {type:b.type,name:b.name} : b);
  return { deployments, versionId: version.id, settings:{bindings} };
}

export function checkSettings(settings, config) {
  const bindings = settings?.bindings;
  check(Array.isArray(bindings), 'SETTINGS_BINDINGS_INVALID');
  check(bindings.every(b => typeof b.name === 'string' && b.name.length > 0) &&
    new Set(bindings.map(b => b.name)).size === bindings.length, 'BINDING_NAMES_INVALID');
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

// Compare only documented binding fields. Secret values never enter an audit/log.
export function bindingState(bindings) {
  check(Array.isArray(bindings), 'SETTINGS_BINDINGS_INVALID');
  return bindings.map(b => {
    if (b.type === 'secret_text') return { name:b.name, type:b.type };
    if (b.type === 'plain_text') return { name:b.name, type:b.type, text:b.text };
    if (b.type === 'd1') return { name:b.name, type:b.type, id:b.id };
    if (b.type === 'ratelimit') return { name:b.name, type:b.type,
      namespace_id:String(b.namespace_id), simple:{limit:b.simple?.limit, period:b.simple?.period} };
    if (b.type === 'service') return { name:b.name, type:b.type, service:b.service,
      environment:b.environment ?? 'production', entrypoint:b.entrypoint ?? 'default' };
    fail('UNREVIEWED_RESOURCE_BINDING');
  }).sort((a,b) => a.name.localeCompare(b.name));
}

const identityScopeNames = ['IDENTITY_APPLICATION_ID', 'IDENTITY_WORKSPACE_ID', 'IDENTITY_CYINVOICE_WORKSPACE_ID'];
const serviceBindings = bindings => bindings.filter(b => b.type === 'service');
const configServices = services => (services ?? []).map(({binding, ...b}) => ({name:binding, type:'service', ...b}));

export function readCyIdPreparation(env) {
  const names = ['CYID_WORKER_NAME', ...identityScopeNames];
  check(names.every(name => typeof env[name] === 'string' && env[name].length > 0), 'CYID_PREPARATION_SETTINGS_REQUIRED');
  check(/^[a-z0-9][a-z0-9-]{0,62}$/.test(env.CYID_WORKER_NAME) &&
    /^[A-Z0-9_-]{2,64}$/.test(env.IDENTITY_APPLICATION_ID) &&
    env.IDENTITY_WORKSPACE_ID.length >= 5 && env.IDENTITY_WORKSPACE_ID.length <= 80 &&
    identityScopeNames.every(name => env[name] === env[name].trim() && !/[\s\x00-\x1f\x7f]/.test(env[name])) &&
    env.IDENTITY_CYINVOICE_WORKSPACE_ID.length <= 128, 'CYID_PREPARATION_SETTINGS_INVALID');
  return Object.fromEntries(names.map(name => [name, env[name]]));
}

// Configuration preparation only: no CYID authority lookup, registration or switch.
export function prepareCyIdConfig(config, settings, input, workspaceFingerprints) {
  const preparation = readCyIdPreparation(input);
  check(preparation.CYID_WORKER_NAME !== config.name, 'CYID_PROVIDER_TARGET_INVALID');
  check(Object.hasOwn(workspaceFingerprints ?? {}, digest(preparation.IDENTITY_CYINVOICE_WORKSPACE_ID)),
    'CYID_ORIGINAL_WORKSPACE_REQUIRED');
  const bindings = settings.bindings;
  for (const name of identityScopeNames) {
    const existing = bindings.find(b => b.name === name);
    check(!existing || (existing.type === 'plain_text' && existing.text === preparation[name]), 'CYID_SCOPE_BINDING_CONFLICT');
  }
  const required = [
    {name:'IDENTITY', type:'service', service:preparation.CYID_WORKER_NAME},
    {name:'IDENTITY_AUTHORITY', type:'service', service:preparation.CYID_WORKER_NAME, entrypoint:'ConsumerAuthoritySync'},
  ];
  const services = serviceBindings(bindings);
  for (const target of required) {
    const existing = bindings.find(b => b.name === target.name);
    check(!existing || digest(bindingState([existing])) === digest(bindingState([target])), 'CYID_SERVICE_BINDING_CONFLICT');
    if (!existing) services.push(target);
  }
  return {...config, vars:{...config.vars, ...Object.fromEntries(identityScopeNames.map(name => [name,preparation[name]])),
    CYID_ENABLED:'false'}, services:services.map(({name,type,...b}) => ({binding:name,...b}))};
}

// One development deployment owner: modern version API supports inheritance from
// an exact active version. Legacy Wrangler upload implicitly inherits from latest.
export async function deployReviewedVersion(audit, config, code, request, save, now = Date.now()) {
  check(audit?.worker === config.name && uuid(audit.databaseId) &&
    audit.databaseId === config.d1_databases?.[0]?.database_id, 'AUDIT_SCOPE_MISMATCH');
  check(now >= Date.parse(audit.capturedAt) && now - Date.parse(audit.capturedAt) < 20*60*1000,
    'AUDIT_SNAPSHOT_EXPIRED');
  check(!audit.uploadAttempted && !audit.uploadedVersionId, 'DEPLOYMENT_ALREADY_ATTEMPTED');
  const original = audit.deployments?.deployments?.[0]?.versions;
  check(original?.length === 1 && original[0].percentage === 100 && uuid(original[0].version_id),
    'SINGLE_ACTIVE_DEPLOYMENT_REQUIRED');
  checkSettings(audit.settings, config);
  check(typeof code === 'string' && code.length > 0 && !code.includes('PROBE_AUTH_KEY'), 'BUSINESS_BUNDLE_REQUIRED');
  check(config.vars?.APP_ENV === 'development' && config.vars?.API_VERSION === '1' &&
    config.vars?.CYID_ENABLED !== 'true', 'BUILT_IN_AUTHORITY_REQUIRED');
  const vars = { ...Object.fromEntries(audit.settings.bindings.filter(b => b.type === 'plain_text').map(b => [b.name,b.text])),
    ...config.vars, CYID_ENABLED:'false' };
  const preserved = audit.settings.bindings.filter(b => b.type !== 'plain_text');
  const reviewed = audit.cyIdPreparation
    ? prepareCyIdConfig(config, audit.settings, audit.cyIdPreparation, audit.state?.workspaces) : config;
  const expectedServices = audit.cyIdPreparation ? configServices(reviewed.services) : serviceBindings(preserved);
  check(digest(bindingState(configServices(config.services))) === digest(bindingState(expectedServices)), 'UNREVIEWED_SERVICE_BINDING_CHANGE');
  if (audit.cyIdPreparation)
    check(identityScopeNames.every(name => vars[name] === reviewed.vars[name]), 'CYID_SCOPE_BINDING_CONFLICT');
  else
    check(identityScopeNames.every(name => vars[name] === audit.settings.bindings.find(b => b.name === name && b.type === 'plain_text')?.text),
      'CYID_SCOPE_PREPARATION_REQUIRED');
  const additions = expectedServices.filter(b => !preserved.some(existing => existing.name === b.name));
  check(!Object.keys(vars).some(name => preserved.some(b => b.name === name)), 'BINDING_NAMES_INVALID');
  const expected = [...preserved, ...additions, ...Object.entries(vars).map(([name,text]) => ({type:'plain_text',name,text}))];
  checkSettings({bindings:expected}, config);
  const prefix = `/workers/workers/${encodeURIComponent(config.name)}/versions`;
  const routing = `/workers/scripts/${encodeURIComponent(config.name)}`;
  async function unchanged() {
    const active = await activeDeployment(request, config.name);
    check(JSON.stringify(active.deployments.deployments[0].versions) === JSON.stringify(original) &&
      digest(bindingState(active.settings.bindings)) === digest(bindingState(audit.settings.bindings)),
      'ACTIVE_DEPLOYMENT_CHANGED');
    check(digest(await request(routing+'/routes')) === digest(audit.routes) &&
      digest(await request(routing+'/subdomain')) === digest(audit.workerSubdomain), 'WORKER_ROUTES_CHANGED');
  }
  await unchanged();
  audit.uploadAttempted = true;
  save(audit);
  const created = await request(prefix+'?deploy=false', {
    main_module:'app.js', compatibility_date:config.compatibility_date,
    compatibility_flags:config.compatibility_flags ?? [],
    bindings:[...preserved.map(b => ({name:b.name,type:'inherit',version_id:original[0].version_id})),
      ...additions,
      ...expected.filter(b => b.type === 'plain_text')],
    modules:[{name:'app.js',content_type:'application/javascript+module',content_base64:Buffer.from(code).toString('base64')}],
    annotations:{'workers/message':'CYInvoice reviewed development business code; Built-in retained'},
  });
  check(uuid(created?.id) && created.id !== original[0].version_id, 'UPLOADED_VERSION_ID_INVALID');
  // Save intent before deployment: a lost response is never retried automatically.
  audit.uploadedVersionId = created.id;
  audit.expectedBindings = bindingState(expected);
  save(audit);
  const uploaded = await request(prefix+'/'+created.id);
  check(uploaded?.id === created.id && uploaded.compatibility_date === config.compatibility_date &&
    digest(uploaded.compatibility_flags ?? []) === digest(config.compatibility_flags ?? []) &&
    digest(bindingState(uploaded.bindings)) === digest(audit.expectedBindings), 'UPLOADED_BINDINGS_MISMATCH');
  console.log('Reviewed business version readback passed: exact active-version inheritance, original D1/services/rate limits/Secret names and Built-in vars. No preview/test invocation.');
  if (audit.cyIdPreparation)
    console.log('CYID configuration preparation readback passed: both private service targets and three approved scope vars. Runtime transport and authority acceptance remain pending.');
  await unchanged();
  audit.deploymentRequested = true;
  save(audit);
  await request(routing+'/deployments', {strategy:'percentage',versions:[{version_id:created.id,percentage:100}]});
  const active = await activeDeployment(request, config.name);
  check(active.versionId === created.id && digest(bindingState(active.settings.bindings)) === digest(audit.expectedBindings),
    'DEPLOYED_VERSION_MISMATCH');
  console.log('Reviewed development business code deployed at 100% with CYID disabled. Expired test history retained; no cleanup retry or Secret replacement.');
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

export async function main(mode, statePath, env = process.env, bundlePath) {
  check(['before', 'after', 'deploy'].includes(mode) && statePath, 'DEPLOYMENT_AUDIT_ARGUMENTS_INVALID');
  check(env.CLOUDFLARE_API_TOKEN && env.CLOUDFLARE_ACCOUNT_ID, 'CLOUDFLARE_CREDENTIALS_REQUIRED');
  const operation = env.CYINVOICE_DEPLOYMENT_MODE ?? 'preflight';
  check(['preflight','deploy','prepare-cyid'].includes(operation), 'DEPLOYMENT_MODE_INVALID');
  const cyIdPreparation = mode === 'before' && operation === 'prepare-cyid' ? readCyIdPreparation(env) : undefined;
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
    const apiCode = Number.isSafeInteger(data.errors?.[0]?.code) ? data.errors[0].code : 0;
    check(response.ok && data.success === true, `REMOTE_API_HTTP_${response.status}_CODE_${apiCode}`);
    return data.result;
  }
  if (mode === 'deploy') {
    check(bundlePath && path.basename(bundlePath) === 'app.js', 'BUSINESS_BUNDLE_REQUIRED');
    check(fs.readdirSync(path.dirname(bundlePath)).filter(name => /\.(?:m?js|wasm)$/.test(name)).join(',') === 'app.js',
      'BUSINESS_BUNDLE_MODULES_INVALID');
    const audit = JSON.parse(fs.readFileSync(statePath, 'utf8'));
    check(audit.operation === operation && operation !== 'preflight', 'AUDIT_DEPLOYMENT_MODE_MISMATCH');
    check(Boolean(audit.cyIdPreparation) === (operation === 'prepare-cyid'), 'AUDIT_DEPLOYMENT_MODE_MISMATCH');
    check(operation !== 'prepare-cyid' || digest(readCyIdPreparation(env)) === digest(audit.cyIdPreparation), 'AUDIT_SCOPE_MISMATCH');
    check(audit.sourceIdentity === sourceIdentity, 'AUDIT_SCOPE_MISMATCH');
    check(audit.worker === config.name && uuid(audit.databaseId) && audit.databaseId !== env.CYID_D1_DATABASE_ID, 'AUDIT_SCOPE_MISMATCH');
    const deployConfig = JSON.parse(fs.readFileSync(path.join(root, 'wrangler.deploy.generated.jsonc'), 'utf8'));
    // Real deployments require the original D1's complete reviewed schema.
    const results = await request(`/d1/database/${encodeURIComponent(audit.databaseId)}/query`,
      {sql:'SELECT name FROM d1_migrations ORDER BY id'});
    check(Array.isArray(results) && results.length === 1 && results[0].success === true, 'D1_READ_RESULT_INVALID');
    checkMigrations(results[0].results.map(r => r.name), canonical, true);
    await deployReviewedVersion(audit, deployConfig, fs.readFileSync(bundlePath, 'utf8'), request,
      value => fs.writeFileSync(statePath, JSON.stringify(value), {mode:0o600}));
    return;
  }
  const {settings, deployments, versionId} = await activeDeployment(request, config.name);
  const routing = `/workers/scripts/${encodeURIComponent(config.name)}`;
  const routes = await request(routing+'/routes');
  const workerSubdomain = await request(routing+'/subdomain');
  console.log('Active business deployment read; latest uploaded version is not used as runtime configuration.');
  const uploaded = await request(`/workers/workers/${encodeURIComponent(config.name)}/versions?per_page=100`);
  check(Array.isArray(uploaded), 'UPLOADED_VERSION_READ_REQUIRED');
  const probes = uploaded.filter(v => /^CYID_BINDING_PROBE_[a-f0-9]{32}$/.test(v.annotations?.['workers/message'] ?? ''));
  check(probes.length <= 10, 'UNREVIEWED_PROBE_VERSION_COUNT');
  let expired = 0;
  for (const probe of probes) {
    check(uuid(probe.id) && !deployments.deployments[0].versions.some(v => v.version_id === probe.id), 'PROBE_VERSION_ACTIVE');
    const version = await request(`/workers/workers/${encodeURIComponent(config.name)}/versions/${probe.id}`);
    const bindings = version?.bindings ?? [];
    const plain = name => bindings.find(b => b.type === 'plain_text' && b.name === name)?.text;
    const services = bindings.filter(b => b.type === 'service');
    const expires = Number(plain('PROBE_EXPIRES_AT'));
    check(version?.id === probe.id && version.annotations?.['workers/message'] === probe.annotations['workers/message'] &&
      probe.annotations['workers/message'] === `CYID_BINDING_PROBE_${plain('PROBE_OWNER_NONCE')}` &&
      bindings.every(b => ['plain_text','secret_text','service'].includes(b.type)) && services.length === 2 && env.CYID_WORKER_NAME &&
      services.every(b => b.service === env.CYID_WORKER_NAME) &&
      services.some(b => b.name === 'IDENTITY' && (!b.entrypoint || b.entrypoint === 'default')) &&
      services.some(b => b.name === 'IDENTITY_AUTHORITY' && b.entrypoint === 'ConsumerAuthoritySync') &&
      bindings.filter(b => b.type === 'secret_text').every(b => b.name === 'PROBE_AUTH_KEY') &&
      Number.isSafeInteger(expires) && expires > 0, 'RETAINED_PROBE_SCOPE_INVALID');
    check(expires <= Date.now(), 'RETAINED_PROBE_NOT_EXPIRED');
    expired++;
  }
  console.log(`Retained probe versions: ${probes.length}; expired: ${expired}; none deployed or bound to a D1. No version mutation performed by this audit.`);
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
  async function query(sql, params) {
    const results = await request(databasePath + '/query', { sql, ...(params ? {params} : {}) });
    check(Array.isArray(results) && results.length === 1 && results[0].success === true &&
      Array.isArray(results[0].results), 'D1_READ_RESULT_INVALID');
    return results[0].results;
  }
  const applied = (await query('SELECT name FROM d1_migrations ORDER BY id')).map(r => r.name);
  const pending = checkMigrations(applied, canonical, mode === 'after' || Boolean(cyIdPreparation));
  const state = await snapshot(query);
  if (cyIdPreparation) {
    const workspace = await query('SELECT workspace_id, status FROM workspaces WHERE workspace_id = ?',
      [cyIdPreparation.IDENTITY_CYINVOICE_WORKSPACE_ID]);
    check(workspace.length === 1 && workspace[0].status === 'active', 'CYID_ORIGINAL_WORKSPACE_REQUIRED');
  }
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
    const services = settings.bindings.filter(b => b.type === 'service').map(b => ({
      binding: b.name, service: b.service, ...(b.environment ? { environment: b.environment } : {}),
      ...(b.entrypoint ? { entrypoint: b.entrypoint } : {}),
    }));
    // Preserve runtime vars/services rather than deleting them with a static template.
    let deployConfig = { ...runtimeConfig, vars: { ...vars, ...config.vars, CYID_ENABLED: 'false' }, ...(services.length ? { services } : {}) };
    if (cyIdPreparation) deployConfig = prepareCyIdConfig(deployConfig, settings, cyIdPreparation, state.workspaces);
    const configPath = path.join(root, 'wrangler.deploy.generated.jsonc');
    fs.writeFileSync(configPath, JSON.stringify(deployConfig, null, 2), { mode: 0o600 });
    fs.writeFileSync(statePath, JSON.stringify({ sourceIdentity, databaseId, worker: config.name, state, retainedProbeCount:probes.length,
      bookmark, deployments, settings, routes, workerSubdomain, origin, capturedAt, operation,
      ...(cyIdPreparation ? {cyIdPreparation} : {}) }), { mode: 0o600 });
    console.log(`Private recovery checkpoint captured at ${capturedAt}; retrieve its bookmark by timestamp from the original D1 if recovery is needed. Never restore automatically.`);
    console.log(`Preflight passed: Cloud ${health.cloudVersion}; storage ${applied.length}; pending ${pending.length}; ` +
      `Workspace ${Object.keys(state.workspaces).length}; Devices ${Object.keys(state.devices).length}; ` +
      'Built-in retained; original D1 verified; FK clean; restore bookmark and prior Worker version captured privately.');
    if (env.CYID_WORKER_NAME) {
      const response = await fetch(`https://${env.CYID_WORKER_NAME}.${subdomain.subdomain}.workers.dev/v1/health`,
        { signal: AbortSignal.timeout(15000) });
      const identity = await response.json();
      check(response.ok && identity.ok === true && identity.service === 'cycloud-identity' &&
        identity.environment === 'development' && identity.status === 'ok' && identity.identity === 'ready',
        'CYID_DEVELOPMENT_HEALTH_INVALID');
      console.log(`CYID development provider health: ${identity.serviceVersion ?? 'unknown'}; private binding and Application/Workspace readiness still require staging acceptance.`);
    }
  } else {
    const before = JSON.parse(fs.readFileSync(statePath, 'utf8'));
    check(before.worker === config.name && before.databaseId === databaseId && before.sourceIdentity === sourceIdentity,
      'AUDIT_SCOPE_MISMATCH');
    check(Date.now() - Date.parse(before.capturedAt) < 30 * 60 * 1000 &&
      Date.now() >= Date.parse(before.capturedAt), 'AUDIT_SNAPSHOT_EXPIRED');
    check(digest(routes) === digest(before.routes) && digest(workerSubdomain) === digest(before.workerSubdomain),
      'WORKER_ROUTES_CHANGED');
    checkContinuity(before.state, state);
    if (before.uploadedVersionId && versionId === before.uploadedVersionId) {
      check(before.deploymentRequested && digest(bindingState(settings.bindings)) === digest(before.expectedBindings),
        'DEPLOYED_VERSION_MISMATCH');
      console.log('Live version is the reviewed business upload; exact inherited binding readback passed.');
    } else {
      check(JSON.stringify(deployments.deployments[0].versions) === JSON.stringify(before.deployments.deployments[0].versions) &&
        digest(bindingState(settings.bindings)) === digest(bindingState(before.settings.bindings)), 'ACTIVE_DEPLOYMENT_CHANGED');
      console.log('Original active deployment and bindings retained; no unreviewed version promoted.');
    }
    console.log(`Deployment verified: Cloud ${expectedVersion}; API 1; marker 8; storage ${applied.length}; ` +
      'runtime-sync capability; original Workspace/Device token hashes/employee/history retained; FK clean; CYID disabled.');
  }
}

if (process.argv[1] && import.meta.url === pathToFileURL(path.resolve(process.argv[1])).href) {
  try {
    check(process.argv.length === (process.argv[2] === 'deploy' ? 5 : 4), 'DEPLOYMENT_AUDIT_ARGUMENTS_INVALID');
    await main(process.argv[2], process.argv[3], process.env, process.argv[4]);
  }
  catch (error) {
    // Never print remote response bodies, resource identifiers, secrets or stack traces.
    const code = /^[A-Z][A-Z0-9_]+$/.test(error?.message ?? '') ? error.message : 'DEPLOYMENT_AUDIT_FAILED';
    console.error(code); process.exitCode = 1;
  }
}
