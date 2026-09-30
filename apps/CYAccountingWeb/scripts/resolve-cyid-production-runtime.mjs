import fs from 'node:fs';
import process from 'node:process';

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

export async function resolveCyidProductionRuntime({
  accountId,
  apiToken,
  configuredService,
  fetchImpl = fetch
}) {
  const account = String(accountId || '').trim();
  const token = String(apiToken || '').trim();
  if (!account || !token) throw new Error('Cloudflare deployment credentials are required for CYID production runtime verification.');

  const service = String(configuredService || '').trim();
  if (!WORKER_NAME.test(service)) throw new Error('Configured CYID Service Binding target has an invalid format.');

  const url = `https://api.cloudflare.com/client/v4/accounts/${encodeURIComponent(account)}/workers/scripts/${encodeURIComponent(service)}/settings`;
  const response = await fetchImpl(url, {
    headers: { authorization: `Bearer ${token}` }
  });
  const payload = await response.json().catch(() => ({}));
  if (!response.ok || payload?.success === false) {
    throw new Error('Unable to verify the configured CYID production provider runtime.');
  }

  return parseCyidProductionSettings(payload, service);
}

async function cli() {
  try {
    const runtime = await resolveCyidProductionRuntime({
      accountId: process.env.CLOUDFLARE_ACCOUNT_ID,
      apiToken: process.env.CLOUDFLARE_API_TOKEN,
      configuredService: process.env.CF_IDENTITY_SERVICE
    });

    for (const value of [runtime.service, runtime.applicationId, runtime.workspaceId]) {
      console.log(`::add-mask::${value}`);
    }

    const githubEnv = String(process.env.GITHUB_ENV || '').trim();
    if (!githubEnv) throw new Error('GITHUB_ENV is unavailable.');
    fs.appendFileSync(
      githubEnv,
      `CF_CYID_APPLICATION_ID=${runtime.applicationId}\nCF_CYID_WORKSPACE_ID=${runtime.workspaceId}\n`,
      { encoding: 'utf8' }
    );

    console.log('CYID production consumer runtime verified and injected for this deployment.');
  } catch (error) {
    console.error(`CYID production runtime verification failed: ${error instanceof Error ? error.message : 'unknown error'}`);
    process.exitCode = 1;
  }
}

if (import.meta.url === new URL(`file://${process.argv[1]}`).href) {
  await cli();
}
