import { webcrypto } from 'node:crypto';
import {
  buildBackupSet,
  resolveBackupTopology,
  storeBackupSet,
  validateBackupSetBytes
} from '../src/gcs-backup.js';

if (!globalThis.crypto) globalThis.crypto = webcrypto;

function assert(condition, message) {
  if (!condition) throw new Error(message);
}

function expectCode(fn, expectedCode) {
  let caught = null;
  try { fn(); } catch (error) { caught = error; }
  assert(caught, `expected ${expectedCode}`);
  assert(caught.code === expectedCode, `expected ${expectedCode}, got ${caught.code}`);
}

assert(resolveBackupTopology({}) === 'legacy_gcs', 'default topology must remain legacy_gcs');
assert(resolveBackupTopology({ BACKUP_TOPOLOGY: 'legacy_gcs' }) === 'legacy_gcs', 'explicit legacy_gcs must be accepted');
expectCode(() => resolveBackupTopology({ BACKUP_TOPOLOGY: 'parallel_dual_provider' }), 'BACKUP_TOPOLOGY_UNSUPPORTED');

const tableData = {
  accounts: [{ id: 1, name: '現金', sort_order: 0, is_default: 1, color_slot: 1, created_at: '2026-09-01T00:00:00Z', archived_at: null }],
  category_groups: [{ id: 1, kind: 'expense', name: '支出', sort_order: 0, created_at: '2026-09-01T00:00:00Z' }],
  categories: [{ id: 1, kind: 'expense', group_id: 1, name: '一般支出', sort_order: 0, is_favorite: 1, created_at: '2026-09-01T00:00:00Z' }],
  transactions: [{ id: 1, tx_date: '2026-09-25', account_name: '現金', kind: 'expense', category_name: '一般支出', summary: 'Phase B', amount: 10, created_at: '2026-09-25T00:00:00Z', updated_at: '2026-09-25T00:00:00Z' }],
  opening_balance_overrides: [{ month: '2026-09', account_name: '現金', amount: 100, reason: '測試', created_at: '2026-09-01T00:00:00Z', updated_at: '2026-09-01T00:00:00Z', updated_by_employee_id: 'emp-1', updated_by_employee_no: '0001', updated_by_name: '管理員', updated_by_role: 'SUPER_ADMIN' }],
  opening_balance_audit: [{ id: 1, month: '2026-09', account_name: '現金', action: 'set', previous_amount: null, new_amount: 100, reason: '測試', actor_employee_id: 'emp-1', actor_employee_no: '0001', actor_name: '管理員', actor_role: 'SUPER_ADMIN', created_at: '2026-09-01T00:00:00Z' }],
  app_settings: [{ key: 'locked_through', value: '2026-08' }]
};

let prepareCount = 0;
class MockStatement {
  constructor(sql) { this.sql = sql; this.args = []; }
  bind(...args) { this.args = args; return this; }
  async first() {
    return this.sql.includes("FROM meta WHERE key = 'schema_version'") ? { value: '7' } : null;
  }
  async all() {
    const table = Object.keys(tableData).find(name => this.sql.includes(`FROM ${name}`));
    if (!table) throw new Error(`unexpected query: ${this.sql}`);
    const limit = Number(this.args.at(-2) || 1000);
    const offset = Number(this.args.at(-1) || 0);
    return { results: tableData[table].slice(offset, offset + limit) };
  }
}
const db = {
  prepare(sql) {
    prepareCount += 1;
    return new MockStatement(sql);
  }
};

const now = new Date('2026-09-26T03:30:00.000Z');
const backupSet = await buildBackupSet(db, now);
const prepareCountAfterBuild = prepareCount;

assert(Object.isFrozen(backupSet), 'BackupSet should be immutable at the outer object boundary');
assert(backupSet.backupId === '20260926T033000Z', 'backupId changed unexpectedly');
assert(backupSet.manifest.format === 'CYAccountingWebBackupSet', 'accepted backup format v2 must not change in Phase B');
assert(backupSet.manifest.formatVersion === 2, 'accepted backup format v2Version must remain 2 in Phase B');
assert(backupSet.manifest.app === 'CYAccountingWeb', 'accepted backup app marker changed');
assert(backupSet.dataKey === 'CYAccountingWeb/20260926T033000Z/data.json', 'backup format v2 data key changed');
assert(backupSet.manifestKey === 'CYAccountingWeb/20260926T033000Z/manifest.json', 'backup format v2 manifest key changed');

