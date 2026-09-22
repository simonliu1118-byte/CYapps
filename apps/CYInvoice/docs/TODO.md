# CYInvoice 待辦與驗證

本檔只保留目前仍未完成、需要實機驗證或已明確延後的工作。已完成內容與歷史決策由 README、PR、測試與設計文件保存。

目前工程開發基準：**CYInvoice V2.6.3 Build 0**  
最新正式 Release：`cyinvoice-v2.4.2`

## 1. V2.6.x 實機與光貿驗證

- [ ] 全新首次設定：建立超級管理員、密碼規則、一次性復原碼與重新啟動。
- [ ] 首次設定未完成時，主畫面右上角光貿連線 Tag 顯示灰色「連線中」；完成後才切換實際 API 狀態。
- [ ] 單機帳號管理／權限驗證：一般使用者、管理員、超級管理員建立、修改、停用、重設與禁止操作邊界。
- [ ] MO店+ 未設定 Excel 密碼時，先提示設定且不得先開選檔視窗。
- [ ] 設定選單持續開啟至少 30 秒，確認不再連續閃爍。
- [ ] `設定 → 系統診斷`：API／SQLite／WebView2／Excel／印表機／同步狀態與複製摘要符合實機，且不顯示敏感資料。
- [ ] 測試環境實際送出直接作廢 `3015-消退` 與管理員覆核作廢 `3001-3015-消退`。
- [ ] 保留完整 `invoice_query` JSON，確認 AMEGO 是否回傳可可靠解析的 `CancelReason`／作廢原因；不得猜未文件化欄位。
- [ ] 實測紙本未收回人工確認：一般使用者不能批准，管理員可批准／取消，批准後才送作廢。
- [ ] 作廢確認開啟期間，背景已開立清單可見發票號碼持續隱藏，即使背景重新整理也不重新顯示。
- [ ] 實際建立人工折讓，以 `invoice_query.allowance[]` 驗證同日舊／新折讓辨識、含稅金額與多候選保守停止。
- [ ] `/json/allowance_file` A4、A4（地址+A5）、A5 三種 PDF 均可正常開啟／列印。
- [ ] 折讓官方狀態／日期／類型／稅額／金額變動後，PDF Cache 必須換版。
- [ ] 折讓作廢暫行人工流程：一般使用者可提出，管理員在「上傳問題」完成或取消；現階段不得直接呼叫 `/json/g0501`。
- [ ] 單一「上傳問題」清單：技術問題、Failed、作廢、折讓、折讓作廢均正確顯示；只有 Failed 可勾選清除。
- [ ] 超過兩期的發票作廢、折讓、折讓作廢待辦顯示「可結案」，只有 ADMIN／SUPER_ADMIN 可結案；結案只停止本機追蹤，不代表 AMEGO 已完成。

完整操作步驟見 `RC_TEST.md`。

## 2. 待取得實機資料後再決定

- [ ] 折讓作廢人工完成後的官方確認策略：取得真實 `invoice_query.allowance[]` 折讓作廢樣本後，再判斷能否可靠回查；無法確認時維持 pending。
- [ ] `CancelReason` 回查：取得真實 `invoice_query` 樣本後再決定是否可跨重新啟動還原使用者／覆核管理員／原因；沒有官方欄位就不自行保存一套永久作廢歷史。

## 3. 已有自動化測試仍需實機補強

`tests/CYInvoice.Allowance.Tests/` 與 Windows CI 已保護折讓核心，包括 PDF style/cache、Employee allowance void workflow、管理員完成／取消、跨期 administrative closure、WinForms startup smoke。

- [ ] 仍需光貿 live API／Windows 實機驗證；CI 不取代正式 API 行為。

## 4. 既有同步與 Cache 實機回歸

- [ ] 以舊版真實 `Data` 備份升級到現行 SQLite，確認舊資料完整。
- [ ] 正式環境 recent 3-day sync，確認可抓到另一台電腦／光貿端合法更新。
- [ ] 程式持續開啟超過 5 分鐘，確認啟動同步／每 5 分鐘同步不重疊，手動重新整理維持冷卻。
- [ ] 每日第一次完整同步不影響 recent 3-day 核心。
- [ ] 雙擊任何發票都先 `invoice_query`；query 無法確認時不得把舊 Cache 冒充最新資料。
- [ ] 測試環境 namespaced OrderID 不與共享測試池撞號，UI 仍顯示原始可讀 OrderID。
- [ ] 發票 PDF／Preview Cache 不保存短效 `file_url`，retention 清理時同步移除。

