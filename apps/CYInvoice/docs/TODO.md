# CYInvoice 待辦與驗證

本檔只保留目前仍未完成、需要後續驗證或已明確延後的工作。已完成內容與歷史決策由 README、PR、測試與設計文件保存。

目前工程開發基準：**CYInvoice V2.6.4 Build 1**
最新正式 Release：`cyinvoice-v2.4.2`

> V3 Cloud identity 工作若由新的長時間工作階段／ChatGPT Work 接手，先讀 `docs/CLOUD_WORK_HANDOFF.md`，再讀 `CLOUD_ARCHITECTURE_STATUS.md`、`CLOUD_ROADMAP.md` 與本檔。接手時仍必須依 `AGENTS.md` 指示先讀三層永久規則。

## 1. V2.6.x 實機與光貿驗證

- [ ] 全新首次設定：建立超級管理員、密碼規則、一次性復原碼與重新啟動。
- [ ] 首次設定尚未完成、尚未建立超級管理員時，主畫面右上角光貿連線 Tag 先顯示灰色「連線中」；完成首次設定後才執行既有光貿 API 連線檢查，再切換為綠色「光貿連線正常」或紅色「光貿連線異常」。不得因首次設定視窗尚未完成而留下空白 Tag。
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

已新增 `tests/CYInvoice.Allowance.Tests/` 並接入 Windows CI，專門保護 V2.6.2 起新增的折讓核心：

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

目前只完成已寄出 DeliveryList 的可靠基本解析。沒有樣本前維持安全停止，不猜欄位／金額：

- [ ] 未出貨／不同狀態樣本。
- [ ] 公司統編訂單樣本。
- [ ] 多商品訂單樣本。
- [ ] 數量大於 1 樣本。
- [ ] 折扣／負數調整樣本。
- [ ] 取得樣本後補 parser、金額、防重與測試案例。

## 6. 正式折讓 API（雲端化階段）

目前仍以人工折讓／人工折讓作廢待辦為主；完成紀錄以 `invoice_query.allowance[]` 為官方依據。正式 AMEGO 折讓開立與作廢 API 延後到雲端協調架構成熟後，以跨裝置資料模型一次完成。

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

完整長期定位與分期見 `CLOUD_ROADMAP.md`；Workspace／Device／Token／SUPER_ADMIN／Recovery 的完整定案見 `CLOUD_IDENTITY_LIFECYCLE.md`。V3.0 的產品範圍已收斂為：**志遠高雄單一公司／單一統編，多台 CYInvoice 電腦協同。**

### 7.1 Cloud Identity 已完成的 source foundation

- [x] Workspace / Device identity、protected Device Token、retry-safe bootstrap / Join。
- [x] Existing Workspace B-side Join 先做執行時 Local ADMIN / SUPER_ADMIN 驗證，再允許輸入 Pairing Code。
- [x] Pairing Code 授權 Device，不直接授予 Employee role。
- [x] Local → Cloud 採 whole-device Employee Transition，一次盤點全部既有 Local Employees。
- [x] Identity matching：Employee No + Email 同命中同一人直接採 Cloud；兩者都不存在建立新人；partial／divergent match 進 conflict。
- [x] 姓名不是 identity matching authority。
- [x] Conflict 由目前 Workspace SUPER_ADMIN 當下重新驗證後人工處理；沒有 unresolved case 時不顯示「待確認帳號」。
- [x] 第一台 X 成為中央 SUPER_ADMIN；bootstrap 已驗證 Email 可直接沿用，不重複 OTP。
- [x] 既有 Workspace 新 Local SUPER_ADMIN Y 若是新中央 Employee，中央 role 為 ADMIN；若精確命中既有 Employee，保留既有 Cloud role。
- [x] Cutover 後 Cloud Employee 是唯一帳號 authority；舊 Local EmployeeStore 不再作權限來源。
- [x] Cloud Mode 斷網時使用最後成功同步的 Cloud Employee cache + protected offline credential verifier，不切回舊 Local authority。
- [x] 中央 Employee 新增、姓名／Email、`ADMIN ↔ EMPLOYEE`、enabled、password API/client/UI foundation 已完成。
- [x] 新 Employee Email 必須驗證；Email 修改時新 Email 必須驗證後才 commit。
- [x] 新密碼明文不上 Cloud；Windows 先產生 PBKDF2-SHA256 verifier。
- [x] SUPER_ADMIN transfer：X 執行時帳密 re-auth + X Email OTP；atomic X→ADMIN、Y→SUPER_ADMIN、Recovery Email→Y。
- [x] 舊單一 Local SUPER_ADMIN `reconcile-local` mutation 與任意 30 分鐘 import window 已退役；whole-device transition 為唯一正式路徑。
- [x] Cloud compatibility 已對齊 API 1 / Cloud 0.8.2 / Schema 7（migrations `0001`～`0007`）。

