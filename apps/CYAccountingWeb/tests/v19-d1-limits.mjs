import assert from 'node:assert/strict';
import { DatabaseSync } from 'node:sqlite';
import { buildSafeMigrationStatements } from '../src/v19-migration-safe.js';

class CollectingDB {
  constructor() { this.statements = []; }
  prepare(sql) {
    return {
      bind: (...params) => {
        const statement = { sql, params };
        this.statements.push(statement);
        return statement;
      }
    };
  }
}

function iso(index = 0) {
  return `2026-01-${String((index % 28) + 1).padStart(2, '0')}T12:34:56`;
}

function baseAnalysis() {
  const snapshot = {
    source: {
      fileSha256: 'a'.repeat(64),
      fileName: 'CYaccounting.db',
      schemaVersion: 2
    },
    accounts: [
      { name: '現金', sortOrder: 0, isDefault: 0, createdAt: iso(0) },
      { name: '銀行', sortOrder: 1, isDefault: 1, createdAt: iso(1) }
    ],
    groups: [
      { kind: 'income', name: '收入分類', sortOrder: 0, createdAt: iso(0) },
      { kind: 'expense', name: '支出分類', sortOrder: 0, createdAt: iso(0) },
      { kind: 'expense', name: '營業費用', sortOrder: 1, createdAt: iso(1) }
    ],
    categories: [
      { kind: 'income', groupName: '收入分類', name: '一般收入', sortOrder: 0, isFavorite: 1, createdAt: iso(0) },
      { kind: 'expense', groupName: '支出分類', name: '一般支出', sortOrder: 0, isFavorite: 0, createdAt: iso(0) },
      { kind: 'expense', groupName: '營業費用', name: '運費', sortOrder: 0, isFavorite: 1, createdAt: iso(1) }
    ],
    transactions: [],
    openingBalances: [],
    lockedThrough: '2025-12'
  };
  const target = {
    history: [],
    accounts: [{ name: '現金', sortOrder: 0, isDefault: 1 }],
    groups: [
      { kind: 'income', name: '收入分類', sortOrder: 0 },
      { kind: 'expense', name: '支出分類', sortOrder: 0 }
    ],
    categories: [
      { kind: 'income', name: '一般收入', groupName: '收入分類', sortOrder: 0, isFavorite: 0 },
      { kind: 'expense', name: '一般支出', groupName: '支出分類', sortOrder: 0, isFavorite: 0 }
    ]
  };
  const plan = {
    mode: 'pristine_merge',
    missingAccounts: [{ ...snapshot.accounts[1], targetSortOrder: 1 }],
    missingGroups: [{ ...snapshot.groups[2], targetSortOrder: 1 }],
    missingCategories: [{ ...snapshot.categories[2], targetSortOrder: 0 }],
    readyTransactions: [],
    duplicateTransactions: 0,
    readyOpeningBalances: [],
    duplicateOpeningBalances: 0,
    historicalAccounts: 0,
    historicalCategories: 0,
    resultingLockedThrough: '2025-12'
  };
  return { snapshot, target, plan };
}

// Worst supported payload must stay under D1's per-invocation/query/string limits.
const max = baseAnalysis();
const longSummary = '摘要'.repeat(500); // 1,000 chars.
const maxHistoricalAccount = '帳'.repeat(200);
const maxHistoricalCategory = '科'.repeat(200);
for (let index = 0; index < 10_000; index += 1) {
  max.plan.readyTransactions.push({
    txDate: '2025-12-31',
    accountName: maxHistoricalAccount,
    kind: index % 2 ? 'expense' : 'income',
    categoryName: maxHistoricalCategory,
    summary: longSummary,
    amount: 9_999_999,
    createdAt: iso(index),
    updatedAt: iso(index)
  });
}
for (let index = 0; index < 5_000; index += 1) {
  max.plan.readyOpeningBalances.push({
    month: `${2020 + Math.floor(index / 12)}-${String((index % 12) + 1).padStart(2, '0')}`,
    accountName: `歷史帳戶${index}`,
    amount: index % 2 ? -index : index,
    createdAt: iso(index),
    updatedAt: iso(index)
  });
}
const maxDb = new CollectingDB();
const maxBuilt = buildSafeMigrationStatements(max, maxDb, { employee_no: '0001' });
assert.ok(maxBuilt.statements.length <= 40, `migration batch uses ${maxBuilt.statements.length} statements; expected <= 40`);
assert.ok(maxBuilt.statements.length + 8 <= 50, 'commit analysis + write batch must fit Free-plan 50 D1 queries per invocation');
for (const statement of maxBuilt.statements) {
  assert.ok(statement.params.length <= 100, `statement has ${statement.params.length} bound parameters`);
  for (const param of statement.params) {
    if (typeof param === 'string') {
      assert.ok(Buffer.byteLength(param, 'utf8') < 2_000_000, 'bound JSON payload must stay below D1 2 MB string limit');
    }
  }
}
assert.ok(maxBuilt.statements.some(statement => statement.sql.includes('json_each(?)')), 'bulk writes must use D1 JSON expansion');

