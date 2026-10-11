import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import worker from '../scripts/private-binding-probe-worker.mjs';
import { main } from '../scripts/private-binding-probe.mjs';

function fixture() {
  let fetches = 0, rpc = 0;
  const env = { PROBE_AUTH_KEY: 'synthetic-gate', EXPECTED_VERSION: '0.3.6', INVALID_SESSION: 'synthetic-invalid',
    CORE_APPLICATION: 'APP_TEST', SYNTHETIC_WORKSPACE: 'workspace-synthetic-missing', SYNTHETIC_APPLICATION: 'APP_SYNTHETIC_MISSING', SYNTHETIC_EMPLOYEE: 'employee-synthetic-missing',
    IDENTITY: { async fetch(url, args) { fetches++;
      if (url.endsWith('/health')) return Response.json({ ok: true, service: 'cycloud-identity', environment: 'development', serviceVersion: '0.3.6' });
      assert.equal(args.method, 'POST');
      return Response.json({ error: { code: 'SESSION_INVALID' } }, { status: 401 });
    } }, IDENTITY_AUTHORITY: { async invalidateCache(input) { rpc++;
      return { workspaceId: input.workspaceId, applicationId: input.applicationId, available: false, invalidated: [input.authorities[0].employeeId] };
    } } };
  return { env, counts: () => ({ fetches, rpc }) };
}
const request = () => new Request('https://probe.example.test/probe', { headers: { 'x-probe-key': 'synthetic-gate' } });
test('unauthenticated and wrong-route probes perform no provider call', async () => {
  const f = fixture();
  for (const r of [new Request('https://probe.example.test/probe'), new Request('https://probe.example.test/other'), new Request('https://probe.example.test/probe', { method: 'POST' })])
    assert.equal((await worker.fetch(r, f.env)).status, 404);
  assert.deepEqual(f.counts(), { fetches: 0, rpc: 0 });
});
test('private probe verifies default Fetcher and named RPC without exporting a scope/principal', async () => {
  const f = fixture();
  const result = await worker.fetch(request(), f.env);
  assert.deepEqual(await result.json(), { ok: true, defaultFetcher: true, invalidSessionDenied: true, namedInvalidationRpc: true });
  assert.deepEqual(f.counts(), { fetches: 2, rpc: 1 });
});
test('wrong provider version, accepted Session, positive grant and missing RPC fail closed', async () => {
  for (const type of ['version', 'session', 'grant', 'rpc']) {
    const f = fixture();
    if (type === 'version') f.env.EXPECTED_VERSION = '0.3.5';
    if (type === 'session') { const original = f.env.IDENTITY.fetch; f.env.IDENTITY.fetch = (url, args) => url.endsWith('/health') ? original(url, args) : Response.json({ ok: true, principal: 'DO-NOT-EXPORT' }); }
    if (type === 'grant') f.env.IDENTITY_AUTHORITY.invalidateCache = async input => ({ workspaceId: input.workspaceId, applicationId: input.applicationId, available: true, invalidated: [] });
    if (type === 'rpc') f.env.IDENTITY_AUTHORITY = {};
    const result = await worker.fetch(request(), f.env);
    assert.equal(result.status, 503);
    assert.doesNotMatch(await result.text(), /DO-NOT-EXPORT|workspace-synthetic|employee-synthetic/);
  }
});

for (const failure of ['none', 'request', 'wrong-owner', 'wrong-target', 'new-d1', 'deploy']) test(`probe controller cleanup boundaries: ${failure}`, async () => {
  const directory = fs.mkdtempSync(path.join(os.tmpdir(), 'cyid-private-probe-'));
  const env = { CLOUDFLARE_API_TOKEN: 'synthetic-api-token', CLOUDFLARE_ACCOUNT_ID: 'synthetic-account', CYID_WORKER_NAME: 'identity-test-development', RUNNER_TEMP: directory };
  const original = { name: env.CYID_WORKER_NAME, vars: { APP_ENV: 'development', CORE_ACCOUNT_APPLICATION_ID: 'APP_TEST' } };
  let config, exists = false, deleted = false, secret;
  const operations = [];
  const command = async (name, args) => {
    assert.equal(name, 'npx'); assert.deepEqual(args.slice(0, 3), ['wrangler', 'deploy', '--config']);
    config = JSON.parse(fs.readFileSync(args[3], 'utf8'));
    assert.notEqual(config.name, original.name);
    assert.equal(config.d1_databases, undefined);
    assert.equal(fs.statSync(args[3]).mode & 0o777, 0o600);
    exists = true;
    if (failure === 'deploy') throw Object.assign(new Error('raw private deployment details'), { stderr: 'Private worker name and settings; [code: 10000]' });
  };
  const fetcher = async (url, args = {}) => {
    const p = new URL(url).pathname; const method = args.method ?? 'GET'; operations.push({ p, method });
    if (url.includes('.workers.dev/')) {
      assert.equal(args.headers['x-probe-key'], secret);
      if (failure === 'request') return Response.json({ ok: false }, { status: 503 });
      return Response.json({ ok: true, defaultFetcher: true, invalidSessionDenied: true, namedInvalidationRpc: true });
    }
    if (p.endsWith('/workers/subdomain')) return Response.json({ success: true, result: { subdomain: 'synthetic' } });
    if (p.endsWith('/settings')) {
      if (!exists) return new Response(null, { status: 404 });
      return Response.json({ success: true, result: { bindings: [
        { type: 'plain_text', name: 'PROBE_OWNER_NONCE', text: failure === 'wrong-owner' ? 'other-owner' : config.vars.PROBE_OWNER_NONCE },
        ...config.services.map(s => ({ type: 'service', name: s.binding, service: failure === 'wrong-target' ? 'other-provider' : s.service, entrypoint: s.entrypoint })),
        ...(failure === 'new-d1' ? [{ type: 'd1', name: 'UNEXPECTED' }] : []),
      ] } });
    }
    if (p.endsWith('/secrets')) { assert.equal(method, 'PUT'); const b = JSON.parse(args.body); assert.equal(b.name, 'PROBE_AUTH_KEY'); secret = b.text; }
    else { assert.equal(method, 'DELETE'); deleted = true; exists = false; }
    return Response.json({ success: true, result: {} });
  };
  try {
    if (failure === 'none') await main(env, fetcher, command, original);
    else await assert.rejects(main(env, fetcher, command, original), failure === 'deploy' ? /PROBE_UPLOAD_FAILED_10000/ : undefined);
    assert.equal(deleted, !['wrong-owner', 'wrong-target', 'new-d1'].includes(failure));
    assert.equal(fs.readdirSync(directory).length, 0);
    assert.equal(operations.filter(x => x.method !== 'GET').every(x => x.p.includes('/workers/scripts/cyid-binding-probe-')), true);
    assert.equal(operations.some(x => /\/d1\/database\/|\/identity\//.test(x.p)), false);
  } finally { fs.rmSync(directory, { recursive: true, force: true }); }
});

test('production scope is refused before any platform request or mutation', async () => {
  const env = { CLOUDFLARE_API_TOKEN: 'synthetic-token', CLOUDFLARE_ACCOUNT_ID: 'synthetic-account', CYID_WORKER_NAME: 'identity-test', RUNNER_TEMP: os.tmpdir() };
  const never = async () => { assert.fail('must not call platform/command'); };
  await assert.rejects(main(env, never, never, { name: env.CYID_WORKER_NAME, vars: { APP_ENV: 'production', CORE_ACCOUNT_APPLICATION_ID: 'APP_TEST' } }), /PROBE_DEVELOPMENT_SCOPE_REQUIRED/);
});
