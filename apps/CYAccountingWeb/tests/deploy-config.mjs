import assert from 'node:assert/strict';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { renderWrangler } from '../scripts/render-wrangler.mjs';

const HERE = path.dirname(fileURLToPath(import.meta.url));
const PROJECT_ROOT = path.resolve(HERE, '..');
const TEMPLATE = path.join(PROJECT_ROOT, 'wrangler.template.jsonc');
const tempDir = fs.mkdtempSync(path.join(os.tmpdir(), 'cyaccounting-wrangler-'));
const output = path.join(tempDir, 'wrangler.ci.generated.jsonc');

try {
  const env = {
    CF_WORKER_NAME: 'cyaccounting-web-ci',
    CF_D1_DATABASE_NAME: 'cyaccounting-web-ci-db',
    CF_D1_DATABASE_ID: '11111111-1111-4111-8111-111111111111',
    CF_IDENTITY_SERVICE: 'cyidentity-ci-placeholder',
    CF_R2_BACKUP_BUCKET: 'cyaccounting-backup-ci',
    CF_BACKUP_TOPOLOGY: 'legacy_gcs'
  };
  renderWrangler({ env, templatePath: TEMPLATE, outputPath: output });
  const renderedText = fs.readFileSync(output, 'utf8');
  const rendered = JSON.parse(renderedText);
  assert.equal(rendered.name, env.CF_WORKER_NAME);
  assert.equal(rendered.main, 'src/app-v18.js');
  assert.equal(rendered.d1_databases[0].binding, 'DB');
  assert.equal(rendered.d1_databases[0].database_name, env.CF_D1_DATABASE_NAME);
  assert.equal(rendered.d1_databases[0].database_id, env.CF_D1_DATABASE_ID);
  assert.equal(rendered.services[0].binding, 'IDENTITY');
  assert.equal(rendered.services[0].service, env.CF_IDENTITY_SERVICE);
  assert.equal(rendered.r2_buckets[0].binding, 'BACKUP_R2');
  assert.equal(rendered.r2_buckets[0].bucket_name, env.CF_R2_BACKUP_BUCKET);
  assert.equal(rendered.vars.BACKUP_TOPOLOGY, 'legacy_gcs');
  assert.equal(rendered.triggers.crons[0], '30 19 * * *');
  assert.equal(/__CF_[A-Z0-9_]+__/.test(renderedText), false);

  assert.throws(() => renderWrangler({ env: { ...env, CF_D1_DATABASE_ID: '' }, templatePath: TEMPLATE, outputPath: output }), /Missing required deployment variable/);
  assert.throws(() => renderWrangler({ env: { ...env, CF_D1_DATABASE_ID: 'not-a-uuid' }, templatePath: TEMPLATE, outputPath: output }), /valid UUID/);
  assert.throws(() => renderWrangler({ env: { ...env, CF_R2_BACKUP_BUCKET: '' }, templatePath: TEMPLATE, outputPath: output }), /Missing required deployment variable/);
  assert.throws(() => renderWrangler({ env: { ...env, CF_BACKUP_TOPOLOGY: 'r2_only' }, templatePath: TEMPLATE, outputPath: output }), /legacy_gcs or parallel_dual_provider/);

  const template = fs.readFileSync(TEMPLATE, 'utf8');
  assert.match(template, /__CF_D1_DATABASE_ID__/);
  assert.match(template, /__CF_IDENTITY_SERVICE__/);
  assert.match(template, /__CF_R2_BACKUP_BUCKET__/);
  assert.match(template, /__CF_BACKUP_TOPOLOGY__/);
  console.log('Deployment config tests passed.');
} finally {
  fs.rmSync(tempDir, { recursive: true, force: true });
}