// Execute a representative generated batch against SQLite JSON1 to catch SQL syntax/ordering errors.
const sample = baseAnalysis();
sample.plan.readyTransactions = [{
  txDate: '2025-12-31',
  accountName: '已刪除舊帳戶',
  kind: 'expense',
  categoryName: '已刪除舊科目',
  summary: '歷史交易',
  amount: 1234,
  createdAt: '2025-12-31T10:00:00',
  updatedAt: '2025-12-31T10:00:00'
}];
sample.plan.readyOpeningBalances = [{
  month: '2025-12',
  accountName: '已刪除舊帳戶',
  amount: -5000,
  createdAt: '2025-12-01T00:00:00',
  updatedAt: '2025-12-01T00:00:00'
}];
sample.plan.historicalAccounts = 1;
sample.plan.historicalCategories = 1;

const sampleDb = new CollectingDB();
const sampleBuilt = buildSafeMigrationStatements(sample, sampleDb, { employee_no: '0001' });
const sqlite = new DatabaseSync(':memory:');
sqlite.exec(`
  PRAGMA foreign_keys = ON;
  CREATE TABLE accounts(id INTEGER PRIMARY KEY AUTOINCREMENT, name TEXT UNIQUE NOT NULL, sort_order INTEGER NOT NULL, is_default INTEGER NOT NULL, created_at TEXT NOT NULL);
  CREATE TABLE category_groups(id INTEGER PRIMARY KEY AUTOINCREMENT, kind TEXT NOT NULL, name TEXT NOT NULL, sort_order INTEGER NOT NULL, created_at TEXT NOT NULL, UNIQUE(kind,name));
  CREATE TABLE categories(id INTEGER PRIMARY KEY AUTOINCREMENT, kind TEXT NOT NULL, group_id INTEGER NOT NULL REFERENCES category_groups(id), name TEXT NOT NULL, sort_order INTEGER NOT NULL, is_favorite INTEGER NOT NULL, created_at TEXT NOT NULL, UNIQUE(kind,name));
  CREATE TABLE transactions(id INTEGER PRIMARY KEY AUTOINCREMENT, tx_date TEXT NOT NULL, account_name TEXT NOT NULL, kind TEXT NOT NULL, category_name TEXT NOT NULL, summary TEXT NOT NULL, amount INTEGER NOT NULL, created_at TEXT NOT NULL, updated_at TEXT NOT NULL);
  CREATE TABLE opening_balances(month TEXT NOT NULL, account_name TEXT NOT NULL, amount INTEGER NOT NULL, created_at TEXT NOT NULL, updated_at TEXT NOT NULL, PRIMARY KEY(month,account_name));
  CREATE TABLE app_settings(key TEXT PRIMARY KEY, value TEXT NOT NULL);
  INSERT INTO accounts(name,sort_order,is_default,created_at) VALUES ('現金',0,1,'2026-01-01T00:00:00');
  INSERT INTO category_groups(kind,name,sort_order,created_at) VALUES ('income','收入分類',0,'2026-01-01T00:00:00'),('expense','支出分類',0,'2026-01-01T00:00:00');
  INSERT INTO categories(kind,group_id,name,sort_order,is_favorite,created_at)
    SELECT 'income',id,'一般收入',0,0,'2026-01-01T00:00:00' FROM category_groups WHERE kind='income';
  INSERT INTO categories(kind,group_id,name,sort_order,is_favorite,created_at)
    SELECT 'expense',id,'一般支出',0,0,'2026-01-01T00:00:00' FROM category_groups WHERE kind='expense';
`);
sqlite.exec('BEGIN');
try {
  for (const statement of sampleBuilt.statements) sqlite.prepare(statement.sql).run(...statement.params);
  sqlite.exec('COMMIT');
} catch (error) {
  sqlite.exec('ROLLBACK');
  throw error;
}
assert.equal(sqlite.prepare("SELECT is_default FROM accounts WHERE name='銀行'").get().is_default, 1);
assert.equal(sqlite.prepare("SELECT is_default FROM accounts WHERE name='現金'").get().is_default, 0);
assert.equal(sqlite.prepare("SELECT group_id FROM categories WHERE kind='expense' AND name='運費'").get().group_id,
  sqlite.prepare("SELECT id FROM category_groups WHERE kind='expense' AND name='營業費用'").get().id);
assert.equal(sqlite.prepare('SELECT COUNT(*) AS count FROM transactions').get().count, 1);
assert.equal(sqlite.prepare('SELECT account_name FROM transactions').get().account_name, '已刪除舊帳戶');
assert.equal(sqlite.prepare('SELECT amount FROM opening_balances').get().amount, -5000);
assert.equal(sqlite.prepare("SELECT value FROM app_settings WHERE key='locked_through'").get().value, '2025-12');
assert.ok(JSON.parse(sqlite.prepare("SELECT value FROM app_settings WHERE key='desktop_migration_history_v1'").get().value).length === 1);
sqlite.close();

console.log(`V0.19 D1-safe migration batch tests passed (${maxBuilt.statements.length} max statements).`);
