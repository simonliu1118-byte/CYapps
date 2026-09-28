# CYCloud Identity — Extraction Architecture

> Status: current architecture/design baseline for the new shared Identity service. CYInvoice Cloud remains unchanged during this workstream.

## 1. Purpose and ownership

CYCloud Identity extracts the account capabilities that are shared by Cloud Apps: Workspace, Employee, Credential, Identity Groups, Application Access, Session, Email OTP, Recovery and Identity security audit.

CYCloud Identity owns:

- Workspace identity/status and Workspace Recovery Email;
- Workspace-scoped Employee identity;
- one protected Workspace highest-authority employee (`SUPER_ADMIN` authority semantics);
- editable/data-driven Identity Groups;
- Employee credential authority/version and enabled state;
- Email verification;
- generic Application registration, Workspace application enablement, direct Employee application access and optional group application access;
- app-scoped browser sessions;
- Email OTP engine and purpose isolation;
- Employee password recovery;
- highest-authority transfer;
- Identity security audit events.

Each App keeps its own business data and detailed module/business permissions.

CYInvoice-specific Device pairing, Device Token, Local EmployeeStore → Cloud transition and Windows offline credential cache are explicitly not extracted in Phase 1. They remain in the CYInvoice workstream until a later controlled cutover.

## 2. Public repository boundary

`CYapps` is Public. This project therefore follows a minimum-disclosure model.

Public source may contain:

- generic schema and API contracts;
- placeholder binding names;
- source code and deterministic synthetic test fixtures;
- provider-neutral adapters and deployment logic.

Public source must not contain:

- production Cloudflare resource IDs or concrete provider targets;
- production Workspace IDs/codes, Employee data, Email addresses or access assignments;
- real application enablement/access matrices;
- API keys, OTP pepper, credential verifier samples derived from real passwords, bootstrap secrets or backup keys;
- production-resolved deployment configuration.

Product names can be public elsewhere in the repository, but the Identity foundation does not need to publish the company's current application catalog or who can enter which system. The `applications` table is a generic runtime registry and starts empty in the public migration.

## 3. Workspace and Employee model

Employee identity is Workspace-scoped.

```text
Workspace
├─ highest-authority employee pointer
├─ Employee 0001
├─ Employee 0002
└─ Employee 0003
```

The same Employee No or Email may exist in different Workspaces. Inside one Workspace, Employee No and Email are unique.

A new Workspace starts in `bootstrap` state. Before it becomes `active`, it must point to one enabled Employee as its highest-authority employee. Because the Workspace owns a single employee pointer, two simultaneous highest-authority employees cannot exist in the relational model.

The current highest-authority Employee cannot be disabled or deleted until authority is transferred to another enabled Employee in the same Workspace.

A Workspace also has a non-secret `workspace_code` for future selection/join UX. Backend authority uses immutable `workspace_id`.

## 4. Highest authority vs editable Identity Groups

Workspace highest authority and ordinary Identity Groups are separate concepts.

The highest-authority pointer exists for security-sensitive Workspace operations such as:

- Workspace Recovery Email ownership;
- final Workspace administration;
- highest-authority transfer;
- preventing accidental lockout from enabled applications.

Ordinary Identity Groups are data-driven records:

```text
Identity Group
├─ stable group_id
├─ editable group_key
├─ editable display_name
├─ optional description
└─ active / disabled
```

Employees join groups through a many-to-many membership table. A Workspace can create new groups, rename them, disable them or adjust memberships without a schema migration.

The database intentionally does **not** use `CHECK role IN (...)` or a fixed role enum for ordinary groups.

Group IDs, not display names, are the referential identity. Renaming a group therefore does not break memberships or access grants.

Identity Groups are still coarse shared identity metadata. Fine-grained module/business permissions remain inside each App.

## 5. Multi-Workspace login

Shared Identity must never assume there is only one active Workspace.

Authentication always resolves:

```text
Application + Workspace + Employee
```

