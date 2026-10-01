import assert from "node:assert/strict";
import test from "node:test";
import { mkdtempSync, readFileSync, writeFileSync, rmSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { createRequire } from "node:module";
import ts from "typescript";

// Compile the actual modules to CommonJS for Node; the Worker uses extensionless imports.
const directory = mkdtempSync(join(tmpdir(), "cyid-session-test-"));
let handleResolveSession, requireIdentitySession;
try {
  for (const name of ["auth", "session-auth", "crypto", "role-access", "http"]) {
    const source = readFileSync(new URL(`../src/${name}.ts`, import.meta.url), "utf8");
    writeFileSync(join(directory, `${name}.js`), ts.transpileModule(source, {
      compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 },
    }).outputText);
  }
  const require = createRequire(join(directory, "test.cjs"));
  ({ handleResolveSession } = require("./auth.js"));
  ({ requireIdentitySession } = require("./session-auth.js"));
} finally { rmSync(directory, { recursive: true, force: true }); }

const valid = {
  workspace_id: "workspace-test", employee_id: "employee-test", application_id: "APP_TEST",
  session_credential_version: 1, current_credential_version: 1,
  expires_at: "2999-01-01T00:00:00.000Z", revoked_at: null,
  employee_no: "0002", name: "Synthetic Employee", email_verified_at: "2026-01-01T00:00:00.000Z",
  employee_enabled: 1, employee_role_key: "ADMIN", employee_identity_admin: 1, employee_revision: 2,
  workspace_status: "active", super_admin_employee_id: "other-employee",
  application_status: "active", workspace_application_enabled: 1,
};
function environment(row, access = true) {
  return { DB: { prepare(sql) {
    return { bind(...values) {
      if (sql.includes("FROM identity_sessions")) {
        assert.match(values[0], /^[0-9a-f]{64}$/);
        assert.equal(values[1], "APP_TEST");
        return { first: async () => row };
      }
      if (sql.includes("FROM employee_application_access")) return { first: async () => access ? { allowed: 1 } : null };
      if (sql.includes("FROM employee_identity_groups")) return { all: async () => ({ results: [] }) };
      throw new Error("Unexpected authority query");
    } };
  } } };
}
function request(application = "APP_TEST", token = `cyid_${"a".repeat(64)}`) {
  return new Request("https://identity.test/v1/identity/session/resolve", {
    headers: { authorization: `Bearer ${token}`, "x-identity-application": application },
  });
}

test("resolve API and management guard expose the same current principal", async () => {
  const env = environment(valid);
  const response = await handleResolveSession(request(), env, "test");
  assert.equal(response.status, 200);
  const body = await response.json();
  assert.deepEqual(body.principal, await requireIdentitySession(request(), env));
  assert.equal(body.principal.workspaceRole, "ADMIN");
  assert.equal(body.principal.isIdentityAdmin, true);
  assert.equal(body.session.expiresAt, valid.expires_at);
});
for (const [name, patch, access] of [
  ["revoked", { revoked_at: "2026-01-01T00:00:00.000Z" }],
  ["expired", { expires_at: "2000-01-01T00:00:00.000Z" }],
  ["disabled employee", { employee_enabled: 0 }],
  ["disabled Workspace", { workspace_status: "disabled" }],
  ["disabled application", { application_status: "disabled" }],
  ["disabled Workspace application", { workspace_application_enabled: 0 }],
  ["rotated credential", { current_credential_version: 2 }],
  ["revoked direct App Access", {}, false],
]) test(`both Session entry points reject ${name}`, async () => {
  const env = environment({ ...valid, ...patch }, access);
  assert.equal(await requireIdentitySession(request(), env), null);
  assert.equal((await handleResolveSession(request(), env, "test")).status, 401);
});
test("invalid inputs and missing authority fail closed at both entry points", async () => {
  for (const [req, env] of [[request("!"), environment(valid)], [request("APP_TEST", "first-login-ticket"), environment(valid)], [request(), environment(null)]]) {
    assert.equal(await requireIdentitySession(req, env), null);
    assert.equal((await handleResolveSession(req, env, "test")).status, 401);
  }
});
test("role and Identity Admin changes appear immediately without Session writes", async () => {
  const env = environment({ ...valid, employee_role_key: "USER", employee_identity_admin: 0, employee_revision: 3 });
  const body = await (await handleResolveSession(request(), env, "test")).json();
  assert.equal(body.principal.workspaceRole, "USER");
  assert.equal(body.principal.isIdentityAdmin, false);
  assert.equal(body.principal.employeeRevision, 3);
  assert.deepEqual(body.principal, await requireIdentitySession(request(), env));
});
