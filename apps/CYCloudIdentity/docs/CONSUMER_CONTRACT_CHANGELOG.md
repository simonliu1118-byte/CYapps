# CYID Consumer Contract Changelog

This changelog tracks consumer-visible CYCloud Identity contract revisions. It is a technical compatibility record, not a fourth permanent-rule layer.

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
