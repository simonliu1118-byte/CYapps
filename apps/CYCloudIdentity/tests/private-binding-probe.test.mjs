import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import worker from '../scripts/private-binding-probe-worker.mjs';
import { main, platformResponse } from '../scripts/private-binding-probe.mjs';

test('platform diagnostics project fixed facts from JSON, HTML and intermediary denial without copying private text', async () => {
  for (const [body, headers, pattern] of [
    [JSON.stringify({success:false, errors:[{code:10000, message:'PRIVATE BODY', documentation_url:'https://private.example/PRIVATE'}]}), {}, /CODE_10000_BODY_JSON_ENVELOPE_1.*ERROR_DOC_1/],
    ['<!doctype html><html>Cloudflare PRIVATE BODY</html>', {'cf-ray':'PRIVATE-RAY', server:'cloudflare'}, /BODY_HTML_ENVELOPE_0_CF_RAY_1_CF_SERVER_1_PROXY_DENIAL_0_CF_HTML_1/],
    ['PRIVATE BODY', {'x-mitmproxy-blocked-reason':'PRIVATE-REASON'}, /BODY_TEXT_ENVELOPE_0.*PROXY_DENIAL_1/],
    [JSON.stringify({success:false, errors:'PRIVATE BODY'}), {}, /CODE_UNSPECIFIED_BODY_JSON_ENVELOPE_1/],
    ['null', {}, /CODE_UNSPECIFIED_BODY_JSON_ENVELOPE_0/],
  ]) {
    const result = await platformResponse(new Response(body, {status:403, headers}));
    assert.equal(result.accepted, false);
    assert.match(result.diagnostic, pattern);
    assert.match(result.diagnostic, /^[A-Z0-9_]+$/);
    assert.doesNotMatch(result.diagnostic, /PRIVATE|private\.example/);
  }
});

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

