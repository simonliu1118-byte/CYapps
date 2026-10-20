# CYApps Governance Changelog

## 2.3.54 — 2026/10/19

- CYERPAutoQuery PROJECT_RULES 依使用者要求加入 ERP 自動登入與閒置關閉：ERP 程式、帳號、密碼（DPAPI 加密保存在本機）、公司與閒置分鐘放在設定；查詢找不到 ERP 時自動開啟並登入，登入畫面須有密碼框才輸入；ERP 主視窗或建立作業沒有回應、作業速查開啟逾時時，強制關閉 ERP 重開登入並重試一次；建立作業閒置 10 分鐘、ERP 閒置 30 分鐘關閉（以最後一次查詢與本機鍵盤滑鼠操作較晚者起算），關閉只用視窗訊息，離開確認只按「是／確定」，其他詢問保留不關；可設定開機自動開啟。不改其他 App 規則。

## 2.3.53 — 2026/10/17

- CYERPAutoQuery PROJECT_RULES 依使用者要求加入供應廠商查詢：使用 PURI01 供應廠商資料建立作業，規則同 COPI01；廠商名稱只可貼上程式固定產生的簡稱 OR 公司全名條件（PURMA.MA002／MA003），結果每筆須含關鍵字否則不採用；廠商代號只接受英數。並加入進貨退貨單查詢（PURR18 依廠商與單據日期；PURR22 無廠商條件，同日期整份轉出後只留該廠商，小計須符合）。不改其他 App 規則。

## 2.3.52 — 2026/10/17

- CYERPAutoQuery 依使用者要求比照 CYERPAutoInput 加入自動更新與正式 Release：新增 `.github/workflows/cyerp-auto-query-release.yml`（只由 main 手動啟動、BUILD=0、tag 不覆寫）；PROJECT_RULES 加入自動更新與 Release 規則。不改其他 App 規則。

## 2.3.51 — 2026/10/17

- CYERPAutoQuery PROJECT_RULES 依使用者要求加入單筆銷貨／銷退單查詢（未收款憑證）：23 開頭 COPR15、24 開頭 COPR16，起迄點選後 Home＋貼上單號並讀回，客戶與日期群組須為空，轉出單號須相符。不改其他 App 規則。

## 2.3.50 — 2026/10/17

- CYERPAutoQuery PROJECT_RULES 依使用者要求：「標準進價」送往 CY Web 只在工作帶 includeCost（員工有 ERP 商品成本權限）時才送出，本機顯示不變。不改其他 App 規則。

## 2.3.49 — 2026/10/17

- CYERPAutoQuery PROJECT_RULES 依使用者要求加入未收款查詢（ACRR18 未結案應收明細表）：流程同 COPR15；可查單一客戶、單一業務員或全部，單一時勾區間選擇並讀回代號、其他群組須為空，全部時兩群組皆須為空；轉出須符合查詢代號且結帳單未收加總等於報表合計。不改其他 App 規則。

## 2.3.48 — 2026/10/17

- CYERPAutoQuery PROJECT_RULES 依使用者要求：加入銷貨單查詢（COPR15 銷貨單明細表、COPR16 銷退單明細表：只設客戶代號、單據日期與進階選項狀態，只按即時報表與 XLSX 匯出，ERP 匯出資料夾的檔案立即移到 TEMP 讀完即刪，報表與條件視窗用完關閉）；新增「Agent 與 CY Web 分工」：本程式只當中介層，關聯、呈現與業務規則交給 CY Web，判斷放 Web 更好時必須告知使用者。不改其他 App 規則。

## 2.3.47 — 2026/10/16

- CYERPAutoQuery PROJECT_RULES 依使用者要求加入客戶查詢：使用 COPI01 客戶資料建立作業，規則同 INVI02；客戶名稱只可貼上程式固定產生的簡稱 OR 全名條件（COPMA.MA002／MA003），結果每筆須含關鍵字否則不採用；客戶代號只接受英數。不改其他 App 規則。

## 2.3.46 — 2026/10/16

