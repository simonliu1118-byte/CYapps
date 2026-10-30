import assert from 'node:assert/strict';
import fs from 'node:fs';
import { DatabaseSync } from 'node:sqlite';
import { buildOpeningBalanceSnapshot } from '../src/opening-balances.js';
import { buildBackupPackage } from '../src/backup-package.js';

const sql = new DatabaseSync(':memory:');
for (const name of fs.readdirSync('migrations').filter(name => name.endsWith('.sql')).sort()) sql.exec(fs.readFileSync('migrations/' + name, 'utf8'));
function client() {
  return { queries: [], beforePublish: null, prepare(text) {
    this.queries.push(text);
    const owner=this, statement=sql.prepare(text); let args=[];
    return { bind(...values) { args=values; return this; },
      async all() { return { results: statement.all(...args) }; },
      async first() { return statement.get(...args) || null; },
      async run() {
        if (text.startsWith('INSERT INTO opening_balance_snapshots')) owner.beforePublish?.();
        return { meta: statement.run(...args) };
      }
    };
  } };
}
const one=client(), two=client();
const opening = async (db,month,name='現金') => (await buildOpeningBalanceSnapshot(db,month)).accounts.find(row => row.name===name);
const cached = month => sql.prepare('SELECT accounts_json,created_at FROM opening_balance_snapshots WHERE month=?').get(month);
const transaction=sql.prepare(`INSERT INTO transactions(tx_date,account_name,kind,category_name,summary,amount,created_at,updated_at)
  VALUES(?,?,'income','TEST','TEST',?,'2026-01-01','2026-01-01')`);
const override=sql.prepare(`INSERT INTO opening_balance_overrides(month,account_name,amount,reason,created_at,updated_at,
  updated_by_employee_id,updated_by_employee_no,updated_by_name,updated_by_role)
  VALUES(?,'現金',?,'TEST','2026-01-01','2026-01-01','TEST','TEST','TEST','ADMIN')
  ON CONFLICT(month,account_name) DO UPDATE SET amount=excluded.amount`);
assert.equal((await opening(one,'2026-01')).amount,0);
const january=cached('2026-01');
const janId=Number(transaction.run('2026-01-10','現金',100).lastInsertRowid);
assert.deepEqual(cached('2026-01'),january,'ordinary current-month entry preserves this month opening');
assert.equal((await opening(one,'2026-02')).amount,100);
const before=two.queries.length;
assert.equal((await opening(two,'2026-02')).amount,100);
assert.equal(two.queries.length-before,1,'a separate device gets the D1 snapshot with one point lookup');
assert.doesNotMatch(two.queries.at(-1),/FROM transactions|FROM accounts|FROM opening_balance_overrides/);
const february=cached('2026-02');
const febId=Number(transaction.run('2026-02-10','現金',50).lastInsertRowid);
assert.deepEqual(cached('2026-02'),february);
assert.equal((await opening(two,'2026-03')).amount,150);
transaction.run('2026-01-11','現金',10);
assert.deepEqual(cached('2026-01'),january);
assert.equal(cached('2026-02'),undefined);assert.equal(cached('2026-03'),undefined);
assert.equal((await opening(two,'2026-02')).amount,110);
sql.prepare('UPDATE transactions SET tx_date=? WHERE id=?').run('2026-01-12',febId);
assert.equal((await opening(two,'2026-02')).amount,160,'moving a transaction across months invalidates both ranges');
const stable=cached('2026-02');
sql.prepare('UPDATE transactions SET summary=?,amount=amount WHERE id=?').run('TEST_CHANGED_SUMMARY',janId);
assert.deepEqual(cached('2026-02'),stable,'a summary-only edit does not recalculate opening');
sql.prepare('DELETE FROM transactions WHERE id=?').run(febId);
assert.equal((await opening(two,'2026-02')).amount,110);

override.run('2026-01',0);
assert.equal((await opening(two,'2026-01')).overrideAmount,0,'zero is a real manual anchor');
override.run('2026-01',200);
assert.equal((await opening(two,'2026-02')).amount,310);
override.run('2026-02',500);
const manual=await opening(two,'2026-02');
assert.equal(manual.amount,500);assert.equal(manual.automaticAmount,310);
sql.prepare("DELETE FROM opening_balance_overrides WHERE month='2026-02'").run();
assert.equal((await opening(two,'2026-02')).amount,310);
const unlocked=cached('2026-02');
sql.exec("INSERT INTO app_settings(key,value) VALUES('locked_through','2026-02')");
assert.equal((await buildOpeningBalanceSnapshot(two,'2026-02')).locked,true);
assert.deepEqual(cached('2026-02'),unlocked,'lock status is always live without invalidating financial values');

sql.exec("UPDATE accounts SET archived_at='2026-02-01' WHERE name='現金'");
assert.equal((await opening(two,'2026-02')).isArchived,true);
const orphan=Number(transaction.run('2026-02-20','TEST_ORPHAN',7).lastInsertRowid);
assert.equal((await opening(two,'2026-02','TEST_ORPHAN')).amount,0,'current-only historical account membership remains correct');
sql.prepare('DELETE FROM transactions WHERE id=?').run(orphan);
assert.equal(await opening(two,'2026-02','TEST_ORPHAN'),undefined);
sql.exec("UPDATE accounts SET archived_at=NULL WHERE name='現金'");
assert.equal((await opening(two,'2026-02')).isCurrent,true);

const rollback=cached('2026-02');
sql.exec('BEGIN');transaction.run('2026-01-15','現金',99);sql.exec('ROLLBACK');
assert.deepEqual(cached('2026-02'),rollback,'financial write and cache invalidation roll back together');
sql.exec('DELETE FROM opening_balance_snapshots');
one.beforePublish=()=>{ one.beforePublish=null;transaction.run('2026-01-16','現金',5); };
assert.equal((await opening(one,'2026-02')).amount,315,'a concurrent prior write cannot publish an obsolete result');
assert.equal((await opening(two,'2026-02')).amount,315,'the other device sees the corrected publication');

sql.exec("INSERT INTO meta(key,value) VALUES('accounting_epoch','TEST_BOOK_NEW')");
assert.equal(cached('2026-02'),undefined);
await opening(two,'2026-02');
sql.exec("UPDATE meta SET value='TEST_BOOK_REPLACED' WHERE key='accounting_epoch'");
assert.equal(cached('2026-02'),undefined);
await opening(two,'2026-02');
const backup=await buildBackupPackage(two);
assert.ok(!Object.keys(backup.data).some(key=>/snapshot/i.test(key)),'derived snapshots are rebuilt, never portable financial data');
assert.equal(backup.manifest.schemaVersion,14);
sql.close();
console.log('Shared monthly snapshots: warm read, current-month stability, prior/cross-month edits, manual/zero anchors, historical names, live locks, atomic rollback, concurrent publication and restore epoch passed.');
