import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const HERE = path.dirname(fileURLToPath(import.meta.url));
const ROOT = path.resolve(HERE, '..');
const PUBLIC = path.join(ROOT, 'public');
const SRC = path.join(ROOT, 'src');
const TESTS = path.join(ROOT, 'tests');
const read = relative => fs.readFileSync(path.join(ROOT, relative), 'utf8');

const html = read('public/index.html');
const adaptiveUi = read('public/adaptive-ui.js');
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
assert.deepEqual(fs.readdirSync(SRC).filter(name => /^(?:app-v|v[0-9])/.test(name)), [], 'versioned backend wrappers/modules must not exist');
assert.deepEqual(fs.readdirSync(TESTS).filter(name => /^v[0-9].*\.mjs$/.test(name)), [], 'version-named regression tests must not exist');
assert.equal(files.includes('app-baseline.js'), false);
assert.equal(files.includes('app-baseline.css'), false);

let previous = -1;
for (const name of styles) {
  assert.equal(files.includes(name), true, name + ' must exist');
  const marker = 'href="/' + name;
  const at = html.indexOf(marker);
  assert.ok(at > previous, name + ' must load in semantic cascade order');
  previous = at;
}
previous = -1;
for (const name of scripts) {
  assert.equal(files.includes(name), true, name + ' must exist');
  const marker = 'src="/' + name;
  const at = html.indexOf(marker);
  assert.ok(at > previous, name + ' must load in semantic runtime order');
  previous = at;
  assert.doesNotThrow(() => new Function(read('public/' + name)), name + ' must parse');
}

const runtime = scripts.map(name => read('public/' + name)).join('\n');
assert.doesNotMatch(runtime, /__CYACC_BASELINE_BUNDLE__/);
assert.doesNotMatch(runtime, /script\.src\s*=\s*['"]\/v[0-9]/);
assert.doesNotMatch(runtime, /link\.href\s*=\s*['"]\/v[0-9]/);
assert.doesNotMatch(runtime, /querySelector\(['"]\.version['"]\)/, 'frontend feature modules must not own the global version element');
assert.doesNotMatch(runtime, /V0\.\d+\.\d+(?: Build \d+)?/, 'frontend feature modules must not embed application version strings');

assert.match(read('public/quick-entry-settings.js'), /setupQuickEntrySettingsPane/);
assert.match(read('public/category-management.js'), /setupCategoryTransfer/);
assert.match(read('public/excel-export-ui.js'), /downloadMonthlyExcel/);
assert.match(read('public/ledger-inline-edit.js'), /beginInlineLedgerEdit/);
assert.match(read('public/excel-import-ui.js'), /setupExcelImport/);
assert.match(read('public/backup-ui.js'), /setupBackupSettings/);

for (const name of [
  'quick-entry-settings.js',
  'category-management.js',
  'excel-export-ui.js',
  'ledger-inline-edit.js',
  'excel-import-ui.js',
  'backup-ui.js'
]) {
  const source = read('public/' + name);
  assert.doesNotMatch(source, /(?:V|v)(?:11|12|13|14|15|16|17|18|181)(?=[A-Za-z0-9_-])/, name + ' must use functional internal identifiers');
}
assert.doesNotMatch(read('public/backup-ui.js'), /V0\.18\.[01]/, 'backup UI must not own historical app version display');
assert.match(html, /<span class="version">V0\.21\.7 Build 6<\/span>/, 'index.html must own the current visible version');
assert.doesNotMatch(adaptiveUi, /querySelector\(['"]\.version['"]\)/, 'adaptive UI must not mutate the global version element');
assert.doesNotMatch(adaptiveUi, /\bCY_[A-Z0-9_]*VERSION\b|\b(?:sync|enforce)[A-Za-z0-9_]*Version\b/, 'historical version mutators must not return');
assert.doesNotMatch(adaptiveUi, /V0\.2[01]\.[0-9]+(?: Build [0-9]+)?/, 'adaptive UI must not embed historical application version strings');

console.log('Semantic frontend architecture checks passed.');
