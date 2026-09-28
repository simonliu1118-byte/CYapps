# CYInvoice Next Chat Handoff — 2026-09-29

> Purpose: start a new conversation without re-deriving the current CYInvoice Cloud / identity architecture. Read this file first, then follow the referenced design documents before changing source.

## 1. Repository / branch / release state

- Repository: `simonliu1118-byte/CYapps`
- Project: `apps/CYInvoice/`
- Working branch: `cyinvoice/feat-cloud-security-audit`
- Draft PR: `#121`
- Current engineering version before new source work: **CYInvoice V2.6.6 Build 4**
- Development Cloud baseline: **Cloud 0.8.5 / API 1 / storage Schema 9**
- Formal Release remains `cyinvoice-v2.4.2`
- This latest planning round changed documentation only. No source implementation, deployment, merge, tag or Release was performed.
- Before continuing, read the current PR head rather than relying on a SHA copied into a handoff document.

Before work, obey governance precedence:

1. `/REPOSITORY_RULES.md`
2. `/REPO_POLICY.md`
3. `/apps/CYInvoice/PROJECT_RULES.md`
4. project design/status/TODO documents

## 2. Read these documents in this order

1. `docs/NEXT_CHAT_HANDOFF_2026-09-29.md` — this file.
2. `docs/IDENTITY_PROVIDER_REFACTOR_PLAN.md` — approved next implementation sequence.
3. `docs/CY_ID_INTEGRATION.md` — CYInvoice / CY ID product boundary.
4. `docs/CLOUD_WORK_HANDOFF.md` — current Cloud deployment / A-B acceptance / known defects.
5. `docs/CLOUD_IDENTITY_LIFECYCLE.md` — Workspace / Device / Employee lifecycle rules.
6. `docs/TODO.md` — remaining verification and longer-term work.

Shared desktop architecture reference:

- `simonliu1118-byte/AITeam:docs/DESKTOP_IDENTITY_PROVIDER_GUIDE.md`

AITeam guide is reference architecture only; CYInvoice governance and product rules remain authoritative.

## 3. Product modes now agreed

CYInvoice must remain usable in three formal modes:

```text
1. Local
2. Built-in Cloud / Self-hosted
3. CY ID Cloud
```

### Local

- No Cloud Workspace required.
- Local EmployeeStore is the account authority.
- CYInvoice owns Account Management.

### Built-in Cloud / Self-hosted

- CYInvoice keeps its own Workspace, Employee/Credential authority, Email/OTP path, Device and permissions.
- A third party can self-host its own CYInvoice Cloud without any CY ID dependency.
- This is a first-class mode, not a fallback.
- CYInvoice owns Account Management.

### CY ID Cloud

- CYInvoice still owns its own CYInvoice Workspace, Device, Device Token, pairing/invitation/revoke, synchronization, Work Item and business state.
- Employee / Credential authority comes from CY ID.
- CYInvoice Account Management is hidden; account CRUD is managed from CYWEB / CY ID.
- CY ID internal schema, Group model and App Access implementation are not designed in the CYInvoice workstream.

## 4. Workspace boundary

Do not merge the two Workspace concepts.

```text
CY ID Workspace
= organization / people / credential / shared identity boundary

CYInvoice Workspace
= CYInvoice device / pairing / sync / work-item / business-coordination boundary
```

CYInvoice Workspace remains present in both Built-in Cloud and CY ID Cloud.

In CY ID mode the two Workspaces are linked by a binding; they are not the same database entity and do not need the same ID.

## 5. Role model is now fixed

CYInvoice formal roles are:

```text
SUPER_ADMIN
ADMIN
USER
```

`EMPLOYEE` is no longer a role name. Employee remains the person/account entity; `USER` is the ordinary permission level.

No persisted legacy `EMPLOYEE` role data exists that needs migration. Do not add a permanent compatibility alias unless new evidence appears.

CY ID -> CYInvoice role mapping is direct 1:1:

```text
SUPER_ADMIN -> SUPER_ADMIN
ADMIN       -> ADMIN
USER        -> USER
```

SUPER_ADMIN must remain SUPER_ADMIN; do not downgrade it to "treated as ADMIN".

## 6. CY ID App Access boundary

CY ID App Access is upstream entry authorization: whether an Employee may use CYInvoice at all.

CYInvoice should consume the result but must not create a duplicate App Access administration layer.

Expected future CY ID consumer result needed by CYInvoice is deliberately small:

```text
stable Employee identity
enabled state
CYInvoice access allow/deny
CYInvoice role: SUPER_ADMIN | ADMIN | USER
```

CY ID Groups may exist and evolve, but CYInvoice first integration does not need to understand them.

