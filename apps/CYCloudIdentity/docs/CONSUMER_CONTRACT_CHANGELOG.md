# CYID Consumer Contract Changelog

This changelog tracks consumer-visible CYCloud Identity contract revisions. It is a technical compatibility record, not a fourth permanent-rule layer.

## 1.1.0 — 2026-11-19

CYID Consumer Impact: **BACKWARD_COMPATIBLE**. 新增選用的單一登入入口（§21，`AUTH_CONTRACT.md` §13，設計 `SSO_PORTAL_DESIGN.md` 第 1 階段）：`POST /v1/identity/sso/handoff/issue`（只限 core account portal 帶自己的 Session）與 `POST /v1/identity/sso/handoff/redeem`（目標 App 以自己的 binding 兌換成新的 App Session，回應形狀同 login）。交換碼 32 bytes、只存 SHA-256、60 秒、單次、綁定 App；兌換時重新檢查員工、Workspace、App、Application Access、憑證版本與入口 Session；所有兌換失敗一律 `SSO_HANDOFF_INVALID`，失敗次數依 App 限制。§17.1 登記接口新增選填 `ssoReturnUrl`（https、網域、無帳密／port／query／fragment，正規化儲存、完整字串比對），回應與管理 snapshot 新增對應欄位，新錯誤碼 `INVALID_SSO_RETURN_URL`、`SSO_RETURN_URL_SHARED_APPLICATION`。Provider 0.4.0 新增 migration `0009_sso_handoff.sql`。未採用的 consumer 不需變更；最低相容版本維持 1.0.0。CY Web 須同步 canonical 鏡像（`CONSUMER_SYNC_MANIFEST.json`）後才進行第 2 階段。

## 1.0.6 — 2026-10-15

CYID Consumer Impact: **BACKWARD_COMPATIBLE**. Add optional private `ConsumerNotifications.notifySuperAdmin` RPC (§20) for fixed generic backup-health alerts. Current Super Admin recipient resolution, Email transport and budgets stay provider-owned; backup/incident state stays app-owned. No public route, arbitrary recipient/body, Session, permission grant or business-data storage is added. Provider 0.3.10 introduces a bounded delivery slot migration; existing consumers and minimum 1.0.0 remain supported.

## 1.0.5 — 2026-10-15

CYID Consumer Impact: **BACKWARD_COMPATIBLE**. Add optional admin snapshot testing-account deletion capability and temporary core Super Admin deletion of verified non-self/non-Super-Admin Employees through the existing DELETE route (§17.2). Create/delete lifecycle audit is temporarily suppressed as explicitly requested; other audit and existing history remain. Existing pending-only consumers and all login/resolve/logout contracts remain supported; minimum stays 1.0.0. Provider source 0.3.9 includes the existing 0.3.8 clickable Email template and correct serviceVersion declaration. Actual deployment/Email acceptance and restoration before first business release are tracked in project TODO.

## 1.0.4 — 2026-10-12

Consumer Impact: **BACKWARD_COMPATIBLE**. Add optional core-portal Super Admin application registration/enablement (§17.1). The current authenticated Workspace is retained; catalog collisions/global disablement/core application changes fail closed. Transactional current-authority checks and audit cover registration, with no automatic Employee grants, new Workspace, business-data mutation or bootstrap replay. Provider source is 0.3.7 Build 0; minimum remains 1.0.0. Existing Fetcher/RPC consumers need no changes. CY Web must synchronize canonical mirrors and adopt 1.0.4 before exposing the new operation; actual development registration, consumer bindings and production approval remain separate gates.

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
