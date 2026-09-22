# CYSmartERP

SMART ERP 自動打單工具，目前以鼎新 SMART ERP `COPI08` 銷貨單建立作業為主要自動化目標。

## 目前階段

目前版本為 `V0.0.10` 原型，仍在驗證 ERP 控制項、日期欄、DevExpress 下拉選單與明細 Grid 的可靠操作方式。

既定流程為：

```text
找到 COPI08
-> 還原並帶到前景
-> 確認可輸入狀態，必要時按「新增」
-> 輸入表頭 / 交易 / 送貨 / 發票 / 明細
-> 儲存（目前原型尚未啟用自動儲存）
```

## 安全設計

- 不對 SMART ERP 送出 `Ctrl+A`。
- `Esc` 可中止 CYSmartERP 後續自動操作。
- 不自動操作 ERP「修改」或「取消」。
- 不自行輸入銷貨單號，交由 SMART ERP 產號。
- ERP 下拉選項與公司實際代碼由使用者本機設定或現場讀取，不寫死於 Public source。
- `logs/`、`config/settings.json`、匯入資料與其他 runtime 資料不得提交 Git。

## Public repository 資料原則

Repository 只保存程式邏輯、空白設定範例、測試與維護文件。不得提交真實客戶、品號、倉別、部門、人員、訂單、發票、ERP 下拉選項、公司內部路徑、帳密或 runtime log。

## Build

需求：Go 1.23+

```powershell
$env:GOOS = "windows"
$env:GOARCH = "amd64"
go build -trimpath -ldflags "-H=windowsgui" -o CYSmartERP.exe .
```

正式 Windows 驗收以 GitHub Actions 的 Windows runner 為準。
