# Changelog

## V1.0.0 Build 5 — 2026/09/14

- 完整接回正式 Excel 轉換核心，輸出結構重新對齊既有 V12N4 相容格式。
- 轉換時保留 ERP 來源儲存格的字串／數值型別；外觀看似數字但原本是文字的銷貨單別、銷貨單號、序號等不再被誤寫成數值。
- 輸出 XLSX 改用 `sharedStrings.xml`、Office theme、Excel workbook metadata 與既有日期格式；日期欄位維持 Excel 數值日期並套用 `yyyy/m/d`。
- 輸出改採同一目的資料夾內先寫暫存檔、完成後再 rename，避免轉換中斷時留下可被 POS 誤匯入的半成品。
- 原生檔案選擇器新增 `CommDlgExtendedError` 判斷，能正確區分使用者取消與真正 Win32 對話框錯誤。
- 新增型別保留、shared strings 與日期 style 單元測試。
- 以本機既有 COPI08 樣本完成實際轉換驗證，並與既有可接受輸出逐儲存格比對；測試資料未提交 GitHub。

## V1.0.0 Build 4 — 2026/09/14

- `settings.json` 改為與 EXE 同目錄，移除對 `%LOCALAPPDATA%` 的依賴；舊 Local AppData 設定不再讀取。
- 主視窗新增最小尺寸限制，不能縮小於既有 760×610 基準。
- 歷史紀錄序號欄加寬，並依 ListView 實際寬度自動分配所有欄位，消除右側多餘空白區。
- 視窗放大時，歷史紀錄欄位把新增寬度平均分配給五個欄位。
- 歷史紀錄恢復斑馬紋；仍使用 Windows 原生 ListView，僅透過 `NM_CUSTOMDRAW` 設定交錯列背景，不改回整張表格自繪。
- 無歷史資料時保留 10 個空白原生 ListView 列，維持預期的十列斑馬紋外觀。

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
