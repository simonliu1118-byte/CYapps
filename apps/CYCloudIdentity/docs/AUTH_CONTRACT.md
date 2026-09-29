# CYCloud Identity — Authentication Contract

> **Status:** source contract for CYCloud Identity `0.2.0`. Deployment/acceptance state is tracked in `../TODO.md`; actual Workspace/Application/Employee runtime data is never stored in Public source.

## Transport boundary

Cloud App Workers call CYCloud Identity through a private Cloudflare Service Binding. Browsers never receive provider secrets, credential verifiers, OTP peppers or D1 identifiers.

The consumer App keeps the raw Identity session token in an `HttpOnly; Secure` cookie or another reviewed server-side transport. CYCloud Identity stores only the SHA-256 hash of that raw token.

## Workspace role model

Every Employee resolves to exactly one effective Workspace role:

```text
SUPER_ADMIN
ADMIN
USER
```

`SUPER_ADMIN` is not stored as an editable Employee role. It is derived from the protected Workspace `super_admin_employee_id` pointer. Ordinary Employee storage uses only `ADMIN` or `USER`.

`Identity Admin` is an ADMIN capability, not a fourth role:

```text
workspaceRole = ADMIN
isIdentityAdmin = true | false
```

The role and Identity Admin authority boundaries are canonical in `ROLE_AND_ACCESS_MODEL.md` and `PROJECT_RULES.md`.

## Login

`POST /v1/identity/login`

Request body:

```json
{
  "workspaceId": "<runtime supplied workspace id>",
  "applicationId": "<runtime registered application id>",
  "employeeNo": "0001",
  "password": "<user supplied password>"
}
```

Successful `0.2.0` response:

```json
{
  "ok": true,
  "principal": {
    "workspaceId": "<workspace id>",
    "employeeId": "<employee id>",
    "employeeNo": "0001",
    "displayName": "<display name>",
    "workspaceRole": "USER",
    "isIdentityAdmin": false,
    "emailVerified": true,
    "isWorkspaceSuperAdmin": false,
    "groupKeys": [],
    "applicationRoleKey": "USER",
    "credentialVersion": 1,
    "employeeRevision": 1
  },
  "session": {
    "token": "<opaque one-time-returned session token>",
    "expiresAt": "<UTC ISO-8601 timestamp>"
  }
}
```

Authoritative fields for new consumers:

- `workspaceRole`
- `isIdentityAdmin`
- `emailVerified`
- `isWorkspaceSuperAdmin` as the stable protected-authority compatibility signal
- `credentialVersion`
- `employeeRevision`

Temporary compatibility fields:

- `groupKeys` remains descriptive only while older consumers are removed; it is **not** an authorization source.
- `applicationRoleKey` temporarily mirrors `workspaceRole` so existing consumers do not break during cutover. New consumers must use `workspaceRole`.

The caller must never persist or log the password or raw session token.

## Application entry authorization

Authentication requires all of the following:

- login rate limiter permits the attempt;
- Workspace is active;
- Employee exists, is enabled and has a valid credential;
- Application is active and enabled for the Workspace;
- Application entry is currently allowed.

Entry authorization in `0.2.0`:

1. current Workspace Super Admin may enter every Workspace-enabled CY App;
2. the configured core CY Web account application is always available to every valid Employee and cannot be revoked;
3. every other Employee/Application combination requires an enabled direct `employee_application_access` grant.

Legacy Identity Group membership and Group Application grants no longer authorize login or session resolve.

During migration, active legacy Group grants are materialized once into direct Employee grants before the runtime stops consulting Groups. The old Group tables/endpoints may remain temporarily for consumer compatibility, but changes to them do not change effective login authorization.

## Role projection into consumer Apps

All CYID-integrated CY Apps receive the Workspace role directly:

```text
CYID SUPER_ADMIN -> App SUPER_ADMIN
CYID ADMIN       -> App ADMIN
CYID USER        -> App USER
```

Application Access remains a separate entry decision. App-local USER permissions remain owned by the target App. CY Web's module-access exception is documented in `ROLE_AND_ACCESS_MODEL.md`.

## Resolve session

`POST /v1/identity/session/resolve`

Headers:

```text
Authorization: Bearer <raw Identity session token>
X-Identity-Application: <application id>
```

Resolve re-checks current authority on every request:

- session exists, is not expired and not revoked;
- Workspace remains active;
- Employee remains enabled;
- credential version still matches;
- Application remains active/enabled for the Workspace;
- current direct/core/Super-Admin Application Access still permits entry;
- current Workspace role and Identity Admin capability are read from current authority state.

Normal resolve is read-only and does not use sliding heartbeat writes.

Protected authority changes also revoke sessions where required by the role/access contract. In particular:

