# CYCloud Identity — Authentication Contract

> **Status:** provider authentication contract for current CYID `0.3.x`. All consumer implementations must also follow `CONSUMER_INTEGRATION_STANDARD.md`; exact source/deployment status is tracked only in `../TODO.md`.

## 1. Transport boundary

Cloud App Workers call CYID through a private Cloudflare Service Binding. Browsers never receive provider secrets, credential verifiers, OTP peppers or Identity D1 identifiers.

Normal Identity session tokens are opaque. Consumer Apps keep the raw token only in reviewed protected transport such as an `HttpOnly; Secure` cookie with an app-tested SameSite policy; CYID stores only a digest. Shared browser/session transport requirements and permitted app-specific cookie compatibility are defined in `CONSUMER_INTEGRATION_STANDARD.md`.

## 2. Normalized principal

Every normal authenticated session resolves at least:

~~~ts
interface IdentityPrincipal {
  workspaceId: string;
  employeeId: string;
  employeeNo: string;
  displayName: string;
  workspaceRole: "SUPER_ADMIN" | "ADMIN" | "USER";
  isIdentityAdmin: boolean;
  emailVerified: boolean;
  isWorkspaceSuperAdmin: boolean;
  credentialVersion: number;
  employeeRevision: number;
}
~~~

Consumer Contract 1.0.2 retires the optional legacy `groupKeys` and `applicationRoleKey` projections. Consumers read the direct fields above and must accept their absence.

## 3. Login endpoint

`POST /v1/identity/login`

Request:

~~~json
{
  "workspaceId": "<runtime supplied workspace id>",
  "applicationId": "<runtime registered application id>",
  "employeeNo": "0001",
  "password": "<user supplied password>"
}
~~~

The same login endpoint handles two credential classes.

### 3.1 Permanent password

For an activated Employee with valid permanent credential and current App entry authority, success returns a normal principal + session.

~~~json
{
  "ok": true,
  "principal": { "workspaceRole": "USER", "emailVerified": true },
  "session": { "token": "<opaque token>", "expiresAt": "<UTC timestamp>" }
}
~~~

### 3.2 One-time first-login password

A valid initial credential is accepted **only for the configured CY Web core account application**. It never authorizes ordinary App entry and never creates a normal Identity session.

Success returns a mandatory password-change result with a short-lived first-login ticket:

~~~json
{
  "ok": true,
  "passwordChangeRequired": true,
  "firstLogin": {
    "token": "<opaque first-login token>",
    "employeeNo": "0001",
    "expiresAt": "<UTC timestamp>"
  }
}
~~~

The first-login ticket is not a session token and must be rejected by `/v1/identity/session/resolve`.

## 4. First-login completion

`POST /v1/identity/first-login/complete`

Request:

~~~json
{
  "workspaceId": "<runtime supplied workspace id>",
  "applicationId": "<core CY Web application id>",
  "token": "<first-login token>",
  "password": "<new permanent password>"
}
~~~

On success CYID atomically:

- validates the short-lived first-login ticket;
- creates the permanent password credential;
- marks Email verified and durable first lifecycle complete;
- invalidates/deletes the initial credential;
- consumes the first-login ticket;
- returns completion success **without a normal Identity session**.

Canonical success semantics:

~~~json
{
  "ok": true,
  "emailVerified": true,
  "passwordChanged": true,
  "reloginRequired": true
}
~~~

The consumer must return to the ordinary login screen. A normal session exists only after a fresh login with the new permanent password.

## 5. Initial credential expiry and resend

- Initial credential has explicit issue/sent/expiry metadata and is unusable after expiry.
- Login rate limiting applies to attempts using an initial credential.
- Authorized **重寄驗證 Email** generates a new initial password/verifier and new expiry; any prior initial credential and outstanding first-login ticket become invalid immediately.
- Editing the pending Employee Email does the same against the new Email.
- Delivery failure preserves the Employee as Email-unverified and exposes resend recovery; account creation is not rolled back.
- Consumer-facing terminology remains **Email 驗證**. No separate CY Web「啟用帳號」entry is part of this contract.

## 6. Application entry authorization

Normal authentication requires current Workspace, Employee, Application and App Access authority.

Entry rules:

1. Super Admin may enter every Workspace-enabled App;
2. every valid Employee may enter the configured CY Web core account application;
3. every other Employee/App pair requires enabled direct Employee Application Access.

Role and App Access are independent. Legacy Identity Group grants do not authorize login or session resolve.

## 7. Resolve and logout

`POST /v1/identity/session/resolve` re-checks current session, Workspace, Employee, credential version, Application, App Access, Workspace Role and Identity Admin capability on every request. Normal resolve is read-only.

