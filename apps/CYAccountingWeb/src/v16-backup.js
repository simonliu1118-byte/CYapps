const BACKUP_PROVIDER = 'google_drive';
const BACKUP_FORMAT = 'CYAccountingWebBackup';
const BACKUP_FORMAT_VERSION = 1;
const APP_VERSION = '0.16.0';
const BACKUP_FOLDER_NAME = 'CYAccountingWeb';
const BACKUP_PREFIX = 'CYAccountingWeb_backup_';
const BACKUP_RETENTION = 30;
const OAUTH_STATE_TTL_MS = 10 * 60 * 1000;
const PAGE_SIZE = 1000;
const DRIVE_SCOPE = 'https://www.googleapis.com/auth/drive.file';
const DRIVE_API = 'https://www.googleapis.com/drive/v3';
const DRIVE_UPLOAD_API = 'https://www.googleapis.com/upload/drive/v3';
const encoder = new TextEncoder();
const decoder = new TextDecoder();

export async function handleV16OAuthCallback(request, env) {
  const url = new URL(request.url);
  if (url.pathname !== '/api/backup/google/oauth/callback' || request.method !== 'GET') return null;
  if (!env.DB) return json({ ok: false, error: 'D1 尚未綁定。' }, 503);

  const state = String(url.searchParams.get('state') || '');
  if (!state || state.length > 500) return redirectBackupResult(request, 'error', 'invalid_state');
  const stateHash = await sha256HexText(state);
  const now = new Date().toISOString();
  const saved = await env.DB.prepare(`
    SELECT state_hash, employee_no, redirect_uri, created_at, expires_at
    FROM backup_oauth_states
    WHERE state_hash = ? AND expires_at > ?
    LIMIT 1
  `).bind(stateHash, now).first();

  if (!saved) return redirectBackupResult(request, 'error', 'invalid_state');
  await env.DB.prepare('DELETE FROM backup_oauth_states WHERE state_hash = ?').bind(stateHash).run();

  if (url.searchParams.get('error')) return redirectBackupResult(request, 'error', 'oauth_denied');
  const code = String(url.searchParams.get('code') || '');
  if (!code) return redirectBackupResult(request, 'error', 'missing_code');

  try {
    assertBackupSecrets(env);
    const token = await exchangeAuthorizationCode(env, code, String(saved.redirect_uri));
    const refreshToken = String(token.refresh_token || '');
    const accessToken = String(token.access_token || '');
    if (!refreshToken || !accessToken) throw new BackupError('Google 未提供可持續使用的 refresh token。', 'NO_REFRESH_TOKEN');

    const [user, folderId] = await Promise.all([
      driveAboutUser(accessToken),
      ensureDriveFolder(accessToken, '')
    ]);
    const encryptedRefreshToken = await encryptSecret(refreshToken, env.GOOGLE_DRIVE_TOKEN_KEY);
    const connectedAt = new Date().toISOString();

    await env.DB.prepare(`
      INSERT INTO backup_integrations(
        provider, encrypted_refresh_token, folder_id, account_email, account_name, connected_at, updated_at
      ) VALUES (?, ?, ?, ?, ?, ?, ?)
      ON CONFLICT(provider) DO UPDATE SET
        encrypted_refresh_token = excluded.encrypted_refresh_token,
        folder_id = excluded.folder_id,
        account_email = excluded.account_email,
        account_name = excluded.account_name,
        connected_at = excluded.connected_at,
        updated_at = excluded.updated_at
    `).bind(
      BACKUP_PROVIDER,
      encryptedRefreshToken,
      folderId,
      String(user.emailAddress || ''),
      String(user.displayName || ''),
      connectedAt,
      connectedAt
    ).run();

    let initialBackup = 'success';
    try {
      const result = await runGoogleDriveBackup(env, 'manual');
      if (result?.skipped) initialBackup = 'skipped';
    } catch {
      initialBackup = 'failed';
    }
    return redirectBackupResult(request, 'connected', initialBackup);
  } catch (error) {
    console.error('cyaccounting_google_oauth_callback_failed', safeErrorCode(error));
    return redirectBackupResult(request, 'error', safeErrorCode(error).toLowerCase());
  }
}

