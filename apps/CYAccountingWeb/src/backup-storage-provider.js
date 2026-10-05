/**
 * Provider-neutral storage boundary used by CYAccountingWeb backup services.
 *
 * A provider implementation must expose:
 * - putObject(key, bytes, metadata)
 * - getObject(key)
 * - listObjects(prefix)
 * - deleteObject(key, versionToken?)
 *
 * Provider-facing object metadata must be normalized before it reaches domain
 * code. `versionToken` is intentionally opaque: GCS may map it to a generation,
 * while another provider may use a different token or no token at all.
 */
export function assertBackupStorageProvider(provider) {
  const required = ['putObject', 'getObject', 'listObjects', 'deleteObject'];
  if (!provider || typeof provider !== 'object') {
    throw new Error('BackupStorageProvider is required.');
  }
  for (const method of required) {
    if (typeof provider[method] !== 'function') {
      throw new Error(`BackupStorageProvider.${method} is required.`);
    }
  }
  return provider;
}
