# CYEnvelope

志遠專用 Windows 信封套印工具。正式基準仍是 Go 版 V0.1.1；開發分支 `cyenvelope/csharp-remake` 正分階段重做 C#／WPF，目前測試版本 V0.2.1 Build 1。本分支尚未正式發行。

## 重做進度

| 階段 | 狀態 | 內容 |
| --- | --- | --- |
| 1. 核心與資料 | 已提交、跨平台編譯 | 全新 SQLite 資料層；聯絡人多地址、多電話模型；368 筆離線三碼郵遞區號；電話格式；共用信封繪製器；核准 ENV 圖示 |
| 2. 介面與列印 | 依共通視覺規範修整，待 Windows CI 與實機操作檢查 | 收件表單與信封預覽採分區工作區，調整字級、間距與操作層級；預覽點選輸入、聯絡人管理、格式編輯與橫式欄位旋轉；按列印先保存，再用共用繪製器輸出 |
| 3. Windows 測試包與試印 | Windows CI 與 Artifact 已通過，待實機試印 | Windows x64 portable、掃描後上傳 14 天 Artifact、啟動檢查；15K 實機試印及位置校正尚待驗收 |
| 4. 下載與解壓縮 | Build 1 待 Windows CI 驗證 | 根目錄只有啟動 EXE、VERSION、BUILD、Runtime；WPF 程式與 6 個原生 DLL 同放 Runtime；下載後解壓縮一次即可使用 |

目前有 WPF 主畫面與管理視窗；預覽點選欄位後會以浮動輸入框編輯，畫出的內容仍由共用繪製器顯示。尚未完成這些互動的實機操作驗收。Windows CI 已完成建置、啟動與封包安全掃描，並上傳可供試印的測試 Artifact。既有 `cmd/`、`internal/`、`go.mod`、`build.ps1` 和 `使用說明.txt` 暫作 Go 版行為對照；完成 C# 版驗收後再處理舊碼。新版資料庫從空白建立，不遷移 Go 測試資料。執行資料不進 Git。

## 排版原則

`src/CYEnvelope/EnvelopeRenderer.cs` 以毫米保存信封及文字位置。預覽與未來列印共用同一繪製器；信封原有紅色線條只顯示於預覽，套印只輸出黑字、勾記和黑色直排方框文字。目前預覽底圖是依舊版資料建立的初步示意，尚未通過實物照片逐項比對。15K 實際套印位置仍須使用目標印表機試印，不以編譯成功視為驗收。

## 開發驗證

需要 .NET 10 SDK。Windows 可直接執行：

```powershell
dotnet build .\src\CYEnvelope\CYEnvelope.csproj
dotnet run --project .\tests\CYEnvelope.Tests\CYEnvelope.Tests.csproj
```

非 Windows 環境可用 `dotnet build -p:EnableWindowsTargeting=true` 檢查編譯，但無法執行 WPF 測試或實際列印。前一版 V0.2.1 的 Windows CI 已通過；Build 1 的整理仍須重新驗證。成功後可從 PR 的 Actions 執行下載 `CYEnvelope-V0.2.1-Build-1-windows-x64-test` Artifact，解壓縮一次會得到 `CYEnvelope` 資料夾；開啟根目錄的 `CYEnvelope.exe`，保留 `Runtime` 子資料夾。根目錄的小型原生啟動程式會啟動 `Runtime/CYEnvelope.exe`；後者自包含 .NET 執行環境，無需另行安裝。Artifact 預設保留 14 天。測試包不是正式 Release，CI 成功也不代表實際印表機位置已校正。

## 來源與規範

共通規則依根目錄 `REPOSITORY_RULES.md`、`REPO_POLICY.md` 和本專案 `PROJECT_RULES.md`。介面依 AITeam 主線的 CY Desktop Visual Guide。`assets/ENV.ico`、`assets/ENV.svg` 取自 AITeam 核准的 `shared/cy-visual/icon-family/apps/envelope/`，其中 ICO SHA-256 為 `9d6f1534cb6a1e81efe96f6468db8b885b0076e10e94b85929414d86c90c6256`。

Windows 自包含測試資料夾的可重建指令為 `./build-csharp.ps1`；腳本將 WPF 程式與必要原生 DLL 一起放在 `dist/stage/CYEnvelope/Runtime/`，再製作根目錄的原生啟動 EXE，執行共用安全掃描。CI 直接上傳 `CYEnvelope` 資料夾，驗證根目錄無 DLL，下載後只需一次解壓縮；掃描失敗時不上傳封包。
