import { DatabaseSync } from "node:sqlite";
import { mkdtempSync, readdirSync, readFileSync, writeFileSync, rmSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { createRequire } from "node:module";
import { createHash } from "node:crypto";
import ts from "typescript";

const directory = mkdtempSync(join(tmpdir(), "cyid-authority-test-"));
let worker, testing;
try {
  for (const file of readdirSync(new URL("../src/", import.meta.url)).filter(file => file.endsWith(".ts"))) {
    const source = readFileSync(new URL(`../src/${file}`, import.meta.url), "utf8");
    writeFileSync(join(directory, file.replace(/\.ts$/, ".js")), ts.transpileModule(source, {
      compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 },
    }).outputText.replace('require("cloudflare:workers")', '({ WorkerEntrypoint: class {} })'));
  }
  const require = createRequire(join(directory, "test.cjs"));
  worker = require("./index.js").default;
  testing = require("./account-lifecycle-testing.js").ACCOUNT_LIFECYCLE_TESTING;
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
      async run() { return { meta: { changes: Number(db.prepare(sql).run(...values).changes) } }; },
    }; } }; },
    async batch(statements) {
      beforeBatch?.(db); beforeBatch = null;
      db.exec("BEGIN");
      try {
        const results = statements.map(({ sql, values }) => {
          if (sql.trim().startsWith("SELECT") || /RETURNING/i.test(sql)) return { results: db.prepare(sql).all(...values) };
          const result = db.prepare(sql).run(...values);
          return { results: [], meta: { changes: Number(result.changes) } };
        });
        db.exec("COMMIT"); return results;
      } catch (error) { db.exec("ROLLBACK"); throw error; }
    },
  } };
  return { db, env };
}

export { worker, fixture, token, testing };
