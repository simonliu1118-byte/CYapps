import fs from 'node:fs';
import path from 'node:path';
import process from 'node:process';
import { fileURLToPath } from 'node:url';
import { requireProductionCoreConsumer } from '../../CYCloudIdentity/scripts/render-production-config.mjs';

const WORKER_NAME = /^[a-z0-9][a-z0-9._-]{1,62}$/i;
const APPLICATION_ID = /^[A-Z0-9][A-Z0-9_-]{1,63}$/;
const WORKSPACE_ID = /^[A-Za-z0-9][A-Za-z0-9._:-]{4,79}$/;

export function parseCyidProductionSettings(payload, configuredService) {
  const service = String(configuredService || '').trim();
  if (!WORKER_NAME.test(service)) throw new Error('Configured CYID Service Binding target has an invalid format.');

  const result = payload?.result && typeof payload.result === 'object' ? payload.result : payload;
  const bindings = Array.isArray(result?.bindings) ? result.bindings : [];
  const byName = new Map(bindings.map(binding => [String(binding?.name || ''), binding]));
  const text = name => String(byName.get(name)?.text ?? '').trim();

  if (text('APP_ENV') !== 'production') {
    throw new Error('Configured Identity service is not a CYID production provider.');
  }

  const providerTarget = text('CYACC_SERVICE_BINDING_TARGET');
  if (!WORKER_NAME.test(providerTarget) || providerTarget !== service) {
    throw new Error('Configured Identity service does not match the provider production binding target.');
  }

  const applicationId = text('CYACC_APPLICATION_ID').toUpperCase();
  const workspaceId = text('CYACC_WORKSPACE_ID');
  if (!APPLICATION_ID.test(applicationId)) {
    throw new Error('CYID production provider is missing a valid CYACC Application ID.');
  }
  if (!WORKSPACE_ID.test(workspaceId)) {
    throw new Error('CYID production provider is missing a valid CYACC Workspace ID.');
  }

  return { service, applicationId, workspaceId };
}

export function deriveCyidProductionServiceName(developmentService) {
  const source = String(developmentService || '').trim();
  if (!WORKER_NAME.test(source)) throw new Error('CYID development Worker name has an invalid format.');
  if (source.endsWith('-development')) return source.slice(0, -'-development'.length) + '-production';
  if (source.endsWith('-dev')) return source.slice(0, -'-dev'.length) + '-prod';
  return source + '-prod';
}

export async function resolveCyidProductionRuntime({
  accountId,
  apiToken,
  developmentService,
  fetchImpl = fetch
}) {
  const account = String(accountId || '').trim();
  const token = String(apiToken || '').trim();
  if (!account || !token) throw new Error('Cloudflare deployment credentials are required for CYID production runtime verification.');

  const service = deriveCyidProductionServiceName(developmentService);
  const url = `https://api.cloudflare.com/client/v4/accounts/${encodeURIComponent(account)}/workers/scripts/${encodeURIComponent(service)}/settings`;
  const response = await fetchImpl(url, {
    headers: { authorization: `Bearer ${token}` }
  });
  const payload = await response.json().catch(() => ({}));
  if (!response.ok || payload?.success === false) {
    throw new Error('Unable to verify the configured CYID production provider runtime.');
  }

  const runtime = parseCyidProductionSettings(payload, service);
  const portalUrl = await verifyCyWebProductionPortal({ account, token, runtime, provider: payload, fetchImpl });
  return { ...runtime, portalUrl };
}

