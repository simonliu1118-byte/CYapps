# CYCloud Identity — Extraction Architecture

> Status: current architecture/design baseline for the new shared Identity service. CYInvoice Cloud remains unchanged during this workstream.

## 1. Why this service exists

CYInvoice Cloud currently contains account functions that are no longer invoice-specific: Workspace, Employee, Credential, shared roles, Email OTP, Recovery and Web Auth. CY Web and CYAccountingWeb also need these capabilities.

CYCloud Identity extracts only the truly shared authority so all Cloud Apps can depend on one identity contract without turning CYInvoice into the permanent account server.

## 2. Boundary

### CYCloud Identity owns

- Workspace identity and status.
- Workspace Recovery Email.
- Workspace-scoped Employee identity.
- Shared role: `SUPER_ADMIN`, `ADMIN`, `EMPLOYEE`.
- Employee credential authority and credential version.
- Employee enabled/disabled state.
- Email verification.
- Application registration / Workspace application enablement / Employee application access.
- App-scoped browser sessions.
- Email OTP engine and purpose isolation.
- Employee password recovery.
- SUPER_ADMIN transfer.
- Identity security audit events.

### App owns

- CY Web app tags and module permissions.
- CYAccountingWeb accounting/business permissions.
- CYInvoice invoice/allowance/void permissions and Device lifecycle.
- Domain-specific business data and audit.

### Explicitly not extracted now

The following remain CYInvoice-specific until the CYInvoice workstream later decides how to consume Shared Identity:

- Device pairing and Device Token.
- Local EmployeeStore → Cloud Employee transition.
- Windows DPAPI/offline credential cache.
- CYInvoice onboarding UI and its local/cloud cutover workflow.

This keeps the first extraction small enough to validate with CY Web and CYAccountingWeb without destabilizing CYInvoice.

## 3. Workspace model

Employee identity remains Workspace-scoped, matching the accepted CYInvoice model.

```text
Workspace A
├─ 0001 SUPER_ADMIN
├─ 0002 ADMIN
└─ 0003 EMPLOYEE

Workspace B
├─ 0001 SUPER_ADMIN
└─ 0002 EMPLOYEE
```

The same Employee No or Email may exist in two different Workspaces. It must be unique inside one Workspace.

Every Workspace must have exactly one enabled `SUPER_ADMIN`. Database constraints prevent two simultaneous SUPER_ADMIN rows; service logic prevents removing the last SUPER_ADMIN.

A Workspace also has a non-secret `workspace_code` intended for future join/selection flows. Backend API operations use the stable `workspace_id` authority.

## 4. Web login and multiple Workspaces

A central Identity database can contain more than one Workspace, so Shared Identity must never guess the Workspace by selecting “the only active Workspace”.

Authentication always resolves these three dimensions:

```text
Application + Workspace + Employee
```

For the current internal CY Web / CYAccountingWeb deployments, the App Worker may receive its target Workspace ID through deployment configuration. The browser can therefore continue showing only:

```text
Employee No + Password
```

The App Worker adds its configured application/workspace context when it calls Identity through a private Service Binding.

Future multi-tenant UI may expose Workspace selection without changing credential authority.

## 5. Roles and Application Access

Shared role remains:

- `SUPER_ADMIN`
- `ADMIN`
- `EMPLOYEE`

Application Access is separate from role.

```text
Shared Identity role
  ↓
Can this person enter CYWEB / CYACCOUNTING / CYINVOICE?
  ↓
App-specific permission inside that App
```

`SUPER_ADMIN` must not be locked out of an enabled Workspace application because an application grant row was accidentally removed. Therefore:

- Workspace SUPER_ADMIN may enter any application enabled for that Workspace.
- ADMIN / EMPLOYEE require an active Employee Application Access grant.
- Each App still performs its own detailed module/business authorization after Identity succeeds.

## 6. Credential model

Credentials are stored separately from Employee profile rows.

```text
employee_credentials
- employee_id
- algorithm
- verifier
- credential_version
- updated_at
```

Initial compatibility algorithm remains `pbkdf2-sha256` because CYInvoice already uses this verifier shape and the later CYInvoice cutover must not require plaintext password migration.

Rules:

- Password plaintext exists only during the request that sets/verifies it.
- Plaintext never enters database, log, Audit, Git or backup metadata.
- `credential_version` increments after password change/reset.
- Browser sessions bind the credential version used when the session was created; version mismatch invalidates the session.
- Algorithm/version fields make a later reviewed credential upgrade possible without schema replacement.

## 7. Browser session model

CYCloud Identity owns app-scoped session authority.

Login flow:

