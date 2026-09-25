/**
 * Provider-neutral storage boundary used by CYAccountingWeb backup services.
 *
 * A provider implementation must expose:
 * - putObject(key, bytes, metadata)
 * - getObject(key)
 * - listObjects(prefix)
 * - deleteObject(key, generation)
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
