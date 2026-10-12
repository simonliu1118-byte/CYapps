import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const HERE = path.dirname(fileURLToPath(import.meta.url));
const CYACC_ROOT = path.resolve(HERE, '..');
const CYID_ROOT = path.resolve(CYACC_ROOT, '..', 'CYCloudIdentity');
const read = (...parts) => fs.readFileSync(path.join(...parts), 'utf8');

const providerIndex = read(CYID_ROOT, 'src', 'index.ts');
const providerAuth = read(CYID_ROOT, 'src', 'auth.ts');
const providerInitialAccess = read(CYID_ROOT, 'src', 'initial-access.ts');
const consumerAdapter = read(CYACC_ROOT, 'src', 'identity-adapter.js');
const consumerApp = read(CYACC_ROOT, 'src', 'app.js');

for (const route of [
  '/v1/identity/login',
  '/v1/identity/session/resolve',
  '/v1/identity/logout',
  '/v1/identity/password-recovery/start',
  '/v1/identity/password-recovery/confirm'
]) {
  assert.match(providerIndex, new RegExp(route.replaceAll('/', '\\/')));
  assert.match(consumerAdapter, new RegExp(route.replaceAll('/', '\\/')));
}

assert.match(providerAuth, /x-identity-application/i);
assert.match(consumerAdapter, /x-identity-application/i);
assert.match(providerAuth, /workspaceRole/);
assert.match(consumerAdapter, /workspaceRole/);
assert.match(providerAuth, /employeeRevision/);
assert.match(consumerAdapter, /employeeRevision/);
assert.match(providerInitialAccess, /applicationId/);
assert.match(providerInitialAccess, /CORE_ACCOUNT_APPLICATION_ID/);
assert.match(consumerAdapter, /FIRST_LOGIN_REQUIRED/);

assert.doesNotMatch(consumerAdapter, /\/v1\/web-auth/);
assert.doesNotMatch(consumerApp, /\/v1\/web-auth/);
assert.doesNotMatch(consumerApp, /web_sessions/);

console.log('CYID provider/consumer source contract checks passed.');
