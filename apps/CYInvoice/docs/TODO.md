# CYInvoice 待辦與驗證

本檔只保留目前仍未完成、需要後續驗證或已明確延後的工作。已完成內容與歷史決策由 README、PR、測試與設計文件保存。

目前 PR #121 工程開發基準：**CYInvoice V2.6.6 Build 4**；development remote 已部署 **Cloud 0.8.5 / API 1 / storage Schema 9**，A 機既有功能與 B 機配對碼加入均已實機通過。
最新正式 Release：`cyinvoice-v2.4.2`

> V3 Cloud identity 工作接手時，先讀 `docs/CLOUD_WORK_HANDOFF.md`、`docs/CLOUD_IDENTITY_LIFECYCLE.md`、`docs/CY_ID_INTEGRATION.md`，再讀 `CLOUD_ARCHITECTURE_STATUS.md`、`CLOUD_ROADMAP.md` 與本檔。仍必須依 `AGENTS.md` 指示先讀三層永久規則。

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
- [ ] 單一「上傳問題」清單：技術問題、Failed、作廢、折讓、折讓作廢均能正確顯示；只有 Failed 可以勾選清除。
- [ ] 超過兩期的發票作廢、折讓、折讓作廢待辦均顯示「可結案」，只有 ADMIN／SUPER_ADMIN 可結案；結案後只停止本機追蹤並恢復 retention 清理資格，不代表光貿已完成。

完整操作步驟見 `RC_TEST.md`。

## 2. 待取得實機資料後再決定

- [ ] **折讓作廢人工完成後的官方確認策略。** 目前由管理員確認已完成光貿網站操作後按「已解決」結束本機待辦；取得真實 `invoice_query.allowance[]` 折讓作廢樣本後，再判斷能否可靠地先回查官方狀態，無法確認時維持 pending。
- [ ] **CancelReason 回查。** 取得真實 invoice_query 樣本後，再決定完成作廢歷史能否跨重新啟動解析使用者／覆核管理員／原因；沒有官方欄位就不自行保存一套永久作廢歷史。

## 3. V2.6.3 自動化測試

- [x] `AllowancePdfService` 三種官方 style、簽章欄位、可信任 `invoice.amego.tw`、非 PDF 拒絕、code 15 校時重試。
- [x] 折讓 PDF Cache 命中與官方資料指紋換版；官方資料改變後不得沿用舊 PDF。
- [x] `EmployeeAllowanceVoidWorkflowService`：錯誤帳密零 query、只有唯一且已完成折讓可申請、重複申請防重。
- [x] 一般使用者不能完成折讓作廢待辦；管理員完成／取消只改本機且不產生額外 AMEGO 呼叫。
- [x] 折讓作廢超過兩期可由管理員走既有 administrative closure。
- [x] WinForms startup smoke 涵蓋單一「上傳問題」結構與 V2.6.3 系統診斷視窗。

上述測試仍不能取代光貿 live API／Windows 實機驗證。

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

### 7.1 Built-in Cloud 已完成 foundation

- [x] Workspace / Device identity、protected Device Token、retry-safe bootstrap / Join。
- [x] Existing Workspace B-side Join 先做執行時 Local ADMIN / SUPER_ADMIN 驗證，再允許輸入 Pairing Code。
- [x] Pairing Code 授權 Device，不直接授予 Employee role。
- [x] Local → Cloud 採 whole-device Employee Transition。
- [x] Identity matching 使用 Employee No + Email；姓名不作 identity authority。
- [x] Conflict 由目前 Workspace SUPER_ADMIN 當下重新驗證後人工處理。
- [x] 第一台 X 成為中央 SUPER_ADMIN；既有 Workspace 新 Local SUPER_ADMIN Y 若為新中央 Employee，中央 role 為 ADMIN。
- [x] Cutover 後 Built-in Cloud Employee 是唯一帳號 authority；舊 Local EmployeeStore 不再作權限來源。
- [x] Cloud Mode 斷網時使用最後成功同步的 protected offline cache，不切回舊 Local authority。
- [x] 中央 Employee create/name/Email/role/enabled/password API/client/UI foundation 已完成。
- [x] 新 Employee Email 必須驗證；Email 修改時新 Email 必須驗證後才 commit。
- [x] SUPER_ADMIN transfer foundation 已完成。
- [x] Cloud compatibility 已從 D1 schema equality gate 改為 API contract／capability 策略。