export async function handleV16Api(request, env, session) {
  const url = new URL(request.url);
  if (!url.pathname.startsWith('/api/backup/')) return null;
  if (String(session?.role || '') !== 'SUPER_ADMIN') {
    return json({ ok: false, error: '只有超級管理員可以管理備份與復原。', code: 'SUPER_ADMIN_REQUIRED' }, 403);
  }

  if (url.pathname === '/api/backup/status' && request.method === 'GET') {
    return backupStatus(env);
  }
  if (url.pathname === '/api/backup/google/oauth/start' && request.method === 'POST') {
    return startGoogleOAuth(request, env, session);
  }
  if (url.pathname === '/api/backup/run' && request.method === 'POST') {
    try {
      const result = await runGoogleDriveBackup(env, 'manual');
      if (result?.skipped) return json({ ok: false, error: 'Google Drive 尚未完成連結。', code: 'BACKUP_NOT_CONNECTED' }, 409);
      return json({ ok: true, backup: result });
    } catch (error) {
      return json({ ok: false, error: safeBackupMessage(error), code: safeErrorCode(error) }, 500);
    }
  }
  return null;
}

export async function runScheduledBackup(env) {
  try {
    if (!env?.DB || !backupSecretsReady(env)) return { skipped: true, reason: 'not_configured' };
    const integration = await getDriveIntegration(env.DB);
    if (!integration) return { skipped: true, reason: 'not_connected' };
    return await runGoogleDriveBackup(env, 'scheduled');
  } catch (error) {
    console.error('cyaccounting_scheduled_backup_failed', safeErrorCode(error));
    return { ok: false, error: safeErrorCode(error) };
  }
}

async function backupStatus(env) {
  const configured = backupSecretsReady(env);
  const [integration, latestSuccess, recentRuns] = await Promise.all([
    getDriveIntegration(env.DB),
    env.DB.prepare(`
      SELECT id, completed_at, file_name, data_sha256, file_sha256, row_count, byte_size
      FROM backup_runs
      WHERE provider = ? AND status = 'success'
      ORDER BY id DESC LIMIT 1
    `).bind(BACKUP_PROVIDER).first(),
    env.DB.prepare(`
      SELECT id, trigger_kind, status, started_at, completed_at, file_name, row_count, byte_size, error_message
      FROM backup_runs
      WHERE provider = ?
      ORDER BY id DESC LIMIT 8
    `).bind(BACKUP_PROVIDER).all()
  ]);

  return json({
    ok: true,
    configured,
    connected: Boolean(integration),
    account: integration ? { email: integration.account_email || '', name: integration.account_name || '' } : null,
    folderId: integration?.folder_id || '',
    connectedAt: integration?.connected_at || null,
    schedule: { cron: '30 19 * * *', localTime: '每日 03:30（台灣時間）' },
    retention: BACKUP_RETENTION,
    latestSuccess: latestSuccess ? normalizeRunRow(latestSuccess) : null,
    recentRuns: (recentRuns.results || []).map(normalizeRunRow),
    restoreAvailable: false
  });
}

async function startGoogleOAuth(request, env, session) {
  try {
    assertBackupSecrets(env);
    await env.DB.prepare('DELETE FROM backup_oauth_states WHERE expires_at <= ?').bind(new Date().toISOString()).run();

    const state = randomToken(32);
    const stateHash = await sha256HexText(state);
    const now = new Date();
    const expires = new Date(now.getTime() + OAUTH_STATE_TTL_MS);
    const origin = new URL(request.url).origin;
    const redirectUri = `${origin}/api/backup/google/oauth/callback`;

    await env.DB.prepare(`
      INSERT INTO backup_oauth_states(state_hash, employee_no, redirect_uri, created_at, expires_at)
      VALUES (?, ?, ?, ?, ?)
    `).bind(stateHash, String(session.employee_no || ''), redirectUri, now.toISOString(), expires.toISOString()).run();

    const params = new URLSearchParams({
      client_id: String(env.GOOGLE_DRIVE_CLIENT_ID),
      redirect_uri: redirectUri,
      response_type: 'code',
      scope: DRIVE_SCOPE,
      access_type: 'offline',
      prompt: 'consent',
      include_granted_scopes: 'true',
      state
    });
    return json({ ok: true, authUrl: `https://accounts.google.com/o/oauth2/v2/auth?${params.toString()}` });
  } catch (error) {
    return json({ ok: false, error: safeBackupMessage(error), code: safeErrorCode(error) }, 503);
  }
}

