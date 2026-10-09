import assert from 'node:assert/strict';
import fs from 'node:fs';
import vm from 'node:vm';
const read = name => fs.readFileSync(new URL('../public/' + name, import.meta.url), 'utf8');
const source = read('adaptive-ui.js');
const ledgerToolsSource = read('ledger-tools.js');
const exportSource = read('excel-export-ui.js');
const migrationSource = read('desktop-migration-ui.js');
const tabletCode = source.slice(source.indexOf('function isTabletWorkspace()'));
const listeners = new Map();
let coarse = true;
let viewportHeight = 1100;
const attrs = new Map();
const button = { textContent: '', setAttribute: (name, value) => attrs.set(name, value), addEventListener(name, fn) { this[name] = fn; } };
const controls = { parentElement: null, querySelector: selector => selector === '#tabletEntryToggle' ? button : null };
const quickHost = { append(group) { group.parentElement = this; } };
const entryGrid = { append(group) { group.parentElement = this; } };
const favoriteGroup = { parentElement: quickHost };
const summaryGroup = { parentElement: quickHost };
const rail = { dataset: {}, prepend(node) { assert.equal(node, controls); node.parentElement = this; } };
const shell = { dataset: {} };
const classes = { remove() {}, contains: () => true };
const entry = { prepend(node) { assert.equal(node, controls); node.parentElement = this; }, classList: classes };
const ledger = { classList: classes };
const style = new Map();
const root = { dataset: {}, style: { setProperty: (key, value) => style.set(key, value) } };
let writes = 0;
const draft = { amount: '123', summary: '尚未儲存' };
let edit = { id: 9, draft };
let splitCalls = 0;
const nodes = { '.quick-entry-tools': quickHost, '.entry-grid': entryGrid, '#favoriteCategoryGroup': favoriteGroup, '#summarySuggestionGroup': summaryGroup, 'main.shell': shell, '.entry-card': entry, '.ledger-card': ledger, '.cy-entry-rail': rail, '#tabletEntryToggle': button,
  '.topbar': { getBoundingClientRect: () => ({ height: 60 }) }, '#readOnlyNotice': { classList: classes, getBoundingClientRect: () => ({ height: 30 }) } };
