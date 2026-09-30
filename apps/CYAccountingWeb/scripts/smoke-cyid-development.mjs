import process from 'node:process';

const baseUrl = required('CYACC_SMOKE_BASE_URL').replace(/\/+$/, '');
const employeeNo = required('CYACC_SMOKE_EMPLOYEE_NO');
const password = required('CYACC_SMOKE_PASSWORD');
const expectedRole = required('CYACC_SMOKE_EXPECTED_ROLE').toUpperCase();
const month = process.env.CYACC_SMOKE_MONTH || new Date().toISOString().slice(0, 7);

if (!/^\d{4}$/.test(employeeNo)) throw new Error('CYACC_SMOKE_EMPLOYEE_NO must be exactly 4 digits.');
if (!['USER', 'ADMIN', 'SUPER_ADMIN'].includes(expectedRole)) {
  throw new Error('CYACC_SMOKE_EXPECTED_ROLE must be USER, ADMIN, or SUPER_ADMIN.');
}
if (!/^\d{4}-\d{2}$/.test(month)) throw new Error('CYACC_SMOKE_MONTH must use YYYY-MM.');

const loginEntry = await fetch(baseUrl + '/login', { redirect: 'manual' });
assertStatus(loginEntry, 200, 'GET /login');
const loginHtml = await loginEntry.text();
if (!loginHtml.includes('action="/login"') || loginHtml.includes('src="/app.js"')) {
  throw new Error('Standalone login contract failed: login page must not load the accounting app bundle.');
}

const form = new URLSearchParams({ employeeNo, password });
const login = await fetch(baseUrl + '/login', {
  method: 'POST',
  headers: { 'content-type': 'application/x-www-form-urlencoded' },
  body: form,
  redirect: 'manual'
});
assertStatus(login, 303, 'POST /login');
if (login.headers.get('location') !== '/') throw new Error('Login did not redirect to /.');

const setCookie = login.headers.get('set-cookie') || '';
const cookie = setCookie.split(';', 1)[0];
if (!/^cyaccounting_session=cyid_[0-9a-f]{64}$/.test(cookie)) {
  throw new Error('Login did not return an app-scoped CYID provider session cookie.');
}
for (const flag of ['HttpOnly', 'Secure', 'SameSite=Lax', 'Expires=']) {
  if (!setCookie.includes(flag)) throw new Error(`Session cookie is missing ${flag}.`);
}

const me = await api('/api/auth/me', { headers: { cookie } });
assertStatus(me.response, 200, 'GET /api/auth/me');
if (me.data?.user?.role !== expectedRole) {
  throw new Error(`Expected role ${expectedRole}, received ${String(me.data?.user?.role || 'missing')}.`);
}

const bootstrap = await api('/api/bootstrap', { headers: { cookie } });
assertStatus(bootstrap.response, 200, 'GET /api/bootstrap');

const transactions = await api(`/api/transactions?month=${encodeURIComponent(month)}`, { headers: { cookie } });
assertStatus(transactions.response, 200, 'GET /api/transactions');

if (expectedRole === 'USER') {
  const denied = await api('/api/transactions', {
    method: 'POST',
    headers: { cookie, 'content-type': 'application/json' },
    body: JSON.stringify({})
  });
  assertStatus(denied.response, 403, 'USER POST /api/transactions');
  if (denied.data?.code !== 'READ_ONLY_USER') {
    throw new Error('USER write gate did not return READ_ONLY_USER.');
  }
}

const exportResponse = await fetch(baseUrl + `/api/export/month.xlsx?month=${encodeURIComponent(month)}`, {
  headers: { cookie, accept: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet' },
  redirect: 'manual'
});
assertStatus(exportResponse, 200, 'GET /api/export/month.xlsx');
if (!String(exportResponse.headers.get('content-type') || '').includes('application/vnd.openxmlformats-officedocument.spreadsheetml.sheet')) {
  throw new Error('Excel export returned an unexpected content type.');
}
const exportBytes = new Uint8Array(await exportResponse.arrayBuffer());
if (exportBytes.length < 4 || exportBytes[0] !== 0x50 || exportBytes[1] !== 0x4b) {
  throw new Error('Excel export is not a non-empty XLSX/ZIP payload.');
}

const logout = await api('/api/auth/logout', {
  method: 'POST',
  headers: { cookie }
});
assertStatus(logout.response, 200, 'POST /api/auth/logout');

const afterLogout = await api('/api/auth/me', { headers: { cookie } });
assertStatus(afterLogout.response, 401, 'GET /api/auth/me after logout');

console.log(`CYID development smoke passed for role=${expectedRole}, month=${month}.`);

async function api(path, init = {}) {
  const response = await fetch(baseUrl + path, {
    ...init,
    redirect: 'manual',
    cache: 'no-store'
  });
  const data = await response.json().catch(() => null);
  return { response, data };
}

function required(name) {
  const value = String(process.env[name] || '').trim();
  if (!value) throw new Error(`Missing required smoke variable: ${name}`);
  return value;
}

function assertStatus(response, expected, label) {
  if (response.status !== expected) {
    throw new Error(`${label} expected HTTP ${expected}, received ${response.status}.`);
  }
}
