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
const v021 = read('public/v021.js');
const build8Js = read('public/v021b8.js');
const build10Js = read('public/v021b10.js');
const build11Js = read('public/v021b11.js');
const build12Js = read('public/v021b12.js');
const build13Js = read('public/v021b13.js');
const build14Js = read('public/v021b14.js');
const build15Js = read('public/v021b15.js');
const v03 = read('public/v03.js');
const v06 = read('public/v06.js');
const v07 = read('public/v07.js');
const v013 = read('public/v013.js');
const appV19 = read('src/app-v19.js');
const v11Tools = read('src/v11-tools.js');

assert.equal(version, '0.21.0');
assert.equal(build, '15');
assert.match(build11Js, /ensureV21Build12Script\(\)/);
assert.match(build11Js, /ensureV21Build13Script\(\)/);
assert.match(build11Js, /ensureV21Build14Script\(\)/);
assert.match(build11Js, /ensureV21Build15Script\(\)/);
assert.match(build11Js, /script\.src = '\/v021b15\.js'/);
assert.match(build12Js, /CY_V21_BUILD12_VERSION = 'V0\.21\.0 Build 12'/);
assert.match(build13Js, /CY_V21_BUILD13_VERSION = 'V0\.21\.0 Build 13'/);
assert.match(build14Js, /CY_V21_BUILD14_VERSION = 'V0\.21\.0 Build 14'/);
assert.match(build15Js, /CY_V21_BUILD15_VERSION = 'V0\.21\.0 Build 15'/);
assert.match(build15Js, /CY_V21_BUILD15_DESKTOP = '\(min-width: 1024px\)'/);
assert.match(build15Js, /link\.href = '\/v021b15\.css'/);
for (const js of [build8Js, build10Js, build11Js, build12Js, build13Js, build14Js, build15Js]) {
  assert.doesNotThrow(() => new Function(js), 'V0.21 overlay JavaScript must parse');
}

