# CYInvoice 待辦與驗證

本檔只保留目前仍未完成、需要後續驗證或已明確延後的工作。已完成內容與歷史決策由 README、PR、測試與設計文件保存。

目前工程開發基準：**CYInvoice V2.6.3 Build 0**  
最新正式 Release：`cyinvoice-v2.4.2`

## 1. V2.6.x 實機與光貿驗證

- [ ] 全新首次設定：建立超級管理員、密碼規則、一次性復原碼與重新啟動。
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

## 7. 雲端版上線工作

目前架構定位與已定案內容見 `CLOUD_ARCHITECTURE_STATUS.md`；完整分期另見 `CLOUD_ROADMAP.md`。

### Phase 1：Public Repo Cloud Foundation 收斂

- [ ] Windows client 預設固定 `Local Only`；未啟用雲端時完全不呼叫 Cloud API。
- [ ] 移除 Windows client 中任何專案擁有者／開發環境的預設 Cloud endpoint。
- [ ] 設定頁提供「單機模式／雲端模式」選擇；只有選擇雲端模式才顯示 Cloud API 相關設定。
- [ ] Cloud API endpoint 由使用者自行填寫並保存。
- [ ] Windows client 永遠只連 CYInvoice-compatible HTTPS API，不直接連任何 D1／SQL／其他資料庫。
- [ ] 將 Cloud API contract 與特定 backend 實作分離；Cloudflare Worker + D1 僅保留為目前 reference implementation。
- [ ] Workspace／公司資料模型。
- [ ] Device register／pair／revoke 與 Device Token。
- [ ] Cloud API health／version compatibility。
- [ ] staging／production 分離。
- [ ] Cloud schema migration、監控與服務層復原。
- [ ] Cloud Enabled 時採 Cloud Preferred；Cloud 暫時不可用時安全本機功能可 fallback，但需要跨機唯一性的操作不得假裝取得 lock。

### Phase 2：中央員工與權限

- [ ] 員工／角色／enabled／lockout 中央化。
- [ ] 明確區分 Employee/User 與 Device 身分。
- [ ] 維持 per-operation authentication，不強制改成程式啟動登入。
- [ ] 第一台既有超級管理員建立 Cloud Workspace 的一次性遷移流程。
- [ ] 第二台開始以 Cloud 員工資料為權威，本機只作必要 Cache。
- [ ] 定案首次 SUPER_ADMIN／既有 SUPER_ADMIN／新裝置加入的正式驗證流程；Email OTP 為目前候選方案，完成安全與 UX 設計後再實作。

### Phase 3：跨機工作中心與防重

- [ ] 作廢／折讓／折讓作廢 work items 上雲。
- [ ] 原子 state transition／optimistic version。
- [ ] idempotency key／operation lock，避免 A／B 機重複處理。
- [ ] 管理員手動結案跨機同步。

### Phase 4：雲端操作稽核

- [ ] 有 Cloud backend 後才新增操作／稽核紀錄；不回頭為單機版另做一份。
- [ ] 記錄使用者、管理員、裝置、work item 與官方結果摘要，不保存密碼、復原碼、App Key 或不必要發票內容。

### Phase 5：正式折讓 API

- [ ] 全域唯一 AllowanceNumber。
- [ ] `/json/g0401`／`/json/g0501`。
- [ ] 官方回查、pending state machine、跨裝置防重。

### 對外雲端相容指南（功能定案後撰寫）

- [ ] 完成 CYInvoice 雲端版 API／資料模型／認證與裝置流程定案後，撰寫一份**技術中立的 Cloud Integration Guide**，只定義 CYInvoice 雲端端點、協定、資料格式、認證／權限、錯誤碼、版本相容與必要行為；不限定 Cloudflare、D1 或任何特定雲端／資料庫技術。第三方只要依此 Guide 實作相容的雲端服務，即可在 CYInvoice「雲端模式」填入其服務位址後使用。
- [ ] Guide 不負責教第三方如何選擇或建立其雲端基礎設施；Cloudflare 僅作為本專案開發／參考實作之一，不是 CYInvoice 雲端版的必要條件。

### 後續增強

- [ ] Email 忘記密碼／驗證碼。
- [ ] MO 密碼安全雲端同步。
- [ ] 小型營運摘要：今日／本月開票張數與金額、待處理工作數、同步異常數；目前只保留產品候選。

AMEGO App Key 不列入雲端同步範圍，仍維持各電腦自行設定、Windows DPAPI 本機保護。

## 8. 單機版明確不排入

以下已由使用者定案，不再當成單機待辦：

- **本機操作／稽核紀錄：不做。** 等 Cloud backend 後做跨機稽核。
- **本機備份／還原：不做。** CYInvoice 為中介層，發票／折讓官方資料以光貿為準。
- **發票 Excel／CSV 匯出：不做。** 有需要直接使用光貿網站；對應業務單號另回填 ERP。
- **管理員開機待辦提醒：不做。** 現行沒有持續登入，只在需要權限時驗證，無法可靠知道開程式的人是不是管理員。

小型營運摘要仍保留未來候選，但沒有排入目前單機版本。
