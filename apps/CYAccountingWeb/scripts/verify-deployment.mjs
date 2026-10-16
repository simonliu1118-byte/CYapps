import { execFileSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';
import path from 'node:path';

export const DEPLOYMENT_BRANCH = 'deploy/cyaccountingweb-production';

export function isDeploymentEvent(event, branch) {
  return (event === 'push' && branch === DEPLOYMENT_BRANCH)
    || (event === 'workflow_dispatch' && branch === 'main');
}

export function verifySource({ event, ref, eventSha, checkoutSha, mainSha }) {
  const branch = ref?.startsWith('refs/heads/') ? ref.slice('refs/heads/'.length) : '';
  if (!isDeploymentEvent(event, branch)) throw new Error('DEPLOYMENT_EVENT_NOT_ALLOWED');
  if (![eventSha, checkoutSha, mainSha].every(sha => /^[0-9a-f]{40}$/.test(sha ?? '')))
    throw new Error('DEPLOYMENT_SOURCE_SHA_REQUIRED');
  if (eventSha !== checkoutSha || eventSha !== mainSha)
    throw new Error('DEPLOYMENT_SOURCE_MUST_MATCH_CURRENT_MAIN');
}

// A successful validation workflow or a skipped deploy job is not deployment evidence.
export async function verifyPublishedSource({ repository, sha, token, fetchImpl = fetch }) {
  if (!/^[A-Za-z0-9_.-]+\/[A-Za-z0-9_.-]+$/.test(repository ?? '')
      || repository.split('/').some(part => part === '.' || part === '..')
      || !/^[0-9a-f]{40}$/.test(sha ?? '') || !token)
    throw new Error('DEPLOYMENT_EVIDENCE_INPUT_REQUIRED');
  async function get(endpoint) {
    const response = await fetchImpl(`https://api.github.com/repos/${repository}/${endpoint}`, {
      method: 'GET',
      redirect: 'error',
      headers: { authorization: `Bearer ${token}`, accept: 'application/vnd.github+json', 'X-GitHub-Api-Version': '2022-11-28' },
      signal: AbortSignal.timeout(15_000),
    });
    if (!response.ok) throw new Error(`DEPLOYMENT_EVIDENCE_HTTP_${response.status}`);
    return response.json();
  }
  for (let page = 1; ; page++) {
    const data = await get(`actions/workflows/cyaccountingweb-deploy.yml/runs?head_sha=${sha}&per_page=100&page=${page}`);
    if (!Array.isArray(data.workflow_runs)) throw new Error('DEPLOYMENT_EVIDENCE_INVALID_RESPONSE');
    for (const run of data.workflow_runs) {
      if (run.head_sha !== sha || run.status !== 'completed' || run.conclusion !== 'success'
          || !isDeploymentEvent(run.event, run.head_branch)) continue;
      if (!Number.isSafeInteger(run.id) || run.id <= 0) throw new Error('DEPLOYMENT_EVIDENCE_INVALID_RUN');
      // Use the matching attempt: a later rerun must not validate an earlier successful result.
      if (!Number.isSafeInteger(run.run_attempt) || run.run_attempt <= 0)
        throw new Error('DEPLOYMENT_EVIDENCE_INVALID_ATTEMPT');
      for (let jobPage = 1; ; jobPage++) {
        const jobs = await get(`actions/runs/${run.id}/attempts/${run.run_attempt}/jobs?per_page=100&page=${jobPage}`);
        if (!Array.isArray(jobs.jobs)) throw new Error('DEPLOYMENT_EVIDENCE_INVALID_JOBS');
        if (jobs.jobs.some(job => job.name === 'deploy' && job.status === 'completed' && job.conclusion === 'success'))
          return run.id;
        if (jobs.jobs.length < 100) break;
      }
    }
    if (data.workflow_runs.length < 100) break;
  }
  throw new Error('SUCCESSFUL_PRODUCTION_DEPLOYMENT_REQUIRED');
}

async function main(mode) {
  if (mode === 'source') {
    let checkoutSha, mainSha;
    try {
      checkoutSha = execFileSync('git', ['rev-parse', 'HEAD'], { encoding: 'utf8', stdio: ['ignore', 'pipe', 'pipe'] }).trim();
      const remote = execFileSync('git', ['ls-remote', '--exit-code', 'origin', 'refs/heads/main'], { encoding: 'utf8', stdio: ['ignore', 'pipe', 'pipe'], timeout: 30_000 }).trim();
      if (!/^[0-9a-f]{40}\s+refs\/heads\/main$/.test(remote)) throw new Error();
      mainSha = remote.split(/\s+/)[0];
    } catch {
      throw new Error('DEPLOYMENT_MAIN_SOURCE_LOOKUP_FAILED');
    }
    verifySource({ event: process.env.GITHUB_EVENT_NAME, ref: process.env.GITHUB_REF, eventSha: process.env.GITHUB_SHA, checkoutSha, mainSha });
    console.log('Deployment source matches the current reviewed main commit.');
  } else if (mode === 'release') {
    await verifyPublishedSource({ repository: process.env.GITHUB_REPOSITORY, sha: process.env.RELEASE_SHA, token: process.env.GH_TOKEN });
    console.log('The release source has a successful production deploy job.');
  } else {
    throw new Error('EXPECTED_SOURCE_OR_RELEASE_MODE');
  }
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  main(process.argv[2]).catch(error => {
    console.error(error.message);
    process.exitCode = 1;
  });
}
