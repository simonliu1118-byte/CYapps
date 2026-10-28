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

const phaseD = context.backupUiModel({provider:'tiered',topology:'parallel_dual_provider',configured:true,policy:{phase:'phase_d'},providers:{cloudflare_r2:{retentionDays:30},google_cloud_storage:{retentionDays:182,role:'cross_cloud_long_term'}},schedule:{localTime:'R2 每日；GCS 週三、週日 03:30（台灣時間）'},logicalBackups:[]});
assert.match(phaseD.retention,/GCS 182 天（Phase D）/);
assert.match(phaseD.schedule,/週三、週日/);
const nodes = new Map([['#backupHistoryRows',{}],['#backupRestoreTools',{}],['#backupRestoreStatus',{}],['#backupVerifyRestore',{}],['#backupActivatePhaseD',{}]]);
context.document.querySelector = selector => nodes.get(selector) || null;
context.renderTieredBackupHistory([{gcsExpected:false,copies:[{provider:'cloudflare_r2',status:'success'}],status:'success'}]);
assert.doesNotMatch(nodes.get('#backupHistoryRows').innerHTML,/本日未排程/);
assert.match(nodes.get('#backupHistoryRows').innerHTML, /<td class="backup-copy-cell"><\/td>/, 'unscheduled GCS stays an empty cell');
context.renderTieredBackupHistory([{backupId:'TEST_RETRY',createdAt:'2026-10-20T00:00:00Z',trigger:'scheduled',status:'success',copies:[]}], [
  {slot:'retry',backupId:'TEST_RETRY',createdAt:'2026-10-20T00:00:00Z',status:'success'},
  {slot:'primary',backupId:'',createdAt:'2026-10-19T19:30:00Z',status:'failed',errorCode:'D1_DAILY_LIMIT_EXCEEDED'}
]);
const retryHistory = nodes.get('#backupHistoryRows').innerHTML;
assert.match(retryHistory, /補跑/);
assert.match(retryHistory, /D1 每日額度已用完/);
assert.equal((retryHistory.match(/<tr>/g) || []).length, 2, 'the failure remains beside the successful retry, without duplicating its logical backup');
context.renderTieredBackupHistory([], [{slot:'primary',createdAt:'2026-10-19T19:30:00Z',status:'failed',errorCode:'<script>"'}]);
assert.doesNotMatch(nodes.get('#backupHistoryRows').innerHTML, /<script>/);
assert.doesNotMatch(nodes.get('#backupHistoryRows').innerHTML, /0 筆/, 'a failed pre-export attempt is not presented as an empty ledger');
assert.doesNotMatch(renderedHtml, /backupVerifyRestore|backupRestoreStatus|backupActivatePhaseD|檢查備份可還原性|試還原並核對/);
nodes.set('#backupRecoveryTools', {hidden:true});nodes.set('#backupRecoveryStatus',{});nodes.set('#backupRecoverySummary',{});nodes.set('#backupRecoveryPreview',{});
context.renderBackupRecoveryState({policy:{},recoveryWorkspace:{configured:false,ready:false},logicalBackups:[]});
assert.match(nodes.get('#backupRecoveryStatus').textContent, /需要時建立/);
assert.equal(nodes.get('#backupRecoveryPreview').disabled,true);
context.renderBackupRecoveryState({policy:{},recoveryWorkspace:{configured:true,ready:false},logicalBackups:[]});
assert.match(nodes.get('#backupRecoveryStatus').textContent, /已有資料.*不可覆寫/);
assert.match(renderedHtml,/立即備份/); assert.doesNotMatch(renderedHtml,/立即執行測試備份/);

// Only the launcher remains in settings; the single restore form is dialog-owned.
assert.ok(renderedHtml.indexOf('id="backupRunNow"') < renderedHtml.indexOf('id="backupMessage"'));
assert.equal((renderedHtml.match(/id="backupRunNow"/g)||[]).length,1);
assert.ok(renderedHtml.includes('id="backupOpenRecovery"'));
assert.ok(!renderedHtml.includes('id="backupRecoverySource"'));
assert.ok(renderedHtml.includes('backup-emergency-link'));
const confirmPanel={hidden:false}, reviewed={checked:true}, text={value:'重建獨立帳本'}, commit={disabled:false};
nodes.set('#backupRecoveryConfirm',confirmPanel);nodes.set('#backupRecoveryReviewed',reviewed);nodes.set('#backupRecoveryConfirmation',text);nodes.set('#backupRecoveryCommit',commit);
vm.runInContext('backupRecoveryPreview={id:"TEST_PREVIEW"}',context);
context.cancelBackupRecoveryPreview();
assert.equal(confirmPanel.hidden,true);assert.equal(reviewed.checked,false);assert.equal(text.value,'');assert.equal(commit.disabled,true);
vm.runInContext('backupRecoveryPreview={id:"TEST_PREVIEW"}',context);
reviewed.checked=true;text.value='wrong';context.syncBackupRecoveryConfirmation();assert.equal(commit.disabled,true);
text.value='重建獨立帳本';context.syncBackupRecoveryConfirmation();assert.equal(commit.disabled,false);
// A completed asynchronous preview cannot revive a closed/cancelled dialog.
const previewButton={disabled:false}, recoveryDialog={open:true};
nodes.set('#backupRecoveryPreview',previewButton);nodes.set('#backupRecoveryDialog',recoveryDialog);
for(const [id,value] of [['Source','TEST_BACKUP'],['Provider','google_cloud_storage'],['Reason','TEST_REASON']])nodes.set('#backupRecovery'+id,{value});
let resolvePreview;context.api=()=>new Promise(resolve=>{resolvePreview=resolve;});
context.jsonHeaders=()=>({'content-type':'application/json'});
const pendingPreview=context.previewBackupRecovery();
recoveryDialog.open=false;context.cancelBackupRecoveryPreview();
resolvePreview({preview:{id:'TEST_LATE',counts:{transactions:1}}});await pendingPreview;
assert.equal(confirmPanel.hidden,true);assert.equal(commit.disabled,true);
console.log('Backup launcher/dialog boundary, cancellation, typed confirmation and late-preview discard passed.');

// Healthy boots stay silent; failed health opens one accessible warning, without
// exposing operational identifiers or making any restore request.
let warningCreated=0,shown=0,closed=0;
const textNodes=new Map([['#backupHealthWarningText',{}],['.backup-health-delivery',{}]]);
const warning={open:false,setAttribute(){},querySelector(selector){return selector==='button'?{addEventListener(name,fn){this.close=fn;}}:textNodes.get(selector);},showModal(){shown++;this.open=true;},close(){closed++;this.open=false;}};
context.document.createElement=()=>{warningCreated++;return warning;};
context.document.body={append(node){nodes.set('#backupHealthWarning',node);}};
context.api=async()=>({health:{status:'healthy'}});
await context.showBackupHealthWarning();assert.equal(warningCreated,0);
context.api=async()=>({health:{status:'failed',issues:[{provider:'google_cloud_storage',code:'RESTORE_SIZE_OR_SOURCE'}],emailPending:true}});
await context.showBackupHealthWarning();assert.equal(warningCreated,1);assert.equal(shown,1);
assert.match(textNodes.get('#backupHealthWarningText').textContent,/GCS.*自動檢查未通過/);
assert.match(textNodes.get('.backup-health-delivery').textContent,/下次排程重試/);
await context.showBackupHealthWarning();assert.equal(warningCreated,1);assert.equal(shown,1);
console.log('Healthy boot silence and single accessible Super Admin incident warning passed.');
