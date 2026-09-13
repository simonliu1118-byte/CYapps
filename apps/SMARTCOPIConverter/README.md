# SMARTCOPIConverter

SMART 銷貨單格式轉換工具，用於將 ERP COPI08 匯出的 Excel 交易資料轉換為 SMART 銷貨單匯入格式。

## 使用方式

1. 第一次啟動時選擇 SMART POS 銷貨單匯入檔的輸出資料夾；設定只保存在本機。
2. 在 ERP 選擇要匯出的單據，切換到「交易資料」頁籤，使用 Excel 匯出；正常檔名通常為 `COPI08_2`。
3. 在程式按「選擇檔案」，可一次選擇多個 `.xlsx` 檔案；開始轉換前可按待轉檔清單右側 `×` 移除單項。
4. 按「開始轉換」，確認所有檔案均轉換成功。
5. 轉換檔依 `COPI-銷貨單別-銷貨單號.xlsx` 命名並寫入已設定的 POS 輸出資料夾。

## 主要行為

- 支援多檔批次轉換。
- 來源欄位依欄名對應，不依 ERP 匯出欄位位置固定假設。
- 缺少必要欄位的檔案不輸出，並列入失敗紀錄。
- `客戶描述` 在輸出時清空。
- 保留最近 99 筆成功轉檔紀錄。
- `COPI08_1` 會顯示提醒，但使用者仍可自行確認後繼續。
- 不會靜默覆蓋同名輸出檔。
- GUI 採事件驅動；檔案清單與歷史紀錄使用 Windows 原生 ListView，不以短週期 timer 或持續自繪刷新。

## 本機資料與診斷紀錄

- POS 輸出資料夾設定與轉檔歷史儲存在目前 Windows 使用者的 Local AppData，不提交 Git。
- 程式每次啟動即建立診斷 log：`<EXE所在資料夾>\log\app(YYYYMMDD-HHMMSS).log`，用於追查啟動、路徑設定與轉檔異常。
- 「保留LOG」勾選時會額外記錄較詳細的逐檔轉換資訊；基礎啟動診斷紀錄不受此勾選影響。

## 開發與建置

需求：Go 1.23 或更新版本。

```powershell
go test ./...
$env:GOOS = "windows"
$env:GOARCH = "amd64"
go build -ldflags "-H windowsgui -X main.version=1.0.0" -o SMARTCOPIConverter.exe .
```

正式 CI 由專案 workflow 從 `VERSION` / `BUILD` 讀取版本身分，不在 workflow 另寫一套版本規則。
