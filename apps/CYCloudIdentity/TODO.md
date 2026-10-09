# CYCloud Identity TODO

> 本文件只記錄 **current implementation status 與下一步**；不是永久規則來源。永久規則讀 `PROJECT_RULES.md`，產品/API/UI contract 讀 `README.md` 所列 active documents。

## Consumer integration readiness review — 2026-10-10

This dated section records **GitHub main source observations**, not a new consumer contract, a Cloudflare live binding inspection, a production acceptance, or authorization to modify runtime/deploy resources. Earlier 2026-10-01 evidence below remains historical.

- Confirmed code paths: CY Web `worker/identity/cycloud-identity-adapter.ts` / `provider-transport.ts` and CYACCweb `src/identity-adapter.js` both invoke `env.IDENTITY.fetch()` over the private `IDENTITY` Cloudflare Service Binding. Both use CYID `/v1/identity/login`, `/v1/identity/session/resolve`, `/v1/identity/logout`, Bearer Session transport and `x-identity-application`. Consumers do **not** bind directly to CYID D1 or mint their own parallel Identity Session.
- Consumer declarations observed: CY Web `CYID_CONSUMER_VERSION=1.0.2`; CYACCweb `CYID_CONSUMER_VERSION=1.0.1`; CYID current contract `1.0.2`, minimum `1.0.0`. Different declaration revisions are supported by the current compatibility window; identical adapter source is not required.
- Reviewed app-specific differences (not a contract change): CY Web strict Principal shape/authority invariant validation versus looser CYACC boolean/version normalization; CYACC immediate fresh resolve after login versus CY Web use of issued Session until next protected request; CY Web five-second deadline covers response body, whereas CYACC's `fetch()` race primarily bounds response arrival; CY Web `SameSite=Strict` versus CYACC `SameSite=Lax` plus `Expires` for documented Tablet/Safari navigation compatibility.
- Logout observation: CYACC's Worker `handleLogout` sends HTTP 200/`ok: true` and clears the cookie even when provider revocation was not confirmed, with `providerRevoked: false`; its browser logout handler navigates to `/login` in `finally` even if the request fails entirely. This is a status/browser-state issue requiring app-local remediation and failure-path tests, **not** evidence that the current provider lacks Session revocation. CY Web currently classifies any provider logout response below 500 as successful; consider explicit acknowledgement validation during consumer work.
- Provider code independently resolves app-scoped Session from its D1 and checks current Employee, Workspace, Credential, Application and App Access authority. The source review found no alternative consumer Identity authority. **Not yet verified**: current production Worker settings/Service Binding target parity, provider production D1 target, real usage and CPU metrics, full device/browser/Email acceptance. GitHub templates and historical workflow passes are not live Cloudflare attestation.
- CYInvoice existing Cloud Worker still has Built-in Cloud Employee/Device authority and is **not** yet a completed CYID consumer. Its planned `CyIdIdentityProvider` (or equivalent) must preserve Device/Workspace/local/offline ownership and avoid dual Employee/Credential authority. CYERPAutoInput is an early Windows/Go ERP automation prototype, currently not a CYID consumer. The user has **not selected** which desktop application is next.
- Resource planning direction: do not assume a separate remote D1 or permanent development deployment per lightweight desktop app. Evaluate shared small-data D1/Worker for compatible, non-identity domain data with explicit per-app ownership/migrations/access checks; keep CYID credential/Session authority isolated. Use local D1 and synthetic data where enough; use isolated cloud integration tests when real binding or authority migration demands them. Assess quotas from actual account-level metrics before paid-tier decisions. Do not assert that current free-tier headroom has been measured.

### Next consumer preparation (implementation is not started)

