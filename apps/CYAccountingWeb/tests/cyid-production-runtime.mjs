import assert from 'node:assert/strict';
import {
  deriveCyidProductionServiceName,
  parseCyidProductionSettings,
  verifyCyWebProductionPortal,
  resolveCyidProductionRuntime
} from '../scripts/resolve-cyid-production-runtime.mjs';

assert.equal(deriveCyidProductionServiceName('cyid-development'), 'cyid-production');
assert.equal(deriveCyidProductionServiceName('cyid-dev'), 'cyid-prod');
assert.equal(deriveCyidProductionServiceName('cyid'), 'cyid-prod');

const developmentService = 'cyid-development-provider';
const service = 'cyid-development-provider-prod';
const payload = {
  success: true,
  result: {
    bindings: [
      { name: 'APP_ENV', type: 'plain_text', text: 'production' },
      { name: 'CYACC_SERVICE_BINDING_TARGET', type: 'plain_text', text: service },
      { name: 'CYACC_APPLICATION_ID', type: 'plain_text', text: 'CYACC_PROD_TEST' },
      { name: 'CYACC_WORKSPACE_ID', type: 'plain_text', text: 'workspace-prod-test' },
      { name: 'ACCOUNT_PORTAL_URL', type: 'plain_text', text: 'https://cyweb.example.invalid/' },
      { name: 'CORE_ACCOUNT_APPLICATION_ID', type: 'plain_text', text: 'CYWEB_TEST' },
      { name: 'OTP_PEPPER', type: 'secret_text' }
    ]
  }
};

assert.deepEqual(parseCyidProductionSettings(payload, service), {
  service,
  applicationId: 'CYACC_PROD_TEST',
  workspaceId: 'workspace-prod-test'
});

assert.throws(
  () => parseCyidProductionSettings({
    result: { bindings: payload.result.bindings.map(binding => binding.name === 'APP_ENV' ? { ...binding, text: 'development' } : binding) }
  }, service),
  /not a CYID production provider/
);

assert.throws(
  () => parseCyidProductionSettings({
    result: { bindings: payload.result.bindings.map(binding => binding.name === 'CYACC_SERVICE_BINDING_TARGET' ? { ...binding, text: 'different-provider' } : binding) }
  }, service),
  /does not match/
);

assert.throws(
  () => parseCyidProductionSettings({
    result: { bindings: payload.result.bindings.filter(binding => binding.name !== 'CYACC_APPLICATION_ID') }
  }, service),
  /Application ID/
);

const coreSettings={success:true,result:{bindings:[{name:'IDENTITY',type:'service',service},{name:'IDENTITY_WORKSPACE_ID',type:'plain_text',text:'workspace-prod-test'},{name:'IDENTITY_APPLICATION_ID',type:'plain_text',text:'CYWEB_TEST'},{name:'IDENTITY_CONSUMER_VERSION',type:'plain_text',text:'1.0.4'}]}};
const health={ok:true,data:{service:'cyweb',database:'ok',identityConsumerVersion:'1.0.4'}};
const requests=[];
const mockFetch=async(url,init={})=>{
  requests.push({url:String(url),headers:new Headers(init.headers),redirect:init.redirect});
  if(String(url).endsWith('/api/health'))return Response.json(health);
  if(String(url).includes('/workers/domains?'))return Response.json({success:true,result:[{hostname:'cyweb.example.invalid',service:'cyweb-test-production',environment:'production',enabled:true}]});
  if(String(url).includes('/cyweb-test-production/settings'))return Response.json(coreSettings);
  return Response.json(payload);
};
const runtime=await resolveCyidProductionRuntime({accountId:'account-ci',apiToken:'token-ci',developmentService,fetchImpl:mockFetch});
assert.equal(runtime.applicationId,'CYACC_PROD_TEST');assert.equal(runtime.portalUrl,'https://cyweb.example.invalid/');
assert.match(requests[0].url,/workers\/scripts\/cyid-development-provider-prod\/settings$/);
assert.equal(requests[0].headers.get('authorization'),'Bearer token-ci');
assert.equal(requests.at(-1).headers.get('authorization'),null,'Cloudflare token never sent to the portal');
const verifyPortal=overrides=>verifyCyWebProductionPortal({account:'account-ci',token:'token-ci',runtime,provider:payload,fetchImpl:async(url,init)=>{
  if(String(url).includes('/cyweb-test-production/settings'))return Response.json(overrides.core||coreSettings);
  if(String(url).endsWith('/api/health'))return Response.json(overrides.health||health);
  if(String(url).includes('/workers/domains?')&&overrides.domains)return Response.json(overrides.domains);
  return mockFetch(url,init);
}});
await assert.rejects(verifyPortal({core:{success:true,result:{bindings:coreSettings.result.bindings.map(binding=>binding.name==='IDENTITY'?{...binding,service:'other-production'}:binding)}}}),/PRODUCTION_CORE_CONSUMER_NOT_READY/);
await assert.rejects(verifyPortal({core:{success:true,result:{bindings:coreSettings.result.bindings.map(binding=>binding.name==='IDENTITY_WORKSPACE_ID'?{...binding,text:'other-workspace'}:binding)}}}),/WORKSPACE_MISMATCH/);
await assert.rejects(verifyPortal({domains:{success:true,result:[]}}),/DOMAIN_UNVERIFIED/);
await assert.rejects(verifyPortal({health:{ok:false}}),/PRODUCTION_CORE_CONSUMER_NOT_READY/);

await assert.rejects(
  resolveCyidProductionRuntime({
    accountId: 'account-ci',
    apiToken: 'token-ci',
    developmentService,
    fetchImpl: async () => Response.json({ success: false }, { status: 403 })
  }),
  /Unable to verify/
);

console.log('CYID production runtime bridge tests passed.');
