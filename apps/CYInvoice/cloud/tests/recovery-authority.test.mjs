import assert from "node:assert/strict";
import { build } from "esbuild";

const bundle = await build({
  entryPoints: ["src/recovery-authority.ts"], bundle: true, write: false,
  format: "esm", platform: "node", target: "node24",
});
const { withAuthorizedCyWebRecovery } = await import(
  "data:text/javascript;base64," + Buffer.from(bundle.outputFiles[0].text).toString("base64")
);

const operationId = "11111111-1111-4111-8111-111111111111";
const employeeId = "actor_synthetic";
const principal = {
  workspaceId: "identity_workspace",
  employeeId, employeeNo: "0001", displayName: "Synthetic Owner",
  workspaceRole: "SUPER_ADMIN", isIdentityAdmin: false,
  isWorkspaceSuperAdmin: true, emailVerified: true,
  credentialVersion: 4, employeeRevision: 7,
};
const input = {
  operationId, employeeNo: "0001", password: "syntheticpass",
  expectedEmployeeId: employeeId,
  expectedIdentityWorkspaceId: "identity_workspace",
  expectedCredentialVersion: 4, expectedEmployeeRevision: 7,
};
const token = "cyid_" + "a".repeat(64);
let livePrincipal = principal;
let profileReady = true;
let calls = { login: 0, resolve: 0, snapshot: 0, logout: 0, query: 0 };

const env = {
  CYID_ENABLED: "true",
  IDENTITY_APPLICATION_ID: "CYINVOICE",
  IDENTITY_WORKSPACE_ID: "identity_workspace",
  IDENTITY_CYINVOICE_WORKSPACE_ID: "business_workspace",
  DB: {
    prepare(sql) {
      assert.ok(sql.includes("JOIN cyid_storage_profile") &&
        sql.includes("JOIN cyweb_recovery_storage_profile"), "both storage gates required");
      return {
        bind(workspaceId) {
          assert.equal(workspaceId, "business_workspace");
          return { first: async () => {
            calls.query++;
            return profileReady ? { workspace_id: workspaceId } : null;
          }};
        },
      };
    },
  },
  IDENTITY: {
    async fetch(request) {
      const url = new URL(request.url);
      assert.equal(url.origin, "https://identity.internal");
      assert.equal(request.headers.get("x-identity-application"), "CYINVOICE",
        "CYWEB core session must never be accepted in place of CYInvoice app auth");
      if (url.pathname === "/v1/identity/login") {
        calls.login++;
        const body = await request.json();
        assert.equal(body.applicationId, "CYINVOICE");
        assert.equal(body.employeeNo, "0001");
        assert.equal(body.password, "syntheticpass");
        return Response.json({ ok: true, principal: livePrincipal,
          session: { token, expiresAt: new Date(Date.now() + 300_000).toISOString() } });
      }
      assert.equal(request.headers.get("authorization"), "Bearer " + token);
      if (url.pathname === "/v1/identity/session/resolve") {
        calls.resolve++;
        return Response.json({ ok: true, principal: livePrincipal });
      }
      if (url.pathname === "/v1/admin/identity/snapshot") {
        calls.snapshot++;
        return Response.json({ ok: true, workspaceId: "identity_workspace", employees: [{
          employee_id: employeeId, email_verified_at: "2026-10-01",
          email_normalized: "owner@example.test",
        }] });
      }
      if (url.pathname === "/v1/identity/logout") {
        calls.logout++;
        return Response.json({ ok: true, loggedOut: true });
      }
      throw new Error("unexpected provider path");
    },
  },
};

let actionCalls = 0;
async function execute(payload = input, environment = env, action = async verifyNow => {
  actionCalls++;
  return verifyNow();
}) {
  return withAuthorizedCyWebRecovery(environment, payload, action);
}
const result = await execute();
assert.equal(result.email, "owner@example.test");
assert.equal(result.operationId, operationId);
assert.equal(result.workspaceId, "business_workspace");
assert.equal(result.employeeId, employeeId);
assert.ok(calls.resolve >= 4 && calls.resolve <= 5,"keep live authorization without redundant resolves");
assert.equal(calls.snapshot,1,"same request reuses profile data, never authorization");
assert.equal(calls.logout, 1);
assert.equal(JSON.stringify(result).includes("syntheticpass"), false);
assert.equal(JSON.stringify(result).includes(token), false);

const noCall = actionCalls;
await assert.rejects(() => execute({ ...input, expectedIdentityWorkspaceId: "other_workspace" }),
  /RECOVERY_SCOPE_MISMATCH/);
await assert.rejects(() => execute({ ...input, expectedEmployeeId: "different_employee" }),
  /RECOVERY_AUTHORITY_CHANGED/);
await assert.rejects(() => execute({ ...input, expectedCredentialVersion: 3 }),
  /RECOVERY_AUTHORITY_CHANGED/);
await assert.rejects(() => execute({ ...input, expectedEmployeeRevision: 8 }),
  /RECOVERY_AUTHORITY_CHANGED/);
await assert.rejects(() => execute({ ...input, operationId: "bad" }),
  /RECOVERY_REQUEST_INVALID/);
await assert.rejects(() => execute({ ...input, workspaceRole: "SUPER_ADMIN" }),
  /RECOVERY_REQUEST_INVALID/);
await assert.rejects(() => execute(input, { ...env, CYID_ENABLED: "false" }),
  /IDENTITY_PROVIDER_MISMATCH/);
assert.equal(actionCalls, noCall, "Denied requests must never enter mutation callback");

// Source-mode gate must require the physically retired CYID business D1 *and*
// the separately approved optional recovery issuer storage profile.
profileReady = false;
await assert.rejects(() => execute(), /RECOVERY_STORAGE_NOT_READY/);
assert.equal(actionCalls, noCall);
profileReady = true;

livePrincipal = { ...principal, workspaceRole: "ADMIN", isWorkspaceSuperAdmin: false, isIdentityAdmin: true };
await assert.rejects(() => execute(), /RECOVERY_AUTHORITY_CHANGED/);
assert.equal(actionCalls, noCall);
livePrincipal = principal;

// Invalidate authorization between the first successful check and a later
// action boundary. The action must call verifyNow again before writing.
await assert.rejects(() => execute(input, env, async verifyNow => {
  livePrincipal = { ...principal, credentialVersion: 5 };
  try {
    await verifyNow();
  } finally {
    livePrincipal = principal;
  }
}), /RECOVERY_AUTHORITY_CHANGED/);
assert.equal(calls.login, calls.logout, "CYID Session must be logged out after each verified/denied attempt");
console.log("CYInvoice V3 private recovery: CYID app-scope reauthentication, actor/version/email, dual profile, fresh boundary checks and logout passed");