// Breakpoint ownership remains explicit: Build 10 phone, Build 9 Tablet, Builds 12-15 Desktop only.
assert.match(build10Css, /@media \(max-width: 767px\)/);
assert.match(build11Css, /@media \(min-width: 768px\)/);
assert.match(build12Css, /@media \(min-width: 1024px\)/);
assert.match(build13Css, /@media \(min-width: 1024px\)/);
assert.match(build14Css, /@media \(min-width: 1024px\)/);
assert.match(build15Css, /@media \(min-width: 1024px\)/);
assert.doesNotMatch(build15Css, /@media \(max-width:/);

// Approved Desktop split, slider and entry mode tints remain intact.
assert.match(build1Css, /@media \(min-width: 1360px\)/);
assert.match(build1Css, /grid-template-columns:\s*minmax\(380px, 420px\) minmax\(0, 1fr\)/);
assert.match(build1Css, /\.entry-card\.entry-income\s*\{[\s\S]*?background:\s*#f4fbf6;/);
assert.match(build1Css, /\.entry-card\.entry-expense\s*\{[\s\S]*?background:\s*#fff6f5;/);
for (const css of [baseCss, build3Css, build5Css, build6Css, build7Css, build8Css, build11Css, build12Css, build13Css, build14Css]) {
  assert.doesNotMatch(css, /\.entry-kind-switch\s*\{/);
}

// Identity badge/readability from Build 12 stays intact.
assert.match(build8Css, /\.current-user\.role-super-admin\s*\{[\s\S]*?#fff4d6/);
assert.match(build8Css, /\.current-user\.role-admin\s*\{[\s\S]*?#f8ebe1/);
assert.match(build12Css, /\.current-user-main,[\s\S]*?\.current-user-role\s*\{[\s\S]*?font-size:\s*13\.5px !important;/);
assert.match(build12Css, /\.current-user-role::before\s*\{[\s\S]*?content:\s*"\[" !important;/);
assert.match(build12Css, /#monthSummary,[\s\S]*?font-size:\s*13\.5px !important;/);
assert.match(build12Css, /\.ledger-card th\s*\{[\s\S]*?font-size:\s*13\.5px !important;/);

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
assert.match(build13Js, /status\.classList\.add\('hidden'\)/);

// Custom month picker remains backed by native month sources and Build 14 extends it to all visible Desktop month fields.
assert.match(build12Js, /root\.id = 'ledgerMonthPickerCustom'/);
assert.match(build12Js, /input\.dispatchEvent\(new Event\('change', \{ bubbles: true \}\)\)/);
assert.match(build13Css, /\.v21-month-picker-head\s*\{[\s\S]*?grid-template-columns:\s*36px minmax\(0, 1fr\) 36px !important;/);
assert.match(build14Js, /document\.querySelectorAll\('input\[type="month"\]'\)/);
assert.match(build14Js, /ensureV21Build14MonthPickerForInput\(input\)/);
assert.match(build14Js, /input\.dispatchEvent\(new Event\('change', \{ bubbles: true \}\)\)/);
assert.match(build14Css, /\.v21-native-month-source\s*\{[\s\S]*?clip-path:\s*inset\(50%\) !important;/);
assert.match(build14Css, /\.v21-month-picker-caret\s*\{[\s\S]*?display:\s*none !important;/);
assert.match(v06, /els\.monthFilter\.dispatchEvent\(new Event\('change', \{ bubbles: true \}\)\)/);

// Opening balance remains a compact current-month CRUD dialog with consistent Save copy.
assert.match(build13Css, /\.opening-modal\s*\{[\s\S]*?width:\s*min\(300px, calc\(100vw - 28px\)\) !important;/);
assert.match(build13Css, /\.opening-modal \.opening-dialog-heading\s*\{[\s\S]*?display:\s*none !important;/);
assert.match(build13Js, /title\.textContent = `\$\{month\.replace\('-', '\/'\)\}期初餘額`/);
assert.match(build13Js, /openingSave\.textContent = '儲存'/);
assert.match(build13Js, /editSave\.textContent = '儲存'/);

// Account/category management stays in Desktop header and Build 14 keeps inline editing.
assert.match(build13Js, /headerAccountManagerButton/);
assert.match(build13Js, /headerCategoryManagerButton/);
assert.match(build14Js, /beginV21Build14InlineEdit\('account'/);
assert.match(build14Js, /beginV21Build14InlineEdit\('category'/);
assert.match(build14Js, /beginV21Build14InlineEdit\('group'/);
assert.doesNotMatch(build14Js, /prompt\s*\(/);
assert.match(build14Js, /input\.maxLength = type === 'account' \? 8 : 60/);
assert.match(appV19, /ACCOUNT_NAME_MAX_CHARS = 8/);
assert.match(appV19, /ACCOUNT_NAME_TOO_LONG/);

// Build 15 refines the managers: exact header-button style parity, fixed default tags, drag handles and mode tint.
assert.match(build15Css, /#headerAccountManagerButton,[\s\S]*?#headerCategoryManagerButton,[\s\S]*?#settingsButton\s*\{/);
assert.match(build15Css, /\.v21-account-manager-row\s*\{[\s\S]*?grid-template-columns:\s*24px 66px minmax\(0, 1fr\) auto/);
assert.match(build15Css, /\.v21-default-tag\s*\{[\s\S]*?width:\s*66px;[\s\S]*?height:\s*23px;/);
assert.match(build15Js, /data-v21-drag-account/);
assert.match(build15Js, />預設<\/button>/);
assert.match(build15Js, />設為預設<\/button>/);
assert.match(build15Js, /entry-kind-switch v21-category-kind-switch/);
assert.match(build15Css, /v21-category-kind-income[\s\S]*?background:\s*#f4fbf6;/);
assert.match(build15Css, /v21-category-kind-expense[\s\S]*?background:\s*#fff6f5;/);
assert.match(build15Js, /data-v21-drag-group/);
assert.match(build15Js, /data-v21-drag-category/);
assert.match(build15Js, /v21Build15CategoryOrderPayload/);
assert.match(build15Js, /\/api\/accounts\/reorder/);
assert.match(build15Js, /\/api\/category-groups\/reorder/);
assert.match(build15Js, /\/api\/categories\/reorder/);

// Reorder APIs validate complete ID sets and support cross-group category placement.
assert.match(v11Tools, /url\.pathname === '\/api\/accounts\/reorder'/);
assert.match(v11Tools, /url\.pathname === '\/api\/category-groups\/reorder'/);
assert.match(v11Tools, /url\.pathname === '\/api\/categories\/reorder'/);
assert.match(v11Tools, /handleReorderCategories/);
assert.match(v11Tools, /UPDATE categories SET group_id = \?, sort_order = \?/);
assert.match(v11Tools, /sameIdSet/);
assert.match(v11Tools, /runStatements/);

// Ledger/export behavior and Build 4 observer hotfix remain protected.
assert.match(v013, /button\.textContent = '匯出中…'/);
assert.match(v013, /button\.setAttribute\('aria-busy', 'true'\)/);
assert.match(v021, /if \(account\.textContent !== nextText\) account\.textContent = nextText;/);
assert.match(v021, /if \(empty && empty\.textContent !== '本次尚無輸入紀錄。'\) empty\.textContent = '本次尚無輸入紀錄。';/);

console.log('V0.21.0 Build 15 desktop regression tests passed.');
