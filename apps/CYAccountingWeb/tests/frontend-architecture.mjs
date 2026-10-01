import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const HERE = path.dirname(fileURLToPath(import.meta.url));
const ROOT = path.resolve(HERE, '..');
const PUBLIC = path.join(ROOT, 'public');
const read = relative => fs.readFileSync(path.join(ROOT, relative), 'utf8');

const html = read('public/index.html');
const files = fs.readdirSync(PUBLIC);
const styles = [
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
];
const scripts = [
  'auth.js',
  'app.js',
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
];

assert.deepEqual(files.filter(name => /^v[0-9].*\.(?:js|css)$/.test(name)), [], 'versioned frontend assets must not exist');
assert.equal(files.includes('app-baseline.js'), false);
assert.equal(files.includes('app-baseline.css'), false);

let previous = -1;
for (const name of styles) {
  assert.equal(files.includes(name), true, name + ' must exist');
  const marker = 'href="/' + name + '?rev=semantic1"';
  const at = html.indexOf(marker);
  assert.ok(at > previous, name + ' must load in semantic cascade order');
  previous = at;
}
previous = -1;
for (const name of scripts) {
  assert.equal(files.includes(name), true, name + ' must exist');
  const marker = 'src="/' + name + '?rev=semantic1"';
  const at = html.indexOf(marker);
  assert.ok(at > previous, name + ' must load in semantic runtime order');
  previous = at;
  assert.doesNotThrow(() => new Function(read('public/' + name)), name + ' must parse');
}

const runtime = scripts.map(name => read('public/' + name)).join('\n');
assert.doesNotMatch(runtime, /__CYACC_BASELINE_BUNDLE__/);
assert.doesNotMatch(runtime, /script\.src\s*=\s*['"]\/v[0-9]/);
assert.doesNotMatch(runtime, /link\.href\s*=\s*['"]\/v[0-9]/);

assert.match(read('public/quick-entry-settings.js'), /setupQuickEntrySettingsPane/);
assert.match(read('public/category-management.js'), /setupCategoryTransfer/);
assert.match(read('public/excel-export-ui.js'), /downloadMonthlyExcel/);
assert.match(read('public/ledger-inline-edit.js'), /beginInlineLedgerEdit/);
assert.match(read('public/excel-import-ui.js'), /setupExcelImportV15/);
assert.match(read('public/backup-ui.js'), /setupBackupSettingsV17/);

console.log('Semantic frontend architecture checks passed.');
