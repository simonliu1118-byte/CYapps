import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import vm from 'node:vm';
import { fileURLToPath } from 'node:url';

const HERE = path.dirname(fileURLToPath(import.meta.url));
const source = fs.readFileSync(path.resolve(HERE, '../public/v016.js'), 'utf8');
const source181 = fs.readFileSync(path.resolve(HERE, '../public/v0181.js'), 'utf8');
const css181 = fs.readFileSync(path.resolve(HERE, '../public/v0181.css'), 'utf8');
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
  RegExp,
  confirm() { return false; }
});
vm.runInContext(source, context, { filename: 'v016.js' });
vm.runInContext(source181, context, { filename: 'v0181.js' });

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
    packageSha256: 'a'.repeat(64),
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

const acceptance = context.phaseCAcceptanceUiModelV181({
  provider: 'tiered',
  topology: 'parallel_dual_provider',
  phaseCAcceptance: {
    requiredConsecutiveScheduled: 14,
    consecutiveScheduledSuccesses: 3,
    remaining: 11,
    completed: false,
    latestScheduledAt: '2026-09-29T19:30:00Z',
    latestScheduledBackupId: '20260929T193000Z'
  }
});
assert.equal(acceptance.visible, true);
assert.equal(acceptance.count, 3);
assert.equal(acceptance.required, 14);
assert.equal(acceptance.remaining, 11);
assert.equal(acceptance.completed, false);

const manualOnly = context.phaseCAcceptanceUiModelV181({
  provider: 'tiered',
  topology: 'parallel_dual_provider',
  logicalBackups: [{
    trigger: 'manual',
    status: 'success',
    packageSha256: 'b'.repeat(64),
    copies: [
      { provider: 'cloudflare_r2', status: 'success' },
      { provider: 'google_cloud_storage', status: 'success' }
    ]
  }]
});
assert.equal(manualOnly.count, 0, 'manual runs must not count toward Phase C scheduled acceptance');

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
assert.equal(context.phaseCAcceptanceUiModelV181({ configured: true }).visible, false);

assert.match(source, /logical backup/);
assert.match(source, /cloudflare_r2/);
assert.match(source, /google_cloud_storage/);
assert.match(source181, /Phase C 排程驗收/);
assert.match(source181, /colspan=\"6\"/);
assert.doesNotMatch(source181, /資料筆數<\/th><th class=\"num\">大小/);
assert.match(css181, /overflow-x:\s*hidden/);
assert.match(css181, /min-width:\s*0/);
assert.doesNotMatch(css181, /min-width:\s*850px/);
console.log('Tiered backup UI, compact history, and Phase C acceptance progress tests passed.');