export async function runGoogleDriveBackup(env, triggerKind = 'scheduled') {
  if (!['scheduled', 'manual'].includes(triggerKind)) throw new BackupError('備份觸發類型錯誤。', 'INVALID_TRIGGER');
  assertBackupSecrets(env);
  const integration = await getDriveIntegration(env.DB);
  if (!integration) return { skipped: true, reason: 'not_connected' };

  const startedAt = new Date().toISOString();
  let backup = null;
  let fileId = '';
  let fileName = '';

  try {
    backup = await buildBackupPackage(env.DB, new Date());
    fileName = backup.fileName;
    const refreshToken = await decryptSecret(String(integration.encrypted_refresh_token || ''), env.GOOGLE_DRIVE_TOKEN_KEY);
    if (!refreshToken) throw new BackupError('Google Drive 授權資訊無法解密。', 'TOKEN_DECRYPT_FAILED');
    const accessToken = await refreshAccessToken(env, refreshToken);
    const folderId = await ensureDriveFolder(accessToken, String(integration.folder_id || ''));
    if (folderId !== integration.folder_id) {
      await env.DB.prepare('UPDATE backup_integrations SET folder_id = ?, updated_at = ? WHERE provider = ?')
        .bind(folderId, new Date().toISOString(), BACKUP_PROVIDER).run();
    }

    const uploaded = await uploadDriveBackup(accessToken, folderId, backup.fileName, backup.bytes);
    fileId = String(uploaded.id || '');
    if (!fileId) throw new BackupError('Google Drive 未回傳備份檔案 ID。', 'UPLOAD_NO_FILE_ID');

    const downloaded = await downloadDriveFile(accessToken, fileId);
    const remoteSha = await sha256HexBytes(downloaded);
    if (remoteSha !== backup.fileSha256 || downloaded.byteLength !== backup.bytes.byteLength) {
      try { await driveDelete(accessToken, fileId); } catch { /* best effort */ }
      throw new BackupError('Google Drive 回讀驗證失敗，備份檔案已視為無效。', 'REMOTE_VERIFY_FAILED');
    }

    try {
      await cleanupDriveBackups(accessToken, folderId, BACKUP_RETENTION);
    } catch (cleanupError) {
      console.warn('cyaccounting_backup_retention_cleanup_failed', safeErrorCode(cleanupError));
    }

    const completedAt = new Date().toISOString();
    await env.DB.prepare(`
      INSERT INTO backup_runs(
        provider, trigger_kind, status, started_at, completed_at, file_id, file_name,
        data_sha256, file_sha256, row_count, byte_size, error_message
      ) VALUES (?, ?, 'success', ?, ?, ?, ?, ?, ?, ?, ?, '')
    `).bind(
      BACKUP_PROVIDER, triggerKind, startedAt, completedAt, fileId, backup.fileName,
      backup.dataSha256, backup.fileSha256, backup.totalRowCount, backup.bytes.byteLength
    ).run();

    return {
      ok: true,
      fileId,
      fileName: backup.fileName,
      completedAt,
      dataSha256: backup.dataSha256,
      fileSha256: backup.fileSha256,
      rowCount: backup.totalRowCount,
      byteSize: backup.bytes.byteLength
    };
  } catch (error) {
    const completedAt = new Date().toISOString();
    try {
      await env.DB.prepare(`
        INSERT INTO backup_runs(
          provider, trigger_kind, status, started_at, completed_at, file_id, file_name,
          data_sha256, file_sha256, row_count, byte_size, error_message
        ) VALUES (?, ?, 'failed', ?, ?, ?, ?, ?, ?, ?, ?, ?)
      `).bind(
        BACKUP_PROVIDER, triggerKind, startedAt, completedAt, fileId, fileName,
        backup?.dataSha256 || '', backup?.fileSha256 || '', backup?.totalRowCount || 0,
        backup?.bytes?.byteLength || 0, truncateError(safeBackupMessage(error))
      ).run();
    } catch (logError) {
      console.error('cyaccounting_backup_failure_log_failed', safeErrorCode(logError));
    }
    throw error;
  }
}

