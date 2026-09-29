import { createHash } from 'node:crypto';
import { mkdir, rm, writeFile } from 'node:fs/promises';
import { gunzipSync } from 'node:zlib';

const VERSION = '1.14.2';
const TARBALL_URL = `https://registry.npmjs.org/sql.js/-/sql.js-${VERSION}.tgz`;
const EXPECTED_TARBALL_SHA512_BASE64 = '3ZGPovObMFrdw79zrUHbfdE/DLIsy8jdNdssmMSQuRAymedU6q84asPt0kgiqrdMYlPegDItiIMfmIXzZnYFcw==';
const EXPECTED_WASM_SHA256 = '38c14f6e379210bc942bdc4ebca44e7bfdb4318ecc1c72ca666a28fdce96670a';
const EXPECTED_WASM_BYTES = 658410;
const OUT_DIR = new URL('../public/vendor/sqljs/', import.meta.url);

if (process.argv.includes('--clean')) {
  await rm(OUT_DIR, { recursive: true, force: true });
  process.exit(0);
}

const response = await fetch(TARBALL_URL, { redirect: 'follow' });
if (!response.ok) throw new Error(`sql.js download failed: HTTP ${response.status}`);
const tgz = Buffer.from(await response.arrayBuffer());
const integrity = createHash('sha512').update(tgz).digest('base64');
if (integrity !== EXPECTED_TARBALL_SHA512_BASE64) {
  throw new Error(`sql.js tarball integrity mismatch: ${integrity}`);
}

const tar = gunzipSync(tgz);
const wanted = new Map([
  ['package/dist/sql-wasm.js', 'sql-wasm.js'],
  ['package/dist/sql-wasm.wasm', 'sql-wasm.wasm'],
  ['package/LICENSE', 'LICENSE']
]);
const extracted = new Map();

for (let offset = 0; offset + 512 <= tar.length;) {
  const header = tar.subarray(offset, offset + 512);
  if (header.every(byte => byte === 0)) break;
  const name = cString(header.subarray(0, 100));
  const prefix = cString(header.subarray(345, 500));
  const fullName = prefix ? `${prefix}/${name}` : name;
  const sizeText = cString(header.subarray(124, 136)).trim();
  const size = sizeText ? Number.parseInt(sizeText, 8) : 0;
  if (!Number.isFinite(size) || size < 0) throw new Error(`Invalid tar entry size for ${fullName}`);
  const contentStart = offset + 512;
  const contentEnd = contentStart + size;
  if (contentEnd > tar.length) throw new Error(`Truncated tar entry ${fullName}`);
  if (wanted.has(fullName)) extracted.set(fullName, Buffer.from(tar.subarray(contentStart, contentEnd)));
  offset = contentStart + Math.ceil(size / 512) * 512;
}

for (const sourcePath of wanted.keys()) {
  if (!extracted.has(sourcePath)) throw new Error(`sql.js package missing ${sourcePath}`);
}

const wasm = extracted.get('package/dist/sql-wasm.wasm');
const wasmSha256 = sha256(wasm);
if (wasm.length !== EXPECTED_WASM_BYTES || wasmSha256 !== EXPECTED_WASM_SHA256) {
  throw new Error(`sql.js wasm mismatch: size=${wasm.length} sha256=${wasmSha256}`);
}

await rm(OUT_DIR, { recursive: true, force: true });
await mkdir(OUT_DIR, { recursive: true });
const manifest = {
  package: 'sql.js',
  version: VERSION,
  source: TARBALL_URL,
  tarballSha512: integrity,
  files: {}
};
for (const [sourcePath, fileName] of wanted.entries()) {
  const bytes = extracted.get(sourcePath);
  await writeFile(new URL(fileName, OUT_DIR), bytes);
  manifest.files[fileName] = { bytes: bytes.length, sha256: sha256(bytes) };
}
await writeFile(new URL('SOURCE.json', OUT_DIR), `${JSON.stringify(manifest, null, 2)}\n`, 'utf8');
console.log(`Prepared sql.js ${VERSION} in public/vendor/sqljs (${wasm.length} byte wasm).`);

function cString(buffer) {
  const end = buffer.indexOf(0);
  return buffer.subarray(0, end < 0 ? buffer.length : end).toString('utf8');
}

function sha256(bytes) {
  return createHash('sha256').update(bytes).digest('hex');
}
