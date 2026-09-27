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
const v20Css = read('public/v020.css');
const v201Css = read('public/v0201.css');

assert.equal(build, '11');
assert.match(html, /src="\/v021b10\.js"/);
assert.match(html, /src="\/v021b11\.js"/);
assert.match(build10Js, /CY_V21_BUILD10_VERSION = 'V0\.21\.0 Build 10'/);
assert.match(build10Js, /CY_V21_BUILD10_MOBILE = '\(max-width: 767px\)'/);
assert.match(build10Js, /link\.href = '\/v021b10\.css'/);
assert.match(build11Js, /CY_V21_BUILD11_VERSION = 'V0\.21\.0 Build 11'/);
assert.match(build11Js, /CY_V21_BUILD11_DESKTOP = '\(min-width: 768px\)'/);
assert.match(build11Js, /link\.href = '\/v021b11\.css'/);
assert.doesNotThrow(() => new Function(build8Js), 'Build 8/9 adaptive JavaScript must parse');
assert.doesNotThrow(() => new Function(build10Js), 'Build 10 mobile JavaScript must parse');
assert.doesNotThrow(() => new Function(build11Js), 'Build 11 isolation JavaScript must parse');

// User-approved Enter workflow remains date -> summary -> amount -> save, then focus returns to summary.
assert.match(v03, /enterStep\(els\.txDate, \(\) => els\.summary\?\.focus\(\)\)/);
assert.match(v03, /enterStep\(els\.summary,[\s\S]*?els\.amount\?\.focus\(\)/);
assert.match(v03, /enterStep\(els\.amount,[\s\S]*?els\.form\.requestSubmit\(\)/);
assert.doesNotMatch(v03, /const flow = \[els\.txDate, els\.accountName, els\.categoryName/);
assert.match(v03, /cyFocusSummaryAfterSave[\s\S]*?els\.summary\.focus\(\)/);
assert.match(build8Js, /日期 Enter → 摘要 Enter → 金額 Enter 儲存 → 回摘要/);

// Build 10 remains deliberately phone-only. Build 11 must not overwrite phone styles.
assert.match(build10Css, /@media \(max-width: 767px\)/);
assert.doesNotMatch(build10Css, /@media \(min-width: 768px\)/);
assert.match(build11Css, /@media \(min-width: 768px\)/);
assert.doesNotMatch(build11Css, /@media \(max-width:/);
assert.match(build9Css, /@media \(min-width: 768px\) and \(max-width: 1023px\)/);
const tabletCss = build9Css.match(/@media \(min-width: 768px\) and \(max-width: 1023px\)[\s\S]*?(?=\/\* Mobile:)/)?.[0] || '';
assert.match(tabletCss, /\.entry-grid\s*\{[\s\S]*?grid-template-columns:\s*minmax\(0, 1\.3fr\) minmax\(140px, \.8fr\) 112px;/);
assert.doesNotMatch(tabletCss, /\.ledger-card table\s*,[\s\S]*?display:\s*block/);

// Mobile remains two explicit task pages, but Build 10 turns the selector into bottom task navigation.
assert.match(build8Js, /setupV21Build9MobilePages\(\)/);
assert.match(build8Js, /nav\.id = 'mobileMainNav'/);
assert.match(build8Js, /data-mobile-page="entry"/);
assert.match(build8Js, /data-mobile-page="ledger"/);
assert.match(build8Js, /v21-mobile-page-hidden/);
assert.match(build10Js, /v21-mobile-bottom-nav/);
assert.match(build10Js, /document\.body\.append\(nav\)/);
assert.match(build10Css, /\.v21-mobile-main-nav\.v21-mobile-bottom-nav\s*\{[\s\S]*?position:\s*fixed !important;[\s\S]*?bottom:\s*0;/);
assert.match(build10Css, /body\.v21-mobile-app\s*\{[\s\S]*?padding-bottom:/);

// Mobile header is an app bar with one compact identity entry; Desktop header controls are not squeezed into the phone bar.
assert.match(build10Js, /trigger\.id = 'mobileAccountMenuButton'/);
assert.match(build10Js, /menu\.id = 'mobileAccountMenu'/);
assert.match(build10Js, /data-mobile-account-action="settings"/);
assert.match(build10Js, /data-mobile-account-action="logout"/);
assert.match(build10Css, /\.topbar\s*\{[\s\S]*?grid-template-columns:\s*minmax\(0, 1fr\) auto !important;/);
assert.match(build10Css, /\.topbar-actions\s*\{[\s\S]*?display:\s*none !important;/);
assert.match(build10Css, /\.v21-mobile-account-menu-button\.role-super-admin\s*\{[\s\S]*?#fff4d6/);
assert.match(build10Css, /\.v21-mobile-account-menu-button\.role-admin\s*\{[\s\S]*?#f7e8dc/);
assert.match(build10Css, /\.v21-brand-line \.status\.ok[\s\S]*?display:\s*none !important;/);

// Account selection is one row on the form and opens a real bottom sheet; it is not a horizontal scrolling button strip.
assert.match(build8Js, /trigger\.id = 'entryAccountPickerButton'/);
assert.match(build8Js, /setV21Build9AccountPickerOpen/);
assert.match(build10Js, /mobileAccountSheetBackdrop/);
assert.match(build10Css, /\.entry-account-buttons\s*\{[\s\S]*?position:\s*fixed !important;[\s\S]*?bottom:\s*0 !important;/);
assert.match(build10Css, /\.entry-account-choice-row\.mobile-picker-open \.entry-account-buttons\s*\{[\s\S]*?display:\s*grid !important;/);
assert.doesNotMatch(build10Css, /\.entry-account-buttons\s*\{[\s\S]*?overflow-x:\s*auto/);

// The phone entry page is a list-form, not a compressed desktop grid. Native date/select controls never share a row.
assert.match(build10Css, /\.entry-grid\s*\{[\s\S]*?grid-template-columns:\s*1fr !important;/);
assert.match(build10Css, /\.entry-grid > \.date-field,[\s\S]*?grid-column:\s*1 !important;[\s\S]*?grid-row:\s*auto !important;/);
assert.match(build10Css, /\.entry-grid input,[\s\S]*?font-size:\s*16px !important;/);
assert.match(build10Css, /\.entry-grid > #saveButton\s*\{[\s\S]*?width:\s*100% !important;[\s\S]*?height:\s*48px !important;/);
assert.match(build10Css, /\.entry-kind-switch\s*\{[\s\S]*?grid-template-columns:\s*repeat\(2, minmax\(0, 1fr\)\) !important;/);
assert.match(build10Css, /\.entry-card \.section-title h2,[\s\S]*?display:\s*none !important;/);

// Recently-entered confirmation is a desktop/tablet workspace, not a third phone panel or floating bubble.
assert.match(build10Js, /setConfirmationDrawer\(false, false\)/);
assert.match(build10Css, /#confirmationToggle,[\s\S]*?\.confirmation-edge-open,[\s\S]*?#inputConfirmationCard\s*\{[\s\S]*?display:\s*none !important;/);

// Ledger keeps mobile transaction cards but moves low-frequency month tools behind one More sheet.
assert.match(v20Css, /@media \(max-width: 767px\)[\s\S]*?\.ledger-card table,[\s\S]*?display:\s*block;/);
assert.match(v201Css, /tbody > tr:not\(\.account-group-row\)\s*\{[\s\S]*?display:\s*grid;/);
assert.match(build10Js, /button\.id = 'mobileLedgerMoreButton'/);
assert.match(build10Js, /sheet\.id = 'mobileLedgerToolsSheet'/);
assert.match(build10Js, /data-mobile-ledger-action="opening"/);
assert.match(build10Js, /data-mobile-ledger-action="lock"/);
assert.match(build10Js, /data-mobile-ledger-action="export"/);
assert.match(build10Css, /\.v21-summary-actions,[\s\S]*?\.ledger-view-tools\s*\{[\s\S]*?display:\s*none !important;/);
assert.match(build10Css, /\.v21-mobile-tools-sheet\s*\{[\s\S]*?position:\s*fixed;[\s\S]*?bottom:\s*0;/);

console.log('V0.21.0 Build 11 adaptive regression passed; Build 10 mobile-first UI remains intact.');
