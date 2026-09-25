import coreWorker from './index.js';
import { handleV11Api } from './v11-tools.js';
import { handleV12Api } from './v12-tools.js';
import { handleV13Api } from './v13-export.js';
import { handleV15Api } from './v15-import.js';
import { handleV16Api, handleV16OAuthCallback, runScheduledBackup } from './v16-backup.js';

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
      const v16Callback = await handleV16OAuthCallback(request, env);
      if (v16Callback) return v16Callback;

      if (url.pathname === '/api/auth/login' && request.method === 'POST') {
        return handleLogin(request, env);
      }
      if (url.pathname === '/api/auth/password-reset/challenge' && request.method === 'POST') {
        return handlePasswordResetChallenge(request, env);
      }
      if (url.pathname === '/api/auth/password-reset/confirm' && request.method === 'POST') {
        return handlePasswordResetConfirm(request, env);
      }
      if (url.pathname === '/api/auth/logout' && request.method === 'POST') {
        return handleLogout(request, env.DB);
      }
      if (url.pathname === '/api/auth/me' && request.method === 'GET') {
        return handleMe(request, env.DB);
      }

      const session = await sessionFromRequest(request, env.DB);
      if (!session) return json({ ok: false, error: '尚未登入。', code: 'AUTH_REQUIRED' }, 401);

      const v16Response = await handleV16Api(request, env, session);
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

