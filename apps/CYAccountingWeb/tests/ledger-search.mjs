import assert from 'node:assert/strict';
import fs from 'node:fs';
import vm from 'node:vm';
import { DatabaseSync } from 'node:sqlite';
import core from '../src/index.js';
import app from '../src/app.js';

// Synthetic SQLite data exercises the real query, cursor ordering and date index.
const sql = new DatabaseSync(':memory:');
for (const name of fs.readdirSync(new URL('../migrations/', import.meta.url)).filter(n => n.endsWith('.sql')).sort()) {
  sql.exec(fs.readFileSync(new URL('../migrations/' + name, import.meta.url), 'utf8'));
}
const insert = sql.prepare(`INSERT INTO transactions(tx_date,account_name,kind,category_name,summary,amount,created_at,updated_at) VALUES(?,?,?,?,?,?,'2026-01-01','2026-01-01')`);
for (let i = 0; i < 113; i++) insert.run('2026-03-15', 'TEST_ARCHIVED', 'expense', 'TEST_CATEGORY', `Needle ${i}`, i + 1);
insert.run('2024-02-29', 'TEST_ARCHIVED', 'income', 'TEST_CATEGORY', 'needle leap day', 7);
insert.run('2026-02-01', 'needle account', 'expense', 'TEST_CATEGORY', 'other', 8);
insert.run('2026-01-01', 'TEST_ARCHIVED', 'income', 'needle category', 'other', 9);
insert.run('2023-12-31', 'TEST_ARCHIVED', 'expense', 'TEST_CATEGORY', 'needle outside', 10);
insert.run('2027-01-01', 'TEST_ARCHIVED', 'expense', 'TEST_CATEGORY', 'needle outside', 11);
insert.run('2026-03-16', 'TEST_ARCHIVED', 'expense', 'TEST_CATEGORY', '50%_OFF', 12);
const queries = [];
const db = { prepare(query) {
  let values = [];
  return { bind(...args) { values = args; return this; },
    async first() { return sql.prepare(query).get(...values) || null; },
    async all() { queries.push({ query, values, plan: sql.prepare('EXPLAIN QUERY PLAN ' + query).all(...values) }); return { results: sql.prepare(query).all(...values) }; }
  };
} };
const params = { start: '2024-01', end: '2026-12', q: 'needle' };
const request = (extra = {}) => new Request('https://test.invalid/api/transactions/search?' + new URLSearchParams({ ...params, ...extra }));
const search = async extra => { const response = await core.fetch(request(extra), { DB: db }); return { status: response.status, body: await response.json() }; };
let result = await search();
assert.equal(result.status, 200); assert.equal(result.body.transactions.length, 50); assert.ok(result.body.nextCursor);
const all = [...result.body.transactions];
for (let page = 1; page < 3; page++) {
  result = await search({ cursor: result.body.nextCursor });
  all.push(...result.body.transactions);
}
assert.equal(result.body.nextCursor, null); assert.equal(all.length, 116); assert.equal(new Set(all.map(r => r.id)).size, 116);
assert.equal(all[0].summary, 'Needle 112', 'equal dates use ID as the stable descending tie break');
assert.ok(all.some(r => r.account_name === 'needle account')); assert.ok(all.some(r => r.category_name === 'needle category'));
assert.equal(queries.length, 3, 'one bounded SELECT per page, without a full COUNT or one request per month');
for (const query of queries) {
  assert.match(query.plan.map(row => row.detail).join('\n'), /SEARCH transactions USING INDEX idx_transactions_date/);
  assert.doesNotMatch(query.query, /OFFSET|COUNT\(/);
}
result = await search({ start: '2024-02', end: '2024-02' });
assert.deepEqual(result.body.transactions.map(r => r.summary), ['needle leap day']);
result = await search({ q: '50%_off' }); assert.equal(result.body.transactions.length, 1, '% and underscore are literal keyword characters');
result = await search({ q: "' OR 1=1 --" }); assert.equal(result.body.transactions.length, 0);
const beforeInvalid = queries.length;
for (const extra of [{ start: '2023-12' }, { start: '2026-13' }, { start: '2027-01' }, { q: '  ' }, { q: 'x'.repeat(101) }, { cursor: 'not-json' }]) {
  assert.equal((await search(extra)).status, 400);
}
assert.equal(queries.length, beforeInvalid, 'invalid or over-36-month searches never query transactions');

// The production entry point retains its shared CYID authorization, including USER reads.
const token = 'cyid_' + 'a'.repeat(64);
const env = { DB: db, CYID_WORKSPACE_ID: 'workspace-ci-placeholder', CYID_APPLICATION_ID: 'CYACC_CI',
  IDENTITY: { async fetch() { return Response.json({ principal: { workspaceId: 'workspace-ci-placeholder', employeeId: 'employee-ci', employeeNo: '0123', displayName: 'CI User', workspaceRole: 'USER', isIdentityAdmin: false, emailVerified: true, isWorkspaceSuperAdmin: false, credentialVersion: 1, employeeRevision: 1 }, session: { expiresAt: new Date(Date.now() + 60_000).toISOString() } }); } } };
assert.equal((await app.fetch(request(), env)).status, 401);
const authenticated = new Request(request(), { headers: { cookie: `cyaccounting_session=${token}` } });
const authResult=await app.fetch(authenticated, env); assert.equal(authResult.status,200, JSON.stringify(await authResult.json()));
sql.close();

// The controller uses the same central renderer and preserves the monthly snapshot.
const source = fs.readFileSync(new URL('../public/ledger-tools.js', import.meta.url), 'utf8');
const appSource = fs.readFileSync(new URL('../public/app.js', import.meta.url), 'utf8');
const nodes = new Map(), calls = [], responses = [], viewed = [];
const node = () => ({ value: '', textContent: '', innerHTML: '', hidden: false, disabled: false, dataset: {}, listeners: new Map(),
  setAttribute() {}, addEventListener(type, fn) { const list=this.listeners.get(type)||[];list.push(fn);this.listeners.set(type,list); }, focus() {},
  dispatchEvent(event) { this.listeners.get(event.type)?.forEach(fn=>fn(event)); }, querySelector: () => null });
for (const id of ['ledgerPeriodSearchForm','ledgerPeriodSearchStart','ledgerPeriodSearchEnd','ledgerPeriodSearchKeyword','ledgerPeriodSearchMessage','ledgerPeriodSearchControls','ledgerPeriodSearchPage','ledgerPeriodSearchStatus','ledgerPeriodSearchPrev','ledgerPeriodSearchNext','ledgerSummarySearch','ledgerSearchForm','ledgerSearchClear']) nodes.set('#' + id, node());
nodes.set('.ledger-card .table-wrap', {scrollTop:200}); nodes.set('.ledger-card thead tr',node());
nodes.get('#ledgerSummarySearch').value = 'old keyword';
const month = Object.assign(node(), { value: '2026-10' }), transactionRows=node(), monthSummary=node();
const monthlyRows=[{id:900,tx_date:'2026-10-03',account_name:'TEST_ACCOUNT',category_name:'TEST_MONTH',summary:'MONTHLY_SNAPSHOT',amount:11,kind:'expense'}];
let desktop=true;
const context = vm.createContext({ URLSearchParams, Event, CustomEvent: class { constructor(type,options){this.type=type;this.detail=options?.detail;} }, Number, String, Map, Intl,
  state:{transactions:monthlyRows,accounts:[],archivedAccounts:[]},
  document: { createElement() { const dialog = node(); dialog.showModal = () => { dialog.open = true; }; dialog.close = () => { dialog.open = false; dialog.dispatchEvent({ type: 'close' }); }; return dialog; },
    body: {dataset:{},append(dialog) { nodes.set('#' + dialog.id, dialog); }}, querySelector: selector => nodes.get(selector) || null,
    querySelectorAll: () => [...nodes].filter(([id]) => /Search(Start|End|Keyword)$/.test(id)).map(([, value]) => value) },
  window: {cyIsDesktopInteractionWorkspace:()=>desktop,dispatchEvent(){}},els:{monthFilter:month,transactionRows,monthSummary},
  isLocked:()=>false,syncLedgerMonthDisplay(){},handleLedgerQuickLock(){},
  ledgerAccountVisual:()=>({slot:1,background:'#fff',foreground:'#000'}),
  revealSavedTransaction:(id,value)=>viewed.push({id,month:value}),
  async api(url) { calls.push(url); return await responses.shift()(); }
});
vm.runInContext("const cyLedgerPeriodSearch={criteria:null,pages:[],page:0,requestId:0,busy:false,active:false};let cyLedgerSearch='',cyLedgerGroupByAccount=false,cyLedgerDateDescending=false;let cyLedgerOpeningData={accounts:[{name:'TEST_ACCOUNT',amount:100}]};\n"+
 source.slice(source.indexOf('function setupLedgerPeriodSearch()'),source.indexOf('function syncLedgerMonthDisplay()'))+'\n'+
 source.slice(source.indexOf('function renderDesktopLedger()'),source.indexOf('const LEDGER_ACCOUNT_LIGHT_PALETTE'))+'\n'+
 source.slice(source.indexOf('function splitLedgerAccountName('),source.indexOf('let cyMarkerPicker'))+'\n'+
 source.slice(source.indexOf('function updateLedgerHeader()'),source.indexOf('function revealSavedTransaction('))+'\n'+
 source.slice(source.indexOf('function writeLedgerRows('))+'\n'+
 appSource.split('\n').filter(line=>/^function (escapeHtml|money)\(/.test(line)).join('\n'),context);
context.scrollToSavedTransaction=()=>{};
context.setupLedgerPeriodSearch();context.bindLedgerToolbar();
let initialMonthChanges=0;
for(const name of ['Start','End'])nodes.get('#ledgerPeriodSearch'+name).addEventListener('change',()=>{initialMonthChanges++;});
context.openLedgerPeriodSearch();assert.equal(initialMonthChanges,2);assert.equal(calls.length,0);
assert.equal(nodes.get('#ledgerPeriodSearchKeyword').value,'old keyword');
const field=(id,value)=>{nodes.get('#ledgerPeriodSearch'+id).value=value;};
field('Start','2023-10');field('End','2026-10');field('Keyword','needle');
await context.searchLedgerPeriod(true);assert.equal(calls.length,0);assert.match(nodes.get('#ledgerPeriodSearchMessage').textContent,/36/);
field('Start','2023-11');
const row={id:42,tx_date:'2026-02-02',account_name:'TEST_ARCHIVED',category_name:'TEST_CATEGORY',summary:'<img src=x onerror=alert(1)>',amount:10,kind:'expense',marker_color:'orange',marker_target:'summary'};
const queue=(transactions,nextCursor=null)=>responses.push(()=>({transactions,nextCursor}));
queue([row],'TEST_CURSOR');await context.searchLedgerPeriod(true);
assert.equal(nodes.get('#ledgerPeriodSearchDialog').open,false,'successful search closes the condition dialog');
assert.doesNotMatch(nodes.get('#ledgerPeriodSearchDialog').innerHTML,/ledgerPeriodSearchResults|<table/,'there is no popup result renderer');
assert.match(transactionRows.innerHTML,/data-transaction-id="42"/);assert.match(transactionRows.innerHTML,/&lt;img/);assert.doesNotMatch(transactionRows.innerHTML,/<img/);
assert.match(transactionRows.innerHTML,/data-marker-color="orange"/);assert.match(transactionRows.innerHTML,/data-ledger-search-view="42"/);
assert.doesNotMatch(transactionRows.innerHTML,/data-balance-popover-id|data-edit-id|data-delete-id/,'partial results cannot claim a computed balance or bypass the monthly edit snapshot');
assert.equal(context.window.cyLedgerBalanceBreakdowns.size,0);
assert.match(monthSummary.innerHTML,/全帳簿搜尋/);assert.doesNotMatch(monthSummary.innerHTML,/期初|期末/,'monthly totals are not presented as cross-period totals');
assert.equal(context.state.transactions,monthlyRows);assert.equal(month.value,'2026-10','query does not replace the underlying month');
queue([{...row,id:43}]);await context.searchLedgerPeriod(false);assert.match(calls[1],/cursor=TEST_CURSOR/);
nodes.get('#ledgerPeriodSearchPrev').dispatchEvent({type:'click'});await context.searchLedgerPeriod(false);assert.equal(calls.length,2,'cached page navigation adds no read');
const beforeClear=calls.length;nodes.get('#ledgerSearchClear').dispatchEvent({type:'click'});
assert.match(transactionRows.innerHTML,/MONTHLY_SNAPSHOT/);assert.doesNotMatch(transactionRows.innerHTML,/data-ledger-search-view/);
assert.match(transactionRows.innerHTML,/>\$89</,'clear restores the full monthly chronological balance');
assert.equal(nodes.get('#ledgerPeriodSearchControls').hidden,true);assert.equal(nodes.get('#ledgerSummarySearch').value,'');assert.equal(calls.length,beforeClear);
// Empty searches are shown in the central list and remain clearable.
queue([]);await context.searchLedgerPeriod(true,{start:'2024-01',end:'2026-12',q:'none'});
assert.match(transactionRows.innerHTML,/沒有符合/);nodes.get('#ledgerSearchClear').dispatchEvent({type:'click'});
// Cancelling or clearing an outstanding search rejects its late response.
context.openLedgerPeriodSearch();field('Keyword','needle');let complete;
responses.push(()=>new Promise(resolve=>{complete=resolve;}));
let pending=context.searchLedgerPeriod(true);nodes.get('#ledgerPeriodSearchDialog').close();complete({transactions:[row],nextCursor:null});await pending;
assert.match(transactionRows.innerHTML,/MONTHLY_SNAPSHOT/);assert.equal(vm.runInContext('cyLedgerPeriodSearch.busy',context),false);
queue([row],'TEST_CURSOR');await context.searchLedgerPeriod(true,{start:'2024-01',end:'2026-12',q:'needle'});
responses.push(()=>new Promise(resolve=>{complete=resolve;}));pending=context.searchLedgerPeriod(false);nodes.get('#ledgerSearchClear').dispatchEvent({type:'click'});
complete({transactions:[{...row,id:43}],nextCursor:null});await pending;assert.match(transactionRows.innerHTML,/MONTHLY_SNAPSHOT/);
queue([row]);await context.searchLedgerPeriod(true,{start:'2024-01',end:'2026-12',q:'needle'});
// Failed pagination preserves the accepted query and rows.
vm.runInContext("cyLedgerPeriodSearch.pages[0].nextCursor='TEST_CURSOR'",context);
responses.push(()=>{throw new Error('TEST_SEARCH_FAILED');});await context.searchLedgerPeriod(false);
assert.match(transactionRows.innerHTML,/data-transaction-id="42"/);assert.equal(nodes.get('#ledgerPeriodSearchStatus').textContent,'TEST_SEARCH_FAILED');
// Viewing uses the existing month loader and locator, without publishing filtered rows to state.
transactionRows.dispatchEvent({type:'click',target:{closest:()=>({dataset:{ledgerSearchView:'42'}})}});
assert.equal(month.value,'2026-02');assert.deepEqual(viewed,[{id:42,month:'2026-02'}]);assert.equal(vm.runInContext('cyLedgerPeriodSearch.active',context),false);
// Closing an edited condition form keeps accepted results; the central keyword bar reuses the accepted period.
queue([row]);await context.searchLedgerPeriod(true,{start:'2024-01',end:'2026-12',q:'needle'});
context.openLedgerPeriodSearch();field('Start','2025-01');nodes.get('#ledgerPeriodSearchForm').dispatchEvent({type:'change'});
nodes.get('#ledgerPeriodSearchDialog').close();assert.equal(vm.runInContext('cyLedgerPeriodSearch.active',context),true);
assert.match(transactionRows.innerHTML,/data-transaction-id="42"/);
nodes.get('#ledgerSummarySearch').value='revised';queue([row]);
nodes.get('#ledgerSearchForm').dispatchEvent({type:'submit',preventDefault(){}});
for(let i=0;i<8;i++)await Promise.resolve();
assert.equal(new URL(calls.at(-1),'https://test.invalid').searchParams.get('start'),'2024-01');
assert.equal(new URL(calls.at(-1),'https://test.invalid').searchParams.get('q'),'revised');
// Changing month or returning to touch exits the desktop query presentation.
queue([row]);await context.searchLedgerPeriod(true,{start:'2024-01',end:'2026-12',q:'needle'});
month.value='2026-09';month.dispatchEvent({type:'change'});assert.equal(vm.runInContext('cyLedgerPeriodSearch.active',context),false);
queue([row]);await context.searchLedgerPeriod(true,{start:'2024-01',end:'2026-12',q:'needle'});desktop=false;context.renderDesktopLedger();
assert.equal(vm.runInContext('cyLedgerPeriodSearch.active',context),false);assert.match(transactionRows.innerHTML,/MONTHLY_SNAPSHOT/);
console.log('Ledger search: indexed 36-month API and shared auth; central rows, clear/month restore, popup removal, marker display, truthful balances, paging/errors/cancellation, view and touch exit passed.');
