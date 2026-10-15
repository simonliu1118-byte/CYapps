# CYERPAutoInput → CYInvoice：接口追加交接（紙本發票 PDF 與兩項確認，2026-10-15）

> 狀態：**待 CYInvoice 主控實作**。由 CYERPAutoInput（AutoInput）主控對話整理。本文件是需求，不是永久規則；CYInvoice 端以 `/REPOSITORY_RULES.md`、`/REPO_POLICY.md`、`apps/CYInvoice/PROJECT_RULES.md` 為準，修改走 CYInvoice 自己的 branch／PR（建議延續 PR #445 `cyinvoice/feat-autoinput-interface`）。
>
> **使用者要求（2026-10-15）：CYInvoice 做完後直接產出 Windows 工程測試包（Actions Artifact），把下載連結寫回本文件最下方「完成回報」，AutoInput 端即可下載做兩個程式的協同測試。**

## 0. 背景

- AutoInput 已依 PR #445 的契約寫好呼叫端（V0.3.0 Build 26，PR #424 `cyerp-auto-input/shopee-batch`）：`--autoinput-query` → 問過使用者 → `--autoinput-issue`（只送 query 回 `skipped` 的訂單）→ 逐筆核對（號碼、日期、金額、統編）才帶入 ERP 預覽。環境由 AutoInput 從 query 結果的 `environment` 判斷：全自動只收 `production`，測試入口兩者都可但測試環境不進 ERP。
- AutoInput 入口「MO店+ › 測試：發票」：使用者放真實 MO店+ 訂單匯出檔、CYInvoice 開**測試環境**（使用者：CYInvoice 對測試環境送出的資料自己有處理，不必擔心外洩）。
- 使用者 2026-10-15 的紙本發票規則：
  - MO店+ **一般消費者 → 會員載具發票，不印紙本**。
  - 客人有填**統編抬頭 → 紙本發票，要印出**。
  - **光貿開立完成時間不一定，完成後才拿得到發票 PDF**；PDF 有 5 種格式（CYInvoice `InvoicePdfStyles.Company`），使用者在 AutoInput 設定先選好。
  - 列印走 AutoInput 的 A4 印表機，印前跳出放紙確認（AutoInput 已實作）。

## 1. 需新增：`--autoinput-pdf`（只取官方 PDF，絕不開立）

```text
CYInvoice.exe --autoinput-pdf <request.json> --result <result.json>
```

與 query／issue 相同的程序規則：同一 Windows 使用者、CYInvoice 已開著就委派主行程、未開著自行處理；result.json 原子寫入、不覆寫；結束碼 0／1／2／3 同現有定義（全部 `ready` 才是 0）。

### request

沿用 `cyinvoice-autoinput-request/1`，多兩個欄位（現有 `ParseRequest` 會忽略未知欄位，所以不需改 schema 版本）：

```json
{
  "schema": "cyinvoice-autoinput-request/1",
  "requestId": "7d1c2a9e-0000-4000-8000-000000000001",
  "source": "mo",
  "file": "C:\\Example\\CYERPAutoInput\\Temp\\mo-20261015-101500\\synthetic-orders.xls",
  "orderIds": ["90000000000001"],
  "pdfFolder": "C:\\Example\\CYERPAutoInput\\Temp\\invoice-20261015-101530\\paper",
  "pdfStyle": 0
}
```

- `orderIds`：pdf 模式**必填**；AutoInput 只送有統編、先前 `issued`／`existing` 的訂單。
- `pdfStyle`：`InvoicePdfStyles.Company` 的代碼：`0` A4 整張、`1` A4（地址＋A5）、`2` A4（A5 內容）、`3` A5、`5` QRcode A4。其他值 → 結束碼 2。
- `pdfFolder`：AutoInput 已建立的絕對路徑；CYInvoice 把官方 PDF（光貿下載，沿用現有 PDF Cache 與 20 MiB 上限）**複製**到這裡，檔名建議 `<發票號碼>_style<代碼>.pdf`。
- `file`：同一份未出貨原始檔（若 pdf 模式不需要讀檔，可只做存在性檢查，但請保留欄位以維持同一套解析）。

### result

沿用 `cyinvoice-autoinput-result/1`，每筆多一個 `pdfFile`：

