import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import vm from 'node:vm';

const HERE = path.dirname(fileURLToPath(import.meta.url));
const ROOT = path.resolve(HERE, '..');
const read = relative => fs.readFileSync(path.join(ROOT, relative), 'utf8');

const html = read('public/index.html');
const auth = read('public/auth.js');
const app = read('public/app.js');
const ledger = read('public/ledger-tools.js');
const login = read('public/login.js');
const core = read('src/index.js');
const features = [
  'quick-entry.js',
  'ledger-tools.js',
  'input-confirmation.js',
  'quick-entry-settings.js',
  'category-management.js',
  'excel-export-ui.js',
  'ledger-inline-edit.js',
  'excel-import-ui.js',
  'backup-ui.js',
  'desktop-migration-ui.js',
  'adaptive-ui.js'
].map(name => read('public/' + name)).join('\n');

assert.match(html, /<body class="cyacc-booting">/);
assert.match(html, /id="cyaccBootStatus"/);
assert.match(html, /__cyaccBootWatchdog/);
assert.match(auth, /startCyaccAuth/);
assert.doesNotMatch(auth, /window\.__CYACC_BOOT_USER__/);
assert.match(auth, /window\.cyaccSessionPromise = checkSessionFallback\(\)/);
assert.match(auth, /AbortController/);
assert.match(auth, /8_000/);
assert.match(auth, /Promise\.race\(\[request, timeout\]\)/);
assert.match(auth, /TimeoutError/);
assert.match(app, /startCyaccApp/);
assert.match(app, /if \(window\.cyaccSessionPromise\) await window\.cyaccSessionPromise;[\s\S]*?setCyaccBootStage\('正在準備帳務資料…'\)/);
assert.match(app, /finishCyaccBoot/);
assert.match(app, /12_000/);
assert.match(app, /Promise\.race\(\[request, timeout\]\)/);
assert.match(app, /error\?\.message \|\| '載入失敗，請重新整理後再試。'/);
assert.match(ledger, /window\.cyaccRefreshLedgerView = loadLedgerOpeningAndRender/);
assert.match(login, /\/api\/auth\/login/);
assert.match(login, /AbortController/);
assert.match(login, /8_000/);
assert.match(login, /Promise\.race\(\[request, timeout\]\)/);
assert.match(login, /TimeoutError/);
assert.match(login, /window\.location\.replace\('\/'\)/);
assert.match(html, /auth\.js\?rev=session-expiry-navigation/);
const appAsset = /src="(\/app\.js\?rev=[^"]+)"/.exec(html)?.[1];
assert.ok(appAsset, 'the current app asset must have a cache revision');
const deployWorkflow = fs.readFileSync(path.resolve(ROOT, '../../.github/workflows/cyaccountingweb-deploy.yml'), 'utf8');
assert.ok(deployWorkflow.includes(appAsset), 'production verification must request the same app asset revision as the page');
const bootstrapSource = core.slice(core.indexOf('async function handleBootstrap'), core.indexOf('async function handleListTransactions'));
assert.match(bootstrapSource, /await db\.batch\(/);
assert.doesNotMatch(bootstrapSource, /Promise\.all\(/);
assert.equal((auth.match(/\/api\/auth\/me/g) || []).length, 1);
assert.doesNotMatch(features, /\/api\/auth\/me/);
assert.match(features, /mobileMainNav/);
assert.match(features, /mobileAccountMenuButton/);
assert.match(features, /ledgerMoreButton/);

console.log('Deterministic startup checks passed.');


const browserListeners = {};
const bootStatus = { textContent: '', hidden: false };
const classNames = new Set(['cyacc-booting']);
const browserContext = {
  AbortController,
  CustomEvent: class CustomEvent { constructor(type, init) { this.type = type; this.detail = init?.detail; } },
  Error,
  Promise,
  console,
  fetch: async () => await new Promise(() => {}),
  location: { replace() {} },
  document: {
    readyState: 'loading',
    addEventListener(type, listener) { browserListeners[type] = listener; },
    querySelector(selector) { return selector === '#cyaccBootStatus' ? bootStatus : null; },
    body: {
      dataset: {},
      classList: {
        add(name) { classNames.add(name); },
        remove(name) { classNames.delete(name); },
        contains(name) { return classNames.has(name); }
      }
    }
  },
  window: {
    setTimeout,
    clearTimeout,
    matchMedia() { return { matches: false }; },
    dispatchEvent() {}
  }
};
vm.createContext(browserContext);
vm.runInContext(auth.replaceAll('8_000', '40'), browserContext, { filename: 'auth.js' });
browserListeners.DOMContentLoaded();
await assert.rejects(browserContext.window.cyaccSessionPromise, /帳號驗證逾時/);
assert.equal(bootStatus.textContent, '帳號驗證逾時，請重新整理後再試。');
assert.equal(classNames.has('cyacc-boot-failed'), true);

// Expiry navigation uses the provider timestamp and catches revocation on API calls.
const redirects = [], expiryTimers = [], expiryListeners = {};
let browserNow = 1000;
const expiryScope = vm.createContext({
  Date: { parse: Date.parse, now: () => browserNow },
  Number, Math, location: { replace: target => redirects.push(target) },
  window: { setTimeout: (fn, delay) => expiryTimers.push({ fn, delay }), addEventListener: (event, fn) => { expiryListeners[event] = fn; } },
  document: { visibilityState: 'visible', addEventListener: (event, fn) => { expiryListeners[event] = fn; } }
});
vm.runInContext(auth.slice(0, auth.indexOf('let cyaccAuthStarted')), expiryScope);
assert.equal(vm.runInContext('window.cyaccHandleAuthResponse({status:403})', expiryScope), false, 'permission denial must not log out a valid user');
assert.equal(vm.runInContext('window.cyaccHandleAuthResponse({status:401})', expiryScope), true);
assert.deepEqual(redirects, ['/login']);
redirects.length = 0;
const expirySource = auth.slice(auth.indexOf('  function scheduleSessionExpiry'), auth.indexOf('  function validBootUser'));
vm.runInContext(expirySource, expiryScope);
vm.runInContext("scheduleSessionExpiry('1970-01-01T00:00:02.000Z')", expiryScope);
assert.equal(expiryTimers[0].delay, 1000);
browserNow = 2001;
expiryTimers[0].fn();
assert.deepEqual(redirects, ['/login'], 'idle page navigates at the provider expiry');
redirects.length = 0;
expiryListeners.visibilitychange();
assert.deepEqual(redirects, ['/login'], 'resuming a suspended expired tab navigates immediately');
