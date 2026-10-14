# CYInvoice 現行工作交接／新對話入口（2026-10-15）

> **這一份是下一個 CYInvoice 對話的唯一現況入口，不另建立重複交接文件。**
> 永久開發規範：根目錄 `REPOSITORY_RULES.md`／`REPO_POLICY.md`／`apps/CYInvoice/PROJECT_RULES.md`；未完成 checklist 統一 [TODO](TODO.md)，業務規格見 [REQUIREMENTS](REQUIREMENTS.md)，災難復原設計見 [CLOUD_DISASTER_RECOVERY](CLOUD_DISASTER_RECOVERY.md)，人工驗收見 [RC_TEST](RC_TEST.md)。不要根據本檔推定任何正式環境狀態。

## 2026-10-15 最新產品決策（優先於本文內舊 Built-in 設計敘述）

**使用者正式確認 V3 只支援兩種模式：單機 Local 與 CYID Cloud；停止提供／維護 Built-in Cloud／Self-hosted。** 現無正式 Built-in 使用者，GitHub 使用者如需獨立雲端版本自行 fork 修改，CYInvoice 不再為其投入產品開發、身分路由、D1 雙 storage-profile 相容測試、OTP／Employee CRUD／超管移交或對外自架文件。此前文件提到「Built-in 正式支援模式」、「三版共用」、「其他公司 Built-in 原 D1」、「需保留 Built-in migration」等皆是**舊版架構／歷史證據**，不再是 V3 完成條件。舊程式碼在清理、遷移與 CI 驗證前不能誤寫成已刪除。**不重做**本公司已完成的原 D1 帳號表退役維護。

**V3 最小雙模式架構：**兩版共用光貿 `g0401`／`g0501`、發票、折讓、安全回查與 Windows 核心；單機帳號／資料／折讓配號使用本機 SQLite（無 CYID／D1）；CYID Cloud 由 CYID 負責 Employee／Credential／App Access，CYInvoice 自己的業務 D1 負責 Device、Work Item、Audit、折讓配號。兩版都使用「公司設定卡片」UI；Local 保存既有安全的本機設定，CYID 版公司憑證由 SUPER_ADMIN 授權寫入並加密存於其業務設定，不把金鑰暴露給 ADMIN；不因此讓單機依賴雲端，也不改現行角色權限而未經驗證。

**此先前離線光貿列表暫配號提案已由上方更新決議取代**：Cloud D1／Work Item 不可用時暫停跨機折讓核准；單機繼續使用 SQLite。

## 2026-10-15 雲端故障及公司憑證決議（使用者已批准）