```json
{
  "schema": "cyinvoice-autoinput-result/1",
  "requestId": "7d1c2a9e-0000-4000-8000-000000000001",
  "cyinvoiceVersion": "2.6.21+0",
  "environment": "test",
  "finishedAt": "2026-10-15T10:16:00+08:00",
  "orders": [
    {
      "orderId": "90000000000001",
      "status": "ready",
      "invoiceNumber": "AB12345678",
      "invoiceDate": "2026/10/15",
      "buyerBan": "12345675",
      "buyerName": "範例股份有限公司",
      "totalAmount": 913,
      "message": "",
      "pdfFile": "C:\\Example\\CYERPAutoInput\\Temp\\invoice-20261015-101530\\paper\\AB12345678_style0.pdf"
    }
  ]
}
```

| status | 意義 | AutoInput 已實作的做法 |
|---|---|---|
| `ready` | 已取得官方 PDF，`pdfFile` 是 `pdfFolder` 內的絕對路徑 | `invoiceNumber` 與先前開立結果相同才印 |
| `pending` | 光貿尚未開立完成，還沒有 PDF | 在背景每 20 秒再呼叫一次（不中斷全自動與打單），最多 30 分鐘 |
| `failed`／`unknown` | 取不到（`message` 說明原因） | 不印，結果清單註明「請在 CYInvoice 列印」 |
| `skipped` | 沒有這筆發票、或是一般消費者（無統編） | 不印 |

- 只取 PDF、**絕不送開立**；重複要同一張 PDF 不可有副作用（AutoInput 自己記錄印過的發票號碼，不會重印）。
- `environment` 必須是該發票所在的環境；AutoInput 會比對與先前 query／issue 相同才印。
- 等待光貿完成的重試由 AutoInput 做；`--autoinput-pdf` 每次呼叫請盡快回傳（建議單次不超過 60 秒，光貿未完成就回 `pending`），不要在 CYInvoice 內長時間等待。
- 舊版（不認得 `--autoinput-pdf`）時 AutoInput 會收到沒有結果檔的結束碼，只註明「請在 CYInvoice 列印」，不影響開立流程；請確認不認得的模式會以結束碼 2 盡快結束、不會跳出視窗卡住。

## 2. 需確認（如與現況不同請調整並註明）

1. **紙本／載具由 CYInvoice 決定**：MO店+ 訂單無統編 → 會員載具（不產生紙本）；有統編抬頭 → 紙本。AutoInput 只以結果的 `buyerBan` 是否空白判斷要不要印。請確認 MO 匯入開立時就是這個規則。
2. **`orderId` 在測試與正式環境都等於請求送出的 MO店+ 14 碼訂單編號**：PR #445 前曾提到「測試環境的 OrderID namespace 另行處理」。AutoInput 以 `orderId` 對回訂單，若測試環境在內部加了前綴，結果檔仍請回傳原始訂單編號，否則測試入口全部會對不上。
3. **`totalAmount` 等於 MO 匯出檔的「開立發票金額」整單合計**（AutoInput 以此比對，不同即轉人工）。
4. CYInvoice 未開啟時，query／issue／pdf 由 CLI 自行處理是否需要使用者先開好測試環境；若 CLI 會沿用上次關閉時的環境，請在交接註明，AutoInput 測試說明會照寫。

## 3. AutoInput 端已完成（供對照，不需 CYInvoice 修改）

- `CyInvoiceLink.cs`：request 產生、result 解析（schema、requestId 必須相符）、逐筆核對、紙本判斷、`Data\paper_invoices.json` 防重印；單元測試 `CyInvoiceLinkTests`。
- `MainForm.Invoice.cs`：子行程呼叫（`ProcessStartInfo.ArgumentList`、逾時＝每筆 60 秒＋60 秒，只停止等待不殺行程）、query→issue→unknown 再 query 一次、環境判斷、紙本 PDF 輪詢與 A4 列印、測試入口。
- 每次的 request／result 留在 `<AutoInput>\Temp\invoice-日期時間\`。

## 4. 完成回報（CYInvoice 主控填寫）

- PR／commit：
- CYInvoice 版本：
- Windows 工程測試包下載連結（Actions Artifact，保留 3 天）：
- 第 2 節四項確認結果：
- 已知限制：
