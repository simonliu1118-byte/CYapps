# CYApps Governance Changelog

## 2.3.31 — 2026/10/06

- REPO_POLICY §3：本 repository 的 Actions Artifact 一律保留 3 天（使用者指定，取代共通規則 14 天預設），每個 upload-artifact 步驟都須明確寫出；`cyerp-auto-input-build.yml`、`cyinvoice-cloud.yml` 由 14 天改為 3 天。
- Governance Check 新增檢查：upload-artifact 步驟數須與 `retention-days: 3` 一致。

## 2.3.30 — 2026/10/06

- CYERPAutoInput `PROJECT_RULES.md`：使用者核准批次輸入的失敗隔離方式。仍不自動操作「修改」；「取消」只允許用於放棄 CYERPAutoInput 自行新增、輸入途中失敗的單據，且必須先確認 ERP 仍在該張新增單據的輸入狀態，否則停止整批等待人工處理。Esc 緊急停止仍不得替使用者按「取消」。

## 2.3.29 — 2026/10/06

- Public package scanner：Google refresh token pattern 改為只接受 base64url 字元（`1//[A-Za-z0-9_-]{20,}`），不再接受 `.` 與 `/`。原 pattern 會把 OpenCvSharp4.runtime.win 官方 OpenCV native DLL 內的二進位資料表誤判為 token，阻擋 CYERPAutoInput single-file package。
- 新增對應 regression：OpenCV 樣式資料不得阻擋；既有 UTF-8／UTF-16 token 偵測、NUL 拼接誤判與 pinned dependency 檢查維持不變。

## 2.3.28 — 2026/10/04

- Sync AITeam Common Rules 2.8.0 Canonical Owner / Replacement / Architecture Exception governance.
- CYAccountingWeb `PROJECT_RULES.md` now records the current backend/frontend canonical owner map for Identity, transactions, ledger rendering/lifecycle/toolbar, settings, adaptive presentation, opening balances, account lifecycle, backup and desktop migration.
- CYACC architecture changes must replace/remove old paths instead of silently adding second owners. Existing legacy observer/wrapper/retry paths are not grandfathered; real API/schema/file-format/version contracts remain valid.
## 2.3.27 — 2026/10/02

- CYAccountingWeb opening balances default to automatic carry-forward through one calculation service. Manual baseline exceptions require reason, append-only effective old/new value audit and CYID actor, and wait for server confirmation.
- SUPER_ADMIN permanent deletion uses archived state, no transactions and latest effective zero opening; preserve historical financial/audit rows and reserve audited names against reuse.

## 2.3.26 — 2026/10/02

- CYAccountingWeb adopts optimistic UI as the default mutation policy for low/medium-risk, safely reversible actions: render the expected result immediately, persist through the canonical API in the background, and rollback with a clear error if persistence fails. High-risk or non-safely-reversible actions remain server-confirmed.

## 2.3.25 — 2026/10/01

- Production consumer readiness also reads the protected existing core Worker binding/application/consumer declaration. A development consumer health marker alone cannot authorize provider retirement. No production resource/configuration was changed.

## 2.3.24 — 2026/10/01

- Verify deployed CYID development Worker/D1 isolation, source version and invalid Session rejection after routine source deployment. No account/Session injection or authority replay.

## 2.3.23 — 2026/10/01

- Add main-only manual CYID production deployment with protected existing-target readback, strict DB/environment/binding isolation, deployed core-consumer adoption gate, dry-run before mutations, forward schema and source deployment only. No resource creation, development authority export/import, Employee repair, secret replacement or synthetic Session injection.
- Consumer 1.0.2 coordinates retired Group/role/initial-delivery aliases with CY Web; unaffected direct-principal consumers retain their existing supported declaration.

## 2.3.22 — 2026/10/01

- Retire completed CYID one-time production provisioning. The retired workflow has no credentials, checkout, resource creation, migrations, authority imports, or deployment steps; ordinary provider releases require a separate deployment path.

## 2.3.21 — 2026/09/30

