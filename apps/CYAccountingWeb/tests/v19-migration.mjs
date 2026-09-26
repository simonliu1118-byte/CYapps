import { webcrypto } from 'node:crypto';
import {
  handleV19MigrationApi,
  migrationDatasetDigest,
  validateMigrationStartPayload,
  validateOpeningBalanceRows,
  validateTransactionRows
} from '../src/v19-migration-api.js';

if (!globalThis.crypto) globalThis.crypto = webcrypto;

function assert(condition, message) {
  if (!condition) throw new Error(message);
}

async function sha256Text(value) {
  const bytes = new TextEncoder().encode(value);
  const digest = new Uint8Array(await crypto.subtle.digest('SHA-256', bytes));
  return Array.from(digest, byte => byte.toString(16).padStart(2, '0')).join('');
}

const historicalTransaction = {
  id: 7,
  txDate: '2024-03-12',
  accountName: '已刪除的舊帳戶',
  kind: 'expense',
  categoryName: '已刪除的舊科目',
  summary: '歷史資料',
  amount: 1234,
  createdAt: '2024-03-12T10:11:12',
  updatedAt: '2024-03-12T10:11:12'
};
const txValidation = validateTransactionRows([historicalTransaction]);
assert(txValidation.ok, 'historical transaction names must not require current master-data membership');
assert(txValidation.rows[0].accountName === '已刪除的舊帳戶', 'historical account label must be preserved');
assert(txValidation.rows[0].categoryName === '已刪除的舊科目', 'historical category label must be preserved');

const openingValidation = validateOpeningBalanceRows([{
  month: '2024-03',
  accountName: '歷史帳戶',
  amount: -987654321,
  createdAt: '2024-03-01T00:00:00',
  updatedAt: '2024-03-01T00:00:00'
}]);
assert(openingValidation.ok, 'opening balances must preserve desktop semantics including negative values');
assert(openingValidation.rows[0].amount === -987654321, 'opening balance must not inherit transaction 7-digit limit');

assert(!validateTransactionRows([{ ...historicalTransaction, amount: 10_000_000 }]).ok, 'transaction amount must remain limited to 9,999,999');
assert(!validateTransactionRows([{ ...historicalTransaction, txDate: '2026-02-30' }]).ok, 'invalid dates must be rejected');

const structureV2 = {
  accounts: [{ id: 3, name: '銀行', sortOrder: 0, isDefault: 1, createdAt: '2024-01-01T00:00:00' }],
  categoryGroups: [
    { id: 10, kind: 'income', name: '收入', sortOrder: 0, createdAt: '2024-01-01T00:00:00' },
    { id: 11, kind: 'expense', name: '支出', sortOrder: 0, createdAt: '2024-01-01T00:00:00' }
  ],
  categories: [
    { id: 20, kind: 'income', groupId: 10, name: '銷售', sortOrder: 0, isFavorite: 1, createdAt: '2024-01-01T00:00:00' },
    { id: 21, kind: 'expense', groupId: 11, name: '雜支', sortOrder: 0, isFavorite: 0, createdAt: '2024-01-01T00:00:00' }
  ],
  settings: [{ key: 'locked_through', value: '2024-01' }]
};
const structureShaV2 = await sha256Text(JSON.stringify(structureV2));
const transactionRows = [historicalTransaction];
const transactionSha = await sha256Text(JSON.stringify(transactionRows));
const manifestV2 = {
  format: 'CYAccountingLegacyMigration',
  formatVersion: 1,
  sourceSchemaVersion: 2,
  sourceFileName: 'CYaccounting.db',
  sourceFileSize: 4096,
  sourceFileSha256: 'a'.repeat(64),
  structureSha256: structureShaV2,
  datasetSha256: '',
  counts: {
    accounts: 1,
    categoryGroups: 2,
    categories: 2,
    transactions: 1,
    openingBalances: 0
  },
  transactionChunks: [{ index: 0, rowCount: 1, sha256: transactionSha }],
  openingBalanceChunks: []
};
manifestV2.datasetSha256 = await migrationDatasetDigest(manifestV2);

const validV2 = await validateMigrationStartPayload({ manifest: manifestV2, structure: structureV2 });
assert(validV2.ok, `valid schema v2 migration payload rejected: ${validV2.error || ''}`);

const tamperedDataset = await validateMigrationStartPayload({
  manifest: { ...manifestV2, datasetSha256: 'b'.repeat(64) },
  structure: structureV2
});
assert(!tamperedDataset.ok && tamperedDataset.code === 'MIGRATION_DATASET_DIGEST_MISMATCH', 'server must recompute dataset digest');

const tamperedStructure = structuredClone(structureV2);
tamperedStructure.accounts[0].name = '被改過';
const rejectedStructure = await validateMigrationStartPayload({ manifest: manifestV2, structure: tamperedStructure });
assert(!rejectedStructure.ok && rejectedStructure.code === 'MIGRATION_STRUCTURE_DIGEST_MISMATCH', 'server must reject modified master data');

const structureV1Input = {
  accounts: [{ id: 1, name: '現金', sortOrder: 0, isDefault: 1, createdAt: '2023-01-01T00:00:00' }],
  categoryGroups: [
    { id: 1, kind: 'income', name: '收入', sortOrder: 0, createdAt: '2023-01-01T00:00:00' },
    { id: 2, kind: 'expense', name: '支出', sortOrder: 0, createdAt: '2023-01-01T00:00:00' }
  ],
  categories: [
    { id: 1, kind: 'income', groupId: 1, name: '收入科目', sortOrder: 0, createdAt: '2023-01-01T00:00:00' },
    { id: 2, kind: 'expense', groupId: 2, name: '支出科目', sortOrder: 0, createdAt: '2023-01-01T00:00:00' }
  ],
  settings: []
};
const normalizedV1Structure = {
  ...structureV1Input,
  categories: structureV1Input.categories.map(row => ({ ...row, isFavorite: 0 }))
};
const manifestV1 = {
  format: 'CYAccountingLegacyMigration',
  formatVersion: 1,
  sourceSchemaVersion: 1,
  sourceFileName: 'old.db',
  sourceFileSize: 2048,
  sourceFileSha256: 'c'.repeat(64),
  structureSha256: await sha256Text(JSON.stringify(normalizedV1Structure)),
  datasetSha256: '',
  counts: { accounts: 1, categoryGroups: 2, categories: 2, transactions: 0, openingBalances: 0 },
  transactionChunks: [],
  openingBalanceChunks: []
};
manifestV1.datasetSha256 = await migrationDatasetDigest(manifestV1);
const validV1 = await validateMigrationStartPayload({ manifest: manifestV1, structure: structureV1Input });
assert(validV1.ok, `schema v1 payload should be accepted: ${validV1.error || ''}`);
assert(validV1.structure.categories.every(row => row.isFavorite === 0), 'schema v1 categories must default isFavorite to zero');

const forbidden = await handleV19MigrationApi(
  new Request('https://example.invalid/api/migration/sqlite/status'),
  { DB: {} },
  { role: 'ADMIN' }
);
assert(forbidden.status === 403, 'ADMIN must not access migration API');
const forbiddenBody = await forbidden.json();
assert(forbiddenBody.code === 'MIGRATION_FORBIDDEN', 'migration permission error code mismatch');

console.log('V0.19 legacy migration validation, digest, historical-data and authorization tests passed.');
