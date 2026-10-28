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
  'excel-import.css',
  'backup.css',
  'desktop-migration.css',
  'adaptive-ui.css'
].map(name => read('public/' + name)).join('\n');
const appJs = read('public/app.js');
const quick = read('public/quick-entry.js');
const category = read('public/category-management.js');
const exportUi = read('public/excel-export-ui.js');
const adaptive = read('public/adaptive-ui.js');
const adaptiveCss = read('public/adaptive-ui.css');
const confirmationJs = read('public/input-confirmation.js');
const markup = read('public/index.html');
const ledgerSource = read('public/ledger-tools.js');
const worker = read('src/app.js');
const accountingTools = read('src/accounting-tools.js');
const adaptiveAsset = /src="(\/adaptive-ui\.js\?rev=[^"]+)"/.exec(markup)?.[1];
const deployment = fs.readFileSync(path.resolve(ROOT, '../../.github/workflows/cyaccountingweb-deploy.yml'), 'utf8');
assert.ok(adaptiveAsset && deployment.includes(adaptiveAsset), 'the deployed date picker and page use the same cache revision');

assert.equal(version, JSON.parse(read('src/build-metadata.json')).appVersion);
assert.equal(build, '2');
assert.match(adaptiveCss, /html\[data-viewport="desktop"\] body \{\s*display: flex;/);
assert.match(adaptiveCss, /\.shell\.cy-split-layout \{[\s\S]*?overflow: hidden;/);
assert.match(adaptiveCss, /\.ledger-card \{[\s\S]*?display: flex;[\s\S]*?overflow: hidden;/);
assert.match(adaptiveCss, /\.ledger-card \.table-wrap \{[\s\S]*?overflow-x: hidden;[\s\S]*?overflow-y: auto;/);
assert.match(adaptiveCss, /\.statistics-card \{[\s\S]*?overflow-y: auto;/);
assert.match(adaptiveCss, /\.ledger-card thead th \{\s*position: sticky;\s*top: 0;/);
assert.match(ledgerSource, /ledgerHead\.append\(title, toolbar\)/);

assert.match(markup, /志遠記帳系統 CYACC Web/);
assert.match(read('public/login.html'), /id="firstLoginHelp"/);
assert.match(read('public/login.html'), /cyacc-icon\.svg/);
assert.match(read('public/auth.js'), /renderCyaccAccountMenu/);
assert.doesNotMatch(read('public/auth.js'), /我的帳號（密碼、Email）/);
assert.match(adaptive, /accountCluster\.className = 'cy-account-menu'/);
assert.match(read('public/auth.js'), /root\.dataset\.role/);
assert.ok(read('public/auth.js').includes("root.querySelector('.cy-account-trigger')"));
assert.doesNotMatch(adaptiveCss, /cy-account-cluster/);
const appearanceCss = read('public/cyacc-appearance.css');
assert.match(appearanceCss, /color-scheme: light/);
assert.match(appearanceCss, /--cy-bg: #f6f6f9/);
assert.match(appearanceCss, /\.cy-account-menu\[data-role="SUPER_ADMIN"\] \.cy-account-avatar/);
assert.match(appearanceCss, /\.cy-account-menu:not\(\[data-identity-ready="true"\]\)/);
assert.doesNotMatch(appearanceCss, /data-cy-dark|color-scheme:\s*dark|cy-segmented|cy-theme-toggle/);
assert.doesNotMatch(markup, /data-cyacc-theme-switch|cyacc-appearance\.js|cyacc-appearance-slot/);
assert.doesNotMatch(read('public/login.html'), /data-cyacc-theme-toggle|cyacc-appearance\.js/);
assert.match(read('public/login.html'), /name="color-scheme" content="light"/);
assert.equal(fs.existsSync(path.join(ROOT, 'public/cyacc-appearance.js')), false, 'retired theme runtime must not remain');
assert.doesNotMatch(appearanceCss, /!important;\s*!\s*important/);
assert.match(css, /@media \(min-width: 1360px\)/);
assert.match(css, /grid-template-columns:\s*360px minmax\(0, 1fr\)/);
assert.match(appearanceCss, /\.cy-account-menu\[data-role="SUPER_ADMIN"\] \.cy-account-avatar/);
assert.match(css, /\.cy-confirm-dialog/);
assert.match(css, /\.settings-manager-dialog/);
assert.match(css, /\.ledger-balance-popover/);
assert.match(css, /\.opening-modal\s*\{[\s\S]*?width:\s*min\(880px, calc\(100vw - 48px\)\) !important;/);

assert.match(quick, /enterStep\(els\.txDate, \(\) => els\.summary\?\.focus\(\)\)/);
assert.match(quick, /enterStep\(els\.summary,[\s\S]*?els\.amount\?\.focus\(\)/);
assert.match(quick, /enterStep\(els\.amount,[\s\S]*?els\.form\.requestSubmit\(\)/);
assert.doesNotMatch(category, /data-category-transfer|setupCategoryTransfer|injectCategoryTransferButtons/);
assert.match(exportUi, /button\.textContent = '匯出中…'/);
assert.match(exportUi, /aria-busy/);
assert.match(exportUi, /navigator\.share/);
assert.match(exportUi, /navigator\.canShare/);
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

// Exercise the actual picker handlers with HTML-style attribute normalization.
// HTML parsers lowercase attribute names; mixed-case data-* markup must not
// silently pass because a fixture constructed dataset objects by hand.
class PickerElement {
  constructor() { this.attributes = {}; this.dataset = {}; this.listeners = {}; this.children = []; this.hidden = false; this.classList = { add() {}, toggle() {} }; }
  setAttribute(name, value) {
    name = name.toLowerCase();
    this.attributes[name] = String(value);
    if (name.startsWith('data-')) this.dataset[name.slice(5).replace(/-([a-z])/g, (_, c) => c.toUpperCase())] = String(value);
    if (name === 'hidden') this.hidden = true;
    if (name === 'id') this.id = String(value);
  }
  set innerHTML(markup) {
    this.children = [...markup.matchAll(/<[a-z][\w-]*\b([^>]*)>/gi)].map(tag => {
      const child = new PickerElement();
      for (const attr of tag[1].matchAll(/([\w-]+)(?:="([^"]*)")?/g)) child.setAttribute(attr[1], attr[2] || '');
      return child;
    });
  }
  matches(selector) {
    if (selector.startsWith('.')) return (this.attributes.class || '').split(/\s+/).includes(selector.slice(1));
    const match = /^\[([^=\]]+)(?:="([^"]*)")?\]$/.exec(selector);
    return Boolean(match && match[1].toLowerCase() in this.attributes &&
      (match[2] === undefined || this.attributes[match[1].toLowerCase()] === match[2]));
  }
  querySelectorAll(selector) { return this.children.filter(child => child.matches(selector)); }
  querySelector(selector) { return this.querySelectorAll(selector)[0] || null; }
  closest(selector) { return this.matches(selector) ? this : null; }
  addEventListener(type, listener) { (this.listeners[type] ||= []).push(listener); }
  dispatchEvent(event) { for (const listener of this.listeners[event.type] || []) listener(event); }
  click(target = this) { this.dispatchEvent({ type: 'click', target, stopPropagation() {} }); }
  focus() {}
}
function pickerFixture(entryDate = false) {
  const input = new PickerElement();
  if (entryDate) input.id = 'txDate';
  let value = '2026-10-13';
  Object.defineProperty(input, 'value', {
    get: () => value,
    set: next => { value = /^\d{4}-\d{2}-\d{2}$/.test(String(next)) ? String(next) : ''; }
  });
  const roots = [];
  const form = new PickerElement();
  const summary = new PickerElement(); summary.focus = () => { context.summaryFocused = true; };
  const amount = new PickerElement();
  input.insertAdjacentElement = (_, root) => roots.push(root);
  const context = vm.createContext({ Date, Event, String, Number, AbortController,
    els: { txDate: input, form, summary, amount }, window: { cyIsDesktopInteractionWorkspace: () => true },
    updateEntryLockState() {}, showMessage(message) { context.message = message; },
    localDateString: date => [date.getFullYear(), String(date.getMonth() + 1).padStart(2, '0'), String(date.getDate()).padStart(2, '0')].join('-'),
    CY_DesktopUi_WEEKDAYS: ['日', '一', '二', '三', '四', '五', '六'],
    CY_DesktopUi_MONTHS: Array.from({ length: 12 }, (_, i) => `${i + 1}月`),
    document: { createElement: () => new PickerElement(), addEventListener() {} }
  });
  const quickDates = read('public/quick-entry-settings.js');
  vm.runInContext("let cyDateDigitBuffer = ''; let cyDateDigitAt = 0;\n" + quickDates.slice(quickDates.indexOf('function bindDateQuickEntry('), quickDates.indexOf('function applyBufferedDateIfComplete(')), context);
  context.bindDateQuickEntry();
  vm.runInContext(quickDates.slice(quickDates.indexOf('function validDateValue('), quickDates.indexOf('function updateQuickEntryKeyboardHint(')), context);
  vm.runInContext(adaptive.slice(adaptive.indexOf('function ensureDesktopUiDatePicker(input)'),
    adaptive.indexOf('function refineDesktopUiHeaderIdentity()')), context);
  context.ensureDesktopUiDatePicker(input);
  context.ensureDesktopUiDatePicker(input);
  assert.equal(roots.length, 1, 'a date field keeps one picker');
  return { input, root: roots[0], context };
}
const { input: pickerDate, root: pickerRoot } = pickerFixture();
const dateTrigger = pickerRoot.querySelector('.desktopUi-date-trigger');
const dateContent = pickerRoot.querySelector('.desktopUi-date-content');
const dateTitle = pickerRoot.querySelector('.desktopUi-date-title');
const dateLabel = pickerRoot.querySelector('.desktopUi-date-label');
let pickerChanges = 0;
pickerDate.addEventListener('change', () => pickerChanges++);
dateTrigger.click();
const selectedDay = dateContent.querySelectorAll('.desktopUi-date-day')
  .find(day => Object.values(day.attributes).includes('2026-10-21'));
assert.ok(selectedDay, 'calendar offers a different date');
dateContent.click(selectedDay);
assert.equal(pickerDate.value, '2026-10-21', 'selecting a day updates the required canonical input instead of clearing it');
assert.equal(dateLabel.textContent, '2026/10/21');
assert.equal(pickerRoot.querySelector('.desktopUi-date-popover').hidden, true);
assert.equal(pickerChanges, 1, 'lock state and other consumers receive one canonical change');
let dateSaveRequest;
const dateSaveElements = {
  txDate: pickerDate, accountName: { value: 'TEST_ACCOUNT' }, categoryName: { value: 'TEST_SUBJECT' },
  summary: { value: 'picker regression' }, amount: { value: '50', focus() {} },
  monthFilter: { value: '', dispatchEvent() {} }, saveButton: { disabled: false }
};
const dateSaveContext = vm.createContext({ Number, String, Array, JSON, Error, Date, Map, Promise, Event,
  CustomEvent: class { constructor(type, options) { this.type = type; this.detail = options?.detail; } },
  window: { dispatchEvent() {} }, els: dateSaveElements, state: { kind: 'expense', transactions: [] }, isLocked: () => false,
  showMessage() {}, updateEntryLockState() {}, jsonHeaders: () => ({}),
  loadTransactions: async () => {},
  api: async (url, request) => { dateSaveRequest = { url, ...request, body: JSON.parse(request.body) }; return { transaction: { id: 99, tx_date: dateSaveRequest.body.txDate } }; }
});
vm.runInContext(appJs.slice(appJs.indexOf('function summaryCharacterUnits('),
  appJs.indexOf('function mergePendingTransactionUpdates(')), dateSaveContext);
vm.runInContext('const cyPendingTransactionCreates = new Map(); let cyTransactionCreateId = 0; let cyEntryDraftRevision = 0; let cyTransactionMutationRevision = 0;', dateSaveContext);
vm.runInContext(appJs.slice(appJs.indexOf('async function saveTransaction(event)'),
  appJs.indexOf('async function loadTransactions()')), dateSaveContext);
await dateSaveContext.saveTransaction({ preventDefault() {} });
assert.equal(dateSaveRequest.url, '/api/transactions');
assert.equal(dateSaveRequest.body.txDate, '2026-10-21', 'canonical submit serializes the selected calendar date');
assert.equal(dateSaveElements.monthFilter.value, '2026-10');
dateTrigger.click();
const dateNav = pickerRoot.querySelectorAll('[data-desktop-ui-date-nav]');
dateNav[0].click();
assert.equal(dateTitle.textContent, '2026年09月');
dateNav[1].click();
dateNav[1].click();
assert.equal(dateTitle.textContent, '2026年11月');
dateTitle.click();
dateContent.click(dateContent.querySelector('[data-desktop-ui-date-month="12"]'));
assert.equal(dateTitle.textContent, '2026年12月');
dateNav[1].click();
assert.equal(dateTitle.textContent, '2027年01月');
dateTitle.click();
dateTitle.click();
dateContent.click(dateContent.querySelector('[data-desktop-ui-date-year="2028"]'));
dateContent.click(dateContent.querySelector('[data-desktop-ui-date-month="2"]'));
dateContent.click(dateContent.querySelector('[data-desktop-ui-date-value="2028-02-29"]'));
assert.equal(pickerDate.value, '2028-02-29', 'year, month and leap day selection preserve the chosen date');
assert.equal(dateLabel.textContent, '2028/02/29');
pickerRoot.querySelector('.desktopUi-date-today-button').click();
assert.ok(pickerDate.value && /^\d{4}\/\d{2}\/\d{2}$/.test(dateLabel.textContent));
pickerDate.value = '2026-10-22';
pickerDate.dispatchEvent(new Event('change'));
assert.equal(dateLabel.textContent, '2026/10/22', 'canonical edit/clear and keyboard changes still update the desktop label');

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
  CY_ADAPTIVE_SPLIT_MEDIA: '(min-width: 1360px)',
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


/* V0.22.30: all breakpoints share a ledger without visible kind column;
   desktop alone reflows the canonical entry quick-tools and edit fields. */
assert.doesNotMatch(markup, /<h2>新增記帳<\/h2>|<h2>記帳資料<\/h2>/);
assert.match(markup, /class="card entry-card" aria-label="新增記帳"/);
assert.match(markup, /class="card ledger-card" aria-label="記帳資料"/);
assert.match(adaptiveCss, /Canonical shared ledger presentation/);
assert.match(adaptiveCss, /\.ledger-card thead th:nth-child\(3\),[\s\S]*?display: none !important;/);
assert.match(adaptiveCss, /ledger-row-income > \.ledger-amount::before\s*\{\s*content: "\+"/);
assert.match(adaptiveCss, /ledger-row-expense > \.ledger-amount::before\s*\{\s*content: "−"/);
assert.match(ledgerSource, /id="ledgerMonthLoading"/);
assert.match(appJs, /loadingLabel\.hidden = !busy/);
assert.match(adaptive, /function setupDesktopEntryQuickTools\(\)/);
assert.match(adaptive, /grid\.insertBefore\(item, anchor\)/);
assert.match(adaptive, /clearTouchWorkspaceEntryForm\(\)/);
assert.match(adaptiveCss, /grid-template-columns: minmax\(0, 1fr\) 78px 68px !important/);
assert.match(adaptiveCss, /#monthSummary[\s\S]*?grid-template-areas: "opening income expense ending net" !important/);
assert.match(adaptiveCss, /\.opening-modal\s*\{\s*width: min\(880px/);
assert.match(appJs, /class="opening-account-column"/);
assert.match(appJs, /cyIsDesktopInteractionWorkspace\?\.\(\) \? ' open' : ''/);
assert.match(adaptive, /svg class="desktopUi-date-calendar-icon"/);
assert.doesNotMatch(adaptive, /desktopUi-date-calendar-icon" aria-hidden="true">▣/);

// Editable dates and the picker update the same required source field.
const editable = pickerFixture(true);
const textDate = editable.root.querySelector('.desktopUi-date-text');
const typeDate = value => { textDate.value = value; textDate.dispatchEvent({ type: 'input' }); };
typeDate('0924');
assert.equal(editable.input.value, `${new Date().getFullYear()}-09-24`);
typeDate('20260924'); assert.equal(editable.input.value, '2026-09-24');
typeDate('2024/02/29'); assert.equal(editable.input.value, '2024-02-29');
typeDate('2026/02/29'); assert.equal(editable.input.value, '', 'invalid days cannot leave a stale canonical date');
typeDate('20261231');
const pressDate = (key, ctrlKey = false) => {
  const event = { type: 'keydown', target: textDate, key, ctrlKey, preventDefault() { this.defaultPrevented = true; }, stopPropagation() { this.stopped = true; } };
  textDate.dispatchEvent(event); if (!event.stopped) editable.context.els.form.dispatchEvent(event);
};
pressDate('ArrowUp', true); assert.equal(editable.input.value, '2027-01-01');
pressDate('ArrowDown', true); assert.equal(editable.input.value, '2026-12-31');
pressDate('Enter'); assert.equal(editable.context.summaryFocused, true);
assert.equal(textDate.value, '2026/12/31');
editable.root.querySelector('.desktopUi-date-trigger').click();
editable.root.querySelector('.desktopUi-date-content').click(editable.root.querySelector('.desktopUi-date-content').querySelectorAll('.desktopUi-date-day').find(day => day.dataset.desktopUiDateValue === '2026-12-20'));
assert.equal(editable.input.value, '2026-12-20'); assert.equal(textDate.value, '2026/12/20');
assert.doesNotMatch(adaptive, /setupDesktopUiKeyboardBridge|desktopUiDateObserver|new KeyboardEvent/, 'date typing must not rely on an observer or a synthetic keyboard retry bridge');
console.log('Desktop UI, editable date, four/eight digit dates and day-step regression checks passed.');

const spanSource = ledgerSource.match(/function ledgerPresentationColumnSpan\(\) \{[\s\S]*?\n\}/)[0];
for (const desktop of [true, false]) {
  const scope = vm.createContext({ window: { cyIsDesktopInteractionWorkspace: () => desktop } });
  vm.runInContext(spanSource, scope);
  assert.equal(vm.runInContext('ledgerPresentationColumnSpan()', scope), desktop ? 7 : 8);
}

// Desktop routes non-boundary months to the existing settings action; touch
// retains disabled state. Boundary writes keep the shared confirmation/writer.
const quickLockSource = ledgerSource.slice(ledgerSource.indexOf('function shiftLedgerMonth('),
  ledgerSource.indexOf('window.cySyncLedgerMonthDisplay'));
const lockTargets = [];
let desktopMode = true, settingsVisits = 0;
const quickButton = { disabled: false, title: '', classList: { toggle() {} }, setAttribute() {} };
const lockContext = vm.createContext({
  state: { lockedThrough: '2026-08' }, els: { monthFilter: { value: '2026-06' } }, Date,
  document: { querySelector: selector => selector === '#ledgerQuickLockButton' ? quickButton
    : selector === '#ledgerLockSettingsButton' ? { click() { settingsVisits++; } }
    : { classList: { contains() { return false; } } } },
  window: { cyIsDesktopInteractionWorkspace: () => desktopMode,
    cyConfirm: async () => true, cyaccSaveLock: async target => lockTargets.push(target) }
});
vm.runInContext(quickLockSource, lockContext);
lockContext.syncLedgerQuickLock();
assert.equal(quickButton.disabled, false);
await lockContext.handleLedgerQuickLock();
assert.equal(settingsVisits, 1);
desktopMode = false;
lockContext.syncLedgerQuickLock();
assert.equal(quickButton.disabled, true);
await lockContext.handleLedgerQuickLock();
assert.equal(settingsVisits, 1);
desktopMode = true;
lockContext.els.monthFilter.value = '2026-09';
await lockContext.handleLedgerQuickLock();
lockContext.els.monthFilter.value = '2026-08';
await lockContext.handleLedgerQuickLock();
assert.deepEqual(lockTargets, ['2026-09', '2026-07']);

// View sorting never changes the chronological balances or shared source rows.
let ledgerDesktop = true;
const ledgerClicks = new Map();
const ledgerHeader = { innerHTML: '', dataset: {}, querySelector(selector) {
  const id = selector.slice(1);
  return this.innerHTML.includes(`id="${id}"`) ? { addEventListener: (_, fn) => ledgerClicks.set(id, fn) } : null;
} };
const ledgerRows = { innerHTML: '' }, renderedRows = [];
const ledgerTransactions = [
  { id: 1, tx_date: '2026-10-01', account_name: 'A', kind: 'income', amount: 100, summary: 'match' },
  { id: 2, tx_date: '2026-10-02', account_name: 'A', kind: 'expense', amount: 20, summary: 'other' },
  { id: 3, tx_date: '2026-10-02', account_name: 'B', kind: 'income', amount: 30, summary: 'other' },
  { id: 4, tx_date: '2026-10-03', account_name: 'B', kind: 'expense', amount: 10, summary: 'other' }
];
const ordering = vm.createContext({ Map, Set, Number, String, Boolean, Array, Intl,
  state: { transactions: ledgerTransactions },
  els: { transactionRows: ledgerRows, monthFilter: { value: '2026-10' }, monthSummary: { innerHTML: '' } },
  document: { querySelector: selector => selector === '.ledger-card thead tr' ? ledgerHeader : null },
  window: { cyIsDesktopInteractionWorkspace: () => ledgerDesktop },
  money: n => `$${n}`, escapeHtml: value => String(value),
  ledgerPresentationColumnSpan: () => ledgerDesktop ? 7 : 8,
  ledgerAccountVisual: () => ({ slot: 1, background: '#fff', foreground: '#000' }),
  renderLedgerMessage: message => { ledgerRows.innerHTML = message; },
  writeLedgerRows: html => { ledgerRows.innerHTML = html; },
  renderLedgerRow(tx, balance) { renderedRows.push({ id: tx.id, balance }); return `<tr data-test-id="${tx.id}">${balance}</tr>`; }
});
vm.runInContext("let cyLedgerGroupByAccount = false, cyLedgerDateDescending = false, cyLedgerSearch = ''; let cyLedgerOpeningData = { accounts: [{name:'A',amount:1000},{name:'B',amount:2000}] };", ordering);
vm.runInContext(ledgerSource.slice(ledgerSource.indexOf('function renderDesktopLedger()'),
  ledgerSource.indexOf('const LEDGER_ACCOUNT_LIGHT_PALETTE')), ordering);
vm.runInContext(ledgerSource.slice(ledgerSource.indexOf('function updateLedgerHeader()'),
  ledgerSource.indexOf('function ledgerPresentationColumnSpan()')), ordering);
const renderOrder = action => { renderedRows.length = 0; action(); return renderedRows.map(row => row.id); };
assert.deepEqual(renderOrder(() => ordering.renderDesktopLedger()), [1, 3, 2, 4]);
assert.match(ledgerHeader.innerHTML, /aria-sort="ascending"/);
const chronologicalBalances = new Map(renderedRows.map(row => [row.id, row.balance]));
assert.deepEqual(renderOrder(() => ledgerClicks.get('ledgerDateHeader')()), [4, 3, 2, 1]);
assert.match(ledgerHeader.innerHTML, /aria-sort="descending"/);
for (const row of renderedRows) assert.equal(row.balance, chronologicalBalances.get(row.id));
assert.deepEqual(renderOrder(() => ledgerClicks.get('ledgerAccountHeader')()), [2, 1, 4, 3]);
assert.match(ledgerHeader.innerHTML, /帳戶 \[分組\]/);
assert.deepEqual(renderedRows, [{ id: 2, balance: 1080 }, { id: 1, balance: 1100 }, { id: 4, balance: 2020 }, { id: 3, balance: 2030 }]);
assert.deepEqual(renderOrder(() => ledgerClicks.get('ledgerDateHeader')()), [1, 2, 3, 4]);
assert.deepEqual(renderOrder(() => ledgerClicks.get('ledgerAccountHeader')()), [1, 3, 2, 4]);
assert.doesNotMatch(ledgerHeader.innerHTML, /帳戶 \[分組\]/);
renderOrder(() => ledgerClicks.get('ledgerDateHeader')());
ledgerDesktop = false;
assert.deepEqual(renderOrder(() => ordering.syncLedgerPresentation()), [1, 3, 2, 4], 'touch returns to its existing chronological display');
assert.doesNotMatch(ledgerHeader.innerHTML, /ledgerDateHeader|ledger-header-button/);
renderOrder(() => ledgerClicks.get('ledgerAccountHeader')());
assert.match(ledgerHeader.innerHTML, /帳戶 ▲/, 'touch grouping label is preserved');
ledgerDesktop = true;
assert.deepEqual(renderOrder(() => ordering.syncLedgerPresentation()), [2, 1, 4, 3]);
assert.match(ledgerHeader.innerHTML, /帳戶 \[分組\]/);
assert.deepEqual(ledgerTransactions.map(tx => tx.id), [1, 2, 3, 4], 'presentation sorting never mutates the transaction snapshot');
vm.runInContext("cyLedgerGroupByAccount = false; cyLedgerSearch = 'match';", ordering);
assert.deepEqual(renderOrder(() => ordering.renderDesktopLedger()), [1]);
assert.equal(renderedRows[0].balance, chronologicalBalances.get(1), 'search retains balances from the complete month');

// The form owns one Ctrl-day shortcut, including summary and amount, without moving focus.
for (const name of ['summary','amount','txDate']) {
  const field=editable.context.els[name];
  editable.input.value='2026-12-31'; let changes=0;
  const listener=()=>changes++; editable.input.addEventListener('change',listener);
  const key=(value,extra={})=>editable.context.els.form.dispatchEvent({type:'keydown',target:field,key:value,ctrlKey:true,preventDefault(){this.defaultPrevented=true;},...extra});
  key('ArrowUp'); assert.equal(editable.input.value,'2027-01-01'); assert.equal(changes,1);
  key('ArrowDown'); assert.equal(editable.input.value,'2026-12-31'); assert.equal(changes,2);
  for(const extra of [{isComposing:true},{altKey:true},{metaKey:true},{shiftKey:true}]) key('ArrowUp',extra);
  assert.equal(editable.input.value,'2026-12-31');
}
editable.context.window.cyIsDesktopInteractionWorkspace=()=>false;
editable.input.value='2026-12-31';
editable.context.els.form.dispatchEvent({type:'keydown',target:editable.context.els.summary,key:'ArrowUp',ctrlKey:true,preventDefault(){throw Error('touch summary unchanged');}});
assert.equal(editable.input.value,'2026-12-31');

// The settings render updates the canonical month and its existing picker listener.
let lockChangeEvents=0, settingsDesktop=true;
const lockRenderNodes={lockedThrough:{value:'',dispatchEvent(event){assert.equal(event.type,'change');lockChangeEvents++;}},lockStatusText:{}};
const settingsRenderContext=vm.createContext({state:{lockedThrough:'2026-04'},els:lockRenderNodes,formatMonth:value=>value,Event:class{constructor(type){this.type=type;}},window:{cyIsDesktopInteractionWorkspace:()=>settingsDesktop}});
vm.runInContext(appJs.slice(appJs.indexOf('function renderSettings()'),appJs.indexOf('async function handleAccountAction')),settingsRenderContext);
settingsRenderContext.renderSettings();assert.equal(lockRenderNodes.lockedThrough.value,'2026-04');assert.equal(lockRenderNodes.lockStatusText.hidden,true);assert.equal(lockChangeEvents,1);
settingsRenderContext.state.lockedThrough=null;settingsRenderContext.renderSettings();assert.equal(lockRenderNodes.lockedThrough.value,'');assert.equal(lockRenderNodes.lockStatusText.hidden,false);
settingsDesktop=false;settingsRenderContext.state.lockedThrough='2026-04';settingsRenderContext.renderSettings();assert.equal(lockRenderNodes.lockStatusText.hidden,false,'touch keeps existing lock status');
// The canonical navigation lifecycle keeps DOM/keyboard order aligned with presentation.
const settingsNavButtons=new Map(['accounts','categories','statistics','data','quick','lock','backup'].map(name=>[name,{name,textContent:''}]));
const settingsNav={children:[...settingsNavButtons.values()],querySelector(selector){return settingsNavButtons.get(/="([^"]+)"/.exec(selector)?.[1]);},append(...nodes){this.children=this.children.filter(node=>!nodes.includes(node)).concat(nodes);}};
let navDesktop=true;const quickSettingsTitle={};
const navContext=vm.createContext({isDesktopInteractionWorkspace:()=>navDesktop,document:{querySelector(selector){if(selector==='.settings-nav')return settingsNav;if(selector.endsWith(' h3'))return quickSettingsTitle;return settingsNav.querySelector(selector);}}});
vm.runInContext(adaptive.slice(adaptive.indexOf('function syncSettingsNavigation()'),adaptive.indexOf('function prepareSettingsPresentation(')),navContext);
navContext.syncSettingsNavigation();assert.deepEqual(settingsNav.children.map(node=>node.name),['accounts','categories','quick','statistics','lock','data','backup']);assert.equal(settingsNavButtons.get('quick').textContent,'一般');assert.equal(settingsNavButtons.get('lock').textContent,'月份鎖帳');
navDesktop=false;navContext.syncSettingsNavigation();assert.deepEqual(settingsNav.children.map(node=>node.name),['accounts','categories','statistics','data','quick','lock','backup']);assert.equal(settingsNavButtons.get('quick').textContent,'快速輸入');
console.log('Settings navigation order/touch restoration and current locked month lifecycle passed.');
