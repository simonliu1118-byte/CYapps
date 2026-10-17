# CYCloud Identity TODO

> 本文件只記錄 **current implementation status 與下一步**；不是永久規則來源。永久規則讀 `PROJECT_RULES.md`，產品/API/UI contract 讀 `README.md` 所列 active documents。

## 測試期帳號刪除與 Audit 暫停 — 2026-10-15

- 使用者明確要求：為反覆測試新增／Email 驗證流程，暫時允許刪除已驗證帳號，並暫停新增／刪除帳號 Audit；一次性測試例外，不改永久 PROJECT_RULES。
- Source 0.3.9／consumer 1.0.5（minimum1.0.0）：沿用 DELETE route與snapshot；已驗證帳號僅由目前 core portal Super Admin刪除，保留自刪／Super Admin／跨Workspace禁止與transactional session/revision檢查。原credential／first-login material／Session／App Access cascade移除，相關OTP同transaction清除，其他consumer業務資料與history保留。
- 新增（含首次寄信結果）／刪除 Audit 暫停，既有Audit與其他事件保留。新employeeId不繼承舊帳號或App/Module Access。
- 本輪一併帶上既有0.3.8 HTML／text可點擊CYWEB驗證入口；使用者提供的舊信確實只有「請前往CY Web」文字而無URL。修正serviceVersion原本落後VERSION的宣告，正式部署後才算寄信程式已更新；不自動重寄真人Email。
- [x] Local TypeScript／95項Node回歸與實際本機Worker/D1新增→拒絕一般ADMIN刪已驗證→SA刪除→舊Session/密碼拒絕→同編號/Email重建→無繼承App Access／FK檢查通過；新增/刪除Audit為零、其他Audit保留，HTML/text初寄與重寄實際adapter pipeline通過（Email transport為stub）。
- [ ] exact-head CI／正式既有provider部署；不重播authority、不建test Worker。
- [ ] 受控真人新增→實際收信連結→驗證→新密碼登入→刪除→同編號／Email重建驗收；未操作真人帳號。
- [ ] **第一版業務上線前恢復**：`src/account-lifecycle-testing.ts` 的 allowVerifiedDeletion=false、recordCreateDeleteAudit=true，更新consumer契約／Web mirror並部署，驗收已驗證帳號不可刪、pending刪除及新增／刪除Audit正常；完成後收斂本暫時例外。

## Verification Email clickable CY Web link — 2026-10-13

- [x] CYID source 0.3.8: use existing runtime `ACCOUNT_PORTAL_URL` to include the CY Web root login address in both HTML-clickable and plaintext new-Employee / resend-initial Email; clarify that initial passwords cannot log into CYACCweb, that permanent password setting happens in CY Web, and that fresh login is required.
- [x] Validate URL before writing or rotating an initial credential; production URL must be HTTPS, without embedded credentials/query/fragment. No URL with password, token or employee data is sent.
- [ ] After PR CI and a **separately authorized** CYID deployment, read back the actual Provider `ACCOUNT_PORTAL_URL` and CY Web Service Binding to verify both address the same CYID authority; do not expose real IDs or credentials in Public logs.
- [ ] Controlled real Email/browser acceptance (new Employee + resend): received email HTML/text link, click-through, initial login, permanent password, confirmation read-back, fresh login, CYACC app access and error classification. Do not reissue actual employee credentials during this source-only change.
- **CYID Consumer Impact: NONE**; API/Session/Role/App Access/CYID consumer contract 1.0.4 unchanged. Existing CYACC automatic deployment and Cloudflare workers are not touched by this CYID-only PR.

## Current checkpoint — 2026-10-12

