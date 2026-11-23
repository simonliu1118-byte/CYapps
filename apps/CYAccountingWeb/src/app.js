import coreWorker from './index.js';
import { handleStatisticsApi } from './statistics.js';
import { handleAccountingToolsApi } from './accounting-tools.js';
import { handleAccountingControlsApi } from './accounting-controls.js';
import { handleAccountLifecycleApi } from './account-lifecycle.js';
import { handleOpeningBalanceApi } from './opening-balances.js';
import { handleExcelExportApi } from './excel-export.js';
import { handleExcelImportApi } from './excel-import.js';
import { handleBackupApi, runScheduledBackup, recoveryLanding, handleBackupVerificationApi } from './backup-service.js';
import { handleDesktopMigrationApi } from './desktop-migration.js';
import {
  canWriteAccounting,
  clearProviderSessionCookie,
  logoutCyid,
  resolveIdentitySession
} from './identity-adapter.js';
import { handleSsoRoute, ssoServiceUnavailablePage, startSsoLogin } from './sso.js';

const SUMMARY_MAX_UNITS = 40;
const ACCOUNT_NAME_MAX_CHARS = 8;
const DEFAULT_ASSET_TIMEOUT_MS = 5_000;
const MIN_ASSET_TIMEOUT_MS = 100;
const MAX_ASSET_TIMEOUT_MS = 15_000;

function shouldDisableBrowserCache(pathname) {
  return pathname === '/' || pathname.endsWith('.html') || pathname.endsWith('.js');
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
      if (url.pathname.startsWith('/auth/sso/')) {
        return await handleSsoRoute(request, env, url);
      }

      // Bookmarks of the retired password login page continue into the CY Web portal login.
      if ((url.pathname === '/login' || url.pathname === '/login.html') && ['GET', 'HEAD'].includes(request.method)) {
        return redirect('/');
      }

      if(url.pathname==='/recovery'&&request.method==='GET') {
        const resolved=await resolveIdentitySession(request,env);
        if(!resolved.ok)return resolved.status>=500?ssoServiceUnavailablePage():startSsoLogin(env,'/recovery');
        return recoveryLanding(env,resolved.session);
      }
      if ((url.pathname === '/' || url.pathname === '/index.html') && request.method === 'GET') {
        const resolved = await resolveIdentitySession(request, env);
        if (!resolved.ok) {
          return resolved.status >= 500 ? ssoServiceUnavailablePage() : startSsoLogin(env, '/');
        }
        return fetchAsset(request, env, '/index.html');
      }

      if (!url.pathname.startsWith('/api/')) {
        const response = await boundedAssetFetch(request, env);
        return shouldDisableBrowserCache(url.pathname) ? noCache(response) : response;
      }

      if (url.pathname.startsWith('/api/backup/verification/')) {
        return await handleBackupVerificationApi(request, env);
      }

      if (url.pathname === '/api/health' && request.method === 'GET') {
        return coreWorker.fetch(request, env);
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

      if(url.pathname.startsWith('/api/backup/recovery/')) {
        return await handleBackupApi(request,env,resolved.session)||json({ok:false,error:'找不到復原操作。'},404);
      }
      const recoveryHold=await env.DB.prepare("SELECT value FROM meta WHERE key='recovery_hold'").first();
      if(recoveryHold?.value==='1') return json({ok:false,error:'此帳本仍為還原驗證工作區，尚未核准切換。',code:'RECOVERY_WORKSPACE_HOLD'},503);
      if(!['GET','HEAD'].includes(request.method)) {
        const epoch=await env.DB.prepare("SELECT value FROM meta WHERE key='accounting_epoch'").first();
        if(epoch?.value&&request.headers.get('x-cyacc-book-epoch')!==epoch.value) return json({ok:false,error:'帳本已完成還原切換，請重新整理後再操作。',code:'ACCOUNTING_BOOK_CHANGED'},409);
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

      const importResponse = await handleExcelImportApi(request, env, resolved.session);
      if (importResponse) return importResponse;

      const statisticsResponse = await handleStatisticsApi(request, env);
      if (statisticsResponse) return statisticsResponse;

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

      return coreWorker.fetch(request, env, {session:resolved.session});
    } catch (error) {
      console.error('cyaccounting_request_failed', error instanceof Error ? error.message : 'unknown_error');
      return json({ ok: false, error: '系統處理失敗，請稍後再試。' }, 500);
    }
  },

  async scheduled(controller, env, ctx) {
    ctx.waitUntil(runScheduledBackup(env, { cron: controller.cron, now: new Date(controller.scheduledTime) }));
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
  return json({ ok: true, user: result.user, session: { expiresAt: result.expiresAt } });
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
  return response;
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
