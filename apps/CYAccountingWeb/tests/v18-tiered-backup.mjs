import { webcrypto } from 'node:crypto';
import { createR2BackupStorageProvider, r2ConfigReady } from '../src/r2-backup-provider.js';
import {
  backupPackageDigest,
  resolveTieredBackupTopology,
  runParallelBackup
} from '../src/v18-backup.js';

if (!globalThis.crypto) globalThis.crypto = webcrypto;

function assert(condition, message) {
  if (!condition) throw new Error(message);
}

async function expectReject(fn, code) {
  let caught = null;
  try { await fn(); } catch (error) { caught = error; }
  assert(caught, `expected rejection ${code}`);
  assert(caught.code === code, `expected ${code}, got ${caught.code}`);
  return caught;
}

assert(resolveTieredBackupTopology({}) === 'legacy_gcs', 'default topology must remain legacy_gcs');
assert(resolveTieredBackupTopology({ BACKUP_TOPOLOGY: 'parallel_dual_provider' }) === 'parallel_dual_provider', 'parallel topology must be accepted');
await expectReject(async () => resolveTieredBackupTopology({ BACKUP_TOPOLOGY: 'tiered_direct' }), 'BACKUP_TOPOLOGY_UNSUPPORTED');

const r2Objects = new Map();
let r2Version = 0;
const r2Bucket = {
  async put(key, bytes, options = {}) {
    const copy = new Uint8Array(bytes);
    r2Version += 1;
    const object = {
      key,
      version: String(r2Version),
      size: copy.byteLength,
      uploaded: new Date('2026-09-26T03:30:00Z'),
      customMetadata: options.customMetadata || {}
    };
    r2Objects.set(key, { bytes: copy, object });
    return object;
  },
  async get(key) {
    const stored = r2Objects.get(key);
    if (!stored) return null;
    return {
      ...stored.object,
      async arrayBuffer() {
        return stored.bytes.buffer.slice(stored.bytes.byteOffset, stored.bytes.byteOffset + stored.bytes.byteLength);
      }
    };
  },
  async list({ prefix = '' } = {}) {
    return {
      objects: [...r2Objects.values()].map(item => item.object).filter(item => item.key.startsWith(prefix)),
      truncated: false
    };
  },
  async delete(key) { r2Objects.delete(key); }
};
assert(r2ConfigReady({ BACKUP_R2: r2Bucket }), 'R2 binding should be detected');
const r2Provider = createR2BackupStorageProvider({ BACKUP_R2: r2Bucket });
const smoke = new TextEncoder().encode('{"r2":true}\n');
const put = await r2Provider.putObject('CYAccountingWeb/smoke/data.json', smoke, { role: 'data' });
assert(put.versionToken === '1', 'R2 version must normalize to versionToken');
assert(new TextDecoder().decode(await r2Provider.getObject('CYAccountingWeb/smoke/data.json')) === '{"r2":true}\n', 'R2 getObject mismatch');
assert((await r2Provider.listObjects('CYAccountingWeb/')).length === 1, 'R2 listObjects mismatch');
await r2Provider.deleteObject('CYAccountingWeb/smoke/data.json', put.versionToken);
assert((await r2Provider.listObjects('CYAccountingWeb/')).length === 0, 'R2 deleteObject mismatch');

const tableData = {
  accounts: [{ id: 1, name: '現金', sort_order: 0, is_default: 1, created_at: '2026-09-01T00:00:00Z' }],
  category_groups: [{ id: 1, kind: 'expense', name: '支出', sort_order: 0, created_at: '2026-09-01T00:00:00Z' }],
  categories: [{ id: 1, kind: 'expense', group_id: 1, name: '一般支出', sort_order: 0, is_favorite: 1, created_at: '2026-09-01T00:00:00Z' }],
  transactions: [{ id: 1, tx_date: '2026-09-25', account_name: '現金', kind: 'expense', category_name: '一般支出', summary: '測試', amount: 10, created_at: '2026-09-25T00:00:00Z', updated_at: '2026-09-25T00:00:00Z' }],
  opening_balances: [{ month: '2026-09', account_name: '現金', amount: 100, created_at: '2026-09-01T00:00:00Z', updated_at: '2026-09-01T00:00:00Z' }],
  app_settings: [{ key: 'locked_through', value: '2026-08' }]
};

