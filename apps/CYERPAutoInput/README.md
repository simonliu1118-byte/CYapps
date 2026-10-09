# CYERPAutoInput

SMART ERP 自動輸入工具，以鼎新 SMART ERP `COPI08` 銷貨單建立作業為主要自動化目標。

## 版本

目前開發版本為 `V0.2.0`（Build 編號見 `BUILD`），在 PR #104 工程測試中，尚未正式 Release；`main` 仍是舊 Go 線。逐版變更見 `CHANGELOG.md`，目前狀態與待辦見 `WORK_HANDOFF.md`。

## 主要流程

```text
找到 COPI08（只允許一個）→ 最大化並帶到前景
-> 檢視狀態送 F5 新增；以交易資料「部門代號／業務人員」皆空白確認是新單
-> 輸入表頭 / 交易資料 / 送貨資料 / 發票資料（課稅別、發票聯數以下拉清單位置選取並讀回確認）
-> 點一次商品明細區建立第一列，以 PP-OCRv5 辨識明細欄位位置
-> 每列：品號 -> 單位（有指定才 F2）-> 數量 -> 贈/備品量 -> 庫別 -> 單價 -> 批號
         批號：按 F2，ERP 開出批號查詢才選由上往下第一筆「現有存量 > 0」；沒開出即視為無批號
-> 下鍵進入下一列；ERP「庫存量或批號量不足」提示按確定並記為待確認
-> 自動儲存（設定開啟時）：送 F12，確認回到檢視且單號相同；完成後列出待確認項目
```

## 介面

- 只送出有填內容的欄位；有資料的明細列必須同時有「品號＋數量」。
- 銷貨單別最多 4 字元；日期畫面顯示 `YYYY/MM/DD`，送 ERP 前正規化為 `YYYYMMDD` 並驗證為真實日期。
- 課稅別、發票聯數為下拉選單，選項依 ERP 清單順序內建（標準分類，Governance 2.3.32 核准寫入 source）。
- 商品明細標準畫面顯示約 10 列，可垂直捲動。
- 進階模式目前隱藏，固定使用標準模式（程式碼保留）。
- 蝦皮匯入：見下方「蝦皮訂單匯入」。MO店+、酷澎商城匯入按鈕先保留位置。
- 右上角大標籤每秒顯示 ERP 狀態：`檢視`／`新增`／`修改`、`新增/修改？`（CY 開啟時 ERP 已在輸入中、無法判斷）、`多個 COPI08`、`未開啟`、`無法判斷`。新增或修改的判斷依據：由檢視進入輸入的瞬間，部門代號與業務人員在新單必定空白、舊單必定有值。標籤只作提示，不阻止輸入。
- 狀態列提示 Windows 顯示比例：只以 100% 驗證，非 100% 時以警告色提示。
- 「清除表單」清空欄位與明細並重新帶入本機預設值；狀態列可開啟 LOG 資料夾；完成後若有待確認項目或未自動儲存會列出原因。

### 設定（只存本機 `Data/settings.json`）

- 診斷模式：預設 LOG 只記錄 ERP 內容（品號、單號、OCR 文字）的長度；開啟後才記錄實際內容，供除錯。
- 自動儲存：預設關閉。只儲存 CY 自行新增的單據；開始時 ERP 已在輸入中且部門代號／業務人員已有值（可能是修改中的單據）則不儲存。有待確認項目的單據仍會儲存。
- 商品明細第一列庫別預設填入：勾選並填庫別後，表單第一列庫別直接預填（啟動、儲存設定、清除表單時；已有值不覆蓋）；ERP 新增列會沿用上一列庫別。
- 本機預設值：銷貨單別、部門代號等欄位的預設內容。
- 蝦皮匯入：銷貨單別、客戶代號、備註前綴（預設「蝦皮訂單」）。
- ERP 結構探測：唯讀輸出 COPI08 與開著的 ERP 視窗之 Win32／MSAA 控制項樹到 `logs/erp-probe_*.txt`；不送按鍵或點擊，節點／深度／時間皆有上限。

## 蝦皮訂單匯入

讀取蝦皮官方匯出的 `.xlsx`（第一個工作表，一列一張訂單；欄位 `tracking_number`、`order_sn`、`product_info`，選用 `remark_from_buyer`、`seller_note`）。

| COPI08 | 來源 |
|---|---|
| 銷貨單別、客戶代號 | 設定「蝦皮匯入」 |
| 交易資料・備註 | 備註前綴＋`order_sn`，例如「蝦皮訂單2610096XXXXXXX」 |
| 送貨資料・送貨地址(一) | `tracking_number` |
| 明細品號／數量／單價 | `product_info` 每個商品的「商品選項貨號」（沒有時用「商品貨號」）／「數量」／「價格」 |
| 其他欄位（單據日期、員工代號、貨運別…） | 表單目前的值與本機預設值 |

- 匯入前先預覽：可匯入的訂單、無法匯入的列與原因（缺品號、數量或價格無法辨識、訂單重複），有問題的訂單整張不匯入。
- 「載入選取的訂單到表單」：放到表單，由使用者確認後按「開始輸入 ERP」。
- 「全部依序輸入 ERP」：需開啟自動儲存；每張儲存後才接下一張。任何失敗或按 Esc 即停止整批，列出已儲存、停止原因與未處理的訂單（自動放棄失敗單據再繼續尚未實作）。
- 有買家備註或賣家備註的訂單：只輸入到單頭（表頭與交易／送貨／發票資料）就停止，明細轉人工輸入並儲存；批次停在這張。
- 員工代號目前取表單／本機預設值；之後接 CYID 時改為登入者（TODO）。

