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
const v021 = read('public/v021.js');
const build8Js = read('public/v021b8.js');
const build10Js = read('public/v021b10.js');
const build11Js = read('public/v021b11.js');
const build12Js = read('public/v021b12.js');
const v03 = read('public/v03.js');
const v06 = read('public/v06.js');
const v07 = read('public/v07.js');
const v013 = read('public/v013.js');
const appV19 = read('src/app-v19.js');

assert.equal(version, '0.21.0');
assert.equal(build, '12');
assert.match(html, /src="\/v021b11\.js"/);
assert.match(build11Js, /ensureV21Build12Script\(\)/);
assert.match(build11Js, /script\.src = '\/v021b12\.js'/);
assert.match(build12Js, /CY_V21_BUILD12_VERSION = 'V0\.21\.0 Build 12'/);
assert.match(build12Js, /CY_V21_BUILD12_DESKTOP = '\(min-width: 1024px\)'/);
assert.match(build12Js, /link\.href = '\/v021b12\.css'/);
assert.doesNotThrow(() => new Function(build8Js), 'Build 8/9 JavaScript must parse');
assert.doesNotThrow(() => new Function(build10Js), 'Build 10 JavaScript must parse');
assert.doesNotThrow(() => new Function(build11Js), 'Build 11 JavaScript must parse');
assert.doesNotThrow(() => new Function(build12Js), 'Build 12 JavaScript must parse');