1. [ ] Identify next target app explicitly and read its `PROJECT_RULES.md`, current runtime/desktop architecture and existing Identity boundary; do not assume CYInvoice/CYERPAutoInput are interchangeable.
2. [ ] Confirm CYID production `IDENTITY` target, registered Application ID/Workspace mapping, supported `CYID_CONSUMER_VERSION`, exact consumer contract baseline and app-specific role/access behavior **through read-only runtime inspection** before any cutover.
3. [ ] Design one consumer-owned transport/identity adapter; keep all Employee/Credential/Role/App Access/Session authority in CYID. For desktop apps, define secure Token storage, execution-time versus ongoing login, real device identity and offline/reconnect behavior before implementation.
4. [ ] Choose minimum infrastructure: no new D1 if no cloud business data; consider shared lightweight D1 if appropriate; do not mix CYID D1 with app data. Local-first development and scoped isolated Cloudflare tests as needed.
5. [ ] Add contract-version gate, source/Worker tests, denied access, immediate revoke/role update, timeout/unavailable fail-closed, logout, credential/token leakage and app-specific real-device acceptance; only then propose production rollout for separate authorization.
6. [ ] Track existing consumer cleanup in their own workstreams: CYACC Principal response validation and logout failure semantics, CY Web logout acknowledgement handling and any bounded-response differences. No consumer runtime change is included in this documentation update.
7. [ ] Respect user-deferred real Email acceptance; preserve open acceptance gates without inventing results or demanding a mailbox for unrelated engineering work.

## Current checkpoint — 2026-10-01

- Current source release is **CYCloudIdentity 0.3.5 Build 0**；單一 Login/Employee handler、共用 Email budget、Session resolver 與 public legacy API/field retirement 已完成。**Development 0.3.5 Build 0 已部署並驗證**，run `36821423383`；source 與部署證據分開記錄。
- CY Web coordinated source release **0.7.0 Build 0** adopts contract 1.0.2 and completes WorkLog/Settings/Audit Worker → D1 transport. Provider production release must wait for deployed core consumer health to confirm 1.0.2 adoption; no CY Web production business-data rollout is implied.
- 0.2 direct Workspace Role / Identity Admin / direct App Access model 維持不變；0.3 的主要新增是新 Employee 首次 Email 驗證 credential flow。
- 新 Employee 建立後，CYID 自動寄出 **Email 驗證**郵件與 8 字元一次性首次登入密碼。首次登入密碼：
  - 只允許 CY Web core account application；
  - 24 小時 expiry；
  - 真正 single-use，成功驗證一次後不得再換第二張 ticket；
  - 不建立 normal Identity Session。
- 成功驗證首次登入密碼後，只簽發 10 分鐘的 opaque first-login ticket。ticket 不是 Session，normal session resolve 必須拒絕。
- first-login completion 建立正式 scrypt credential、完成 Email verification / durable first lifecycle、刪除 temporary credential/ticket，回傳 `reloginRequired: true`，**不發 normal Session**。
- 使用者必須回到 CY Web 一般登入頁，使用剛設定的正式密碼重新登入後，才會得到一般 CYID Session。
- `重寄驗證 Email`、pending Email 修改都會先輪替 temporary credential 並使舊密碼/ticket 立即失效；pending Email 修改不受舊信 resend cooldown 阻擋。
- Email delivery / Email-budget reservation 失敗時，不留下「未寄出但仍可登入」的新 temporary credential；Employee 保持 Email 未驗證並可由管理員重寄。
- Provider local acceptance 已證明：expiry、single-use、core-app-only、ticket-not-session、completion-no-session、ticket replay rejection、explicit re-login、resend/edit invalidation。
- CYID 0.3 development deploy 已成功完成 remote migration `0007_initial_email_password.sql`、Worker deploy 與 Identity secret configuration。
- Controlled real Email/browser lifecycle 驗收依使用者目前條件暫緩；這不重開已定案 contract，但仍是 production 前必要 acceptance。
- CYID shared consumer contract 已正式版本化為 **Consumer Contract 1.0.2 / Minimum Compatible 1.0.0**；所有完成接入的 consumer 必須宣告自己的 `CYID_CONSUMER_VERSION` 並維持在支援窗內。
- CYAccountingWeb（CYACCweb）handoff 已收斂為 app-specific migration guide：`docs/consumers/CYACC_INTEGRATION_HANDOFF.md`；共同 Role / Session / App Access / first-login / recovery 規範只讀 `docs/CONSUMER_INTEGRATION_STANDARD.md`。
- CYACC **V0.21.6 Build 1 / Draft PR #243** isolated development live acceptance run #96 已通過：USER login/read-only/Excel、Role change Session invalidation、ADMIN isolated write、App Access revoke/restore + Session invalidation、logout。Password Recovery Email/browser、Tablet 真機與 production cutover 仍為獨立 gate。
- Cross-repository contract mirror 已定義 manifest + exact-sync 規則：CY Web 等外部 repo 必須鏡像 manifest 所列 7 個 artifacts 並在 governance/CI/deploy 前 byte-compare；同 repo consumer 直接讀 canonical files。
- **CYID production provisioning 已獲批准並完成**：`deploy/cycloudidentity-production` at `0e9dc3342cd581bf41090012aae0759bce026329`，Production Provisioning run #12 成功，包含獨立 Worker/D1、durable authority continuity、CYACC registry/Workspace enablement、runtime binding 與 Session resolve/logout/revoke probe；不是 development provider。該 deployment workflow 尚未整合到 main，source VERSION/BUILD 未變。
- Production probe 使用短效合成 Session，並非真實密碼/Email/browser 驗收；這些 acceptance 與 backup/restore 尚未因此通過。CYACC 自己的正式部署/驗收由 CYACC 工作線追蹤。
- **相容層與部署檢查完成**：讀 `docs/COMPATIBILITY_REVIEW.md`。已確認 obsolete Group API/projections、兩份 Session authority 檢查、舊不可達 lifecycle handlers 與 initial-access wrapper；第一批 source 清理已完成：resolve API 與 management/self-service 共用 `resolveIdentitySession`；移除 6 個未 dispatch 的 legacy lifecycle/admin handlers。Provider Group endpoints／public fields、initial-access wrapper 與 Email budget 重複尚待下一批協調清理。
- **Production provisioning replay 已封住**：PR #254 直接在既有 production deployment branch 退休一次性 workflow；main 保存相同 inert gate。重跑只回 retirement error，不持有 Secrets，也沒有 continuity／migrations／deploy。日常 production deployment path 已建立於 `cycloudidentity-production-deploy.yml`：只接受 main 手動觸發，讀既有正式 bindings，consumer readiness 與 dry-run 通過後只做 forward migration/source deploy，不重播 authority。不得直接把含舊 consumer baseline 的整個 deployment branch 合入 main。
- Backup rollout / restore acceptance 與 CY Web production business-data rollout 未完成。

