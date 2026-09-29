# CYCloud Identity — Architecture

> **Status:** current technical architecture. Product permanence belongs to `../PROJECT_RULES.md`; current progress belongs only to `../TODO.md`。

## 1. Purpose and ownership

CYID is the shared Identity authority for CY Cloud applications.

CYID owns:

- Workspace identity/status and protected Super Admin authority pointer;
- Workspace-scoped Employee identity and durable lifecycle;
- permanent credentials plus temporary first-login credentials;
- Workspace Role `SUPER_ADMIN / ADMIN / USER` and ADMIN-only Identity Admin capability;
- generic Application registry and Employee Application Access;
- app-scoped Identity sessions and revocation;
- Email verification, OTP, password recovery, activated-account Email recovery and Super Admin transfer;
- Identity security audit events.

Consumer Apps own their business data and detailed module/business authorization. CY Web additionally owns CY Web-local Module Access.

## 2. Runtime topology

~~~text
Browser
  -> consumer App Worker
  -> private Cloudflare Service Binding
  -> CYCloud Identity Worker
  -> Identity D1
~~~

Browsers never receive provider secrets, credential verifiers, D1 identifiers or direct database access. Consumer Workers keep only the opaque Identity session token in reviewed server/browser transport such as an HttpOnly/Secure cookie.

## 3. Workspace and Employee identity

- Employee identity is Workspace-scoped.
- Employee No and Email are unique inside one Workspace but may repeat across different Workspaces.
- `workspace_id` and `employee_id` are immutable authority identifiers; human-readable codes are not authorization keys.
- Active Workspace has exactly one protected Super Admin pointer. Ordinary persisted Employee role is `ADMIN` or `USER`; effective role becomes `SUPER_ADMIN` for the pointed Employee.

## 4. Role, Identity Admin and Application Access

Role and Application Access are independent dimensions.

- `SUPER_ADMIN / ADMIN / USER` is the complete Workspace role set.
- Identity Admin is a capability on ADMIN, never a fourth role.
- CY Web core entry is always available to every valid Employee.
- Other App Access is direct Employee access and defaults to not granted.
- Super Admin automatically enters every Workspace-enabled App.
- Legacy Identity Group memberships and Group App grants are not forward authorization sources.

Current runtime may retain old tables/compatibility fields only long enough for safe migration/readback. New code must not extend Group-derived authority.

## 5. Credential model

Permanent credentials are stored separately from Employee profile rows. New permanent credentials use the reviewed `scrypt` verifier; legacy verifier support is compatibility-only.

CYID 0.3 introduces a separate **initial credential** for first Email verification:

- generated only for Email delivery;
- plaintext never stored;
- verifier stored separately from permanent credential;
- accepted only for the configured CY Web core account application;
- has an explicit expiry and is replaced by resend;
- cannot create a normal Identity session;
- successful verification yields only a short-lived first-login ticket used to set the permanent password.

After first-login completion the initial credential and ticket are unusable. The user must authenticate again with the new permanent password to create a normal session.

## 6. Session model

Normal login creates an opaque app-scoped session token. D1 stores only the token digest.

Resolve checks current authority on every request: session state/expiry, Workspace active state, Employee enabled state, credential version, Application enabled state, current App Access, Workspace Role and Identity Admin capability.

No sliding-write heartbeat is used. Disable, role/access changes, credential changes, forced Email recovery and Super Admin transfer revoke or invalidate affected sessions.

## 7. Email verification and first login

New Employee lifecycle:

1. authorized administrator creates Employee and selects allowed initial Role;
2. CYID stores Employee as Email-unverified / first-login incomplete;
3. CYID sends an Email verification message containing a one-time first-login password;
4. user uses the normal CY Web login entry;
5. CYID recognizes the valid initial credential and returns a first-login ticket, not a normal session;
6. CY Web forces the user to set a permanent 8–16 character password;
7. CYID atomically creates the permanent credential, marks Email verified / first lifecycle complete, invalidates initial credential and ticket;
8. CY Web returns to the normal login screen; only a fresh permanent-password login creates a normal session.

Expired initial credential or authorized resend keeps the lifecycle as **Email verification incomplete**. Resend replaces the credential and expiry; it is not a separate account-activation product flow.

## 8. Activated-account Email recovery and OTP

Forced Email recovery for an already activated account preserves the permanent password and activated history, marks the replacement Email unverified and revokes sessions. This flow is distinct from first login.

OTP remains a shared engine for bootstrap, password recovery, activated Email verification/recovery and Super Admin transfer. OTP challenges are purpose-scoped, single-use and bounded by expiry/rate/attempt policies in `OTP_SECURITY.md`.

## 9. Audit, migrations and public-source boundary

- Protected Identity operations write Identity audit events without secrets.
- D1 schema changes use forward migrations; applied migrations are never rewritten.
- Public source contains generic schema/contracts and synthetic fixtures only.
- Runtime resource IDs, real access matrices, Employee/Email data and secrets are injected through controlled deployment environments.

## 10. Consumer boundary

- CY Web hosts the core account-management UI but never becomes credential/session authority.
- CYAccountingWeb and CYInvoice consume the same normalized principal/session/App Access contract in their own workstreams.
- CYInvoice-specific Device/local/offline mechanisms remain outside CYID unless separately promoted later.

## 11. Source map

- permanent product rules: `../PROJECT_RULES.md`
- role/access product model: `ROLE_AND_ACCESS_MODEL.md`
- executable consumer auth contract: `AUTH_CONTRACT.md`
- management UI contract: `UI_ACCESS.md`
- OTP policy: `OTP_SECURITY.md`
- implementation status: `../TODO.md`
- schema source of truth: `../migrations/`