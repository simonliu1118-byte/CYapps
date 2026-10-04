import assert from 'node:assert/strict';
import fs from 'node:fs';
import vm from 'node:vm';
import { DatabaseSync } from 'node:sqlite';
import { handleAccountingToolsApi } from '../src/accounting-tools.js';

// Execute the existing canonical frontend mutation and pointer handlers.
const source = fs.readFileSync(new URL('../public/adaptive-ui.js', import.meta.url), 'utf8');
const groups = [
  { id: 1, kind: 'income', name: '門市' },
  { id: 2, kind: 'income', name: '網路' },
  { id: 3, kind: 'expense', name: '成本' }
];
const categories = [
  { id: 11, kind: 'income', group_id: 1, group_name: '門市', name: '門市收入' },
  { id: 12, kind: 'income', group_id: 1, group_name: '門市', name: '外送收入' },
  { id: 21, kind: 'income', group_id: 2, group_name: '網路', name: '網路收入' },
  { id: 31, kind: 'expense', group_id: 3, group_name: '成本', name: '材料' }
];
const calls = [];
let failure = false;
let releaseWrite;
let blockWrite = false;
let hit = null;
let frame;
let captured = null;
let renders = 0;
const classes = () => ({ add() {}, remove() {} });
const host = {
  scrollTop: 0,
  setAttribute() {}, removeAttribute() {},
  setPointerCapture(id) { captured = id; },
  hasPointerCapture(id) { return captured === id; },
  releasePointerCapture() { captured = null; },
  contains(target) { return target === targetRow || target === targetGroup; },
  getBoundingClientRect() { return { top: 100, bottom: 400, left: 0, right: 320 }; }
};
const targetGroup = { dataset: { groupId: '2' }, classList: classes(), closest(selector) { return selector === '[data-group-id]' ? this : null; } };
const targetRow = {
  dataset: { settingsCategoryRow: '21' }, classList: classes(),
  closest(selector) { return selector === '[data-settings-category-row]' ? this : selector === '[data-group-id]' ? targetGroup : null; },
  getBoundingClientRect() { return { top: 300, height: 34 }; }
};
const sourceRow = { classList: classes() };
const handle = { dataset: { settingsDragCategory: '11' }, closest() { return sourceRow; } };
const startTarget = { closest() { return handle; } };
const context = vm.createContext({
  state: { settingsKind: 'income', groups: structuredClone(groups), categories: structuredClone(categories) },
  els: { settingsMessage: {} }, SETTINGS_MANAGER_DESKTOP: '(min-width: 1024px)',
  window: { matchMedia: () => ({ matches: false }) },
  document: {
    querySelector: () => host, querySelectorAll: () => [], elementFromPoint: () => hit
  },
  requestAnimationFrame(callback) { frame = callback; return 1; },
  cancelAnimationFrame() { frame = null; },
  jsonHeaders: () => ({}), setDialogMessage() {},
  renderSettingsCategoryManager() { renders++; },
  renderSettingsAccountManager() {},
  async api(path, request) {
    calls.push({ path, body: JSON.parse(request.body) });
    if (blockWrite) await new Promise(resolve => { releaseWrite = resolve; });
    if (failure) throw new Error('write failed');
    return { ok: true };
  }
});
vm.runInContext('let settingsManagerDrag = null; let settingsManagerPointer = null; let settingsManagerSaving = false;\n' + source.slice(source.indexOf('function settingsCategoryGroupHtml('), source.indexOf('const CY_LEDGER_BALANCE_HOVER')), context);
const ids = () => Array.from(context.state.groups, group => group.id);
const byGroup = () => context.state.categories.map(item => [item.id, item.group_id, item.group_name]);
const reset = () => {
  context.state.groups = structuredClone(groups);
  context.state.categories = structuredClone(categories);
  calls.length = 0; failure = false; blockWrite = false;
};
const event = (extra = {}) => ({ pointerType: 'touch', isPrimary: true, pointerId: 9, clientX: 180, clientY: 330, currentTarget: host, target: startTarget, preventDefault() {}, ...extra });

assert.match(context.settingsGroupOrderButtons(1, 'income'), /data-direction="up"[^>]* disabled/);
assert.match(context.settingsGroupOrderButtons(2, 'income'), /data-direction="down"[^>]* disabled/);
await context.moveSettingsGroup(1, 'up');
assert.equal(calls.length, 0, 'first group must not move upward');
await context.moveSettingsGroup(1, 'down');
assert.deepEqual(ids(), [2, 1, 3]);
assert.deepEqual(calls[0], { path: '/api/category-groups/reorder', body: { kind: 'income', ids: [2, 1] } });
failure = true;
await context.moveSettingsGroup(1, 'up');
assert.deepEqual(ids(), [2, 1, 3], 'failed group reorder must restore the previous order');
reset(); blockWrite = true;
const firstWrite = context.moveSettingsGroup(1, 'down');
await context.moveSettingsGroup(2, 'down');
assert.equal(calls.length, 1, 'an outstanding write must prevent competing reorder');
releaseWrite(); await firstWrite; reset();

