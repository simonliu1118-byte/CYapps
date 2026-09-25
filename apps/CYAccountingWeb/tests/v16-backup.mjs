import { buildBackupPackage, encryptSecret, decryptSecret } from '../src/v16-backup.js';

function assert(condition, message) {
  if (!condition) throw new Error(message);
}

const tableData = {
  accounts: [{ id: 1, name: '現金', sort_order: 0, is_default: 1, created_at: '2026-09-01T00:00:00Z' }],
  category_groups: [
    { id: 1, kind: 'income', name: '收入分類', sort_order: 0, created_at: '2026-09-01T00:00:00Z' },
    { id: 2, kind: 'expense', name: '支出分類', sort_order: 0, created_at: '2026-09-01T00:00:00Z' }
  ],
  categories: [
    { id: 1, kind: 'income', group_id: 1, name: '一般收入', sort_order: 0, is_favorite: 0, created_at: '2026-09-01T00:00:00Z' },
    { id: 2, kind: 'expense', group_id: 2, name: '一般支出', sort_order: 0, is_favorite: 1, created_at: '2026-09-01T00:00:00Z' }
  ],
  transactions: [
    { id: 10, tx_date: '2026-09-24', account_name: '現金', kind: 'expense', category_name: '一般支出', summary: '文具', amount: 20, created_at: '2026-09-24T01:00:00Z', updated_at: '2026-09-24T01:00:00Z' }
  ],
  opening_balances: [
    { month: '2026-09', account_name: '現金', amount: 10000, created_at: '2026-09-01T00:00:00Z', updated_at: '2026-09-01T00:00:00Z' }
  ],
  app_settings: [
    { key: 'locked_through', value: '2026-08' },
    { key: 'frequent_summary_recent_count', value: '100' }
  ]
};

class MockStatement {
  constructor(sql) {
    this.sql = sql;
    this.args = [];
  }
  bind(...args) {
    this.args = args;
    return this;
  }
  async first() {
    if (this.sql.includes("FROM meta WHERE key = 'schema_version'")) return { value: '3' };
    return null;
  }
  async all() {
    const table = Object.keys(tableData).find(name => this.sql.includes(`FROM ${name}`));
    if (!table) throw new Error(`unexpected backup query: ${this.sql}`);
    const limit = Number(this.args.at(-2) || 1000);
    const offset = Number(this.args.at(-1) || 0);
    return { results: tableData[table].slice(offset, offset + limit) };
  }
}

const db = { prepare(sql) { return new MockStatement(sql); } };
const backup = await buildBackupPackage(db, new Date('2026-09-25T03:30:00.000Z'));
const parsed = JSON.parse(new TextDecoder().decode(backup.bytes));

assert(parsed.manifest.format === 'CYAccountingWebBackup', 'backup format mismatch');
assert(parsed.manifest.formatVersion === 1, 'backup format version mismatch');
assert(parsed.manifest.schemaVersion === 3, 'schema version mismatch');
assert(parsed.manifest.totalRowCount === 9, `row count mismatch: ${parsed.manifest.totalRowCount}`);
assert(parsed.manifest.dataSha256 === backup.dataSha256, 'data checksum mismatch');
assert(/^[0-9a-f]{64}$/.test(backup.fileSha256), 'file checksum must be SHA-256');
assert(backup.fileName === 'CYAccountingWeb_backup_20260925T033000Z.json', `filename mismatch: ${backup.fileName}`);
assert(parsed.data.transactions[0].summary === '文具', 'transaction data missing');
assert(!('webSessions' in parsed.data), 'web sessions must never be backed up');
assert(!('backupIntegrations' in parsed.data), 'OAuth integration data must never be backed up');

const keyBytes = Uint8Array.from({ length: 32 }, (_, index) => index + 1);
let binary = '';
for (const byte of keyBytes) binary += String.fromCharCode(byte);
const key = btoa(binary);
const secret = 'refresh-token-test-value';
const encrypted = await encryptSecret(secret, key);
assert(encrypted.startsWith('v1:'), 'encrypted token version missing');
assert(encrypted !== secret && !encrypted.includes(secret), 'encrypted token leaked plaintext');
assert(await decryptSecret(encrypted, key) === secret, 'AES-GCM token roundtrip failed');

console.log('V0.16 backup tests passed');