## Documentation consolidation

- `PROJECT_RULES.md` 是唯一 project-level 永久規則來源。
- `docs/CONSUMER_INTEGRATION_STANDARD.md` 保存所有 CYID consumer 的 shared technical contract；`CONSUMER_CONTRACT_VERSION` / `CONSUMER_MIN_COMPATIBLE_VERSION` 保存 machine-readable compatibility window。
- `docs/CONSUMER_CONTRACT_CHANGELOG.md` 保存 consumer-visible contract revision history。
- `docs/ROLE_AND_ACCESS_MODEL.md` 保存 Role / Identity Admin / App Access / lifecycle product contract。
- `docs/AUTH_CONTRACT.md` 保存 login/session/first-login/Email verification consumer contract。
- `docs/UI_ACCESS.md` 保存 account-management UI terminology、action 與 authority matrix。
- `docs/ARCHITECTURE.md` 保存 technical architecture / ownership / migration boundary。
- `docs/OTP_SECURITY.md` 保存 OTP/security-policy contract。
- `docs/consumers/CYACC_INTEGRATION_HANDOFF.md` 是 CYACC implementation guide；若與 canonical contract 衝突，以 canonical contract 為準。
- `TODO.md` 是唯一 current status / next-work tracker。
- Dated conversation handoff 與 obsolete Application Role Mapping 不留在 active tree；歷史用 Git history 追溯。

## Active next sequence

