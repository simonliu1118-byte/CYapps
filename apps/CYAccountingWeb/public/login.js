document.addEventListener('DOMContentLoaded', () => {
  const loginPanel = document.querySelector('#loginPanel');
  const recoveryPanel = document.querySelector('#recoveryPanel');
  const loginMessage = document.querySelector('#loginMessage');
  const forgot = document.querySelector('#forgotPassword');
  const loginForm = document.querySelector('#loginForm');
  const loginEmployeeNo = document.querySelector('#employeeNo');
  const loginPassword = document.querySelector('input[name="password"]');
  const loginButton = document.querySelector('#loginButton');

  const form = document.querySelector('#recoveryForm');
  const employeeNo = document.querySelector('#recoveryEmployeeNo');
  const send = document.querySelector('#recoverySend');
  const fields = document.querySelector('#recoveryConfirmFields');
  const delivery = document.querySelector('#recoveryDelivery');
  const code = document.querySelector('#recoveryCode');
  const password = document.querySelector('#recoveryPassword');
  const passwordConfirm = document.querySelector('#recoveryPasswordConfirm');
  const confirmButton = document.querySelector('#recoveryConfirm');
  const resend = document.querySelector('#recoveryResend');
  const message = document.querySelector('#recoveryMessage');
  const back = document.querySelector('#recoveryBack');

  let recovery = null;
  let resendTimer = null;

  showLoginError();

  loginForm?.addEventListener('submit', handleLoginSubmit);

  forgot?.addEventListener('click', () => {
    employeeNo.value = /^\d{4}$/.test(String(loginEmployeeNo?.value || '').trim()) ? loginEmployeeNo.value.trim() : '';
    loginPanel.hidden = true;
    recoveryPanel.hidden = false;
    employeeNo.focus();
  });

  back?.addEventListener('click', () => {
    resetRecovery();
    recoveryPanel.hidden = true;
    loginPanel.hidden = false;
    loginEmployeeNo?.focus();
  });

  resend?.addEventListener('click', sendCode);

  form?.addEventListener('submit', async event => {
    event.preventDefault();
    if (!recovery) {
      await sendCode();
      return;
    }
    await confirmRecovery();
  });

  async function handleLoginSubmit(event) {
    event.preventDefault();
    const no = String(loginEmployeeNo?.value || '').trim();
    const loginPasswordValue = String(loginPassword?.value || '');
    loginMessage.textContent = '';
    loginMessage.classList.remove('error');

    if (!/^\d{4}$/.test(no) || Array.from(loginPasswordValue).length < 8 || Array.from(loginPasswordValue).length > 16) {
      loginMessage.textContent = '請輸入正確的 4 碼員工編號與 8–16 字元密碼。';
      loginMessage.classList.add('error');
      return;
    }

    const controller = new AbortController();
    const timeout = window.setTimeout(() => controller.abort(), 8_000);
    let navigating = false;
    setLoginBusy(true);
    loginMessage.textContent = '正在驗證帳號…';

    try {
      const response = await fetch('/api/auth/login', {
        method: 'POST',
        headers: { 'content-type': 'application/json' },
        credentials: 'include',
        cache: 'no-store',
        signal: controller.signal,
        body: JSON.stringify({ employeeNo: no, password: loginPasswordValue })
      });
      const data = await response.json().catch(() => ({}));
      if (!response.ok || data.ok === false) throw new Error(data.error || '登入失敗。');
      loginMessage.textContent = '登入成功，正在開啟記帳系統…';
      navigating = true;
      window.location.replace('/');
    } catch (error) {
      const timedOut = controller.signal.aborted || error?.name === 'AbortError';
      loginMessage.textContent = timedOut
        ? '登入逾時，請確認網路後再試。'
        : (error?.message || '登入失敗，請稍後再試。');
      loginMessage.classList.add('error');
    } finally {
      window.clearTimeout(timeout);
      if (!navigating) setLoginBusy(false);
    }
  }

  function setLoginBusy(busy) {
    if (loginButton) {
      loginButton.disabled = busy;
      loginButton.textContent = busy ? '登入中…' : '登入';
    }
    if (loginEmployeeNo) loginEmployeeNo.disabled = busy;
    if (loginPassword) loginPassword.disabled = busy;
    if (forgot) forgot.disabled = busy;
  }

  async function sendCode() {
    const no = String(employeeNo?.value || '').trim();
    setMessage('');
    if (!/^\d{4}$/.test(no)) {
      setMessage('請輸入 4 碼員工編號。', true);
      employeeNo.focus();
      return;
    }

    setBusy(true);
    try {
      const response = await fetch('/api/auth/password-recovery/start', {
        method: 'POST',
        headers: { 'content-type': 'application/json' },
        cache: 'no-store',
        body: JSON.stringify({ employeeNo: no })
      });
      const data = await response.json().catch(() => ({}));
      if (!response.ok || data.ok === false) throw new Error(data.error || '驗證碼寄送失敗。');
      recovery = data.recovery;
      fields.hidden = false;
      send.hidden = true;
      employeeNo.disabled = true;
      delivery.textContent = data.message || '若帳號符合條件，驗證碼已寄至登記且已驗證的 Email。';
      setMessage('驗證碼要求已送出。');
      startResendCountdown(recovery.resendAfter);
      code.focus();
    } catch (error) {
      setMessage(error.message || '驗證碼寄送失敗。', true);
    } finally {
      setBusy(false);
    }
  }

  async function confirmRecovery() {
    const no = String(employeeNo?.value || '').trim();
    const otp = String(code?.value || '').trim();
    const nextPassword = String(password?.value || '');
    const repeated = String(passwordConfirm?.value || '');
    setMessage('');

    if (!/^\d{6}$/.test(otp)) {
      setMessage('Email 驗證碼必須是 6 碼數字。', true);
      code.focus();
      return;
    }
    const length = Array.from(nextPassword).length;
    if (length < 8 || length > 16) {
      setMessage('新密碼需為 8–16 個字元。', true);
      password.focus();
      return;
    }
    if (nextPassword !== repeated) {
      setMessage('兩次輸入的新密碼不一致。', true);
      passwordConfirm.focus();
      return;
    }

    setBusy(true);
    try {
      const response = await fetch('/api/auth/password-recovery/confirm', {
        method: 'POST',
        headers: { 'content-type': 'application/json' },
        cache: 'no-store',
        body: JSON.stringify({
          employeeNo: no,
          challengeId: recovery.challengeId,
          code: otp,
          newPassword: nextPassword
        })
      });
      const data = await response.json().catch(() => ({}));
      if (!response.ok || data.ok === false) throw new Error(data.error || '密碼重設失敗。');
      loginEmployeeNo.value = no;
      resetRecovery();
      recoveryPanel.hidden = true;
      loginPanel.hidden = false;
      loginMessage.textContent = '密碼已重設，請使用新密碼登入。';
      loginMessage.classList.remove('error');
      document.querySelector('input[name="password"]')?.focus();
    } catch (error) {
      setMessage(error.message || '密碼重設失敗。', true);
    } finally {
      setBusy(false);
    }
  }

  function startResendCountdown(resendAfter) {
    if (resendTimer) clearInterval(resendTimer);
    const update = () => {
      const seconds = Math.max(0, Math.ceil((Date.parse(resendAfter) - Date.now()) / 1000));
      if (seconds > 0) {
        resend.disabled = true;
        resend.textContent = `重新寄送驗證碼（${seconds}）`;
        return;
      }
      resend.disabled = false;
      resend.textContent = '重新寄送驗證碼';
      if (resendTimer) {
        clearInterval(resendTimer);
        resendTimer = null;
      }
    };
    update();
    resendTimer = setInterval(update, 1000);
  }

  function setBusy(busy) {
    if (!send.hidden) send.disabled = busy;
    confirmButton.disabled = busy;
    code.disabled = busy;
    password.disabled = busy;
    passwordConfirm.disabled = busy;
    back.disabled = busy;
    if (busy) resend.disabled = true;
  }

  function resetRecovery() {
    recovery = null;
    if (resendTimer) clearInterval(resendTimer);
    resendTimer = null;
    fields.hidden = true;
    send.hidden = false;
    employeeNo.disabled = false;
    code.value = '';
    password.value = '';
    passwordConfirm.value = '';
    delivery.textContent = '';
    resend.disabled = false;
    resend.textContent = '重新寄送驗證碼';
    setMessage('');
  }

  function setMessage(text, error = false) {
    message.textContent = text;
    message.classList.toggle('error', Boolean(error));
  }

  function showLoginError() {
    const error = new URLSearchParams(location.search).get('error');
    const messages = {
      invalid: '請輸入正確的 4 碼員工編號與密碼。',
      failed: '員工編號或密碼不正確。',
      access: '此員工帳號沒有記帳系統 App Access。',
      rate: '登入嘗試次數過多，請稍後再試。',
      service: '中央帳號服務目前無法使用，請稍後再試。',
      'first-login': '請先至 CY Web 帳號管理入口完成首次帳號啟用與 Email 驗證。'
    };
    if (!error || !messages[error]) return;
    loginMessage.textContent = messages[error];
    loginMessage.classList.add('error');
    history.replaceState(null, '', '/login');
  }
});
