import previousApp from './app-v18.js';
import { handleV19MigrationApi } from './v19-migration.js';

const SESSION_COOKIE = 'cyaccounting_session';

export default {
  async fetch(request, env) {
    const url = new URL(request.url);
    if (!url.pathname.startsWith('/api/migration/desktop/')) {
      return previousApp.fetch(request, env);
    }

    if (!env.DB) return json({ ok: false, error: 'D1 尚未綁定。', code: 'DB_NOT_CONFIGURED' }, 503);

    try {
      const session = await sessionFromRequest(request, env.DB);
      if (!session) return json({ ok: false, error: '尚未登入。', code: 'AUTH_REQUIRED' }, 401);
      const response = await handleV19MigrationApi(request, env, session);
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

async function sessionFromRequest(request, db) {
  const token = cookieValue(request, SESSION_COOKIE);
  if (!token || token.length > 128) return null;
  const hash = await sha256Hex(token);
  const now = new Date().toISOString();
  return db.prepare(`
    SELECT session_hash, employee_id, employee_no, employee_name, role,
           credential_version, employee_revision, created_at, expires_at
    FROM web_sessions
    WHERE session_hash = ? AND expires_at > ?
    LIMIT 1
  `).bind(hash, now).first();
}

function cookieValue(request, name) {
  const raw = request.headers.get('cookie') || '';
  for (const part of raw.split(';')) {
    const index = part.indexOf('=');
    if (index < 0) continue;
    const key = part.slice(0, index).trim();
    if (key !== name) continue;
    return decodeURIComponent(part.slice(index + 1).trim());
  }
  return null;
}

async function sha256Hex(value) {
  const digest = new Uint8Array(await crypto.subtle.digest('SHA-256', new TextEncoder().encode(value)));
  return Array.from(digest, part => part.toString(16).padStart(2, '0')).join('');
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
