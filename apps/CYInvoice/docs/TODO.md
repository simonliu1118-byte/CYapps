# CYInvoice 待辦與驗證

本檔只保留目前仍未完成、需要後續驗證或已明確延後的工作。已完成內容與歷史決策由 README、PR、測試與設計文件保存。

目前工程開發基準：**CYInvoice V2.6.3 Build 0**  
最新正式 Release：`cyinvoice-v2.4.2`

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

### 7.1 已定案的架構邊界

- [x] V3.0 不做多公司 UI、跨公司權限、跨公司查詢或跨公司待辦。
- [x] V3.0 不先導入 `company_id`；未來真正有台北／台中等不同統編需求時再新增 Company 層與 migration。
- [x] `workspace_id` 不得等於統編；Workspace 定義為協作／管理範圍，不永久等同公司或 AMEGO 帳號。
- [x] Workspace 建立後不因 Device／Token 遺失、Windows 重灌或程式重新下載而重建。
- [x] 未來志遠多家公司可以共用同一 Workspace；是否做跨公司功能留到當時再決定。
- [x] Windows Client 只依賴 CYInvoice-compatible HTTPS API，不綁定 Cloudflare、D1 或特定資料庫。
- [x] Cloudflare Worker + D1 僅為目前 reference implementation。
- [x] 既有 `0001_cloud_foundation.sql`／`0002_device_pairing.sql` 保留，後續一律新增 migration，不回寫已執行 migration。
- [x] Cloud 定位為 Coordination Service，不是業務總開關；Cloud 掛掉時原則上降回單機模式，不採全系統 blanket lock。
- [x] AMEGO 仍是發票／作廢／折讓官方結果唯一準則；App Key 不上雲。
- [x] Employee 與 Device 分離；不另建立第二套 Cloud Admin。
- [x] 每個 Workspace 永遠只允許一名 SUPER_ADMIN；ADMIN 可多人。
- [x] 既有單機 B 機合法加入 Workspace 後，原唯一 Local SUPER_ADMIN Y 自動成為 Workspace ADMIN，可立即工作。
- [x] SUPER_ADMIN 更換只走原子的「移交超管權限」：原超管降 ADMIN、指定 ADMIN 升 SUPER_ADMIN。
- [x] 雙重災難（所有 Device Token 與原 Recovery Email 同時失效）最終由 Cloudflare／reference backend 管理端人工修改 Recovery Email，不新增第四套 emergency secret。

### 7.2 Phase 1：Public Repo Cloud Foundation 收斂

- [x] Windows Client 預設 `Local Only`；未啟用雲端時不呼叫 Cloud API。
- [x] Windows Client 不預填專案擁有者／development Cloud endpoint。
- [x] 設定頁提供「單機模式／雲端模式」。
- [x] Cloud API endpoint 由使用者自行填寫並保存，且只接受相容 HTTPS endpoint。
- [x] Windows Client 不直接連 D1／SQL／其他資料庫。
- [x] Cloud API contract 與 backend 實作分離。
- [x] Health／API／schema／storage compatibility 基礎完成。
- [x] D1 `0001`／`0002` migration 與 Windows → Worker → D1 live connection 已驗證。
- [x] 主畫面具備 `單機模式`／`雲端模式`／`雲端異常(單機模式)` 執行狀態顯示。
- [ ] staging／production 正式環境切分、監控與服務層復原完成。
- [ ] 將目前開發 endpoint／reference backend 的部署與維運流程整理成正式工程文件，不把 owner endpoint 寫入 Windows 包。

### 7.3 Phase 2：Workspace + 第一台 Device

#### Batch 1：安全 bootstrap contract

- [ ] Workspace ID／Device ID 由 Cloud 產生，Device Token 由 Windows 產生；Cloud 只保存 Token hash。
- [ ] 同一 Pending Token 能安全恢復 timeout／lost response／bootstrap retry，不依賴 Client 自行產生 Device ID。
- [ ] Workspace + 第一台 Device 必須原子建立，不留下只有 Workspace 沒有可信任 Device 的狀態。
- [ ] Cloud contract tests 與 Worker type／bundle／Windows build／startup smoke 全部通過。

