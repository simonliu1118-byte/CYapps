import coreWorker from './index.js';
import { handleV11Api } from './v11-tools.js';
import { handleV12Api } from './v12-tools.js';
import { handleV13Api } from './v13-export.js';
import { handleV15Api } from './v15-import.js';
import { handleV16Api, handleV16OAuthCallback, runScheduledBackup } from './v16-backup.js';
import {
  canWriteAccounting,
  clearProviderSessionCookie,
  confirmPasswordRecovery,
  loginWithCyid,
  logoutCyid,
  resolveIdentitySession,
  startPasswordRecovery
} from './identity-adapter.js';

function shouldDisableBrowserCache(pathname) {
  return pathname === '/' || pathname === '/login' || pathname.endsWith('.html') || pathname.endsWith('.js');
}

function requestUsesInsecureTransport(request, url) {
  if (url.protocol === 'http:') return true;
  if (String(request.headers.get('x-forwarded-proto') || '').toLowerCase() === 'http') return true;
  const visitor = String(request.headers.get('cf-visitor') || '');
  return /"scheme"\s*:\s*"http"/i.test(visitor);
}

function enforceHttps(request, url) {
  if (!requestUsesInsecureTransport(request, url)) return null;
  const secureUrl = new URL(url.toString());
  secureUrl.protocol = 'https:';
  return new Response(null, {
    status: 308,
    headers: {
      location: secureUrl.toString(),
      'cache-control': 'no-store'
    }
  });
}

export default {
  async fetch(request, env) {
    const url = new URL(request.url);

    try {
      const httpsRedirect = enforceHttps(request, url);
      if (httpsRedirect) return httpsRedirect;
      if (url.pathname === '/login.html' && request.method === 'GET') {
        return redirect('/login' + url.search);
      }

      if (url.pathname === '/login' && request.method === 'GET') {
        const resolved = await resolveIdentitySession(request, env);
        if (resolved.ok) return redirect('/');
        return fetchAsset(request, env, '/login.html');
      }

      if (url.pathname === '/login' && request.method === 'POST') {
        return handleNavigationLogin(request, env);
      }

      if ((url.pathname === '/' || url.pathname === '/index.html') && request.method === 'GET') {
        const resolved = await resolveIdentitySession(request, env);
        if (!resolved.ok) {
          const error = resolved.status >= 500 ? '?error=service' : '';
          return redirect('/login' + error);
        }
        return fetchAsset(request, env, '/index.html');
      }

      if (!url.pathname.startsWith('/api/')) {
        const response = await env.ASSETS.fetch(request);
        return shouldDisableBrowserCache(url.pathname) ? noCache(response) : response;
      }

      if (url.pathname === '/api/health' && request.method === 'GET') {
        return coreWorker.fetch(request, env);
      }

      if (url.pathname === '/api/auth/password-recovery/start' && request.method === 'POST') {
        return handlePasswordRecoveryStart(request, env);
      }
      if (url.pathname === '/api/auth/password-recovery/confirm' && request.method === 'POST') {
        return handlePasswordRecoveryConfirm(request, env);
      }
      if (url.pathname === '/api/auth/logout' && request.method === 'POST') {
        return handleLogout(request, env);
      }
      if (url.pathname === '/api/auth/me' && request.method === 'GET') {
        return handleMe(request, env);
      }

      if (!env.DB) {
        return json({ ok: false, error: 'D1 尚未綁定。', code: 'DB_NOT_CONFIGURED' }, 503);
      }

      const resolved = await resolveIdentitySession(request, env);
      if (!resolved.ok) return authFailure(resolved);

      if (!canWriteAccounting(resolved.principal) && !['GET', 'HEAD'].includes(request.method)) {
        return json({
          ok: false,
          error: '此帳號為唯讀權限，只能檢視資料與匯出 Excel。',
          code: 'READ_ONLY_USER'
        }, 403);
      }

      const v16Callback = await handleV16OAuthCallback(request, env);
      if (v16Callback) return v16Callback;

      const v16Response = await handleV16Api(request, env, resolved.session);
      if (v16Response) return v16Response;

      const v15Response = await handleV15Api(request, env);
      if (v15Response) return v15Response;

      const v13Response = await handleV13Api(request, env);
      if (v13Response) return v13Response;

      const v12Response = await handleV12Api(request, env);
      if (v12Response) return v12Response;

      const v11Response = await handleV11Api(request, env);
      if (v11Response) return v11Response;

      return coreWorker.fetch(request, env);
    } catch (error) {
      console.error('cyaccounting_request_failed', error instanceof Error ? error.message : 'unknown_error');
      return json({ ok: false, error: '系統處理失敗，請稍後再試。' }, 500);
    }
  },

  async scheduled(_controller, env, ctx) {
    ctx.waitUntil(runScheduledBackup(env));
  }
};

