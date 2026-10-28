import assert from 'node:assert/strict';
import { test } from 'node:test';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { execFileSync, spawnSync } from 'node:child_process';
import { DEPLOYMENT_BRANCH, isDeploymentEvent, verifySource, verifyPublishedSource } from '../scripts/verify-deployment.mjs';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const sha = 'a'.repeat(40);
const otherSha = 'b'.repeat(40);
const goodRun = { id: 123, run_attempt: 2, event: 'push', head_branch: DEPLOYMENT_BRANCH, head_sha: sha, status: 'completed', conclusion: 'success' };
const deployJob = { name: 'deploy', status: 'completed', conclusion: 'success' };

test('only the explicit deployment branch or main dispatch can publish', () => {
  for (const [event, branch, allowed] of [
    ['push', DEPLOYMENT_BRANCH, true], ['workflow_dispatch', 'main', true],
    ['push', 'main', false], ['pull_request', DEPLOYMENT_BRANCH, false],
    ['workflow_dispatch', 'feature/change', false], ['push', 'deploy/other', false],
  ]) assert.equal(isDeploymentEvent(event, branch), allowed, `${event}: ${branch}`);
  const source = { event: 'push', ref: `refs/heads/${DEPLOYMENT_BRANCH}`, eventSha: sha, checkoutSha: sha, mainSha: sha };
  verifySource(source);
  verifySource({ ...source, event: 'workflow_dispatch', ref: 'refs/heads/main' });
  for (const override of [{ event: 'pull_request' }, { ref: 'refs/heads/main' }, { ref: `refs/tags/${DEPLOYMENT_BRANCH}` }])
    assert.throws(() => verifySource({ ...source, ...override }), /EVENT_NOT_ALLOWED/);
  for (const override of [{ eventSha: otherSha }, { checkoutSha: otherSha }, { mainSha: otherSha }])
    assert.throws(() => verifySource({ ...source, ...override }), /MUST_MATCH_CURRENT_MAIN/);
  assert.throws(() => verifySource({ ...source, mainSha: '' }), /SHA_REQUIRED/);
});

test('the source CLI checks the real checkout and remote main, including queued stale commits', () => {
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'cyacc-deployment-'));
  const origin = path.join(dir, 'origin.git');
  const checkout = path.join(dir, 'checkout');
  const git = (...args) => execFileSync('git', ['-C', checkout, ...args], { encoding: 'utf8', stdio: ['ignore', 'pipe', 'pipe'] }).trim();
  try {
    execFileSync('git', ['init', '--bare', origin], { stdio: 'ignore' });
    execFileSync('git', ['init', '--initial-branch=main', checkout], { stdio: 'ignore' });
    git('config', 'user.name', 'Deployment test');
    git('config', 'user.email', '286269326+simonliu1118-byte@users.noreply.github.com');
    fs.writeFileSync(path.join(checkout, 'source.txt'), 'reviewed\n');
    git('add', '.'); git('commit', '-m', 'Reviewed source');
    git('remote', 'add', 'origin', origin); git('push', 'origin', 'main');
    const reviewed = git('rev-parse', 'HEAD');
    const run = () => spawnSync(process.execPath, [path.join(root, 'scripts/verify-deployment.mjs'), 'source'], {
      cwd: checkout, encoding: 'utf8', env: { ...process.env, GITHUB_EVENT_NAME: 'push', GITHUB_REF: `refs/heads/${DEPLOYMENT_BRANCH}`, GITHUB_SHA: reviewed },
    });
    assert.equal(run().status, 0);
    fs.writeFileSync(path.join(checkout, 'source.txt'), 'newer reviewed source\n');
    git('commit', '-am', 'Newer main source'); git('push', 'origin', 'main');
    git('checkout', '--detach', reviewed);
    const stale = run();
    assert.equal(stale.status, 1);
    assert.match(stale.stderr, /MUST_MATCH_CURRENT_MAIN/);
  } finally { fs.rmSync(dir, { recursive: true, force: true }); }
});

function fixture({ runs = [goodRun], jobs = [deployJob], runPages, jobPages, status = 200 } = {}) {
  const calls = [];
  const fetchImpl = async (url, options) => {
    calls.push(url);
    assert.equal(options.method, 'GET');
    assert.equal(options.redirect, 'error');
    const u = new URL(url);
    assert.equal(u.origin, 'https://api.github.com');
    assert.ok(u.pathname.startsWith('/repos/example/project/actions/'));
    if (status !== 200) return Response.json({ message: 'private response must not leak' }, { status });
    const page = Number(u.searchParams.get('page'));
    if (u.pathname.endsWith('/runs')) {
      assert.equal(u.searchParams.get('head_sha'), sha);
      return Response.json({ workflow_runs: runPages?.[page] ?? runs });
    }
    assert.match(u.pathname, /\/attempts\/2\/jobs$/);
    return Response.json({ jobs: jobPages?.[page] ?? jobs });
  };
  return { calls, verify: () => verifyPublishedSource({ repository: 'example/project', sha, token: 'synthetic-token', fetchImpl }) };
}

