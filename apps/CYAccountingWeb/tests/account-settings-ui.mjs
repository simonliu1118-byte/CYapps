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
  SETTINGS_MANAGER_DESKTOP: '(min-width: 1024px)',
  window: { cyaccCurrentUser: { role: 'ADMIN' }, matchMedia: () => ({ matches: true }) },
  document: { querySelector(selector) {
    return ({ '#archivedAccountsDialog': dialog, '#openArchivedAccountsButton': button, '#archivedAccountRows': archiveHost, '#accountRows': activeHost })[selector];
  } },
  escapeHtml(value) { return String(value).replaceAll('&', '&amp;').replaceAll('<', '&lt;').replaceAll('"', '&quot;'); },
  setDialogMessage(target, value) { target.value = value; },
  handleAccountAction() { actionCalls++; },
  bindMobileAccountReorder() {},
  els: { settingsMessage: { parent: true } }
});
vm.runInContext(app.slice(app.indexOf('function openingAccountRowHtml('), app.indexOf('async function loadOpening')), context);
vm.runInContext(app.slice(app.indexOf('function accountArchiveMessage('), app.indexOf('async function restoreAccountOptimistically')), context);
vm.runInContext(adaptive.slice(adaptive.indexOf('function renderSettingsAccountManager('), adaptive.indexOf('function renderSettingsCategoryManager(')), context);
vm.runInContext(adaptive.slice(adaptive.indexOf('function renderMobileAccountManager('), adaptive.indexOf('function bindMobileAccountReorder(')), context);
vm.runInContext(adaptive.slice(adaptive.indexOf('function settingsManagerEscape('), adaptive.indexOf('const CY_V0214_HOVER')), context);

const automatic = context.openingAccountRowHtml({ name: '自動帳戶', amount: 50, automaticAmount: 50, source: 'automatic', automaticAnchorMonth: '2026-09' });
assert.doesNotMatch(automatic, /<span class="opening-source|<small>|承接|歷史收支|>自動</);
const manual = context.openingAccountRowHtml({ name: '<現金>', amount: 100, automaticAmount: 50, source: 'override', overrideReason: '對帳' });
assert.match(manual, />調整<\/span>/);
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
context.renderMobileAccountManager();
assert.doesNotMatch(activeHost.innerHTML, /已封存|零餘額|有交易|有餘額/);
context.state.archivedAccounts = [];
context.renderArchivedAccountManager();
assert.match(archiveHost.innerHTML, /沒有已封存帳戶/);
console.log('Opening adjustment-only presentation, separate archived account dialog and role/action routing tests passed.');
