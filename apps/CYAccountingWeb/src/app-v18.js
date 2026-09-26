import legacyApp from './app-v17.js';
import { handleV18Api, runScheduledTieredBackup } from './v18-backup.js';

const SESSION_COOKIE = 'cyaccounting_session';

export default {
  async fetch(request, env) {
    const url = new URL(request.url);
    if (!url.pathname.startsWith('/api/backup/')) {
      return legacyApp.fetch(request, env);
    }

    if (!env.DB) return json({ ok: false, error: 'D1 尚未綁定。' }, 503);

    try {
      const session = await sessionFromRequest(request, env.DB);
      if (!session) return json({ ok: false, error: '尚未登入。', code: 'AUTH_REQUIRED' }, 401);
      const response = await handleV18Api(request, env, session);
      if (response) return response;
      return json({ ok: false, error: '找不到此備份功能。', code: 'BACKUP_ROUTE_NOT_FOUND' }, 404);
    } catch (error) {
      console.error('cyaccounting_backup_request_failed', error instanceof Error ? error.message : 'unknown_error');
      return json({ ok: false, error: '備份服務處理失敗，請稍後再試。' }, 500);
    }
  },

  async scheduled(_controller, env, ctx) {
    ctx.waitUntil(runScheduledTieredBackup(env));
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
