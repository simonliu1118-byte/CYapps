import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const HERE = path.dirname(fileURLToPath(import.meta.url));
const ROOT = path.resolve(HERE, '..');
const read = relative => fs.readFileSync(path.join(ROOT, relative), 'utf8');

const build = read('BUILD').trim();
const html = read('public/index.html');
const v03 = read('public/v03.js');
const build8Js = read('public/v021b8.js');
const build9Css = read('public/v021b9.css');
const build10Js = read('public/v021b10.js');
const build10Css = read('public/v021b10.css');
const build11Js = read('public/v021b11.js');
const build11Css = read('public/v021b11.css');
const build12Js = read('public/v021b12.js');
const build12Css = read('public/v021b12.css');
const v20Css = read('public/v020.css');
const v201Css = read('public/v0201.css');

assert.equal(build, '12');
assert.match(html, /src="\/v021b10\.js"/);
assert.match(html, /src="\/v021b11\.js"/);
assert.match(build11Js, /ensureV21Build12Script\(\)/);
assert.match(build12Js, /CY_V21_BUILD12_DESKTOP = '\(min-width: 1024px\)'/);
assert.doesNotThrow(() => new Function(build8Js), 'Build 8/9 adaptive JavaScript must parse');
assert.doesNotThrow(() => new Function(build10Js), 'Build 10 mobile JavaScript must parse');
assert.doesNotThrow(() => new Function(build11Js), 'Build 11 isolation JavaScript must parse');
assert.doesNotThrow(() => new Function(build12Js), 'Build 12 Desktop JavaScript must parse');

// User-approved Enter workflow remains date -> summary -> amount -> save, then focus returns to summary.
assert.match(v03, /enterStep\(els\.txDate, \(\) => els\.summary\?\.focus\(\)\)/);
assert.match(v03, /enterStep\(els\.summary,[\s\S]*?els\.amount\?\.focus\(\)/);
assert.match(v03, /enterStep\(els\.amount,[\s\S]*?els\.form\.requestSubmit\(\)/);
assert.match(v03, /cyFocusSummaryAfterSave[\s\S]*?els\.summary\.focus\(\)/);

// Breakpoint ownership stays explicit: Build 10 phone, Build 9 Tablet, Build 12 Desktop.
assert.match(build10Css, /@media \(max-width: 767px\)/);
assert.doesNotMatch(build10Css, /@media \(min-width: 768px\)/);
assert.match(build9Css, /@media \(min-width: 768px\) and \(max-width: 1023px\)/);
assert.match(build11Css, /@media \(min-width: 768px\)/);
assert.doesNotMatch(build11Css, /@media \(max-width:/);
assert.match(build12Css, /@media \(min-width: 1024px\)/);
assert.doesNotMatch(build12Css, /@media \(max-width:/);

// Build 10 mobile-first direction remains unchanged.
assert.match(build8Js, /setupV21Build9MobilePages\(\)/);
assert.match(build8Js, /nav\.id = 'mobileMainNav'/);
assert.match(build8Js, /data-mobile-page="entry"/);
assert.match(build8Js, /data-mobile-page="ledger"/);
assert.match(build10Js, /v21-mobile-bottom-nav/);
assert.match(build10Js, /trigger\.id = 'mobileAccountMenuButton'/);
assert.match(build10Js, /menu\.id = 'mobileAccountMenu'/);
assert.match(build10Js, /mobileAccountSheetBackdrop/);
assert.match(build10Js, /button\.id = 'mobileLedgerMoreButton'/);
assert.match(build10Js, /sheet\.id = 'mobileLedgerToolsSheet'/);
assert.match(build10Css, /\.v21-mobile-main-nav\.v21-mobile-bottom-nav\s*\{[\s\S]*?position:\s*fixed !important;[\s\S]*?bottom:\s*0;/);
assert.match(build10Css, /\.entry-grid\s*\{[\s\S]*?grid-template-columns:\s*1fr !important;/);
assert.match(build10Css, /\.entry-grid > #saveButton\s*\{[\s\S]*?width:\s*100% !important;/);
assert.match(build10Css, /#confirmationToggle,[\s\S]*?#inputConfirmationCard\s*\{[\s\S]*?display:\s*none !important;/);
assert.match(build10Css, /\.v21-mobile-tools-sheet\s*\{[\s\S]*?position:\s*fixed;[\s\S]*?bottom:\s*0;/);

// Tablet remains table-oriented and does not inherit Build 12 Desktop custom picker/layout.
const tabletCss = build9Css.match(/@media \(min-width: 768px\) and \(max-width: 1023px\)[\s\S]*?(?=\/\* Mobile:)/)?.[0] || '';
assert.match(tabletCss, /\.entry-grid\s*\{[\s\S]*?grid-template-columns:\s*minmax\(0, 1\.3fr\) minmax\(140px, \.8fr\) 112px;/);
assert.doesNotMatch(tabletCss, /\.ledger-card table\s*,[\s\S]*?display:\s*block/);
assert.match(build12Js, /CY_V21_BUILD12_DESKTOP = '\(min-width: 1024px\)'/);
assert.match(build12Css, /\.v21-month-picker-custom\s*\{[\s\S]*?display:\s*none;/);

// Existing phone transaction-card ledger remains intact.
assert.match(v20Css, /@media \(max-width: 767px\)[\s\S]*?\.ledger-card table,[\s\S]*?display:\s*block;/);
assert.match(v201Css, /tbody > tr:not\(\.account-group-row\)\s*\{[\s\S]*?display:\s*grid;/);

console.log('V0.21.0 Build 12 adaptive UI regression tests passed.');
