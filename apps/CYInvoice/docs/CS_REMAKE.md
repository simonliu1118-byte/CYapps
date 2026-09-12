# CYInvoice C# 重製線

## 定位

- 版本以 `V1.1.0-cs.N` 標示。
- 這是 C#／WinForms 重製測試線，並非 Go `V1.1.0` 的後續正式版本。
- Go `V1.1.0` 的 commit、tag、Release 與原始碼保持不動，若重製失敗可直接回復。
- C# 重製在功能、安全規則與實機操作通過前，不得取代公司使用的 Go 正式版。

## 第一階段範圍

只重製 Go `V1.1.0` 已有能力：光貿 API、手動開立、ERP／MO店+／酷澎匯入、設定、本機紀錄、查詢與唯讀明細。

PDF 預覽、作廢與折讓不進入第一階段。完成同等功能與實機驗收後，才進入 `1.2.0` 功能開發。

## 不可改變的安全規則

- 結果不明、處理中及未知狀態一律禁止重送。
- `OrderID` 不可單獨視為成功；必須核對發票號碼、金額、稅額、總額、明細稅別及作廢狀態。
- 測試／正式環境資料與防重判斷隔離；舊紀錄採較安全的跨環境阻擋。
- 遠端已成功但本機保存失敗，仍須呈現「已開立」與本機保存錯誤，不可改標失敗。
- API、Excel、檔案 I/O 不在 UI thread 執行；只有 UI thread 能操作 WinForms control。
- 金額核心使用固定 7 位小數，不使用 binary floating point 作為發票計算依據。
- 設定與發票 JSON 損壞時停止並保留原檔；不得自動覆寫。
- App Key 與 MO店+ 密碼使用 Windows DPAPI；不得明碼落盤或寫入 Log。

## 目錄

- `src/CYInvoice.Core`：計算、模型、API、儲存與匯入等非 UI 邏輯。
- `src/CYInvoice.WinForms`：Windows Forms UI；僅協調 UI 狀態與背景工作。
- `tests/CYInvoice.Core.Tests`：不依賴外部 test framework 的 parity test runner，CI 直接執行。

## 目前狀態

`V1.1.0-cs.1` 已建立 C# 核心、WinForms 可編譯入口、光貿簽章／查詢 client，以及與 Go JSON 相容的設定、發票紀錄及買方名稱儲存層。設定使用相同 PBKDF2 參數，Windows 執行端使用 DPAPI；JSON 採同目錄暫存檔、flush-to-disk 與 `MoveFileExW` write-through 取代。

手動開立的安全狀態機已依 Go 正式版移植：送出前先保存處理中紀錄、明確 API 拒絕與不明結果分流、Order ID／發票號碼／金額／稅額／DetailVat 嚴格回查、結果不明防重、作廢後以新 API Order ID 重開，以及遠端成功與本機保存失敗分離。負數折扣列、固定 7 位小數與含／未稅計算亦有 parity tests。

MO店+與酷澎原始匯出檔的轉換核心已移植。使用者只選擇平台原始檔：MO店+讀取原始 `OrderExport`，程式在背景 STA 執行緒透過已安裝的 Excel 解密，開檔前強制停用巨集、事件及外部連結；酷澎原始 `.xlsx` 則以受限 Open XML 讀取器在背景執行。兩者都只在記憶體內轉成光貿開票所需的標準資料，再進入相同的固定小數與發票安全驗證。光貿官方轉換後範例只作為欄位映射及結果比對依據，不是日常輸入檔。\n\n目前仍未完成 ERP 匯入與實際操作畫面。程式會明確顯示不可用於開立發票；未完成同等功能前不提供公司測試 ZIP。
