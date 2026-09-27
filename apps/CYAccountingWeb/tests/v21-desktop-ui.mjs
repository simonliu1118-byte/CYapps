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
const js = read('public/v021.js');
const v06 = read('public/v06.js');
const v07 = read('public/v07.js');
const v011 = read('public/v011.js');

assert.equal(version, '0.21.0');
assert.equal(build, '4');
assert.match(html, /href="\/v021\.css"/);
assert.match(html, /src="\/v021\.js"/);
assert.ok(html.indexOf('/v021.css') > html.indexOf('/v020.css'), 'v021.css must load after v020.css');
assert.ok(html.indexOf('/v021.js') > html.indexOf('/v020.js'), 'v021.js must load after v020.js');
assert.match(js, /CY_V21_VERSION = 'V0\.21\.0 Build 4'/);
assert.match(js, /ensureV21Build1Stylesheet\(\)/);
assert.match(js, /ensureV21Build2Stylesheet\(\)/);
assert.match(js, /ensureV21Build3Stylesheet\(\)/);
assert.match(js, /link\.href = '\/v021b1\.css'/);
assert.match(js, /link\.href = '\/v021b2\.css'/);
assert.match(js, /link\.href = '\/v021b3\.css'/);

// Base Desktop redesign remains isolated from Tablet/Mobile.
assert.match(css, /@media \(min-width: 1024px\)/);
assert.doesNotMatch(css, /@media \(max-width:/);
assert.match(build3Css, /@media \(min-width: 1024px\)/);
assert.doesNotMatch(build3Css, /@media \(max-width:/);

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

// Approved slider and mode tint remain untouched by all V0.21 build overlays.
for (const stylesheet of [css, build1Css, build2Css, build3Css]) {
  assert.doesNotMatch(stylesheet, /\.entry-kind-switch/);
  assert.doesNotMatch(stylesheet, /\.entry-kind-switch-field/);
}
assert.match(build1Css, /\.entry-card\.entry-income\s*\{[\s\S]*?background:\s*#f4fbf6;/);
assert.match(build1Css, /\.entry-card\.entry-expense\s*\{[\s\S]*?background:\s*#fff6f5;/);

// Input confirmation remains inline below entry on wide Desktop.
assert.match(build1Css, /\.confirmation-drawer\.v21-inline-confirmation\s*\{[\s\S]*?position:\s*relative;/);
assert.match(build1Css, /body\.v21-wide-split > \.confirmation-edge-open/);
assert.match(build1Css, /\.v21-inline-confirmation \.confirmation-list\s*\{[\s\S]*?overflow-y:\s*auto;/);
assert.match(js, /heading\.textContent = '最近輸入'/);
assert.match(js, /hint\.textContent = '最近 10 筆'/);

// Build 3: typography hierarchy is lighter and data has more visual weight than toolbar controls.
assert.match(build3Css, /\.primary,[\s\S]*?\.secondary\s*\{[\s\S]*?font-weight:\s*500;/);
assert.match(build3Css, /\.entry-card #saveButton\s*\{[\s\S]*?width:\s*110px;[\s\S]*?font-weight:\s*600;/);
assert.match(build3Css, /\.ledger-desktop-tools \.secondary\.compact\s*\{[\s\S]*?height:\s*28px;[\s\S]*?font-size:\s*12px;[\s\S]*?font-weight:\s*500;/);
assert.match(build3Css, /th\s*\{[\s\S]*?font-size:\s*12\.5px;[\s\S]*?font-weight:\s*550;/);
assert.match(build3Css, /td\s*\{[\s\S]*?font-size:\s*13\.5px;[\s\S]*?font-weight:\s*400;/);
assert.match(build3Css, /td\.num,[\s\S]*?\.ledger-balance\s*\{[\s\S]*?font-weight:\s*500;/);

// Month is promoted to the primary ledger context and no longer reads as a toolbar button.
assert.match(js, /setupV21LedgerContext\(\)/);
assert.match(js, /context\.className = 'v21-ledger-context'/);
assert.match(js, /context\.append\(monthTools\)/);
assert.match(build3Css, /#monthFilter\s*\{[\s\S]*?font-size:\s*21px;[\s\S]*?font-weight:\s*600;/);
assert.match(build3Css, /#ledgerPrevMonth,[\s\S]*?#ledgerNextMonth\s*\{[\s\S]*?width:\s*28px;[\s\S]*?background:\s*transparent;/);

// Keyboard help becomes on-demand instead of permanent visual noise.
assert.match(build3Css, /\.keyboard-hint\s*\{[\s\S]*?display:\s*none !important;/);
assert.match(js, /button\.id = 'entryHelpButton'/);
assert.match(js, /popover\.id = 'entryHelpPopover'/);
assert.match(js, /快速輸入說明/);
assert.match(js, /Ctrl \+ ↑↓/);

// Low-frequency Excel import moves into Settings > Data Management, while export stays in ledger tools.
assert.match(js, /tab\.dataset\.settingsTab = 'data'/);
assert.match(js, /tab\.textContent = '資料管理'/);
assert.match(js, /host\.append\(button\)/);
assert.match(js, /button\.textContent = '匯入 Excel'/);
assert.match(js, /帳戶、科目、資料與鎖帳/);
assert.match(build3Css, /\.v21-data-pane/);

// Account grouping is controlled only from the Account table header; normal ledger order follows Desktop date ordering.
assert.doesNotMatch(v06, /<button id="ledgerGroupToggle"/);
assert.match(v06, /const visible = \(query[\s\S]*?\)\.sort\(compareLedgerChronological\);/);
assert.match(v06, /cyLedgerGroupByAccount = false;[\s\S]*?scheduleLedgerDesktopRefresh\(\)/);
assert.match(v06, /id="ledgerAccountHeader"/);
assert.match(v06, /cyLedgerGroupByAccount = !cyLedgerGroupByAccount/);
assert.match(v06, /new Intl\.Collator\('zh-Hant-TW'/);
assert.match(v06, /accountLabel = cyLedgerGroupByAccount \? '帳戶 ▲' : '帳戶'/);

// Empty-state and wide-ledger height no longer create a full-screen blank card.
assert.match(js, /class="ledger-empty-state"/);
assert.match(build3Css, /\.ledger-empty-state\s*\{[\s\S]*?min-height:\s*180px;/);
assert.match(build3Css, /\.v21-split-layout > \.ledger-card\s*\{[\s\S]*?min-height:\s*0;[\s\S]*?align-self:\s*start;/);

// Header exposes the existing session role without changing Identity/authorization semantics.
assert.match(js, /fetch\('\/api\/auth\/me'/);
assert.match(js, /role === 'SUPER_ADMIN'/);
assert.match(js, /return '超級管理員'/);
assert.match(js, /role === 'ADMIN'/);
assert.match(js, /return '管理員'/);
assert.match(build3Css, /\.current-user-role\s*\{[\s\S]*?font-weight:\s*400;/);

// Build 4 hotfix: MutationObserver callbacks must be idempotent and not trigger themselves forever.
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

console.log('V0.21.0 Build 4 desktop hierarchy regression tests passed.');
