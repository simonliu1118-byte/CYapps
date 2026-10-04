import assert from 'node:assert/strict';
import fs from 'node:fs';
import { DatabaseSync } from 'node:sqlite';
import readExcelFile from 'read-excel-file/universal';
import app from '../src/app.js';
import { buildOpeningBalanceSnapshot } from '../src/opening-balances.js';
import { buildBackupPackage } from '../src/backup-package.js';

// Execute the production migration chain, SQL and Worker routing against real
// SQLite. Only the CYID transport is synthetic; client identity is untrusted.
class D1 {
  constructor() { this.sqlite = new DatabaseSync(':memory:'); }
  prepare(sql) {
    const statement = this.sqlite.prepare(sql);
    let args = [];
    return {
      bind(...values) { args = values; return this; },
      async all() { return { results: statement.all(...args) }; },
      async first() { return statement.get(...args) || null; },
      async run() {
        const result = statement.run(...args);
        return { meta: { changes: result.changes, last_row_id: result.lastInsertRowid } };
      }
    };
  }
  async batch(statements) {
    this.sqlite.exec('BEGIN');
    try {
      const results = [];
      for (const statement of statements) results.push(await statement.run());
      this.sqlite.exec('COMMIT');
      return results;
    } catch (error) { this.sqlite.exec('ROLLBACK'); throw error; }
  }
}

const db = new D1();
const sql = db.sqlite;
const migrations = fs.readdirSync(new URL('../migrations/', import.meta.url)).filter(name => name.endsWith('.sql')).sort();
for (const name of migrations.filter(name => name < '0006')) {
  sql.exec(fs.readFileSync(new URL('../migrations/' + name, import.meta.url), 'utf8'));
}
sql.exec(`INSERT INTO opening_balances VALUES ('2025-12','現金',0,'2025-12-01','2025-12-02')`);
const openingMigration = fs.readFileSync(new URL('../migrations/0006_opening_balance_overrides.sql', import.meta.url), 'utf8');
sql.exec(openingMigration);
const colorSlotMigration = fs.readFileSync(new URL('../migrations/0007_account_color_slots.sql', import.meta.url), 'utf8');
sql.exec(colorSlotMigration);
assert.equal(sql.prepare("SELECT value FROM meta WHERE key='schema_version'").get().value, '7');
assert.equal(sql.prepare("SELECT color_slot FROM accounts WHERE name='現金'").get().color_slot, 1, 'existing accounts receive stable slots');
assert.equal(sql.prepare("SELECT count(*) AS n FROM sqlite_master WHERE name='opening_balances'").get().n, 0);
assert.equal(sql.prepare('SELECT amount FROM opening_balance_overrides').get().amount, 0);
assert.equal(sql.prepare('SELECT action FROM opening_balance_audit').get().action, 'migration');
assert.throws(() => sql.exec(openingMigration), /already exists/, 'forward migration must not silently re-import a second time');
assert.throws(() => sql.exec("UPDATE opening_balance_audit SET reason='rewrite'"), /append-only/);
assert.throws(() => sql.exec('DELETE FROM opening_balance_audit'), /append-only/);

let role = 'ADMIN';
const token = 'cyid_' + 'b'.repeat(64);
const env = {
  DB: db,
  CYID_WORKSPACE_ID: 'workspace-ci-placeholder',
  CYID_APPLICATION_ID: 'CYACC_CI',
  IDENTITY: { async fetch() {
    return Response.json({
      principal: {
        workspaceId: 'workspace-ci-placeholder', employeeId: 'employee-ci', employeeNo: '0123',
        displayName: '測試管理員', workspaceRole: role, isIdentityAdmin: false,
        emailVerified: true, isWorkspaceSuperAdmin: role === 'SUPER_ADMIN', credentialVersion: 1, employeeRevision: 1
      },
      session: { expiresAt: new Date(Date.now() + 3600000).toISOString() }
    });
  } }
};
async function call(path, method = 'GET', body, headers = {}) {
  return app.fetch(new Request('https://acc.example.com' + path, {
    method,
    headers: { cookie: `cyaccounting_session=${token}`, 'content-type': 'application/json', ...headers },
    ...(body ? { body: JSON.stringify(body) } : {})
  }), env);
}
async function adjust(month, amount, reason = '測試對帳調整') {
  return call('/api/opening-balance-overrides', 'PUT', {
    month, values: { 現金: amount }, reason,
    employeeId: 'forged', actor: { employeeNo: '9999', workspaceRole: 'SUPER_ADMIN' }
  });
}
async function opening(month) {
  const response = await call('/api/opening-balances?month=' + month + '&audit=1');
  assert.equal(response.status, 200);
  return (await response.json()).accounts.find(row => row.name === '現金');
}

assert.equal((await opening('2025-11')).amount, 0, 'no override or transactions');
sql.exec(`INSERT INTO transactions(tx_date,account_name,kind,category_name,summary,amount,created_at,updated_at)
  VALUES ('2026-01-10','現金','income','一般收入','測試收入',50,'2026-01-10','2026-01-10'),
         ('2026-02-10','現金','expense','一般支出','測試支出',20,'2026-02-10','2026-02-10')`);
