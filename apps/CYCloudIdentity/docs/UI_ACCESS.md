# CYCloud Identity — Management UI Access

> **Status: approved target consumer UI contract; runtime migration pending.**
>
> The deployed CYCloud Identity `0.1.14` / current CY Web management UI still reflects the earlier Super-Admin-only + Identity Group implementation in several areas. New UI work must converge on this document and `ROLE_AND_ACCESS_MODEL.md`.

## 1. Terminology

Consumer-facing management UI uses:

- **超級管理員 (Super Admin)** — protected unique Workspace final authority;
- **管理員 (Admin)** — normal Workspace administrator;
- **身分管理員 (Identity Admin)** — special capability on an ADMIN, not a fourth Workspace role;
- **使用者 (User)** — normal Employee.

Stable protocol/storage identifiers such as `isWorkspaceSuperAdmin`, `superAdminEmployeeId` and `super_admin_employee_id` may remain during migration.

## 2. Employee lifecycle states

Employee management UI distinguishes at least:

- **尚未驗證／待啟用** — first activation incomplete;
- **啟用** — activation complete and enabled;
- **停用** — previously activated and currently disabled;
- **啟用 · Email 待驗證** — activated account whose Email is being re-verified after an authorized Email change/recovery.

A pending account must never be displayed as a disabled activated account.

## 3. Employee creation

Employee creation selects the initial role at creation time.

- normal ADMIN can create `USER` only;
- Identity Admin can create `USER` or `ADMIN`;
- Super Admin can create `USER` or `ADMIN`;
- no create form directly grants Identity Admin capability.

After successful creation:

- the Employee remains pending first activation;
- the first activation email is sent automatically;
- the email contains a direct link to the CY Web activation flow;
- Application Access / CY Web Module Access are configured separately after creation.

## 4. Pending Employee actions

Pending first-activation rows expose at least:

```text
編輯 | 重寄啟用信 | 刪除
```

They do **not** expose an admin-side `啟用` button because the Employee must complete Email verification and first-password setup.

If the first activation email fails to send, the pending Employee remains created and the UI must show a clear send-failure state with a resend action.

Only never-activated Employees can be deleted. Activated or disabled Employees remain historical records and are not physically deleted.

## 5. Normal ADMIN management surface

Normal ADMIN may perform ordinary USER lifecycle work:

- create USER;
- edit pending USER data;
- resend pending USER activation email;
- delete never-activated USER;
- disable/re-enable activated USER.

Normal ADMIN does not receive:

- App Access management;
- CY Web Module Access management;
- USER ↔ ADMIN controls;
- forced Email recovery for activated Employees;
- Identity Admin grant/revoke;
- Super Admin transfer;
- Workspace Recovery / security-core / OTP policy controls.

## 6. Identity Admin management surface

Identity Admin receives the normal ADMIN surface plus:

- create USER or ADMIN;
- USER ↔ ADMIN;
- Application Access management for USER / ADMIN / other Identity Admin accounts;
- CY Web Module Access management through the CY Web management UI;
- forced Email recovery for activated Employees.

Identity Admin UI must prevent self-escalation:

- its own App Access controls are non-editable;
- its own CY Web Module Access controls are non-editable;
- Identity Admin cannot grant/revoke Identity Admin capability;
- Identity Admin cannot demote another Identity Admin to USER;
- Identity Admin cannot modify Super Admin identity/access/security state.

Another Identity Admin or Super Admin may change an Identity Admin's normal App/Module Access.

## 7. Super Admin management surface

Super Admin receives the full management surface and is the only authority for:

- grant/revoke Identity Admin capability;
- protected Identity Admin demotion path;
- Super Admin transfer;
- Workspace Recovery / final-control recovery;
- Workspace security-core / OTP policy.

Super Admin Application Access is implicit for all Workspace-enabled CY Apps and is not presented as a cancellable ordinary grant.

CY Web Module Access for Super Admin is also implicit/all-enabled and non-cancellable.

## 8. Application Access UI

Application Access is entry authorization, not the App's detailed business permission model.

- CY Web core entry is shown as required/locked `TRUE` for every valid Employee and cannot be unchecked.
- Other Apps default to no grant after Employee creation.
- normal ADMIN sees no Access-management controls.
- Identity Admin / Super Admin manage eligible Employee App Access under the anti-self-escalation restrictions above.
- changing Role does not silently change existing Access.

## 9. CY Web multi-module special case

CY Web is the core multi-module App.

CY Web entry remains available for self-service even when an Employee has zero business-module access.

CY Web itself owns Module Access. The Identity management UI may surface those controls in the same management experience, but the authoritative data/enforcement remains CY Web-local.

- Super Admin: all modules implicitly allowed;
- Identity Admin / Super Admin: may configure eligible Employee Module Access;
- normal ADMIN: no Module Access configuration capability;
- ADMIN with access to a module has full administration authority within that module.

## 10. Forced Email recovery UI

For an activated Employee with an unusable Email:

- Identity Admin / Super Admin may start forced Email change;
- new Email becomes unverified;
- existing password remains;
- existing sessions are revoked;
- account remains an activated account;
- UI exposes resend verification for the new Email.

Normal ADMIN cannot perform this operation.

## 11. Super Admin transfer

The Super Admin transfer entry belongs with Employee account management rather than a separate authority block.

The current Super Admin row presents the transfer action. The flow still requires provider-owned credential re-authentication and Email OTP confirmation, and the Recovery Email follows the new Super Admin on successful transfer.

## 12. OTP settings visibility

Workspace security-core / OTP policy controls remain Super-Admin-only.

Non-Super-Admin accounts must not receive a disabled fake control or a permission-error page inviting an unsupported operation. Backend authorization remains authoritative even when controls are hidden.

Established defaults remain:

- resend cooldown: 60 seconds;
- maximum verification attempts: 5;
- maximum sent OTP messages per Email + purpose per hour: 5;
- Workspace daily Email limit: 100, capped by the lower system-wide ceiling;
- OTP validity: 10 minutes.

## 13. Ownership

CY Web may host the account-management UI. Shared Employee identity, Workspace Role, Identity Admin capability, Application Access, Session, Email/OTP/Recovery authority belong to CYCloud Identity.

CY Web Module Access belongs to CY Web and is enforced server-side by CY Web.