class MockStatement {
  constructor(db, sql) { this.db = db; this.sql = sql; this.args = []; }
  bind(...args) { this.args = args; return this; }
  async first() {
    if (this.sql.includes("FROM meta WHERE key = 'schema_version'")) return { value: '4' };
    return null;
  }
  async all() {
    const table = Object.keys(tableData).find(name => this.sql.includes(`FROM ${name}`));
    if (!table) return { results: [] };
    this.db.sourceReads.push(table);
    const limit = Number(this.args.at(-2) || 1000);
    const offset = Number(this.args.at(-1) || 0);
    return { results: tableData[table].slice(offset, offset + limit) };
  }
  async run() {
    this.db.writes.push({ sql: this.sql.replace(/\s+/g, ' ').trim(), args: [...this.args] });
    return { success: true };
  }
}
class MockDb {
  constructor() { this.sourceReads = []; this.writes = []; }
  prepare(sql) { return new MockStatement(this, sql); }
}

function memoryProvider(kind, { failPut = false } = {}) {
  const objects = new Map();
  let version = 0;
  return {
    kind,
    objects,
    async putObject(key, bytes) {
      if (failPut) {
        const error = new Error(`${kind} injected failure`);
        error.code = `${kind.toUpperCase()}_PUT_FAILED`;
        throw error;
      }
      version += 1;
      objects.set(key, new Uint8Array(bytes));
      return { key, versionToken: String(version), byteSize: bytes.byteLength, timeCreated: '2026-09-26T03:30:00Z' };
    },
    async getObject(key) {
      const value = objects.get(key);
      if (!value) throw new Error(`missing ${key}`);
      return new Uint8Array(value);
    },
    async listObjects() { return []; },
    async deleteObject(key) { objects.delete(key); }
  };
}

const db = new MockDb();
const r2Memory = memoryProvider('cloudflare_r2');
const gcsMemory = memoryProvider('google_cloud_storage');
const result = await runParallelBackup(
  { DB: db },
  'scheduled',
  {
    topology: 'parallel_dual_provider',
    now: new Date('2026-09-26T03:30:00Z'),
    r2Provider: r2Memory,
    gcsProvider: gcsMemory
  }
);
assert(result.ok, 'dual-provider backup should succeed');
assert(result.copies.length === 2 && result.copies.every(copy => copy.status === 'success'), 'both copies must verify');
assert(db.sourceReads.length === 6, `expected one D1 export (6 source reads), got ${db.sourceReads.length}`);
assert(new Set(db.sourceReads).size === 6, 'each exported table must be read once');
assert(r2Memory.objects.size === 2 && gcsMemory.objects.size === 2, 'both providers must receive data + manifest');
for (const [key, r2Bytes] of r2Memory.objects) {
  const gcsBytes = gcsMemory.objects.get(key);
  assert(gcsBytes, `GCS missing ${key}`);
  assert(Buffer.compare(Buffer.from(r2Bytes), Buffer.from(gcsBytes)) === 0, `provider bytes differ for ${key}`);
}
assert(result.packageSha256.length === 64, 'package digest must be SHA-256');
assert(db.writes.some(item => item.sql.startsWith('INSERT INTO backup_sets')), 'logical backup catalog row missing');
assert(db.writes.filter(item => item.sql.startsWith('INSERT INTO backup_copies')).length === 2, 'provider copy catalog rows missing');
assert(db.writes.some(item => item.sql.startsWith('INSERT INTO backup_runs')), 'legacy GCS run compatibility row missing');

const digestAgain = await backupPackageDigest({
  manifestBytes: gcsMemory.objects.get(`CYAccountingWeb/${result.backupId}/manifest.json`),
  dataBytes: gcsMemory.objects.get(`CYAccountingWeb/${result.backupId}/data.json`)
});
assert(digestAgain === result.packageSha256, 'package digest must be reproducible from stored bytes');

const failureDb = new MockDb();
const goodR2 = memoryProvider('cloudflare_r2');
const failingGcs = memoryProvider('google_cloud_storage', { failPut: true });
const partial = await expectReject(() => runParallelBackup(
  { DB: failureDb },
  'manual',
  {
    topology: 'parallel_dual_provider',
    now: new Date('2026-09-26T03:31:00Z'),
    r2Provider: goodR2,
    gcsProvider: failingGcs
  }
), 'BACKUP_COPY_PARTIAL');
assert(partial.backupResult.copies.find(copy => copy.provider === 'cloudflare_r2')?.status === 'success', 'R2 success must survive GCS failure');
assert(partial.backupResult.copies.find(copy => copy.provider === 'google_cloud_storage')?.status === 'failed', 'GCS failure must be distinct');
assert(goodR2.objects.size === 2, 'successful R2 copy must not be removed when GCS fails');

console.log('Phase C R2 provider, export-once dual copy, digest parity and failure isolation tests passed.');
