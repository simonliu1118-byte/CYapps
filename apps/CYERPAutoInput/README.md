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
-> 只點一次商品明細區，建立第一列
-> 以目前 TcxGridSite + PP-OCRv5 辨識明細幾何
-> 品號 -> ERP自動判斷批號(F2選第一筆正庫存) -> 單位(F2) -> 數量 -> 其他明細；每格以 Enter 完成
-> 下一筆以 ERP 原生列移動進入下一列
-> 完成後停在 ERP，不自動儲存
```

## UI

- 標準／進階模式；進階模式最大化。
- 有填內容的欄位才送入 ERP。
- 上半部欄位依 SMART ERP 原生 Tab / blur 規則離開欄位。
- 商品明細每一格以 Enter 進入／完成 ERP grid 編輯。
- 商品明細使用 WinForms `DataGridView`，標準畫面顯示約 10 列並使用垂直捲軸。
- 有資料的明細列必須同時有「品號＋數量」。
- 蝦皮、MO店+、酷澎商城匯入入口固定保留。

## 光學定位 / OCR

Build 11 起正式辨識引擎改為本機 **PaddleOCR PP-OCRv5 + ONNX Runtime (CPU)**，不再依賴 `Windows.Media.Ocr` 或 Windows 中文 OCR 語言包。

- Recognition：`ch_PP-OCRv5_rec_mobile`；使用與 RapidOCRSharpOnnx 已驗證字典完全匹配的 PP-OCRv5 模型。
- Detection：`ch_PP-OCRv5_det_mobile`。
- Text-line orientation：`ch_PP-LCNet_x0_25_textline_ori_cls_mobile`。
- OCR 文字比對會先做常見繁簡等價正規化，例如 `数→數`、`库→庫`、`别→別`、`换→換`、`单→單`；原始 OCR 與 normalized 結果都會寫入診斷 LOG。
- 「新增」優先使用 Win32 caption / Ribbon 綠色＋號；只有前兩者都失敗才對 bounded Ribbon 跑 PaddleOCR，不再先掃整個 ERP 視窗。
- ERP 頁籤沿用已驗證的 `TcxPageControl` 幾何點擊，不以 OCR 決定實際操作流程。
- 商品明細：完成所有上半部欄位後，只點一次 `TcxGridSite` 建立第一列，再辨識目前 Grid；OCR 用於辨識欄位文字，座標以目前 Grid 幾何為準。
- 非最大化時，如果需要的明細欄位在水平 viewport 外，程式使用 ERP grid 原生左右移動讓欄位進入可視範圍，再重新辨識目前 Grid，不使用固定螢幕座標。
- F2 單位：先定位 `TcxGridSite` 與「換算單位」欄；必要時逐格裁切後用 PP-OCRv5 辨識「支／箱」等短字，再點選該格並送一次 Enter。
- F2 批號：品號輸入後檢查批號欄；若 ERP 顯示 `************`，開啟批號 F2，辨識「現有存量」欄並選取由上往下第一筆 `> 0` 的批號。批號數量不足警告屬可記錄警告，確認後以 Enter 通過。
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
- 不自行輸入銷貨單號；ERP 在銷貨單別／日期完成後自動產生，CY 只精確讀取以供批次追蹤。
- 任何未預期視窗／焦點／欄位狀態都必須停止目前單據，禁止猜座標繼續輸入。
- 目前不自動儲存 ERP 單據。
- 真實 ERP 代碼、客戶／品號／訂單／發票資料、runtime log 不得提交 Public source。

## 本機資料與下載包

工程測試包單次解壓縮後只有一個頂層資料夾。根目錄只保留日常使用需要看到的檔案；PDB、LIB、DLL 與 SHA256 不放進使用者測試包。

```text
CYERPAutoInput/
  CYERPAutoInput.exe
  BUILD
  VERSION
  logs/                    # 第一次執行後產生
  Data/
    settings.example.json
    settings.json          # 執行後依需要產生
  README.md
  THIRD_PARTY_NOTICES.md
  runtime/
    ocr/
      ch_PP-OCRv5_det_mobile.onnx
      ch_PP-OCRv5_rec_mobile.onnx
      ch_PP-LCNet_x0_25_textline_ori_cls_mobile.onnx
```

## Build

需求：.NET 8 SDK；正式目標為 Windows x64 self-contained GUI。

```powershell
./tools/fetch-canonical-icon.ps1
./tools/fetch-ocr-models.ps1
dotnet restore CYERPAutoInput.csproj -r win-x64
dotnet publish CYERPAutoInput.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:PublishTrimmed=false
```

正式 Windows 編譯與驗收基準以 GitHub Actions Windows runner 為準。

## TODO

- **跨批號自動分配 / 拆列**：例如銷售 100、批號庫存 67 + 100 時，自動拆成 67 + 33。實作前必須先定義批號優先順序、效期、總庫存不足與來源訂單對應規則。
- **批次 Fault Isolation / Recovery**：未來 XLS/XLSX 多單批次輸入時，單張發生 hard failure 要記錄來源訂單、ERP 銷貨單號、失敗階段與原因，安全跳過該張後繼續；若無法驗證 ERP 已回復安全起始狀態則停止整批。
- **批次結果總表**：中途可安全處理的 warning 不逐張跳 MessageBox；全部完成後統一列出成功、需人工確認、失敗的訂單與銷貨單號。
- **自動儲存**：待單張輸入、批號與 recovery gate 穩定後，再加入每張單的 ERP 儲存與下一張流程。
