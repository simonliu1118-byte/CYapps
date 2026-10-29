import assert from 'node:assert/strict';
import fs from 'node:fs';
import { DatabaseSync } from 'node:sqlite';
import { buildOpeningBalanceSnapshot } from '../src/opening-balances.js';
import { handleAccountingToolsApi } from '../src/accounting-tools.js';

const sql = new DatabaseSync(':memory:');
const migrations = fs.readdirSync(new URL('../migrations/', import.meta.url)).filter(name => name.endsWith('.sql')).sort();
const ddl = migrations.map(name => fs.readFileSync(new URL('../migrations/' + name, import.meta.url), 'utf8'));
for (const migration of ddl.slice(0, -1)) sql.exec(migration);
const fixture = `
  WITH RECURSIVE n(i) AS (SELECT 0 UNION ALL SELECT i+1 FROM n WHERE i<59999)
  INSERT INTO transactions(tx_date,account_name,kind,category_name,summary,amount,created_at,updated_at)
  SELECT printf('%04d-%02d-%02d',2018+(i/600)%8,1+(i/600)%12,1+i%28),
    printf('TEST_ACCOUNT_%02d',i%60), CASE WHEN i%7=0 THEN 'income' ELSE 'expense' END,
    printf('TEST_CATEGORY_%d',i%3), CASE WHEN i%11=0 THEN '' ELSE printf('TEST_SUMMARY_%d',i%5) END,
    1+i%1000, printf('2025-12-%02dT00:00:00Z',1+i%28),'2025-12-01'
  FROM n;
  INSERT INTO transactions(tx_date,account_name,kind,category_name,summary,amount,created_at,updated_at) VALUES
    ('2026-01-01','TEST_CURRENT_ONLY','income','TEST','TEST',7,'2026-01-01','2026-01-01'),
    ('2026-02-01','TEST_FUTURE_ONLY','income','TEST','TEST',9,'2026-02-01','2026-02-01');
  INSERT INTO accounts(name,created_at,archived_at) VALUES('TEST_CURRENT_ONLY','2025-01-01','2025-02-01');
  INSERT INTO opening_balance_overrides(month,account_name,amount,reason,created_at,updated_at,
    updated_by_employee_id,updated_by_employee_no,updated_by_name,updated_by_role)
  SELECT '2025-07',printf('TEST_ACCOUNT_%02d',i),CASE WHEN i%2=0 THEN 0 ELSE 100 END,
    'TEST_ANCHOR','2025-07-01','2025-07-01','TEST','TEST','TEST','ADMIN'
  FROM (WITH RECURSIVE n(i) AS (SELECT 0 UNION ALL SELECT i+1 FROM n WHERE i<59) SELECT i FROM n);
  INSERT INTO opening_balance_overrides(month,account_name,amount,reason,created_at,updated_at,
    updated_by_employee_id,updated_by_employee_no,updated_by_name,updated_by_role) VALUES
    ('2026-01','TEST_ACCOUNT_00',555,'TEST_EXACT','2026-01-01','2026-01-01','TEST','TEST','TEST','ADMIN'),
    ('2020-01','TEST_ACCOUNT_00',999,'TEST_OLD','2020-01-01','2020-01-01','TEST','TEST','TEST','ADMIN'),
    ('2026-01','TEST_OVERRIDE_ONLY',-10,'TEST_ONLY','2026-01-01','2026-01-01','TEST','TEST','TEST','ADMIN');
`;
sql.exec(fixture);
const queries = [];
const db = { prepare(text) {
  const query = { sql: text, args: [] }; queries.push(query);
  const statement = sql.prepare(text);
  return {
    bind(...args) { query.args = args; return this; },
    async all() { return { results: statement.all(...query.args) }; },
    async first() { return statement.get(...query.args) || null; }
  };
} };
const frequentUrl = new URL('https://acc.example.test/api/summaries/frequent?kind=expense&account=TEST_ACCOUNT_01&category=TEST_CATEGORY_1');
const before = [];
for (const basis of ['tx_date', 'created_at']) {
  sql.prepare("INSERT OR REPLACE INTO app_settings(key,value) VALUES('frequent_summary_basis',?)").run(basis);
  before.push(await (await handleAccountingToolsApi(new Request(frequentUrl), { DB: db })).json());
}
sql.exec(ddl.at(-1));
assert.equal(sql.prepare("SELECT value FROM meta WHERE key='schema_version'").get().value, '12');
assert.throws(() => sql.exec(ddl.at(-1)), /already exists/, 'the forward migration is not silently applied twice');
const plan = query => sql.prepare('EXPLAIN QUERY PLAN ' + query.sql).all(...query.args).map(row => row.detail).join('\n');
for (const [i, basis] of ['tx_date', 'created_at'].entries()) {
  sql.prepare("INSERT OR REPLACE INTO app_settings(key,value) VALUES('frequent_summary_basis',?)").run(basis);
  assert.deepEqual(await (await handleAccountingToolsApi(new Request(frequentUrl), { DB: db })).json(), before[i],
    'indexes preserve the exact recent window, blank summaries and tied-date ordering');
  assert.match(plan(queries.at(-1)), new RegExp('COVERING INDEX idx_transactions_frequent_' + basis));
  assert.doesNotMatch(plan(queries.at(-1)), /TEMP B-TREE|SCAN transactions/);
}

