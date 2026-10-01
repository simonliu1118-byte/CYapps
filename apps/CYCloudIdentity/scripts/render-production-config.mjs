import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

export function productionWorkerName(developmentName) {
  if (!/^[a-z0-9][a-z0-9-]{1,62}$/.test(developmentName ?? '')) throw new Error('INVALID_DEVELOPMENT_WORKER_NAME');
  const name = developmentName.endsWith('-development') ? developmentName.replace(/-development$/, '-production')
    : developmentName.endsWith('-dev') ? developmentName.replace(/-dev$/, '-prod') : `${developmentName}-prod`;
  if (name.length > 63) throw new Error('INVALID_PRODUCTION_WORKER_NAME');
  return name;
}

/** Preserve the existing production bindings/variables; never copy development authority/configuration. */
export function buildProductionConfig({ settings, database, workerName, developmentDatabaseId }) {
  if (!settings || !Array.isArray(settings.bindings)) throw new Error('PRODUCTION_SETTINGS_MISSING');
  if (!/^[a-z0-9][a-z0-9-]{1,62}$/.test(workerName ?? '')) throw new Error('INVALID_PRODUCTION_WORKER_NAME');
  const bindings = settings.bindings;
  if (bindings.some(b => !['plain_text', 'secret_text', 'd1', 'ratelimit'].includes(b.type))) throw new Error('UNMODELED_PRODUCTION_BINDING');
  const vars = Object.fromEntries(bindings.filter(b => b.type === 'plain_text').map(b => [b.name, b.text]));
  if (vars.APP_ENV !== 'production') throw new Error('PRODUCTION_ENVIRONMENT_REQUIRED');
  for (const name of ['API_VERSION', 'SESSION_TTL_SECONDS', 'EMAIL_PROVIDER', 'EMAIL_FROM', 'EMAIL_DAILY_BUDGET', 'CORE_ACCOUNT_APPLICATION_ID', 'ACCOUNT_PORTAL_URL']) {
    if (typeof vars[name] !== 'string' || !vars[name].trim()) throw new Error('PRODUCTION_RUNTIME_VALUE_MISSING');
  }
  if (!/^\d+$/.test(vars.SESSION_TTL_SECONDS) || Number(vars.SESSION_TTL_SECONDS) < 900 || Number(vars.SESSION_TTL_SECONDS) > 86400) throw new Error('INVALID_PRODUCTION_SESSION_TTL');
  if (!/^\d+$/.test(vars.EMAIL_DAILY_BUDGET) || Number(vars.EMAIL_DAILY_BUDGET) < 1 || Number(vars.EMAIL_DAILY_BUDGET) > 10000) throw new Error('INVALID_PRODUCTION_EMAIL_BUDGET');
  if (!/^[A-Z0-9_-]{2,64}$/.test(vars.CORE_ACCOUNT_APPLICATION_ID)) throw new Error('INVALID_PRODUCTION_CORE_APPLICATION');
  if (!['brevo', 'resend'].includes(vars.EMAIL_PROVIDER)) throw new Error('INVALID_PRODUCTION_EMAIL_PROVIDER');
  if (!bindings.some(b => b.type === 'secret_text' && b.name === (vars.EMAIL_PROVIDER === 'brevo' ? 'BREVO_API_KEY' : 'RESEND_API_KEY'))) throw new Error('PRODUCTION_EMAIL_SECRET_MISSING');
  if (new URL(vars.ACCOUNT_PORTAL_URL).protocol !== 'https:') throw new Error('INVALID_PRODUCTION_PORTAL');
  const dbBindings = bindings.filter(b => b.type === 'd1');
  if (dbBindings.length !== 1 || dbBindings[0].name !== 'DB') throw new Error('PRODUCTION_DATABASE_BINDING_REQUIRED');
  const id = dbBindings[0].id;
  if (!/^[0-9a-f]{8}-[0-9a-f]{4}-[1-5][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i.test(id ?? '') || id === developmentDatabaseId) throw new Error('PRODUCTION_DATABASE_ISOLATION_REQUIRED');
  if (database?.uuid !== id || !/^[A-Za-z0-9_-]{1,64}$/.test(database?.name ?? '')) throw new Error('PRODUCTION_DATABASE_METADATA_MISMATCH');
  const rates = bindings.filter(b => b.type === 'ratelimit');
  if (rates.length !== 1 || rates[0].name !== 'LOGIN_RATE_LIMITER' || !/^\d{1,20}$/.test(String(rates[0].namespace_id))
      || !Number.isSafeInteger(rates[0].simple?.limit) || rates[0].simple.limit < 1 || ![10, 60].includes(rates[0].simple?.period)) throw new Error('PRODUCTION_RATE_LIMIT_REQUIRED');
  return {
    name: workerName, main: 'src/index.ts', compatibility_date: '2026-09-25', workers_dev: true,
    compatibility_flags: [...new Set([...(settings.compatibility_flags ?? []), 'nodejs_compat'])],
    d1_databases: [{ binding: 'DB', database_name: database.name, database_id: id, migrations_dir: 'migrations' }],
    ratelimits: [{ name: 'LOGIN_RATE_LIMITER', namespace_id: String(rates[0].namespace_id), simple: rates[0].simple }], vars,
  };
}

/** A healthy development consumer cannot satisfy production retirement readiness. */
export function requireProductionCoreConsumer({ settings, health, providerName, applicationId, consumerVersion }) {
  const bindings = settings?.result?.bindings;
  const plain = name => bindings?.find(b => b.type === 'plain_text' && b.name === name)?.text;
  const identity = bindings?.find(b => b.type === 'service' && b.name === 'IDENTITY');
  if (settings?.success !== true || !Array.isArray(bindings)
      || identity?.service !== providerName || ![undefined, 'production'].includes(identity?.environment)
      || plain('IDENTITY_APPLICATION_ID') !== applicationId || plain('IDENTITY_CONSUMER_VERSION') !== consumerVersion
      || health?.ok !== true || health.data?.service !== 'cyweb' || health.data?.database !== 'ok'
      || health.data?.identityConsumerVersion !== consumerVersion) throw new Error('PRODUCTION_CORE_CONSUMER_NOT_READY');
}

function cli() {
  const [settingsPath, databasePath, outputPath] = process.argv.slice(2);
  const settings = JSON.parse(fs.readFileSync(settingsPath, 'utf8'));
  const database = JSON.parse(fs.readFileSync(databasePath, 'utf8'));
  if (settings.success !== true || database.success !== true || !database.result) throw new Error('PRODUCTION_RESOURCE_READ_FAILED');
  const config = buildProductionConfig({ settings: settings.result, database: database.result, workerName: process.env.PROD_CYID_WORKER_NAME, developmentDatabaseId: process.env.DEV_CYID_D1_DATABASE_ID });
  fs.writeFileSync(outputPath, JSON.stringify(config, null, 2)+'\n', { mode: 0o600 });
  const health = new URL('/api/health', config.vars.ACCOUNT_PORTAL_URL).href;
  console.log(`::add-mask::${health}`);
  console.log(`::add-mask::${config.vars.CORE_ACCOUNT_APPLICATION_ID}`);
  fs.appendFileSync(process.env.GITHUB_ENV, `CYID_CORE_HEALTH_URL=${health}\nCYID_PROD_CORE_APPLICATION_ID=${config.vars.CORE_ACCOUNT_APPLICATION_ID}\n`);
}
if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) cli();
