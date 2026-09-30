import assert from 'node:assert/strict';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { renderWrangler } from '../scripts/render-wrangler.mjs';

const HERE = path.dirname(fileURLToPath(import.meta.url));
const PROJECT_ROOT = path.resolve(HERE, '..');
const TEMPLATE = path.join(PROJECT_ROOT, 'wrangler.template.jsonc');
const CYID_DEV_TEMPLATE = path.join(PROJECT_ROOT, 'wrangler.cyid-development.template.jsonc');
const tempDir = fs.mkdtempSync(path.join(os.tmpdir(), 'cyaccounting-wrangler-'));
const output = path.join(tempDir, 'wrangler.ci.generated.jsonc');
const devOutput = path.join(tempDir, 'wrangler.cyid-development.ci.jsonc');

try {
  const env = {
    CF_WORKER_NAME: 'cyaccounting-web-ci',
    CF_D1_DATABASE_NAME: 'cyaccounting-web-ci-db',
    CF_D1_DATABASE_ID: '11111111-1111-4111-8111-111111111111',
    CF_IDENTITY_SERVICE: 'cyidentity-ci-placeholder',
    CF_CYID_APPLICATION_ID: 'CYACC_CI',
    CF_CYID_WORKSPACE_ID: 'workspace-ci-placeholder',
    CF_R2_BACKUP_BUCKET: 'cyaccounting-backup-ci',
    CF_BACKUP_TOPOLOGY: 'legacy_gcs',
    CF_CYACCOUNTINGWEB_CUSTOM_DOMAIN: 'acc.example.com'
  };
  renderWrangler({ env, templatePath: TEMPLATE, outputPath: output });
  const renderedText = fs.readFileSync(output, 'utf8');
  const rendered = JSON.parse(renderedText);
  assert.equal(rendered.name, env.CF_WORKER_NAME);
  assert.equal(rendered.main, 'src/app-v19.js');
  assert.equal(rendered.workers_dev, true);
  assert.equal(rendered.routes[0].pattern, env.CF_CYACCOUNTINGWEB_CUSTOM_DOMAIN);
  assert.equal(rendered.routes[0].custom_domain, true);
  assert.equal(rendered.d1_databases[0].binding, 'DB');
  assert.equal(rendered.d1_databases[0].database_name, env.CF_D1_DATABASE_NAME);
  assert.equal(rendered.d1_databases[0].database_id, env.CF_D1_DATABASE_ID);
  assert.equal(rendered.services[0].binding, 'IDENTITY');
  assert.equal(rendered.services[0].service, env.CF_IDENTITY_SERVICE);
  assert.equal(rendered.r2_buckets[0].binding, 'BACKUP_R2');
  assert.equal(rendered.r2_buckets[0].bucket_name, env.CF_R2_BACKUP_BUCKET);
  assert.equal(rendered.vars.BACKUP_TOPOLOGY, 'legacy_gcs');
  assert.equal(rendered.vars.CYID_APPLICATION_ID, env.CF_CYID_APPLICATION_ID);
  assert.equal(rendered.vars.CYID_WORKSPACE_ID, env.CF_CYID_WORKSPACE_ID);
  assert.deepEqual(rendered.assets.run_worker_first, ['/api/*', '/', '/index.html', '/login', '/login.html']);
  assert.equal(rendered.triggers.crons[0], '30 19 * * *');
  assert.equal(/__CF_[A-Z0-9_]+__/.test(renderedText), false);

  assert.throws(() => renderWrangler({ env: { ...env, CF_D1_DATABASE_ID: '' }, templatePath: TEMPLATE, outputPath: output }), /Missing required deployment variable/);
  assert.throws(() => renderWrangler({ env: { ...env, CF_D1_DATABASE_ID: 'not-a-uuid' }, templatePath: TEMPLATE, outputPath: output }), /valid UUID/);
  assert.throws(() => renderWrangler({ env: { ...env, CF_CYID_APPLICATION_ID: '' }, templatePath: TEMPLATE, outputPath: output }), /Missing required deployment variable/);
  assert.throws(() => renderWrangler({ env: { ...env, CF_CYID_APPLICATION_ID: 'bad app' }, templatePath: TEMPLATE, outputPath: output }), /CF_CYID_APPLICATION_ID has an invalid format/);
  assert.throws(() => renderWrangler({ env: { ...env, CF_CYID_WORKSPACE_ID: '' }, templatePath: TEMPLATE, outputPath: output }), /Missing required deployment variable/);
  assert.throws(() => renderWrangler({ env: { ...env, CF_R2_BACKUP_BUCKET: '' }, templatePath: TEMPLATE, outputPath: output }), /Missing required deployment variable/);
  assert.throws(() => renderWrangler({ env: { ...env, CF_BACKUP_TOPOLOGY: 'r2_only' }, templatePath: TEMPLATE, outputPath: output }), /legacy_gcs or parallel_dual_provider/);
  assert.throws(() => renderWrangler({ env: { ...env, CF_CYACCOUNTINGWEB_CUSTOM_DOMAIN: '' }, templatePath: TEMPLATE, outputPath: output }), /Missing required deployment variable/);
  assert.throws(() => renderWrangler({ env: { ...env, CF_CYACCOUNTINGWEB_CUSTOM_DOMAIN: 'https://acc.example.com' }, templatePath: TEMPLATE, outputPath: output }), /valid hostname/);

  const devEnv = {
    CF_WORKER_NAME: 'cyaccounting-cyid-preview-ci',
    CF_D1_DATABASE_NAME: 'cyaccounting-cyid-preview-ci-db',
    CF_D1_DATABASE_ID: '22222222-2222-4222-8222-222222222222',
    CF_IDENTITY_SERVICE: 'cyidentity-development-placeholder',
    CF_CYID_APPLICATION_ID: 'CYACC_DEV',
    CF_CYID_WORKSPACE_ID: 'workspace-development-placeholder'
  };
  renderWrangler({ env: devEnv, templatePath: CYID_DEV_TEMPLATE, outputPath: devOutput });
  const devRenderedText = fs.readFileSync(devOutput, 'utf8');
  const devRendered = JSON.parse(devRenderedText);
  assert.equal(devRendered.name, devEnv.CF_WORKER_NAME);
  assert.equal(devRendered.workers_dev, true);
  assert.equal(devRendered.d1_databases[0].database_name, devEnv.CF_D1_DATABASE_NAME);
  assert.equal(devRendered.d1_databases[0].database_id, devEnv.CF_D1_DATABASE_ID);
  assert.equal(devRendered.services[0].service, devEnv.CF_IDENTITY_SERVICE);
  assert.equal(devRendered.vars.CYID_APPLICATION_ID, devEnv.CF_CYID_APPLICATION_ID);
  assert.equal(devRendered.vars.CYID_WORKSPACE_ID, devEnv.CF_CYID_WORKSPACE_ID);
  assert.equal('routes' in devRendered, false);
  assert.equal('r2_buckets' in devRendered, false);
  assert.equal('triggers' in devRendered, false);
  assert.equal('BACKUP_TOPOLOGY' in devRendered.vars, false);
  assert.equal(/__CF_[A-Z0-9_]+__/.test(devRenderedText), false);
  assert.throws(() => renderWrangler({
    env: { ...devEnv, CF_CYID_WORKSPACE_ID: '' },
    templatePath: CYID_DEV_TEMPLATE,
    outputPath: devOutput
  }), /Missing required deployment variable/);

  const template = fs.readFileSync(TEMPLATE, 'utf8');
  const devTemplate = fs.readFileSync(CYID_DEV_TEMPLATE, 'utf8');
  assert.match(template, /"workers_dev"\s*:\s*true/);
  assert.match(template, /__CF_D1_DATABASE_ID__/);
  assert.match(template, /__CF_IDENTITY_SERVICE__/);
  assert.match(template, /__CF_CYID_APPLICATION_ID__/);
  assert.match(template, /__CF_CYID_WORKSPACE_ID__/);
  assert.match(template, /__CF_R2_BACKUP_BUCKET__/);
  assert.match(template, /__CF_BACKUP_TOPOLOGY__/);
  assert.match(template, /__CF_CUSTOM_DOMAIN__/);
  assert.match(devTemplate, /__CF_CYID_APPLICATION_ID__/);
  assert.match(devTemplate, /__CF_CYID_WORKSPACE_ID__/);
  assert.doesNotMatch(devTemplate, /__CF_CUSTOM_DOMAIN__/);
  assert.doesNotMatch(devTemplate, /__CF_R2_BACKUP_BUCKET__/);
  console.log('Deployment config tests passed.');
} finally {
  fs.rmSync(tempDir, { recursive: true, force: true });
}
