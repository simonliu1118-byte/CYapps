import assert from "node:assert/strict";
import test from "node:test";

import { createFirstLoginEmail, firstLoginPortalUrl } from "../dist/first-login-email.js";

test("new Employee and resend mail share an explicit clickable CY Web verification link", () => {
  const mail = createFirstLoginEmail("0199", "TempP4ss", "https://portal.example.test/settings", "production");
  assert.match(mail.text, /https:\/\/portal\.example\.test\//);
  assert.match(mail.text, /前往|點擊/);
  assert.match(mail.text, /一次性首次登入密碼：TempP4ss/);
  assert.match(mail.text, /CYACCweb/);
  assert.match(mail.text, /10 分鐘/);
  assert.match(mail.text, /24 小時/);
  assert.match(mail.html, /<a href="https:\/\/portal\.example\.test\/">前往 CY Web 完成 Email 驗證<\/a>/);
  assert.doesNotMatch(mail.html, /href="[^"]*TempP4ss/);
  assert.doesNotMatch(mail.html, /href="[^"]*(?:employeeNo|token|password)=/);
});

test("Email HTML escapes special characters; the initial password is never placed in the URL", () => {
  const mail = createFirstLoginEmail("0999", 'p&<>"word', "https://portal.example.test/", "production");
  assert.match(mail.html, /p&amp;&lt;&gt;&quot;word/);
  assert.doesNotMatch(mail.html, /href="[^"]*word/);
  assert.match(mail.text, /p&<>"word/);
});

test("login URL follows the configured CY Web origin and rejects unsafe/missing configurations", () => {
  assert.equal(firstLoginPortalUrl("https://portal.example.test/some/path", "production"), "https://portal.example.test/");
  assert.equal(firstLoginPortalUrl("http://localhost:8787/", "local-test"), "http://localhost:8787/");
  for (const value of [undefined, "", "ftp://portal.example.test", "http://portal.example.test", "https://user:pass@portal.example.test", "https://portal.example.test/?token=leak", "https://portal.example.test/#fragment", "javascript:alert(1)"]) {
    assert.throws(() => firstLoginPortalUrl(value, "production"), /ACCOUNT_PORTAL_URL_/);
  }
  assert.throws(() => firstLoginPortalUrl("http://localhost:8787/", "production"), /ACCOUNT_PORTAL_URL_INVALID/);
});
