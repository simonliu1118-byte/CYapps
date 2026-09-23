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
} finally {
  database.exec("ROLLBACK");
  database.close();
}

console.log("Bootstrap Workspace and Device SQL regression check passed.");
