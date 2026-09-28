# CYCloud Identity — Authentication Contract

> Status: executable authentication contract for Shared Identity consumers. This document contains generic protocol only; actual Workspace/Application/Employee runtime data is not stored in Public source.

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

Successful response contains:

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
    "credentialVersion": 1,
    "employeeRevision": 1
  },
  "session": {
    "token": "<opaque one-time-returned session token>",
    "expiresAt": "<UTC ISO-8601 timestamp>"
  }
}
```

The caller must not persist the password. The raw session token is returned only on login and must not be logged.

Before credential/D1 work, the Worker applies the configured Cloudflare `LOGIN_RATE_LIMITER` to a SHA-256 key derived from Application + Workspace + Employee No. The deployed rate-limit binding is mandatory; if it is missing, authentication fails closed instead of running an unprotected password verifier.

Authentication requires all of the following:

- login rate limiter permits the attempt;
- Workspace is active;
- Employee exists in that Workspace and is enabled;
- credential algorithm is currently supported and password verifies;
- Application is active and enabled for that Workspace;
- effective Application Access exists through the protected Workspace highest authority, a direct Employee grant, or an active Identity Group grant.

Ordinary Identity Groups are data-driven. Consumers must not reconstruct a fixed `EMPLOYEE / ADMIN / SUPER_ADMIN` enum from `groupKeys`. Workspace highest-authority semantics use `isWorkspaceSuperAdmin` separately.

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
- current highest-authority/direct/group Application Access still permits entry.

Normal resolve is read-only and does not use sliding heartbeat writes.

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
- hard-code actual Workspace IDs or Application access matrices in Public source;
- infer highest Workspace authority from an editable Identity Group name.

App-specific module/business permissions remain inside each App after Identity authenticates the principal.
