# CYInvoice 本機資料格式

本文件記錄 C#／WinForms 現行工程線的本機資料、SQLite 遷移、Cache 與安全規則。

- 目前工程測試基準：**V2.6.15**。
- 最新公開正式 Release：**V2.4.2**。
- 主要本機資料庫：`Data/CYInvoice.db`。
- 安全設定：`Data/settings.json`。
- 舊 `invoices.json`／`buyer_names.json` 只作首次 SQLite 遷移來源與保留備份，不再是主要寫入資料。

## 1. 目錄與 Cache

程式會使用／建立：

- `Data/`
- `Cache/InvoicePDF/`
- `Cache/InvoicePreview/`
- `Cache/AllowancePDF/`（第一次取得折讓 PDF 時建立）
- `Cache/WebView2/`
- `Cache/WebView2Print/`（直接列印需要時）

### 發票 PDF

`Cache/InvoicePDF/{test|prod}/{yyyyMMdd}/{發票號碼}_style{0|1|2|3|5}.pdf`

### 發票第一頁預覽

`Cache/InvoicePreview/{test|prod}/{yyyyMMdd}/{發票號碼}_style0.page1.png`

### 折讓 PDF

V2.6.2 使用官方折讓資料版本化 Cache：

`Cache/AllowancePDF/{test|prod}_{公司統編}_{折讓單號}_{style}_{官方資料指紋}.pdf`

目前折讓 PDF style 只允許：

- `0`：A4
- `1`：A4 (地址+A5)
- `3`：A5

「官方資料指紋」由目前本機保存的 `invoice_query.allowance[]` 對應折讓資料計算，涵蓋折讓單號、日期、類型、狀態、AllowanceType、稅額與金額。只要上述任一官方欄位改變，Cache key 就會改變，舊 PDF 不可能再被當成目前版本命中；下一次取得同一折讓 PDF 時會嘗試清除該折讓單的舊版本 Cache。即使 Windows 暫時阻止刪除舊檔，因指紋不同也不會重新使用舊檔。

PDF 下載後必須通過大小與 `%PDF` 檔頭驗證，再原子寫入 Cache。光貿短效 `file_url` 不保存到本機資料庫或設定檔。

## 2. settings.json

目前 `Settings` 本機發票／安全欄位：

- `environment`：`test` 或 `prod`。
- `prod_invoice`：正式公司 8 碼統編。
- `prod_app_key_enc`：Windows DPAPI 加密後的正式 App Key。
- `mo_password_enc`：Windows DPAPI 加密後的 MO店+ Excel 密碼。
- `invoice_printer_name`：CYInvoice 記住的發票印表機名稱。

Cloud 設定另含 cloud_mode（local_only／cloud_transition／cloud_preferred）、cloud_base_url、cloud_workspace_id、cloud_device_id、cloud_device_token_enc 與 cloud_employee_authority_ready。Pending bootstrap／device join 分別保存目標、開始時間及預先 protected 的 Token，供結果不明時恢復；欄位名稱以 SettingsModels.cs 為準。這些是執行期狀態，不進 Public Git，不能憑 Device Token 推定 Employee 管理權限。

V2.6.x 已不再以舊管理密碼欄位作為目前管理員認證來源。管理／員工驗證統一經 Identity Provider；Local 使用 employees，Cloud cutover 使用中央 authority 與 protected cache；`settings.json` 不保存員工密碼或復原碼。

MO店+ 密碼不是首次設定必要條件；未設定時程式仍可啟動，但按 MO店+ 匯入前必須先完成設定。

## 3. CYInvoice.db

目前發票 schema version 維持 `1`；員工資料使用獨立 `employee_schema_version` 管理 additive schema。

主要資料表：

- `invoices`：發票快照與 CYInvoice metadata。
- `invoice_items`：發票商品明細。
- `buyer_names`：人工確認後保存的公司名稱記憶。
- `sync_state`：同步與 UI read-state 等時間標記。
- `sync_issues`：技術問題及人工待辦。
- `employees`：Local 員工帳號／角色／密碼 Hash／復原碼 Hash。
- `cloud_employee_cache`／`cloud_employee_cache_state`：中央快照／revision／protected verifier；Cloud cutover 後不回復 Local employees 作第二 authority。
- `schema_info`：schema version 與遷移 metadata。

### schema_info

至少可能包含：

- `schema_version=1`
- `employee_schema_version=1`
- role 遷移適用時：employee_role_vocabulary=USER／cloud_employee_cache_role_vocabulary=USER
- `created_utc`
- `legacy_invoices_imported`
- `legacy_buyer_names_imported`

員工 schema 以獨立 transaction 建立，不為新增員工功能強制改寫既有發票 schema。

## 4. employees

主要欄位：

- `employee_no`：4 碼數字，PRIMARY KEY。
- `name`
- `email`
- `password_hash`
- `role`：`SUPER_ADMIN`、`ADMIN`、`USER`。
- `enabled`
- `recovery_hash`
- `created_utc`
- `updated_utc`

規則：

