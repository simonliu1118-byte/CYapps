# CYAccountingWeb → CYCloud Identity Integration Handoff

> Purpose: consumer-specific implementation handoff for CYAccountingWeb (CYACCweb).
>
> Provider baseline: CYCloud Identity 0.3.0 is merged and deployed to development.
>
> Authority: this is a derived consumer guide. If it conflicts with `../AUTH_CONTRACT.md`, `../ROLE_AND_ACCESS_MODEL.md`, `../ARCHITECTURE.md`, or `../../PROJECT_RULES.md`, the canonical CYID documents win.
>
> Acceptance note: automated/local D1/development-deployment evidence is green. The real controlled Email/browser first-login acceptance is temporarily deferred and remains a pre-production acceptance item.

## 1. CYID responsibility for CYACC

CYACC consumes CYID for:

- Workspace-scoped Employee identity;
- permanent-password authentication;
- Workspace Role `SUPER_ADMIN / ADMIN / USER`;
- `Identity Admin` as an extra ADMIN capability;
- CYACC Application Access;
- app-scoped normal Identity Session;
- session resolve and revocation;
- account disable / role / credential / App Access invalidation.

CYACC must not copy CYID credential, OTP, recovery or session tables into its own database. Detailed accounting/module/business permissions remain CYACC-owned unless a later shared contract explicitly promotes them.

## 2. Critical first-login boundary

New-Employee Email verification belongs to CY Web + CYID, not CYACC.

CYACC must not implement:

- a separate `啟用帳號` entry;
- first-login password handling;
- first-login ticket handling;
- new-Employee Email-verification OTP;
- permanent-password creation for a never-verified Employee.

The one-time first-login password is accepted only for the configured CY Web core account application. It must not authenticate CYACC.

Expected journey:

~~~text
Manager creates Employee
  -> CYID sends Email 驗證 mail
  -> user enters CY Web with one-time first-login password
  -> user sets permanent password
  -> CYID completes Email verification
  -> CY Web returns to normal login
  -> user logs in with permanent password
  -> Identity Admin / Super Admin grants CYACC App Access when appropriate
  -> user may log in to CYACC with the permanent password
~~~

If a CYACC user has not completed first Email verification, direct the user to CY Web. Do not expose or forward a first-login ticket through CYACC.

## 3. Cloudflare topology

Preferred topology:

~~~text
Browser
  -> CYACC Worker
  -> private Cloudflare Service Binding
  -> CYCloud Identity Worker
  -> Identity D1
~~~

CYACC Worker needs deployment-injected values equivalent to:

~~~text
IDENTITY                 private Service Binding
IDENTITY_APPLICATION_ID  registered CYACC application id
IDENTITY_WORKSPACE_ID    target Workspace id
~~~

Public source must not commit actual production Workspace IDs, Cloudflare resource IDs, provider targets or secrets.

## 4. Application registration prerequisite

Before CYACC login works:

1. register one CYACC `application_id` in CYID;
2. enable that Application for the target Workspace;
3. inject that exact Application ID into CYACC runtime;
4. grant eligible Employees direct CYACC Application Access;
5. keep Super Admin implicit entry for all Workspace-enabled Apps.

Do not invent a production Application ID in Public source. Use a placeholder such as `<CYACC_APPLICATION_ID>` until governed deployment supplies the registered value.

## 5. Normal login

CYACC Worker calls `POST /v1/identity/login`.

Request:

~~~json
{
  "workspaceId": "<runtime workspace id>",
  "applicationId": "<runtime CYACC application id>",
  "employeeNo": "0001",
  "password": "<user supplied permanent password>"
}
~~~

For CYACC, success must be the normal `principal + session` form. A `passwordChangeRequired` response is not a CYACC login success; fail closed and direct the user to CY Web.

## 6. Normalized principal

CYACC consumes the direct principal:

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

Consistency rules:

~~~text
isWorkspaceSuperAdmin === (workspaceRole === "SUPER_ADMIN")
isIdentityAdmin === true only when workspaceRole === "ADMIN"
~~~

Direct role projection is mandatory:

~~~text
CYID SUPER_ADMIN -> CYACC SUPER_ADMIN
CYID ADMIN       -> CYACC ADMIN
CYID USER        -> CYACC USER
~~~

Legacy `groupKeys` / `applicationRoleKey` are compatibility data only and are not forward authority.

## 7. Session transport

Recommended browser boundary:

~~~text
CYACC Worker receives CYID session.token
  -> app-specific HttpOnly cookie
  -> Secure
  -> SameSite=Strict
  -> Max-Age bounded by provider expiresAt
~~~

Rules:

- no raw token in localStorage/sessionStorage;
- no token in URL/query/hash;
- no token in logs/Audit/analytics/error text;
- no competing CYACC Identity session table;
- browser cookie deletion alone is not provider logout.

An app-specific cookie name such as `cyacc_identity_session` is appropriate.

## 8. App Access is separate from Role

