# Changelog

## V1.0.0 Build 3 — 2026/09/13

- 修正主視窗文字標籤出現灰色色塊的外觀問題。
- 使用 Win32 `WM_CTLCOLORSTATIC` / `WM_CTLCOLORBTN` 配合系統 `COLOR_WINDOW` 背景處理，讓靜態文字與「保留LOG」核取方塊背景和主視窗一致。
- 這項修正仍使用 Windows 原生控制項與標準訊息處理，不加入自繪、owner-draw、timer 或背景 polling。

## V1.0.0 Build 2 — 2026/09/13

- 修正按「選擇檔案」時因 `OPENFILENAMEW` filter 直接傳入含 NUL 的字串，造成 `syscall.StringToUTF16` panic 並結束程式的問題。
- filter 改為逐段轉成 UTF-16，再組成 Win32 要求的 NUL 分隔與雙 NUL 結尾格式。

## V1.0.0 Build 1 — 2026/09/13

- 修正首次設定 POS 輸出資料夾後可能無回應的問題：首次設定改為主訊息迴圈啟動後，以事件觸發原生資料夾選擇器。
- 主視窗尺寸與主要控制項位置恢復接近既有 V12N4 版面。
- 減少 Go 自繪：待轉檔清單與歷史紀錄改用 Windows 原生 ListView；保留事件驅動，不加入短週期 timer 或背景 polling。
- 程式啟動即在 EXE 同目錄 `log/` 建立診斷紀錄，可追蹤啟動、路徑選擇、設定儲存與批次轉換階段。
- 本機設定改採暫存檔後 rename 的原子寫入方式，降低設定檔中斷損壞風險。

## V1.0.0 — 2026/09/13

- 首次納入 `CYapps` 正式版本管理。
- 提供 COPI08 Excel 至 SMART 銷貨單匯入格式轉換。
- 支援多檔批次處理、失敗紀錄與最近 99 筆成功轉檔紀錄。
- POS 輸出資料夾改為首次啟動時由使用者設定，不在 source 中保存實際公司共享路徑。
- GUI 採事件驅動，不使用短週期 timer 進行背景刷新。
