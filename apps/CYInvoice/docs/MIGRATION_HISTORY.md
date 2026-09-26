# CYInvoice V2 遷移紀錄

本檔保存產品線與資料架構遷移的歷史事實；目前產品狀態以 `README.md`、`VERSION`／`BUILD` 與 GitHub Release 為準。

## 產品線遷移

- Go／Win32 V1.1.0 是上一個正式回退基線。
- `cyinvoice/csharp-remake` 曾用 `V1.1.0-cs.N` 進行 C#／WinForms parity、Windows CI 與實機 UI 驗收。
- 使用者於 2026/09/15 完成驗收並批准 Major 升級；C#／WinForms 自 V2.0.0 起取代 Go，成為 `main` 唯一正式產品線。
- 原 `VERSION-CS`、永久 C# 分支與 Go 現行 source 已停止使用；Go V1.1.0 由歷史 tag／Release／commit 保存。
- V2.0.0 保留原有發票成功判定、防重、結果不明禁止重送、測試／正式環境隔離、DPAPI 與固定 7 位小數安全語意。

## V2.3.0

- 2026/09/18 正式發布，tag：`cyinvoice-v2.3.0`。
- 新增鼎新 ERP 直接 Excel 匯入、共用統編／買方名稱查詢行為、官方 PDF 直接列印與相關 UI 整理。

## V2.4.x：JSON → SQLite、AMEGO 同步與後續收斂

V2.4.0 完成主要資料與同步架構遷移；V2.4.1、V2.4.2 再完成上傳問題 UI、測試 OrderID namespace、紀錄清單排序與多輪實機 UI 修正。最終於 **V2.4.2 Build 0** 正式發布，tag：`cyinvoice-v2.4.2`。

### SQLite 與資料遷移

- 發票、商品明細與人工買方名稱的主要本機保存改為 `Data/CYInvoice.db`；安全設定 `Data/settings.json` 繼續使用 JSON。
- 第一次啟動且 SQLite DB 不存在時，先完整讀取既有 `invoices.json`／`buyer_names.json`，在同一 `Data` 目錄建立 `.migrating` 暫存 DB，以 transaction 建立 schema 與匯入資料；驗證後才原子切換成 `CYInvoice.db`。
- 舊 JSON 不會因成功遷移而刪除；若舊 JSON 損壞、既有 SQLite 損壞或 schema 驗證失敗，程式停止並回報，不以空白 DB 覆蓋既有資料。
- SQLite 使用技術 `local_id` 主鍵；發票號碼、OrderID、API OrderID 只建立索引，不設過度嚴格 business UNIQUE。

### AMEGO 權威資料與同步

- AMEGO／光貿官方資料成為發票內容權威來源；SQLite 是本機 Cache 加 CYInvoice metadata。
- 正式環境加入 `/json/invoice_list` recent sync：啟動、每 5 分鐘與手動重新整理共用最近 3 天同步；手動冷卻 30 秒，重疊同步直接略過。
- 每個本機日第一次自動同步校對目前兩月發票期別與上一期別；測試環境不掃描共享測試池。
- 雙擊任一發票前一律再 `invoice_query`；無法確認最新內容時，不以舊 Cache 冒充最新資料。
- 正式環境本機 Cache 保留目前及上一個兩月期別；測試環境只保留當日。
- 真正技術同步問題寫入 `sync_issues`；多筆本機候選時停止猜測並建立 `ambiguous_match`。

### V2.4.1／V2.4.2 收斂

- 「上傳問題」與 Failed 紀錄的格線、欄寬、錯誤摘要與刪除數量完成 UI 收斂。
- 測試環境加入獨立 OrderID namespace，避免共享測試池撞號；UI 仍顯示原始可讀 OrderID。
- 已開立清單加入可點擊排序表頭並調整來源／Order ID 欄寬。
- 已作廢紀錄、載具預覽與詳細資訊完成多輪實機修正。

## V2.5.x：員工帳號與發票作廢

V2.5 工程線在既有 `CYInvoice.db` 上加入員工與角色，不另建第二套帳號資料庫，也不改寫既有發票 `schema_version=1` 語意。

### 員工 additive schema

- 新增 `employees` 資料表與獨立 `employee_schema_version`。
- 員工編號固定 4 碼；角色為 `SUPER_ADMIN`、`ADMIN`、`EMPLOYEE`。
- 新密碼至少 8 碼且只接受 ASCII 英數字；密碼與復原碼只保存 Hash。
- 第一位帳號為唯一超級管理員；SQLite index／trigger 與應用程式共同保護其不可降級、停用或刪除。
- 舊設定檔管理密碼不再作為現行管理員認證來源；設定與帳號管理改共用員工權限驗證。
- MO店+ Excel 密碼與首次設定解耦，改為需要匯入時才要求設定。

### 發票作廢 workflow

- 一般使用者可驗證帳密後提出作廢。
- 紙本證明聯未收回時建立 `void_manual_review`，不立即送 `/json/f0501`。
- 直接作廢 `CancelReason`：`使用者編號-原因`。
- 管理員覆核作廢 `CancelReason`：`管理員編號-使用者編號-原因`。
- `cyinvoice_void_pending` 在送出前先持久化，結果不明時禁止盲目重送；後續以官方 query 確認。

V2.5.x 為工程線，未取代目前最新公開正式 Release V2.4.2。

## V2.6.x：暫行人工折讓與 pending 管理

V2.6 在正式折讓 API 尚未自動化前，先以本機人工待辦接入實際營運流程。

### 人工折讓

- 一般使用者提出折讓原因與含稅總額，先 `invoice_query` 取得最新發票與既有 `allowance[]` 基線。
- 申請保存於發票 `extra_json` 並建立 `allowance_manual_review`。
- 管理員至光貿網站人工折讓後，CYInvoice 共用 `invoice_query.allowance[]` 比對新折讓，不建立第二套查詢核心。
- 唯一候選且金額吻合才自動確認；多候選或金額不符時保留問題，不猜測。
- 超過目前＋上一個兩月期別的舊 pending 可由管理員本機結案；結案不代表光貿完成。

### V2.6.2 折讓 PDF 與折讓作廢人工待辦

- 新增 `/json/allowance_file` PDF：style 0、1、3，下載後驗證並存入 `Cache/AllowancePDF`，沿用既有版型選擇與 WebView2 Viewer。
- 已完成折讓可由一般使用者提出「折讓作廢」申請，保存 `cyinvoice_allowance_void_manual_review` 並建立 `allowance_void_manual_review` 待辦。
- V2.6.2 **不直接呼叫 `/json/g0501`**；管理員仍在光貿網站人工完成，再於 CYInvoice 詳細待辦中完成或取消。
- 「上傳問題」UI 整併為單一清單；技術問題、Failed 與各類人工待辦集中顯示，但只有 Failed 可勾選清除。

V2.6.2 Build 0 已通過 Windows engineering CI；仍需實機／光貿驗證，尚未建立正式 Release。

## 正式發布結果

- 最新公開正式 Release 仍為 V2.4.2（2026/09/19）。
- Release tag：`cyinvoice-v2.4.2`。
- V2.5／V2.6 目前均屬工程線；後續是否正式發布只依使用者當次明確指示。
- 未完成工作與實機驗證以 `docs/TODO.md`、`docs/RC_TEST.md` 為準。
