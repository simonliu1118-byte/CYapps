import assert from 'node:assert/strict';
import { unstable_dev } from 'wrangler';
import { accountingFixture } from './support/backup-fixtures.mjs';
import { buildBackupSet, validateBackupSetBytes } from '../src/gcs-backup.js';

const db = accountingFixture();
db.db.exec('BEGIN');
const insert = db.db.prepare("INSERT INTO transactions(tx_date,account_name,kind,category_name,summary,amount,created_at,updated_at) VALUES('2026-01-04','現金','income','一般收入',?,1,'2026-01-04','2026-01-04')");
for (let index=0;index<9000;index++) insert.run('TEST_LARGE_'+index+' 測試文字'.repeat(12));
db.db.exec('COMMIT');
const backup = await buildBackupSet(db);
const validated = await validateBackupSetBytes(backup.manifestBytes, backup.dataBytes, backup.backupId);
db.close();
const worker = await unstable_dev('tests/fixtures/backup-restore-worker.js', {
  config: 'tests/fixtures/backup-restore-worker.jsonc', local: true, persist: false, ip: '127.0.0.1', port: 0, logLevel: 'error',
  experimental: { disableExperimentalWarning: true, disableDevRegistry: true, watch: false }
});
try {
  const cases=[validated,validated];
  for(const schemaVersion of [6,7,11,12]) {
    const old=structuredClone(validated);old.manifest.schemaVersion=schemaVersion;
    if(schemaVersion<8){delete old.manifest.recoveryContext;delete old.data.excelImportRuns;}
    if(schemaVersion===6)old.data.accounts.forEach(row=>{delete row.colorSlot;});
    cases.push(old);
  }
  for (const input of cases) {
    const response = await worker.fetch('/', { method: 'POST', headers: { 'content-type': 'application/json' }, body: JSON.stringify(input) });
    const text = await response.text();
    assert.equal(response.status, 200, text);
    const report = JSON.parse(text);
    assert.equal(report.fullRowRoundtrip, true); assert.equal(report.isolated, true); assert.equal(report.liveAccountingModified, false);
    assert.equal(report.counts.transactions, 9003, 'repeated requests each restore a fresh SQLite database');
    assert.equal(report.sourceSchemaVersion,input.manifest.schemaVersion);
    if(input.manifest.schemaVersion<8)assert.equal(report.normalization.sourceSchemaVersion,input.manifest.schemaVersion);
  }
  const paired = await worker.fetch('/', {method:'POST',headers:{'content-type':'application/json'},body:JSON.stringify([validated,validated])});
  assert.equal(paired.status,200,await paired.clone().text());
  const reports=await paired.json();
  assert.equal(reports.length,2);
  assert.ok(reports.every(report=>report.fullRowRoundtrip&&report.counts.transactions===9003), 'two large provider checks in one invocation keep separate empty databases');
} finally { await worker.stop(); }
console.log('Actual Workers/workerd runtime restores and validates a fresh isolated SQLite database using static WASM.');
