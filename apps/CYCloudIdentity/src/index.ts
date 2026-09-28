import { handleLogin, handleLogout, handleResolveSession } from "./auth";
import { handleBootstrapConfirm, handleBootstrapStart } from "./bootstrap";
import { json, requestIdFrom } from "./http";
import { enforceLoginRateLimit } from "./rate-limit";
import type { Env } from "./types";

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