`POST /v1/identity/logout` revokes the matching session. Consumer browser cookie clearing does not substitute for provider revocation.

## 8. Immediate invalidation

At minimum:

- Employee disable -> affected sessions invalid;
- Workspace Role change -> affected sessions invalid;
- permanent credential change/reset -> older sessions invalid;
- App Access removal -> that App session stops authorizing;
- forced Email recovery -> Employee sessions revoked;
- Super Admin transfer -> old/new authority sessions invalidated so both re-authenticate.

## 9. Email verification vs other OTP flows

New-Employee first verification uses the one-time first-login password flow above. OTP remains used for other reviewed purposes such as Workspace bootstrap, password recovery, activated-account Email verification/recovery and Super Admin transfer.

Do not rename the new-Employee product flow away from **Email 驗證** simply because its transport credential is a temporary password.

## 10. Management endpoints

Management APIs remain provider-authorized server-side. Initial Email re-send uses `.../email-verification/resend-initial`; activated-account Email re-verification uses `.../email-verification/resend`. The obsolete `.../activation/resend` alias is retired. Responses use only `emailVerificationDelivery` for initial Email delivery. Consumer UI/action semantics are **重寄驗證 Email** and must not expose a separate activation product concept.

Representative active operations include Employee create/update/delete-pending, resend Email verification, Identity Admin management, direct Application Access, forced Email recovery, current Email verification and security policy.

## 11. Credential and password security

- Permanent passwords are 8–16 Unicode characters.
- New permanent credentials use reviewed `scrypt` verifier parameters.
- Initial password plaintext is generated only for Email delivery and never returned by admin APIs or stored in D1/log/Audit/Git.
- Initial and permanent credential verifiers are separate records so an unverified Employee never appears to hold a normal permanent credential.
- Consumer Apps never read Identity D1 or copy credential/OTP/recovery tables.

## 12. Stable error handling

Consumers branch on HTTP status and stable `error.code`; provider internal messages are not user-facing authority. Relevant classes include malformed request, authentication failed, session invalid, application access denied, rate limited, first-login expired/invalid, authorization denied and provider unavailable.

Exact operation-specific codes belong to source/tests; UI must not infer authorization from hidden controls alone.
### Optional application registration

The core account portal may call the current-Workspace Super Admin registration endpoint defined in `CONSUMER_INTEGRATION_STANDARD.md` §17.1. It uses the same normal Session resolver, adds no credential/session authority and leaves ordinary Employee grants separate.

## 13. 單一登入交換（SSO handoff，consumer contract 1.1.0）

設計見 `SSO_PORTAL_DESIGN.md`；consumer 端（入口與目標 App）的完整義務見 `CONSUMER_INTEGRATION_STANDARD.md` §21。本節只定義 provider 接口。兩個接口都只走 private Service Binding，回應一律帶 `Cache-Control: no-store`、`Referrer-Policy: no-referrer`。

### 13.1 回跳網址登記

`applications.sso_return_url`（可空）由 core account portal 的 Super Admin 透過 §17.1 登記接口的選填欄位 `ssoReturnUrl` 設定（`null` 清除、省略不變）。只接受 `https://`＋網域名稱（不接受 IP、`localhost`、結尾點）＋固定路徑；不得含帳密、port、query、fragment、`%` 編碼、`\`、`//` 或 `.`／`..` 路徑段。主機名轉小寫後以 `https://<host><path>` 正規形式儲存；之後一律以**完整字串相等**比對。實際變更才寫 Audit `application_sso_return_url_updated`。已被其他 Workspace 登記的 App 不能在此變更回跳網址（`409 SSO_RETURN_URL_SHARED_APPLICATION`）。未登記回跳網址的 App 不能使用單一登入。

### 13.2 發交換碼

`POST /v1/identity/sso/handoff/issue`

~~~text
Authorization: Bearer <入口自己的 Session token>
x-identity-application: <core account application id>
~~~

~~~json
{ "applicationId": "<目標 App id>", "returnUrl": "<選填：只作斷言>" }
~~~

- 只有 core account application 可呼叫；其他 App（即使持有有效 Session）一律 `403 SSO_PORTAL_REQUIRED`。
- 入口 Session 依 §7 resolve 規則驗證，無效 `401 SESSION_INVALID`。
- 不接受其他欄位（例如 `redirectUri`、`workspaceId`）→ `400 INVALID_SSO_HANDOFF_REQUEST`。回跳網址只由 CYID 依登記值決定；`returnUrl` 若有提供，必須與登記值完全相等，否則 `400 SSO_RETURN_URL_MISMATCH`，不發碼。
- 員工尚未完成首次登入（未 activated 或仍有首次登入密碼）→ `403 FIRST_LOGIN_REQUIRED`。
- 目標 App 不存在、全域停用、本 Workspace 未啟用、是 core app 本身或未登記回跳網址 → `404 SSO_APPLICATION_UNAVAILABLE`。
- 依 §6 規則沒有進入權 → `403 APPLICATION_ACCESS_DENIED`。

