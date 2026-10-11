import fs from 'node:fs';
import path from 'node:path';
import { randomBytes, createHash } from 'node:crypto';
import { execFile } from 'node:child_process';
import { promisify } from 'node:util';
import { pathToFileURL } from 'node:url';
import { activeDeployment } from '../../CYInvoice/cloud/scripts/deployment-preflight.mjs';
const root = path.resolve(import.meta.dirname, '..');
const requireValue = (condition, code) => { if (!condition) throw new Error(code); };
const run = promisify(execFile);
// Fixed projection only: API bodies/headers may contain credentials, IDs and names.
export async function platformResponse(response) {
  const text = await response.text();
  let result, body = 'TEXT';
  try { result = JSON.parse(text); body = 'JSON'; }
  catch { if (/^\s*(?:<!doctype html|<html)/i.test(text)) body = 'HTML'; }
  const errors = Array.isArray(result?.errors) ? result.errors : [];
  const code = errors.find(e => Number.isSafeInteger(e?.code) && e.code >= 0 && e.code <= 999999)?.code;
  const diagnostic = `HTTP_${response.status}_CODE_${code ?? 'UNSPECIFIED'}_BODY_${body}` +
    `_ENVELOPE_${Number(typeof result?.success === 'boolean')}_CF_RAY_${Number(response.headers.has('cf-ray'))}` +
    `_CF_SERVER_${Number(response.headers.get('server')?.toLowerCase() === 'cloudflare')}` +
    `_PROXY_DENIAL_${Number(response.headers.has('x-mitmproxy-blocked-reason'))}` +
    `_CF_HTML_${Number(body === 'HTML' && /cloudflare/i.test(text))}` +
    `_ERROR_DOC_${Number(errors.some(e => typeof e?.documentation_url === 'string'))}`;
  return { accepted: response.ok && result?.success === true, result: result?.result, diagnostic };
}
const stable = value => JSON.stringify(value, (_, item) => item && typeof item === 'object' && !Array.isArray(item)
  ? Object.fromEntries(Object.entries(item).sort(([a], [b]) => a.localeCompare(b))) : item);