## 7. Immediate next work package

Do **not** wire CY ID into existing Forms/workflows yet.

The next authorized planning target is **Identity Foundation Refactor**, defined in `IDENTITY_PROVIDER_REFACTOR_PLAN.md`.

Package 1 scope:

```text
A. EMPLOYEE role name -> USER
B. normalized AppPrincipal / AppRole
C. minimal IIdentityProvider boundary
D. LocalIdentityProvider
E. BuiltInCloudIdentityProvider
F. central provider selection/composition
G. route current execution-time authentication call sites through provider
H. preserve current Local and Built-in Cloud behavior
I. regression tests / Windows build / startup smoke
```

Package 1 intentionally does **not** include:

- CY ID provider implementation;
- Cloud current-authority freshness behavior change;
- Device revoke;
- Cloud -> Local destructive reset;
- Work Item / business sync;
- generic plugin framework for arbitrary external identity providers.

The first refactor must reduce coupling without mixing in multiple behavioral changes.

## 8. Then fix the known Cloud authority defect

After Package 1 is stable, Package 2 fixes the reproduced stale-Employee-cache defect.

Observed A/B behavior:

- A created a new central Employee.
- B did not see it with its stale local Employee snapshot.
- B fully restarted and then saw the Employee.

Target Built-in Cloud behavior:

```text
Protected operation
  ↓
Cloud reachable
  -> verify against current Cloud authority
  -> refresh local protected cache

Cloud truly unreachable
  -> use last trusted protected offline cache
```

Do not require a 5-minute sync interval or restart before new Employee / role / enabled / password changes take effect on another Device.

## 9. Device / reset dependency

Next dependency chain after Cloud auth freshness:

```text
Device revoke / retire
        ↓
Cloud -> Local destructive reset
```

Device revoke requirements already decided:

- revoke/retire rather than hard-delete history;
- old Device Token becomes unusable immediately;
- authorized management only;
- security audit event;
- rejoining the same physical PC creates a new Device identity.

Cloud -> Local is a destructive current-PC reset with two confirmations. It clears local CYInvoice data/identity state and restarts first-run; it does **not** delete the Cloud Workspace, other Devices or central Employees.

## 10. CY ID implementation timing

Do not implement `CyIdIdentityProvider` until CYCloudIdentity/CYWEB publishes a stable consumer handoff/contract.

When that contract is stable, the intent is that CYInvoice only adds another provider/adapter. Existing business workflows should already depend on normalized principal/authorization rather than CY ID-specific APIs.

CY ID mode also requires a reviewed Windows Offline cache protocol. Do not read CY ID D1 directly and do not create Built-in + CY ID dual authority.

## 11. Existing Cloud acceptance still outstanding

After the foundation work, continue with:

- invitation-code join real-machine acceptance;
- invitation revoke/resend;
- result-unknown recovery;
- A/B USER/ADMIN/SUPER_ADMIN behavior;
- Employee CRUD/password/enabled/role synchronization;
- Offline -> reconnect acceptance;
- security audit viewer;
- all-Device-Token-loss Recovery Device flow;
- later V3 Work Item / business coordination.

Offline cache server-signature/tamper-evidence remains a long-term non-blocking TODO unless the threat model changes or a real incident occurs.

## 12. Migration decision

Do not build a broad Built-in -> CY ID migration framework now.

Current development Workspace is not carrying a mature multi-user production account history, and AMEGO remains the official invoice truth. Before a real cutover, perform a targeted acceptance check for local-only pending/unknown/special state that cannot be recreated by the normal two-period AMEGO synchronization.

That cutover check is not justification for a generic migration subsystem today.

## 13. Public repository privacy boundary

`CYapps` is Public. During source/docs/tests:

Do not commit real:

- Workspace / Device / Employee identifiers;
- Employee names / Emails;
- AMEGO credentials or real customer/invoice data;
- Cloud endpoints that are treated as private runtime configuration;
- API keys, passwords, OTPs, invitation/pairing codes, Device Tokens;
- production resource IDs or private deployment topology;
- credential verifier material derived from real passwords.

Use synthetic fixtures/placeholders only.

## 14. What the next conversation should do first

Before editing source:

1. Re-read governance and the documents listed in section 2.
2. Inspect the current source for every Employee authentication / direct credential-check call site.
3. Inspect current role constants/schema/tests/UI usage of `EMPLOYEE`.
4. Inspect current application composition/runtime mode selection.
5. Produce the precise Package 1 modification map and acceptance matrix.
6. Confirm version/Build classification under governance.
7. Only then begin implementation after user authorization.

Do not start by coding a CY ID adapter. The provider boundary comes first.
