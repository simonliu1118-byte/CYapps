import {
  clearProviderSessionCookie,
  cyWebPortalUrl,
  loginWithCyid,
  logoutCyid,
  resolveIdentitySession
} from './identity.js';
import {handleAuditApi} from './audit.js';
import {handlePendingApi} from './pending.js';
import {handleSearchApi} from './search.js';
import {handleClassificationsApi} from './classifications.js';
import {handleTrackingApi} from './tracking.js';
import {runScheduled} from './tracking-run.js';

const LOCAL_HOSTS = new Set(['localhost', '127.0.0.1', '[::1]']);

export default {
  async fetch(request, env) {
    const url = new URL(request.url);
    if (url.protocol === 'http:' && !LOCAL_HOSTS.has(url.hostname)) {
      url.protocol = 'https:';
      return new Response(null, {status: 308, headers: {location: url.toString(), 'cache-control': 'no-store'}});
    }
    const {pathname} = url;
    const method = request.method;

    if (pathname === '/login.html' && method === 'GET') return redirect('/login' + url.search);

    if (pathname === '/login' && method === 'GET') {
      if ((await resolveIdentitySession(request, env)).ok) return redirect('/');
      return asset(request, env, '/login.html');
    }

    if ((pathname === '/' || pathname === '/index.html') && method === 'GET') {
      const resolved = await resolveIdentitySession(request, env);
      if (!resolved.ok) return redirect(resolved.status >= 500 ? '/login?error=service' : '/login', rejectedCookie(resolved));
      return asset(request, env, '/index.html');
    }

    if (!pathname.startsWith('/api/')) return noCache(await env.ASSETS.fetch(request));

    if (pathname === '/api/auth/login' && method === 'POST') return handleLogin(request, env);
    if (pathname === '/api/auth/logout' && method === 'POST') return handleLogout(request, env);
    if (pathname === '/api/auth/portal' && method === 'GET') {
      const portal = cyWebPortalUrl(env);
      return portal ? json({url: portal}) : json({error: 'CY Web 入口尚未設定'}, 503);
    }

    const resolved = await resolveIdentitySession(request, env);
    if (!resolved.ok) return authFailure(resolved);
    const principal = resolved.principal;

    if (pathname === '/api/me' && method === 'GET') {
      return json({employeeNo: principal.employeeNo, displayName: principal.displayName, workspaceRole: principal.workspaceRole});
    }
    const response = await handleAuditApi(request, env, url)
      || await handleSearchApi(request, env, url)
      || await handleClassificationsApi(request, env, url, principal)
      || await handleTrackingApi(request, env, url, principal)
      || await handlePendingApi(request, env, url);
    return response || json({error: '找不到此功能'}, 404);
  },

  // Cron */10: runs at most one due tracked product (see tracking-run.js).
  async scheduled(event, env, ctx) {
    ctx.waitUntil(runScheduled(env));
  }
};

async function handleLogin(request, env) {
  const form = await request.formData().catch(() => null);
  const employeeNo = String(form?.get('employeeNo') || '').trim();
  const password = typeof form?.get('password') === 'string' ? form.get('password') : '';
  const result = await loginWithCyid(request, env, employeeNo, password);
  if (!result.ok) return redirect('/login?error=' + loginErrorKey(result));
  return redirect('/', {'set-cookie': result.cookie});
}

async function handleLogout(request, env) {
  await logoutCyid(request, env);
  return redirect('/login', {'set-cookie': clearProviderSessionCookie()});
}

function loginErrorKey(result) {
  if (result.code === 'FIRST_LOGIN_REQUIRED') return 'first-login';
  if (result.status === 403 || result.code === 'APPLICATION_ACCESS_DENIED') return 'access';
  if (result.status === 429) return 'rate';
  if (result.status >= 500) return 'service';
  if (result.status === 400) return 'invalid';
  return 'failed';
}

// A provider outage is not a rejection: keep the cookie and fail closed with 503.
const rejectedCookie = resolved => resolved.status >= 500 ? {} : {'set-cookie': clearProviderSessionCookie()};

function authFailure(resolved) {
  if (resolved.status >= 500) return json({error: '中央帳號服務目前無法驗證登入狀態。', code: 'IDENTITY_UNAVAILABLE'}, 503);
  return json({error: '尚未登入。', code: 'AUTH_REQUIRED'}, 401, rejectedCookie(resolved));
}

async function asset(request, env, pathname) {
  const url = new URL(request.url);
  url.pathname = pathname;
  url.search = '';
  return noCache(await env.ASSETS.fetch(new Request(url.toString(), {method: 'GET', headers: request.headers})));
}

function noCache(response) {
  const headers = new Headers(response.headers);
  headers.set('cache-control', 'no-store');
  headers.set('strict-transport-security', 'max-age=31536000');
  return new Response(response.body, {status: response.status, statusText: response.statusText, headers});
}

function redirect(location, extraHeaders = {}) {
  return new Response(null, {
    status: 302,
    headers: {location, 'cache-control': 'no-store', 'strict-transport-security': 'max-age=31536000', ...extraHeaders}
  });
}

function json(data, status = 200, extraHeaders = {}) {
  return new Response(JSON.stringify(data), {
    status,
    headers: {
      'content-type': 'application/json; charset=utf-8',
      'cache-control': 'no-store',
      'strict-transport-security': 'max-age=31536000',
      'x-content-type-options': 'nosniff',
      ...extraHeaders
    }
  });
}
