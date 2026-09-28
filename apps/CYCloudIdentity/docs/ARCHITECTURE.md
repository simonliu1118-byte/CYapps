# CYCloud Identity — Extraction Architecture

> Status: current architecture/design baseline for the new shared Identity service. CYInvoice Cloud remains unchanged during this workstream.

## 1. Purpose and ownership

CYInvoice Cloud currently contains account functions that are not invoice-specific: Workspace, Employee, Credential, shared roles, Email OTP, Recovery and Web Auth. CY Web and CYAccountingWeb need the same authority. CYCloud Identity extracts only those shared responsibilities.

CYCloud Identity owns:

- Workspace identity/status and Workspace Recovery Email;
- Workspace-scoped Employee identity;
- shared roles `SUPER_ADMIN`, `ADMIN`, `EMPLOYEE`;
- Employee credential authority/version and enabled state;
- Email verification;
- Application registration, Workspace application enablement and Employee application access;
- app-scoped browser sessions;
- Email OTP engine and purpose isolation;
- Employee password recovery;
- SUPER_ADMIN transfer;
- Identity security audit events.

Each App keeps its own business data and detailed module/business permissions.

CYInvoice-specific Device pairing, Device Token, Local EmployeeStore → Cloud transition and Windows offline credential cache are explicitly not extracted in Phase 1. They remain in the CYInvoice workstream until a later controlled cutover.

## 2. Workspace and Employee model

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

The same Employee No or Email may exist in different Workspaces. Inside one Workspace, Employee No and Email are unique.

Each Workspace must maintain exactly one enabled `SUPER_ADMIN`. The database prevents two simultaneous SUPER_ADMIN rows; service operations must prevent removal/demotion of the last one except through the reviewed transfer transaction.

A Workspace also has a non-secret `workspace_code` for future selection/join UX. Backend authority uses immutable `workspace_id`.

## 3. Multi-Workspace login

Shared Identity must never copy the temporary CYInvoice Web Auth assumption that there is only one active Workspace.

Authentication always resolves:

```text
Application + Workspace + Employee
```

For the first internal CY Web / CYAccountingWeb deployment, the App Worker may receive its target Workspace ID through deployment configuration, so the browser can still show only Employee No + Password.

If a person later manages multiple Workspaces from one App entry point, the UI can add Workspace selection without changing the credential authority model.

## 4. Shared role vs Application Access

Role and application entry are separate concepts.

```text
Shared Identity role
  ↓
Can this Employee enter CYWEB / CYACCOUNTINGWEB / CYINVOICE?
  ↓
App-specific module/business authorization
```

Rules:

- Workspace must explicitly enable an application.
- Current Workspace SUPER_ADMIN may enter every application enabled for that Workspace, preventing accidental lockout.
- ADMIN / EMPLOYEE require active Employee Application Access for that application.
- Detailed permissions remain inside each App.

## 5. Credential model

Credentials are stored separately from Employee profile rows.

Initial compatibility algorithm remains `pbkdf2-sha256` because CYInvoice already uses this verifier shape and a later CYInvoice cutover must not require plaintext-password migration.

Rules:

- password plaintext exists only during the request that sets/verifies it;
- plaintext never enters database, log, Audit, Git or backup metadata;
- `credential_version` increments after password change/reset;
- sessions bind the credential version used when created, so a credential-version change invalidates existing sessions;
- algorithm/version metadata permits a later reviewed credential upgrade without replacing the account model.

## 6. Browser session model

CYCloud Identity owns app-scoped session authority.

```text
Browser
  ↓ employeeNo + password
App Worker
  ↓ private Service Binding
CYCloud Identity
  ↓ verify Workspace + Employee + Credential + Application Access
Identity session created
  ↓ opaque random token returned only to App Worker
App Worker
  ↓ HttpOnly / Secure / SameSite cookie
Browser
```

D1 stores only a SHA-256 hash of the raw session token.

