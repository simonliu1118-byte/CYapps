import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const HERE = path.dirname(fileURLToPath(import.meta.url));
const ROOT = path.resolve(HERE, '..');
const read = relative => fs.readFileSync(path.join(ROOT, relative), 'utf8');

const build = read('BUILD').trim();
const v03 = read('public/v03.js');
const build8Js = read('public/v021b8.js');
const build9Css = read('public/v021b9.css');
const build10Js = read('public/v021b10.js');
const build10Css = read('public/v021b10.css');
const build11Js = read('public/v021b11.js');
const build11Css = read('public/v021b11.css');
const build12Js = read('public/v021b12.js');
const build12Css = read('public/v021b12.css');
const build13Js = read('public/v021b13.js');
const build13Css = read('public/v021b13.css');
const build14Js = read('public/v021b14.js');
const build14Css = read('public/v021b14.css');
const build15Js = read('public/v021b15.js');
const build15Css = read('public/v021b15.css');
const build16Js = read('public/v021b16.js');
const build16Css = read('public/v021b16.css');
const v20Css = read('public/v020.css');
const v201Css = read('public/v0201.css');

assert.equal(build, '16');
assert.match(build11Js, /ensureV21Build12Script\(\)/);
assert.match(build11Js, /ensureV21Build13Script\(\)/);
assert.match(build11Js, /ensureV21Build14Script\(\)/);
assert.match(build11Js, /ensureV21Build15Script\(\)/);
assert.match(build11Js, /ensureV21Build16Script\(\)/);
assert.match(build12Js, /CY_V21_BUILD12_DESKTOP = '\(min-width: 1024px\)'/);
assert.match(build13Js, /CY_V21_BUILD13_DESKTOP = '\(min-width: 1024px\)'/);
assert.match(build14Js, /CY_V21_BUILD14_DESKTOP = '\(min-width: 1024px\)'/);
assert.match(build15Js, /CY_V21_BUILD15_DESKTOP = '\(min-width: 1024px\)'/);
assert.match(build16Js, /CY_V21_BUILD16_DESKTOP = '\(min-width: 1024px\)'/);
for (const js of [build8Js, build10Js, build11Js, build12Js, build13Js, build14Js, build15Js, build16Js]) {
  assert.doesNotThrow(() => new Function(js), 'adaptive overlay JavaScript must parse');
}

assert.match(v03, /enterStep\(els\.txDate, \(\) => els\.summary\?\.focus\(\)\)/);
assert.match(v03, /enterStep\(els\.summary,[\s\S]*?els\.amount\?\.focus\(\)/);
assert.match(v03, /enterStep\(els\.amount,[\s\S]*?els\.form\.requestSubmit\(\)/);
assert.match(v03, /cyFocusSummaryAfterSave[\s\S]*?els\.summary\.focus\(\)/);

// Breakpoint ownership: Build 10 phone, Build 9 Tablet, Builds 12-16 Desktop.
assert.match(build10Css, /@media \(max-width: 767px\)/);
assert.doesNotMatch(build10Css, /@media \(min-width: 768px\)/);
assert.match(build9Css, /@media \(min-width: 768px\) and \(max-width: 1023px\)/);
assert.match(build11Css, /@media \(min-width: 768px\)/);
assert.match(build12Css, /@media \(min-width: 1024px\)/);
assert.match(build13Css, /@media \(min-width: 1024px\)/);
assert.match(build14Css, /@media \(min-width: 1024px\)/);
assert.match(build15Css, /@media \(min-width: 1024px\)/);
assert.match(build16Css, /@media \(min-width: 1024px\)/);
assert.doesNotMatch(build16Css, /@media \(max-width:/);

// Build 10 mobile-first direction remains unchanged.
assert.match(build8Js, /setupV21Build9MobilePages\(\)/);
assert.match(build8Js, /nav\.id = 'mobileMainNav'/);
assert.match(build10Js, /trigger\.id = 'mobileAccountMenuButton'/);
assert.match(build10Js, /menu\.id = 'mobileAccountMenu'/);
assert.match(build10Js, /button\.id = 'mobileLedgerMoreButton'/);
assert.match(build10Css, /\.v21-mobile-main-nav\.v21-mobile-bottom-nav\s*\{[\s\S]*?position:\s*fixed !important;[\s\S]*?bottom:\s*0;/);
assert.match(build10Css, /\.entry-grid\s*\{[\s\S]*?grid-template-columns:\s*1fr !important;/);

// Tablet stays table-oriented and does not inherit Build 16 Desktop-only controls.
const tabletCss = build9Css.match(/@media \(min-width: 768px\) and \(max-width: 1023px\)[\s\S]*?(?=\/\* Mobile:)/)?.[0] || '';
assert.match(tabletCss, /\.entry-grid\s*\{[\s\S]*?grid-template-columns:\s*minmax\(0, 1\.3fr\) minmax\(140px, \.8fr\) 112px;/);
assert.doesNotMatch(tabletCss, /\.ledger-card table\s*,[\s\S]*?display:\s*block/);
assert.match(build13Js, /if \(!window\.matchMedia\(CY_V21_BUILD13_DESKTOP\)\.matches\) return;/);
assert.match(build14Js, /if \(!media\.matches\) return;/);
assert.match(build15Js, /if \(!window\.matchMedia\(CY_V21_BUILD15_DESKTOP\)\.matches\) return;/);
assert.match(build16Js, /if \(!window\.matchMedia\(CY_V21_BUILD16_DESKTOP\)\.matches/);

// Build 16 remains Desktop-only and does not touch mobile navigation or mobile account picker code.
assert.match(build16Css, /\.entry-account-choice\s*\{/);
assert.match(build16Js, /setupV21Build16OptimisticDrag\(\)/);
assert.doesNotMatch(build16Js, /mobileMainNav|mobileAccountMenuButton|mobileLedgerMoreButton|entryAccountPickerButton/);

// Existing phone transaction-card ledger remains intact.
assert.match(v20Css, /@media \(max-width: 767px\)[\s\S]*?\.ledger-card table,[\s\S]*?display:\s*block;/);
assert.match(v201Css, /tbody > tr:not\(\.account-group-row\)\s*\{[\s\S]*?display:\s*grid;/);

console.log('V0.21.0 Build 16 adaptive UI regression tests passed.');
