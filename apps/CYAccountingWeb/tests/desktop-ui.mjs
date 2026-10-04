import assert from 'node:assert/strict';
import fs from 'node:fs';
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
const quick = read('public/quick-entry.js');
const category = read('public/category-management.js');
const exportUi = read('public/excel-export-ui.js');
const inlineEdit = read('public/ledger-inline-edit.js');
const adaptive = read('public/adaptive-ui.js');
const worker = read('src/app.js');
const accountingTools = read('src/accounting-tools.js');

assert.equal(version, '0.22.3');
assert.equal(build, '3');
assert.match(css, /@media \(min-width: 1360px\)/);
assert.match(css, /grid-template-columns:\s*minmax\(380px, 420px\) minmax\(0, 1fr\)/);
assert.match(css, /\.current-user\.role-super-admin/);
assert.match(css, /\.cy-confirm-dialog/);
assert.match(css, /\.settings-manager-dialog/);
assert.match(css, /\.v0214-balance-popover/);
assert.match(css, /\.opening-modal\s*\{[\s\S]*?width:\s*min\(300px, calc\(100vw - 28px\)\) !important;/);

assert.match(quick, /enterStep\(els\.txDate, \(\) => els\.summary\?\.focus\(\)\)/);
assert.match(quick, /enterStep\(els\.summary,[\s\S]*?els\.amount\?\.focus\(\)/);
assert.match(quick, /enterStep\(els\.amount,[\s\S]*?els\.form\.requestSubmit\(\)/);
assert.match(category, /data-category-transfer/);
assert.match(exportUi, /button\.textContent = '匯出中…'/);
assert.match(exportUi, /aria-busy/);
assert.match(exportUi, /navigator\.share/);
assert.match(exportUi, /navigator\.canShare/);
assert.match(inlineEdit, /beginInlineLedgerEdit/);
assert.match(adaptive, /window\.cyConfirm = options => new Promise/);
assert.match(adaptive, /renderSettingsCategoryManager/);
assert.match(adaptive, /handleV0214AccountDefault/);
assert.match(adaptive, /handleV0214FavoriteToggle/);
assert.match(accountingTools, /url\.pathname === '\/api\/accounts\/reorder'/);
assert.match(accountingTools, /url\.pathname === '\/api\/category-groups\/reorder'/);
assert.match(accountingTools, /url\.pathname === '\/api\/categories\/reorder'/);
assert.match(worker, /SUMMARY_MAX_UNITS = 40/);
assert.match(worker, /SUMMARY_TOO_LONG/);

console.log('Desktop UI regression checks passed.');
