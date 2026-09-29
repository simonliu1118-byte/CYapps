import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const HERE = path.dirname(fileURLToPath(import.meta.url));
const ROOT = path.resolve(HERE, '..');
const read = relative => fs.readFileSync(path.join(ROOT, relative), 'utf8');

const version = read('VERSION').trim();
const build = read('BUILD').trim();
const v03 = read('public/v03.js');
const authJs = read('public/auth.js');
const indexHtml = read('public/index.html');
const workerApp = read('src/app.js');
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
const patchJs = read('public/v0211.js');
const keyboardJs = read('public/v0211-keyboard.js');
const patchCss = read('public/v0211.css');
const patch2Js = read('public/v0212.js');
const patch2Css = read('public/v0212.css');
const patch4Js = read('public/v0214.js');
const patch4Css = read('public/v0214.css');
const patch5Css = read('public/v0215.css');
const patch5Build3Js = read('public/v0215b3.js');
const v06 = read('public/v06.js');
const v20Css = read('public/v020.css');
const v201Css = read('public/v0201.css');

assert.equal(version, '0.21.5');
assert.equal(build, '3');
assert.match(build11Js, /ensureV0211PatchScript\(\)/);
assert.match(build11Js, /ensureV0211KeyboardScript\(\)/);
assert.match(build11Js, /ensureV0212PatchScript\(\)/);
assert.match(build11Js, /script\.src = '\/v0212\.js'/);
assert.match(build11Js, /ensureV0215Stylesheet\(\)/);
assert.match(build11Js, /link\.href = '\/v0215\.css\?v=0215b3'/);
assert.match(build11Js, /ensureV0215Build3Script\(\)/);
assert.match(build11Js, /script\.src = '\/v0215b3\.js'/);
assert.match(build12Js, /CY_V21_BUILD12_DESKTOP = '\(min-width: 1024px\)'/);
assert.match(build13Js, /CY_V21_BUILD13_DESKTOP = '\(min-width: 1024px\)'/);
assert.match(build14Js, /CY_V21_BUILD14_DESKTOP = '\(min-width: 1024px\)'/);
assert.match(build15Js, /CY_V21_BUILD15_DESKTOP = '\(min-width: 1024px\)'/);
assert.match(build16Js, /CY_V21_BUILD16_DESKTOP = '\(min-width: 1024px\)'/);
assert.match(patchJs, /CY_V0211_DESKTOP = '\(min-width: 1024px\)'/);
assert.match(keyboardJs, /CY_V0211_KEYBOARD_DESKTOP = '\(min-width: 1024px\)'/);
assert.match(patch2Js, /CY_V0212_DESKTOP = '\(min-width: 1024px\)'/);
for (const js of [build8Js, build10Js, build11Js, build12Js, build13Js, build14Js, build15Js, build16Js, patchJs, keyboardJs, patch2Js, patch4Js, patch5Build3Js]) {
  assert.doesNotThrow(() => new Function(js), 'adaptive overlay JavaScript must parse');
}

