import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const HERE = path.dirname(fileURLToPath(import.meta.url));
const ROOT = path.resolve(HERE, '..');
const read = relative => fs.readFileSync(path.join(ROOT, relative), 'utf8');

const version = read('VERSION').trim();
const build = read('BUILD').trim();
const appJs = read('public/app.js');
const v03 = read('public/v03.js');
const authJs = read('public/auth.js');
const authCss = read('public/auth.css');
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
const patch5Build4Js = read('public/v0215b4.js');
const v06 = read('public/v06.js');
const v20Css = read('public/v020.css');
const v201Css = read('public/v0201.css');

assert.equal(version, '0.21.5');
assert.equal(build, '8');
assert.match(build11Js, /ensureV0211PatchScript\(\)/);
assert.match(build11Js, /ensureV0211KeyboardScript\(\)/);
assert.match(build11Js, /ensureV0212PatchScript\(\)/);
assert.match(build11Js, /script\.src = '\/v0212\.js'/);
assert.match(build11Js, /ensureV0215Stylesheet\(\)/);
assert.match(build11Js, /link\.href = '\/v0215\.css\?v=0215b8'/);
assert.match(build11Js, /ensureV0215Build3Script\(\)/);
assert.match(build11Js, /script\.src = '\/v0215b3\.js'/);
assert.match(build11Js, /ensureV0215Build4Script\(\)/);
assert.match(build11Js, /script\.src = '\/v0215b4\.js\?v=0215b8'/);
assert.match(build12Js, /CY_V21_BUILD12_DESKTOP = '\(min-width: 1024px\)'/);
assert.match(build13Js, /CY_V21_BUILD13_DESKTOP = '\(min-width: 1024px\)'/);
assert.match(build14Js, /CY_V21_BUILD14_DESKTOP = '\(min-width: 1024px\)'/);
assert.match(build15Js, /CY_V21_BUILD15_DESKTOP = '\(min-width: 1024px\)'/);
assert.match(build16Js, /CY_V21_BUILD16_DESKTOP = '\(min-width: 1024px\)'/);
assert.match(patchJs, /CY_V0211_DESKTOP = '\(min-width: 1024px\)'/);
assert.match(keyboardJs, /CY_V0211_KEYBOARD_DESKTOP = '\(min-width: 1024px\)'/);
assert.match(patch2Js, /CY_V0212_DESKTOP = '\(min-width: 1024px\)'/);
for (const js of [build8Js, build10Js, build11Js, build12Js, build13Js, build14Js, build15Js, build16Js, patchJs, keyboardJs, patch2Js, patch4Js, patch5Build3Js, patch5Build4Js]) {
  assert.doesNotThrow(() => new Function(js), 'adaptive overlay JavaScript must parse');
}

