# CYERPAutoInput → CYInvoice：酷澎接入的接口調整（2026-10-15）

> 狀態：**待 CYInvoice 主控處理**（延續 Draft PR #445 `cyinvoice/feat-autoinput-interface`）。由 CYERPAutoInput（AUTO）主控整理；需求，不是永久規則。做完請出 Windows 工程測試包，把連結與確認結果寫在最下方「完成回報」。AUTO 收到後會把本文件彙整回 AUTO 的 README／WORK_HANDOFF 再刪除（使用者 2026-10-15 的文件規則）。
> 現有契約見 AUTO `README.md`「MO店+ 發票（CYInvoice）」一節（query／issue／pdf、request／result、status、結束碼）。

## 背景

- 使用者 2026-10-15：先做酷澎的部分流程測試——使用者在 Chrome 開好酷澎 Wing「我的訂單 › 配送指示」→ AUTO 按「下載Excel」存到 Temp → 以 `source: "coupang"` 呼叫 CYInvoice 查詢／開立 → 帶回發票 → 列印紙本 → ERP。AUTO 端已寫好（「酷澎商城 › 測試：配送指示下載 Excel → 發票 → 列印 → ERP」）。測試時不論 CYInvoice 是測試或正式環境都照做（使用者自己切 CYInvoice 環境）。
- 使用者 2026-10-15 確認的既有規則：**紙本或載具由 CYInvoice 依開立入口決定**——MO店+ 入口開會員載具（沒統編不印）；**酷澎入口一律開紙本**，沒統編印無統編版本、有統編印有統編版本。
- AUTO 依此對酷澎**每一張**已開立（`issued`／`existing`）的發票呼叫 `--autoinput-pdf` 取 PDF 列印（MO店+ 仍只取有統編的）。

## 1. 需調整：`--autoinput-pdf` 支援酷澎無統編的紙本發票

- 2026-10-15 完成回報寫的是「`--autoinput-pdf` 僅輸出有合法公司統編且本機紀錄為紙本的已開立發票，B2C 回 `skipped`」。酷澎入口的 B2C 也是紙本，請改為：**本機紀錄為紙本的已開立發票（不論有無統編）都回 `ready`＋`pdfFile`**；載具／捐贈發票維持 `skipped`。
- `pdfStyle`：AUTO 只送一個設定值（使用者選的公司版型 0／1／2／3／5）。無統編的紙本若只有一種版型（`InvoicePdfStyles.Consumer` 只有 A4），請**自動改用該版型**而不是回錯誤，並在結果 `message` 註明實際使用的版型。
- 若一般消費者紙本 PDF 有正反面或需雙面列印（CYInvoice 的 `InvoicePdfPrinter` 對消費者紙本要求雙面印表機），請在完成回報說明 PDF 的頁面構成（幾頁、是否需雙面），AUTO 再決定列印方式（目前 AUTO 以 A4 印表機單面逐頁、縮放到可列印範圍）。

## 2. 請確認（如與現況不同請調整並註明）

1. 酷澎 Wing「配送指示」頁「下載Excel」下載的檔案，`CoupangImporter` 可直接讀（AUTO 以表頭「訂單編號」「應開立予買家之發票金額」辨認，與 `CoupangImporter` 相同）。若下載檔與先前的 DeliveryList 格式不同，請告知差異。
2. 結果的 `orderId`＝檔案的「訂單編號」（AUTO 以此對應）。
3. `totalAmount`＝該訂單「應開立予買家之發票金額」各列合計（AUTO 以此核對，不同即轉人工）。
4. `buyerBan`／`buyerName`：酷澎有統編時回統編與抬頭；無統編時 `buyerBan` 空白。
5. query／issue／pdf 對 `source: "coupang"` 的行為與 `mo` 相同（重跑回 `existing`、不重開；unknown 不重送）。

## 3. 完成回報（CYInvoice 主控填寫）

- PR／commit：
- CYInvoice 版本：
- Windows 工程測試包下載連結（Actions Artifact，保留 3 天）：
- 第 1 節調整結果（含消費者紙本 PDF 頁面構成）：
- 第 2 節五項確認結果：
- 已知限制：
