import legacyApp from './app-v18.js';
import { handleV19MigrationApi, hasActiveLegacyMigration } from './v19-migration-api.js';

const SESSION_COOKIE = 'cyaccounting_session';

export default {
  async fetch(request, env) {
    const url = new URL(request.url);

    if (!url.pathname.startsWith('/api/')) {
      return legacyApp.fetch(request, env);
    }

    if (!env.DB) return legacyApp.fetch(request, env);

    if (url.pathname.startsWith('/api/migration/sqlite/')) {
      try {
        const session = await sessionFromRequest(request, env.DB);
        if (!session) return json({ ok: false, error: '尚未登入。', code: 'AUTH_REQUIRED' }, 401);
        const response = await handleV19MigrationApi(request, env, session);
        if (response) return response;
        return json({ ok: false, error: '找不到此遷移功能。', code: 'MIGRATION_ROUTE_NOT_FOUND' }, 404);
      } catch (error) {
        console.error('cyaccounting_migration_request_failed', error instanceof Error ? error.message : 'unknown_error');
        return json({ ok: false, error: '舊帳本遷移服務處理失敗，請稍後再試。' }, 500);
      }
    }

    if (isBlockedMutation(request, url)) {
      try {
        const activeRunId = await hasActiveLegacyMigration(env.DB);
        if (activeRunId) {
          return json({
            ok: false,
            error: '舊帳本遷移尚未完成，暫停其他資料修改。請先完成或中止遷移。',
            code: 'MIGRATION_WRITE_LOCKED',
            runId: activeRunId
          }, 423);
        }
      } catch (error) {
        console.error('cyaccounting_migration_lock_check_failed', error instanceof Error ? error.message : 'unknown_error');
        return json({ ok: false, error: '無法確認舊帳本遷移狀態，為避免資料衝突已暫停寫入。' }, 503);
      }
    }

    return legacyApp.fetch(request, env);
  },

  async scheduled(controller, env, ctx) {
    return legacyApp.scheduled(controller, env, ctx);
  }
};

function isBlockedMutation(request, url) {
  if (!['POST', 'PUT', 'PATCH', 'DELETE'].includes(request.method)) return false;
  if (url.pathname.startsWith('/api/auth/')) return false;
  if (url.pathname.startsWith('/api/backup/')) return false;
  if (url.pathname.startsWith('/api/migration/')) return false;
  return true;
}

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