assert.match(v03, /enterStep\(els\.txDate, \(\) => els\.summary\?\.focus\(\)\)/);
assert.match(v03, /enterStep\(els\.summary,[\s\S]*?els\.amount\?\.focus\(\)/);
assert.match(v03, /enterStep\(els\.amount,[\s\S]*?els\.form\.requestSubmit\(\)/);
assert.match(v03, /cyFocusSummaryAfterSave[\s\S]*?els\.summary\.focus\(\)/);
assert.match(keyboardJs, /if \(!window\.matchMedia\(CY_V0211_KEYBOARD_DESKTOP\)\.matches\) return;/);

// Build 8 auth recovery: let the browser finish a top-level navigation before verifying
// the freshly-set HttpOnly cookie, and keep Tablet touch behavior on the proven overlay.
assert.match(authJs, /credentials: 'include'/);
assert.match(authJs, /location\.replace\('\/\?auth_recovery=0215b8'\)/);
assert.doesNotMatch(authJs, /帳號密碼已通過，但登入狀態沒有保存/);
assert.match(indexHtml, /auth\.js\?v=0215b8/);
assert.match(indexHtml, /auth\.css\?v=0215b8/);
assert.match(indexHtml, /v021b11\.js\?v=0215b8/);
assert.doesNotMatch(authCss, /@media \(min-width: 641px\) and \(max-width: 1023px\)/);
assert.match(authCss, /@media \(max-width: 640px\)/);
assert.match(workerApp, /SameSite=Lax/);
assert.match(workerApp, /Expires=\$\{expires\}/);
assert.match(workerApp, /no-store, no-cache, must-revalidate, max-age=0/);

// V0.21.5 Build 8 keeps Build 7 behavior while fixing Safari auth, switch corners, and the five-slot month toolbar.
assert.match(patch5Css, /@media \(max-width: 767px\)/);
assert.match(patch5Css, /\.entry-card\.entry-income\s*\{[\s\S]*?linear-gradient\(to right, rgba\(86, 176, 113/);
assert.match(patch5Css, /\.entry-card\.entry-expense\s*\{[\s\S]*?linear-gradient\(to right, rgba\(207, 104, 94/);
assert.match(patch5Css, /\.entry-card\.entry-income\s*\{[\s\S]*?border-top:\s*0 !important/);
assert.match(patch5Css, /\.entry-card\.entry-expense\s*\{[\s\S]*?border-top:\s*0 !important/);
assert.match(patch5Css, /#mobileEntrySecondaryButton[\s\S]*?grid-area:\s*secondary !important/);
assert.match(patch5Css, /\.entry-card > \.section-title[\s\S]*?background:\s*transparent !important/);
assert.match(patch5Css, /\.entry-kind-switch-field[\s\S]*?background:\s*transparent !important/);
assert.match(patch5Css, /\.entry-kind-switch\s*\{[\s\S]*?overflow:\s*hidden !important[\s\S]*?clip-path:\s*inset\(0 round 10px\) !important/);
assert.match(patch5Css, /#ledgerMonthSlot > \.month-picker[\s\S]*?width:\s*100% !important/);
assert.match(patch5Css, /#ledgerDisplayMonth\s*\{[\s\S]*?display:\s*none !important/);
assert.match(patch5Css, /"save"[\s\S]*?"secondary"[\s\S]*?"message"/);
assert.match(patch5Css, /\.v0215-mobile-save-message[\s\S]*?transition:\s*opacity 400ms ease/);
assert.match(patch5Css, /\.quick-chip-list[\s\S]*?overflow-x:\s*auto !important/);
assert.match(patch5Css, /\.entry-grid input,[\s\S]*?text-align:\s*center !important/);
assert.match(patch5Css, /\.ledger-month-tools\s*\{[\s\S]*?grid-template-columns:\s*32px minmax\(0, 1fr\) 32px/);
assert.match(patch5Css, /\.ledger-month-tools\.v0215-toolbar-ready\s*\{[\s\S]*?grid-template-columns:\s*48px 32px minmax\(0, 1fr\) 32px 48px/);
assert.match(patch5Css, /grid-template-areas:\s*"opening income expense ending net"/);
assert.match(patch5Css, /\.net\.profit[\s\S]*?color:\s*#2f7a4c/);
assert.match(patch5Css, /\.net\.loss[\s\S]*?color:\s*#a54b45/);
assert.match(patch5Css, /\.topbar\s*\{[\s\S]*?border-bottom:\s*0 !important/);
assert.match(patch5Build4Js, /document\.readyState === 'loading'/);
assert.match(patch5Build4Js, /document\.addEventListener\('DOMContentLoaded', setupV0215Build4/);
assert.match(patch5Build4Js, /window\.setTimeout\(setupV0215Build4, 0\)/);
assert.match(patch5Build4Js, /if \(cyV0215Build4SetupDone\) return/);
assert.match(patch5Build4Js, /setupV0215Build4Toolbar\(attempt = 0\)/);
assert.match(patch5Build4Js, /attempt < 60/);
assert.match(patch5Build4Js, /monthTools\.classList\.add\('v0215-toolbar-ready'\)/);
assert.match(patch5Build4Js, /const slot = document\.querySelector\('#ledgerMonthSlot'\)/);
assert.match(patch5Build4Js, /if \(picker\.parentElement !== slot\) slot\.append\(picker\)/);
assert.match(patch5Build4Js, /monthTools\.append\(balance, prev, slot, next, more\)/);
assert.match(patch5Build4Js, /button\.id = 'mobileEntrySecondaryButton'/);
assert.match(patch5Build4Js, /button\.textContent = cyV0215Build4Edit \? '取消' : '清空'/);
assert.match(patch5Build4Js, /cancelV0215Build4MobileEditAndReturn/);
assert.match(patch5Build4Js, /restoreV0215Build4LedgerContext\(context, false\)/);
assert.match(patch5Build4Js, /clearV0215Build4EntryForm/);
assert.match(patch5Css, /\.ledger-search-submit\s*\{[\s\S]*?display:\s*none !important/);
assert.match(patch5Css, /#ledgerSummarySearch:placeholder-shown ~ #ledgerSearchClear/);
assert.match(v06, /enterkeyhint="search"/);
assert.match(v06, /netLabel = net > 0 \? '淨利' : net < 0 \? '淨損' : '淨利損'/);
assert.match(v06, /money\(Math\.abs\(net\)\)/);
assert.match(v06, /data-transaction-id="\$\{id\}"/);
assert.match(appJs, /window\.cyAfterSaveMessage\?\.\(els\.saveMessage, text, isError\)/);
assert.match(patch5Build3Js, /CY_V0215_BUILD3_EDGE_GUARD = 24/);
assert.match(patch5Build3Js, /openV0215Build3SwipeRow\(row, 'edit'\)/);
assert.match(patch5Build3Js, /openV0215Build3SwipeRow\(row, 'delete'\)/);
assert.doesNotMatch(patch5Build3Js, /MutationObserver/);
assert.match(patch5Build4Js, /beginV0215Build4MobileEdit/);
assert.match(patch5Build4Js, /els\.saveButton\.textContent = '儲存修改'/);
assert.match(patch5Build4Js, /event\.stopImmediatePropagation\(\)/);
assert.match(patch5Build4Js, /api\('\/api\/transactions\/' \+ edit\.id,[\s\S]*?method: 'PUT'/);
assert.match(patch5Build4Js, /cancelV0215Build4MobileEdit\(\)/);
assert.match(patch5Build4Js, /switchV0215Build4MobilePage\('ledger'\)/);
assert.match(patch5Build4Js, /rowTop: row\?\.getBoundingClientRect\(\)\.top/);
assert.match(patch5Build4Js, /2500/);
assert.match(patch5Build4Js, /mobileLedgerBalanceButton/);
assert.match(patch5Build4Js, /data-mobile-ledger-action="opening"/);
assert.doesNotMatch(patch5Build4Js, /MutationObserver/);

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

console.log('V0.21.5 Build 8 adaptive UI regression tests passed.');
