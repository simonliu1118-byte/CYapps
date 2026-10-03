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
const nodes = { 'main.shell': shell, '.entry-card': entry, '.ledger-card': ledger, '.v21-entry-rail': rail, '#tabletEntryToggle': button,
  '.topbar': { getBoundingClientRect: () => ({ height: 60 }) }, '#readOnlyNotice': { classList: classes, getBoundingClientRect: () => ({ height: 30 }) } };
const win = {
  innerWidth: 820, innerHeight: 1100, screen: { orientation: { type: 'portrait-primary', addEventListener() {} } },
  visualViewport: { get height() { return viewportHeight; }, addEventListener(name, fn) { listeners.set('visual:' + name, fn); } },
  matchMedia(query) { return { matches: query.includes('coarse') ? coarse : query.includes('767') ? win.innerWidth < 768 : win.innerWidth >= 1360, addEventListener() {} }; },
  addEventListener(name, fn) { listeners.set(name, fn); }
};
const context = vm.createContext({ window: win, document: { documentElement: root, createElement: () => controls,
  querySelector: selector => nodes[selector] || null, querySelectorAll: () => [] },
  CY_V21_SPLIT_MEDIA: '(min-width: 1360px)', cyV0215Build4Edit: edit,
  applyV21DesktopSplitWorkspace() { splitCalls++; }, setupV0215Build4EntrySecondaryAction() {}, renderSettingsAccountManager() {},
  cancelV0215Build4MobileEdit() { context.cyV0215Build4Edit = null; }, api() { writes++; }
});
vm.runInContext(tabletCode, context);
for (const [width, touch, expected] of [[375,true,false],[767,true,false],[768,false,true],[820,true,true],[1024,true,true],[1194,true,true],[1366,true,true],[1440,true,false],[1280,false,false],[1440,false,false]]) {
  win.innerWidth = width; coarse = touch;
  assert.equal(context.isTabletWorkspace(), expected, `tablet classification ${width}/${touch}`);
}
win.innerWidth = 820; coarse = true;
context.setupTabletWorkspace();
assert.equal(root.dataset.tabletLayout, 'portrait');
assert.equal(rail.dataset.entryExpanded, 'false');
button.click();
assert.equal(rail.dataset.entryExpanded, 'true');
pin.checked = true; pin.change({ target: pin }); button.click();
assert.equal(rail.dataset.entryExpanded, 'true', 'pinned entry stays open');
pin.checked = false; button.click();
assert.equal(rail.dataset.entryExpanded, 'false');
context.setTabletEntryExpanded(true);
const currentEdit = context.cyV0215Build4Edit;
win.innerWidth = 1194; win.screen.orientation.type = 'landscape-primary'; listeners.get('resize')();
assert.equal(root.dataset.tabletLayout, 'landscape');
assert.equal(context.cyV0215Build4Edit, currentEdit, 'rotation preserves the existing edit owner');
assert.equal(currentEdit.draft, draft, 'rotation preserves the same draft');
win.innerWidth = 820; win.screen.orientation.type = 'portrait-primary'; listeners.get('resize')();
viewportHeight = 420; listeners.get('visual:resize')();
assert.equal(root.dataset.tabletLayout, 'portrait', 'software keyboard does not change orientation');
assert.equal(style.get('--tablet-visible-height'), '420px');
assert.equal(context.cyV0215Build4Edit, currentEdit);
assert.equal(writes, 0, 'layout/rotation/keyboard/pinning never writes accounting data');
const count = splitCalls; context.setupTabletWorkspace(); assert.equal(splitCalls, count, 'setup binds once');
const css = read('adaptive-ui.css');
assert.match(css, /data-tablet-layout="portrait"[\s\S]*?grid-template-rows: minmax\(0, 1fr\) auto/);
assert.match(css, /\.ledger-card \.table-wrap \{[^}]*overflow: auto/);
assert.match(css, /body\[data-cyacc-read-only="true"\] \.v21-entry-rail \{ display: none/);
assert.match(css, /input\.v0211-native-date-source,[\s\S]*?pointer-events: auto !important/);
assert.match(read('ledger-inline-edit.js'), /window\.cyUsesEntryTransactionEditor\?\.\(\)/);
console.log('Tablet classification, rotation, keyboard, pinning and shared edit ownership passed.');
