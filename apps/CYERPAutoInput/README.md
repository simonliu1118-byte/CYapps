# CYERPAutoInput

SMART ERP 自動輸入工具，以鼎新 SMART ERP `COPI08` 銷貨單建立作業為主要自動化目標。

## V0.1.0

`V0.1.0` 起改以 **C# / .NET 8 / WinForms** 維護；原 Go 實作可由 Git 歷史追溯。

主要流程：

```text
找到 COPI08
-> 還原並帶到前景
-> 判斷 BROWSE / INPUT，必要時定位「新增」
-> 輸入表頭 / 交易 / 送貨 / 發票
-> 啟用 ERP 商品明細第一列
-> 以即時 TcxGridSite 格線 + PP-OCRv5 辨識明細幾何
-> 品號 -> 單位(F2) -> 數量 -> 其他明細
-> 完成後停在 ERP，不自動儲存
```

## UI

- 標準／進階模式；進階模式最大化。
- 有填內容的欄位才送入 ERP。
- Enter / Tab 往下一欄；Shift+Enter / Shift+Tab 反向。
- 商品明細使用 WinForms `DataGridView`，標準畫面顯示約 10 列並使用垂直捲軸。
- 有資料的明細列必須同時有「品號＋數量」。
- 蝦皮、MO店+、酷澎商城匯入入口固定保留。

## 光學定位 / OCR

Build 11 起正式辨識引擎改為本機 **PaddleOCR PP-OCRv5 + ONNX Runtime (CPU)**，不再依賴 `Windows.Media.Ocr` 或 Windows 中文 OCR 語言包。

- Recognition：`ch_PP-OCRv5_rec_mobile`；使用與 RapidOCRSharpOnnx 已驗證字典完全匹配的 PP-OCRv5 模型。
- Detection：`ch_PP-OCRv5_det_mobile`。
- Text-line orientation：`ch_PP-LCNet_x0_25_textline_ori_cls_mobile`。
- 商品明細：實際點擊 `TcxGridSite` 建立第一列，座標由目前 Grid 的格線與欄位語意推導；OCR 用於辨識欄位文字，不使用固定螢幕座標。
- F2 單位：先定位 `TcxGridSite` 與「換算單位」欄；必要時逐格裁切後用 PP-OCRv5 辨識「支／箱」等短字，再點選該格並送一次 Enter。
- OCR 暫存 PNG 僅存在 Windows Temp，辨識後立即刪除。
- 模型不提交到 Public repository；Windows build 由固定 revision 下載後包進 `runtime/ocr/`。

## 正式 ICON

CYERPAutoInput 使用 AITeam CY App Icon Family 的正式 `Auto` 資產：

- Canonical repository：`simonliu1118-byte/AITeam`
- Canonical revision：`887633147ef363b5b412458f687354293159c131`
- Windows icon：`shared/cy-visual/icon-family/apps/erp-autoinput/Auto.ico`
- Auto.ico SHA-256：`b35e87231fcd3238a4e7d73a687225d282bd1d60fe9de937f23de59393cc8e11`

## 安全設計

- 不對 SMART ERP 送出 `Ctrl+A`。
- 自動操作期間全域 `Esc` 可中止後續 CY 動作，但不會替使用者按 ERP「取消」。
- 不自動操作 ERP「修改」或「取消」。
- 不自行輸入銷貨單號。
- 目前不自動儲存 ERP 單據。
- 真實 ERP 代碼、客戶／品號／訂單／發票資料、runtime log 不得提交 Public source。

## 本機資料與下載包

工程測試包單次解壓縮後只有一個頂層資料夾：

```text
CYERPAutoInput/
  CYERPAutoInput.exe
  BUILD
  VERSION
  README.md
  THIRD_PARTY_NOTICES.md
  Data/
    settings.example.json
    settings.json          # 執行後依需要產生
    Logs/                  # 執行後產生
  runtime/
    ocr/
      ch_PP-OCRv5_det_mobile.onnx
      ch_PP-OCRv5_rec_mobile.onnx
      ch_PP-LCNet_x0_25_textline_ori_cls_mobile.onnx
      SHA256.txt
```

## Build

需求：.NET 8 SDK；正式目標為 Windows x64 self-contained GUI。

```powershell
./tools/fetch-canonical-icon.ps1
./tools/fetch-ocr-models.ps1
dotnet restore CYERPAutoInput.csproj -r win-x64
dotnet publish CYERPAutoInput.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:PublishTrimmed=false -o package/CYERPAutoInput
```

正式 Windows 編譯與驗收基準以 GitHub Actions Windows runner 為準。
