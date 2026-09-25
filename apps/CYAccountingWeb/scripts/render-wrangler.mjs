import fs from 'node:fs';
import path from 'node:path';
import process from 'node:process';
import { fileURLToPath } from 'node:url';

const HERE = path.dirname(fileURLToPath(import.meta.url));
const PROJECT_ROOT = path.resolve(HERE, '..');
const DEFAULT_TEMPLATE = path.join(PROJECT_ROOT, 'wrangler.template.jsonc');

const REQUIRED = {
  CF_WORKER_NAME: '__CF_WORKER_NAME__',
  CF_D1_DATABASE_NAME: '__CF_D1_DATABASE_NAME__',
  CF_D1_DATABASE_ID: '__CF_D1_DATABASE_ID__',
  CF_IDENTITY_SERVICE: '__CF_IDENTITY_SERVICE__'
};

function requireValue(env, name) {
  const value = String(env[name] || '').trim();
  if (!value) throw new Error(`Missing required deployment variable: ${name}`);
  return value;
}

function validateValues(values) {
  const workerLike = /^[a-z0-9][a-z0-9._-]{1,62}$/i;
  if (!workerLike.test(values.CF_WORKER_NAME)) throw new Error('CF_WORKER_NAME has an invalid format.');
  if (!/^[A-Za-z0-9][A-Za-z0-9._-]{0,127}$/.test(values.CF_D1_DATABASE_NAME)) throw new Error('CF_D1_DATABASE_NAME has an invalid format.');
  if (!/^[0-9a-f]{8}-[0-9a-f]{4}-[1-5][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i.test(values.CF_D1_DATABASE_ID)) {
    throw new Error('CF_D1_DATABASE_ID must be a valid UUID.');
  }
  if (!workerLike.test(values.CF_IDENTITY_SERVICE)) throw new Error('CF_IDENTITY_SERVICE has an invalid format.');
}

function jsonFragment(value) {
  return JSON.stringify(value).slice(1, -1);
}

export function renderWrangler({ env = process.env, templatePath = DEFAULT_TEMPLATE, outputPath }) {
  if (!outputPath) throw new Error('An output path is required.');
  const values = Object.fromEntries(Object.keys(REQUIRED).map(name => [name, requireValue(env, name)]));
  validateValues(values);

  let rendered = fs.readFileSync(templatePath, 'utf8');
  for (const [name, placeholder] of Object.entries(REQUIRED)) {
    if (!rendered.includes(placeholder)) throw new Error(`Template placeholder is missing: ${placeholder}`);
    rendered = rendered.replaceAll(placeholder, jsonFragment(values[name]));
  }
  if (/__CF_[A-Z0-9_]+__/.test(rendered)) throw new Error('Unresolved Cloudflare deployment placeholder remains.');

  const absoluteOutput = path.resolve(outputPath);
  fs.writeFileSync(absoluteOutput, rendered, { encoding: 'utf8', mode: 0o600 });
  return absoluteOutput;
}

function cli() {
  const index = process.argv.indexOf('--output');
  const outputPath = index >= 0 ? process.argv[index + 1] : '';
  if (!outputPath) {
    console.error('Usage: node scripts/render-wrangler.mjs --output <path>');
    process.exitCode = 2;
    return;
  }
  try {
    const output = renderWrangler({ outputPath });
    console.log(`Cloudflare deployment config rendered: ${path.basename(output)}`);
  } catch (error) {
    console.error(`Cloudflare deployment config render failed: ${error instanceof Error ? error.message : 'unknown error'}`);
    process.exitCode = 1;
  }
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) cli();
