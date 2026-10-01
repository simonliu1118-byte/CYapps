import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const HERE = path.dirname(fileURLToPath(import.meta.url));
const ROOT = path.resolve(HERE, '..');
const read = relative => fs.readFileSync(path.join(ROOT, relative), 'utf8');

const css = read('public/adaptive-ui.css');
const js = read('public/adaptive-ui.js');

assert.match(css, /@media \(max-width: 767px\)/);
assert.match(css, /@media \(min-width: 768px\) and \(max-width: 1023px\)/);
assert.match(css, /@media \(min-width: 1024px\)/);
assert.match(css, /@media \(min-width: 1360px\)/);
assert.match(css, /\.v21-mobile-main-nav\.v21-mobile-bottom-nav\s*\{[\s\S]*?position:\s*fixed !important;[\s\S]*?bottom:\s*0;/);
assert.match(css, /\.ledger-month-tools\.v0215-toolbar-ready/);
assert.match(css, /\.opening-modal\[open\][\s\S]*?display:\s*flex !important/);
assert.match(css, /\.quick-chip-list[\s\S]*?overflow-x:\s*auto !important/);

assert.match(js, /trigger\.id = 'mobileAccountMenuButton'/);
assert.match(js, /data-mobile-account-action="logout">登出/);
assert.doesNotMatch(js, /data-mobile-account-action="settings"/);
assert.match(js, /button\.id = 'mobileLedgerMoreButton'/);
assert.match(js, /data-mobile-ledger-action="accounts">帳戶設定/);
assert.match(js, /data-mobile-ledger-action="categories">科目設定/);
assert.match(js, /data-mobile-ledger-action="lock">月份鎖帳/);
assert.match(js, /data-mobile-ledger-action="export">匯出 Excel/);
assert.doesNotMatch(js, /data-mobile-ledger-action="opening">期初餘額/);
assert.match(js, /mobileLedgerBalanceButton/);
assert.match(js, /window\.cyOpenMobileLedgerOpening/);
assert.match(js, /window\.cyOpenMobileLedgerLock/);
assert.match(js, /window\.cyOpenMobileSettingsPane/);

console.log('Adaptive UI regression checks passed.');
