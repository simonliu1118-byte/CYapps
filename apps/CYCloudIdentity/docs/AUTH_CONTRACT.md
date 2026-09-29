# CYCloud Identity — Authentication Contract

> **Status: executable authentication contract for the currently deployed runtime.** This document contains generic protocol only; actual Workspace/Application/Employee runtime data is not stored in Public source.
>
> **Forward contract note — 2026-09-29:** the approved product model is now the three-role Workspace model in `ROLE_AND_ACCESS_MODEL.md`. The deployed `0.1.14` runtime still returns legacy Group/compatibility-role fields. Treat `groupKeys` and Group-derived `applicationRoleKey` as migration compatibility, not as the forward authorization design. Do not build new product behavior that depends on the legacy Group role projection.

## Transport boundary

Cloud App Workers should call CYCloud Identity through a private Cloudflare Service Binding. Browsers do not call the Identity Worker directly and never receive provider secrets, credential verifiers, OTP peppers or D1 identifiers.

The consumer App is responsible for keeping the raw Identity session token in an `HttpOnly; Secure` cookie or another reviewed server-side transport. CYCloud Identity stores only the SHA-256 hash of that raw token.

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

Current `0.1.14` successful response contains:

```json
{
  "ok": true,
  "principal": {
    "workspaceId": "<workspace id>",
    "employeeId": "<employee id>",
    "employeeNo": "0001",
    "displayName": "<display name>",
    "isWorkspaceSuperAdmin": false,
    "groupKeys": ["<current identity group key>"],
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

`groupKeys` and Group-derived `applicationRoleKey` are legacy compatibility fields in the current runtime. The approved migration target is a direct Workspace role (`SUPER_ADMIN` / `ADMIN` / `USER`) plus Identity Admin capability and independent Application Access. Consumer Apps should be prepared to move to that contract rather than treating legacy Group projection as permanent.

The caller must not persist the password. The raw session token is returned only on login and must not be logged.

Before credential/D1 work, the Worker applies the configured Cloudflare `LOGIN_RATE_LIMITER` to a SHA-256 key derived from Application + Workspace + Employee No. The deployed rate-limit binding is mandatory; if it is missing, authentication fails closed instead of running an unprotected password verifier.

Current runtime authentication requires all of the following:

- login rate limiter permits the attempt;
- Workspace is active;
- Employee exists in that Workspace and is enabled;
- credential algorithm is currently supported and password verifies;
- Application is active and enabled for that Workspace;
- effective Application Access exists through the protected Workspace Super Admin, a direct Employee grant, or a legacy active Identity Group grant.

The approved target model removes Group-derived role authority. See `ROLE_AND_ACCESS_MODEL.md`.

## Current legacy compatibility-role projection

The deployed runtime may use optional `USER_ADMIN` compatibility mode. This section documents only the current implementation pending migration.

When enabled, CYCloud Identity computes `principal.applicationRoleKey` server-side using current authority:

1. Workspace Super Admin → `SUPER_ADMIN`.
2. Otherwise any active Group grant mapped `ADMIN` → `ADMIN`.
3. Otherwise any active Group grant mapped `USER` → `USER`.
4. Otherwise an active direct Employee Application grant → `USER`.
5. Otherwise the Employee has no effective Application access.

Legacy precedence:

```text
SUPER_ADMIN > ADMIN > USER
```

This projection is superseded for forward design by the direct Workspace-role model. New consumer integrations should not add dependencies on Group names or Group-to-App role mappings.

## Approved target role/access contract

After the role/access migration, all CYID-integrated CY Apps consume the Workspace role directly:

```text
CYID SUPER_ADMIN -> App SUPER_ADMIN
CYID ADMIN       -> App ADMIN
CYID USER        -> App USER
```

Application Access is a separate entry decision. CY Web is the core-account special case whose entry access is always available for valid Employees; other App Access is configured independently.

`Identity Admin` is a capability on ADMIN, not a fourth role. Exact target lifecycle, Access-management and CY Web multi-module rules are canonical in `ROLE_AND_ACCESS_MODEL.md` and `PROJECT_RULES.md`.

The runtime response schema for that migration must be versioned/changed only when implementation and consumer adapters are updated together; this document does not pretend that the target fields already exist in `0.1.14`.

## Resolve session

`POST /v1/identity/session/resolve`

Headers:

```text
Authorization: Bearer <raw Identity session token>
X-Identity-Application: <application id>
```

A successful response returns current `principal` plus session expiry. Resolve re-checks current authority on every request:

- session exists, is not expired and not revoked;
- Workspace remains active;
- Employee remains enabled;
- credential version still matches;
- Application remains active/enabled for the Workspace;
- current effective Application Access still permits entry;
- current role/authority state is not accepted from stale browser UI.

Normal resolve is read-only and does not use sliding heartbeat writes.

In the current legacy implementation, Group membership/access-role changes can alter the compatibility projection on the next resolve. In the approved target implementation, Workspace Role and Application Access changes must similarly take effect immediately through session revocation/re-resolution rules defined in `ROLE_AND_ACCESS_MODEL.md`.

## Logout

`POST /v1/identity/logout`

Uses the same `Authorization` and `X-Identity-Application` headers. Logout is idempotent and revokes the matching Identity session. The consumer App must clear its browser cookie regardless of whether the remote session had already expired/revoked.

## Current errors

Callers should branch primarily on HTTP status and `error.code`:

- `400 INVALID_LOGIN_REQUEST` — malformed login request;
- `401 AUTHENTICATION_FAILED` — credential authentication failed;
- `403 APPLICATION_ACCESS_DENIED` — App is not enabled or Employee has no effective App entry grant;
- `429 AUTH_RATE_LIMITED` — too many login attempts for the current logical login key; current response includes `Retry-After: 60`;
- `503 AUTH_RATE_LIMITER_UNAVAILABLE` — deployment is missing its required login-protection binding;
- `401 SESSION_INVALID` — session missing, malformed, expired, revoked or no longer authorized;
- `404 NOT_FOUND` — unsupported endpoint;
- `500 IDENTITY_REQUEST_FAILED` — Identity runtime failure.

## Credential compatibility

New CYCloud Identity credentials use the data-driven algorithm key `scrypt` and the verifier form:

```text
scrypt$16384$8$1$<16-byte-salt-hex>$<32-byte-digest-hex>
```

Current parameters are N=16,384, r=8, p=1 with a random 16-byte salt. The password policy is 8 through 16 Unicode characters. Plaintext passwords never enter D1, Git, Audit or backup metadata.

The Worker may verify legacy `pbkdf2-sha256` verifiers only when their iteration count is within the Cloudflare production ceiling of 100,000. Higher-iteration PBKDF2 credentials must be migrated through a reviewed rehash/reset flow rather than silently weakening or mislabelling the stored algorithm.

The schema keeps `algorithm` data-driven so later reviewed password-algorithm upgrades do not require replacing the account model. New credential generation must always store the actual algorithm used; algorithm labels may not be reused for a different KDF.

## Session compatibility

The current opaque token is 32 random bytes represented as a prefixed lowercase-hex string. Its exact representation is an Identity implementation detail. Consumers must treat the token as opaque and must not parse role, Workspace or Employee information from it.

Default session TTL is 8 hours, with runtime configuration constrained to reviewed bounds. Credential change/reset invalidates older sessions through `credential_version` mismatch without requiring heartbeat writes.

## Consumer migration rule

A consumer App should depend on this contract through its own provider/adapter boundary. It must not:

- read the Identity D1 directly;
- copy credential/OTP/recovery tables into its own database;
- persist credential verifiers returned from another service;
- hard-code actual Workspace IDs or access matrices in Public source;
- infer Super Admin authority from an editable field other than the protected provider authority signal;
- reconstruct a permanent role model from legacy Identity Group names/mappings;
- treat legacy `applicationRoleKey` compatibility projection as the forward source of Workspace role once the three-role migration begins.

App-specific module/business permissions remain inside each App after Identity authenticates the principal. CY Web's mandatory core entry and multi-module exception are defined in `ROLE_AND_ACCESS_MODEL.md`.