async function handleLogin(request, env) {
  const identityUnavailable = identityConfigurationError(env);
  if (identityUnavailable) return identityUnavailable;

  const body = await request.json().catch(() => null);
  const employeeNo = String(body?.employeeNo || '').trim();
  const password = typeof body?.password === 'string' ? body.password : '';
  if (!/^\d{4}$/.test(employeeNo) || password.length < 1 || password.length > 200) {
    return json({ ok: false, error: '請輸入 4 碼員工編號與密碼。', code: 'INVALID_LOGIN_REQUEST' }, 400);
  }

  const identityResponse = await identityFetch(request, env, '/v1/web-auth/login', {
    application: APPLICATION,
    employeeNo,
    password
  });
  const identity = await identityResponse.json().catch(() => ({}));

  if (!identityResponse.ok || identity?.ok === false) {
    const code = String(identity?.error?.code || 'WEB_AUTHENTICATION_FAILED');
    if (identityResponse.status === 429) {
      return json({ ok: false, error: '登入嘗試次數過多，請稍後再試。', code }, 429, retryHeader(identityResponse));
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

async function handlePasswordResetChallenge(request, env) {
  const identityUnavailable = identityConfigurationError(env);
  if (identityUnavailable) return identityUnavailable;

  const body = await request.json().catch(() => null);
  const employeeNo = String(body?.employeeNo || '').trim();
  if (!/^\d{4}$/.test(employeeNo)) {
    return json({ ok: false, error: '請輸入 4 碼員工編號。', code: 'INVALID_PASSWORD_RESET_REQUEST' }, 400);
  }

  const identityResponse = await identityFetch(request, env, '/v1/web-auth/password-reset/challenge', {
    application: APPLICATION,
    employeeNo
  });
  const identity = await identityResponse.json().catch(() => ({}));
  if (!identityResponse.ok || identity?.ok === false) {
    return passwordResetError(identityResponse, identity);
  }

  const challenge = identity?.challenge;
  if (!challenge?.challengeId || !challenge?.maskedEmail || !challenge?.expiresAt || !challenge?.resendAfter) {
    return json({ ok: false, error: '中央帳號服務回應格式錯誤。', code: 'IDENTITY_INVALID_RESPONSE' }, 502);
  }
  return json({
    ok: true,
    challenge: {
      challengeId: String(challenge.challengeId),
      maskedEmail: String(challenge.maskedEmail),
      expiresAt: String(challenge.expiresAt),
      resendAfter: String(challenge.resendAfter)
    }
  }, 201);
}

async function handlePasswordResetConfirm(request, env) {
  const identityUnavailable = identityConfigurationError(env);
  if (identityUnavailable) return identityUnavailable;

  const body = await request.json().catch(() => null);
  const employeeNo = String(body?.employeeNo || '').trim();
  const challengeId = String(body?.challengeId || '').trim();
  const otp = String(body?.otp || '').trim();
  const newPassword = typeof body?.newPassword === 'string' ? body.newPassword : '';
  if (!/^\d{4}$/.test(employeeNo) || !/^otp_[0-9a-f-]{36}$/i.test(challengeId)
      || !/^\d{6}$/.test(otp) || !/^[A-Za-z0-9]{8,200}$/.test(newPassword)) {
    return json({ ok: false, error: '驗證資料或新密碼格式不正確。', code: 'INVALID_PASSWORD_RESET_CONFIRMATION' }, 400);
  }

  const identityResponse = await identityFetch(request, env, '/v1/web-auth/password-reset/confirm', {
    application: APPLICATION,
    employeeNo,
    challengeId,
    otp,
    newPassword
  });
  const identity = await identityResponse.json().catch(() => ({}));
  if (!identityResponse.ok || identity?.ok === false) {
    return passwordResetError(identityResponse, identity);
  }

  await env.DB.prepare('DELETE FROM web_sessions WHERE employee_no = ?').bind(employeeNo).run();
  return json({ ok: true, passwordReset: true });
}

function passwordResetError(response, identity) {
  const code = String(identity?.error?.code || 'PASSWORD_RESET_FAILED');
  const retry = retryHeader(response);
  if (code === 'OTP_INVALID') return json({ ok: false, error: 'Email 驗證碼錯誤。', code }, 400);
  if (code === 'OTP_EXPIRED') return json({ ok: false, error: 'Email 驗證碼已過期，請重新寄送。', code }, 410);
  if (code === 'OTP_ALREADY_USED') return json({ ok: false, error: '此驗證碼已使用，請重新寄送。', code }, 409);
  if (code === 'OTP_ATTEMPTS_EXCEEDED') return json({ ok: false, error: '驗證碼錯誤次數已達上限，請重新寄送。', code }, 429, retry);
  if (code === 'OTP_RESEND_COOLDOWN') {
    const seconds = Number(identity?.retryAfterSeconds || response.headers.get('retry-after') || 60);
    return json({ ok: false, error: `驗證碼剛寄出，請 ${Math.max(1, seconds)} 秒後再重寄。`, code, retryAfterSeconds: Math.max(1, seconds) }, 429, retry);
  }
  if (code === 'OTP_RATE_LIMITED' || code === 'PASSWORD_RESET_RATE_LIMITED') {
    return json({ ok: false, error: '要求驗證碼或重設密碼的次數過多，請稍後再試。', code }, 429, retry);
  }
  if (code === 'PASSWORD_RESET_UNAVAILABLE') {
    return json({ ok: false, error: '此帳號目前無法使用 Email 重設密碼，請確認帳號已啟用且 Email 已完成驗證。', code }, 400);
  }
  if (code === 'EMAIL_DELIVERY_FAILED') return json({ ok: false, error: '驗證信目前無法寄出，請稍後再試。', code }, 503);
  if (code === 'EMAIL_PROVIDER_NOT_CONFIGURED' || code === 'OTP_NOT_CONFIGURED') {
    return json({ ok: false, error: 'Email 驗證服務尚未完成設定。', code }, 503);
  }
  if (code === 'WEB_AUTH_NOT_READY' || response.status === 503) {
    return json({ ok: false, error: '中央員工帳號服務目前無法處理密碼重設。', code }, 503);
  }
  return json({ ok: false, error: '密碼重設失敗，請稍後再試。', code }, response.status >= 400 ? response.status : 400, retry);
}

async function identityFetch(request, env, path, body) {
  const headers = new Headers({ 'content-type': 'application/json' });
  const clientIp = request.headers.get('cf-connecting-ip');
  if (clientIp) headers.set('cf-connecting-ip', clientIp);
  const requestId = request.headers.get('x-request-id');
  if (requestId) headers.set('x-request-id', requestId);
  return env.IDENTITY.fetch(new Request(`https://cyinvoice.internal${path}`, {
    method: 'POST',
    headers,
    body: JSON.stringify(body)
  }));
}

function identityConfigurationError(env) {
  if (env.IDENTITY && typeof env.IDENTITY.fetch === 'function') return null;
  return json({ ok: false, error: '中央帳號服務尚未連線。', code: 'IDENTITY_NOT_CONFIGURED' }, 503);
}

function retryHeader(response) {
  return { 'retry-after': response.headers.get('retry-after') || '60' };
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
