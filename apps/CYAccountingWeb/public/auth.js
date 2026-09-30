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

  void checkSession();

  async function checkSession() {
    try {
      const response = await fetchWithTimeout('/api/auth/me', {
        cache: 'no-store',
        credentials: 'include'
      }, 8_000);
      const data = await response.json().catch(() => ({}));
      if (!response.ok || data.ok === false) {
        location.replace('/login');
        return;
      }
      applyUser(data.user || {});
    } catch {
      location.replace('/login?error=service');
    }
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

async function fetchWithTimeout(input, init = {}, timeoutMs = 8_000) {
  const controller = new AbortController();
  const timeout = window.setTimeout(() => controller.abort(), timeoutMs);
  try {
    return await fetch(input, { ...init, signal: controller.signal });
  } finally {
    window.clearTimeout(timeout);
  }
}
