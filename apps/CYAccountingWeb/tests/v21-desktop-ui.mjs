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
const build1Css = read('public/v021b1.css');
const build2Css = read('public/v021b2.css');
const js = read('public/v021.js');
const v07 = read('public/v07.js');
const v011 = read('public/v011.js');

assert.equal(version, '0.21.0');
assert.equal(build, '2');
assert.match(html, /href="\/v021\.css"/);
assert.match(html, /src="\/v021\.js"/);
assert.ok(html.indexOf('/v021.css') > html.indexOf('/v020.css'), 'v021.css must load after v020.css');
assert.ok(html.indexOf('/v021.js') > html.indexOf('/v020.js'), 'v021.js must load after v020.js');
assert.match(js, /CY_V21_VERSION = 'V0\.21\.0 Build 2'/);
assert.match(js, /ensureV21Build1Stylesheet\(\)/);
assert.match(js, /ensureV21Build2Stylesheet\(\)/);
assert.match(js, /link\.href = '\/v021b1\.css'/);
assert.match(js, /link\.href = '\/v021b2\.css'/);

// Base Desktop redesign remains isolated from Tablet/Mobile.
assert.match(css, /@media \(min-width: 1024px\)/);
assert.doesNotMatch(css, /@media \(max-width:/);

// Modern business surfaces remain in the base V0.21 layer.
assert.match(css, /\.topbar\s*\{[\s\S]*?backdrop-filter:\s*blur\(12px\)/);
assert.match(css, /\.card\s*\{[\s\S]*?border-radius:\s*14px;[\s\S]*?box-shadow:/);
assert.match(css, /\.ledger-desktop-tools\s*\{[\s\S]*?background:\s*#fafbfd;/);
assert.match(css, /tbody tr:hover td\s*\{[\s\S]*?background:\s*#f8fbff;/);
assert.match(css, /\.settings-tab\.active\s*\{[\s\S]*?background:\s*#fff;[\s\S]*?box-shadow:/);
assert.match(css, /\.modal\s*\{[\s\S]*?border-radius:\s*16px;[\s\S]*?box-shadow:/);

// Build 1: wide Desktop becomes left quick-entry + right ledger, while narrower Desktop keeps V0.21 stacked layout.
assert.match(build1Css, /@media \(min-width: 1360px\)/);
assert.match(build1Css, /\.shell\.v21-split-layout\s*\{[\s\S]*?grid-template-columns:\s*minmax\(380px, 420px\) minmax\(0, 1fr\)/);
assert.match(build1Css, /\.v21-entry-rail\s*\{[\s\S]*?position:\s*sticky;[\s\S]*?top:\s*88px;/);
assert.match(js, /CY_V21_SPLIT_MEDIA = '\(min-width: 1360px\)'/);
assert.match(js, /document\.createElement\('aside'\)/);
assert.match(js, /rail\.prepend\(entry\)/);
assert.match(js, /rail\.append\(confirmation\)/);
assert.match(js, /document\.body\.append\(confirmation\)/);
assert.match(js, /setConfirmationDrawer\(true, false\)/);

// Approved slider itself remains untouched; the entry workspace tint returns for fast income/expense recognition.
assert.doesNotMatch(css, /\.entry-kind-switch/);
assert.doesNotMatch(css, /\.entry-kind-switch-field/);
assert.doesNotMatch(build1Css, /\.entry-kind-switch/);
assert.doesNotMatch(build1Css, /\.entry-kind-switch-field/);
assert.doesNotMatch(build2Css, /\.entry-kind-switch/);
assert.doesNotMatch(build2Css, /\.entry-kind-switch-field/);
assert.match(build1Css, /\.entry-card\.entry-income\s*\{[\s\S]*?background:\s*#f4fbf6;/);
assert.match(build1Css, /\.entry-card\.entry-expense\s*\{[\s\S]*?background:\s*#fff6f5;/);

// Input confirmation is inline below entry on wide Desktop and the right-edge drawer controls disappear there.
assert.match(build1Css, /\.confirmation-drawer\.v21-inline-confirmation\s*\{[\s\S]*?position:\s*relative;/);
assert.match(build1Css, /body\.v21-wide-split > \.confirmation-edge-open/);
assert.match(build1Css, /\.v21-inline-confirmation \.confirmation-list\s*\{[\s\S]*?overflow-y:\s*auto;/);

// Build 2: desktop buttons have clear primary/secondary/ghost/icon hierarchy without changing behavior.
assert.match(build2Css, /@media \(min-width: 1024px\)/);
assert.doesNotMatch(build2Css, /@media \(max-width:/);
assert.match(build2Css, /\.entry-card #saveButton\s*\{[\s\S]*?width:\s*126px;[\s\S]*?background:\s*#3569a8;[\s\S]*?box-shadow:\s*none;/);
assert.match(build2Css, /#ledgerPrevMonth,[\s\S]*?#ledgerNextMonth\s*\{[\s\S]*?width:\s*32px;[\s\S]*?background:\s*#f3f5f7;/);
assert.match(build2Css, /#ledgerOpeningBalanceButton,[\s\S]*?#ledgerGroupToggle\s*\{[\s\S]*?background:\s*#f8f9fb;/);
assert.match(build2Css, /#ledgerSearchForm button\[type="submit"\]\s*\{[\s\S]*?background:\s*#edf3f9;/);
assert.match(build2Css, /#ledgerSearchClear\s*\{[\s\S]*?background:\s*transparent;/);
assert.match(build2Css, /\.topbar \.secondary\.compact\s*\{[\s\S]*?border:\s*1px solid transparent;[\s\S]*?background:\s*#f6f8fa;/);

// Shortcut remains plain Tab inside the entry form only. Shift+Tab and other site dialogs retain native focus navigation.
assert.match(v07, /els\.form\?\.addEventListener\('keydown'/);
assert.match(v07, /event\.key !== 'Tab'/);
assert.match(v07, /event\.shiftKey/);
assert.match(v07, /event\.target\.matches\('input, select'\)/);
assert.doesNotMatch(v07, /event\.key !== 'F2'/);
assert.match(v011, /<kbd>Tab<\/kbd> 切換收入／支出/);
assert.doesNotMatch(v011, /<kbd>F2<\/kbd> 切換收入／支出/);
assert.match(js, /<kbd>Tab<\/kbd> 切換收入／支出/);

console.log('V0.21.0 Build 2 desktop button hierarchy regression tests passed.');
