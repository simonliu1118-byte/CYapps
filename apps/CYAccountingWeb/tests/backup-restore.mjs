import assert from 'node:assert/strict';
import initSqlJs from 'sql.js';
import { accountingFixture, schemas } from './support/backup-fixtures.mjs';
import { buildBackupSet, validateBackupSetBytes } from '../src/gcs-backup.js';
import { verifyRestoreIntoEmptySqlite } from '../src/backup-restore.js';

const SQL = await initSqlJs();
import { verify } from './support/restore-fixture.mjs';
const source = accountingFixture();
const unchanged = JSON.stringify(source.db.prepare('SELECT * FROM transactions ORDER BY id').all());
const backup = await buildBackupSet(source);
const validated = await validateBackupSetBytes(backup.manifestBytes, backup.dataBytes, backup.backupId);
const result = await verify(validated);
assert.equal(result.fullRowRoundtrip, true); assert.equal(result.integrityCheck, true); assert.equal(result.foreignKeyCheck, true);
assert.equal(result.counts.transactions, 3); assert.equal(result.counts.accounts, 2);
assert.equal(result.counts.openingBalanceAudit, 1); assert.equal(result.counts.excelImportRuns, 1);
assert.equal(result.liveAccountingModified, false);
assert.equal(JSON.stringify(source.db.prepare('SELECT * FROM transactions ORDER BY id').all()), unchanged);
assert.ok(validated.data.transactions[0].summary.includes('DROP TABLE'), 'SQL-like values roundtrip through bound parameters');
const schema8 = structuredClone(validated); schema8.manifest.schemaVersion = 8;
assert.equal((await verify(schema8)).sourceSchemaVersion, 8, 'current pre-rollout backups restore to the new schema');
for (const version of [6, 7, 10]) {
  const bad = structuredClone(validated); bad.manifest.schemaVersion = version;
  await assert.rejects(verify(bad), error => error.code === 'RESTORE_SCHEMA_UNSUPPORTED');
}
const orphan = structuredClone(validated); orphan.data.categories[0].groupId = 999;
await assert.rejects(verify(orphan), error => error.code === 'RESTORE_CONSTRAINT_FAILED');
const omitted = structuredClone(validated); delete omitted.data.appSettings;
await assert.rejects(verify(omitted), error => error.code === 'RESTORE_DATA_SHAPE');
const extra = structuredClone(validated); extra.data.accounts[0].unexpectedField = 'TEST';
await assert.rejects(verify(extra), error => error.code === 'RESTORE_ROUNDTRIP_MISMATCH');
const populated = new SQL.Database(); populated.run('CREATE TABLE existing(id)');
await assert.rejects(verifyRestoreIntoEmptySqlite(populated, schemas, validated), error => error.code === 'SANDBOX_NOT_EMPTY');
populated.close(); source.close();
console.log('Real SQLite isolated restore: all rows, duplicate transactions, archived accounts, audits, settings, schema bounds, constraints and parameter safety passed.');
