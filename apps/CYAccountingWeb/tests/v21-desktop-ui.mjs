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
const css = read('public/v021.css');
const build1Css = read('public/v021b1.css');
const build2Css = read('public/v021b2.css');
const build3Css = read('public/v021b3.css');
const build5Css = read('public/v021b5.css');
const build6Css = read('public/v021b6.css');
const build7Css = read('public/v021b7.css');
const build8Css = read('public/v021b8.css');
const build9Css = read('public/v021b9.css');
const js = read('public/v021.js');
const build8Js = read('public/v021b8.js');
const appV19 = read('src/app-v19.js');
const v03 = read('public/v03.js');
const v06 = read('public/v06.js');
const v07 = read('public/v07.js');
const v011 = read('public/v011.js');
const v013 = read('public/v013.js');

assert.equal(version, '0.21.0');
assert.equal(build, '9');
assert.match(html, /href="\/v021\.css"/);
assert.match(html, /src="\/v021\.js"/);
assert.match(html, /src="\/v021b8\.js"/);
assert.ok(html.indexOf('/v021.css') > html.indexOf('/v020.css'), 'v021.css must load after v020.css');
assert.ok(html.indexOf('/v021.js') > html.indexOf('/v020.js'), 'v021.js must load after v020.js');
assert.ok(html.indexOf('/v021b8.js') > html.indexOf('/v021.js'), 'current V0.21 overlay must load after v021.js');
assert.match(js, /CY_V21_VERSION = 'V0\.21\.0 Build 7'/);
assert.match(build8Js, /CY_V21_BUILD8_VERSION = 'V0\.21\.0 Build 9'/);
assert.match(build8Js, /link\.href = '\/v021b8\.css'/);
assert.match(build8Js, /link\.href = '\/v021b9\.css'/);
assert.doesNotThrow(() => new Function(build8Js), 'current V0.21 browser JavaScript must parse');

