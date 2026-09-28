# CYCloud Identity — OTP Security

> Status: Shared Identity OTP security contract. This document contains generic protocol and bounds only; actual Workspace values are runtime data and are not committed to Public Git.

## Ownership boundary

CYCloud Identity owns authentication controls and enforcement. A consumer App such as CY Web may provide the management UI, but it must call the Identity admin API and must not copy these settings into its own business database.

Only the current protected Workspace highest authority may change Workspace OTP controls. Ordinary Identity Groups do not confer this authority.

## Established defaults

Keep the existing defaults unless the Workspace highest authority changes them through the management UI:

- OTP resend cooldown: 60 seconds;
- maximum OTP verification attempts: 5;
- maximum sent OTP messages per Email + purpose per hour: 5;
- Workspace Email daily limit: 100, capped by the lower system-wide Email Daily Ceiling when that ceiling is lower;
- OTP validity period: 10 minutes.

The OTP validity period is intentionally not editable in the initial contract.

## Editable Workspace controls

The highest authority may adjust these values without a code change:

- OTP resend cooldown: 30–600 seconds;
- maximum OTP verification attempts: 3–10;
- maximum sent OTP messages per Email + purpose per hour: 1–20;
- Workspace Email daily limit: 1 through the current system Email Daily Ceiling.

## System ceiling

`EMAIL_DAILY_BUDGET` is a runtime-only global safety ceiling for the shared Email provider account. It is not a Workspace preference and cannot be raised through the Workspace admin API.

This separation is intentional:

- Workspace settings control how conservative one Workspace wants to be;
- the global ceiling protects the shared provider/free-tier account from aggregate overuse;
- a Workspace daily limit may be lower than the global ceiling, never higher.

## Admin API

Consumers call through a private Service Binding and pass the current Identity session:

```text
Authorization: Bearer <opaque Identity session token>
X-Identity-Application: <runtime application id>
```

Read current settings:

```text
GET /v1/admin/security-policy
```

Update current Workspace settings:

```text
PUT /v1/admin/security-policy
Content-Type: application/json
```

Request body:

```json
{
  "otpResendCooldownSeconds": 60,
  "otpMaxAttempts": 5,
  "otpMaxSentPerEmailPurposeHour": 5,
  "emailDailyLimit": 100
}
```

Successful responses include the effective Workspace settings and the read-only `systemEmailDailyCeiling`.

## UI visibility

CY Web renders the OTP security-settings entry only when the resolved Identity principal has `isWorkspaceSuperAdmin = true`.

For every other Identity Group or Employee, the navigation item, page link and action controls are omitted entirely. The normal user experience is therefore not "attempt then reject"; there is no OTP-settings entry to use.

CYCloud Identity still validates highest authority on the API as a backend security boundary so a manually crafted request cannot bypass the UI.

## Safety properties

- Server-side bounds are authoritative; a UI cannot bypass them.
- Only a currently valid highest-authority Identity session can update these settings.
- Changes increment a revision and write an Identity Audit event.
- Existing OTP challenges retain the limits captured when they were issued; changing settings does not retroactively make an already-issued challenge weaker or stronger.
- Global and Workspace daily Email budgets are both enforced when an OTP belongs to an existing Workspace.
- First-Workspace bootstrap occurs before Workspace settings exist, so it uses the established defaults plus the global Email ceiling.

## CY Web management UI plan

CY Web may expose a Shared Identity management section for:

- Employees;
- extensible Identity Groups;
- Application entry access;
- highest-authority / Recovery Email operations;
- OTP security settings, visible only to the Workspace highest authority.

The UI is only a management client. CYCloud Identity remains the authority for account data, credentials, OTP, Recovery, sessions and security controls. This allows CYInvoice account-management UI/ownership to be removed later without moving Identity authority into CY Web.
