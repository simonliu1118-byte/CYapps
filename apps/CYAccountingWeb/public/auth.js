document.addEventListener('DOMContentLoaded', () => {
  const overlay = document.querySelector('#authOverlay');
  const form = document.querySelector('#loginForm');
  const employeeNo = document.querySelector('#loginEmployeeNo');
  const password = document.querySelector('#loginPassword');
  const submit = document.querySelector('#loginSubmit');
  const message = document.querySelector('#loginMessage');
  const currentUser = document.querySelector('#currentUser');
  const logoutButton = document.querySelector('#logoutButton');
  const loginCard = overlay?.querySelector('.auth-card');
  const version = document.querySelector('.version');
  if (version) version.textContent = 'V0.5.0';

  const forgotButton = document.createElement('button');
  forgotButton.type = 'button';
  forgotButton.className = 'auth-link-button';
  forgotButton.textContent = '忘記密碼';
  submit?.insertAdjacentElement('afterend', forgotButton);

  const resetCard = document.createElement('div');
  resetCard.className = 'auth-card auth-reset-card hidden';
  resetCard.innerHTML = `
    <h2>重設密碼</h2>
    <p class="auth-subtitle">使用員工帳號目前已驗證的 Email</p>
    <form id="passwordResetForm" class="auth-form" autocomplete="off">
      <label><span>員工編號</span><input id="resetEmployeeNo" type="text" inputmode="numeric" maxlength="4" pattern="\\d{4}" autocomplete="username" required></label>
      <button id="resetSendCode" class="primary" type="submit">寄送驗證碼</button>
      <div id="resetVerifyFields" class="auth-reset-fields hidden">
        <p id="resetDeliveryText" class="auth-delivery-text"></p>
        <label><span>Email 驗證碼</span><input id="resetOtp" type="text" inputmode="numeric" maxlength="6" pattern="\\d{6}" autocomplete="one-time-code"></label>
        <label><span>新密碼</span><input id="resetNewPassword" type="password" maxlength="200" autocomplete="new-password"></label>
        <label><span>再次輸入新密碼</span><input id="resetConfirmPassword" type="password" maxlength="200" autocomplete="new-password"></label>
        <button id="resetConfirm" class="primary" type="submit">重設密碼</button>
        <button id="resetResend" class="secondary auth-full-button" type="button">重新寄送驗證碼</button>
      </div>
      <p id="resetMessage" class="auth-message" aria-live="polite"></p>
      <button id="resetBack" class="auth-link-button" type="button">返回登入</button>
    </form>
    <p class="auth-note">驗證碼為 6 碼數字，10 分鐘內有效。新密碼至少 8 碼，且只能使用英文字母或數字。</p>`;
  overlay?.appendChild(resetCard);

  const resetForm = resetCard.querySelector('#passwordResetForm');
  const resetEmployeeNo = resetCard.querySelector('#resetEmployeeNo');
  const resetSendCode = resetCard.querySelector('#resetSendCode');
  const resetVerifyFields = resetCard.querySelector('#resetVerifyFields');
  const resetDeliveryText = resetCard.querySelector('#resetDeliveryText');
  const resetOtp = resetCard.querySelector('#resetOtp');
  const resetNewPassword = resetCard.querySelector('#resetNewPassword');
  const resetConfirmPassword = resetCard.querySelector('#resetConfirmPassword');
  const resetConfirm = resetCard.querySelector('#resetConfirm');
  const resetResend = resetCard.querySelector('#resetResend');
  const resetMessage = resetCard.querySelector('#resetMessage');
  const resetBack = resetCard.querySelector('#resetBack');

  let resetChallenge = null;
  let resendTimer = null;

  form?.addEventListener('submit', async event => {
    event.preventDefault();
    setLoginMessage('');
    const no = String(employeeNo?.value || '').trim();
    const pwd = String(password?.value || '');
    if (!/^\d{4}$/.test(no) || !pwd) {
      setLoginMessage('請輸入 4 碼員工編號與密碼。', true);
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
      setLoginMessage(error.message || '登入失敗。', true);
      password?.select();
      submit.disabled = false;
    }
  });

  forgotButton.addEventListener('click', () => {
    resetEmployeeNo.value = /^\d{4}$/.test(String(employeeNo?.value || '').trim()) ? employeeNo.value.trim() : '';
    showReset();
  });

  resetBack?.addEventListener('click', () => {
    showLogin();
  });

  resetResend?.addEventListener('click', async () => {
    await sendResetCode();
  });

  resetForm?.addEventListener('submit', async event => {
    event.preventDefault();
    if (!resetChallenge) {
      await sendResetCode();
      return;
    }
    await confirmPasswordReset();
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

  async function sendResetCode() {
    setResetMessage('');
    const no = String(resetEmployeeNo?.value || '').trim();
    if (!/^\d{4}$/.test(no)) {
      setResetMessage('請輸入 4 碼員工編號。', true);
      resetEmployeeNo?.focus();
      return;
    }

    setResetBusy(true);
    try {
      const response = await fetch('/api/auth/password-reset/challenge', {
        method: 'POST',
        headers: { 'content-type': 'application/json' },
        body: JSON.stringify({ employeeNo: no })
      });
      const data = await response.json().catch(() => ({}));
      if (!response.ok || data.ok === false) throw new Error(data.error || '驗證碼寄送失敗。');
      resetChallenge = data.challenge;
      resetVerifyFields.classList.remove('hidden');
      resetSendCode.classList.add('hidden');
      resetDeliveryText.textContent = `驗證碼已寄至 ${resetChallenge.maskedEmail}，請在 10 分鐘內完成驗證。`;
      setResetMessage('驗證碼已寄出。');
      startResendCountdown(resetChallenge.resendAfter);
      resetOtp?.focus();
    } catch (error) {
      setResetMessage(error.message || '驗證碼寄送失敗。', true);
    } finally {
      setResetBusy(false);
    }
  }

  async function confirmPasswordReset() {
    setResetMessage('');
    const no = String(resetEmployeeNo?.value || '').trim();
    const otp = String(resetOtp?.value || '').trim();
    const nextPassword = String(resetNewPassword?.value || '');
    const confirmPassword = String(resetConfirmPassword?.value || '');
    if (!/^\d{6}$/.test(otp)) {
      setResetMessage('Email 驗證碼必須是 6 碼數字。', true);
      resetOtp?.focus();
      return;
    }
    if (!/^[A-Za-z0-9]{8,200}$/.test(nextPassword)) {
      setResetMessage('新密碼至少 8 碼，且只能使用英文字母或數字。', true);
      resetNewPassword?.focus();
      return;
    }
    if (nextPassword !== confirmPassword) {
      setResetMessage('兩次輸入的新密碼不一致。', true);
      resetConfirmPassword?.focus();
      return;
    }

    setResetBusy(true);
    try {
      const response = await fetch('/api/auth/password-reset/confirm', {
        method: 'POST',
        headers: { 'content-type': 'application/json' },
        body: JSON.stringify({
          employeeNo: no,
          challengeId: resetChallenge.challengeId,
          otp,
          newPassword: nextPassword
        })
      });
      const data = await response.json().catch(() => ({}));
      if (!response.ok || data.ok === false) throw new Error(data.error || '密碼重設失敗。');
      employeeNo.value = no;
      password.value = '';
      resetState();
      showLogin('密碼已重設，請使用新密碼登入。', false);
      password?.focus();
    } catch (error) {
      setResetMessage(error.message || '密碼重設失敗。', true);
    } finally {
      setResetBusy(false);
    }
  }

  function startResendCountdown(resendAfter) {
    if (resendTimer) clearInterval(resendTimer);
    const update = () => {
      const seconds = Math.max(0, Math.ceil((Date.parse(resendAfter) - Date.now()) / 1000));
      if (seconds > 0) {
        resetResend.disabled = true;
        resetResend.textContent = `重新寄送驗證碼（${seconds}）`;
        return;
      }
      resetResend.disabled = false;
      resetResend.textContent = '重新寄送驗證碼';
      if (resendTimer) {
        clearInterval(resendTimer);
        resendTimer = null;
      }
    };
    update();
    resendTimer = setInterval(update, 1000);
  }

  function setResetBusy(busy) {
    if (resetSendCode && !resetSendCode.classList.contains('hidden')) resetSendCode.disabled = busy;
    if (resetConfirm) resetConfirm.disabled = busy;
    if (resetEmployeeNo) resetEmployeeNo.disabled = busy || Boolean(resetChallenge);
    if (resetOtp) resetOtp.disabled = busy;
    if (resetNewPassword) resetNewPassword.disabled = busy;
    if (resetConfirmPassword) resetConfirmPassword.disabled = busy;
    if (resetBack) resetBack.disabled = busy;
    if (resetResend && busy) resetResend.disabled = true;
  }

  function resetState() {
    resetChallenge = null;
    if (resendTimer) clearInterval(resendTimer);
    resendTimer = null;
    resetVerifyFields?.classList.add('hidden');
    resetSendCode?.classList.remove('hidden');
    if (resetEmployeeNo) resetEmployeeNo.disabled = false;
    if (resetOtp) resetOtp.value = '';
    if (resetNewPassword) resetNewPassword.value = '';
    if (resetConfirmPassword) resetConfirmPassword.value = '';
    if (resetDeliveryText) resetDeliveryText.textContent = '';
    if (resetResend) {
      resetResend.disabled = false;
      resetResend.textContent = '重新寄送驗證碼';
    }
    setResetMessage('');
  }

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

  function showReset() {
    setLoginMessage('');
    loginCard?.classList.add('hidden');
    resetCard.classList.remove('hidden');
    overlay?.classList.remove('hidden');
    document.body.classList.add('auth-locked');
    setTimeout(() => resetEmployeeNo?.focus(), 0);
  }

  function showLogin(text = '', isError = true) {
    resetState();
    resetCard.classList.add('hidden');
    loginCard?.classList.remove('hidden');
    overlay?.classList.remove('hidden');
    document.body.classList.add('auth-locked');
    setLoginMessage(text, isError);
    setTimeout(() => employeeNo?.focus(), 0);
  }

  function setLoginMessage(text, isError = false) {
    if (!message) return;
    message.textContent = text;
    message.classList.toggle('error', isError);
  }

  function setResetMessage(text, isError = false) {
    if (!resetMessage) return;
    resetMessage.textContent = text;
    resetMessage.classList.toggle('error', isError);
  }
});
