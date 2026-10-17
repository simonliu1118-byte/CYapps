## 2026-10-15 插隊完成的新增實作：AutoInput 紙本 PDF（工程待驗收）

**版號：V2.6.21 Build 0，PR #445（Draft，尚未合併／正式發行）**。本節對應 AutoInput PR #424 的 `apps/CYERPAutoInput/docs/CYINVOICE_PDF_HANDOFF.md`；原文 V2.6.20 的 issue/query 紀錄繼續有效，版本已往前推進。

- 新增 `--autoinput-pdf <request.json> --result <result.json>`：原 `cyinvoice-autoinput-request/1` 加 `orderIds` **必填**、絕對路徑且事先建立的 `pdfFolder`、有效 `pdfStyle` 0／1／2／3／5（不另起 schema）。壞參數與未知 `--autoinput-*` 模式回 exit 2，不會開 UI。IPC 多 `Pdf` 位元但沿用同使用者管道／原 portable Mutex，主程式開著時只由原行程存取 SQLite。
- `AutoInputPdfExport` 用本機唯一的正式 `InvoiceRecord`，驗證來源／原 14 碼 OrderID／環境、公司 8 碼統編、紙本 `DeliveryPaper`、有效發票號與日期、原發票狀態 `Opened`、`UploadStatus=99` 後，呼叫**原有 `InvoiceService.GetInvoicePdfAsync`**（同版型與 PDF Cache，最多 20 MiB）。`--autoinput-pdf` **絕不呼叫 IssueMo／IssueCoupang／AMEGO 開立 API**。
- `pdfFile` 為 `pdfFolder\<invoiceNumber>_styleN.pdf`；先下載官方 PDF 到舊 cache，再獨立暫存檔複製／原子 move，不覆寫已有的 PDF；重複取得時核對既存檔 SHA256 完全等於 cache，若內容衝突回 `unknown` 不更動舊檔。
- 公司戶且官方 PDF 可取得 → `ready`；已有正式發票、光貿狀態未完成／PDF 尚不可得 → `pending`；一般消費者／根本無原發票 → `skipped`；環境、來源、狀態、檔案或結果不明 → `unknown`；逐筆成功皆 ready 才 exit 0，其餘 1。PDF 查詢單次限約 55 秒，不做 CYInvoice 內部輪詢，AutoInput 每 20 秒外部重查。
- 既有 issue/query 的 `orderId` 是傳入的**原始 MO 訂單號碼**，測試環境送給光貿的 API OrderID 若加 namespace 也不會輸出成 `orderId`。金額使用 CYInvoice MO 匯出檔的 `TotalAmount` 並與已開立本機紀錄核對。
- 既有 `IssueMoWithLookupAsync`：**B2C → 會員載具**，有 8 碼公司統編 → **紙本**（若另明確捐贈為例外，優先走捐贈）。`--autoinput-pdf` 只處理公司統編＋紙本交付，防止意外列印消費者載具。
- CYInvoice CLI 沒開主視窗時**讀取該 portable 目錄最後儲存的 `settings.Environment`**；不會由 AutoInput request 強迫切換 test/prod。AutoInput 測試前先在 CYInvoice GUI 設定「測試環境」並儲存，或用獨立測試 portable 目錄；所有回覆提供實際 `environment` 供再次核對。

新增 Core 測試涵蓋 PDF 參數、未知 CLI、公司戶／一般消費者、成功／pending／skipped、重複下載、PDF 檔案衝突不得覆寫、`pdfFile` JSON、零開立呼叫；Windows build／startup smoke 仍以**最新 exact PR commit CI**為準。**尚未驗證**：真實 Windows IPC／ACL、加密 MO XLS 的 Excel COM、光貿不同完成時間、AutoInput ERP 實機 A4 列印。直到雙端驗收前不啟用正式自動開立／自動列印。

---

# CYInvoice ↔ CYERPAutoInput 接口實作／驗收交接（2026-10-14）

> **V2.6.20 工程測試版，目前是待驗收的 Draft PR #445，尚未合併或發布；AutoInput 不應在正式流程啟用自動開票。** 本文件如實區分「已寫入 source」與「CI、Windows 實機驗收通過」。

