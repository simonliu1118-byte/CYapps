# CYInvoice V2 遷移紀錄

- Go／Win32 V1.1.0 是上一個正式回退基線。
- `cyinvoice/csharp-remake` 曾用 `V1.1.0-cs.N` 進行獨立 C#／WinForms parity、Windows CI 與實機 UI 驗收。
- 使用者於 2026/09/15 完成驗收並批准 Major 升級；C#／WinForms 自 V2.0.0 起取代 Go，成為 `main` 唯一正式產品線。
- 原 `VERSION-CS`、永久 C# 分支與 Go 現行 source 已停止使用；Go V1.1.0 由歷史 tag／Release／commit 保存。
- V2.0.0 保留原有發票成功判定、防重、結果不明禁止重送、測試／正式環境隔離、DPAPI 與固定 7 位小數安全語意。
- V2.3.0 於 2026/09/18 正式發布，新增鼎新 ERP 直接 Excel 匯入、共用統編／買方名稱查詢行為、官方 PDF 直接列印與相關 UI 整理；正式 tag 為 `cyinvoice-v2.3.0`。

## V2.4.0：JSON → SQLite 與 AMEGO 同步

V2.4.0 為目前工程測試基準，尚未建立正式 Release；最新公開正式 Release 仍為 V2.3.0。

- 發票、商品明細與人工買方名稱的主要本機保存改為 `Data/CYInvoice.db`；安全設定 `Data/settings.json` 繼續使用 JSON，DPAPI 與管理密碼安全語意不變。
- 第一次啟動且 SQLite DB 不存在時，先完整讀取既有 `invoices.json`／`buyer_names.json`，在同一 `Data` 目錄建立 `.migrating` 暫存 DB，以 transaction 建立 schema 與匯入資料；完成筆數及內容交叉驗證後才原子切換成 `CYInvoice.db`。
- 舊 JSON 不會因成功遷移而刪除；若舊 JSON 損壞、既有 SQLite 損壞或 schema 驗證失敗，程式停止並回報，不會以空白 DB 覆蓋既有資料。
- SQLite 使用技術 `local_id` 主鍵；發票號碼、OrderID、API OrderID 只建立索引，不設過度嚴格的 business UNIQUE，避免本機 Cache 拒絕光貿官方合法但特殊的遠端狀態。
- AMEGO／光貿官方資料成為發票內容權威來源；SQLite 定位為本機 Cache 加上 CYInvoice 本機來源／安全 metadata。遠端買受人、統編、金額、品項、作廢與上傳狀態變化都屬正常官方更新，不以內容差異建立衝突。
- 正式環境加入 `/json/invoice_list` recent sync：啟動、每 5 分鐘與手動重新整理共用同一套最近 3 天同步核心；手動重新整理冷卻 30 秒，同時間已有同步時略過，不另排隊。
- 每個本機日第一次自動同步會校對目前兩月發票期別與上一期別；測試環境只回查本機當日已知測試資料，不掃描光貿共享測試池。
- 雙擊任一發票開啟詳細資訊前，一律再執行 `invoice_query`；無法向光貿確認最新內容時，不以舊 Cache 冒充最新資料。
- 正式環境本機 Cache 保留目前及上一個兩月期別，測試環境只保留當日；能確認過期的舊資料會連同相關 PDF／預覽 Cache 與對應 sync issue 清除，日期無法判定時不猜測刪除。
- 真正技術同步問題寫入 `sync_issues` 並去重；同一發票號碼或 OrderID 對到多筆本機候選時停止自動猜測，保留資料並建立 `ambiguous_match`。已開立清單提供「同步問題」視窗供查看、標記完成、刪除與重新整理。
- V2.4.0 的 migration、retention、同步、sync issue、coordinator、WinForms startup smoke、Windows x64 package、PE/layout 與 packaged startup smoke 已納入同一套 Windows CI；正式 Release 前仍需用實際 V2.3.0 Data 備份與正式 AMEGO 帳號完成實機驗證。
