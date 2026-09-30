import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const HERE = path.dirname(fileURLToPath(import.meta.url));
const ROOT = path.resolve(HERE, '..', '..', '..');
const projectRoot = path.join(ROOT, 'apps', 'CYAccountingWeb');

const workflow = fs.readFileSync(path.join(ROOT, '.github', 'workflows', 'cyaccountingweb-cyid-development.yml'), 'utf8');
const template = fs.readFileSync(path.join(projectRoot, 'wrangler.cyid-development.template.jsonc'), 'utf8');

assert.match(workflow, /deploy\/cyaccountingweb-cyid-development/);
assert.match(workflow, /environment:\s*cyaccountingweb-cyid-development/);
assert.match(workflow, /wrangler\.cyid-development\.template\.jsonc/);
assert.match(workflow, /smoke-cyid-development\.mjs/);
assert.match(workflow, /CYACC_CYID_DEV_SMOKE_EMPLOYEE_NO/);
assert.match(workflow, /CYACC_CYID_DEV_SMOKE_PASSWORD/);
assert.match(workflow, /https:\/\/cyacc-cyid-development-cyaccounting-web\.simonliu1118\.workers\.dev/);
assert.match(workflow, /CYACC_SMOKE_EXPECTED_ROLE:\s*USER/);
assert.doesNotMatch(workflow, /CF_CYACCOUNTINGWEB_CUSTOM_DOMAIN/);
assert.doesNotMatch(workflow, /CF_R2_BACKUP_BUCKET/);
assert.doesNotMatch(workflow, /CF_BACKUP_TOPOLOGY/);
assert.doesNotMatch(workflow, /CLOUDFLARE_API_TOKEN/);
assert.doesNotMatch(workflow, /CF_D1_DATABASE_ID/);
assert.doesNotMatch(workflow, /branches:\s*\n\s*-\s*main/);

const renderedTemplate = JSON.parse(template);
assert.equal(renderedTemplate.workers_dev, true);
assert.equal('routes' in renderedTemplate, false);
assert.equal('r2_buckets' in renderedTemplate, false);
assert.equal('triggers' in renderedTemplate, false);
assert.deepEqual(renderedTemplate.assets.run_worker_first, ['/api/*', '/', '/index.html', '/login', '/login.html']);

console.log('Isolated CYID development workflow checks passed.');
