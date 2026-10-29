import assert from 'node:assert/strict';
import fs from 'node:fs';
import vm from 'node:vm';

const source = fs.readFileSync(new URL('../public/app.js', import.meta.url), 'utf8');
const calls = [];
const context = vm.createContext({
  AbortController, Map, Promise, Error,
  window: { setTimeout, clearTimeout, cyaccBookEpoch: 'book-a' },
  fetch(path, options) {
    return new Promise(resolve => calls.push({ path, options,
      finish(data, status = 200) { resolve({ ok: status < 400, status, json: async () => data }); } }));
  }
});
vm.runInContext(source.slice(source.indexOf('window.cyaccApi = api;'), source.indexOf('function jsonHeaders()')), context);
const api = context.window.cyaccApi;
const tick = async () => { for (let i = 0; i < 15; i++) await Promise.resolve(); };
const opening = '/api/opening-balances?month=2026-05';
const frequent = '/api/summaries/frequent?kind=income&account=TEST&category=TEST';
for (const path of [opening, frequent]) {
  const count = calls.length;
  const reads = Array.from({ length: 10 }, () => api(path));
  await tick();
  assert.equal(calls.length, count + 1, 'concurrent identical reads issue one fetch');
  calls.at(-1).finish({ ok: true, value: 1 });
  assert.deepEqual((await Promise.all(reads)).map(data => data.value), Array(10).fill(1));
  const fresh = api(path); await tick();
  assert.equal(calls.length, count + 2, 'settled financial data is not cached');
  calls.at(-1).finish({ ok: true, value: 2 }); await fresh;
}
const first = api(opening), otherMonth = api(opening.replace('05', '06'));
await tick();
assert.notEqual(calls.at(-1).path, calls.at(-2).path);
calls.at(-2).finish({ ok: true }); calls.at(-1).finish({ ok: true }); await Promise.all([first, otherMonth]);

// A read started before a successful or ambiguous write must not publish old data.
for (const status of [200, 500]) {
  const old = api(opening); await tick(); const oldCall = calls.at(-1);
  const write = api('/api/transactions', { method: 'POST' });
  const writeResult = status === 200 ? write : assert.rejects(write, /WRITE_FAILED/);
  await tick(); calls.at(-1).finish({ ok: status === 200, error: 'WRITE_FAILED' }, status); await writeResult;
  const fresh = api(opening); await tick(); const newCall = calls.at(-1);
  oldCall.finish({ ok: true, value: 'stale' }); await tick();
  assert.equal(calls.at(-1), newCall, 'old readers join the new generation');
  newCall.finish({ ok: true, value: 'fresh' });
  assert.equal((await old).value, 'fresh'); assert.equal((await fresh).value, 'fresh');
}

const failure = api(frequent); const failed = assert.rejects(failure, /TEST_READ_FAILED/);
await tick(); calls.at(-1).finish({ ok: false, error: 'TEST_READ_FAILED' }, 500); await failed;
const retry = api(frequent); await tick(); calls.at(-1).finish({ ok: true }); await retry;
const timed = api(frequent, {}, 1); await assert.rejects(timed, /連線逾時/);
const afterTimeout = api(frequent); await tick(); calls.at(-1).finish({ ok: true }); await afterTimeout;

const oldBook = api(opening); await tick(); const oldBookCall = calls.at(-1);
context.window.cyaccBookEpoch = 'book-b';
oldBookCall.finish({ ok: true, value: 'old book' }); await tick();
assert.equal(calls.at(-1).options.headers['x-cyacc-book-epoch'], 'book-b');
calls.at(-1).finish({ ok: true, value: 'new book' });
assert.equal((await oldBook).value, 'new book');

const changing = api(opening);
const limited = assert.rejects(changing, /資料已更新/);
await tick(); let changingCall = calls.at(-1);
context.invalidateAccountingReads(); changingCall.finish({ ok: true }); await tick();
changingCall = calls.at(-1);
context.invalidateAccountingReads(); changingCall.finish({ ok: true }); await limited;
assert.equal(calls.at(-1), changingCall, 'continuous mutations cannot cause an unbounded read loop');

// Quick-entry keeps its existing latest-request guard, even when a selection is cleared.
const quick = fs.readFileSync(new URL('../public/quick-entry.js', import.meta.url), 'utf8');
let finishSummary;
const group = { hidden: true, classList: { add() { group.hidden = true; }, toggle(_name, hidden) { group.hidden = hidden; } } };
const container = { innerHTML: '' };
const quickContext = vm.createContext({ URLSearchParams, state: { kind: 'income' },
  els: { accountName: { value: 'TEST' }, categoryName: { value: 'TEST' } },
  document: { querySelector: selector => selector === '#summarySuggestionGroup' ? group : container },
  api: () => new Promise(resolve => { finishSummary = resolve; }), escapeHtml: value => value
});
vm.runInContext('let cyFastSummaryRequest = 0;\n' + quick.slice(quick.indexOf('async function loadFrequentSummaries()')), quickContext);
const summary = quickContext.loadFrequentSummaries();
quickContext.els.accountName.value = '';
await quickContext.loadFrequentSummaries();
finishSummary({ summaries: [{ summary: 'stale', count: 2 }] }); await summary;
assert.equal(container.innerHTML, ''); assert.equal(group.hidden, true);
console.log('Accounting browser read coalescing, mutation/epoch invalidation, retry and stale-selection regressions passed.');