export async function buildBackupPackage(db, now = new Date()) {
  const [schemaRow, accounts, groups, categories, transactions, openings, settings] = await Promise.all([
    db.prepare("SELECT value FROM meta WHERE key = 'schema_version'").first(),
    readPaged(db, 'SELECT id, name, sort_order, is_default, created_at FROM accounts ORDER BY sort_order, id', mapAccount),
    readPaged(db, 'SELECT id, kind, name, sort_order, created_at FROM category_groups ORDER BY kind, sort_order, id', mapGroup),
    readPaged(db, 'SELECT id, kind, group_id, name, sort_order, is_favorite, created_at FROM categories ORDER BY kind, group_id, sort_order, id', mapCategory),
    readPaged(db, 'SELECT id, tx_date, account_name, kind, category_name, summary, amount, created_at, updated_at FROM transactions ORDER BY id', mapTransaction),
    readPaged(db, 'SELECT month, account_name, amount, created_at, updated_at FROM opening_balances ORDER BY month, account_name', mapOpening),
    readPaged(db, 'SELECT key, value FROM app_settings ORDER BY key', mapSetting)
  ]);

  const data = {
    accounts,
    categoryGroups: groups,
    categories,
    openingBalances: openings,
    transactions,
    appSettings: settings
  };
  const counts = Object.fromEntries(Object.entries(data).map(([key, rows]) => [key, rows.length]));
  const totalRowCount = Object.values(counts).reduce((sum, count) => sum + count, 0);
  const canonicalData = JSON.stringify(data);
  const dataSha256 = await sha256HexBytes(encoder.encode(canonicalData));
  const createdAt = now.toISOString();
  const manifest = {
    format: BACKUP_FORMAT,
    formatVersion: BACKUP_FORMAT_VERSION,
    appVersion: APP_VERSION,
    schemaVersion: Number(schemaRow?.value || 0),
    createdAt,
    counts,
    totalRowCount,
    dataSha256
  };
  const payload = JSON.stringify({ manifest, data }, null, 2) + '\n';
  const bytes = encoder.encode(payload);
  const fileSha256 = await sha256HexBytes(bytes);
  const stamp = createdAt.replace(/[-:]/g, '').replace(/\.\d{3}Z$/, 'Z');
  const fileName = `${BACKUP_PREFIX}${stamp}.json`;
  return { manifest, data, bytes, dataSha256, fileSha256, totalRowCount, fileName };
}

async function readPaged(db, selectSql, mapper) {
  const rows = [];
  let offset = 0;
  while (true) {
    const result = await db.prepare(`${selectSql} LIMIT ? OFFSET ?`).bind(PAGE_SIZE, offset).all();
    const page = result.results || [];
    for (const row of page) rows.push(mapper(row));
    if (page.length < PAGE_SIZE) break;
    offset += page.length;
  }
  return rows;
}

function mapAccount(row) {
  return { id: Number(row.id), name: String(row.name), sortOrder: Number(row.sort_order), isDefault: Number(row.is_default), createdAt: String(row.created_at) };
}
function mapGroup(row) {
  return { id: Number(row.id), kind: String(row.kind), name: String(row.name), sortOrder: Number(row.sort_order), createdAt: String(row.created_at) };
}
function mapCategory(row) {
  return { id: Number(row.id), kind: String(row.kind), groupId: Number(row.group_id), name: String(row.name), sortOrder: Number(row.sort_order), isFavorite: Number(row.is_favorite), createdAt: String(row.created_at) };
}
function mapTransaction(row) {
  return {
    id: Number(row.id), txDate: String(row.tx_date), accountName: String(row.account_name), kind: String(row.kind),
    categoryName: String(row.category_name), summary: String(row.summary || ''), amount: Number(row.amount),
    createdAt: String(row.created_at), updatedAt: String(row.updated_at)
  };
}
function mapOpening(row) {
  return { month: String(row.month), accountName: String(row.account_name), amount: Number(row.amount), createdAt: String(row.created_at), updatedAt: String(row.updated_at) };
}
function mapSetting(row) {
  return { key: String(row.key), value: String(row.value) };
}