### 7.2 目前剩餘驗證／修正

- [x] development staged deployment：migration `0009` 只套一次、Worker `0.8.5` 已部署、health API 1 / storage Schema 9，A 機最終回歸通過。
- [x] B 機以乾淨 Run255 使用配對碼加入既有 Workspace 實機通過。
- [ ] 邀請碼加入路徑、邀請撤銷／重寄與結果不明恢復實機驗收。
- [ ] 精確命中、全新 Employee、Employee No only、Email only、兩欄各撞不同人的實機／integration 測試。
- [ ] **Built-in Cloud Employee 即時驗證 defect：**Cloud 在線時，任何 Employee 密碼／權限驗證前必須取得最新 Cloud authority／snapshot，再驗證並刷新本機 cache；不得等待 5 分鐘背景同步或重開程式。
- [ ] Built-in Employee CRUD、Email OTP、password、enabled、role 在 A/B 間即時與背景 snapshot 同步實機測試。
- [ ] Cloud Mode 斷網：以最後 Cloud cache 做 execution-time auth；恢復連線後 Online authority 覆蓋 cache。
- [ ] **Cloud → Local 破壞性重置：**雙重確認、Device revoke/retire、本機全清、重啟首次使用並重新建立 Local SUPER_ADMIN。
- [ ] Device revoke UI/API；亦作為 Cloud → Local 安全退場的一部分。
- [ ] 所有 Device Token 遺失但 Recovery path 可用時的 Recovery Device flow。
- [ ] 所有 Device Token + recovery path 同時失效時的 reference-backend 人工維運文件。
- [ ] 驗收 fresh-install 首次分流。
- [ ] **Role 正式改名：**CYInvoice `EMPLOYEE` role 改為 `USER`，最終只保留 `SUPER_ADMIN / ADMIN / USER`。目前沒有已持久化的 `EMPLOYEE` role 資料需要 migration；同步修 source、schema fixtures、tests、docs、UI。
- [ ] **延後／非目前阻塞：Cloud Employee offline cache 完整性簽章。** 未來可評估 server-signed snapshot／等效完整性保護；沒有實際竄改事件、威脅模型提高或稽核需求時長期擱置。

### 7.3 CY ID / Self-hosted 架構定案（尚未實作）

- [x] 產品模式定案：Local、Built-in Cloud / Self-hosted、CY ID Cloud 三種均為正式路線。
- [x] Built-in Cloud 必須保留，第三方公司可依 User Manual 自架自己的 Worker／Database／Email Provider／Workspace／權限庫，不依賴 CY ID。
- [x] CY ID Workspace 與 CYInvoice Workspace 不合併；CY ID 管 Employee／Credential，共通 identity；CYInvoice Workspace 管 Device／Pairing／Token／Sync／Work Item／Invoice business state。
- [x] CY ID 模式下「帳號管理」功能直接隱藏；帳號 CRUD、Email、Password、SUPER_ADMIN、CYInvoice access／role 由 CYWEB / CY ID 帳號中心管理。
- [x] CY ID role 對 CYInvoice 直接 1:1：`SUPER_ADMIN → SUPER_ADMIN`、`ADMIN → ADMIN`、`USER → USER`。
- [x] App Access 是 CY ID 的上層入口控制；CYInvoice 不建立第二套 App Access 管理介面。
- [x] CY ID 的 Group、App Access schema 與 CYWEB 帳號中心內部設計不在 CYInvoice 工作線決定。
- [x] 不為 Entra ID／LDAP／Google Workspace／任意第三方 provider 預先建插件框架；需要者可 fork 自行擴充。
- [x] 現階段不建立 Built-in → CY ID 通用 migration framework；正式切換前只做必要 acceptance check。
- [ ] 等 CY ID consumer contract 穩定後，設計 CYInvoice 最小 Employee authority abstraction，避免把 Built-in backend 寫死在所有驗證路徑。
- [ ] CY ID 模式 Windows Offline credential/cache 協定：Online 以 CY ID authority 為準、Offline 使用最後可信 protected cache、reconnect 後最新 authority 重生效；不得直接讀 CY ID D1 或形成雙 authority。
- [ ] CY ID 模式 Account Management visibility / settings status / execution-time auth Windows UI acceptance。