1. [x] 建立獨立 CYCloud Identity authority 與 initial Workspace/Employee/Credential/Session/OTP/Audit foundation。
2. [x] 完成並 development-deploy 0.2 direct Role / Identity Admin / direct App Access migration。
3. [x] 修正 0.2.1 first-lifecycle Email verified durability race並 development-deploy。
4. [x] 收斂 active documentation，移除 dated handoff 與 obsolete role-mapping 文件。
5. [x] 實作首次登入密碼 expiry、single-use、resend/pending-email-change invalidation。
6. [x] first-login complete 改為只回 completion + `reloginRequired`，不建立 normal Identity Session。
7. [x] 對外 UI/API wording 收斂為 Email 驗證，保留必要 legacy technical alias 但不再作產品語意。
8. [x] Merge CYID 0.3.0 to `main` 並經 `deploy/cycloudidentity-development` 成功部署 development。
9. [x] 驗證 0.3 migration、Worker bundle、auth-core、local login/first-login acceptance 與 development deployment health。
10. [x] CY Web 0.3 consumer source完成並 development-deploy：單一登入入口、HttpOnly first-login ticket transport、forced permanent-password screen、完成後回登入。
11. [ ] 建立 controlled USER，實際瀏覽器驗收：建立 -> 收驗證 Email -> 首次登入密碼 -> 設正式密碼 -> 回登入 -> 正式密碼登入。
12. [ ] 實際驗收首次登入密碼 expiry、重寄驗證 Email、pending Email 修改、舊 credential 失效與 delivery-failure recovery。
13. [ ] 實際驗收既有 Super Admin permanent-password login/F5/session resolve，並完成 USER self-service、正常 ADMIN、Identity Admin、Super Admin 權限矩陣。
14. [ ] 驗收 role/App Access change session invalidation、literal expired normal Session、forgot-password、own Email change 與 controlled Super Admin transfer。**CYACC consumer 子矩陣的 Role/App Access Session invalidation 已於 run #96 通過；其餘項目仍未完成。**
15. [x] 建立 governed CYID Consumer Integration Standard + contract compatibility versions + PR consumer-impact gate；再加入 cross-repository sync manifest / exact mirror requirement；CYACC handoff 收斂為 app-specific migration guide。
16. [x] CYAccountingWeb 已採用 `CYID_CONSUMER_VERSION=1.0.1`，V0.21.6 Build 1 source + isolated development live Role/App Access/Session acceptance 已完成；real Email/browser lifecycle、Tablet 與 production cutover 仍是後續 gate。
17. [ ] 在 CYInvoice 工作線適合的接入點，以同一 consumer standard 建立其 app-specific migration handoff；Device/local/offline 邊界仍由 CYInvoice 自己管理。
18. [ ] 完成 low-frequency backup + restore acceptance；production provisioning 已明確批准並由 run #12 完成，這不代表 backup/restore 或 broader Email/browser acceptance 已通過。
19. [x] 完成 CYWEB/CYID 相容層與 production workflow 檢查，結果在 `docs/COMPATIBILITY_REVIEW.md`。
20. [x] 收斂 routine production deployment：既有 production settings/DB isolation、deployed core consumer 1.0.2 gate、dry-run、forward schema/source only；本批不執行尚未滿足 consumer/real Email acceptance 的 production release。
21. [x] 移除不可達 handlers，統一 Session resolver、Login/Employee boundary、Email budget，退休 Group API/fields 與初次寄信 aliases；CY Web 0.7.0 coordinated consumer source 採 1.0.2。

## Explicitly deferred

- Non-ADMIN HR Identity capability，直到有真實 HR workflow。
- Universal fine-grained business permission catalog inside CYID。
- CYInvoice Device pairing / Device Token / Local->Cloud / Windows offline credential cache。
- 尚未批准的其他 production rollout、paid Cloudflare/Email/Google Cloud plans。

## Reading order for current work

`PROJECT_RULES.md` -> `README.md` -> relevant active contract -> this `TODO.md` -> current source/tests/migrations。

不讀 dated handoff 作 current state；需要歷史 checkpoint 時看 Git history。

## 0.3.4 source cleanup validation

- Consumer Impact: **NONE**；Consumer Contract remains 1.0.1 / minimum 1.0.0。Public response fields 與仍在 provider 的 Group APIs 未在本批改動。
- Typecheck 與 25 個 Node tests 通過，含 Session resolve/guard parity、revoked/expired/disabled/credential-version/App Access cases。
- Local Wrangler acceptance 被執行環境 `uv_interface_addresses` 錯誤阻擋；合併 gate 使用 GitHub Actions 的 existing auth + first-login Worker/D1 acceptance，不以 unit tests 替代。
- 本 patch 未更新 development／production Worker；runtime deploy acceptance 與下一批移除 provider public legacy fields 分開追蹤。

## 0.3.5 cleanup verification

- Consumer Impact: **CONSUMER_UPDATE_REQUIRED** for callers of retired Group/role-mode APIs, `groupKeys`/`applicationRoleKey`, `activationDelivery` and `/activation/resend`. Canonical re-send is `/email-verification/resend-initial`. CYACC's direct 1.0.1 fields/routes are unaffected; minimum remains 1.0.0.
- Typecheck and 38 Node tests pass, including obsolete-route rejection without DB access, shared Email-budget rollback/settlement and strict existing production configuration rendering. Auth + first-login real Worker/D1 acceptance remain required CI gates.
- Physical historical tables/migrations remain. Bounded PBKDF2 verification remains for credential continuity; all newly written credentials use scrypt. No credentials were exported to establish this change.
- Routine production workflow has no authority import/Employee repair, resource creation, secret replacement or synthetic Session writes. Real Email/browser, backup/restore and CY Web production cutover remain deferred separately.

