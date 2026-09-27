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
const v03 = read('public/v03.js');
const v06 = read('public/v06.js');
const v07 = read('public/v07.js');
const v013 = read('public/v013.js');
const appV19 = read('src/app-v19.js');
const v11Tools = read('src/v11-tools.js');

assert.equal(version, '0.21.1');
assert.equal(build, '0');
assert.match(build11Js, /ensureV21Build12Script\(\)/);
assert.match(build11Js, /ensureV21Build13Script\(\)/);
assert.match(build11Js, /ensureV21Build14Script\(\)/);
assert.match(build11Js, /ensureV21Build15Script\(\)/);
assert.match(build11Js, /ensureV21Build16Script\(\)/);
assert.match(build11Js, /ensureV0211PatchScript\(\)/);
assert.match(build11Js, /script\.src = '\/v0211\.js'/);
assert.match(build12Js, /CY_V21_BUILD12_VERSION = 'V0\.21\.0 Build 12'/);
assert.match(build13Js, /CY_V21_BUILD13_VERSION = 'V0\.21\.0 Build 13'/);
assert.match(build14Js, /CY_V21_BUILD14_VERSION = 'V0\.21\.0 Build 14'/);
assert.match(build15Js, /CY_V21_BUILD15_VERSION = 'V0\.21\.0 Build 15'/);
assert.match(build16Js, /CY_V21_BUILD16_VERSION = 'V0\.21\.0 Build 16'/);
assert.match(patchJs, /CY_V0211_VERSION = 'V0\.21\.1'/);
for (const js of [build8Js, build10Js, build11Js, build12Js, build13Js, build14Js, build15Js, build16Js, patchJs]) {
  assert.doesNotThrow(() => new Function(js), 'V0.21 overlay JavaScript must parse');
}

// Breakpoint ownership remains explicit: Build 10 phone, Build 9 Tablet, Builds 12-16 + V0.21.1 Desktop patch.
assert.match(build10Css, /@media \(max-width: 767px\)/);
assert.match(build11Css, /@media \(min-width: 768px\)/);
assert.match(build12Css, /@media \(min-width: 1024px\)/);
assert.match(build13Css, /@media \(min-width: 1024px\)/);
assert.match(build14Css, /@media \(min-width: 1024px\)/);
assert.match(build15Css, /@media \(min-width: 1024px\)/);
assert.match(build16Css, /@media \(min-width: 1024px\)/);
assert.match(patchCss, /@media \(min-width: 1024px\)/);