## 來源與責任
- AutoInput 需求母本：`apps/CYERPAutoInput/docs/CYINVOICE_INTERFACE_HANDOFF.md`，分支 `cyerp-auto-input/shopee-batch`（PR #424）。
- CYInvoice 實作：`cyinvoice/feat-autoinput-interface`（PR #445），獨立於 V3 災難復原 PR #440。
- 只有 CYInvoice 負責 MO店+／酷澎訂單解析、光貿開立、確認與本機持久化；AutoInput 不持有光貿憑證。
- 無新增登入；權限邊界為**同一個 Windows 使用者＋相同 CYInvoice portable 程式目錄**的具名管道 ACL，及現有 Mutex 寫入者。
- 沒有雲端 API、Cloudflare D1、CYID 權限模型或正式 AMEGO 開票資料變更。

## 實作檔案
- `src/CYInvoice.Core/AutoInputContract.cs`：schema／GUID／來源／絕對路徑／orderIds 格式限制。
- `src/CYInvoice.Core/AutoInputCommandLine.cs`：兩種 CLI 模式及結果檔參數解析。
- `src/CYInvoice.Core/AutoInputResultFile.cs`：結果 JSON 同目錄暫存寫入、原子更名、不覆寫已存在結果。
- `src/CYInvoice.Core/AutoInputRecordDecision.cs`：本機已開、未知、失敗、來源／買方／金額不符時的保守判斷。
- `src/CYInvoice.Core/InvoiceService.cs`：在既有開立序列化鎖**內部**加入 AutoInput 專用 replaySafe／expectedEnvironment 檢查；另有 `ReconcileForAutoInputAsync` 僅回查光貿、不執行開立。
- `src/CYInvoice.WinForms/AutoInputProcessor.cs`：透過原有 `PlatformImportReader` 及 `InvoiceService` 實際處理兩種來源，批次結果逐單彙總，部分失敗不重送。
- `src/CYInvoice.WinForms/AutoInputPipe.cs`：同一 Windows 使用者 ACL／portable 目錄雜湊命名的具名管道。主程式持續服務，第二行程不碰 SQLite。
- `src/CYInvoice.WinForms/Program.cs`：無主視窗 CLI；CYInvoice 未開啟時先取得原 Mutex，已開啟則委派主行程。
- `src/CYInvoice.WinForms/MainForm.cs`：主程式啟動 IPC，批次尚在執行時防止一般關閉。
- `tests/CYInvoice.Core.Tests/Program.cs`：schema、重複訂單、既有／未知狀態、無覆寫原子結果、序列化防重送、環境釘選，以及未知發票只查詢不開立的測試。

## AutoInput 呼叫契約
```text
CYInvoice.exe --autoinput-issue <request.json> --result <result.json>
CYInvoice.exe --autoinput-query <request.json> --result <result.json>
```
`request.json` 範例（**假資料**）：
```json
{
  "schema": "cyinvoice-autoinput-request/1",
  "requestId": "0b6f4f4e-6c3e-4a51-9f0e-2f3c5d1a7e10",
  "source": "mo",
  "file": "C:\\Example\\AutoInput\\Temp\\synthetic-orders.xls",
  "orderIds": ["90000000000001"]
}
```
- `source`：`mo`、`coupang`；來源原始 Excel 一律由 CYInvoice 匯入器解析。MO店+ 加密 XLS 使用 CYInvoice 自己的 MO 密碼，依現有實作透過本機 Microsoft Excel COM 讀取（需安裝 Excel）。
- `orderIds` 可省略；若存在必須至少一筆、不得重複，最多 10,000 筆，選填時只處理指定訂單。JSON 請求最多 1 MiB。
- AutoInput 必須指定存在的 CYInvoice.exe 完整路徑、不同的 request/result 絕對路徑；每次執行使用**全新 result.json 路徑**。既有 result.json 不覆蓋。
- `--autoinput-query` 不送開立請求；先查本機資料，對唯一且資料相符的未知／待核對紀錄，允許向光貿嚴格回查並更新本機發票狀態；來源、買方或金額衝突時不回查代替推斷。