- Super Admin: automatic entry if CYACC is enabled for the Workspace;
- all other Employees: enabled direct Employee → CYACC Application Access required;
- new Employee: CYACC App Access defaults ungranted;
- normal ADMIN cannot manage App Access;
- Identity Admin / Super Admin manage eligible App Access;
- Identity Admin cannot change own App Access;
- Super Admin App Access is implicit/protected;
- Role changes preserve App Access.

CYACC must not infer App Access from Role except the explicit Super Admin implicit-all rule.

## 9. Session resolve on protected requests

Use `POST /v1/identity/session/resolve` with:

~~~text
Authorization: Bearer <opaque CYID session token>
x-identity-application: <CYACC application id>
~~~

CYID re-checks session expiry/state, Workspace, Employee enabled state, credential version, Application enabled state, current CYACC App Access, Workspace Role and Identity Admin capability.

Do not treat the principal captured at login as permanently authoritative.

## 10. Immediate invalidation

CYACC must honor provider authority on subsequent protected requests:

- Employee disable -> existing CYACC session no longer authorizes;
- Role change -> affected session invalidated/re-resolved;
- password change/reset -> older sessions invalid;
- CYACC App Access removal -> CYACC authorization stops;
- forced Email recovery -> sessions revoked;
- Super Admin transfer -> affected authority sessions re-authenticate.

## 11. Logout

Call `POST /v1/identity/logout` with the current bearer token and CYACC application header, then clear the CYACC cookie.

Do not implement logout as cookie deletion only.

## 12. Error normalization

Recommended CYACC stable classes:

| Provider/condition | CYACC stable class |
| --- | --- |
| malformed login | `INVALID_LOGIN_REQUEST` |
| credential rejected | `LOGIN_FAILED` |
| App Access denied | `ACCESS_DENIED` |
| invalid/revoked session | `AUTH_INVALID` |
| no session | `AUTH_REQUIRED` |
| rate limited | `LOGIN_RATE_LIMITED` |
| provider unavailable/invalid response | `IDENTITY_UNAVAILABLE` |

Do not expose raw provider/D1 errors.

## 13. CYACC-local business authorization

CYID answers:

~~~text
Who is the Employee?
What is the Workspace Role?
May this Employee enter CYACC?
~~~

CYACC answers:

~~~text
Which accounting modules/features may this Employee use?
Which business records/actions are allowed?
~~~

CYACC-local permissions must be server-side enforced inside CYACC. Do not move accounting/module permission rows into CYID merely because CYID already owns App Access.

## 14. Identity-management UI scope

CY Web remains the primary account-management UI. CYACC does not need to duplicate Employee creation, Email verification, resend verification Email, Identity Admin grant/revoke, App Access administration, forced Email recovery, Super Admin transfer, or Workspace security policy.

## 15. Recommended CYACC implementation sequence

1. add a provider-neutral Identity adapter inside CYACC;
2. add private CYID Service Binding and runtime Application/Workspace IDs;
3. implement Worker-side permanent-password login;
4. keep raw session token in an app-specific secure HttpOnly cookie;
5. normalize `IdentityPrincipal` exactly;
6. implement `/auth/me` through CYID session resolve;
7. implement provider logout then cookie clear;
8. make CYACC shell entry depend on valid CYID principal/App Access;
9. retire any legacy Group-derived coarse role authority;
10. keep detailed CYACC business authorization local;
11. add source tests proving CYACC does not copy credential verifier/Identity D1;
12. add synthetic login/resolve/logout/error acceptance;
13. deploy only to CYACC development;
14. run the acceptance matrix below;
15. production cutover only after explicit approval.

## 16. Minimum acceptance matrix

Before CYACC production cutover prove:

- permanent-password Super Admin can log in;
- USER with CYACC App Access can log in;
- USER without CYACC App Access is denied;
- ADMIN with App Access resolves as `ADMIN`;
- Identity Admin resolves as `ADMIN + isIdentityAdmin=true`, not a fourth role;
- removing CYACC App Access invalidates authorization on next protected request;
- disabling Employee invalidates authorization;
- Role change is reflected after invalidation/re-authentication;
- password change/reset invalidates older session;
- logout revokes provider session;
- first-login temporary password cannot log into CYACC;
- first-login ticket cannot be used as CYACC session;
- no raw CYID token appears in source/log/localStorage;
- CYACC business permission enforcement remains app-local and server-side.

## 17. Current known acceptance gap

As of 2026-09-30, CYID 0.3 provider source, migrations, typecheck, auth-core, synthetic login/first-login acceptance and development deployment are complete.

The controlled real Email/browser lifecycle is temporarily deferred:

~~~text
create Employee
-> receive real Email verification mail
-> first-login password in CY Web
-> set permanent password
-> return to login
-> permanent-password login
~~~

CYACC development integration may proceed against the stable provider contract. Final production acceptance must still include this real lifecycle evidence.

## 18. Canonical references

Read in this order:

1. `../../PROJECT_RULES.md`
2. `../ROLE_AND_ACCESS_MODEL.md`
3. `../AUTH_CONTRACT.md`
4. `../ARCHITECTURE.md`
5. `../UI_ACCESS.md`
6. `../../TODO.md`

This handoff is a consumer guide, not a new source of permanent CYID rules.