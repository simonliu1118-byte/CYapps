# CYInvoice ↔ CYERPAutoInput 呼叫介面交接

> 狀態：提案，CYInvoice 端尚未實作。2026-10-13 由 CYERPAutoInput 主控對話整理，交給 CYInvoice 主控對話實作接口。  
> 本文件是需求與介面設計，不是永久規則；CYInvoice 端仍以 `/REPOSITORY_RULES.md`、`/REPO_POLICY.md`、`apps/CYInvoice/PROJECT_RULES.md` 為準，修改走 CYInvoice 自己的 branch／PR。

## 1. 目的

使用者 2026-10-13 指定的最終目標：CYERPAutoInput（以下稱 AutoInput）的「MO店+ 全自動」「酷澎全自動」一次跑完下列 1–9 步，不需人工介入（酷澎等有訂單時再做，介面先預留）。

1. MO店+ 後台「未出貨」匯出報表 → 未出貨訂單檔（加密 `.xls`）。
2. 批次出貨 → 寄件單 PDF（各物流分開、加密）。
3. 「配送中」匯出報表 → 配送中全部訂單檔（含物流單號）。
4. **呼叫 CYInvoice 匯入未出貨訂單檔並開立發票。**
5. **依訂單編號取得發票號碼、發票日期等。**
6. AutoInput 載入兩份訂單檔，帶入物流單號與發票資料（已完成，V0.3.0）。
7. ERP 打單。
8. 列印寄件單 PDF。
9. 清除暫存檔。

本文件只描述第 4、5 步需要 CYInvoice 提供的接口。兩個程式在同一台電腦、同一個 Windows 帳號下執行。

## 2. 原則

- **發票只由 CYInvoice 開立。** AutoInput 不直接呼叫光貿、不持有光貿帳號金鑰、不自行判斷是否已開立；防重複開票、結果不明不盲目重送、回查光貿等既有規則全部沿用 CYInvoice 現有路徑（`InvoiceService.IssueMoAsync`／`IssueCoupangAsync` 與重新整理回查），不得另建第二套開立邏輯。
- **不需登入。** 使用者 2026-10-13 確認：CYInvoice 開立發票本來就不需要登入，接口同樣不需要驗證。
- **重跑安全。** 同一份訂單檔再呼叫一次，已開立的訂單只回傳原發票，不會重開；結果不明的訂單依 CYInvoice 既有回查流程處理，查不清楚就回報「不明」，AutoInput 不輸入 ERP、轉人工。
- **檔案解析由 CYInvoice 自己做。** AutoInput 傳檔案路徑，不傳解析後的品項或金額；CYInvoice 用自己的 `MoImporter`／`CoupangImporter` 與自己設定的 MO 密碼讀檔，開立內容與手動匯入完全一致。

## 3. 建議的呼叫方式

### 3.1 命令列

```
CYInvoice.exe --autoinput-issue  <request.json> --result <result.json>
CYInvoice.exe --autoinput-query  <request.json> --result <result.json>
```

- `--autoinput-issue`：匯入並開立（已開立的回傳原發票）。
- `--autoinput-query`：只查詢、不開立（AutoInput 可先用它做第一階段整合、也用於重跑時核對）。
- 結束碼：`0` 全部成功（含已開立）；`1` 有訂單失敗或結果不明（結果檔仍完整）；`2` 請求檔或訂單檔無法讀取；`3` CYInvoice 目前無法處理（例如正在重設、服務連線矩陣規定阻擋）。結束碼只是摘要，AutoInput 以結果檔為準。
- 不顯示主視窗；若需要顯示進度，可用小型不搶焦點的提示（AutoInput 執行期間會鎖定實體滑鼠並置中顯示「全自動運轉中」）。

### 3.2 已開啟的 CYInvoice（單一寫入者）

`Program.cs` 以 `Local\CYInvoice-<directoryKey>` Mutex 保證同一資料夾只有一個寫入者，第二個行程目前會跳「已經開啟」訊息後結束。接口需處理兩種情況：

- **CYInvoice 沒開**：命令列行程取得 Mutex，以無主視窗模式處理後結束。
- **CYInvoice 已開**：命令列行程不得自行寫資料庫；建議把請求轉交給已開啟的那個行程處理，例如具名管道 `\\.\pipe\CYInvoice-<directoryKey>-autoinput`，ACL 限同一使用者，再把結果寫回 `result.json` 後結束。也不得跳出需要人工按確定的訊息框。

使用者 2026-10-13 確認：已開啟時把請求轉交給已開著的那一個處理。管道或其他轉交機制的細節由 CYInvoice 主控決定；AutoInput 只依賴 3.1 的命令列與結果檔。

### 3.3 回傳方式與等待

AutoInput 的角度永遠只有一種流程，不論 CYInvoice 原本有沒有開：

1. 寫好 `request.json`，啟動 `CYInvoice.exe --autoinput-issue …`（命令列行程）。
2. 等這個行程結束（`WaitForExit`）。CYInvoice 已開啟時，命令列行程把請求轉交給已開著的那一個，並一直等到它處理完、把結果交回來，才寫 `result.json` 並結束。
3. 行程結束後讀 `result.json`，結束碼只當摘要。
4. 進度：命令列行程可在標準輸出逐行寫 `progress <已處理>/<總數> <訂單編號>`，AutoInput 顯示在畫面中央的提示上；沒有也可以。
5. 逾時：AutoInput 依訂單數給上限（例如每張 60 秒＋60 秒）。逾時只停止等待並轉人工，**不重複呼叫開立**；之後可用 `--autoinput-query` 查回實際結果。

