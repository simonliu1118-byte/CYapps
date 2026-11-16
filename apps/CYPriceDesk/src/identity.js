// CYID consumer adapter (login / resolve / logout). Ported from apps/CYAccountingWeb/src/identity-adapter.js
// without the CYACC-specific parts. CYID owns Session authority; this file only talks to the IDENTITY binding.
export const SESSION_COOKIE = 'cypd_session';

const SESSION_TOKEN_PATTERN = /^cyid_[0-9a-f]{64}$/;
const ROLES = new Set(['USER', 'ADMIN', 'SUPER_ADMIN']);
const DEFAULT_IDENTITY_TIMEOUT_MS = 5_000;
const MIN_IDENTITY_TIMEOUT_MS = 100;
const MAX_IDENTITY_TIMEOUT_MS = 15_000;
const resolutionCache = new WeakMap();

/** Public navigation metadata only. CYID still owns all authentication. */
export function cyWebPortalUrl(env) {
  const configured = String(env?.CYWEB_PORTAL_URL || '').trim();
  if (!configured) return null;
  try {
    const url = new URL(configured);
    if (url.protocol !== 'https:' || !url.hostname || url.username || url.password || url.search || url.hash) return null;
    return url.origin + '/';
  } catch {
    return null;
  }
}

export function identityRuntimeConfig(env) {
  const workspaceId = String(env?.CYID_WORKSPACE_ID || '').trim();
  const applicationId = String(env?.CYID_APPLICATION_ID || '').trim().toUpperCase();
  const serviceReady = Boolean(env?.IDENTITY && typeof env.IDENTITY.fetch === 'function');
  const workspaceReady = workspaceId.length >= 5 && workspaceId.length <= 80;
  const applicationReady = /^[A-Z0-9][A-Z0-9_-]{1,63}$/.test(applicationId);
  return { ready: serviceReady && workspaceReady && applicationReady, workspaceId, applicationId };
}

export function passwordWithinCyidBounds(value) {
  return typeof value === 'string' && Array.from(value).length >= 8 && Array.from(value).length <= 16;
}

export async function loginWithCyid(request, env, employeeNo, password) {
  const config = identityRuntimeConfig(env);
  if (!config.ready) return failure(503, 'IDENTITY_NOT_CONFIGURED');
  if (!/^\d{4}$/.test(String(employeeNo || '').trim()) || !passwordWithinCyidBounds(password)) {
    return failure(400, 'INVALID_LOGIN_REQUEST');
  }

  const response = await identityFetch(request, env, '/v1/identity/login', {
    body: {
      workspaceId: config.workspaceId,
      applicationId: config.applicationId,
      employeeNo: String(employeeNo).trim(),
      password
    }
  });
  const payload = await response.json().catch(() => ({}));

  if (!response.ok) {
    return failure(response.status, String(payload?.error?.code || 'AUTHENTICATION_FAILED'), response);
  }
  // CYID standard section 8: a non-core consumer fails closed on first-login and never creates a Session.
  if (payload?.passwordChangeRequired || payload?.firstLogin) {
    return failure(403, 'FIRST_LOGIN_REQUIRED');
  }

  const principal = normalizePrincipal(payload?.principal);
  const token = String(payload?.session?.token || '');
  const expiresAt = String(payload?.session?.expiresAt || '');
  if (!principal || principal.workspaceId !== config.workspaceId || !SESSION_TOKEN_PATTERN.test(token) || !validFutureTimestamp(expiresAt)) {
    return failure(502, 'IDENTITY_INVALID_RESPONSE');
  }

  return { ok: true, principal, token, expiresAt, cookie: providerSessionCookie(token, expiresAt) };
}

export async function resolveIdentitySession(request, env) {
  if (resolutionCache.has(request)) return resolutionCache.get(request);
  const pending = resolveIdentitySessionUncached(request, env);
  resolutionCache.set(request, pending);
  return pending;
}

async function resolveIdentitySessionUncached(request, env) {
  const config = identityRuntimeConfig(env);
  if (!config.ready) return failure(503, 'IDENTITY_NOT_CONFIGURED');

  const token = cookieValue(request, SESSION_COOKIE);
  if (!SESSION_TOKEN_PATTERN.test(token || '')) return failure(401, 'SESSION_INVALID');

  const response = await identityFetch(request, env, '/v1/identity/session/resolve', { token });
  const payload = await response.json().catch(() => ({}));
  if (!response.ok) {
    return failure(response.status, String(payload?.error?.code || 'SESSION_INVALID'), response);
  }

  const principal = normalizePrincipal(payload?.principal);
  if (!principal || principal.workspaceId !== config.workspaceId) return failure(502, 'IDENTITY_INVALID_RESPONSE');

  return { ok: true, principal, expiresAt: String(payload?.session?.expiresAt || '') };
}

