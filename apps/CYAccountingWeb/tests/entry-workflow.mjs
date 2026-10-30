import assert from 'node:assert/strict';
import fs from 'node:fs';
import vm from 'node:vm';

const read = name => fs.readFileSync(new URL('../public/' + name, import.meta.url), 'utf8');
const app = read('app.js'), adaptive = read('adaptive-ui.js'), quick = read('quick-entry.js');
const piece = (source, start, end) => source.slice(source.indexOf(start), source.indexOf(end));
const classes = () => ({ values: new Set(), add(...names) { names.forEach(n => this.values.add(n)); }, remove(...names) { names.forEach(n => this.values.delete(n)); }, toggle(name, on) { if (on) this.values.add(name); else this.values.delete(name); } });
const node = value => ({ value, disabled: false, listeners: new Map(), classList: classes(),
  addEventListener(type, listener) { (this.listeners.get(type) || this.listeners.set(type, []).get(type)).push(listener); },
  dispatchEvent(event) { this.listeners.get(event.type)?.forEach(fn => fn(event)); }, focus() {} });
const select = value => Object.assign(node(value), { options: [{ value }], append(option) {
  this.options.push(option); option.remove = () => { this.options = this.options.filter(x => x !== option); };
}, querySelectorAll() { return this.options.filter(x => x.dataset?.entryEditTemporary === '1'); } });

const els = { txDate: node('2026-10-01'), accountName: select('TEST_DRAFT_ACCOUNT'), categoryName: select('TEST_DRAFT_CATEGORY'),
  summary: node('新增草稿'), amount: node('17'), saveButton: node(''), kindButtons: [node(''), node('')], monthFilter: node('2026-10') };
const draft = Object.fromEntries(['txDate', 'accountName', 'categoryName', 'summary', 'amount'].map(key => [key, els[key].value]));
const nodes = { '#desktopEntryClearButton': node(''), '#transactionRows': node(''), '#transactionForm': node(''), '#mobileMainNav': node(''),
  '.entry-card': Object.assign(node(''), { scrollIntoView() {} }) };
