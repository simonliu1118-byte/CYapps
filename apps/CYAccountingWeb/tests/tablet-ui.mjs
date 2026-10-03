import assert from 'node:assert/strict';
import fs from 'node:fs';
import vm from 'node:vm';
const read = name => fs.readFileSync(new URL('../public/' + name, import.meta.url), 'utf8');
const source = read('adaptive-ui.js');
const tabletCode = source.slice(source.indexOf('function isTabletWorkspace()'));
const listeners = new Map();
let coarse = true;
let viewportHeight = 1100;
const attrs = new Map();
const button = { textContent: '', setAttribute: (name, value) => attrs.set(name, value), addEventListener(name, fn) { this[name] = fn; } };
const pin = { checked: false, addEventListener(name, fn) { this[name] = fn; } };
const controls = { querySelector: selector => selector === '#tabletEntryToggle' ? button : pin };
const quickHost = { append(group) { group.parentElement = this; } };
const entryGrid = { append(group) { group.parentElement = this; } };
const favoriteGroup = { parentElement: quickHost };
const summaryGroup = { parentElement: quickHost };
const rail = { dataset: {} };
const shell = { dataset: {} };
const classes = { remove() {}, contains: () => true };
const entry = { prepend(node) { assert.equal(node, controls); }, classList: classes };
const ledger = { classList: classes };
const style = new Map();
const root = { dataset: {}, style: { setProperty: (key, value) => style.set(key, value) } };
let writes = 0;
const draft = { amount: '123', summary: '尚未儲存' };
let edit = { id: 9, draft };
let splitCalls = 0;
const nodes = { '.quick-entry-tools': quickHost, '.entry-grid': entryGrid, '#favoriteCategoryGroup': favoriteGroup, '#summarySuggestionGroup': summaryGroup, 'main.shell': shell, '.entry-card': entry, '.ledger-card': ledger, '.cy-entry-rail': rail, '#tabletEntryToggle': button,
  '.topbar': { getBoundingClientRect: () => ({ height: 60 }) }, '#readOnlyNotice': { classList: classes, getBoundingClientRect: () => ({ height: 30 }) } };