async function exchangeAuthorizationCode(env, code, redirectUri) {
  const response = await fetch('https://oauth2.googleapis.com/token', {
    method: 'POST',
    headers: { 'content-type': 'application/x-www-form-urlencoded' },
    body: new URLSearchParams({
      client_id: String(env.GOOGLE_DRIVE_CLIENT_ID),
      client_secret: String(env.GOOGLE_DRIVE_CLIENT_SECRET),
      code,
      grant_type: 'authorization_code',
      redirect_uri: redirectUri
    })
  });
  const data = await response.json().catch(() => ({}));
  if (!response.ok) throw new BackupError('Google OAuth 授權碼交換失敗。', 'OAUTH_TOKEN_EXCHANGE_FAILED');
  return data;
}

async function refreshAccessToken(env, refreshToken) {
  const response = await fetch('https://oauth2.googleapis.com/token', {
    method: 'POST',
    headers: { 'content-type': 'application/x-www-form-urlencoded' },
    body: new URLSearchParams({
      client_id: String(env.GOOGLE_DRIVE_CLIENT_ID),
      client_secret: String(env.GOOGLE_DRIVE_CLIENT_SECRET),
      refresh_token: refreshToken,
      grant_type: 'refresh_token'
    })
  });
  const data = await response.json().catch(() => ({}));
  const accessToken = String(data.access_token || '');
  if (!response.ok || !accessToken) throw new BackupError('Google Drive 授權已失效，請由超管重新連結。', 'REFRESH_TOKEN_FAILED');
  return accessToken;
}

async function driveAboutUser(accessToken) {
  const response = await driveFetch(accessToken, `${DRIVE_API}/about?fields=user(displayName,emailAddress)`);
  return response.user || {};
}

async function ensureDriveFolder(accessToken, existingFolderId) {
  if (existingFolderId) {
    try {
      const existing = await driveFetch(accessToken, `${DRIVE_API}/files/${encodeURIComponent(existingFolderId)}?fields=id,name,mimeType,trashed`);
      if (!existing.trashed && existing.mimeType === 'application/vnd.google-apps.folder') return existingFolderId;
    } catch { /* find/create below */ }
  }

  const q = `trashed=false and mimeType='application/vnd.google-apps.folder' and name='${BACKUP_FOLDER_NAME}'`;
  const params = new URLSearchParams({ q, spaces: 'drive', pageSize: '10', fields: 'files(id,name,mimeType)' });
  const found = await driveFetch(accessToken, `${DRIVE_API}/files?${params.toString()}`);
  if (found.files?.[0]?.id) return String(found.files[0].id);

  const created = await driveFetch(accessToken, `${DRIVE_API}/files?fields=id,name`, {
    method: 'POST',
    headers: { 'content-type': 'application/json; charset=utf-8' },
    body: JSON.stringify({ name: BACKUP_FOLDER_NAME, mimeType: 'application/vnd.google-apps.folder' })
  });
  if (!created.id) throw new BackupError('無法建立 Google Drive 備份資料夾。', 'DRIVE_FOLDER_CREATE_FAILED');
  return String(created.id);
}

async function uploadDriveBackup(accessToken, folderId, fileName, bytes) {
  const start = await fetch(`${DRIVE_UPLOAD_API}/files?uploadType=resumable&fields=id,name,size,md5Checksum`, {
    method: 'POST',
    headers: {
      authorization: `Bearer ${accessToken}`,
      'content-type': 'application/json; charset=utf-8',
      'x-upload-content-type': 'application/json; charset=utf-8',
      'x-upload-content-length': String(bytes.byteLength)
    },
    body: JSON.stringify({ name: fileName, parents: [folderId] })
  });
  if (!start.ok) throw new BackupError('Google Drive 無法建立備份上傳工作。', 'DRIVE_UPLOAD_START_FAILED');
  const location = start.headers.get('location');
  if (!location) throw new BackupError('Google Drive 未回傳上傳位置。', 'DRIVE_UPLOAD_NO_LOCATION');

  const upload = await fetch(location, {
    method: 'PUT',
    headers: { 'content-type': 'application/json; charset=utf-8', 'content-length': String(bytes.byteLength) },
    body: bytes
  });
  const data = await upload.json().catch(() => ({}));
  if (!upload.ok) throw new BackupError('Google Drive 備份上傳失敗。', 'DRIVE_UPLOAD_FAILED');
  return data;
}