An App Worker may receive its target Workspace through protected deployment/runtime configuration, allowing a simple browser login form when appropriate. If one person later manages multiple Workspaces from one App entry point, Workspace selection can be added without replacing the credential authority model.

## 6. Generic Application registry and access

Application registration is data, not schema.

The public migration creates these generic structures only:

- `applications`
- `workspace_applications`
- `employee_application_access`
- `identity_group_application_access`

It does **not** seed current company application IDs.

At runtime, a controlled admin/bootstrap flow registers the applications appropriate to that environment. A Workspace then explicitly enables an application.

Effective coarse application entry can be granted by:

- the protected Workspace highest-authority rule for an application enabled in that Workspace;
- a direct Employee application grant;
- membership in an active Identity Group with an application grant.

Detailed permissions remain inside the target App.

## 7. Credential model

Credentials are stored separately from Employee profile rows.

The first implementation can support the existing `pbkdf2-sha256` verifier shape for compatibility, but the schema does not permanently enumerate only one algorithm identifier. Runtime code decides which algorithms are currently supported and rejects unknown algorithms during credential operations.

Rules:

- password plaintext exists only during the request that sets/verifies it;
- plaintext never enters database, log, Audit, Git or backup metadata;
- `credential_version` increments after password change/reset;
- sessions bind the credential version used when created, so a credential-version change invalidates existing sessions;
- algorithm/version metadata permits a later reviewed credential upgrade without replacing the account model.

## 8. Browser session model

CYCloud Identity owns app-scoped session authority.

```text
Browser
  ↓ login data
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
- current highest-authority/direct/group access still permits the application.

Normal session resolution is read-only: no sliding heartbeat write.

Initial browser session TTL target is 8 hours and remains runtime-configurable within reviewed bounds.

## 9. Compatibility contract

The first implementation exposes an application-aware Web Auth compatibility contract so App consumers can switch provider without copying credential logic.

Conceptual request:

```json
{
  "application": "<runtime-registered application id>",
  "workspaceId": "<deployment/runtime supplied workspace id>",
  "employeeNo": "0001",
  "password": "..."
}
```

Successful responses contain only normalized principal metadata required by the caller. Credential verifiers, OTP and recovery material are never returned.

## 10. Email OTP engine

The OTP engine preserves the accepted account-security behavior:

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

## 11. Email provider boundary and free-tier protection

Identity owns the OTP lifecycle; an Email provider only sends a prepared message.

```text
Identity OTP service
  ↓ EmailSender interface
provider adapter
```

Secrets are deployment-injected. Runtime must fail closed when the configured provider/quota is unavailable and must not automatically switch to a paid path. Send budgets, resend cooldowns and the absence of uncontrolled background retry loops protect the approved free-tier operating envelope.

## 12. First Workspace bootstrap

Because there is no production account migration requirement, the new authority starts cleanly. Bootstrap is not public registration.

```text
protected one-time bootstrap authorization
  ↓
Workspace in bootstrap state
  ↓
first Employee + credential
  ↓
Email OTP verification
  ↓
set highest-authority employee pointer
set Recovery Email = verified highest-authority Email
set Workspace active
  ↓
register/enable environment applications through controlled runtime data
```

No real bootstrap secret or actual application catalog is committed to Public source.

## 13. Employee and Identity Group lifecycle

Create Employee:

- an authorized manager re-authenticates;
- Employee No / Email must be unique inside the Workspace;
- the new Employee completes Email OTP as required;
- profile + credential are created;
- ordinary Identity Group memberships and application grants are managed separately.

Identity Group management:

- create a new group without schema change;
- rename group key/display name without changing `group_id`;
- disable a group without deleting historical references;
- add/remove Employee membership;
- optionally grant application entry to a group;
- cross-Workspace membership/access references are rejected by foreign keys.

Highest authority cannot be changed through ordinary group membership operations.

Password:

- Employee can change own password after current-password verification;
- reviewed manager-reset and forgot-password flows use credential version invalidation;
- successful change/reset invalidates old sessions.

## 14. Highest-authority transfer

Target must already be an enabled Employee in the same Workspace with verified Email and a ready credential.

```text
Current highest authority re-authenticates
  ↓
