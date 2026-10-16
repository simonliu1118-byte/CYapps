# CYInvoice 現行工作交接／新對話入口（2026-10-16）

> **這一份是下一個 CYInvoice 對話的唯一現況入口，不另建立重複交接文件。**
> 永久開發規範：根目錄 `REPOSITORY_RULES.md`／`REPO_POLICY.md`／`apps/CYInvoice/PROJECT_RULES.md`；未完成 checklist 統一 [TODO](TODO.md)，業務規格見 [REQUIREMENTS](REQUIREMENTS.md)，災難復原設計見 [CLOUD_DISASTER_RECOVERY](CLOUD_DISASTER_RECOVERY.md)，人工驗收見 [RC_TEST](RC_TEST.md)。不要根據本檔推定任何正式環境狀態。

## 2026-10-15 V3 新對話接續點：一步一提交（以此節覆蓋下方過時進度）

**這次僅更新交接與唯一 TODO，不改功能、不部署。** GitHub：`simonliu1118-byte/CYapps`，V3 Draft [PR #440](https://github.com/simonliu1118-byte/CYapps/pull/440)，分支 `cyinvoice/v3-recovery-invitation-schema`。文件整理前最新**功能** HEAD `b55751f0156784ba4548bffac49e61b9b25af86a`：Cloud Validate ×2、Windows Client、Windows x64、Governance 均成功；Worker deploy skipped。**後續文檔 commit 不改功能，但新開發 commit 需要重新確認其 own exact-HEAD CI。**

### 已實作於 Draft 分支的安全流程（非正式上線）

- **g0401**：CYID Manager + Frozen Work Item + 官方 `invoice_query` 嚴格未稅品項及稅額核對 → append-only Grant → D1 一次性配號及 send CAS → 單次送光貿 → `allowance_query` 原號查回。已存在折讓／含稅或官方資料不完整不得自動核准，HTTP 接受不代表稅務已完成。
- **g0501**：Cloud 官方 `allowance_query`、`allowance_status`、`g0501` 單次呼叫；`drafts/0019_allowance_void_workflow.sql` 不可變申請／核准、唯一 Claim、官方作廢證據後結案；`allowance-void-applications-route.ts`、`allowance-void-manager.ts` 已建立 CYID 受保護的 create/get/list/approve/send-once/status-readback/close 服務端路徑。**先前下方寫「尚沒有對外路由」是當時的歷史狀態，不是最新現況。**
- **清單對帳**：`amego-allowance-list.ts` 僅完成兩種日期分頁與例外 Work Item 異常定位器，**不是**定期自動對帳服務；必須選擇性官方詳查，不得從「清單找不到」推定允許重送。

### 下一輪只做一個工作

**步驟 1（Windows g0501 Core Client）已完成。** 功能 Commit `214063e1810d55746d77d22c483de42135f4f907`：已提交 `src/CYInvoice.Core/CloudAllowanceVoidClient.cs`、`tests/CYInvoice.Cloud.Tests/CloudAllowanceVoidClientTests.cs`，在 `tests/CYInvoice.Cloud.Tests/Program.cs` 登錄回歸測試；CYID Core 支援 create/get/list/approve/send-once/status-readback/close，拒絕後端可自動重試、偽造官方作廢、異常工作單及非 HTTPS 端點；Cloud Validate ×2、Windows Client、Windows x64、Governance **全部通過**，Worker deploy **skipped**。此提交未操作正式 D1／光貿，亦未接 WinForms。後面的文件提交是純文件異動，不能假裝增加了功能測試結果。

**步驟 2（Windows g0501 WinForms）已完成 Draft Source。** 介面 Commit `275ec1c652c629aa9ff6eb95877148ff2ce70daa`、耐久原 ID 保護 Commit `e7efd90b1a7b25ab862d24efb3bd3daa7214c7a6`、Nullable CI 修正 Commit `1875b3132f770bc7fd6dae2aee58b5e0e18bfd1b`。新版 `CloudAllowanceVoidManagerForm.cs` 透過 `SyncIssuesForm` CYID「處理中」頁開啟，提供員工申請／原工作單查回、管理員 open→review、核准、首次送出、官方 status-readback、官方再次核對後結案；使用既有 `CloudAllowanceVoidClient` 與 `CloudWorkItemClient`，任何官方操作仍由 Cloud CYID Worker 授權，Windows 不持有公司 AppKey 或自選 g0501 號碼。畫面提醒並要求確認，對同一 Work Item 禁止於同一視窗再次送出；Cloud D1 仍有永久唯一 Claim，不能靠重開程式解除。建立前將 Work Item ID 寫入依 Workspace+Cloud URL 隔離的本機小型記錄，不存帳密／憑證／發票內容；逾時或應用程式重啟仍能以原 ID 查回，確認後才清除。Windows startup smoke 在**完全離線**條件驗證畫面及本機 ID 的重啟保存／跨 Workspace 隔離。最後功能 SHA `1875b3132f770bc7fd6dae2aee58b5e0e18bfd1b` 的 Cloud Validate ×2、Windows Client、Windows x64、Governance **均通過**；Cloud deploy skipped。這只代表 source CI 和合成 UI smoke，**不是**正式 D1 或真人作廢驗收。

**步驟 3（D1 0015～0019 隔離 Migration 演練）已加入 Draft Source／待本 HEAD CI 驗證。** `cloud/tests/v3-migration-rehearsal.test.mjs` 從合成 0013→CYID_ONLY 維護→0014 基準建立未升級備份，五個草案 0015→0019 分階段升級、各階段故障後交易回復，檢查歷史 Device／Invitation／Audit、FK／SQLite integrity、正常發票不落 D1，以及從原始備份完整還原。僅隔離 Node SQLite，本批沒有接 Cloudflare、光貿或正式 D1。**下一步是步驟 4：雙日期 List／例外對帳與 Audit**；若 CI 失敗，先完成本步修正，不可往後推進。

**步驟 4 小提交 1：** `87412384` 新增兩日期有限官方 List 掃描，Cloud Validate 與 Governance 通過、Windows 驗證見 exact SHA checks；無任何生產環境變更。

**步驟 4 小提交 6（本輪）：** g0501 的管理畫面 `CloudAllowanceVoidManagerForm` 直接加「官方對帳」按鈕，沿用既有 CYID `reconcile` 路由和 g0501 `status-readback` Audit。Core `CloudAllowanceVoidClient.ReconcileAsync` 驗證 D1 原 Work Item、revision、原折讓號碼、官方 List 命中／查無與不可重送／自動結案；查無仍保留「官方查回」人工補查。只增原 Core 合成測試與 WinForms startup smoke 控制項驗證，不建新視窗／資料表／排程，也沒有正式部署。**最新 exact-head CI 必須另行核實。**

**步驟 4 小提交 5：** g0401 Windows 既有 `CloudAllowanceManagerForm` 直接增加「官方對帳」，不建立平行視窗；`CloudAllowanceApplicationClient.ReconcileForManagerAsync` 只送原 ID／revision、當次 CYID 憑證，依官方 List 查無／命中原號兩種回應 fail-closed，拒絕假冒可重送與原號不符。原「查回原號」仍保留供較早資料人工核實。Windows Core 合成測試補查無／命中／偽造結果。仍不自動改號、再送或結案，正式環境不變；待本次 exact-head CI。

**步驟 4 小提交 4：** 沿用既有 `reconcile`/原號 `allowance_query`，只在確實查到官方原號時寫入新加至 **Draft 0016** 的最小 `allowance_reconciliation_checks` append-only D1 Audit。寫入時再次檢查 Ledger、公司版次、Work Item revision、有效 Device 與 Workspace；只存號碼／類型／狀態／官方 snapshot SHA256／CYID 查詢人／時間，不存正常成功發票或官方原始回覆。任何不一致或失敗不會回傳成功，清單查無不能寫虛假官方 Audit；所有情況不得改 Ledger／重送。隔離遷移與回歸測試納入 CI，總草案表為 11 張，不改正規 migration、不部署。Windows UI、實機及最終 Release 留後續。本輪 exact-head CI 待驗。

**步驟 4 小提交 3：** g0401 於既有 CYID `allowance-applications` 管理員路由增加 `reconcile`，僅在 D1 原 Ledger `sending/confirmed`、公司版本／Workspace／Device／revision 符合時，先做 14 日雙日期有界 List 掃描，命中才沿用 `number-readback` 官方原號 `allowance_query`；未命中回待人工處理。保持 `safeToAutoResend=false`、`safeToAutoConfirm=false`，不寫入 Ledger、不改號、不重送。已擴充現有 synthetic route tests；仍缺 g0401 持久 Audit、WinForms 按鈕、排程與正式驗證。**CI 需用本次 exact HEAD 判斷。**

**步驟 4 小提交 2：** 不建新系統、資料表或 Cron，只在既有 CYID Manager `allowance-void-applications/reconcile` 增手動對帳。由原 Work Item、原永久 g0501 Claim、D1 公司權威提供查詢範圍及號碼；只有清單命中，才走現有 `status-readback` 查回及 `allowance_void_official_checks` append-only Audit。查無不代表未送達，回傳不自動重送、不自動結案；需再次通過授權與版本比對。整合測試涵蓋無 Claim、權限拒絕、偽造欄位、命中詳查／稽核與查無保守處理。**此提交驗證以最新 exact-head CI 為準。** Windows UI、g0401 跨機對帳、排程與正式 D1／官方實測仍待完成。

完成後依序做：D1 0015～0019 隔離升級演練 → 官方雙日期 List／例外對帳排程及 Audit → 含稅與歷史折讓餘額／公司設定等缺口 → DR-01～DR-13／人工實機驗收／V3 Release。確切排序及逐項勾選均以唯一 [TODO](TODO.md) 頂端「逐項交付索引」為準，不另開新交接文件。

**步驟 5 小提交 2（本輪）：** 不變更稅務與官方核准安全邏輯，只在 Windows 現有 `CloudAllowanceManagerForm`「官方查驗」呈現既有 `ItemCheck.Reason` 的正體中文人工處理提示，核准 API 回 `ALLOWANCE_APPROVAL_NOT_AUTHORIZED` 時以專屬警示說明保留原工作單、先人工核對、不得配號送出。沒有第二套介面、沒有新 API／資料表／排程；待本次 exact-head Windows CI 驗證。

**步驟 5 小提交 1：** 只強化原有 g0401 官方品項稅額核對：`invoice_query` 原品項的應稅未稅額依已支援的 5%／整元四捨五入後必須與官方稅額相同。原淨額＋官方稅額恰好等於官方總額仍不足以通過；額外合成案例驗證高估、低估與免稅品項的異常稅額都必須 fail closed。這不是新增含稅或歷史折讓支援，不建表、不改 API、配號或 Windows；正式資源不變。本提交 CI 需以 exact HEAD 確認。

**步驟 5 小提交 3（本輪）：** 發現兩個獨立 CYID Work Item 可能在官方首次折讓尚未回查前同時通過原發票檢查。僅在 Draft `0018_allowance_applications.sql` 加原子 Trigger，禁止同 Workspace／公司／環境／原發票的第二筆 `reserved→sending`（已存在另一筆 `sending/confirmed` 時）；`allowance-execute-once.ts` 將此安全衝突回傳 `G0401_ORIGINAL_INVOICE_IN_FLIGHT`，不可送第二張 g0401。沿用既有合成測試核對同發票拒絕／不同發票允許，不新增表格、背景查詢或正式 API。本批仍為 Draft Source，CI 待 exact HEAD 核對，正式 D1／光貿沒有異動。

**步驟 5 小提交 4（本輪）：** `9ef66a8` 已全套 CI 通過；既有 Windows 設定選單 `SettingsForm` 的正式公司欄旁新增唯讀「CYID 公司卡」查詢，使用既有 `CloudCompanySettingsClient.GetAsync` 取得 D1 安全中繼資料（統編／revision／兩種密鑰是否設定）並經 CYID 管理員當次驗證，不取回明文憑證、不從本機設定覆寫雲端。沒有新對話框或資料模型（僅 MessageBox 顯示結果），且只在完整 CYID Cloud 裝置啟用；Startup smoke 已納入位置與 Enabled 核對。CI 待 exact SHA 驗證。下一個小項目將復用現有 Update API 處理超級管理員明確編輯，不擴建另一套設定架構。

**步驟 5 小提交 5（本輪）：** 原 `SettingsForm`「CYID 公司卡」加入超級管理員明確編輯（臨時簡潔表單，不建新設定系統）。只以 GET 取得 D1 權威統編與版次，雲端金鑰從不預填。空欄 keep、明確勾選才 clear、填新值 replace；二次確認後恰好一次 UpdateAsync。後端 CYID 超管／Device／revision／Audit 保護保持不變；逾時／衝突只讀回最新安全狀態，不自動重送，無本機同步。Core 合成測試加入 keep/clear 及 stale revision。**Draft Source，本輪 CI 須 exact-head 驗證，正式 Cloud 資源未操作。**

**步驟 5 小提交 6（本輪）：** 既有 `work-items` 加管理員**唯讀／分頁**清單：`/v1/work-items/list` 在 CYID Manager 身分與有效 Device／Workspace 內使用不可變 `created_at+work_item_id` keyset，最多 50 筆／頁，僅輸出 work item ID、環境、種類、狀態、revision 與時間；不輸出任何原發票、明細、reference hash、actor 或 secrets。不新增 D1 table/schema/Worker/Cron。Cloud SQLite 合成測試覆蓋 2 頁、權限、惡意範圍與欄位遮蔽。**尚未實作上傳異常事件入庫、`UPLOAD_ISSUE` 類型及 WinForms 來源串接**；此回合不宣稱跨機上傳問題已完成；CI 以 exact HEAD 為準，正式環境不變。

**步驟 5 小提交 7（本輪）：** 沿用既有 `CloudWorkItemClient` 加入 Manager `ListAsync`，對齊前筆 Cloud Work Item List：最多 50 筆、固定資料投影、keyset cursor，不傳來源雜湊或買方明細，不允許 Windows 指定 Workspace；任何多餘敏感欄、偽造狀態、順序／cursor 不一致均拒絕。原 Cloud Core 合成測試覆蓋兩頁、資料邊界、非法類型及不可信回覆。**只補 Windows Core 契約**，不建 UI、不更動 Worker、D1 或正式資料；上傳異常來源、入庫和跨機 UI 仍待下一小項。使用者既有節奏為小功能→CI→Artifact，**Release 只在明確要求時才做**。最新提交須看 exact HEAD CI。

### 不可跨越的界線

V3 SQL 0015～0019 仍是 `cloud/drafts`，**尚未遷移正式 D1**。不可建立測試 Worker、部署正式 Worker、合併 PR、對真實光貿 g0401／g0501 操作，或從合成 CI 宣稱 V3 已完成。出現 `sending`／作廢結果不明，查回原單號，沒有官方及 Audit 證據不得改號、再送或結案。既有 AUTOINPUT 獨立 PR #445 不應混進 V3。每輪只完成一個小提交、CI、回報，再決定下一輪，避免對話逾時。

## 2026-10-15 V3 最新工程交接：折讓作廢 D1 草案與官方 List 對帳（Draft）

接續 PR #440，Cloud 新增 `amego-allowance-void.ts`（g0501 單次請求，絕不自動重送）、`amego-allowance-status.ts`（僅唯讀查回官方類型/99 成功狀態）、`amego-allowance-list.ts`（折讓日/建立日分頁，只產生既有例外 Work Item 的選擇性詳查提示）。三者均受限於 **Draft Source**，尚未透過正式 CYID Manager route 暴露操作，也沒有連結正式 Cloud D1 或光貿。

`drafts/0019_allowance_void_workflow.sql` 的申請、核准、送出 Claim、官方查回稽核全為原子來源比對及 INSERT-only／不可竄改紀錄；唯一 send claim 不能刪除，阻止 Worker timeout 後第二次 g0501。官方結果可疑／查無資料只回「待查」，不代表可以改號、重送或結案。草案 CI 使用 Node SQLite 和攔截的合成 HTTP，**尚沒有 Work Item 對外路由、Windows g0501 UI、正式 Migration、官方定期雙 List 實作或真人多機驗收**。

下一階段實作次序：CYID protected ALLOWANCE_VOID create → 管理員官方確認與 append-only approval → D1 single send claim＋g0501 一次性呼叫 → 原號 `allowance_status`/query 和觀測 Audit → Windows 結案操作；同時完成 V3 0015～0019 schema 升級演練、公司卡安全憑證治理、所有 DR 腳本與 Windows 操作驗收。切勿依舊段落日期較早的「尚未有 g0401」紀錄判定此刻功能；**目前 V3 仍非正式可使用版本，無任何正式發票操作**。

## 2026-10-15 V3 接續：稅額安全核對與原號查回（Draft Source）

`allowance-item-validation.ts` 改依 `FixedDecimal` 規則嚴格比對申請／官方行內 `數量 × 單價 = 小計`，稅額 5% 的整數元四捨五入及原發票稅額/淨額/總額相互一致；避免明細錯價／錯稅額在單純不超額判斷下誤為符合。新增獨立官方 `/json/allowance_query` 的唯讀模組，從 D1 ledger **原號**透過服務端 CYID 公司設定查詢，不接受瀏覽器更改折讓號碼。新增 `number-readback` CYID Manager 入口及 Windows `ReadbackNumberForManagerAsync`；真實 API timeout、缺單、外部碰撞或跨原發票資料都不會自動換號／重送／核准／確認。回覆固定 `safeToAutoResend=false`、`safeToAutoConfirm=false`，Ledger 不變。

上述只為 V3 #440 的 Draft Source + Node SQLite／Windows 模擬測試；仍沒完成含稅換算、歷史折讓餘額、最後 preflight、Manager Grant 寫入、g0401/g0501 實際串接、正式 D1 migrations、雲端部署與 WinForms UI。所有開票作廢相關正式驗收必須額外完成；詳見唯一 TODO 及最新 PR CI。

## 2026-10-15 V3 續作：官方品項與折讓申請保守比對（Source Draft）

新的 `allowance-item-validation.ts` 為單張原發票／尚無已存在折讓／官方明確 `detail_vat=0` 的保守核對器：BigInt ×10^7 比對原日期、品項、課稅別、未稅單價、數量與小計、申請稅額及總額，重複折讓請求行會累加防超量。正式 `invoice_query` 的 `wait[]` 只要有尚未處理異動（包含 D0401、C0501）即標 `officialPendingMutation`，阻止核對通過。受 CYID 保護的 `/v1/allowance-applications/official-inspection` 及 Windows Core 客戶端只讀取／顯示 `itemCheck` 安全原因代碼，不傳原發票全部明細與公司密鑰。

**不得將 matched 當作 approval**：已有折讓跨多原發票的精確餘額分攤、B2C 含稅 `detail_vat=1`、稅額/四捨五入、動態最終查核仍未完成；回傳依舊是 `canApprove=false`、`requiresPerItemVerification=true`，尚未啟用任何核准 Grant、g0401、正式 D1 migration、Cloud Worker、WinForms UI 或 Release。實際光貿未被呼叫；測試使用攔截的合成網路回覆。後續驗收依唯一 `docs/TODO.md`。

## 2026-10-15 官方唯讀 invoice_query 介接（Draft Source）

CYInvoice V3 #440 的 `amego-invoice-query.ts` 已採光貿官方規定的簽章／表單編碼與唯讀 `invoice_query`，限一次 POST、timeout 與回覆長度，並以原發票號碼、明確 `allowance[]`、作廢及 C0501 待處理旗標判斷官方狀態（超過 180 天、官方缺欄／拒絕不可冒充無折讓）。受保護的 `/v1/allowance-applications/official-inspection` 必須由現行 CYID Manager、有效 Cloud Device、服務端公司密鑰及不可變 Work Item 才能觸發，回覆為狀態摘要與 SHA256；Windows `InspectOfficialForManagerAsync` 只接收未核准唯讀狀態。Cloud 合成測試會攔截所有網路請求，用假的光貿回覆驗證簽章，**沒有使用真實公司憑證、沒有連正式光貿、更沒有官方折讓開立**。

**Critical gate**：目前 `canApprove=false` 且 `requiresPerItemVerification=true`，即使官方上傳為 99、未作廢、allowance 為空亦然。不可將現有只查總額及摘要視為品項／稅額／剩餘折讓額驗證完成；真正管理員 approval writer 和 g0401 執行尚未串接，0015～0018 Draft 無正式 D1 migration。詳細未完成清單回寫唯一 TODO。

## 2026-10-15 V3 續開：折讓管理員預覽與永久 Audit 防護（Draft Source）

已在 CYInvoice V3 #440 加入 `/v1/allowance-applications/review-preview` 與 Windows Core 的 `PreviewForManagerAsync`，以 CYID Manager + 已凍結 Work Item 的 D1 revision、公司版本、環境與 AES-GCM/SHA256 驗證取得白名單品項（非原始私密 JSON）；預覽明確標示 `officialVerification=NOT_PERFORMED` 及 `canApprove=false`。無官方 Gateway 查驗也無 g0401 權限，請勿將預覽視為「已覆核」。

Draft 0016 補 Work Item 未送畢的 `reserved/sending` 關閉柵欄、折讓號碼永不刪改、核准 Audit INSERT-only trigger；expired grant 合成測試改為模擬時間前進，不更改前筆核准歷史。0015～0018 仍在 Draft、**未跑原 D1 migration／Worker deploy／正式光貿送出／Release**。其餘下一步見唯一 TODO；所有成功／失敗以精確 PR head 的 Cloud/Windows/Governance 檢查確認。

## 2026-10-15 最新接續：CYID Protected APIs／Windows 公司卡傳輸（Source Draft）

在原 V3 #440 延伸原 SQL 0017／0018、加密與 Work Item 模組：`cloud/src/allowance-applications-route.ts` 加入受保護的 create/get/list，USER 只能查看自己的例外申請，ADMIN／SUPER_ADMIN 可分頁檢視 Workspace 內待審折讓；`cloud/src/company-settings-route.ts` 僅允許 CYID 超管變更公司卡、一般 ADMIN 唯讀設定狀態。兩者均由 `app.ts` app-scoped `withCyIdSession` 及 Device authorization 接入；CYID Identity Workspace 和 CYInvoice Business D1 Workspace 僅以 server binding 對應，不採 browser 欄位。加上 SQL 0018 插入 trigger，杜絕 Work Item／申請 batch 的半筆落盤；Windows 新增 `CloudCompanySettingsClient`／測試，舊 Work Item 客戶端拒絕不帶 frozen source 的折讓建立。

**工程與正式環境界線：**前述 endpoints 仍為 Draft（依 0015～0018 draft schemas 與未注入的私有加密金鑰，正式 Worker 不可用）。真 CYID/AMEGO live、WinForms card／申請／覆核、金鑰版本同步、官方 per-item verification、管理員核准執行及 g0401 皆未接通。即使 GitHub source CI 通過，也不得宣稱跨裝置折讓可正式使用；無 schema 變更、Worker deployment、光貿 mutation 或 Windows Release。依根規則和唯一 TODO 記錄未完成 gate。

## 2026-10-15 本輪接續：公司設定權威與不可變折讓申請（Draft Source）

CYInvoice V3 Draft #440 新增 `0017_company_settings.sql`、`company-settings.ts`（加密公司卡、CYID 超管重驗、Device/Workspace SQL 柵欄、revision CAS、append-only Audit、ADMIN 不讀秘密）與 `0018_allowance_applications.sql`、`allowance-applications.ts`（USER 可建立內部 frozen ALLOWANCE Work Item、加密完整未編號模板、Server SHA256／申請人、公司版本與原發票綁定、不可修改／刪除）。AMA/AMEGO 專用內部讀取只取得 App Key，**不連帶解密 MO Excel 密碼**。通用 Work Item create 已拒絕 ALLOWANCE 的 client-provided hash，未正式啟用新 route。

這兩項仍**只有內部模組與合成 SQLite CI**，未接光貿官方餘額驗證、Cloud protected API、Windows UI、DPAPI Device Cache、失聯公司設定版次回收、D1 migration／實際部署或真實 API。原工作單 Draft 0015、approval／ledger 0016 仍沒有正式可操作路徑；不得視為跨機折讓已上線。詳細待辦見唯一 TODO 開頭；精確 head/CI 應以 [PR #440](https://github.com/simonliu1118-byte/CYapps/pull/440/checks) 的最新執行查核。

## 2026-10-15 CYWEB 災難復原 Source blocker 已交回；V3 D1 折讓配號 Draft 新進度

[CYWEB #151 原交接](https://github.com/simonliu1118-byte/chihyuan-web/blob/cyweb/feat-cyinvoice-recovery-auth-gate/docs/development/CYINVOICE_RECOVERY_HANDOFF.md) 在 10/14 指出 CYInvoice `revoked` 不明確；截至 10/15 **CYInvoice recovery-issuer.ts 已修復，並由 recovery-issuer.test.mjs 覆蓋真 SQLite 明確撤銷、已 consumed／expired、重複查回、D1 回覆遺失與 claim/revoke race**。已在 CYWEB Draft #151 留交回留言、更新該檔及其根 TODO。原問題已可標 Source complete，**不要刪交接文件或宣稱正式部署 DR 完成**；DR 全矩陣仍由此 V3 順序最後集中驗收。

CYID Cloud 折讓配號下一步已新增只在本 Draft 的 `cloud/drafts/0016_allowance_number_ledger.sql`／`src/allowance-number-ledger.ts`、synthetic SQLite test。其 D1 草案 ledger 記錄 exceptional ALLOWANCE 工作單 per-attempt 號碼／request SHA256／狀態／執行 Device／不可回收序號，而不存一般 invoice rows、原始 g0401 payload、公司密鑰。工作單目前 `review` revision 與 active Device/Workspace 只作**底層 SQL execution-time fencing**；申請資料完整核對、CYID manager online revalidation、真正批准狀態與 g0401 API 執行尚未接入，不可把 `review` 當批准信號；也未正式套用 migrations 0015／0016 或觸發 AMEGO。Local 已有獨立 SQLite Ledger；兩版需在最終共同 API 覆核狀態機上整合。CI 依此處修改後的**最新 PR head**證據為準。

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

## 2026-10-15 光貿網站外部操作之官方回補：最新低 API 量方案

**最新確認：不再背景逐張查 `invoice_query`。** 使用者指出三臺 Windows 各掃兩期發票，逐張 API 呼叫量過高。官方 [API 文件](https://invoice.amego.tw/api_doc/) 的 `invoice_list` 有 `cancel_date` 可發現原發票作廢，**但沒有折讓欄位／折讓異動旗標**；`allowance_list` 有 `allowance_number`、`invoice_type`（如 D0401／D0501）、`invoice_status`、`allowance_date`、`cancel_date`、`create_date` 與 `product_item[].original_invoice_number`，可直接把官方折讓關聯回本機發票（同折讓單可能有多個原發票號）。`invoice_query`／`allowance_query` 只在發票詳情被打開、列表無足夠資料、對應不明或待處理狀態需查明時精準使用。**絕不能把實際作廢 API `f0501`／`g0501` 當成查詢 API。**

沿用現有 `InvoiceAutomaticSyncService` 每日首次當期＋前一期、當日其後近三日的同步節奏；一次先取**兩種分頁 List**（官方上限 500 筆／頁），對照原本地 SQLite 快照：原發票 `cancel_date` 變化與折讓號碼、作廢時間／類型／狀態變化；僅保存真的改變的既有紀錄，失敗／漏頁保留最後可信結果，新增折讓的官方資料仍只存在本機（不新增業務 D1 發票索引）。三臺 Device 各打少量列表頁，先不新增雲端共用同步服務／D1 折讓總表來減少額外架構與 token 成本。現有 `InvoiceSyncService.ApplyList` 已可反映原發票作廢；**`IAmegoGateway.ListAllowancesAsync`／`allowance_list` 分頁及合併尚未實作**。`InvoiceAllowanceMetadata.ApplyQuery` 已能保存官方折讓資料，但 `InvoiceSyncService.Fingerprint`／`InvoiceDetailRefreshService.OfficialFingerprint` 缺折讓快照，需修正儲存與 PDF Cache invalidation。

**日期／延後作廢限制：**官方 `allowance_list.date_select` 只有 1 折讓日期、2 建立日期，**沒有 documented last_modified 或作廢日期篩選**。每日較寬範圍回掃＋針對已知舊折讓之 `allowance_status` 批次／精確回查是必要補救，不能永遠只列近三日就保證較早開立的折讓今日被作廢也能發現。`invoice_query` 原發票日期 180 天限制不得推論為「無折讓」。晚於本機 retention 的發票如何查閱仍屬有限範圍限制，不用 D1 存所有發票解決。

**Audit 權威：**管理員因 D1 離線、光貿正常而直接在光貿網站人工作廢發票／開立折讓／作廢折讓，CYInvoice 回查後只更新**本機官方狀態**，標記光貿官方來源；**不可虛構 D1 的 CYInvoice 執行操作 Audit／操作者／覆核紀錄**。若已有 CYInvoice Work Item，原本的申請／覆核 Audit 保留，回連後可更新原工作單的官方核對狀態；沒申請的發票不因此進 D1。Local 同樣支援低呼叫量官方同步，不依賴 D1。完整 E2E 尚未驗收、未部署。

## 2026-10-15 新增：管理員折讓申請覆核時即時顯示官方折讓

使用者要求：管理員開啟**同意／退回折讓申請的覆核介面**，應當場向光貿查**這張原發票已存在的所有折讓／作廢折讓狀態**，讓管理員看過最新實際情況後才核准。**API 名稱要修正**：`/json/allowance_query` 只能用已知 **allowance_number** 查一張折讓；正確的入口是以原發票號碼呼叫**一次 `/json/invoice_query`**，呈現 `data.allowance[]`，必要時才針對個別折讓單號補 `allowance_query`，無須開窗時取整批 `allowance_list`。顯示：原發票與作廢狀態、折讓單號／日期／種類／官方上傳狀態／金額、本次申請與申請人、查詢時間；明確區分已開立有效、已作廢、待處理、錯誤。若需計算**這張原發票**剩餘可折讓額，應依該發票的品項／稅額核算：光貿單張折讓可含多張原發票，`allowance[]` 的折讓單總金額不一定全屬當前原發票，必要時按已知單號補查 `allowance_query.product_item[]`，不能直接總加。

開窗查詢不可阻塞 Windows 主執行緒；等結果回來再允許核准。API 失敗、超過原發票日期 180 天查詢期限、已作廢／仍有不明狀態等無法安全核准的情況，顯示「官方資料無法確認／需處理」，**不開放正式核准送出**（退回仍可操作），不可把本機快取冒充最新結果。實際按下核准並呼叫 g0401 前，仍須按最新官方狀態、剩餘折讓額、當下 actor 權限、Work Item revision／D1 原子分配做 final preflight，避免兩臺裝置同時核准。同一 Cloud 管理員電腦不一定有申請人本機原發票 SQLite 紀錄，因此此查詢必須可用 Work Item 原發票號碼／授權公司設定直接取得 Gateway，不可硬性綁在 `InvoiceDetailRefreshService.RefreshAsync(local record)`。現有 `SyncIssuesForm.AllowanceDetailRows` 僅顯示本機已保存的 `InvoiceAllowanceMetadata`，**尚未在開窗時即時呼叫 API**，這項屬後續 V3 UI／Core 接線與驗收；不影響前述低頻背景雙 List 對帳方案，亦不為外部光貿操作新增 D1 Audit。

## 2026-10-15 V3 當次程式接續：Built-in active path 收斂＋g0501 Gateway

**最新 Source 方向／尚未部署：**
- `cloud/src/app.ts` 的 Worker entrypoint 現只允許 `CYID_ENABLED=true` 且具有效 CYID Binding；非 CYID mode 409 `CYID_REQUIRED`，原 Built-in Employee CRUD、密碼、WEB Auth、Local→Cloud transition handler 不再由 V3 dispatch。舊路由回 409 `IDENTITY_PROVIDER_MISMATCH`，不能將 404 誤當安全回絕。CYID Device join、正常 Device 管理、Work Items、Recovery 等共用路由保留。
- `cloud/src/work-items.ts`／`work-items-route.ts`：刪 Built-in `cloud_employees` verifier SQL／PBKDF account fallback，CYID manager 身分操作前後重新驗證、D1 寫入時仍檢 active Device／Workspace。仍只有 Draft 三端點與未套用 `drafts/0015_work_items.sql`；工作單明細／申請者權限／g0401 execution lease／配號未接，不可稱跨機正式覆核完成。V3 Cloud test `cyid.test.mjs` 與 `work-items.test.mjs` 已改驗證 CYID-only 與 Built-in fail-closed。舊 migration 仍是歷史，絕不自行操作原 D1。
- `src/CYInvoice.Core/IdentityProvider.cs`／`LocalRepository.cs`：runtime constructor 只接受 Local／CYID；Windows 不再實例化 Built-in provider，不會下載舊 Cloud Employee 快取作為登入 authority；首次加入只用 CYID Direct Join。歷史 `BuiltInCloudIdentityProvider` type、Cloud Setup 舊建置 code 與特定測試 source 尚在等待最後殘餘清理，不能直接刪掉 ServiceConnectivity／Local Reset／Device 等仍被 CYID 用的類別。
- `AmegoClient.VoidAllowanceAsync` 新增正式 `/json/g0501` 單張折讓作廢 API，一次只送 `[{"CancelAllowanceNumber":"已知折讓單號"}]`，不得新增折讓號碼、不得在 timeout／未知結果盲重送；既有網站折讓號亦可查／作廢。已新增 synthetic HTTP payload／驗證／拒絕測試，僅 Gateway 完成，沒有 WinForms 按鈕的正式 API 接線或 live 真實光貿成功證據。
- 過去 source 新增 `ListAllowancesAsync`、`LocalAllowanceNumberStore`、`allowance_status` array 兼容及 allowance fingerprint；皆是底層階段成果。**未做 D1 原子配號與 Work Item 交易執行，未做官方兩份 List 完整同步，未做公司卡片、DR 真實全項驗收或 V3.0 Release。**

**規劃順序仍以 TODO「V3.0 正式交付工作順序」為準。** Worker／D1 遠端未改、Windows Release 仍 V2.6.19；CI 以精確 GitHub PR head 查詢，不把已取消／較早 CI 當成最新通過。

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