const versionUuid = value => /^[a-f0-9]{8}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{12}$/i.test(value ?? '');
export async function main(env = process.env, fetcher = fetch, command = run, originalConfig, consumerAudit, cleanupOnly = false) {
  requireValue(env.CLOUDFLARE_ACCOUNT_ID && env.CLOUDFLARE_API_TOKEN && env.CYID_WORKER_NAME && env.RUNNER_TEMP, 'PROBE_INPUTS_REQUIRED');
  requireValue(!cleanupOnly || consumerAudit, 'PROBE_CLEANUP_AUDIT_REQUIRED');
  let original = originalConfig ?? (consumerAudit ? undefined : JSON.parse(fs.readFileSync(path.join(root, 'wrangler.deploy.generated.jsonc'), 'utf8')));
  const nonce = randomBytes(16).toString('hex');
  const worker = consumerAudit?.worker ?? `cyid-binding-probe-${nonce}`;
  const key = randomBytes(32).toString('hex');
  const api = `https://api.cloudflare.com/client/v4/accounts/${encodeURIComponent(env.CLOUDFLARE_ACCOUNT_ID)}`;
  const configPath = path.join(env.RUNNER_TEMP, `cyid-binding-${nonce}.jsonc`);
  const bundlePath = path.join(env.RUNNER_TEMP, `cyid-binding-bundle-${nonce}`);
  const platformCall = async (suffix, method = 'GET', body, stage = 'SCOPE_READ', user = false) => {
    const multipart = body instanceof FormData;
    let response;
    try {
      response = await fetcher((user ? 'https://api.cloudflare.com/client/v4/user' : api) + suffix, { method,
        headers: { authorization: `Bearer ${env.CLOUDFLARE_API_TOKEN}`, ...(multipart ? {} : { 'content-type': 'application/json' }) },
        ...(body ? { body: multipart ? body : JSON.stringify(body) } : {}), signal: AbortSignal.timeout(30000) });
    } catch {
      console.log(`Probe platform diagnostic: ${stage}_TRANSPORT_UNAVAILABLE`);
      return { accepted: false, diagnostic: 'TRANSPORT_UNAVAILABLE' };
    }
    if (response.status === 404 && method === 'GET') return { absent: true };
    let result;
    try { result = await platformResponse(response); }
    catch { result = { accepted: false, diagnostic: 'RESPONSE_UNREADABLE' }; }
    if (!result.accepted) console.log(`Probe platform diagnostic: ${stage}_${result.diagnostic}`);
    return result;
  };
  const apiCall = async (suffix, method = 'GET', body, stage) => {
    const result = await platformCall(suffix, method, body, stage);
    requireValue(result.absent || result.accepted, `PROBE_PLATFORM_${result.diagnostic}`);
    return result;
  };
  if (!original && consumerAudit) {
    const provider = (await apiCall(`/workers/scripts/${encodeURIComponent(env.CYID_WORKER_NAME)}/settings`)).result;
    original = { name: env.CYID_WORKER_NAME, vars: Object.fromEntries((provider?.bindings ?? [])
      .filter(b => b.type === 'plain_text').map(b => [b.name, b.text])) };
  }
  requireValue(original?.name === env.CYID_WORKER_NAME && original.vars?.APP_ENV === 'development' && original.vars.CORE_ACCOUNT_APPLICATION_ID, 'PROBE_DEVELOPMENT_SCOPE_REQUIRED');
  const prefix = `/workers/scripts/${worker}`;
  const versionPrefix = `/workers/workers/${worker}/versions`;
  let deployments, originalBindings, originalRoute, retained;
  if (consumerAudit) {
    const consumer = JSON.parse(fs.readFileSync(path.join(root, '../CYInvoice/cloud/wrangler.jsonc'), 'utf8'));
    const age = Date.now() - Date.parse(consumerAudit.capturedAt);
    originalBindings = consumerAudit.settings?.bindings;
    const plain = name => originalBindings?.find(b => b.name === name && b.type === 'plain_text')?.text;
    requireValue(worker === consumer.name && worker !== original.name && age >= 0 && age < 20 * 60 * 1000 &&
      plain('APP_ENV') === 'development' && [undefined, 'false'].includes(plain('CYID_ENABLED')) &&
      originalBindings?.filter(b => b.type === 'd1').length === 1 &&
      originalBindings.find(b => b.name === 'DB' && b.type === 'd1')?.id === consumerAudit.databaseId &&
      consumerAudit.databaseId !== env.CYID_D1_DATABASE_ID, 'PROBE_ORIGINAL_CONSUMER_SCOPE_REQUIRED');
    const active = await activeDeployment(async suffix => (await apiCall(suffix)).result, worker);
    requireValue(stable(active.settings.bindings) === stable(originalBindings), 'PROBE_CONSUMER_SETTINGS_CHANGED');
    deployments = active.deployments.deployments[0].versions;
    requireValue(Array.isArray(deployments) && deployments.length > 0 &&
      stable(deployments) === stable(consumerAudit.deployments?.deployments?.[0]?.versions), 'PROBE_CONSUMER_DEPLOYMENT_CHANGED');
    originalRoute = (await apiCall(prefix + '/subdomain')).result;
    requireValue(cleanupOnly || (originalRoute?.enabled === true && originalRoute.previews_enabled === true), 'PROBE_EXISTING_VERSION_URL_REQUIRED');
    const listed = (await apiCall(versionPrefix + '?per_page=100')).result;
    requireValue(Array.isArray(listed), 'PROBE_VERSION_CLEANUP_READ_REQUIRED');
    retained = listed.filter(v => /^CYID_BINDING_PROBE_[a-f0-9]{32}$/.test(v.annotations?.['workers/message'] ?? ''));
    requireValue(cleanupOnly ? retained.length === 1 && consumerAudit.retainedProbeCount === 1 : retained.length === 0,
      cleanupOnly ? 'PROBE_SINGLE_RETAINED_VERSION_REQUIRED' : 'PROBE_PREVIOUS_VERSION_REQUIRES_CLEANUP');
    console.log('Existing development consumer checked; active deployment and Built-in retained.');
  } else requireValue((await apiCall(prefix + '/settings')).absent === true, 'PROBE_NAME_NOT_NEW');
  const subdomain = (await apiCall('/workers/subdomain')).result?.subdomain;
  requireValue(/^[a-z0-9-]+$/.test(subdomain ?? ''), 'PROBE_ENDPOINT_UNAVAILABLE');
  if (!consumerAudit) console.log('Probe runtime scope checked; temporary Worker name is new.');
  // Optional read-only inspection of this credential only. Missing token-read permission
  // is not evidence of missing Worker-write permission and never requests broader access.
  let verification, user = false;
  for (const kind of ['ACCOUNT', 'USER']) {
    user = kind === 'USER';
    const candidate = await platformCall('/tokens/verify', 'GET', undefined, `TOKEN_VERIFY_${kind}`, user);
    if (candidate.accepted && ['active', 'disabled', 'expired'].includes(candidate.result?.status)) {
      verification = candidate.result;
      console.log(`Probe token diagnostic: KIND_${kind}_STATUS_${verification.status.toUpperCase()}`);
      break;
    }
  }
  if (!verification) console.log('Probe token diagnostic: STATUS_UNKNOWN');
  if (/^[a-f0-9]{32}$/i.test(verification?.id ?? '')) {
    const details = await platformCall(`/tokens/${verification.id}`, 'GET', undefined, 'CURRENT_TOKEN_POLICY_READ', user);
    const policies = Array.isArray(details.result?.policies) ? details.result.policies : [];
    const writeHint = policies.some(p => p?.effect === 'allow' &&
      (p.resources?.[`com.cloudflare.api.account.${env.CLOUDFLARE_ACCOUNT_ID}`] === '*' || p.resources?.['com.cloudflare.api.account.*'] === '*') &&
      Array.isArray(p.permission_groups) && p.permission_groups.some(g => g?.name === 'Workers Scripts Write'));
    console.log(`Probe token policy diagnostic: READABLE_${details.accepted === true}_TARGET_WORKER_WRITE_NAME_HINT_${writeHint}` +
      `_DENY_PRESENT_${policies.some(p => p?.effect === 'deny')}_CONDITION_PRESENT_${!!details.result?.condition}`);
  }
  requireValue(!verification || verification.status === 'active', 'PROBE_TOKEN_NOT_ACTIVE');
  // Both fresh-probe cleanup and explicit retained-version recovery have one owner.
  const deleteOwnedVersion = async (versionId, owner, expiredOnly = false) => {
    requireValue(versionUuid(versionId) && /^[a-f0-9]{32}$/.test(owner), 'PROBE_VERSION_ID_INVALID');
    const current = (await apiCall(versionPrefix + '/' + versionId)).result;
    requireValue(current?.id === versionId && current.annotations?.['workers/message'] === `CYID_BINDING_PROBE_${owner}` &&
      ownsProbe(current.bindings ?? [], owner) &&
      current.bindings.filter(b => b.type === 'secret_text').every(b => b.name === 'PROBE_AUTH_KEY'), 'PROBE_VERSION_CLEANUP_SCOPE_FAILED');
    if (expiredOnly) {
      const expires = Number(current.bindings.find(b => b.type === 'plain_text' && b.name === 'PROBE_EXPIRES_AT')?.text);
      requireValue(Number.isSafeInteger(expires) && expires > 0 && expires <= Date.now(), 'PROBE_RETAINED_VERSION_NOT_EXPIRED');
    }
    const deployed = (await apiCall(prefix + '/deployments')).result?.deployments?.[0]?.versions;
    requireValue(Array.isArray(deployed) && !deployed.some(v => v.version_id === versionId), 'PROBE_VERSION_IN_USE_REFUSE_DELETE');
    requireValue(stable(deployed) === stable(deployments), 'PROBE_CONSUMER_DEPLOYMENT_CHANGED');
    await apiCall(versionPrefix + '/' + versionId, 'DELETE', undefined, 'OWNED_VERSION_DELETE');
    requireValue((await apiCall(versionPrefix + '/' + versionId)).absent === true, 'PROBE_CLEANUP_NOT_CONFIRMED');
  };
  const verifyOriginal = async () => {
    const active = await activeDeployment(async suffix => (await apiCall(suffix)).result, worker);
    requireValue(stable(active.deployments.deployments[0].versions) === stable(deployments), 'PROBE_CONSUMER_DEPLOYMENT_CHANGED');
    requireValue(stable(active.settings.bindings) === stable(originalBindings) &&
      stable((await apiCall(prefix + '/subdomain')).result) === stable(originalRoute), 'PROBE_CONSUMER_SETTINGS_CHANGED');
  };
  const ownsProbe = (bindings, owner = nonce) => {
    const services = bindings.filter(b => b.type === 'service');
    return bindings.some(b => b.type === 'plain_text' && b.name === 'PROBE_OWNER_NONCE' && b.text === owner) &&
      services.length === 2 && services.every(b => b.service === original.name) &&
      services.some(b => b.name === 'IDENTITY' && (!b.entrypoint || b.entrypoint === 'default')) &&
      services.some(b => b.name === 'IDENTITY_AUTHORITY' && b.entrypoint === 'ConsumerAuthoritySync') &&
      bindings.every(b => ['plain_text', 'secret_text', 'service'].includes(b.type));
  };
  if (cleanupOnly) {
    await deleteOwnedVersion(retained[0].id, retained[0].annotations['workers/message'].slice('CYID_BINDING_PROBE_'.length), true);
    requireValue(!(await apiCall(versionPrefix + '?per_page=100')).result.some(v => v.id === retained[0].id), 'PROBE_CLEANUP_NOT_CONFIRMED');
    await verifyOriginal();
    console.log('Expired owned probe version absence confirmed; original deployment, bindings, secret names and routes unchanged. No upload, deployment or authority switch.');
    return;
  }
  const expected = fs.readFileSync(path.join(root, 'VERSION'), 'utf8').trim();
  const config = { name: worker, main: path.join(root, 'scripts/private-binding-probe-worker.mjs'),
    compatibility_date: '2026-09-25', workers_dev: true,
    vars: { PROBE_OWNER_NONCE: nonce, PROBE_EXPIRES_AT: String(Date.now() + 10 * 60 * 1000), EXPECTED_VERSION: expected, CORE_APPLICATION: original.vars.CORE_ACCOUNT_APPLICATION_ID,
      INVALID_SESSION: `cyid_${randomBytes(32).toString('hex')}`, SYNTHETIC_WORKSPACE: `probe-${nonce}`,
      SYNTHETIC_APPLICATION: `PROBE_${nonce.toUpperCase()}`, SYNTHETIC_EMPLOYEE: `probe-${nonce}` },
    services: [{ binding: 'IDENTITY', service: original.name },
      { binding: 'IDENTITY_AUTHORITY', service: original.name, entrypoint: 'ConsumerAuthoritySync' }] };
  if (consumerAudit && env.IDENTITY_APPLICATION_ID && env.IDENTITY_WORKSPACE_ID && env.IDENTITY_CYINVOICE_WORKSPACE_ID) {
    const idHash = createHash('sha256').update(JSON.stringify(env.IDENTITY_CYINVOICE_WORKSPACE_ID)).digest('hex');
    requireValue(Object.hasOwn(consumerAudit.state?.workspaces ?? {}, idHash), 'PROBE_APPROVED_WORKSPACE_NOT_ORIGINAL');
    config.vars.TARGET_APPLICATION = env.IDENTITY_APPLICATION_ID;
    config.vars.TARGET_WORKSPACE = env.IDENTITY_WORKSPACE_ID;
  }
  let mutationAttempted = false;
  let completed = false;
  let versionId, scopeResult;
  try {
    fs.writeFileSync(configPath, JSON.stringify(config), { mode: 0o600 });
    fs.mkdirSync(bundlePath, { mode: 0o700 });
    // Local bundle only. API upload below owns all remote mutation/error handling.
    try {
      await command(process.execPath, [path.join(root, 'node_modules/wrangler/bin/wrangler.js'), 'deploy', '--dry-run', '--config', configPath, '--outdir', bundlePath], { cwd: root, env, timeout: 120000, maxBuffer: 2 * 1024 * 1024 });
    } catch { throw new Error('PROBE_LOCAL_BUNDLE_FAILED'); }
    const moduleName = 'private-binding-probe-worker.js';
    requireValue(fs.readdirSync(bundlePath).filter(name => /\.(?:m?js|wasm)$/.test(name)).join(',') === moduleName, 'PROBE_UNEXPECTED_BUNDLE_MODULES');
    const metadata = { main_module: moduleName, compatibility_date: config.compatibility_date,
      bindings: [...Object.entries(config.vars).map(([name, text]) => ({ type: 'plain_text', name, text })),
        ...config.services.map(({ binding, ...service }) => ({ type: 'service', name: binding, ...service }))] };
    const upload = new FormData();
    upload.set('metadata', new Blob([JSON.stringify(metadata)], { type: 'application/json' }), 'metadata.json');
    upload.set(moduleName, new Blob([fs.readFileSync(path.join(bundlePath, moduleName))], { type: 'application/javascript+module' }), moduleName);
    mutationAttempted = true;
    let origin;
    if (consumerAudit) {
      const version = (await apiCall(versionPrefix + '?deploy=false', 'POST', {
        main_module: moduleName, compatibility_date: config.compatibility_date,
        annotations: { 'workers/message': `CYID_BINDING_PROBE_${nonce}` },
        bindings: [...metadata.bindings, { type: 'secret_text', name: 'PROBE_AUTH_KEY', text: key }],
        modules: [{ name: moduleName, content_type: 'application/javascript+module',
          content_base64: fs.readFileSync(path.join(bundlePath, moduleName)).toString('base64') }],
      }, 'EXISTING_VERSION_UPLOAD')).result;
      requireValue(versionUuid(version?.id), 'PROBE_VERSION_ID_INVALID');
      versionId = version.id;
      origin = `https://${versionId.slice(0, 8)}-${worker}.${subdomain}.workers.dev`;
      requireValue(version.urls?.includes(origin), 'PROBE_VERSION_URL_UNAVAILABLE');
      requireValue(stable((await apiCall(prefix + '/deployments')).result?.deployments?.[0]?.versions) === stable(deployments), 'PROBE_CONSUMER_DEPLOYMENT_CHANGED');
      console.log('Probe version uploaded without deployment, D1 or original runtime secrets.');
    } else {
      await apiCall(prefix, 'PUT', upload, 'WORKER_UPLOAD');
      console.log('Temporary probe Worker uploaded.');
      requireValue(!(await apiCall(prefix + '/secrets', 'PUT', { name: 'PROBE_AUTH_KEY', text: key, type: 'secret_text' }, 'PROBE_SECRET_WRITE')).absent, 'PROBE_SECRET_NOT_READY');
      console.log('Temporary probe-only secret configured.');
      const route = await apiCall(prefix + '/subdomain', 'POST', { enabled: true, previews_enabled: false }, 'PROBE_ROUTE_ENABLE');
      requireValue(route.result?.enabled === true && route.result?.previews_enabled === false, 'PROBE_ROUTE_NOT_ENABLED');
      origin = `https://${worker}.${subdomain}.workers.dev`;
    }
    let response;
    for (let attempt = 0; attempt < 20; attempt++) {
      response = await fetcher(origin + '/probe', {
        headers: { 'x-probe-key': key }, signal: AbortSignal.timeout(15000) });
      if (response.status !== 404) break;
      await new Promise(resolve => setTimeout(resolve, 1500));
    }
    requireValue(response?.status !== 404, 'PROBE_ENDPOINT_NOT_READY');
    const result = await response.json();
    requireValue(response.ok && result.ok === true && result.defaultFetcher === true && result.invalidSessionDenied === true && result.namedInvalidationRpc === true, 'PRIVATE_BINDING_TRANSPORT_FAILED');
    if (consumerAudit) requireValue(result.scopeConfigured === !!config.vars.TARGET_WORKSPACE &&
      (result.scopeConfigured ? typeof result.scopeAvailable === 'boolean' : result.scopeAvailable === null), 'PROBE_SCOPE_RESULT_INVALID');
    scopeResult = result;
    completed = true;
    console.log('Private binding request evidence: defaultFetcher=true; invalidSessionDenied=true; namedInvalidationRpc=true. Cleanup must still pass.');
  } finally {
    try {
      if (mutationAttempted && consumerAudit) {
        const listed = (await apiCall(versionPrefix + '?per_page=100')).result;
        requireValue(Array.isArray(listed), 'PROBE_VERSION_CLEANUP_READ_REQUIRED');
        const owned = listed.filter(v => v.annotations?.['workers/message'] === `CYID_BINDING_PROBE_${nonce}`);
        requireValue(owned.length <= 1 && (!versionId || owned[0]?.id === versionId), 'PROBE_VERSION_CLEANUP_SCOPE_FAILED');
        if (owned.length) {
          versionId = owned[0].id;
          await deleteOwnedVersion(versionId, nonce);
        }
        await verifyOriginal();
        console.log('Owned probe version absence confirmed; original deployment, bindings, secret names and routes unchanged.');
      } else if (mutationAttempted) {
        const current = await apiCall(prefix + '/settings');
        if (!current.absent) {
          const bindings = current.result?.bindings ?? [];
          requireValue(ownsProbe(bindings), 'PROBE_CLEANUP_SCOPE_FAILED');
          await apiCall(prefix, 'DELETE', undefined, 'OWNED_PROBE_DELETE');
          requireValue((await apiCall(prefix + '/settings')).absent === true, 'PROBE_CLEANUP_NOT_CONFIRMED');
        }
        console.log('Temporary probe Worker absence confirmed.');
      }
    } finally { fs.rmSync(configPath, { force: true }); fs.rmSync(bundlePath, { recursive: true, force: true }); }
  }
  requireValue(completed, 'PRIVATE_BINDING_TRANSPORT_FAILED');
  console.log(`Private binding transport passed against development provider ${expected}: default Fetcher health/invalid Session denial and named RPC unavailable-scope invalidation verified; owned probe removed. No provider secret/authority/Session write; positive authorization/Windows acceptance remain pending.`);
  if (consumerAudit) console.log(`Approved scope readiness: configured=${scopeResult.scopeConfigured === true}; available=${scopeResult.scopeConfigured === true ? scopeResult.scopeAvailable === true : 'unknown'}. Original Workspace/Device authorization requires real-client acceptance.`);
}

if (process.argv[1] && import.meta.url === pathToFileURL(path.resolve(process.argv[1])).href) {
  try {
    requireValue(process.argv.length === 2 || (process.argv.length === 4 && ['existing-version', 'retained-version-cleanup'].includes(process.argv[2])), 'PROBE_MODE_INVALID');
    await main(process.env, fetch, run, undefined, process.argv.length === 4 ? JSON.parse(fs.readFileSync(process.argv[3], 'utf8')) : undefined,
      process.argv[2] === 'retained-version-cleanup');
  }
  catch (error) { console.error(/^[A-Z][A-Z0-9_]+$/.test(error?.message ?? '') ? error.message : 'PRIVATE_BINDING_PROBE_FAILED'); process.exitCode = 1; }
}