```text
Browser
  ↓ employeeNo + password
App Worker
  ↓ private Service Binding
CYCloud Identity
  ↓ verify Workspace + Employee + Credential + Application Access
Identity session row created
  ↓ opaque random token returned only to App Worker
App Worker
  ↓ HttpOnly / Secure / SameSite cookie
Browser
```

The raw session token is never stored in D1. D1 stores only its SHA-256 hash.

Session resolution checks:

- session exists and not expired/revoked;
- Workspace active;
- Employee enabled;
- credential version still matches;
- Employee role is current;
- application still enabled;
- Employee still has application access, unless current role is SUPER_ADMIN.

No sliding heartbeat write is performed on normal requests. Session resolution is read-only. This minimizes D1 writes and keeps revocation authoritative.

Initial browser session TTL target is 8 hours; this remains runtime-configurable within reviewed bounds.

## 8. Compatibility endpoint

To make CY Web and CYAccountingWeb cutover low-risk, the first Shared Identity implementation keeps a provider-neutral compatibility login contract equivalent in purpose to the current temporary Web Auth bridge.

Conceptual request:

```json
{
  "application": "CYWEB",
  "workspaceId": "<deployment-injected>",
  "employeeNo": "0001",
  "password": "..."
}
```

Conceptual successful principal:

```json
{
  "employeeId": "...",
  "employeeNo": "0001",
  "name": "...",
  "role": "SUPER_ADMIN",
  "workspaceId": "...",
  "credentialVersion": 1,
  "revision": 1
}
```

No password verifier or recovery material is returned.

CY Web may first switch its existing provider adapter to this endpoint, then migrate from its temporary local `web_sessions` projection to Identity-owned sessions at the same adapter boundary. Business modules do not change.

## 9. Email OTP engine

The extracted OTP engine preserves the accepted security semantics:

- 6 digit random code.
- HMAC-SHA256 digest at rest using a secret pepper.
- 10 minute expiry target.
- 60 second resend cooldown target.
- maximum attempts.
- per-purpose / per-scope / per-email request limits.
- one-time consumption.
- provider delivery state (`pending`, `sent`, `failed`).
- no OTP value in logs or audit.

Initial purpose set keeps shared account use cases:

- `workspace_bootstrap`
- `workspace_recovery`
- `employee_email_verification`
- `employee_password_reset`
- `super_admin_transfer_authorization`
- `recovery_email_change`

CYInvoice-specific Device pairing purpose is not required by CY Web / CYAccountingWeb Phase 1. It may be added later through a forward migration if the CYInvoice migration chooses Shared Identity to authorize Device recovery/pairing.

## 10. Email provider boundary

Identity owns OTP lifecycle. Email provider only delivers the prepared message.

```text
Identity OTP service
  ↓ EmailSender interface
Brevo adapter (current)
Resend adapter (optional/future)
```

Runtime configuration remains secret-injected:

- `EMAIL_PROVIDER`
- `BREVO_API_KEY` or another provider secret
- `EMAIL_FROM`
- `OTP_PEPPER`

Provider errors must not copy recipient/provider diagnostic bodies into public logs.

### Free-tier protection

The current deployment is designed for free service tiers.

- Email send budget is runtime-configurable and intentionally set below the provider daily free allowance.
- Resend cooldown and hourly limits prevent accidental repeated sends.
- No background email retry loop that can unexpectedly consume the daily quota.
- A provider quota failure returns a controlled error rather than automatically upgrading or switching to a paid path.

## 11. Workspace bootstrap

Because the current account store has no production data requiring migration, the first Workspace is created cleanly in CYCloud Identity.

Bootstrap is not open public registration.

Proposed flow:

```text
One-time deployment bootstrap secret
  ↓
Workspace display name + workspace code + first SUPER_ADMIN profile
  ↓
Email OTP to first SUPER_ADMIN Email
  ↓ OTP confirm
Create Workspace
Create first SUPER_ADMIN
Create credential
Set Workspace Recovery Email = verified SUPER_ADMIN Email
Enable approved initial applications
Commit atomically
```

The bootstrap secret is a Cloudflare Worker Secret and never appears in Public source or browser storage. After successful bootstrap it can be rotated/removed from runtime.

## 12. Employee lifecycle

### Create Employee

- ADMIN or SUPER_ADMIN re-authenticates.
- New Employee No / Email must be unique inside Workspace.
- Employee Email OTP is completed.
- Employee profile + credential are created.
- Role may be ADMIN or EMPLOYEE; normal creation cannot create a second SUPER_ADMIN.
- Application grants are assigned separately.

### Edit Employee