// Breakpoint isolation: phone remains Build 10, Tablet remains Build 9, Build 12 is Desktop-only.
assert.match(build10Css, /@media \(max-width: 767px\)/);
assert.doesNotMatch(build10Css, /@media \(min-width: 768px\)/);
assert.match(build11Css, /@media \(min-width: 768px\)/);
assert.doesNotMatch(build11Css, /@media \(max-width:/);
assert.match(build12Css, /@media \(min-width: 1024px\)/);
assert.doesNotMatch(build12Css, /@media \(max-width:/);
assert.match(build11Css, /#mobileAccountMenuButton,[\s\S]*?#mobileLedgerMoreButton,[\s\S]*?#mobileMainNav[\s\S]*?display:\s*none !important;/);

// Approved Desktop split, slider and entry mode tints remain intact.
assert.match(build1Css, /@media \(min-width: 1360px\)/);
assert.match(build1Css, /grid-template-columns:\s*minmax\(380px, 420px\) minmax\(0, 1fr\)/);
assert.match(build1Css, /\.entry-card\.entry-income\s*\{[\s\S]*?background:\s*#f4fbf6;/);
assert.match(build1Css, /\.entry-card\.entry-expense\s*\{[\s\S]*?background:\s*#fff6f5;/);
for (const css of [baseCss, build3Css, build5Css, build6Css, build7Css, build8Css, build11Css, build12Css]) {
  assert.doesNotMatch(css, /\.entry-kind-switch\s*\{/);
}

// Desktop header account cluster remains Settings -> identity/role -> Logout.
assert.match(v021, /actions\.insertBefore\(settings, accountCluster\)/);
assert.match(v021, /accountCluster\.append\(currentUser\)/);
assert.match(v021, /accountCluster\.append\(logout\)/);
assert.match(build8Css, /\.current-user\.role-super-admin\s*\{[\s\S]*?#fff4d6/);
assert.match(build8Css, /\.current-user\.role-admin\s*\{[\s\S]*?#f8ebe1/);
assert.match(build12Css, /\.current-user-main,[\s\S]*?\.current-user-role\s*\{[\s\S]*?font-size:\s*13\.5px !important;/);
assert.match(build12Css, /\.current-user-main\s*\{[\s\S]*?font-weight:\s*600 !important;/);
assert.match(build12Css, /\.current-user-role::before\s*\{[\s\S]*?content:\s*"\[" !important;/);
assert.match(build12Css, /\.current-user-role::after\s*\{[\s\S]*?content:\s*"\]";/);

// Supporting Desktop copy is one tier larger while the approved headings/month context are not redefined.
assert.match(build12Css, /#monthSummary,[\s\S]*?font-size:\s*13\.5px !important;/);
assert.match(build12Css, /\.ledger-card th\s*\{[\s\S]*?font-size:\s*13\.5px !important;/);
assert.match(build12Css, /#ledgerSummarySearch\s*\{[\s\S]*?font-size:\s*14px !important;/);
assert.match(build12Css, /\.entry-field-label,[\s\S]*?font-size:\s*14px !important;/);
assert.doesNotMatch(build12Css, /\.topbar h1\s*\{/);

// Quick entry: summary gets the flexible width, amount keeps a seven-digit footprint and Save is smaller.
assert.match(build12Css, /\.entry-grid\s*\{[\s\S]*?grid-template-columns:\s*minmax\(0, 1fr\) 86px 78px !important;/);
assert.match(build12Css, /\.entry-grid > #saveButton\s*\{[\s\S]*?width:\s*78px !important;[\s\S]*?min-width:\s*78px !important;/);
assert.match(build12Js, /summary\.placeholder = '最多20個字'/);
assert.match(build12Js, /editSummary\.placeholder = '最多20個字'/);

// Summary business rule still mirrors Desktop: 40 weighted units = 20 CJK / 40 ASCII.
assert.match(build8Js, /CY_V21_BUILD8_SUMMARY_UNITS = 40/);
assert.match(build8Js, /function v21Build8WeightedUnits\(value\)/);
assert.match(appV19, /SUMMARY_MAX_UNITS = 40/);
assert.match(appV19, /SUMMARY_TOO_LONG/);

// Enter flow remains date -> summary -> amount -> save, then returns to summary.
assert.match(v03, /enterStep\(els\.txDate, \(\) => els\.summary\?\.focus\(\)\)/);
assert.match(v03, /enterStep\(els\.summary,[\s\S]*?els\.amount\?\.focus\(\)/);
assert.match(v03, /enterStep\(els\.amount,[\s\S]*?els\.form\.requestSubmit\(\)/);
assert.match(v03, /cyFocusSummaryAfterSave[\s\S]*?els\.summary\.focus\(\)/);
assert.match(v07, /event\.key !== 'Tab'/);

// Custom Desktop month picker replaces only the presentation; monthFilter remains the single source of truth.
assert.equal((build12Js.match(/'一月'/g) || []).length, 1);
assert.match(build12Js, /CY_V21_BUILD12_MONTHS = \['一月',[\s\S]*?'十二月'\]/);
assert.match(build12Js, /root\.id = 'ledgerMonthPickerCustom'/);
assert.match(build12Js, /id="ledgerMonthPickerYearButton"/);
assert.match(build12Js, /data-picker-nav="-1"/);
assert.match(build12Js, /Array\.from\(\{ length: 12 \}/);
assert.match(build12Js, /yearStart \+= delta \* 12/);
assert.match(build12Js, /input\.value = `\$\{displayYear\}-\$\{String\(month\)\.padStart\(2, '0'\)\}`/);
assert.match(build12Js, /input\.dispatchEvent\(new Event\('change', \{ bubbles: true \}\)\)/);
assert.match(build12Css, /#ledgerMonthSlot > \.month-picker\s*\{[\s\S]*?clip-path:\s*inset\(50%\) !important;/);
assert.match(build12Css, /\.v21-month-picker-grid\s*\{[\s\S]*?grid-template-columns:\s*repeat\(4, minmax\(0, 1fr\)\);/);
assert.match(v06, /function moveLedgerMonth\(delta\)/);
assert.match(v06, /els\.monthFilter\.dispatchEvent\(new Event\('change', \{ bubbles: true \}\)\)/);

// Ledger summary/tool hierarchy and export behavior remain unchanged.
assert.match(v06, /期初 <strong>\$\{money\(openingTotal\)\}<\/strong>/);
assert.match(v013, /button\.textContent = '匯出中…'/);
assert.match(v013, /button\.setAttribute\('aria-busy', 'true'\)/);

// Build 4 observer hotfix remains protected.
assert.match(v021, /if \(account\.textContent !== nextText\) account\.textContent = nextText;/);
assert.match(v021, /if \(empty && empty\.textContent !== '本次尚無輸入紀錄。'\) empty\.textContent = '本次尚無輸入紀錄。';/);

console.log('V0.21.0 Build 12 desktop regression tests passed.');
