import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const HERE = path.dirname(fileURLToPath(import.meta.url));
const ROOT = path.resolve(HERE, '..');
const read = relative => fs.readFileSync(path.join(ROOT, relative), 'utf8');

const indexHtml = read('public/index.html');
const authJs = read('public/auth.js');
const appJs = read('public/app.js');
const v06 = read('public/v06.js');
const v16 = read('public/v016.js');
const v19 = read('public/v019.js');
const v20 = read('public/v020.js');
const v21 = read('public/v021.js');
const build8 = read('public/v021b8.js');
const build10 = read('public/v021b10.js');
const build11 = read('public/v021b11.js');
const patch5Build3 = read('public/v0215b3.js');
const versionPatch = read('public/v0216.js');

assert.match(indexHtml, /<body class="cyacc-booting">/);
assert.match(indexHtml, /id="cyaccBootStatus"/);
assert.match(indexHtml, /__cyaccBootWatchdog/);
assert.match(indexHtml, /v021b8\.css\?v=0216b6/);
assert.match(indexHtml, /v021b9\.css\?v=0216b6/);
assert.match(indexHtml, /v021b10\.css\?v=0216b6/);
assert.match(indexHtml, /v021b11\.css\?v=0216b6/);
assert.match(indexHtml, /v0215\.css\?v=0216b6/);
assert.match(indexHtml, /rel="preload" as="style"/);
assert.match(indexHtml, /auth\.js\?v=0216b7/);
assert.match(indexHtml, /app\.js\?v=0216b7/);
assert.match(indexHtml, /v06\.js\?v=0216b7/);
assert.match(indexHtml, /v016\.js\?v=0216b7/);
assert.match(indexHtml, /v019\.js\?v=0216b7/);
assert.match(indexHtml, /v020\.js\?v=0216b6/);
assert.match(indexHtml, /v021\.js\?v=0216b7/);
assert.match(indexHtml, /v021b8\.js\?v=0216b6/);
assert.match(indexHtml, /v021b10\.js\?v=0216b6/);
assert.match(indexHtml, /v021b11\.js\?v=0216b6/);
assert.match(indexHtml, /v0216\.js\?v=0216b7/);

assert.match(authJs, /document\.readyState === 'loading'/);
assert.match(authJs, /startCyaccAuth/);
assert.match(authJs, /AbortController/);
assert.match(authJs, /8_000/);
assert.match(authJs, /window\.cyaccSessionPromise = checkSession\(\)/);
assert.match(authJs, /window\.cyaccCurrentUser = user/);
assert.match(appJs, /if \(window\.cyaccSessionPromise\) await window\.cyaccSessionPromise/);
assert.match(v06, /cyacc:core-ready/);
assert.match(v06, /if \(!window\.cyaccCoreReady\) return/);
assert.doesNotMatch(v16, /fetch\('\/api\/auth\/me'/);
assert.doesNotMatch(v19, /fetch\('\/api\/auth\/me'/);
assert.doesNotMatch(v21, /fetch\('\/api\/auth\/me'/);

assert.match(appJs, /document\.readyState === 'loading'/);
assert.match(appJs, /startCyaccApp/);
assert.match(appJs, /finishCyaccBoot/);
assert.match(appJs, /AbortController/);
assert.match(appJs, /12_000/);
assert.match(appJs, /連線逾時/);

assert.match(v06, /startV06LedgerTools/);
assert.match(v06, /document\.readyState === 'loading'/);
assert.match(v20, /startV20/);
assert.match(v20, /document\.readyState === 'loading'/);
assert.match(v21, /startV21/);
assert.match(v21, /document\.readyState === 'loading'/);

assert.match(build8, /startV21Build8/);
assert.match(build8, /runV21Build8Step\('mobile-pages', setupV21Build9MobilePages\)/);
assert.match(build8, /cyaccounting_mobile_build8_step_failed/);
assert.match(build8, /link\[href\^="\/v021b8\.css"/);
assert.match(build8, /link\[href\^="\/v021b9\.css"/);
assert.match(build10, /startV21Build10/);
assert.match(build10, /link\[href\^="\/v021b10\.css"/);
assert.match(build10, /runV21Build10Step\('mobile-app-bar', setupV21Build10MobileAppBar\)/);
assert.match(build10, /runV21Build10Step\('mobile-navigation', setupV21Build10MobileNavigation\)/);
assert.match(build10, /cyaccounting_mobile_build10_step_failed/);
assert.match(build11, /startV21Build11/);
assert.match(patch5Build3, /document\.readyState === 'loading'/);
assert.match(versionPatch, /CY_V0216_VERSION = 'V0\.21\.6 Build 7'/);
assert.match(versionPatch, /document\.readyState === 'loading'/);

console.log('Mobile deterministic startup regression checks passed.');