for (const failure of ['none', 'request', 'wrong-owner', 'wrong-target', 'new-d1', 'bundle', 'upload', 'partial-upload', 'subdomain', 'gateway', 'token-policy', 'policy-denied', 'user-token', 'token-expired', 'token-unknown', 'token-transport']) test(`probe controller cleanup boundaries: ${failure}`, async t => {
  const directory = fs.mkdtempSync(path.join(os.tmpdir(), 'cyid-private-probe-'));
  const env = { CLOUDFLARE_API_TOKEN: 'synthetic-api-token', CLOUDFLARE_ACCOUNT_ID: 'synthetic-account', CYID_WORKER_NAME: 'identity-test-development', RUNNER_TEMP: directory };
  const original = { name: env.CYID_WORKER_NAME, vars: { APP_ENV: 'development', CORE_ACCOUNT_APPLICATION_ID: 'APP_TEST' } };
  let config, exists = false, deleted = false, secret;
  const operations = [];
  const logs = [];
  t.mock.method(console, 'log', value => logs.push(value));
  const command = async (name, args) => {
    assert.equal(name, process.execPath); assert.equal(args[0], path.resolve(import.meta.dirname, '../node_modules/wrangler/bin/wrangler.js'));
    assert.deepEqual(args.slice(1, 4), ['deploy', '--dry-run', '--config']);
    config = JSON.parse(fs.readFileSync(args[4], 'utf8'));
    assert.notEqual(config.name, original.name);
    assert.equal(config.d1_databases, undefined);
    assert.equal(fs.statSync(args[4]).mode & 0o777, 0o600);
    if (failure === 'bundle') throw new Error('raw private bundler failure');
    assert.equal(args[5], '--outdir');
    assert.equal(fs.statSync(args[6]).mode & 0o777, 0o700);
    fs.writeFileSync(path.join(args[6], 'private-binding-probe-worker.js'), 'export default {};');
  };
  const fetcher = async (url, args = {}) => {
    const p = new URL(url).pathname; const method = args.method ?? 'GET'; operations.push({ p, method });
    if (p.endsWith('/tokens/verify')) {
      assert.equal(method, 'GET');
      if (failure === 'token-transport') throw new Error('PRIVATE REQUEST TOKEN URL');
      if (failure === 'token-unknown' || (failure === 'user-token' && p.includes('/accounts/'))) return Response.json({success:false,errors:[{code:10000,message:'PRIVATE TOKEN DENIAL'}]}, {status:403});
      return Response.json({success:true,result:{status:failure === 'token-expired' ? 'expired' : 'active',
        ...(['token-policy','policy-denied','user-token'].includes(failure) ? {id:'a'.repeat(32)} : {})}});
    }
    if (p.endsWith('/tokens/' + 'a'.repeat(32))) {
      assert.equal(method, 'GET');
      if (failure === 'user-token') assert.match(p, /\/user\/tokens\//);
      if (failure === 'policy-denied') return Response.json({success:false,errors:[{code:10000,message:'PRIVATE TOKEN POLICY'}]}, {status:403});
      return Response.json({success:true,result:{name:'PRIVATE TOKEN NAME', condition:{request_ip:{in:['PRIVATE IP']}}, policies:[
        {effect:'allow',resources:{['com.cloudflare.api.account.' + env.CLOUDFLARE_ACCOUNT_ID]:'*'},permission_groups:[{name:'Workers Scripts Write',id:'PRIVATE PERMISSION ID'}]},
        {effect:'deny',resources:{'PRIVATE RESOURCE':'*'},permission_groups:[]}]}});
    }
    if (url.includes('.workers.dev/')) {
      assert.equal(args.headers['x-probe-key'], secret);
      if (failure === 'request') return Response.json({ ok: false }, { status: 503 });
      return Response.json({ ok: true, defaultFetcher: true, invalidSessionDenied: true, namedInvalidationRpc: true });
    }
    if (p.endsWith('/workers/subdomain')) return Response.json({ success: true, result: { subdomain: 'synthetic' } });
    if (method === 'PUT' && args.body instanceof FormData) {
      assert.equal(args.headers['content-type'], undefined);
      const metadata = JSON.parse(await args.body.get('metadata').text());
      assert.equal(metadata.main_module, 'private-binding-probe-worker.js');
      assert.equal(await args.body.get(metadata.main_module).text(), 'export default {};');
      assert.equal(metadata.bindings.some(b => b.type === 'd1'), false);
      assert.deepEqual(metadata.bindings.filter(b => b.type === 'service').map(b => [b.name, b.service, b.entrypoint]), [['IDENTITY', original.name, undefined], ['IDENTITY_AUTHORITY', original.name, 'ConsumerAuthoritySync']]);
      exists = !['upload','gateway'].includes(failure);
      if (failure === 'gateway') return new Response('PRIVATE NETWORK BODY', {status:403, headers:{'x-mitmproxy-blocked-reason':'PRIVATE REASON'}});
      if (['upload', 'partial-upload'].includes(failure)) return Response.json({success: false, errors: [{code: 10021, message: 'PRIVATE CONFIG MUST NOT LEAK'}]}, {status: 400});
      return Response.json({success: true, result: {}});
    }
    if (p.endsWith('/subdomain')) {
      assert.equal(method, 'POST'); assert.deepEqual(JSON.parse(args.body), {enabled:true, previews_enabled:false});
      if (failure === 'subdomain') return new Response('Private gateway body', {status:503});
      return Response.json({success:true, result:{enabled:true, previews_enabled:false}});
    }
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
    if (['none','token-policy','policy-denied','user-token','token-unknown','token-transport'].includes(failure)) await main(env, fetcher, command, original);
    else await assert.rejects(main(env, fetcher, command, original), failure === 'bundle' ? /PROBE_LOCAL_BUNDLE_FAILED/ : ['upload','partial-upload'].includes(failure) ? /PROBE_PLATFORM_HTTP_400_CODE_10021/ : failure === 'subdomain' ? /PROBE_PLATFORM_HTTP_503_CODE_UNSPECIFIED/ : failure === 'token-expired' ? /PROBE_TOKEN_NOT_ACTIVE/ : failure === 'gateway' ? /PROBE_PLATFORM_HTTP_403_CODE_UNSPECIFIED_BODY_TEXT.*PROXY_DENIAL_1/ : undefined);
    assert.equal(deleted, !['wrong-owner', 'wrong-target', 'new-d1', 'bundle', 'upload', 'gateway', 'token-expired'].includes(failure));
    assert.equal(fs.readdirSync(directory).length, 0);
    assert.equal(operations.filter(x => x.method !== 'GET').every(x => x.p.includes('/workers/scripts/cyid-binding-probe-')), true);
    assert.equal(operations.some(x => /\/d1\/database\/|\/identity\//.test(x.p)), false);
    assert.equal(operations.filter(x => x.p.includes('/tokens/')).every(x => x.method === 'GET' && /\/tokens\/(?:verify|a{32})$/.test(x.p)), true);
    assert.doesNotMatch(logs.join('\n'), /PRIVATE|synthetic-account|synthetic-api-token|identity-test-development|a{32}/);
    if (failure === 'token-policy') assert.match(logs.join('\n'), /READABLE_true_TARGET_WORKER_WRITE_NAME_HINT_true_DENY_PRESENT_true_CONDITION_PRESENT_true/);
    if (failure === 'policy-denied') assert.match(logs.join('\n'), /READABLE_false_TARGET_WORKER_WRITE_NAME_HINT_false/);
    if (failure === 'token-expired') assert.equal(operations.some(x => x.method !== 'GET'), false);
    if (failure === 'token-transport') assert.match(logs.join('\n'), /TOKEN_VERIFY_ACCOUNT_TRANSPORT_UNAVAILABLE/);
    if (failure === 'gateway') assert.match(logs.join('\n'), /WORKER_UPLOAD_HTTP_403_CODE_UNSPECIFIED_BODY_TEXT.*PROXY_DENIAL_1/);
  } finally { fs.rmSync(directory, { recursive: true, force: true }); }
});

test('production scope is refused before any platform request or mutation', async () => {
  const env = { CLOUDFLARE_API_TOKEN: 'synthetic-token', CLOUDFLARE_ACCOUNT_ID: 'synthetic-account', CYID_WORKER_NAME: 'identity-test', RUNNER_TEMP: os.tmpdir() };
  const never = async () => { assert.fail('must not call platform/command'); };
  await assert.rejects(main(env, never, never, { name: env.CYID_WORKER_NAME, vars: { APP_ENV: 'production', CORE_ACCOUNT_APPLICATION_ID: 'APP_TEST' } }), /PROBE_DEVELOPMENT_SCOPE_REQUIRED/);
});
