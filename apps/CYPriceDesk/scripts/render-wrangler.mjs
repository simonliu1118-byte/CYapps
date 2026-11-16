import fs from 'node:fs';
import path from 'node:path';
import process from 'node:process';
import { fileURLToPath } from 'node:url';

const HERE = path.dirname(fileURLToPath(import.meta.url));
const DEFAULT_TEMPLATE = path.join(HERE, '..', 'wrangler.template.jsonc');

// Deployment-specific values are injected from the GitHub Environment; source only holds placeholders.
const REQUIRED = {
  CF_WORKER_NAME: '__CF_WORKER_NAME__',
  CF_D1_DATABASE_NAME: '__CF_D1_DATABASE_NAME__',
  CF_D1_DATABASE_ID: '__CF_D1_DATABASE_ID__',
  CF_IDENTITY_SERVICE: '__CF_IDENTITY_SERVICE__',
  CF_CYID_APPLICATION_ID: '__CF_CYID_APPLICATION_ID__',
  CF_CYID_WORKSPACE_ID: '__CF_CYID_WORKSPACE_ID__',
  CF_CYWEB_PORTAL_URL: '__CF_CYWEB_PORTAL_URL__',
  CF_CYPD_FETCHER_URL: '__CF_CYPD_FETCHER_URL__',
  CF_CYPRICEDESK_CUSTOM_DOMAIN: '__CF_CUSTOM_DOMAIN__'
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
  if (!/^[0-9a-f]{8}-[0-9a-f]{4}-[1-5][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i.test(values.CF_D1_DATABASE_ID)) throw new Error('CF_D1_DATABASE_ID must be a valid UUID.');
  if (!workerLike.test(values.CF_IDENTITY_SERVICE)) throw new Error('CF_IDENTITY_SERVICE has an invalid format.');
  if (!/^[A-Z0-9][A-Z0-9_-]{1,63}$/.test(values.CF_CYID_APPLICATION_ID)) throw new Error('CF_CYID_APPLICATION_ID has an invalid format.');
  if (!/^[A-Za-z0-9][A-Za-z0-9._:-]{4,79}$/.test(values.CF_CYID_WORKSPACE_ID)) throw new Error('CF_CYID_WORKSPACE_ID has an invalid format.');
  let url;
  try { url = new URL(values.CF_CYWEB_PORTAL_URL); } catch { throw new Error('CF_CYWEB_PORTAL_URL must be HTTPS.'); }
  if (url.protocol !== 'https:' || !url.hostname || url.username || url.password || url.search || url.hash || url.pathname !== '/') {
    throw new Error('CF_CYWEB_PORTAL_URL must be a plain HTTPS root URL.');
  }
  // The fetcher is optional: without it the site runs with auto query and scheduling shown as not configured.
  if (values.CF_CYPD_FETCHER_URL) {
    let fetcher;
    try { fetcher = new URL(values.CF_CYPD_FETCHER_URL); } catch { throw new Error('CF_CYPD_FETCHER_URL must be HTTPS.'); }
    if (fetcher.protocol !== 'https:' || !fetcher.hostname || fetcher.username || fetcher.password || fetcher.search || fetcher.hash) {
      throw new Error('CF_CYPD_FETCHER_URL must be an HTTPS URL without credentials, query or fragment.');
    }
  }
  const hostname = /^(?=.{1,253}$)(?:[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?\.)+[a-z]{2,63}$/i;
  if (!hostname.test(values.CF_CYPRICEDESK_CUSTOM_DOMAIN)) throw new Error('CF_CYPRICEDESK_CUSTOM_DOMAIN must be a valid hostname.');
}

export function renderWrangler({ env = process.env, templatePath = DEFAULT_TEMPLATE, outputPath }) {
  if (!outputPath) throw new Error('An output path is required.');
  let rendered = fs.readFileSync(templatePath, 'utf8');
  const values = Object.fromEntries(Object.keys(REQUIRED).map(name => [name, name === 'CF_CYPD_FETCHER_URL' ? String(env[name] || '').trim() : requireValue(env, name)]));
  validateValues(values);
  for (const [name, placeholder] of Object.entries(REQUIRED)) {
    rendered = rendered.replaceAll(placeholder, JSON.stringify(values[name]).slice(1, -1));
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
    console.log(`Cloudflare deployment config rendered: ${path.basename(renderWrangler({ outputPath }))}`);
  } catch (error) {
    console.error(`Cloudflare deployment config render failed: ${error instanceof Error ? error.message : 'unknown error'}`);
    process.exitCode = 1;
  }
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) cli();
