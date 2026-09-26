import assert from 'node:assert/strict';
import { normalizeDesktopSnapshot, planDesktopMigration } from '../src/v19-migration.js';

const SHA = 'a'.repeat(64);
const NOW = '2026-09-27T00:00:00.000Z';

function sourceSnapshot(overrides = {}) {
  return {
    source: { schemaVersion: 2, fileName: 'CYaccounting.db', fileSize: 4096, fileSha256: SHA },
    accounts: [
      { name: '現金', sortOrder: 0, isDefault: 1, createdAt: NOW },
      { name: '銀行', sortOrder: 1, isDefault: 0, createdAt: NOW }
    ],
    groups: [
      { kind: 'income', name: '收入', sortOrder: 0, createdAt: NOW },
      { kind: 'expense', name: '支出', sortOrder: 0, createdAt: NOW }
    ],
    categories: [
      { kind: 'income', groupName: '收入', name: '銷售', sortOrder: 0, isFavorite: 1, createdAt: NOW },
      { kind: 'expense', groupName: '支出', name: '雜支', sortOrder: 0, isFavorite: 0, createdAt: NOW }
    ],
    transactions: [
      { sourceId: 1, txDate: '2026-09-01', accountName: '現金', kind: 'income', categoryName: '銷售', summary: 'A', amount: 100, createdAt: NOW, updatedAt: NOW },
      { sourceId: 2, txDate: '2026-09-01', accountName: '現金', kind: 'income', categoryName: '銷售', summary: 'A', amount: 100, createdAt: NOW, updatedAt: NOW }
    ],
    openingBalances: [
      { month: '2026-09', accountName: '現金', amount: 500, createdAt: NOW, updatedAt: NOW }
    ],
    lockedThrough: '2026-08',
    ...overrides
  };
}

function pristineTarget(overrides = {}) {
  return {
    accounts: [{ name: '現金', sortOrder: 0, isDefault: 1 }],
    groups: [
      { kind: 'income', name: '收入分類', sortOrder: 0 },
      { kind: 'expense', name: '支出分類', sortOrder: 0 }
    ],
    categories: [
      { kind: 'income', name: '一般收入', groupName: '收入分類', sortOrder: 0, isFavorite: 0 },
      { kind: 'expense', name: '一般支出', groupName: '支出分類', sortOrder: 0, isFavorite: 0 }
    ],
    openingBalances: [],
    transactions: [],
    transactionCount: 0,
    openingCount: 0,
    lockedThrough: null,
    history: [],
    pristineSeed: true,
    ...overrides
  };
}

function mergeTarget(overrides = {}) {
  return {
    accounts: [{ name: '現金', sortOrder: 0, isDefault: 1 }],
    groups: [{ kind: 'income', name: '收入', sortOrder: 0 }],
    categories: [{ kind: 'income', name: '銷售', groupName: '收入', sortOrder: 0, isFavorite: 0 }],
    openingBalances: [],
    transactions: [],
    transactionCount: 1,
    openingCount: 0,
    lockedThrough: null,
    history: [],
    pristineSeed: false,
    ...overrides
  };
}

const normalized = normalizeDesktopSnapshot(sourceSnapshot());
assert.equal(normalized.source.schemaVersion, 2);
assert.equal(normalized.transactions.length, 2);
assert.equal(normalized.accounts.find(item => item.isDefault === 1)?.name, '現金');

const v1 = normalizeDesktopSnapshot(sourceSnapshot({
  source: { schemaVersion: 1, fileName: 'old.db', fileSize: 2048, fileSha256: 'b'.repeat(64) },
  categories: sourceSnapshot().categories.map(({ isFavorite, ...item }) => ({ ...item, isFavorite: 0 }))
}));
assert.equal(v1.source.schemaVersion, 1);

assert.throws(() => normalizeDesktopSnapshot(sourceSnapshot({
  source: { schemaVersion: 3, fileName: 'future.db', fileSize: 4096, fileSha256: SHA }
})), error => error?.code === 'UNSUPPORTED_DESKTOP_SCHEMA');

assert.throws(() => normalizeDesktopSnapshot(sourceSnapshot({
  transactions: [{ ...sourceSnapshot().transactions[0], amount: 10_000_000 }]
})), error => error?.code === 'SOURCE_AMOUNT_OUT_OF_RANGE');

assert.throws(() => normalizeDesktopSnapshot(sourceSnapshot({
  accounts: [
    { name: '現金', sortOrder: 0, isDefault: 1, createdAt: NOW },
    { name: '現金', sortOrder: 1, isDefault: 0, createdAt: NOW }
  ]
})), error => error?.code === 'SOURCE_DUPLICATE_KEY');

const pristinePlan = planDesktopMigration(normalized, pristineTarget());
assert.equal(pristinePlan.mode, 'pristine_merge');
assert.equal(pristinePlan.canCommit, true);
assert.equal(pristinePlan.missingAccounts.length, 1, '銀行 should be inserted');
assert.equal(pristinePlan.readyTransactions.length, 2, 'both legitimate repeated source rows should import into empty target');
assert.equal(pristinePlan.readyOpeningBalances.length, 1);
assert.equal(pristinePlan.resultingLockedThrough, '2026-08');