const reordered = context.settingsCategoryPayload(context.state.categories, 'income', 12, 1, 11, false);
assert.deepEqual(JSON.parse(JSON.stringify(reordered)), [ { groupId: 1, categoryIds: [12, 11] }, { groupId: 2, categoryIds: [21] } ]);
assert.equal(context.settingsCategoryPayload(context.state.categories, 'income', 11, 3, 31, true), null, 'cannot cross income/expense');
const emptyTarget = context.settingsCategoryPayload(context.state.categories.filter(item => item.id !== 21), 'income', 11, 2, 0, true);
assert.deepEqual(JSON.parse(JSON.stringify(emptyTarget)), [ { groupId: 1, categoryIds: [12] }, { groupId: 2, categoryIds: [11] } ], 'empty groups accept a dropped category');
context.state.categories.push({ id: -1, kind: 'income', group_id: 1 });
assert.equal(context.settingsCategoryPayload(context.state.categories, 'income', 11, 2, 21, true), null, 'pending category creates cannot enter a reorder payload');
context.state.categories.pop();
assert.equal(context.settingsCategoryPayload(context.state.categories, 'income', 11, 1, 11, true), null, 'dropping on itself is a no-op');

let nativePayload;
context.handleSettingsManagerDragStart({ target: startTarget, preventDefault() {}, dataTransfer: { setData(type, value) { nativePayload = value; } } });
assert.equal(nativePayload, 'category:11', 'native category drag remains available outside desktop');
context.finishSettingsManagerDrag();
context.startSettingsCategoryPointerDrag(event({ pointerType: 'mouse' }));
assert.equal(captured, null, 'mouse uses the existing native drag path');
context.startSettingsCategoryPointerDrag(event());
assert.equal(captured, 9);
hit = targetRow;
context.moveSettingsCategoryPointerDrag(event({ clientY: 395 }));
frame();
assert.ok(host.scrollTop > 0, 'touch drag near bottom must scroll the list');
context.cancelSettingsCategoryPointerDrag(event());
assert.equal(captured, null); assert.equal(frame, null); assert.equal(calls.length, 0);
context.startSettingsCategoryPointerDrag(event());
hit = null;
context.endSettingsCategoryPointerDrag(event());
assert.equal(calls.length, 0, 'release outside list must not write');
context.startSettingsCategoryPointerDrag(event());
hit = targetRow;
context.endSettingsCategoryPointerDrag(event());
await new Promise(resolve => setImmediate(resolve));
assert.equal(captured, null); assert.equal(frame, null);
assert.deepEqual(calls[0], { path: '/api/categories/reorder', body: { kind: 'income', groups: [ { groupId: 1, categoryIds: [12] }, { groupId: 2, categoryIds: [21, 11] } ] } });
assert.deepEqual(byGroup().find(item => item[0] === 11), [11, 2, '網路']);
assert.deepEqual(byGroup().find(item => item[0] === 31), [31, 3, '成本']);
reset(); failure = true;
context.startSettingsCategoryPointerDrag(event());
context.endSettingsCategoryPointerDrag(event());
await new Promise(resolve => setImmediate(resolve));
assert.deepEqual(JSON.parse(JSON.stringify(context.state.categories)), categories, 'failed drop restores all membership and order');
assert.ok(renders > 0);

// Execute the payload against real SQL; historical ledger strings stay intact.
const sql = new DatabaseSync(':memory:');
sql.exec(`CREATE TABLE category_groups (id INTEGER PRIMARY KEY, kind TEXT, sort_order INTEGER);
CREATE TABLE categories (id INTEGER PRIMARY KEY, kind TEXT, group_id INTEGER, sort_order INTEGER);
CREATE TABLE transactions (id INTEGER PRIMARY KEY, category_name TEXT, amount INTEGER);
INSERT INTO category_groups VALUES (1,'income',0),(2,'income',1),(3,'expense',0);
INSERT INTO categories VALUES (11,'income',1,0),(12,'income',1,1),(21,'income',2,0),(31,'expense',3,0);
INSERT INTO transactions VALUES (1,'門市收入',500);`);
const db = {
  prepare(query) {
    const statement = sql.prepare(query); let args = [];
    return { bind(...values) { args = values; return this; }, async all() { return { results: statement.all(...args) }; }, async run() { return statement.run(...args); } };
  },
  async batch(statements) {
    sql.exec('BEGIN');
    try { const results = []; for (const statement of statements) results.push(await statement.run()); sql.exec('COMMIT'); return results; }
    catch (error) { sql.exec('ROLLBACK'); throw error; }
  }
};
async function put(path, body) {
  return handleAccountingToolsApi(new Request('https://test.invalid' + path, { method: 'PUT', headers: { 'content-type': 'application/json' }, body: JSON.stringify(body) }), { DB: db });
}
assert.equal((await put('/api/categories/reorder', { kind: 'income', groups: [ { groupId: 1, categoryIds: [12] }, { groupId: 2, categoryIds: [21, 11] } ] })).status, 200);
assert.deepEqual({ ...sql.prepare('SELECT group_id, sort_order FROM categories WHERE id=11').get() }, { group_id: 2, sort_order: 1 });
assert.equal(sql.prepare('SELECT category_name FROM transactions').get().category_name, '門市收入');
assert.equal((await put('/api/categories/reorder', { kind: 'income', groups: [ { groupId: 1, categoryIds: [12] }, { groupId: 3, categoryIds: [21, 11] } ] })).status, 409);
assert.equal((await put('/api/categories/reorder', { kind: 'income', groups: [ { groupId: 1, categoryIds: [12, 11] }, { groupId: 2, categoryIds: [21, 11] } ] })).status, 400);
assert.equal((await put('/api/category-groups/reorder', { kind: 'income', ids: [2, 1] })).status, 200);
assert.deepEqual(sql.prepare("SELECT id FROM category_groups WHERE kind='income' ORDER BY sort_order").all().map(row => row.id), [2, 1]);
sql.close();
console.log('Category ordering, touch drag, cancellation, auto-scroll, rollback and real SQL membership tests passed.');
