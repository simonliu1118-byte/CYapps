import legacyApp from './app.js';
import { handleV17Api, runScheduledBackup } from './v17-backup.js';
import { resolveIdentitySession } from './identity-adapter.js';

export default {
  async fetch(request, env) {
    const url = new URL(request.url);
    if (!url.pathname.startsWith('/api/backup/')) {
      return legacyApp.fetch(request, env);
    }

    if (!env.DB) return json({ ok: false, error: 'D1 尚未綁定。' }, 503);

    try {
      const resolved = await resolveIdentitySession(request, env);
      if (!resolved.ok) return authFailure(resolved);
      const response = await handleV17Api(request, env, resolved.session);
      if (response) return response;
      return json({ ok: false, error: '找不到此備份功能。', code: 'BACKUP_ROUTE_NOT_FOUND' }, 404);
    } catch (error) {
      console.error('cyaccounting_backup_request_failed', error instanceof Error ? error.message : 'unknown_error');
      return json({ ok: false, error: '備份服務處理失敗，請稍後再試。' }, 500);
    }
  },

  async scheduled(_controller, env, ctx) {
    ctx.waitUntil(runScheduledBackup(env));
  }
};

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