- CYERPAutoQuery PROJECT_RULES 依使用者要求：INVQ01 歷史異動起迄範圍不再由 Agent 限制最多三年，改由 CY Web 查詢模組負責；Agent 只提供開放的查詢功能。不改其他 App 規則。

## 2.3.45 — 2026/10/16

- CYERPAutoQuery PROJECT_RULES 依使用者要求加入：ERP 每個動作之後的畫面讀取一律以有上限的輪詢等待，不得只看一次就判定失敗（多次實機失敗皆因太早讀取）。不改其他 App 規則。

## 2.3.44 — 2026/10/16

- CYERPAutoQuery PROJECT_RULES 依使用者要求加入品名查詢：只在 INVI02「設定查詢條件 → 進階設定」貼上程式固定產生的品名條件（欄位 INVMB.MB002 寫在程式、關鍵字拒收引號與萬用字元並讀回確認），結果每筆品名須含關鍵字否則不採用；品號查詢前清除進階設定殘留條件、品名查詢前清空一般選項品號。不改其他 App 規則。

## 2.3.43 — 2026/10/16

- CYERPAutoQuery PROJECT_RULES 依使用者要求加入 INVQ01 庫存狀況查詢作業：只從 INVI02「串查」開啟並核對品號、只設頁籤與歷史異動起迄日（未填最近一年、最多三年）、「開始查詢」與「轉出到Excel」比照只讀與暫存檔即刪規則、成本與金額欄位解析後即丟棄、INVQ01 用完一律關閉；不改其他 App 規則。

## 2.3.42 — 2026/10/16

- 依使用者要求新增 `apps/CYERPAutoQuery/`（SMART ERP 自動查詢工具）：最小 `PROJECT_RULES.md`、VERSION／BUILD 與 PR／手動 Build workflow；REPO_POLICY 正式維護專案與 CY 桌面視覺採用清單加入本專案。
- PROJECT_RULES 記錄對 ERP 只讀、INVI02 定位（多個時用檢視狀態者、沒有時從主視窗作業速查開啟）、介面與 CYERPAutoInput 一致、查詢期間鎖滑鼠＋中央提示、頁籤「固定座標為主、未切換才 OCR」單一備援、品號資料以 ERP「轉出至 Excel」取得（匯出前單頭／單身各按全選、暫存檔只放程式旁 TEMP、讀完即刪、Ribbon 以 OCR 點擊）、以及使用者核准先由 CYERPAutoInput 複製必要程式碼、日後收斂為共用庫；不改其他 App 規則。
## 2.3.41 — 2026/10/17

- 依使用者新指示，CYAccountingWeb 改採合併與發布分離：PR 自動驗證，main 合併不自動部署；只有 `deploy/cyaccountingweb-production` 發布分支 push 或 main workflow_dispatch 可部署，來源須與當前 main 完整 SHA 相同。
- 正式部署使用固定鎖且不取消進行中的部署，保留既有 Environment、Identity／migration／登入與資產檢查；Stable Release 必須取得相同 SHA 的成功 deploy job 證據，不能把 validation-only／skipped 當成已部署。README 為操作入口，交接與 PROJECT_RULES 同步；產品 VERSION／BUILD、業務程式與資料契約不變。

## 2.3.40 — 2026/10/14

- 依使用者要求，CYAccountingWeb 桌機編輯回到原記帳表單；跨裝置共用既有 Adaptive entry editor／draft／cancel 與 app.js transaction writer，移除被取代的 ledger inline renderer、listener、assets 及樣式。PROJECT_RULES owner 地圖同步更新，不新增第二 writer 或 authority。
- 日期 presentation 改可輸入文字＋日曆按鈕，消費既有日期 parser／setter；移除 owned-DOM observer 及 synthetic-keyboard retry bridge，datepicker teardown 中止舊 lifecycle listeners。產品 V0.23.4；帳務 schema、Identity contract、Stable Release 不變。

## 2.3.39 — 2026/10/13

