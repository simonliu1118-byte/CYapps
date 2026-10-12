import { randomBytes, scryptSync } from 'node:crypto';
import { spawnSync } from 'node:child_process';

const required = [
  'CLOUDFLARE_API_TOKEN',
  'CLOUDFLARE_ACCOUNT_ID',
  'CYID_WORKER_NAME',
  'CYID_CORE_ACCOUNT_APPLICATION_ID',
  'CF_CYID_APPLICATION_ID',
  'CF_CYID_WORKSPACE_ID',
  'CYACC_BASE_URL'
];
for (const name of required) {
  if (!String(process.env[name] || '').trim()) throw new Error(`Missing development acceptance setting: ${name}`);
}

const configPath = 'wrangler.deploy.generated.jsonc';
const workspaceId = process.env.CF_CYID_WORKSPACE_ID.trim();
const cyaccApplicationId = process.env.CF_CYID_APPLICATION_ID.trim().toUpperCase();
const coreApplicationId = process.env.CYID_CORE_ACCOUNT_APPLICATION_ID.trim().toUpperCase();
const cyaccBaseUrl = process.env.CYACC_BASE_URL.replace(/\/+$/, '');
const actorId = 'emp_cyacc_acceptance_actor';
const targetId = 'emp_cyacc_acceptance_user';
const actorEmail = 'cyacc-acceptance-actor@example.test';
const targetEmail = 'cyacc-acceptance-user@example.test';

const actorPassword = randomPassword();
const targetPassword = randomPassword();
mask(actorPassword);
mask(targetPassword);

let actorNo = '';
let targetNo = '';
let targetRevision = 1;
let actorToken = '';

