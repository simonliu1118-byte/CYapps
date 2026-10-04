# CYEnvelope

志遠專用 Windows 信封套印工具。正式基準仍是 Go 版 V0.1.1；開發分支 `cyenvelope/csharp-remake` 正分階段重做 C#／WPF，目前測試版本 V0.2.1 Build 2。本分支尚未正式發行。

## 本次修整（Build 2）

- 主畫面收件人／郵遞區號並排，已存地址／電話選單與對應欄位整合；日常欄位及方框文字在預設視窗完整顯示。
- 主畫面與聯絡人、格式、方框文字、設定共用 `Theme.xaml`；一般按鈕使用 WPF 原生樣板，主動作用 Blue；焦點只改色、不改尺寸。管理視窗不顯示應用程式標題圖示。
- 格式設定與聯絡人儲存列固定在視窗底部；聯絡人表格隨視窗伸縮。直接輸入區支援 Button 的 Tab／Enter 操作，顯示可編輯範圍；浮動輸入框依預覽縮放補償，維持可讀字級。
- 移除將多尺寸 ICO 覆蓋為單一 BitmapImage 的程式；視窗保留 ICO decoder，封包驗證直接比對兩個最終 EXE 的七組原生 icon payload。
- 可攜封裝維持一層 `CYEnvelope` 資料夾；根目錄為 `CYEnvelope.exe`、`VERSION`、`BUILD`、`Runtime`。WPF 程式與六個原生 DLL 在 Runtime；首次啟動於根目錄建立 Data。沒有內層 ZIP 或獨立 SHA 檔。

## 信封素材與列印

`EnvelopeRenderer.cs` 以毫米保存位置，預覽與套印共用同一繪製器。紅色底圖不送往印表機；只印黑字、勾記與黑色直排方框。

Build 2 依使用者提供的 `903127F6-8FE6-48FB-AB63-89B7606DA592.png`（393×393 商品圖）重畫可辨識的 15K 結構：左上郵票框、右上六格郵遞區號、左側郵件表格與狹長中央紅框；移除舊版錯誤的紅色三角封口及底部三格。底圖不再跟著文字位置變動。郵遞區號仍使用離線三碼，填前三格。

**素材限制：** 商品標籤遮住下半部，細字不清楚；中央框的下緣為暫估，不宣稱已與實物一比一校準。收到完整掃描後才能精確核對底部與實際尺寸。新資料庫採修正後初始位置，完全未修改的舊內建格式會更新；任何已調整的格式保留原值。15K 實際套印仍須目標印表機試印。

既有 Go 程式暫作行為對照；不遷移 Go 測試資料。實際聯絡人資料不進 Git 或測試包。

## 開發驗證

需要 .NET 10 SDK。Windows 可直接執行：

```powershell
dotnet build .\src\CYEnvelope\CYEnvelope.csproj
dotnet run --project .\tests\CYEnvelope.Tests\CYEnvelope.Tests.csproj
```

非 Windows 可用 `-p:EnableWindowsTargeting=true` 做編譯檢查。Windows 視覺檢查另執行：

```powershell
dotnet run --project ./tests/CYEnvelope.VisualReview/CYEnvelope.VisualReview.csproj -- ./dist/visual-review ./dist/stage/CYEnvelope ./assets/ENV.ico
```

Build 2 的 Windows CI #34 已完成畫面與七尺寸圖示驗證；後續檢查也涵蓋直接輸入框的可讀尺寸與空白聯絡人的編輯初始化，結果及擷取圖以最新提交的 CI 為準。檢查會產生實際 WPF 主畫面（空白、已填、最小視窗、直接輸入）與四個管理視窗 PNG，驗證預設／最小視窗的表單可見性、焦點尺寸、圖示及原生 EXE 資源。素材皆為合成範例，與測試包分開上傳。CI 同時執行核心資料測試、安全掃描、下載結構與封裝啟動驗證。Windows 125%／150% 顯示縮放、實機互動與列印仍須另驗；不得將 96 DPI 圖像當成所有 DPI 的驗收。

從 [CYEnvelope Windows 檢查](https://github.com/simonliu1118-byte/CYapps/actions/workflows/cyenvelope-build.yml) 的 Build 2 執行下載 `CYEnvelope-V0.2.1-Build-2-windows-x64-test`。解壓縮一次後開啟 `CYEnvelope/CYEnvelope.exe`，保留 Runtime；不需另裝 .NET。Artifact 保留 14 天。本分支及 PR 保持開發測試狀態，不是正式 Release。

## 來源與規範

共通規則依根目錄 `REPOSITORY_RULES.md`、`REPO_POLICY.md` 和本專案 `PROJECT_RULES.md`。介面依 AITeam 主線的 CY Desktop Visual Guide。`assets/ENV.ico`、`assets/ENV.svg` 取自 AITeam 核准的 `shared/cy-visual/icon-family/apps/envelope/`，其中 ICO SHA-256 為 `9d6f1534cb6a1e81efe96f6468db8b885b0076e10e94b85929414d86c90c6256`。

Windows 自包含測試資料夾的可重建指令為 `./build-csharp.ps1`；腳本將 WPF 程式與必要原生 DLL 一起放在 `dist/stage/CYEnvelope/Runtime/`，再製作根目錄的原生啟動 EXE，執行共用安全掃描。CI 直接上傳 `CYEnvelope` 資料夾，驗證根目錄無 DLL，下載後只需一次解壓縮；掃描失敗時不上傳封包。
