import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const HERE = path.dirname(fileURLToPath(import.meta.url));
const ROOT = path.resolve(HERE, '..');
const read = relative => fs.readFileSync(path.join(ROOT, relative), 'utf8');

const version = read('VERSION').trim();
const build = read('BUILD').trim();
const baseCss = read('public/v021.css');
const build1Css = read('public/v021b1.css');
const build3Css = read('public/v021b3.css');
const build5Css = read('public/v021b5.css');
const build6Css = read('public/v021b6.css');
const build7Css = read('public/v021b7.css');
const build8Css = read('public/v021b8.css');
const build10Css = read('public/v021b10.css');
const build11Css = read('public/v021b11.css');
const build12Css = read('public/v021b12.css');
const build13Css = read('public/v021b13.css');
const build14Css = read('public/v021b14.css');
const build15Css = read('public/v021b15.css');
const build16Css = read('public/v021b16.css');
const patchCss = read('public/v0211.css');
const patch2Css = read('public/v0212.css');
const patch4Js = read('public/v0214.js');
const patch4Css = read('public/v0214.css');
const patch5Css = read('public/v0215.css');
const patch5Build4Js = read('public/v0215b4.js');
const v021 = read('public/v021.js');
const build8Js = read('public/v021b8.js');
const build10Js = read('public/v021b10.js');
const build11Js = read('public/v021b11.js');
const build12Js = read('public/v021b12.js');
const build13Js = read('public/v021b13.js');
const build14Js = read('public/v021b14.js');
const build15Js = read('public/v021b15.js');
const build16Js = read('public/v021b16.js');
const patchJs = read('public/v0211.js');
const patch2Js = read('public/v0212.js');
const keyboardJs = read('public/v0211-keyboard.js');
const v03 = read('public/v03.js');
const v06 = read('public/v06.js');
const v07 = read('public/v07.js');
const v013 = read('public/v013.js');
const v019 = read('public/v019.js');
const appV19 = read('src/app-v19.js');
const v11Tools = read('src/v11-tools.js');

assert.equal(version, '0.21.5');
assert.equal(build, '4');
assert.match(build11Js, /ensureV21Build12Script\(\)/);
assert.match(build11Js, /ensureV21Build13Script\(\)/);
assert.match(build11Js, /ensureV21Build14Script\(\)/);
assert.match(build11Js, /ensureV21Build15Script\(\)/);
assert.match(build11Js, /ensureV21Build16Script\(\)/);
assert.match(build11Js, /ensureV0211PatchScript\(\)/);
assert.match(build11Js, /ensureV0211KeyboardScript\(\)/);
assert.match(build11Js, /ensureV0212PatchScript\(\)/);
assert.match(build11Js, /script\.src = '\/v0212\.js'/);
assert.match(build12Js, /CY_V21_BUILD12_VERSION = 'V0\.21\.0 Build 12'/);
assert.match(build13Js, /CY_V21_BUILD13_VERSION = 'V0\.21\.0 Build 13'/);
assert.match(build14Js, /CY_V21_BUILD14_VERSION = 'V0\.21\.0 Build 14'/);
assert.match(build15Js, /CY_V21_BUILD15_VERSION = 'V0\.21\.0 Build 15'/);
assert.match(build16Js, /CY_V21_BUILD16_VERSION = 'V0\.21\.0 Build 16'/);
assert.match(patchJs, /CY_V0211_VERSION = 'V0\.21\.1'/);
assert.match(patch2Js, /CY_V0212_VERSION = 'V0\.21\.2'/);
for (const js of [build8Js, build10Js, build11Js, build12Js, build13Js, build14Js, build15Js, build16Js, patchJs, patch2Js, patch4Js, patch5Build4Js, keyboardJs]) {
  assert.doesNotThrow(() => new Function(js), 'V0.21 overlay JavaScript must parse');
}

// Breakpoint ownership remains explicit: Build 10 phone, Build 9 Tablet, later manager patches Desktop only.
assert.match(build10Css, /@media \(max-width: 767px\)/);
assert.match(build11Css, /@media \(min-width: 768px\)/);
assert.match(build12Css, /@media \(min-width: 1024px\)/);
assert.match(build13Css, /@media \(min-width: 1024px\)/);
assert.match(build14Css, /@media \(min-width: 1024px\)/);
assert.match(build15Css, /@media \(min-width: 1024px\)/);
assert.match(build16Css, /@media \(min-width: 1024px\)/);
assert.match(patchCss, /@media \(min-width: 1024px\)/);
assert.match(patch2Css, /@media \(min-width: 1024px\)/);