const existingOne = {
  txDate: '2026-09-01', accountName: '現金', kind: 'income', categoryName: '銷售', summary: 'A', amount: 100
};
const duplicatePlan = planDesktopMigration(normalized, mergeTarget({
  accounts: [
    { name: '現金', sortOrder: 0, isDefault: 1 },
    { name: '銀行', sortOrder: 1, isDefault: 0 }
  ],
  groups: [
    { kind: 'income', name: '收入', sortOrder: 0 },
    { kind: 'expense', name: '支出', sortOrder: 0 }
  ],
  categories: [
    { kind: 'income', name: '銷售', groupName: '收入', sortOrder: 0, isFavorite: 0 },
    { kind: 'expense', name: '雜支', groupName: '支出', sortOrder: 0, isFavorite: 0 }
  ],
  transactions: [existingOne],
  openingBalances: []
}));
assert.equal(duplicatePlan.duplicateTransactions, 1, 'one existing occurrence should consume only one incoming occurrence');
assert.equal(duplicatePlan.readyTransactions.length, 1, 'the second identical legitimate occurrence must remain importable');

const categoryConflict = planDesktopMigration(normalizeDesktopSnapshot(sourceSnapshot({ transactions: [], openingBalances: [] })), mergeTarget({
  accounts: [
    { name: '現金', sortOrder: 0, isDefault: 1 },
    { name: '銀行', sortOrder: 1, isDefault: 0 }
  ],
  groups: [
    { kind: 'income', name: '收入', sortOrder: 0 },
    { kind: 'income', name: '其他收入', sortOrder: 1 },
    { kind: 'expense', name: '支出', sortOrder: 0 }
  ],
  categories: [
    { kind: 'income', name: '銷售', groupName: '其他收入', sortOrder: 0, isFavorite: 0 },
    { kind: 'expense', name: '雜支', groupName: '支出', sortOrder: 0, isFavorite: 0 }
  ]
}));
assert.equal(categoryConflict.canCommit, false);
assert(categoryConflict.conflicts.some(item => item.includes('銷售')));

const openingConflict = planDesktopMigration(normalized, mergeTarget({
  accounts: [
    { name: '現金', sortOrder: 0, isDefault: 1 },
    { name: '銀行', sortOrder: 1, isDefault: 0 }
  ],
  groups: [
    { kind: 'income', name: '收入', sortOrder: 0 },
    { kind: 'expense', name: '支出', sortOrder: 0 }
  ],
  categories: [
    { kind: 'income', name: '銷售', groupName: '收入', sortOrder: 0, isFavorite: 0 },
    { kind: 'expense', name: '雜支', groupName: '支出', sortOrder: 0, isFavorite: 0 }
  ],
  openingBalances: [{ month: '2026-09', accountName: '現金', amount: 999 }],
  lockedThrough: '2026-07'
}));
assert.equal(openingConflict.canCommit, false);
assert(openingConflict.conflicts.some(item => item.includes('期初餘額')));
assert.equal(openingConflict.resultingLockedThrough, '2026-08', 'migration must keep the stricter/later lock');

const stricterWebLock = planDesktopMigration(normalized, mergeTarget({
  accounts: [
    { name: '現金', sortOrder: 0, isDefault: 1 },
    { name: '銀行', sortOrder: 1, isDefault: 0 }
  ],
  groups: [
    { kind: 'income', name: '收入', sortOrder: 0 },
    { kind: 'expense', name: '支出', sortOrder: 0 }
  ],
  categories: [
    { kind: 'income', name: '銷售', groupName: '收入', sortOrder: 0, isFavorite: 0 },
    { kind: 'expense', name: '雜支', groupName: '支出', sortOrder: 0, isFavorite: 0 }
  ],
  lockedThrough: '2026-10'
}));
assert.equal(stricterWebLock.resultingLockedThrough, '2026-10');

const historyBlocked = planDesktopMigration(normalized, mergeTarget({
  accounts: [
    { name: '現金', sortOrder: 0, isDefault: 1 },
    { name: '銀行', sortOrder: 1, isDefault: 0 }
  ],
  groups: [
    { kind: 'income', name: '收入', sortOrder: 0 },
    { kind: 'expense', name: '支出', sortOrder: 0 }
  ],
  categories: [
    { kind: 'income', name: '銷售', groupName: '收入', sortOrder: 0, isFavorite: 0 },
    { kind: 'expense', name: '雜支', groupName: '支出', sortOrder: 0, isFavorite: 0 }
  ],
  history: [{ sha256: SHA }]
}));
assert.equal(historyBlocked.alreadyImported, true);
assert.equal(historyBlocked.canCommit, false);

console.log('Desktop SQLite migration normalization, conservative merge, occurrence dedupe, conflict and lock tests passed.');
