import initSqlJs from 'sql.js/dist/sql-wasm-browser.js';
import wasmModule from 'sql.js/dist/sql-wasm-browser.wasm';
import initial from '../migrations/0001_initial.sql';
import sessions from '../migrations/0002_web_sessions.sql';
import runs from '../migrations/0003_google_drive_backup.sql';
import catalog from '../migrations/0004_tiered_backup_catalog.sql';
import archive from '../migrations/0005_account_archival.sql';
import opening from '../migrations/0006_opening_balance_overrides.sql';
import colors from '../migrations/0007_account_color_slots.sql';
import imports from '../migrations/0008_excel_import_runs.sql';
import recovery from '../migrations/0010_backup_recovery.sql';
import phaseD from '../migrations/0009_backup_phase_d.sql';
import health from '../migrations/0011_backup_health_retire_sessions.sql';
import readIndexes from '../migrations/0012_transaction_read_indexes.sql';
import openingSnapshots from '../migrations/0013_opening_balance_snapshots.sql';
import markers from '../migrations/0014_transaction_markers.sql';
import { verifyRestoreIntoEmptySqlite } from './backup-restore.js';

const schemas = [initial, sessions, runs, catalog, archive, opening, colors, imports, phaseD, recovery, health, readIndexes, openingSnapshots, markers];
export async function verifyRestoreInMemory(backup) {
  // Each verification owns a new SQLite database in the pinned WASM engine.
  // It has no D1 binding and closes the database on success or failure.
  const SQL = await initSqlJs({
    instantiateWasm(imports, ready) {
      const instance = new WebAssembly.Instance(wasmModule, imports);
      ready(instance, wasmModule);
      return instance.exports;
    },
    print() {}, printErr() {}
  });
  const sandbox = new SQL.Database();
  try { return await verifyRestoreIntoEmptySqlite(sandbox, schemas, backup); }
  finally { sandbox.close(); }
}