assert.match(v03, /enterStep\(els\.txDate, \(\) => els\.summary\?\.focus\(\)\)/);
assert.match(v03, /enterStep\(els\.summary,[\s\S]*?els\.amount\?\.focus\(\)/);
assert.match(v03, /enterStep\(els\.amount,[\s\S]*?els\.form\.requestSubmit\(\)/);
assert.match(v03, /cyFocusSummaryAfterSave[\s\S]*?els\.summary\.focus\(\)/);
assert.match(keyboardJs, /if \(!window\.matchMedia\(CY_V0211_KEYBOARD_DESKTOP\)\.matches\) return;/);

// Recovery: login verifies the HttpOnly session before reload and critical JS uses a fresh URL.
assert.match(authJs, /sessionResponse = await fetch\('\/api\/auth\/me'/);
assert.match(authJs, /credentials: 'same-origin'/);
assert.match(authJs, /帳號密碼已通過，但登入狀態沒有保存/);
assert.match(indexHtml, /auth\.js\?v=0214b1-recovery/);
assert.match(indexHtml, /v021b11\.js\?v=0214b1-recovery/);
assert.match(workerApp, /no-store, no-cache, must-revalidate, max-age=0/);

// V0.21.5 Build 3 refines the mobile entry/ledger and adds delegated swipe actions without an observer.
assert.match(patch5Css, /@media \(max-width: 767px\)/);
assert.match(patch5Css, /\.entry-card\.entry-income\s*\{[\s\S]*?linear-gradient\(to right, rgba\(86, 176, 113/);
assert.match(patch5Css, /\.entry-card\.entry-expense\s*\{[\s\S]*?linear-gradient\(to right, rgba\(207, 104, 94/);
assert.match(patch5Css, /#favoriteCategoryGroup\.hidden[\s\S]*?display:\s*grid !important/);
assert.match(patch5Css, /#summarySuggestionGroup\.hidden[\s\S]*?display:\s*grid !important/);
assert.match(patch5Css, /\.quick-chip-list[\s\S]*?overflow-x:\s*auto !important/);
assert.match(patch5Css, /\.entry-grid input,[\s\S]*?text-align:\s*center !important/);
assert.match(patch5Css, /\.ledger-month-tools[\s\S]*?grid-template-columns:\s*34px minmax\(0, 1fr\) 34px/);
assert.match(patch5Css, /tr\.ledger-row:not\(\.inline-editing\)[\s\S]*?touch-action:\s*pan-y !important/);
assert.match(patch5Css, /data-swipe-open="edit"/);
assert.match(patch5Css, /data-swipe-open="delete"/);
assert.match(patch5Build3Js, /CY_V0215_BUILD3_EDGE_GUARD = 24/);
assert.match(patch5Build3Js, /openV0215Build3SwipeRow\(row, 'edit'\)/);
assert.match(patch5Build3Js, /openV0215Build3SwipeRow\(row, 'delete'\)/);
assert.doesNotMatch(patch5Build3Js, /MutationObserver/);
assert.match(indexHtml, /<div id="favoriteCategoryGroup"/);
assert.match(indexHtml, /<div id="summarySuggestionGroup"/);
assert.doesNotMatch(indexHtml, /<details id="favoriteCategoryGroup"/);

// Breakpoint ownership: Build 10 phone, Build 9 Tablet, manager/date patch work Desktop only.
assert.match(build10Css, /@media \(max-width: 767px\)/);
assert.doesNotMatch(build10Css, /@media \(min-width: 768px\)/);
assert.match(build9Css, /@media \(min-width: 768px\) and \(max-width: 1023px\)/);
assert.match(build11Css, /@media \(min-width: 768px\)/);
assert.match(build12Css, /@media \(min-width: 1024px\)/);
assert.match(build13Css, /@media \(min-width: 1024px\)/);
assert.match(build14Css, /@media \(min-width: 1024px\)/);
assert.match(build15Css, /@media \(min-width: 1024px\)/);
assert.match(build16Css, /@media \(min-width: 1024px\)/);
assert.match(patchCss, /@media \(min-width: 1024px\)/);
assert.match(patch2Css, /@media \(min-width: 1024px\)/);

// Build 10 mobile-first direction remains unchanged.
assert.match(build8Js, /setupV21Build9MobilePages\(\)/);
assert.match(build8Js, /nav\.id = 'mobileMainNav'/);
assert.match(build10Js, /trigger\.id = 'mobileAccountMenuButton'/);
assert.match(build10Js, /menu\.id = 'mobileAccountMenu'/);
assert.match(build10Js, /button\.id = 'mobileLedgerMoreButton'/);
assert.match(build10Css, /\.v21-mobile-main-nav\.v21-mobile-bottom-nav\s*\{[\s\S]*?position:\s*fixed !important;[\s\S]*?bottom:\s*0;/);
assert.match(build10Css, /\.entry-grid\s*\{[\s\S]*?grid-template-columns:\s*1fr !important;/);

// Tablet stays table-oriented and does not inherit V0.21.2 manager work.
const tabletCss = build9Css.match(/@media \(min-width: 768px\) and \(max-width: 1023px\)[\s\S]*?(?=\/\* Mobile:)/)?.[0] || '';
assert.match(tabletCss, /\.entry-grid\s*\{[\s\S]*?grid-template-columns:\s*minmax\(0, 1\.3fr\) minmax\(140px, \.8fr\) 112px;/);
assert.doesNotMatch(tabletCss, /\.ledger-card table\s*,[\s\S]*?display:\s*block/);
assert.match(patch2Js, /if \(!window\.matchMedia\(CY_V0212_DESKTOP\)\.matches\) return;/);
assert.match(patch2Js, /window\.matchMedia\(CY_V0212_DESKTOP\)/);
assert.doesNotMatch(patch2Js, /mobileMainNav|mobileAccountMenuButton|mobileLedgerMoreButton|entryAccountPickerButton/);
assert.match(patch2Css, /@media \(min-width: 1024px\)[\s\S]*?\.v0212-category-toolbar/);

// App-owned confirmation dialog remains responsive and shared across breakpoints.
assert.match(patchCss, /\.cy-confirm-dialog/);
assert.match(patchJs, /window\.cyConfirm = options => new Promise/);
assert.match(patchJs, /installV0211ConfirmInterceptors\(\)/);

// V0.21.4 balance detail remains available on pointer and touch/click without creating a second mobile implementation.
assert.match(patch4Js, /data-balance-popover-id/);
assert.match(patch4Js, /CY_V0214_HOVER/);
assert.match(patch4Js, /document\.addEventListener\('click'/);
assert.match(patch4Css, /@media \(max-width: 767px\)/);

// Existing phone transaction-card ledger remains intact.
assert.match(v20Css, /@media \(max-width: 767px\)[\s\S]*?\.ledger-card table,[\s\S]*?display:\s*block;/);
assert.match(v201Css, /tbody > tr:not\(\.account-group-row\)\s*\{[\s\S]*?display:\s*grid;/);

console.log('V0.21.5 Build 3 adaptive UI regression tests passed.');
