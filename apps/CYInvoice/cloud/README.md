# CYInvoice Cloud Reference Backend

Cloudflare Worker + D1 reference implementation for CYInvoice V3 coordination and central Employee identity.

AMEGO remains the authoritative source for invoice / void / allowance business state. AMEGO App Keys remain local to Windows and are not part of this backend.

The identity contract is defined in `../docs/CLOUD_IDENTITY_LIFECYCLE.md`; engineering status is in `../docs/CLOUD_ARCHITECTURE_STATUS.md`. Current Windows baseline is V2.6.11 Build 2 (PR #216, not merged); source CI evidence and package are in [the current handoff](../docs/CLOUD_WORK_HANDOFF.md), and remaining acceptance/implementation is tracked only in [TODO](../docs/TODO.md).

## Current compatibility

- Service: `cyinvoice-cloud`
- Cloud implementation: `0.8.8`
- API: `1`
- Legacy API compatibility marker: `schemaVersion=8`
- Actual storage schema: `storageSchemaVersion=11`
- Migrations: `0001` through `0011`
- Worker entrypoint: `src/app.ts`

`wrangler.jsonc` advertises the client compatibility schema. Applied migrations are immutable; future changes must use new forward migrations.

> GitHub Actions validates the Worker bundle and migrations with local SQLite. It does not prove current remote deployment health. The last recorded development deployment is 2026-09-29 staged Run #7 at Cloud 0.8.8 / API 1 / storage Schema 11; this documentation update did not query or deploy the live service.

## Development commands

```bash
npm install
npm run check
npm test
npm run deploy:dry-run
npm run db:migrate:local
```

Remote actions must be deliberate:

```bash
npm run db:migrate:remote
npm run deploy
```

or, only when explicitly intended:

```bash
npm run deploy:with-migrations
```

Do not infer remote deployment state from source or CI alone.

## Runtime secrets

Secrets must be configured as Worker runtime secrets and never committed:

- `BOOTSTRAP_KEY`
- `OTP_PEPPER`
- `BREVO_API_KEY` or another provider credential
- sender identity / `EMAIL_FROM`

Do not commit Cloudflare API tokens, account keys, passwords, Device Tokens, OTP values, recovery codes, AMEGO credentials, real Employee Email addresses, or production data.

## Identity model

### Device

- Workspace ID: Cloud-generated.
- Device ID: Cloud-generated.
- Device Token: Windows-generated before the request, protected locally before network submission.
- Cloud stores only Device Token hash.
- Pairing Code authorizes a Device to join a Workspace; it does not grant Employee role.
- New devices join by a 10-minute one-use pairing code or a 72-hour one-use emailed invitation code. The latter also requires the current verified SUPER_ADMIN credentials. Neither path asks the new device to enter a Workspace ID.
- The API URL is entered by the new device from the connected machine or invitation email. It is not embedded in the Windows client.
- Pairing and invitation issuance, verification, denial, revocation, and device joining write Cloud security audit events without storing code, password, OTP, or Device Token plaintext. The audit viewing UI is deferred.

### Employee

Before cutover, Local EmployeeStore remains the single authority. Device onboarding then runs a whole-device Employee Transition over all existing Local Employees.

Identity matching:

```text
Employee No absent + Email absent
  → new Cloud Employee after own Email verification

Employee No + Email both identify the same existing Employee
  → directly adopt that Cloud Employee

partial or divergent match
  → pending conflict for current Workspace SUPER_ADMIN
```

Name is display data, not matching authority.

After cutover:

```text
Cloud Employee = only account authority
Local data = synchronized cache + protected offline credential verifier
```

A network outage does not switch the machine back to the old Local Employee authority.

## Execution-time authorization

CYInvoice has no persistent application login. Sensitive Cloud operations include Employee No + password at execution time; the backend verifies the credential and current role for that request.

Central account mutations are Online-only:

- Employee create.
- name / Email update.
- `ADMIN ↔ USER`.
- enabled state.
- password change / reset.
- pending identity resolution.
- SUPER_ADMIN transfer.

New or changed Email is committed only after OTP verification where required.

Online desktop authentication refreshes current central authority before each protected operation. Only transport outage/timeout permits the last trusted protected cache; HTTP rejection, revoked Device, malformed data, Workspace mismatch and caller cancellation fail closed.

## Device lifecycle

Revoke preserves Device history and audit while invalidating the old Token. Rejoining creates a fresh identity. Built-in active Workspaces keep LAST_ACTIVE_DEVICE protection. The Windows Cloud-to-Local reset uses double confirmation, shutdown, revoke/self-status confirmation and local wipe; unknown results preserve local state for startup recovery. Windows does not disable/delete/purge Workspaces.

USER is the current role vocabulary. Forward migration 0010 converts legacy EMPLOYEE storage; 0011 adds revoked-device lifecycle state. No active role alias is retained.

## SUPER_ADMIN

Each Workspace has exactly one enabled `SUPER_ADMIN`.

Transfer is a dedicated high-privilege operation:

```text
current X password re-auth
  → OTP to X verified Email
  → backend rechecks X and target Y
  → atomic X=ADMIN, Y=SUPER_ADMIN
  → Workspace Recovery Email moves to Y verified Email
```

Normal role update cannot create or remove `SUPER_ADMIN`.

## Main endpoint groups

Foundation / Device:

- `GET /v1/health`
- `GET /v1/onboarding/status`
- `POST /v1/onboarding/bootstrap-email`
- `POST /v1/bootstrap`
- `GET /v1/device`
- `GET /v1/devices` (inventory)
- `POST /v1/devices/revoke` (execution-time SUPER_ADMIN)
- `GET /v1/devices/self-status` (narrow terminal-state recovery)
- Device pairing authorization / create / claim routes

Employee Transition:

- whole-device transition inspection / actions
- Employee Email verification
- pending conflict list / resolution
- authority snapshot / cutover

Central account management:

- Employee create challenge / confirm
- Employee update challenge / confirm
- enabled update
- password update
- SUPER_ADMIN transfer challenge / confirm

The legacy single-account mutation route:

```text
POST /v1/employees/reconcile-local
```

is retired and fails closed with `LEGACY_EMPLOYEE_RECONCILIATION_RETIRED`. The arbitrary old Employee import window is no longer part of the reference backend.

`GET /v1/employees` remains read-only compatibility only; current Windows Cloud authority/cache flow uses the Employee Authority snapshot contract.

## Email / OTP

The backend uses a provider-neutral Email adapter. Brevo is the current development reference provider; a Resend adapter is retained for later use.

OTP requirements include:

- Web Crypto random 6-digit code.
- HMAC-SHA256 digest at rest.
- 10-minute expiry.
- resend cooldown.
- attempt limit.
- rate limit.
- one-time consumption.
- scope binding for the intended operation.

No live Email-delivery claim should be made until the development deployment has the required runtime secrets and is tested end-to-end.

## Public repository boundary

This directory is public source. Keep implementation provider-neutral at the Windows boundary and do not place runtime secrets or operational customer data in source, PR text, logs, or engineering artifacts.
