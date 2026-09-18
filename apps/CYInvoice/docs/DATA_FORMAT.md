# CYInvoice 本機資料格式

本文件記錄 C#／WinForms 正式線目前的本機資料、SQLite 遷移與安全規則。

- 目前開發／工程測試基準：**V2.4.0**。
- 最新公開正式 Release 仍為 **V2.3.0**。
- V2.4.0 的主要本機資料庫為 `Data/CYInvoice.db`；`settings.json` 仍保留 JSON。
- 舊 `invoices.json`／`buyer_names.json` 只作為第一次建立 SQLite 時的遷移來源與保留備份，不再是 V2.4.0 的主要寫入資料。

## 目錄與 Cache

程式啟動會自行建立：

- `Data/`
- `Cache/InvoicePDF/`
- `Cache/InvoicePreview/`

紙本發票 PDF：

`Cache/InvoicePDF/{test|prod}/{yyyyMMdd}/{發票號碼}_style{0|1|2|3|5}.pdf`

詳細資訊第一頁預覽：

`Cache/InvoicePreview/{test|prod}/{yyyyMMdd}/{發票號碼}_style0.page1.png`

PDF／PNG 都必須先通過檔頭與大小驗證再原子移入正式 Cache。正式資料被光貿同步更新時，相關發票的舊 PDF／預覽 Cache 會失效；本機 retention 刪除舊發票時也會清除對應 Cache。

`Cache/WebView2` 只保存 Edge/WebView2 執行資料，不保存 App Key。

## settings.json

`settings.json` 與 SQLite 分開保存，因其生命週期與安全要求不同。

主要相容欄位：

- `environment`：`test` 或 `prod`。
- `prod_invoice`：正式公司 8 碼統編。
- `prod_app_key_enc`：目前 Windows 使用者 DPAPI 加密後的值。
- `mo_password_enc`：同樣使用 DPAPI；原始密碼不得寫入 JSON、LOG 或原始碼。
- `password_salt`、`password_hash`：管理密碼 salted PBKDF2-HMAC-SHA256 210,000 次衍生結果；舊雜湊只保留驗證相容。

切換正式環境前，公司統編與 App Key 都必須存在。

## CYInvoice.db

SQLite schema version 目前為 `1`，主要資料表：

- `invoices`：發票快照與 CYInvoice 本機 metadata。
- `invoice_items`：發票商品明細，外鍵連到 `invoices.local_id`，刪除發票時 cascade。
- `buyer_names`：API 正常但名稱空白時，由使用者人工確認並在成功開立後保存的公司名稱。
- `sync_state`：同步範圍最後成功時間，例如每日兩期校對。
- `sync_issues`：真正的技術性同步異常與解決時間。
- `schema_info`：schema version、建立時間及舊 JSON 匯入數量等遷移資訊。

### 主鍵與比對原則

- `invoices.local_id` 是技術主鍵。
- 發票號碼、OrderID、API OrderID 不設過度嚴格的 business `UNIQUE`，只建立查詢索引。
- 原因是光貿官方資料可能出現合法但不符合本機預期的重開／修改狀態，本機 Cache 不得因 UNIQUE constraint 拒絕官方資料。
- 同步時優先依發票號碼，再依 OrderID 類欄位找本機候選；若同一官方項目對到多筆本機紀錄，停止自動猜測，寫入 `ambiguous_match`，不任意覆蓋其中一筆。

### invoices 重要欄位

官方快照／查詢欄位包含：

- `seller_invoice`
- `environment`
- `invoice_number`
- `order_id`／`api_order_id`
- `invoice_state`
- `buyer_identifier`／`buyer_name`
- `amount`
- `invoice_date`／`invoice_time`
- 載具、捐贈、上傳狀態、主備註、DetailVat 等

CYInvoice 本機 metadata 包含：

- `record_id`
- `source`
- `record_origin`：`local` 或 `sync`
- `original_order_id`
- `sent_at`
- `last_checked`
- `buyer_name_needs_memory`
- `extra_json`