OTP to current highest-authority verified Email
  ↓ confirm
Backend re-checks actor + target
  ↓ atomic transaction
Workspace highest-authority pointer → target
Workspace Recovery Email → target verified Email
session invalidation/review
Audit event
```

Ordinary Identity Group membership does not control this invariant.

## 15. Recovery distinction

Employee password recovery means “I forgot my password” and uses that Employee's verified Email.

Workspace Recovery Email means “this Workspace needs a final trusted recovery contact” and follows the current highest-authority Employee verified Email.

CYInvoice's future all-Device-Token-loss recovery remains a separate Device-lifecycle problem.

## 16. Initial physical schema

The initial migration contains:

- `workspaces`
- `employees`
- `identity_groups`
- `employee_identity_groups`
- `employee_credentials`
- `applications`
- `workspace_applications`
- `employee_application_access`
- `identity_group_application_access`
- `identity_sessions`
- `email_otp_challenges`
- `identity_audit_events`

Critical lookup paths have explicit indexes. The application registry starts empty. Synthetic tests register fake applications at test runtime only.

The initial migration is still pre-deployment foundation work; it may be corrected before its first remote application. After an accepted remote baseline exists, schema evolution uses numbered forward migrations rather than rewriting applied history.

## 17. Free-resource design

The runtime is intentionally low-write and index-driven:

- one shared Identity D1 rather than one database per Workspace;
- no session heartbeat writes;
- no high-frequency polling or Cron;
- indexed Employee/group/session/application/OTP lookups;
- lazy or low-frequency expired-row cleanup;
- bounded email sending;
- low-frequency, retention-bounded backup.

Provider free-tier limits are operational constraints, not hard-coded business semantics. If limits change or usage approaches them, measure/optimize first and ask the user before any paid upgrade.

## 18. Backup boundary

Before production cutover, Identity backup/restore acceptance is required. Planned behavior:

- low-frequency logical snapshot/export;
- encrypt before cross-cloud storage when credential verifiers are included;
- use only approved backup targets within the approved free usage envelope;
- bounded retention;
- restore into a separate validation database first;
- never include OTP pepper, provider API key, bootstrap secret or backup encryption key inside the backup object.

## 19. Rollout sequence

```text
A. Build CYCloud Identity source + local D1 tests
B. Deploy non-production Identity Worker/D1
C. Bootstrap test Workspace + highest-authority account
D. Connect first development App through private Identity binding
E. Accept login / session / logout / disabled / password recovery
F. Connect additional development Apps
G. Accept multiple Apps together
H. Add backup/restore acceptance
I. Hand stable Identity contract to the CYInvoice workstream
J. CYInvoice performs its own Device/desktop-safe migration later
```

At no point in A–H is the CYInvoice Cloud runtime modified.

## 20. Acceptance gates before production use

Required:

- Public migration contains no real application catalog or production operational data;
- Workspace isolation;
- one protected highest-authority Employee per active Workspace;
- Recovery Email follows current highest-authority Employee;
- Employee No / Email uniqueness within Workspace;
- arbitrary ordinary Identity Group creation/rename/disable without schema change;
- cross-Workspace group membership and application access rejected;
- credential verification and credential-version invalidation;
- direct/group/highest-authority application access semantics;
- disabled Employee cannot authenticate or resolve a session;
- expired/revoked session rejected and logout revokes session;
- OTP expiry / cooldown / attempts / single-use / purpose isolation;
- delivery failure does not become a successful OTP flow;
- password recovery invalidates old credential sessions;
- highest-authority transfer atomicity;
- provider/quota errors fail closed;
- local D1 plus non-production Worker acceptance;
- no production secrets or identifiers in Public Git/logs/artifacts;
- resource use remains inside the currently approved free-tier operating envelope.
