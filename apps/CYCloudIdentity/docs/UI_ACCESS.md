# CYCloud Identity — Management UI Access

> **Status:** approved consumer UI contract. Backend authority remains CYID; current rollout/acceptance state is tracked only in `../TODO.md`.

## 1. Terminology

Consumer-facing role names:

- **超級管理員 (Super Admin)**
- **管理員 (Admin)**
- **身分管理員 (Identity Admin)** — ADMIN capability, not a fourth role
- **使用者 (User)**

New-Employee flow is always called **Email 驗證**. Do not expose a separate「啟用帳號」entry or rename the process because the Email contains a temporary first-login password.

## 2. Employee lifecycle display

- **Email 未驗證** — new Employee has not completed first Email verification/permanent-password setup;
- **啟用** — lifecycle complete and enabled;
- **停用** — previously completed lifecycle and disabled;
- **啟用 · Email 待驗證** — activated account whose replacement Email still requires verification.

Pending Email verification must not look like a disabled activated account.

## 3. Employee creation and first verification

Create form selects initial role under current actor authority. Identity Admin capability is never granted in create.

After create:

- Employee remains Email-unverified;
- CYID automatically sends the verification Email;
- Email includes the one-time first-login password;
- user goes to the ordinary CY Web login screen;
- valid initial password enters a forced permanent-password screen;
- after password setup CY Web returns to the login screen and the user must log in again with the new permanent password.

CY Web must not display an independent「啟用帳號」button/link.

## 4. Pending actions

At minimum:

~~~text
編輯 | 重寄驗證 Email | 刪除
~~~

`重寄驗證 Email` creates a new initial password and expiry and invalidates the old one. Editing the pending Email does the same for the new address.

If delivery fails, keep the Employee and show an actionable resend state. Only never-completed first-verification Employees may be deleted.

## 5. Normal ADMIN surface

Normal ADMIN may create USER, edit Email-unverified USER, resend verification Email, delete never-verified USER and disable/re-enable activated USER.

Normal ADMIN gets no App Access controls, CY Web Module Access controls, USER<->ADMIN controls, forced activated-account Email recovery, Identity Admin management, Super Admin transfer or security-core settings.

## 6. Identity Admin surface

Identity Admin gets normal ADMIN actions plus USER<->ADMIN, eligible direct App Access, CY Web Module Access through CY Web and forced activated-account Email recovery.

UI must prevent self-escalation: own App/Module Access non-editable; no Identity Admin grant/revoke; no direct demotion of another Identity Admin; no Super Admin protected-state actions.

## 7. Super Admin surface

Super Admin has the full surface and is the only authority for Identity Admin grant/revoke, protected demotion path, Super Admin transfer, Workspace Recovery/final-control recovery and security-core/OTP policy.

Super Admin App Access and CY Web Module Access are implicit/all-enabled and not shown as cancellable ordinary grants.

## 8. Application and CY Web Module Access

- CY Web core entry is required/locked TRUE for every valid Employee.
- Other Apps default ungranted after create.
- normal ADMIN sees no Access-management controls.
- Identity Admin/Super Admin manage eligible App Access under anti-self-escalation rules.
- CY Web module controls may appear in the same account-management experience, but authoritative data/enforcement is CY Web-local.

## 9. Forced Email recovery

For an activated Employee, Identity Admin/Super Admin may replace an unusable Email. Password remains; new Email becomes unverified; sessions are revoked; UI exposes resend verification. This state is not the new-Employee first-login flow.

## 10. Super Admin transfer and OTP settings

Super Admin transfer belongs with Employee account management and still requires provider-owned re-authentication plus Email OTP. Workspace security-core/OTP policy controls are visible only to Super Admin; backend authorization remains authoritative.

Established OTP defaults and editable bounds live only in `OTP_SECURITY.md`; this UI contract does not duplicate them.

## 11. Ownership

CY Web may host the account-management UI. CYID owns Employee identity, role/capability, App Access, credentials, session, Email verification, OTP/recovery and security policy. CY Web owns its business Module Access and enforces it server-side.