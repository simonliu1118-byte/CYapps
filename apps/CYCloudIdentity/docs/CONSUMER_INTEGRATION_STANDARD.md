# CYCloud Identity Consumer Integration Standard

> **Contract version:** `1.0.6`
>
> **Minimum compatible consumer version:** `1.0.0`
>
> **Status:** canonical technical integration standard for every application that consumes CYCloud Identity.
>
> Permanent obligations that make this standard mandatory live in `../PROJECT_RULES.md` and repository/project governance. This document defines the shared technical contract; it is not a fourth rules layer.

## 1. Purpose and authority

CYCloud Identity (CYID) is the shared Identity authority for CY applications.

Every CYID consumer must implement Identity against this standard rather than copying a consumer-specific handoff or inventing an app-local Identity model.

This standard answers the common integration questions:

- how a consumer authenticates a permanent credential;
- how it transports and resolves a CYID Session;
- how Workspace Role / Identity Admin / Application Access are interpreted;
- how password recovery is delegated;
- what first-login behavior a non-core consumer must reject;
- what authorization remains app-local;
- how contract compatibility and upgrades are governed.

Consumer-specific handoffs may document migration from an existing implementation, app-specific browser constraints, or app-local acceptance. They may not redefine this standard.

## 2. Consumer contract versioning

CYID tracks two machine-readable versions:

- `CONSUMER_CONTRACT_VERSION`: newest published consumer contract revision;
- `CONSUMER_MIN_COMPATIBLE_VERSION`: oldest consumer revision still supported by the current provider runtime.

A consumer that has completed CYID integration records the contract it implements in its own `CYID_CONSUMER_VERSION`.

A deployable consumer version is supported only when:

~~~text
CYID minimum compatible version
  <= consumer CYID_CONSUMER_VERSION
  <= CYID current consumer contract version
~~~

The consumer-contract version is independent from CYID product `VERSION`. Internal provider patches that do not alter consumer obligations do not need to advance the consumer contract.

### Compatibility classes

Every CYID PR must declare one of:

- `NONE` — no consumer-visible contract change;
- `BACKWARD_COMPATIBLE` — consumer-visible addition/clarification while existing supported consumers remain valid;
- `CONSUMER_UPDATE_REQUIRED` — consumers must migrate before an old behavior can be retired or before the compatibility minimum advances.

For `BACKWARD_COMPATIBLE` and `CONSUMER_UPDATE_REQUIRED`, update this standard, `CONSUMER_CONTRACT_VERSION`, and `CONSUMER_CONTRACT_CHANGELOG.md` in the same work item.

A breaking provider deployment must not strand an existing production consumer outside the supported version window. Keep a compatibility path during migration, or coordinate the affected consumer updates before raising `CONSUMER_MIN_COMPATIBLE_VERSION`.

### Cross-repository contract synchronization

CYID publishes `CONSUMER_SYNC_MANIFEST.json` as the machine-readable list of shared contract artifacts that must be mirrored by consumers living in another repository.

Cross-repository consumers must:

- keep a synchronized mirror of every manifest-listed file under their governed contract-mirror directory;
- provide a repeatable sync command/script that refreshes those files from CYID `main`;
- validate mirror bytes against CYID `main` in governance/CI and before development/production deployment;
- treat the mirror as a read-only synchronized copy, never as a new authority;
- update the mirror whenever any manifest-listed source file changes, even when `CONSUMER_CONTRACT_VERSION` does not change.

Consumers in the same repository as CYID must read the canonical files directly and must not create a redundant mirror.

## 3. Identity authority boundary

CYID owns:

- Workspace identity;
- Employee identity and Employee No;
- permanent credentials;
- Workspace Role;
- Identity Admin capability;
- Application registry and Employee Application Access;
- normal Identity Session creation, resolve, expiry and revocation;
- Email verification;
- password recovery and Identity OTP flows;
- protected Super Admin authority.

A consumer must not create a competing copy of:

- password/verifier authority;
- CYID OTP/recovery authority;
- CYID Workspace Role semantics;
- CYID App Access authority;
- CYID Session authority.

Consumers may keep a local Employee/member projection keyed by stable CYID `employeeId` for business ownership, audit and local authorization.

## 4. Role and capability projection

Every consumer directly adopts:

~~~text
CYID SUPER_ADMIN -> consumer SUPER_ADMIN
CYID ADMIN       -> consumer ADMIN
CYID USER        -> consumer USER
~~~

`Identity Admin` is an extra capability on `ADMIN`; it is never a fourth role.

Consumers must not:

- derive coarse authority from legacy `groupKeys`;
- recreate `USER_ADMIN` or another compatibility role;
- infer a different global role from local business permissions.

Required consistency:

~~~text
isWorkspaceSuperAdmin === (workspaceRole === "SUPER_ADMIN")
isIdentityAdmin === true only when workspaceRole === "ADMIN"
~~~

## 5. Application Access

Workspace Role and Application Access are independent.

For a non-core consumer application:

- Super Admin may enter automatically when the Application is enabled for the Workspace;
- every other Employee requires current direct Application Access;
- new Employee App Access defaults ungranted unless an approved flow explicitly grants it;
- Role changes do not silently add/remove App Access.

A consumer must not treat local UI visibility as App Access authority.

## 6. Runtime configuration and service topology

Preferred Cloudflare topology:

~~~text
Browser
  -> consumer Worker
  -> private IDENTITY Service Binding
  -> CYCloud Identity Worker
  -> Identity D1
~~~

A consumer Worker receives deployment-injected values equivalent to:

~~~text
IDENTITY
IDENTITY_APPLICATION_ID
IDENTITY_WORKSPACE_ID
~~~

Binding names may differ only when the app has a documented reason, but semantics must remain equivalent.

Production Workspace IDs, registered Application IDs, actual provider service names and secrets are deployment data and must not be hard-coded into Public source.

## 7. Permanent-password login

Normal consumer login calls:

`POST /v1/identity/login`

Request semantics:

~~~json
{
  "workspaceId": "<runtime workspace id>",
  "applicationId": "<runtime application id>",
  "employeeNo": "0001",
  "password": "<permanent password>"
}
~~~

Permanent password input uses the shared boundary: **8–16 Unicode characters**.

A normal success contains a normalized principal and app-scoped provider Session.

Consumers must validate the provider response before creating browser login state.

## 8. First-login / Email-verification boundary

The new-Employee first Email-verification flow belongs to the **CY Web core account application + CYID**.

A non-core consumer must not:

- accept the one-time first-login password as a normal login;
- receive or persist a first-login ticket;
- create a normal Session from `passwordChangeRequired`;
- implement a parallel 「啟用帳號」flow;
- create the first permanent password for a never-verified Employee.

If `POST /v1/identity/login` returns a first-login-required response to a non-core consumer, fail closed and direct the user to CY Web.

A first-login ticket is not a Session and must never authorize a consumer business API.

## 9. Normalized principal

Consumers normalize at least:

~~~ts
type WorkspaceRole = "SUPER_ADMIN" | "ADMIN" | "USER";

interface IdentityPrincipal {
  workspaceId: string;
  employeeId: string;
  employeeNo: string;
  displayName: string;
  workspaceRole: WorkspaceRole;
  isIdentityAdmin: boolean;
  emailVerified: boolean;
  isWorkspaceSuperAdmin: boolean;
  credentialVersion: number;
  employeeRevision: number;
}
~~~

A consumer may retain extra descriptive provider fields but must not use legacy compatibility fields as forward authority.

## 10. Session transport

CYID owns Session authority.

Consumer requirements:

- do not mint a second independent Identity Session as the forward authority;
- do not persist raw provider Session tokens in consumer business tables;
- do not expose raw provider Session tokens to localStorage/sessionStorage, URL/query/hash, application log, Audit payload or analytics;
- browser transport uses an app-specific HttpOnly + Secure cookie or an equivalently protected transport;
- cookie `SameSite` policy is chosen and tested per consumer/browser constraints; it must not enable cross-site token leakage;
- cookie lifetime is bounded by provider `expiresAt`;
- clearing a browser cookie is not a substitute for provider logout.

Existing app-specific browser compatibility may justify `SameSite=Lax` instead of `Strict`; such exceptions do not change CYID authority.

## 11. Session resolve

Every protected consumer server request that depends on Identity authority must resolve/revalidate the provider Session or use an approved short-lived server cache that cannot outlive provider invalidation requirements.

Canonical provider endpoint:

`POST /v1/identity/session/resolve`

