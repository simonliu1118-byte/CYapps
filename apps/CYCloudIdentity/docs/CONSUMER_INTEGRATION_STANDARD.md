# CYCloud Identity Consumer Integration Standard

> **Contract version:** `1.0.0`
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

## 18. Consumer adoption and update procedure

When an application begins CYID integration:

1. read the current `CONSUMER_INTEGRATION_STANDARD.md`;
2. read the current consumer contract/version window;
3. add `CYID_CONSUMER_VERSION` to the consumer project/repository;
4. implement against canonical CYID endpoints/fields;
5. keep domain authorization local;
6. add automated source/session/error acceptance;
7. deploy to development first;
8. run app-specific browser/device acceptance;
9. record any app-specific permanent exception only in that app's `PROJECT_RULES.md`;
10. never copy shared CYID semantics into a handoff as an independent authority.

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
- `ARCHITECTURE.md`.

Consumer-specific handoffs contain only migration/app differences and may never override the documents above.
