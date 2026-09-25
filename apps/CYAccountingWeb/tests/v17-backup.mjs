import { webcrypto } from 'node:crypto';
import { assertBackupStorageProvider } from '../src/backup-storage-provider.js';
import {
  createGcsBackupStorageProvider,
  createServiceAccountJwt,
  gcsConfigReady,
  readGcsConfig
} from '../src/gcs-backup-provider.js';
import {
  buildV17BackupSet,
  cleanupExpiredBackups,
  verifyBackupSet
} from '../src/v17-backup.js';

if (!globalThis.crypto) globalThis.crypto = webcrypto;

function assert(condition, message) {
  if (!condition) throw new Error(message);
}

async function expectReject(fn, expectedCode) {
  let caught = null;
  try { await fn(); } catch (error) { caught = error; }
  assert(caught, `expected rejection: ${expectedCode}`);
  assert(caught.code === expectedCode, `expected ${expectedCode}, got ${caught.code}`);
}

assert(!gcsConfigReady({}), 'empty GCS config must be rejected');
assert(!gcsConfigReady({ GCS_BUCKET: 'test', GCS_SERVICE_ACCOUNT_JSON: '{bad' }), 'malformed JSON must be rejected');

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
const privateKey = `-----BEGIN PRIVATE KEY-----\n${base64}\n-----END PRIVATE KEY-----`;
const env = {
  GCS_BUCKET: 'cyaccounting-backup-test',
  GCS_SERVICE_ACCOUNT_JSON: JSON.stringify({
    type: 'service_account',
    project_id: 'test-project',
    client_email: 'cy-backup@test-project.iam.gserviceaccount.com',
    private_key: privateKey
  })
};
assert(gcsConfigReady(env), 'valid GCS config should be accepted');
const config = readGcsConfig(env);
assert(config.bucket === 'cyaccounting-backup-test', 'bucket parse mismatch');
assert(config.clientEmail === 'cy-backup@test-project.iam.gserviceaccount.com', 'client email parse mismatch');

const jwt = await createServiceAccountJwt(env, new Date('2026-09-26T03:30:00Z'));
const [head, payload, signature] = jwt.split('.');
assert(head && payload && signature, 'JWT must contain 3 segments');
const decodeJson = value => JSON.parse(Buffer.from(value.replaceAll('-', '+').replaceAll('_', '/'), 'base64').toString('utf8'));
const claims = decodeJson(payload);
assert(claims.iss === config.clientEmail, 'JWT issuer mismatch');
assert(claims.scope === 'https://www.googleapis.com/auth/devstorage.read_write', 'JWT scope mismatch');
assert(claims.aud === 'https://oauth2.googleapis.com/token', 'JWT audience mismatch');
assert(claims.exp - claims.iat === 3600, 'JWT lifetime must be 1 hour');
const signatureBytes = Buffer.from(signature.replaceAll('-', '+').replaceAll('_', '/'), 'base64');
assert(await crypto.subtle.verify(
  { name: 'RSASSA-PKCS1-v1_5' }, pair.publicKey, signatureBytes,
  new TextEncoder().encode(`${head}.${payload}`)
), 'JWT signature verification failed');

const stored = new Map();
const callLog = [];
const fetchImpl = async (url, options = {}) => {
  const target = String(url);
  callLog.push([target, options.method || 'GET']);
  if (target === 'https://oauth2.googleapis.com/token') {
    return new Response(JSON.stringify({ access_token: 'test-token', expires_in: 3600 }), {
      status: 200, headers: { 'content-type': 'application/json' }
    });
  }
  if (target.includes('/upload/storage/v1/') && target.includes('uploadType=resumable')) {
    const body = JSON.parse(options.body);
    return new Response('', { status: 200, headers: { location: `https://upload.test/${encodeURIComponent(body.name)}` } });
  }
  if (target.startsWith('https://upload.test/')) {
    const key = decodeURIComponent(target.slice('https://upload.test/'.length));
    const bytes = new Uint8Array(options.body);
    stored.set(key, bytes);
    return new Response(JSON.stringify({ name: key, generation: '1', size: String(bytes.byteLength) }), {
      status: 200, headers: { 'content-type': 'application/json' }
    });
  }
  if (target.includes('/storage/v1/b/') && target.includes('?alt=media')) {
    const encoded = target.split('/o/')[1].split('?')[0];
    const key = decodeURIComponent(encoded);
    const bytes = stored.get(key);
    return bytes ? new Response(bytes, { status: 200 }) : new Response('', { status: 404 });
  }
  if (target.includes('/storage/v1/b/') && target.includes('/o?')) {
    const items = [...stored.entries()].map(([name, bytes]) => ({
      name, timeCreated: '2026-09-26T00:00:00Z', generation: '1', size: String(bytes.byteLength)
    }));
    return new Response(JSON.stringify({ items }), { status: 200, headers: { 'content-type': 'application/json' } });
  }
  if (target.includes('/storage/v1/b/') && options.method === 'DELETE') {
    const encoded = target.split('/o/')[1].split('?')[0];
    stored.delete(decodeURIComponent(encoded));
    return new Response(null, { status: 204 });
  }
  return new Response('', { status: 500 });
};
const provider = assertBackupStorageProvider(createGcsBackupStorageProvider(env, { fetchImpl }));
const smokeBytes = new TextEncoder().encode('{"ok":true}\n');
await provider.putObject('CYAccountingWeb/test/data.json', smokeBytes, { role: 'data' });
assert(stored.has('CYAccountingWeb/test/data.json'), 'putObject did not upload object');
const got = await provider.getObject('CYAccountingWeb/test/data.json');
assert(new TextDecoder().decode(got) === '{"ok":true}\n', 'getObject bytes mismatch');
const listed = await provider.listObjects('CYAccountingWeb/');
assert(listed.some(item => item.key === 'CYAccountingWeb/test/data.json'), 'listObjects missing uploaded object');
await provider.deleteObject('CYAccountingWeb/test/data.json', '1');
assert(!stored.has('CYAccountingWeb/test/data.json'), 'deleteObject did not delete object');
assert(callLog.filter(([url]) => url === 'https://oauth2.googleapis.com/token').length === 1, 'access token should be cached per provider instance');