export async function logoutCyid(request, env) {
  const config = identityRuntimeConfig(env);
  if (!config.ready) return failure(503, 'IDENTITY_NOT_CONFIGURED');

  const token = cookieValue(request, SESSION_COOKIE);
  if (SESSION_TOKEN_PATTERN.test(token || '')) {
    const response = await identityFetch(request, env, '/v1/identity/logout', { token });
    if (!response.ok) {
      const payload = await response.json().catch(() => ({}));
      return failure(response.status, String(payload?.error?.code || 'IDENTITY_LOGOUT_FAILED'), response);
    }
  }
  return { ok: true };
}

// SameSite=Lax with an explicit Expires bounded by the provider expiresAt.
export function providerSessionCookie(token, expiresAt) {
  const expires = new Date(expiresAt);
  if (!SESSION_TOKEN_PATTERN.test(token) || Number.isNaN(expires.getTime())) {
    throw new Error('Invalid CYID provider session cookie input.');
  }
  const maxAge = Math.max(0, Math.floor((expires.getTime() - Date.now()) / 1000));
  return `${SESSION_COOKIE}=${encodeURIComponent(token)}; Path=/; HttpOnly; Secure; SameSite=Lax; Max-Age=${maxAge}; Expires=${expires.toUTCString()}`;
}

export function clearProviderSessionCookie() {
  return `${SESSION_COOKIE}=; Path=/; HttpOnly; Secure; SameSite=Lax; Max-Age=0; Expires=Thu, 01 Jan 1970 00:00:00 GMT`;
}

async function identityFetch(request, env, path, options = {}) {
  const config = identityRuntimeConfig(env);
  const headers = new Headers();
  const clientIp = request.headers.get('cf-connecting-ip');
  if (clientIp) headers.set('cf-connecting-ip', clientIp);
  const requestId = request.headers.get('x-request-id');
  if (requestId) headers.set('x-request-id', requestId);

  if (options.token) {
    headers.set('authorization', `Bearer ${options.token}`);
    headers.set('x-identity-application', config.applicationId);
  }
  if (options.body !== undefined) headers.set('content-type', 'application/json');

  const timeoutMs = identityProviderTimeoutMs(env);
  const controller = new AbortController();
  const providerRequest = new Request(`https://cyid.internal${path}`, {
    method: 'POST',
    headers,
    body: options.body === undefined ? undefined : JSON.stringify(options.body),
    signal: controller.signal
  });

  let timeoutId;
  const provider = Promise.resolve()
    .then(() => env.IDENTITY.fetch(providerRequest))
    .catch(error => {
      if (!controller.signal.aborted) throw error;
      return identityTimeoutResponse();
    });
  const timeout = new Promise(resolve => {
    timeoutId = setTimeout(() => {
      controller.abort();
      resolve(identityTimeoutResponse());
    }, timeoutMs);
  });

  try {
    return await Promise.race([provider, timeout]);
  } finally {
    if (timeoutId !== undefined) clearTimeout(timeoutId);
  }
}

function identityTimeoutResponse() {
  return new Response(JSON.stringify({
    error: { code: 'IDENTITY_TIMEOUT', message: 'Identity provider did not respond in time.' }
  }), { status: 504, headers: { 'content-type': 'application/json; charset=utf-8' } });
}

function identityProviderTimeoutMs(env) {
  const configured = Number(env?.CYID_PROVIDER_TIMEOUT_MS);
  if (!Number.isFinite(configured)) return DEFAULT_IDENTITY_TIMEOUT_MS;
  return Math.max(MIN_IDENTITY_TIMEOUT_MS, Math.min(Math.trunc(configured), MAX_IDENTITY_TIMEOUT_MS));
}

function normalizePrincipal(value) {
  if (!value || typeof value !== 'object') return null;
  const workspaceRole = String(value.workspaceRole || '');
  const employeeNo = String(value.employeeNo || '');
  const employeeId = String(value.employeeId || '');
  const workspaceId = String(value.workspaceId || '');
  if (!ROLES.has(workspaceRole) || !/^\d{4}$/.test(employeeNo) || !employeeId || !workspaceId) return null;

  return {
    workspaceId,
    employeeId,
    employeeNo,
    displayName: String(value.displayName || ''),
    workspaceRole,
    isIdentityAdmin: Boolean(value.isIdentityAdmin),
    emailVerified: Boolean(value.emailVerified),
    isWorkspaceSuperAdmin: Boolean(value.isWorkspaceSuperAdmin),
    credentialVersion: Number(value.credentialVersion || 0),
    employeeRevision: Number(value.employeeRevision || 0)
  };
}

function cookieValue(request, name) {
  const raw = request.headers.get('cookie') || '';
  for (const part of raw.split(';')) {
    const index = part.indexOf('=');
    if (index < 0) continue;
    if (part.slice(0, index).trim() !== name) continue;
    try {
      return decodeURIComponent(part.slice(index + 1).trim());
    } catch {
      return null;
    }
  }
  return null;
}

function validFutureTimestamp(value) {
  const time = Date.parse(value);
  return Number.isFinite(time) && time > Date.now();
}

function failure(status, code, response = null) {
  return { ok: false, status: Number(status) || 500, code, retryAfter: response?.headers?.get?.('retry-after') || null };
}
