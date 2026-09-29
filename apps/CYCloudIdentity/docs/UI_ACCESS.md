# CYCloud Identity — Management UI Access

> Status: consumer UI contract. CYCloud Identity remains the authentication authority; consumer Apps only render management surfaces.

## Super Admin naming

Consumer-facing management UI uses **超級管理員 (Super Admin)** for the protected Workspace highest-authority account. Existing protocol and storage identifiers such as `isWorkspaceSuperAdmin`, `superAdminEmployeeId` and `super_admin_employee_id` remain stable compatibility fields and are not renamed by this UI decision.

There is exactly one protected Workspace Super Admin at a time. This authority is separate from ordinary editable Identity Groups and from an Application compatibility role named `ADMIN`.

## Employee lifecycle presentation

Employee management UI must distinguish these states instead of treating every `enabled = 0` Employee as the same condition:

- **尚未驗證／待啟用** — first-time activation is incomplete; Email is not yet verified and no credential exists. The UI must not present an enable action for this state.
- **啟用** — activation is complete and the Employee is enabled.
- **停用** — activation was completed previously but the Employee is currently disabled. This state may present an enable action subject to server-side authorization.

A Workspace Super Admin may delete an Employee only while that Employee is still pending first-time activation. The provider must reject deletion after Email verification/credential activation, and must reject deletion of the current Workspace Super Admin. Consumer UI should therefore present **刪除** only for pending-activation rows and should not expose the pending-delete path for activated or disabled accounts.

The Super Admin transfer entry belongs with Employee account management rather than in a separate authority-management panel. The current Super Admin row may present a transfer action; when no eligible target exists the action is disabled. Transfer still requires the provider-owned current-credential recheck and Email OTP confirmation.

## OTP settings visibility

The OTP security-settings entry is rendered only when the current resolved Identity principal has `isWorkspaceSuperAdmin = true`.

For every other Identity Group or ordinary Employee, the OTP security-settings navigation item, page link and action controls are not rendered. The consumer App should not present a disabled button, a permission-error page, or an interactive path that invites a non-Super-Admin user to attempt the operation.

This is a UI rule only. CYCloud Identity still validates Super Admin authority on the admin API so a manually crafted request cannot bypass the UI. That server-side validation is a security boundary, not a user-facing authorization workflow.

## Default OTP controls

Keep the established defaults unless the Workspace Super Admin changes them through the management UI:

- resend cooldown: 60 seconds;
- maximum verification attempts: 5;
- maximum sent OTP messages per Email + purpose per hour: 5;
- Workspace daily Email limit: 100, capped by the lower system-wide Email Daily Ceiling when the provider/free-tier ceiling is lower.

OTP validity remains 10 minutes and is not user-editable in the initial contract.

## Ownership

CY Web may host the account-management UI. Employee, Identity Group, Application entry-access, Recovery/Email and OTP security data continue to be stored and enforced by CYCloud Identity.
