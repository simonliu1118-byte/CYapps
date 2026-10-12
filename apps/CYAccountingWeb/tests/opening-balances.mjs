import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { buildOpeningBalanceSnapshot } from '../src/opening-balances.js';

class Statement {
  constructor(db, sql) {
    this.db = db;
    this.sql = sql;
    this.args = [];
  }
  bind(...args) {
    this.args = args;
    return this;
  }
  async all() {
    const month = String(this.args[0] || '');
    if (this.sql.includes('FROM accounts')) {
      return { results: [{ name: '現金', archived_at: null }] };
    }
    if (this.sql.includes('SELECT DISTINCT account_name AS name')) {
      return { results: [{ name: '現金' }] };
    }
    if (this.sql.includes('FROM opening_balance_overrides')) {
      return {
        results: this.db.overrides
          .filter(row => row.month <= month)
          .map(row => ({
            ...row,
            updated_at: '2026-01-01T00:00:00Z',
            updated_by_employee_no: '0001',
            updated_by_name: '管理員',
            updated_by_role: 'SUPER_ADMIN'
          }))
      };
    }
    if (this.sql.includes('SUM(CASE WHEN kind')) {
      return { results: this.db.monthlyNets.filter(row => row.month < month) };
    }
    throw new Error('unexpected all query: ' + this.sql);
  }
  async first() {
    if (this.sql.includes("key = 'locked_through'")) return null;
    throw new Error('unexpected first query: ' + this.sql);
  }
}

class MockDb {
  constructor() {
    this.overrides = [
      { month: '2026-01', account_name: '現金', amount: 100, reason: '年初盤點' },
      { month: '2026-03', account_name: '現金', amount: 200, reason: '銀行對帳調整' }
    ];
    this.monthlyNets = [
      { account_name: '現金', month: '2026-01', net: 50 },
      { account_name: '現金', month: '2026-02', net: -20 },
      { account_name: '現金', month: '2026-03', net: 30 }
    ];
  }
  prepare(sql) {
    return new Statement(this, sql);
  }
}

const db = new MockDb();

const march = await buildOpeningBalanceSnapshot(db, '2026-03');
assert.equal(march.accounts.length, 1);
assert.equal(march.accounts[0].automaticAmount, 130);
assert.equal(march.accounts[0].amount, 200);
assert.equal(march.accounts[0].source, 'override');
assert.equal(march.accounts[0].overrideReason, '銀行對帳調整');

const april = await buildOpeningBalanceSnapshot(db, '2026-04');
assert.equal(april.accounts[0].automaticAmount, 230);
assert.equal(april.accounts[0].amount, 230);
assert.equal(april.accounts[0].source, 'automatic');
assert.equal(april.accounts[0].automaticAnchorMonth, '2026-03');

const HERE = path.dirname(fileURLToPath(import.meta.url));
const ROOT = path.resolve(HERE, '..');
const source = fs.readFileSync(path.join(ROOT, 'src/opening-balances.js'), 'utf8');
assert.match(source, /手動調整期初餘額必須填寫理由/);
assert.match(source, /opening_balance_audit/);
assert.match(source, /action: 'clear'/);
assert.doesNotMatch(source, /FROM opening_balances/);

console.log('Automatic opening balance tests passed.');