const win = {
  innerWidth: 820, innerHeight: 1100, screen: { orientation: { type: 'portrait-primary', addEventListener() {} } },
  visualViewport: { get height() { return viewportHeight; }, addEventListener(name, fn) { listeners.set('visual:' + name, fn); } },
  matchMedia(query) { return { matches: query.includes('coarse') ? coarse : query.includes('767') ? win.innerWidth < 768 : win.innerWidth >= 1360, addEventListener() {} }; },
  addEventListener(name, fn) { listeners.set(name, fn); }
};
const context = vm.createContext({ window: win, document: { documentElement: root, createElement: () => controls,
  querySelector: selector => nodes[selector] || null, querySelectorAll: () => [] },
  CY_V21_SPLIT_MEDIA: '(min-width: 1360px)', cyTouchWorkspaceEdit: edit,
  applyV21DesktopSplitWorkspace() { splitCalls++; }, setupTouchWorkspaceEntrySecondaryAction() {}, renderSettingsAccountManager() {},
  cancelTouchWorkspaceMobileEdit() { context.cyTouchWorkspaceEdit = null; }, api() { writes++; }
});
vm.runInContext(tabletCode, context);
for (const [width, touch, expected] of [[375,true,false],[767,true,false],[768,false,true],[820,true,true],[1024,true,true],[1194,true,true],[1366,true,true],[1440,true,false],[1280,false,false],[1440,false,false]]) {
  win.innerWidth = width; coarse = touch;
  assert.equal(context.isTabletWorkspace(), expected, `tablet classification ${width}/${touch}`);
}
win.innerWidth = 820; coarse = true;
context.setupTabletWorkspace();
assert.equal(root.dataset.tabletLayout, 'portrait');
assert.equal(favoriteGroup.parentElement, entryGrid);
assert.equal(summaryGroup.parentElement, entryGrid);
assert.equal(rail.dataset.entryExpanded, 'false');
button.click();
assert.equal(rail.dataset.entryExpanded, 'true');
pin.checked = true; pin.change({ target: pin }); button.click();
assert.equal(rail.dataset.entryExpanded, 'true', 'pinned entry stays open');
pin.checked = false; button.click();
assert.equal(rail.dataset.entryExpanded, 'false');
// Handle drags must act once, ignore jitter and cancellation, and preserve the draft.
button.pointerdown({ button: 0, pointerId: 1, clientY: 150 });
button.pointerup({ pointerId: 1, clientY: 100 }); button.click();
assert.equal(rail.dataset.entryExpanded, 'true', 'upward drag opens without synthetic click closing it');
button.pointerdown({ button: 0, pointerId: 2, clientY: 100 });
button.pointerup({ pointerId: 2, clientY: 160 }); button.click();
assert.equal(rail.dataset.entryExpanded, 'false', 'downward drag closes exactly once');
button.pointerdown({ button: 0, pointerId: 3, clientY: 100 });
button.pointercancel(); button.pointerup({ pointerId: 3, clientY: 30 });
assert.equal(rail.dataset.entryExpanded, 'false', 'cancelled drag does not toggle');
context.setTabletEntryExpanded(true);
const currentEdit = context.cyTouchWorkspaceEdit;
win.innerWidth = 1194; win.screen.orientation.type = 'landscape-primary'; listeners.get('resize')();
assert.equal(root.dataset.tabletLayout, 'landscape');
assert.equal(context.cyTouchWorkspaceEdit, currentEdit, 'rotation preserves the existing edit owner');
assert.equal(currentEdit.draft, draft, 'rotation preserves the same draft');
win.innerWidth = 820; win.screen.orientation.type = 'portrait-primary'; listeners.get('resize')();
viewportHeight = 420; listeners.get('visual:resize')();
assert.equal(root.dataset.tabletLayout, 'portrait', 'software keyboard does not change orientation');
assert.equal(style.get('--tablet-visible-height'), '420px');
assert.equal(context.cyTouchWorkspaceEdit, currentEdit);
assert.equal(writes, 0, 'layout/rotation/keyboard/pinning never writes accounting data');
win.innerWidth = 375; listeners.get('resize')();
assert.equal(favoriteGroup.parentElement, quickHost, 'phone restores existing quick-entry container');
assert.equal(summaryGroup.parentElement, quickHost);
win.innerWidth = 820; listeners.get('resize')();
assert.equal(favoriteGroup.parentElement, entryGrid);
const count = splitCalls; context.setupTabletWorkspace(); assert.equal(splitCalls, count, 'setup binds once');
const css = read('adaptive-ui.css');
assert.match(css, /data-tablet-layout="portrait"[\s\S]*?grid-template-rows: minmax\(0, 1fr\) auto/);
assert.match(css, /\.ledger-card \.table-wrap \{[^}]*overflow: auto/);
assert.match(css, /body\[data-cyacc-read-only="true"\] \.cy-entry-rail \{ display: none/);
assert.match(css, /input\.cy-native-date-source,[\s\S]*?pointer-events: auto !important/);
assert.match(read('ledger-inline-edit.js'), /window\.cyUsesEntryTransactionEditor\?\.\(\)/);
assert.match(css.slice(css.indexOf('/* Tablet workspace:')), /grid-template-columns: minmax\(0, 1fr\) !important/, 'tablet overrides important desktop entry columns');
assert.match(css, /data-tablet-layout="landscape"\] \.shell\.cy-split-layout \{ grid-template-columns: minmax\(280px, 31%\)/, 'landscape entry rail is narrower only in landscape');
assert.match(css, /data-tablet-layout="landscape"\] \.tablet-entry-controls,[\s\S]*?confirmation-edge-open \{ display: none !important/, 'landscape hides both non-landscape handles');
assert.match(css, /data-tablet-layout="landscape"\] #txDate\.cy-native-date-source[\s\S]*?-webkit-appearance:auto !important/, 'landscape date stays native');
assert.match(css, /data-tablet-layout="landscape"\] #accountName,[\s\S]*?#categoryName \{ text-align:center !important; text-align-last:center !important/, 'landscape account and category are centered');
assert.match(css, /data-tablet-layout="landscape"\] \.entry-grid > #favoriteCategoryGroup\.quick-tool-row\.hidden,[\s\S]*?display:grid !important/, 'landscape favorites remain visible when empty');
assert.match(css, /data-tablet-layout="landscape"\] \.ledger-search-submit \{ display:none !important/, 'landscape search submits by Enter like mobile');
assert.match(css, /data-tablet-layout="landscape"\] #monthSummary \.ledger-summary-item:not\(\.opening\) \{ border-left:1px solid/, 'landscape summary uses one separated row');
assert.match(css, /data-tablet-layout="landscape"\] \.ledger-card td\.action-col \[data-edit-id\]::before/, 'landscape edit is icon-only');
assert.match(css, /data-tablet-layout="landscape"\] \.ledger-card td\.action-col \[data-delete-id\]::before/, 'landscape delete is icon-only');
assert.match(source, /!media\.matches \|\| \(typeof isTabletWorkspace === 'function' && isTabletWorkspace\(\)\)/, 'tablet must not create desktop custom date pickers');
assert.match(source, /syncTabletPickerOwnership\(orientation\)/, 'tablet orientation owns date and month picker presentation');
assert.match(css, /data-tablet-layout="landscape"\] \.shell\.cy-split-layout \{[\s\S]*?minmax\(250px, 28%\)/, 'landscape entry rail is reduced another ten percent');
assert.match(css, /data-tablet-layout="landscape"\] #mobileLedgerMonthDisplay/, 'landscape ledger month reuses mobile display layer');
assert.match(css, /data-tablet-layout="landscape"\] #ledgerExcelImport \{ display: none !important/, 'tablet landscape removes Excel import');
console.log('Tablet classification, rotation, keyboard, pinning and shared edit ownership passed.');

