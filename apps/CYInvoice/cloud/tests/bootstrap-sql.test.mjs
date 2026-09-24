import assert from "node:assert/strict";
import { readFileSync, readdirSync } from "node:fs";
import { DatabaseSync } from "node:sqlite";
import { join } from "node:path";
import { fileURLToPath } from "node:url";
import {
  BOOTSTRAP_DEVICE_INSERT_SQL,
  BOOTSTRAP_WORKSPACE_INSERT_SQL,
} from "../src/bootstrap-sql.ts";

const cloudRoot = fileURLToPath(new URL("..", import.meta.url));
const migrationsRoot = join(cloudRoot, "migrations");
const database = new DatabaseSync(":memory:");

for (const migration of readdirSync(migrationsRoot).filter(name => name.endsWith(".sql")).sort()) {
  database.exec(readFileSync(join(migrationsRoot, migration), "utf8"));
}

database.exec("BEGIN");
try {
  const now = "2026-09-22T00:00:00.000Z";
  database.prepare(BOOTSTRAP_WORKSPACE_INSERT_SQL).run(
    "ws_bootstrap_test",
    "CYInvoice Test",
    "owner@example.test",
    now,
  );
  database.prepare(BOOTSTRAP_DEVICE_INSERT_SQL).run(
    "dev_bootstrap_test",
    "ws_bootstrap_test",
    "Test Device",
    "a".repeat(64),
    "2.6.4",
    now,
  );

  const counts = database.prepare(`SELECT
    (SELECT COUNT(*) FROM workspaces) AS workspaces,
    (SELECT COUNT(*) FROM devices) AS devices`).get();
  assert.equal(counts.workspaces, 1);
  assert.equal(counts.devices, 1);
  database.prepare(`INSERT INTO device_pairing_codes
    (pairing_id, workspace_id, code_hash, created_by_device_id, expires_at, created_at)
    VALUES (?, ?, ?, ?, ?, ?)`).run("pair_test", "ws_bootstrap_test", "b".repeat(64),
    "dev_bootstrap_test", now, now);
  assert.equal(database.prepare("SELECT COUNT(*) AS count FROM device_pairing_codes").get().count, 1);
  assert.equal(database.prepare("SELECT COUNT(*) AS count FROM device_invitations").get().count, 0);
  assert.equal(database.prepare("SELECT COUNT(*) AS count FROM security_audit_events").get().count, 0);
  database.prepare(`INSERT INTO cloud_employees (employee_id, workspace_id, employee_no,
    name, email_normalized, email_verified_at, role, enabled)
    VALUES (?, ?, ?, ?, ?, ?, 'SUPER_ADMIN', 1)`).run("emp_test", "ws_bootstrap_test",
    "3001", "Test Owner", "owner@example.test", now);
  database.prepare(`INSERT INTO device_invitations (invitation_id, workspace_id, code_hash,
    issued_by_device_id, issued_by_employee_id, delivery_state, expires_at, created_at)
    VALUES (?, ?, ?, ?, ?, 'sent', ?, ?)`).run("inv_test", "ws_bootstrap_test",
    "c".repeat(64), "dev_bootstrap_test", "emp_test", "2026-09-26T00:00:00.000Z", now);
  database.prepare(`INSERT INTO devices (device_id, workspace_id, display_name, token_hash,
    client_version, status, invitation_id, employee_authority_state, paired_at, created_at, updated_at)
    VALUES (?, ?, ?, ?, ?, 'active', ?, 'cloud', ?, ?, ?)`).run("dev_invited",
    "ws_bootstrap_test", "New Device", "d".repeat(64), "2.6.6", "inv_test", now, now, now);
  assert.throws(() => database.prepare(`INSERT INTO devices (device_id, workspace_id, display_name,
    invitation_id) VALUES (?, ?, ?, ?)`).run("dev_duplicate", "ws_bootstrap_test",
    "Duplicate", "inv_test"), /UNIQUE constraint failed/);
} finally {
  database.exec("ROLLBACK");
  database.close();
}

console.log("Bootstrap Workspace and Device SQL regression check passed.");