**已確認，不再作為待討論問題：**
- CYID Cloud 在 CYInvoice 業務 D1／Work Item 服務無法連線時，**暫停新增／核准／執行跨機折讓申請與其他必須先取得 Work Item 原子鎖的操作**；不因光貿 `allowance_list` 可查詢或 `4040161`／`4040162` 防撞就改用本機離線核准、先送出再補寫 D1。恢復服務後才依原 Work Item 流程處理。已送出之光貿**唯讀官方查回**可在不寫 D1 的前提下繼續，結果不明不盲重送。Local 單機折讓仍按本機 SQLite durable 與光貿執行，不受 Cloud/D1 影響。
- **普通開票、發票查詢、其他現行被允許的光貿業務**維持既有 Cloud 暫時故障的最後可信身分與權限降級矩陣；絕不因新增折讓配號依賴 D1 而整體封鎖正常開票。已知身分／權限／Device 拒絕不可走快取 fallback。
- **統一公司設定卡片**：Local 繼續用本機 DPAPI 安全保存 App Key／MO 密碼；CYID Cloud 的**公司設定權威**為 CYInvoice 業務 D1，由 CYID SUPER_ADMIN 在 Worker 實際執行時驗證，才能新增／修改／移除公司統編、AMEGO App Key、MO店+ 密碼；ADMIN 可看統編及是否已設定，UI 不回傳 Secrets。Server-side 加密金鑰與 D1 密文分離，Audit 不寫 Secrets。Windows 可保存**已授權取得、綁定 Device 的本機 DPAPI 加密快取**，供 D1 暫時無法連線、光貿正常時執行原本允許的發票業務；恢復連線後更新設定版本，撤銷或替換舊快取，已知撤銷必須 fail closed。離線時無法承諾立即套用 D1 新的公司設定與秘密撤銷，須明示此限制並測試。
- **光貿官方發票→折讓關聯查詢**：`POST /json/invoice_query` 以 `{"type":"invoice","invoice_number":"EC00000001"}` 查詢；`data.allowance[]` 含對應折讓單號、日期、類型（開立 D0401／作廢 D0501）、上傳狀態、稅與未稅總額。光貿網站手動建立的同統編／授權範圍折讓亦可回查，並非只能看到 CYInvoice 自己送出的折讓。發票主體 `invoice_type`／`invoice_status`／`cancel_date`／`wait[]` 可判別發票作廢狀態與待處理異動，但**不是完整不可抹除的人員／操作 Audit**。官方 `invoice_query` 依發票日期限查 **180 天**，超過可回 `code=51`，不能判為無折讓；需要時依指定折讓單號查 `allowance_query`（含明細原發票號碼／作廢時間），或設計 `allowance_list` 日期範圍＋原發票號碼篩選之受限歷史探索，不把 API 無資料冒充確定不存在。官方來源：https://invoice.amego.tw/api_doc/ 。現有 `AmegoClient.QueryByInvoiceNumberAsync` 與 `ParseAllowances` 已有這個解析邊界；實際完整折讓／作廢樣本仍需驗收，不把 mock 當正式真資料。

## 2026-10-15 光貿網站外部操作之官方回補（使用者確認）

**使用者新規則：**若 CYID 雲端 D1 服務暫時不能使用、但光貿網站仍可用，管理員可以在光貿網站直接人工作廢發票／開立折讓／作廢折讓；CYInvoice 應於後續既有同步時機／開啟發票詳情時，從光貿 `/json/invoice_query` 的原發票作廢資訊及 `data.allowance[]` 同步最新官方事實到**既有本機 SQLite 紀錄／官方折讓快照**，以「光貿官方同步」辨識資料來源。**不是 CYInvoice 發出的操作，不得新增 D1『CYInvoice 管理員開立折讓／作廢』Audit，也不得憑 API 倒推操作者、原因或批准紀錄。** 有原 Work Item 時保留原本 CYInvoice 申請／覆核歷史；日後復原雲端連線可將查回狀態關聯至既有工作單，不為光貿網站的操作杜撰 Audit；沒有申請則不因此在 D1 建 Work Item／一般發票索引。單機版亦可共用官方回補，不依賴 D1。

**現存的真實缺口（已讀 source）：**`InvoiceAutomaticSyncService` 每日首次同步兩個期別、其餘三日清單；`InvoiceSyncService.SyncListedAsync` 只有新發票、發票列表摘要改變、品項缺失、某些待處理錯誤才會完整 `invoice_query`，光貿網站手動折讓可能**不會修改 invoice_list 摘要**而漏查。`InvoiceDetailRefreshService.RefreshAsync` 開啟詳情時會查，但不代表背景掃到。`InvoiceAllowanceMetadata.ApplyQuery` 已保存 `cyinvoice_official_allowances`，但 `InvoiceSyncService.Fingerprint`／`InvoiceDetailRefreshService.OfficialFingerprint` 沒包含折讓快照，容易漏寫背景變更／漏失效相關 PDF Cache。V3 須先補齊**限速、分批、可持續輪巡**的完整發票回查，優先既有 pending／近期、逐步涵蓋有效範圍，後續不再對完全未變動的資料重寫；查詢失敗保留最後可信資料及例外提示。官方 invoice_query 最多查發票日期 180 天，舊資料不可將期限錯誤當作無折讓。不要在每 15 秒連線狀態探測裡做完整業務同步。

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

