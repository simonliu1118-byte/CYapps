# CYID Consumer Contract Changelog

This changelog tracks consumer-visible CYCloud Identity contract revisions. It is a technical compatibility record, not a fourth permanent-rule layer.

## 1.0.3 — 2026-10-10

Consumer Impact: **BACKWARD_COMPATIBLE**. Add the optional named private `ConsumerAuthoritySync.invalidateCache` RPC capability described in the canonical standard §11.1. It only invalidates supplied subordinate cache descriptions against current indexed authority; no positive grant, verifier export, Session, public route, cursor or persistent login is added. Provider source advances to 0.3.6 Build 0. Existing Fetcher login/resolve/logout consumers remain supported; minimum compatible stays 1.0.0. Production deployment and cross-repository canonical mirror synchronization remain separate gates.

## 1.0.2 — 2026-10-01

Consumer update required for obsolete Group projections/routes, compatibility role mode, initial delivery alias and re-send route. CY Web adopts direct fields and canonical Email verification routes before provider production deployment. CYACC direct 1.0.1 integration is unaffected; minimum remains 1.0.0. No migration deletes historical authority or Group data.

## 1.0.1 — 2026-09-30

Backward-compatible governance addition:

- define `CONSUMER_SYNC_MANIFEST.json` as the canonical cross-repository contract package membership;
- require cross-repository consumers to keep an exact synchronized mirror and validate it in governance/CI and before deployment;
- keep same-repository consumers on direct canonical reads rather than redundant copies;
- separate contract semantic versioning from document-byte synchronization so documentation-only corrections can sync without artificial contract-version bumps.

## 1.0.0 — 2026-09-30

Initial governed shared-consumer baseline, derived from CYID 0.3.x:

- Workspace Role is directly projected as `SUPER_ADMIN / ADMIN / USER`;
- Identity Admin is an ADMIN capability, not a fourth role;
- Application Access is separate from Role;
- normal consumers use permanent-password login, provider-owned app-scoped Session, resolve and logout;
- raw provider Session tokens remain server/HttpOnly-cookie transport data and are not consumer business data;
- new-Employee first Email verification / first-login password is restricted to the CY Web core account application;
- permanent password boundary is 8–16 Unicode characters;
- consumers keep domain/module/business authorization local unless explicitly promoted into CYID;
- consumer-specific migration notes belong in app handoffs and do not redefine the shared contract.
