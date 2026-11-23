import { cyWebPortalUrl, identityRuntimeConfig, redeemSsoHandoff } from './identity-adapter.js';

/**
 * CY Web portal login for CYAccountingWeb (SSO_PORTAL_DESIGN section 6).
 *
 *   GET /auth/sso/start       builds a state, stores it (with the safe return path) in a short-lived cookie and
 *                             sends the browser to the CY Web portal.
 *   GET /auth/sso/callback    checks state, redeems the single-use code through the private IDENTITY binding,
 *                             sets the unchanged CYACC Session cookie and returns to the saved path.
 *   GET /auth/sso/signed-out  static "signed out" page; never redirects by itself.
 *
 * The code, the state and Session tokens are never logged. Every response here is no-store / no-referrer.
 */

export const SSO_STATE_COOKIE = '__Host-cyacc_sso_state';
export const SSO_START_PATH = '/auth/sso/start';
export const SSO_CALLBACK_PATH = '/auth/sso/callback';
export const SSO_SIGNED_OUT_PATH = '/auth/sso/signed-out';

const STATE_MAX_AGE_SECONDS = 600;
const STATE_PATTERN = /^[A-Za-z0-9_-]{43}$/;
const RETURN_PATH_MAX_LENGTH = 512;
const BLOCKED_RETURN_PREFIXES = ['/auth/', '/api/', '/login'];

/** Same-origin relative path only; anything else becomes '/'. */
export function safeReturnPath(value) {
  if (typeof value !== 'string' || !value || value.length > RETURN_PATH_MAX_LENGTH) return '/';
  if (!value.startsWith('/') || value.startsWith('//')) return '/';
  if (/[\\\u0000-\u001f\u007f\s]/.test(value)) return '/';
  let parsed;
  try {
    parsed = new URL(value, 'https://sso-path.invalid');
  } catch {
    return '/';
  }
  if (parsed.origin !== 'https://sso-path.invalid') return '/';
  const path = parsed.pathname + parsed.search;
  if (!path.startsWith('/') || path.startsWith('//')) return '/';
  if (BLOCKED_RETURN_PREFIXES.some(prefix => parsed.pathname === prefix.replace(/\/$/, '') || parsed.pathname.startsWith(prefix))) return '/';
  return path;
}

export function startSsoLogin(env, requestedPath = '/') {
  const config = identityRuntimeConfig(env);
  const portal = cyWebPortalUrl(env);
  if (!config.ready || !portal) {
    return errorPage(503, '帳號服務尚未設定完成，請聯絡系統管理員。');
  }

  const state = base64url(crypto.getRandomValues(new Uint8Array(32)));
  const target = new URL('/sso', portal);
  target.searchParams.set('app', config.applicationId);
  target.searchParams.set('state', state);

  const headers = baseHeaders({ location: target.toString() });
  headers.append('set-cookie', stateCookie(`${state}.${base64url(new TextEncoder().encode(safeReturnPath(requestedPath)))}`));
  return new Response(null, { status: 303, headers });
}

export function ssoServiceUnavailablePage() {
  return errorPage(503, '中央帳號服務目前無法使用，請稍後再試。');
}

export async function handleSsoRoute(request, env, url) {
  if (request.method !== 'GET' && request.method !== 'HEAD') {
    return new Response('Method Not Allowed', { status: 405, headers: baseHeaders({ allow: 'GET, HEAD', 'content-type': 'text/plain; charset=utf-8' }) });
  }
  if (url.pathname === SSO_START_PATH) return startSsoLogin(env, url.searchParams.get('next') || '/');
  if (url.pathname === SSO_CALLBACK_PATH) return handleCallback(request, env, url);
  if (url.pathname === SSO_SIGNED_OUT_PATH) return signedOutPage();
  return new Response('Not Found', { status: 404, headers: baseHeaders({ 'content-type': 'text/plain; charset=utf-8' }) });
}

async function handleCallback(request, env, url) {
  const stored = parseStateCookie(cookieValue(request, SSO_STATE_COOKIE));
  const queryState = url.searchParams.get('state') || '';
  const code = url.searchParams.get('code') || '';

  // State is single use: every outcome below deletes the cookie.
  if (!stored || !STATE_PATTERN.test(queryState) || !sameValue(queryState, stored.state) || !code) {
    return errorPage(400, '登入驗證已失效或不完整，請重新登入。', { clearState: true });
  }

  const result = await redeemSsoHandoff(request, env, code);
  if (!result.ok) {
    if (result.status === 429) return errorPage(429, '登入嘗試次數過多，請稍後再重新登入。', { clearState: true, retryAfter: result.retryAfter });
    if (result.status >= 500) return errorPage(503, '中央帳號服務目前無法使用，請稍後再重新登入。', { clearState: true });
    return errorPage(401, '登入驗證已失效，請重新登入。', { clearState: true });
  }

  const headers = baseHeaders({ location: stored.next });
  headers.append('set-cookie', clearStateCookie());
  headers.append('set-cookie', result.cookie);
  return new Response(null, { status: 303, headers });
}

