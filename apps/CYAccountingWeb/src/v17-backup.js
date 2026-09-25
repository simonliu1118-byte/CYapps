import { buildBackupPackage } from './v16-backup.js';

const BACKUP_PROVIDER = 'google_cloud_storage';
const APP_VERSION = '0.17.0';
const BACKUP_RETENTION_DAYS = 14;
const BACKUP_OBJECT_PREFIX = 'CYAccountingWeb/';
const TOKEN_URI = 'https://oauth2.googleapis.com/token';
const STORAGE_SCOPE = 'https://www.googleapis.com/auth/devstorage.read_write';
const STORAGE_API = 'https://storage.googleapis.com/storage/v1';
const STORAGE_UPLOAD_API = 'https://storage.googleapis.com/upload/storage/v1';
const encoder = new TextEncoder();
const decoder = new TextDecoder();

export async function handleV17Api(request, env, session) {
  const url = new URL(request.url);
  if (!url.pathname.startsWith('/api/backup/')) return null;
  if (String(session?.role || '') !== 'SUPER_ADMIN') {
    return json({ ok: false, error: '只有超級管理員可以管理備份與復原。', code: 'SUPER_ADMIN_REQUIRED' }, 403);
  }

  if (url.pathname === '/api/backup/status' && request.method === 'GET') {
    return backupStatus(env);
  }
  if (url.pathname === '/api/backup/run' && request.method === 'POST') {
    try {
      const result = await runGcsBackup(env, 'manual');
      return json({ ok: true, backup: result });
    } catch (error) {
      return json({ ok: false, error: safeBackupMessage(error), code: safeErrorCode(error) }, 500);
    }
  }
  return null;
}

export async function runScheduledBackup(env) {
  try {
    if (!env?.DB || !gcsConfigReady(env)) return { skipped: true, reason: 'not_configured' };
    return await runGcsBackup(env, 'scheduled');
  } catch (error) {
    console.error('cyaccounting_scheduled_backup_failed', safeErrorCode(error));
    return { ok: false, error: safeErrorCode(error) };
  }
}

async function backupStatus(env) {
  const configured = gcsConfigReady(env);
  const [latestSuccess, recentRuns] = await Promise.all([
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
    provider: BACKUP_PROVIDER,
    configured,
    bucketName: configured ? String(env.GCS_BUCKET_NAME || '') : '',
    serviceAccountEmail: configured ? String(env.GCS_SERVICE_ACCOUNT_EMAIL || '') : '',
    schedule: { cron: '30 19 * * *', localTime: '每日 03:30（台灣時間）' },
    retentionDays: BACKUP_RETENTION_DAYS,
    latestSuccess: latestSuccess ? normalizeRunRow(latestSuccess) : null,
    recentRuns: (recentRuns.results || []).map(normalizeRunRow),
    restoreAvailable: false
  });
}

export async function runGcsBackup(env, triggerKind = 'scheduled') {
  if (!['scheduled', 'manual'].includes(triggerKind)) throw new BackupError('備份觸發類型錯誤。', 'INVALID_TRIGGER');
  assertGcsConfig(env);

  const startedAt = new Date().toISOString();
  let backup = null;
  let objectName = '';

  try {
    backup = await buildV17BackupPackage(env.DB, new Date());
    objectName = `${BACKUP_OBJECT_PREFIX}${backup.fileName}`;
    const accessToken = await serviceAccountAccessToken(env);
    const bucketName = String(env.GCS_BUCKET_NAME).trim();

    const uploaded = await uploadGcsBackup(accessToken, bucketName, objectName, backup.bytes);
    const uploadedName = String(uploaded.name || objectName);
    const downloaded = await downloadGcsObject(accessToken, bucketName, uploadedName);
    const remoteSha = await sha256HexBytes(downloaded);
    if (remoteSha !== backup.fileSha256 || downloaded.byteLength !== backup.bytes.byteLength) {
      try { await deleteGcsObject(accessToken, bucketName, uploadedName, uploaded.generation); } catch { /* best effort */ }
      throw new BackupError('Cloud Storage 回讀驗證失敗，備份檔案已視為無效。', 'REMOTE_VERIFY_FAILED');
    }

    try {
      await cleanupExpiredGcsBackups(accessToken, bucketName, BACKUP_RETENTION_DAYS, new Date());
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
      BACKUP_PROVIDER, triggerKind, startedAt, completedAt, uploadedName, backup.fileName,
      backup.dataSha256, backup.fileSha256, backup.totalRowCount, backup.bytes.byteLength
    ).run();

    return {
      ok: true,
      objectName: uploadedName,
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
        BACKUP_PROVIDER, triggerKind, startedAt, completedAt, objectName, backup?.fileName || '',
        backup?.dataSha256 || '', backup?.fileSha256 || '', backup?.totalRowCount || 0,
        backup?.bytes?.byteLength || 0, truncateError(safeBackupMessage(error))
      ).run();
    } catch (logError) {
      console.error('cyaccounting_backup_failure_log_failed', safeErrorCode(logError));
    }
    throw error;
  }
}