try {
  const subdomain = await workersDevSubdomain();
  const cyidBaseUrl = `https://${process.env.CYID_WORKER_NAME.trim()}.${subdomain}.workers.dev`;

  ({ actorNo, targetNo, targetRevision } = provisionSyntheticPrincipals(actorPassword, targetPassword));
  mask(actorNo);
  mask(targetNo);

  console.log('CYACC acceptance: synthetic development principals are ready.');

  const actorLogin = await jsonFetch(cyidBaseUrl + '/v1/identity/login', {
    method: 'POST',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify({
      workspaceId,
      applicationId: coreApplicationId,
      employeeNo: actorNo,
      password: actorPassword
    })
  }, 200, 'Identity Admin login');
  actorToken = String(actorLogin.session?.token || '');
  if (!/^cyid_[0-9a-f]{64}$/.test(actorToken) || actorLogin.principal?.workspaceRole !== 'ADMIN' || actorLogin.principal?.isIdentityAdmin !== true) {
    throw new Error('Synthetic Identity Admin principal validation failed.');
  }
  mask(actorToken);

  let targetCookie = await loginCyacc(targetNo, targetPassword);
  let me = await cyaccJson('/api/auth/me', { headers: { cookie: targetCookie } }, 200, 'USER principal resolve');
  if (me.user?.role !== 'USER' || me.user?.canWriteAccounting !== false || me.user?.canExportExcel !== true) {
    throw new Error('CYACC USER principal/capability projection failed.');
  }

  const bootstrap = await cyaccJson('/api/bootstrap', { headers: { cookie: targetCookie } }, 200, 'USER bootstrap read');
  await cyaccJson('/api/transactions', { headers: { cookie: targetCookie } }, 200, 'USER transaction read');
  const denied = await cyaccJson('/api/transactions', {
    method: 'POST',
    headers: { cookie: targetCookie, 'content-type': 'application/json' },
    body: '{}'
  }, 403, 'USER transaction write denial');
  if (denied.code !== 'READ_ONLY_USER') throw new Error('USER write gate did not return READ_ONLY_USER.');

  const excel = await fetch(cyaccBaseUrl + '/api/export/month.xlsx', {
    headers: { cookie: targetCookie, accept: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet' },
    redirect: 'manual'
  });
  if (excel.status !== 200 || !String(excel.headers.get('content-type') || '').includes('application/vnd.openxmlformats-officedocument.spreadsheetml.sheet')) {
    throw new Error('USER Excel export acceptance failed.');
  }
  const excelBytes = new Uint8Array(await excel.arrayBuffer());
  if (excelBytes.length < 4 || excelBytes[0] !== 0x50 || excelBytes[1] !== 0x4b) {
    throw new Error('USER Excel export did not return an XLSX payload.');
  }
  console.log('CYACC acceptance: USER login, read-only enforcement and Excel export passed.');

  const promoted = await adminJson(
    cyidBaseUrl,
    `/v1/admin/identity/employees/${encodeURIComponent(targetId)}`,
    {
      method: 'PATCH',
      body: JSON.stringify({ roleKey: 'ADMIN', revision: targetRevision })
    },
    200,
    'USER to ADMIN role change'
  );
  targetRevision = Number(promoted.employee?.revision || 0);
  if (promoted.employee?.roleKey !== 'ADMIN' || targetRevision < 2) {
    throw new Error('Role promotion response was invalid.');
  }

  await cyaccJson('/api/auth/me', { headers: { cookie: targetCookie } }, 401, 'old USER session after role change');
  targetCookie = await loginCyacc(targetNo, targetPassword);
  me = await cyaccJson('/api/auth/me', { headers: { cookie: targetCookie } }, 200, 'ADMIN principal resolve');
  if (me.user?.role !== 'ADMIN' || me.user?.canWriteAccounting !== true) {
    throw new Error('CYACC ADMIN projection after role change failed.');
  }

  const account = bootstrap.accounts?.[0];
  const category = bootstrap.categories?.find(item => item?.kind === 'income') || bootstrap.categories?.[0];
  if (!account?.name || !category?.name) throw new Error('Isolated accounting fixture lacks an account/category for ADMIN write acceptance.');
  const today = new Date().toISOString().slice(0, 10);
  const created = await cyaccJson('/api/transactions', {
    method: 'POST',
    headers: { cookie: targetCookie, 'content-type': 'application/json' },
    body: JSON.stringify({
      txDate: today,
      accountName: account.name,
      kind: category.kind || 'income',
      categoryName: category.name,
      summary: 'CYID acceptance',
      amount: 1
    })
  }, 201, 'ADMIN transaction create');
  const transactionId = Number(created.id || 0);
  if (!Number.isInteger(transactionId) || transactionId <= 0) throw new Error('ADMIN write acceptance did not return a transaction ID.');
  await cyaccJson(`/api/transactions/${transactionId}`, {
    method: 'DELETE',
    headers: { cookie: targetCookie }
  }, 200, 'ADMIN transaction cleanup');
  console.log('CYACC acceptance: Role change invalidated the old Session and ADMIN write capability passed.');

  const revoked = await adminJson(
    cyidBaseUrl,
    `/v1/admin/identity/employees/${encodeURIComponent(targetId)}/applications/${encodeURIComponent(cyaccApplicationId)}`,
    { method: 'PUT', body: JSON.stringify({ enabled: false }) },
    200,
    'CYACC App Access revoke'
  );
  if (revoked.access?.enabled !== false || revoked.affectedApplicationSessionsRevoked !== true) {
    throw new Error('App Access revoke response did not confirm Session revocation.');
  }

  await cyaccJson('/api/auth/me', { headers: { cookie: targetCookie } }, 401, 'old ADMIN session after App Access revoke');
  const deniedLogin = await fetch(cyaccBaseUrl + '/login', {
    method: 'POST',
    headers: { 'content-type': 'application/x-www-form-urlencoded' },
    body: new URLSearchParams({ employeeNo: targetNo, password: targetPassword }),
    redirect: 'manual'
  });
  if (deniedLogin.status !== 303 || deniedLogin.headers.get('location') !== '/login?error=access') {
    throw new Error('Fresh CYACC login was not denied after App Access revoke.');
  }

  await adminJson(
    cyidBaseUrl,
    `/v1/admin/identity/employees/${encodeURIComponent(targetId)}/applications/${encodeURIComponent(cyaccApplicationId)}`,
    { method: 'PUT', body: JSON.stringify({ enabled: true }) },
    200,
    'CYACC App Access restore'
  );

  const restored = await adminJson(
    cyidBaseUrl,
    `/v1/admin/identity/employees/${encodeURIComponent(targetId)}`,
    {
      method: 'PATCH',
      body: JSON.stringify({ roleKey: 'USER', revision: targetRevision })
    },
    200,
    'ADMIN to USER role restore'
  );
  targetRevision = Number(restored.employee?.revision || 0);
  if (restored.employee?.roleKey !== 'USER') throw new Error('Role restore response was invalid.');

  targetCookie = await loginCyacc(targetNo, targetPassword);
  me = await cyaccJson('/api/auth/me', { headers: { cookie: targetCookie } }, 200, 'restored USER principal');
  if (me.user?.role !== 'USER' || me.user?.canWriteAccounting !== false) {
    throw new Error('Restored USER projection failed.');
  }

  await cyaccJson('/api/auth/logout', {
    method: 'POST',
    headers: { cookie: targetCookie }
  }, 200, 'CYACC logout');
  await cyaccJson('/api/auth/me', { headers: { cookie: targetCookie } }, 401, 'CYACC post-logout Session invalidation');

  await jsonFetch(cyidBaseUrl + '/v1/identity/logout', {
    method: 'POST',
    headers: {
      authorization: `Bearer ${actorToken}`,
      'x-identity-application': coreApplicationId
    }
  }, 200, 'Identity Admin logout');

  console.log('CYACC acceptance: App Access revoke/restore, Role restore and logout invalidation passed.');
  console.log('CYACC isolated development acceptance passed.');
} finally {
  try {
    restoreSyntheticTarget();
  } catch {
    console.error('CYACC acceptance cleanup could not fully restore the synthetic target.');
  }
}

function provisionSyntheticPrincipals(actorPwd, targetPwd) {
  const existing = d1Rows(`
    SELECT employee_id, employee_no, revision
      FROM employees
     WHERE workspace_id = ${sql(workspaceId)}
       AND employee_id IN (${sql(actorId)}, ${sql(targetId)})
     ORDER BY employee_id;
  `);

  const byId = new Map(existing.map(row => [String(row.employee_id), row]));
  const usedNos = new Set(d1Rows(`SELECT employee_no FROM employees WHERE workspace_id = ${sql(workspaceId)};`).map(row => String(row.employee_no)));
  const chooseNo = id => {
    const current = byId.get(id)?.employee_no;
    if (/^\d{4}$/.test(String(current || ''))) return String(current);
    for (let value = 9900; value <= 9998; value += 1) {
      const candidate = String(value);
      if (!usedNos.has(candidate)) {
        usedNos.add(candidate);
        return candidate;
      }
    }
    throw new Error('No unused synthetic development Employee No is available.');
  };

  const actorEmployeeNo = chooseNo(actorId);
  const targetEmployeeNo = chooseNo(targetId);
  const actorVerifier = credentialVerifier(actorPwd);
  const targetVerifier = credentialVerifier(targetPwd);
  const now = new Date().toISOString();

  d1Run(`
    INSERT INTO employees(
      employee_id, workspace_id, employee_no, name, email_normalized,
      email_verified_at, enabled, role_key, identity_admin, activated_at,
      revision, created_at, updated_at
    ) VALUES(
      ${sql(actorId)}, ${sql(workspaceId)}, ${sql(actorEmployeeNo)}, 'CYACC Acceptance Identity Admin',
      ${sql(actorEmail)}, ${sql(now)}, 1, 'ADMIN', 1, ${sql(now)}, 1, ${sql(now)}, ${sql(now)}
    )
    ON CONFLICT(employee_id) DO UPDATE SET
      employee_no=excluded.employee_no,
      name=excluded.name,
      email_normalized=excluded.email_normalized,
      email_verified_at=excluded.email_verified_at,
      enabled=1,
      role_key='ADMIN',
      identity_admin=1,
      activated_at=excluded.activated_at,
      revision=employees.revision+1,
      updated_at=excluded.updated_at;

    INSERT INTO employees(
      employee_id, workspace_id, employee_no, name, email_normalized,
      email_verified_at, enabled, role_key, identity_admin, activated_at,
      revision, created_at, updated_at
    ) VALUES(
      ${sql(targetId)}, ${sql(workspaceId)}, ${sql(targetEmployeeNo)}, 'CYACC Acceptance User',
      ${sql(targetEmail)}, ${sql(now)}, 1, 'USER', 0, ${sql(now)}, 1, ${sql(now)}, ${sql(now)}
    )
    ON CONFLICT(employee_id) DO UPDATE SET
      employee_no=excluded.employee_no,
      name=excluded.name,
      email_normalized=excluded.email_normalized,
      email_verified_at=excluded.email_verified_at,
      enabled=1,
      role_key='USER',
      identity_admin=0,
      activated_at=excluded.activated_at,
      revision=employees.revision+1,
      updated_at=excluded.updated_at;

    INSERT INTO employee_credentials(employee_id, algorithm, verifier, credential_version, updated_at)
    VALUES(${sql(actorId)}, 'scrypt', ${sql(actorVerifier)}, 1, ${sql(now)})
    ON CONFLICT(employee_id) DO UPDATE SET
      algorithm='scrypt',
      verifier=excluded.verifier,
      credential_version=employee_credentials.credential_version+1,
      updated_at=excluded.updated_at;

    INSERT INTO employee_credentials(employee_id, algorithm, verifier, credential_version, updated_at)
    VALUES(${sql(targetId)}, 'scrypt', ${sql(targetVerifier)}, 1, ${sql(now)})
    ON CONFLICT(employee_id) DO UPDATE SET
      algorithm='scrypt',
      verifier=excluded.verifier,
      credential_version=employee_credentials.credential_version+1,
      updated_at=excluded.updated_at;

    INSERT INTO employee_application_access(workspace_id, employee_id, application_id, enabled, created_at, updated_at)
    VALUES(${sql(workspaceId)}, ${sql(targetId)}, ${sql(cyaccApplicationId)}, 1, ${sql(now)}, ${sql(now)})
    ON CONFLICT(workspace_id, employee_id, application_id) DO UPDATE SET enabled=1, updated_at=excluded.updated_at;

    UPDATE identity_sessions
       SET revoked_at=COALESCE(revoked_at, ${sql(now)})
     WHERE employee_id IN (${sql(actorId)}, ${sql(targetId)});
  `);

  const target = d1Rows(`
    SELECT employee_no, revision
      FROM employees
     WHERE workspace_id=${sql(workspaceId)} AND employee_id=${sql(targetId)}
     LIMIT 1;
  `)[0];
  const actor = d1Rows(`
    SELECT employee_no
      FROM employees
     WHERE workspace_id=${sql(workspaceId)} AND employee_id=${sql(actorId)}
     LIMIT 1;
  `)[0];
  if (!target || !actor) throw new Error('Synthetic acceptance principal readback failed.');

  return {
    actorNo: String(actor.employee_no),
    targetNo: String(target.employee_no),
    targetRevision: Number(target.revision)
  };
}

function restoreSyntheticTarget() {
  const now = new Date().toISOString();
  d1Run(`
    UPDATE employees
       SET enabled=1,
           role_key='USER',
           identity_admin=0,
           revision=revision+1,
           updated_at=${sql(now)}
     WHERE workspace_id=${sql(workspaceId)}
       AND employee_id=${sql(targetId)};

    INSERT INTO employee_application_access(workspace_id, employee_id, application_id, enabled, created_at, updated_at)
    VALUES(${sql(workspaceId)}, ${sql(targetId)}, ${sql(cyaccApplicationId)}, 1, ${sql(now)}, ${sql(now)})
    ON CONFLICT(workspace_id, employee_id, application_id) DO UPDATE SET enabled=1, updated_at=excluded.updated_at;

    UPDATE identity_sessions
       SET revoked_at=COALESCE(revoked_at, ${sql(now)})
     WHERE employee_id IN (${sql(actorId)}, ${sql(targetId)});
  `);
}

async function loginCyacc(employeeNo, password) {
  const response = await fetch(cyaccBaseUrl + '/login', {
    method: 'POST',
    headers: { 'content-type': 'application/x-www-form-urlencoded' },
    body: new URLSearchParams({ employeeNo, password }),
    redirect: 'manual'
  });
  if (response.status !== 303 || response.headers.get('location') !== '/') {
    throw new Error(`CYACC login failed with HTTP ${response.status}.`);
  }
  const setCookie = response.headers.get('set-cookie') || '';
  const cookie = setCookie.split(';', 1)[0];
  if (!/^cyaccounting_session=cyid_[0-9a-f]{64}$/.test(cookie)) {
    throw new Error('CYACC login did not return a provider Session cookie.');
  }
  return cookie;
}

async function cyaccJson(path, init, expected, label) {
  return jsonFetch(cyaccBaseUrl + path, { ...init, redirect: 'manual' }, expected, label);
}

async function adminJson(base, path, init, expected, label) {
  return jsonFetch(base + path, {
    ...init,
    headers: {
      authorization: `Bearer ${actorToken}`,
      'x-identity-application': coreApplicationId,
      'content-type': 'application/json',
      ...(init.headers || {})
    }
  }, expected, label);
}

async function jsonFetch(url, init, expected, label) {
  const response = await fetch(url, { ...init, redirect: 'manual' });
  const data = await response.json().catch(() => ({}));
  if (response.status !== expected) {
    const code = String(data?.error?.code || data?.code || 'UNKNOWN');
    throw new Error(`${label} expected HTTP ${expected}, received ${response.status} (${code}).`);
  }
  return data;
}

async function workersDevSubdomain() {
  const response = await fetch(
    `https://api.cloudflare.com/client/v4/accounts/${encodeURIComponent(process.env.CLOUDFLARE_ACCOUNT_ID)}/workers/subdomain`,
    { headers: { authorization: `Bearer ${process.env.CLOUDFLARE_API_TOKEN}` } }
  );
  const data = await response.json().catch(() => ({}));
  const subdomain = String(data?.result?.subdomain || '');
  if (!response.ok || !/^[a-z0-9-]{1,63}$/i.test(subdomain)) {
    throw new Error('Unable to resolve the development workers.dev subdomain.');
  }
  return subdomain;
}

function d1Rows(command) {
  const raw = d1(command);
  const roots = Array.isArray(raw) ? raw : [raw];
  const rows = [];
  for (const root of roots) {
    if (Array.isArray(root?.results)) rows.push(...root.results);
    else if (Array.isArray(root?.result?.results)) rows.push(...root.result.results);
  }
  return rows;
}

function d1Run(command) {
  d1(command);
}

function d1(command) {
  const result = spawnSync('npx', [
    'wrangler', 'd1', 'execute', 'DB', '--remote',
    '--config', configPath, '--json', '--command', command
  ], {
    encoding: 'utf8',
    maxBuffer: 8 * 1024 * 1024,
    env: process.env
  });
  if (result.status !== 0) throw new Error('Remote development D1 acceptance query failed.');
  try {
    return JSON.parse(result.stdout);
  } catch {
    throw new Error('Remote development D1 acceptance response was invalid.');
  }
}

function credentialVerifier(password) {
  const salt = randomBytes(16);
  const key = scryptSync(password, salt, 32, {
    N: 16_384,
    r: 8,
    p: 1,
    maxmem: 64 * 1024 * 1024
  });
  return `scrypt$16384$8$1$${salt.toString('hex')}$${key.toString('hex')}`;
}

function randomPassword() {
  return randomBytes(12).toString('base64url').slice(0, 16);
}

function sql(value) {
  return "'" + String(value).replaceAll("'", "''") + "'";
}

function mask(value) {
  if (value) console.log(`::add-mask::${value}`);
}