### 7.2 Cloud Identity 目前剩餘驗證／功能

- [x] 2026-09-23 development deploy Run #6 已確認 Worker remote 為 Cloud 0.8.2 / API 1、D1 Schema 7、`/v1/health` storage `ok`；部署前 Workspace／Device／Employee／Pairing 各為 0。
- [x] 2026-09-22 已確認 development D1 remote migrations 到 Schema 7。
- [x] Brevo runtime secrets 已完成設定，bootstrap OTP 實際收信成功。
- [x] V2.6.4 Build 1 已修正第一個 Workspace 建立 SQL 與誤報錯誤；Cloud 0.8.2 已部署，Windows client 已回報首次 Workspace／Device 建立及 Device identity 驗證成功。
- [ ] 以本機 Codex 已連線的 Cloudflare MCP 唯讀回查 development D1：Workspace／Device／Employee／Pairing 筆數，及首個 Workspace 與 Device 的關聯；不得把部署前 0 筆當成現在結果。
- [ ] A 機 first Employee Transition／cutover 實機驗收；Windows client 的 Device identity 成功不代表中央 SUPER_ADMIN 或帳號轉換已完成。
- [ ] B 機 Pairing + 多 Local Employee transition matrix 實機測試。
- [ ] 精確命中、全新 Employee、Employee No only、Email only、兩欄各撞不同人的實機／integration 測試。
- [ ] Central Employee CRUD、Email OTP、password、enabled、role 在 A/B 間 snapshot 同步實機測試。
- [ ] Cloud Mode 斷網：以最後 Cloud cache 做 execution-time auth；恢復連線後 Cloud authority 覆蓋 cache。
- [ ] Device revoke UI/API。
- [ ] 所有 Device Token 遺失但 Recovery Email 可用時的 Recovery Device flow。
- [ ] 所有 Device Token + Recovery Email 同時失效時的 reference-backend 人工維運文件。
- [ ] 驗收 fresh-install 首次分流：單機版原流程；雲端加入可選配對碼或邀請碼，不建立本機帳號。先確認 A 機中央帳號已完成 cutover，再使用 Windows 測試包驗證兩條路徑及斷線恢復。

### 新裝置加入方式與安全紀錄（2026-09-25 討論定案，工程實作中）