Session resolution checks current authority every time:

- session exists, is not expired and not revoked;
- Workspace is active;
- Employee is enabled;
- credential version still matches;
- application is active and enabled for the Workspace;
- current role/access still permits that application.

Normal session resolution is read-only: no sliding heartbeat write. This reduces D1 writes and avoids stale role/access authority.

Initial browser session TTL target is 8 hours and remains runtime-configurable within reviewed bounds.

## 7. Compatibility path for CY Web / CYAccountingWeb

The first implementation exposes an application-aware Web Auth compatibility contract so current consumers can switch provider with minimal code change.

Conceptual request:

```json
{
  "application": "CYWEB",
  "workspaceId": "<deployment-injected>",
  "employeeNo": "0001",
  "password": "..."
}
```

Successful response contains only normalized principal metadata such as Employee ID/No, name, role, Workspace ID, credential version and revision. Password verifier, OTP and recovery material are never returned.

CY Web can first replace its temporary CYInvoice provider at the existing provider-neutral adapter boundary. Business modules do not need to know which Identity service is behind it.

## 8. Email OTP engine

The extracted OTP engine preserves the accepted account-security behavior:

- 6-digit random code;
- HMAC-SHA256 digest at rest using a secret pepper;
- 10-minute expiry target;
- 60-second resend cooldown target;
- maximum attempts;
- per-purpose / per-scope / per-email limits;
- one-time consumption;
- delivery state `pending` / `sent` / `failed`;
- no OTP value in logs or Audit.

Initial shared purposes:

- `workspace_bootstrap`
- `workspace_recovery`
- `employee_email_verification`
- `employee_password_reset`
- `super_admin_transfer_authorization`
- `recovery_email_change`

CYInvoice Device-pairing OTP remains deferred from Phase 1 because it belongs to the Device lifecycle.

## 9. Email provider boundary and free-tier protection

Identity owns the OTP lifecycle; the provider only sends a prepared email.

```text
Identity OTP service
  ↓ EmailSender interface
Brevo adapter (current)
Resend adapter (optional/future)
```

Secrets are deployment-injected: `EMAIL_PROVIDER`, provider API key, `EMAIL_FROM`, `OTP_PEPPER`.

The runtime must fail closed when a provider/quota is unavailable and must not automatically switch to a paid path. Email send budget is configurable below the currently approved free allowance; resend cooldown/hourly limits and the absence of an uncontrolled background retry loop prevent accidental quota burn.

## 10. First Workspace bootstrap

Because there is no production account data requiring migration, the new authority starts cleanly. Bootstrap is not public registration.

```text
one-time deployment bootstrap secret
  ↓
Workspace + first SUPER_ADMIN profile
  ↓
Email OTP to first SUPER_ADMIN Email
  ↓ confirm
Create Workspace
Create SUPER_ADMIN + credential
Recovery Email = verified SUPER_ADMIN Email
Enable approved initial applications
Commit atomically
```

The bootstrap secret exists only in protected runtime configuration and is rotated/removed after successful bootstrap.

## 11. Employee lifecycle

Create Employee:

- ADMIN or SUPER_ADMIN re-authenticates;
- Employee No / Email must be unique inside the Workspace;
- the new Employee completes Email OTP;
- profile + credential are created;
- normal creation may create ADMIN or EMPLOYEE, never a second SUPER_ADMIN;
- application grants are managed separately.

Edit Employee:

- name change requires manager re-authentication;
- email change requires manager re-authentication + OTP to the new Email;
- ADMIN ↔ EMPLOYEE requires manager re-authentication;
- SUPER_ADMIN cannot be changed through general role update;
- SUPER_ADMIN cannot be disabled.

Password:

- Employee can change own password after current-password verification;
- manager reset of another non-SUPER_ADMIN is handled by the reviewed management contract;
- forgot-password sends OTP only to that Employee's already-verified Email;
- successful change/reset increments credential version and invalidates old sessions.