**2026-10-15 官方碼補證／使用者最新決議：**光貿 [官方 API 錯誤碼](https://invoice.amego.tw/info_detail?mid=71) 明確說明 `/json/g0401` 的 `4040161`＝「指定折讓單號 已存在折讓開立」、`4040162`＝「指定折讓單號 已存在作廢折讓」。**兩個錯誤碼皆表示號碼已使用、不能回收，且採相同撞號處置**；`4040163` 是折讓類型錯誤，不屬撞號。Core `AmegoAllowanceErrorCodes.IsAllowanceNumberOccupied` 精確接受 4040161 與 4040162，隔離 HTTP 測試驗證兩個錯誤及未自動重送。Gateway 仍只 throw 帶 code 的 `AmegoApiException`。

**未來自動換號條件不變：**只有同一折讓操作不存在先前 timeout／不明送出，且此次光貿正式回覆 4040161 或 4040162 的明確拒絕，才將舊號永久記錄為 `occupied_external`，同 Work Item 原子分配下一個號碼並做有限次自動重試。如果之前曾有不明送出，兩個碼都可能指同一筆操作已被建立（之後甚至作廢），應按原號查回、比對發票／金額及其他必要資訊；不能因為碼屬「占用」就盲目換新號。D1 durable operation／原子配號／重試流程仍未完成，未修改正式環境 D1／Worker，未呼叫真 AMEGO，未正式發布。

## 4. 下一個新對話按此順序開發，不過度設計

1. **先作 /json/g0401 的現有 Gateway/Core API 升級**：盤點正式 request／response、先產生並保存 AllowanceNumber、結果查回與不盲重送；同一功能供 CYID、單機，配相關回歸。尚未驗收前保留原人工流程，**不改動正常開立／OrderID 防重**。
2. g0401 安全驗收後，**在同一套 Work Item／上傳問題架構**設計 D1 事件／申請 Audit 的最少欄位與必要狀態，涵蓋三種人工申請及開立失敗／技術異常，不建一般發票索引。既有 `0015` 只是驗證技術核心，不可直接當完整業務 schema 部署。
3. 將 D1 的跨機申請／異常接現有 Windows「上傳問題」和管理員處理入口，將刪失敗改封存、追蹤結果不明的三種人工處置；保留來源 Device、本機權威、AMEGO 官方權威及 Audit。
4. **兩模式公司設定卡片**：Local 延續本機安全設定，CYID Cloud 使用加密憑證與 SUPER_ADMIN write guard；多機 DR／CYWEB #151 的 Cloudflare 配置、復原驗收依現有 TODO 繼續，與 g0401 不互相當阻塞。

沒有必要為正常開票另建 D1 invoice table、event bus、第二套同步服務或全域 OrderID Lock。進入新對話前先看最新 PR #440 HEAD／diff，不要只信文件的舊 commit SHA。

## 5. 部署、測試與其他不可跨越的界線

- **這次對話僅整理文件**；無正式合併、Cloudflare Worker/D1 migration、測試 Worker、正式 Windows Release、真 Email、真 AMEGO 開立或真 Device 撤銷。任何上線請另取得明確核准。
- 不能自行建立新的 Cloudflare 測試 Worker／handler。僅可於 source、synthetic SQLite、Windows CI 驗證；V3 schema 0014／optional issuer／draft 0015 不能誤當已套用原 D1。
- CYID 是中央員工身分權威，CYInvoice 只管理自身 Device／業務 Workspace／業務 Audit，不復活本公司已核准退役的 Built-in 帳號表，不把工作單敏感資料或 Secrets 推到 public Git。
- AutoInput #445 的工程測試與 Windows v2.6.19 正式版分開；最終量產及 DR、多機多人 E2E 要按 [RC_TEST](RC_TEST.md) 有實機證據。
