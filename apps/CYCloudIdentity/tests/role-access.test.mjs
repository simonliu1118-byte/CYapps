import assert from "node:assert/strict";
import test from "node:test";

import {
  canCreateRole,
  canManageEmployeeLifecycle,
  effectiveWorkspaceRole,
  normalizeStoredEmployeeRole,
} from "../dist/role-access.js";

function principal(workspaceRole, isIdentityAdmin = false) {
  return {
    workspaceId: "workspace-test",
    employeeId: `actor-${workspaceRole}-${isIdentityAdmin}`,
    employeeNo: "0001",
    displayName: "Synthetic Actor",
    workspaceRole,
    isIdentityAdmin,
    emailVerified: true,
    isWorkspaceSuperAdmin: workspaceRole === "SUPER_ADMIN",
    credentialVersion: 1,
    employeeRevision: 1,
  };
}

test("stored role accepts only USER or ADMIN", () => {
  assert.equal(normalizeStoredEmployeeRole("user"), "USER");
  assert.equal(normalizeStoredEmployeeRole("ADMIN"), "ADMIN");
  assert.equal(normalizeStoredEmployeeRole("SUPER_ADMIN"), null);
  assert.equal(normalizeStoredEmployeeRole("HR"), null);
});

test("Super Admin is derived from protected Workspace pointer", () => {
  assert.equal(effectiveWorkspaceRole("emp-1", "emp-1", "ADMIN"), "SUPER_ADMIN");
  assert.equal(effectiveWorkspaceRole("emp-2", "emp-1", "ADMIN"), "ADMIN");
  assert.equal(effectiveWorkspaceRole("emp-2", "emp-1", "USER"), "USER");
});

test("normal ADMIN creates USER only", () => {
  const actor = principal("ADMIN", false);
  assert.equal(canCreateRole(actor, "USER"), true);
  assert.equal(canCreateRole(actor, "ADMIN"), false);
});

test("Identity Admin and Super Admin may create USER or ADMIN", () => {
  const identityAdmin = principal("ADMIN", true);
  const superAdmin = principal("SUPER_ADMIN", false);
  for (const role of ["USER", "ADMIN"]) {
    assert.equal(canCreateRole(identityAdmin, role), true);
    assert.equal(canCreateRole(superAdmin, role), true);
  }
});

test("normal ADMIN manages ordinary USER lifecycle only", () => {
  const actor = principal("ADMIN", false);
  assert.equal(canManageEmployeeLifecycle(actor, "USER", false), true);
  assert.equal(canManageEmployeeLifecycle(actor, "ADMIN", false), false);
  assert.equal(canManageEmployeeLifecycle(actor, "SUPER_ADMIN", false), false);
});

test("Identity Admin manages ordinary USER/ADMIN but not protected Identity Admin or Super Admin", () => {
  const actor = principal("ADMIN", true);
  assert.equal(canManageEmployeeLifecycle(actor, "USER", false), true);
  assert.equal(canManageEmployeeLifecycle(actor, "ADMIN", false), true);
  assert.equal(canManageEmployeeLifecycle(actor, "ADMIN", true), false);
  assert.equal(canManageEmployeeLifecycle(actor, "SUPER_ADMIN", false), false);
});

test("Super Admin manages all non-Super-Admin lifecycle states", () => {
  const actor = principal("SUPER_ADMIN", false);
  assert.equal(canManageEmployeeLifecycle(actor, "USER", false), true);
  assert.equal(canManageEmployeeLifecycle(actor, "ADMIN", false), true);
  assert.equal(canManageEmployeeLifecycle(actor, "ADMIN", true), true);
  assert.equal(canManageEmployeeLifecycle(actor, "SUPER_ADMIN", false), false);
});
