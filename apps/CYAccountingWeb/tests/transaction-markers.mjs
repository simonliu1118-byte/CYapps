import assert from 'node:assert/strict';
import fs from 'node:fs';
import vm from 'node:vm';
import core from '../src/index.js';
import { FixtureD1 } from './support/backup-fixtures.mjs';

const db = new FixtureD1();
const storedSchema = db.db.prepare("SELECT value FROM meta WHERE key='schema_version'").get();
assert.equal(storedSchema.value, '15');
const columns = db.db.prepare('PRAGMA table_info(transactions)').all();
assert.ok(columns.some(column => column.name === 'marker_color'));
assert.ok(columns.some(column => column.name === 'marker_target'));

const account = db.db.prepare('SELECT name FROM accounts WHERE archived_at IS NULL LIMIT 1').get().name;
const category = db.db.prepare("SELECT name FROM categories WHERE kind='expense' LIMIT 1").get().name;
const request = (path, method = 'GET', body) => new Request('https://acc.example.test' + path, {
  method, ...(body === undefined ? {} : { headers: { 'content-type': 'application/json' }, body: JSON.stringify(body) })
});
const env = { DB: db };
const initialBody = { txDate: '2026-10-15', accountName: account, kind: 'expense',
  categoryName: category, summary: '螢光摘要測試', amount: 880 };
const createdResponse = await core.fetch(request('/api/transactions', 'POST', initialBody), env);
assert.equal(createdResponse.status, 201);
const created = await createdResponse.json();
// FixtureD1 reports SQLite lastInsertRowid, not the production D1 last_row_id alias.
const id = db.db.prepare('SELECT id FROM transactions WHERE summary=?').get(initialBody.summary).id;
assert.equal(created.transaction.marker_color, null);
assert.equal(created.transaction.marker_target, 'summary');
assert.equal(db.db.prepare('SELECT marker_color FROM transactions WHERE id=?').get(id).marker_color, null);

const change = (color, markerTarget) => core.fetch(request('/api/transactions/' + id + '/marker', 'PATCH', { markerColor: color, ...(markerTarget === undefined ? {} : { markerTarget }) }), env);
for (const color of ['orange', 'pink', 'yellow', 'green']) {
  const response = await change(color);
  assert.equal(response.status, 200);
  assert.equal((await response.json()).markerColor, color);
  const stored = db.db.prepare('SELECT summary, category_name, amount, marker_color, marker_target FROM transactions WHERE id=?').get(id);
  assert.equal(stored.marker_color, color);
  assert.equal(stored.marker_target, 'summary');
  assert.equal(stored.summary, initialBody.summary, 'marker must never change accounting summary');
  assert.equal(stored.amount, initialBody.amount, 'marker must never change amount');
  const month = await core.fetch(request('/api/transactions?month=2026-10'), env);
  assert.equal((await month.json()).transactions.find(tx => tx.id === id).marker_color, color,
    'reloading the month must preserve the marker');
}
for (const invalid of ['blue', '', 0, '#ff0']) {
  assert.equal((await change(invalid)).status, 400, 'unexpected marker should fail validation');
}
assert.equal((await core.fetch(request('/api/transactions/' + id + '/marker', 'PATCH', {}), env)).status, 400);
assert.equal((await core.fetch(request('/api/transactions/999999/marker', 'PATCH', { markerColor: 'pink' }), env)).status, 404);
assert.equal((await change(null)).status, 200);
assert.equal(db.db.prepare('SELECT marker_color FROM transactions WHERE id=?').get(id).marker_color, null);
assert.equal((await change('yellow')).status, 200);
const byCategory = await change('green', 'category');
assert.equal(byCategory.status, 200);
assert.equal((await byCategory.json()).markerTarget, 'category');
assert.equal(db.db.prepare('SELECT marker_target FROM transactions WHERE id=?').get(id).marker_target, 'category');
assert.equal((await change('pink', 'invalid')).status, 400);
assert.equal((await core.fetch(request('/api/transactions?month=2026-10'), env).then(r=>r.json())).transactions.find(tx=>tx.id===id).marker_target, 'category');
db.db.prepare("INSERT OR REPLACE INTO app_settings(key,value) VALUES('locked_through','2026-10')").run();
assert.equal((await change('orange', 'summary')).status, 200, 'locked month must allow presentation markers');
assert.equal(db.db.prepare('SELECT marker_color, marker_target FROM transactions WHERE id=?').get(id).marker_color, 'orange');
assert.equal(db.db.prepare('SELECT marker_target FROM transactions WHERE id=?').get(id).marker_target, 'summary');
assert.equal((await change('pink', 'category')).status, 200, 'locked month must permit moving marker onto category');
assert.equal(db.db.prepare('SELECT category_name, summary, amount FROM transactions WHERE id=?').get(id).amount, 880, 'locked-month marking cannot change money');
assert.equal((await change(null)).status, 200, 'locked month must allow clearing marker');
assert.equal(db.db.prepare('SELECT marker_color, marker_target FROM transactions WHERE id=?').get(id).marker_target, 'summary');
assert.equal((await core.fetch(request('/api/transactions/'+id, 'PUT', initialBody), env)).status, 409, 'normal accounting edit remains locked');
db.close();

