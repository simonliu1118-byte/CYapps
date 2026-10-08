import assert from 'node:assert/strict';
import fs from 'node:fs';
import vm from 'node:vm';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const HERE = path.dirname(fileURLToPath(import.meta.url));
const ROOT = path.resolve(HERE, '..');
const read = relative => fs.readFileSync(path.join(ROOT, relative), 'utf8');

const version = read('VERSION').trim();
const build = read('BUILD').trim();
const css = [
  'app.css',
  'accounting-ui.css',
  'quick-entry-settings.css',
  'category-management.css',
  'excel-export.css',
  'ledger-inline-edit.css',
  'excel-import.css',
  'backup.css',
  'desktop-migration.css',
  'adaptive-ui.css'
].map(name => read('public/' + name)).join('\n');
const appJs = read('public/app.js');
const quick = read('public/quick-entry.js');
const category = read('public/category-management.js');
const exportUi = read('public/excel-export-ui.js');
const inlineEdit = read('public/ledger-inline-edit.js');
const adaptive = read('public/adaptive-ui.js');
const adaptiveCss = read('public/adaptive-ui.css');
const confirmationJs = read('public/input-confirmation.js');
const worker = read('src/app.js');
const accountingTools = read('src/accounting-tools.js');

assert.equal(version, '0.22.29');
assert.equal(build, '0');
assert.match(css, /@media \(min-width: 1360px\)/);
assert.match(css, /grid-template-columns:\s*minmax\(380px, 420px\) minmax\(0, 1fr\)/);
assert.match(css, /\.current-user\.role-super-admin/);
assert.match(css, /\.cy-confirm-dialog/);
assert.match(css, /\.settings-manager-dialog/);
assert.match(css, /\.ledger-balance-popover/);
assert.match(css, /\.opening-modal\s*\{[\s\S]*?width:\s*min\(300px, calc\(100vw - 28px\)\) !important;/);

assert.match(quick, /enterStep\(els\.txDate, \(\) => els\.summary\?\.focus\(\)\)/);
assert.match(quick, /enterStep\(els\.summary,[\s\S]*?els\.amount\?\.focus\(\)/);
assert.match(quick, /enterStep\(els\.amount,[\s\S]*?els\.form\.requestSubmit\(\)/);
assert.doesNotMatch(category, /data-category-transfer|setupCategoryTransfer|injectCategoryTransferButtons/);
assert.match(exportUi, /button\.textContent = '匯出中…'/);
assert.match(exportUi, /aria-busy/);
assert.match(exportUi, /navigator\.share/);
assert.match(exportUi, /navigator\.canShare/);
assert.match(inlineEdit, /beginInlineLedgerEdit/);
assert.match(adaptive, /window\.cyConfirm = options => new Promise/);
assert.match(adaptive, /renderSettingsCategoryManager/);
assert.match(appJs, /const defaultButton = event\.target\.closest\('\[data-account-default\]'\)/, 'account default stays with the canonical account action owner');
assert.match(adaptive, /const favorite = event\.target\.closest\('\[data-category-favorite\]'\)/, 'category favorite stays with the canonical settings action owner');
assert.match(accountingTools, /url\.pathname === '\/api\/accounts\/reorder'/);
assert.match(accountingTools, /url\.pathname === '\/api\/category-groups\/reorder'/);
assert.match(accountingTools, /url\.pathname === '\/api\/categories\/reorder'/);
assert.match(worker, /SUMMARY_MAX_UNITS = 40/);
assert.match(worker, /SUMMARY_TOO_LONG/);


// Regression: the confirmation drawer is built on window load, later than the
// adaptive layout's initial DOMContentLoaded pass. Its ready signal must retry
// the canonical desktop split without a DOM observer or alternate renderer.
assert.match(confirmationJs, /new CustomEvent\('cyacc:confirmation-ready'\)/);
assert.match(adaptive, /window\.addEventListener\('cyacc:confirmation-ready', sync\)/);
assert.match(adaptiveCss, /\.shell\.cy-split-layout\s*\{/);
assert.match(adaptive, /root\.className = 'desktopUi-date-picker'/);
assert.match(adaptive, /input\.classList\.add\('desktopUi-native-date-source'\)/);
assert.match(adaptiveCss, /\.desktopUi-native-date-source\s*\{[\s\S]*?clip-path: inset\(50%\)/);
for (const cls of ['desktopUi-date-picker', 'desktopUi-date-trigger', 'desktopUi-date-popover',
                   'desktopUi-date-weekdays', 'desktopUi-date-days', 'desktopUi-date-day',
                   'desktopUi-date-choice-grid', 'desktopUi-date-footer']) {
  assert.ok(adaptiveCss.includes('.' + cls), cls + ' must match the canonical date picker DOM');
}
assert.doesNotMatch(adaptiveCss, /\.cy-native-date-source|\.cy-date-(?:picker|trigger|popover|day|head)/,
  'obsolete renamed date CSS must not remain');

const layoutSource = adaptive.slice(adaptive.indexOf('function setupAdaptiveSplitWorkspace()'),
  adaptive.indexOf('const CY_ENTRY_UI_SUMMARY_UNITS'));
assert.ok(layoutSource.startsWith('function setupAdaptiveSplitWorkspace()'), 'desktop split functions must exist');
const classes = () => {
  const values = new Set();
  return { add: value => values.add(value), remove: value => values.delete(value),
           contains: value => values.has(value) };
};
function element() {
  return { classList: classes(), parentElement: null, setAttribute() {},
    prepend(child) { child.parentElement = this; },
    append(child) { child.parentElement = this; } };
}
const shell = element(), entry = element(), ledger = element(), confirmation = element();
entry.parentElement = shell;
ledger.parentElement = shell;
let rail = null, confirmationReady = false, drawerOpen = false;
const listeners = new Map();
shell.querySelector = selector =>
  selector === '.entry-card' ? entry : selector === '.ledger-card' ? ledger :
  selector === '.cy-entry-rail' ? rail : null;
shell.insertBefore = child => { rail = child; child.parentElement = shell; };
const documentMock = {
  body: element(),
  createElement: () => element(),
  querySelector(selector) {
    if (selector === 'main.shell') return shell;
    if (selector === '#inputConfirmationCard') return confirmationReady ? confirmation : null;
    if (selector === '.entry-card') return entry;
    if (selector === '.ledger-card') return ledger;
    return null;
  }
};
confirmation.parentElement = documentMock.body;
const windowMock = {
  matchMedia: () => ({ matches: true, addEventListener() {} }),
  addEventListener: (event, callback) => listeners.set(event, callback)
};
const layoutContext = vm.createContext({
  window: windowMock, document: documentMock,
  isTabletWorkspace: () => false,
  setConfirmationDrawer: open => { drawerOpen = open; }
});
vm.runInContext(layoutSource, layoutContext);
vm.runInContext('setupAdaptiveSplitWorkspace()', layoutContext);
assert.equal(shell.classList.contains('cy-split-layout'), false,
  'initial desktop pass correctly waits while confirmation has not been built');
assert.equal(typeof listeners.get('cyacc:confirmation-ready'), 'function');
confirmationReady = true;
listeners.get('cyacc:confirmation-ready')();
assert.equal(shell.classList.contains('cy-split-layout'), true,
  'ready signal reconstructs the original desktop left/right workspace');
assert.equal(entry.parentElement, rail, 'entry returns to the left desktop rail');
assert.equal(confirmation.parentElement, rail, 'confirmation is inline instead of overlaying the ledger');
assert.equal(confirmation.classList.contains('cy-inline-confirmation'), true);
assert.equal(drawerOpen, true);

console.log('Desktop UI regression checks passed.');