// Selecting another visible tablet row must not replace the original new-entry draft.
let cancellations = 0;
const originalDraft = { summary: '新增草稿', amount: '17' };
let shownDraft = { summary: '第一筆修改', amount: '99' };
const editor = vm.createContext({
  cyTouchWorkspaceEdit: { id: 1, draft: originalDraft },
  state: { transactions: [{ id: 2, tx_date: '2026-10-01', kind: 'income', account_name: '現金', category_name: '一般收入', amount: 8 }] },
  els: { summary: { focus() {} }, amount: {}, txDate: {}, accountName: {}, categoryName: {}, saveButton: {}, kindButtons: [], monthFilter: { value: '2026-10' } },
  document: { querySelector: () => null }, window: { scrollY: 0, scrollTo() {} },
  isTabletWorkspace: () => true, setTabletEntryExpanded() {}, isLocked: () => false,
  cancelTouchWorkspaceMobileEdit() { cancellations++; shownDraft = originalDraft; editor.cyTouchWorkspaceEdit = null; },
  captureTouchWorkspaceEntryDraft: () => shownDraft,
  setEntryKind() {}, ensureTouchWorkspaceOption() {}, syncTouchWorkspaceEntrySecondaryAction() {}, showMessage() {}, updateEntryLockState() {}, switchTouchWorkspaceMobilePage() {}
});
vm.runInContext(source.slice(source.indexOf('function beginTouchWorkspaceMobileEdit('), source.indexOf('async function saveTouchWorkspaceMobileEdit(')), editor);
editor.beginTouchWorkspaceMobileEdit(2);
assert.equal(editor.cyTouchWorkspaceEdit.draft, originalDraft);
assert.equal(cancellations, 1);
const selectedEdit = editor.cyTouchWorkspaceEdit;
editor.beginTouchWorkspaceMobileEdit(2);
assert.equal(editor.cyTouchWorkspaceEdit, selectedEdit, 'reselecting preserves unsaved edits');
assert.equal(cancellations, 1);

const editEvents = new Map();
const bindNode = label => ({ addEventListener(name, fn) { editEvents.set(label + ':' + name, fn); } });
const editNodes = { '#transactionRows': bindNode('rows'), '#transactionForm': bindNode('form'), '#mobileMainNav': bindNode('nav') };
const controller = vm.createContext({
  document: { querySelector: key => editNodes[key] }, window: { matchMedia: () => ({ addEventListener() {} }), addEventListener() {} },
  els: { monthFilter: bindNode('month') }, CY_TOUCH_WORKSPACE_MOBILE: '(max-width: 767px)', cyTouchWorkspaceEdit: { id: 2 },
  usesEntryTransactionEditor: () => true,
  cancelTouchWorkspaceMobileEdit() { controller.cyTouchWorkspaceEdit = null; },
  saveTouchWorkspaceMobileEdit() { writes++; }
});
vm.runInContext(source.slice(source.indexOf('function setupTouchWorkspaceMobileEdit()'), source.indexOf('function beginTouchWorkspaceMobileEdit(')), controller);
controller.setupTouchWorkspaceMobileEdit();
editEvents.get('month:change')();
assert.equal(controller.cyTouchWorkspaceEdit, null, 'month navigation cancels the old-month entry edit');
assert.equal(writes, 0, 'navigation never saves an unfinished edit');
console.log('Tablet selection and month navigation preserve the shared draft/cancel semantics.');