- Current source **CYCloudIdentity 0.3.7 Build 0／contract 1.0.4** 已由 #416 合併，八項 final-head CI 通過；development actual #158（38197480766）同已測 tree 升級成功，保留原 authority／history／settings／Secrets／七 migrations／FK。Production未部署此批；不自動登記App或授予員工權限。
- CYInvoice 原 development 已經 #420 exact-head CI／actual activate-cyid #42（38206372325）啟用 CYID；原 Invoice Workspace／Device Token hashes／資料／history／FK 保留，Provider 不變。原 Windows 已由使用者回報 Built-in continuity；live CYID 員工帳密、其餘 App Access／Role／private transport／AA／AB／AC 仍待驗收，詳見 Invoice 現行交接／TODO。不代表 production 或 Release。
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
- CYID shared consumer contract 已正式版本化為 **Consumer Contract 1.0.4 / Minimum Compatible 1.0.0**；所有完成接入的 consumer 必須宣告自己的 `CYID_CONSUMER_VERSION` 並維持在支援窗內。
- CYAccountingWeb（CYACCweb）handoff 已收斂為 app-specific migration guide：`docs/consumers/CYACC_INTEGRATION_HANDOFF.md`；共同 Role / Session / App Access / first-login / recovery 規範只讀 `docs/CONSUMER_INTEGRATION_STANDARD.md`。
- CYACC **V0.21.6 Build 1 / Draft PR #243** isolated development live acceptance run #96 已通過：USER login/read-only/Excel、Role change Session invalidation、ADMIN isolated write、App Access revoke/restore + Session invalidation、logout。Password Recovery Email/browser、Tablet 真機與 production cutover 仍為獨立 gate。
- Cross-repository contract mirror 已定義 manifest + exact-sync 規則：CY Web 等外部 repo 必須鏡像 manifest 所列 7 個 artifacts 並在 governance/CI/deploy 前 byte-compare；同 repo consumer 直接讀 canonical files。
- **CYID production provisioning 已獲批准並完成**：`deploy/cycloudidentity-production` at `0e9dc3342cd581bf41090012aae0759bce026329`，Production Provisioning run #12 成功，包含獨立 Worker/D1、durable authority continuity、CYACC registry/Workspace enablement、runtime binding 與 Session resolve/logout/revoke probe；不是 development provider。該 deployment workflow 尚未整合到 main，source VERSION/BUILD 未變。
- Production probe 使用短效合成 Session，並非真實密碼/Email/browser 驗收；這些 acceptance 與 backup/restore 尚未因此通過。CYACC 自己的正式部署/驗收由 CYACC 工作線追蹤。
- **相容層與部署檢查完成**：讀 `docs/COMPATIBILITY_REVIEW.md`。已確認 obsolete Group API/projections、兩份 Session authority 檢查、舊不可達 lifecycle handlers 與 initial-access wrapper；第一批 source 清理已完成：resolve API 與 management/self-service 共用 `resolveIdentitySession`；移除 6 個未 dispatch 的 legacy lifecycle/admin handlers。Provider Group endpoints／public fields、initial-access wrapper 與 Email budget 重複尚待下一批協調清理。
- **Production provisioning replay 已封住**：PR #254 直接在既有 production deployment branch 退休一次性 workflow；main 保存相同 inert gate。重跑只回 retirement error，不持有 Secrets，也沒有 continuity／migrations／deploy。日常 production deployment path 已建立於 `cycloudidentity-production-deploy.yml`：只接受 main 手動觸發，讀既有正式 bindings，consumer readiness 與 dry-run 通過後只做 forward migration/source deploy，不重播 authority。不得直接把含舊 consumer baseline 的整個 deployment branch 合入 main。
- Backup rollout / restore acceptance 與 CY Web production business-data rollout 未完成。

## Development upgrade preparation

- [x] CYInvoice Consumer/canonical 1.0.3 工程基準 PR #380 已合併到 main `59d63b6785fa028657c9a2fb13fc9515e497d95d`；精確功能 head `46723a89` 七項 CI 通過，無自動部署／正式切換。
- [x] 既有 development workflow 收斂為唯讀 preflight／保留原設定的 code-only upgrade；只讀 Provider 維運 D1 metadata、exact migration／FK／hashed authority／history 與 recovery reference，不重寫 Secrets／bootstrap、不跑 remote migration。deployment tooling 不修改產品／contract／VERSION／BUILD。
- [x] Deployment audit source 348185fb 四項 CI 通過並合併 #384；read-only development #123（38110828710）及實際 0.3.6 upgrade #124（38111211344）通過，部署 head 56c27685 與已測 source tree 相同。原七個 migration 無 pending／未寫 remote schema、FK 清潔、原 vars／bindings／Secret names／hashed authority metadata／history counts 保留，health 與 synthetic invalid Session 拒絕正常。恢復參考 UTC checkpoint 2026-10-11T04:20:13.295Z；無 bootstrap／Secret replacement／D1 restore。private RPC／真實 Employee 與原 Device 另行驗收。
- [x] CY Web governance PR #145 合併 main a0cb4675；七個 manifest mirror 與 canonical 1.0.3 bytes 相同，CY Web consumer 維持相容 1.0.2，沒有 Web runtime／部署變更。

