import coreWorker from './index.js';

const SESSION_COOKIE = 'cyaccounting_session';
const SESSION_TTL_SECONDS = 8 * 60 * 60;
const APPLICATION = 'CYAccountingWeb';

export default {
  async fetch(request, env) {
    const url = new URL(request.url);

    if (!url.pathname.startsWith('/api/')) {
      return env.ASSETS.fetch(request);
    }

    if (url.pathname === '/api/health' && request.method === 'GET') {
      return coreWorker.fetch(request, env);
    }

    if (!env.DB) {
      return json({ ok: false, error: 'D1 尚未綁定。' }, 503);
    }

    try {
      if (url.pathname === '/api/auth/login' && request.method === 'POST') {
        return handleLogin(request, env);
      }
      if (url.pathname === '/api/auth/logout' && request.method === 'POST') {
        return handleLogout(request, env.DB);
      }
      if (url.pathname === '/api/auth/me' && request.method === 'GET') {
        return handleMe(request, env.DB);
      }

      const session = await sessionFromRequest(request, env.DB);
      if (!session) return json({ ok: false, error: '尚未登入。', code: 'AUTH_REQUIRED' }, 401);

      return coreWorker.fetch(request, env);
    } catch (error) {
      console.error('cyaccounting_auth_failed', error instanceof Error ? error.message : 'unknown_error');
      return json({ ok: false, error: '登入服務處理失敗。' }, 500);
    }
  }
};

async function handleLogin(request, env) {
  if (!env.IDENTITY || typeof env.IDENTITY.fetch !== 'function') {
    return json({ ok: false, error: '中央帳號服務尚未連線。', code: 'IDENTITY_NOT_CONFIGURED' }, 503);
  }

  const body = await request.json().catch(() => null);
  const employeeNo = String(body?.employeeNo || '').trim();
  const password = typeof body?.password === 'string' ? body.password : '';
  if (!/^\d{4}$/.test(employeeNo) || password.length < 1 || password.length > 200) {
    return json({ ok: false, error: '請輸入 4 碼員工編號與密碼。', code: 'INVALID_LOGIN_REQUEST' }, 400);
  }

  const headers = new Headers({ 'content-type': 'application/json' });
  const clientIp = request.headers.get('cf-connecting-ip');
  if (clientIp) headers.set('cf-connecting-ip', clientIp);

  const identityResponse = await env.IDENTITY.fetch(new Request('https://cyinvoice.internal/v1/web-auth/login', {
    method: 'POST',
    headers,
    body: JSON.stringify({ application: APPLICATION, employeeNo, password })
  }));
  const identity = await identityResponse.json().catch(() => ({}));

  if (!identityResponse.ok || identity?.ok === false) {
    const code = String(identity?.error?.code || 'WEB_AUTHENTICATION_FAILED');
    if (identityResponse.status === 429) {
      return json({ ok: false, error: '登入嘗試次數過多，請稍後再試。', code }, 429, { 'retry-after': identityResponse.headers.get('retry-after') || '60' });
    }
    if (identityResponse.status === 503 || code === 'WEB_AUTH_NOT_READY') {
      return json({ ok: false, error: '中央員工帳號尚未完成雲端啟用。', code: 'IDENTITY_NOT_READY' }, 503);
    }
    if (identityResponse.status === 403 || code === 'APPLICATION_ACCESS_DENIED') {
      return json({ ok: false, error: '此員工帳號沒有記帳系統使用權限。', code: 'ACCESS_DENIED' }, 403);
    }
    return json({ ok: false, error: '員工編號或密碼不正確。', code: 'LOGIN_FAILED' }, 401);
  }

  const employee = identity.employee;
  if (!employee || !employee.employeeId || !/^\d{4}$/.test(String(employee.employeeNo || ''))) {
    return json({ ok: false, error: '中央帳號服務回應格式錯誤。', code: 'IDENTITY_INVALID_RESPONSE' }, 502);
  }

  const token = randomToken();
  const sessionHash = await sha256Hex(token);
  const now = new Date();
  const expires = new Date(now.getTime() + SESSION_TTL_SECONDS * 1000);

  await env.DB.batch([
    env.DB.prepare('DELETE FROM web_sessions WHERE expires_at <= ?').bind(now.toISOString()),
    env.DB.prepare(`
      INSERT INTO web_sessions(
        session_hash, employee_id, employee_no, employee_name, role,
        credential_version, employee_revision, created_at, expires_at
      ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?)
    `).bind(
      sessionHash,
      String(employee.employeeId),
      String(employee.employeeNo),
      String(employee.name || ''),
      String(employee.role || ''),
      Number(employee.credentialVersion || 0),
      Number(employee.revision || 0),
      now.toISOString(),
      expires.toISOString()
    )
  ]);

  return json({ ok: true, user: sessionUser(employee) }, 200, {
    'set-cookie': sessionCookie(token, SESSION_TTL_SECONDS)
  });
}

async function handleLogout(request, db) {
  const token = cookieValue(request, SESSION_COOKIE);
  if (token) {
    const hash = await sha256Hex(token);
    await db.prepare('DELETE FROM web_sessions WHERE session_hash = ?').bind(hash).run();
  }
  return json({ ok: true }, 200, {
    'set-cookie': `${SESSION_COOKIE}=; Path=/; HttpOnly; Secure; SameSite=Strict; Max-Age=0`
  });
}

async function handleMe(request, db) {
  const session = await sessionFromRequest(request, db);
  if (!session) return json({ ok: false, error: '尚未登入。', code: 'AUTH_REQUIRED' }, 401);
  return json({ ok: true, user: {
    employeeId: session.employee_id,
    employeeNo: session.employee_no,
    name: session.employee_name,
    role: session.role
  }});
}

async function sessionFromRequest(request, db) {
  const token = cookieValue(request, SESSION_COOKIE);
  if (!token || token.length > 128) return null;
  const hash = await sha256Hex(token);
  const now = new Date().toISOString();
  const session = await db.prepare(`
    SELECT session_hash, employee_id, employee_no, employee_name, role,
           credential_version, employee_revision, created_at, expires_at
    FROM web_sessions
    WHERE session_hash = ? AND expires_at > ?
    LIMIT 1
  `).bind(hash, now).first();
  if (!session) return null;
  return session;
}

function sessionUser(employee) {
  return {
    employeeId: String(employee.employeeId),
    employeeNo: String(employee.employeeNo),
    name: String(employee.name || ''),
    role: String(employee.role || '')
  };
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

function sessionCookie(token, maxAge) {
  return `${SESSION_COOKIE}=${encodeURIComponent(token)}; Path=/; HttpOnly; Secure; SameSite=Strict; Max-Age=${maxAge}`;
}

function randomToken() {
  const bytes = new Uint8Array(32);
  crypto.getRandomValues(bytes);
  let binary = '';
  for (const byte of bytes) binary += String.fromCharCode(byte);
  return btoa(binary).replaceAll('+', '-').replaceAll('/', '_').replaceAll('=', '');
}

async function sha256Hex(value) {
  const digest = new Uint8Array(await crypto.subtle.digest('SHA-256', new TextEncoder().encode(value)));
  return Array.from(digest, part => part.toString(16).padStart(2, '0')).join('');
}

function json(data, status = 200, extraHeaders = {}) {
  return new Response(JSON.stringify(data), {
    status,
    headers: {
      'content-type': 'application/json; charset=utf-8',
      'cache-control': 'no-store',
      'x-content-type-options': 'nosniff',
      ...extraHeaders
    }
  });
}
