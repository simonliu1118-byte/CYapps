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
const css = read('public/v020.css');
const refineCss = read('public/v0201.css');
const js = read('public/v020.js');

assert.equal(version, '0.20.1');
assert.equal(build, '0');
assert.match(html, /href="\/v020\.css"/);
assert.match(html, /src="\/v020\.js"/);
assert.ok(html.indexOf('/v020.css') > html.indexOf('/v019.css'), 'v020.css must load after v019.css');
assert.ok(html.indexOf('/v020.js') > html.indexOf('/v019.js'), 'v020.js must load after v019.js');

// Explicit device bands: Desktop >=1024, Tablet 768-1023, Mobile <768.
assert.match(css, /@media \(min-width: 768px\) and \(max-width: 1023px\)/);
assert.match(css, /@media \(max-width: 767px\)/);

// Phase 1 mobile entry remains a single-column touch form.
assert.match(css, /@media \(max-width: 767px\)[\s\S]*?\.entry-grid\s*\{[\s\S]*?grid-template-columns:\s*1fr;/);
assert.match(css, /\.entry-grid input,[\s\S]*?min-height:\s*44px;/);

// Phase 1 ledger remains adaptive rather than horizontally scrolling a desktop table.
assert.match(css, /\.ledger-card table,[\s\S]*?\.ledger-card td\s*\{[\s\S]*?display:\s*block;/);
assert.match(css, /td:nth-child\(1\)::before\s*\{\s*content:\s*"日期";/);
assert.match(css, /td:nth-child\(7\)::before\s*\{\s*content:\s*"餘額";/);
assert.match(css, /\.ledger-card table\s*\{[\s\S]*?min-width:\s*0;/);

// Mobile settings/dialogs keep sheet/full-screen presentation.
assert.match(css, /height:\s*100dvh;/);
assert.match(css, /\.settings-nav\s*\{[\s\S]*?overflow-x:\s*auto;/);
assert.match(css, /\.confirmation-drawer\s*\{[\s\S]*?bottom:\s*0;/);
assert.match(css, /\.confirmation-drawer\s*\{[\s\S]*?transform:\s*translateY\(102%\)/);
assert.match(css, /\.confirmation-drawer\.open\s*\{[\s\S]*?transform:\s*translateY\(0\)/);

// V0.20.1 refinement stylesheet must be loaded by the already-versioned adaptive script.
assert.match(js, /CY_V20_VERSION = 'V0\.20\.1'/);
assert.match(js, /ensureV201Stylesheet\(\)/);
assert.match(js, /link\.href = '\/v0201\.css'/);
assert.match(js, /dataset\.viewport = width < 768 \? 'mobile' : width < 1024 \? 'tablet' : 'desktop'/);
assert.match(js, /setConfirmationDrawer\(false, false\)/);
assert.match(js, /scrollIntoView/);
assert.match(js, /setupV201MobileInlineEditVisibility/);

// Phase 2 mobile card hierarchy: two-column card, prominent amount/balance and full-width actions.
assert.match(refineCss, /@media \(max-width: 767px\)/);
assert.match(refineCss, /tbody > tr:not\(\.account-group-row\)\s*\{[\s\S]*?display:\s*grid;[\s\S]*?grid-template-columns:/);
assert.match(refineCss, /td:nth-child\(1\)\s*\{[\s\S]*?grid-row:\s*1;/);
assert.match(refineCss, /td:nth-child\(3\)\s*\{[\s\S]*?grid-column:\s*2;[\s\S]*?grid-row:\s*1;/);
assert.match(refineCss, /td:nth-child\(5\)\s*\{[\s\S]*?grid-column:\s*1 \/ -1;[\s\S]*?grid-row:\s*3;/);
assert.match(refineCss, /td:nth-child\(6\)\s*\{[\s\S]*?font-size:\s*16px;[\s\S]*?font-weight:\s*700;/);
assert.match(refineCss, /td\.action-col\s*\{[\s\S]*?grid-column:\s*1 \/ -1;[\s\S]*?grid-row:\s*5;/);

// Phase 2 keeps search compact and inline edit structurally aligned with the card.
assert.match(refineCss, /\.ledger-search\s*\{[\s\S]*?grid-template-columns:\s*minmax\(0, 1fr\) auto auto;/);
assert.match(refineCss, /\.ledger-card tr\.inline-editing/);
assert.match(refineCss, /\.inline-edit-actions\s*\{[\s\S]*?grid-template-columns:\s*repeat\(2, minmax\(0, 1fr\)\);/);
assert.match(refineCss, /@media \(max-width: 420px\)/);

console.log('V0.20.1 adaptive UI regression tests passed.');
