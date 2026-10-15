# CYInvoice 接口實作完成交接 → CYERPAutoInput（2026-10-14）

> **2026-10-15 新交接優先資訊：**CYInvoice Draft #445 已升為 **V2.6.21 Build 0**，加入 `--autoinput-pdf`；[下載已驗證 Windows 工程測試包](https://github.com/simonliu1118-byte/CYapps/actions/runs/38682406918/artifacts/11948538865)（3 天）、精確 commit `283e9f269d74cdea6771962ba2544e5894fa0346`，Windows x64／Windows Client／Cloud／Governance 全通過。PDF 規格、四項確認及未驗收的實機事項以本工作分支 [CYINVOICE_PDF_HANDOFF.md](./CYINVOICE_PDF_HANDOFF.md) 的「完成回報」為準。**以下 V2.6.20／10-14 工程包是舊版參考，請優先使用 V2.6.21 工程包聯測；不代表已正式發行。**


> **AutoInput 端可開始整合測試，但尚不可在正式 MO店+／ERP 全自動流程啟用開票。** CYInvoice 接口已實作、Windows CI 通過；尚未合併／正式發行，實機雙行程、Excel COM 與真實平台流程未驗收。本文件直接放在 AutoInput 的 V0.3.0 工作分支，供其主控接手。

## 可下載的 CYInvoice 工程測試包（V2.6.20 Build 0）

