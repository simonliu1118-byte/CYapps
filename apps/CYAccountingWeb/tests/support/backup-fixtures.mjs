import fs from 'node:fs';
import { DatabaseSync } from 'node:sqlite';

const root = new URL('../../', import.meta.url);
export const schemas = fs.readdirSync(new URL('migrations/', root)).filter(name => name.endsWith('.sql')).sort().map(name => fs.readFileSync(new URL('migrations/' + name, root), 'utf8'));
export const session = { role: 'SUPER_ADMIN', employee_id: 'TEST_EMPLOYEE', employee_no: 'TEST', employee_name: '測試管理員' };
export class FixtureD1 {
  constructor() { this.db = new DatabaseSync(':memory:'); for (const sql of schemas) this.db.exec(sql); }
  prepare(sql) {
    const statement = { sql, args: [], bind(...args) { this.args = args; return this; } };
    statement.first = async () => this.db.prepare(sql).get(...statement.args) || null;
    statement.all = async () => ({ results: this.db.prepare(sql).all(...statement.args) });
    statement.run = async () => ({ success: true, meta: this.db.prepare(sql).run(...statement.args) });
    return statement;
  }
  async batch(statements) {
    this.db.exec('BEGIN');
    try { const results=[];for(const statement of statements)results.push(await statement.run());this.db.exec('COMMIT');return results; }
    catch(error) {this.db.exec('ROLLBACK');throw error;}
  }
  close() { this.db.close(); }
}
export function accountingFixture() {
  const db = new FixtureD1();
  db.db.exec(`
    INSERT INTO accounts(id,name,sort_order,is_default,color_slot,created_at,archived_at) VALUES(10,'TEST_ARCHIVED',1,0,2,'2026-01-01','2026-10-01');
    INSERT INTO transactions(tx_date,account_name,kind,category_name,summary,amount,created_at,updated_at) VALUES
      ('2026-01-02','現金','income','一般收入','TEST_INCOME',123,'2026-01-02','2026-01-02'),
      ('2026-01-03','TEST_ARCHIVED','expense','一般支出','TEST_DUPLICATE',17,'2026-01-03','2026-01-03'),
      ('2026-01-03','TEST_ARCHIVED','expense','一般支出','TEST_DUPLICATE',17,'2026-01-03','2026-01-03');
    INSERT INTO app_settings(key,value) VALUES('locked_through','2026-01'),('statistics_transfer_category_ids','[2]');
    INSERT INTO opening_balance_overrides VALUES('2026-01','現金',99,'TEST_REASON','2026-01-01','2026-01-01','TEST_ID','TEST','TEST_NAME','SUPER_ADMIN');
    INSERT INTO opening_balance_audit(month,account_name,action,previous_amount,new_amount,reason,actor_employee_id,actor_employee_no,actor_name,actor_role,created_at)
      VALUES('2026-01','現金','set',NULL,99,'TEST_REASON','TEST_ID','TEST','TEST_NAME','SUPER_ADMIN','2026-01-01');
    INSERT INTO excel_import_runs VALUES('TEST_IMPORT','2026-01-01','2026-02-01','TEST_IMPORT_REASON','TEST_SOURCE_SHA','TEST_BACKUP',0,3,'{"employeeNo":"TEST"}','2026-01-04');
  `);
  db.db.prepare('UPDATE transactions SET summary=? WHERE id=1').run("文字 <script> & '); DROP TABLE accounts; --");
  return db;
}
export function memoryProvider(kind) {
  const objects = new Map(), deleted = [];
  return { kind, objects, deleted,
    async putObject(key, bytes) { objects.set(key, new Uint8Array(bytes)); return { versionToken: 'TEST_VERSION' }; },
    async getObject(key) { if (!objects.has(key)) throw new Error('TEST_OBJECT_MISSING'); return objects.get(key); },
    async listObjects(prefix) { return [...objects.keys()].filter(key => key.startsWith(prefix)).map(key => ({ key, versionToken: 'TEST_VERSION', timeCreated: key.replace(/^CYAccountingWeb\/(\d{4})(\d{2})(\d{2})T(\d{2})(\d{2})(\d{2})Z\/.*$/, '$1-$2-$3T$4:$5:$6Z') })); },
    async deleteObject(key) { deleted.push(key); objects.delete(key); }
  };
}

export function memoryR2Bucket() {
  const objects = new Map(), calls = []; let sequence = 0;
  return { objects, calls,
    async put(key, value, options = {}) {
      calls.push(['put', key]);
      const prior = objects.get(key), condition = options.onlyIf;
      if (condition?.etagDoesNotMatch === '*' && prior) return null;
      if (condition?.etagMatches && condition.etagMatches !== prior?.etag) return null;
      const bytes = typeof value === 'string' ? new TextEncoder().encode(value) : new Uint8Array(value);
      const object = { key, etag: String(++sequence), version: String(sequence), size: bytes.length,
        uploaded: new Date(), customMetadata: options.customMetadata || {}, bytes };
      objects.set(key, object); return object;
    },
    async get(key) {
      calls.push(['get', key]); const object = objects.get(key); if (!object) return null;
      return { ...object, async text() { return new TextDecoder().decode(object.bytes); },
        async arrayBuffer() { return object.bytes.slice().buffer; } };
    },
    async delete(keys) { for (const key of Array.isArray(keys) ? keys : [keys]) { calls.push(['delete', key]); objects.delete(key); } },
    async list({ prefix = '', limit = 1000 } = {}) {
      calls.push(['list', prefix]); const matching = [...objects.values()].filter(object => object.key.startsWith(prefix));
      return { objects: matching.slice(0, limit), truncated: matching.length > limit };
    }
  };
}
