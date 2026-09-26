import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import vm from 'node:vm';
import { fileURLToPath } from 'node:url';

const HERE = path.dirname(fileURLToPath(import.meta.url));
const source = fs.readFileSync(path.resolve(HERE, '../public/v016.js'), 'utf8');
const context = vm.createContext({
  window: { addEventListener() {} },
  document: { querySelector() { return null; } },
  console,
  Intl,
  Date,
  Number,
  String,
  Array,
  Boolean,
  Object,
  confirm() { return false; }
});
vm.runInContext(source, context, { filename: 'v016.js' });

const tiered = context.backupUiModelV18({
  ok: true,
  provider: 'tiered',
  topology: 'parallel_dual_provider',
  configured: true,
  providers: {
    cloudflare_r2: { configured: true, retentionDays: 30, role: 'operational' },
    google_cloud_storage: { configured: true, retentionDays: 14, role: 'cross_cloud_validation' }
  },
  schedule: { localTime: '每日 03:30（台灣時間）' },
  logicalBackups: [{
    backupId: '20260926T165125Z',
    status: 'success',
    packageSha256: 'abc123',
    copies: [
      { provider: 'cloudflare_r2', status: 'success' },
      { provider: 'google_cloud_storage', status: 'success' }
    ]
  }]
});
assert.equal(tiered.tiered, true);
assert.equal(tiered.topology, 'parallel_dual_provider');
assert.equal(tiered.r2.retentionDays, 30);
assert.equal(tiered.gcs.retentionDays, 14);
assert.equal(tiered.latest.backupId, '20260926T165125Z');
assert.match(tiered.heading, /R2 \+ GCS/);

const legacy = context.backupUiModelV18({
  configured: true,
  retentionDays: 14,
  latestSuccess: { fileName: 'legacy' },
  recentRuns: [{ fileName: 'legacy' }]
});
assert.equal(legacy.tiered, false);
assert.equal(legacy.topology, 'legacy_gcs');
assert.equal(legacy.latest.fileName, 'legacy');
assert.equal(legacy.recentRuns.length, 1);
assert.match(legacy.heading, /Google Cloud Storage/);

assert.match(source, /logical backup/);
assert.match(source, /cloudflare_r2/);
assert.match(source, /google_cloud_storage/);
console.log('Tiered backup UI tests passed.');