## 5. 酷澎樣本補齊

- [ ] 未出貨／不同狀態樣本。
- [ ] 公司統編訂單樣本。
- [ ] 多商品訂單樣本。
- [ ] 數量大於 1 樣本。
- [ ] 折扣／負數調整樣本。
- [ ] 取得樣本後補 parser、金額、防重與測試；沒有樣本前安全停止，不猜欄位。

## 6. 正式折讓 API（Cloud 協調成熟後）

目前維持人工折讓／人工折讓作廢待辦，以 `invoice_query.allowance[]` 為官方完成依據。

- [ ] `/json/g0401` 折讓開立。
- [ ] `/json/g0501` 折讓作廢；正式自動化後只允許 ADMIN／SUPER_ADMIN 直接執行，結果不明不得標成功。
- [ ] 全域唯一 `AllowanceNumber` 與跨裝置防重。
- [ ] `allowance_query`／`allowance_status` 正式同步模型；目前刻意共用 `invoice_query`。
- [ ] 部分折讓、多次折讓、品項／數量／金額規則。
- [ ] 發票與折讓的 idempotency、timeout、unknown-result 與同步安全規則。
- [ ] 雲端化完成後再移除暫行人工折讓／折讓作廢路徑。

## 7. CYInvoice V3.0 Cloud 協同

V3.0 範圍：**志遠高雄單一公司／單一統編，多台 CYInvoice 電腦協同。** 目前不導入 Company entity／`company_id`。

### 7.1 已定案且已落實的 Identity foundation

- [x] Windows Client 只依賴 CYInvoice-compatible HTTPS API；Cloudflare Worker + D1 只是 reference backend。
- [x] Workspace ID／Device ID 由 Cloud 產生；Device Token 由 Windows 產生並先安全保存；Cloud 只存 hash。
- [x] Workspace 建立後不因 Device／Token 遺失、Windows 重灌或程式重新下載而重建。
- [x] 每個 Workspace 恰好一名 `SUPER_ADMIN`；`ADMIN`／`EMPLOYEE` 可多人。
- [x] CYInvoice 維持 per-operation authentication，不改成程式啟動即持續登入。
- [x] Device 與 Employee 分離；Pairing Code 授權 Device，不授予 Employee role。
- [x] B 機輸入 Pairing Code 前先當下驗證 Local ADMIN／SUPER_ADMIN，Workspace 授權與本機管理權分離。
- [x] Pairing Claim timeout／lost response 使用同一 Pending Device Token 復原，不建立第二個孤兒 Device。
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
- [x] Cloud compatibility 已對齊 API 1 / Cloud 0.8.0 / Schema 7（migrations `0001`～`0007`）。

### 7.2 Cloud Identity 目前剩餘驗證／功能

- [ ] 確認 development Worker 實際部署版本；GitHub CI 綠燈不等於 remote 已部署。
- [ ] 對 development D1 實際套用／確認 migrations 到 Schema 7；不得只依 source 推定。
- [ ] 完成 Brevo runtime secrets 後做 live Email OTP delivery test。
- [ ] A 機真實建立 Workspace + first Employee transition。
- [ ] B 機 Pairing + 多 Local Employee transition matrix 實機測試。
- [ ] 精確命中、全新 Employee、Employee No only、Email only、兩欄各撞不同人的實機／integration 測試。
- [ ] Central Employee CRUD、Email OTP、password、enabled、role 在 A/B 間 snapshot 同步實機測試。
- [ ] Cloud Mode 斷網：以最後 Cloud cache 做 execution-time auth；恢復連線後 Cloud authority 覆蓋 cache。
- [ ] Device revoke UI/API。
- [ ] 所有 Device Token 遺失但 Recovery Email 可用時的 Recovery Device flow。
- [ ] 所有 Device Token + Recovery Email 同時失效時的 reference-backend 人工維運文件。
- [ ] Fresh-install 第二台是否需要額外 first-run 分流，待多機實測後決定最小 UI；不得因此另建第二套帳號模型。

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