const documentListeners = new Map(), windowListeners = new Map();
const tx = { id: 1, tx_date: '2026-10-02', account_name: 'TEST_OLD_ACCOUNT', category_name: 'TEST_OLD_CATEGORY', kind: 'income', summary: '原摘要', amount: 99 };
const writes = [], messages = [], focus = [];
let fail = false, locked = false;
els.summary.focus = () => focus.push('summary'); els.amount.focus = () => focus.push('amount');
const context = vm.createContext({ CustomEvent: class { constructor(type, options) { this.type = type; this.detail = options?.detail; } }, Number, String, Boolean, Array, Date, JSON, Event, Error,
  els, state: { kind: 'expense', accounts: [], categories: [], transactions: [tx, { ...tx, id: 2, summary: '第二筆' }] },
  cyEntryTransactionEdit: null, CY_TOUCH_WORKSPACE_MOBILE: '(max-width: 767px)',
  window: { dispatchEvent() {}, scrollY: 0, addEventListener: (type, listener) => windowListeners.set(type, listener) },
  document: { querySelector: selector => nodes[selector] || null, createElement: () => ({ dataset: {} }), addEventListener: (type, listener) => documentListeners.set(type, listener) },
  isDesktopInteractionWorkspace: () => true, usesEntryTransactionEditor: () => true, isTabletWorkspace: () => false,
  setEntryKind(kind) { context.state.kind = kind; }, isLocked: () => locked,
  updateEntryLockState() {}, syncEntryUiAccountChoices() {},
  showMessage(text) { messages.push(text); }, jsonHeaders: () => ({}), renderDesktopLedger() {},
  async api(path, request) { const body = JSON.parse(request.body); writes.push({ path, ...request, body }); if (fail) throw new Error('TEST_WRITE_FAILED'); return { transaction: { id: 10, tx_date: body.txDate } }; },
  switchTouchWorkspaceMobilePage() { throw new Error('desktop must not use mobile page navigation'); }
});
vm.runInContext('let cyTransactionMutationRevision = 0; const cyPendingTransactionUpdates = new Map(); const cyPendingTransactionCreates = new Map(); let cyTransactionCreateId = 0; let cyEntryDraftRevision = 0;\n' + piece(app, 'function summaryCharacterUnits(', 'async function deleteTransaction('), context);
vm.runInContext(piece(adaptive, 'function syncTouchWorkspaceEntrySecondaryAction()', 'function clearTouchWorkspaceEntryForm()'), context);
vm.runInContext(piece(adaptive, 'function cancelEntryTransactionEditAndReturn()', 'function handleTouchWorkspaceSaveMessage('), context);
vm.runInContext(piece(adaptive, 'function setupEntryTransactionEditing()', 'function switchTouchWorkspaceMobilePage('), context);
context.setupEntryTransactionEditing();
const assertDraft = () => { for (const [key, value] of Object.entries(draft)) assert.equal(els[key].value, value, key); assert.equal(context.state.kind, 'expense'); };
vm.runInContext(piece(app, 'function openEditTransaction(', 'function summaryCharacterUnits('), context);
const begin = () => context.openEditTransaction(1);
begin();
assert.equal(els.summary.value, '原摘要'); assert.equal(els.accountName.value, 'TEST_OLD_ACCOUNT');
assert.equal(els.saveButton.textContent, '儲存修改'); assert.equal(nodes['#desktopEntryClearButton'].textContent, '取消');
assert.ok(els.kindButtons.every(button => button.disabled));
els.summary.value = '未儲存修改';
context.cancelEntryTransactionEditAndReturn(); assertDraft();
assert.equal(nodes['#desktopEntryClearButton'].textContent, '清空'); assert.equal(writes.length, 0);
assert.ok(!els.accountName.options.some(option => option.dataset?.entryEditTemporary), 'historical edit options are removed');
begin(); els.summary.value = '第一筆未存'; context.beginEntryTransactionEdit(2); context.cancelEntryTransactionEditAndReturn(); assertDraft();
for (const cancel of [
  () => els.monthFilter.dispatchEvent({ type: 'change' }),
  () => documentListeners.get('keydown')({ key: 'Escape', preventDefault() {} }),
  () => documentListeners.get('click')({ target: { closest: () => ({ id: 'settingsButton' }) } }),
  () => windowListeners.get('pagehide')()
]) { begin(); els.summary.value = '未儲存'; cancel(); assertDraft(); assert.equal(writes.length, 0); }
locked = true; begin(); assert.equal(context.cyEntryTransactionEdit, null); locked = false;
begin(); els.summary.value = '完成修改'; els.amount.value = '123';
await context.saveEntryTransactionEdit();
assert.equal(writes.length, 1); assert.equal(writes[0].method, 'PUT'); assert.equal(writes[0].path, '/api/transactions/1');
assert.equal(writes[0].body.summary, '完成修改'); assert.equal(context.state.transactions.find(row => row.id === 1).amount, 123);
assert.equal(context.cyEntryTransactionEdit, null); assertDraft(); assert.equal(messages.at(-1), '修改成功。');
begin(); els.summary.value = '失敗修改'; els.amount.value = '456'; fail = true;
await context.saveEntryTransactionEdit();
assert.equal(context.state.transactions.find(row => row.id === 1).amount, 123, 'shared canonical writer rolls back failed update');
assert.equal(context.state.transactions.find(row => row.id === 1).summary, '完成修改'); assertDraft();
assert.equal(messages.at(-1), 'TEST_WRITE_FAILED');

// Successful creation focuses the text input once after refresh, never the
// numeric field; failed validation keeps the original amount correction path.
fail = false; locked = false; writes.length = 0; focus.length = 0;
context.window.dispatchEvent = event => { if (event.type === 'cyacc:entry-submitted') context.handleFastEntrySubmitted(); };
context.state.transactionMonth = '2026-10';
context.loadFrequentSummaries = () => {}; context.loadTransactions = async () => {};
vm.runInContext(piece(quick, 'function handleFastEntrySubmitted()', 'async function prepareQuickEntryUi()'), context);
vm.runInContext(piece(app, 'async function saveTransaction(', 'async function loadTransactions('), context);
await context.saveTransaction({ preventDefault() {} });
assert.deepEqual(focus, ['summary']); assert.equal(writes[0].method, 'POST');
focus.length = 0; writes.length = 0; els.amount.value = '0';
await context.saveTransaction({ preventDefault() {} });
assert.deepEqual(focus, ['amount']); assert.equal(writes.length, 0);
console.log('Desktop shared entry edit, unsaved cancellation, PUT/rollback and single post-save text focus passed.');