Headers include:

~~~text
Authorization: Bearer <opaque provider session token>
x-identity-application: <runtime application id>
~~~

The consumer must accept current provider authority for:

- session expiry/revocation;
- Employee enabled state;
- Workspace state;
- credential version;
- current Workspace Role;
- Identity Admin capability;
- Application enabled state;
- direct Employee Application Access.

A stale browser page never preserves server authority after provider rejection.

### 11.1 Optional private cache-invalidation capability (1.0.3)

A trusted consumer gateway may bind the named `ConsumerAuthoritySync` Worker entrypoint using a private RPC Service Binding (consumer name `IDENTITY_AUTHORITY`). It supplements the existing `IDENTITY` Fetcher; it has no public HTTP endpoint. This optional capability is **BACKWARD_COMPATIBLE**, minimum compatible remains 1.0.0; existing login/resolve/logout consumers and bindings keep working.

`invalidateCache({workspaceId, applicationId, authorities})` accepts at most 10,000 already-confirmed cache descriptions: `employeeId`, positive `credentialVersion` / `employeeRevision`, canonical `workspaceRole`, boolean `isIdentityAdmin` / `emailVerified`. The gateway derives Workspace/Application from deployment configuration and authenticates its own consumer Device; it must not expose arbitrary scope selection or a public provider proxy.

The provider returns `{workspaceId, applicationId, available, invalidated}`. `available` reports Workspace/Application enablement; `invalidated` contains supplied IDs whose current Employee/enabled/Role/App Access/credential/revision/capability/Email state no longer matches (including deleted or foreign-scope IDs). It compares current indexed authority, so no cursor or missed event can preserve obsolete entries. No principal, Session, verifier, password, permission grant or authority mutation is returned. An unchanged entry is not authorization for a protected operation; normal Login → Resolve → Logout and app-local permission enforcement remain required.

Temporary service failure retains only the consumer's separately approved offline semantics. Known denial invalidates subordinate authority; the capability cannot restore a cache entry or change an old password proof into a newly privileged principal. Polling cadence and offline exceptions are consumer-specific. Empty checks establish service/scope availability without creating Employee authority. Provider work is read-only: one scope lookup and one indexed cached-ID query, no Session/heartbeat writes. Measure real Worker/D1 usage before rollout; actual quotas are deployment data.

## 12. Immediate invalidation behavior

At minimum, subsequent protected requests must stop authorizing after:

- Employee disable;
- relevant Role change/session invalidation;
- permanent credential change/reset;
- Application Access removal;
- forced Email recovery;
- protected Super Admin transfer.

Consumers must fail closed if current Identity authority cannot be established.

## 13. Logout

Normal logout calls:

`POST /v1/identity/logout`

with the current provider bearer token and consumer Application header.

Provider revocation is the authority action. Browser cookie deletion is local cleanup.

## 14. Password recovery

Consumers that expose password recovery delegate it to CYID:

- `POST /v1/identity/password-recovery/start`
- `POST /v1/identity/password-recovery/confirm`

The consumer supplies runtime Workspace ID server-side and follows the shared 8–16 Unicode permanent-password boundary.

Recovery-start UX must remain non-enumerating unless CYID explicitly changes that contract. Do not recreate account enumeration merely to preserve a legacy masked-Email UI.

## 15. Error normalization

Consumers expose stable app-local error classes instead of raw provider/storage internals.

Recommended shared classes:

| Condition | Stable consumer class |
| --- | --- |
| no authenticated Session | `AUTH_REQUIRED` |
| invalid/revoked Session | `AUTH_INVALID` |
| malformed login | `INVALID_LOGIN_REQUEST` |
| credential rejected | `LOGIN_FAILED` |
| Application Access denied | `ACCESS_DENIED` |
| rate limited | `LOGIN_RATE_LIMITED` |
| provider unavailable/invalid response | `IDENTITY_UNAVAILABLE` |

Consumers may add app-specific errors outside the Identity boundary.

## 16. App-local authorization

CYID answers:

~~~text
Who is the Employee?
What is the Workspace Role / Identity capability?
May this Employee enter this Application?
~~~

The consumer answers:

~~~text
Which app modules/features may this Employee use?
Which domain records/actions are allowed?
~~~

Business/module authorization remains app-local unless the CYID permanent rules explicitly promote a capability into the shared Identity model.

