import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { DatabaseSync } from 'node:sqlite';
import { schemas } from './support/backup-fixtures.mjs';

const legacy=new DatabaseSync(':memory:');
try {
  const healthIndex=schemas.findIndex(migration=>migration.includes('CREATE TABLE backup_health_state'));
  for(const migration of schemas.slice(0,healthIndex))legacy.exec(migration);
  legacy.exec("INSERT INTO transactions(tx_date,account_name,kind,category_name,summary,amount,created_at,updated_at) VALUES('2026-01-01','現金','income','一般收入','TEST_PRESERVE',99,'2026-01-01','2026-01-01')");
  const snapshot=()=>JSON.stringify(['accounts','categories','category_groups','transactions','opening_balance_overrides','opening_balance_audit','app_settings','excel_import_runs'].map(table=>legacy.prepare('SELECT * FROM '+table).all()));
  const before=snapshot();
  assert.ok(legacy.prepare("SELECT name FROM sqlite_master WHERE name='web_sessions'").get());
  legacy.exec(schemas[healthIndex]);
  assert.equal(legacy.prepare("SELECT name FROM sqlite_master WHERE name='web_sessions'").get(),undefined);
  assert.equal(snapshot(),before,'forward migration preserves every accounting/settings/audit row');
}finally{legacy.close();}

console.log('Backup health schema: retiring legacy sessions preserves all accounting/settings/audit rows.');
