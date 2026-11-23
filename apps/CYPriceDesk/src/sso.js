// Sign-in through the CY Web portal (CONSUMER_INTEGRATION_STANDARD section 21.3). This App has no login form of its own:
// an unauthenticated page request is sent to CY Web with a one-time `state`, and CY Web sends the browser back to
// /auth/sso/callback with a short-lived code that this Worker redeems through its private IDENTITY binding.
import {cookieValue, cyWebPortalUrl, identityRuntimeConfig, redeemSsoHandoff} from './identity.js';

export const SSO_STATE_COOKIE = '__Host-cypd_sso_state';
export const SSO_CALLBACK_PATH = '/auth/sso/callback';
export const SSO_START_PATH = '/auth/sso/start';
export const SIGNED_OUT_PATH = '/auth/signed-out';

const STATE_MAX_AGE_SECONDS = 600;
const STATE_PATTERN = /^[A-Za-z0-9_-]{43}$/;
const CODE_PATTERN = /^[A-Za-z0-9_-]{43}$/;
const MAX_RETURN_PATH_LENGTH = 512;

/** Only same-origin relative paths are kept; everything else (and the auth/API routes themselves) falls back to `/`. */
export function safeReturnPath(value) {
  if (typeof value !== 'string' || !value.startsWith('/') || value.startsWith('//') || value.length > MAX_RETURN_PATH_LENGTH) return '/';
  if (/[\\\u0000-\u001f\u007f]/.test(value)) return '/';
  let parsed;
  try { parsed = new URL(value, 'https://return.invalid'); } catch { return '/'; }
  if (parsed.origin !== 'https://return.invalid') return '/';
  const path = parsed.pathname + parsed.search;
  if (path.startsWith('//') || parsed.pathname === '/index.html' || parsed.pathname.startsWith('/auth/') || parsed.pathname.startsWith('/api/')) return '/';
  return path;
}

/** Redirect the browser to CY Web, remembering the intended path. `extraCookies` are appended unchanged. */
export function startSso(env, returnPath = '/', extraCookies = []) {
  const config = identityRuntimeConfig(env);
  const portal = cyWebPortalUrl(env);
  if (!config.ready || !portal) return errorPage(503, '登入服務尚未設定完成，請稍後再試。');

  const state = base64url(crypto.getRandomValues(new Uint8Array(32)));
  const target = new URL('/sso', portal);
  target.searchParams.set('app', config.applicationId);
  target.searchParams.set('state', state);
  const headers = ssoHeaders({location: target.toString()});
  headers.append('set-cookie', stateCookie(`${state}.${base64url(new TextEncoder().encode(safeReturnPath(returnPath)))}`, STATE_MAX_AGE_SECONDS));
  for (const cookie of extraCookies) headers.append('set-cookie', cookie);
  return new Response(null, {status: 303, headers});
}

export async function handleSsoCallback(request, env, url) {
  const clear = stateCookie('', 0);
  const stored = parseStateCookie(cookieValue(request, SSO_STATE_COOKIE));
  const queryState = url.searchParams.get('state') || '';
  const code = url.searchParams.get('code') || '';

  // Missing, mismatched or expired state: never redeem.
  if (!stored || !STATE_PATTERN.test(queryState) || !sameValue(queryState, stored.state)) {
    return errorPage(400, '登入連結已失效，請重新登入。', [clear]);
  }
  if (!CODE_PATTERN.test(code)) return errorPage(401, '登入連結已失效，請重新登入。', [clear]);

  const result = await redeemSsoHandoff(request, env, code);
  if (!result.ok) {
    if (result.status === 429) return errorPage(429, '嘗試登入次數過多，請稍候一分鐘後再重新登入。', [clear]);
    if (result.status === 401) return errorPage(401, '登入連結已失效，請重新登入。', [clear]);
    return errorPage(result.status >= 500 ? 503 : 502, '中央帳號服務目前無法使用，請稍後再重新登入。', [clear]);
  }

  const headers = ssoHeaders({location: stored.returnPath});
  headers.append('set-cookie', clear);
  headers.append('set-cookie', result.cookie);
  return new Response(null, {status: 303, headers});
}

