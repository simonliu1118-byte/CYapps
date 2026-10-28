// Authentication navigation belongs to this module; callers only report server responses.
window.cyaccHandleAuthResponse = (response, data = {}) => {
  if (response.status !== 401 && data?.code !== 'AUTH_REQUIRED') return false;
  location.replace('/login');
  return true;
};

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
      if (window.cyaccHandleAuthResponse(response, data)) {
        throw new Error('AUTH_REQUIRED');
      }
      if (!response.ok || data.ok === false) {
        throw new Error(data.error || '帳號服務目前無法驗證登入狀態。');
      }
      if (!validBootUser(data.user)) throw new Error('帳號資料格式不正確。');
      scheduleSessionExpiry(data.session?.expiresAt);
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

  function scheduleSessionExpiry(expiresAt) {
    const deadline = Date.parse(expiresAt);
    if (!Number.isFinite(deadline)) return;
    // Use only CYID's expiry timestamp. This is navigation, not a second session policy.
    const checkExpiry = () => {
      const remaining = deadline - Date.now();
      if (remaining <= 0) { location.replace('/login'); return; }
      window.setTimeout(checkExpiry, Math.min(remaining, 2_147_483_647));
    };
    checkExpiry();
    window.addEventListener('pageshow', () => { if (Date.now() >= deadline) location.replace('/login'); });
    document.addEventListener('visibilitychange', () => {
      if (document.visibilityState === 'visible' && Date.now() >= deadline) location.replace('/login');
    });
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
      const cluster = currentUser.closest('.cy-account-menu');
      if (cluster) cluster.dataset.identityReady = 'true';
    }

    logoutButton?.classList.remove('hidden');
    window.cyaccRefreshAccountMenu = () => renderCyaccAccountMenu(user, roleLabels[role] || role, currentUser, logoutButton);
    window.cyaccRefreshAccountMenu();
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

function renderCyaccAccountMenu(user, roleLabel, currentUser, logoutButton) {
  // Auth renders the person controls; the existing topbar layout only mounts the slot.
  const root = currentUser?.closest('.cy-account-menu');
  if (!root || !logoutButton) return;
  root.dataset.role = String(user.role || 'USER');
  root.dataset.identityReady = 'true';
  let trigger = root.querySelector('.cy-account-trigger');
  let popover = root.querySelector('.cy-account-popover');
  if (!trigger) {
    trigger = document.createElement('button');
    trigger.type = 'button';
    trigger.className = 'cy-account-trigger';
    trigger.setAttribute('aria-haspopup', 'menu');
    trigger.setAttribute('aria-expanded', 'false');
    trigger.setAttribute('aria-controls', 'cyaccAccountPopover');
    popover = document.createElement('div');
    popover.id = 'cyaccAccountPopover';
    popover.className = 'cy-account-popover';
    popover.setAttribute('role', 'menu');
    popover.setAttribute('aria-label', '個人選單');
    popover.hidden = true;
    const logout = document.createElement('button');
    logout.type = 'button';
    logout.className = 'danger';
    logout.setAttribute('role', 'menuitem');
    logout.textContent = '登出';
    logout.addEventListener('click', () => { close(); logoutButton.click(); });
    popover.append(logout);
    const close = () => {
      popover.hidden = true;
      trigger.setAttribute('aria-expanded', 'false');
    };
    trigger.addEventListener('click', () => {
      popover.hidden = !popover.hidden;
      trigger.setAttribute('aria-expanded', String(!popover.hidden));
    });
    document.addEventListener('pointerdown', event => { if (!root.contains(event.target)) close(); });
    document.addEventListener('keydown', event => {
      if (event.key === 'Escape' && !popover.hidden) { close(); trigger.focus(); }
    });
    root.append(trigger, popover);
  }
  const displayName = String(user.name || user.employeeNo || '使用者');
  const avatar = document.createElement('span');
  avatar.className = 'cy-account-avatar';
  avatar.setAttribute('aria-hidden', 'true');
  avatar.textContent = displayName.slice(0, 1);
  const nameWrap = document.createElement('span');
  nameWrap.className = 'cy-account-name';
  const name = document.createElement('strong');
  name.textContent = displayName;
  const role = document.createElement('span');
  role.className = 'cy-account-role';
  role.textContent = roleLabel;
  nameWrap.append(name, role);
  const caret = document.createElementNS('http://www.w3.org/2000/svg', 'svg');
  caret.classList.add('cy-account-caret');
  caret.setAttribute('viewBox', '0 0 24 24');
  caret.setAttribute('width', '14');
  caret.setAttribute('height', '14');
  caret.setAttribute('fill', 'none');
  caret.setAttribute('stroke', 'currentColor');
  caret.setAttribute('stroke-width', '2');
  caret.setAttribute('aria-hidden', 'true');
  const path = document.createElementNS('http://www.w3.org/2000/svg', 'path');
  path.setAttribute('d', 'm6 9 6 6 6-6');
  caret.append(path);
  trigger.replaceChildren(avatar, nameWrap, caret);
  trigger.setAttribute('aria-label', '個人選單：' + displayName);
}
