import assert from "node:assert/strict";
import test from "node:test";
import { DatabaseSync } from "node:sqlite";
import { mkdtempSync, readdirSync, readFileSync, writeFileSync, rmSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { createRequire } from "node:module";
import { createHash } from "node:crypto";
import ts from "typescript";

const directory = mkdtempSync(join(tmpdir(), "cyid-registration-test-"));
let worker;
try {
  for (const file of readdirSync(new URL("../src/", import.meta.url)).filter(file => file.endsWith(".ts"))) {
    const source = readFileSync(new URL(`../src/${file}`, import.meta.url), "utf8");
    writeFileSync(join(directory, file.replace(/\.ts$/, ".js")), ts.transpileModule(source, {
      compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 },
    }).outputText.replace('require("cloudflare:workers")', '({ WorkerEntrypoint: class {} })'));
  }
  worker = createRequire(join(directory, "test.cjs"))("./index.js").default;
} finally { rmSync(directory, { recursive: true, force: true }); }

const token = `cyid_${"b".repeat(64)}`;
const tokenHash = createHash("sha256").update(token).digest("hex");
function fixture(options = {}) {
  const db = new DatabaseSync(":memory:");
  for (const file of readdirSync(new URL("../migrations/", import.meta.url)).filter(f => f.endsWith(".sql")).sort()) {
    db.exec(readFileSync(new URL(`../migrations/${file}`, import.meta.url), "utf8"));
  }
  db.exec(`INSERT INTO applications(application_id, display_name) VALUES ('CORE_TEST', 'Synthetic Core'), ('OTHER_APP', 'Other App');`);
  for (const suffix of ["one", "two"]) {
    db.exec(`INSERT INTO workspaces(workspace_id, workspace_code, display_name) VALUES ('workspace-${suffix}', 'TEST_${suffix.toUpperCase()}', 'Synthetic Workspace');
      INSERT INTO employees(employee_id, workspace_id, employee_no, name, email_normalized, email_verified_at, role_key, activated_at)
      VALUES ('employee-${suffix}', 'workspace-${suffix}', '0001', 'Synthetic Super', '${suffix}@example.test', '2026-01-01', 'ADMIN', '2026-01-01');
      INSERT INTO employee_credentials(employee_id, algorithm, verifier, credential_version) VALUES ('employee-${suffix}', 'pbkdf2-sha256', '${"x".repeat(96)}', 1);
      UPDATE workspaces SET super_admin_employee_id='employee-${suffix}', status='active' WHERE workspace_id='workspace-${suffix}';
      INSERT INTO workspace_applications(workspace_id, application_id) VALUES ('workspace-${suffix}', 'CORE_TEST'), ('workspace-${suffix}', 'OTHER_APP');`);
  }
  db.prepare(`INSERT INTO identity_sessions(session_hash, workspace_id, employee_id, application_id, credential_version, employee_revision, created_at, expires_at)
    VALUES (?, 'workspace-one', 'employee-one', ?, 1, 1, '2026-01-01', '2999-01-01')`).run(tokenHash, options.application ?? "CORE_TEST");
  let beforeBatch = options.beforeBatch;
  const env = { CORE_ACCOUNT_APPLICATION_ID: "CORE_TEST", DB: {
    prepare(sql) { return { bind(...values) { return {
      sql, values,
      async first() { return db.prepare(sql).get(...values) ?? null; },
      async all() { return { results: db.prepare(sql).all(...values) }; },
    }; } }; },
    async batch(statements) {
      beforeBatch?.(db); beforeBatch = null;
      db.exec("BEGIN");
      try {
        const results = statements.map(({ sql, values }) => {
          if (sql.trim().startsWith("SELECT")) return { results: db.prepare(sql).all(...values) };
          const result = db.prepare(sql).run(...values);
          return { results: [], meta: { changes: Number(result.changes) } };
        });
        db.exec("COMMIT"); return results;
      } catch (error) { db.exec("ROLLBACK"); throw error; }
    },
  } };
  return { db, env };
}
function request(body = { applicationId: "APP_NEW", displayName: "New App" }, application = "CORE_TEST") {
  return new Request("https://identity.test/v1/admin/identity/applications", {
    method: "POST", headers: { "content-type": "application/json", authorization: `Bearer ${token}`, "x-identity-application": application },
    body: JSON.stringify(body),
  });
}
async function register(f, body, application) { return worker.fetch(request(body, application), f.env); }
const count = (db, table) => db.prepare(`SELECT count(*) AS n FROM ${table}`).get().n;

test("core Super Admin registers only its Workspace, without employee grants or authority changes", async () => {
  const f = fixture();
  try {
    const employees = f.db.prepare("SELECT * FROM employees ORDER BY employee_id").all();
    assert.equal((await register(f, { applicationId: " app_new ", displayName: " New App " })).status, 200);
    assert.deepEqual(f.db.prepare("SELECT workspace_id, application_id, enabled FROM workspace_applications WHERE application_id='APP_NEW'").all(),
      [Object.assign(Object.create(null), { workspace_id: "workspace-one", application_id: "APP_NEW", enabled: 1 })]);
    assert.equal(count(f.db, "employee_application_access"), 0);
    assert.deepEqual(f.db.prepare("SELECT * FROM employees ORDER BY employee_id").all(), employees);
    assert.equal(count(f.db, "identity_sessions"), 1);
    assert.equal(count(f.db, "identity_audit_events"), 1);
    assert.equal((await register(f)).status, 200);
    assert.equal(count(f.db, "applications"), 3);
    assert.equal(f.db.prepare("SELECT count(*) AS n FROM workspace_applications WHERE application_id='APP_NEW'").get().n, 1);
    assert.deepEqual(f.db.prepare("PRAGMA foreign_key_check").all(), []);
  } finally { f.db.close(); }
});

