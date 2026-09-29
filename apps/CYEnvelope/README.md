# CYEnvelope

志遠專用 Windows 信封套印工具，以 C#／WPF（.NET 10）開發。目前測試版本 V0.2.1 Build 3，尚未正式發行。

Go 版（V0.1.x）已依使用者決定停止開發，程式碼自本分支移除；歷史保留在 Git 與 `V0.1.0.txt`、`V0.1.1.txt`。不遷移 Go 版測試資料。

操作方式見 `使用說明.txt`，各版變更見 `V0.2.0.txt`、`V0.2.1.txt`。

## 結構

- `src/CYEnvelope/`：WPF 程式。`EnvelopeRenderer.cs` 以毫米座標繪製，預覽與列印共用；`Postal.cs`＋`postal.tsv` 為離線三碼郵遞區號；`PhoneFormatting.cs` 為臺灣電話格式；`Repository.cs` 為 SQLite 保存。
- `src/CYEnvelope.Launcher/`：可攜資料夾根目錄的原生啟動程式，開啟 `Runtime/CYEnvelope.exe`。
- `tests/CYEnvelope.Tests/`：核心檢查（郵遞區號、電話、保存、繪製、溢出判斷）。
- `tests/CYEnvelope.VisualReview/`：Windows 上的實際視窗擷取、表單可見性、圖示與最終 EXE 資源驗證。
- `build-csharp.ps1`：重建可攜測試資料夾並執行公開封包安全掃描。

## 信封素材與列印

紅色底圖只在預覽顯示，不送往印表機；實際只印黑字、勾記與方框文字的黑框。列印前會向驅動程式要求信封尺寸並確認實際採用的紙張，扣除印表機可列印區原點，讓毫米座標以紙張邊緣為準。

15K 底圖依使用者提供的 393×393 商品照片重畫：左上郵票框、右上六格郵遞區號、左側郵件種類表與中央狹長紅框。照片下半部被標籤遮住，中央框下緣為暫估；收到完整掃描前不宣稱一比一。15K 實際套印位置仍須在目標印表機試印校正。

## 開發驗證

需要 .NET 10 SDK。Windows：

```powershell
dotnet build .\src\CYEnvelope\CYEnvelope.csproj
dotnet run --project .\tests\CYEnvelope.Tests\CYEnvelope.Tests.csproj
dotnet run --project .\tests\CYEnvelope.VisualReview\CYEnvelope.VisualReview.csproj -- .\dist\visual-review .\dist\stage\CYEnvelope .\assets\ENV.ico
```

非 Windows 可加 `-p:EnableWindowsTargeting=true` 做編譯檢查；核心檢查與畫面驗證需在 Windows 執行。

CI（`CYEnvelope WPF Check`）在 Windows 上建置、執行核心檢查與啟動檢查，以 `build-csharp.ps1` 建立並掃描可攜資料夾，產生畫面證據，並重新下載 Artifact 驗證一次解壓縮的結構與封裝後啟動。測試包名稱為 `CYEnvelope-V<版本>[-Build-N]-windows-x64-test`，解壓縮一次後開啟 `CYEnvelope/CYEnvelope.exe`，不需另裝 .NET。Artifact 保留 3 天。

Windows 125%／150% 顯示縮放、實機操作與 15K 試印仍須另行驗收；96 DPI 擷取圖不代表所有 DPI。

## 資料位置

可攜資料夾根目錄的 `Data/CYEnvelope.db` 保存聯絡人、格式、方框文字與設定。實際聯絡人資料不得進 Git 或測試包。

## 來源與規範

依根目錄 `REPOSITORY_RULES.md`、`REPO_POLICY.md` 與本專案 `PROJECT_RULES.md`；介面依 AITeam `main` 的 CY Desktop Visual Guide。`assets/ENV.ico`、`assets/ENV.svg` 取自 AITeam `shared/cy-visual/icon-family/apps/envelope/`：

- `ENV.ico` SHA-256 `9d6f1534cb6a1e81efe96f6468db8b885b0076e10e94b85929414d86c90c6256`
- `ENV.svg` SHA-256 `b82bdeeaf4ebde71cb1a125603dc0ee4100c35f66568c6cbb7081b8dc308cc47`
