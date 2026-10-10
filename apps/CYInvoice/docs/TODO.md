# CYInvoice 待辦與驗證

本檔追蹤目前未完成、需要後續驗證或已明確延後的工作；7.3 保留本輪完成核對項供切換 gate 追溯。已完成內容與歷史決策由 README、PR、測試與設計文件保存。

更新日期：2026-10-10（Asia/Taipei）。main 為 V2.6.10 Build 2／a04f706c（治理 #381 後）；目前工程候選版 **V2.6.15** 為 CYID Consumer source，前置 V2.6.14 PR #216 仍未合併。最新正式 Release 仍為 cyinvoice-v2.4.2。

唯一現行交接：[CLOUD_WORK_HANDOFF.md](CLOUD_WORK_HANDOFF.md)；唯一步驟清單：[RC_TEST.md](RC_TEST.md)。9/29 NEXT_CHAT_HANDOFF 與 V2.5 設計均為歷史資料，不能重新開啟已完成工作。接手前仍依 AGENTS.md 先讀三層永久規則，本檔不是額外規則層。

Cloud source 0.9.0／API 1／compatibility marker 8／storage 13；未部署。最後 development 0.8.9／storage 12 的 development staged **10/10 Run #8 attempt 2** 已通過 migration／aggregate／FK／health／新 capabilities，0012 已套用且無 pending migration。CI 成功不等於實機驗收或遠端部署。

## 0. 接續優先順序

| 優先 | 未完成工作 | 驗收位置／依賴 |
| --- | --- | --- |
| 目前 | V2.6.14 裝置管理、使用版本／最後使用與改名；保留 V2.6.13 介面 | RC Z／Y；source CI 已通過、人工尚待驗收 |
| 目前 | V2.6.12 邀請首次加入與首次說明 | RC X；舊包重開成功有使用者證據，修正版首次成功待實機 |
| 目前 | V2.6.11 處理中／上傳問題分流、舊資料結案、Danger button／號碼隱藏 | RC W；Windows #258 通過，人工待測 |
| 目前 | A/B 最新工程版基線與即時中央權限／Offline reconnect | RC Q／R；四包已實作，不再重做 |
| 目前 | 可拋棄 C 的 revoke／reset 與 ambiguous 恢復 | RC S／T／U；A/B recovery path 保留 |
| 接續 | Invitation、Employee identity matrix／CRUD／Email／password recovery／transfer | 7.2；使用最新測試包，舊 Run343 Artifact 已到期 |
| 本輪 | CYID Consumer source CI／Windows 實機／隔離 staging 與切換計畫 | 7.3／RC AA；provider／gateway／offline 已接線，正式切換及 0-Device recovery 未完成 |
| V3 協同 | Cloud Work Item／原子結案／revision、多機 OrderID 防撞、Audit | 7.5／7.6；identity gate 後分項開發 |
| 後續 | 正式折讓 API／全域單號、自架手冊、酷澎樣本 | 5／6／9；不可用人工流程冒充完成 |

- [ ] 完成 RC Y 的介面實機驗收（96 DPI）；125／150 DPI 與 High Contrast 尚未取得證據。使用版本／最後使用為成功啟動回報，不代表即時在線；持續 heartbeat 不在本次範圍。
- [ ] 完成 RC W 的實機驗收並記錄包／環境／結果：正常等待→完成、等待→錯誤→恢復、數量／公司隔離、舊折讓 null 分類、無 active upload issue 的作廢仍能管理員結案。
- [ ] V2.6.14 裝置管理與既有介面、邀請與既有分流驗收／必要修正收斂後，依當時 main 與精確 head CI 整合 PR #216。本次另修邀請 UI 空物件錯誤；目前尚未合併。

本檔既有未勾選實機項目代表「尚未取得可引用證據」，不等於已發現 defect。已完成工程證據集中在交接／CHANGELOG，不把 CI 自動勾成人工成功。

- [ ] 完成 RC Z：active 清單、獨立新增視窗標籤／欄位、A/B 升級重啟回報、離線值保留、超管改名與 revoked 歷史仍可由雲端管理查詢。

## 1. V2.6.x 實機與光貿驗證