- CYAccountingWeb 一次性清理 #436／run 38383025033 完成並回讀：34 個過期分支、16 個退場流程的 22 筆 completed runs 已刪，候選殘留為零，沒有到期 Artifact。#268／#291／#383 過期文件 PR 已關閉；現行部署／Release、未合併 #435 與 optimistic create、其他 App／共通流程保留。
- 移除本次 maintenance workflow／script／manifest，彙整現況、驗收及後續工作；不留永久清理權限入口，產品 VERSION／BUILD、正式資料與 Stable Release 不變。

## 2.3.38 — 2026/10/13

- 使用者授權清理 CYAccountingWeb 無用 GitHub 產物；新增一次性 maintenance 流程，PR 僅唯讀盤點，main 才執行已檢視的 branch SHA 清單、已退場 workflow 的 completed runs 與本 App 到期 Artifact 清理。現行部署／Release、執行中 run、open PR 及指定未完成工作保留；不接入 Cloudflare 或讀取帳務。
- 清理完成後移除一次性 workflow／script／manifest，保留正式流程及可追溯維護紀錄；不新增永久授權例外，不改產品 VERSION／BUILD。

## 2.3.37 — 2026/10/13

- CYInvoice V2.6.19 首次正式 Release 將 Cloud client contract test 以 `--no-build` 執行，但 Cloud.Tests 不在 CYInvoice.sln 內，Windows runner 未產生 exe；修成測試專案自行建置，保留此額外 test gate，不繞過測試。
- 若同一授權版本的正式 Release 在未產生 tag／Release 前失敗，允許在驗證原失敗並完成治理 PR 後，從最新 main 一次建立明確命名 `release/cyinvoice-vX.Y.Z-retryN` 的新分支重試；仍僅分支建立事件有效，不能推送舊分支、自動發行其他版本或部署 Cloudflare。

## 2.3.36 — 2026/10/13

- CYInvoice 明確核准在已有當次版本 Release 授權時，使用從 main 精確 commit 建立的 `release/cyinvoice-vX.Y.Z` 一次性 push 作為正式 Windows Release 的額外啟動入口；只有分支新建事件符合，分支後續 push 不會再次發行。
- 正式 Release 仍從 main checkout、核對精確來源 commit、VERSION／BUILD、全部必要 Windows 驗證／掃描、ZIP／SHA-256 與未占用 tag；不自動從一般 PR／main push 發布，亦不涉及 Cloudflare Worker／D1。

## 2.3.35 — 2026/10/12

- 依使用者確認新增 `apps/CYPriceDesk/` 的最小 `PROJECT_RULES.md`、VERSION／BUILD 與必要 prototype validation workflow。專案採用既有共通／repository 規則，沒有新增永久架構或身分例外。
- 原型獨立保存，CYWEB 僅提供外部入口；正式需求與併入／獨立發展尚未定案。沒有變更 CYID、其他程式的部署或現有 Sites audience。

## 2.3.34 — 2026/10/10

- CYInvoice `PROJECT_RULES.md` 明列採用 CYID canonical Consumer Integration Standard；完成使用者授權 consumer 接入所需的專案 adoption gate，直接引用同 repo canonical package，不新增身分語意／規則層，也不授權正式切換。

## 2.3.33 — 2026/10/10

- CYERPAutoInput `PROJECT_RULES.md` §1：使用者核准新增第二個放棄單據例外。按「開始輸入」時 ERP 已在新增或修改狀態，須先提醒並經使用者明確確認，才可放棄該單據回到檢視、再從新增開始輸入；未確認不得開始。原批次失敗放棄自建單據的例外不變。

## 2.3.32 — 2026/10/09

- CYERPAutoInput `PROJECT_RULES.md` §3：使用者確認「課稅別」「發票聯數」是一般稅務／發票標準分類、非機密資料，核准依 ERP 清單順序寫入 source，作為「ERP 下拉選項不得硬編碼」的明列例外；其他欄位的下拉選項與公司實際值仍只能來自本機設定或 ERP 現場讀取。

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
