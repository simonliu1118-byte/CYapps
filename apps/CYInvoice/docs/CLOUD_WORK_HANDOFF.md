# CYInvoice 現行工作交接／新對話入口（2026-10-15）

> **這一份是下一個 CYInvoice 對話的唯一現況入口，不另建立重複交接文件。**
> 永久開發規範：根目錄 `REPOSITORY_RULES.md`／`REPO_POLICY.md`／`apps/CYInvoice/PROJECT_RULES.md`；未完成 checklist 統一 [TODO](TODO.md)，業務規格見 [REQUIREMENTS](REQUIREMENTS.md)，災難復原設計見 [CLOUD_DISASTER_RECOVERY](CLOUD_DISASTER_RECOVERY.md)，人工驗收見 [RC_TEST](RC_TEST.md)。不要根據本檔推定任何正式環境狀態。

## 1. 三條分支，禁止混同

| 工作線 | 現況 |
| --- | --- |
| **正式已發行 Windows** | [CYInvoice V2.6.19 Build 0](https://github.com/simonliu1118-byte/CYapps/releases/tag/cyinvoice-v2.6.19)，2026-10-13 發布；**V3 尚未正式發版**。 |
| **AutoInput 工程介接** | [CYInvoice Draft #445](https://github.com/simonliu1118-byte/CYapps/pull/445)，`cyinvoice/feat-autoinput-interface`，V2.6.20 Build 0；與 [AutoInput PR #424](https://github.com/simonliu1118-byte/CYapps/pull/424) 另線整合，仍待真 EXE／雙行程 E2E，不能混進 V3 PR。 |
| **V3 Device 災復＋多機工作單** | [CYInvoice Draft #440](https://github.com/simonliu1118-byte/CYapps/pull/440)，`cyinvoice/v3-recovery-invitation-schema`；配套 [CYWEB Draft #151](https://github.com/simonliu1118-byte/chihyuan-web/pull/151)，兩個 PR 都未合併、**未部署**。 |

**最近一份已確認完整 CI 的 V3 程式／文件 HEAD：** `ea23098be56fb20e523862bcc05afeaedea56073`（後續純文件提交需要另外對照該次 checks）：
[Cloud Check #38599397565](https://github.com/simonliu1118-byte/CYapps/actions/runs/38599397565)、
[Windows Build #38599397577](https://github.com/simonliu1118-byte/CYapps/actions/runs/38599397577)、
[Governance #38599397647](https://github.com/simonliu1118-byte/CYapps/actions/runs/38599397647) **success**；
[Development validation #38599397575](https://github.com/simonliu1118-byte/CYapps/actions/runs/38599397575) validation **success，真正 deploy job skipped**。
CI 不等於正式 D1、Cloudflare Worker、真 Email／Device 或 AMEGO 操作驗收。

## 2. V3 已有 source，不能當成已上線

- **CYWEB↔CYInvoice 全裝置復原**：CYID 已驗證 SUPER_ADMIN／CSRF gate，CYInvoice private named RPC 的簽發／狀態查回／明確撤銷；明確 `revoked` 只依 persisted `revoked_at`／D1 成功撤銷判定，consumed、expired／失敗不冒充撤銷。Windows 在「直接加入雲端」內提供「災難復原／繼續災難復原」，preview／claim／DPAPI Pending Token 已在 source；成功 claim 才同 D1 交易撤銷同 Workspace 舊 Device／邀請／配對並留 Audit。舊裝置 in-flight 業務寫入仍需依操作邊界 fencing，真跨服務 DR-01～DR-13 尚未驗收。CYWEB owner 交接：[CYINVOICE_RECOVERY_HANDOFF](https://github.com/simonliu1118-byte/chihyuan-web/blob/cyweb/feat-cyinvoice-recovery-auth-gate/docs/development/CYINVOICE_RECOVERY_HANDOFF.md)。
- **Device revoke 執行當下保護**：`device-lifecycle.ts` 在 D1 寫入時確認原 Device＋Workspace active，Built-in 再核對管理員角色／憑證；CYID_ONLY 不查已退役的 Built-in 表；成功 Audit 與真正 SQL mutation 一致。已有 synthetic race／雙 profile 測試；**不代表所有 AMEGO 業務 side effect 已 fencing**。
- **Work Item 最小技術原型**：`cloud/drafts/0015_work_items.sql` **只是一張待審草案表，不在正式 migration 清單**；`work-items.ts` 有 create/get/open→review→closed、unique reference／revision CAS／SQL-time Device fence。Worker `work-items-route.ts` 三個受保護 POST，Windows `CloudWorkItemClient.cs` 對齊契約，Built-in／CYID_ONLY 合成 SQLite＋Windows Cloud Client 測試已加入 CI。**尚未實際接上「上傳問題」或作廢／折讓按鈕，沒有正式 D1 表／API 可用**。`closed` 目前只是原型狀態，**不是**光貿官方成功。
- **原 D1**：本公司開發環境 Cloud0.9.3／API1／storage13／CYID_ENABLED=true／CYID_ONLY；2026-10-13 經另行授權完成本公司舊 Built-in 帳號表退役、備份與 FK 驗證，**不可重做**。V3 的 canonical 0014／optional issuer maintenance 未套用正式 D1，0015 還只是 draft。其他公司 Built-in／Self-hosted 的原支持不可刪掉。

## 3. 此對話已確認的 V3 業務規則（下一輪不能再重問／改錯）

### A. D1 不是發票資料庫

**只有真的提出申請或發生異常事件，才在業務 D1 建必要工作單、來源資訊和不可抹除的 Audit。**
一般開立成功、未申請且無任何異常的發票，**完全不能在自己的 D1 建發票紀錄或最小索引**。不搬整張發票的品項、PDF、訂單 Excel、匯入記憶體資料或光貿原始 payload。官方發票真實狀態仍以光貿為準。

需要跨機管理員覆核的**既有三種申請**：
1. **紙本未收回的作廢申請**：一般使用者送審，管理員可在自己電腦核准／取消退回。
2. **一般使用者的折讓申請**：先做正式折讓 API，之後才改成 D1 跨機覆核。
3. **折讓作廢申請**：現有人工管理員待辦也需跨機；是否由 g0501 自動化，依獨立 API 驗收。
核准、退回、重送審核、人工結案、申請者／覆核管理員／時間／原因，保留不可覆寫 Audit。未發生過申請的發票仍不留雲端紀錄；退回也不能清除歷史。

**「上傳問題」整個頁面的待辦也要跨機可見**，不只上述三種人工覆核：`InvoiceSyncIssueStore` 的 invoice_list_failed／query_failed／remote_not_found／unknown_remote_not_found／reconciliation_failed／ambiguous_match／local_write_failed 等，**以及另外來自本機 `InvoiceRecords` 的「開立失敗」**（這不是 `InvoiceSyncIssueStore` 的同一資料來源）。管理員在其他電腦看得到來源 Device、必要 OrderID／發票識別、清洗過的錯誤分類、處理紀錄與狀態；不能以遠端已讀或結案冒充光貿已成功或原機已修好。一般「處理中」的正常上傳等待／已送出且尚待光貿完成作廢，不要另建永久 D1 事件，完成後從處理中消失；若產生異常才建事件。

**原本「清除／刪除開立失敗」要改為封存**，保留 D1 事件／Audit，不因此清掉既存失敗原因。事件不應每次重新整理就重複寫新單。同一 Workspace 管理員可追蹤異常；需來源裝置修復的本機 SQLite 寫入錯誤，不得由另一台電腦直接假裝已修復。不要引入第二套待辦框架。

### B. 折讓 API「先」於折讓 D1 協同

**下一輪第一個功能重點：先把一般管理員去光貿手動折讓，升級成 CYInvoice Core/Gateway 正式 `/json/g0401` API**，同時適用 **CYID／Built-in／單機**，不依賴 Cloud 啟用。光貿要求**呼叫端提供且先保存唯一 `AllowanceNumber`（16 字內）**，不是官方自動配發／回傳；收到成功或逾時後，以該號查官方狀態，結果不明不得新號盲目重送。API 及真正查回安全驗收後才能移除手動輸入折讓單號／人工挑多個候選折讓單號的正常 UI；歷史資料與結果不明安全回查仍保留。**`/json/g0501` 折讓作廢另做驗收，不因 g0401 完成就視為自動化完成。** 參考 [光貿官方 API 文件](https://invoice.amego.tw/api_doc/)。

### C. 原 OrderID／既有防重完整，禁止重做

**已直接看過 source 及既有回歸**：
- MO店+／酷澎／鼎新 ERP 等正式匯入沿用**原來源 OrderID** 送光貿（只有測試環境另用原有測試前綴）。正式開立相同 OrderID 防重以光貿為準，不新增 D1 全域 Cloud Lock。
- **第一次成功開立**，正常發票清單已有同 OrderID 的**一張成功發票**；第二次以相同 OrderID 送出被光貿拒絕時，**第二次嘗試在本機 SQLite 仍有一筆 `Failed`，號碼清空，只顯示「上傳問題 → 開立失敗（例如訂單編號重複）」**；不是另一張成功發票，也不是要刪掉第一次。
- `RecordsControl.Matches` 排除 Failed；`InvoiceService.RefreshAllAsync` 跳過 Failed；`InvoiceDetailRefreshService.RefreshAsync` 拒絕 Failed 詳查；`InvoiceSyncService.Match` 排除 Failed，不把遠端同 OrderID 轉成第二筆成功。已有 Core `TestWireDuplicateRejectionAsync`／`TestIssueOpensAndBlocksResendAsync`／`TestRecoveryOfExistingInvoiceAsync`，Sync `TestFailedDetailRefreshAsync`／`TestFailedOrderMatchAsync`／`TestTestEnvironmentSkipsFailedAsync` 及 WinForms smoke。**不要重新設計本機防重，僅讓 Failed 事件跨機可見及封存留 Audit。**
- 真正「官方結果不明」是請求／回覆／回查都無法確認或成功無號碼等少見情況。管理員可：① 留光貿客服詢問原因／回覆與 actor／時間；② 憑官網已開立證據，**更新原本那筆紀錄**的發票號碼、日期、時間（不可 Append 第二筆成功），記人工補登 Audit；③ 官網明確確認沒開立後，由管理員明確核准以**同一原 OrderID** 重送一次並留 Audit。不得逾時自動重送。光貿確定開立但本機保存失敗走人工本機修復，不要混成官方結果不明。

### D. CYID 專用公司卡片

設定頁**目前 ADMIN／SUPER_ADMIN 都能開啟，也都可改本機統編／App Key／MO店+ 密碼**，這是已看過的既有 source，不要誤寫成只限超管。V3 新增「公司設定卡片」時，**只有 CYID SUPER_ADMIN 可新增／修改／移除統編＋AMEGO App Key＋MO店+ 密碼的公司綁定**；一般 ADMIN 可開設定，但只看公司統編與憑證是否設定，不應讀取明文；Worker 必須強制權限驗證，不能只隱藏 UI。敏感密鑰要加密（解密金鑰不可明文和 D1 密文同存），Audit 不留 Secrets。Built-in／單機既有 DPAPI 不因這功能被強制遷移。

## 2026-10-15 接續實作增量（未驗收、未部署）\n\nV3 Draft #440 已開始補上正式折讓 API 的 **Core Gateway 底層能力**：新增 `AmegoAllowanceContracts.cs` 與 `AllowanceNumberGenerator`，在 `AmegoContracts.cs` 加入三個 optional gateway methods、`AmegoClient.cs` 實作 `/json/g0401`（整批陣列）、`/json/allowance_query`、`/json/allowance_status`，並在既有 Allowance.Tests 補隔離請求格式、查回及已確認的每日流水號格式／邊界測試。原 `EmployeeAllowanceWorkflowService`、WinForms 管理員人工折讓 UI、正式開立與 OrderID 路徑均未改。\n\n**不可誤認已完成：** 尚無送出前 SQLite durable operation＋payload／原號保存、跨機操作互斥或結果不明狀態機；這些要先於 UI 接線完成。不能單純靠每臺裝置各自計算本機流水號聲稱不撞號；不得讓 API 結果不明自動更換號碼或再次發出 g0401。正式 Cloud D1、Worker、CYID、Release 未變更；真 AMEGO 尚未送出。\n\n**2026-10-15 更新折讓配號決策：**固定 `AyyyyMMddXXX` 共 12 碼，每日 `001..999`，第 1000 筆後 `A00..AZZ → B00..ZZZ`，共 **34,695** 號；管理員真正核准執行時才配號，未核准申請不占號。號碼配出即永不回收；Cloud 由同一個 Work Item／業務 D1 安全交易持久化，同一公司／環境跨裝置唯一，單機不應依賴 D1，改 SQLite durable owner。Core 格式器和測試已更新；**尚無正式 allocation/送出狀態機**。

**2026-10-15 官方碼補證：**使用者提供光貿 [API 錯誤碼官方頁](https://invoice.amego.tw/info_detail?mid=71)，直接核對 `/json/g0401` 的 `4040161`＝「指定折讓單號 已存在折讓開立」，`4040162`＝「指定折讓單號 已存在作廢折讓」，`4040163`＝「指定折讓單號 折讓類型錯誤」。Core 新增 `AmegoAllowanceErrorCodes` exact `4040161` 分類與 HTTP 假回覆測試；Gateway 繼續 throw 帶 code 的 `AmegoApiException`，沒有啟用自動撞號重試。**僅在本操作沒有任何先前 timeout／不明送出且光貿明確回覆 `4040161`**，才可把原 D1 號永久記為 `occupied_external`，用相同 Work Item 分配下一號並設有限次重試。若本操作先前曾送出不明，`4040161` 可能指自己的前次成功；要查原號並比對官方資料，不得直接換號；`4040162`／`4040163` 及其他錯誤碼一律不自動換號。D1 原子分配、durable submit state、實機光貿測試皆待辦；本批沒有 Worker deploy、D1 migration、正式開立或 Windows Release。

## 4. 下一個新對話按此順序開發，不過度設計

1. **先作 /json/g0401 的現有 Gateway/Core API 升級**：盤點正式 request／response、先產生並保存 AllowanceNumber、結果查回與不盲重送；同一功能供 CYID、Built-in、單機，配相關回歸。尚未驗收前保留原人工流程，**不改動正常開立／OrderID 防重**。
2. g0401 安全驗收後，**在同一套 Work Item／上傳問題架構**設計 D1 事件／申請 Audit 的最少欄位與必要狀態，涵蓋三種人工申請及開立失敗／技術異常，不建一般發票索引。既有 `0015` 只是驗證技術核心，不可直接當完整業務 schema 部署。
3. 將 D1 的跨機申請／異常接現有 Windows「上傳問題」和管理員處理入口，將刪失敗改封存、追蹤結果不明的三種人工處置；保留來源 Device、本機權威、AMEGO 官方權威及 Audit。
4. **CYID_ONLY 公司卡片**獨立做加密與 SUPER_ADMIN write guard；多機 DR／CYWEB #151 的 Cloudflare 配置、復原驗收依現有 TODO 繼續，與 g0401 不互相當阻塞。

沒有必要為正常開票另建 D1 invoice table、event bus、第二套同步服務或全域 OrderID Lock。進入新對話前先看最新 PR #440 HEAD／diff，不要只信文件的舊 commit SHA。

## 5. 部署、測試與其他不可跨越的界線

- **這次對話僅整理文件**；無正式合併、Cloudflare Worker/D1 migration、測試 Worker、正式 Windows Release、真 Email、真 AMEGO 開立或真 Device 撤銷。任何上線請另取得明確核准。
- 不能自行建立新的 Cloudflare 測試 Worker／handler。僅可於 source、synthetic SQLite、Windows CI 驗證；V3 schema 0014／optional issuer／draft 0015 不能誤當已套用原 D1。
- CYID 是中央員工身分權威，CYInvoice 只管理自身 Device／業務 Workspace／業務 Audit，不復活本公司已核准退役的 Built-in 帳號表，不把工作單敏感資料或 Secrets 推到 public Git。
- AutoInput #445 的工程測試與 Windows v2.6.19 正式版分開；最終量產及 DR、多機多人 E2E 要按 [RC_TEST](RC_TEST.md) 有實機證據。