assert.equal((await opening('2026-02')).amount, 50);
assert.equal((await adjust('2026-02', 100, ' ')).status, 400);
const beforeWrite = sql.prepare('SELECT count(*) AS n FROM opening_balance_audit').get().n;
role = 'USER';
assert.equal((await adjust('2026-02', 100)).status, 403);
assert.equal(sql.prepare('SELECT count(*) AS n FROM opening_balance_audit').get().n, beforeWrite);
role = 'ADMIN';
assert.equal((await adjust('2026-02', 100)).status, 200);
assert.equal((await opening('2026-02')).amount, 100);
assert.equal((await opening('2026-03')).amount, 80, 'override plus subsequent transactions');
const audit = sql.prepare('SELECT * FROM opening_balance_audit ORDER BY id DESC LIMIT 1').get();
assert.equal(audit.previous_amount, 50, 'first override records the actual automatic old value');
assert.equal(audit.new_amount, 100);
assert.equal(audit.actor_employee_id, 'employee-ci');
assert.equal(audit.actor_employee_no, '0123');
assert.equal(audit.actor_role, 'ADMIN');
assert.ok(audit.created_at);
assert.equal((await adjust('2026-02', 50, '恢復自動計算')).status, 200);
assert.equal((await opening('2026-02')).source, 'automatic');
assert.equal(sql.prepare("SELECT count(*) AS n FROM opening_balance_overrides WHERE month='2026-02'").get().n, 0);
assert.equal(sql.prepare('SELECT action FROM opening_balance_audit ORDER BY id DESC LIMIT 1').get().action, 'clear');
sql.exec("INSERT INTO app_settings VALUES ('locked_through','2026-02')");
assert.equal((await adjust('2026-02', 100)).status, 409);
sql.exec("DELETE FROM app_settings WHERE key='locked_through'");
role = 'SUPER_ADMIN';
assert.equal((await adjust('2026-03', 120)).status, 200);

// API and both XLSX sheets must share the canonical opening calculation.
for (const month of ['2026-03', '2026-04']) {
  const snapshot = await buildOpeningBalanceSnapshot(db, month);
  const expected = snapshot.accounts.find(row => row.name === '現金');
  role = 'USER';
  const excel = await call('/api/export/month.xlsx?month=' + month);
  assert.equal(excel.status, 200);
  const bytes = await excel.arrayBuffer();
  const sheets = await readExcelFile(bytes);
  const rows = sheets[1].data;
  const row = rows.find(row => row[0] === '現金');
  assert.equal(row[1], expected.amount);
  assert.equal(row[2], expected.source === 'override' ? '手動調整' : '自動計算');
  assert.equal(sheets[0].data[2][1], snapshot.accounts.reduce((sum, item) => sum + item.amount, 0));
}

// Browser identity never changes the domain contract. All interfaces call the same router.
role = 'ADMIN';
for (const browser of ['Mobile Safari iPhone', 'Mobile Safari iPad', 'Desktop Chromium']) {
  const headers = { 'user-agent': browser };
  const values = { txDate: '2026-04-01', accountName: '現金', kind: 'income', categoryName: '一般收入', summary: '中'.repeat(20), amount: 1 };
  const created = await call('/api/transactions', 'POST', values, headers);
  assert.equal(created.status, 201);
  const txId = (await created.json()).id;
  assert.equal((await call(`/api/transactions/${txId}`, 'PUT', { ...values, summary: '中'.repeat(21) }, headers)).status, 400);
  assert.equal((await call(`/api/transactions/${txId}`, 'PUT', { ...values, kind: 'expense', summary: 'A'.repeat(40), amount: 7 }, headers)).status, 200);
  assert.equal(sql.prepare('SELECT kind FROM transactions WHERE id=?').get(txId).kind, 'income', 'editing cannot change the stored kind');
  sql.exec("INSERT INTO app_settings VALUES ('locked_through','2026-03')");
  assert.equal((await call(`/api/transactions/${txId}`, 'PUT', { ...values, txDate: '2026-03-01' }, headers)).status, 409);
  sql.exec("DELETE FROM app_settings WHERE key='locked_through'");
  role = 'USER';
  assert.equal((await call(`/api/transactions/${txId}`, 'PUT', values, headers)).status, 403);
  role = 'ADMIN';
  assert.equal((await call(`/api/transactions/${txId}`, 'DELETE', undefined, headers)).status, 200);
}