test('release accepts a successful deploy job for the exact source and run attempt', async () => {
  assert.equal(await fixture().verify(), goodRun.id);
  assert.equal(await fixture({ runs: [{ ...goodRun, event: 'workflow_dispatch', head_branch: 'main' }] }).verify(), goodRun.id);
});

test('validation-only, PR, other-branch, wrong-source and failed runs never prove deployment', async () => {
  for (const override of [
    { event: 'push', head_branch: 'main' }, { event: 'pull_request' },
    { event: 'workflow_dispatch', head_branch: 'feature/change' },
    { head_sha: otherSha }, { conclusion: 'failure' }, { status: 'in_progress' },
  ]) {
    const f = fixture({ runs: [{ ...goodRun, ...override }] });
    await assert.rejects(f.verify(), /SUCCESSFUL_PRODUCTION_DEPLOYMENT_REQUIRED/);
    assert.equal(f.calls.length, 1, 'ineligible runs must not request job evidence');
  }
});

test('skipped, cancelled, failed, pending and missing deploy jobs fail closed', async () => {
  for (const jobs of [[], [{ name: 'validate', status: 'completed', conclusion: 'success' }],
    ...['skipped', 'cancelled', 'failure'].map(conclusion => [{ ...deployJob, conclusion }]),
    [{ ...deployJob, status: 'in_progress' }]]) {
    await assert.rejects(fixture({ jobs }).verify(), /SUCCESSFUL_PRODUCTION_DEPLOYMENT_REQUIRED/);
  }
});

test('run and job pagination retain evidence beyond the first page', async () => {
  const f = fixture({
    runPages: { 1: Array.from({ length: 100 }, (_, i) => ({ ...goodRun, id: i + 1, conclusion: 'failure' })), 2: [goodRun] },
    jobPages: { 1: Array.from({ length: 100 }, () => ({ name: 'validate', status: 'completed', conclusion: 'success' })), 2: [deployJob] },
  });
  assert.equal(await f.verify(), goodRun.id);
  assert.equal(f.calls.length, 4);
});

test('API denial does not expose provider bodies or bypass the release gate', async () => {
  await assert.rejects(fixture({ status: 403 }).verify(), { message: 'DEPLOYMENT_EVIDENCE_HTTP_403' });
  for (const repository of ['../other', 'example/..', 'example/project/extra'])
    await assert.rejects(verifyPublishedSource({ repository, sha, token: 'synthetic' }), /INPUT_REQUIRED/);
});

test('workflow wiring keeps deployment explicit, serialized, guarded and documented', () => {
  const workflow = fs.readFileSync(path.resolve(root, '../../.github/workflows/cyaccountingweb-deploy.yml'), 'utf8');
  const release = fs.readFileSync(path.resolve(root, '../../.github/workflows/cyaccountingweb-release.yml'), 'utf8');
  const push = workflow.split('  push:\n')[1].split('\npermissions:')[0];
  assert.match(push, new RegExp(`branches:\\s*\\n\\s*- ${DEPLOYMENT_BRANCH}`));
  assert.doesNotMatch(push, /- main\b|paths:/, 'the explicit publication signal must not be suppressed by path filters');
  assert.match(workflow, /github\.event_name == 'workflow_dispatch' && github\.ref == 'refs\/heads\/main'/);
  const deploy = workflow.split('\n  deploy:\n')[1];
  assert.match(deploy, /concurrency:\s*\n\s*group: cyaccountingweb-production\s*\n\s*cancel-in-progress: false/);
  assert.match(deploy, /environment: development/, 'the deployment keeps its existing settings scope');
  const guard = deploy.indexOf('node scripts/verify-deployment.mjs source');
  assert.ok(guard > 0 && guard < deploy.indexOf('node scripts/provision-recovery-d1.mjs'));
  assert.match(deploy, /resolve-cyid-production-runtime\.mjs --require-account-portal/);
  assert.match(deploy, /Verify production secure login entry/);
  assert.match(deploy, /Verify production semantic frontend assets/);
  assert.match(workflow, /recovery_workspace:[\s\S]*?default: disabled[\s\S]*?- disabled[\s\S]*?- prepare/);
  assert.match(deploy, /RECOVERY_D1_MODE:.*github\.event_name == 'workflow_dispatch'.*inputs\.recovery_workspace.*'disabled'/);
  assert.match(deploy, /name: Apply D1 migrations\s*\n\s*if: inputs\.recovery_workspace != 'prepare'/, 'emergency preparation does not require the failed primary database to migrate');
  assert.match(release, /node apps\/CYAccountingWeb\/scripts\/verify-deployment\.mjs release/);
  assert.doesNotMatch(release, /event=push&branch=main/);
  assert.match(workflow, /node tests\/deployment-flow\.mjs/);
  const readme = fs.readFileSync(path.join(root, 'README.md'), 'utf8');
  assert.ok(readme.includes(`git push origin refs/remotes/origin/main:refs/heads/${DEPLOYMENT_BRANCH}`));
  assert.ok(readme.includes('gh workflow run cyaccountingweb-deploy.yml --ref main'));
});
