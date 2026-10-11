import fs from 'node:fs';
import path from 'node:path';
import { randomBytes } from 'node:crypto';
import { execFile } from 'node:child_process';
import { promisify } from 'node:util';
import { pathToFileURL } from 'node:url';
const root = path.resolve(import.meta.dirname, '..');
const requireValue = (condition, code) => { if (!condition) throw new Error(code); };
const run = promisify(execFile);

export async function main(env = process.env, fetcher = fetch, command = run, originalConfig) {
  requireValue(env.CLOUDFLARE_ACCOUNT_ID && env.CLOUDFLARE_API_TOKEN && env.CYID_WORKER_NAME && env.RUNNER_TEMP, 'PROBE_INPUTS_REQUIRED');
  const original = originalConfig ?? JSON.parse(fs.readFileSync(path.join(root, 'wrangler.deploy.generated.jsonc'), 'utf8'));
  requireValue(original.name === env.CYID_WORKER_NAME && original.vars.APP_ENV === 'development' && original.vars.CORE_ACCOUNT_APPLICATION_ID, 'PROBE_DEVELOPMENT_SCOPE_REQUIRED');
  const nonce = randomBytes(16).toString('hex');
  const worker = `cyid-binding-probe-${nonce}`;
  const key = randomBytes(32).toString('hex');
  const api = `https://api.cloudflare.com/client/v4/accounts/${encodeURIComponent(env.CLOUDFLARE_ACCOUNT_ID)}`;
  const configPath = path.join(env.RUNNER_TEMP, `cyid-binding-${nonce}.jsonc`);
  const apiCall = async (suffix, method = 'GET', body) => {
    const response = await fetcher(api + suffix, { method,
      headers: { authorization: `Bearer ${env.CLOUDFLARE_API_TOKEN}`, 'content-type': 'application/json' },
      ...(body ? { body: JSON.stringify(body) } : {}), signal: AbortSignal.timeout(30000) });
    if (response.status === 404) return { absent: true };
    const result = await response.json();
    requireValue(response.ok && result.success === true, 'PROBE_PLATFORM_REQUEST_FAILED');
    return { result: result.result };
  };
  const prefix = `/workers/scripts/${worker}`;
  requireValue((await apiCall(prefix + '/settings')).absent === true, 'PROBE_NAME_NOT_NEW');
  const subdomain = (await apiCall('/workers/subdomain')).result?.subdomain;
  requireValue(/^[a-z0-9-]+$/.test(subdomain ?? ''), 'PROBE_ENDPOINT_UNAVAILABLE');
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
    mutationAttempted = true;
    // Output may contain private deployment names: capture, never print or upload it.
    await command('npx', ['wrangler', 'deploy', '--config', configPath], { cwd: root, env, timeout: 120000, maxBuffer: 2 * 1024 * 1024 });
    requireValue(!(await apiCall(prefix + '/secrets', 'PUT', { name: 'PROBE_AUTH_KEY', text: key, type: 'secret_text' })).absent, 'PROBE_SECRET_NOT_READY');
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
          await apiCall(prefix, 'DELETE');
          requireValue((await apiCall(prefix + '/settings')).absent === true, 'PROBE_CLEANUP_NOT_CONFIRMED');
        }
      }
    } finally { fs.rmSync(configPath, { force: true }); }
  }
  requireValue(completed, 'PRIVATE_BINDING_TRANSPORT_FAILED');
  console.log(`Private binding transport passed against development provider ${expected}: default Fetcher health/invalid Session denial and named RPC unavailable-scope invalidation verified; isolated probe Worker removed. No provider secret/authority/Session write; real scope/positive authorization/Windows acceptance remain pending.`);
}

if (process.argv[1] && import.meta.url === pathToFileURL(path.resolve(process.argv[1])).href) {
  try { await main(); }
  catch (error) { console.error(/^[A-Z][A-Z0-9_]+$/.test(error?.message ?? '') ? error.message : 'PRIVATE_BINDING_PROBE_FAILED'); process.exitCode = 1; }
}