const scrollEvents = new Map();
let desktop = true, backToTop, scrollRequest;
const scrolling = vm.createContext({
  document: { querySelector: () => backToTop, createElement: () => Object.assign(node(''), { setAttribute() {} }), body: { append(button) { backToTop = button; } } },
  window: { scrollY: 0, addEventListener: (type, listener) => scrollEvents.set(type, listener),
    matchMedia: () => ({ matches: false }), scrollTo(request) { scrollRequest = request; } },
  isDesktopInteractionWorkspace: () => desktop
});
vm.runInContext(piece(adaptive, 'function setupDesktopBackToTop()', 'function setupAdaptiveViewportState()'), scrolling);
scrolling.setupDesktopBackToTop();
assert.equal(backToTop.hidden, true);
scrolling.window.scrollY = 200; scrollEvents.get('scroll')(); assert.equal(backToTop.hidden, false);
backToTop.dispatchEvent({ type: 'click' }); assert.equal(scrollRequest.top, 0); assert.equal(scrollRequest.behavior, 'smooth');
desktop = false; scrollEvents.get('resize')(); assert.equal(backToTop.hidden, true, 'mobile and tablet retain their presentation');
console.log('Desktop back-to-top visibility and scroll action passed.');

// A confirmed desktop save reveals its canonical row inside the ledger only.
const ledger = read('ledger-tools.js');
let desktopReveal = true, rowPresent = true, revealRenders = 0;
const revealScrolls = [], searchField = { value: 'old filter' };
const revealMonth = { value: '2026-10' };
const savedRow = { getBoundingClientRect: () => ({ top: 900, height: 40 }) };
const ledgerWrap = { scrollTop: 100, clientTop: 1, clientHeight: 500,
  getBoundingClientRect: () => ({ top: 100 }),
  querySelector: () => ({ getBoundingClientRect: () => ({ height: 30 }) }),
  scrollTo: request => revealScrolls.push(request) };
const revealContext = vm.createContext({
  els: { monthFilter: revealMonth, transactionRows: { innerHTML: '', querySelector: selector => rowPresent && selector === 'tr[data-transaction-id="42"]' ? savedRow : null } },
  window: { cyIsDesktopInteractionWorkspace: () => desktopReveal, dispatchEvent() {} },
  document: { querySelector: selector => selector === '.ledger-card .table-wrap' ? ledgerWrap : selector === '#ledgerSummarySearch' ? searchField : null },
  CustomEvent: class { constructor(type, options) { this.type = type; this.detail = options?.detail; } },
  renderDesktopLedger() { revealRenders++; revealContext.writeLedgerRows('TEST_FILTERED_ROWS'); }
});
vm.runInContext("let cyLedgerSavedTarget=null,cyLedgerSearch='';\n" + ledger.slice(ledger.indexOf('function revealSavedTransaction(')), revealContext);
revealContext.revealSavedTransaction(42, '2026-10');
assert.equal(revealScrolls.length, 1); assert.equal(revealScrolls[0].top, 654); assert.equal(revealScrolls[0].behavior, 'auto');
revealContext.writeLedgerRows('TEST_RERENDER'); assert.equal(revealScrolls.length, 1, 'unrelated rerenders do not repeatedly pull the ledger');
rowPresent = false; revealContext.revealSavedTransaction(42, '2026-10');
assert.equal(revealScrolls.length, 1); rowPresent = true; revealContext.writeLedgerRows('TEST_MONTH_LOADED');
assert.equal(revealScrolls.length, 2, 'a still-loading month reveals the row after canonical rendering');
rowPresent = false; revealContext.revealSavedTransaction(42, '2026-10'); revealMonth.value = '2026-11';
rowPresent = true; revealContext.writeLedgerRows('TEST_OTHER_MONTH'); revealMonth.value = '2026-10';
revealContext.writeLedgerRows('TEST_RETURN'); assert.equal(revealScrolls.length, 2, 'changing month discards a deferred reveal');
vm.runInContext("cyLedgerSearch='old filter'", revealContext);
revealContext.revealSavedTransaction(42, '2026-10');
assert.equal(searchField.value, ''); assert.equal(revealRenders, 1); assert.equal(revealScrolls.length, 3, 'save clears a summary filter so the saved row is visible');
desktopReveal = false; revealContext.revealSavedTransaction(42, '2026-10');
desktopReveal = true; revealContext.revealSavedTransaction(-1, '2026-10'); revealContext.revealSavedTransaction(42, '2026-11');
assert.equal(revealScrolls.length, 3, 'touch, provisional IDs and another month never scroll');
console.log('Confirmed desktop save: ledger-only scroll, delayed render, filter reset, one-shot/month guards and unchanged touch behavior passed.');