- [ ] 全新首次設定：建立超級管理員、密碼規則、一次性復原碼與重新啟動。
- [ ] 首次設定尚未完成、尚未建立超級管理員時，主畫面右上角光貿連線 Tag 先顯示灰色「連線中」；完成首次設定後才執行既有光貿 API 連線檢查，再切換為綠色「光貿連線正常」或紅色「光貿連線異常」。
- [ ] 帳號管理／權限驗證：一般使用者、管理員、超級管理員的建立、修改、停用、重設與禁止操作邊界。
- [ ] MO店+ 未設定 Excel 密碼時，按 MO店+ 必須先提示設定且不得先開選檔視窗。
- [ ] 設定選單持續開啟至少 30 秒，確認不再連續閃爍。
- [ ] `設定 → 系統診斷`：重新檢查、API／SQLite／WebView2／Excel／印表機／同步狀態與複製摘要均符合實機狀態，且不顯示 App Key、密碼、發票內容或完整本機路徑。
- [ ] 測試環境實際送出直接作廢 `3015-消退` 與管理員覆核作廢 `3001-3015-消退`。
- [ ] 保留完整 `invoice_query` JSON，確認 AMEGO 是否回傳可解析的 `CancelReason`／作廢原因；目前不得假設未文件化欄位存在。
- [ ] 實測紙本未收回人工確認：一般使用者不能批准，管理員可批准／取消，批准後才送作廢。
- [ ] 實測作廢確認開啟期間，背景已開立清單所有可見發票號碼持續隱藏，即使背景重新整理也不會重新顯示。
- [ ] 實際建立一筆人工折讓，以 `invoice_query.allowance[]` 驗證同日舊／新折讓辨識、含稅金額比對與多候選保守停止。
- [ ] `/json/allowance_file` 三種版型 A4、A4 (地址+A5)、A5 均能取得有效 PDF 並由既有 WebViewer 正常開啟／列印。
- [ ] 折讓官方資料發生狀態／日期／類型／稅額／金額變動後，再次開啟 PDF 必須使用新官方資料指紋，不得命中舊 Cache。
- [ ] 「折讓作廢」暫行人工流程：一般使用者可提出，管理員可於「上傳問題」完成或取消退回，現階段不得直接呼叫 `/json/g0501`。
- [ ] 「上傳問題／處理中」兩清單正確分流、數量與明細一致；只有上傳問題內 Failed 可勾選清除，正常等待不能刪除／手動標成功。
- [ ] 超過兩期的發票作廢、折讓、折讓作廢待辦均顯示「可結案」，只有 ADMIN／SUPER_ADMIN 可結案；結案後只停止本機追蹤並恢復 retention 清理資格，不代表光貿已完成。

完整操作步驟見 `RC_TEST.md`。

## 2. 待取得實機資料後再決定

- [ ] **折讓作廢人工完成後的官方確認策略。** 目前由管理員確認已完成光貿網站操作後按「已人工處理」結束本機待辦；取得真實 `invoice_query.allowance[]` 折讓作廢樣本後，再判斷能否可靠地先回查官方狀態，無法確認時維持 pending。
- [ ] **CancelReason 回查。** 取得真實 invoice_query 樣本後，再決定完成作廢歷史能否跨重新啟動解析使用者／覆核管理員／原因；沒有官方欄位就不自行保存一套永久作廢歷史。

## 3. 已完成工程證據（不重排為 TODO）

V2.6.14 功能 source db1a3b95：Governance #1171、Cloud #377、Windows #266 全部通過，含 client contracts、WinForms label 測量、完整回歸、packaged smoke／safety scan。Development staged Run #8 attempt 2 已驗證 Cloud 0.8.9／storage 12／usage／rename；0012 一次套用，撤銷歷史保留。後續僅文件更新不推進 VERSION／BUILD，精確 head 以 PR checks 為準。


功能 commit 66ec3671 已通過 Governance #1137、Cloud #369、Windows #258。Core／Void／Employee void workflow（29/29）／Allowance／SQLite／Sync／SyncCoordinator 與 Windows smoke／package 全綠；歷史行政結案契約已修正，source TODO／FIXME／NotImplementedException 盤點沒有另找到明確未實作 placeholder。

人工折讓／折讓 PDF／人工折讓作廢／系統診斷已實作；Identity Foundation、Online Authority Freshness、Device inventory／revoke、crash-safe Cloud → Local reset 已合併。Built-in Cloud Email 忘記密碼已有 API／Windows UI／client，不再列為未實作，仍需 live 驗收。版本／CI／deployment 的精確證據與功能邊界見 CLOUD_WORK_HANDOFF.md。

## 4. V2.4.x／既有同步與 Cache 實機回歸

