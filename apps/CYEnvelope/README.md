# CYEnvelope

志遠專用的 Windows 信封套印工具。程式、資料模型、測試、資源與封裝均位於本目錄；不引用 `CYInvoice` 的程式碼或資料。

## V0.1.1

- 15K 標準信封（105 × 222 mm），直式套印。
- 收件人即時篩選、同名多地址、每筆地址記住上次電話。
- 臺灣市話、手機、0800 與分機格式化。
- 離線三碼郵遞區號（中華郵政 368 筆地區表）。
- 6–7 個郵件種類勾記、方框文字與每格式預設值。
- 格式即時示意與毫米座標、字體、字級設定。
- Windows 原生印表機列舉、內容視窗及 GDI 套印。
- SQLite 本機資料庫；按下「列印」先保存，才送 Windows 列印。
- 修正中文輸入法搭配收件人即時清單時，游標被重設而使字序錯亂的問題。
- 依實際 15K 信封重畫主畫面預覽，並更新內建 15K 初始座標。
- 改用「印表機＋直式信封」多尺寸應用程式圖示。

目前預覽依提供的 15K 信封照片重畫；實際套印位置仍可在「格式設定」依印表機進紙差異精校。

## 開發

需求：Go 1.22 或更新版。

```powershell
./build.ps1
```

或：

```powershell
$env:GOOS='windows'
$env:GOARCH='amd64'
$env:CGO_ENABLED='0'
go test ./...
go build -buildvcs=false -trimpath -ldflags '-H windowsgui -s -w' -o dist/CYEnvelope/CYEnvelope.exe ./cmd/CYEnvelope
```

## 資料位置

可攜版會在 EXE 同層使用：

- `Data/CYEnvelope.db`：聯絡人、格式、方框文字與設定
- `Logs/CYEnvelope.log`：執行紀錄
- `Cache/`：保留供後續快取功能

備份時關閉程式，再複製整個 `Data` 目錄即可。