#### Batch 2：Windows Pending Token

- [ ] Windows 在 bootstrap 送網路前先以 DPAPI 持久化 Pending Device Token。
- [ ] Pending onboarding state 可跨程式重啟恢復。
- [ ] bootstrap timeout 時先查 onboarding status；Workspace 已存在時先以 Pending Token 驗證，不盲目重建。

#### Batch 3：Local SUPER_ADMIN Email OTP

- [ ] 建立 Workspace 前先驗證既有 Local SUPER_ADMIN；不重新輸入 Email。
- [ ] UI 只顯示遮罩後既有 SUPER_ADMIN Email。
- [ ] Email OTP challenge／verify：短效、單次、OTP hash、錯誤次數限制、重寄 cooldown、rate limit。
- [ ] 需要新增 Cloud schema 時只用 `0003+` forward migration，不修改 `0001`／`0002`。
- [ ] OTP 通過後，既有 Local SUPER_ADMIN 成為 Workspace 唯一 SUPER_ADMIN。

#### Batch 4：首次建立 UI

- [ ] Windows「雲端連線設定」在 health 成功且尚無 Workspace 時提供「建立雲端空間」。
- [ ] 一次性 server-side bootstrap guard／「雲端初始化碼」不得保存到 repo／log／安裝包／明文設定。
- [ ] 初始化成功後立即以 `GET /v1/device` 驗證 Workspace／Device；成功後 Pending identity 才轉正式。
- [ ] Workspace 已存在時不得再顯示首次建立流程。

### 7.4 Phase 3：中央員工、SUPER_ADMIN 與 Device Join／Recovery

- [ ] 員工／role／enabled／lockout 中央化，維持 per-operation authentication。
- [ ] 全新第二台電腦第一次啟動可直接選「加入既有雲端空間」，不必先建立 Local SUPER_ADMIN。
- [ ] Cloud 有 Workspace、本機無 Device identity 時顯示「此雲端空間已建立，但這台電腦尚未加入」。
- [ ] Device Join 支援短效 Pairing Code，或 Workspace 已登記 SUPER_ADMIN Email OTP。
- [ ] 既有 Workspace 加入授權不得使用新機自己的 Local SUPER_ADMIN Email 自我批准。
- [ ] 第二台 Device／Recovery Device 也使用獨立 Token；正式流程收斂為 Windows 產生 Token、Cloud 只存 hash。
- [ ] Token 遺失／Windows 重灌時加入原 Workspace，不建立新 Workspace。
- [ ] 舊 Device 不因名稱相同自動撤銷；由 SUPER_ADMIN／授權 ADMIN 明確 revoke。
- [ ] 既有單機 B 機合法加入後，原 Local SUPER_ADMIN Y 自動建立／對應為 Workspace ADMIN，可立即工作。
- [ ] 若 Y 已經是 Cloud Employee，使用穩定 Employee identity／明確驗證 mapping，不建立第二個 Y，不只靠姓名猜。
- [ ] 帳號管理新增「移交超管權限」；只有目前 SUPER_ADMIN 可操作，且 Server 以單一原子 transaction 同時降舊超管、升新超管。
- [ ] SUPER_ADMIN 不提供獨立「降為管理員」按鈕；要降級只能先移交給另一位 ADMIN。
- [ ] Pairing Code 只有在有效 Device + SUPER_ADMIN／指定 ADMIN 人員驗證 + OTP 成功後才產生；B 機輸入已授權 Pairing Code 後不再做第二次管理員 OTP。
- [ ] SUPER_ADMIN 有有效 Device 時可修改自己的 Email並 OTP 驗證新 Email；忘記本機密碼沿用單機 Recovery Code。
- [ ] 所有 Device Token 遺失時以 Workspace Recovery／SUPER_ADMIN Email OTP 重建 Device。
- [ ] 雙重災難只保留 Cloudflare／reference backend 人工維運救援，不新增 Windows emergency recovery API。

### 7.5 Phase 4：跨機 Work Item、離線降級與恢復