async function downloadDriveFile(accessToken, fileId) {
  const response = await fetch(`${DRIVE_API}/files/${encodeURIComponent(fileId)}?alt=media`, {
    headers: { authorization: `Bearer ${accessToken}` }
  });
  if (!response.ok) throw new BackupError('Google Drive 備份回讀失敗。', 'DRIVE_VERIFY_DOWNLOAD_FAILED');
  return new Uint8Array(await response.arrayBuffer());
}

async function cleanupDriveBackups(accessToken, folderId, keep) {
  const files = [];
  let pageToken = '';
  const seen = new Set();
  do {
    const q = `trashed=false and '${folderId}' in parents and name contains '${BACKUP_PREFIX}'`;
    const params = new URLSearchParams({
      q, spaces: 'drive', pageSize: '100', orderBy: 'createdTime desc', fields: 'nextPageToken,files(id,name,createdTime)'
    });
    if (pageToken) params.set('pageToken', pageToken);
    const data = await driveFetch(accessToken, `${DRIVE_API}/files?${params.toString()}`);
    for (const file of data.files || []) {
      if (String(file.name || '').startsWith(BACKUP_PREFIX) && String(file.name || '').endsWith('.json')) files.push(file);
    }
    const next = String(data.nextPageToken || '');
    if (!next) break;
    if (seen.has(next)) break;
    seen.add(next);
    pageToken = next;
  } while (pageToken);

  files.sort((a, b) => String(b.createdTime || '').localeCompare(String(a.createdTime || '')) || String(b.name || '').localeCompare(String(a.name || '')));
  for (const file of files.slice(Math.max(0, keep))) {
    try { await driveDelete(accessToken, String(file.id)); } catch { /* cleanup should not invalidate successful backup */ }
  }
}

async function driveDelete(accessToken, fileId) {
  const response = await fetch(`${DRIVE_API}/files/${encodeURIComponent(fileId)}`, {
    method: 'DELETE', headers: { authorization: `Bearer ${accessToken}` }
  });
  if (!response.ok && response.status !== 404) throw new BackupError('Google Drive 清理舊備份失敗。', 'DRIVE_DELETE_FAILED');
}

async function driveFetch(accessToken, url, options = {}) {
  const headers = new Headers(options.headers || {});
  headers.set('authorization', `Bearer ${accessToken}`);
  const response = await fetch(url, { ...options, headers });
  const data = await response.json().catch(() => ({}));
  if (!response.ok) throw new BackupError('Google Drive API 連線失敗。', `DRIVE_HTTP_${response.status}`);
  return data;
}

async function getDriveIntegration(db) {
  return db.prepare(`
    SELECT provider, encrypted_refresh_token, folder_id, account_email, account_name, connected_at, updated_at
    FROM backup_integrations WHERE provider = ? LIMIT 1
  `).bind(BACKUP_PROVIDER).first();
}

export async function encryptSecret(plainText, encodedKey) {
  const key = await importTokenKey(encodedKey, ['encrypt']);
  const iv = new Uint8Array(12);
  crypto.getRandomValues(iv);
  const cipher = new Uint8Array(await crypto.subtle.encrypt({ name: 'AES-GCM', iv }, key, encoder.encode(String(plainText))));
  return `v1:${base64UrlEncode(iv)}:${base64UrlEncode(cipher)}`;
}