光貿是官方發票內容的權威來源；同步可覆寫官方快照欄位。`record_origin`、原始來源脈絡與開票安全資訊依用途保留，不把本機 Cache 當成官方帳本。

### invoice_items

商品明細保存：

- 品名、數量、單位
- 單價／金額的整數欄及固定精度文字欄
- 稅別、備註
- subtotal rounding 相容旗標

固定精度仍遵守 7 位小數可逆性規則，不得改回以二進位浮點作為財務判定基準。

## 舊 JSON → SQLite 遷移

第一次啟動且 `CYInvoice.db` 不存在時：

1. 先完整讀取 `invoices.json` 與 `buyer_names.json`；損壞 JSON 直接停止，不建立空 DB 覆蓋。
2. 在同一 `Data` 目錄建立隨機名稱的 `.migrating` 暫存 SQLite。
3. transaction 建立 schema，匯入發票、商品及人工買方名稱。
4. 驗證資料筆數、商品筆數、主要欄位與 migration metadata。
5. 全部成功後 commit，並將暫存 DB 原子移為 `CYInvoice.db`。
6. 舊 JSON 不刪除；SQLite 一旦建立完成，之後以 SQLite 為主要資料來源。

若既有 `CYInvoice.db` 損壞、schema 不符或驗證失敗，程式停止並回報；不得靜默改建空資料庫。這項規則特別用來避免遺失 `Unknown`／`Changing` 等本機安全脈絡。

## 同步與權威來源

AMEGO／光貿官方資料是發票內容權威來源；SQLite 是本機 Cache。

因此下列差異不是「衝突」，而是正常官方更新：

- 買受人／統編變更
- 金額變更
- 商品明細變更
- 作廢／重新開立狀態變更
- 上傳狀態變更

同步不得因此重送發票。

真正寫入 `sync_issues` 的情況包括：

- `invoice_list` 失敗
- `invoice_query` 失敗
- 光貿查無資料，尤其本機仍是結果不明
- 本機 SQLite 寫入失敗
- 官方資料無法安全解析／比對
- 多筆本機候選造成無法唯一匹配

相同帳號＋發票／OrderID＋問題類型的未解決 issue 會更新既有列，不會每 5 分鐘重複新增。後續同步若已成功確認，對應問題可自動標記 resolved；UI 也提供手動標記完成或刪除問題紀錄。

## 同步範圍與 retention

- 啟動同步、每 5 分鐘同步、手動重新整理共用相同 recent sync 核心；正式環境 recent 範圍為最近 3 天。
- 手動重新整理有 30 秒冷卻；同步重疊時不排隊。
- 每個本機日第一次自動同步，正式環境校對「目前兩月發票期別＋上一期別」；測試環境只回查當日已知本機測試資料。
- 雙擊任何發票開啟詳細資訊前，一律再 `invoice_query` 該張，確認最新官方內容後才開啟。
- 正式環境只保留目前及上一個兩月期別的本機發票 Cache；測試環境只保留當日。
- 能確認日期已超出保存範圍的資料會清除，不再因舊 `Unknown`／`Changing` 或舊 sync issue 永久保留；與被刪發票直接對應的 sync issue 及 PDF／預覽 Cache 一併清理。
- 無法判定日期的資料不會因猜測而刪除。

## 開票安全仍高於 Cache 同步

SQLite 改為 Cache 不代表可以放寬開票安全：

- 遠端開立成功與本機保存成功仍是兩件事。
- 結果不明時不得盲目重送。
- 同來源＋原始訂單的防重規則仍保留。
- 同步只做唯讀查詢與本機更新，不得呼叫開立 API 重送。
- 上傳狀態只有官方明確狀態才能更新，未知狀態不得視為成功。

測試資料只使用虛構值；正式公司資料、發票歷史、App Key、平台密碼與執行期 DB／LOG 不得進入 Git。
