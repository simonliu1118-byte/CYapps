const TOKEN_URI = 'https://oauth2.googleapis.com/token';
const STORAGE_SCOPE = 'https://www.googleapis.com/auth/devstorage.read_write';
const STORAGE_API = 'https://storage.googleapis.com/storage/v1';
const STORAGE_UPLOAD_API = 'https://storage.googleapis.com/upload/storage/v1';
const encoder = new TextEncoder();

export function gcsConfigReady(env) {
  try {
    readGcsConfig(env);
    return true;
  } catch {
    return false;
  }
}

export function readGcsConfig(env) {
  const bucket = String(env?.GCS_BUCKET || '').trim();
  const rawJson = String(env?.GCS_SERVICE_ACCOUNT_JSON || '').trim();
  if (!bucket || bucket.length > 222 || !rawJson) {
    throw new GcsProviderError('Google Cloud Storage 備份尚未完成 Cloudflare Secrets 設定。', 'BACKUP_NOT_CONFIGURED');
  }

  let credentials;
  try {
    credentials = JSON.parse(rawJson);
  } catch {
    throw new GcsProviderError('Google Cloud Service Account JSON 格式錯誤。', 'GCS_SERVICE_ACCOUNT_JSON_INVALID');
  }

  const clientEmail = String(credentials?.client_email || '').trim();
  const privateKey = normalizePrivateKeyPem(credentials?.private_key);
  if (credentials?.type !== 'service_account' || !clientEmail.includes('@')
      || !privateKey.includes('-----BEGIN PRIVATE KEY-----')
      || !privateKey.includes('-----END PRIVATE KEY-----')) {
    throw new GcsProviderError('Google Cloud Service Account JSON 缺少必要欄位。', 'GCS_SERVICE_ACCOUNT_JSON_INVALID');
  }

  return {
    bucket,
    clientEmail,
    privateKey
  };
}

export function createGcsBackupStorageProvider(env, options = {}) {
  const config = readGcsConfig(env);
  const fetchImpl = options.fetchImpl || fetch;
  let tokenCache = null;

  async function accessToken() {
    const now = Date.now();
    if (tokenCache && tokenCache.expiresAt > now + 60_000) return tokenCache.value;

    const assertion = await createServiceAccountJwt(config, new Date(now));
    const response = await fetchImpl(TOKEN_URI, {
      method: 'POST',
      headers: { 'content-type': 'application/x-www-form-urlencoded' },
      body: new URLSearchParams({
        grant_type: 'urn:ietf:params:oauth:grant-type:jwt-bearer',
        assertion
      })
    });
    const data = await response.json().catch(() => ({}));
    const value = String(data.access_token || '');
    if (!response.ok || !value) {
      throw new GcsProviderError('Google Cloud Service Account 驗證失敗。', 'GCS_AUTH_FAILED');
    }
    const expiresIn = Math.max(60, Number(data.expires_in || 3600));
    tokenCache = { value, expiresAt: now + expiresIn * 1000 };
    return value;
  }

  async function putObject(key, bytes, metadata = {}) {
    const token = await accessToken();
    const startUrl = `${STORAGE_UPLOAD_API}/b/${encodeURIComponent(config.bucket)}/o?uploadType=resumable&ifGenerationMatch=0`;
    const start = await fetchImpl(startUrl, {
      method: 'POST',
      headers: {
        authorization: `Bearer ${token}`,
        'content-type': 'application/json; charset=utf-8',
        'x-upload-content-type': 'application/json; charset=utf-8',
        'x-upload-content-length': String(bytes.byteLength)
      },
      body: JSON.stringify({
        name: String(key),
        metadata: normalizeMetadata(metadata)
      })
    });
    if (!start.ok) {
      throw new GcsProviderError('Google Cloud Storage 無法建立上傳工作。', `GCS_UPLOAD_START_${start.status}`);
    }
    const location = start.headers.get('location');
    if (!location) {
      throw new GcsProviderError('Google Cloud Storage 未回傳上傳位置。', 'GCS_UPLOAD_NO_LOCATION');
    }

    const upload = await fetchImpl(location, {
      method: 'PUT',
      headers: {
        authorization: `Bearer ${token}`,
        'content-type': 'application/json; charset=utf-8',
        'content-length': String(bytes.byteLength)
      },
      body: bytes
    });
    const data = await upload.json().catch(() => ({}));
    if (!upload.ok) {
      throw new GcsProviderError('Google Cloud Storage 備份上傳失敗。', `GCS_UPLOAD_${upload.status}`);
    }
    if (!data.name) {
      throw new GcsProviderError('Google Cloud Storage 未回傳物件名稱。', 'GCS_UPLOAD_NO_OBJECT');
    }
    return normalizeObject(data);
  }

  async function getObject(key) {
    const token = await accessToken();
    const response = await fetchImpl(
      `${STORAGE_API}/b/${encodeURIComponent(config.bucket)}/o/${encodeURIComponent(String(key))}?alt=media`,
      { headers: { authorization: `Bearer ${token}` } }
    );
    if (!response.ok) {
      throw new GcsProviderError('Google Cloud Storage 備份回讀失敗。', `GCS_DOWNLOAD_${response.status}`);
    }
    return new Uint8Array(await response.arrayBuffer());
  }

  async function listObjects(prefix) {
    const token = await accessToken();
    const items = [];
    let pageToken = '';
    const seen = new Set();
    do {
      const params = new URLSearchParams({
        prefix: String(prefix || ''),
        maxResults: '1000',
        fields: 'nextPageToken,items(name,timeCreated,generation,size,metadata)'
      });
      if (pageToken) params.set('pageToken', pageToken);
      const response = await fetchImpl(
        `${STORAGE_API}/b/${encodeURIComponent(config.bucket)}/o?${params.toString()}`,
        { headers: { authorization: `Bearer ${token}` } }
      );
      const data = await response.json().catch(() => ({}));
      if (!response.ok) {
        throw new GcsProviderError('Google Cloud Storage 無法列出備份。', `GCS_LIST_${response.status}`);
      }
      for (const item of data.items || []) items.push(normalizeObject(item));
      const next = String(data.nextPageToken || '');
      if (!next || seen.has(next)) break;
      seen.add(next);
      pageToken = next;
    } while (pageToken);
    return items;
  }

  async function deleteObject(key, generation = '') {
    const token = await accessToken();
    const params = new URLSearchParams();
    if (generation) params.set('generation', String(generation));
    const suffix = params.size ? `?${params.toString()}` : '';
    const response = await fetchImpl(
      `${STORAGE_API}/b/${encodeURIComponent(config.bucket)}/o/${encodeURIComponent(String(key))}${suffix}`,
      {
        method: 'DELETE',
        headers: { authorization: `Bearer ${token}` }
      }
    );
    if (!response.ok && response.status !== 404) {
      throw new GcsProviderError('Google Cloud Storage 清理舊備份失敗。', `GCS_DELETE_${response.status}`);
    }
  }

  return Object.freeze({
    kind: 'google_cloud_storage',
    putObject,
    getObject,
    listObjects,
    deleteObject
  });
}

