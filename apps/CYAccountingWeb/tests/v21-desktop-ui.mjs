import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const HERE = path.dirname(fileURLToPath(import.meta.url));
const ROOT = path.resolve(HERE, '..');
const read = relative => fs.readFileSync(path.join(ROOT, relative), 'utf8');

const version = read('VERSION').trim();
const build = read('BUILD').trim();
const html = read('public/index.html');
const css = read('public/v021.css');
const js = read('public/v021.js');
const v07 = read('public/v07.js');
const v011 = read('public/v011.js');

assert.equal(version, '0.21.0');
assert.equal(build, '0');
assert.match(html, /href="\/v021\.css"/);
assert.match(html, /src="\/v021\.js"/);
assert.ok(html.indexOf('/v021.css') > html.indexOf('/v020.css'), 'v021.css must load after v020.css');
assert.ok(html.indexOf('/v021.js') > html.indexOf('/v020.js'), 'v021.js must load after v020.js');
assert.match(html, /V0\.21\.0/);
assert.match(js, /CY_V21_VERSION = 'V0\.21\.0'/);

// Desktop redesign must stay isolated to >=1024px so Tablet/Mobile adaptive layers remain intact.
assert.match(css, /@media \(min-width: 1024px\)/);
assert.doesNotMatch(css, /@media \(max-width:/);

// Modern business surfaces: lighter shell, elevated cards, modern toolbar/table and settings modal.
assert.match(css, /\.topbar\s*\{[\s\S]*?backdrop-filter:\s*blur\(12px\)/);
assert.match(css, /\.card\s*\{[\s\S]*?border-radius:\s*14px;[\s\S]*?box-shadow:/);
assert.match(css, /\.ledger-desktop-tools\s*\{[\s\S]*?background:\s*#fafbfd;/);
assert.match(css, /tbody tr:hover td\s*\{[\s\S]*?background:\s*#f8fbff;/);
assert.match(css, /\.settings-tab\.active\s*\{[\s\S]*?background:\s*#fff;[\s\S]*?box-shadow:/);
assert.match(css, /\.modal\s*\{[\s\S]*?border-radius:\s*16px;[\s\S]*?box-shadow:/);

// Income/expense segmented control is user-approved and V0.21 must not override its selectors.
assert.doesNotMatch(css, /\.entry-kind-switch/);
assert.doesNotMatch(css, /\.entry-kind-switch-field/);

// Shortcut is now plain Tab inside the entry form only. Shift+Tab and other site dialogs retain native focus navigation.
assert.match(v07, /els\.form\?\.addEventListener\('keydown'/);
assert.match(v07, /event\.key !== 'Tab'/);
assert.match(v07, /event\.shiftKey/);
assert.match(v07, /event\.target\.matches\('input, select'\)/);
assert.doesNotMatch(v07, /event\.key !== 'F2'/);
assert.match(v011, /<kbd>Tab<\/kbd> 切換收入／支出/);
assert.doesNotMatch(v011, /<kbd>F2<\/kbd> 切換收入／支出/);
assert.match(js, /<kbd>Tab<\/kbd> 切換收入／支出/);

console.log('V0.21 desktop business UI and Tab shortcut regression tests passed.');
