// In-memory D1 stand-in on node:sqlite. Applies the real migration files and mimics prepare/bind/all/first/run/batch.
import {DatabaseSync} from 'node:sqlite';
import fs from 'node:fs';

export function createD1() {
  const db = new DatabaseSync(':memory:');
  const dir = new URL('../../migrations/', import.meta.url);
  for (const file of fs.readdirSync(dir).sort()) db.exec(fs.readFileSync(new URL(file, dir), 'utf8'));
  const statement = (sql, params = []) => ({
    bind: (...values) => statement(sql, values),
    all: async () => ({results: db.prepare(sql).all(...params)}),
    first: async () => db.prepare(sql).get(...params) ?? null,
    run: async () => ({success: true, meta: {changes: Number(db.prepare(sql).run(...params).changes)}})
  });
  return {
    prepare: sql => statement(sql),
    async batch(statements) {
      db.exec('BEGIN');
      try {
        const results = [];
        for (const item of statements) results.push(await item.run());
        db.exec('COMMIT');
        return results;
      } catch (error) {
        db.exec('ROLLBACK');
        throw error;
      }
    },
    sqlite: db
  };
}
