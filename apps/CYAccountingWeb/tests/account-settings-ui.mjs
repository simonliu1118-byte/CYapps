import assert from 'node:assert/strict';
import fs from 'node:fs';
import vm from 'node:vm';

const app = fs.readFileSync(new URL('../public/app.js', import.meta.url), 'utf8');
const adaptive = fs.readFileSync(new URL('../public/adaptive-ui.js', import.meta.url), 'utf8');
const messages = {};
const archiveHost = { innerHTML: '' };
const activeHost = { innerHTML: '' };
const closeButton = { addEventListener(type, callback) { this[type] = callback; } };
const button = { addEventListener(type, callback) { this[type] = callback; } };
const dialog = {
  dataset: {}, open: false,
  querySelector(selector) { return selector === '[data-close-archived-accounts]' ? closeButton : messages; },
  addEventListener(type, callback) { this[type] = callback; },
  showModal() { this.open = true; }, close() { this.open = false; }
};
let actionCalls = 0;
const context = vm.createContext({
  state: {
    accounts: [{ id: 9, name: '使用中', is_default: 1 }],
    archivedAccounts: [
      { id: 1, name: '零餘額', transaction_count: 0, latest_opening_amount: 0 },
      { id: 2, name: '有交易', transaction_count: 1, latest_opening_amount: 0 },
      { id: 3, name: '有餘額', transaction_count: 0, latest_opening_amount: 10 }
    ]
  },
  isTabletWorkspace: () => false,
  SETTINGS_MANAGER_DESKTOP: '(min-width: 1024px)',
  isDesktopInteractionWorkspace: () => true,
  window: { cyaccCurrentUser: { role: 'ADMIN' }, matchMedia: () => ({ matches: true }) },
  document: { querySelector(selector) {
    return ({ '#archivedAccountsDialog': dialog, '#openArchivedAccountsButton': button, '#archivedAccountRows': archiveHost, '#accountRows': activeHost })[selector];
  } },
  escapeHtml(value) { return String(value).replaceAll('&', '&amp;').replaceAll('<', '&lt;').replaceAll('"', '&quot;'); },
  setDialogMessage(target, value) { target.value = value; },
  handleAccountAction() { actionCalls++; },
  bindMobileAccountReorder() {},
  els: { settingsMessage: { parent: true }, categoryName: { innerHTML: '', value: '' } }
});
vm.runInContext(app.slice(app.indexOf('function openingAccountRowHtml('), app.indexOf('async function loadOpening')), context);
vm.runInContext(app.slice(app.indexOf('function accountArchiveMessage('), app.indexOf('async function restoreAccountOptimistically')), context);
vm.runInContext(adaptive.slice(adaptive.indexOf('function renderSettingsAccountManager('), adaptive.indexOf('function renderSettingsCategoryManager(')), context);
vm.runInContext(adaptive.slice(adaptive.indexOf('function renderMobileAccountManager('), adaptive.indexOf('function bindMobileAccountReorder(')), context);
vm.runInContext(adaptive.slice(adaptive.indexOf('function settingsManagerEscape('), adaptive.indexOf('const CY_LEDGER_BALANCE_HOVER')), context);

vm.runInContext(app.slice(app.indexOf('function renderCategories('), app.indexOf('function setEntryKind(')), context);
context.state.kind = 'income';
context.state.groups = [{ id: 8, name: '網路' }];
context.state.categories = [
  { name: '門市收入', kind: 'income', group_name: '門市' },
  { name: '網路<&收入', kind: 'income', group_id: 8 },
  { name: '歷史科目', kind: 'income' },
  { name: '租金', kind: 'expense', group_name: '店務' }
];
context.renderCategories('網路<&收入');
assert.match(context.els.categoryName.innerHTML, /value="門市收入">門市／門市收入<\/option>/);
assert.match(context.els.categoryName.innerHTML, /value="網路&lt;&amp;收入">網路／網路&lt;&amp;收入<\/option>/);
assert.match(context.els.categoryName.innerHTML, /value="歷史科目">歷史科目<\/option>/);
assert.doesNotMatch(context.els.categoryName.innerHTML, /租金/);
assert.equal(context.els.categoryName.value, '網路<&收入', 'displaying groups preserves transaction and favorite category values');
context.state.kind = 'expense';
context.renderCategories('租金');
assert.match(context.els.categoryName.innerHTML, /value="租金">店務／租金<\/option>/);
assert.doesNotMatch(context.els.categoryName.innerHTML, /門市收入/);

const automatic = context.openingAccountRowHtml({ name: '自動帳戶', amount: 50, automaticAmount: 50, source: 'automatic', automaticAnchorMonth: '2026-09' });
assert.doesNotMatch(automatic, /<span class="opening-source|<small>|承接|歷史收支|>自動</);
const manual = context.openingAccountRowHtml({ name: '<現金>', amount: 100, automaticAmount: 50, source: 'override', overrideReason: '對帳' });
assert.match(manual, />調整<\/span><strong>/, 'adjustment badge precedes the account name');
assert.doesNotMatch(manual, /<small>|手動調整|自動值|對帳/);
assert.match(manual, /&lt;現金>/);
assert.match(manual, /data-opening-automatic="50"/, 'clearing back to automatic remains supported');

context.setupArchivedAccountDialog();
context.setupArchivedAccountDialog();
button.click();
assert.equal(dialog.open, true);
assert.match(archiveHost.innerHTML, /data-account-restore="1"/);
assert.doesNotMatch(archiveHost.innerHTML, /data-account-permanent-delete|<small>|歷史記帳|目前期初/);
dialog.click(); assert.equal(actionCalls, 1, 'archive dialog reuses the existing account action owner');
assert.equal(context.accountArchiveMessage(), messages);
closeButton.click(); assert.equal(dialog.open, false);
assert.equal(context.accountArchiveMessage(), context.els.settingsMessage);

context.window.cyaccCurrentUser.role = 'SUPER_ADMIN';
context.renderArchivedAccountManager();
assert.match(archiveHost.innerHTML, /data-account-permanent-delete="1"[^>]*aria-label="永久刪除帳戶"/);
assert.match(archiveHost.innerHTML, /data-account-permanent-delete="2"[^>]* disabled/);
assert.match(archiveHost.innerHTML, /data-account-permanent-delete="3"[^>]* disabled/);
context.renderSettingsAccountManager();
assert.doesNotMatch(activeHost.innerHTML, /已封存|零餘額|有交易|有餘額/);
assert.match(activeHost.innerHTML, /data-account-archive="9"[^>]*aria-label="封存帳戶">\s*<svg/);
assert.doesNotMatch(activeHost.innerHTML, /settings-account-section-title/);
context.isTabletWorkspace = () => true;
context.isDesktopInteractionWorkspace = () => false;
context.renderSettingsAccountManager();
assert.match(activeHost.innerHTML, /data-mobile-account-drag/, 'tablet uses the same touch sorting handles');
context.renderMobileAccountManager();
assert.match(activeHost.innerHTML, /data-account-archive="9"[^>]*aria-label="封存帳戶">\s*<svg/);
assert.doesNotMatch(activeHost.innerHTML, /mobile-account-section-title/);
assert.doesNotMatch(activeHost.innerHTML, /已封存|零餘額|有交易|有餘額/);
context.state.archivedAccounts = [];
context.renderArchivedAccountManager();
assert.match(archiveHost.innerHTML, /沒有已封存帳戶/);
console.log('Opening adjustment-only presentation, separate archived account dialog and role/action routing tests passed.');