const deniedProvider = createGcsBackupStorageProvider(env, {
  fetchImpl: async (url) => String(url) === 'https://oauth2.googleapis.com/token'
    ? new Response(JSON.stringify({ access_token: 'test-token', expires_in: 3600 }), { status: 200, headers: { 'content-type': 'application/json' } })
    : new Response(JSON.stringify({ error: { code: 403 } }), { status: 403, headers: { 'content-type': 'application/json' } })
});
await expectReject(() => deniedProvider.listObjects('CYAccountingWeb/'), 'GCS_LIST_403');

const tableData = {
  accounts: [{ id: 1, name: '現金', sort_order: 0, is_default: 1, created_at: '2026-09-01T00:00:00Z' }],
  category_groups: [{ id: 1, kind: 'expense', name: '支出', sort_order: 0, created_at: '2026-09-01T00:00:00Z' }],
  categories: [{ id: 1, kind: 'expense', group_id: 1, name: '一般支出', sort_order: 0, is_favorite: 1, created_at: '2026-09-01T00:00:00Z' }],
  transactions: [{ id: 1, tx_date: '2026-09-25', account_name: '現金', kind: 'expense', category_name: '一般支出', summary: '測試', amount: 10, created_at: '2026-09-25T00:00:00Z', updated_at: '2026-09-25T00:00:00Z' }],
  opening_balances: [{ month: '2026-09', account_name: '現金', amount: 100, created_at: '2026-09-01T00:00:00Z', updated_at: '2026-09-01T00:00:00Z' }],
  app_settings: [{ key: 'locked_through', value: '2026-08' }]
};
class MockStatement {
  constructor(sql) { this.sql = sql; this.args = []; }
  bind(...args) { this.args = args; return this; }
  async first() { return this.sql.includes("FROM meta WHERE key = 'schema_version'") ? { value: '3' } : null; }
  async all() {
    const table = Object.keys(tableData).find(name => this.sql.includes(`FROM ${name}`));
    if (!table) throw new Error(`unexpected query: ${this.sql}`);
    const limit = Number(this.args.at(-2) || 1000);
    const offset = Number(this.args.at(-1) || 0);
    return { results: tableData[table].slice(offset, offset + limit) };
  }
}
const db = { prepare(sql) { return new MockStatement(sql); } };
const backupSet = await buildV17BackupSet(db, new Date('2026-09-26T03:30:00.000Z'));
assert(backupSet.backupId === '20260926T033000Z', 'backupId mismatch');
assert(backupSet.dataKey.endsWith('/data.json'), 'data.json key missing');
assert(backupSet.manifestKey.endsWith('/manifest.json'), 'manifest.json key missing');
assert(backupSet.manifest.format === 'CYAccountingWebBackupSet', 'backup set format mismatch');
assert(backupSet.manifest.appVersion === '0.17.0', 'backup app version mismatch');
assert(backupSet.manifest.files.data.sha256 === backupSet.dataSha256, 'manifest data SHA mismatch');

const fakeObjects = new Map([
  [backupSet.dataKey, backupSet.dataBytes],
  [backupSet.manifestKey, backupSet.manifestBytes]
]);
const fakeProvider = assertBackupStorageProvider({
  async putObject() { throw new Error('not used'); },
  async getObject(key) { return fakeObjects.get(key); },
  async listObjects() { return []; },
  async deleteObject() {}
});
assert(await verifyBackupSet(fakeProvider, backupSet), 'backup set verification failed');

const deleted = [];
const retentionProvider = assertBackupStorageProvider({
  async putObject() {},
  async getObject() {},
  async listObjects() {
    return [
      { key: 'CYAccountingWeb/20260910T033000Z/data.json', timeCreated: '2026-09-10T03:30:00Z', generation: '1' },
      { key: 'CYAccountingWeb/20260910T033000Z/manifest.json', timeCreated: '2026-09-10T03:30:01Z', generation: '1' },
      { key: 'CYAccountingWeb/20260920T033000Z/data.json', timeCreated: '2026-09-20T03:30:00Z', generation: '1' },
      { key: 'other-app/file.json', timeCreated: '2026-01-01T00:00:00Z', generation: '1' }
    ];
  },
  async deleteObject(key) { deleted.push(key); }
});
await cleanupExpiredBackups(retentionProvider, 14, new Date('2026-09-26T03:30:00Z'));
assert(deleted.length === 2, `expected 2 expired managed objects deleted, got ${deleted.length}`);
assert(deleted.every(key => key.includes('20260910T033000Z')), 'retention deleted wrong objects');

console.log('V0.17 GCS provider and backup-set tests passed.');
