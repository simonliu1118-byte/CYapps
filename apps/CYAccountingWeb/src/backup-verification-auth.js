// Machine authentication for the one reviewed backup-check workflow. This does
// not create a CYID user/session and cannot invoke accounting or restore APIs.
const ISSUER = 'https://token.actions.githubusercontent.com';
const REPOSITORY = 'simonliu1118-byte/CYapps';
const WORKFLOW = `${REPOSITORY}/.github/workflows/cyaccountingweb-backup-verify.yml@refs/heads/main`;
let publicKeys;

export async function authenticateBackupVerifier(request, env, options = {}) {
  const token = /^Bearer ([A-Za-z0-9_.-]+)$/.exec(request.headers.get('authorization') || '')?.[1];
  if (!token || token.length > 16000) throw new Error('VERIFIER_UNAUTHORIZED');
  const parts = token.split('.');
  if (parts.length !== 3) throw new Error('VERIFIER_UNAUTHORIZED');
  const header = decode(parts[0]), claims = decode(parts[1]);
  const now = Math.floor((options.now?.getTime() ?? Date.now()) / 1000);
  const repositoryId = String(env.BACKUP_VERIFY_REPOSITORY_ID || '');
  if (header.alg !== 'RS256' || !header.kid || !repositoryId
      || claims.iss !== ISSUER || claims.aud !== new URL(request.url).origin
      || String(claims.repository_id) !== repositoryId || claims.repository !== REPOSITORY
      || claims.ref !== 'refs/heads/main' || claims.workflow_ref !== WORKFLOW
      || claims.runner_environment !== 'github-hosted'
      || !['schedule','workflow_dispatch'].includes(claims.event_name)
      || !Number.isFinite(claims.exp) || claims.exp <= now
      || !Number.isFinite(claims.nbf) || claims.nbf > now + 30
      || !Number.isFinite(claims.iat) || claims.iat > now + 30 || now - claims.iat > 600
      || !/^\d+$/.test(String(claims.run_id)) || !/^\d+$/.test(String(claims.run_attempt))) {
    throw new Error('VERIFIER_UNAUTHORIZED');
  }
  const fetchImpl = options.fetchImpl || fetch;
  if (!publicKeys || publicKeys.expires <= now) {
    const response = await fetchImpl(`${ISSUER}/.well-known/jwks`);
    if (!response.ok) throw new Error('VERIFIER_UNAUTHORIZED');
    publicKeys = { keys: (await response.json()).keys || [], expires: now + 60 };
  }
  const jwk = publicKeys.keys.find(key => key.kid === header.kid && key.kty === 'RSA' && key.use === 'sig');
  if (!jwk) throw new Error('VERIFIER_UNAUTHORIZED');
  const key = await crypto.subtle.importKey('jwk', jwk, {name:'RSASSA-PKCS1-v1_5',hash:'SHA-256'}, false, ['verify']);
  if (!await crypto.subtle.verify('RSASSA-PKCS1-v1_5', key, bytes(parts[2]), new TextEncoder().encode(`${parts[0]}.${parts[1]}`))) throw new Error('VERIFIER_UNAUTHORIZED');
  return { run: `${claims.run_id}:${claims.run_attempt}` };
}

function bytes(value) {
  return Uint8Array.from(atob(value.replace(/-/g,'+').replace(/_/g,'/')), ch => ch.charCodeAt(0));
}
function decode(value) { return JSON.parse(new TextDecoder().decode(bytes(value))); }
