import fs from 'node:fs';
const root = new URL('../', import.meta.url);
const migrations = fs.readdirSync(new URL('migrations/', root)).filter(name => /^\d+_.*\.sql$/.test(name)).sort();
const metadata = {
  appVersion: fs.readFileSync(new URL('VERSION', root), 'utf8').trim(),
  schemaVersion: Number(migrations.at(-1).split('_')[0])
};
const file = new URL('src/build-metadata.json', root);
const text = JSON.stringify(metadata, null, 2) + '\n';
if (process.argv.includes('--check')) {
  if (fs.readFileSync(file, 'utf8') !== text) throw new Error('Build metadata does not match VERSION and migrations.');
} else fs.writeFileSync(file, text);
