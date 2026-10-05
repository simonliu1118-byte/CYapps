# CYCloud Identity — OTP Security

> Status: shared OTP/security-policy contract. Actual Workspace values are runtime data and are not committed to Public Git.

## Ownership boundary

CYID owns OTP controls and enforcement. A consumer such as CY Web may host management UI but must call CYID APIs and must not copy security policy into its business database.

Only the current Super Admin may change Workspace OTP/security-core controls. Identity Admin, normal ADMIN, USER and legacy Group membership do not confer this authority.

## Established defaults

- OTP resend cooldown: 60 seconds
- maximum OTP verification attempts: 5
- maximum sent OTP messages per Email + purpose per hour: 5
- Workspace Email daily limit: 100, capped by the lower system-wide Email ceiling
- OTP validity: 10 minutes

OTP validity is not editable in the initial contract.

## Editable Workspace controls

Super Admin may adjust:

- resend cooldown: 30–600 seconds;
- maximum attempts: 3–10;
- maximum sent messages per Email + purpose per hour: 1–20;
- Workspace Email daily limit: 1 through the current system Email Daily Ceiling.

## System ceiling

`EMAIL_DAILY_BUDGET` is a runtime-only global safety ceiling for the shared provider account. It cannot be raised from Workspace UI.

## Admin API

Consumers call through private Service Binding using the current Identity session and application context.

- `GET /v1/admin/security-policy`
- `PUT /v1/admin/security-policy`

Server-side bounds and current Super Admin authority are always revalidated.

## Safety properties

- OTP is purpose-scoped, one-time, expiry-bound and attempt/rate limited.
- Existing challenges keep the policy captured when issued.
- Global and Workspace daily budgets are both enforced when applicable.
- Bootstrap before Workspace policy exists uses established defaults plus the global ceiling.
- Secrets/OTP values never enter logs, Audit or Public source.

## Relationship to first Email verification

CYID 0.3 new-Employee first verification uses a one-time first-login password rather than an Email OTP. This does **not** rename the product flow: it remains Email verification.

OTP remains used for Workspace bootstrap, password recovery, activated-account Email re-verification/recovery, Super Admin transfer and other explicitly reviewed purposes.

First-login temporary-password expiry/rate/reissue semantics belong to `AUTH_CONTRACT.md`, not this OTP document.

## UI visibility

CY Web exposes Workspace OTP/security-policy settings only to resolved Super Admin. Other roles receive no fake disabled controls or navigational entry; CYID API authorization still protects manually crafted requests.