### 7.4 新裝置加入方式與安全紀錄

- [x] 新裝置加入只保留「配對碼」與「邀請碼」兩種方式；移除 Workspace 識別碼＋超管帳密／Email OTP 直連入口。
- [x] A 機立即配對：超管驗證後顯示 API URL + 約 10 分鐘一次性配對碼；B 機確認 Workspace 名稱後加入。
- [x] A 機 Email 邀請：72 小時一次性邀請碼，可撤銷／重寄；B 機使用 invitation code + SUPER_ADMIN credential，確認 Workspace 名稱後加入。
- [x] 程式不內嵌 Cloud API URL；Workspace ID 不作為新機手動輸入欄位。
- [x] Cloud D1 安全操作紀錄涵蓋配對、邀請、撤銷與加入，且不記錄 secret 原文。
- [x] migration `0009`／Worker `0.8.5` staged deployment 完成。
- [x] B 機 Run255 配對碼加入實機通過。
- [ ] 邀請碼加入、邀請撤銷／重寄、result-unknown recovery 實機驗收。
- [ ] 安全操作紀錄／稽核紀錄查看介面。
- [ ] 所有原裝置皆遺失且無有效邀請的受控災難復原流程。

### 7.5 跨機 Work Item / Sync

- [ ] 作廢／折讓／折讓作廢／管理員結案 Work Item 上 Cloud。
- [ ] Work Item 原子 state transition／optimistic revision，避免多機同時結案。
- [ ] Cloud coordination 只用於真正需要跨機原子性的點，不把 Cloud Lock 變成所有業務前置條件。
- [ ] 離線期間 AMEGO 結果不明操作不得因恢復連線而自動重送。
- [ ] 解決多機離線自動 OrderID 撞號；正式多機上線前需 Device namespace／短碼或等效方案。

### 7.6 Cloud Audit

- [ ] 記錄 Employee、Device、Work Item、SUPER_ADMIN transfer 與必要維運事件摘要。
- [ ] 不保存密碼、Recovery Code、OTP、Device Token、App Key 或不必要的完整發票內容。

## 8. 後續大版本：多公司 Workspace

不列入 V3.0 阻塞項目。

- [ ] 真正有不同統編需求時再新增 Company entity／`company_id`。
- [ ] V3 單公司 Workspace 升級時自動建立第一個 Company。
- [ ] 視實際需求再做 Employee ↔ Company scope、Device 預設 Company、公司切換 UI、跨公司待辦／查詢／報表。

## 9. 對外相容／自架準備

- [ ] Cloud API 穩定後撰寫 CYInvoice Self-hosted / Cloud Integration User Manual。
- [ ] Guide 定義 Built-in Cloud 部署需求、endpoint、schema、Device auth、Employee authorization、錯誤碼、版本、idempotency／reconciliation 語意；不得要求 CY ID 才能完成基本部署。

## 10. 後續增強

- [ ] Built-in Cloud 一般 USER／ADMIN 忘記密碼的 Email self-service。
- [ ] MO 密碼安全 Cloud sync。
- [ ] 小型營運摘要：今日／本月開票張數與金額、待處理工作數、同步異常數。

AMEGO App Key 不列入 Cloud sync，仍由各電腦自行設定並以 Windows secure storage 保護。

## 11. 單機版明確不排入

- 本機操作／稽核紀錄：不做；等 Cloud backend 後做跨機 Audit。
- 本機備份／還原：不做；CYInvoice 是中介層，官方資料以光貿為準。
- 發票 Excel／CSV 匯出：不做；需要時使用光貿網站。
- 管理員開機待辦提醒：不做；沒有持續登入，無法可靠知道開程式的人是管理員。

任何 merge、tag、正式 Release 均需明確授權。