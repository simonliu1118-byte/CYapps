import previousApp from './app-v18.js';
import { handleV19MigrationApi } from './v19-migration-safe.js';
import { canWriteAccounting, resolveIdentitySession } from './identity-adapter.js';

const SUMMARY_MAX_UNITS = 40;
const ACCOUNT_NAME_MAX_CHARS = 8;

export default {
  async fetch(request, env) {
    const url = new URL(request.url);
    const migrationRequest = url.pathname.startsWith('/api/migration/desktop/');
    const transactionWrite = isTransactionWrite(url.pathname, request.method);
    const accountWrite = isAccountWrite(url.pathname, request.method);

    if (!migrationRequest && !transactionWrite && !accountWrite) {
      return previousApp.fetch(request, env);
    }

    if (!env.DB) return json({ ok: false, error: 'D1 尚未綁定。', code: 'DB_NOT_CONFIGURED' }, 503);

    try {
      const resolved = await resolveIdentitySession(request, env);
      if (!resolved.ok) return authFailure(resolved);

      if ((transactionWrite || accountWrite) && !canWriteAccounting(resolved.principal)) {
        return json({ ok: false, error: '此帳號為唯讀權限，只能檢視資料與匯出 Excel。', code: 'READ_ONLY_USER' }, 403);
      }

      if (transactionWrite) {
        const body = await request.clone().json().catch(() => null);
        if (body && summaryWeightedUnits(body.summary) > SUMMARY_MAX_UNITS) {
          return json({ ok: false, error: '摘要不可超過 20 個中文字或 40 個英數字元。', code: 'SUMMARY_TOO_LONG' }, 400);
        }
        return previousApp.fetch(request, env);
      }

      if (accountWrite) {
        const body = await request.clone().json().catch(() => null);
        const name = normalizeAccountName(body?.name);
        if (!name) return json({ ok: false, error: '帳戶名稱不可空白。', code: 'ACCOUNT_NAME_REQUIRED' }, 400);
        if (Array.from(name).length > ACCOUNT_NAME_MAX_CHARS) {
          return json({ ok: false, error: '帳戶名稱最多 8 個字。', code: 'ACCOUNT_NAME_TOO_LONG' }, 400);
        }
        return previousApp.fetch(request, env);
      }

      const response = await handleV19MigrationApi(request, env, resolved.session);
      if (response) return response;
      return json({ ok: false, error: '找不到此資料移轉功能。', code: 'MIGRATION_ROUTE_NOT_FOUND' }, 404);
    } catch (error) {
      console.error('cyaccounting_migration_request_failed', error instanceof Error ? error.message : 'unknown_error');
      return json({ ok: false, error: '資料移轉服務處理失敗，請稍後再試。', code: 'MIGRATION_FAILED' }, 500);
    }
  },

  async scheduled(controller, env, ctx) {
    return previousApp.scheduled(controller, env, ctx);
  }
};

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

function authFailure(result) {
  if (result.status >= 500) {
    return json({ ok: false, error: '中央帳號服務目前無法驗證登入狀態。', code: 'IDENTITY_UNAVAILABLE' }, 503);
  }
  return json({ ok: false, error: '尚未登入。', code: 'AUTH_REQUIRED' }, 401);
}

function json(data, status = 200) {
  return new Response(JSON.stringify(data), {
    status,
    headers: {
      'content-type': 'application/json; charset=utf-8',
      'cache-control': 'no-store',
      'x-content-type-options': 'nosniff'
    }
  });
}
