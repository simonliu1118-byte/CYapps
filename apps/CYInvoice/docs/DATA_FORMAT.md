# CYInvoice 本機資料格式

本文件記錄 C#／WinForms 現行工程線的本機資料、SQLite 遷移、Cache 與安全規則。

- 目前工程測試基準：**V2.6.2 Build 0**。
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

V2.6.2 暫行格式：

`Cache/AllowancePDF/{test|prod}_{折讓單號}_{style}.pdf`

目前折讓 PDF style 只允許：

- `0`：A4
- `1`：A4 (地址+A5)
- `3`：A5

PDF 下載後必須通過大小與 `%PDF` 檔頭驗證，再原子寫入 Cache。光貿短效 `file_url` 不保存到本機資料庫或設定檔。

## 2. settings.json

目前 `Settings` 正式欄位：

- `environment`：`test` 或 `prod`。
- `prod_invoice`：正式公司 8 碼統編。
- `prod_app_key_enc`：Windows DPAPI 加密後的正式 App Key。
- `mo_password_enc`：Windows DPAPI 加密後的 MO店+ Excel 密碼。
- `invoice_printer_name`：CYInvoice 記住的發票印表機名稱。

V2.6.x 已不再以舊管理密碼欄位作為目前管理員認證來源。管理／員工登入統一使用 `employees` 資料表；`settings.json` 不保存員工密碼或復原碼。

MO店+ 密碼不是首次設定必要條件；未設定時程式仍可啟動，但按 MO店+ 匯入前必須先完成設定。

## 3. CYInvoice.db

目前發票 schema version 維持 `1`；員工資料使用獨立 `employee_schema_version` 管理 additive schema。

主要資料表：

- `invoices`：發票快照與 CYInvoice metadata。
- `invoice_items`：發票商品明細。
- `buyer_names`：人工確認後保存的公司名稱記憶。
- `sync_state`：同步與 UI read-state 等時間標記。
- `sync_issues`：技術問題及人工待辦。
- `employees`：員工帳號／角色／密碼 Hash／復原碼 Hash。
- `schema_info`：schema version 與遷移 metadata。

### schema_info

至少可能包含：

- `schema_version=1`
- `employee_schema_version=1`
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
- `role`：`SUPER_ADMIN`、`ADMIN`、`EMPLOYEE`。
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
- `cyinvoice_void_manual_review`：紙本證明聯未收回時的人工覆核申請，包含申請員工、原因、申請時間。

對應 `sync_issues.issue_type`：

- `void_manual_review`
- 作廢核心另有等待官方確認類型，由既有作廢同步流程維護。

### 折讓人工申請

- `cyinvoice_allowance_manual_review`：人工折讓申請，包含申請員工、原因、含稅金額、申請時間、提出申請當下既有折讓單號基線、等待確認狀態及人工完成時間等。
- `cyinvoice_allowance_pending`：管理員已完成網站操作，等待 `invoice_query.allowance[]` 官方資料確認。

對應 `sync_issues.issue_type`：

- `allowance_manual_review`

### 折讓作廢人工申請

- `cyinvoice_allowance_void_manual_review`：V2.6.2 暫行折讓作廢待辦，包含折讓單號、申請員工、原因、申請時間。

對應 `sync_issues.issue_type`：

- `allowance_void_manual_review`

目前折讓作廢不呼叫 `/json/g0501`；管理員在光貿網站人工操作後，本機只負責完成／取消該待辦。

## 7. 官方折讓資料

`invoice_query.allowance[]` 會正規化為本機 `InvoiceAllowanceResult`，目前保存／顯示的重要欄位包含：

- `invoice_type`
- `invoice_status`
- `allowance_type`
- `allowance_number`
- `allowance_date`
- `tax_amount`
- `total_amount`

官方折讓資料會隨發票 query 更新到本機 metadata，用於歷史顯示與人工折讓確認。Pending 工作與已完成歷史必須分開；待辦不因出現在本機 metadata 就被當成官方已完成。

## 8. sync_issues

`sync_issues` 同時承載：

1. 技術問題，例如 `invoice_list`／`invoice_query` 失敗、本機寫入失敗、解析／比對失敗、`ambiguous_match`。
2. 需要管理員處理的人工作業，例如紙本作廢確認、折讓人工處理、折讓作廢人工處理。

V2.6.2 UI 將它們與開立失敗紀錄集中在單一「上傳問題」清單顯示，但資料來源仍不同：

- 技術問題／人工待辦：`sync_issues`
- 開立失敗：`invoices` 中 `invoice_state=開立失敗` 的紀錄

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
- 未完成且仍在追蹤的作廢／折讓 pending 不得被一般 retention 直接清除。
- 超過兩期的舊 pending 只有在管理員手動結案後才停止追蹤並恢復一般清理資格。

## 11. 安全原則

- 遠端成功與本機保存成功是兩件事。
- 結果不明不得盲目重送。
- 同來源＋原始訂單防重規則保留。
- 同步只做唯讀查詢與本機更新，不呼叫開票 API重送。
- 本機結案不代表光貿已完成作廢／折讓。
- 正式公司資料、員工密碼／復原碼、發票歷史、App Key、平台密碼、執行期 DB／LOG 不得進入 Public Git。
