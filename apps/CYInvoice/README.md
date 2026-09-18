# CYInvoice

CY Windows 10/11 x64 電子發票工具。Public 可見不代表開放原始碼；使用、修改與散布權利以 repository 根目錄 `LICENSE` 為準。

Copyright © 2026 C.C. Liu, Chihyuan Co. All Rights Reserved.

## 版本狀態

- 目前開發／工程測試基準：**V2.4.1**。
- 最新公開正式 Release：**V2.3.0**（tag：`cyinvoice-v2.3.0`）。
- V2.4.1 尚未建立正式 Release；只有使用者於當次工作明確要求 `release` 時，才可從 `main` 啟動正式 Release workflow。
- C#／WinForms 自 V2.0.0 起為唯一正式產品線，source 直接由 `main` 維護。
- 唯一版本來源為 `VERSION`；目前 `BUILD=0`。工程測試 Artifact 以 `Channel: engineering` 與 Artifact 名稱和正式包區分。
- Go／Win32 V1.1.0 只保留為歷史公開回退版本，不再位於 `main` 現行 source。

## V2.4.1 主要調整

- 「上傳問題」視窗上下兩個 ListView 都使用直向／橫向格線；欄位目前設定只作預設寬度，實際內容較長時會自動撐寬並使用原生水平 scrollbar，確保完整資訊仍可查看，不再把最後一欄硬壓進視窗。
- 「開立失敗」來源欄縮窄，主要空間留給失敗原因；畫面將光貿 API technical field／code 轉為可理解的中文摘要，原始 API 訊息仍保留在本機資料供診斷。
- 「刪除」在未勾選時不顯示 `(0)`；勾選後才顯示 `刪除(N)`。
- 主清單「上傳問題」按鈕與左側操作按鈕使用相同垂直 margin，維持同一水平線。
- 來源欄的「同步／更新」改為較緊湊的圓角彩色 tag；同步使用藍色系、更新使用黃色系，來源文字本身仍維持正常字級。

## V2.4.0 主要內容

- 本機發票、發票明細與人工買方名稱正式改存 `Data/CYInvoice.db`；`settings.json` 仍保留 JSON，敏感值仍使用 Windows DPAPI。
- 第一次啟動若尚無 SQLite DB，會先完整讀取既有 `invoices.json`／`buyer_names.json`，在暫存 DB 中建立 schema、匯入並交叉驗證後才原子切換；舊 JSON 不會被刪除。既有 DB 若損壞則停止並回報，不會靜默建立空白資料庫覆蓋。
- AMEGO／光貿官方資料為發票權威來源；SQLite 是本機 Cache 加上 CYInvoice 本機安全／來源資訊。遠端買受人、金額、品項、統編、作廢或上傳狀態變更都視為正常官方更新，不當作衝突。
- 正式環境以 `/json/invoice_list` 執行最近 3 天同步；程式啟動、每 5 分鐘背景同步及手動「重新整理」共用同一套同步核心，手動重新整理有 30 秒冷卻，重疊同步直接略過、不排隊。
- 每個本機日第一次自動同步會做較廣的「目前期別＋上一期別」校對；其餘自動同步回到最近 3 天。測試環境不掃描共享測試池，只回查本機當日測試紀錄。
- 雙擊任一已開立發票開啟詳細資訊前，一律先執行 `invoice_query` 更新該張 SQLite Cache；若無法向光貿確認最新資料，不以舊 Cache 冒充最新資料開啟詳細資訊。
- 正式環境本機發票 Cache 只保留目前及上一個兩月期別；測試環境只保留當日。能確認已超出保存範圍的舊資料會清除，相關 PDF／預覽 Cache 與對應舊 `sync_issues` 一併清理。
- 同步遇到真正的技術問題才寫入 `sync_issues`，例如 invoice list／query 失敗、本機寫入失敗、結果不明仍查無或無法唯一對應本機紀錄；相同未解決問題會更新原列，不會每 5 分鐘重複堆疊。
- 已開立發票頁的「上傳問題」視窗以上半技術問題、下半開立失敗的方式呈現；技術問題保留未讀／已讀／已解決狀態，Failed 紀錄則可由使用者批次刪除本機資料。

## V2.3.0 延續功能

- 鼎新 ERP 銷貨單 `.xlsx` 直接匯入：固定讀取「單頭資料／單身資料」，一個活頁簿對應一張銷貨單。
- 鼎新明細以品名／數量／金額為必要資料；若有單位欄則一併傳送至 AMEGO。明細金額為權威值，單價由金額 ÷ 數量推導，支援合法負數折扣列。
- 手動開票與鼎新匯入共用 8 碼統編自動查詢、API 名稱基準、名稱不一致提示與欄內 `↻` 強制 API 重查。
- 手動自動 OrderID 使用 `MYYYYMMDDXXX`。
- 紙本發票支援官方 PDF 檢視與直接列印；公司統編可選五種官方版型，直接列印使用 CYInvoice 記住的發票印表機，不修改 Windows 全域預設印表機。

## 技術基準

- 語言／UI：C#、.NET 10、Windows Forms。
- 平台：Windows 10/11 x64。
- Solution：`CYInvoice.sln`。
- 執行方式：self-contained 可攜資料夾內的 `CYInvoice.exe`；WebView2 必要組件集中於 `Runtime/WebView2`。
- 發行 ZIP 解壓後根資料夾固定為 `CYInvoice`。
- 現行本機主要資料：`Data/CYInvoice.db`；安全設定：`Data/settings.json`。
- 舊 `Data/invoices.json`、`Data/buyer_names.json` 只作為首次 SQLite 遷移來源／保留備份，不再是現行主要寫入資料庫。
- SQLite business key 不設過度嚴格 UNIQUE；以技術 `local_id` 為主鍵，發票號碼／OrderID 使用索引與應用層匹配，遇到多筆候選時停止自動更新而不是猜測。

## 開發與驗證

日常版本／文件變更經 PR 通過並合併後只提供工程測試 Artifact。Windows CI 會執行 source confidentiality scan、warnings-as-errors、WinForms startup smoke、核心回歸、SQLite migration／retention、invoice sync、sync coordinator、Windows x64 package、PE／layout 與 packaged startup smoke。

正式 Release 只有在使用者當次工作明確要求 `release` 後才執行；不得因版本號已推進到 V2.4.1 而自動發布。

## 目錄

```text
apps/CYInvoice/
├─ src/CYInvoice.Core/                    # 發票、安全、SQLite、同步與匯入核心
├─ src/CYInvoice.WinForms/                # Windows Forms 正式 UI
├─ tests/CYInvoice.Core.Tests/            # 核心 parity／回歸
├─ tests/CYInvoice.SqliteMigration.Tests/ # SQLite migration／retention
├─ tests/CYInvoice.Sync.Tests/            # AMEGO 同步與 sync issue
├─ tests/CYInvoice.SyncCoordinator.Tests/ # 啟動／排程／手動同步協調
├─ assets/                                # icon 等 Windows 建置資源
├─ scripts/                               # 建置、打包與驗證腳本
└─ docs/                                  # 規格、測試、歷史與待辦文件
```

## 文件

- [功能基準](docs/REQUIREMENTS.md)
- [CYInvoice 永久規則](PROJECT_RULES.md)
- [待辦與後續規劃](docs/TODO.md)
- [RC／實機測試](docs/RC_TEST.md)
- [本機資料格式與安全規則](docs/DATA_FORMAT.md)
- [V2 遷移紀錄](docs/MIGRATION_HISTORY.md)
- [歷史版本紀錄](CHANGELOG.md)
