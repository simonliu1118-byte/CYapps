import assert from 'node:assert/strict';
import fs from 'node:fs';
import vm from 'node:vm';

const read = name => fs.readFileSync(new URL('../public/' + name, import.meta.url), 'utf8');
const app = read('app.js');
const adaptive = read('adaptive-ui.js');
const inline = read('ledger-inline-edit.js');
const piece = (text, from, to) => text.slice(text.indexOf(from), text.indexOf(to));
const field = value => ({ value, disabled: false, focus() {} });

for (const width of [375, 820, 1440]) {
  const writes = [];
  let lockedMonth = '';
  const tx = { id: 1, tx_date: '2026-08-01', account_name: '現金', category_name: '一般收入', kind: 'income', summary: '原摘要', amount: 1 };
  const fields = {
    '[data-inline-date]': field('2026-09-02'), '[data-inline-account]': field('現金'),
    '[data-inline-category]': field('一般收入'), '[data-inline-summary]': field('  對帳  '),
    '[data-inline-amount]': field('777'), '[data-inline-save]': field(''), '[data-inline-cancel]': field(''), '[data-inline-message]': {}
  };
  const row = { isConnected: true, querySelector: selector => fields[selector] };
  const els = {
    editDate: field('2026-09-02'), editAccount: field('現金'), editCategory: field('一般收入'), editSummary: field('  對帳  '), editAmount: field('777'),
    txDate: field('2026-09-02'), accountName: field('現金'), categoryName: field('一般收入'), summary: field('  對帳  '), amount: field('777'),
    editSaveButton: field(''), saveButton: field(''), editMessage: {}, settingsMessage: {}, editDialog: { close() {} }, monthFilter: field('2026-09')
  };
  const nameInput = field('新分類');
  const dialogFields = { '#settingsNameInput': nameInput, '#settingsGroupSelect': field('2'), '#settingsManagerSave': field(''), '#settingsManagerMessage': {} };
  const dialog = { querySelector: selector => dialogFields[selector], close() {} };
  let fail = false;
  let release;
  let hold = false;
  const context = vm.createContext({
    state: { transactions: [tx], editingTx: tx, settingsKind: 'income', groups: [{ id: 2, kind: 'income', name: '門市' }], categories: [] }, els,
    window: { innerWidth: width, matchMedia: () => ({ matches: width < 768 }) },
    document: { querySelector: selector => selector === '#settingsManagerDialog' ? dialog : null },
    isLocked: month => month === lockedMonth && Boolean(lockedMonth), jsonHeaders: () => ({ 'content-type': 'application/json' }),
    async api(path, options) { writes.push({ path, method: options.method, body: JSON.parse(options.body) }); if (hold) await new Promise(resolve => { release = resolve; }); if (fail) throw new Error('write failed'); return { id: 99 }; },
    setDialogMessage() {}, showMessage() {}, setInlineEditMessage() {}, async loadTransactions() {},
    resumeLedgerRefreshObserver() {}, cancelInlineLedgerEdit() {}, cancelV0215Build4MobileEdit() {}, switchV0215Build4MobilePage() {}, restoreV0215Build4LedgerContext() {},
    showV0215Build4LedgerNotice() {}, updateEntryLockState() {}, renderSettingsCategoryManager() {}, renderCategories() {},
    renderSettingsAccountManager() {}, renderTransactions() {}, renderAccounts() {}, renderSettings() {}
  });
  vm.runInContext('let cyTransactionMutationRevision = 0; const cyPendingTransactionUpdates = new Map();\n' + piece(app, 'function summaryCharacterUnits(', 'async function deleteTransaction('), context);
  vm.runInContext('let cyV0215Build4Edit = { id: 1, returnContext: { month: "2026-09", search: "" } };\n' + piece(adaptive, 'async function saveV0215Build4MobileEdit(', 'function cancelV0215Build4MobileEdit('), context);
  vm.runInContext(piece(adaptive, 'function isTabletWorkspace()', 'function tabletWorkspaceOrientation()'), context);
  let intercepted = false;
  context.beginInlineLedgerEdit = () => {};
  vm.runInContext(piece(inline, 'function handleInlineLedgerClick(', 'function beginInlineLedgerEdit('), context);
  context.handleInlineLedgerClick({ target: { closest: selector => selector === '[data-edit-id]' ? { dataset: { editId: '1' }, closest: () => row } : null }, preventDefault() { intercepted = true; }, stopImmediatePropagation() {} });
  assert.equal(intercepted, width >= 1024, 'desktop inline handlers never intercept the shared phone/tablet entry editor');
  context.inlineRow = row;
  vm.runInContext('let cyInlineLedgerEdit = { id: 1, row: inlineRow };\n' + piece(inline, 'async function saveInlineLedgerEdit(', 'function cancelInlineLedgerEdit('), context);
  await context.saveTransactionEdit({ preventDefault() {} });
  await context.saveV0215Build4MobileEdit();
  await context.saveInlineLedgerEdit();
  assert.equal(writes.length, 3);
  for (const write of writes) assert.deepEqual(write, { path: '/api/transactions/1', method: 'PUT', body: { txDate: '2026-09-02', accountName: '現金', categoryName: '一般收入', summary: '對帳', amount: 777 } });
  writes.length = 0;
  context.state.transactions = [tx];
  lockedMonth = '2026-08';
  await context.saveTransactionEdit({ preventDefault() {} });
  await context.saveV0215Build4MobileEdit();
  vm.runInContext('cyInlineLedgerEdit = { id: 1, row: inlineRow };', context);
  await context.saveInlineLedgerEdit();
  assert.equal(writes.length, 0, `${width}: all edit interfaces reject a locked source month`);
  lockedMonth = '';
  assert.equal(context.summaryWeightedUnits('中'.repeat(20)), 40);
  assert.equal(context.summaryWeightedUnits('A'.repeat(40)), 40);
  await assert.rejects(context.persistTransactionUpdate(1, { txDate: '2026-09-02', summary: '中'.repeat(21), amount: 1 }), /摘要/);
  await assert.rejects(context.persistTransactionUpdate(1, { txDate: '2026-09-02', summary: 'A'.repeat(41), amount: 1 }), /摘要/);
  hold = true;
  context.state.transactions = [tx];
  const optimisticEdit = context.persistTransactionUpdate(1, { txDate: '2026-09-02', accountName: '現金', categoryName: '一般收入', summary: '即時修改', amount: 8 });
  assert.equal(context.state.transactions[0].amount, 8, `${width}: transaction update is visible before the API completes`);
  const overlaid = context.mergePendingTransactionUpdates([tx], '2026-09');
  assert.equal(overlaid[0].amount, 8, 'a pending write survives a stale month response');
  assert.equal(context.mergePendingTransactionUpdates([tx], '2026-08').length, 0, 'moving months removes the old-month row optimistically');
  await assert.rejects(context.persistTransactionUpdate(1, { txDate: '2026-09-02', amount: 9 }), /儲存中/);
  fail = true;
  release(); await assert.rejects(optimisticEdit, /write failed/);
  assert.equal(context.state.transactions.length, 0, 'failure removes the row from the destination month');
  context.els.monthFilter.value = '2026-08';
  context.state.transactions = [tx];
  const rollbackEdit = context.persistTransactionUpdate(1, { txDate: '2026-08-02', accountName: '現金', categoryName: '一般收入', summary: '即時修改', amount: 8 });
  context.state.transactions.push({ id: 2, tx_date: '2026-08-03', amount: 6 });
  release(); await assert.rejects(rollbackEdit, /write failed/);
  assert.equal(context.state.transactions.find(row => row.id === 1).amount, 1);
  assert.equal(context.state.transactions.find(row => row.id === 2).amount, 6, 'rollback preserves unrelated concurrent changes');
  fail = false; hold = false;
  context.window.cyaccRefreshLedgerView = async () => { throw new Error('refresh failed'); };
  await context.persistTransactionUpdate(1, { txDate: '2026-09-02', accountName: '現金', categoryName: '一般收入', summary: '即時修改', amount: 8 });
  assert.equal(context.state.transactions.some(row => row.id === 1), false, 'post-commit read failure never rolls back a successful cross-month write');
  delete context.window.cyaccRefreshLedgerView;
  context.els.monthFilter.value = '2026-09';
  vm.runInContext('const cyPendingSettingsMutations = new Set(); let cySettingsTempId = -1;\n' + piece(app, 'function renderSettingsMutationState(', 'function openingAccountRowHtml('), context);
  context.state.accounts = [{ id: 1, name: '現金', is_default: 1 }, { id: 2, name: '銀行', is_default: 0 }];
  hold = true;
  const rename = context.mutateSettings('/api/accounts/1', { method: 'PUT', body: JSON.stringify({ name: '現金二' }) }, '已更新');
  assert.equal(context.state.accounts[0].name, '現金二');
  fail = true; release(); await rename;
  assert.equal(context.state.accounts[0].name, '現金');
  fail = false;
  const defaultChange = context.mutateSettings('/api/accounts/2/default', { method: 'POST' }, '已更新');
  assert.deepEqual(Array.from(context.state.accounts, account => account.is_default), [0, 1]);
  release(); await defaultChange;
  const accountAdd = context.mutateSettings('/api/accounts', { method: 'POST', body: JSON.stringify({ name: '新帳戶' }) }, '已新增');
  assert.ok(context.state.accounts.some(account => account.name === '新帳戶' && account.id < 0));
  release(); await accountAdd;
  assert.equal(context.state.accounts.find(account => account.name === '新帳戶').id, 99);

  vm.runInContext('let settingsManagerSaving = false;\n' + piece(adaptive, 'async function persistSettingsOrder(', 'function settingsCategoryPayload(') + piece(adaptive, 'function restoreSettingsOrderState(', 'async function moveSettingsGroup(') + piece(adaptive, 'async function applySettingsAccountOrder(', 'function finishMobileAccountDrag('), context);
  context.state.accounts = [{ id: 1, name: '現金' }, { id: 2, name: '銀行' }];
  const previousAccounts = context.state.accounts;
  hold = true;
  const orderWrite = context.applySettingsAccountOrder(previousAccounts, [2, 1]);
  assert.deepEqual(Array.from(context.state.accounts, account => account.id), [2, 1], `${width}: account order changes immediately`);
  const count = writes.length;
  await context.applySettingsAccountOrder(previousAccounts, [1, 2]);
  assert.equal(writes.length, count, 'pending order prevents concurrent writes');
  release(); await orderWrite;
  fail = true;
  const orderFailure = context.applySettingsAccountOrder(context.state.accounts, [1, 2]);
  context.state.accounts.find(account => account.id === 1).name = '新名稱';
  release(); await orderFailure;
  assert.deepEqual(Array.from(context.state.accounts, account => account.id), [2, 1], 'failed shared order writer restores prior order');
  assert.equal(context.state.accounts.find(account => account.id === 1).name, '新名稱', 'order rollback preserves a concurrent rename');
  fail = false;

  vm.runInContext('let settingsTempId = -1; let settingsManagerDialogState = { mode: "add-group" };\n' + piece(adaptive, 'function nextSettingsTempId(', 'function setupMobileLockMonthControls(') + piece(adaptive, 'async function saveSettingsManagerDialog(', 'function setupSettingsManagerDragAndDrop('), context);
  hold = true;
  const pending = context.saveSettingsManagerDialog({ preventDefault() {} });
  assert.ok(context.state.groups.some(group => group.name === '新分類' && group.id < 0), `${width}: category group is optimistic on every breakpoint`);
  release(); await pending;
  assert.equal(context.state.groups.find(group => group.name === '新分類').id, 99);
  vm.runInContext('settingsManagerDialogState = { mode: "add-category" };', context);
  nameInput.value = '新科目';
  fail = true;
  const rejected = context.saveSettingsManagerDialog({ preventDefault() {} });
  assert.ok(context.state.categories.some(category => category.name === '新科目'), `${width}: optimistic category appears immediately`);
  release(); await rejected;
  assert.equal(context.state.categories.length, 0, 'failed category addition rolls back at every breakpoint');
}

