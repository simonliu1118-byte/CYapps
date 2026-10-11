import fs from 'node:fs';
import path from 'node:path';
import { randomBytes } from 'node:crypto';
import { execFile } from 'node:child_process';
import { promisify } from 'node:util';
import { pathToFileURL } from 'node:url';
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
export async function main(env = process.env, fetcher = fetch, command = run, originalConfig) {
  requireValue(env.CLOUDFLARE_ACCOUNT_ID && env.CLOUDFLARE_API_TOKEN && env.CYID_WORKER_NAME && env.RUNNER_TEMP, 'PROBE_INPUTS_REQUIRED');
  const original = originalConfig ?? JSON.parse(fs.readFileSync(path.join(root, 'wrangler.deploy.generated.jsonc'), 'utf8'));
  requireValue(original.name === env.CYID_WORKER_NAME && original.vars.APP_ENV === 'development' && original.vars.CORE_ACCOUNT_APPLICATION_ID, 'PROBE_DEVELOPMENT_SCOPE_REQUIRED');
  const nonce = randomBytes(16).toString('hex');
  const worker = `cyid-binding-probe-${nonce}`;
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
  const prefix = `/workers/scripts/${worker}`;
  requireValue((await apiCall(prefix + '/settings')).absent === true, 'PROBE_NAME_NOT_NEW');
  const subdomain = (await apiCall('/workers/subdomain')).result?.subdomain;
  requireValue(/^[a-z0-9-]+$/.test(subdomain ?? ''), 'PROBE_ENDPOINT_UNAVAILABLE');
  console.log('Probe runtime scope checked; temporary Worker name is new.');
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
  const expected = fs.readFileSync(path.join(root, 'VERSION'), 'utf8').trim();
  const config = { name: worker, main: path.join(root, 'scripts/private-binding-probe-worker.mjs'),
    compatibility_date: '2026-09-25', workers_dev: true,
    vars: { PROBE_OWNER_NONCE: nonce, EXPECTED_VERSION: expected, CORE_APPLICATION: original.vars.CORE_ACCOUNT_APPLICATION_ID,
      INVALID_SESSION: `cyid_${randomBytes(32).toString('hex')}`, SYNTHETIC_WORKSPACE: `probe-${nonce}`,
      SYNTHETIC_APPLICATION: `PROBE_${nonce.toUpperCase()}`, SYNTHETIC_EMPLOYEE: `probe-${nonce}` },
    services: [{ binding: 'IDENTITY', service: original.name },
      { binding: 'IDENTITY_AUTHORITY', service: original.name, entrypoint: 'ConsumerAuthoritySync' }] };
  let mutationAttempted = false;
  let completed = false;
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
    await apiCall(prefix, 'PUT', upload, 'WORKER_UPLOAD');
    console.log('Temporary probe Worker uploaded.');
    requireValue(!(await apiCall(prefix + '/secrets', 'PUT', { name: 'PROBE_AUTH_KEY', text: key, type: 'secret_text' }, 'PROBE_SECRET_WRITE')).absent, 'PROBE_SECRET_NOT_READY');
    console.log('Temporary probe-only secret configured.');
    const route = await apiCall(prefix + '/subdomain', 'POST', { enabled: true, previews_enabled: false }, 'PROBE_ROUTE_ENABLE');
    requireValue(route.result?.enabled === true && route.result?.previews_enabled === false, 'PROBE_ROUTE_NOT_ENABLED');
    let response;
    for (let attempt = 0; attempt < 20; attempt++) {
      response = await fetcher(`https://${worker}.${subdomain}.workers.dev/probe`, {
        headers: { 'x-probe-key': key }, signal: AbortSignal.timeout(15000) });
      if (response.status !== 404) break;
      await new Promise(resolve => setTimeout(resolve, 1500));
    }
    requireValue(response?.status !== 404, 'PROBE_ENDPOINT_NOT_READY');
    const result = await response.json();
    requireValue(response.ok && result.ok === true && result.defaultFetcher === true && result.invalidSessionDenied === true && result.namedInvalidationRpc === true, 'PRIVATE_BINDING_TRANSPORT_FAILED');
    completed = true;
  } finally {
    try {
      if (mutationAttempted) {
        const current = await apiCall(prefix + '/settings');
        if (!current.absent) {
          const bindings = current.result?.bindings ?? [];
          const services = bindings.filter(b => b.type === 'service');
          requireValue(bindings.some(b => b.type === 'plain_text' && b.name === 'PROBE_OWNER_NONCE' && b.text === nonce) &&
            services.length === 2 && services.every(b => b.service === original.name) &&
            services.some(b => b.name === 'IDENTITY' && (!b.entrypoint || b.entrypoint === 'default')) &&
            services.some(b => b.name === 'IDENTITY_AUTHORITY' && b.entrypoint === 'ConsumerAuthoritySync') &&
            !bindings.some(b => b.type === 'd1'), 'PROBE_CLEANUP_SCOPE_FAILED');
          await apiCall(prefix, 'DELETE', undefined, 'OWNED_PROBE_DELETE');
          requireValue((await apiCall(prefix + '/settings')).absent === true, 'PROBE_CLEANUP_NOT_CONFIRMED');
        }
        console.log('Temporary probe Worker absence confirmed.');
      }
    } finally { fs.rmSync(configPath, { force: true }); fs.rmSync(bundlePath, { recursive: true, force: true }); }
  }
  requireValue(completed, 'PRIVATE_BINDING_TRANSPORT_FAILED');
  console.log(`Private binding transport passed against development provider ${expected}: default Fetcher health/invalid Session denial and named RPC unavailable-scope invalidation verified; isolated probe Worker removed. No provider secret/authority/Session write; real scope/positive authorization/Windows acceptance remain pending.`);
}

if (process.argv[1] && import.meta.url === pathToFileURL(path.resolve(process.argv[1])).href) {
  try { await main(); }
  catch (error) { console.error(/^[A-Z][A-Z0-9_]+$/.test(error?.message ?? '') ? error.message : 'PRIVATE_BINDING_PROBE_FAILED'); process.exitCode = 1; }
}