const win = {
  setTimeout(fn) { fn(); return 1; },
  innerWidth: 820, innerHeight: 1100, screen: { orientation: { type: 'portrait-primary', addEventListener() {} } },
  visualViewport: { get height() { return viewportHeight; }, addEventListener(name, fn) { listeners.set('visual:' + name, fn); } },
  matchMedia(query) { return { matches: query.includes('coarse') ? coarse : query.includes('767') ? win.innerWidth < 768 : win.innerWidth >= 1360, addEventListener() {} }; },
  addEventListener(name, fn) { listeners.set(name, fn); }
};
const context = vm.createContext({ window: win, document: { documentElement: root, createElement: () => controls,
  querySelector: selector => nodes[selector] || null, querySelectorAll: () => [] },
  CY_ADAPTIVE_SPLIT_MEDIA: '(min-width: 1360px)', cyTouchWorkspaceEdit: edit,
  applyAdaptiveSplitWorkspace() { splitCalls++; }, setupTouchWorkspaceEntrySecondaryAction() {}, isDesktopInteractionWorkspace() { return !context.isTabletWorkspace(); }, renderSettingsAccountManager() {},
  cancelTouchWorkspaceMobileEdit() { context.cyTouchWorkspaceEdit = null; }, api() { writes++; }
});
vm.runInContext(tabletCode, context);
for (const [width, touch, expected] of [[375,true,false],[767,true,false],[768,false,true],[820,true,true],[1024,true,true],[1194,true,true],[1366,true,true],[1440,true,false],[1280,false,false],[1440,false,false]]) {
  win.innerWidth = width; coarse = touch;
  assert.equal(context.isTabletWorkspace(), expected, `tablet classification ${width}/${touch}`);
}
win.innerWidth = 375; coarse = true; win.__cyaccTabletPreviewEnabled = true;
assert.equal(context.isTabletWorkspace(), true, 'phone tablet preview forces the shared tablet presentation owner');
win.__cyaccTabletPreviewEnabled = false;
win.innerWidth = 820; coarse = true;
context.setupTabletWorkspace();
assert.equal(root.dataset.tabletLayout, 'portrait');
assert.equal(controls.parentElement, rail, 'portrait handle is owned by the entry rail edge, not the entry-card interior');
assert.equal(favoriteGroup.parentElement, entryGrid);
assert.equal(summaryGroup.parentElement, entryGrid);
assert.equal(rail.dataset.entryExpanded, 'true', 'portrait entry rail starts expanded');
assert.equal(attrs.get('aria-expanded'), 'true', 'entry handle aria state matches the expanded default');
button.click();
assert.equal(rail.dataset.entryExpanded, 'false');
button.click();
assert.equal(rail.dataset.entryExpanded, 'true');
button.click();
assert.equal(rail.dataset.entryExpanded, 'false', 'entry rail remains directly toggleable without a pinned-open mode');
// Handle drags must act once, ignore jitter and cancellation, and preserve the draft.
button.pointerdown({ button: 0, pointerId: 1, clientY: 150 });
button.pointerup({ pointerId: 1, clientY: 100 }); button.click();
assert.equal(rail.dataset.entryExpanded, 'true', 'upward drag opens without synthetic click closing it');
button.pointerdown({ button: 0, pointerId: 2, clientY: 100 });
button.pointerup({ pointerId: 2, clientY: 160 }); button.click();
assert.equal(rail.dataset.entryExpanded, 'false', 'downward drag closes exactly once');
button.pointerdown({ button: 0, pointerId: 3, clientY: 100 });
button.pointercancel(); button.pointerup({ pointerId: 3, clientY: 30 });
assert.equal(rail.dataset.entryExpanded, 'false', 'cancelled drag does not toggle');
context.setTabletEntryExpanded(true);
const currentEdit = context.cyTouchWorkspaceEdit;
win.innerWidth = 1194; win.screen.orientation.type = 'landscape-primary'; listeners.get('resize')();
assert.equal(root.dataset.tabletLayout, 'landscape');
assert.equal(context.cyTouchWorkspaceEdit, currentEdit, 'rotation preserves the existing edit owner');
assert.equal(currentEdit.draft, draft, 'rotation preserves the same draft');
win.innerWidth = 820; win.screen.orientation.type = 'portrait-primary'; listeners.get('resize')();
viewportHeight = 420; listeners.get('visual:resize')();
assert.equal(root.dataset.tabletLayout, 'portrait', 'software keyboard does not change orientation');
assert.equal(style.get('--tablet-visible-height'), '1100px', 'software keyboard must not collapse the tablet workspace into the visual viewport');
assert.equal(context.cyTouchWorkspaceEdit, currentEdit);
assert.equal(writes, 0, 'layout/rotation/keyboard/entry toggling never writes accounting data');
win.innerWidth = 375; listeners.get('resize')();
assert.equal(controls.parentElement, entry, 'leaving tablet restores the hidden handle node to the entry card');
assert.equal(favoriteGroup.parentElement, quickHost, 'phone restores existing quick-entry container');
assert.equal(summaryGroup.parentElement, quickHost);
win.innerWidth = 820; listeners.get('resize')();
assert.equal(favoriteGroup.parentElement, entryGrid);
const count = splitCalls; context.setupTabletWorkspace(); assert.equal(splitCalls, count, 'setup binds once');
const css = read('adaptive-ui.css');
assert.match(css, /data-tablet-layout="portrait"[\s\S]*?grid-template-rows: minmax\(0, 1fr\) auto/);
assert.match(css, /\.ledger-card \.table-wrap \{[^}]*overflow: auto/);
assert.match(css, /body\[data-cyacc-read-only="true"\] \.cy-entry-rail \{ display: none/);
assert.match(css, /input\.desktopUi-native-date-source,[\s\S]*?pointer-events: auto !important/);
assert.match(read('ledger-inline-edit.js'), /window\.cyUsesEntryTransactionEditor\?\.\(\)/);
assert.match(css.slice(css.indexOf('/* Tablet workspace:')), /grid-template-columns: minmax\(0, 1fr\) !important/, 'tablet overrides important desktop entry columns');
assert.match(css, /data-tablet-layout="landscape"\] \.shell\.cy-split-layout \{ grid-template-columns: minmax\(280px, 31%\)/, 'landscape entry rail is narrower only in landscape');
assert.match(css, /data-tablet-layout\]:not\(\[data-tablet-layout=""\]\) \.confirmation-edge-open,[\s\S]*?confirmation-drawer-collapse \{[\s\S]*?display: none !important/, 'tablet hides the unrelated confirmation drawer handle in both orientations');
assert.match(css, /data-tablet-layout="landscape"\] \.tablet-entry-controls \{ display: none !important/, 'landscape hides only the portrait entry handle');
assert.match(css, /data-tablet-layout="landscape"\] \.entry-grid #txDate \{[\s\S]*?-webkit-appearance:\s*auto !important/, 'landscape date uses the shared native touch control');
assert.match(css, /data-tablet-layout="landscape"\] #accountName,[\s\S]*?#categoryName \{ text-align:center !important; text-align-last:center !important/, 'landscape account and category are centered');
assert.match(css, /data-tablet-layout="landscape"\] \.entry-grid > #favoriteCategoryGroup\.quick-tool-row\.hidden,[\s\S]*?display:grid !important/, 'landscape favorites remain visible when empty');
assert.match(css, /data-tablet-layout="landscape"\] \.ledger-search-submit \{ display:none !important/, 'landscape search submits by Enter like mobile');
assert.match(css, /data-tablet-layout="portrait"[\s\S]*?\.ledger-search-submit,[\s\S]*?#ledgerSearchClear \{[\s\S]*?display:\s*none !important/, 'portrait search uses the native search field without Search/Clear buttons');
assert.match(css, /data-tablet-layout="portrait"[\s\S]*?#ledgerSummarySearch \{[\s\S]*?-webkit-appearance:\s*searchfield !important/, 'portrait search keeps native search-field affordances');
assert.match(css, /data-tablet-layout="landscape"\] #monthSummary \.ledger-summary-item:not\(\.opening\) \{ border-left:1px solid/, 'landscape summary uses one separated row');
assert.match(css, /data-tablet-layout="landscape"\] \.ledger-card td\.action-col \[data-edit-id\]::before/, 'landscape edit is icon-only');
assert.match(css, /data-tablet-layout="landscape"\] \.ledger-card td\.action-col \[data-delete-id\]::before/, 'landscape delete is icon-only');
assert.match(source, /if \(!isDesktopInteractionWorkspace\(\)\) return;/, 'tablet must not create desktop custom date pickers');
assert.match(source, /syncTabletPickerOwnership\(orientation\)/, 'tablet orientation owns date and month picker presentation');
assert.match(css, /data-tablet-layout="landscape"\] \.shell\.cy-split-layout \{[\s\S]*?minmax\(250px, 28%\)/, 'landscape entry rail is reduced another ten percent');
assert.match(css, /data-tablet-layout\]:not\(\[data-tablet-layout=""\]\) #ledgerMonthDisplay/, 'both tablet orientations reuse the shared mobile month display layer');
assert.match(css, /data-tablet-layout\]:not\(\[data-tablet-layout=""\]\) \.cy-account-cluster[\s\S]*?display:\s*flex !important[\s\S]*?role-super-admin[\s\S]*?#ddc789[\s\S]*?role-admin[\s\S]*?#d7b79e/, 'tablet keeps the inline account cluster and shares mobile gold/bronze role colors');
assert.match(css, /data-tablet-layout\]:not\(\[data-tablet-layout=""\]\) \.cy-account-cluster \.current-user-role \{[\s\S]*?border:\s*0 !important[\s\S]*?background:\s*transparent !important/, 'tablet role text is inline text rather than a separate pill');
assert.match(css, /data-tablet-layout\]:not\(\[data-tablet-layout=""\]\) \.cy-account-cluster \.current-user-role::before[\s\S]*?content:\s*"［"/, 'tablet role label keeps the inline bracketed presentation');
assert.match(css, /data-tablet-layout\]:not\(\[data-tablet-layout=""\]\) \.topbar #mobileAccountMenuButton,[\s\S]*?cy-mobile-account-menu[\s\S]*?display:\s*none !important/, 'tablet does not reuse the phone dropdown account owner');
assert.doesNotMatch(css, /data-tablet-layout[^\n]*\.cy-account-cluster[^\{]*\{[^\}]*display:\s*none !important/, 'tablet must never hide the canonical inline account cluster');
assert.doesNotMatch(css, /data-tablet-layout[^\n]*#mobileAccountMenuButton[^\{]*\{[^\}]*display:\s*(?:block|flex|inline-flex) !important/, 'tablet must never show the phone account trigger');
assert.match(css, /data-tablet-preview="true"\] #tabletPreviewReturnButton[\s\S]*?display:\s*inline-flex !important/, 'phone tablet preview exposes a temporary return-to-phone button');
assert.match(css, /#tabletPreviewReturnButton \{[\s\S]*?display:\s*none !important/, 'return-to-phone is hidden by default outside preview');
assert.match(css, /data-tablet-preview="true"\] #tabletPreviewReturnButton:not\(\[hidden\]\)[\s\S]*?display:\s*inline-flex !important/, 'only active phone preview may expose return-to-phone');
assert.match(css, /data-tablet-layout="portrait"[\s\S]*?\.cy-entry-rail \.entry-card \{[\s\S]*?grid-template-columns:\s*46px minmax\(0, 1fr\)/, 'portrait entry rail reserves a slimmer left mode switch column');
assert.match(css, /data-tablet-layout="portrait"[\s\S]*?\.entry-kind-switch \{[\s\S]*?grid-template-columns:\s*1fr[\s\S]*?grid-template-rows:\s*repeat\(2, minmax\(0, 1fr\)\)/, 'income and expense are a vertical two-segment switch');
assert.match(css, /data-tablet-layout="portrait"[\s\S]*?\.entry-grid \{[\s\S]*?grid-template-columns:\s*repeat\(12,[\s\S]*?grid-template-rows:\s*repeat\(3, 40px\)/, 'portrait entry form uses three equal compact rows');
assert.match(css, /#favoriteCategoryGroup \{ grid-column:\s*8 \/ 13[\s\S]*?grid-row:\s*1[\s\S]*?#summarySuggestionGroup \{ grid-column:\s*8 \/ 13[\s\S]*?grid-row:\s*2/, 'favorite category and summary suggestions occupy the right side of rows one and two');
assert.match(css, /\.entry-grid > \.summary-field \{ grid-column:\s*4 \/ 8 !important; grid-row:\s*2 !important; \}/, 'summary is paired with date and common-summary controls on row two');
assert.match(source, /tablet-entry-handle-arrow" aria-hidden="true">↑<\/span><span class="tablet-entry-handle-label">展開新增/, 'collapsed rail exposes upward arrow and 展開新增');
assert.match(source, /arrow\.textContent = expanded \? '↓' : '↑'/, 'entry handle arrow follows expanded state');
assert.match(source, /label\.textContent = expanded \? '收合隱藏' : '展開新增'/, 'entry handle text follows expanded state');
assert.match(css, /V0\.22\.26 portrait tab handle hotfix/, 'portrait handle fix is explicitly scoped');
assert.match(css, /data-tablet-layout="portrait"[\s\S]*?\.tablet-entry-controls \{[\s\S]*?position:\s*absolute !important[\s\S]*?top:\s*-26px !important[\s\S]*?left:\s*50% !important/, 'portrait handle is raised from the entry panel top edge');
assert.match(css, /data-tablet-layout="portrait"[\s\S]*?\.tablet-entry-handle \{[\s\S]*?border-bottom:\s*0 !important[\s\S]*?border-radius:\s*11px 11px 0 0 !important/, 'portrait handle joins the panel edge as a tab');
assert.match(css, /data-tablet-layout="portrait"[\s\S]*?\.cy-entry-rail\[data-entry-expanded="false"\] \.entry-card \{[\s\S]*?height:\s*0 !important[\s\S]*?border:\s*0 !important/, 'collapsed rail leaves only the raised tab and no panel edge');
assert.match(css, /data-tablet-layout="portrait"[\s\S]*?#monthSummary \{[\s\S]*?grid-row:\s*3 !important/, 'portrait month summary stays below the month/actions row and cannot collide with the month picker');
assert.doesNotMatch(source, /tabletEntryPinned|保持展開/, 'portrait entry rail no longer exposes a pinned-open mode');
assert.match(css, /V0\.22\.27 tablet polish[\s\S]*?\.ledger-card th\.action-col \{[\s\S]*?text-align:\s*center !important/, 'tablet action header is centered in both orientations');
assert.match(css, /data-tablet-layout="portrait"[\s\S]*?\.ledger-title \.title-with-badge \{[\s\S]*?display:\s*none !important/, 'portrait removes the ledger title row');
assert.match(css, /V0\.22\.27 tablet polish[\s\S]*?#monthSummary \{[\s\S]*?grid-template-columns:\s*repeat\(5, minmax\(0, 1fr\)\) !important[\s\S]*?min-height:\s*38px !important/, 'tablet summary keeps five fixed slots regardless of values');
assert.match(css, /data-tablet-layout="portrait"[\s\S]*?#monthSummary \.ledger-summary-item:not\(\.opening\) \{[\s\S]*?border-left:\s*1px solid #dfe5eb !important/, 'portrait summary uses the same separators as landscape');
assert.match(css, /data-tablet-layout="portrait"[\s\S]*?#ledgerQuickLockButton\.is-locked[\s\S]*?background:\s*#fff0df !important/, 'portrait locked month has a colored lock state');
assert.match(css, /data-tablet-layout="portrait"[\s\S]*?\.ledger-card\.is-loading \.table-wrap::after[\s\S]*?content:\s*"載入中…"/, 'portrait reuses the visible ledger loading state');
assert.match(css, /data-tablet-layout="portrait"[\s\S]*?#ledgerMonthSlot \{[\s\S]*?border-radius:\s*10px !important[\s\S]*?background:\s*#fff !important/, 'portrait month selector is a bounded touch capsule');
assert.match(css, /data-tablet-layout="portrait"[\s\S]*?#ledgerMonthDisplay::after[\s\S]*?content:\s*"▾"/, 'portrait month selector exposes an explicit picker cue');
assert.match(css, /data-tablet-layout="portrait"[\s\S]*?#ledgerSummarySearch \{[\s\S]*?font-size:\s*16px !important/, 'portrait native search stays at iOS-safe 16px and does not trigger focus zoom');
assert.match(source, /syncTabletPortraitLedgerExportPlacement\(orientation\)/, 'tablet layout owns export placement');
assert.match(source, /orientation === 'portrait' \? summaryActions : viewTools/, 'portrait moves the canonical Excel button beside opening balance');
assert.match(exportSource, /Boolean\(document\.documentElement\.dataset\.tabletLayout\)/, 'both tablet orientations use the phone native-share owner');
assert.match(exportSource, /window\.__cyaccTabletPreviewEnabled === true/, 'tablet preview also uses the phone native-share owner');
assert.match(exportSource, /type:\s*'application\/vnd\.openxmlformats-officedocument\.spreadsheetml\.sheet'/, 'Excel native share uses the canonical xlsx MIME type');
assert.match(css, /V0\.22\.27 tablet polish[\s\S]*?tbody > tr:not\(\.ledger-message-row\) > td:nth-child\(5\) \{ width:\s*27% !important/, 'portrait ledger has explicit summary width independent of tbody contents');
assert.match(css, /V0\.22\.27 tablet polish[\s\S]*?\.cy-entry-rail\[data-entry-expanded="false"\] \.entry-card \{[\s\S]*?height:\s*0 !important[\s\S]*?border:\s*0 !important/, 'collapsed portrait rail leaves no residual line beneath the tab');
assert.match(css, /V0\.22\.27 tablet polish[\s\S]*?\.entry-card\.entry-income \.tablet-entry-handle,[\s\S]*?\.entry-card\.entry-expense \.tablet-entry-handle[\s\S]*?background:\s*#fff !important/, 'tab handle stays white for both income and expense');
assert.match(css, /\.entry-kind-switch-field \{[\s\S]*?width:\s*42px !important[\s\S]*?height:\s*132px !important/, 'portrait income/expense switch is the canonical pill rotated into a slimmer, taller vertical control');
assert.match(css, /V0\.22\.27 tablet polish[\s\S]*?\.entry-kind-switch \.kind-button \{[\s\S]*?writing-mode:\s*vertical-rl !important[\s\S]*?text-orientation:\s*upright !important/, 'portrait income and expense labels are vertical text');
assert.match(css, /data-tablet-layout="portrait"[\s\S]*?\.ledger-card th:nth-child\(3\),[\s\S]*?display:\s*none !important/, 'portrait removes the separate income-expense column');
assert.match(css, /data-tablet-layout="portrait"[\s\S]*?ledger-row-income[\s\S]*?td:nth-child\(6\)::before[\s\S]*?content:\s*"\+"/, 'portrait income amount carries the phone-style plus sign');
assert.match(css, /data-tablet-layout="portrait"[\s\S]*?ledger-row-expense[\s\S]*?td:nth-child\(6\)::before[\s\S]*?content:\s*"−"/, 'portrait expense amount carries the phone-style minus sign');
assert.match(css, /data-tablet-layout="portrait"[\s\S]*?td\.action-col \[data-edit-id\]::before/, 'portrait edit action is icon-only');
assert.match(css, /data-tablet-layout="portrait"[\s\S]*?td\.action-col \[data-delete-id\]::before/, 'portrait delete action is icon-only');
assert.match(ledgerToolsSource, /data-edit-id="\$\{tx\.id\}" aria-label="編輯"/, 'icon-only edit retains an accessible name');
assert.match(ledgerToolsSource, /data-delete-id="\$\{tx\.id\}" aria-label="刪除"/, 'icon-only delete retains an accessible name');
assert.match(source, /function usesCompactTouchUtility\(\)[\s\S]*?isTabletWorkspace\(\) && tabletWorkspaceOrientation\(\) === 'portrait'/, 'portrait tablet reuses the compact touch utility lifecycle');
assert.match(source, /setupAdaptiveDataSettings\(\)[\s\S]*?!isDesktopInteractionWorkspace\(\)\) return;/, 'tablet never installs desktop-only Data Management');
assert.match(migrationSource, /installMigrationSettings\(\)[\s\S]*?!window\.cyIsDesktopInteractionWorkspace\(\)\) return;/, 'tablet never installs desktop-only Data Migration');
assert.match(css, /#settingsDialog\.settings-modal \{[\s\S]*?width:\s*min\(640px, calc\(100vw - 48px\)\) !important/, 'tablet settings dialog is materially narrower');
assert.match(css, /data-settings-pane="lock"[\s\S]*?\.lock-form \{[\s\S]*?grid-template-columns:\s*minmax\(168px, 1\.15fr\) minmax\(118px, \.9fr\) minmax\(118px, \.9fr\)/, 'tablet month-lock controls stay on one stable row');
assert.match(css, /#backupRefreshStatus \{[\s\S]*?width:\s*34px !important[\s\S]*?font-size:\s*0 !important/, 'tablet backup refresh is a compact icon control');

const quickLockFns = ledgerToolsSource.slice(
  ledgerToolsSource.indexOf('function shiftLedgerMonth('),
  ledgerToolsSource.indexOf('function syncLedgerQuickLock(')
);
const quickLock = vm.createContext({ state: { lockedThrough: '2026-08' }, Date });
vm.runInContext(quickLockFns, quickLock);
let quick = quickLock.ledgerQuickLockState('2026-09');
assert.equal(quick.canLock, true, 'the month immediately after lockedThrough may quick-lock');
assert.equal(quick.canUnlock, false);
quick = quickLock.ledgerQuickLockState('2026-10');
assert.equal(quick.canLock, false, 'quick-lock cannot skip a month');
quick = quickLock.ledgerQuickLockState('2026-08');
assert.equal(quick.canUnlock, true, 'only the current lockedThrough boundary may quick-unlock');
quick = quickLock.ledgerQuickLockState('2026-07');
assert.equal(quick.locked, true);
assert.equal(quick.canUnlock, false, 'older locked months stay locked and disabled');
quickLock.state.lockedThrough = '';
quick = quickLock.ledgerQuickLockState('2026-09');
assert.equal(quick.canLock, false, 'without an existing lock boundary the compact button cannot establish an arbitrary starting month');
assert.match(css, /data-tablet-layout="landscape"\] #ledgerExcelImport \{ display: none !important/, 'tablet landscape removes Excel import');
console.log('Tablet classification, rotation, keyboard, pinning and shared edit ownership passed.');

// Selecting another visible tablet row must not replace the original new-entry draft.
let cancellations = 0;
const originalDraft = { summary: '新增草稿', amount: '17' };
let shownDraft = { summary: '第一筆修改', amount: '99' };
const editor = vm.createContext({
  cyTouchWorkspaceEdit: { id: 1, draft: originalDraft },
  state: { transactions: [{ id: 2, tx_date: '2026-10-01', kind: 'income', account_name: '現金', category_name: '一般收入', amount: 8 }] },
  els: { summary: { focus() {} }, amount: {}, txDate: {}, accountName: {}, categoryName: {}, saveButton: {}, kindButtons: [], monthFilter: { value: '2026-10' } },
  document: { querySelector: () => null }, window: { scrollY: 0, scrollTo() {} },
  isTabletWorkspace: () => true, setTabletEntryExpanded() {}, isLocked: () => false,
  cancelTouchWorkspaceMobileEdit() { cancellations++; shownDraft = originalDraft; editor.cyTouchWorkspaceEdit = null; },
  captureTouchWorkspaceEntryDraft: () => shownDraft,
  setEntryKind() {}, ensureTouchWorkspaceOption() {}, syncTouchWorkspaceEntrySecondaryAction() {}, showMessage() {}, updateEntryLockState() {}, switchTouchWorkspaceMobilePage() {}
});
vm.runInContext(source.slice(source.indexOf('function beginTouchWorkspaceMobileEdit('), source.indexOf('async function saveTouchWorkspaceMobileEdit(')), editor);
editor.beginTouchWorkspaceMobileEdit(2);
assert.equal(editor.cyTouchWorkspaceEdit.draft, originalDraft);
assert.equal(cancellations, 1);
const selectedEdit = editor.cyTouchWorkspaceEdit;
editor.beginTouchWorkspaceMobileEdit(2);
assert.equal(editor.cyTouchWorkspaceEdit, selectedEdit, 'reselecting preserves unsaved edits');
assert.equal(cancellations, 1);

const editEvents = new Map();
const bindNode = label => ({ addEventListener(name, fn) { editEvents.set(label + ':' + name, fn); } });
const editNodes = { '#transactionRows': bindNode('rows'), '#transactionForm': bindNode('form'), '#mobileMainNav': bindNode('nav') };
const controller = vm.createContext({
  document: { querySelector: key => editNodes[key] }, window: { matchMedia: () => ({ addEventListener() {} }), addEventListener() {} },
  els: { monthFilter: bindNode('month') }, CY_TOUCH_WORKSPACE_MOBILE: '(max-width: 767px)', cyTouchWorkspaceEdit: { id: 2 },
  usesEntryTransactionEditor: () => true,
  cancelTouchWorkspaceMobileEdit() { controller.cyTouchWorkspaceEdit = null; },
  saveTouchWorkspaceMobileEdit() { writes++; }
});
vm.runInContext(source.slice(source.indexOf('function setupTouchWorkspaceMobileEdit()'), source.indexOf('function beginTouchWorkspaceMobileEdit(')), controller);
controller.setupTouchWorkspaceMobileEdit();
editEvents.get('month:change')();
assert.equal(controller.cyTouchWorkspaceEdit, null, 'month navigation cancels the old-month entry edit');
assert.equal(writes, 0, 'navigation never saves an unfinished edit');
console.log('Tablet selection and month navigation preserve the shared draft/cancel semantics.');

assert.match(source, /function isDesktopInteractionWorkspace\(\)/, 'desktop interaction authority is defined');
assert.doesNotMatch(source, /min-width:\s*1024px/, 'desktop interactions must not infer desktop from 1024px');

assert.match(source, /window\.cySyncLedgerMonthDisplay\?\.\(\)/, 'tablet landscape consumes the shared ledger month presenter');
assert.match(ledgerToolsSource, /function syncLedgerMonthDisplay\(\)/, 'ledger toolbar owns the shared month presenter lifecycle');
assert.match(source, /document\.querySelectorAll\('\.desktopUi-date-picker'\)\.forEach\(root => root\.remove\(\)\)/, 'tablet removes desktop date presentation instead of adding a tablet picker');
assert.doesNotMatch(source, /tabletEntryDateDisplay|setupTabletDateDisplay/, 'tablet must not own a separate date display component');


assert.match(css, /data-tablet-layout\]:not\(\[data-tablet-layout=""\]\) #monthFilter \{[\s\S]*?opacity:\s*0 !important/, 'tablet month native text is fully hidden behind the shared presenter');
assert.match(css, /data-tablet-layout\]:not\(\[data-tablet-layout=""\]\) #ledgerMonthDisplay \{[\s\S]*?z-index:\s*4 !important/, 'shared month presenter stays visibly above the native month input');
assert.equal((css.match(/data-tablet-layout\]:not\(\[data-tablet-layout=""\]\)[^\n]*#monthFilter \{/g) || []).length, 1, 'tablet has one shared month presentation rule');
assert.equal((css.match(/data-tablet-layout="landscape"\][^\n]*\.entry-grid #txDate \{/g) || []).length, 1, 'tablet landscape has one entry date presentation rule');