// Approved Desktop split, slider and entry mode tints remain intact.
assert.match(build1Css, /@media \(min-width: 1360px\)/);
assert.match(build1Css, /grid-template-columns:\s*minmax\(380px, 420px\) minmax\(0, 1fr\)/);
assert.match(build1Css, /\.entry-card\.entry-income\s*\{[\s\S]*?background:\s*#f4fbf6;/);
assert.match(build1Css, /\.entry-card\.entry-expense\s*\{[\s\S]*?background:\s*#fff6f5;/);
for (const css of [baseCss, build3Css, build5Css, build6Css, build7Css, build8Css, build11Css, build12Css, build13Css, build14Css]) {
  assert.doesNotMatch(css, /\.entry-kind-switch\s*\{/);
}

// Identity badge/readability stays intact and V0.21.1 forces real vertical centering.
assert.match(build8Css, /\.current-user\.role-super-admin\s*\{[\s\S]*?#fff4d6/);
assert.match(build8Css, /\.current-user\.role-admin\s*\{[\s\S]*?#f8ebe1/);
assert.match(build12Css, /\.current-user-role::before\s*\{[\s\S]*?content:\s*"\[" !important;/);
assert.match(patchCss, /#currentUser\.current-user\s*\{[\s\S]*?align-items:\s*center !important;[\s\S]*?height:\s*30px !important;/);
assert.match(patchCss, /#headerAccountManagerButton,[\s\S]*?#headerCategoryManagerButton,[\s\S]*?#settingsButton/);

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

// Normal connection state is silent; warnings remain visible.
assert.match(build13Js, /setupV21Build13ConnectionStatus\(\)/);
assert.match(build13Js, /status\.classList\.contains\('warn'\)/);
assert.match(build13Js, /status\.textContent = ''/);

// Month controls stay custom; V0.21.1 additionally replaces native Desktop date pickers.
assert.match(build12Js, /root\.id = 'ledgerMonthPickerCustom'/);
assert.match(build14Js, /document\.querySelectorAll\('input\[type="month"\]'\)/);
assert.match(build14Js, /ensureV21Build14MonthPickerForInput\(input\)/);
assert.match(patchJs, /document\.querySelectorAll\('input\[type="month"\]'\)/);
assert.match(patchJs, /document\.querySelectorAll\('input\[type="date"\]'\)/);
assert.match(patchJs, /ensureV0211DatePicker/);
assert.match(patchCss, /\.v0211-native-date-source\s*\{[\s\S]*?clip-path:\s*inset\(50%\) !important;/);
assert.match(patchJs, /data-v0211-date-month/);
assert.match(patchJs, /data-v0211-date-year/);

// Opening balance remains a compact current-month CRUD dialog with consistent Save copy.
assert.match(build13Css, /\.opening-modal\s*\{[\s\S]*?width:\s*min\(300px, calc\(100vw - 28px\)\) !important;/);
assert.match(build13Js, /title\.textContent = `\$\{month\.replace\('-', '\/'\)\}期初餘額`/);
assert.match(build13Js, /openingSave\.textContent = '儲存'/);
assert.match(build13Js, /editSave\.textContent = '儲存'/);

// Account manager remains compact; tags never overflow and names carry the visual weight.
assert.match(patchCss, /data-management-pane="accounts"[\s\S]*?width:\s*min\(360px/);
assert.match(patchCss, /\.v0211-default-tag\s*\{[\s\S]*?width:\s*54px !important;[\s\S]*?font-size:\s*9\.5px !important;/);
assert.match(patchCss, /\.v0211-account-name-cell \.v21-editable-name\s*\{[\s\S]*?font-size:\s*14px !important;/);
assert.match(patchCss, /\.v0211-account-row \.v21-edit-name-button\[hidden\]/);
assert.match(patchJs, /renderV0211AccountManager/);
assert.match(patchJs, /window\.renderV21Build15AccountManager = renderV0211AccountManager/);

// Category manager has exactly one patch-owned hierarchy; no legacy card/move controls are rendered.
assert.match(patchJs, /renderV0211CategoryManager/);
assert.match(patchJs, /data-v0211-manager-kind="income"/);
assert.match(patchJs, /data-v21-drag-group/);
assert.match(patchJs, /data-v21-drag-category/);
assert.doesNotMatch(patchJs, /data-v11-move-category|data-v11-move-group|data-v12-category-transfer/);
assert.doesNotMatch(patchJs, /class="category-item/);
assert.match(patchJs, /\.category-item, \.order-button, \[data-v12-category-transfer\]/);
assert.match(patchCss, /v0211-category-income[\s\S]*?background:\s*#f4fbf6/);
assert.match(patchCss, /v0211-category-expense[\s\S]*?background:\s*#fff6f5/);

// Optimistic reorder stays protected from Build 16.
assert.match(build16Js, /state\.accounts = v21Build16OrderObjects\(previous, nextIds\);[\s\S]*?await persistV21Build16Optimistic/);
assert.match(build16Js, /state\.groups = v21Build16ReplaceKindOrder\(previous, kind, nextIds\);[\s\S]*?await persistV21Build16Optimistic/);
assert.match(build16Js, /state\.categories = v21Build16ApplyCategoryPayload\(previous, kind, payload\);[\s\S]*?await persistV21Build16Optimistic/);
assert.match(v11Tools, /url\.pathname === '\/api\/accounts\/reorder'/);
assert.match(v11Tools, /url\.pathname === '\/api\/category-groups\/reorder'/);
assert.match(v11Tools, /url\.pathname === '\/api\/categories\/reorder'/);

// Native browser confirmation boxes are intercepted by the app-owned dialog for every destructive/high-impact flow.
assert.match(patchJs, /window\.cyConfirm = options => new Promise/);
assert.match(patchJs, /\[data-delete-id\]/);
assert.match(patchJs, /\[data-account-delete\]/);
assert.match(patchJs, /\[data-category-delete\]/);
assert.match(patchJs, /\[data-group-delete\]/);
assert.match(patchJs, /#excelImportCommitButton/);
assert.match(patchJs, /#backupRunNow/);
assert.match(patchJs, /#desktopMigrationCommitV19/);
assert.match(patchCss, /\.cy-confirm-dialog/);

// Ledger/export behavior and Build 4 observer hotfix remain protected.
assert.match(v013, /button\.textContent = '匯出中…'/);
assert.match(v013, /button\.setAttribute\('aria-busy', 'true'\)/);
assert.match(v021, /if \(account\.textContent !== nextText\) account\.textContent = nextText;/);
assert.match(v021, /if \(empty && empty\.textContent !== '本次尚無輸入紀錄。'\) empty\.textContent = '本次尚無輸入紀錄。';/);

console.log('V0.21.1 desktop regression tests passed.');