- [ ] 作廢／折讓／折讓作廢／管理員結案 Work Item 上雲。
- [ ] Work Item 使用原子 state transition／optimistic version，避免同一待辦被兩台同時結案。
- [ ] idempotency／operation lock 只用在真正需要跨機協調的點，不把 Cloud Lock 變成所有本機業務的前置條件。
- [ ] Cloud 失效時盤點並實測現有單機功能：查閱／同步／PDF／列印／一般開票／直接作廢／人工作廢覆核／人工折讓／人工折讓作廢／管理員結案。
- [ ] Cloud 失效時，純 Cloud 管理功能（新 Device、中央帳號／角色／Workspace 管理、超管移交）停止；安全本機業務直接降單機模式。
- [ ] Cloud 恢復後，對離線期間本機狀態做 reconciliation；任何 AMEGO 結果不明操作都不得因重新連線而自動重送。
- [ ] 解決多機離線自動 OrderID 撞號：目前 `MyyyyMMddNNN` 只看本機紀錄，正式多機上線前需改成 Device namespace／短碼或等效不依賴即時 Cloud 的方案。

### 7.6 Phase 5：Cloud Audit

- [ ] 有 Cloud backend 後才新增跨機操作／稽核紀錄；不回頭為單機版另做一份。
- [ ] 記錄 Employee、管理員、Device、Work Item、超管移交與必要維運事件摘要。
- [ ] 不保存密碼、復原碼、OTP、Device Token、App Key 或不必要的完整發票內容。

### 7.7 Phase 6：正式折讓 API

- [ ] 全域唯一 AllowanceNumber。
- [ ] `/json/g0401`／`/json/g0501`。
- [ ] 官方回查、pending state machine、跨裝置防重。

## 8. 後續大版本：多公司 Workspace

此區只保留擴充點，不列入 V3.0 工程範圍。

- [ ] 真正有志遠台北／台中等不同統編需求時，再新增 `companies`／Company entity 與 `company_id`。
- [ ] V3.x 單公司 Workspace 升級時，自動建立第一個 Company，既有公司相關資料全部歸到該 Company。
- [ ] 視實際需求再做 Employee ↔ Company 權限、Device 預設 Company、公司切換 UI、跨公司待辦／查詢／報表。
- [ ] 向下相容：Workspace 仍只有一家公司時，舊 V3.x Client 可由 Server 套用唯一公司；啟用兩家公司以上後，舊 Client 不得自行猜測，應要求升級。

## 9. 對外雲端相容／開源準備

此區不列入目前志遠高雄 V3.0 上線阻塞項目。

- [ ] Cloud 功能與 API Contract 穩定後，撰寫技術中立的 **Cloud Integration Guide**。
- [ ] Guide 只定義 endpoint、request／response schema、Device authentication、Employee authorization、錯誤碼、版本相容、reconciliation／idempotency 必要語意。
- [ ] Guide 不規定第三方使用 Cloudflare、D1、AWS、Azure、SQL Server、PostgreSQL 或其他技術；第三方只要提供 CYInvoice-compatible Cloud API 即可。
- [ ] 對外販售／開源時，不假設使用者的 Workspace 只有一家公司，也不假設使用者採用本專案的 reference backend。

## 10. 後續增強

- [ ] 一般員工／管理員忘記密碼的 Email self-service 流程；Workspace／Device Recovery OTP 已列入 V3.0 核心，不放在此延後項目。
- [ ] MO 密碼安全雲端同步。
- [ ] 小型營運摘要：今日／本月開票張數與金額、待處理工作數、同步異常數；目前只保留產品候選。

AMEGO App Key 不列入雲端同步範圍，仍維持各電腦自行設定、Windows DPAPI 本機保護。

## 11. 單機版明確不排入

以下已由使用者定案，不再當成單機待辦：

- **本機操作／稽核紀錄：不做。** 等 Cloud backend 後做跨機稽核。
- **本機備份／還原：不做。** CYInvoice 為中介層，發票／折讓官方資料以光貿為準。
- **發票 Excel／CSV 匯出：不做。** 有需要直接使用光貿網站；對應業務單號另回填 ERP。
- **管理員開機待辦提醒：不做。** 現行沒有持續登入，只在需要權限時驗證，無法可靠知道開程式的人是不是管理員。

小型營運摘要仍保留未來候選，但沒有排入目前單機版本。