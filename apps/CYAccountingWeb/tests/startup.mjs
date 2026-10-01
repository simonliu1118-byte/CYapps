import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const HERE = path.dirname(fileURLToPath(import.meta.url));
const ROOT = path.resolve(HERE, '..');
const read = relative => fs.readFileSync(path.join(ROOT, relative), 'utf8');

const html = read('public/index.html');
const auth = read('public/auth.js');
const app = read('public/app.js');
const ledger = read('public/ledger-tools.js');
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
assert.match(auth, /window\.__CYACC_BOOT_USER__/);
assert.match(auth, /bootUser \? Promise\.resolve\(bootUser\) : checkSessionFallback\(\)/);
assert.match(auth, /AbortController/);
assert.match(auth, /8_000/);
assert.match(app, /startCyaccApp/);
assert.match(app, /if \(window\.cyaccSessionPromise\) await window\.cyaccSessionPromise/);
assert.match(app, /finishCyaccBoot/);
assert.match(app, /12_000/);
assert.match(ledger, /cyacc:core-ready/);
assert.equal((auth.match(/\/api\/auth\/me/g) || []).length, 1);
assert.doesNotMatch(features, /\/api\/auth\/me/);
assert.match(features, /mobileMainNav/);
assert.match(features, /mobileAccountMenuButton/);
assert.match(features, /mobileLedgerMoreButton/);

console.log('Deterministic startup checks passed.');
