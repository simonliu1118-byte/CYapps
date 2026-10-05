import fs from 'node:fs';
import path from 'node:path';

const root = path.resolve(import.meta.dirname, '..');
const templatePath = path.join(root, 'deploy-config.template.jsonc');
const outputArgIndex = process.argv.indexOf('--output');
const outputName = outputArgIndex >= 0 ? process.argv[outputArgIndex + 1] : 'wrangler.deploy.generated.jsonc';
if (!outputName || path.basename(outputName) !== outputName) {
  throw new Error('deploy config output must be a file name inside CYCloudIdentity');
}
const outputPath = path.join(root, outputName);

function required(name) {
  const value = (process.env[name] ?? '').trim();
  if (!value) throw new Error(`${name} is required`);
  return value;
}

function requirePattern(name, pattern, description) {
  const value = required(name);
  if (!pattern.test(value)) throw new Error(`${name} must be ${description}`);
  return value;
}

function requireInteger(name, min, max) {
  const value = required(name);
  if (!/^\d+$/.test(value)) throw new Error(`${name} must be an integer`);
  const number = Number(value);
  if (!Number.isSafeInteger(number) || number < min || number > max) {
    throw new Error(`${name} must be between ${min} and ${max}`);
  }
  return String(number);
}

function requireHttpUrl(name) {
  const value = required(name);
  let url;
  try {
    url = new URL(value);
  } catch {
    throw new Error(`${name} must be an absolute HTTP(S) URL`);
  }
  if (url.protocol !== 'https:' && url.protocol !== 'http:') {
    throw new Error(`${name} must be an absolute HTTP(S) URL`);
  }
  return value;
}

const values = {
  CYID_WORKER_NAME: requirePattern('CYID_WORKER_NAME', /^[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?$/, 'a valid lowercase Worker name'),
  CYID_D1_DATABASE_NAME: requirePattern('CYID_D1_DATABASE_NAME', /^[A-Za-z0-9_-]{1,64}$/, 'a valid D1 database name'),
  CYID_D1_DATABASE_ID: requirePattern('CYID_D1_DATABASE_ID', /^[0-9a-f]{8}-[0-9a-f]{4}-[1-5][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i, 'a UUID'),
  CYID_LOGIN_RATE_LIMIT_NAMESPACE_ID: requirePattern('CYID_LOGIN_RATE_LIMIT_NAMESPACE_ID', /^\d{1,20}$/, 'a numeric Rate Limit namespace id'),
  CYID_SESSION_TTL_SECONDS: requireInteger('CYID_SESSION_TTL_SECONDS', 900, 86400),
  CYID_EMAIL_PROVIDER: requirePattern('CYID_EMAIL_PROVIDER', /^(brevo|resend)$/i, 'brevo or resend').toLowerCase(),
  CYID_EMAIL_FROM: required('CYID_EMAIL_FROM'),
  CYID_EMAIL_DAILY_BUDGET: requireInteger('CYID_EMAIL_DAILY_BUDGET', 1, 10000),
  CYID_CORE_ACCOUNT_APPLICATION_ID: requirePattern('CYID_CORE_ACCOUNT_APPLICATION_ID', /^[A-Z0-9_-]{2,64}$/i, 'a valid Application ID').toUpperCase(),
  CYID_ACCOUNT_PORTAL_URL: requireHttpUrl('CYID_ACCOUNT_PORTAL_URL'),
};

if (/[\r\n]/.test(values.CYID_EMAIL_FROM) || values.CYID_EMAIL_FROM.length > 320) {
  throw new Error('CYID_EMAIL_FROM is invalid');
}

let rendered = fs.readFileSync(templatePath, 'utf8');
for (const [name, value] of Object.entries(values)) {
  const placeholder = `__${name}__`;
  if (!rendered.includes(placeholder)) throw new Error(`template placeholder missing: ${placeholder}`);
  rendered = rendered.split(placeholder).join(JSON.stringify(value).slice(1, -1));
}

if (/__CYID_[A-Z0-9_]+__/.test(rendered)) {
  throw new Error('unresolved deployment placeholder remains');
}

fs.writeFileSync(outputPath, rendered, { encoding: 'utf8', mode: 0o600 });
console.log(`Rendered ${outputName}`);
