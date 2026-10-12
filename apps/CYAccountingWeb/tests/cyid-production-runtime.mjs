import assert from 'node:assert/strict';
import {
  deriveCyidProductionServiceName,
  parseCyidProductionSettings,
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

let requestedUrl = '';
let authorization = '';
const runtime = await resolveCyidProductionRuntime({
  accountId: 'account-ci',
  apiToken: 'token-ci',
  developmentService,
  fetchImpl: async (url, init) => {
    requestedUrl = String(url);
    authorization = new Headers(init.headers).get('authorization') || '';
    return Response.json(payload);
  }
});
assert.equal(runtime.applicationId, 'CYACC_PROD_TEST');
assert.match(requestedUrl, /workers\/scripts\/cyid-development-provider-prod\/settings$/);
assert.equal(authorization, 'Bearer token-ci');

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
