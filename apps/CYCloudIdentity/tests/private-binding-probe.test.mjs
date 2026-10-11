import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { createHash } from 'node:crypto';
import worker from '../scripts/private-binding-probe-worker.mjs';
import { main, platformResponse } from '../scripts/private-binding-probe.mjs';

for (const scenario of ['success', 'none', 'multiple', 'wrong-owner', 'wrong-target', 'd1', 'secret', 'active',
  'not-expired', 'invalid-expiry', 'wrong-id', 'delete-denied', 'absence-unconfirmed', 'changed-after']) {
  test(`explicit retained-version cleanup: ${scenario}`, async t => {
    const consumer = JSON.parse(fs.readFileSync(path.resolve(import.meta.dirname, '../../CYInvoice/cloud/wrangler.jsonc'), 'utf8'));
    const env = {CLOUDFLARE_API_TOKEN:'PRIVATE_TOKEN', CLOUDFLARE_ACCOUNT_ID:'PRIVATE_ACCOUNT',
      CYID_WORKER_NAME:'private-provider-development', CYID_D1_DATABASE_ID:'private-provider-d1', RUNNER_TEMP:os.tmpdir()};
    const original = {name:env.CYID_WORKER_NAME,vars:{APP_ENV:'development',CORE_ACCOUNT_APPLICATION_ID:'PRIVATE_CORE_APP'}};
    const activeId='11111111-1111-1111-1111-111111111111', probeId='22222222-2222-2222-2222-222222222222', nonce='a'.repeat(32);
    const originalBindings=[{type:'plain_text',name:'APP_ENV',text:'development'},{type:'plain_text',name:'CYID_ENABLED',text:'false'},
      {type:'d1',name:'DB',id:'private-consumer-d1'},{type:'secret_text',name:'PRIVATE_SECRET'}];
    const deployments={deployments:[{versions:[{version_id:activeId,percentage:100}]}]};
    const audit={worker:consumer.name,databaseId:'private-consumer-d1',capturedAt:new Date().toISOString(),
      settings:{bindings:originalBindings},deployments,retainedProbeCount:scenario==='none'?0:scenario==='multiple'?2:1};
    const annotations={'workers/message':'CYID_BINDING_PROBE_'+nonce};
    const probe={id:scenario==='wrong-id'?activeId:probeId,annotations,bindings:[
      {type:'plain_text',name:'PROBE_OWNER_NONCE',text:scenario==='wrong-owner'?'b'.repeat(32):nonce},
      {type:'plain_text',name:'PROBE_EXPIRES_AT',text:scenario==='invalid-expiry'?'not-a-number':String(Date.now()+(scenario==='not-expired'?600000:-600000))},
      {type:'secret_text',name:scenario==='secret'?'PRIVATE_ORIGINAL_SECRET':'PROBE_AUTH_KEY'},
      {type:'service',name:'IDENTITY',service:scenario==='wrong-target'?'private-other-provider':original.name},
      {type:'service',name:'IDENTITY_AUTHORITY',service:original.name,entrypoint:'ConsumerAuthoritySync'},
      ...(scenario==='d1'?[{type:'d1',name:'DB',id:'private-original-d1'}]:[])]};
    const operations=[],logs=[]; let deleted=false;
    t.mock.method(console,'log',value=>logs.push(value));
    const ok=result=>Response.json({success:true,result});
    const versions=`/workers/workers/${consumer.name}/versions`,script=`/workers/scripts/${consumer.name}`;
    const fetcher=async(url,args={})=>{
      const p=new URL(url).pathname.replace(/^\/client\/v4\/accounts\/PRIVATE_ACCOUNT/,''),method=args.method??'GET';
      operations.push({p,method});
      if(p===script+'/deployments')return ok(scenario==='active'?{deployments:[{versions:[{version_id:probeId,percentage:100}]}]}:deployments);
      if(p===versions+'/'+activeId)return ok({id:activeId,bindings:deleted&&scenario==='changed-after'?[]:originalBindings});
      if(p===script+'/subdomain')return ok({enabled:true,previews_enabled:false});
      if(p==='/workers/subdomain')return ok({subdomain:'synthetic'});
      if(p==='/tokens/verify')return ok({status:'active'});
      if(p===versions)return ok(deleted||scenario==='none'?[]:Array.from({length:scenario==='multiple'?2:1},()=>({id:probeId,annotations})));
      if(p===versions+'/'+probeId&&method==='GET')return deleted&&scenario!=='absence-unconfirmed'?new Response(null,{status:404}):ok(probe);
      if(p===versions+'/'+probeId&&method==='DELETE'){
        if(scenario==='delete-denied')return Response.json({success:false,errors:[{message:'PRIVATE_DENIAL'}]},{status:403});
        deleted=true;return ok({});
      }
      assert.fail('Unexpected remote operation');
    };
    const command=async()=>assert.fail('cleanup must never bundle or upload');
    if(scenario==='success')await main(env,fetcher,command,original,audit,true);
    else await assert.rejects(main(env,fetcher,command,original,audit,true));
    const mayDelete=['success','delete-denied','absence-unconfirmed','changed-after'].includes(scenario);
    assert.equal(operations.filter(o=>o.method!=='GET').length,mayDelete?1:0);
    assert.equal(operations.filter(o=>o.method!=='GET').every(o=>o.method==='DELETE'&&o.p===versions+'/'+probeId),true);
    assert.doesNotMatch(logs.join('\n'),/PRIVATE_|private-provider|private-consumer|11111111|22222222|a{32}/);
    assert.equal(logs.join('\n').includes(consumer.name),false);
    assert.equal(logs.join('\n').includes('Expired owned probe version absence confirmed'),scenario==='success');
  });
}

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
  const env = { PROBE_AUTH_KEY: 'synthetic-gate', PROBE_EXPIRES_AT:String(Date.now() + 60000), EXPECTED_VERSION: '0.3.6', INVALID_SESSION: 'synthetic-invalid',
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
  assert.deepEqual(await result.json(), { ok: true, defaultFetcher: true, invalidSessionDenied: true, namedInvalidationRpc: true, scopeConfigured:false, scopeAvailable:null });
  assert.deepEqual(f.counts(), { fetches: 2, rpc: 1 });
});

