document.addEventListener('DOMContentLoaded', () => {
  const overlay = document.querySelector('#authOverlay');
  const form = document.querySelector('#loginForm');
  const employeeNo = document.querySelector('#loginEmployeeNo');
  const password = document.querySelector('#loginPassword');
  const submit = document.querySelector('#loginSubmit');
  const message = document.querySelector('#loginMessage');
  const currentUser = document.querySelector('#currentUser');
  const logoutButton = document.querySelector('#logoutButton');

  form?.addEventListener('submit', async event => {
    event.preventDefault();
    setMessage('');
    const no = String(employeeNo?.value || '').trim();
    const pwd = String(password?.value || '');
    if (!/^\d{4}$/.test(no) || !pwd) {
      setMessage('請輸入 4 碼員工編號與密碼。', true);
      return;
    }

    submit.disabled = true;
    try {
      const response = await fetch('/api/auth/login', {
        method: 'POST',
        headers: { 'content-type': 'application/json' },
        body: JSON.stringify({ employeeNo: no, password: pwd })
      });
      const data = await response.json().catch(() => ({}));
      if (!response.ok || data.ok === false) throw new Error(data.error || '登入失敗。');
      location.reload();
    } catch (error) {
      setMessage(error.message || '登入失敗。', true);
      password?.select();
      submit.disabled = false;
    }
  });

  logoutButton?.addEventListener('click', async () => {
    logoutButton.disabled = true;
    try {
      await fetch('/api/auth/logout', { method: 'POST' });
    } finally {
      location.reload();
    }
  });

  checkSession();

  async function checkSession() {
    try {
      const response = await fetch('/api/auth/me', { cache: 'no-store' });
      const data = await response.json().catch(() => ({}));
      if (!response.ok || data.ok === false) {
        showLogin();
        return;
      }
      const user = data.user || {};
      currentUser.textContent = `${user.employeeNo || ''} ${user.name || ''}`.trim();
      currentUser.classList.remove('hidden');
      logoutButton.classList.remove('hidden');
      overlay.classList.add('hidden');
      document.body.classList.remove('auth-locked');
    } catch {
      showLogin('無法連線到登入服務。');
    }
  }

  function showLogin(text = '') {
    overlay.classList.remove('hidden');
    document.body.classList.add('auth-locked');
    if (text) setMessage(text, true);
    setTimeout(() => employeeNo?.focus(), 0);
  }

  function setMessage(text, isError = false) {
    if (!message) return;
    message.textContent = text;
    message.classList.toggle('error', isError);
  }
});
