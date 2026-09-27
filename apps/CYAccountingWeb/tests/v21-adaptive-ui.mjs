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
const v20Css = read('public/v020.css');
const v201Css = read('public/v0201.css');

assert.equal(build, '9');
assert.match(build8Js, /CY_V21_BUILD8_VERSION = 'V0\.21\.0 Build 9'/);
assert.match(build8Js, /ensureV21Build9Stylesheet\(\)/);
assert.match(build8Js, /link\.href = '\/v021b9\.css'/);
assert.doesNotThrow(() => new Function(build8Js), 'current V0.21 adaptive JavaScript must parse');

// User-approved Enter workflow is date -> summary -> amount -> save, then focus returns to summary.
assert.match(v03, /enterStep\(els\.txDate, \(\) => els\.summary\?\.focus\(\)\)/);
assert.match(v03, /enterStep\(els\.summary,[\s\S]*?els\.amount\?\.focus\(\)/);
assert.match(v03, /enterStep\(els\.amount,[\s\S]*?els\.form\.requestSubmit\(\)/);
assert.doesNotMatch(v03, /const flow = \[els\.txDate, els\.accountName, els\.categoryName/);
assert.match(v03, /cyFocusSummaryAfterSave[\s\S]*?els\.summary\.focus\(\)/);
assert.match(build8Js, /日期 Enter → 摘要 Enter → 金額 Enter 儲存 → 回摘要/);
assert.match(build8Js, /date\.setAttribute\('enterkeyhint', 'next'\)/);
assert.match(build8Js, /summary\.setAttribute\('enterkeyhint', 'next'\)/);
assert.match(build8Js, /amount\.setAttribute\('enterkeyhint', 'done'\)/);

// Account buttons remain the canonical fast selector while Mobile collapses them behind one trigger.
assert.match(build8Js, /row\.hidden = false/);
assert.match(build8Js, /select\.dispatchEvent\(new Event\('change', \{ bubbles: true \}\)\)/);
assert.match(build8Js, /trigger\.id = 'entryAccountPickerButton'/);
assert.match(build8Js, /setV21Build9AccountPickerOpen/);
assert.match(build8Js, /mobile-picker-open/);
assert.match(build8Js, /entry-account-picker-value/);
assert.match(build9Css, /\.entry-account-picker-trigger\s*\{[\s\S]*?display:\s*flex;/);
assert.match(build9Css, /\.entry-account-buttons\s*\{[\s\S]*?position:\s*absolute;[\s\S]*?display:\s*none;/);
assert.match(build9Css, /\.entry-account-choice-row\.mobile-picker-open \.entry-account-buttons\s*\{[\s\S]*?display:\s*grid;/);
assert.doesNotMatch(build9Css, /\.entry-account-buttons\s*\{[\s\S]*?overflow-x:\s*auto;/);

// Mobile is split into two explicit task pages instead of stacking entry and ledger vertically.
assert.match(build8Js, /setupV21Build9MobilePages\(\)/);
assert.match(build8Js, /nav\.id = 'mobileMainNav'/);
assert.match(build8Js, /data-mobile-page="entry"/);
assert.match(build8Js, /data-mobile-page="ledger"/);
assert.match(build8Js, /v21-mobile-page-hidden/);
assert.match(build9Css, /\.v21-mobile-main-nav\s*\{[\s\S]*?grid-template-columns:\s*repeat\(2, minmax\(0, 1fr\)\);/);
assert.match(build9Css, /\.shell > \.v21-mobile-page-hidden\s*\{[\s\S]*?display:\s*none !important;/);

// Tablet gets a compact two-row quick-entry workspace and keeps the table-oriented ledger.
assert.match(build9Css, /@media \(min-width: 768px\) and \(max-width: 1023px\)/);
assert.match(build9Css, /\.entry-grid\s*\{[\s\S]*?grid-template-columns:\s*minmax\(0, 1\.3fr\) minmax\(140px, \.8fr\) 112px;/);
assert.match(build9Css, /\.entry-grid > \.account-source-field\s*\{[\s\S]*?display:\s*none;/);
assert.match(build9Css, /\.entry-grid > \.summary-field\s*\{[\s\S]*?grid-row:\s*2;/);
assert.match(build9Css, /\.ledger-card td\s*\{[\s\S]*?font-size:\s*13\.5px;/);
const tabletCss = build9Css.match(/@media \(min-width: 768px\) and \(max-width: 1023px\)[\s\S]*?(?=\/\* Mobile:)/)?.[0] || '';
assert.doesNotMatch(tabletCss, /\.ledger-card table\s*,[\s\S]*?display:\s*block/);

// Mobile quick entry remains compact: date/category, summary, then amount/save.
assert.match(build9Css, /@media \(max-width: 767px\)/);
assert.match(build9Css, /\.entry-grid\s*\{[\s\S]*?grid-template-columns:\s*minmax\(0, \.92fr\) minmax\(0, 1\.08fr\);/);
assert.match(build9Css, /\.entry-grid > \.date-field\s*\{[\s\S]*?grid-row:\s*1;/);
assert.match(build9Css, /\.entry-grid > \.category-field\s*\{[\s\S]*?grid-column:\s*2;[\s\S]*?grid-row:\s*1;/);
assert.match(build9Css, /\.entry-grid > \.summary-field\s*\{[\s\S]*?grid-column:\s*1 \/ -1;[\s\S]*?grid-row:\s*2;/);
assert.match(build9Css, /\.entry-grid > \.amount-field\s*\{[\s\S]*?grid-row:\s*3;/);
assert.match(build9Css, /\.entry-grid > #saveButton\s*\{[\s\S]*?grid-column:\s*2;[\s\S]*?grid-row:\s*3;/);
assert.match(build9Css, /\.entry-grid input,[\s\S]*?font-size:\s*16px;/);

// Existing mobile ledger card behavior remains active and Build 9 only refines it.
assert.match(v20Css, /@media \(max-width: 767px\)[\s\S]*?\.ledger-card table,[\s\S]*?display:\s*block;/);
assert.match(v201Css, /tbody > tr:not\(\.account-group-row\)\s*\{[\s\S]*?display:\s*grid;/);
assert.match(build9Css, /\.ledger-card tbody > tr:not\(\.account-group-row\) > td:nth-child\(6\)\s*\{[\s\S]*?font-size:\s*17px;/);

// Mobile/Tablet header keeps role identity compact and hides healthy connection noise.
assert.match(build9Css, /\.v21-brand-line \.status\.ok\s*\{[\s\S]*?display:\s*none;/);
assert.match(build9Css, /\.current-user\.role-super-admin\s*\{[\s\S]*?#fff4d6/);
assert.match(build9Css, /\.current-user\.role-admin\s*\{[\s\S]*?#f8ebe1/);
assert.match(build9Css, /\.v21-account-cluster\s*\{[\s\S]*?overflow:\s*hidden;/);

// Approved mode tints remain available on touch layouts.
assert.match(build9Css, /\.entry-card\.entry-income\s*\{[\s\S]*?background:\s*#f4fbf6;/);
assert.match(build9Css, /\.entry-card\.entry-expense\s*\{[\s\S]*?background:\s*#fff6f5;/);

console.log('V0.21.0 Build 9 Tablet/Mobile adaptive UI regression tests passed.');
