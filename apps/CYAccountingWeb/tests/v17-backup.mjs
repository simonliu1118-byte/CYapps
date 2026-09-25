import { webcrypto } from 'node:crypto';
import { createServiceAccountJwt, gcsConfigReady, normalizePrivateKeyPem } from '../src/v17-backup.js';

if (!globalThis.crypto) globalThis.crypto = webcrypto;

function assert(condition, message) {
  if (!condition) throw new Error(message);
}

assert(!gcsConfigReady({}), 'empty GCS config must be rejected');

const pair = await crypto.subtle.generateKey({
  name: 'RSASSA-PKCS1-v1_5',
  modulusLength: 2048,
  publicExponent: new Uint8Array([1, 0, 1]),
  hash: 'SHA-256'
}, true, ['sign', 'verify']);

const pkcs8 = new Uint8Array(await crypto.subtle.exportKey('pkcs8', pair.privateKey));
let binary = '';
for (const byte of pkcs8) binary += String.fromCharCode(byte);
const base64 = btoa(binary).match(/.{1,64}/g).join('\n');
const pem = `-----BEGIN PRIVATE KEY-----\n${base64}\n-----END PRIVATE KEY-----`;
const escapedPem = pem.replaceAll('\n', '\\n');
const env = {
  GCS_SERVICE_ACCOUNT_EMAIL: 'cy-backup@test-project.iam.gserviceaccount.com',
  GCS_BUCKET_NAME: 'cyaccounting-backup-test',
  GCS_PRIVATE_KEY: escapedPem
};

assert(gcsConfigReady(env), 'valid GCS config should be accepted');
assert(normalizePrivateKeyPem(escapedPem) === pem, 'escaped PEM newlines should normalize');

const jwt = await createServiceAccountJwt(env, new Date('2026-09-25T03:30:00Z'));
const [head, payload, signature] = jwt.split('.');
assert(head && payload && signature, 'JWT must contain 3 segments');

const decodeJson = value => JSON.parse(Buffer.from(value.replaceAll('-', '+').replaceAll('_', '/'), 'base64').toString('utf8'));
const claims = decodeJson(payload);
assert(claims.iss === env.GCS_SERVICE_ACCOUNT_EMAIL, 'JWT issuer mismatch');
assert(claims.scope === 'https://www.googleapis.com/auth/devstorage.read_write', 'JWT scope mismatch');
assert(claims.aud === 'https://oauth2.googleapis.com/token', 'JWT audience mismatch');
assert(claims.exp - claims.iat === 3600, 'JWT lifetime must be 1 hour');

const signatureBytes = Buffer.from(signature.replaceAll('-', '+').replaceAll('_', '/'), 'base64');
const verified = await crypto.subtle.verify(
  { name: 'RSASSA-PKCS1-v1_5' },
  pair.publicKey,
  signatureBytes,
  new TextEncoder().encode(`${head}.${payload}`)
);
assert(verified, 'JWT signature verification failed');

console.log('V0.17 GCS backup auth tests passed.');