- [ ] 以 V2.3.0 真實 `Data` 備份升級到現行 SQLite，確認 DB 建立、舊 JSON 保留、資料筆數與內容一致。
- [ ] 正式環境 recent 3-day sync，確認可抓到另一台電腦／光貿端合法更新。
- [ ] 程式持續開啟超過 5 分鐘，確認啟動同步／每 5 分鐘同步不重疊，手動重新整理維持 30 秒冷卻。
- [ ] 每日第一次同步執行目前＋上一期完整校對，不影響 recent 3-day 核心。
- [ ] 雙擊不同日期發票都先 `invoice_query`；query 無法確認時不得把舊 Cache 冒充最新資料。
- [ ] 測試環境 namespaced OrderID 不與共享測試池其他資料撞號，UI 仍顯示原始可讀 OrderID。
- [ ] 發票 PDF／Preview Cache 不保存短效 `file_url`，retention 清理時同步移除對應 Cache。

## 5. 酷澎樣本補齊

- [ ] 未出貨／不同狀態樣本。
- [ ] 公司統編訂單樣本。
- [ ] 多商品訂單樣本。
- [ ] 數量大於 1 樣本。
- [ ] 折扣／負數調整樣本。
- [ ] 取得樣本後補 parser、金額、防重與測試案例。

## 6. 正式折讓 API（雲端化階段）

目前仍以人工折讓／人工折讓作廢待辦為主；完成紀錄以 `invoice_query.allowance[]` 為官方依據。

- [ ] `/json/g0401` 折讓開立：CYInvoice 直接送出，不再要求管理員至光貿網站人工建立。
- [ ] `/json/g0501` 折讓作廢：正式自動化後只允許 ADMIN／SUPER_ADMIN 直接執行；送出前重新確認最新折讓狀態，結果不明不得自行標成成功。
- [ ] `AllowanceNumber` 由 CYInvoice 自動產生，建立全域唯一編號與跨裝置防重機制。
- [ ] `allowance_query`／`allowance_status` 正式查詢與同步模型；目前刻意共用 `invoice_query`。
- [ ] 部分折讓、多次折讓、品項／數量／金額規則。
- [ ] 發票與折讓之間的 idempotency、timeout、結果不明與同步安全規則。
- [ ] 自動折讓／折讓作廢時，確認 AMEGO 正式可保存且可依折讓單號查回的欄位，再決定如何保存 `管理員編號-使用者編號-原因`；若官方沒有合適欄位，不濫用未文件化欄位。
- [ ] 雲端化完成後移除暫行人工折讓與人工折讓作廢路徑，收斂為正式 API 流程。

`/json/allowance_file` 折讓 PDF 已完成工程實作，後續只需實機驗證與雲端化時確認多裝置 Cache 行為。

## 7. CYInvoice V3.0 雲端協同

完整長期定位與分期見 `CLOUD_ROADMAP.md`；Workspace／Device／Token／Employee authority 定案見 `CLOUD_IDENTITY_LIFECYCLE.md`；CY ID consumer 邊界見 `CY_ID_INTEGRATION.md`。

### 7.1 已完成 foundation

Workspace／protected Device identity、pairing／invitation、whole-device transition、中央 Employee CRUD／OTP／transfer／password recovery、USER role migration、provider abstraction、freshness、revoke 與 reset 均已實作。9/29 development staged deploy Run #7 與 migration／aggregate／FK audit 有既有證據；A 機連線與 B 機 Run255 pairing 曾有人工作業證據。後續驗收必須用最新包，不沿用歷史版本成功推定新版本全通過。

### 7.2 目前剩餘驗證／修正

