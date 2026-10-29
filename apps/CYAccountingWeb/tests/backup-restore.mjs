import assert from 'node:assert/strict';
import initSqlJs from 'sql.js';
import { accountingFixture, schemas } from './support/backup-fixtures.mjs';
import { buildBackupSet, validateBackupSetBytes } from '../src/gcs-backup.js';
import { verifyRestoreIntoEmptySqlite, normalizeAccountingRestoreData } from '../src/backup-restore.js';

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
const schema11 = structuredClone(validated); schema11.manifest.schemaVersion = 11;
assert.equal((await verify(schema11)).targetSchemaVersion, 12, 'pre-index backups restore into schema12 without changing financial rows');
for (const version of [5, 13]) {
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

// Schema 6/7 normalization preserves every financial/audit value and never mutates source.
for (const schemaVersion of [6, 7]) {
  const old = structuredClone(validated);
  old.manifest.schemaVersion = schemaVersion;
  delete old.manifest.recoveryContext;
  delete old.data.excelImportRuns;
  if (schemaVersion === 6) for (const row of old.data.accounts) delete row.colorSlot;
  const before = JSON.stringify(old);
  const proof = await verify(old);
  assert.equal(proof.fullRowRoundtrip, true);assert.equal(proof.sourceSchemaVersion, schemaVersion);
  assert.equal(proof.counts.openingBalanceAudit,1);assert.equal(proof.counts.excelImportRuns,0);
  assert.equal(proof.normalization.accountColorSlotsAssigned,schemaVersion===6);
  assert.equal(JSON.stringify(old),before,'immutable historical source stays unchanged');
  const normalized=normalizeAccountingRestoreData(old).data;
  assert.deepEqual(normalized.transactions,old.data.transactions);assert.deepEqual(normalized.openingBalanceAudit,old.data.openingBalanceAudit);
  assert.deepEqual(normalized.appSettings,old.data.appSettings);
  if(schemaVersion===7)assert.deepEqual(normalized.accounts,old.data.accounts);
}
const ranked=structuredClone(validated);ranked.manifest.schemaVersion=6;ranked.data.excelImportRuns=[];
ranked.data.accounts.forEach(row=>{delete row.colorSlot;});
ranked.data.accounts[0].createdAt='2026-01-02';ranked.data.accounts[1].createdAt='2026-01-01';
assert.deepEqual(normalizeAccountingRestoreData(ranked).data.accounts.map(row=>row.colorSlot),[2,1],'colors follow migration order, not exporter/insertion order');
const contradictory=structuredClone(ranked);contradictory.data.excelImportRuns=[{id:'TEST_CONTRADICTORY'}];
assert.throws(()=>normalizeAccountingRestoreData(contradictory),e=>e.code==='RESTORE_LEGACY_IMPORT_AUDIT_CONFLICT');
const missingNewAudit=structuredClone(validated);delete missingNewAudit.data.excelImportRuns;
assert.throws(()=>normalizeAccountingRestoreData(missingNewAudit),e=>e.code==='RESTORE_DATA_SHAPE','new schemas cannot silently lose import audits');
console.log('Portable schema 6/7: deterministic canonical color migration, empty import audit initialization, exact financial/audit roundtrip and immutable source passed.');