async function handleNavigationLogin(request, env) {
  const form = await request.formData().catch(() => null);
  const employeeNo = String(form?.get('employeeNo') || '').trim();
  const password = typeof form?.get('password') === 'string' ? form.get('password') : '';
  const result = await loginWithCyid(request, env, employeeNo, password);

  if (!result.ok) {
    const error = loginErrorKey(result);
    return redirect('/login?error=' + encodeURIComponent(error));
  }

  return redirect('/', { 'set-cookie': result.cookie });
}

async function handlePasswordRecoveryStart(request, env) {
  const body = await request.json().catch(() => null);
  const result = await startPasswordRecovery(request, env, body?.employeeNo);
  if (!result.ok) return recoveryFailure(result);

  return json({
    ok: true,
    recovery: result.recovery,
    message: '若帳號符合條件，驗證碼已寄至登記且已驗證的 Email。'
  }, 202);
}

async function handlePasswordRecoveryConfirm(request, env) {
  const body = await request.json().catch(() => null);
  const result = await confirmPasswordRecovery(request, env, {
    employeeNo: body?.employeeNo,
    challengeId: body?.challengeId,
    code: body?.code,
    newPassword: body?.newPassword
  });
  if (!result.ok) return recoveryFailure(result);
  return json({ ok: true, passwordReset: true });
}

async function handleLogout(request, env) {
  const result = await logoutCyid(request, env);
  return json({
    ok: true,
    providerRevoked: Boolean(result.ok)
  }, 200, {
    'set-cookie': clearProviderSessionCookie()
  });
}

async function handleMe(request, env) {
  const result = await resolveIdentitySession(request, env);
  if (!result.ok) return authFailure(result);
  return json({ ok: true, user: result.user });
}

function loginErrorKey(result) {
  if (result.code === 'FIRST_LOGIN_REQUIRED') return 'first-login';
  if (result.status === 403 || result.code === 'APPLICATION_ACCESS_DENIED') return 'access';
  if (result.status === 429) return 'rate';
  if (result.status >= 500) return 'service';
  if (result.status === 400) return 'invalid';
  return 'failed';
}

function recoveryFailure(result) {
  if (result.status === 429) {
    const headers = result.retryAfter ? { 'retry-after': result.retryAfter } : {};
    return json({ ok: false, error: '要求驗證碼或重設密碼的次數過多，請稍後再試。', code: result.code }, 429, headers);
  }
  if (result.status >= 500) {
    return json({ ok: false, error: '帳號服務目前無法處理密碼重設。', code: result.code }, 503);
  }
  return json({ ok: false, error: '驗證資料或新密碼格式不正確。', code: result.code }, result.status || 400);
}

function authFailure(result) {
  const status = result.status >= 500 ? 503 : 401;
  const headers = status === 401 ? { 'set-cookie': clearProviderSessionCookie() } : {};
  return json({
    ok: false,
    error: status === 401 ? '尚未登入。' : '中央帳號服務目前無法驗證登入狀態。',
    code: status === 401 ? 'AUTH_REQUIRED' : 'IDENTITY_UNAVAILABLE'
  }, status, headers);
}

async function fetchAsset(request, env, pathname) {
  const url = new URL(request.url);
  url.pathname = pathname;
  url.search = '';
  const assetRequest = new Request(url.toString(), {
    method: 'GET',
    headers: request.headers
  });
  return noCache(await env.ASSETS.fetch(assetRequest));
}

function noCache(response) {
  const headers = new Headers(response.headers);
  headers.set('cache-control', 'no-store, no-cache, must-revalidate, max-age=0');
  headers.set('pragma', 'no-cache');
  headers.set('strict-transport-security', 'max-age=31536000');
  return new Response(response.body, {
    status: response.status,
    statusText: response.statusText,
    headers
  });
}

function redirect(location, extraHeaders = {}) {
  return new Response(null, {
    status: 303,
    headers: {
      location,
      'cache-control': 'no-store',
      'strict-transport-security': 'max-age=31536000',
      ...extraHeaders
    }
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