- Name change: manager re-authentication.
- Email change: manager re-authentication + OTP to new Email.
- ADMIN ↔ EMPLOYEE: manager re-authentication; cannot alter SUPER_ADMIN through general role update.
- enabled state: manager re-authentication; SUPER_ADMIN cannot be disabled.

### Password

- Employee may change own password after current-password verification.
- Manager may reset another non-SUPER_ADMIN account according to the management API contract.
- Forgot-password flow sends OTP only to that Employee's already-verified Email.
- Password reset increments credential version and revokes/invalidates existing sessions.

## 13. SUPER_ADMIN transfer

Target must already be:

```text
enabled ADMIN
+ verified Email
+ ready credential
```

Flow:

```text
Current SUPER_ADMIN re-authenticates
  ↓
OTP sent to current SUPER_ADMIN verified Email
  ↓
OTP succeeds
  ↓
backend re-checks actor and target current state
  ↓ atomic mutation
Current SUPER_ADMIN → ADMIN
Target ADMIN → SUPER_ADMIN
Workspace Recovery Email → target verified Email
Current sessions reviewed/revoked as required
Audit event written
```

The service must never leave a Workspace with zero or two SUPER_ADMIN accounts.

## 14. Recovery distinction

Two recovery concepts remain separate:

### Employee password recovery

"I forgot my password."

Uses that Employee's verified Email OTP and changes only that Employee credential.

### Workspace Recovery Email

"The Workspace needs an emergency trusted contact."

It follows the current SUPER_ADMIN verified Email and changes during SUPER_ADMIN transfer/email change.

CYInvoice's future all-Device-Token-loss recovery is not implemented by this Phase 1 because it is a Device lifecycle problem. The Recovery Email data is preserved now so the CYInvoice workstream can later build that recovery flow without changing shared account semantics.

## 15. Initial physical schema

Initial migration contains:

- `workspaces`
- `employees`
- `employee_credentials`
- `applications`
- `workspace_applications`
- `employee_application_access`
- `identity_sessions`
- `email_otp_challenges`
- `identity_audit_events`

Critical query paths have explicit indexes. Schema evolution after acceptance uses numbered forward migrations.

## 16. Free-resource design

The runtime is intentionally low-write and index-driven:

- one shared Identity D1 rather than one database per Workspace;
- no session heartbeat writes;
- no high-frequency polling;
- no high-frequency Cron;
- indexed session / employee / application / OTP queries;
- lazy/low-frequency expired-row cleanup;
- configurable email daily budget below provider free allowance;
- backup is low-frequency and retention-bounded.

Current free-tier limits are operational constraints, not hard-coded business semantics. If provider limits change, deployment configuration/operations are reviewed before any paid upgrade.

## 17. Backup boundary

Identity data is small but security-critical. Before production cutover, backup/restore acceptance is required.

Planned behavior:

- low-frequency logical snapshot/export;
- encrypted before cross-cloud storage when credential verifier data is included;
- Google Cloud backup target only within the approved free usage envelope;
- bounded retention;
- restore into a separate validation database first;
- no OTP pepper, provider API key, bootstrap secret or backup encryption key inside the backup object.

Backup implementation is intentionally after core Identity acceptance so it does not block CY Web / CYAccountingWeb development cutover.

## 18. Rollout sequence

```text
A. Build CYCloud Identity source + local D1 tests
B. Deploy non-production Identity Worker/D1
C. Bootstrap test Workspace + SUPER_ADMIN
D. CY Web development binding → CYCloud Identity
E. Accept login / session / logout / disabled / password recovery
F. CYAccountingWeb development → CYCloud Identity
G. Accept both Apps together
H. Add backup/restore acceptance
I. Only then hand stable contract to CYInvoice workstream
J. CYInvoice performs its own Device/desktop-safe migration later
```

At no point in A–H is the CYInvoice Cloud runtime modified.

## 19. Acceptance gates before CY Web / ACC Web production use

Required:

- Workspace isolation.
- exactly-one-SUPER_ADMIN invariant.
- Recovery Email follows current SUPER_ADMIN.
- Employee No / Email uniqueness within Workspace.
- credential verification and credential-version invalidation.
- EMPLOYEE / ADMIN / SUPER_ADMIN login according to Application Access.
- disabled Employee cannot authenticate/resolve session.
- revoked/expired session rejected.
- logout revokes the session.
- OTP expiry / cooldown / attempts / single-use / purpose isolation.
- Email delivery failure does not consume a successful OTP flow.
- password recovery invalidates old credential sessions.
- SUPER_ADMIN transfer atomicity.
- provider unavailable / quota errors fail closed.
- D1 local and non-production runtime acceptance.
- no production secrets or identifiers in Public Git/logs/artifacts.
- resource usage remains inside the currently approved free-tier operating envelope.
