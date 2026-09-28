import {
  handleChangeOwnPassword,
  handleConfirmOwnEmailChange,
  handleConfirmPasswordRecovery,
  handleStartOwnEmailChange,
  handleStartPasswordRecovery,
} from "./account-lifecycle";
import { handleLogin, handleLogout, handleResolveSession } from "./auth";
import {
  handleConfirmHighestAuthorityTransfer,
  handleStartHighestAuthorityTransfer,
} from "./authority-transfer";
import { handleBootstrapConfirm, handleBootstrapStart } from "./bootstrap";
import {
  handleConfirmEmployeeActivation,
  handleCreateEmployee,
  handleStartEmployeeActivation,
  handleUpdateEmployee,
} from "./employee-lifecycle";
import { json, requestIdFrom } from "./http";
import {
  handleCreateIdentityGroup,
  handleDeleteIdentityGroupMember,
  handleIdentityAdminSnapshot,
  handlePutApplicationCompatibilityRoleMode,
  handlePutEmployeeApplicationAccess,
  handlePutGroupApplicationAccess,
  handlePutIdentityGroupMember,
  handleUpdateIdentityGroup,
} from "./identity-admin";
import { enforceLoginRateLimit } from "./rate-limit";
import { handleGetSecurityPolicy, handleUpdateSecurityPolicy } from "./security-policy";
import type { Env } from "./types";

function decodedSegment(value: string): string | null {
  try {
    return decodeURIComponent(value);
  } catch {
    return null;
  }
}