// Independent row-by-row financial reference; production uses its canonical SQL.
const transactions = sql.prepare('SELECT * FROM transactions').all();
const overrides = sql.prepare('SELECT * FROM opening_balance_overrides ORDER BY month').all();
const master = sql.prepare('SELECT * FROM accounts').all();
for (const month of ['2017-12', '2020-01', '2025-07', '2026-01', '2026-02', '9999-12']) {
  const snapshot = await buildOpeningBalanceSnapshot(db, month);
  const names = new Set(master.filter(row => !row.archived_at).map(row => row.name));
  for (const row of transactions) if (row.tx_date.slice(0, 7) <= month) names.add(row.account_name);
  for (const row of overrides) if (row.month <= month) names.add(row.account_name);
  assert.deepEqual(snapshot.accounts.map(row => row.name).sort(), [...names].sort());
  for (const account of snapshot.accounts) {
    const prior = overrides.filter(row => row.account_name === account.name && row.month < month).at(-1);
    const exact = overrides.find(row => row.account_name === account.name && row.month === month);
    const net = transactions.filter(row => row.account_name === account.name && row.tx_date.slice(0, 7) < month
      && (!prior || row.tx_date.slice(0, 7) >= prior.month))
      .reduce((sum, row) => sum + (row.kind === 'income' ? row.amount : -row.amount), 0);
    assert.equal(account.automaticAmount, (prior?.amount ?? 0) + net, month + ':' + account.name);
    assert.equal(account.amount, exact?.amount ?? account.automaticAmount);
    assert.equal(account.automaticAnchorMonth, prior?.month ?? null);
  }
}
const openingQuery = queries.find(query => query.sql.includes('WITH RECURSIVE transaction_accounts') && query.args[0] === '2026-01');
assert.match(plan(openingQuery), /idx_transactions_opening_cover/);
assert.match(plan(openingQuery), /<expr>>\? AND <expr><\?/);
assert.doesNotMatch(plan(openingQuery), /SCAN t\b|TEMP B-TREE FOR (?:ORDER BY|GROUP BY)/);
const current = (await buildOpeningBalanceSnapshot(db, '2026-01')).accounts.find(row => row.name === 'TEST_CURRENT_ONLY');
assert.equal(current.isArchived, true); assert.equal(current.amount, 0);

sql.exec('DELETE FROM opening_balance_overrides;');
const noAnchors = await buildOpeningBalanceSnapshot(db, '2026-01');
for (const account of noAnchors.accounts) {
  assert.equal(account.amount, transactions.filter(row => row.account_name === account.name && row.tx_date < '2026-01-01')
    .reduce((sum, row) => sum + (row.kind === 'income' ? row.amount : -row.amount), 0));
}
const openingQueryNoAnchors = queries.filter(query => query.sql.includes('WITH RECURSIVE transaction_accounts')).at(-1);
// Optional local benchmark input contains synthetic rows only, never production data.
if (process.env.CYACC_QUERY_COST_OUTPUT) fs.writeFileSync(process.env.CYACC_QUERY_COST_OUTPUT, JSON.stringify({
  ddl, fixture, openingQuery, openingQueryNoAnchors,
  summaryQueries: queries.filter(query => query.sql.includes('SELECT summary, tx_date')).slice(0, 2)
}));
sql.exec('DELETE FROM transactions;');
assert.ok((await buildOpeningBalanceSnapshot(db, '2026-01')).accounts.every(row => row.amount === 0));
sql.close();
console.log('60,002 synthetic transactions: financial parity, zero/exact anchors, archived/orphan accounts, both summary indexes and bounded plans passed.');