- [ ] **目前候選版 A/B baseline acceptance：**兩台既有 Device 升級 V2.6.14 通過 CI 的工程包後仍可連線，Device Management 清單、一般同步／開票既有路徑無回歸。
- [ ] V2.6.12 邀請第一次加入與首次說明實機驗收（RC X）。10/09 舊包已取得「第一次錯誤、重開成功」證據，已定位 password.Clear → TextChanged → preview=null；修正版單次完成尚待使用者驗證。邀請撤銷／重寄與其他不明結果仍待實測。
- [ ] 精確命中、全新 Employee、Employee No only、Email only、兩欄各撞不同人的實機／integration 測試。
- [ ] **Authority freshness A/B 實機驗收：**A 修改中央 Employee 密碼／role／enabled 後，B 不等待背景同步或重開即可在下一次 protected operation 套用最新 authority；真正斷網時使用最後可信 cache，恢復連線後 Online authority 重新覆蓋。
- [ ] Built-in Employee CRUD、Email OTP、password、enabled、role 在 A/B 間即時與背景 snapshot 同步實機測試。
- [ ] Built-in Cloud「忘記密碼」員編／已驗證 Email challenge、重寄倒數、OTP／新密碼 confirm 及 A/B 舊密碼失效實機驗收；工程已實作。
- [ ] SUPER_ADMIN transfer 的 execution-time re-auth／OTP／原子 X→ADMIN、Y→SUPER_ADMIN／Recovery Email 與 A/B authority 驗收。
- [ ] **Cloud → Local 實機驗收：**優先用可拋棄的 Device C，在 A/B 仍保留 Workspace recovery path 的前提下測 current-device reset；確認 A/B、Workspace、中央 Employees 完整保留。
- [ ] **Device revoke A/B/C 實機驗收：**新增可拋棄 Device C，從 A/B 遠端 revoke C，確認 C 舊 Token 立即失效、history 保留 revoked、重新加入產生新 Device identity。
- [ ] **LAST_ACTIVE_DEVICE 實機驗收：**只在專用可拋棄 Workspace／環境驗證，不為測試而破壞目前 A/B 唯一 recovery topology。
- [ ] **Ambiguous revoke fault-injection：**模擬 Cloud 已執行但 response 遺失／狀態暫時無法確認，確認不 wipe、本機資料／Token 保留，下一次啟動由 self-status 收斂。
- [ ] **尚未實作：**所有 Device Token 遺失但 Recovery path 可用時的 Recovery Device flow。
- [ ] **尚未完成文件：**所有 Device Token + recovery path 同時失效時的 reference-backend 人工維運程序。
- [ ] 驗收 fresh-install 首次分流。
- [ ] **延後／非目前阻塞：Cloud Employee offline cache 完整性簽章。** 未來可評估 server-signed snapshot／等效完整性保護；沒有實際竄改事件、威脅模型提高或稽核需求時長期擱置。

### 7.3 CYID Consumer（V2.6.15 source 已實作，正式切換未完成）

設計與授權／限制見 CY_ID_INTEGRATION.md §14，PR #379 的 §13 保留原交接快照。Canonical 1.0.2／minimum 1.0.0；同 repo 直接引用，不建 contract mirror。

- [x] 核對 main 94f559cd、PR #216 V2.6.14 head 1e6d4137、PR #379 文件；整合新獨立工作分支，VERSION 2.6.15／BUILD 0。
- [x] CyIdIdentityProvider、集中 provider 選擇、authenticated protected Workspace／Device binding；Local／Built-in 保留，確認 CYID 後不自動回退。
- [x] Worker private IDENTITY gateway、Login／Resolve／finally Logout、穩定錯誤與無 Session／verifier 外洩；Device 與 Employee／App Access 權限分離。
- [x] Device rename／revoke、pairing／invitation 沿用現有 mutation owner；0013 forward migration 區分 external invitation actor，保留 Built-in 歷史／FK。
- [x] last-trusted protected offline cache，僅 transport failure 使用；online reject 清除 cache、reconnect 新權限重生效；不 export CYID verifier。
- [x] Consumer Version 1.0.2、canonical support-window gate、real-provider Worker 與 C# contracts／拒絕／scope／outage／logout loss／onboarding 本機回歸。
- [x] 原 Workspace 接續 source regression：原兩台 Device Token 切換前後可用；本機切換後重開保留 Workspace／Device／Token、發票 pending、買方／PDF、公司設定及 protected credentials；錯綁定不覆寫。

