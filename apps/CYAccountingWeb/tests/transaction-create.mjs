import assert from 'node:assert/strict';
import fs from 'node:fs';
import vm from 'node:vm';
import { DatabaseSync } from 'node:sqlite';
import core from '../src/index.js';

const read = name => fs.readFileSync(new URL('../public/' + name, import.meta.url), 'utf8');
const app = read('app.js'), quick = read('quick-entry.js'), ledger = read('ledger-tools.js');
const piece = (source, start, end) => source.slice(source.indexOf(start), source.indexOf(end));
const deferred = () => { let resolve, reject; const promise = new Promise((yes, no) => { resolve = yes; reject = no; }); return { promise, resolve, reject }; };
const tick = async () => { for (let i = 0; i < 8; i++) await Promise.resolve(); };
const emitter = () => ({ listeners: new Map(), addEventListener(type, listener) {
  const list = this.listeners.get(type) || []; list.push(listener); this.listeners.set(type, list);
}, dispatchEvent(event) { this.listeners.get(event.type)?.forEach(listener => listener(event)); } });
const node = value => Object.assign(emitter(), { value, disabled: false, focus() {}, classList: { toggle() {} } });
const before = { id: 1, tx_date: '2026-10-01', account_name: 'TEST_ACCOUNT', category_name: 'TEST_CATEGORY', kind: 'expense', summary: 'before', amount: 7 };

function fixture(width) {
  const requests = [], renders = [], messages = [], openings = [], focused = [], revealed = [];
  const els = { form: node(''), txDate: node('2026-10-02'), accountName: node('TEST_ACCOUNT'), categoryName: node('TEST_CATEGORY'), summary: node('first'), amount: node('12'), monthFilter: node('2026-10'), kindButtons: [], entryLockBadge: node(''), ledgerLockBadge: node('') };
  for (const key of ['saveButton', 'transactionRows', 'settingsButton', 'settingsDialog', 'addAccountButton', 'newAccountName', 'accountRows', 'addGroupButton', 'newGroupName', 'categoryManager', 'openingMonth', 'saveOpeningButton', 'saveLockButton', 'clearLockButton', 'lockedThrough']) els[key] = node('');
  els.settingsTabs = []; els.settingsKindButtons = [];
  els.summary.focus = () => focused.push('summary'); els.amount.focus = () => focused.push('amount');
  const window = Object.assign(emitter(), { matchMedia: () => ({ matches: width < 768 }), cyaccRefreshLedgerView: async month => { openings.push(month); }, cyaccRevealSavedTransaction: (id,month) => revealed.push({id,month}) });
  const context = vm.createContext({ window, testEls: els, Event, CustomEvent: class { constructor(type, options) { this.type = type; this.detail = options?.detail; } },
    document: { addEventListener() {}, querySelector: () => null, querySelectorAll: () => [] },
    renderDesktopLedger() { renders.push(context.snapshot()); }, showMessage(text, error) { messages.push({ text, error }); },
    numericInput() {}, openSettings() {}, addAccount() {}, handleAccountAction() {}, addGroup() {}, handleCategoryAction() {}, loadOpeningBalances() {}, saveOpeningBalances() {},
    isLocked: () => false, jsonHeaders: () => ({}), prepareQuickEntryUi() {}, handleQuickEntryKindChanged() {}, loadFrequentSummaries() {},
    async api(path, options) { const request = deferred(); requests.push({ ...request, path, options, body: options?.body ? JSON.parse(options.body) : null }); return request.promise; }
  });
  vm.runInContext(app.slice(0, app.indexOf("if (document.readyState")) + '\nObject.assign(els,testEls);', context);
  vm.runInContext(piece(app, 'function bindEvents()', 'async function initialize()'), context);
  vm.runInContext(piece(app, 'function updateEntryLockState()', 'function openEditTransaction('), context);
  vm.runInContext(piece(app, 'function summaryCharacterUnits(', 'async function persistTransactionUpdate('), context);
  // Binding the real form lifecycle also exercises draft revision and month loading.
  context.bindEvents();
  vm.runInContext(piece(quick, 'function handleFastEntrySubmitted()', 'async function prepareQuickEntryUi()'), context);
  window.addEventListener('cyacc:entry-submitted', context.handleFastEntrySubmitted);
  vm.runInContext('state.transactions=[...testRows];', Object.assign(context, { testRows: [before] }));
  vm.runInContext("state.transactionMonth='2026-10';", context);
  context.snapshot = () => vm.runInContext('({ rows:state.transactions.map(row=>({...row})),month:state.transactionMonth })', context);
  context.pending = () => vm.runInContext('cyPendingTransactionCreates.size', context);
  const edit = (summary, amount = '12') => { els.summary.value = summary; els.amount.value = amount; els.form.dispatchEvent(new Event('input')); };
  const submit = () => context.saveTransaction({ preventDefault() {} });
  const ack = (request, id) => request.resolve({ ok: true, transaction: { id, tx_date: request.body.txDate, account_name: request.body.accountName,
    category_name: request.body.categoryName, kind: request.body.kind, summary: request.body.summary, amount: request.body.amount, created_at: '2026-10-02T00:00:00Z', updated_at: '2026-10-02T00:00:00Z' } });
  return { context, window, els, requests, renders, messages, openings, focused, revealed, edit, submit, ack };
}

