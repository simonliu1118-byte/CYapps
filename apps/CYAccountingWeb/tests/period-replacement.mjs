import assert from 'node:assert/strict';
import fs from 'node:fs';
import { DatabaseSync } from 'node:sqlite';
import { handleExcelImportApi, buildReplacementStatements } from '../src/excel-import.js';
import { buildBackupPackage } from '../src/backup-package.js';

class D1 {
  constructor() {
    this.db = new DatabaseSync(':memory:');
    for (const name of fs.readdirSync('migrations').filter(name => name.endsWith('.sql')).sort()) this.db.exec(fs.readFileSync(`migrations/${name}`, 'utf8'));
  }
  prepare(sql) {
    const statement = { sql, params: [], bind(...params) { this.params = params; return this; } };
    statement.first = async () => this.db.prepare(sql).get(...statement.params) || null;
    statement.all = async () => ({ results: this.db.prepare(sql).all(...statement.params) });
    statement.run = async () => this.db.prepare(sql).run(...statement.params);
    return statement;
  }
  async batch(statements) {
    this.db.exec('BEGIN');
    try {
      const result = statements.map(s => this.db.prepare(s.sql).run(...s.params));
      this.db.exec('COMMIT'); return result;
    } catch (error) { this.db.exec('ROLLBACK'); throw error; }
  }
}
const db = new D1();
const session = { role: 'SUPER_ADMIN', employee_id: 'test-employee', employee_no: 'TEST', employee_name: '測試管理員' };
const now = '2026-10-01T00:00:00Z';
const tx = (date, summary = '測試交易', category = '一般支出') => ({ txDate: date, accountName: '現金', kind: 'expense', categoryName: category, summary, amount: 100 });
const insert = row => db.db.prepare('INSERT INTO transactions(tx_date,account_name,kind,category_name,summary,amount,created_at,updated_at) VALUES(?,?,?,?,?,?,?,?)').run(row.txDate,row.accountName,row.kind,row.categoryName,row.summary,row.amount,now,now);
insert(tx('2026-03-10', '被替換'));
insert(tx('2026-04-10', '保留'));
const savedLater = JSON.stringify(db.db.prepare("SELECT * FROM transactions WHERE tx_date >= '2026-04-01'").all());
const body = { mode: 'replace_period', startMonth: '2026-03', endMonth: '2026-03', reason: '測試來源帳簿更正', createCategories: true,
  rows: [tx('2026-03-11', '相同交易', '歷史測試科目'), tx('2026-03-11', '相同交易', '歷史測試科目')].map((row,i) => ({ ...row, sourceRow: i+2 })) };