- CYAccountingWeb adopts CYCloudIdentity Consumer Contract 1.0.1 as its sole Identity authority path; active runtime must not mint or resolve a local second Identity session after cutover.
- The first CYACC CYID authority cutover must be rollback-safe: active source stops using local `web_sessions`, but the physical legacy table is intentionally retained until development + production acceptance; its DROP is a later independent forward migration, never a migration-before-Worker-deploy step in the initial cutover.
- CYID App Access controls CYACC entry while CYACC owns accounting authorization: SUPER_ADMIN / ADMIN keep normal writable behavior, and USER with CYACC App Access is read-only except monthly Excel export; server-side enforcement is mandatory across Desktop / Tablet / Mobile.
- CYAccountingWeb login becomes a standalone server-gated entry instead of an overlay on the full accounting App. Browser Session keeps the Tablet-safe HttpOnly + Secure + SameSite=Lax + Expires navigation pattern until real-device acceptance proves stricter settings safe.
- CYACC deployment must inject CYID Application ID and Workspace ID at runtime and validate its adopted consumer version is inside the provider support window; public source must not contain actual deployment identifiers.

## 2.3.20 — 2026/09/30

- CYID consumer contract 增加跨 repository 同步治理：`CONSUMER_SYNC_MANIFEST.json` 明列外部 consumer 必須鏡像的 7 個 canonical artifacts（consumer current/minimum version、shared standard、consumer changelog、Auth、Role/Access、Architecture）。
- Cross-repository consumer 必須保存 read-only mirror、提供可重現 sync 流程，並在 governance/CI 與 deployment 前逐檔 byte-compare CYID `main`；mirror 漂移時不得繼續部署。
- 同一 `CYapps` repository 內的 consumer（例如 CYAccountingWeb／未來 CYInvoice）直接讀 canonical CYID files，不建立無意義重複副本。
- Consumer Contract 升為 `1.0.1`、Minimum Compatible 維持 `1.0.0`；contract version 只表示 consumer 語意相容性，documentation-only byte 變更仍透過 manifest mirror 同步，不必為每個 typo 人工升 contract version。
- Governance Check 驗證 sync manifest schema、source/target path safety、canonical membership 與 source-file existence，避免漏檔或錯誤 mirror package。
## 2.3.19 — 2026/09/30

- 建立 CYCloud Identity 唯一 shared consumer technical standard：`apps/CYCloudIdentity/docs/CONSUMER_INTEGRATION_STANDARD.md`。所有接入 CYID 的 App 必須以該 standard 與 CYID canonical contracts 為共同 Identity contract；consumer-specific handoff 只可保存 app 差異／遷移／例外／驗收，不得複製共同規範成第二套 authority。
- 新增 `CONSUMER_CONTRACT_VERSION` 與 `CONSUMER_MIN_COMPATIBLE_VERSION`，分離 CYID product version 與 consumer contract version；完成接入的 consumer 以自己的 `CYID_CONSUMER_VERSION` 宣告所採用 revision，且部署版本必須落在 provider 支援窗內。
- CYID PR 新增 mandatory `CYID Consumer Impact: NONE / BACKWARD_COMPATIBLE / CONSUMER_UPDATE_REQUIRED` 分類。Consumer-visible 變更必須同步更新 standard、contract version 與 consumer contract changelog；提高最低相容版本只允許在 update-required migration 下進行。
- 明確禁止 provider 單邊 breaking cutover：受影響 production consumer 尚未遷移時，CYID 必須保留 compatibility path 或先完成協調 migration，不得先提高最低相容版本造成既有 consumer 中斷。
- CYAccountingWeb handoff 收斂為 CYACC-specific migration map；shared Role／Session／App Access／first-login／recovery 規範改由 consumer standard 單一維護。

## 2.3.18 — 2026/09/30

- 依使用者最終確認，CYCloud Identity 新 Employee 首次使用流程對外統一稱 **Email 驗證**；不再以獨立「啟用帳號」流程作產品模型。
- 新 Employee 建立後由 CYID 自動寄出 Email 驗證郵件，內含具 expiry 的一次性首次登入密碼；temporary credential 只能進 CY Web 核心帳號流程，不得建立一般 Identity Session，也不得登入其他 CY App。
- 有效首次登入密碼只可換取短效 first-login ticket；使用者設定正式密碼後，CYID 完成 Email 驗證、正式 credential 建立與 temporary credential/ticket 作廢，但**不得直接發 normal Session**，必須回到 CY Web 一般登入頁重新以正式密碼登入。
- 首次登入密碼逾期、管理員「重寄驗證 Email」或 pending Email 修改時，舊 temporary credential 必須立即失效並重新產生 credential／expiry；寄送失敗保留 pending Employee。