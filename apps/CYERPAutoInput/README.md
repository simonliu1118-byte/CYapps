# CYERPAutoInput

SMART ERP 自動輸入工具，以鼎新 SMART ERP `COPI08` 銷貨單建立作業為主要自動化目標。

## V0.1.0

`V0.1.0` 起改以 **C# / .NET 8 / WinForms** 維護；原 Go 實作已自目前分支移除，歷史版本仍可由 Git 記錄追溯。

主要方向：

```text
找到 COPI08
-> 還原並帶到前景
-> 判斷 BROWSE / INPUT，必要時以光學辨識定位「新增」
-> 輸入表頭 / 交易 / 送貨 / 發票
-> 先點擊 ERP 商品明細區，讓 COPI08 建立第一列
-> 截取 ERP 明細 Grid 畫面並光學辨識欄位/列位置
-> 品號 -> 單位(F2 OCR 選列 + Enter) -> 數量 -> 其他明細
-> 完成後停在 ERP，不自動儲存
```

## UI

- 標準／進階模式。
- 上方欄位不使用「是否套用」勾選框：有填內容才送入 ERP。
- Enter / Tab 可往下一欄；Shift+Enter / Shift+Tab 可反向。
- 商品明細直接使用 WinForms `DataGridView` 輸入。
- 只要某明細列有任何資料，該列「品號＋數量」均為必填；不完整時會在開始 ERP 自動化前停止並警告。
- 蝦皮、MO店+、酷澎商城匯入入口固定保留，實際解析逐一串接。

## 光學定位

光學辨識只在需要定位的局部 ERP 畫面使用，不做持續影像監控：

- 商品明細：先以實體滑鼠點擊 `TcxGridSite` 的第一列位置，讓 COPI08 建立第一列；再截取 Grid，用 OCR 找表頭並以實際格線計算可見列位置，不依 ERP 視窗大小比例猜座標。
- F2 單位查詢：截取 `F2開窗查詢`，OCR 找指定單位的實際位置，點選後只送一次實體 Enter，並確認 F2 視窗已關閉。
- OCR 使用 Windows 內建 `Windows.Media.Ocr`；優先選用 `zh-TW` / `zh-Hant` 中文辨識器，若系統沒有可用中文 OCR 語言則退回使用者語言，辨識不到目標時直接停止，不做座標猜測。
- 小型 ERP/F2 截圖會在辨識前暫時放大，以提高小字 OCR 成功率；回傳座標會換算回原始畫面座標。
- OCR 暫存 PNG 僅存在 Windows Temp，辨識完成後立即刪除；不自動上傳或保存到 repository。

## 正式 ICON

CYERPAutoInput 使用 AITeam 的 CY App Icon Family 正式 `Auto` 資產，不在本專案自行重畫：

- Canonical repository：`simonliu1118-byte/AITeam`
- Canonical revision：`887633147ef363b5b412458f687354293159c131`
- Vector source：`shared/cy-visual/icon-family/apps/erp-autoinput/Auto.svg`
- Windows icon：`shared/cy-visual/icon-family/apps/erp-autoinput/Auto.ico`
- Auto.ico SHA-256：`b35e87231fcd3238a4e7d73a687225d282bd1d60fe9de937f23de59393cc8e11`

本 repository 保存該版 `Auto.svg` 作為來源追溯；Windows build 會以 `tools/fetch-canonical-icon.ps1` 從上述固定 revision 取得正式 `Auto.ico`、驗證 SHA-256 後嵌入 EXE。

## 安全設計

- 不對 SMART ERP 送出 `Ctrl+A`。
- 自動操作期間全域 `Esc` 可中止 CYERPAutoInput 後續動作。
- Esc 只停止 CY 自動化，不會替使用者按 ERP「取消」。
- 不自動操作 ERP「修改」或「取消」。
- 不自行輸入銷貨單號，交由 SMART ERP 產號。
- 目前仍不自動儲存 ERP 單據。
- ERP 下拉選項與公司實際代碼由使用者本機設定或現場讀取，不寫死於 Public source。
- `logs/`、`config/settings.json`、匯入資料與 runtime 資料不得提交 Git。

## Public repository 資料原則

Repository 只保存程式邏輯、空白設定範例、測試與維護文件。不得提交真實客戶、品號、倉別、部門、人員、訂單、發票、ERP 下拉選項、公司內部路徑、帳密或 runtime log。

## Build

需求：.NET 8 SDK；正式發行目標為 Windows x64 self-contained single-file GUI。

```powershell
./tools/fetch-canonical-icon.ps1
dotnet restore CYERPAutoInput.csproj -r win-x64
dotnet publish CYERPAutoInput.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:PublishTrimmed=false -o package
```

GitHub Actions 工程包會在 ZIP 內保留單一版本資料夾，例如 `CYERPAutoInput-v0.1.0-build5-windows-x64/`；解壓縮後不再把 EXE、README、設定範例與 checksum 散放在同一層。

正式 Windows 編譯與驗收基準以 GitHub Actions Windows runner 為準。