async function buildV17BackupPackage(db, now = new Date()) {
  const base = await buildBackupPackage(db, now);
  const parsed = JSON.parse(decoder.decode(base.bytes));
  parsed.manifest.appVersion = APP_VERSION;
  const payload = JSON.stringify(parsed, null, 2) + '\n';
  const bytes = encoder.encode(payload);
  const fileSha256 = await sha256HexBytes(bytes);
  return {
    ...base,
    manifest: parsed.manifest,
    data: parsed.data,
    bytes,
    fileSha256
  };
}

export function gcsConfigReady(env) {
  try {
    const email = String(env?.GCS_SERVICE_ACCOUNT_EMAIL || '').trim();
    const bucket = String(env?.GCS_BUCKET_NAME || '').trim();
    const key = normalizePrivateKeyPem(env?.GCS_PRIVATE_KEY);
    return Boolean(email.includes('@') && bucket.length >= 3 && bucket.length <= 222
      && key.includes('-----BEGIN PRIVATE KEY-----') && key.includes('-----END PRIVATE KEY-----'));
  } catch {
    return false;
  }
}

function assertGcsConfig(env) {
  if (!gcsConfigReady(env)) {
    throw new BackupError('Google Cloud Storage 備份尚未完成 Cloudflare Secrets 設定。', 'BACKUP_NOT_CONFIGURED');
  }
}

export async function createServiceAccountJwt(env, now = new Date()) {
  assertGcsConfig(env);
  const issuedAt = Math.floor(now.getTime() / 1000) - 5;
  const header = { alg: 'RS256', typ: 'JWT' };
  const claims = {
    iss: String(env.GCS_SERVICE_ACCOUNT_EMAIL).trim(),
    scope: STORAGE_SCOPE,
    aud: TOKEN_URI,
    iat: issuedAt,
    exp: issuedAt + 3600
  };
  const unsigned = `${base64UrlEncodeText(JSON.stringify(header))}.${base64UrlEncodeText(JSON.stringify(claims))}`;
  const key = await importServiceAccountPrivateKey(env.GCS_PRIVATE_KEY);
  const signature = new Uint8Array(await crypto.subtle.sign(
    { name: 'RSASSA-PKCS1-v1_5' },
    key,
    encoder.encode(unsigned)
  ));
  return `${unsigned}.${base64UrlEncodeBytes(signature)}`;
}

async function serviceAccountAccessToken(env) {
  const assertion = await createServiceAccountJwt(env);
  const response = await fetch(TOKEN_URI, {
    method: 'POST',
    headers: { 'content-type': 'application/x-www-form-urlencoded' },
    body: new URLSearchParams({
      grant_type: 'urn:ietf:params:oauth:grant-type:jwt-bearer',
      assertion
    })
  });
  const data = await response.json().catch(() => ({}));
  const accessToken = String(data.access_token || '');
  if (!response.ok || !accessToken) {
    throw new BackupError('Google Cloud Service Account 驗證失敗。', 'GCS_AUTH_FAILED');
  }
  return accessToken;
}

async function uploadGcsBackup(accessToken, bucketName, objectName, bytes) {
  const params = new URLSearchParams({ uploadType: 'media', name: objectName, ifGenerationMatch: '0' });
  const response = await fetch(`${STORAGE_UPLOAD_API}/b/${encodeURIComponent(bucketName)}/o?${params.toString()}`, {
    method: 'POST',
    headers: {
      authorization: `Bearer ${accessToken}`,
      'content-type': 'application/json; charset=utf-8'
    },
    body: bytes
  });
  const data = await response.json().catch(() => ({}));
  if (!response.ok) throw new BackupError('Google Cloud Storage 備份上傳失敗。', `GCS_UPLOAD_${response.status}`);
  if (!data.name) throw new BackupError('Google Cloud Storage 未回傳物件名稱。', 'GCS_UPLOAD_NO_OBJECT');
  return data;
}

async function downloadGcsObject(accessToken, bucketName, objectName) {
  const response = await fetch(`${STORAGE_API}/b/${encodeURIComponent(bucketName)}/o/${encodeURIComponent(objectName)}?alt=media`, {
    headers: { authorization: `Bearer ${accessToken}` }
  });
  if (!response.ok) throw new BackupError('Google Cloud Storage 備份回讀失敗。', `GCS_DOWNLOAD_${response.status}`);
  return new Uint8Array(await response.arrayBuffer());
}

