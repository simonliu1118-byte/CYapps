import assert from 'node:assert/strict';
import fs from 'node:fs';
import { DatabaseSync } from 'node:sqlite';
import vm from 'node:vm';
import app from '../src/app.js';
import { statisticsPeriod } from '../src/statistics.js';
import { buildBackupPackage } from '../src/backup-package.js';

class D1 {
  constructor() { this.sqlite = new DatabaseSync(':memory:'); }
  prepare(sql) {
    const statement = this.sqlite.prepare(sql); let args = [];
    return {
      bind(...values) { args = values; return this; },
      async all() { return { results: statement.all(...args) }; },
      async first() { return statement.get(...args) || null; },
      async run() { const result = statement.run(...args); return { meta: { changes: result.changes, last_row_id: result.lastInsertRowid } }; },
      async execute() { return /^\s*SELECT/i.test(sql) ? this.all() : this.run(); }
    };
  }
  async batch(statements) {
    this.sqlite.exec('BEGIN');
    try { const results = []; for (const statement of statements) results.push(await statement.execute()); this.sqlite.exec('COMMIT'); return results; }
    catch (error) { this.sqlite.exec('ROLLBACK'); throw error; }
  }
}
const db = new D1(); const sql = db.sqlite;
for (const name of fs.readdirSync(new URL('../migrations/', import.meta.url)).filter(name => name.endsWith('.sql')).sort()) sql.exec(fs.readFileSync(new URL('../migrations/' + name, import.meta.url), 'utf8'));
let role = 'ADMIN'; let authenticated = true;
const env = { DB: db, CYID_WORKSPACE_ID: 'workspace-ci-placeholder', CYID_APPLICATION_ID: 'CYACC_CI', IDENTITY: { async fetch() {
  if (!authenticated) return Response.json({ ok: false, code: 'AUTH_REQUIRED' }, { status: 401 });
  return Response.json({ principal: { workspaceId: 'workspace-ci-placeholder', workspaceStatus: 'active', workspaceRole: role,
    employeeId: 'employee-ci', employeeNo: '0123', displayName: '測試', isIdentityAdmin: false, employeeStatus: 'active', appId: 'CYACC_CI', appAccessGranted: true,
    emailVerified: true, isWorkspaceSuperAdmin: role === 'SUPER_ADMIN', credentialVersion: 1, employeeRevision: 1 },
    session: { expiresAt: new Date(Date.now() + 3600000).toISOString() } });
} } };
async function call(path, method = 'GET', body) {
  return app.fetch(new Request('https://acc.example.com' + path, { method, headers: { cookie: 'cyaccounting_session=cyid_' + 'b'.repeat(64), 'content-type': 'application/json' }, ...(body ? { body: JSON.stringify(body) } : {}) }), env);
}
async function stats(start, end = start) { const r = await call(`/api/statistics?start=${start}&end=${end}`); assert.equal(r.status, 200); return r.json(); }
assert.equal(statisticsPeriod('2026-12', '2026-12').until, '2027-01-01');
assert.equal(statisticsPeriod('0001-01', '0001-01').until, '0001-02-01');
for (const [start, end] of [['2026-00','2026-12'], ['2026-13','2026-13'], ['2027-01','2026-01'], ['bad','bad']]) assert.equal((await call(`/api/statistics?start=${start}&end=${end}`)).status, 400);
const incomeId = sql.prepare("SELECT id FROM categories WHERE kind='income'").get().id;
const expenseId = sql.prepare("SELECT id FROM categories WHERE kind='expense'").get().id;
const insert = sql.prepare('INSERT INTO transactions(tx_date,account_name,kind,category_name,summary,amount,created_at,updated_at) VALUES (?,?,?,?,?,?,?,?)');
function tx(date, kind, subject, amount, account = '現金') { insert.run(date, account, kind, subject, '合成測試', amount, date, date); }
tx('2025-12-31','income','一般收入',100); tx('2026-01-01','income','一般收入',100); tx('2026-01-31','expense','一般支出',20);
tx('2026-02-01','income','一般收入',50); tx('2026-02-28','expense','已刪除的歷史科目',7,'已封存帳戶'); tx('2026-03-01','expense','一般支出',999);
for (let i = 0; i < 1501; i++) tx('2026-02-11','income','一般收入',1);
assert.deepEqual((await stats('2026-01')).cash, { income: 100, expense: 20, net: 80, count: 2 });
const period = await stats('2026-01','2026-02');
assert.deepEqual(period.cash, { income: 1651, expense: 27, net: 1624, count: 1505 });
assert.equal(period.groups.find(group => group.name === '未分類').amount, 7, 'unmatched historical subjects and archived accounts must be retained');
assert.deepEqual((await stats('2030-01')).cash, { income: 0, expense: 0, net: 0, count: 0 });
for (const ids of [[0], [incomeId,incomeId], ['1'], [999999]]) assert.notEqual((await call('/api/settings/statistics','PUT',{transferCategoryIds:ids})).status,200);
assert.equal((await call('/api/settings/statistics','PUT',{transferCategoryIds:[incomeId]})).status,200);
const classified = await stats('2026-01','2026-02');
assert.deepEqual(classified.cash, period.cash, 'transfer classification cannot change ledger totals');
assert.equal(classified.operating.income,0); assert.equal(classified.operating.net,-27); assert.equal(classified.transfer.income,1651);
assert.equal(classified.operating.net + classified.transfer.net, classified.cash.net);
assert.equal(classified.groups.find(group => group.kind === 'income').subjects[0].isTransfer,true);
// IDs survive a rename and regroup, and same names on the other kind are not classified by name.
sql.prepare('UPDATE categories SET name=? WHERE id=?').run('重新命名',incomeId);
sql.prepare("UPDATE transactions SET category_name='重新命名' WHERE kind='income' AND category_name='一般收入'").run();
assert.equal((await stats('2026-01')).transfer.income,100);
const setting = await (await call('/api/settings/statistics')).json(); assert.ok(setting.transferCategoryIds.includes(incomeId));
const backup = await buildBackupPackage(db);
assert.ok(JSON.stringify(backup).includes('statistics_transfer_category_ids'), 'existing backup includes the report setting');
role = 'USER'; assert.equal((await call('/api/statistics?start=2026-01')).status,200); assert.equal((await call('/api/settings/statistics')).status,200);
assert.equal((await call('/api/settings/statistics','PUT',{transferCategoryIds:[]})).status,403);
authenticated = false; assert.equal((await call('/api/statistics?start=2026-01')).status,401); authenticated = true; role = 'ADMIN';
assert.equal((await call('/api/settings/statistics','PUT',{transferCategoryIds:[]})).status,200);
assert.equal((await stats('2026-01')).operating.net,80);

