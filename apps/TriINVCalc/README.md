# TriINVCalc

**三聯式發票開立計算機**是一個 Windows x64 單機小工具，用於將含稅商品資料換算為三聯式發票所需的未稅單價、未稅金額、銷售額與 5% 營業稅，並以三聯式發票版面即時預覽。

**目前正式版本：`V1.5.6`，已完成驗收與 GitHub Release。**

[正式 Release（Windows x64）](https://github.com/simonliu1118-byte/CYapps/releases/tag/TriINVCalc-v1.5.6) · [Portable ZIP](https://github.com/simonliu1118-byte/CYapps/releases/download/TriINVCalc-v1.5.6/TriINVCalc_V1.5.6.zip) · [獨立 EXE](https://github.com/simonliu1118-byte/CYapps/releases/download/TriINVCalc-v1.5.6/TriINVCalc.exe)

專案功能與視覺調整已結案，後續僅視需要進行維護；正式原始碼以 `main` 為準。前版 `V1.5.5` GitHub Release 已依使用者指示移除，其版本說明留在 Git 中作歷史紀錄。

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

## 視覺資產

本程式使用 AITeam `shared/cy-visual/icon-family/apps/tri-invoice-calc/` 的正式 `CAL.svg` 與七尺寸 `CAL.ico`，圖示不重新設計。介面依 CY Desktop Visual Guide 使用中性色作為主體，珊瑚紅作為少量強調，錯誤與成功維持獨立語意色。

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

1. 核對 AITeam 正式 `assets/CAL.ico` 的 SHA-256 並內嵌 Windows PE resource。
2. 使用 `akavel/rsrc` 產生 Windows icon/manifest resource。
3. 執行 `go test ./...`。
4. 以 Windows GUI subsystem 建置 `TriINVCalc.exe`。
5. 從 `VERSION` 與 `BUILD` 注入使用者可見版本身分。
6. 在 `dist/` 產生 portable ZIP。

## 驗收與正式發行

- V1.5.6 已通過 Windows x64 GitHub Actions 的建置、Go 測試、公開封裝安全掃描與 EXE／ZIP SHA-256 產生檢查。
- 已以 AITeam 原始資產 SHA-256 驗證 `CAL.ico`；不自行重新繪製圖示。
- 使用者已確認本次介面與 Icon 更新方向；`125%`／`150%` DPI 與 Windows Explorer／工作列圖示的個別環境顯示仍屬視覺規範中的實機驗收項目，不宣稱已全面測試。
- 正式發行走 [TriINVCalc Release](https://github.com/simonliu1118-byte/CYapps/actions/workflows/tri-inv-calc-release.yml) 的 `main` 手動工作流程；未經使用者新一輪核准，不自動建立下一個正式 Release。

## 資料與隱私

本工具不串接電子發票平台或政府 API，不保存買受人、統編、地址或發票號碼。執行期產生的 Trace／ERROR log 僅供本機除錯，不應提交到 repository。

## 授權

本 repository 為公開可檢視原始碼，但並非開放原始碼。使用、修改、散布與商業利用權利依 repository 根目錄 `LICENSE` 為準。

Copyright © 2026 C.C. Liu, Chihyuan Co. All Rights Reserved.
