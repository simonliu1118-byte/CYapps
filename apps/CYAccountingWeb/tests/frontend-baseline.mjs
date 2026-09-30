import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const HERE = path.dirname(fileURLToPath(import.meta.url));
const ROOT = path.resolve(HERE, '..');
const read = relative => fs.readFileSync(path.join(ROOT, relative), 'utf8');

const html = read('public/index.html');
const js = read('public/app-baseline.js');
const css = read('public/app-baseline.css');

const externalStyles = html.match(/<link\b[^>]*(?:rel="stylesheet"|rel="preload"[^>]*as="style")[^>]*>/g) || [];
const externalScripts = html.match(/<script\b[^>]*src="[^"]+"[^>]*><\/script>/g) || [];

assert.equal(externalStyles.length, 1, 'main app must load exactly one external stylesheet');
assert.equal(externalScripts.length, 1, 'main app must load exactly one external script');
assert.match(externalStyles[0], /app-baseline\.css\?v=0216b9/);
assert.match(externalScripts[0], /app-baseline\.js\?v=0216b9/);

assert.match(js, /startCyaccAuth/);
assert.match(js, /startCyaccApp/);
assert.match(js, /setupV21Build10MobileAppBar/);
assert.match(js, /setupV21Build10MobileNavigation/);
assert.match(js, /setupV0215Build4Toolbar/);
assert.match(js, /CY_V0216_VERSION = 'V0\.21\.6 Build 8'/);
assert.doesNotMatch(js, /script\.src\s*=\s*['"]\/v/);
assert.doesNotMatch(js, /link\.href\s*=\s*['"]\/v/);
assert.doesNotMatch(js, /ensureV21Build12Script|ensureV0215Build4Script|ensureV201Stylesheet/);

assert.match(css, /@media \(max-width: 767px\)/);
assert.match(css, /@media \(min-width: 1024px\)/);
assert.match(css, /\.ledger-month-tools\.v0215-toolbar-ready/);
assert.match(css, /\.current-user\.role-super-admin/);
assert.match(css, /\.cy-confirm-dialog/);

assert.doesNotMatch(html, /src="\/v0|href="\/v0|src="\/auth\.js|href="\/auth\.css/);

console.log('Consolidated frontend baseline tests passed.');