test('expired probe credentials perform no provider call; approved scope readiness never returns a principal', async () => {
  const expired=fixture(); expired.env.PROBE_EXPIRES_AT=String(Date.now()-1);
  assert.equal((await worker.fetch(request(),expired.env)).status,404); assert.deepEqual(expired.counts(),{fetches:0,rpc:0});
  const f=fixture(); f.env.TARGET_WORKSPACE='private-approved-workspace'; f.env.TARGET_APPLICATION='PRIVATE_APPROVED_APPLICATION';
  const prior=f.env.IDENTITY_AUTHORITY.invalidateCache;
  f.env.IDENTITY_AUTHORITY.invalidateCache=input => input.authorities.length ? prior(input) :
    ({workspaceId:input.workspaceId,applicationId:input.applicationId,available:true,invalidated:[]});
  const result=await worker.fetch(request(),f.env);
  assert.deepEqual(await result.json(),{ok:true,defaultFetcher:true,invalidSessionDenied:true,namedInvalidationRpc:true,scopeConfigured:true,scopeAvailable:true});
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

// Exercise the existing-Worker path independently of temporary Worker creation.
// Synthetic API responses retain the original traffic deployment, D1 and secrets.
for (const scenario of ['success', 'approved-scope', 'scope-unavailable', 'scope-result', 'wrong-workspace', 'expired-audit',
  'settings-changed', 'deployment-changed', 'no-preview', 'provider-as-consumer', 'provider-d1', 'bundle',
  'upload-denied', 'partial-upload', 'wrong-owner', 'wrong-target', 'new-d1', 'version-in-use', 'wrong-url', 'retained-version', 'delete-denied']) {
  test(`undeployed existing consumer version: ${scenario}`, async t => {
    const directory = fs.mkdtempSync(path.join(os.tmpdir(), 'cyid-version-probe-'));
    const consumer = JSON.parse(fs.readFileSync(path.resolve(import.meta.dirname, '../../CYInvoice/cloud/wrangler.jsonc'), 'utf8'));
    const env = { CLOUDFLARE_API_TOKEN: 'PRIVATE_API_TOKEN', CLOUDFLARE_ACCOUNT_ID: 'PRIVATE_ACCOUNT',
      CYID_WORKER_NAME: 'private-provider-development', CYID_D1_DATABASE_ID: 'private-provider-d1', RUNNER_TEMP: directory };
    const original = { name: env.CYID_WORKER_NAME, vars: { APP_ENV: 'development', CORE_ACCOUNT_APPLICATION_ID: 'PRIVATE_CORE_APP' } };
    const originalId = '11111111-1111-1111-1111-111111111111', probeId = '22222222-2222-2222-2222-222222222222';
    const initialBindings = [{type:'plain_text',name:'APP_ENV',text:'development'}, {type:'plain_text',name:'CYID_ENABLED',text:'false'},
      {type:'d1',name:'DB',id:'private-original-consumer-d1'}, {type:'secret_text',name:'PRIVATE_ORIGINAL_SECRET'}];
    const originalDeployment = [{version_id:originalId,percentage:100}];
    const hash = createHash('sha256').update(JSON.stringify('PRIVATE_ORIGINAL_WORKSPACE')).digest('hex');
    const audit = {worker:consumer.name,databaseId:'private-original-consumer-d1',capturedAt:new Date().toISOString(),
      settings:{bindings:initialBindings},deployments:{deployments:[{versions:originalDeployment}]},state:{workspaces:{[hash]:'private-fingerprint'}}};
    if (scenario === 'expired-audit') audit.capturedAt = new Date(Date.now()-21*60*1000).toISOString();
    if (scenario === 'provider-as-consumer') audit.worker = original.name;
    if (scenario === 'provider-d1') env.CYID_D1_DATABASE_ID = audit.databaseId;
    if (['approved-scope','scope-unavailable','wrong-workspace'].includes(scenario)) Object.assign(env, {
      IDENTITY_CYINVOICE_WORKSPACE_ID:scenario === 'wrong-workspace' ? 'PRIVATE_OTHER_WORKSPACE' : 'PRIVATE_ORIGINAL_WORKSPACE',
      IDENTITY_WORKSPACE_ID:'PRIVATE_APPROVED_IDENTITY_WORKSPACE',IDENTITY_APPLICATION_ID:'PRIVATE_APPROVED_APP'});
    const operations = [], logs = [];
    let config, uploaded, deleted = false, probeRequests = 0;
    t.mock.method(console,'log',value=>logs.push(value));
    const command = async (name,args) => {
      assert.equal(name,process.execPath);
      config = JSON.parse(fs.readFileSync(args[4],'utf8'));
      assert.equal(config.name,consumer.name); assert.equal(config.d1_databases,undefined);
      assert.equal(fs.statSync(args[4]).mode & 0o777,0o600);
      if (scenario === 'bundle') throw new Error('PRIVATE BUNDLE FAILURE');
      fs.writeFileSync(path.join(args[6],'private-binding-probe-worker.js'),'export default {};');
    };
    const ok = result => Response.json({success:true,result});
    const missing = () => new Response(null,{status:404});
    const script = `/workers/scripts/${consumer.name}`, versions = `/workers/workers/${consumer.name}/versions`;
    const fetcher = async (url,args={}) => {
      const u = new URL(url), p = u.pathname.replace(/^\/client\/v4\/accounts\/PRIVATE_ACCOUNT/,''), method = args.method ?? 'GET';
      operations.push({p,method,query:u.search});
      if (url.includes('.workers.dev/')) {
        probeRequests++;
        assert.equal(url,`https://${probeId.slice(0,8)}-${consumer.name}.synthetic.workers.dev/probe`);
        assert.equal(args.headers['x-probe-key'],uploaded.bindings.find(b=>b.name==='PROBE_AUTH_KEY').text);
        const scopeConfigured = !!env.IDENTITY_WORKSPACE_ID;
        return Response.json({ok:true,defaultFetcher:true,invalidSessionDenied:true,namedInvalidationRpc:true,
          scopeConfigured:scenario === 'scope-result' ? true : scopeConfigured,
          scopeAvailable:scopeConfigured ? scenario !== 'scope-unavailable' : null});
      }
      if (p === `/workers/scripts/${original.name}/settings`) return ok({bindings:Object.entries(original.vars).map(([name,text])=>({type:'plain_text',name,text}))});
      if (p === '/workers/subdomain') return ok({subdomain:'synthetic'});
      if (p === '/tokens/verify') return ok({status:'active'});
      if (p === versions+'/'+originalId) return ok({id:originalId,bindings:scenario === 'settings-changed' ? [] : initialBindings});
      if (p === script+'/subdomain') return ok({enabled:true,previews_enabled:scenario !== 'no-preview'});
      if (p === script+'/deployments') return ok({deployments:[{versions:scenario === 'deployment-changed' ? [] :
        scenario === 'version-in-use' && uploaded ? [{version_id:probeId,percentage:100}] : originalDeployment}]});
      if (p === versions && method === 'GET') { assert.equal(u.search,'?per_page=100'); return ok(uploaded ? [{id:probeId,annotations:uploaded.annotations}] : scenario === 'retained-version' ? [{id:probeId,annotations:{'workers/message':'CYID_BINDING_PROBE_'+'a'.repeat(32)}}] : []); }
      if (p === versions && method === 'POST') {
        assert.equal(u.search,'?deploy=false'); assert.equal(args.headers['content-type'],'application/json');
        const input = JSON.parse(args.body);
        assert.equal(input.bindings.some(b=>b.type==='d1' || b.name==='PRIVATE_ORIGINAL_SECRET'),false);
        assert.deepEqual(input.bindings.filter(b=>b.type==='service').map(b=>[b.name,b.service,b.entrypoint]),
          [['IDENTITY',original.name,undefined],['IDENTITY_AUTHORITY',original.name,'ConsumerAuthoritySync']]);
        assert.equal(Buffer.from(input.modules[0].content_base64,'base64').toString(),'export default {};');
        assert.equal(input.modules[0].name,input.main_module);
        assert.equal(input.bindings.filter(b=>b.type==='secret_text').length,1);
        assert.equal(input.bindings.find(b=>b.type==='secret_text').name,'PROBE_AUTH_KEY');
        uploaded = scenario === 'upload-denied' ? undefined : input;
        if (['upload-denied','partial-upload'].includes(scenario)) return Response.json({success:false,errors:[{code:10000,message:'PRIVATE PLATFORM FAILURE'}]},{status:403});
        return ok({id:probeId,urls:scenario === 'wrong-url' ? ['https://private-external.example/'] : [`https://${probeId.slice(0,8)}-${consumer.name}.synthetic.workers.dev`]});
      }
      if (p === versions+'/'+probeId && method === 'GET') {
        if (!uploaded) return missing();
        return ok({id:probeId,annotations:uploaded.annotations,bindings:uploaded.bindings.map(b=>
          scenario === 'wrong-owner' && b.name==='PROBE_OWNER_NONCE' ? {...b,text:'other-owner'} :
          scenario === 'wrong-target' && b.type==='service' ? {...b,service:'other-provider'} : b)
          .concat(scenario === 'new-d1' ? [{type:'d1',name:'UNEXPECTED'}] : [])});
      }
      if (p === versions+'/'+probeId && method === 'DELETE') {
        if (scenario === 'delete-denied') return Response.json({success:false,errors:[{message:'PRIVATE DELETE DENIAL'}]},{status:403});
        deleted = true; uploaded = undefined; return ok({});
      }
      assert.fail(`Unexpected API method/path: ${method} ${p}`);
    };
    const success = ['success','approved-scope','scope-unavailable'].includes(scenario);
    const noMutation = ['wrong-workspace','expired-audit','settings-changed','deployment-changed','no-preview','provider-as-consumer','provider-d1','bundle','retained-version'].includes(scenario);
    const refuseDelete = ['wrong-owner','wrong-target','new-d1','version-in-use'].includes(scenario);
    try {
      // Omit originalConfig to cover the real CLI's private provider-settings read.
      if (success) await main(env,fetcher,command,undefined,audit);
      else await assert.rejects(main(env,fetcher,command,undefined,audit), /^Error: (?:PROBE_[A-Z0-9_]+|SINGLE_ACTIVE_DEPLOYMENT_REQUIRED|ACTIVE_[A-Z0-9_]+|PRIVATE_BINDING_TRANSPORT_FAILED)$/);
      assert.equal(deleted,!noMutation && !refuseDelete && !['upload-denied','delete-denied'].includes(scenario));
      assert.equal(operations.filter(o=>o.method!=='GET').every(o=>o.p===versions || o.p===versions+'/'+probeId),true);
      assert.equal(operations.some(o=>/\/d1\/|\/identity\//.test(o.p)),false);
      if (noMutation) assert.equal(operations.some(o=>o.method!=='GET'),false);
      assert.equal(fs.readdirSync(directory).length,0);
      assert.doesNotMatch(logs.join('\n'),/PRIVATE_|private-provider|private-original|11111111|22222222/);
      assert.equal(logs.join('\n').includes(consumer.name),false);
      if (scenario === 'wrong-url') assert.equal(probeRequests,0);
      if (success) assert.match(logs.join('\n'),/Owned probe version absence confirmed/);
      if (scenario === 'approved-scope') assert.match(logs.join('\n'),/configured=true; available=true/);
      if (scenario === 'scope-unavailable') assert.match(logs.join('\n'),/configured=true; available=false/);
      if (scenario === 'delete-denied') {
        assert.match(logs.join('\n'),/Private binding request evidence:/);
        assert.doesNotMatch(logs.join('\n'),/Owned probe version absence confirmed/);
        assert.equal(operations.filter(o=>o.method==='DELETE').length,1);
      }
      if (config && env.IDENTITY_WORKSPACE_ID) assert.equal(config.vars.TARGET_WORKSPACE,env.IDENTITY_WORKSPACE_ID);
    } finally { fs.rmSync(directory,{recursive:true,force:true}); }
  });
}