## 光學定位 / OCR

ERP 的明細表格、F2 查詢表格與 ERP 自繪訊息框的文字不在 Win32／MSAA 控制項中（結構探測確認），只能截圖辨識；表頭欄位、銷貨單號等一般欄位直接以 `WM_GETTEXT` 讀取。

- 引擎：本機 PaddleOCR PP-OCRv5 mobile（`ch_PP-OCRv5_det_mobile`、`ch_PP-OCRv5_rec_mobile`、`ch_PP-LCNet_x0_25_textline_ori_cls_mobile`）+ ONNX Runtime CPU，不依賴 Windows OCR 語言包。
- 比對前先做常見繁簡等價正規化（`数→數`、`库→庫`…）；容易誤讀的訊息只比對穩定片段。
- 新增／儲存使用 ERP 快捷鍵 F5／F12，不點 Ribbon。頁籤沿用已驗證的 `TcxPageControl` 幾何位置。
- 商品明細：點一次 `TcxGridSite` 建立第一列後，以目前 Grid 幾何＋表頭 OCR 定位欄位；欄位不在可視範圍時以 ERP 原生左右鍵捲動後重新辨識，不使用固定螢幕座標。
- F2 單位：OCR 定位「換算單位」欄中的指定單位，點選後送 Enter，並確認查詢視窗關閉。
- F2 批號：等查詢畫面穩定且未被遮擋、讀到「批號／現有存量」表頭才判讀。「現有存量」逐格裁到字形後只跑辨識模型；ERP 的斜線 0 單獨出現時信心偏低，只有候選中沒有其他數字才接受。由上往下選第一筆可確認 `> 0` 的批號；在找到之前有任何一列無法確認就停止，不猜列。
- OCR 暫存 PNG 只存在 Windows Temp，辨識後立即刪除。
- 模型不提交到 repo。模型組（檔名、大小、SHA-256、Release tag、上游固定 revision）定義在 `tools/ocr-models.json`；`tools/fetch-ocr-models.ps1` 優先從本 repo 模型 Release 下載，失敗才用上游固定 revision，大小或 SHA-256 不符即中止。模型 Release 由 `CYERPAutoInput OCR Model Mirror` workflow 從 `main` 手動建立（不是產品版本），已存在的 tag 不覆寫。

## 正式 ICON

CYERPAutoInput 使用 AITeam CY App Icon Family 的正式 `Auto` 資產：

- Canonical repository：`simonliu1118-byte/AITeam`
- Canonical revision：`887633147ef363b5b412458f687354293159c131`
- Windows icon：`shared/cy-visual/icon-family/apps/erp-autoinput/Auto.ico`
- Auto.ico SHA-256：`b35e87231fcd3238a4e7d73a687225d282bd1d60fe9de937f23de59393cc8e11`
- Windows 執行時同時以 managed embedded resource 指派同一份 `Auto.ico`，並設定固定 AppUserModelID，避免 single-file 執行時工作列退回通用圖示。

## 安全設計

- 不對 SMART ERP 送出 `Ctrl+A`。
- 自動操作期間全域 `Esc` 只中止 CY 後續動作，不替使用者按 ERP「取消」。
- 不自動操作 ERP「修改」或「取消」（PROJECT_RULES §1）。
- 不自行輸入銷貨單號；ERP 在單別／日期完成後產生，CY 讀取並驗證 `YYYYMMDDXXX`。單據識別為「銷貨單別＋銷貨單號」，例如 `234-20260926001`。
- 同時開啟多個 COPI08 時不開始輸入。
- 任何未預期視窗、焦點或欄位狀態都停止目前單據，不猜座標繼續；唯一自動處理的 ERP 訊息是「庫存量或批號量不足」（按確定並記為待確認，依使用者 2026-10-09 指示）。
- 下拉欄位、F2 單位／批號選取後都會讀回或確認視窗關閉，不以「看起來點到了」當作成功。
- 真實 ERP 代碼、客戶／品號／訂單／發票資料、runtime log 不得提交 Public source（課稅別、發票聯數的標準選項為核准例外）。

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

`tests/CYERPAutoInput.Tests` 只測不依賴 Win32／WinForms／OCR 的純規則（日期、銷貨單號、庫存數字與低信心判讀、下拉選項比對、OCR 文字正規化、新增／修改判斷、探測遮罩），以連結原始檔方式編譯，可在任何作業系統執行。光學流程由 `--vision-self-test` 驗證（含合成的「現有存量」儲存格）；CI 另以 `--ui-snapshot` 輸出主畫面與設定畫面 PNG 作為版面檢查 Artifact。

## TODO

依使用者 2026-10-09 確定的五個階段（詳見 `WORK_HANDOFF.md` §6）：

1. 自動輸入核心穩定（2026-10-10 完成）。
2. 蝦皮訂單匯出檔 → 自動打單（進行中：匯入、預覽、單張載入、批次依序輸入已完成；待辦：批次中失敗單據自動放棄後接續下一張、防止重複打單）。
3. 串接 CYID（含員工代號改為登入者）。
4. 從 CYWEB 訂單工單系統撈單自動打單，完成後寄信通知。
5. 辨識查詢單據，完成地端無人值守。

其他待評估：跨批號自動拆列（例如銷售 100、批號庫存 67＋100 時拆成 67＋33；需先定義批號優先順序、效期與庫存不足規則）。
