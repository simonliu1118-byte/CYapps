// One-shot, read-only incident audit. Remove this script and its CI step after
// inspecting the encrypted result. No credentials or plaintext data are logged.
import { createCipheriv, publicEncrypt, randomBytes, constants } from 'node:crypto';
import { gzipSync } from 'node:zlib';

const publicKey = "-----BEGIN PUBLIC KEY-----\nMIIBojANBgkqhkiG9w0BAQEFAAOCAY8AMIIBigKCAYEAtxGmbxO4wE8a6WPf8wj7\nS+2RsouvtzQ1bugYBCmIMYTLBdiHNvnxVyM3e3c3kRlnHMnhHeQOcHtomKhGRleL\nWyftZcmBsEH0aCRVyZKeTUbK9qBLaXVM9MPLmXqhzgE3IEuO2Eja0eUkfODqtyKf\n1Y7wtt3rBJx54Pcv/n+16WJkCInOiyEY4B/tXH8MoXf6muU5xizgCEObJCpyJcnX\nqmFcUKY5JKmacLuhE0u4tZoAMXvijzuiYZ64uxI/pf/tMKMG2PDNnSVjmsUXx193\n7JI/O70YUzyeRoXC/qeGZaoRopKqkqX5pKlGyu5LZvtwjFGZgciQ4g7aDoMEpuvh\ng1/WLHNL19LWR/dcWXXivy814HZPtfNPXjp9HBvUbV5rHvJKRrUMQM48cmtmzxcL\nLHPM7z8i4l5n4o7M0nuf8vEx3RHqW/0wNm/NBqPr5J83r2pLixgednR+Uw2hlydZ\nMlIlHiFNQ2bqom2cbDcF+jy1clBdRQ+NiOi1Y2uwn60BAgMBAAE=\n-----END PUBLIC KEY-----\n";
const queries = [
  ['counts', 'SELECT COUNT(*) AS total, MIN(tx_date) AS first_date, MAX(tx_date) AS last_date FROM transactions'],
  ['byMonthAccount', "SELECT substr(tx_date,1,7) AS month, account_name, COUNT(*) AS count, SUM(CASE WHEN kind='income' THEN amount ELSE 0 END) AS income, SUM(CASE WHEN kind='expense' THEN amount ELSE 0 END) AS expense, MIN(created_at) AS first_created, MAX(updated_at) AS last_updated FROM transactions GROUP BY substr(tx_date,1,7), account_name ORDER BY month, account_name"],
  ['normalizedDates', "SELECT substr(replace(trim(tx_date), '/', '-'),1,7) AS month, account_name, COUNT(*) AS count, MIN(tx_date) AS min_date, MAX(tx_date) AS max_date, typeof(tx_date) AS date_type, length(tx_date) AS date_length FROM transactions GROUP BY month,account_name,date_type,date_length ORDER BY month,account_name"],
  ['accounts', 'SELECT name, archived_at FROM accounts ORDER BY id'],
  ['migrationCounts', "SELECT json_extract(j.value,'$.importedAt') AS imported_at,json_extract(j.value,'$.mode') AS mode,json_extract(j.value,'$.insertedTransactions') AS inserted,json_extract(j.value,'$.skippedDuplicateTransactions') AS duplicate FROM app_settings AS s,json_each(s.value) AS j WHERE s.key='desktop_migration_history_v1'"],
  ['backupCounts', 'SELECT created_at, record_count, trigger_kind FROM backup_sets ORDER BY created_at DESC LIMIT 30']
];

async function main() {
  const account = process.env.CLOUDFLARE_ACCOUNT_ID;
  const database = process.env.CF_D1_DATABASE_ID;
  const token = process.env.CLOUDFLARE_API_TOKEN;
  if (!account || !database || !token) throw new Error('AUDIT_ENV_MISSING');
  const results = {};
  for (const [name, sql] of queries) {
    if (!sql.startsWith('SELECT ') || sql.includes(';')) throw new Error('AUDIT_READ_ONLY_REQUIRED');
    const response = await fetch(`https://api.cloudflare.com/client/v4/accounts/${encodeURIComponent(account)}/d1/database/${encodeURIComponent(database)}/query`, {
      method: 'POST', headers: { authorization: `Bearer ${token}`, 'content-type': 'application/json' }, body: JSON.stringify({ sql }), signal: AbortSignal.timeout(30000)
    });
    const data = await response.json();
    if (!response.ok || !data.success || !data.result?.every(item => item.success)) throw new Error('AUDIT_QUERY_FAILED');
    if (data.result.some(item => item.meta?.changed_db || item.meta?.rows_written > 0)) throw new Error('AUDIT_READ_ONLY_VIOLATION');
    results[name] = data.result.flatMap(item => item.results || []);
  }
  const key = randomBytes(32), iv = randomBytes(12);
  const cipher = createCipheriv('aes-256-gcm', key, iv);
  const encrypted = Buffer.concat([cipher.update(gzipSync(Buffer.from(JSON.stringify(results)))), cipher.final()]);
  const envelope = { key: publicEncrypt({ key: publicKey, oaepHash: 'sha256', padding: constants.RSA_PKCS1_OAEP_PADDING }, key).toString('base64'), iv: iv.toString('base64'), tag: cipher.getAuthTag().toString('base64'), data: encrypted.toString('base64') };
  console.log('CYACC_ENCRYPTED_AUDIT=' + Buffer.from(JSON.stringify(envelope)).toString('base64'));
}
main().catch(() => { console.error('Read-only encrypted audit failed; no response or credential details logged.'); process.exitCode = 1; });
