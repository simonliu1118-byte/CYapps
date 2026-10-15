# SMARTCOPIConverter

SMART 銷貨單格式轉換工具，用於將 ERP COPI08 匯出的 Excel 交易資料轉換為 SMART 銷貨單匯入格式。

## 使用方式

1. 第一次啟動時選擇 SMART POS 銷貨單匯入檔的輸出資料夾；設定只保存在本機。後續可使用主畫面「設定」更改。
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
- 主視窗最小尺寸固定為既有基準；放大時清單與欄位會跟隨可用寬度調整。

## 本機資料與診斷紀錄

- `settings.json` 與 EXE 放在同一資料夾，保存 POS 輸出資料夾設定與轉檔歷史，不提交 Git。
- 要重置設定時，先關閉程式，再刪除 EXE 同目錄的 `settings.json`；下次啟動會重新要求設定 POS 輸出資料夾。
- 程式每次啟動即建立診斷 log：`<EXE所在資料夾>\log\app(YYYYMMDD-HHMMSS).log`，用於追查啟動、路徑設定與轉檔異常。
- 「保留LOG」勾選時會額外記錄較詳細的逐檔轉換資訊；基礎啟動診斷紀錄不受此勾選影響。

## 開發與建置

Windows x64；Go 1.23 或更新版本。請透過 `build.ps1` 建置（包含 Go 測試、正式 CVT icon 下載與 SHA-256 驗證及內嵌 Windows 資源）；直接使用 `go build` 不代表完整正式發行建置。

```powershell
cd apps/SMARTCOPIConverter
./build.ps1
```

- 版本唯一來源：`VERSION` 與 `BUILD`。
- PR 測試包：`.github/workflows/smartcopiconverter-build.yml`，Windows x64 建置、Go 測試及公開發行安全掃描；Artifact 保留 3 天。
- 正式版：`.github/workflows/smartcopiconverter-release.yml`，經使用者同意後由 `main` 手動觸發，重新 Build/Test、公開安全掃描並提供 ZIP、EXE 與 SHA-256。
- ZIP 請解壓至可寫入的 Windows 資料夾再啟動。因 `settings.json` 與 log 會寫入 EXE 目錄，請勿直接在壓縮檔或唯讀路徑執行。

## CVT 視覺與正式圖示

- 正式識別圖示由 `AITeam/main:shared/cy-visual/icon-family/apps/converter/` 管理；本專案保留核准的 `assets/CVT.svg` 供查閱。
- 建置流程由 AITeam 固定提交版本下載 **CVT.ico**（內含 16、24、32、48、64、128、256 px 七個原生尺寸），並比對母本 SHA-256。建置機器需要可連線至 GitHub。
- 配色遵循 AITeam `CY_DESKTOP_VISUAL_GUIDE.md`：CVT 識別色 `#F09D03`，桌面操作主題使用 Apricot 色系；主要按鈕選用深橘底色搭配白字以確保對比，一般動作保留原生控制項。
- 維持 Windows 高對比模式、原生清單選取與鍵盤焦點，不改 Excel 轉換邏輯。

## 正式發行

- `V1.0.1` 的發行說明為 `V1.0.1.txt`；歷史變更見 `CHANGELOG.md`。
- 正式 EXE/ZIP 只保存於 GitHub Releases，不提交 binary 至 Git 原始碼庫。
- 本機 POS 輸出資料夾只透過首次啟動或「設定」按鈕指定，不把公司共享路徑寫入原始碼或正式發行包。