- [ ] 新裝置加入最終只保留「配對碼」與「邀請碼」兩種方式；移除現有「Workspace 識別碼＋超管帳密／Email OTP」直接加入入口與 API。上述舊版驗收項目須依新流程改寫，不能視為最終設計。
- [ ] A 機「新增雲端裝置」提供「立即配對」：超管驗證後顯示目前連線的 Cloud API 網址與約 10 分鐘、限用一次的配對碼；B 機輸入網址與配對碼，確認 Workspace 名稱後加入。A 機視窗顯示加入結果。
- [ ] A 機另提供「寄送新裝置邀請」：超管驗證後，將 API 網址與 72 小時、限用一次的邀請碼寄至超管帳號已驗證的 Email；邀請可由 A 機撤銷或重新寄送。B 機輸入網址、邀請碼、超管編號與密碼，確認 Workspace 名稱後加入；不再要求第二封 Email 驗證碼。A 機邀請視窗可顯示加入結果。
- [ ] 程式不內嵌 Cloud API 網址。Workspace 識別碼僅供內部定位，不作為新機手動輸入欄位；配對碼／邀請碼由伺服器解析目標 Workspace。新機成功加入後才保存 API 網址與 Device identity。
- [ ] 先在 Cloud D1 建立安全操作紀錄，涵蓋現有配對碼核發、驗證與新機加入；邀請碼及撤銷功能實作時寫入同一紀錄。紀錄需支援 A 機查詢邀請／配對狀態，並保留核發、寄送、撤銷、使用成功及相關失敗事件；不得記錄配對碼、邀請碼、密碼、OTP 或 Device Token 原文。現有配對流程的資料表名稱與 Worker 查詢名稱也需在實作時核對修正。
- [ ] 後續版本新增「安全操作紀錄／稽核紀錄」查看介面；本階段只建立雲端紀錄，不製作查看介面。
- [ ] 完成 Windows 編譯、A／B 機工程包測試及遠端 Cloudflare migration／Worker 部署前的審核；目前只完成本機 Cloud 型別與 D1 migration 測試，不代表已部署。

### 7.3 跨機 Work Item / Sync

- [ ] 作廢／折讓／折讓作廢／管理員結案 Work Item 上 Cloud。
- [ ] Work Item 原子 state transition／optimistic revision，避免多機同時結案。
- [ ] Cloud coordination 只用於真正需要跨機原子性的點，不把 Cloud Lock 變成所有業務前置條件。
- [ ] 離線期間 AMEGO 結果不明操作不得因恢復連線而自動重送。
- [ ] 解決多機離線自動 OrderID 撞號；正式多機上線前需 Device namespace／短碼或等效方案。

### 7.4 Cloud Audit

- [ ] 記錄 Employee、Device、Work Item、SUPER_ADMIN transfer 與必要維運事件摘要。
- [ ] 不保存密碼、Recovery Code、OTP、Device Token、App Key 或不必要的完整發票內容。

## 8. 後續大版本：多公司 Workspace

不列入 V3.0 阻塞項目。

- [ ] 真正有不同統編需求時再新增 Company entity／`company_id`。
- [ ] V3 單公司 Workspace 升級時自動建立第一個 Company。
- [ ] 視實際需求再做 Employee ↔ Company scope、Device 預設 Company、公司切換 UI、跨公司待辦／查詢／報表。

## 9. 對外相容／開源準備

- [ ] Cloud API 穩定後撰寫 provider-neutral Cloud Integration Guide。
- [ ] Guide 定義 endpoint、schema、Device auth、Employee authorization、錯誤碼、版本、idempotency／reconciliation 語意，不指定 Cloud provider／DB。

## 10. 後續增強

- [ ] 一般員工／管理員忘記密碼的 Email self-service。
- [ ] MO 密碼安全 Cloud sync。
- [ ] 小型營運摘要：今日／本月開票張數與金額、待處理工作數、同步異常數。

AMEGO App Key 不列入 Cloud sync，仍由各電腦自行設定並以 Windows secure storage 保護。

## 11. 單機版明確不排入

- 本機操作／稽核紀錄：不做；等 Cloud backend 後做跨機 Audit。
- 本機備份／還原：不做；CYInvoice 是中介層，官方資料以光貿為準。
- 發票 Excel／CSV 匯出：不做；需要時使用光貿網站。
- 管理員開機待辦提醒：不做；沒有持續登入，無法可靠知道開程式的人是管理員。

任何 merge、tag、正式 Release 均需明確授權。
