# SMARTCOPIConverter Project Rules

本文件只記錄 `apps/SMARTCOPIConverter/**` 的專案專屬永久規則；共通規則與 Public repository 政策分別以根目錄 `REPOSITORY_RULES.md`、`REPO_POLICY.md` 為準，不在此重複。

## 1. 專案定位

- 正式名稱：`SMARTCOPIConverter`；中文顯示名稱為「SMART 銷貨單格式轉換工具」。
- 用途：將鼎新 ERP COPI08 匯出的 Excel 交易資料轉換為 SMART 銷貨單匯入格式。
- 正式 Windows 發行為 x64 portable GUI 程式。

## 2. 資料與公開安全

- POS 輸出資料夾屬本機設定，必須由程式首次啟動時設定，不得把實際公司共享路徑寫死在 source、README、測試或 Release note。
- 不得提交真實 ERP 匯出 Excel、SMART/POS 匯入檔、客戶資料、實際銷貨單內容、runtime log、runtime settings 或含上述資料的截圖。
- 自動測試若需要 Excel 內容，只能使用程式生成的假資料／去識別化測試資料。

## 3. 轉換核心

- 單頭輸出欄位與單身輸出欄位依目前 SMART 匯入格式的固定欄名與順序產生；來源欄位以欄名對應，不依來源欄位位置硬編碼。
- `單頭資料` 至少必須存在 `銷貨單別`、`銷貨單號`；`單身資料` 至少必須存在 `序號`、`品號`、`品名`、`數量`、`單價`、`金額`。缺少必要欄位時該檔不得輸出。
- 輸出的 `客戶描述` 必須清空。
- 輸出檔名固定為 `COPI-銷貨單別-銷貨單號.xlsx`；不得靜默覆蓋同名既有檔案。

## 4. GUI 穩定性

- GUI 必須採事件驅動；不得用短週期 timer 或背景 polling 反覆刷新 UI／網路共享路徑。
- 閒置狀態不得持續存取 POS 網路路徑。