- [x] 歷史 #19／#21／#23 原 consumer Built-in／D1／Workspace／Device token hashes／history／FK 安全核對；測試版本無D1、未部署、已過期。DELETE403→404／10339未解決，不算transport成功。歷史source/run證據集中 docs/DEVELOPMENT_DEPLOYMENT.md。
- [x] 依使用者最新定案，移除 remote probe／cleanup controller、測試Worker與workflow modes／branches；保留 expired history，停止重試／擴權。CI只作本地synthetic驗證，不上傳測試程式。
- [x] CYInvoice exact-active-source真實business部署：#402六CI全通過並合併；actual #25（38134710138）原Cloud0.9.2／storage13／Built-in／Workspace／Device hashes／history／FK／routes保留，before/after expired probe仍1個；無test upload／DELETE／Secret replacement／authority switch。精確source/tree/recovery evidence見docs/DEVELOPMENT_DEPLOYMENT.md。三個old branch refs已停放到無probe入口的#402 tree。
- [x] CYInvoice 既有部署 owner 新增 prepare-cyid source，Built-in／原 Workspace 保留，僅 approved bindings／scope 準備；source／本地回歸完成，無 remote test upload／CYID D1／authority switch。技術操作見 CYInvoice Cloud README。
- [x] 共通 deployment input／Invoice scope resolver follow-up source：四個 workflows 共用 Provider-name Secret；Invoice 只讀 Provider active Worker metadata／original business D1，不讀 Identity D1、不借 CYACC scope，不重建 Workspace；Provider drift 阻擋。本地回歸通過，CI 依 PR。
- [x] 共通 Secret 已設定；#413 七 CI 通過並合併，唯讀 Invoice #29／CYACC main #608 實際通過，原 Invoice Workspace／Device hashes／history／Builtin保留，無 Identity D1／Invoice upload／authority switch。來源／run見 DEVELOPMENT_DEPLOYMENT。
- [x] 使用者完成中央 CYInvoice 登記／目前身分 Workspace 啟用並配置 Provider 兩項 Invoice runtime scope；Invoice 唯讀 #38 configured=true，prepare-cyid #39 通過兩個私有 bindings／三個 approved vars readback。原 Invoice Workspace／Device／Token hashes／history／FK 保留，Builtin／CYID false；Consumer 不讀 CYID D1。詳細證據見 docs/DEVELOPMENT_DEPLOYMENT.md。
- [x] 官方active與retired部署入口已同步Secret並read-back：Provider development #153／preflight #154通過；Invoice #31於Provider切換時在upload前阻擋，等待Provider完成的新snapshot #32通過，原Workspace／Device hashes／history／Builtin保留、無test upload／authority switch。Exact source／refs／run evidence見現行交接及Provider DEVELOPMENT_DEPLOYMENT。
- [x] 使用者已刪除 development 同名 CYID_WORKER_NAME Variable 並保留 Secret；唯讀 Invoice #34（38195315134）通過，Cloud0.9.2／storage13／原 Workspace／Device／Builtin／FK 保留，Provider scope 仍 false，無 upload／Identity D1 access。
- [ ] 實際private Fetcher／named RPC與App Access／Role／原Windows裝置驗收，使用真實consumer功能；不再增加遠端probe入口。配對／範圍／Free量測與正式切換仍獨立gate。
- [ ] Temporary Workers Admin移除由使用者確認；不增加權限。刪除10339留歷史／非目前部署阻塞。

## Application registration / CYInvoice next step

- Source 0.3.7 / contract 1.0.4 adds optional core-portal Super Admin registration and Workspace enablement through the existing role-admin owner. Same current Session scope, immutable existing catalog metadata, protected core app, transactional authority recheck/audit; no Employee grant or consumer Workspace mutation.
- [x] #416 final head cbae06a0／tree 8fe98f7c 八項 CI（含實際 Worker/D1 登記／登入與 Windows）通過，合併 d3411e2；development #158（38197480766）source 3eac1a22／同 tree 升級成功，原 authority／history／settings／Secrets／七 migrations／FK 保留。
- [x] CY Web #146 source 8aad63d9／tree a02fe9d4 通過 Governance244／Runtime160／deploy-contract137，合併2dfd374e；actual development #138（38197908123）source5325ef77／同 tree 完成0.7.23／consumer1.0.4及canonical／workers.dev版本、D1、invalid Session驗收。七項canonical鏡像exact-sync通過。
- [x] 使用者以既有 core-portal Super Admin 完成 CYInvoice 真人中央登記／目前 Workspace 啟用，系統使用權畫面已出現 CYInvoice；不自動授予一般員工 App Access。取消／dirty guard／busy／行動裝置及一般員工授權仍待實機驗收。
- [ ] Approved Invoice App/scope metadata, prepare-cyid, actual Employee/App Access/private transport/original Windows acceptance and separately approved cutover.

## Optional consumer invalidation source

- [x] Contract 1.0.3 / source 0.3.6 adds the private named RPC entrypoint; indexed current-state comparison, no positive grant/Session/verifier export. Canonical semantics are in CONSUMER_INTEGRATION_STANDARD §11.1; impact BACKWARD_COMPATIBLE, minimum remains 1.0.0.
- [x] Real-provider synthetic D1 regression through CYInvoice covers Role, enabled, App Access, credential version, deleted ID, unavailable application and revoke during provider outage; existing provider test suite remains required.
- [ ] Isolated staging with the named entrypoint binding, positive authorization and actual consumer usage/cost measurement. Development 0.3.6 is deployed (#124); transport/deny proof alone does not establish real consumer authorization.
- [x] CY Web cross-repository manifest byte-sync 1.0.3（#145）；同 repo consumers 直接讀 canonical，不新增 mirror。
- [ ] Coordinated private binding／consumer acceptance and production approval. Existing CY Web/CYACC compatibility is retained; this task does not modify their runtime.

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
