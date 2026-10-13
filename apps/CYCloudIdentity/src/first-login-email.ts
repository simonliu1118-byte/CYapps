// First-login Email content is owned by CYID; no public consumer gets a credential verifier.
export interface FirstLoginEmailContent {
  text: string;
  html: string;
}

function htmlEscape(value: string): string {
  return value.replace(/[&<>"']/g, (char) => {
    switch (char) {
      case "&": return "&amp;";
      case "<": return "&lt;";
      case ">": return "&gt;";
      case '"': return "&quot;";
      default: return "&#39;";
    }
  });
}

/** Use the provider's deployed CY Web portal setting; never invent another login origin. */
export function firstLoginPortalUrl(configuredUrl: string | undefined, environment?: string): string {
  if (!configuredUrl?.trim()) throw new Error("ACCOUNT_PORTAL_URL_NOT_CONFIGURED");

  let url: URL;
  try {
    url = new URL(configuredUrl.trim());
  } catch {
    throw new Error("ACCOUNT_PORTAL_URL_INVALID");
  }
  const localhost = ["localhost", "127.0.0.1", "[::1]"].includes(url.hostname);
  const allowedProtocol = url.protocol === "https:"
    || (url.protocol === "http:" && environment !== "production" && localhost);
  if (!allowedProtocol || !url.hostname || url.username || url.password || url.search || url.hash) {
    throw new Error("ACCOUNT_PORTAL_URL_INVALID");
  }

  // CY Web's AuthGate is the root entrypoint, not an app-specific consumer login.
  return new URL("/", url).href;
}

export function createFirstLoginEmail(
  employeeNo: string,
  initialPassword: string,
  configuredPortalUrl: string | undefined,
  environment?: string,
): FirstLoginEmailContent {
  const portalUrl = firstLoginPortalUrl(configuredPortalUrl, environment);
  const text = [
    "您的 CY Web 帳號已建立。",
    "",
    "請點擊以下 CY Web 連結，使用一次性首次登入密碼完成 Email 驗證及設定正式密碼：",
    portalUrl,
    "",
    "員工編號：" + employeeNo,
    "一次性首次登入密碼：" + initialPassword,
    "",
    "此密碼僅可使用一次，並於 24 小時後失效。",
    "請先在 CY Web 完成 Email 驗證，不要直接使用此一次性密碼登入 CYACCweb 或其他應用程式。",
    "登入 CY Web 後，請在 10 分鐘內設定新的正式密碼。",
    "設定完成後系統會回到登入頁，請使用新密碼重新登入。",
    "",
    "若首次登入密碼或驗證流程已逾期，請聯絡有權限的管理員重寄驗證 Email。",
    "若您未預期收到此信，請聯絡系統管理員。",
  ].join("\n");

  const safeUrl = htmlEscape(portalUrl);
  const html = [
    '<!doctype html><html lang="zh-Hant"><body>',
    "<p>您的 CY Web 帳號已建立。</p>",
    "<p>請點擊以下連結，使用一次性首次登入密碼完成 Email 驗證及設定正式密碼：</p>",
    '<p><a href="' + safeUrl + '">前往 CY Web 完成 Email 驗證</a></p>',
    '<p>若無法點擊，請複製此網址至瀏覽器：<a href="' + safeUrl + '">' + safeUrl + "</a></p>",
    "<p>員工編號：<strong>" + htmlEscape(employeeNo) + "</strong></p>",
    "<p>一次性首次登入密碼：<strong>" + htmlEscape(initialPassword) + "</strong></p>",
    "<p>此密碼僅可使用一次，並於 24 小時後失效。</p>",
    "<p>請先在 CY Web 完成 Email 驗證，不要直接使用此一次性密碼登入 CYACCweb 或其他應用程式。</p>",
    "<p>登入 CY Web 後，請在 10 分鐘內設定新的正式密碼；完成後請使用新密碼重新登入。</p>",
    "<p>若首次登入密碼或驗證流程已逾期，請聯絡有權限的管理員重寄驗證 Email。</p>",
    "<p>若您未預期收到此信，請聯絡系統管理員。</p>",
    "</body></html>",
  ].join("\n");
  return { text, html };
}
