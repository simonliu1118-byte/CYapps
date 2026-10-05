import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const HERE = path.dirname(fileURLToPath(import.meta.url));
const ROOT = path.resolve(HERE, '..');
const PUBLIC = path.join(ROOT, 'public');
const SRC = path.join(ROOT, 'src');
const TESTS = path.join(ROOT, 'tests');
const read = relative => fs.readFileSync(path.join(ROOT, relative), 'utf8');

const html = read('public/index.html');
const appJs = read('public/app.js');
const ledgerTools = read('public/ledger-tools.js');
const adaptiveUi = read('public/adaptive-ui.js');
const quickEntry = read('public/quick-entry.js');
const inlineEdit = read('public/ledger-inline-edit.js');
const inputConfirmation = read('public/input-confirmation.js');
const backupUi = read('public/backup-ui.js');
const excelExportUi = read('public/excel-export-ui.js');
const excelImportUi = read('public/excel-import-ui.js');
const adaptiveCss = read('public/adaptive-ui.css');
const accountingUiCss = read('public/accounting-ui.css');
const accountingTools = read('src/accounting-tools.js');
const coreApi = read('src/index.js');
const files = fs.readdirSync(PUBLIC);
const styles = [
  'app.css',
  'accounting-ui.css',
  'quick-entry-settings.css',
  'category-management.css',
  'excel-export.css',
  'ledger-inline-edit.css',
  'excel-import.css',
  'backup.css',
  'desktop-migration.css',
  'adaptive-ui.css'
];
const scripts = [
  'auth.js',
  'app.js',
  'quick-entry.js',
  'ledger-tools.js',
  'input-confirmation.js',
  'quick-entry-settings.js',
  'category-management.js',
  'excel-export-ui.js',
  'ledger-inline-edit.js',
  'excel-import-ui.js',
  'backup-ui.js',
  'desktop-migration-ui.js',
  'adaptive-ui.js'
];

assert.deepEqual(files.filter(name => /^v[0-9].*\.(?:js|css)$/.test(name)), [], 'versioned frontend assets must not exist');
assert.deepEqual(fs.readdirSync(SRC).filter(name => /^(?:app-v|v[0-9])/.test(name)), [], 'versioned backend wrappers/modules must not exist');
assert.deepEqual(fs.readdirSync(TESTS).filter(name => /^v[0-9].*\.mjs$/.test(name)), [], 'version-named regression tests must not exist');
assert.equal(files.includes('app-baseline.js'), false);
assert.equal(files.includes('app-baseline.css'), false);

let previous = -1;
for (const name of styles) {
  assert.equal(files.includes(name), true, name + ' must exist');
  const marker = 'href="/' + name;
  const at = html.indexOf(marker);
  assert.ok(at > previous, name + ' must load in semantic cascade order');
  previous = at;
}
previous = -1;
for (const name of scripts) {
  assert.equal(files.includes(name), true, name + ' must exist');
  const marker = 'src="/' + name;
  const at = html.indexOf(marker);
  assert.ok(at > previous, name + ' must load in semantic runtime order');
  previous = at;
  assert.doesNotThrow(() => new Function(read('public/' + name)), name + ' must parse');
}