export async function decryptSecret(value, encodedKey) {
  const match = /^v1:([A-Za-z0-9_-]+):([A-Za-z0-9_-]+)$/.exec(String(value || ''));
  if (!match) throw new BackupError('加密的 Google 授權格式不正確。', 'TOKEN_FORMAT_INVALID');
  const key = await importTokenKey(encodedKey, ['decrypt']);
  try {
    const plain = await crypto.subtle.decrypt({ name: 'AES-GCM', iv: base64UrlDecode(match[1]) }, key, base64UrlDecode(match[2]));
    return decoder.decode(plain);
  } catch {
    throw new BackupError('Google 授權資訊無法解密。', 'TOKEN_DECRYPT_FAILED');
  }
}

async function importTokenKey(encodedKey, usages) {
  const raw = decodeBase64Key(encodedKey);
  if (raw.byteLength !== 32) throw new BackupError('Google Drive token encryption key 必須是 32 bytes。', 'TOKEN_KEY_INVALID');
  return crypto.subtle.importKey('raw', raw, 'AES-GCM', false, usages);
}

function backupSecretsReady(env) {
  try {
    return Boolean(
      String(env?.GOOGLE_DRIVE_CLIENT_ID || '').trim()
      && String(env?.GOOGLE_DRIVE_CLIENT_SECRET || '').trim()
      && decodeBase64Key(env?.GOOGLE_DRIVE_TOKEN_KEY).byteLength === 32
    );
  } catch {
    return false;
  }
}

function assertBackupSecrets(env) {
  if (!backupSecretsReady(env)) {
    throw new BackupError('Google Drive 備份尚未完成 Cloudflare Secrets 設定。', 'BACKUP_NOT_CONFIGURED');
  }
}

function decodeBase64Key(value) {
  const raw = String(value || '').trim().replace(/-/g, '+').replace(/_/g, '/');
  if (!raw) return new Uint8Array();
  const padded = raw + '='.repeat((4 - raw.length % 4) % 4);
  const binary = atob(padded);
  return Uint8Array.from(binary, char => char.charCodeAt(0));
}

function base64UrlEncode(bytes) {
  let binary = '';
  for (const byte of bytes) binary += String.fromCharCode(byte);
  return btoa(binary).replaceAll('+', '-').replaceAll('/', '_').replaceAll('=', '');
}

function base64UrlDecode(value) {
  return decodeBase64Key(value);
}

async function sha256HexText(value) {
  return sha256HexBytes(encoder.encode(String(value)));
}

async function sha256HexBytes(bytes) {
  const digest = new Uint8Array(await crypto.subtle.digest('SHA-256', bytes));
  return Array.from(digest, byte => byte.toString(16).padStart(2, '0')).join('');
}

function randomToken(size) {
  const bytes = new Uint8Array(size);
  crypto.getRandomValues(bytes);
  return base64UrlEncode(bytes);
}

function normalizeRunRow(row) {
  return {
    id: Number(row.id),
    trigger: row.trigger_kind || undefined,
    status: row.status || 'success',
    startedAt: row.started_at || undefined,
    completedAt: row.completed_at || null,
    fileName: row.file_name || '',
    dataSha256: row.data_sha256 || '',
    fileSha256: row.file_sha256 || '',
    rowCount: Number(row.row_count || 0),
    byteSize: Number(row.byte_size || 0),
    errorMessage: row.error_message || ''
  };
}

function redirectBackupResult(request, result, detail) {
  const url = new URL(request.url);
  url.pathname = '/';
  url.search = '';
  url.searchParams.set('backup', result);
  if (detail) url.searchParams.set('detail', detail);
  return Response.redirect(url.toString(), 302);
}

function truncateError(value) {
  return String(value || '').slice(0, 500);
}

function safeBackupMessage(error) {
  if (error instanceof BackupError) return error.message;
  return 'Google Drive 備份處理失敗，請稍後再試。';
}

function safeErrorCode(error) {
  return error instanceof BackupError ? error.code : 'BACKUP_FAILED';
}

class BackupError extends Error {
  constructor(message, code) {
    super(message);
    this.name = 'BackupError';
    this.code = code;
  }
}

function json(data, status = 200) {
  return new Response(JSON.stringify(data), {
    status,
    headers: { 'content-type': 'application/json; charset=utf-8', 'cache-control': 'no-store' }
  });
}