## Development release acceptance — 2026-10-01

- Formal CYID source `cca89e30eec2e7df6938bb37736c520af9db8197`; deployment commit `26ae3424df2ee2c57a9877682c4bb51dd593db58` uses the exact formal-main tree while preserving the old development branch history. It does not merge old one-time provisioning code into runtime.
- [Development Deploy run 36821423383](https://github.com/simonliu1118-byte/CYapps/actions/runs/36821423383) passed migrations, bundle/source deployment, existing secret configuration, actual development Worker/D1 binding isolation, health serviceVersion 0.3.5 and unregistered Session rejection. No account/Session was created by the verification.
- CY Web 0.7.0 / consumer 1.0.2 deployed through [run 36821410198](https://github.com/simonliu1118-byte/chihyuan-web/actions/runs/36821410198), with actual canonical/workers.dev D1 health/version/consumer readback and provider Session rejection.
- Routine production release additionally requires protected `CYID_PRODUCTION_CORE_CONSUMER_WORKER_NAME` and actual core Worker `IDENTITY` binding to the production provider plus the production core Application ID and consumer declaration. A development health marker cannot satisfy this target check. The prerequisite is not provisioned by this work.
- TypeScript and 44 Node tests pass after the production-binding readiness guard was added; regression cases reject a healthy development binding, wrong Application/consumer declaration and unhealthy D1.
- Production source release remains unexecuted. Real Email/browser/device acceptance, backup/restore and CY Web production business-data rollout remain pending.

## CY Web real browser checkpoint — 2026-10-01

- The deployed CY Web/CYID development pair accepted permanent-password SUPER_ADMIN login in a dedicated cloud browser; full reload re-resolved the Session and returned to the protected business UI. CY Web Identity management, Settings/Audit and all six business page reads completed.
- CY Web synthetic Customer create/read-after-reload passed; this establishes business persistence for that operation only, not every lifecycle or role. No real Employee identifiers, credentials or opaque tokens are recorded in Public source.
- Real Email/new USER/initial-password/credential-setting journey remains pending. A controlled inbox not already assigned in the development Workspace must be designated before creation; creation sends a real Email and grants mandatory core-App entry. No new Employee, Email send, Role/Access change, credential change or transfer was performed in this browser session.
- USER/normal ADMIN/Identity Admin real-browser matrix, resend/expiry/pending-email-edit/recovery, backup/restore and production rollout remain open. Product/consumer versions are unchanged; Consumer Impact: NONE.

## Durable pending tests — user deferred Email on 2026-10-01

The user explicitly deferred real Email acceptance and asked that the checklist remain in Git across conversations. Continue engineering work without requiring an inbox. Automated/source acceptance does not close these real-browser/provider items.

- [ ] Controlled new USER Email receipt, initial password, ticket-only first login, user-entered permanent password and explicit fresh login with no automatic Session.
- [ ] Expired initial credential, resend, pending Email edit, invalidation of prior credential/tickets, send failure and retry.
- [ ] Activated Employee forced Email recovery/re-verification, forgot-password and own Email change.
- [ ] Protected Super Admin transfer with credential/OTP/user confirmation.
- [ ] USER/normal ADMIN/Identity Admin real-browser capability matrix; role/App Access/Session expiry and revocation. Current real-browser evidence is SUPER_ADMIN login/reload only.
- [ ] Backup/restore acceptance specific to CYID authority data; CY Web business-backup rehearsal does not back up CYID credentials or prove provider recovery.
- [ ] Production source release readiness and explicit authorization; existing provisioning success does not close Email/browser/restore gates.

Prerequisites for later Email tests: user-designated controlled inbox, explicit recipient/account-creation authority, secure login/OTP collection and user handoff for new permanent credential entry. No credentials or real recipient addresses belong in Public documents. CY Web's business backup target remains R2 + GCS under its own approved architecture; independent App/scope/credentials must be preserved.
