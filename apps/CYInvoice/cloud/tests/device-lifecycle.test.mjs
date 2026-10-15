import assert from "node:assert/strict";
import { createHash } from "node:crypto";
import { readFileSync, readdirSync } from "node:fs";
import { DatabaseSync } from "node:sqlite";
import { build } from "esbuild";

// V3 supports CYID Cloud only. The historical forward migrations are applied
// to an isolated in-memory database, then Built-in employee tables are retired.
const built = await build({ entryPoints: ["src/device-lifecycle.ts"], bundle: true,
  write: false, format: "esm", platform: "node" });
const { handleDeviceLifecycle } = await import(
  "data:text/javascript;base64," + Buffer.from(built.outputFiles[0].text).toString("base64"));
const db = new DatabaseSync(":memory:");
const migrations = readdirSync("migrations").filter(x => /^\d{4}_.*\.sql$/.test(x)).sort();
for (const name of migrations.filter(name => name < "0014"))
  db.exec(readFileSync("migrations/" + name, "utf8"));
db.prepare("INSERT INTO workspaces (workspace_id, display_name) VALUES ('ws_cyid', 'CYID Synthetic')").run();
const ids = ["11111111-1111-4111-8111-111111111111",
  "22222222-2222-4222-8222-222222222222",
  "33333333-3333-4333-8333-333333333333"].map(x => "dev_" + x);
const tokens = ["a", "b", "c"].map(x => "cydev_" + x.repeat(64));
for (let i = 0; i < ids.length; i++)
  db.prepare(`INSERT INTO devices (device_id, workspace_id, display_name, token_hash,
    status, employee_authority_state, client_version)
    VALUES (?, 'ws_cyid', ?, ?, 'active', 'cloud', '2.6.13')`)
    .run(ids[i], "CYID Device " + i, createHash("sha256").update(tokens[i]).digest("hex"));
db.exec(readFileSync("maintenance/retire-built-in-for-cyid-only.sql", "utf8"));
for (const name of migrations.filter(name => name >= "0014"))
  db.exec(readFileSync("migrations/" + name, "utf8"));
assert.equal(db.prepare("SELECT COUNT(*) n FROM sqlite_master WHERE name='cloud_employees'").get().n, 0);
let beforeBatch;
class Statement {
  constructor(sql) { this.sql = sql; this.args = []; }
  bind(...args) { this.args = args; return this; }
  async first() { return db.prepare(this.sql).get(...this.args) ?? null; }
  async all() { return { results: db.prepare(this.sql).all(...this.args) }; }
  async run() { return { meta: { changes: db.prepare(this.sql).run(...this.args).changes } }; }
}
let allowed = true;
const env = {
  APP_ENV: "test", API_VERSION: "1", SCHEMA_VERSION: "14", CYID_ENABLED: "true",
  IDENTITY_APPLICATION_ID: "CYINVOICE", IDENTITY_WORKSPACE_ID: "identity_ws",
  IDENTITY_CYINVOICE_WORKSPACE_ID: "ws_cyid", IDENTITY: { fetch: async () => Response.json({}) },
  cyIdContext: { revalidate: async () => ({
    workspaceRole: allowed ? "SUPER_ADMIN" : "USER", employeeId: "cyid_admin",
  }) },
  DB: {
    prepare: sql => new Statement(sql),
    async batch(statements) {
      if (beforeBatch) { const change = beforeBatch; beforeBatch = null; change(); }
      db.exec("BEGIN");
      try {
        const result = [];
        for (const statement of statements) result.push(await statement.run());
        db.exec("COMMIT");
        return result;
      } catch (error) { db.exec("ROLLBACK"); throw error; }
    },
  },
};
async function call(action, body, token = tokens[0]) {
  const response = await handleDeviceLifecycle(new Request(
    "https://cyinvoice.synthetic/v1/devices" + action, {
      method: body === undefined ? "GET" : "POST",
      headers: { authorization: "Bearer " + token, "content-type": "application/json" },
      body: body === undefined ? undefined : JSON.stringify(body),
    }), env);
  return { status: response.status, body: await response.json() };
}
const initial = await call("");
assert.equal(initial.status, 200);
assert.equal(initial.body.activeDeviceCount, 3);
assert.equal(initial.body.devices.length, 3);
const usage = await call("/usage", { clientVersion: " 2.6.20 Build 0 " });
assert.equal(usage.status, 200);
assert.equal(usage.body.device.clientVersion, "2.6.20 Build 0");
for (const value of ["", "x".repeat(65), "Bad\nVersion"])
  assert.equal((await call("/usage", { clientVersion: value })).status, 400);
assert.equal((await call("/usage", { clientVersion: "new" }, "cydev_invalid")).status, 401);
assert.equal((await call("/rename", { targetDeviceId: ids[1], displayName: "Bad\nName" })).status, 400);
allowed = false;
assert.equal((await call("/rename", { targetDeviceId: ids[1], displayName: "No" })).status, 401);
allowed = true;
const renamed = await call("/rename", { targetDeviceId: ids[1], displayName: "新裝置 B" });
assert.equal(renamed.status, 200);
assert.equal(renamed.body.device.displayName, "新裝置 B");
assert.equal(db.prepare("SELECT display_name FROM devices WHERE device_id=?").get(ids[1]).display_name, "新裝置 B");
assert.equal((await call("/rename", { targetDeviceId: ids[1], displayName: "Missing" },
  "cydev_" + "f".repeat(64))).status, 401);
const unknown = await call("/rename", {
  targetDeviceId: "dev_44444444-4444-4444-8444-444444444444", displayName: "Missing",
});
assert.equal(unknown.status, 409);
// Failed Audit insertion must roll back the Device rename atomically.
db.exec(`CREATE TRIGGER reject_rename_audit BEFORE INSERT ON security_audit_events
  WHEN NEW.event_type='device_renamed'
  BEGIN SELECT RAISE(ABORT,'synthetic audit failure'); END;`);
assert.equal((await call("/rename", { targetDeviceId: ids[1], displayName: "Rollback" })).status, 500);
assert.equal(db.prepare("SELECT display_name FROM devices WHERE device_id=?").get(ids[1]).display_name, "新裝置 B");
db.exec("DROP TRIGGER reject_rename_audit");

assert.equal((await call("/revoke", { targetDeviceId: ids[2] })).status, 200);
assert.equal((await call("/usage", { clientVersion: "2.6.20" }, tokens[2])).status, 401);
const revokedAuditBefore = db.prepare(
  "SELECT COUNT(*) n FROM security_audit_events WHERE event_type='device_revoked' AND outcome='success'").get().n;
beforeBatch = () => db.prepare("UPDATE devices SET status='revoked', revoked_at=? WHERE device_id=?")
  .run(new Date().toISOString(), ids[0]);
const late = await call("/revoke", { targetDeviceId: ids[1] });
assert.equal(late.status, 409);
assert.equal(late.body.error.code, "DEVICE_AUTHORITY_CHANGED");
assert.equal(db.prepare("SELECT status FROM devices WHERE device_id=?").get(ids[1]).status, "active");
assert.equal(db.prepare(
  "SELECT COUNT(*) n FROM security_audit_events WHERE event_type='device_revoked' AND outcome='success'").get().n,
  revokedAuditBefore);
assert.equal((await call("/revoke", { targetDeviceId: ids[1] })).status, 401);
assert.deepEqual(db.prepare("PRAGMA foreign_key_check").all(), []);
db.close();
console.log("CYID-only Device usage, rename, no Built-in credentials, audited rollback and revocation race passed");
