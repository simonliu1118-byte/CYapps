import assert from 'node:assert/strict';
import fs from 'node:fs';
import test from 'node:test';

const workflow = fs.readFileSync(new URL('../../../.github/workflows/cypricedesk-deploy.yml', import.meta.url), 'utf8').replaceAll('\r\n', '\n');

test('deploy workflow resolves identifiers from Cloudflare and never reads them from GitHub variables', () => {
  assert.doesNotMatch(workflow, /vars\.CF_/);
  assert.doesNotMatch(workflow, /Mask deployment resource identifiers/);
  assert.match(workflow, /CF_CYPRICEDESK_CUSTOM_DOMAIN: price\.chihyuancm\.com/);
  const resolve = workflow.indexOf('node .github/scripts/resolve-deploy-runtime.mjs --app cypricedesk');
  assert.ok(resolve > 0);
  assert.ok(resolve < workflow.indexOf('node scripts/render-wrangler.mjs'));
  assert.ok(resolve < workflow.indexOf('wrangler d1 migrations apply'));
});

test('the only GitHub-held deployment inputs are secrets', () => {
  const refs = [...workflow.matchAll(/\$\{\{\s*(secrets|vars)\.([A-Z0-9_]+)/g)].map(m => `${m[1]}.${m[2]}`).sort();
  assert.deepEqual([...new Set(refs)], ['secrets.CLOUDFLARE_ACCOUNT_ID', 'secrets.CLOUDFLARE_API_TOKEN', 'secrets.CYPD_FETCHER_SECRET', 'secrets.GROK_API_TOKEN', 'secrets.GROK_WEBHOOK_TOKEN', 'secrets.GROK_WEBHOOK_URL']);
});

test('release-branch pushes and main dispatch use the same verified deployment', () => {
  assert.match(workflow, /\non:\n  workflow_dispatch:\n  push:\n    branches: \[deploy\/cypricedesk-production\]\n\npermissions:/);
  assert.match(workflow, /github\.event_name == 'workflow_dispatch' && github\.ref == 'refs\/heads\/main'/);
  assert.match(workflow, /github\.event_name == 'push' && github\.ref == 'refs\/heads\/deploy\/cypricedesk-production'/);
  assert.match(workflow, /git ls-remote --exit-code origin refs\/heads\/main/);
  assert.match(workflow, /\[ "\$\(git rev-parse HEAD\)" != "\$GITHUB_SHA" \] \|\| \[ "\$remote" != "\$GITHUB_SHA" \]/);
  assert.ok(workflow.indexOf('git ls-remote') < workflow.indexOf('wrangler d1 migrations apply'));
  assert.ok(workflow.indexOf('Write public release metadata') < workflow.indexOf('wrangler deploy --config'));
  assert.ok(workflow.indexOf('Verify live PriceDesk version') > workflow.indexOf('wrangler deploy --config'));
  assert.match(workflow, /cmp -s public\/release\.json "\$release_file"/);
});