async function preview(value = body, actor = session) {
  const response = await handleExcelImportApi(new Request('https://test.invalid/api/import/preview', { method: 'POST', body: JSON.stringify(value) }), { DB: db }, actor);
  return { status: response.status, data: await response.json() };
}
assert.equal((await preview(body, { role: 'ADMIN' })).status, 403);
assert.equal((await preview({ ...body, reason: '' })).status, 400);
assert.equal((await preview({ ...body, startMonth: '2026-04' })).status, 400);
assert.equal((await preview({ ...body, createCategories: false })).data.canCommit, false);
const outOfRange = await preview({ ...body, rows: [{...tx('2026-04-01'),sourceRow:2}] });
assert.equal(outOfRange.data.canCommit, false);
db.db.prepare("INSERT OR REPLACE INTO app_settings(key,value) VALUES('locked_through','2026-03')").run();
assert.equal((await preview()).data.canCommit, false);
assert.equal((await preview({ ...body, startMonth: '2026-02', endMonth: '2026-04', rows: [tx('2026-04-01')] })).data.replacement.lockedRange, true, 'a gap in the source must not allow deleting locked rows');
db.db.prepare("DELETE FROM app_settings WHERE key='locked_through'").run();
let p = (await preview()).data;
assert.equal(p.canCommit, true);
assert.equal(p.summary.ready, 2, 'identical source occurrences must survive');
assert.equal(p.replacement.deleteCount, 1);
assert.equal(p.replacement.preservedCount, 1);
assert.deepEqual(p.replacement.missingCategories, [{ kind:'expense',name:'歷史測試科目' }]);
const snapshot = JSON.stringify({
  transactions: db.db.prepare('SELECT id,tx_date,account_name,kind,category_name,summary,amount,created_at,updated_at FROM transactions ORDER BY id').all().map(row=>Object.values(row)),
  accounts: db.db.prepare('SELECT id,name,sort_order,is_default,archived_at FROM accounts ORDER BY id').all().map(row=>Object.values(row)),
  groups: db.db.prepare('SELECT id,kind,name,sort_order FROM category_groups ORDER BY id').all().map(row=>Object.values(row)),
  categories: db.db.prepare('SELECT id,kind,group_id,name,sort_order,is_favorite FROM categories ORDER BY id').all().map(row=>Object.values(row)),
  openings: [], lockedThrough:null
});
const replacement = { plan: p.replacement, analysis: { _readyRows: body.rows }, range: {start:'2026-03-01',endExclusive:'2026-04-01'}, targetJson: snapshot };
const statements = buildReplacementStatements(db, replacement, body, session, 'TEST_BACKUP', now);
// The canonical commit must stop if its paired backup is unconfigured/fails.
const noBackup = await handleExcelImportApi(new Request('https://test.invalid/api/import/commit',{method:'POST',body:JSON.stringify({...body,confirm:true,previewToken:p.replacement.previewToken})}),{DB:db},session);
assert.equal(noBackup.status,503);
assert.equal(db.db.prepare('SELECT COUNT(*) AS n FROM excel_import_runs').get().n,0);
// Failure after deletion rolls back the audit, masters, deletion and inserts.
await assert.rejects(db.batch([...statements,db.prepare("INSERT INTO transactions(tx_date,account_name,kind,category_name,summary,amount,created_at,updated_at) VALUES('2026-03-01','現金','invalid','一般支出','',1,'x','x')")]));
assert.equal(db.db.prepare('SELECT COUNT(*) AS n FROM transactions').get().n,2);
assert.equal(db.db.prepare('SELECT COUNT(*) AS n FROM excel_import_runs').get().n,0);
assert.equal(db.db.prepare("SELECT COUNT(*) AS n FROM categories WHERE name='歷史測試科目'").get().n,0);
// A concurrent change between preview and batch makes ALL guarded writes no-op.
db.db.prepare("UPDATE transactions SET summary='另一個管理員修改' WHERE tx_date='2026-04-10'").run();
await db.batch(statements);
assert.equal(db.db.prepare('SELECT COUNT(*) AS n FROM transactions').get().n,2);
assert.equal(db.db.prepare('SELECT COUNT(*) AS n FROM excel_import_runs').get().n,0);
db.db.prepare("UPDATE transactions SET summary='保留' WHERE tx_date='2026-04-10'").run();
await db.batch(statements);
assert.equal(db.db.prepare("SELECT COUNT(*) AS n FROM transactions WHERE tx_date<'2026-04-01'").get().n,2);
assert.equal(JSON.stringify(db.db.prepare("SELECT * FROM transactions WHERE tx_date >= '2026-04-01'").all()),savedLater);
assert.equal(db.db.prepare('SELECT COUNT(*) AS n FROM opening_balance_overrides').get().n,0,'never invent initial baselines');
assert.equal(db.db.prepare('SELECT COUNT(*) AS n FROM excel_import_runs').get().n,1);
assert.throws(()=>db.db.prepare('DELETE FROM excel_import_runs').run(),/append-only/);
assert.throws(()=>db.db.prepare("UPDATE excel_import_runs SET reason='x'").run(),/append-only/);
const backup = await buildBackupPackage(db);
assert.equal(backup.manifest.schemaVersion,8);
assert.equal(backup.data.excelImportRuns.length,1,'backup includes replacement audit');
assert.equal(backup.data.excelImportRuns[0].actor.employeeNo,'TEST');
const large = { ...replacement, plan: { ...p.replacement, previewToken:'large-test', insertCount:5000 }, analysis:{_readyRows:Array.from({length:5000},()=>tx('2026-03-15'))} };
assert.ok(buildReplacementStatements(db,large,body,session,'TEST_BACKUP',now).length<20,'JSON chunks stay below D1 query/bind limits');
console.log('Period replacement: authorization, bounds, locks, backup failure, atomic rollback, concurrent changes, duplicate occurrences, preservation, audit and backup passed');
