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
const js = read('public/v020.js');

assert.equal(version, '0.20.0');
assert.equal(build, '0');
assert.match(html, /V0\.20\.0/);
assert.doesNotMatch(html, /V0\.20\.0<\/span>\s*Build/);
assert.match(html, /href="\/v020\.css"/);
assert.match(html, /src="\/v020\.js"/);
assert.ok(html.indexOf('/v020.css') > html.indexOf('/v019.css'), 'v020.css must load after v019.css');
assert.ok(html.indexOf('/v020.js') > html.indexOf('/v019.js'), 'v020.js must load after v019.js');

// Explicit device bands: Desktop >=1024, Tablet 768-1023, Mobile <768.
assert.match(css, /@media \(min-width: 768px\) and \(max-width: 1023px\)/);
assert.match(css, /@media \(max-width: 767px\)/);

// Mobile entry must be a single-column touch form.
assert.match(css, /@media \(max-width: 767px\)[\s\S]*?\.entry-grid\s*\{[\s\S]*?grid-template-columns:\s*1fr;/);
assert.match(css, /\.entry-grid input,[\s\S]*?min-height:\s*44px;/);

// Ledger must adapt from a wide desktop table into labeled transaction cards.
assert.match(css, /\.ledger-card table,[\s\S]*?\.ledger-card td\s*\{[\s\S]*?display:\s*block;/);
assert.match(css, /td:nth-child\(1\)::before\s*\{\s*content:\s*"日期";/);
assert.match(css, /td:nth-child\(7\)::before\s*\{\s*content:\s*"餘額";/);
assert.match(css, /\.ledger-card table\s*\{[\s\S]*?min-width:\s*0;/);

// Mobile settings/dialogs must use a sheet/full-screen presentation, not a squeezed desktop modal.
assert.match(css, /height:\s*100dvh;/);
assert.match(css, /\.settings-nav\s*\{[\s\S]*?overflow-x:\s*auto;/);
assert.match(css, /\.confirmation-drawer\s*\{[\s\S]*?bottom:\s*0;/);

// Adaptive behavior: viewport classification, safe mobile confirmation default and current-version correction.
assert.match(js, /CY_V20_VERSION = 'V0\.20\.0'/);
assert.match(js, /dataset\.viewport = width < 768 \? 'mobile' : width < 1024 \? 'tablet' : 'desktop'/);
assert.match(js, /setConfirmationDrawer\(false, false\)/);
assert.match(js, /scrollIntoView/);

console.log('V0.20 adaptive UI regression tests passed.');
