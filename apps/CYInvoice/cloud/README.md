# CYInvoice Cloud Reference Backend

Cloudflare Worker + D1 reference implementation for CYInvoice V3 coordination and central Employee identity.

AMEGO remains the authoritative source for invoice / void / allowance business state. AMEGO App Keys remain local to Windows and are not part of this backend.

The identity contract is defined in `../docs/CLOUD_IDENTITY_LIFECYCLE.md`; engineering status is in `../docs/CLOUD_ARCHITECTURE_STATUS.md`. Current source baseline is V2.6.15 (CYID Consumer candidate, not deployed); V2.6.14 predecessor PR #216 remains open; source CI evidence and package are in [the current handoff](../docs/CLOUD_WORK_HANDOFF.md), and remaining acceptance/implementation is tracked only in [TODO](../docs/TODO.md).

## Current compatibility

- Service: `cyinvoice-cloud`
- Cloud implementation: `0.9.0`
- API: `1`
- Legacy API compatibility marker: `schemaVersion=8`
- Actual storage schema: `storageSchemaVersion=13`
- Migrations: `0001` through `0013`
- Worker entrypoint: `src/app.ts`

`wrangler.jsonc` advertises the client compatibility schema. Applied migrations are immutable; future changes must use new forward migrations.

> Source CI passed on db1a3b95 (Cloud #377 / Windows #266). Development staged [Run #8 attempt 2](https://github.com/simonliu1118-byte/CYapps/actions/runs/37983363509) verified Cloud 0.8.9 / API 1 / compatibility marker 8 / storage Schema 12 and both new capabilities. Migration 0012 was applied once, with stable aggregate Device state and an empty foreign-key check; retry found no unapplied migrations. The first immediate health read still returned 0.8.8; identical-source retry passed. This does not replace Windows A/B/C manual acceptance.

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


## Device metadata (Windows V2.6.14 / Cloud 0.8.9)

- `POST /v1/devices/usage` accepts `{clientVersion}` with an active Device Token. It updates only that Device's version and server UTC `last_seen_at` on startup. Client timestamps/target IDs cannot redirect the update. Offline/older clients retain their last confirmed values; this is not a live presence heartbeat.
- `POST /v1/devices/rename` accepts `{targetDeviceId, displayName, employeeNo, password}`. Current central SUPER_ADMIN credentials and an active same-Workspace target are required. The transaction rechecks actor, Workspace and credential freshness and records `device_renamed` atomically.
- Migration `0012_device_rename_audit.sql` retains all prior audit rows and adds the rename vocabulary. Metadata columns already exist. API compatibility marker stays 8; storage schema advances to 12.
- Desktop management filters active Devices and hides status/revocation columns. Cloud inventory and DB retain revoked history. Existing last-active protection and self-reset remain intact.
- Local `npm test` executes real lifecycle handlers/SQL, including auth/race rejection and audit-failure rollback. Remote deployment evidence is recorded separately in the handoff/PR.

## CYID Consumer candidate (disabled by default)

Canonical identity semantics come directly from `../../CYCloudIdentity/docs/CONSUMER_INTEGRATION_STANDARD.md`; this consumer declares `../CYID_CONSUMER_VERSION=1.0.2`. No mirror or CYID D1 binding is added.

An independently reviewed deployment injects `CYID_ENABLED=true`, private `IDENTITY` Service Binding, and runtime-only `IDENTITY_APPLICATION_ID`, `IDENTITY_WORKSPACE_ID`, `IDENTITY_CYINVOICE_WORKSPACE_ID`. The two Workspace IDs are distinct scopes; this Worker binds exactly one CYInvoice Workspace. Missing/mismatched configuration fails closed. No real deployment binding or CYID application enablement was verified in this source task. With the flag absent/false, Built-in remains available.

`IDENTITY_CYINVOICE_WORKSPACE_ID` is the existing business Workspace ID, not a new Workspace. Cutover retains the current consumer D1, Workspace, Devices and Tokens; existing installations must not rejoin or reset. Upgrade all active Windows installations to CYID-capable clients (this batch: V2.6.15), prepare Employee activation/App Access/Role/verified Super Admin Email, and prove continuity in isolated staging before enabling the flag. Until those gates pass, retain the Built-in deployment. Windows offline authentication is not offline invoice issuance: AMEGO still requires connectivity; a reachable Worker returning private CYID outage HTTP 503 does not activate the cache.

`GET /v1/identity-provider` requires an active Device Token and returns the confirmed provider/binding. `POST /v1/cyid/authenticate` accepts only per-operation employeeNo/password plus Device Token. The gateway owns Login → Resolve → result → finally Logout, returning normalized principal without Session/verifier. CYID owns Employee/enabled/Role/App Access; CYInvoice owns Device and business permission. Legacy account/bootstrap routes reject in CYID mode.

Device rename/revoke, pairing issuance and invitation issue/preview/claim/revoke reuse the existing handler/mutation owners with a request-local CYID context. Invitation requires current SUPER_ADMIN credentials; pairing claim uses the existing one-time ticket issued after SUPER_ADMIN + Email OTP and grants Device membership only. Verified owner Email is read transiently from the authorized CYID admin snapshot, never imported into consumer authority/cache. Migration 0013 retains old invitation history/FKs and distinguishes Built-in from external CYID actors.

Logout is best effort in finally. Response loss/crash can leave a CYID Session active until provider revocation/expiry (current default 8 hours); no consumer token is persisted and no completed mutation is retried. No distributed transaction is claimed between CYID Resolve and consumer D1 mutation. Windows uses a subordinate DPAPI cache only after transport failure, never after an HTTP authority error; offline server administration is unavailable. See CY_ID_INTEGRATION §14 for acceptance and cutover gates.