export default {
  async fetch(request: Request, env: Env): Promise<Response> {
    const requestId = requestIdFrom(request);
    const url = new URL(request.url);

    try {
      if (request.method === "GET" && url.pathname === "/v1/health") {
        return json(env, requestId, 200, {
          status: "ok",
          identity: "ready",
        });
      }

      if (request.method === "POST" && url.pathname === "/v1/bootstrap/start") {
        return await handleBootstrapStart(request, env, requestId);
      }

      if (request.method === "POST" && url.pathname === "/v1/bootstrap/confirm") {
        return await handleBootstrapConfirm(request, env, requestId);
      }

      if (request.method === "POST" && url.pathname === "/v1/identity/login") {
        const limited = await enforceLoginRateLimit(request, env, requestId);
        if (limited) return limited;
        return await handleLogin(request, env, requestId);
      }

      if (request.method === "POST" && url.pathname === "/v1/identity/session/resolve") {
        return await handleResolveSession(request, env, requestId);
      }

      if (request.method === "POST" && url.pathname === "/v1/identity/logout") {
        return await handleLogout(request, env, requestId);
      }

      if (request.method === "POST" && url.pathname === "/v1/identity/activation/start") {
        return await handleStartEmployeeActivation(request, env, requestId);
      }

      if (request.method === "POST" && url.pathname === "/v1/identity/activation/confirm") {
        return await handleConfirmEmployeeActivation(request, env, requestId);
      }

      if (request.method === "POST" && url.pathname === "/v1/identity/password/change") {
        return await handleChangeOwnPassword(request, env, requestId);
      }

      if (request.method === "POST" && url.pathname === "/v1/identity/email-change/start") {
        return await handleStartOwnEmailChange(request, env, requestId);
      }

      if (request.method === "POST" && url.pathname === "/v1/identity/email-change/confirm") {
        return await handleConfirmOwnEmailChange(request, env, requestId);
      }

      if (request.method === "POST" && url.pathname === "/v1/identity/password-recovery/start") {
        return await handleStartPasswordRecovery(request, env, requestId);
      }

      if (request.method === "POST" && url.pathname === "/v1/identity/password-recovery/confirm") {
        return await handleConfirmPasswordRecovery(request, env, requestId);
      }

      if (request.method === "GET" && url.pathname === "/v1/admin/security-policy") {
        return await handleGetSecurityPolicy(request, env, requestId);
      }

      if (request.method === "PUT" && url.pathname === "/v1/admin/security-policy") {
        return await handleUpdateSecurityPolicy(request, env, requestId);
      }

      if (request.method === "POST" && url.pathname === "/v1/admin/authority-transfer/start") {
        return await handleStartHighestAuthorityTransfer(request, env, requestId);
      }

      if (request.method === "POST" && url.pathname === "/v1/admin/authority-transfer/confirm") {
        return await handleConfirmHighestAuthorityTransfer(request, env, requestId);
      }

      if (request.method === "GET" && url.pathname === "/v1/admin/identity/snapshot") {
        return await handleIdentityAdminSnapshot(request, env, requestId);
      }

      if (request.method === "POST" && url.pathname === "/v1/admin/identity/employees") {
        return await handleCreateEmployee(request, env, requestId);
      }

      if (request.method === "POST" && url.pathname === "/v1/admin/identity/groups") {
        return await handleCreateIdentityGroup(request, env, requestId);
      }

      let match = /^\/v1\/admin\/identity\/employees\/([^/]+)$/.exec(url.pathname);
      if (request.method === "PATCH" && match) {
        const employeeId = decodedSegment(match[1]);
        if (employeeId) return await handleUpdateEmployee(request, env, requestId, employeeId);
      }

      match = /^\/v1\/admin\/identity\/groups\/([^/]+)$/.exec(url.pathname);
      if (request.method === "PATCH" && match) {
        const groupId = decodedSegment(match[1]);
        if (groupId) return await handleUpdateIdentityGroup(request, env, requestId, groupId);
      }

      match = /^\/v1\/admin\/identity\/groups\/([^/]+)\/members\/([^/]+)$/.exec(url.pathname);
      if (match) {
        const groupId = decodedSegment(match[1]);
        const employeeId = decodedSegment(match[2]);
        if (groupId && employeeId && request.method === "PUT") {
          return await handlePutIdentityGroupMember(request, env, requestId, groupId, employeeId);
        }
        if (groupId && employeeId && request.method === "DELETE") {
          return await handleDeleteIdentityGroupMember(request, env, requestId, groupId, employeeId);
        }
      }

      match = /^\/v1\/admin\/identity\/groups\/([^/]+)\/applications\/([^/]+)$/.exec(url.pathname);
      if (request.method === "PUT" && match) {
        const groupId = decodedSegment(match[1]);
        const applicationId = decodedSegment(match[2]);
        if (groupId && applicationId) {
          return await handlePutGroupApplicationAccess(request, env, requestId, groupId, applicationId);
        }
      }

      match = /^\/v1\/admin\/identity\/employees\/([^/]+)\/applications\/([^/]+)$/.exec(url.pathname);
      if (request.method === "PUT" && match) {
        const employeeId = decodedSegment(match[1]);
        const applicationId = decodedSegment(match[2]);
        if (employeeId && applicationId) {
          return await handlePutEmployeeApplicationAccess(request, env, requestId, employeeId, applicationId);
        }
      }

      match = /^\/v1\/admin\/identity\/applications\/([^/]+)\/compatibility-role-mode$/.exec(url.pathname);
      if (request.method === "PUT" && match) {
        const applicationId = decodedSegment(match[1]);
        if (applicationId) {
          return await handlePutApplicationCompatibilityRoleMode(request, env, requestId, applicationId);
        }
      }

      return json(env, requestId, 404, {
        error: { code: "NOT_FOUND", message: "Identity endpoint was not found." },
      });
    } catch (error) {
      console.error("identity_request_failed", {
        requestId,
        path: url.pathname,
        error: error instanceof Error ? error.message : "unknown_error",
      });
      return json(env, requestId, 500, {
        error: { code: "IDENTITY_REQUEST_FAILED", message: "Identity request could not be completed." },
      });
    }
  },
} satisfies ExportedHandler<Env>;