Local authorization must be enforced server-side on protected APIs; UI/navigation visibility is not authority.

## 17. Identity-management UI boundary

CY Web is the primary shared account-management UI.

A normal consumer does not need to duplicate:

- Employee creation;
- first Email verification;
- resend verification Email;
- Identity Admin grant/revoke;
- App Access management;
- forced activated-account Email recovery;
- Super Admin transfer;
- Workspace security policy.

If a consumer exposes a subset of these operations later, it remains CYID-backed and must use the same authority contract.

### 17.1 Optional application registration through the core account portal

Contract 1.0.4 adds `POST /v1/admin/identity/applications`. It accepts only
`{applicationId, displayName}`: the ID normalizes to uppercase and must contain
2–64 ASCII letters, digits, underscores or hyphens; the trimmed name contains
1–120 Unicode characters and no control characters. Only the current Workspace
Super Admin's valid Session for the configured core account application may call
it. Workspace scope comes exclusively from that Session, never from request
body or consumer business Workspace IDs.

The operation registers the global catalog entry if absent and enables that
application for the actor's existing Workspace. An existing catalog ID is reused
only with the exact same display name and active status; this route never renames
or globally reactivates an existing application, changes another Workspace,
changes the core application, or creates Employee App Access. Re-enabling an
existing Workspace entry preserves its existing grants; eligible previous grants
may therefore allow entry again. Existing revoked Sessions remain revoked.

Registration, Workspace enablement and audit are one D1 transaction, with current
core Session/credential/Workspace Super Admin authority checked again inside it.
Repeating the same valid request does not duplicate catalog or Workspace entries;
each accepted call is audited. Success returns
`{workspaceId, application: {applicationId, displayName, enabled: true}}` in the
normal provider envelope. Stable errors are `CORE_SUPER_ADMIN_REQUIRED` (403),
`INVALID_APPLICATION_REGISTRATION` (400), `CORE_APPLICATION_ACCESS_LOCKED` (409)
and `APPLICATION_REGISTRATION_CONFLICT` (409).

CY Web exposes this as an explicit Super Admin registration action and refreshes
the existing application-access snapshot. Ordinary consumers continue to use the
existing login/resolve/logout and App Access contracts. Registering an application
does not configure service bindings, enable a consumer's CYID flag, merge identity
and business Workspaces, or authorize a production rollout.

### 17.2 Temporary testing account deletion — 2026-10-15

Contract 1.0.5 adds optional `testingAccountDeletionAllowed: boolean` to the admin
snapshot. An absent/false field retains the pending-only delete UI. During the
explicitly approved testing period the existing `DELETE
/v1/admin/identity/employees/{employeeId}` also accepts previously verified
accounts, but only for the current Super Admin's core account application Session.
Self-deletion, the protected Super Admin and foreign-Workspace targets remain
forbidden. Normal ADMIN/Identity Admin retain only their existing pending-account
management permissions. The server rechecks target revision and live core Super
Admin Session/authority transactionally before deleting a verified account.

Deletion removes the Employee and its provider-owned credentials, first-login
material, Sessions and direct App Access by existing FK cascades; scoped Email
verification/password-reset challenges are removed in the same transaction.
Consumer-owned business history/member projections are retained and must never
be rebound by employee number or email. Recreating the same number/email receives
a fresh employeeId, no inherited Session or App/Module Access.

For this testing exception only, new-account creation (including its initial
Email delivery result) and account deletion do not append Identity audit events.
Existing audit history and other events, including password/verification, resend,
role/access changes and application registration, remain intact. The source-owned
`ACCOUNT_LIFECYCLE_TESTING` defaults must be restored to verified deletion=false
and create/delete audit=true before the first business release. Root project TODO
tracks restoration and actual acceptance; this is not a permanent rules change.

## 18. Consumer adoption and update procedure

When an application begins CYID integration:

1. read the current `CONSUMER_INTEGRATION_STANDARD.md`;
2. read the current consumer contract/version window;
3. add `CYID_CONSUMER_VERSION` to the consumer project/repository;
4. if the consumer is in another repository, synchronize the full `CONSUMER_SYNC_MANIFEST.json` package and enable exact mirror validation;
5. implement against canonical CYID endpoints/fields;
6. keep domain authorization local;
7. add automated source/session/error acceptance;
8. deploy to development first;
9. run app-specific browser/device acceptance;
10. record any app-specific permanent exception only in that app's `PROJECT_RULES.md`;
11. never copy shared CYID semantics into a handoff as an independent authority.