async function listGcsBackupObjects(accessToken, bucketName) {
  const items = [];
  let pageToken = '';
  const prefix = `${BACKUP_OBJECT_PREFIX}CYAccountingWeb_backup_`;
  do {
    const params = new URLSearchParams({
      prefix,
      maxResults: '1000',
      fields: 'nextPageToken,items(name,timeCreated,generation,size)'
    });
    if (pageToken) params.set('pageToken', pageToken);
    const response = await fetch(`${STORAGE_API}/b/${encodeURIComponent(bucketName)}/o?${params.toString()}`, {
      headers: { authorization: `Bearer ${accessToken}` }
    });
    const data = await response.json().catch(() => ({}));
    if (!response.ok) throw new BackupError('Google Cloud Storage 無法列出備份。', `GCS_LIST_${response.status}`);
    for (const item of data.items || []) {
      const name = String(item.name || '');
      if (name.startsWith(prefix) && name.endsWith('.json')) items.push(item);
    }
    pageToken = String(data.nextPageToken || '');
  } while (pageToken);
  return items;
}

async function cleanupExpiredGcsBackups(accessToken, bucketName, retentionDays, now) {
  const cutoff = now.getTime() - retentionDays * 24 * 60 * 60 * 1000;
  const items = await listGcsBackupObjects(accessToken, bucketName);
  for (const item of items) {
    const created = Date.parse(String(item.timeCreated || ''));
    if (!Number.isFinite(created) || created >= cutoff) continue;
    try {
      await deleteGcsObject(accessToken, bucketName, String(item.name || ''), item.generation);
    } catch {
      // Cleanup failure must not invalidate an already verified backup.
    }
  }
}

async function deleteGcsObject(accessToken, bucketName, objectName, generation = '') {
  const params = new URLSearchParams();
  if (generation) params.set('generation', String(generation));
  const suffix = params.size ? `?${params.toString()}` : '';
  const response = await fetch(`${STORAGE_API}/b/${encodeURIComponent(bucketName)}/o/${encodeURIComponent(objectName)}${suffix}`, {
    method: 'DELETE',
    headers: { authorization: `Bearer ${accessToken}` }
  });
  if (!response.ok && response.status !== 404) {
    throw new BackupError('Google Cloud Storage 清理舊備份失敗。', `GCS_DELETE_${response.status}`);
  }
}

async function importServiceAccountPrivateKey(value) {
  const pem = normalizePrivateKeyPem(value);
  const base64 = pem
    .replace('-----BEGIN PRIVATE KEY-----', '')
    .replace('-----END PRIVATE KEY-----', '')
    .replace(/\s+/g, '');
  if (!base64) throw new BackupError('Google Cloud Service Account 私鑰格式錯誤。', 'GCS_PRIVATE_KEY_INVALID');
  let der;
  try {
    der = Uint8Array.from(atob(base64), char => char.charCodeAt(0));
  } catch {
    throw new BackupError('Google Cloud Service Account 私鑰格式錯誤。', 'GCS_PRIVATE_KEY_INVALID');
  }
  try {
    return await crypto.subtle.importKey(
      'pkcs8',
      der,
      { name: 'RSASSA-PKCS1-v1_5', hash: 'SHA-256' },
      false,
      ['sign']
    );
  } catch {
    throw new BackupError('Google Cloud Service Account 私鑰無法載入。', 'GCS_PRIVATE_KEY_INVALID');
  }
}

export function normalizePrivateKeyPem(value) {
  return String(value || '').trim().replace(/\\n/g, '\n').replace(/\r\n/g, '\n');
}

function base64UrlEncodeText(value) {
  return base64UrlEncodeBytes(encoder.encode(String(value)));
}

function base64UrlEncodeBytes(bytes) {
  let binary = '';
  for (const byte of bytes) binary += String.fromCharCode(byte);
  return btoa(binary).replaceAll('+', '-').replaceAll('/', '_').replaceAll('=', '');
}

async function sha256HexBytes(bytes) {
  const digest = new Uint8Array(await crypto.subtle.digest('SHA-256', bytes));
  return Array.from(digest, byte => byte.toString(16).padStart(2, '0')).join('');
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

function truncateError(value) {
  return String(value || '').slice(0, 500);
}

function safeBackupMessage(error) {
  if (error instanceof BackupError) return error.message;
  return 'Google Cloud Storage 備份處理失敗，請稍後再試。';
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
