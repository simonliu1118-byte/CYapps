let cyaccAuthStarted = false;

if (document.readyState === 'loading') {
  document.addEventListener('DOMContentLoaded', startCyaccAuth, { once: true });
} else {
  startCyaccAuth();
}

function startCyaccAuth() {
  if (cyaccAuthStarted) return;
  cyaccAuthStarted = true;

  const currentUser = document.querySelector('#currentUser');
  const logoutButton = document.querySelector('#logoutButton');
  const readOnlyNotice = document.querySelector('#readOnlyNotice');

  logoutButton?.addEventListener('click', async () => {
    logoutButton.disabled = true;
    try {
      await fetchWithTimeout('/api/auth/logout', {
        method: 'POST',
        credentials: 'include',
        cache: 'no-store'
      }, 8_000);
    } finally {
      location.replace('/login');
    }
  });

  window.cyaccSessionPromise = checkSessionFallback();
  window.cyaccSessionPromise
    .then(user => {
      window.cyaccCurrentUser = user;
      applyUser(user);
      window.dispatchEvent(new CustomEvent('cyacc:session-ready', { detail: { user } }));
    })
    .catch(() => {});

  async function checkSessionFallback() {
    try {
      setBootStage('正在驗證帳號…');
      const response = await fetchWithTimeout('/api/auth/me', {
        cache: 'no-store',
        credentials: 'include'
      }, 8_000);
      const data = await response.json().catch(() => ({}));
      if (response.status === 401 || data.code === 'AUTH_REQUIRED') {
        location.replace('/login');
        throw new Error('AUTH_REQUIRED');
      }
      if (!response.ok || data.ok === false) {
        throw new Error(data.error || '帳號服務目前無法驗證登入狀態。');
      }
      if (!validBootUser(data.user)) throw new Error('帳號資料格式不正確。');
      return data.user;
    } catch (error) {
      if (String(error?.message || '') === 'AUTH_REQUIRED') throw error;
      const message = ['AbortError', 'TimeoutError'].includes(error?.name)
        ? '帳號驗證逾時，請重新整理後再試。'
        : (error?.message || '帳號服務目前無法驗證登入狀態。');
      setBootFailure(message);
      throw new Error(message);
    }
  }

  function validBootUser(user) {
    return Boolean(
      user
      && /^\d{4}$/.test(String(user.employeeNo || ''))
      && ['USER', 'ADMIN', 'SUPER_ADMIN'].includes(String(user.role || ''))
    );
  }

  function applyUser(user) {
    const role = String(user.role || '');
    const roleLabels = {
      SUPER_ADMIN: '超級管理員',
      ADMIN: '管理員',
      USER: '使用者'
    };

    if (currentUser) {
      currentUser.replaceChildren();
      const main = document.createElement('span');
      main.className = 'current-user-main';
      main.textContent = [user.employeeNo, user.name].filter(Boolean).join(' ').trim();
      const roleBadge = document.createElement('span');
      roleBadge.className = 'current-user-role';
      roleBadge.textContent = roleLabels[role] || role;
      currentUser.append(main, roleBadge);
      currentUser.classList.remove('hidden', 'role-super-admin', 'role-admin', 'role-user');
      if (role === 'SUPER_ADMIN') currentUser.classList.add('role-super-admin');
      else if (role === 'ADMIN') currentUser.classList.add('role-admin');
      else currentUser.classList.add('role-user');
    }

    logoutButton?.classList.remove('hidden');
    document.body.dataset.cyaccRole = role;
    if (user.canWriteAccounting === false || role === 'USER') {
      document.body.dataset.cyaccReadOnly = 'true';
      readOnlyNotice?.classList.remove('hidden');
      activateReadOnlyMobileLedger();
    } else {
      delete document.body.dataset.cyaccReadOnly;
      readOnlyNotice?.classList.add('hidden');
    }
  }

  function activateReadOnlyMobileLedger(attempt = 0) {
    if (!window.matchMedia('(max-width: 767px)').matches) return;
    const ledgerButton = document.querySelector('#mobileMainNav [data-mobile-page="ledger"]');
    if (ledgerButton) {
      ledgerButton.click();
      return;
    }
    if (attempt < 60) window.setTimeout(() => activateReadOnlyMobileLedger(attempt + 1), 50);
  }
}

function setBootStage(message) {
  const status = document.querySelector('#cyaccBootStatus');
  if (status) status.textContent = message;
}

function setBootFailure(message) {
  const status = document.querySelector('#cyaccBootStatus');
  document.body.classList.remove('cyacc-booting');
  document.body.classList.add('cyacc-boot-failed');
  if (status) {
    status.hidden = false;
    status.textContent = message;
  }
}

async function fetchWithTimeout(input, init = {}, timeoutMs = 8_000) {
  const controller = new AbortController();
  let timeoutId;
  const request = Promise.resolve().then(() => fetch(input, { ...init, signal: controller.signal }));
  const timeout = new Promise((_, reject) => {
    timeoutId = window.setTimeout(() => {
      controller.abort();
      const error = new Error('CYACC_BROWSER_TIMEOUT');
      error.name = 'TimeoutError';
      reject(error);
    }, timeoutMs);
  });

  try {
    return await Promise.race([request, timeout]);
  } finally {
    if (timeoutId !== undefined) window.clearTimeout(timeoutId);
  }
}
