# TriINVCalc

**三聯式發票開立計算機**是一個 Windows x64 單機小工具，用於將含稅商品資料換算為三聯式發票所需的未稅單價、未稅金額、銷售額與 5% 營業稅，並以三聯式發票版面即時預覽。

目前正式基準：`V1.5.5`

## 主要功能

- 固定五筆商品輸入：品名、數量、含稅單價。
- 數量範圍 1–999；含稅單價範圍 1–9,999 元，最多兩位小數。
- 含稅折扣以整數元輸入。
- 每列含稅金額先四捨五入為整數元。
- 由折扣後含稅總計反推整數營業稅與未稅銷售額。
- 未稅單價優先使用兩位小數，必要時自動使用三位小數，使明細單價 × 數量可核對到整數未稅金額。
- 自動分配四捨五入尾差，並執行總額與明細核對。
- Enter／Tab 依序移動輸入欄位。
- 計算異常另寫 ERROR log；一般 Trace log 採循環上限。
- Windows PE resource 內嵌多尺寸 Icon 與 manifest。

## 建置

需求：

- Windows x64
- Go 1.22 或更新版本
- PowerShell

在本目錄執行：

```powershell
./build.ps1
```

建置腳本會：

1. 從 `assets/icon.ico.b64` 重建 `icon.ico`。
2. 使用 `akavel/rsrc` 產生 Windows icon/manifest resource。
3. 執行 `go test ./...`。
4. 以 Windows GUI subsystem 建置 `TriINVCalc.exe`。
5. 從 `VERSION` 與 `BUILD` 注入使用者可見版本身分。
6. 在 `dist/` 產生 portable ZIP。

## 資料與隱私

本工具不串接電子發票平台或政府 API，不保存買受人、統編、地址或發票號碼。執行期產生的 Trace／ERROR log 僅供本機除錯，不應提交到 repository。

## 授權

本 repository 為公開可檢視原始碼，但並非開放原始碼。使用、修改、散布與商業利用權利依 repository 根目錄 `LICENSE` 為準。

Copyright © 2026 C.C. Liu, Chihyuan Co. All Rights Reserved.