`result.json` 結構：
```json
{
  "schema": "cyinvoice-autoinput-result/1",
  "requestId": "0b6f4f4e-6c3e-4a51-9f0e-2f3c5d1a7e10",
  "cyinvoiceVersion": "2.6.20+0",
  "environment": "test",
  "finishedAt": "2026-10-14T10:00:00+08:00",
  "orders": [
    {
      "orderId": "90000000000001",
      "status": "existing",
      "invoiceNumber": "AB12345678",
      "invoiceDate": "2026/10/14",
      "buyerBan": "",
      "buyerName": "消費者",
      "totalAmount": 725,
      "message": ""
    }
  ]
}
```
回傳狀態：`issued`（此次開立且嚴格確認）、`existing`（既有已確認發票）、`failed`（歷史明確失敗）、`unknown`（結果不明、來源／金額衝突或待查）、`skipped`（指定訂單檔內不存在或中途批次停止未處理）。非公司戶的發票統編佔位字串 `0000000000` 不會交給 ERP，輸出為空白。

結束碼：`0` 所有訂單為 issued／existing、`1` 有 unknown／failed／skipped、`2` 請求／匯入檔／結果路徑無效、`3` 服務／裝置無法安全處理。遇到 2／3 可能**沒有結果檔**，AutoInput 不可視為未開立，更不可因此重送 issue。

## 核心安全邊界
1. **同一來源＋訂單號碼＋環境**判斷，跨來源、買方或金額不符一律 unknown。已有發票回 existing，絕不再開；先前失敗需人工處理。
2. 既有 `InvoiceService` 的序列化 issueGate 內再檢查歷史紀錄，防止主 UI 與 AutoInput 同時送同一訂單的常見競態。
3. 每次 issue 將預期的 test／prod 環境傳入 `InvoiceService`，送往光貿前再次確認環境沒有切換。
4. 逾時、IPC 中斷、外部結果不明、本機保存異常時不憑例外類型決定重送；AutoInput 需以 query 查回，查不清楚轉人工。
5. AutoInput **正式 ERP 僅接受** `environment=production` 且逐筆 `issued|existing`、日期、號碼、金額通過比對的訂單。
6. 使用者要求免員工登入，不等於開放跨 Windows 使用者、跨資料夾或雲端 HTTP 未驗證入口；本接口僅接受本機同 Windows 使用者端。

## 驗收狀態與剩餘工作
- **2026-10-14 CI 已通過**：Windows Build [run 38552466619](https://github.com/simonliu1118-byte/CYapps/actions/runs/38552466619)（編譯、Core parity／既有回歸測試、portable 封裝與 smoke）、[Governance run 38552466669](https://github.com/simonliu1118-byte/CYapps/actions/runs/38552466669)、[Cloud Check run 38552466568](https://github.com/simonliu1118-byte/CYapps/actions/runs/38552466568)。對應驗證 commit `57c9d8b2c2dca43c4416b07a0062a32638becb73`。
- **仍未完成 Windows 實機端對端驗收**：雙行程 IPC／ACL 與 MO店+ Excel COM 使用真實 Windows 安裝環境尚待 AutoInput 端聯測，CI 不代表正式光貿開票已驗證。
- 仍需使用**合成／去識別化**訂單檔驗證：CLI 主程式未開啟及已開啟、現有 SQLite 單 writer、IPC ACL、Excel COM、同筆重跑、query 回查、部分失敗、timeout、Cloud revoked／reset／環境切換、result.json 實際與 ERP 欄位比對。
- 在上述驗收完成前，AutoInput **不得**移除 MO店+ 發票欄位手動補入的過渡操作，也不得啟用正式 `--autoinput-issue`；不得對真實光貿進行測試開票。
- 完成驗收後，此文件補充 CI run／正式版號／發行 commit 與 AutoInput 端可使用版本，交回 AutoInput 主控。正式 Release／PR 合併須另外確認。
