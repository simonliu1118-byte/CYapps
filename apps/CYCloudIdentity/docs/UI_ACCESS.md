# CYCloud Identity — Management UI Access

> Status: consumer UI contract. CYCloud Identity remains the authentication authority; consumer Apps only render management surfaces.

## OTP settings visibility

The OTP security-settings entry is rendered only when the current resolved Identity principal has `isWorkspaceSuperAdmin = true`.

For every other Identity Group or ordinary Employee, the OTP security-settings navigation item, page link and action controls are not rendered. The consumer App should not present a disabled button, a permission-error page, or an interactive path that invites a non-highest-authority user to attempt the operation.

This is a UI rule only. CYCloud Identity still validates highest authority on the admin API so a manually crafted request cannot bypass the UI. That server-side validation is a security boundary, not a user-facing authorization workflow.

## Default OTP controls

Keep the established defaults unless the Workspace highest authority changes them through the management UI:

- resend cooldown: 60 seconds;
- maximum verification attempts: 5;
- maximum sent OTP messages per Email + purpose per hour: 5;
- Workspace daily Email limit: 100, capped by the lower system-wide Email Daily Ceiling when the provider/free-tier ceiling is lower.

OTP validity remains 10 minutes and is not user-editable in the initial contract.

## Ownership

CY Web may host the account-management UI. Employee, Identity Group, Application entry-access, Recovery/Email and OTP security data continue to be stored and enforced by CYCloud Identity.