When CYID updates the consumer contract:

- every affected consumer workstream must review the standard/changelog;
- backward-compatible changes may be adopted while the consumer remains inside the supported version window;
- consumer-update-required changes must be implemented before the provider removes the old compatible behavior;
- consumer deployment must refuse a declared `CYID_CONSUMER_VERSION` older than `CONSUMER_MIN_COMPATIBLE_VERSION`.

## 19. Minimum shared acceptance

Every normal consumer should prove at least:

- permanent-password login;
- App Access allowed/denied behavior;
- direct Role projection;
- Identity Admin remains a capability, not a role;
- Session resolve;
- immediate invalidation on Employee/App Access/credential authority changes;
- provider logout;
- first-login password rejected as normal consumer auth;
- first-login ticket rejected as Session;
- raw provider token absent from client storage/logs/source;
- local business authorization remains server-side and app-local.

Real-device/browser checks remain consumer-specific.

## 20. Canonical relationship

Permanent obligation source:

- `../PROJECT_RULES.md`

Shared technical consumer contract:

- this document;
- `AUTH_CONTRACT.md`;
- `ROLE_AND_ACCESS_MODEL.md`;
- `ARCHITECTURE.md`;
- `../CONSUMER_SYNC_MANIFEST.json` for cross-repository mirror membership.

Consumer-specific handoffs contain only migration/app differences and may never override the documents above.

## Consumer Contract 1.0.2 retirement

- Direct principal fields remain the shared authority. Optional legacy `groupKeys` / `applicationRoleKey`, Group CRUD/membership/App Access routes, compatibility-role-mode and Group snapshot arrays are retired. Historical applied D1 migrations/data remain; they are not runtime authority.
- Initial Email verification re-send uses `/v1/admin/identity/employees/{employeeId}/email-verification/resend-initial`; activated-account re-verification retains `/email-verification/resend`. Initial delivery results use `emailVerificationDelivery`; the old `activationDelivery` alias and `/activation/resend` route are retired.
- Consumer Impact: **CONSUMER_UPDATE_REQUIRED** for consumers of these obsolete fields/routes. CY Web adopts 1.0.2 and must deploy before provider retirement reaches production. CYACC direct-principal/session integration does not use these fields/routes and remains compatible with its declared 1.0.1. Minimum compatible remains 1.0.0; it is not raised across unaffected consumers.
- Daily production deployment requires the deployed core consumer to report adopted contract 1.0.2 and does not import development authority.

## 20. Private operational notification capability

Contract 1.0.6 optionally exports the named private RPC entrypoint `ConsumerNotifications`. A trusted server consumer may bind it to the same canonical Identity provider as its `IDENTITY` Fetcher, with an app-local binding name such as `BACKUP_NOTIFICATIONS`. There is no public HTTP route. Existing consumers and minimum 1.0.0 remain supported.

`notifySuperAdmin({workspaceId, applicationId, type: "backup_health_failed", eventId})` accepts only a deployment-derived scope and an opaque UUID incident ID. The consumer owns backup checks, incident state and local business authorization; it must not expose a browser-callable relay or allow browser-selected scope. No recipient, subject, message, finance data, backup contents, resource identifiers or Session is accepted.

The provider revalidates active Workspace, registered/enabled Application and the current protected Super Admin pointer with an enabled Employee and verified Email. It sends fixed generic text using the canonical Email adapter and shared global/Workspace budget. No Email address is returned or copied into app storage. A bounded delivery slot per Workspace/Application/type coalesces concurrent requests; successful incident IDs are idempotent while current, and new incidents are rate-limited to one delivery per hour. Return status is `delivered`, `already_delivered` or `deferred`; failures expose stable codes only. Consumer retries deferred/failed delivery with the same active incident ID, and keeps its login warning until its checks recover. An ambiguous transport failure can cause a repeated alert; it cannot authorize any account or business action.

This binding is a trusted machine capability, not an Identity Session or App Access grant. Deployments must restrict who can attach private bindings. Consumer credential storage, direct provider database access and a second mail credential in each app are unnecessary.