// Create/archive/permanent-delete through the actual authenticated router.
role = 'ADMIN';
assert.equal((await call('/api/accounts','POST',{ name: '一二三四五六七八九' })).status, 400);
assert.equal((await call('/api/accounts/1','PUT',{ name: '一二三四五六七八九' })).status, 400);
const validAccountResponse = await call('/api/accounts','POST',{ name: '一二三四五六七八' });
assert.equal(validAccountResponse.status, 201);
const validAccountCreated = await validAccountResponse.json();
assert.equal(validAccountCreated.color_slot, 2, 'new account receives the smallest free color slot');
assert.equal((await call('/api/transactions','POST',{txDate:'2026-04-01',accountName:'現金',kind:'income',categoryName:'門市收入',summary:'長'.repeat(21),amount:1})).status, 400);
const testAccountResponse = await call('/api/accounts','POST',{ name: '測試帳戶' });
assert.equal(testAccountResponse.status, 201);
const testAccountCreated = await testAccountResponse.json();
assert.equal(testAccountCreated.color_slot, 3);
const id = sql.prepare("SELECT id FROM accounts WHERE name='測試帳戶'").get().id;
assert.equal((await call('/api/opening-balance-overrides','PUT',{month:'2026-01',values:{測試帳戶:100},reason:'測試期初'})).status, 200);
assert.equal((await call('/api/opening-balance-overrides','PUT',{month:'2026-01',values:{測試帳戶:0},reason:'恢復零期初'})).status, 200);
assert.equal(sql.prepare("SELECT count(*) AS n FROM opening_balance_overrides WHERE account_name='測試帳戶'").get().n, 0);
assert.equal((await call(`/api/accounts/${id}/archive`,'POST',{})).status, 200);
assert.equal(sql.prepare("SELECT color_slot FROM accounts WHERE id=?").get(id).color_slot, 3, 'archive retains its color slot');
assert.equal((await call(`/api/accounts/${id}/permanent`,'DELETE')).status, 403);
role = 'SUPER_ADMIN';
assert.equal((await call(`/api/accounts/${id}/permanent`,'DELETE')).status, 200);
assert.equal(sql.prepare("SELECT count(*) AS n FROM opening_balance_audit WHERE account_name='測試帳戶'").get().n, 2);

// An old non-zero baseline followed by a newer zero is deletable, but all
// historical financial rows and audit remain and the name stays reserved.
sql.exec("INSERT INTO accounts(name,sort_order,is_default,created_at,archived_at) VALUES ('歷史帳戶',1,0,'2026-01-01',NULL)");
assert.equal(sql.prepare("SELECT color_slot FROM accounts WHERE name='歷史帳戶'").get().color_slot, 3, 'permanent delete releases the slot for the next account');
const historicalId = sql.prepare("SELECT id FROM accounts WHERE name='歷史帳戶'").get().id;
for (const [month, amount] of [['2026-01', 100], ['2026-02', 0]]) {
  assert.equal((await call('/api/opening-balance-overrides','PUT',{month,values:{歷史帳戶:amount},reason:'測試盤點'})).status, 200);
}
assert.equal((await call(`/api/accounts/${historicalId}/archive`,'POST',{})).status, 200);
const auditCount = sql.prepare('SELECT count(*) AS n FROM opening_balance_audit').get().n;
assert.equal((await call(`/api/accounts/${historicalId}/permanent`,'DELETE')).status, 200);
assert.equal(sql.prepare("SELECT count(*) AS n FROM opening_balance_overrides WHERE account_name='歷史帳戶'").get().n, 2);
assert.equal(sql.prepare('SELECT count(*) AS n FROM opening_balance_audit').get().n, auditCount);
assert.equal((await call('/api/accounts','POST',{name:'歷史帳戶'})).status, 409);
assert.equal((await call('/api/accounts/1','PUT',{name:'歷史帳戶'})).status, 409);

// Transaction history blocks even if opening is zero; non-zero opening alone
// also blocks. Unarchived accounts cannot be permanently deleted.
assert.equal((await call('/api/accounts/1/permanent','DELETE')).status, 409);
sql.exec("UPDATE accounts SET archived_at='2026-04-01' WHERE id=1");
assert.equal((await call('/api/accounts/1/permanent','DELETE')).status, 409);
sql.exec("INSERT INTO accounts(name,sort_order,is_default,created_at,archived_at) VALUES ('非零帳戶',2,0,'2026-01-01',NULL)");
const nonzeroId = sql.prepare("SELECT id FROM accounts WHERE name='非零帳戶'").get().id;
assert.equal((await call('/api/opening-balance-overrides','PUT',{month:'2026-01',values:{非零帳戶:5},reason:'盤點'})).status, 200);
sql.exec("UPDATE accounts SET archived_at='2026-04-01' WHERE name='非零帳戶'");
assert.equal((await call(`/api/accounts/${nonzeroId}/permanent`,'DELETE')).status, 409);

const backup = await buildBackupPackage(db);
assert.equal(backup.manifest.formatVersion, 2);
assert.equal(backup.manifest.schemaVersion, 7);
assert.equal(backup.data.openingBalanceAudit.length, sql.prepare('SELECT count(*) AS n FROM opening_balance_audit').get().n);
assert.equal(backup.data.openingBalanceOverrides.length, sql.prepare('SELECT count(*) AS n FROM opening_balance_overrides').get().n);
assert.ok(backup.data.accounts.find(row => row.name === '現金').archivedAt);
assert.equal(backup.data.accounts.find(row => row.name === '現金').colorSlot, 1);
db.sqlite.close();
console.log('Opening migration, real SQL, audited writes, CYID role gates, Excel parity and permanent-delete regressions passed.');