- 一份本機員工資料最多一位 `SUPER_ADMIN`。
- 超級管理員不能降級、停用或刪除。
- 新密碼至少 8 碼，只接受 ASCII 英文字母與數字。
- 密碼只保存 PBKDF2 Hash，不保存明文。
- 超級管理員復原碼格式為 `CYR-XXXX-XXXX-XXXX-XXXX-XXXX`；只在產生時顯示，SQLite 只保存 Hash。
- 復原碼成功使用後舊 Hash 立即失效並產生新復原碼。

舊 Local／Cloud cache schema 的 EMPLOYEE → USER 由 RoleVocabularySchemaMigration 在 transaction 中一次轉換，保留 credential／recovery 與約束；active runtime 不保留 role alias。Reference Cloud 對應 forward migration 0010。

## 5. invoices 與 invoice_items

### 主鍵與比對

- `invoices.local_id` 為技術主鍵。
- 發票號碼、OrderID、API OrderID 不設過度嚴格 business `UNIQUE`。
- 同步找到多筆候選時停止自動猜測，建立／保留問題，不任意覆寫。

### 官方快照欄位

包含：

- `seller_invoice`
- `environment`
- `invoice_number`
- `order_id`／`api_order_id`
- `invoice_state`
- `buyer_identifier`／`buyer_name`
- `amount`
- `invoice_date`／`invoice_time`
- 載具、捐贈、上傳狀態、主備註、DetailVat 等

### CYInvoice metadata

包含：

- `record_id`
- `source`
- `record_origin`
- `original_order_id`
- `sent_at`
- `last_checked`
- `buyer_name_needs_memory`
- `extra_json`

`extra_json` 保存不適合立即拆成固定欄位、但需要跨重新啟動維持的 CYInvoice metadata。

### invoice_items

保存品名、數量、單位、單價、金額、稅別、備註及固定精度文字欄位。財務判定不得改回以二進位浮點累積誤差。

## 6. V2.5／V2.6 作業 metadata

目前人工流程使用 `invoices.extra_json` 保存少量本機狀態；光貿官方資料仍是最終權威來源。

### 發票作廢

- `cyinvoice_void_pending`：已送出或官方仍有作廢 pending，禁止盲目重送。
- `cyinvoice_void_official_pending`：只有官方查詢明確仍等待作廢才保存；與 durable pending marker 分開，供正常處理中分類。
- `cyinvoice_void_manual_review`：紙本證明聯未收回時的人工覆核申請，包含申請員工、原因、申請時間。

對應 `sync_issues.issue_type`：

- `void_manual_review`
- 舊 void_pending_confirmation 不再作 active upload issue；歷史項目由同步流程收斂。真正回查失敗使用 QueryFailed，不能靠訊息文字推定正常處理中。

### 折讓人工申請

- `cyinvoice_allowance_manual_review`：人工折讓申請，包含申請員工、原因、含稅金額、申請時間、提出申請當下既有折讓單號基線、等待確認狀態及人工完成時間等。既有 record 增加 nullable ConfirmationProblem：false 表示官方正常等待，true 表示回查失敗／需人工判定，舊 null 先保留上傳問題，經正常回查更新後再分類。
- `cyinvoice_allowance_pending`：管理員已完成網站操作，等待 `invoice_query.allowance[]` 官方資料確認。

對應 `sync_issues.issue_type`：

- `allowance_manual_review`

### 折讓作廢人工申請

- `cyinvoice_allowance_void_manual_review`：V2.6.2 暫行折讓作廢待辦，包含折讓單號、申請員工、原因、申請時間。

對應 `sync_issues.issue_type`：

- `allowance_void_manual_review`

目前折讓作廢不呼叫 `/json/g0501`；管理員在光貿網站人工操作後，本機只負責完成／取消該待辦。若待辦已超過本機保留兩期，也納入既有「管理員結案」規則：只清除本機追蹤狀態並解除待辦，不代表光貿已完成折讓作廢。

## 7. 官方折讓資料

`invoice_query.allowance[]` 會正規化為本機 `InvoiceAllowanceResult`，目前保存／顯示的重要欄位包含：

- `invoice_type`
- `invoice_status`
- `allowance_type`
- `allowance_number`
- `allowance_date`
- `tax_amount`
- `total_amount`

官方折讓資料會隨發票 query 更新到本機 metadata，用於歷史顯示、人工折讓確認與折讓 PDF Cache 版本判定。Pending 工作與已完成歷史必須分開；待辦不因出現在本機 metadata 就被當成官方已完成。

## 8. sync_issues

`sync_issues` 同時承載：

1. 技術問題，例如 `invoice_list`／`invoice_query` 失敗、本機寫入失敗、解析／比對失敗、`ambiguous_match`。
2. 需要管理員處理的人工作業，例如紙本作廢確認、折讓人工處理、折讓作廢人工處理。

V2.6.11 UI 以 InvoiceWorkQueue 唯讀投影分為「上傳問題／處理中」，不新增資料表或第二套 pending store。資料來源仍不同：

- 技術問題／人工待辦：`sync_issues`
- 開立失敗：`invoices` 中 `invoice_state=開立失敗` 的紀錄
- 正常上傳／作廢等待：invoices 狀態與官方確認 metadata，投影合成唯讀列，不製造假 sync issue
- 正常折讓等待：既有 allowance_manual_review issue 與明確 workflow 分類