## 12. SUPER_ADMIN transfer

Target must already be an enabled ADMIN with verified Email and a ready credential.

```text
Current SUPER_ADMIN re-authenticates
  ↓
OTP to current SUPER_ADMIN verified Email
  ↓ confirm
Backend re-checks actor + target
  ↓ atomic transaction
Current SUPER_ADMIN → ADMIN
Target ADMIN → SUPER_ADMIN
Workspace Recovery Email → target verified Email
Session invalidation/review
Audit event
```

The transaction must never leave a Workspace with zero or two SUPER_ADMIN accounts.

## 13. Recovery distinction

Employee password recovery means “I forgot my password” and uses that Employee's verified Email.

Workspace Recovery Email means “this Workspace needs a final trusted recovery contact” and follows the current SUPER_ADMIN verified Email.

CYInvoice's future all-Device-Token-loss recovery remains a separate Device-lifecycle problem. The shared Recovery Email data is preserved now so CYInvoice can later use the same trusted contact without redefining account authority.

## 14. Initial physical schema

The initial migration contains:

- `workspaces`
- `employees`
- `employee_credentials`
- `applications`
- `workspace_applications`
- `employee_application_access`
- `identity_sessions`
- `email_otp_challenges`
- `identity_audit_events`

Critical lookup paths have explicit indexes. Schema evolution after acceptance uses numbered forward migrations.

## 15. Free-resource design

The runtime is intentionally low-write and index-driven:

- one shared Identity D1 rather than one database per Workspace;
- no session heartbeat writes;
- no high-frequency polling or Cron;
- indexed Employee/session/application/OTP lookups;
- lazy or low-frequency expired-row cleanup;
- bounded email sending;
- low-frequency, retention-bounded backup.

Provider free-tier limits are operational constraints, not hard-coded business semantics. If limits change or usage approaches them, measure/optimize first and ask the user before any paid upgrade.

## 16. Backup boundary

Before production cutover, Identity backup/restore acceptance is required. Planned behavior:

- low-frequency logical snapshot/export;
- encrypt before cross-cloud storage when credential verifiers are included;
- use the approved Google Cloud backup target only within the free usage envelope;
- bounded retention;
- restore into a separate validation database first;
- never include OTP pepper, provider API key, bootstrap secret or backup encryption key inside the backup object.

Backup work follows core Identity acceptance and does not block the first non-production CY Web / CYAccountingWeb provider switch.

## 17. Rollout sequence

```text
A. Build CYCloud Identity source + local D1 tests
B. Deploy non-production Identity Worker/D1
C. Bootstrap test Workspace + SUPER_ADMIN
D. CY Web development binding → CYCloud Identity
E. Accept login / session / logout / disabled / password recovery
F. CYAccountingWeb development → CYCloud Identity
G. Accept both Apps together
H. Add backup/restore acceptance
I. Hand stable contract to CYInvoice workstream
J. CYInvoice performs its own Device/desktop-safe migration later
```

At no point in A–H is the CYInvoice Cloud runtime modified.

## 18. Acceptance gates before production use

Required:

- Workspace isolation;
- exactly-one-SUPER_ADMIN invariant;
- Recovery Email follows current SUPER_ADMIN;
- Employee No / Email uniqueness within Workspace;
- credential verification and credential-version invalidation;
- EMPLOYEE / ADMIN / SUPER_ADMIN login according to Application Access;
- disabled Employee cannot authenticate or resolve a session;
- expired/revoked session rejected and logout revokes session;
- OTP expiry / cooldown / attempts / single-use / purpose isolation;
- delivery failure does not become a successful OTP flow;
- password recovery invalidates old credential sessions;
- SUPER_ADMIN transfer atomicity;
- provider/quota errors fail closed;
- local D1 plus non-production Worker acceptance;
- no production secrets or identifiers in Public Git/logs/artifacts;
- resource use remains inside the currently approved free-tier operating envelope.
