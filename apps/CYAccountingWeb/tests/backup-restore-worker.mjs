import assert from 'node:assert/strict';
import { unstable_dev } from 'wrangler';
import { accountingFixture } from './support/backup-fixtures.mjs';
import { buildBackupSet, validateBackupSetBytes } from '../src/gcs-backup.js';

const db = accountingFixture();
const backup = await buildBackupSet(db);
const validated = await validateBackupSetBytes(backup.manifestBytes, backup.dataBytes, backup.backupId);
db.close();
const worker = await unstable_dev('tests/fixtures/backup-restore-worker.js', {
  config: 'tests/fixtures/backup-restore-worker.jsonc', local: true, persist: false, ip: '127.0.0.1', port: 0, logLevel: 'error',
  experimental: { disableExperimentalWarning: true, disableDevRegistry: true, watch: false }
});
try {
  for (let i = 0; i < 2; i++) {
    const response = await worker.fetch('/', { method: 'POST', headers: { 'content-type': 'application/json' }, body: JSON.stringify(validated) });
    const text = await response.text();
    assert.equal(response.status, 200, text);
    const report = JSON.parse(text);
    assert.equal(report.fullRowRoundtrip, true); assert.equal(report.isolated, true); assert.equal(report.liveAccountingModified, false);
    assert.equal(report.counts.transactions, 3, 'repeated requests each restore a fresh SQLite database');
  }
} finally { await worker.stop(); }
console.log('Actual Workers/workerd runtime restores and validates a fresh isolated SQLite database using static WASM.');
