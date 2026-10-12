import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import vm from 'node:vm';
import { fileURLToPath } from 'node:url';

const HERE = path.dirname(fileURLToPath(import.meta.url));
const source = fs.readFileSync(path.resolve(HERE, '../public/backup-ui.js'), 'utf8');
const source181 = source;
const css181 = fs.readFileSync(path.resolve(HERE, '../public/backup.css'), 'utf8');
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
vm.runInContext(source, context, { filename: 'backup-ui.js' });

const tiered = context.backupUiModel({
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

const acceptance = context.window.phaseCAcceptanceUiModel({
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

const manualOnly = context.window.phaseCAcceptanceUiModel({
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

const legacy = context.backupUiModel({
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
assert.equal(context.window.phaseCAcceptanceUiModel({ configured: true }).visible, false);

const renderedHtml = context.backupSettingsHtml();
assert.match(renderedHtml, /<th>時間<\/th><th>方式<\/th><th>備份 ID<\/th><th>R2<\/th><th>GCS<\/th><th>資料<\/th>/);
assert.doesNotMatch(renderedHtml, /<th>整體<\/th>/);
assert.doesNotMatch(renderedHtml, /資料筆數<\/th>/);
assert.doesNotMatch(renderedHtml, /<th class="num">大小<\/th>/);
assert.match(renderedHtml, /backupAcceptance/);

const mobileHtml = context.window.mobileBackupInfoHtml({
  ok: true,
  provider: 'tiered',
  topology: 'parallel_dual_provider',
  configured: true,
  providers: {
    cloudflare_r2: { configured: true, retentionDays: 30, role: 'operational' },
    google_cloud_storage: { configured: true, retentionDays: 14, role: 'cross_cloud_validation' }
  },
  schedule: { localTime: '每日 03:30（台灣時間）' },
  phaseCAcceptance: { requiredConsecutiveScheduled: 14, consecutiveScheduledSuccesses: 5, completed: false },
  logicalBackups: [{
    backupId: '20261005T193000Z',
    trigger: 'scheduled',
    status: 'success',
    createdAt: '2026-10-05T19:30:00Z',
    rowCount: 10,
    byteSize: 2048,
    packageSha256: 'c'.repeat(64),
    copies: [
      { provider: 'cloudflare_r2', status: 'success' },
      { provider: 'google_cloud_storage', status: 'success' }
    ]
  }]
});
assert.match(mobileHtml, /備份狀態/);
assert.match(mobileHtml, /Phase C 排程驗收/);
assert.match(mobileHtml, /Cloudflare R2/);
assert.doesNotMatch(mobileHtml, /<button|立即執行|復原操作/);
assert.match(source, /window\.cyOpenMobileBackupInfo = openMobileBackupInfo/);
assert.doesNotMatch(source, /僅供檢視|不提供備份或復原操作/, 'mobile backup dialog must not add redundant explanatory copy');
assert.doesNotMatch(source, /backupSettingsHtml\s*=\s*function|renderBackupStatus\s*=\s*function|renderTieredBackupHistory\s*=\s*function/, 'backup UI must not patch canonical functions after definition');

assert.match(source, /logical backup/);
assert.match(source, /cloudflare_r2/);
assert.match(source, /google_cloud_storage/);
assert.match(source181, /Phase C 排程驗收/);
assert.match(source181, /colspan=\"6\"/);
assert.match(css181, /overflow-x:\s*hidden/);
assert.match(css181, /\.backup-history-table\s*\{[\s\S]*?min-width:\s*850px;[\s\S]*?\}[\s\S]*?\.backup-history-table\s*\{[\s\S]*?min-width:\s*0;/);
console.log('Tiered backup UI, compact history, and Phase C acceptance progress tests passed.');