const ledger = fs.readFileSync(new URL('../public/ledger-tools.js', import.meta.url), 'utf8');
const css = fs.readFileSync(new URL('../public/accounting-ui.css', import.meta.url), 'utf8');
const frontend = fs.readFileSync(new URL('../public/app.js', import.meta.url), 'utf8');
const worker = fs.readFileSync(new URL('../src/app.js', import.meta.url), 'utf8');
assert.match(frontend, /cyPendingTransactionUpdates\.set\(id, \{ original, updated \}\)/,
  'marker mutation must reuse canonical optimistic lifecycle');
assert.match(frontend, /data-marker-id/);
assert.match(worker, /!canWriteAccounting\(resolved\.principal\).*?\['GET', 'HEAD'\]/s,
  'the Worker must reject read-only marker writes');
assert.match(css, /box-decoration-break:\s*clone/);
assert.match(css, /\.transaction-text-highlight\s*\{/);
assert.match(ledger, /標記位置/);
assert.match(ledger, /\['category', '科目'\]/);
assert.match(ledger, /\['summary', '摘要'\]/);
for (const color of ['orange', 'pink', 'yellow', 'green']) {
  assert.ok(css.includes('[data-marker-color="' + color + '"]'), 'missing color palette ' + color);
}
const begin = ledger.indexOf('function renderLedgerRow(');
const end = ledger.indexOf('function updateLedgerHeader()', begin);
assert.ok(begin >= 0 && end > begin);
const renderer = vm.runInNewContext(ledger.slice(begin, end) + '\nrenderLedgerRow', {
  window: { cyLedgerBalanceBreakdowns: new Map() }, document: { body: { dataset: {} } },
  isLocked: () => false, escapeHtml: text => String(text).replaceAll('&', '&amp;').replaceAll('<', '&lt;').replaceAll('>', '&gt;'),
  splitLedgerAccountName: name => [name],
  ledgerAccountVisual: () => ({ slot: 1, background: '#fff', foreground: '#111' }),
  money: value => String(value)
});
const row = { id: 4, tx_date: '2026-10-15', account_name: '現金', kind: 'expense',
  category_name: '一般', summary: '雜支 & 收據', amount: 880 };
const plain = renderer({ ...row, marker_color: null }, 100);
assert.ok(!plain.includes('transaction-text-highlight'), 'plain summaries must stay unmarked');
for (const color of ['orange', 'pink', 'yellow', 'green']) {
  const html = renderer({ ...row, marker_color: color, marker_target: 'summary' }, 100);
  assert.match(html, new RegExp('<td class="summary"><mark class="transaction-text-highlight" data-marker-color="' + color + '"'));
  assert.ok(html.includes('雜支 &amp; 收據</mark></td>'), 'only escaped summary text receives highlighting');
  assert.ok(!html.includes('transaction-marker-dot'), 'no trailing dot in the summary');
  const cat = renderer({ ...row, marker_color: color, marker_target: 'category' }, 100);
  assert.match(cat, new RegExp('<td class="ledger-category"><mark class="transaction-text-highlight" data-marker-color="' + color + '"'));
  assert.ok(cat.includes('一般</mark></td>'), 'category text is highlighted, not the cell');
  assert.ok(cat.includes('<td class="summary">雜支 &amp; 收據</td>'), 'summary must be plain when category selected');
}
assert.match(renderer({ ...row, marker_color: 'pink' }, 100), /data-marker-id="4"/);
assert.match(renderer({ ...row, marker_color: 'pink' }, 100), /更改標記/);
const lockedMarkup = vm.runInNewContext(ledger.slice(begin,end)+'\nrenderLedgerRow', {window:{cyLedgerBalanceBreakdowns:new Map()},document:{body:{dataset:{}}},isLocked:()=>true,escapeHtml:x=>String(x),splitLedgerAccountName:x=>[x],ledgerAccountVisual:()=>({slot:1,background:'#fff',foreground:'#111'}),money:x=>String(x)})({...row,marker_color:'pink'}, 100);
assert.match(lockedMarkup, /data-marker-id="4"[^>]*>/, 'marker remains actionable on locked rows');
assert.match(lockedMarkup, /data-edit-id="4"[^>]*disabled/, 'accounting edit remains disabled on locked rows');
console.log('Schema15 marker targets, category/summary highlight, locked-month metadata, unchanged financial values passed.');