for (const width of [400, 820, 1194, 1440]) {
  const f = fixture(width);
  const first = f.submit();
  assert.equal(f.context.snapshot().rows.length, 2, `${width}: pending row appears before POST resolves`);
  assert.ok(f.context.snapshot().rows.some(row => row.id < 0));
  assert.equal(f.els.summary.value, ''); assert.equal(f.els.amount.value, '');
  assert.deepEqual(f.focused, ['summary']); assert.equal(f.els.saveButton.disabled, false);
  let warned = false;
  f.window.dispatchEvent({ type: 'beforeunload', preventDefault() { warned = true; } });
  assert.equal(warned, true, 'leaving while a create is unresolved asks the browser to warn');
  f.edit('next draft', '31');
  f.ack(f.requests[0], 11); await first;
  assert.equal(f.els.summary.value, 'next draft'); assert.equal(f.els.amount.value, '31');
  assert.deepEqual(f.focused, ['summary'], 'late acknowledgements never steal focus');
  assert.equal(f.requests.length, 1, 'ordinary success never reloads the same month');
  assert.equal(f.context.snapshot().rows.filter(row => row.id === 11).length, 1);
  assert.deepEqual(f.revealed, [{id:11,month:'2026-10'}], 'only a confirmed server ID is revealed');

  const failed = f.submit(); f.edit('newer draft', '42');
  f.requests.at(-1).reject(new Error('TEST_FAILED')); await failed;
  assert.equal(f.els.summary.value, 'newer draft', 'rollback preserves a newer draft');
  assert.equal(f.context.snapshot().rows.length, 2, 'only the failed provisional row is removed');
  const untouched = f.submit(); f.requests.at(-1).reject(new Error('TEST_FAILED')); await untouched;
  assert.equal(f.els.summary.value, 'newer draft'); assert.equal(f.els.amount.value, '42', 'untouched failed input is restored');

  f.edit('same', '20'); const a = f.submit();
  f.edit('same', '20'); const b = f.submit();
  const reqA = f.requests.at(-2), reqB = f.requests.at(-1);
  f.ack(reqB, 13); await b; f.ack(reqA, 12); await a;
  assert.equal(f.context.snapshot().rows.filter(row => row.summary === 'same').length, 2, 'identical legitimate submissions survive reversed responses');
  assert.equal(f.context.pending(), 0);
  assert.deepEqual(f.revealed.map(x=>x.id), [11,13], 'failed saves and older out-of-order acknowledgements never move the ledger');

  f.edit('programmatic', '25'); const programmatic = f.submit();
  f.els.summary.value = 'selected suggestion';
  f.requests.at(-1).reject(new Error('TEST_FAILED')); await programmatic;
  assert.equal(f.els.summary.value, 'selected suggestion', 'programmatically selected text is never overwritten by rollback');

  f.edit('clear reset', '50'); const clear = f.submit();
  f.window.dispatchEvent(new Event('cyacc:entry-draft-reset'));
  f.requests.at(-1).reject(new Error('TEST_FAILED')); await clear;
  assert.equal(f.els.summary.value, '', 'clear/cancel is not undone by a late failure');
  f.els.amount.value = '0'; const count = f.requests.length; await f.submit();
  assert.equal(f.requests.length, count, 'invalid amount never creates a provisional row or POST');
}

