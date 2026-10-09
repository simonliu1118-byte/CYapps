# CYERPAutoInput

SMART ERP 自動輸入工具，以鼎新 SMART ERP `COPI08` 銷貨單建立作業為主要自動化目標。

## 版本

- `V0.1.0`：改以 **C# / .NET 8 / WinForms** 重寫（Codex）；原 Go 實作可由 Git 歷史追溯。
- `V0.2.0`：Claude Code 接手後的版本（2026-10-06 使用者指定升 Minor）——ERP 單據狀態標籤、多視窗防護、診斷模式、選用自動儲存、依 CY 視覺準則整理介面、單元測試與介面截圖 CI。

主要流程：

```text
找到 COPI08
-> 還原並帶到前景
-> 判斷 BROWSE / INPUT，檢視狀態時送 F5 新增
-> 輸入表頭 / 交易 / 送貨 / 發票
-> 只點一次商品明細區，建立第一列
-> 以目前 TcxGridSite + PP-OCRv5 辨識明細幾何
-> 品號 -> 單位(F2，有指定才做) -> 數量 -> 贈/備品量 -> 庫別 -> 單價 -> 批號（有標記才 F2 選第一筆正庫存）；每格以 Enter 完成
-> 下一筆以 ERP 原生列移動進入下一列
-> 自動儲存（設定開啟時）：只限 CY 新增的單據（有需人工確認項目也儲存，完成後列出）；送 F12 儲存後確認 ERP 回到檢視並讀回同一單號
```

## UI

- 標準／進階模式；進階模式最大化。
- 有填內容的欄位才送入 ERP。
- 銷貨單別最多 4 字元；日期欄畫面顯示 `YYYY/MM/DD`，內部與送 ERP 一律正規化為 `YYYYMMDD`，並先驗證真實日曆日期。
- 上半部欄位依 SMART ERP 原生 Tab / blur 規則離開欄位。
- 商品明細每一格以 Enter 進入／完成 ERP grid 編輯。
- 商品明細使用 WinForms `DataGridView`，標準畫面顯示約 10 列並使用垂直捲軸。
- 有資料的明細列必須同時有「品號＋數量」。
- 蝦皮、MO店+、酷澎商城匯入入口固定保留。
- 右上角大標籤每秒顯示 ERP 單據狀態：`檢視`／`新增`／`修改`；另有 `新增/修改？`（CY 開啟時 ERP 已在輸入中且部門代號／業務人員已有值，無法確認）、`多個 COPI08`、`未開啟`、`無法判斷`。新增與修改的區分依據：由檢視進入輸入狀態的瞬間，交易資料的部門代號與業務人員在新單必定空白、舊單必定有值（ERP 按新增即帶出日期與單號，不能用單號判斷）。標籤只作提示，不會阻止開始輸入。
- 底部狀態列提示 Windows 顯示比例；光學定位只以 100% 驗證，非 100% 時以警告色提示改回 100%。
- 工具列「清除表單」清空欄位與明細並重新帶入本機預設值；狀態列可直接開啟 LOG 資料夾。完成後若有需人工確認項目或未自動儲存，會列出明細。
- 課稅別、發票聯數為下拉選單（標準選項內建，依 ERP 清單順序）；ERP 端以下拉清單位置選取並讀回確認。進階模式目前隱藏。
- 設定「商品明細第一列庫別預設填入」：勾選並填庫別代號後，表單商品明細第一列的庫別直接預填該值（啟動、儲存設定、清除表單時；已有值不覆蓋），並像其他欄位一樣由 CY 輸入 ERP 庫別格，取代 ERP 自動帶的預設庫別。
- 設定視窗「ERP 結構探測」：唯讀讀取 COPI08 與目前開著的 ERP 視窗（含 F2 查詢）的控制項樹與無障礙（MSAA）樹，輸出到本機 `logs/erp-probe_*.txt`，用來評估以控制項層級取代座標／OCR。不送出按鍵、點擊或焦點變更，節點／深度／時間皆有上限。
- 設定可開啟「診斷模式」：預設 LOG 只記錄實際 ERP 內容（品號、銷貨單號、OCR 讀到的 ERP 文字）的長度；開啟後才寫入實際內容，供現場除錯，LOG 仍只存在本機。

## 光學定位 / OCR

辨識引擎為本機 **PaddleOCR PP-OCRv5 + ONNX Runtime (CPU)**，不依賴 `Windows.Media.Ocr` 或 Windows 中文 OCR 語言包。

