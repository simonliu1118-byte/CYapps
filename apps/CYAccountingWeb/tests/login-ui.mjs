import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const HERE = path.dirname(fileURLToPath(import.meta.url));
const ROOT = path.resolve(HERE, '..');
const read = relative => fs.readFileSync(path.join(ROOT, relative), 'utf8');

const html = read('public/login.html');
const css = read('public/login.css');
const js = read('public/login.js');

assert.match(html, /viewport-fit=cover/);
assert.match(html, /interactive-widget=resizes-content/);
assert.doesNotMatch(html, /\sautofocus(?:\s|>)/);
assert.match(html, /login\.css\?rev=keyboard-aware-layout/);
assert.match(html, /login\.js\?rev=keyboard-aware-layout/);

assert.match(css, /html,\s*body\s*\{[\s\S]*?height:\s*100%;[\s\S]*?overflow:\s*hidden;/);
assert.match(css, /\.login-shell\s*\{[\s\S]*?height:\s*var\(--login-visible-height, 100vh\);[\s\S]*?overflow-y:\s*auto;/);
assert.match(css, /@media \(min-width: 561px\) and \(max-width: 1023px\)/);
assert.match(css, /body\.login-keyboard-open \.login-shell\s*\{[\s\S]*?align-items:\s*start;/);
assert.match(css, /body\.login-keyboard-open \.login-card\s*\{[\s\S]*?padding:\s*14px 16px;/);
assert.match(css, /body\.login-keyboard-open \.brand-mark\s*\{[\s\S]*?width:\s*36px;/);

assert.match(js, /const viewport = window\.visualViewport/);
assert.match(js, /viewport\?\.addEventListener\('resize', scheduleSync\)/);
assert.match(js, /document\.documentElement\.style\.setProperty\('--login-visible-height'/);
assert.match(js, /document\.body\.classList\.toggle\('login-keyboard-open', keyboardOpen\)/);
assert.match(js, /heightLoss >= 120/);
assert.match(js, /loginShell\.scrollTop = 0/);
assert.match(js, /window\.scrollTo\(0, 0\)/);
assert.match(js, /\(min-width: 1024px\) and \(hover: hover\) and \(pointer: fine\)/);
assert.match(js, /loginEmployeeNo\?\.focus\(\{ preventScroll: true \}\)/);

console.log('Login viewport and keyboard regression checks passed.');
