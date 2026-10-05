export function r2ConfigReady(env) {
  const bucket = env?.BACKUP_R2;
  return Boolean(
    bucket
    && typeof bucket.put === 'function'
    && typeof bucket.get === 'function'
    && typeof bucket.list === 'function'
    && typeof bucket.delete === 'function'
  );
}

export function createR2BackupStorageProvider(env) {
  const bucket = env?.BACKUP_R2;
  if (!r2ConfigReady(env)) {
    throw new R2ProviderError('Cloudflare R2 備份尚未完成 bucket binding 設定。', 'R2_NOT_CONFIGURED');
  }

  async function putObject(key, bytes, metadata = {}) {
    let object;
    try {
      object = await bucket.put(String(key), bytes, {
        customMetadata: normalizeMetadata(metadata)
      });
    } catch {
      throw new R2ProviderError('Cloudflare R2 備份上傳失敗。', 'R2_UPLOAD_FAILED');
    }
    if (!object) {
      throw new R2ProviderError('Cloudflare R2 未回傳上傳物件資訊。', 'R2_UPLOAD_NO_OBJECT');
    }
    return normalizeObject(object);
  }

  async function getObject(key) {
    let object;
    try {
      object = await bucket.get(String(key));
    } catch {
      throw new R2ProviderError('Cloudflare R2 備份回讀失敗。', 'R2_DOWNLOAD_FAILED');
    }
    if (!object || typeof object.arrayBuffer !== 'function') {
      throw new R2ProviderError('Cloudflare R2 找不到備份物件。', 'R2_OBJECT_NOT_FOUND');
    }
    return new Uint8Array(await object.arrayBuffer());
  }

  async function listObjects(prefix) {
    const objects = [];
    let cursor = undefined;
    const seen = new Set();
    do {
      let page;
      try {
        page = await bucket.list({
          prefix: String(prefix || ''),
          limit: 1000,
          cursor,
          include: ['customMetadata']
        });
      } catch {
        throw new R2ProviderError('Cloudflare R2 無法列出備份。', 'R2_LIST_FAILED');
      }
      for (const item of page?.objects || []) objects.push(normalizeObject(item));
      const next = page?.truncated ? String(page.cursor || '') : '';
      if (!next || seen.has(next)) break;
      seen.add(next);
      cursor = next;
    } while (cursor);
    return objects;
  }

  async function deleteObject(key, _versionToken = '') {
    try {
      await bucket.delete(String(key));
    } catch {
      throw new R2ProviderError('Cloudflare R2 清理舊備份失敗。', 'R2_DELETE_FAILED');
    }
  }

  return Object.freeze({
    kind: 'cloudflare_r2',
    putObject,
    getObject,
    listObjects,
    deleteObject
  });
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
    key: String(item?.key || ''),
    byteSize: Number(item?.size || 0),
    timeCreated: item?.uploaded instanceof Date
      ? item.uploaded.toISOString()
      : String(item?.uploaded || ''),
    versionToken: String(item?.version || ''),
    metadata: item?.customMetadata && typeof item.customMetadata === 'object'
      ? item.customMetadata
      : {}
  };
}

export class R2ProviderError extends Error {
  constructor(message, code) {
    super(message);
    this.name = 'R2ProviderError';
    this.code = code;
  }
}
