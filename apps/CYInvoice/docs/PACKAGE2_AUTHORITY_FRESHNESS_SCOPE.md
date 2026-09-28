# CYInvoice Package 2 — Built-in Cloud Online Authority Freshness

Status: implementation working note for the stacked Package 2 branch. This is not a governance rule and does not authorize merge, deployment, tag, or Release.

## Scope

Package 2 fixes the reproduced stale Built-in Cloud Employee snapshot defect at execution-time authentication.

Required flow:

```text
Sensitive Employee authentication
  -> Built-in Cloud mode?
     -> yes: fetch current /v1/employee-authority/snapshot
        -> validate Workspace identity
        -> atomically replace protected local Cloud Employee cache
        -> authenticate supplied Employee No/password against the fresh snapshot
        -> authorize using fresh enabled/role
     -> transport unreachable or request timeout only:
        -> authenticate against last trusted protected Cloud cache
```

A Cloud HTTP/API response, invalid snapshot, invalid Device identity/token, Workspace mismatch, malformed response, or other integrity/authorization failure must not silently fall back to stale cache.

## Existing capability reused

No new Cloud endpoint is required. The existing `/v1/employee-authority/snapshot` endpoint authenticates the active Device/Workspace and reads the current D1 `cloud_employees` rows including current role, enabled state, credential verifier/version, and revision.

## Implementation boundary

- Put online refresh + offline fallback inside `BuiltInCloudIdentityProvider`.
- Keep Local provider behavior unchanged.
- Reuse the same provider refresh path for the existing startup/5-minute background Employee snapshot refresh.
- Preserve server-side current-authority re-auth for Cloud account mutations.
- Do not change credential format or export plaintext passwords.
- Do not implement Device revoke/reset or CY ID work here.

## Acceptance matrix

Automated:

- online current snapshot is fetched before Built-in Cloud authentication;
- newly-created current-authority Employee can authenticate without app restart/background wait;
- role/enabled/password changes in current snapshot take effect on next authentication;
- successful online refresh updates protected local cache;
- transport failure / timeout can use last trusted protected cache;
- 401/403/other Cloud API errors do not use stale cache;
- malformed or wrong-Workspace snapshots do not use stale cache;
- Local provider remains unchanged;
- Windows build/startup smoke/Cloud contract tests remain green.

Real-device follow-up after implementation integration/deployment is separately required for A/B Employee create/role/enabled/password/offline/reconnect behavior.

## Version classification

Package 2 is a new independent defect fix after validated Package 1: CYInvoice `2.6.8`, Build `0`. If Worker/API/storage source remains unchanged, Cloud stays `0.8.6 / API 1 / Schema 10`.