// Desktop presentation from Builds 1-8 remains isolated from Tablet/Mobile.
for (const stylesheet of [css, build3Css, build5Css, build6Css, build7Css, build8Css]) {
  assert.match(stylesheet, /@media \(min-width: 1024px\)/);
  assert.doesNotMatch(stylesheet, /@media \(max-width:/);
}
assert.match(build9Css, /@media \(min-width: 768px\) and \(max-width: 1023px\)/);
assert.match(build9Css, /@media \(max-width: 767px\)/);

// Modern business surfaces and wide split remain intact.
assert.match(css, /\.topbar\s*\{[\s\S]*?backdrop-filter:\s*blur\(12px\)/);
assert.match(css, /\.card\s*\{[\s\S]*?border-radius:\s*14px;[\s\S]*?box-shadow:/);
assert.match(build1Css, /@media \(min-width: 1360px\)/);
assert.match(build1Css, /\.shell\.v21-split-layout\s*\{[\s\S]*?grid-template-columns:\s*minmax\(380px, 420px\) minmax\(0, 1fr\)/);
assert.match(build1Css, /\.v21-entry-rail\s*\{[\s\S]*?position:\s*sticky;[\s\S]*?top:\s*88px;/);
assert.match(js, /CY_V21_SPLIT_MEDIA = '\(min-width: 1360px\)'/);

// Approved slider itself and income/expense mode tint remain protected.
for (const stylesheet of [css, build1Css, build2Css, build3Css, build5Css, build6Css, build7Css, build8Css, build9Css]) {
  assert.doesNotMatch(stylesheet, /\.entry-kind-switch\s*\{/);
}
assert.match(build5Css, /\.entry-card \.entry-kind-switch-field\s*\{[\s\S]*?background:\s*transparent !important;[\s\S]*?box-shadow:\s*none;/);
assert.match(build1Css, /\.entry-card\.entry-income\s*\{[\s\S]*?background:\s*#f4fbf6;/);
assert.match(build1Css, /\.entry-card\.entry-expense\s*\{[\s\S]*?background:\s*#fff6f5;/);

// Input confirmation remains inline below entry on wide Desktop.
assert.match(build1Css, /\.confirmation-drawer\.v21-inline-confirmation\s*\{[\s\S]*?position:\s*relative;/);
assert.match(js, /heading\.textContent = '最近輸入'/);
assert.match(js, /hint\.textContent = '最近 10 筆'/);

// Typography hierarchy and ledger context remain as accepted.
assert.match(build3Css, /\.primary,[\s\S]*?\.secondary\s*\{[\s\S]*?font-weight:\s*500;/);
assert.match(build3Css, /\.entry-card #saveButton\s*\{[\s\S]*?width:\s*110px;[\s\S]*?font-weight:\s*600;/);
assert.match(build3Css, /th\s*\{[\s\S]*?font-size:\s*12\.5px;[\s\S]*?font-weight:\s*550;/);
assert.match(build3Css, /td\s*\{[\s\S]*?font-size:\s*13\.5px;[\s\S]*?font-weight:\s*400;/);
assert.match(js, /setupV21LedgerContext\(\)/);
assert.match(js, /context\.className = 'v21-ledger-context'/);
assert.match(build3Css, /#monthFilter\s*\{[\s\S]*?font-size:\s*21px;[\s\S]*?font-weight:\s*600;/);
assert.match(build5Css, /\.entry-card \.section-title h2,[\s\S]*?\.ledger-title h2\s*\{[\s\S]*?font-size:\s*17px;[\s\S]*?font-weight:\s*600;/);

// Keyboard help is on demand; current copy reflects the reduced Enter flow.
assert.match(build3Css, /\.keyboard-hint\s*\{[\s\S]*?display:\s*none !important;/);
assert.match(js, /button\.id = 'entryHelpButton'/);
assert.match(js, /popover\.id = 'entryHelpPopover'/);
assert.match(build8Js, /日期 Enter → 摘要 Enter → 金額 Enter 儲存 → 回摘要/);

// Excel import remains in Settings > Data Management; export stays contained in its button.
assert.match(js, /tab\.dataset\.settingsTab = 'data'/);
assert.match(js, /button\.textContent = '匯入 Excel'/);
assert.match(build7Css, /\.settings-pane\.v21-data-pane\s*\{[\s\S]*?display:\s*none;/);
assert.match(build7Css, /\.settings-pane\.v21-data-pane\.active\s*\{[\s\S]*?display:\s*grid;/);
assert.match(v013, /button\.textContent = '匯出中…'/);
assert.match(v013, /button\.setAttribute\('aria-busy', 'true'\)/);
assert.doesNotMatch(v013, /setExportStatus\('已下載'\)/);
assert.match(build5Css, /#ledgerExcelExport\s*\{[\s\S]*?min-width:\s*82px;/);

// Account grouping remains on the Account header and chronological order mirrors Desktop semantics.
assert.doesNotMatch(v06, /<button id="ledgerGroupToggle"/);
assert.match(v06, /const visible = \(query[\s\S]*?\)\.sort\(compareLedgerChronological\);/);
assert.match(v06, /id="ledgerAccountHeader"/);
assert.match(v06, /cyLedgerGroupByAccount = !cyLedgerGroupByAccount/);
assert.match(v06, /accountLabel = cyLedgerGroupByAccount \? '帳戶 ▲' : '帳戶'/);

// Header/account cluster and role cues remain intact.
assert.match(js, /role === 'SUPER_ADMIN'/);
assert.match(js, /return '超級管理員'/);
assert.match(js, /accountCluster\.className = 'v21-account-cluster'/);
assert.match(js, /actions\.insertBefore\(settings, accountCluster\)/);
assert.match(build8Js, /target\.classList\.toggle\('role-super-admin', role === '超級管理員'\)/);
assert.match(build8Js, /target\.classList\.toggle\('role-admin', role === '管理員'\)/);
assert.match(build8Css, /\.current-user\.role-super-admin\s*\{[\s\S]*?#fff4d6/);
assert.match(build8Css, /\.current-user\.role-admin\s*\{[\s\S]*?#f8ebe1/);
assert.match(build8Css, /\.current-user-main\s*\{[\s\S]*?font-size:\s*13\.5px;[\s\S]*?font-weight:\s*600;/);

// Summary row owns opening-balance/month-lock actions and Opening Balance remains compact.
assert.match(js, /summaryBar\.className = 'v21-ledger-summary-bar'/);
assert.match(js, /summaryActions\.append\(openingButton\)/);
assert.match(js, /lockButton\.id = 'ledgerLockSettingsButton'/);
assert.match(build6Css, /\.opening-modal\s*\{[\s\S]*?width:\s*min\(420px, calc\(100% - 32px\)\);/);
assert.match(build6Css, /\.opening-modal \.modal-header p\s*\{[\s\S]*?display:\s*none;/);

// Build 7 UI copy cleanup and compact settings remain protected.
assert.match(js, /function cleanupV21InterfaceCopy\(\)/);
assert.match(js, /\.auth-note/);
assert.match(build7Css, /\.settings-modal\s*\{[\s\S]*?width:\s*min\(820px, calc\(100% - 36px\)\);/);

// Desktop quick-entry structure from Build 8 remains: account buttons, date/category, summary/amount/save.
assert.match(html, /id="entryAccountChoiceRow"/);
assert.match(html, /id="entryAccountButtons"[\s\S]*?role="radiogroup"/);
assert.match(html, /class="account-source-field"[\s\S]*?id="accountName"/);
assert.match(build8Css, /\.entry-account-buttons\s*\{[\s\S]*?display:\s*flex;[\s\S]*?flex-wrap:\s*wrap;/);
assert.match(build8Css, /\.entry-grid\s*\{[\s\S]*?grid-template-columns:\s*minmax\(0, 1\.35fr\) minmax\(100px, \.85fr\) 110px;/);
assert.match(build8Css, /\.entry-grid > \.account-source-field\s*\{[\s\S]*?display:\s*none !important;/);
assert.match(build8Js, /select\.dispatchEvent\(new Event\('change', \{ bubbles: true \}\)\)/);

// Supporting text readability stays enlarged without changing the accepted primary headings/month scale.
assert.match(build8Css, /label > span,[\s\S]*?font-size:\s*13px;/);
assert.match(build8Css, /\.entry-card input,[\s\S]*?\.entry-card select\s*\{[\s\S]*?font-size:\s*15px;/);
assert.match(build8Css, /th\s*\{[\s\S]*?font-size:\s*13px;/);
assert.match(build8Css, /td\s*\{[\s\S]*?font-size:\s*14\.5px;/);
assert.doesNotMatch(build8Css, /\.topbar h1\s*\{/);
assert.doesNotMatch(build8Css, /#monthFilter\s*\{/);

// Summary limit mirrors Desktop: 40 weighted units and Worker validation.
assert.match(html, /id="summary"[^>]*maxlength="40"[^>]*最多20個中文字/);
assert.match(html, /id="editSummary"[^>]*maxlength="40"/);
assert.match(build8Js, /CY_V21_BUILD8_SUMMARY_UNITS = 40/);
assert.match(build8Js, /function v21Build8WeightedUnits\(value\)/);
assert.match(appV19, /SUMMARY_MAX_UNITS = 40/);
assert.match(appV19, /SUMMARY_TOO_LONG/);

// Current Enter flow is date -> summary -> amount -> submit -> summary.
assert.match(v03, /enterStep\(els\.txDate, \(\) => els\.summary\?\.focus\(\)\)/);
assert.match(v03, /enterStep\(els\.summary,[\s\S]*?els\.amount\?\.focus\(\)/);
assert.match(v03, /enterStep\(els\.amount,[\s\S]*?els\.form\.requestSubmit\(\)/);
assert.doesNotMatch(v03, /const flow = \[els\.txDate, els\.accountName, els\.categoryName/);
assert.match(v03, /cyFocusSummaryAfterSave[\s\S]*?els\.summary\.focus\(\)/);

// Build 4 hotfix remains protected: MutationObserver callbacks stay idempotent.
assert.match(js, /const nextText = active \? '帳戶 ▲' : '帳戶';/);
assert.match(js, /if \(account\.textContent !== nextText\) account\.textContent = nextText;/);
assert.match(js, /if \(empty && empty\.textContent !== '本次尚無輸入紀錄。'\) empty\.textContent = '本次尚無輸入紀錄。';/);

// Plain Tab remains scoped to entry inputs/selects and Shift+Tab stays native.
assert.match(v07, /els\.form\?\.addEventListener\('keydown'/);
assert.match(v07, /event\.key !== 'Tab'/);
assert.match(v07, /event\.shiftKey/);
assert.match(v07, /event\.target\.matches\('input, select'\)/);
assert.doesNotMatch(v07, /event\.key !== 'F2'/);
assert.match(v011, /<kbd>Tab<\/kbd> 切換收入／支出/);

console.log('V0.21.0 Build 9 desktop regression tests passed.');
