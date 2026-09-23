# CYERPAutoInput Project Rules

本文件只記錄 `apps/CYERPAutoInput/**` 的專案專屬永久規則；共通規則與 Public repository 政策分別以根目錄 `REPOSITORY_RULES.md`、`REPO_POLICY.md` 為準，不在此重複。

## 1. 專案定位

- 正式名稱：`CYERPAutoInput`；中文顯示名稱為「SMART ERP 自動輸入工具」。
- 目標是以 Windows 桌面自動化操作鼎新 SMART ERP `COPI08` 銷貨單建立作業。
- 正式流程限定為：`新增 -> 輸入 -> 儲存`；不自動操作「修改」或「取消」。
- Windows 發行目標為 x64 portable GUI 程式。
- `V0.1.0` 起正式實作基準為 C# / .NET 8 / WinForms；既有 Go source 僅保留作舊版行為與除錯參考，不再作為正式 build target。

## 2. ERP 操作安全

- 對 SMART ERP 的自動鍵盤操作禁止送出 `Ctrl+A`，避免觸發 ERP 非預期行為。
- 程式開始自動操作前必須找到 COPI08；若視窗最小化或被遮蔽，應還原並帶到前景後再操作，不以真正背景輸入作為正式方案。
- 任一自動流程必須支援 `Esc` 緊急停止；停止只中止 CYERPAutoInput 後續自動行為，不得替使用者按 ERP「取消」。
- 程式不得自行輸入銷貨單號；單號一律交由 SMART ERP 自行產生。
- 寫入需觸發 ERP 原生焦點、離焦、lookup、validation 或 dataset 行為；不得把「控制項文字看起來已改變」等同於 ERP 已接受資料。

## 3. Public source 與本機資料

- Source、README、測試、workflow 與 Release note 不得包含真實客戶代號、品號、倉別、部門、人員、貨運別、ERP 下拉選項、公司內部路徑、帳密、訂單或其他營運資料。
- ERP 下拉選項與公司實際欄位值必須由使用者本機設定或由 ERP 現場讀取，不得硬編碼志遠實際值於 source。
- `config/settings.json`、`logs/`、匯入資料、runtime cache 與診斷輸出只屬本機資料，不得提交 Git。
- 可提交的設定範例只能使用空白、假資料或去識別化值。
- 光學辨識用的 runtime 截圖不得自動提交或上傳；正式流程預設以記憶體或系統暫存檔處理，暫存檔使用完即刪除。

## 4. 診斷與 LOG

- GUI 不需要內嵌完整 LOG 檢視；診斷資訊寫入本機 log 檔供除錯。
- 一般 LOG 優先記錄欄位名稱、控制項類型、狀態與成功／失敗，不主動記錄實際客戶或交易值。
- 若為深入診斷必須記錄實際 ERP 顯示內容，該檔仍只能留在本機並由使用者自行提供，不得提交或自動上傳 GitHub。

## 5. 下拉選項

- 下拉選項讀取必須是有界操作：需有停止條件、時間或步數上限，讀取失敗時不得持續無限點擊或送鍵。
- 欄位設定介面應能顯示實際讀到的選項，讓使用者確認後再保存為本機設定。