const runtime = scripts.map(name => read('public/' + name)).join('\n');
assert.doesNotMatch(runtime, /__CYACC_BASELINE_BUNDLE__/);
assert.doesNotMatch(runtime, /script\.src\s*=\s*['"]\/v[0-9]/);
assert.doesNotMatch(runtime, /link\.href\s*=\s*['"]\/v[0-9]/);
assert.doesNotMatch(runtime, /querySelector\(['"]\.version['"]\)/, 'frontend feature modules must not own the global version element');
assert.doesNotMatch(runtime, /V0\.\d+\.\d+(?: Build \d+)?/, 'frontend feature modules must not embed application version strings');
assert.doesNotMatch(runtime, /\b[A-Za-z_$][A-Za-z0-9_$]*(?:V|v)\d{2,4}[A-Za-z0-9_$]*\b/, 'frontend runtime identifiers must be named by responsibility, not historical app versions');
const styleRuntime = styles.map(name => read('public/' + name)).join('\n');
assert.doesNotMatch(styleRuntime, /[-_](?:V|v)\d{2,4}(?:[-_A-Za-z0-9]|$)/, 'frontend CSS selectors must not keep historical app-version suffixes');
assert.doesNotMatch(html, /[?&](?:rev|v)=(?:0?\d|semantic\d)/, 'asset cache revisions must use semantic module names, not app/build numbers');
assert.match(appJs, /await window\.cyaccRefreshLedgerView\(month\)/, 'transaction loading must hand off to the canonical ledger renderer');
assert.match(ledgerTools, /window\.cyaccRefreshLedgerView = loadLedgerOpeningAndRender/, 'ledger tools must expose one canonical refresh path');
assert.match(ledgerTools, /window\.cyaccRenderLedgerMessage = renderLedgerMessage/, 'ledger tools must own ledger status and empty rows');
assert.match(ledgerTools, /new CustomEvent\('cyacc:ledger-rendered'/, 'canonical row writes must publish an explicit render lifecycle');
assert.match(ledgerTools, /ledger-empty-state/, 'empty-state markup must be rendered by the canonical ledger renderer');
assert.match(ledgerTools, /class="ledger-message-row"/, 'ledger status rows must be explicitly distinguished from transaction cards');
assert.doesNotMatch(backupUi, /backupSettingsHtml\s*=\s*function|renderBackupStatus\s*=\s*function|renderTieredBackupHistory\s*=\s*function/, 'backup UI must keep one canonical function path instead of patching functions after definition');
assert.match(backupUi, /function openMobileBackupInfo\(\)/, 'backup UI owner must provide the SA mobile read-only backup surface');
assert.doesNotMatch(ledgerTools, /new MutationObserver/, 'ledger rows must not trigger a second refresh through MutationObserver');
assert.match(ledgerTools, /monthFilter\?\.addEventListener\('change', syncLedgerMonthDisplay\)/, 'shared toolbar owns month presentation sync');
assert.doesNotMatch(ledgerTools, /monthFilter\?\.addEventListener\('change',\s*(?:loadLedgerOpeningAndRender|scheduleLedgerRefresh)/, 'ledger tools must not own a second month data-refresh path');
assert.doesNotMatch(appJs, /function renderTransactions\(|transactionRows\.innerHTML/, 'app state owner must not keep a second transaction row renderer');
assert.match(quickEntry, /addEventListener\('cyacc:ledger-rendered', handleFastEntryLedgerRendered\)/, 'quick entry must subscribe to the explicit ledger lifecycle');
assert.match(appJs, /CY_MOBILE_ENTRY_MEDIA = '\(max-width: 767px\)'[\s\S]*?function initialEntryKind\(\)[\s\S]*?'income'/, 'mobile entry must default to income from the canonical app state owner');
assert.match(appJs, /new CustomEvent\('cyacc:entry-kind-changed'/, 'entry kind changes must publish an explicit lifecycle');
assert.match(quickEntry, /addEventListener\('cyacc:entry-kind-changed', handleQuickEntryKindChanged\)/, 'quick entry must consume the canonical entry-kind lifecycle');
assert.match(quickEntry, /window\.cyPrepareQuickEntryUi = prepareQuickEntryUi/, 'quick entry must expose an explicit bootstrap preparation hook');
assert.match(appJs, /await window\.cyPrepareQuickEntryUi\?\.\(\)/, 'app bootstrap must await quick-entry presentation before startup completes');
assert.doesNotMatch(quickEntry, /new MutationObserver|connectionStatus[^\n]*已連線/, 'quick entry must not infer bootstrap readiness from DOM mutations or connection text');
assert.match(accountingTools, /url\.pathname === '\/api\/summaries\/frequent'/, 'accounting-tools must own the frequent-summary API');
assert.doesNotMatch(coreApi, /\/api\/summaries\/frequent|handleFrequentSummaries/, 'core API must not keep a second frequent-summary owner');
assert.doesNotMatch(adaptiveUi, /setupMobileEntryDateDisplay|cySyncMobileEntryDateDisplay|cy-mobile-entry-date-display/, 'mobile entry date must use the native date control without a display overlay');
assert.doesNotMatch(adaptiveCss, /cy-mobile-entry-date-display|#txDate[\s\S]*?-webkit-text-fill-color:\s*transparent/, 'mobile native date value must remain visible and tappable');
assert.match(html, /<input id="txDate" type="date" required>/, 'index.html must keep the native entry date control');
assert.match(adaptiveUi, /function setupTouchWorkspaceEntrySecondaryAction\(\)/, 'touch workspace must own the entry secondary action');
assert.match(adaptiveUi, /button\.textContent = cyTouchWorkspaceEdit \? '取消' : '清空'/, 'entry secondary action must switch between clear and cancel');
assert.match(adaptiveUi, /function cancelTouchWorkspaceMobileEditAndReturn\(\)[\s\S]*?switchTouchWorkspaceMobilePage\('ledger'\)[\s\S]*?restoreTouchWorkspaceLedgerContext\(context, false\)/, 'mobile edit cancel must return to the prior ledger context');
assert.doesNotMatch(quickEntry, /observe\(els\.transactionRows|ledgerObserver/, 'quick entry must not infer lifecycle from transaction row DOM mutations');
assert.match(inlineEdit, /els\.transactionRows\.addEventListener\('click'/, 'inline edit keeps normal delegated row interaction');
assert.doesNotMatch(inlineEdit, /cyLedgerObserver|suspendLedgerRefreshObserver|resumeLedgerRefreshObserver/, 'inline edit must not coordinate with a hidden ledger DOM observer');
assert.match(ledgerTools, /function setupLedgerToolbar\(\)/, 'ledger-tools must own one shared toolbar structure');
assert.match(ledgerTools, /function ledgerAccountVisual\(value\)/, 'canonical ledger renderer must resolve persisted account color slots');
assert.match(ledgerTools, /account\?\.color_slot/, 'ledger account colors must come from persisted account lifecycle data');
assert.match(ledgerTools, /const cycle = \(slot - 1\) % 40/, 'account visual slots must repeat only after 40 accounts');
assert.match(ledgerTools, /const dark = cycle >= 20/, 'slots 21-40 must use the dark-background palette');
const lightPalette = ledgerTools.match(/LEDGER_ACCOUNT_LIGHT_PALETTE = \[([\s\S]*?)\];/)?.[1]?.match(/#[0-9a-f]{6}/gi) || [];
const darkPalette = ledgerTools.match(/LEDGER_ACCOUNT_DARK_PALETTE = \[([\s\S]*?)\];/)?.[1]?.match(/#[0-9a-f]{6}/gi) || [];
assert.equal(lightPalette.length, 20, 'light account palette must contain exactly 20 colors');
assert.equal(darkPalette.length, 20, 'dark account palette must contain exactly 20 colors');
assert.doesNotMatch(ledgerTools, /Math\.imul\(|2166136261/, 'account colors must not be derived from account-name hashes');
assert.match(ledgerTools, /--ledger-account-bg:\$\{accountVisual\.background\};--ledger-account-fg:\$\{accountVisual\.foreground\}/, 'shared ledger rows must receive both account background and text colors');
assert.match(ledgerTools, /ledger-account-desktop ledger-account-color/, 'desktop/tablet ledger account label must use the shared color surface');
assert.match(ledgerTools, /ledger-account-mobile ledger-account-color/, 'mobile ledger account label must use the shared color surface');
assert.match(accountingUiCss, /\.ledger-account-color::before\s*\{[\s\S]*?background:\s*var\(--ledger-account-bg, transparent\)/, 'account color must be a visual background layer');
assert.match(accountingUiCss, /color:\s*var\(--ledger-account-fg, inherit\)/, 'dark account slots must be able to switch to light text');
const accountColorRule = accountingUiCss.match(/\.ledger-account-color\s*\{([\s\S]*?)\}/)?.[1] || '';
assert.doesNotMatch(accountColorRule, /\b(?:width|min-width|max-width|padding|margin)\s*:/, 'account color styling must not change account column geometry');
assert.match(ledgerTools, /id="ledgerBalanceButton"[\s\S]*?id="ledgerPrevMonth"[\s\S]*?id="ledgerMonthSlot"[\s\S]*?id="ledgerNextMonth"[\s\S]*?id="ledgerMoreButton"/, 'shared month toolbar must contain the device-neutral controls');
assert.match(ledgerTools, /id="ledgerOpeningBalanceButton"[\s\S]*?id="ledgerLockSettingsButton"/, 'opening and lock actions belong to the shared toolbar owner');
assert.match(ledgerTools, /id="ledgerExcelExport"[\s\S]*?id="ledgerExcelExportStatus"/, 'export control belongs to the shared toolbar structure');
assert.match(ledgerTools, /sheet\.id = 'ledgerToolsSheet'[\s\S]*?data-mobile-ledger-action="accounts"[\s\S]*?data-mobile-ledger-action="export"/, 'shared toolbar owns the compact utility menu');
assert.match(ledgerTools, /function syncLedgerMonthDisplay\(\)/, 'shared toolbar owns the month display presenter');
assert.doesNotMatch(adaptiveUi, /setupMobileWorkspaceLedgerTools|setupTouchWorkspaceToolbar|setupTouchWorkspaceMonthDisplay|syncTouchWorkspaceMonthDisplay|moveImportButton/, 'adaptive UI must not create, move, retry or post-process ledger toolbar structure');
assert.match(excelExportUi, /querySelector\('#ledgerExcelExport'\)/, 'Excel export binds the shared toolbar control');
assert.doesNotMatch(excelExportUi, /querySelector\('\.ledger-view-tools'\)|createElement\('button'\)/, 'Excel export must not create a second toolbar control');
assert.match(excelImportUi, /querySelector\('#ledgerExcelImport'\)/, 'Excel import binds the settings-owned control directly');
assert.doesNotMatch(excelImportUi, /querySelector\('\.ledger-view-tools'\)|createElement\('button'\)/, 'Excel import must not create a temporary ledger toolbar control');
assert.match(adaptiveUi, /id="ledgerExcelImport"/, 'data settings directly owns the Excel import control');
assert.match(inlineEdit, /querySelector\('#ledgerToolbar'\)/, 'inline edit observes the shared toolbar boundary');
assert.doesNotMatch(adaptiveCss, /ledger-desktop-tools|ledger-period-tools|cy-toolbar-ready|mobileLedger(?:MoreButton|BalanceButton|ToolsSheet|ToolsBackdrop|MonthDisplay)/, 'toolbar CSS must consume the shared structure without historical desktop/mobile shells');

assert.match(read('public/quick-entry-settings.js'), /setupQuickEntrySettingsPane/);
const categoryManagement = read('public/category-management.js');
const quickEntrySettings = read('public/quick-entry-settings.js');
assert.doesNotMatch(categoryManagement, /setupCategoryTransfer|injectCategoryTransferButtons|data-category-transfer/, 'category manager must not be post-processed by a second transfer owner');
assert.doesNotMatch(quickEntrySettings, /setupOrderingControls|injectAccountOrderButtons|injectCategoryOrderButtons|handleOrderingAction/, 'settings ordering must not be post-processed by a second owner');
assert.match(adaptiveUi, /let settingsManagerInitialized = false/, 'settings manager must own a single lifecycle');
assert.match(adaptiveUi, /document\.addEventListener\('DOMContentLoaded', setupSettingsManager, \{ once: true \}\)/, 'settings manager initializes once at DOM readiness');
assert.doesNotMatch(adaptiveUi, /setTimeout\(setupSettingsManager|addEventListener\('load',[^\n]*setupSettingsManager/, 'settings manager must not use retry or load-time rebinding');
assert.match(read('public/excel-export-ui.js'), /downloadMonthlyExcel/);
assert.match(read('public/ledger-inline-edit.js'), /beginInlineLedgerEdit/);
assert.match(read('public/excel-import-ui.js'), /setupExcelImport/);
assert.match(read('public/backup-ui.js'), /setupBackupSettings/);

for (const name of [
  'quick-entry-settings.js',
  'category-management.js',
  'excel-export-ui.js',
  'ledger-inline-edit.js',
  'excel-import-ui.js',
  'backup-ui.js'
]) {
  const source = read('public/' + name);
  assert.doesNotMatch(source, /(?:V|v)(?:11|12|13|14|15|16|17|18|181)(?=[A-Za-z0-9_-])/, name + ' must use functional internal identifiers');
}
assert.doesNotMatch(read('public/backup-ui.js'), /V0\.18\.[01]/, 'backup UI must not own historical app version display');
assert.match(html, /<span class="version">V0\.22\.15<\/span>/, 'index.html must own the current visible version');
assert.match(appJs, /setLedgerLoadingState\(true\)/, 'month loading must expose an interaction-blocking busy state');
assert.match(appJs, /requestId === cyTransactionRequestId\) setLedgerLoadingState\(false\)/, 'only the current month request may clear the busy state');
assert.match(read('public/excel-export-ui.js'), /navigator\.share/, 'mobile Excel export must prefer the native share sheet');
assert.match(read('public/excel-export-ui.js'), /navigator\.canShare/, 'file sharing capability must be checked before native share');
assert.match(read('public/excel-export-ui.js'), /dataset\.tabletLayout === 'landscape'/, 'tablet landscape Excel export must reuse native share when available');
assert.doesNotMatch(adaptiveUi, /querySelector\(['"]\.version['"]\)/, 'adaptive UI must not mutate the global version element');
assert.doesNotMatch(adaptiveUi, /\bCY_[A-Z0-9_]*VERSION\b|\b(?:sync|enforce)[A-Za-z0-9_]*Version\b/, 'historical version mutators must not return');
assert.match(appJs, /window\.cySettingsManager\?\.renderAccountManager\?\.\(\)/, 'app.js must delegate account rendering to the canonical settings manager');
assert.match(appJs, /window\.cySettingsManager\?\.renderCategoryManager\?\.\(\)/, 'app.js must delegate category rendering to the canonical settings manager');
assert.doesNotMatch(appJs, /function renderAccountManager\(|function renderCategoryManager\(/, 'app.js must not own a second settings renderer');
assert.doesNotMatch(adaptiveUi, /setTimeout\(run[A-Za-z0-9_]+,\s*(?:160|520|900)\)/, 'frontend features must not bootstrap through staged retry timers');
assert.match(adaptiveUi, /document\.addEventListener\('DOMContentLoaded', setupLedgerBalancePopover, \{ once: true \}\)/, 'ledger balance behavior binds once at DOM readiness');
assert.match(adaptiveUi, /window\.cyShowMigrationComplete = showMigrationComplete/, 'migration completion dialog must use the semantic owner');
assert.doesNotMatch(adaptiveUi, /setupOptimisticSettings/, 'settings mutations must not be intercepted by a secondary patch layer');


console.log('Semantic frontend architecture checks passed.');
