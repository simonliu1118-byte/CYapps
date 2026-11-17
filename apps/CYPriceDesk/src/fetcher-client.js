// Calls the Vercel fetcher with an HMAC-SHA256 signature over `timestamp + "." + raw body`
// (hex digest, key = CYPD_FETCHER_SECRET). fetcher/app.py verifies the same format.
const TIMEOUT_MS = 25000;

export async function signBody(secret, timestamp, body) {
  const key = await crypto.subtle.importKey('raw', new TextEncoder().encode(secret), {name: 'HMAC', hash: 'SHA-256'}, false, ['sign']);
  const digest = await crypto.subtle.sign('HMAC', key, new TextEncoder().encode(`${timestamp}.${body}`));
  return [...new Uint8Array(digest)].map(b => b.toString(16).padStart(2, '0')).join('');
}

export const fetcherConfigured = env => Boolean(env.CYPD_FETCHER_URL && env.CYPD_FETCHER_SECRET);

// Returns the fetcher's JSON, or throws when it is unreachable, times out or answers non-2xx.
export async function callFetcher(env, path, payload) {
  const body = JSON.stringify(payload);
  const timestamp = Math.floor(Date.now() / 1000);
  const response = await fetch(env.CYPD_FETCHER_URL.replace(/\/+$/, '') + path, {
    method: 'POST',
    headers: {
      'content-type': 'application/json',
      'x-cypd-timestamp': String(timestamp),
      'x-cypd-signature': await signBody(env.CYPD_FETCHER_SECRET, timestamp, body)
    },
    body,
    signal: AbortSignal.timeout(TIMEOUT_MS)
  });
  if (!response.ok) throw new Error(`fetcher ${response.status}`);
  return response.json();
}