只有正常且目前公司／環境唯一匹配的狀態才列處理中；unresolved 技術問題優先。開啟處理中不更新 upload-issue read-state。

只有開立失敗列可以被批次清除。人工 pending 不得用一般 Failed 刪除路徑移除。

## 9. 舊 JSON → SQLite 遷移

第一次啟動且 `CYInvoice.db` 不存在時：

1. 完整讀取 `invoices.json`、`buyer_names.json`。
2. 損壞 JSON 時停止，不建立空 DB 覆蓋。
3. 在同一 Data 目錄建立暫存 `.migrating` SQLite。
4. transaction 建立 schema、匯入、驗證。
5. 全部成功後才原子切換為 `CYInvoice.db`。
6. 舊 JSON 保留。
7. 員工 schema 另以 additive transaction 建立。

若既有 DB 損壞、schema 不符或驗證失敗，程式停止並回報，不得靜默改建空資料庫。

## 10. 同步與 retention

- AMEGO／光貿官方資料是發票內容權威來源；SQLite 是 Cache。
- 啟動、每 5 分鐘與手動重新整理共用 recent 3-day sync。
- 手動重新整理有 30 秒冷卻；同步重疊不排隊。
- 每日本機日第一次正式環境同步校對目前兩月發票期別＋上一期。
- 雙擊任一發票開詳細資訊前，一律再 `invoice_query`。
- 正式環境發票 Cache 保留目前及上一期；測試環境保留當日。
- 無法判定日期者不得靠猜測刪除。
- 員工資料不屬於發票 retention。
- 未完成且仍在追蹤的發票作廢、折讓、折讓作廢人工待辦／pending 不得被一般 retention 直接清除。
- 超過兩期的舊待辦只有在管理員手動結案後才停止追蹤並恢復一般清理資格。

## 11. Cloud → Local reset 狀態

雙重確認後先關閉主 UI／背景同步，Device revoke 結果由 narrow self-status 確認；只有 Device terminal 或 Workspace inactive 的明確證據才清目前安裝 Data／Cache／settings／identity。最小 reset marker 位於 Data／Cache 外，不保存密碼或 Token；ambiguous 結果保留資料／Token，下一次啟動恢復確認。此流程不刪除中央 Workspace／Employee／其他 Device；不是 Local／Cloud mode flag 切換。

## 12. 安全原則

- 遠端成功與本機保存成功是兩件事。
- 結果不明不得盲目重送。
- 同來源＋原始訂單防重規則保留。
- 同步只做唯讀查詢與本機更新，不呼叫開票 API 重送。
- 本機結案不代表光貿已完成作廢／折讓／折讓作廢。
- 正式公司資料、員工密碼／復原碼、發票歷史、App Key、平台密碼、執行期 DB／LOG 不得進入 Public Git。


## Cloud Device metadata（V2.6.14）

既有 devices.client_version／last_seen_at 由 authenticated self usage 更新；後者為 Cloud UTC、Windows 顯示本機時區。paired_at／created_at 繼續代表加入時間，不由啟動回報覆寫。display_name 可經同 Workspace 中央超管改名，使用既有最大 120 字界限。0012_device_rename_audit.sql 保存既有 security_audit_events 再加入 device_renamed vocabulary；不回寫既有 migration，不刪 revoked Device。

## CYID protected consumer state（V2.6.15）

SettingsModels 新增 `cloud_identity_provider`（BUILT_IN／CYID，default BUILT_IN）及 `cyid_binding_enc`。Confirmed CYID binding 由 Device-authenticated discovery 取得，包含 gateway URL、CYInvoice Workspace、Device、CYID Workspace、Application、Consumer Version、Device Token SHA-256 digest；整筆由 Windows DPAPI 保護，讀取時必須符合目前 endpoint／Device／token。不保存 CYID Session 或 password，不把 client 提供的 scope 當 authority。

切換沿用原 `cloud_workspace_id`／`cloud_device_id`／`cloud_device_token_enc`；CYID Workspace 只保存在 identity binding，不覆寫業務 Workspace。Data／Cache、SQLite 發票／pending 及其他公司設定不因 authority 切換重建或清除。

`Data/cyid_offline_cache.json` 以 employeeNo 索引 protected entry；每筆完整 binding、AppPrincipal（含 Role／credentialVersion／employeeRevision）、隨機 salt 及本機 PBKDF2-SHA256 210000 次 proof 都在 DPAPI ciphertext 中。Proof 是 online success 後本機建立，不是 CYID credential verifier。Offline 僅 transport failure 可用，中央拒絕／失效會清除相應 entry；Device／scope 改變不能重用。沒有新增 TTL，不聲稱即時得知離線撤銷。

Cloud→Local destructive reset 的既有完整 Data replacement 也移除 CYID binding/cache。Built-in SQLite employee cache 保留自己的模式用途，CYID 不下載或使用它作 Employee authority；不新增 CYID employee replica 或第二套 business state。
