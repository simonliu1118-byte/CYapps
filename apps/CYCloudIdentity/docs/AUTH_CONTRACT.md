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

`groupKeys` or `applicationRoleKey` may exist temporarily as compatibility fields but are not forward authority.

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

Management APIs remain provider-authorized server-side. Current source may temporarily retain legacy path names such as `.../activation/resend`; those names are transport compatibility only. Consumer UI/action semantics are **重寄驗證 Email** and must not expose a separate activation product concept.

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