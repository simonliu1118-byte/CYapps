import assert from "node:assert/strict";
import test from "node:test";
import { mkdtempSync, readdirSync, readFileSync, writeFileSync, rmSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { createRequire } from "node:module";
import ts from "typescript";

// Compile the actual modules to CommonJS for Node; the Worker uses extensionless imports.
const directory = mkdtempSync(join(tmpdir(), "cyid-session-test-"));
let handleResolveSession, requireIdentitySession, worker, reserveEmailBudget, settleEmailBudget;
try {
  for (const file of readdirSync(new URL("../src/", import.meta.url)).filter(file => file.endsWith(".ts"))) {
    const name = file.slice(0, -3);
    const source = readFileSync(new URL(`../src/${name}.ts`, import.meta.url), "utf8");
    writeFileSync(join(directory, `${name}.js`), ts.transpileModule(source, {
      compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 },
    }).outputText.replace('require("cloudflare:workers")', '({ WorkerEntrypoint: class { constructor(ctx, env) { this.env = env; } } })'));
  }
  const require = createRequire(join(directory, "test.cjs"));
  ({ handleResolveSession } = require("./auth.js"));
  ({ requireIdentitySession } = require("./session-auth.js"));
  worker = require("./index.js").default;
  ({ reserveEmailBudget, settleEmailBudget } = require("./email-budget.js"));
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
  assert.equal("groupKeys" in body.principal, false);
  assert.equal("applicationRoleKey" in body.principal, false);
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

test("retired Group and activation-alias routes return 404 without authority queries", async () => {
  const env = { DB: { prepare() { throw new Error("retired route accessed authority"); } } };
  for (const [method, path] of [["POST", "groups"], ["PATCH", "groups/group-test"], ["PUT", "groups/group-test/members/employee-test"], ["DELETE", "groups/group-test/members/employee-test"], ["PUT", "groups/group-test/applications/APP_TEST"], ["PUT", "applications/APP_TEST/compatibility-role-mode"], ["POST", "employees/employee-test/activation/resend"]]) {
    assert.equal((await worker.fetch(new Request(`https://identity.test/v1/admin/identity/${path}`, { method }), env)).status, 404);
  }
});
function budgetEnvironment(workspaceResult = {}, globalResult = {}) {
  const batches = [];
  const env = { EMAIL_DAILY_BUDGET: "50", DB: {
    prepare(sql) { return { bind(...values) { return {
      sql, values,
      async first() {
        if (sql.includes("INSERT INTO workspace_email_delivery_budget")) {
          if (workspaceResult instanceof Error) throw workspaceResult;
          return workspaceResult;
        }
        if (sql.includes("INSERT INTO email_delivery_budget")) return globalResult;
        throw new Error("Unexpected budget query");
      },
    }; } }; },
    async batch(statements) { batches.push(statements); return statements.map(() => ({ success: true })); },
  } };
  return { env, batches };
}
test("failed Workspace reservation releases the reserved global budget", async () => {
  for (const result of [null, new Error("D1 failure")]) {
    const { env, batches } = budgetEnvironment(result);
    await assert.rejects(reserveEmailBudget(env, "workspace-test", new Date("2026-10-01T23:59:00Z"), 10));
    assert.equal(batches.length, 1);
    assert.equal(batches[0].length, 1);
    assert.equal(batches[0][0].values[1], 0);
  }
});
test("global budget exhaustion never reserves a Workspace budget", async () => {
  const { env, batches } = budgetEnvironment(new Error("must not be queried"), null);
  await assert.rejects(reserveEmailBudget(env, "workspace-test", new Date(), 10), /EMAIL_DAILY_BUDGET_EXHAUSTED/);
  assert.equal(batches.length, 0);
});
test("settlement commits both counters atomically against the reservation date", async () => {
  for (const sent of [false, true]) {
    const { env, batches } = budgetEnvironment();
    const reservation = await reserveEmailBudget(env, "workspace-test", new Date("2026-10-01T23:59:00Z"), 10);
    await settleEmailBudget(env, reservation, sent, new Date("2026-10-02T00:01:00Z"));
    assert.equal(batches.length, 1);
    assert.equal(batches[0].length, 2);
    assert.equal(batches[0][0].values[0], "2026-10-01");
    assert.equal(batches[0][1].values[1], "2026-10-01");
    assert.equal(batches[0][0].values[1], sent ? 1 : 0);
    assert.equal(batches[0][1].values[2], sent ? 1 : 0);
  }
});
