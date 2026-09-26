# CYEnvelope

志遠專用 Windows 信封套印工具。正式基準仍是 Go 版 V0.1.1；開發分支 `cyenvelope/csharp-remake` 正分階段重做 C#／WPF，目標版本 V0.2.0。本分支的 VERSION 是目標版本，不代表已提供可使用的新版 EXE。

## 重做進度

| 階段 | 狀態 | 內容 |
| --- | --- | --- |
| 1. 核心與資料 | 已提交、跨平台編譯 | 全新 SQLite 資料層；聯絡人多地址、多電話模型；368 筆離線三碼郵遞區號；電話格式；共用信封繪製器；核准 ENV 圖示 |
| 2. 介面與列印 | 初版已編譯，等待 Windows CI 與操作檢查 | 左右工作區、預覽點選輸入、聯絡人管理、格式編輯與橫式欄位旋轉；按列印先保存，再用共用繪製器輸出 |
| 3. Windows 測試包與試印 | 待驗證 | Windows x64 portable、CI Artifact、圖示與啟動檢查、15K 實機試印及位置校正 |

目前有 WPF 主畫面與管理視窗；預覽點選欄位後會以浮動輸入框編輯，畫出的內容仍由共用繪製器顯示。尚未完成這些互動的實機操作驗收，也沒有可供使用者試印的測試包。既有 `cmd/`、`internal/`、`go.mod`、`build.ps1` 和 `使用說明.txt` 暫作 Go 版行為對照；完成 C# 版驗收後再處理舊碼。新版資料庫從空白建立，不遷移 Go 測試資料。執行資料不進 Git。

## 排版原則

`src/CYEnvelope/EnvelopeRenderer.cs` 以毫米保存信封及文字位置。預覽與未來列印共用同一繪製器；信封原有紅色線條只顯示於預覽，套印只輸出黑字、勾記和黑色直排方框文字。目前預覽底圖是依舊版資料建立的初步示意，尚未通過實物照片逐項比對。15K 實際套印位置仍須使用目標印表機試印，不以編譯成功視為驗收。

## 開發驗證

需要 .NET 10 SDK。Windows 可直接執行：

```powershell
dotnet build .\src\CYEnvelope\CYEnvelope.csproj
dotnet run --project .\tests\CYEnvelope.Tests\CYEnvelope.Tests.csproj
```

非 Windows 環境可用 `dotnet build -p:EnableWindowsTargeting=true` 檢查編譯，但無法執行 WPF 測試或實際列印。測試程式需在 Windows 執行後，才能宣稱郵遞區號、SQLite 保存與繪製執行時驗證通過。PR 的 Windows CI 會檢查編譯、核心檢查與啟動；通過前不得宣稱新版已可使用。多檔自包含封裝的安全掃描器修正位於獨立治理 PR，合併與封裝驗證前不提供公開測試包。

## 來源與規範

共通規則依根目錄 `REPOSITORY_RULES.md`、`REPO_POLICY.md` 和本專案 `PROJECT_RULES.md`。介面依 AITeam 主線的 CY Desktop Visual Guide。`assets/ENV.ico`、`assets/ENV.svg` 取自 AITeam 核准的 `shared/cy-visual/icon-family/apps/envelope/`，其中 ICO SHA-256 為 `9d6f1534cb6a1e81efe96f6468db8b885b0076e10e94b85929414d86c90c6256`。

Windows 多檔自包含測試包的可重建指令為 `./build-csharp.ps1`；腳本會封裝 x64 portable ZIP、執行共用安全掃描並產生 SHA-256。目前共用掃描器的正式基準尚未更新，腳本預設會正確停止上傳流程。腳本不提供跳過安全掃描的發布選項。