const compatibility = await validateBackupSetBytes(
  backupSet.manifestBytes,
  backupSet.dataBytes,
  backupSet.backupId
);
assert(compatibility.backupId === backupSet.backupId, 'backup format v2 compatibility reader backupId mismatch');
assert(compatibility.dataSha256 === backupSet.dataSha256, 'backup format v2 compatibility reader SHA mismatch');
assert(compatibility.totalRowCount === backupSet.totalRowCount, 'backup format v2 compatibility reader count mismatch');

const decoder = new TextDecoder();
const encoder = new TextEncoder();
assert(backupSet.manifest.dataFormat === 'CYAccountingWebBackup', 'inner data format must be declared');
assert(backupSet.manifest.dataFormatVersion === 2, 'inner data format v2 must be declared');
assert(backupSet.data.openingBalanceAudit.length === 1, 'audit must survive provider storage');
assert(backupSet.data.accounts[0].colorSlot === 1, 'current backup data must preserve account color slot');

// An actual legacy data shape remains readable without changing old objects.
const legacyData = {
  accounts: [{ id: 1, name: '現金', sortOrder: 0, isDefault: 1, createdAt: '2026-01-01' }],
  categoryGroups: [], categories: [], transactions: [],
  openingBalances: [{ month: '2026-01', accountName: '現金', amount: 100 }], appSettings: []
};
const legacyBytes = encoder.encode(JSON.stringify(legacyData));
const legacyManifest = structuredClone(backupSet.manifest);
delete legacyManifest.dataFormat;
delete legacyManifest.dataFormatVersion;
legacyManifest.appVersion = '0.17.0';
legacyManifest.schemaVersion = 3;
legacyManifest.counts = Object.fromEntries(Object.entries(legacyData).map(([key, rows]) => [key, rows.length]));
legacyManifest.totalRowCount = 2;
legacyManifest.files.data.byteSize = legacyBytes.byteLength;
legacyManifest.files.data.sha256 = Buffer.from(await crypto.subtle.digest('SHA-256', legacyBytes)).toString('hex');
const legacy = await validateBackupSetBytes(encoder.encode(JSON.stringify(legacyManifest)), legacyBytes);
assert(legacy.data.openingBalances[0].amount === 100, 'legacy opening data must remain readable');
assert(legacy.manifest.appVersion === '0.17.0', 'legacy manifest must not be rewritten');
const futureManifest = JSON.parse(decoder.decode(backupSet.manifestBytes));
futureManifest.format = 'CYBackupSet';
futureManifest.formatVersion = 1;
let futureRejected = false;
try {
  await validateBackupSetBytes(
    encoder.encode(JSON.stringify(futureManifest, null, 2) + '\n'),
    backupSet.dataBytes,
    backupSet.backupId
  );
} catch (error) {
  futureRejected = error.code === 'MANIFEST_CONTENT_INVALID';
}
assert(futureRejected, 'Phase B must not silently switch production output to the future common format');

function makeMemoryProvider(label) {
  const objects = new Map();
  const metadata = new Map();
  let sequence = 0;
  return {
    objects,
    provider: {
      kind: label,
      async putObject(key, bytes) {
        sequence += 1;
        objects.set(String(key), new Uint8Array(bytes));
        const info = {
          key: String(key),
          byteSize: bytes.byteLength,
          timeCreated: now.toISOString(),
          versionToken: `${label}-${sequence}`
        };
        metadata.set(String(key), info);
        return info;
      },
      async getObject(key) {
        const value = objects.get(String(key));
        if (!value) throw new Error(`${label}: object not found: ${key}`);
        return new Uint8Array(value);
      },
      async listObjects(prefix) {
        return [...metadata.values()].filter(item => item.key.startsWith(String(prefix || '')));
      },
      async deleteObject(key) {
        objects.delete(String(key));
        metadata.delete(String(key));
      }
    }
  };
}

const copyA = makeMemoryProvider('copy-a');
const copyB = makeMemoryProvider('copy-b');
await storeBackupSet(copyA.provider, backupSet, { now, cleanup: false });
await storeBackupSet(copyB.provider, backupSet, { now, cleanup: false });

assert(prepareCount === prepareCountAfterBuild, 'provider storage execution must not re-export D1');
for (const key of [backupSet.dataKey, backupSet.manifestKey]) {
  const a = copyA.objects.get(key);
  const b = copyB.objects.get(key);
  assert(a && b, `paired provider copies missing ${key}`);
  assert(Buffer.compare(Buffer.from(a), Buffer.from(b)) === 0, `paired provider bytes differ for ${key}`);
  const source = key === backupSet.dataKey ? backupSet.dataBytes : backupSet.manifestBytes;
  assert(Buffer.compare(Buffer.from(a), Buffer.from(source)) === 0, `provider bytes differ from built BackupSet for ${key}`);
}

console.log('Backup compatibility and export-once tests passed.');