- Recognition：`ch_PP-OCRv5_rec_mobile`；使用與 RapidOCRSharpOnnx 已驗證字典完全匹配的 PP-OCRv5 模型。
- Detection：`ch_PP-OCRv5_det_mobile`。
- Text-line orientation：`ch_PP-LCNet_x0_25_textline_ori_cls_mobile`。
- OCR 文字比對會先做常見繁簡等價正規化，例如 `数→數`、`库→庫`、`别→別`、`换→換`、`单→單`；原始 OCR 與 normalized 結果都會寫入診斷 LOG。
- 「新增」「儲存」使用 ERP 快捷鍵 F5／F12，不點 Ribbon（Ribbon「儲存」群組內同時有「取消」）。
- ERP 頁籤沿用已驗證的 `TcxPageControl` 幾何點擊，不以 OCR 決定實際操作流程。
- 商品明細：完成所有上半部欄位後，只點一次 `TcxGridSite` 建立第一列，再辨識目前 Grid；OCR 用於辨識欄位文字，座標以目前 Grid 幾何為準。
- 非最大化時，如果需要的明細欄位在水平 viewport 外，程式使用 ERP grid 原生左右移動讓欄位進入可視範圍，再重新辨識目前 Grid，不使用固定螢幕座標。
- F2 單位：先定位 `TcxGridSite` 與「換算單位」欄；必要時逐格裁切後用 PP-OCRv5 辨識「支／箱」等短字，再點選該格並送一次 Enter。
- F2 批號：完成品號／單位／數量／庫別等明細後，先檢查該列批號 cell；明確空白不送 F2，明確有批號標記才開啟，視覺不確定時才以 F2 行為作 fallback。是否需要批號以 ERP 的 F2 反應為準：2 秒內沒有批號查詢視窗即視為無批號。查詢內逐列辨識「現有存量」（單格依字色正規化、裁到只剩字形後直接跑辨識模型、略過文字偵測；ERP 的斜線 0 單獨出現時信心偏低，只有在候選中沒有其他數字時才接受；讀不到再用一般 OCR），只選由上往下第一筆可確認 `> 0` 的批號，無法確認就停止，不猜列。
- OCR 暫存 PNG 僅存在 Windows Temp，辨識後立即刪除。
- 模型不提交到 Public repository。模型組（檔名、大小、SHA-256、Release tag、上游固定 revision）唯一定義在 `tools/ocr-models.json`。
- `tools/fetch-ocr-models.ps1` 優先從本 repo 的模型 Release 下載；Release 下載不到時才改用上游固定 revision。任何來源的檔案大小或 SHA-256 不符即中止 build。
- 模型 Release 由 `CYERPAutoInput OCR Model Mirror` workflow 從 `main` 手動建立（pre-release，不是產品版本），已存在的 tag 不覆寫；更換模型組時改 `tools/ocr-models.json` 的 tag 與雜湊後再執行一次。

## 正式 ICON

CYERPAutoInput 使用 AITeam CY App Icon Family 的正式 `Auto` 資產：

- Canonical repository：`simonliu1118-byte/AITeam`
- Canonical revision：`887633147ef363b5b412458f687354293159c131`
- Windows icon：`shared/cy-visual/icon-family/apps/erp-autoinput/Auto.ico`
- Auto.ico SHA-256：`b35e87231fcd3238a4e7d73a687225d282bd1d60fe9de937f23de59393cc8e11`
- Windows 執行時同時以 managed embedded resource 指派同一份 `Auto.ico`，並設定固定 AppUserModelID，避免 single-file 執行時工作列退回通用圖示。

## 安全設計

- 不對 SMART ERP 送出 `Ctrl+A`。
- 自動操作期間全域 `Esc` 可中止後續 CY 動作，但不會替使用者按 ERP「取消」。
- 不自動操作 ERP「修改」或「取消」。
- 不自行輸入銷貨單號；ERP 在銷貨單別／日期完成後自動產生，CY 以狀態輪詢讀取並驗證固定 `YYYYMMDDXXX`（11 位）格式。批次追蹤識別固定使用「銷貨單別 + 銷貨單號」，例如 `234-20260926001`。
- 任何未預期視窗／焦點／欄位狀態都必須停止目前單據，禁止猜座標繼續輸入。
- 同時開啟多個 COPI08 視窗時不開始輸入，避免輸入到錯誤的視窗。
- 自動儲存預設關閉，於設定開啟。只儲存 CY 自行新增（或開始時部門代號／業務人員皆空白）的單據；開始時兩欄已有值（可能是修改）時不儲存；有需人工確認項目（例如已按確定的庫存不足提示）仍儲存，完成後列出明細。送出 F12 後若出現任何 ERP 訊息視窗即停止，不代為處理。
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
dotnet test tests/CYERPAutoInput.Tests/CYERPAutoInput.Tests.csproj
dotnet restore CYERPAutoInput.csproj -r win-x64
dotnet publish CYERPAutoInput.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:PublishTrimmed=false
```

正式 Windows 編譯與驗收基準以 GitHub Actions Windows runner 為準。

`tests/CYERPAutoInput.Tests` 只測不依賴 Win32／WinForms／OCR 的純規則（日期、銷貨單號格式、庫存數字、OCR 文字正規化、新增／修改判斷），以連結原始檔方式編譯，可在任何作業系統執行；光學流程由 `--vision-self-test` 驗證；CI 另以 `--ui-snapshot` 輸出主畫面（標準／進階）與設定畫面 PNG 作為版面檢查 Artifact。

## TODO

- **跨批號自動分配 / 拆列**：例如銷售 100、批號庫存 67 + 100 時，自動拆成 67 + 33。實作前必須先定義批號優先順序、效期、總庫存不足與來源訂單對應規則。
- **批次 Fault Isolation / Recovery**：未來 XLS/XLSX 多單批次輸入時，單張發生 hard failure 要記錄來源訂單、ERP 銷貨單號、失敗階段與原因，安全跳過該張後繼續；若無法驗證 ERP 已回復安全起始狀態則停止整批。
- **批次結果總表**：中途可安全處理的 warning 不逐張跳 MessageBox；全部完成後統一列出成功、需人工確認、失敗的訂單與銷貨單號。
- **批次儲存與下一張**：單張自動儲存（選用）已完成；待單張輸入、批號與 recovery gate 實機穩定後，再加入批次中每張單儲存後接續下一張的流程。
