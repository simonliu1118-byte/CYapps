import assert from 'node:assert/strict';
import fs from 'node:fs';
import vm from 'node:vm';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const read = name => fs.readFileSync(path.join(root, 'public', name), 'utf8');
const source = read('input-confirmation.js'), app = read('app.js'), adaptive = read('adaptive-ui.js');
const piece = (text, start, end) => text.slice(text.indexOf(start), text.indexOf(end));
assert.doesNotMatch(source, /MutationObserver/, 'confirmation results and count use explicit owner lifecycle');

const classList = () => {
  const values = new Set();
  return { add: (...names) => names.forEach(n => values.add(n)),
    remove: (...names) => names.forEach(n => values.delete(n)),
    contains: name => values.has(name),
    toggle(name, on) { if (on) values.add(name); else values.delete(name); } };
};
const list = { innerHTML: '' }, badge = { textContent: '', classList: classList() };
const events = new Map();
const message = { textContent: '', classList: classList() };
const els = {
  saveMessage: message,
  txDate: { value: '2026-10-01' }, accountName: { value: 'TEST_ACCOUNT' },
  categoryName: { value: 'TEST_SUBJECT' }, summary: { value: '' }, amount: { value: '100' }
};
const context = vm.createContext({ Number, String, Boolean, Array,
  CustomEvent: class { constructor(type, options) { this.type = type; this.detail = options.detail; } },
  els, state: { kind: 'income' }, setConfirmationDrawer() {},
  document: {
    querySelector: selector => selector === '#inputConfirmationList' ? list : selector === '#confirmationEdgeCount' ? badge : null,
    querySelectorAll: () => [...list.innerHTML.matchAll(/class="confirmation-item /g)]
  },
  window: { addEventListener: (type, listener) => events.set(type, listener),
    dispatchEvent: event => events.get(event.type)?.(event) }
});
vm.runInContext(piece(source, 'const cyInputConfirmations', "window.addEventListener('cyacc:entry-kind-changed'"), context);
vm.runInContext(piece(source, 'function bindInputConfirmation()', 'const CY_CONFIRMATION_DRAWER_KEY'), context);
vm.runInContext(source.slice(source.indexOf('function syncConfirmationCount()')), context);
vm.runInContext(piece(app, 'function showMessage(', 'function setDialogMessage('), context);
vm.runInContext(piece(app, 'function money(', 'function formatMonth('), context);
vm.runInContext(app.slice(app.indexOf('function escapeHtml(')), context);
context.bindInputConfirmation();
context.showMessage('startup failure', true);
assert.equal(list.innerHTML, '', 'unrelated messages do not create attempted transactions');
const entry = summary => ({ txDate: '2026-10-01', accountName: 'TEST_ACCOUNT', kind: 'income', categoryName: 'TEST_SUBJECT', summary, amount: '100' });
const emit = (type, detail) => context.window.dispatchEvent(new context.CustomEvent('cyacc:' + type, { detail }));
for (let i = 1; i <= 11; i++) emit('entry-submitted', { id: -i, entry: entry(`item-${String(i).padStart(2, '0')}`) });
assert.match(list.innerHTML, /class="confirmation-item pending"/);
assert.equal(badge.textContent, '10');
assert.equal([...list.innerHTML.matchAll(/class="confirmation-item /g)].length, 10);
assert.ok(list.innerHTML.indexOf('item-11') < list.innerHTML.indexOf('item-10'));
assert.doesNotMatch(list.innerHTML, /item-01/, 'eleventh entry evicts the oldest');
emit('entry-result', { id: -10, ok: true, message: '存檔成功' });
emit('entry-result', { id: -11, ok: false, message: 'TEST_FAILURE' });
assert.match(list.innerHTML, /item-11[^]*?TEST_FAILURE/);
assert.match(list.innerHTML, /item-10[^]*?存檔成功/);
const beforeEvictedResponse = list.innerHTML;
emit('entry-result', { id: -1, ok: true, message: '存檔成功' });
assert.equal(list.innerHTML, beforeEvictedResponse, 'late responses never reinsert evicted attempts');
emit('entry-result', { entry: entry('<script> & "quoted"'), ok: false, message: '金額必須為 1～9,999,999。' });
assert.match(list.innerHTML, /class="confirmation-item error"/);
assert.match(list.innerHTML, /&lt;script&gt; &amp; &quot;quoted&quot;/);
assert.match(list.innerHTML, /aria-label="金額必須為 1～9,999,999。"/);
const rendered = list.innerHTML;
context.showMessage('金額必須為 1～9,999,999。', true);
assert.equal(list.innerHTML, rendered, 'generic messages never create transaction results');
assert.equal(badge.textContent, '10');

// The existing Adaptive UI placement owner moves one message node. The message
// renderer and the mobile/tablet positions retain their original ownership.
const parent = () => ({ children: [], append(node) {
  if (node.parentElement) node.parentElement.children = node.parentElement.children.filter(n => n !== node);
  this.children.push(node); node.parentElement = this;
} });
const original = parent(), form = parent();
original.append(message);
let slot = null, desktop = true, mobile = false;
const lifecycle = new Map();
const saveButton = { parentElement: form, insertAdjacentElement(_, node) { form.append(node); node.previousElementSibling = this; } };
const placement = vm.createContext({ CY_TOUCH_WORKSPACE_MOBILE: '(max-width: 767px)',
  document: { querySelector: selector => ({ '#saveMessage': message, '#saveButton': saveButton, '#confirmationMessageSlot': slot })[selector] },
  window: { matchMedia: () => ({ addEventListener: (_, fn) => lifecycle.set('media', fn) }), addEventListener: (type, fn) => lifecycle.set(type, fn) },
  isDesktopInteractionWorkspace: () => desktop, usesEntryTransactionEditor: () => mobile
});
vm.runInContext(piece(adaptive, 'function setupTouchWorkspaceSaveMessage()', 'function setupTouchWorkspaceEntrySecondaryAction()'), placement);
placement.setupTouchWorkspaceSaveMessage();
assert.equal(message.parentElement, original, 'desktop waits for the real confirmation header');
slot = parent(); lifecycle.get('cyacc:confirmation-ready')();
assert.equal(message.parentElement, slot);
assert.equal(slot.hidden, false);
assert.equal(message.textContent, '金額必須為 1～9,999,999。');
desktop = false; mobile = true; lifecycle.get('media')();
assert.equal(message.parentElement, form);
assert.equal(message.classList.contains('cy-mobile-save-message'), true);
assert.equal(slot.hidden, true);
mobile = false; lifecycle.get('resize')();
assert.equal(message.parentElement, original, 'landscape tablet restores its original title status');
desktop = true; lifecycle.get('resize')();
assert.equal(message.parentElement, slot);
assert.equal(message.classList.contains('cy-mobile-save-message'), false);
assert.equal([...original.children, ...form.children, ...slot.children].filter(n => n === message).length, 1);

console.log('Confirmation lifecycle, latest-ten retention, escaped content and single-node device placement passed.');