// Exercise actual month reads that finish after a write has left the pending map.
for (const scenario of ['read-before-write', 'read-during-write', 'failed-write']) {
  const original = { id: 1, tx_date: '2026-09-01', account_name: '現金', category_name: '一般收入', kind: 'income', summary: '原值', amount: 1 };
  let stored = { ...original };
  let finishRead;
  let finishWrite;
  let reads = 0;
  const context = vm.createContext({
    state: { transactions: [original] }, els: { monthFilter: field('2026-09'), transactionRows: {} }, window: {},
    setLedgerLoadingState() {}, renderTransactions() {}, updateEntryLockState() {}, isLocked: () => false,
    jsonHeaders: () => ({}), escapeHtml: value => value,
    async api(path, options) {
      if (options?.method === 'PUT') {
        await new Promise(resolve => { finishWrite = resolve; });
        if (scenario === 'failed-write') throw new Error('write failed');
        stored = { ...original, amount: JSON.parse(options.body).amount };
        return {};
      }
      reads += 1;
      if (reads === 1) return new Promise(resolve => { finishRead = () => resolve({ transactions: [original] }); });
      return { transactions: [stored] };
    }
  });
  vm.runInContext('let cyTransactionRequestId = 0; let cyTransactionMutationRevision = 0; const cyPendingTransactionUpdates = new Map();\n' +
    piece(app, 'async function loadTransactions(', 'function setLedgerLoadingState(') +
    piece(app, 'function summaryCharacterUnits(', 'async function saveTransactionEdit('), context);
  let read;
  if (scenario !== 'read-during-write') read = context.loadTransactions();
  const write = context.persistTransactionUpdate(1, { txDate: original.tx_date, accountName: original.account_name, categoryName: original.category_name, summary: original.summary, amount: 8 });
  if (!read) read = context.loadTransactions();
  finishWrite();
  if (scenario === 'failed-write') await assert.rejects(write, /write failed/);
  else await write;
  finishRead();
  await read;
  assert.equal(reads, 2, `${scenario}: a stale response is replaced by a fresh canonical read`);
  assert.equal(context.state.transactions[0].amount, scenario === 'failed-write' ? 1 : 8, `${scenario}: delayed reads cannot overwrite the completed write or rollback`);
}
console.log('Production edit writers, locks, summary rules and optimistic settings are shared at mobile/tablet/desktop widths.');