成功：

~~~json
{ "ok": true, "code": "<43 字元 base64url>", "returnUrl": "<登記值>", "expiresAt": "<UTC，60 秒後>" }
~~~

交換碼為 32 bytes 隨機值（base64url、無 padding），D1 只存 SHA-256；有效 60 秒、單次使用，綁定 Workspace、員工、目標 App、發碼時的憑證版本與發碼用的入口 Session。

### 13.2.1 預覽（consumer contract 1.2.0）

`POST /v1/identity/sso/handoff/preview`，供入口確認畫面在按「前往」之前顯示 App 名稱與可否進入。唯讀：不產生交換碼、不寫 `sso_handoffs`、不寫 Audit，也不回傳回跳網址。

~~~text
Authorization: Bearer <入口自己的 Session token>
x-identity-application: <core account application id>
~~~

~~~json
{ "applicationId": "<目標 App id>" }
~~~

- 呼叫者檢查、入口 Session 檢查與目標 App 可用性（含 core app、未登記回跳網址）與 §13.2 相同：`403 SSO_PORTAL_REQUIRED`、`401 SESSION_INVALID`、`404 SSO_APPLICATION_UNAVAILABLE`。body 只能有 `applicationId`，任何其他欄位（含 `returnUrl`）→ `400 INVALID_SSO_HANDOFF_REQUEST`。
- 與 issue 不同：員工尚未完成首次登入、或沒有進入權，不是錯誤，而是 `200` 內的 `access` 值。

成功：

~~~json
{ "ok": true, "application": { "applicationId": "<目標 App id>", "displayName": "<App 名稱>", "origin": "https://<App 網域>" }, "access": "allowed | denied | first_login_required" }
~~~

`access` 規則與 issue 一致：未完成首次登入 → `first_login_required`；依 §6 沒有進入權 → `denied`；否則 `allowed`。預覽結果只供顯示，不構成授權；實際發碼仍須通過 §13.2 全部檢查。

### 13.3 兌換交換碼

`POST /v1/identity/sso/handoff/redeem`

~~~text
x-identity-application: <目標 App 自己的 application id>
~~~

~~~json
{ "code": "<交換碼>" }
~~~

- 以條件式 `UPDATE … WHERE used_at IS NULL AND expires_at > now` 原子標記已用並檢查 `changes()`；同時兌換只有一個會成功。交換碼第一次被出示即作廢，即使出示者不是綁定的 App。
- 兌換時重新檢查：呼叫者 App 等於綁定 App；Workspace 啟用；員工啟用且已完成首次登入；目前憑證版本等於發碼時版本；發碼用的入口 Session 未登出、未過期、憑證版本相符；App 全域啟用、本 Workspace 啟用、仍有登記回跳網址；依 §6 仍有進入權。
- 成功時以與 login（§3.1）相同的程式建立該 App 的**新** Session（相同 TTL 規則、只存 digest），回應形狀與 login 相同：

~~~json
{ "ok": true, "principal": { "workspaceRole": "USER" }, "session": { "token": "<opaque token>", "expiresAt": "<UTC>" } }
~~~

- 任何失敗（格式錯誤、無此碼、過期、已用、App 不符、權限或狀態已變更）一律 `401 SSO_HANDOFF_INVALID`，不透露原因。
- 失敗次數以既有 `LOGIN_RATE_LIMITER` 依呼叫 App 計數；超過時回 `429 AUTH_RATE_LIMITED`（`retry-after: 60`）。limiter 未設定時 fail closed：`503 AUTH_RATE_LIMITER_UNAVAILABLE`。

### 13.4 Audit、紀錄與清除

- Audit 事件：`sso_handoff_issued`、`sso_handoff_issue_failed`、`sso_handoff_redeemed`、`sso_handoff_redeem_failed`；欄位為 Workspace、員工（已知時）、App 與 `detail_json` 內的結果／原因代碼。交換碼、交換碼雜湊與任何 Session token 都不寫入 Audit、錯誤訊息或 console log；處理過程的儲存錯誤只記固定字串。超過失敗次數限制的請求不再寫 Audit（避免無上限寫入）。
- 過期資料：每次發碼時在同一 transaction 內刪除最多 100 筆已過期（含已用）的交換碼列；表大小因此受最近 60 秒發碼量限制，不需要 Cron（`PROJECT_RULES.md` §8）。