function signedOutPage() {
  return htmlPage(200, '已登出', '您已登出志遠記帳系統。');
}

function errorPage(status, message, options = {}) {
  const extra = {};
  if (options.retryAfter) extra['retry-after'] = options.retryAfter;
  const response = htmlPage(status, '無法登入', message, extra);
  if (options.clearState) response.headers.append('set-cookie', clearStateCookie());
  return response;
}

function htmlPage(status, heading, message, extraHeaders = {}) {
  const html = `<!doctype html><html lang="zh-Hant"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1"><meta name="referrer" content="no-referrer"><meta name="color-scheme" content="light"><title>志遠記帳系統</title><link rel="icon" href="/cyacc-icon.svg" type="image/svg+xml"><style>body{margin:0;min-height:100vh;display:flex;align-items:center;justify-content:center;background:#f4f6f8;font-family:system-ui,-apple-system,"Noto Sans TC","Microsoft JhengHei",sans-serif;color:#1f2933}main{box-sizing:border-box;width:min(92vw,380px);padding:28px 24px;border-radius:12px;background:#fff;box-shadow:0 2px 12px rgba(0,0,0,.08);text-align:center}.heading{display:flex;align-items:center;justify-content:center;gap:12px;margin-bottom:12px}.heading img{width:40px;height:40px;display:block}h1{margin:0;font-size:1.25rem;font-weight:700}p{margin:0 0 20px;line-height:1.6}a.button{display:inline-block;min-height:44px;box-sizing:border-box;padding:11px 24px;border-radius:8px;background:#2f6f5e;color:#fff;font-size:1rem;text-decoration:none}</style></head><body><main><div class="heading"><img src="/cyacc-icon.svg" alt=""><h1>${heading}</h1></div><p>${message}</p><a class="button" href="${SSO_START_PATH}">重新登入</a></main></body></html>`;
  return new Response(html, { status, headers: baseHeaders({ 'content-type': 'text/html; charset=utf-8', ...extraHeaders }) });
}

function baseHeaders(extra = {}) {
  return new Headers({
    'cache-control': 'no-store',
    'referrer-policy': 'no-referrer',
    'strict-transport-security': 'max-age=31536000',
    'x-content-type-options': 'nosniff',
    ...extra
  });
}

function stateCookie(value) {
  return `${SSO_STATE_COOKIE}=${value}; Path=/; HttpOnly; Secure; SameSite=Lax; Max-Age=${STATE_MAX_AGE_SECONDS}`;
}

function clearStateCookie() {
  return `${SSO_STATE_COOKIE}=; Path=/; HttpOnly; Secure; SameSite=Lax; Max-Age=0`;
}

function parseStateCookie(raw) {
  if (!raw) return null;
  const dot = raw.indexOf('.');
  if (dot < 0) return null;
  const state = raw.slice(0, dot);
  if (!STATE_PATTERN.test(state)) return null;
  let next = '/';
  try {
    const encoded = raw.slice(dot + 1);
    if (!/^[A-Za-z0-9_-]*$/.test(encoded)) return null;
    next = safeReturnPath(new TextDecoder().decode(fromBase64url(encoded)));
  } catch {
    next = '/';
  }
  return { state, next };
}

function cookieValue(request, name) {
  const raw = request.headers.get('cookie') || '';
  for (const part of raw.split(';')) {
    const index = part.indexOf('=');
    if (index < 0) continue;
    if (part.slice(0, index).trim() === name) return part.slice(index + 1).trim();
  }
  return null;
}

function sameValue(a, b) {
  if (a.length !== b.length) return false;
  let diff = 0;
  for (let i = 0; i < a.length; i++) diff |= a.charCodeAt(i) ^ b.charCodeAt(i);
  return diff === 0;
}

function base64url(bytes) {
  let binary = '';
  for (const value of bytes) binary += String.fromCharCode(value);
  return btoa(binary).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '');
}

function fromBase64url(value) {
  const padded = value.replace(/-/g, '+').replace(/_/g, '/') + '==='.slice((value.length + 3) % 4);
  const binary = atob(padded);
  return Uint8Array.from(binary, char => char.charCodeAt(0));
}