本輪精確 head Governance／Cloud／Windows CI 與 engineering Artifact 以 [#380 checks](https://github.com/simonliu1118-byte/CYapps/pull/380/checks) 為單一即時結果；source／治理證據見 CLOUD_WORK_HANDOFF，不用手動 checklist 鏡像 run 狀態。

- [x] 專案 canonical adoption 依 governance/* 流程完成 #381／Governance 2.3.34，功能分支同步 main；不把規則修改混入一般功能 PR。
- [ ] RC_TEST AA Windows 實機 CYID：Account Management／settings／高權限操作，斷網／reconnect 與角色、App Access、停用、密碼變更後更新。
- [ ] 核對真實 CYID Application 註冊／Workspace enablement／App Access、private Service Binding、兩 Workspace 配對；先隔離 staging，再討論正式切換（未授權）。
- [ ] 受控切換：備份／0013／FK、EmployeeNo 與歷史 actor／pending 業務稽核、rollback／舊 client gate；不以 destructive reset 代替 migration。
- [ ] 切換前所有 active 裝置在原安裝升級至 CYID-capable Windows（本輪 V2.6.15），員工啟用／App Access／Role／verified 超管 Email 備妥；staging 證明原 Workspace、Device、Token、資料及業務接續。不達 gate 維持 Built-in，不要求重建／重新加入。
- [ ] 分別驗收整機斷網、僅 Windows→驗證入口 transport 失敗但光貿可達、private CYID 故障回覆 503；離線驗證不等於離線開票，503 不 fallback，不新增斷網自動重送。
- [ ] CYID 0-active-Device recovery 實作／驗證後才調整 LAST_ACTIVE_DEVICE；目前保留原保護。
- [ ] 若未來要整合 dialog／core 的重複驗證，依單一 operation context 收斂；不保存可重用 Session 或新增持續登入。

### 7.4 新裝置加入方式與安全紀錄

現行只保留配對碼／邀請碼；API URL 由使用者設定，Windows 不內嵌 endpoint／不以 Workspace ID 作一般手動入口。Security audit 已涵蓋 pairing／invitation／join／revoke 的必要事件，相關 live 驗收集中 7.2，不重複勾選。

- [ ] 安全操作紀錄／稽核紀錄查看介面。
- [ ] 所有原裝置皆遺失且無有效邀請的受控災難復原流程；與 7.2 的 Recovery Device flow／人工維運手冊一起收斂。

### 7.5 跨機 Work Item / Sync

- [ ] 作廢／折讓／折讓作廢／管理員結案 Work Item 上 Cloud。
- [ ] Work Item 原子 state transition／optimistic revision，避免多機同時結案。
- [ ] Cloud coordination 只用於真正需要跨機原子性的點，不把 Cloud Lock 變成所有業務前置條件。
- [ ] 離線期間 AMEGO 結果不明操作不得因恢復連線而自動重送。
- [ ] 解決多機離線自動 OrderID 撞號；正式多機上線前需 Device namespace／短碼或等效方案。

### 7.6 Cloud Audit

- [ ] 補齊 Employee、Work Item、SUPER_ADMIN transfer 與維運事件的必要摘要；現有 Device pairing／invitation／join／revoke audit 不重做。

Audit 沿用既有公開／敏感資料邊界，不保存密碼、Recovery Code、OTP、Device Token、App Key 或不必要的完整發票內容；這是持續約束，不另列一個可勾選功能。

## 8. 後續大版本：多公司 Workspace

不列入 V3.0 阻塞項目。

- [ ] 真正有不同統編需求時再新增 Company entity／`company_id`。
- [ ] V3 單公司 Workspace 升級時自動建立第一個 Company。
- [ ] 視實際需求再做 Employee ↔ Company scope、Device 預設 Company、公司切換 UI、跨公司待辦／查詢／報表。

## 9. 對外相容／自架準備

- [ ] Cloud API 穩定後撰寫 CYInvoice Self-hosted / Cloud Integration User Manual。
- [ ] Guide 定義 Built-in Cloud 部署需求、endpoint、schema、Device auth、Employee authorization、錯誤碼、版本、idempotency／reconciliation 語意；不得要求 CY ID 才能完成基本部署。

## 10. 後續增強

Built-in Cloud Email password recovery 工程已完成，實機驗收見 7.2。

- [ ] MO 密碼安全 Cloud sync。
- [ ] 小型營運摘要：今日／本月開票張數與金額、待處理工作數、同步異常數。

AMEGO App Key 不列入 Cloud sync，仍由各電腦自行設定並以 Windows secure storage 保護。

## 11. 延後／非本次阻塞

- [ ] 125%／150% DPI 人工驗證：依 repository visual 決策維持 Deferred；本次 96 DPI 分流驗收不能冒充其他縮放通過。

Offline cache 整體簽章（7.2）、多公司（8）與營運摘要（10）保持既定延後範圍，沒有本次完成承諾。

## 12. 單機版明確不排入

- 本機操作／稽核紀錄：不做；等 Cloud backend 後做跨機 Audit。
- 本機備份／還原：不做；CYInvoice 是中介層，官方資料以光貿為準。
- 發票 Excel／CSV 匯出：不做；需要時使用光貿網站。
- 管理員開機待辦提醒：不做；沒有持續登入，無法可靠知道開程式的人是管理員。

工程 merge 與 development Cloud deployment 在達到對應 CI／Governance／安全驗證 gate 後可依技術需要進行，不應讓已完成的 stacked work 長期停放；**正式 tag / Release 仍視為獨立 promotion 行為，需另行明確決定。**