// A GET can observe a committed row while the corresponding POST is still in transit.
const race = fixture(1440);
const posting = race.submit();
const loading = race.context.loadTransactions();
const real = { ...before, id: 20, tx_date: '2026-10-02', summary: 'first', amount: 12 };
race.requests[1].resolve({ transactions: [before, real] }); await tick();
assert.equal(race.context.snapshot().rows.length, 2, 'a snapshot is not accepted while its POST acknowledgement is unresolved');
assert.equal(race.context.snapshot().rows.filter(row => row.id === 20).length, 0);
race.ack(race.requests[0], 20); await posting; await tick();
assert.equal(race.requests.length, 3, 'revision guard requests a fresh post-commit snapshot');
race.requests[2].resolve({ transactions: [before, real] }); await loading;
assert.equal(race.context.snapshot().rows.length, 2, 'no duplicate provisional/server occurrence remains');

// Changing month or beginning another draft must never be undone by an old acknowledgement.
const navigation = fixture(1440); const saving = navigation.submit();
navigation.els.monthFilter.value = '2026-11'; const otherMonth = navigation.context.loadTransactions();
navigation.requests[1].resolve({ transactions: [], locked: false }); await otherMonth;
navigation.ack(navigation.requests[0], 22); await saving;
assert.equal(navigation.els.monthFilter.value, '2026-11');
assert.equal(navigation.revealed.length, 0, 'an acknowledgement from another month cannot move the current ledger');
assert.equal(navigation.context.snapshot().rows.length, 0);
assert.ok(navigation.openings.includes('2026-11'), 'prior-month success refreshes the displayed carried opening balance');

// The shared server returns the canonical stored row in its existing POST response.
const sqlite = new DatabaseSync(':memory:');
for (const name of fs.readdirSync(new URL('../migrations/', import.meta.url)).filter(name => name.endsWith('.sql')).sort()) sqlite.exec(fs.readFileSync(new URL('../migrations/' + name, import.meta.url), 'utf8'));
const db = { prepare(sql) { let params = []; return { bind(...values) { params = values; return this; },
  first: async () => sqlite.prepare(sql).get(...params) || null,
  all: async () => ({ results: sqlite.prepare(sql).all(...params) }),
  run: async () => ({ meta: { last_row_id: Number(sqlite.prepare(sql).run(...params).lastInsertRowid) } }) }; } };
const category = sqlite.prepare("SELECT name FROM categories WHERE kind='expense' LIMIT 1").get().name;
const account = sqlite.prepare('SELECT name FROM accounts WHERE archived_at IS NULL LIMIT 1').get().name;
const body = { txDate: '2026-10-02', accountName: account, categoryName: category, kind: 'expense', summary: '  TEST_STORED_ROW  ', amount: 12 };
const request = () => new Request('https://test.invalid/api/transactions', { method: 'POST', body: JSON.stringify(body) });
const response = await core.fetch(request(), { DB: db }); assert.equal(response.status, 201);
const created = await response.json();
assert.deepEqual(created.transaction, { ...sqlite.prepare('SELECT * FROM transactions WHERE id=?').get(created.id) });
assert.equal(created.transaction.summary, 'TEST_STORED_ROW');
sqlite.prepare("INSERT OR REPLACE INTO app_settings(key,value) VALUES('locked_through','2026-10')").run();
assert.equal((await core.fetch(request(), { DB: db })).status, 409);
assert.equal(sqlite.prepare('SELECT COUNT(*) AS n FROM transactions').get().n, 1, 'server still prevents locked-month writes');
sqlite.close();

const pendingRow = piece(ledger, 'function renderLedgerRow(', 'function updateLedgerHeader()');
assert.match(pendingRow, /id < 0 \? '<span class="hint" role="status">儲存中/);
assert.match(read('adaptive-ui.js'), /function beginEntryTransactionEdit\(id\) \{\s+if \(!Number\.isSafeInteger\(Number\(id\)\) \|\| Number\(id\) <= 0\) return;/);
console.log('Shared optimistic creation: immediate rows/focus, concurrent identical writes, rollback/draft preservation, month/read races, server roundtrip and lock guard passed.');