- **[下載 CYInvoice V2.6.20 Windows x64 測試包](https://github.com/simonliu1118-byte/CYapps/actions/runs/38554728718/artifacts/11889940554)**，GitHub Actions Artifact 名稱：`CYInvoice_V2.6.20_engineering-run336`。
- [Windows CI 驗證紀錄](https://github.com/simonliu1118-byte/CYapps/actions/runs/38554728718)：build／core tests／portable smoke／public package scan 全數成功；來源 commit `84704db1a3eb561bb79e1fbfb50364783525da33`。
- 解壓縮後指定 `CYInvoice.exe` 的完整路徑，搭配原有 `--autoinput-query`、`--autoinput-issue` 進行測試；**工程包不應直接覆蓋現有正式工作資料夾**，建議使用獨立的去識別化測試目錄。
- Actions Artifact 依 CYapps 規則只保留 3 天；需要重新取得時需從同一 PR 再產生工程包，勿與正式 Release 混用。

## 交接來源及追蹤
- AutoInput 提案：本專案 `docs/CYINVOICE_INTERFACE_HANDOFF.md`。
- CYInvoice 實作 Draft PR [#445](https://github.com/simonliu1118-byte/CYapps/pull/445)，branch `cyinvoice/feat-autoinput-interface`。
- 完整 CYInvoice 接口規格與安全／驗收狀態：[AUTOINPUT_INTERFACE_HANDOFF.md](https://github.com/simonliu1118-byte/CYapps/blob/cyinvoice/feat-autoinput-interface/apps/CYInvoice/docs/AUTOINPUT_INTERFACE_HANDOFF.md)。
- 已通過 [CYInvoice Windows Build 38552466619](https://github.com/simonliu1118-byte/CYapps/actions/runs/38552466619)（含 Core regression、Windows package/smoke）、[Governance 38552466669](https://github.com/simonliu1118-byte/CYapps/actions/runs/38552466669)、[Cloud Check 38552466568](https://github.com/simonliu1118-byte/CYapps/actions/runs/38552466568)；原始碼驗證基準 commit `57c9d8b2c2dca43c4416b07a0062a32638becb73`。

## 可以開始整合的兩種呼叫
```text
CYInvoice.exe --autoinput-query <request.json> --result <result.json>
CYInvoice.exe --autoinput-issue <request.json> --result <result.json>
```
AutoInput 設定需由使用者指定 **CYInvoice.exe 絕對路徑**，不可猜測安裝目錄。每次建立全新的 request/result 路徑，並保留未處理、需人工確認的暫存目錄。使用者無須再登入 CYInvoice；兩個程式須在**同一 Windows 使用者**下執行。

`request.json`（假資料）：
```json
{
  "schema": "cyinvoice-autoinput-request/1",
  "requestId": "0b6f4f4e-6c3e-4a51-9f0e-2f3c5d1a7e10",
  "source": "mo",
  "file": "C:\\Example\\Temp\\synthetic-orders.xls",
  "orderIds": ["90000000000001"]
}
```
- `source`：`mo`（本輪優先），`coupang`（相同契約預留）；`file` 是未出貨原始 Excel，**不是** AutoInput 自行轉換成其他格式的檔案。CYInvoice 使用自己的解析器和 MO 密碼，MO 加密 XLS 需 Windows 裝有 Excel。
- `orderIds` 省略表示所有匯入訂單；若提供，指定訂單只處理一次。先過濾已判定轉人工的單據，不允許未確認的訂單自動開立。
- `--autoinput-query` 僅查詢既有發票，不新開發票；可對唯一、來源/金額相符的未知紀錄向光貿回查。不明仍不會被當作失敗重新開票。
- `--autoinput-issue` 會實際向光貿發出開立請求！**只可在核准的測試環境及合成資料進行整合驗證，未核准不得在正式環境執行。**

## AutoInput 對 result.json 的判斷
schema `cyinvoice-autoinput-result/1`；頂層含 `requestId`、`cyinvoiceVersion`、`environment`（`production`／`test`）、`finishedAt`、`orders[]`。
各筆訂單含 `orderId`、`status`、`invoiceNumber`、`invoiceDate`（yyyy/MM/dd）、`buyerBan`、`buyerName`、`totalAmount`、`message`。

| status | AutoInput 必須採取的行為 |
|---|---|
| `issued` | 僅當環境、單號、日期、金額核對通過後才可填入 ERP |
| `existing` | 同上；**使用原發票，絕不可重開** |
| `failed` | 轉人工處理，不把「明確失敗」當作可自動重送 |
| `unknown` | 轉人工；可另執行一次 query 回查，**絕不重送 issue** |
| `skipped` | 不輸入 ERP，列為待處理／略過 |

結束碼 `0`＝全數 issued/existing；`1`＝部分失敗／待人工；`2`＝請求／檔案格式問題；`3`＝CYInvoice 目前不可安全處理。**exit code 只作摘要，一定要逐筆解析結果 JSON**；2、3 或執行逾時可能沒有結果檔。

正式 ERP **只接受 `environment=production`**。即使 exit 0，也要逐筆比對平台 OrderID、總金額、發票號碼與日期；任何欄位不符都不進 ERP。
正常免統編的買家 `buyerBan` 為空白，AutoInput 不得自行補入 CYInvoice 內部的 B2C 佔位統編。

## 原本規劃的 MO店+ 流程接點
1. 下載未出貨原始 OrderExport；匯出／批次出貨／列印沿用 AutoInput V0.3.0 流程。
2. 在發票步驟傳**未出貨原始檔**給 CYInvoice（先 query 查既有結果、issue 僅處理需要開立者）。
3. 將成功的發票資訊依 `orderId` 對應到出貨後匯出檔與 ERP 訂單，金額不符停止並轉人工。
4. 使用者按 Esc 停止、Windows 行程逾時／IPC 斷線時，不能假設未開立；保留臨時資料，下一次先 query。
5. 保留原本 MO店+ 預覽手動填發票資料的退路，直到雙端通過實機聯測。

## 仍需由 AutoInput 主控完成
- 設定畫面可指定／檢查 CYInvoice.exe 路徑；生成請求 JSON、啟動／等待 child process，讀 JSON 結果與錯誤處理。
- 在「CYInvoice 已開啟」及「未開啟」兩種狀態測試命令列；核對同 Windows user 的管道 ACL／單一寫入者，另一使用者與其他 portable 目錄需拒絕／隔離。
- 使用合成／去識別化 MO XLS 測試查詢、同訂單重跑、結果不明／官方查無、金額不符、部分失敗、等待逾時、測試／正式環境隔離；酷澎以後有格式樣本再補聯測。
- **仍未完成正式環境實機驗收、未合併 PR #445、尚無含此接口的正式 CYInvoice Release。** 取得正式發行版本並經使用者同意前，不開啟 AutoInput 的 production issue。
- 若聯測查出契約落差，回報 CYInvoice PR #445 修正，不在 AutoInput 另寫第二套開票或光貿鑰匙管理。

本交接為「程式與 CI 已備妥，移交 AutoInput 開始整合」；**不是宣告完整 end-to-end 流程已在真實 MO店+／ERP 跑通**。