for (const [name, patch] of [
  ["normal ADMIN", "INSERT INTO employees(employee_id,workspace_id,employee_no,name,email_normalized,role_key) VALUES('ordinary-admin','workspace-one','0002','Synthetic Admin','admin@example.test','ADMIN'); UPDATE workspaces SET super_admin_employee_id='ordinary-admin' WHERE workspace_id='workspace-one'"],
  ["Identity Admin", "INSERT INTO employees(employee_id,workspace_id,employee_no,name,email_normalized,role_key) VALUES('ordinary-admin','workspace-one','0002','Synthetic Admin','admin@example.test','ADMIN'); UPDATE workspaces SET super_admin_employee_id='ordinary-admin' WHERE workspace_id='workspace-one'; UPDATE employees SET identity_admin=1 WHERE employee_id='employee-one'"],
  ["revoked Session", "UPDATE identity_sessions SET revoked_at='2026-01-02'"],
  ["changed credential", "UPDATE employee_credentials SET credential_version=2 WHERE employee_id='employee-one'"],
  ["disabled Workspace", "UPDATE workspaces SET status='disabled' WHERE workspace_id='workspace-one'"],
]) test(`registration rejects ${name}`, async () => {
  const f = fixture();
  try { f.db.exec(patch); assert.equal((await register(f)).status, 403); assert.equal(count(f.db, "applications"), 2); assert.equal(count(f.db, "identity_audit_events"), 0); }
  finally { f.db.close(); }
});

test("non-core app Session cannot register despite Super Admin authority", async () => {
  const f = fixture({ application: "OTHER_APP" });
  try { assert.equal((await register(f, undefined, "OTHER_APP")).status, 403); assert.equal(count(f.db, "applications"), 2); }
  finally { f.db.close(); }
});
for (const body of [
  { applicationId: "APP_NEW", displayName: "New App", workspaceId: "workspace-two" },
  { applicationId: "APP_NEW", displayName: "New App", grants: ["employee-two"] },
  { applicationId: "!", displayName: "New App" },
  { applicationId: "APP_NEW", displayName: "\n" },
  { applicationId: "APP_NEW", displayName: "a".repeat(121) },
]) test("invalid registration and client-selected scope/grants are rejected without writes", async () => {
  const f = fixture();
  try { assert.equal((await register(f, body)).status, 400); assert.equal(count(f.db, "applications"), 2); assert.equal(count(f.db, "identity_audit_events"), 0); }
  finally { f.db.close(); }
});
test("global catalog collision and disabled application cannot be overwritten", async () => {
  const f = fixture();
  try {
    for (const body of [{ applicationId: "OTHER_APP", displayName: "Different Name" }, { applicationId: "CORE_TEST", displayName: "Synthetic Core" }]) {
      assert.equal((await register(f, body)).status, 409);
    }
    f.db.exec("UPDATE applications SET status='disabled' WHERE application_id='OTHER_APP'");
    assert.equal((await register(f, { applicationId: "OTHER_APP", displayName: "Other App" })).status, 409);
    assert.equal(f.db.prepare("SELECT display_name,status FROM applications WHERE application_id='OTHER_APP'").get().status, "disabled");
    assert.equal(count(f.db, "identity_audit_events"), 0);
  } finally { f.db.close(); }
});
test("explicit registration re-enables this Workspace while preserving other Workspaces and grants", async () => {
  const f = fixture();
  try {
    f.db.exec("UPDATE workspace_applications SET enabled=0 WHERE application_id='OTHER_APP'; INSERT INTO employee_application_access(workspace_id,employee_id,application_id,enabled) VALUES('workspace-one','employee-one','OTHER_APP',0)");
    assert.equal((await register(f, { applicationId: "OTHER_APP", displayName: "Other App" })).status, 200);
    assert.equal(f.db.prepare("SELECT enabled FROM workspace_applications WHERE workspace_id='workspace-two' AND application_id='OTHER_APP'").get().enabled, 0);
    assert.equal(f.db.prepare("SELECT enabled FROM employee_application_access").get().enabled, 0);
  } finally { f.db.close(); }
});
for (const patch of [
  "UPDATE identity_sessions SET revoked_at='2026-01-02'",
  "UPDATE employee_credentials SET credential_version=2 WHERE employee_id='employee-one'",
  "INSERT INTO employees(employee_id,workspace_id,employee_no,name,email_normalized,role_key) VALUES('next-super','workspace-one','0002','Synthetic Next','next@example.test','ADMIN'); UPDATE workspaces SET super_admin_employee_id='next-super' WHERE workspace_id='workspace-one'",
]) test("transaction rejects authority revoked after the initial management guard, with no catalog or audit writes", async () => {
  const f = fixture({ beforeBatch(db) { db.exec(patch); } });
  try { assert.equal((await register(f)).status, 403); assert.equal(count(f.db, "applications"), 2); assert.equal(count(f.db, "identity_audit_events"), 0); }
  finally { f.db.close(); }
});

test("audit failure rolls back the catalog and Workspace registration together", async () => {
  const f = fixture();
  try {
    f.db.exec("CREATE TRIGGER synthetic_audit_failure BEFORE INSERT ON identity_audit_events BEGIN SELECT RAISE(ABORT, 'synthetic audit failure'); END");
    assert.equal((await register(f)).status, 500);
    assert.equal(count(f.db, "applications"), 2);
    assert.equal(f.db.prepare("SELECT count(*) AS n FROM workspace_applications WHERE application_id='APP_NEW'").get().n, 0);
    assert.equal(count(f.db, "identity_audit_events"), 0);
  } finally { f.db.close(); }
});