// Read-only deployment prerequisite. Resolve the configured portal to its actual
// Worker and use CYID's canonical production-consumer guard; URL format alone
// cannot prove that first-login credentials reach the same authority/workspace.
export async function verifyCyWebProductionPortal({ account, token, runtime, provider, fetchImpl = fetch }) {
  const bindings = provider?.result?.bindings || provider?.bindings || [];
  const text = name => String(bindings.find(binding => binding.name === name && binding.type === 'plain_text')?.text || '').trim();
  let portal;
  try {
    portal = new URL(text('ACCOUNT_PORTAL_URL'));
    if (portal.protocol !== 'https:' || portal.username || portal.password || portal.search || portal.hash || portal.pathname !== '/') throw new Error('unsafe');
  } catch { throw new Error('CYWEB_PRODUCTION_PORTAL_INVALID'); }
  const coreApplicationId = text('CORE_ACCOUNT_APPLICATION_ID');
  if (!APPLICATION_ID.test(coreApplicationId)) throw new Error('CYWEB_PRODUCTION_APPLICATION_INVALID');
  const api = `https://api.cloudflare.com/client/v4/accounts/${encodeURIComponent(account)}`;
  const readSettings = async url => {
    const response = await fetchImpl(url, { headers: { authorization: `Bearer ${token}` }, redirect: 'error' });
    const result = await response.json().catch(() => ({}));
    if (!response.ok || result.success !== true) throw new Error('CYWEB_PRODUCTION_SETTINGS_UNAVAILABLE');
    return result;
  };
  const domains = await readSettings(`${api}/workers/domains?hostname=${encodeURIComponent(portal.hostname)}`);
  const matches = (domains.result || []).filter(row => row.hostname === portal.hostname && row.enabled !== false && [undefined, 'production'].includes(row.environment));
  if (matches.length !== 1 || !WORKER_NAME.test(matches[0].service || '')) throw new Error('CYWEB_PRODUCTION_DOMAIN_UNVERIFIED');
  const core = await readSettings(`${api}/workers/scripts/${encodeURIComponent(matches[0].service)}/settings`);
  const plain = name => core.result?.bindings?.find(binding => binding.name === name && binding.type === 'plain_text')?.text;
  const version = String(plain('IDENTITY_CONSUMER_VERSION') || '');
  if (plain('IDENTITY_WORKSPACE_ID') !== runtime.workspaceId || !/^\d+\.\d+\.\d+$/.test(version)) throw new Error('CYWEB_PRODUCTION_WORKSPACE_MISMATCH');
  const response = await fetchImpl(new URL('/api/health', portal).href, { redirect: 'error' });
  if (!response.ok) throw new Error('CYWEB_PRODUCTION_HEALTH_UNAVAILABLE');
  const health = await response.json().catch(() => ({}));
  requireProductionCoreConsumer({ settings: core, health, providerName: runtime.service, applicationId: coreApplicationId, consumerVersion: version });
  return portal.href;
}

async function cli() {
  try {
    const runtime = await resolveCyidProductionRuntime({
      accountId: process.env.CLOUDFLARE_ACCOUNT_ID,
      apiToken: process.env.CLOUDFLARE_API_TOKEN,
      developmentService: process.env.CYID_WORKER_NAME
    });

    for (const value of [runtime.service, runtime.applicationId, runtime.workspaceId, runtime.portalUrl]) {
      console.log(`::add-mask::${value}`);
    }

    const githubEnv = String(process.env.GITHUB_ENV || '').trim();
    if (!githubEnv) throw new Error('GITHUB_ENV is unavailable.');
    fs.appendFileSync(
      githubEnv,
      `CF_IDENTITY_SERVICE=${runtime.service}\nCF_CYID_APPLICATION_ID=${runtime.applicationId}\nCF_CYID_WORKSPACE_ID=${runtime.workspaceId}\nCF_CYWEB_PORTAL_URL=${runtime.portalUrl}\n`,
      { encoding: 'utf8' }
    );

    console.log('CYID production consumer runtime verified and injected for this deployment.');
    console.log('CY Web production portal domain, CYID authority/workspace and consumer health verified.');
  } catch (error) {
    console.error(`CYID production runtime verification failed: ${error instanceof Error ? error.message : 'unknown error'}`);
    process.exitCode = 1;
  }
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  await cli();
}
