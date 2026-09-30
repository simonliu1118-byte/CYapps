# CYEnvelope

志遠專用 Windows 信封套印工具，以 C#／WPF（.NET 10）開發。目前測試版本 V0.2.1 Build 9，尚未正式發行。

Go 版（V0.1.x）已依使用者決定停止開發，程式碼自本分支移除；歷史保留在 Git 與 `V0.1.0.txt`、`V0.1.1.txt`。不遷移 Go 版測試資料。

## 用途

每月印對帳單信封時，不必再從 ERP 一筆筆複製客戶名稱、地址、郵遞區號、電話到 Word 套版，也不必手動調整平信／掛號勾選而跑版。本程式是輕量的信封列印輔助工具，位置印得大致準確即可；最重要的是**列印資料庫**：

- 按下「列印」＝把這筆存進資料庫；下個月只要輸入客戶名稱的一部分（例如「高美」）就列出名稱含該字的客戶，選取後帶出上次的地址、電話與郵件種類。
- 貼上不含郵遞區號的地址，程式依離線資料判斷三碼郵遞區號（只使用三碼）；判斷不出來時區號欄加紅框並提示，由使用者自己填，填過的區號會隨地址存進資料庫。
- 列印送出後自動清空，方便輸入下一筆；「重印上一筆」把剛才列印的資料帶回（仍需自己按列印）。
- 一家客戶可有多組地址與電話。列印時若地址或電話是該客戶沒有的新資料，會跳出視窗問「新增為另一筆」或「覆蓋」，不會默默改資料。
- 郵件種類可在左側勾選，或直接點信封預覽左側的表格，兩種方式同步；「內附對帳單」等方框文字可維護清單，點預覽上的黑框即可選用。

操作方式見 `使用說明.txt`，各版變更見 `V0.2.0.txt`、`V0.2.1.txt`，待辦見 `TODO.md`。

## 結構

- `src/CYEnvelope/`：WPF 程式。`EnvelopeRenderer.cs` 以毫米座標繪製，預覽與列印共用；`Postal.cs`＋`postal.tsv` 為離線三碼郵遞區號；`PhoneFormatting.cs` 為臺灣電話格式；`Repository.cs` 為 SQLite 保存（客戶名稱搜尋、每日備份、損毀偵測）；`ContactSaver.cs` 決定列印時如何存入客戶。
- `src/CYEnvelope.Launcher/`：可攜資料夾根目錄的原生啟動程式，開啟 `Runtime/CYEnvelope.exe`。
- `tests/CYEnvelope.Tests/`：核心檢查（郵遞區號、電話、保存、繪製、溢出判斷）。
- `tests/CYEnvelope.VisualReview/`：Windows 上的實際視窗擷取、表單可見性、圖示與最終 EXE 資源驗證。
- `build-csharp.ps1`：重建可攜測試資料夾並執行公開封包安全掃描。

## 版面（15K 預設）

由左至右：收件人（置中於印好的中央框、位於上半部）、電話、地址。地址第一個字比收件人第一個字低 5 mm，電話與地址頂端同高，電話在收件人框與地址之間。中華郵政規定收件人姓名書於中央、地址書於右側（[國內郵件直式信封書寫方式](https://www.post.gov.tw/post/internet/Postal/index.jsp?ID=21001)），該頁未規定電話位置，電話位置為本專案依信封空間所定，可在「格式設定」調整。

## 信封素材與列印

紅色底圖只在預覽顯示，不送往印表機；實際只印黑字、勾記與方框文字的黑框。「校正列印」可在普通紙印出各欄位外框、範例文字與毫米刻度（含格式的印表機偏移），疊在信封上對光檢查位置。列印前會向驅動程式要求信封尺寸並確認實際採用的紙張，扣除印表機可列印區原點，讓毫米座標以紙張邊緣為準。

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

可攜資料夾根目錄的 `Data/CYEnvelope.db` 保存聯絡人、格式、方框文字與設定；`Data/Backups/` 每天（有資料時）自動保留一份備份，最多 14 份。實際聯絡人資料不得進 Git 或測試包。

## 來源與規範

依根目錄 `REPOSITORY_RULES.md`、`REPO_POLICY.md` 與本專案 `PROJECT_RULES.md`；介面依 AITeam `main` 的 CY Desktop Visual Guide。`assets/ENV.ico`、`assets/ENV.svg` 取自 AITeam `shared/cy-visual/icon-family/apps/envelope/`：

- `ENV.ico` SHA-256 `9d6f1534cb6a1e81efe96f6468db8b885b0076e10e94b85929414d86c90c6256`
- `ENV.svg` SHA-256 `b82bdeeaf4ebde71cb1a125603dc0ee4100c35f66568c6cbb7081b8dc308cc47`
