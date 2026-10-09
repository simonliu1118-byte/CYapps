import coreWorker from './index.js';
import { handleAccountingToolsApi } from './accounting-tools.js';
import { handleAccountingControlsApi } from './accounting-controls.js';
import { handleAccountLifecycleApi } from './account-lifecycle.js';
import { handleOpeningBalanceApi } from './opening-balances.js';
import { handleExcelExportApi } from './excel-export.js';
import { handleExcelImportApi } from './excel-import.js';
import { handleBackupApi, runScheduledBackup } from './backup-service.js';
import { handleDesktopMigrationApi } from './desktop-migration.js';
import {
  canWriteAccounting,
  clearProviderSessionCookie,
  confirmPasswordRecovery,
  loginWithCyid,
  logoutCyid,
  resolveIdentitySession,
  startPasswordRecovery
} from './identity-adapter.js';

const SUMMARY_MAX_UNITS = 40;
const ACCOUNT_NAME_MAX_CHARS = 8;
const DEFAULT_ASSET_TIMEOUT_MS = 5_000;
const MIN_ASSET_TIMEOUT_MS = 100;
const MAX_ASSET_TIMEOUT_MS = 15_000;

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
        const response = await boundedAssetFetch(request, env);
        return shouldDisableBrowserCache(url.pathname) ? noCache(response) : response;
      }

      if (url.pathname === '/api/health' && request.method === 'GET') {
        return coreWorker.fetch(request, env);
      }

      if (url.pathname === '/api/auth/login' && request.method === 'POST') {
        return handleApiLogin(request, env);
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

      const mutationValidation = await validateMutationRequest(request, url);
      if (mutationValidation) return mutationValidation;

      if (url.pathname.startsWith('/api/backup/')) {
        const backupResponse = await handleBackupApi(request, env, resolved.session);
        return backupResponse || json({
          ok: false,
          error: '找不到此備份功能。',
          code: 'BACKUP_ROUTE_NOT_FOUND'
        }, 404);
      }

      if (url.pathname.startsWith('/api/migration/desktop/')) {
        const migrationResponse = await handleDesktopMigrationApi(request, env, resolved.session);
        return migrationResponse || json({
          ok: false,
          error: '找不到此資料移轉功能。',
          code: 'MIGRATION_ROUTE_NOT_FOUND'
        }, 404);
      }

      const importResponse = await handleExcelImportApi(request, env);
      if (importResponse) return importResponse;

      const exportResponse = await handleExcelExportApi(request, env);
      if (exportResponse) return exportResponse;

      const accountLifecycleResponse = await handleAccountLifecycleApi(request, env, resolved.principal);
      if (accountLifecycleResponse) return accountLifecycleResponse;

      const openingBalanceResponse = await handleOpeningBalanceApi(request, env, resolved.principal);
      if (openingBalanceResponse) return openingBalanceResponse;

      const controlsResponse = await handleAccountingControlsApi(request, env);
      if (controlsResponse) return controlsResponse;

      const toolsResponse = await handleAccountingToolsApi(request, env);
      if (toolsResponse) return toolsResponse;

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

async function validateMutationRequest(request, url) {
  const transactionWrite = isTransactionWrite(url.pathname, request.method);
  const accountWrite = isAccountWrite(url.pathname, request.method);
  if (!transactionWrite && !accountWrite) return null;

  const body = await request.clone().json().catch(() => null);

  if (transactionWrite && body && summaryWeightedUnits(body.summary) > SUMMARY_MAX_UNITS) {
    return json({
      ok: false,
      error: '摘要不可超過 20 個中文字或 40 個英數字元。',
      code: 'SUMMARY_TOO_LONG'
    }, 400);
  }

  if (accountWrite) {
    const name = normalizeAccountName(body?.name);
    if (!name) {
      return json({ ok: false, error: '帳戶名稱不可空白。', code: 'ACCOUNT_NAME_REQUIRED' }, 400);
    }
    if (Array.from(name).length > ACCOUNT_NAME_MAX_CHARS) {
      return json({ ok: false, error: '帳戶名稱最多 8 個字。', code: 'ACCOUNT_NAME_TOO_LONG' }, 400);
    }
  }

  return null;
}

function isTransactionWrite(pathname, method) {
  if (method === 'POST' && pathname === '/api/transactions') return true;
  return method === 'PUT' && /^\/api\/transactions\/\d+$/.test(pathname);
}

function isAccountWrite(pathname, method) {
  if (method === 'POST' && pathname === '/api/accounts') return true;
  return method === 'PUT' && /^\/api\/accounts\/\d+$/.test(pathname);
}

function summaryWeightedUnits(value) {
  let units = 0;
  for (const char of String(value ?? '').trim()) {
    const code = char.codePointAt(0) || 0;
    units += code <= 0x7f || (code >= 0xff61 && code <= 0xff9f) ? 1 : 2;
  }
  return units;
}

function normalizeAccountName(value) {
  return String(value ?? '').trim().replace(/\s+/g, ' ');
}

async function handleNavigationLogin(request, env) {
  const form = await request.formData().catch(() => null);
  const employeeNo = String(form?.get('employeeNo') || '').trim();
  const password = typeof form?.get('password') === 'string' ? form.get('password') : '';
  const result = await loginWithCyid(request, env, employeeNo, password);
  if (!result.ok) {
    const error = loginErrorKey(result);
    return redirect('/login?error=' + encodeURIComponent(error));
  }

  const verified = await verifyFreshLoginSession(request, env, result.token);
  if (!verified.ok) {
    const error = loginErrorKey(verified);
    return redirect('/login?error=' + encodeURIComponent(error));
  }

  return redirect('/', { 'set-cookie': result.cookie });
}

async function handleApiLogin(request, env) {
  const body = await request.json().catch(() => null);
  const employeeNo = String(body?.employeeNo || '').trim();
  const password = typeof body?.password === 'string' ? body.password : '';
  const result = await loginWithCyid(request, env, employeeNo, password);
  if (!result.ok) return apiLoginFailure(result);

  const verified = await verifyFreshLoginSession(request, env, result.token);
  if (!verified.ok) return apiLoginFailure(verified);

  return json({
    ok: true,
    user: verified.user
  }, 200, {
    'set-cookie': result.cookie
  });
}

async function verifyFreshLoginSession(request, env, token) {
  const headers = new Headers();
  const clientIp = request.headers.get('cf-connecting-ip');
  if (clientIp) headers.set('cf-connecting-ip', clientIp);
  const requestId = request.headers.get('x-request-id');
  if (requestId) headers.set('x-request-id', requestId);
  headers.set('cookie', `cyaccounting_session=${encodeURIComponent(String(token || ''))}`);
  const verifyRequest = new Request(request.url, { method: 'GET', headers });
  return resolveIdentitySession(verifyRequest, env);
}

function apiLoginFailure(result) {
  const key = loginErrorKey(result);
  const messages = {
    invalid: '請輸入正確的 4 碼員工編號與密碼。',
    failed: '員工編號或密碼不正確。',
    access: '此員工帳號沒有記帳系統 App Access。',
    rate: '登入嘗試次數過多，請稍後再試。',
    service: '中央帳號服務目前無法使用，請稍後再試。',
    'first-login': '請先至 CY Web 帳號管理入口完成首次帳號啟用與 Email 驗證。'
  };
  const status = result.status >= 500 ? 503 : Math.max(400, Number(result.status) || 401);
  return json({
    ok: false,
    error: messages[key] || messages.failed,
    code: String(result.code || 'LOGIN_FAILED')
  }, status);
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
  const response = noCache(await boundedAssetFetch(assetRequest, env));
  return pathname === '/login.html' ? secureLoginDocument(response, request) : response;
}

async function boundedAssetFetch(request, env) {
  if (!env?.ASSETS || typeof env.ASSETS.fetch !== 'function') {
    return new Response('介面資源服務未設定。', {
      status: 503,
      headers: { 'content-type': 'text/plain; charset=utf-8' }
    });
  }

  const timeoutMs = assetTimeoutMs(env);
  let timeoutId;
  const timeout = new Promise(resolve => {
    timeoutId = setTimeout(() => resolve(new Response('介面資源載入逾時，請重新整理後再試。', {
      status: 504,
      headers: { 'content-type': 'text/plain; charset=utf-8' }
    })), timeoutMs);
  });

  try {
    return await Promise.race([Promise.resolve().then(() => env.ASSETS.fetch(request)), timeout]);
  } finally {
    if (timeoutId !== undefined) clearTimeout(timeoutId);
  }
}

function assetTimeoutMs(env) {
  const configured = Number(env?.CYACC_ASSET_TIMEOUT_MS);
  if (!Number.isFinite(configured)) return DEFAULT_ASSET_TIMEOUT_MS;
  return Math.max(MIN_ASSET_TIMEOUT_MS, Math.min(Math.trunc(configured), MAX_ASSET_TIMEOUT_MS));
}

function secureLoginDocument(response, request) {
  const hostname = new URL(request.url).hostname;
  if (['localhost', '127.0.0.1', '::1', '[::1]'].includes(hostname)) return response;

  const headers = new Headers(response.headers);
  headers.set('content-security-policy', 'upgrade-insecure-requests; form-action https:');
  return new Response(response.body, {
    status: response.status,
    statusText: response.statusText,
    headers
  });
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