// Pure report rendering uses exactly the API totals, escapes names and exposes transfer rows separately.
globalThis.document = { readyState: 'loading', addEventListener() {} };
const ui = await import('../public/statistics-ui.js');
delete globalThis.document;
const synthetic = structuredClone(classified); synthetic.groups[0].subjects[0].name = '<script>bad</script>';
assert.ok(ui.renderStatistics(synthetic).includes('&lt;script&gt;bad&lt;/script&gt;'));
assert.ok(ui.renderIncomeStatement(synthetic).includes('帳戶移轉（另列'));
assert.ok(ui.renderIncomeStatement(synthetic).includes('$1,651'));
assert.equal(ui.statisticsItems(classified,'income').reduce((sum,item)=>sum+item.amount,0),1651);
assert.equal(ui.statisticsItems(classified,'expense','subject').reduce((sum,item)=>sum+item.amount,0),27);
assert.ok(!ui.renderStatistics(await stats('2030-01')).includes('<circle'));

// Existing mobile page owner must preserve the entry/ledger visibility and allow statistics as a third page.
const adaptive = fs.readFileSync(new URL('../public/adaptive-ui.js',import.meta.url),'utf8');
const start = adaptive.indexOf('function applyMobileWorkspacePage('); const end = adaptive.indexOf('function restoreTouchWorkspaceLedgerContext(',start);
function element() { const classes = new Set(); return { dataset:{}, classList:{toggle(c,on){on ? classes.add(c):classes.delete(c)},contains(c){return classes.has(c)}},setAttribute(){},querySelectorAll(){return []} }; }
const shell = element(), nav = element(), entry = element(), ledger = element(), statistics = element(); let refreshes=0;
const elements = {'.shell':shell,'#mobileMainNav':nav,'.entry-card':entry,'.ledger-card':ledger,'#statisticsCard':statistics};
const context = vm.createContext({document:{querySelector:s=>elements[s]},window:{cyStatistics:{refresh(){refreshes++}}}});
vm.runInContext(adaptive.slice(start,end),context);
for (const page of ['entry','ledger','statistics']) {
  context.applyMobileWorkspacePage(page,true);
  assert.equal(shell.dataset.mobilePage,page);
  for (const [name,el] of [['entry',entry],['ledger',ledger],['statistics',statistics]]) assert.equal(el.classList.contains('cy-mobile-page-hidden'),name !== page);
}
assert.equal(refreshes,1); context.applyMobileWorkspacePage('statistics',false); assert.equal(shell.dataset.mobilePage,''); assert.ok(!entry.classList.contains('cy-mobile-page-hidden')); assert.ok(!ledger.classList.contains('cy-mobile-page-hidden'));
console.log('Statistics SQL, authorization, transfer settings, backup, rendering and mobile navigation checks passed.');
