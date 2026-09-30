export const SESSION_COOKIE = 'cyaccounting_session';

const SESSION_TOKEN_PATTERN = /^cyid_[0-9a-f]{64}$/;
const ROLES = new Set(['USER', 'ADMIN', 'SUPER_ADMIN']);
const DEFAULT_IDENTITY_TIMEOUT_MS = 5_000;
const MIN_IDENTITY_TIMEOUT_MS = 100;
const MAX_IDENTITY_TIMEOUT_MS = 15_000;
const resolutionCache = new WeakMap();

export function identityRuntimeConfig(env) {
  const workspaceId = String(env?.CYID_WORKSPACE_ID || '').trim();
  const applicationId = String(env?.CYID_APPLICATION_ID || '').trim().toUpperCase();
  const serviceReady = Boolean(env?.IDENTITY && typeof env.IDENTITY.fetch === 'function');
  const workspaceReady = workspaceId.length >= 5 && workspaceId.length <= 80;
  const applicationReady = /^[A-Z0-9][A-Z0-9_-]{1,63}$/.test(applicationId);
  return {
    ready: serviceReady && workspaceReady && applicationReady,
    workspaceId,
    applicationId
  };
}

export function passwordWithinCyidBounds(value) {
  return typeof value === 'string' && Array.from(value).length >= 8 && Array.from(value).length <= 16;
}

export function canWriteAccounting(principal) {
  return principal?.workspaceRole === 'ADMIN' || principal?.workspaceRole === 'SUPER_ADMIN';
}

export function appUserFromPrincipal(principal) {
  return {
    employeeId: String(principal.employeeId),
    employeeNo: String(principal.employeeNo),
    name: String(principal.displayName || ''),
    role: String(principal.workspaceRole),
    isIdentityAdmin: Boolean(principal.isIdentityAdmin),
    canWriteAccounting: canWriteAccounting(principal),
    canExportExcel: true
  };
}

export function appSessionFromPrincipal(principal) {
  return {
    employee_id: String(principal.employeeId),
    employee_no: String(principal.employeeNo),
    employee_name: String(principal.displayName || ''),
    role: String(principal.workspaceRole),
    credential_version: Number(principal.credentialVersion || 0),
    employee_revision: Number(principal.employeeRevision || 0)
  };
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
  if (payload?.passwordChangeRequired || payload?.firstLogin) {
    return failure(403, 'FIRST_LOGIN_REQUIRED');
  }

  const principal = normalizePrincipal(payload?.principal);
  const token = String(payload?.session?.token || '');
  const expiresAt = String(payload?.session?.expiresAt || '');
  if (!principal || principal.workspaceId !== config.workspaceId || !SESSION_TOKEN_PATTERN.test(token) || !validFutureTimestamp(expiresAt)) {
    return failure(502, 'IDENTITY_INVALID_RESPONSE');
  }

  return {
    ok: true,
    principal,
    user: appUserFromPrincipal(principal),
    token,
    expiresAt,
    cookie: providerSessionCookie(token, expiresAt)
  };
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

  return {
    ok: true,
    principal,
    user: appUserFromPrincipal(principal),
    session: appSessionFromPrincipal(principal),
    expiresAt: String(payload?.session?.expiresAt || '')
  };
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

  return { ok: true, cookie: clearProviderSessionCookie() };
}

export async function startPasswordRecovery(request, env, employeeNo) {
  const config = identityRuntimeConfig(env);
  if (!config.ready) return failure(503, 'IDENTITY_NOT_CONFIGURED');
  if (!/^\d{4}$/.test(String(employeeNo || '').trim())) {
    return failure(400, 'INVALID_PASSWORD_RECOVERY');
  }

  const response = await identityFetch(request, env, '/v1/identity/password-recovery/start', {
    body: { workspaceId: config.workspaceId, employeeNo: String(employeeNo).trim() }
  });
  const payload = await response.json().catch(() => ({}));
  if (!response.ok) {
    return failure(response.status, String(payload?.error?.code || 'PASSWORD_RECOVERY_FAILED'), response);
  }

  const recovery = payload?.recovery;
  if (!recovery?.challengeId || !recovery?.expiresAt || !recovery?.resendAfter) {
    return failure(502, 'IDENTITY_INVALID_RESPONSE');
  }

  return {
    ok: true,
    recovery: {
      challengeId: String(recovery.challengeId),
      expiresAt: String(recovery.expiresAt),
      resendAfter: String(recovery.resendAfter)
    }
  };
}

export async function confirmPasswordRecovery(request, env, input) {
  const config = identityRuntimeConfig(env);
  if (!config.ready) return failure(503, 'IDENTITY_NOT_CONFIGURED');

  const employeeNo = String(input?.employeeNo || '').trim();
  const challengeId = String(input?.challengeId || '').trim();
  const code = String(input?.code || '').trim();
  const newPassword = input?.newPassword;
  if (!/^\d{4}$/.test(employeeNo) || !challengeId || !/^\d{6}$/.test(code) || !passwordWithinCyidBounds(newPassword)) {
    return failure(400, 'INVALID_PASSWORD_RECOVERY');
  }

  const response = await identityFetch(request, env, '/v1/identity/password-recovery/confirm', {
    body: {
      workspaceId: config.workspaceId,
      employeeNo,
      challengeId,
      code,
      newPassword
    }
  });
  const payload = await response.json().catch(() => ({}));
  if (!response.ok) {
    return failure(response.status, String(payload?.error?.code || 'PASSWORD_RECOVERY_FAILED'), response);
  }
  return { ok: true };
}

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

  const providerRequest = new Request(`https://cyid.internal${path}`, {
    method: 'POST',
    headers,
    body: options.body === undefined ? undefined : JSON.stringify(options.body)
  });

  const timeoutMs = identityProviderTimeoutMs(env);
  let timeoutId;
  const timeout = new Promise(resolve => {
    timeoutId = setTimeout(() => resolve(new Response(JSON.stringify({
      error: { code: 'IDENTITY_TIMEOUT', message: 'Identity provider did not respond in time.' }
    }), {
      status: 504,
      headers: { 'content-type': 'application/json; charset=utf-8' }
    })), timeoutMs);
  });

  try {
    return await Promise.race([env.IDENTITY.fetch(providerRequest), timeout]);
  } finally {
    if (timeoutId !== undefined) clearTimeout(timeoutId);
  }
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
    const key = part.slice(0, index).trim();
    if (key !== name) continue;
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
  return {
    ok: false,
    status: Number(status) || 500,
    code,
    retryAfter: response?.headers?.get?.('retry-after') || null
  };
}
