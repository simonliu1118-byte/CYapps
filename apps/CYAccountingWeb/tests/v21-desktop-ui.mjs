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
const js = read('public/v021.js');
const build8Js = read('public/v021b8.js');
const appV19 = read('src/app-v19.js');
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
assert.match(js, /ensureV21Build1Stylesheet\(\)/);
assert.match(js, /ensureV21Build2Stylesheet\(\)/);
assert.match(js, /ensureV21Build3Stylesheet\(\)/);
assert.match(js, /ensureV21Build5Stylesheet\(\)/);
assert.match(js, /ensureV21Build6Stylesheet\(\)/);
assert.match(js, /ensureV21Build7Stylesheet\(\)/);
assert.match(js, /link\.href = '\/v021b1\.css'/);
assert.match(js, /link\.href = '\/v021b2\.css'/);
assert.match(js, /link\.href = '\/v021b3\.css'/);
assert.match(js, /link\.href = '\/v021b5\.css'/);
assert.match(js, /link\.href = '\/v021b6\.css'/);
assert.match(js, /link\.href = '\/v021b7\.css'/);

// Desktop redesign remains isolated from Tablet/Mobile through Build 8; Build 9 is the adaptive layer.
for (const stylesheet of [css, build3Css, build5Css, build6Css, build7Css, build8Css]) {
  assert.match(stylesheet, /@media \(min-width: 1024px\)/);
  assert.doesNotMatch(stylesheet, /@media \(max-width:/);
}

// Modern business surfaces remain in the base V0.21 layer.
assert.match(css, /\.topbar\s*\{[\s\S]*?backdrop-filter:\s*blur\(12px\)/);
assert.match(css, /\.card\s*\{[\s\S]*?border-radius:\s*14px;[\s\S]*?box-shadow:/);
assert.match(css, /\.ledger-desktop-tools\s*\{[\s\S]*?background:\s*#fafbfd;/);
assert.match(css, /tbody tr:hover td\s*\{[\s\S]*?background:\s*#f8fbff;/);
assert.match(css, /\.settings-tab\.active\s*\{[\s\S]*?background:\s*#fff;[\s\S]*?box-shadow:/);
assert.match(css, /\.modal\s*\{[\s\S]*?border-radius:\s*16px;[\s\S]*?box-shadow:/);

// Build 1: wide Desktop remains left quick-entry + right ledger.
assert.match(build1Css, /@media \(min-width: 1360px\)/);
assert.match(build1Css, /\.shell\.v21-split-layout\s*\{[\s\S]*?grid-template-columns:\s*minmax\(380px, 420px\) minmax\(0, 1fr\)/);
assert.match(build1Css, /\.v21-entry-rail\s*\{[\s\S]*?position:\s*sticky;[\s\S]*?top:\s*88px;/);
assert.match(js, /CY_V21_SPLIT_MEDIA = '\(min-width: 1360px\)'/);
assert.match(js, /document\.createElement\('aside'\)/);
assert.match(js, /rail\.prepend\(entry\)/);
assert.match(js, /rail\.append\(confirmation\)/);
assert.match(js, /document\.body\.append\(confirmation\)/);
assert.match(js, /setConfirmationDrawer\(true, false\)/);

// Approved slider itself and mode tint remain intact.
for (const stylesheet of [css, build1Css, build2Css, build3Css, build6Css, build7Css, build8Css]) {
  assert.doesNotMatch(stylesheet, /\.entry-kind-switch\s*\{/);
}
assert.doesNotMatch(build5Css, /\.entry-kind-switch\s*\{/);
assert.match(build5Css, /\.entry-card \.entry-kind-switch-field\s*\{[\s\S]*?background:\s*transparent !important;[\s\S]*?box-shadow:\s*none;/);
assert.match(build1Css, /\.entry-card\.entry-income\s*\{[\s\S]*?background:\s*#f4fbf6;/);
assert.match(build1Css, /\.entry-card\.entry-expense\s*\{[\s\S]*?background:\s*#fff6f5;/);

// Input confirmation remains inline below entry on wide Desktop.
assert.match(build1Css, /\.confirmation-drawer\.v21-inline-confirmation\s*\{[\s\S]*?position:\s*relative;/);
assert.match(build1Css, /body\.v21-wide-split > \.confirmation-edge-open/);
assert.match(build1Css, /\.v21-inline-confirmation \.confirmation-list\s*\{[\s\S]*?overflow-y:\s*auto;/);
assert.match(js, /heading\.textContent = '最近輸入'/);
assert.match(js, /hint\.textContent = '最近 10 筆'/);

// Build 3 typography hierarchy stays lighter and data keeps more visual weight than toolbar controls.
assert.match(build3Css, /\.primary,[\s\S]*?\.secondary\s*\{[\s\S]*?font-weight:\s*500;/);
assert.match(build3Css, /\.entry-card #saveButton\s*\{[\s\S]*?width:\s*110px;[\s\S]*?font-weight:\s*600;/);
assert.match(build3Css, /\.ledger-desktop-tools \.secondary\.compact\s*\{[\s\S]*?height:\s*28px;[\s\S]*?font-size:\s*12px;[\s\S]*?font-weight:\s*500;/);
assert.match(build3Css, /th\s*\{[\s\S]*?font-size:\s*12\.5px;[\s\S]*?font-weight:\s*550;/);
assert.match(build3Css, /td\s*\{[\s\S]*?font-size:\s*13\.5px;[\s\S]*?font-weight:\s*400;/);
assert.match(build3Css, /td\.num,[\s\S]*?\.ledger-balance\s*\{[\s\S]*?font-weight:\s*500;/);

// Month is the primary ledger context and workspace titles are peers.
assert.match(js, /setupV21LedgerContext\(\)/);
assert.match(js, /context\.className = 'v21-ledger-context'/);
assert.match(js, /context\.append\(monthTools\)/);
assert.match(build3Css, /#monthFilter\s*\{[\s\S]*?font-size:\s*21px;[\s\S]*?font-weight:\s*600;/);
assert.match(build5Css, /\.entry-card \.section-title h2,[\s\S]*?\.ledger-title h2\s*\{[\s\S]*?font-size:\s*17px;[\s\S]*?font-weight:\s*600;/);

// Keyboard help remains on demand.
assert.match(build3Css, /\.keyboard-hint\s*\{[\s\S]*?display:\s*none !important;/);
assert.match(js, /button\.id = 'entryHelpButton'/);
assert.match(js, /popover\.id = 'entryHelpPopover'/);
assert.match(js, /快速輸入說明/);
assert.match(js, /Ctrl \+ ↑↓/);

// Low-frequency Excel import remains in Settings > Data Management and the pane stays tab-scoped.
assert.match(js, /tab\.dataset\.settingsTab = 'data'/);
assert.match(js, /tab\.textContent = '資料管理'/);
assert.match(js, /host\.append\(button\)/);
assert.match(js, /button\.textContent = '匯入 Excel'/);
assert.match(build7Css, /\.settings-pane\.v21-data-pane\s*\{[\s\S]*?display:\s*none;/);
assert.match(build7Css, /\.settings-pane\.v21-data-pane\.active\s*\{[\s\S]*?display:\s*grid;/);

// Account grouping is controlled only from the Account table header; normal ledger order follows Desktop date ordering.
assert.doesNotMatch(v06, /<button id="ledgerGroupToggle"/);
assert.match(v06, /const visible = \(query[\s\S]*?\)\.sort\(compareLedgerChronological\);/);
assert.match(v06, /cyLedgerGroupByAccount = false;[\s\S]*?scheduleLedgerDesktopRefresh\(\)/);
assert.match(v06, /id="ledgerAccountHeader"/);
assert.match(v06, /cyLedgerGroupByAccount = !cyLedgerGroupByAccount/);
assert.match(v06, /new Intl\.Collator\('zh-Hant-TW'/);
assert.match(v06, /accountLabel = cyLedgerGroupByAccount \? '帳戶 ▲' : '帳戶'/);

// Empty-state and wide-ledger height stay compact and avoid redundant helper prose.
assert.match(js, /class="ledger-empty-state"/);
assert.doesNotMatch(js, /新增記帳後，資料會顯示在這裡。/);
assert.match(build3Css, /\.ledger-empty-state\s*\{[\s\S]*?min-height:\s*180px;/);
assert.match(build3Css, /\.v21-split-layout > \.ledger-card\s*\{[\s\S]*?min-height:\s*0;[\s\S]*?align-self:\s*start;/);
assert.match(build7Css, /\.ledger-empty-state\s*\{[\s\S]*?min-height:\s*150px;/);

// Header keeps Settings before identity/logout; Build 8 adds subtle gold/bronze role cues.
assert.match(js, /fetch\('\/api\/auth\/me'/);
assert.match(js, /role === 'SUPER_ADMIN'/);
assert.match(js, /return '超級管理員'/);
assert.match(js, /setupV21HeaderLayout\(\)/);
assert.match(js, /brandLine\.className = 'v21-brand-line'/);
assert.match(js, /brandLine\.append\(status\)/);
assert.match(js, /accountCluster\.className = 'v21-account-cluster'/);
assert.match(js, /accountCluster\.append\(currentUser\)/);
assert.match(js, /accountCluster\.append\(logout\)/);
assert.match(js, /actions\.insertBefore\(settings, accountCluster\)/);
assert.match(build5Css, /\.v21-brand-line \.status\.ok\s*\{[\s\S]*?display:\s*none;/);
assert.match(build5Css, /\.v21-brand-line \.status\.warn\s*\{[\s\S]*?display:\s*inline-flex;/);
assert.match(build6Css, /\.v21-account-cluster\s*\{[\s\S]*?border:\s*1px solid #dde3ea;[\s\S]*?border-radius:\s*6px;/);
assert.match(build7Css, /\.topbar #settingsButton\s*\{[\s\S]*?order:\s*0 !important;/);
assert.match(build7Css, /\.v21-account-cluster\s*\{[\s\S]*?order:\s*1 !important;[\s\S]*?align-items:\s*center;/);
assert.match(build8Js, /target\.classList\.toggle\('role-super-admin', role === '超級管理員'\)/);
assert.match(build8Js, /target\.classList\.toggle\('role-admin', role === '管理員'\)/);
assert.match(build8Css, /\.current-user\.role-super-admin\s*\{[\s\S]*?linear-gradient[\s\S]*?#fff4d6/);
assert.match(build8Css, /\.current-user\.role-admin\s*\{[\s\S]*?linear-gradient[\s\S]*?#f8ebe1/);
assert.match(build8Css, /\.current-user-main\s*\{[\s\S]*?font-size:\s*13\.5px;[\s\S]*?font-weight:\s*600;/);

// Summary row owns opening-balance and month-lock actions; search stays left and export stays right.
assert.match(js, /summaryBar\.className = 'v21-ledger-summary-bar'/);
assert.match(js, /summaryActions\.className = 'v21-summary-actions'/);
assert.match(js, /summaryActions\.append\(openingButton\)/);
assert.match(js, /lockButton\.id = 'ledgerLockSettingsButton'/);
assert.match(js, /lockButton\.textContent = '鎖定月份'/);
assert.match(js, /setSettingsTab\('lock'\)/);
assert.match(build5Css, /\.ledger-desktop-tools\s*\{[\s\S]*?grid-template-columns:\s*minmax\(0, 1fr\) auto;/);
assert.match(build5Css, /\.ledger-search\s*\{[\s\S]*?justify-self:\s*start;/);
assert.match(build5Css, /\.ledger-view-tools\s*\{[\s\S]*?justify-self:\s*end;/);

// Excel export status is contained inside the fixed-width button and cannot push toolbar layout.
assert.match(v013, /button\.textContent = '匯出中…'/);
assert.match(v013, /button\.setAttribute\('aria-busy', 'true'\)/);
assert.match(v013, /button\.textContent = failed \? '匯出失敗' : defaultLabel/);
assert.doesNotMatch(v013, /setExportStatus\('已下載'\)/);
assert.match(build5Css, /#ledgerExcelExport\s*\{[\s\S]*?min-width:\s*82px;/);
assert.match(build5Css, /#ledgerExcelExportStatus\s*\{[\s\S]*?position:\s*absolute !important;/);

// Opening-balance dialog remains a compact utility surface while redundant helper copy is removed.
assert.match(build6Css, /\.opening-modal\s*\{[\s\S]*?width:\s*min\(420px, calc\(100% - 32px\)\);/);
assert.match(build6Css, /\.opening-modal \.modal-header p\s*\{[\s\S]*?display:\s*none;/);
assert.match(build6Css, /\.opening-modal \.opening-row\s*\{[\s\S]*?grid-template-columns:\s*minmax\(0, 1fr\) 138px;[\s\S]*?min-height:\s*42px;/);
assert.match(build7Css, /\.opening-modal \.opening-dialog-heading\s*\{[\s\S]*?justify-content:\s*flex-end;/);
assert.match(js, /#openingDialog \.opening-dialog-heading > \.hint/);

// Build 7 removes nonessential always-visible explanatory copy while keeping validation/status UI.
assert.match(js, /function cleanupV21InterfaceCopy\(\)/);
assert.match(js, /\.auth-note/);
assert.match(js, /#settingsDialog \[data-settings-pane="accounts"\] > \.hint/);
assert.match(js, /#settingsDialog \[data-settings-pane="backup"\] \.backup-security-note/);
assert.match(js, /#settingsDialog \[data-settings-pane="migration"\] \.migration-privacy-v19/);
assert.match(build7Css, /\.settings-modal\s*\{[\s\S]*?width:\s*min\(820px, calc\(100% - 36px\)\);/);
assert.match(build7Css, /\.settings-layout\s*\{[\s\S]*?grid-template-columns:\s*132px minmax\(0, 1fr\);[\s\S]*?min-height:\s*430px;/);

// Build 8 quick entry: account buttons first, date/category second, summary/amount/save third on Desktop.
assert.match(html, /id="entryAccountChoiceRow"/);
assert.match(html, /id="entryAccountButtons"[\s\S]*?role="radiogroup"/);
assert.match(html, /class="account-source-field"[\s\S]*?id="accountName"/);
assert.match(build8Css, /\.entry-account-buttons\s*\{[\s\S]*?display:\s*flex;[\s\S]*?flex-wrap:\s*wrap;/);
assert.match(build8Css, /\.entry-account-choice\.active\s*\{[\s\S]*?background:\s*#edf4fa;/);
assert.match(build8Css, /\.entry-grid\s*\{[\s\S]*?grid-template-columns:\s*minmax\(0, 1\.35fr\) minmax\(100px, \.85fr\) 110px;/);
assert.match(build8Css, /\.entry-grid > \.account-source-field\s*\{[\s\S]*?display:\s*none !important;/);
assert.match(build8Css, /\.entry-grid > \.date-field\s*\{[\s\S]*?grid-row:\s*1;/);
assert.match(build8Css, /\.entry-grid > \.category-field\s*\{[\s\S]*?grid-row:\s*1;/);
assert.match(build8Css, /\.entry-grid > \.summary-field\s*\{[\s\S]*?grid-row:\s*2;/);
assert.match(build8Css, /\.entry-grid > \.amount-field\s*\{[\s\S]*?grid-row:\s*2;/);
assert.match(build8Js, /row\.hidden = false/);
assert.match(build8Js, /select\.dispatchEvent\(new Event\('change', \{ bubbles: true \}\)\)/);

// Build 8 raises supporting text readability without changing approved headings/month scale.
assert.match(build8Css, /label > span,[\s\S]*?font-size:\s*13px;/);
assert.match(build8Css, /\.entry-card input,[\s\S]*?\.entry-card select\s*\{[\s\S]*?font-size:\s*15px;/);
assert.match(build8Css, /th\s*\{[\s\S]*?font-size:\s*13px;/);
assert.match(build8Css, /td\s*\{[\s\S]*?font-size:\s*14\.5px;/);
assert.match(build8Css, /\.settings-tab\s*\{[\s\S]*?font-size:\s*13\.5px;/);
assert.doesNotMatch(build8Css, /\.topbar h1\s*\{/);
assert.doesNotMatch(build8Css, /#monthFilter\s*\{/);

// Summary limit mirrors Desktop semantics: 40 weighted units, protected in UI and Worker.
assert.match(html, /id="summary"[^>]*maxlength="40"[^>]*最多20個中文字/);
assert.match(html, /id="editSummary"[^>]*maxlength="40"/);
assert.match(build8Js, /CY_V21_BUILD8_SUMMARY_UNITS = 40/);
assert.match(build8Js, /function v21Build8WeightedUnits\(value\)/);
assert.match(build8Js, /function v21Build8TrimWeighted\(value, maxUnits\)/);
assert.match(build8Js, /compositionstart/);
assert.match(build8Js, /compositionend/);
assert.match(appV19, /SUMMARY_MAX_UNITS = 40/);
assert.match(appV19, /summaryWeightedUnits\(body\.summary\) > SUMMARY_MAX_UNITS/);
assert.match(appV19, /SUMMARY_TOO_LONG/);
assert.match(appV19, /摘要不可超過 20 個中文字或 40 個英數字元/);

// Build 4 hotfix remains protected: MutationObserver callbacks must stay idempotent.
assert.match(js, /const nextText = active \? '帳戶 ▲' : '帳戶';/);
assert.match(js, /if \(account\.textContent !== nextText\) account\.textContent = nextText;/);
assert.match(js, /if \(empty && empty\.textContent !== '本次尚無輸入紀錄。'\) empty\.textContent = '本次尚無輸入紀錄。';/);

// Shortcut remains plain Tab inside the entry form only. Shift+Tab and dialogs retain native focus navigation.
assert.match(v07, /els\.form\?\.addEventListener\('keydown'/);
assert.match(v07, /event\.key !== 'Tab'/);
assert.match(v07, /event\.shiftKey/);
assert.match(v07, /event\.target\.matches\('input, select'\)/);
assert.doesNotMatch(v07, /event\.key !== 'F2'/);
assert.match(v011, /<kbd>Tab<\/kbd> 切換收入／支出/);
assert.doesNotMatch(v011, /<kbd>F2<\/kbd> 切換收入／支出/);
assert.match(js, /<kbd>Tab<\/kbd> 切換收入／支出/);

console.log('V0.21.0 Build 9 desktop regression tests passed.');