### 3.4 CYInvoice 位置

AutoInput 在自己的設定裡讓使用者指定 `CYInvoice.exe` 路徑（portable，各電腦不同）；找不到就停止並說明，不猜路徑。

## 4. 請求檔 `request.json`

```json
{
  "schema": "cyinvoice-autoinput-request/1",
  "requestId": "0b6f4f4e-6c3e-4a51-9f0e-2f3c5d1a7e10",
  "source": "mo",
  "file": "C:\\...\\AutoInput\\Temp\\20261013-163538\\export-before.xls",
  "orderIds": ["90000000000001", "90000000000002"]
}
```

- `source`：`"mo"` 或 `"coupang"`（之後再加）。
- `file`：未出貨訂單檔的完整路徑（AutoInput 的暫存資料夾）。
- `orderIds`：選填。給了就只處理這些訂單編號（AutoInput 可排除不輸入的訂單）；省略表示檔案內全部。
- `requestId`：AutoInput 產生的 GUID，CYInvoice 寫進結果檔與 LOG 方便對帳。

## 5. 結果檔 `result.json`

```json
{
  "schema": "cyinvoice-autoinput-result/1",
  "requestId": "0b6f4f4e-6c3e-4a51-9f0e-2f3c5d1a7e10",
  "cyinvoiceVersion": "2.6.19+0",
  "environment": "production",
  "finishedAt": "2026-10-13T16:40:12+08:00",
  "orders": [
    {
      "orderId": "90000000000001",
      "status": "issued",
      "invoiceNumber": "AB12345678",
      "invoiceDate": "2026/10/13",
      "buyerBan": "",
      "buyerName": "",
      "totalAmount": 725,
      "message": ""
    },
    {
      "orderId": "90000000000002",
      "status": "unknown",
      "invoiceNumber": "",
      "invoiceDate": "",
      "buyerBan": "12345678",
      "buyerName": "測試股份有限公司",
      "totalAmount": 913,
      "message": "光貿回應逾時，已排入回查；請勿重送"
    }
  ]
}
```

- `status`：
  - `issued`：本次開立成功。
  - `existing`：之前已開立，回傳原發票（不重開）。
  - `failed`：明確失敗（光貿拒絕、資料不符等），沒有發票。
  - `unknown`：結果不明，依 CYInvoice 既有流程回查中；AutoInput 視同未開立、轉人工，不得重送。
  - `skipped`：檔案中沒有這張訂單，或依 CYInvoice 規則不開立（`message` 說明）。
  - `query` 模式只會回 `existing`、`unknown`、`skipped`（查無）。
- `invoiceDate`：`yyyy/MM/dd`（ERP 發票日期欄格式）。
- `buyerBan`／`buyerName`：實際開立用的統編與買方名稱（CYInvoice 查名後的結果），AutoInput 直接填入 ERP 發票資料，不再自行查名。
- `totalAmount`：發票總額。AutoInput 會與自己算出的「開立發票金額加總」核對，不一致即轉人工。
- `environment`：`production`／`test`。AutoInput 在正式 ERP 只接受 `production`。
- 結果檔寫完才關閉（先寫暫存檔再改名），AutoInput 讀到的一定是完整檔案。

## 6. AutoInput 端的使用方式（之後在 CYERPAutoInput 實作）

1. 第 1–3 步的下載檔都放在 AutoInput 自己建立的暫存資料夾（例如 `<程式資料夾>\Temp\<日期時間>\`），全部成功後連資料夾一起刪除；有任何訂單要人工處理時保留。
2. 第 4 步呼叫 `--autoinput-issue`，等待結束（逾時即停止、轉人工，不重複呼叫開立）。
3. 讀 `result.json`：`issued`／`existing` 的訂單填入發票號碼、日期、統編、買方名稱；其他狀態不輸入 ERP，列在結果清單。
4. 第 6–7 步照現有流程（兩份訂單檔帶物流單號 → ERP 批次）。

## 7. 請 CYInvoice 主控確認的事項

1. ~~正式環境記錄的訂單編號是否就是平台訂單編號~~：使用者 2026-10-13 確認正式環境就是 MO 的 14 碼訂單編號（測試環境的 OrderID namespace 另行處理）。
2. 單一寫入者（3.2）：使用者已確認轉交給已開著的那一個，請 CYInvoice 主控決定轉交機制。
3. 服務連線矩陣中「阻擋至恢復」的狀況（例如光貿或雲端斷線）在接口下如何回報（建議結束碼 3＋結果檔說明）。
4. 「上傳問題」「開立失敗」等既有分類對應到 `failed`／`unknown` 的方式。
5. 酷澎的訂單編號欄位與現有 `CoupangImporter` 是否足以支援同一介面。
6. 測試方式：建議先在 CYInvoice 測試環境以去識別化訂單檔驗證 `issue`、重跑得到 `existing`、`query` 不開立。
