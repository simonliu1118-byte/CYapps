document.addEventListener('DOMContentLoaded', () => {
  const currentUser = document.querySelector('#currentUser');
  const logoutButton = document.querySelector('#logoutButton');
  const readOnlyNotice = document.querySelector('#readOnlyNotice');

  logoutButton?.addEventListener('click', async () => {
    logoutButton.disabled = true;
    try {
      await fetch('/api/auth/logout', {
        method: 'POST',
        credentials: 'include',
        cache: 'no-store'
      });
    } finally {
      location.replace('/login');
    }
  });

  checkSession();

  async function checkSession() {
    try {
      const response = await fetch('/api/auth/me', {
        cache: 'no-store',
        credentials: 'include'
      });
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
    } else {
      delete document.body.dataset.cyaccReadOnly;
      readOnlyNotice?.classList.add('hidden');
    }
  }
});

window.addEventListener('load', () => {
  if (document.body.dataset.cyaccReadOnly !== 'true') return;
  const ledgerButton = document.querySelector('#mobileMainNav [data-mobile-page="ledger"]');
  ledgerButton?.click();
});
