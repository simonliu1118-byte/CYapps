import {clearProviderSessionCookie, logoutCyid, resolveIdentitySession} from './identity.js';
import {SIGNED_OUT_PATH, SSO_CALLBACK_PATH, SSO_START_PATH, errorPage, handleSsoCallback, safeReturnPath, signedOutPage, startSso} from './sso.js';
import {handleAuditApi} from './audit.js';
import {handlePendingApi} from './pending.js';
import {handleSearchApi} from './search.js';
import {handleClassificationsApi} from './classifications.js';
import {handleTrackingApi} from './tracking.js';
import {runScheduled} from './tracking-run.js';

import {handleGrokApi} from './grok-api.js';
import {handleGrokTasks} from './grok-tasks.js';
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

    // Sign-in goes through CY Web only (src/sso.js); this App has no login page of its own.
    if (method === 'GET' && pathname === SSO_CALLBACK_PATH) return handleSsoCallback(request, env, url);
    if (method === 'GET' && pathname === SSO_START_PATH) return startSso(env, safeReturnPath(url.searchParams.get('next')));
    if (method === 'GET' && pathname === SIGNED_OUT_PATH) return signedOutPage();

    if ((pathname === '/' || pathname === '/index.html') && method === 'GET') {
      const resolved = await resolveIdentitySession(request, env);
      if (!resolved.ok) {
        if (resolved.status >= 500) return errorPage(503, '中央帳號服務目前無法驗證登入狀態，請稍後再試。');
        return startSso(env, '/', [clearProviderSessionCookie()]);
      }
      return asset(request, env, '/index.html');
    }

    if (!pathname.startsWith('/api/')) return noCache(await env.ASSETS.fetch(request));

    if (pathname.startsWith('/api/grok/tasks/')) return handleGrokApi(request,env,url);
    if (pathname === '/api/auth/logout' && method === 'POST') return handleLogout(request, env);

    const resolved = await resolveIdentitySession(request, env);
    if (!resolved.ok) return authFailure(resolved);
    const principal = resolved.principal;

    if (pathname === '/api/me' && method === 'GET') {
      return json({employeeNo: principal.employeeNo, displayName: principal.displayName, workspaceRole: principal.workspaceRole});
    }
    const response = await handleGrokTasks(request,env,url,principal)
      || await handleAuditApi(request, env, url)
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

async function handleLogout(request, env) {
  await logoutCyid(request, env);
  return redirect(SIGNED_OUT_PATH, {'set-cookie': clearProviderSessionCookie()});
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
    status: 303,
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