- Employee disable or role change revokes that Employee's sessions;
- direct App Access removal revokes sessions for that Employee + App;
- forced Email recovery revokes that Employee's sessions;
- Super Admin authority transfer invalidates both old and new authority sessions so fresh authentication establishes the new role boundary.

## Logout

`POST /v1/identity/logout`

Uses the same `Authorization` and `X-Identity-Application` headers. Logout is idempotent and revokes the matching Identity session. The consumer App clears its browser cookie regardless of whether the remote session had already expired/revoked.

## Employee activation

New Employee creation is role-aware:

- normal ADMIN may create USER only;
- Identity Admin and Super Admin may create USER or ADMIN;
- Identity Admin capability is never granted through Employee creation.

A new Employee remains pending until first activation completes. Creation attempts to send the first activation Email immediately. The Email contains:

- the Email OTP;
- a direct CY Web account-activation link.

The link opens the activation UI only; it is not an authentication credential. Email OTP verification and first-password creation remain mandatory.

If delivery fails, the Employee record remains pending and an authorized administrator may resend. `POST /v1/identity/activation/start` reuses a still-valid already-sent activation challenge before attempting another Email, so clicking the first Email link does not immediately collide with OTP resend cooldown.

Only never-activated Employees may be physically deleted. `activated_at` is the durable first-activation marker and is not cleared by later Email recovery.

## Activated-account Email recovery

Identity Admin or Super Admin may replace the Email of an already activated non-Super-Admin account through the controlled recovery endpoint.

The operation:

- preserves the Employee and password credential;
- preserves activated state;
- sets the new Email to unverified;
- revokes existing sessions;
- sends a verification OTP to the replacement Email when delivery is available.

The Employee may then authenticate with the existing password and verify the current Email. An account in this state is `啟用 · Email 待驗證`, not first-time pending activation.

## Current management endpoints added by 0.2.0

Generic paths, with provider authorization enforced server-side:

```text
GET    /v1/admin/identity/snapshot
POST   /v1/admin/identity/employees
PATCH  /v1/admin/identity/employees/:employeeId
DELETE /v1/admin/identity/employees/:employeeId
POST   /v1/admin/identity/employees/:employeeId/activation/resend
PUT    /v1/admin/identity/employees/:employeeId/identity-admin
PUT    /v1/admin/identity/employees/:employeeId/applications/:applicationId
POST   /v1/admin/identity/employees/:employeeId/email-recovery
POST   /v1/admin/identity/employees/:employeeId/email-verification/resend
POST   /v1/identity/email-verification/start-current
```

Legacy Group-management endpoints are temporarily retained for migration compatibility only and must not be used as a new authorization model.

## Current errors

Consumers should branch on HTTP status and stable `error.code`. Relevant authentication errors include:

- `400 INVALID_LOGIN_REQUEST`
- `401 AUTHENTICATION_FAILED`
- `401 SESSION_INVALID`
- `403 APPLICATION_ACCESS_DENIED`
- `429 AUTH_RATE_LIMITED`
- `503 AUTH_RATE_LIMITER_UNAVAILABLE`
- `404 NOT_FOUND`
- `500 IDENTITY_REQUEST_FAILED`

Management APIs return operation-specific 4xx errors such as `WORKSPACE_ADMIN_REQUIRED`, `IDENTITY_ADMIN_REQUIRED`, `EMPLOYEE_ROLE_NOT_ALLOWED`, `SELF_ACCESS_CHANGE_NOT_ALLOWED`, `CORE_APPLICATION_ACCESS_LOCKED`, `SUPER_ADMIN_PROTECTED`, and lifecycle/email-recovery conflict codes. Browser UI must not be treated as the authorization boundary.

## Credential compatibility

New CYCloud Identity credentials use `scrypt`:

```text
scrypt$16384$8$1$<16-byte-salt-hex>$<32-byte-digest-hex>
```

Current parameters are N=16,384, r=8, p=1. Password policy is 8–16 Unicode characters. Plaintext passwords never enter D1, Git, Audit or backup metadata.

The Worker may verify legacy `pbkdf2-sha256` verifiers only when their iteration count is within the reviewed Cloudflare ceiling. New credential generation always records the actual algorithm used.

## Session compatibility

The opaque token representation is an implementation detail. Consumers must not parse Workspace, Employee, Role or Access data from it.

Default session TTL is 8 hours, constrained by runtime bounds. Credential changes/resets invalidate older sessions through credential-version mismatch and explicit revocation where required.

## Consumer rules

A consumer App must not:

- read Identity D1 directly;
- copy credential/OTP/recovery tables into its own database;
- persist credential verifiers;
- hard-code actual Workspace IDs, Application IDs or access matrices in Public source;
- infer Workspace role from Identity Group names;
- use Group grants as current App authorization;
- treat deprecated `applicationRoleKey` as the long-term role field;
- implement App Access only by hiding frontend controls.

App-specific module/business permissions remain inside each App after CYID authenticates the principal.