export async function createServiceAccountJwt(configOrEnv, now = new Date()) {
  const config = configOrEnv?.GCS_SERVICE_ACCOUNT_JSON ? readGcsConfig(configOrEnv) : configOrEnv;
  const issuedAt = Math.floor(now.getTime() / 1000) - 5;
  const header = { alg: 'RS256', typ: 'JWT' };
  const claims = {
    iss: String(config.clientEmail),
    scope: STORAGE_SCOPE,
    aud: TOKEN_URI,
    iat: issuedAt,
    exp: issuedAt + 3600
  };
  const unsigned = `${base64UrlEncodeText(JSON.stringify(header))}.${base64UrlEncodeText(JSON.stringify(claims))}`;
  const key = await importServiceAccountPrivateKey(config.privateKey);
  const signature = new Uint8Array(await crypto.subtle.sign(
    { name: 'RSASSA-PKCS1-v1_5' },
    key,
    encoder.encode(unsigned)
  ));
  return `${unsigned}.${base64UrlEncodeBytes(signature)}`;
}

export function normalizePrivateKeyPem(value) {
  return String(value || '').trim().replace(/\\n/g, '\n').replace(/\r\n/g, '\n');
}

async function importServiceAccountPrivateKey(value) {
  const pem = normalizePrivateKeyPem(value);
  const base64 = pem
    .replace('-----BEGIN PRIVATE KEY-----', '')
    .replace('-----END PRIVATE KEY-----', '')
    .replace(/\s+/g, '');
  if (!base64) {
    throw new GcsProviderError('Google Cloud Service Account 私鑰格式錯誤。', 'GCS_PRIVATE_KEY_INVALID');
  }
  let der;
  try {
    der = Uint8Array.from(atob(base64), char => char.charCodeAt(0));
  } catch {
    throw new GcsProviderError('Google Cloud Service Account 私鑰格式錯誤。', 'GCS_PRIVATE_KEY_INVALID');
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
    throw new GcsProviderError('Google Cloud Service Account 私鑰無法載入。', 'GCS_PRIVATE_KEY_INVALID');
  }
}

function normalizeMetadata(metadata) {
  const result = {};
  for (const [key, value] of Object.entries(metadata || {})) {
    if (value === undefined || value === null) continue;
    result[String(key)] = String(value);
  }
  return result;
}

function normalizeObject(item) {
  return {
    key: String(item?.name || ''),
    timeCreated: String(item?.timeCreated || ''),
    generation: String(item?.generation || ''),
    size: Number(item?.size || 0),
    metadata: item?.metadata && typeof item.metadata === 'object' ? item.metadata : {}
  };
}

function base64UrlEncodeText(value) {
  return base64UrlEncodeBytes(encoder.encode(String(value)));
}

function base64UrlEncodeBytes(bytes) {
  let binary = '';
  for (const byte of bytes) binary += String.fromCharCode(byte);
  return btoa(binary).replaceAll('+', '-').replaceAll('/', '_').replaceAll('=', '');
}

export class GcsProviderError extends Error {
  constructor(message, code) {
    super(message);
    this.name = 'GcsProviderError';
    this.code = code;
  }
}