// Approved Desktop split, slider and entry mode tints remain intact.
assert.match(build1Css, /@media \(min-width: 1360px\)/);
assert.match(build1Css, /grid-template-columns:\s*minmax\(380px, 420px\) minmax\(0, 1fr\)/);
assert.match(build1Css, /\.entry-card\.entry-income\s*\{[\s\S]*?background:\s*#f4fbf6;/);
assert.match(build1Css, /\.entry-card\.entry-expense\s*\{[\s\S]*?background:\s*#fff6f5;/);
for (const css of [baseCss, build3Css, build5Css, build6Css, build7Css, build8Css, build11Css, build12Css, build13Css, build14Css]) {
  assert.doesNotMatch(css, /\.entry-kind-switch\s*\{/);
}

// Identity badge/readability stays intact and V0.21.1 keeps real vertical centering.
assert.match(build8Css, /\.current-user\.role-super-admin\s*\{[\s\S]*?#fff4d6/);
assert.match(build8Css, /\.current-user\.role-admin\s*\{[\s\S]*?#f8ebe1/);
assert.match(build12Css, /\.current-user-role::before\s*\{[\s\S]*?content:\s*"\[" !important;/);
assert.match(patchCss, /#currentUser\.current-user\s*\{[\s\S]*?align-items:\s*center !important;[\s\S]*?height:\s*30px !important;/);

// Quick entry proportions and summary rules remain intact.
assert.match(build12Css, /\.entry-grid\s*\{[\s\S]*?grid-template-columns:\s*minmax\(0, 1fr\) 86px 78px !important;/);
assert.match(build12Js, /summary\.placeholder = '最多20個字'/);
assert.match(build8Js, /CY_V21_BUILD8_SUMMARY_UNITS = 40/);
assert.match(appV19, /SUMMARY_MAX_UNITS = 40/);
assert.match(appV19, /SUMMARY_TOO_LONG/);

// Enter flow remains date -> summary -> amount -> save, then returns to summary.
assert.match(v03, /enterStep\(els\.txDate, \(\) => els\.summary\?\.focus\(\)\)/);
assert.match(v03, /enterStep\(els\.summary,[\s\S]*?els\.amount\?\.focus\(\)/);
assert.match(v03, /enterStep\(els\.amount,[\s\S]*?els\.form\.requestSubmit\(\)/);
assert.match(v03, /cyFocusSummaryAfterSave[\s\S]*?els\.summary\.focus\(\)/);
assert.match(v07, /event\.key !== 'Tab'/);
assert.match(keyboardJs, /event\.key === 'Enter'[\s\S]*?#summary/);
assert.match(keyboardJs, /event\.key === 'Tab'[\s\S]*?setEntryKind/);

// Normal connection state is silent; warnings remain visible.
assert.match(build13Js, /setupV21Build13ConnectionStatus\(\)/);
assert.match(build13Js, /status\.classList\.contains\('warn'\)/);
assert.match(build13Js, /status\.textContent = ''/);

// Month/date controls remain custom.
assert.match(build12Js, /root\.id = 'ledgerMonthPickerCustom'/);
assert.match(build14Js, /document\.querySelectorAll\('input\[type="month"\]'\)/);
assert.match(patchJs, /document\.querySelectorAll\('input\[type="date"\]'\)/);
assert.match(patchCss, /\.v0211-native-date-source\s*\{[\s\S]*?clip-path:\s*inset\(50%\) !important;/);

// Opening balance remains compact.
assert.match(build13Css, /\.opening-modal\s*\{[\s\S]*?width:\s*min\(300px, calc\(100vw - 28px\)\) !important;/);
assert.match(build13Js, /openingSave\.textContent = '儲存'/);
assert.match(build13Js, /editSave\.textContent = '儲存'/);

// V0.21.2 fixes the account default action as a real reserved column.
assert.match(patch2Css, /data-management-pane="accounts"[\s\S]*?width:\s*min\(390px/);
assert.match(patch2Css, /\.v0211-account-row\s*\{[\s\S]*?grid-template-columns:\s*20px 68px minmax\(0, 1fr\) auto !important;/);
assert.match(patch2Css, /\.v0211-default-tag\s*\{[\s\S]*?width:\s*68px !important;[\s\S]*?white-space:\s*nowrap !important;/);

// V0.21.2 category manager is a fixed-row hierarchy with all creation actions in the top toolbar.
assert.match(patch2Js, /renderV0212CategoryManager/);
assert.match(patch2Js, /data-v0212-manager-kind="income"/);
assert.match(patch2Js, /data-v0212-add-group/);
assert.match(patch2Js, /data-v0212-add-category/);
assert.match(patch2Js, /v0212-toolbar-spacer/);
assert.match(patch2Js, /data-v21-drag-group/);
assert.match(patch2Js, /data-v21-drag-category/);
assert.match(patch2Js, /class="v0211-category-shell v0212-category-shell"/);
assert.doesNotMatch(patch2Js, /data-new-category-group/);
assert.doesNotMatch(patch2Js, /v21-inline-name-input/);
assert.doesNotMatch(patch2Js, /class="category-item/);
assert.match(patch2Css, /\.v0212-category-toolbar\s*\{[\s\S]*?grid-template-columns:\s*auto auto 1fr auto !important;/);
assert.match(patch2Css, /\.v0212-add-category-button\s*\{[\s\S]*?justify-self:\s*end;/);
assert.match(patch2Css, /\.v0212-category-row\s*\{[\s\S]*?min-height:\s*32px !important;/);
assert.match(patch2Css, /\.v0212-legacy-group-add\s*\{[\s\S]*?display:\s*none !important;/);

// Add-category and rename use a small app-owned dialog, never an expanding inline editor.
assert.match(patch2Js, /id="v0212GroupSelect"/);
assert.match(patch2Js, /id="v0212NameInput"/);
assert.match(patch2Js, /openV0212AddCategoryDialog/);
assert.match(patch2Js, /openV0212RenameDialog/);
assert.match(patch2Js, /window\.addEventListener\('click',[\s\S]*?true\);/);
assert.match(patch2Css, /\.v0212-manager-dialog\s*\{[\s\S]*?width:\s*min\(340px/);

// Optimistic reorder stays protected from Build 16 and the existing reorder APIs.
assert.match(build16Js, /state\.accounts = v21Build16OrderObjects\(previous, nextIds\);[\s\S]*?await persistV21Build16Optimistic/);
assert.match(build16Js, /state\.groups = v21Build16ReplaceKindOrder\(previous, kind, nextIds\);[\s\S]*?await persistV21Build16Optimistic/);
assert.match(build16Js, /state\.categories = v21Build16ApplyCategoryPayload\(previous, kind, payload\);[\s\S]*?await persistV21Build16Optimistic/);
assert.match(v11Tools, /url\.pathname === '\/api\/accounts\/reorder'/);
assert.match(v11Tools, /url\.pathname === '\/api\/category-groups\/reorder'/);
assert.match(v11Tools, /url\.pathname === '\/api\/categories\/reorder'/);

// Native browser confirmation boxes remain intercepted by the app-owned dialog.
assert.match(patchJs, /window\.cyConfirm = options => new Promise/);
assert.match(patchJs, /\[data-delete-id\]/);
assert.match(patchJs, /\[data-account-delete\]/);
assert.match(patchJs, /\[data-category-delete\]/);
assert.match(patchJs, /\[data-group-delete\]/);
assert.match(patchCss, /\.cy-confirm-dialog/);

// V0.21.4 completion feedback, balance breakdown and safe optimistic interactions.
assert.match(v019, /window\.cyShowMigrationComplete\?\.\(result\)/);
assert.match(patch4Js, /帳本移轉完成/);
assert.match(v06, /balancesById/);
assert.match(v06, /data-balance-popover-id/);
assert.match(patch4Js, /handleV0214AccountDefault/);
assert.match(patch4Js, /handleV0214FavoriteToggle/);
assert.match(patch4Js, /已還原/);
assert.match(patch4Css, /\.v0214-balance-popover/);

// V0.21.5 Build 4 keeps the new edit/toolbar/search layer scoped to phone widths.
assert.match(patch5Build4Js, /CY_V0215_BUILD4_MOBILE = '\(max-width: 767px\)'/);
assert.doesNotMatch(patch5Css, /@media \(min-width: 1024px\)[\s\S]*?v0215-mobile-save-message/);
assert.match(patch5Css, /@media \(max-width: 767px\)/);
assert.doesNotMatch(patch5Css, /@media \(min-width: 1024px\)[\s\S]*?grid-template-areas/);

// Ledger/export behavior and Build 4 observer hotfix remain protected.
assert.match(v013, /button\.textContent = '匯出中…'/);
assert.match(v013, /button\.setAttribute\('aria-busy', 'true'\)/);
assert.match(v021, /if \(account\.textContent !== nextText\) account\.textContent = nextText;/);
assert.match(v021, /if \(empty && empty\.textContent !== '本次尚無輸入紀錄。'\) empty\.textContent = '本次尚無輸入紀錄。';/);

console.log('V0.21.5 Build 4 desktop regression tests passed.');