export function signedOutPage() {
  return htmlPage(200, '已登出', '您已登出價格觀測室。', true);
}

export function errorPage(status, message, extraCookies = []) {
  return htmlPage(status, '無法登入', message, true, extraCookies);
}

function htmlPage(status, heading, message, withSignInButton, extraCookies = []) {
  const button = withSignInButton ? `<a class="primary" href="${SSO_START_PATH}">重新登入</a>` : '';
  const html = `<!doctype html>
<html lang="zh-Hant"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1">
<meta name="referrer" content="no-referrer"><meta name="robots" content="noindex,nofollow"><meta name="theme-color" content="#0f5e73">
<title>${heading} · 價格觀測室</title><link rel="icon" type="image/svg+xml" href="/icon.svg">
<style>
:root{--bg:#f6f6f9;--surface:#fff;--border:#e3e3ec;--text:#282a3a;--muted:#676b7e;--accent:#0f5e73;--accent-hover:#0b4b5c;
font-family:"Noto Sans TC","PingFang TC","Microsoft JhengHei",system-ui,sans-serif;color:var(--text)}
*{box-sizing:border-box}body{margin:0;background:var(--bg);font-size:14px;line-height:1.5}
.screen{min-height:100dvh;display:grid;place-items:center;padding:24px 16px}
.card{width:min(400px,100%);padding:32px;border:1px solid var(--border);border-radius:14px;background:var(--surface)}
.heading{display:flex;align-items:center;gap:12px;margin-bottom:16px}.heading img{width:40px;height:40px;display:block}
h1{margin:0;font-size:22px;font-weight:400}p{margin:0 0 20px;color:var(--muted)}
.primary{display:block;width:100%;min-height:42px;padding:8px 16px;border-radius:8px;background:var(--accent);color:#fff;font-size:16px;font-weight:600;text-align:center;text-decoration:none}
.primary:hover{background:var(--accent-hover)}
</style></head><body><main class="screen"><section class="card">
<div class="heading"><img src="/icon.svg" alt=""><h1>${heading}</h1></div>
<p>${message}</p>${button}
</section></main></body></html>`;
  const headers = ssoHeaders({'content-type': 'text/html; charset=utf-8'});
  for (const cookie of extraCookies) headers.append('set-cookie', cookie);
  return new Response(html, {status, headers});
}

function ssoHeaders(initial) {
  return new Headers({
    'cache-control': 'no-store',
    'referrer-policy': 'no-referrer',
    'strict-transport-security': 'max-age=31536000',
    'x-content-type-options': 'nosniff',
    'content-security-policy': "default-src 'none'; style-src 'unsafe-inline'; img-src 'self'; base-uri 'none'; form-action 'none'; frame-ancestors 'none'",
    ...initial
  });
}

function stateCookie(value, maxAge) {
  return `${SSO_STATE_COOKIE}=${value}; Path=/; HttpOnly; Secure; SameSite=Lax; Max-Age=${maxAge}`;
}

function parseStateCookie(raw) {
  if (typeof raw !== 'string') return null;
  const [state, encodedPath, ...rest] = raw.split('.');
  if (rest.length || !STATE_PATTERN.test(state || '')) return null;
  let returnPath = '/';
  try { returnPath = safeReturnPath(new TextDecoder().decode(fromBase64url(encodedPath || ''))); } catch { /* falls back to / */ }
  return {state, returnPath};
}

function base64url(bytes) {
  let binary = '';
  for (const value of bytes) binary += String.fromCharCode(value);
  return btoa(binary).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '');
}

function fromBase64url(text) {
  const binary = atob(text.replace(/-/g, '+').replace(/_/g, '/'));
  return Uint8Array.from(binary, c => c.charCodeAt(0));
}

function sameValue(a, b) {
  if (a.length !== b.length) return false;
  let diff = 0;
  for (let i = 0; i < a.length; i++) diff |= a.charCodeAt(i) ^ b.charCodeAt(i);
  return diff === 0;
}
