import initSqlJs from 'sql.js';
import { schemas } from './backup-fixtures.mjs';
import { verifyRestoreIntoEmptySqlite } from '../../src/backup-restore.js';
const SQL = await initSqlJs();
export async function verify(backup) {
  const db = new SQL.Database();
  try { return await verifyRestoreIntoEmptySqlite(db, schemas, backup); }
  finally { db.close(); }
}
