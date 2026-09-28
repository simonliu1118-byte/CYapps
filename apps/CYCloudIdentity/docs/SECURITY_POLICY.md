# CYCloud Identity — Security Policy

> Status: Shared Identity security-policy contract. This document contains generic protocol and bounds only; actual Workspace policy values are runtime data and are not committed to Public Git.

## Ownership boundary

CYCloud Identity owns authentication policy and enforcement. A consumer App such as CY Web may provide the management UI, but it must call the Identity admin API and must not copy these settings into its own business database.

Only the current protected Workspace highest authority may change Workspace OTP policy. Ordinary Identity Groups do not confer this authority.

## Editable Workspace policy

The highest authority may adjust these values without a code change:

- OTP resend cooldown: 30–600 seconds;
- maximum OTP verification attempts: 3–10;
- maximum sent OTP messages per Email + purpose per hour: 1–20;
- Workspace Email daily limit: 1 through the current system Email Daily Ceiling.

The OTP validity period is intentionally not editable in the initial contract and remains 10 minutes.

## System ceiling

`EMAIL_DAILY_BUDGET` is a runtime-only global safety ceiling for the shared Email provider account. It is not a Workspace preference and cannot be raised through the Workspace admin API.

This separation is intentional:

- Workspace policy controls how conservative one Workspace wants to be;
- the global ceiling protects the shared provider/free-tier account from aggregate overuse;
- a Workspace policy may be lower than the global ceiling, never higher.

## Admin API

Consumers call through a private Service Binding and pass the current Identity session:

```text
Authorization: Bearer <opaque Identity session token>
X-Identity-Application: <runtime application id>
```

Read current policy:

```text
GET /v1/admin/security-policy
```

Update current Workspace policy:

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

Successful responses include the effective Workspace policy and the read-only `systemEmailDailyCeiling`.

## Safety properties

- Server-side bounds are authoritative; a UI cannot bypass them.
- Only a currently valid highest-authority Identity session can update policy.
- Policy changes increment a revision and write an Identity Audit event.
- Existing OTP challenges retain the limits captured when they were issued; changing policy does not retroactively make an already-issued challenge weaker or stronger.
- Global and Workspace daily Email budgets are both enforced when an OTP belongs to an existing Workspace.
- First-Workspace bootstrap occurs before a Workspace policy exists, so it uses conservative built-in OTP defaults plus the global Email ceiling.

## CY Web management UI plan

CY Web may expose a Shared Identity management section for:

- Employees;
- extensible Identity Groups;
- Application entry access;
- highest-authority / Recovery Email operations;
- OTP security policy.

The UI is only a management client. CYCloud Identity remains the authority for account data, credentials, OTP, Recovery, sessions and security policy. This allows CYInvoice account-management UI/ownership to be removed later without moving Identity authority into CY Web.
