# CYERPAutoInput → CYInvoice：酷澎接入的接口調整（2026-10-15）

> 狀態：**CYInvoice 已完成 Source 修正並交回 AUTO；AUTO 尚需實機驗收／列印方式調整**（Draft PR #445 `cyinvoice/feat-autoinput-interface`）。由 CYERPAutoInput（AUTO）主控整理；需求，不是永久規則。做完請出 Windows 工程測試包，把連結與確認結果寫在最下方「完成回報」。AUTO 收到後會把本文件彙整回 AUTO 的 README／WORK_HANDOFF 再刪除（使用者 2026-10-15 的文件規則）。
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

- PR／commit：[CYInvoice PR #445](https://github.com/simonliu1118-byte/CYapps/pull/445)；修正程式 commit `2431410ce5c4`，測試資料日期補強 commit `b9d06600ea94`（V2.6.x AutoInput 介接分支，**不併入 V3 Draft #440**）。
- CYInvoice 版本：**V2.6.21 Build 0**（Windows 工程測試版；不是正式 Release）。
- Windows 工程測試包下載連結（Actions Artifact，保留 3 天）：[下載 CYInvoice_V2.6.21_engineering-run554（GitHub Actions Artifact）](https://github.com/simonliu1118-byte/CYapps/actions/runs/38726511356/artifacts/11968133913)（[完整 Windows x64 Build 執行紀錄](https://github.com/simonliu1118-byte/CYapps/actions/runs/38726511356)；Artifact 約 81 MB，**2026-10-18 19:24 臺灣時間到期**，可能需 GitHub 權限登入）。
- 第 1 節調整結果：**已實作**以 `InvoiceRecord.Delivery == DeliveryPaper` 為輸出紙本依據。酷澎無統編 B2C 紙本現在可得到 `ready`＋`pdfFile`，`buyerBan` 回空字串；指定公司版型 0／1／2／3／5 時，消費者紙本一律實際取 `InvoicePdfStyles.A4`（`pdfStyle=0`），檔名 `<發票號碼>_style0.pdf`，成功 `message` 回「一般消費者紙本發票：實際使用 A4 整張（pdfStyle=0）；請依官方 PDF 頁面確認並使用雙面列印」。公司紙本依原 `pdfStyle`。載具／捐贈／非紙本回 `skipped`，其他不明或錯誤維持保守處理。這個出口只讀官方 PDF Cache，不送出開立 API，也不覆蓋異內容的既存檔。
  - **消費者紙本頁面／雙面說明**：CYInvoice 原有 `InvoicePdfPrinter` 設定 A4、`PagesPerSide=1`、`TwoSidedLongEdge`，且要求雙面印表機；這是程式的列印政策，**不等於已確認官方 PDF 固定幾頁**。此次沒有真實光貿酷澎 B2C PDF 供確認實際頁數、背面內容及頁序。AUTO 目前的「A4 單面逐頁」並非相同列印契約；**正式自動列印 B2C 前必須先取得實際 PDF 樣本、驗證頁數／頁序，並完成雙面長邊或經人工確認的等效處理**，不可先當作單頁單面完成。
- 第 2 節五項確認結果：
  1. **條件相容；真實 Wing 匯出仍待驗收**：`CoupangImporter` 會搜尋含「訂單編號」「應開立予買家之發票金額」的表頭，另要求「顯示產品名稱」「數量」「訂購人姓名」「應開立予酷澎之發票金額」「統一編號」完整存在；解析過程含分組與格式檢查。程式的合成資料測試通過，但**尚未取得使用者當天 Wing「配送指示」真實下載檔**，不能宣稱兩種檔案格式已實機比對；若 Excel 不含這七欄，CYInvoice 會拒絕解析、需另做映射。
  2. **確認**：`orders[].orderId` 由來源檔的「訂單編號」／原始 `order.OrderId` 回傳，不會改寫為光貿環境 namespace 的 API OrderID。
  3. **確認**：`totalAmount` 由「應開立予買家之發票金額」依同 `orderId` 分組逐列 `checked` 累加；query／issue 會比對既有發票紀錄金額，不符回 `unknown`、不自動重開。
  4. **確認程式行為**：酷澎公司戶沿用合法 8 碼統編與既有買方名稱（可能由官方統編名稱查詢更新）；無統編時 `buyerBan` 回空白、`buyerName` 沿用已開立本機紀錄。不能將 `0000000000` 當作 ERP 統編。
  5. **確認源碼契約**：`source: "coupang"` 走與 MO 相同的 `--autoinput-query`／`--autoinput-issue`／`--autoinput-pdf` 契約；僅來源匯入／紙本交付不同。同來源、同環境、相同訂單已開立回 `existing`，不再開第二張；結果不明不自動重送，只可查回／人工處理。PDF 同號重查比對 SHA256，不覆寫內容不同的舊 PDF。
- CI／工程包驗證：CYInvoice `b9d06600ea94e971e42f100982cc2ec0cda96785` 的 **Windows x64 Build、Windows Client、Cloud Validate、Governance 全部 success**，Core parity／void／allowance PDF／SQLite／sync、Windows 啟動／PE 封裝／公開安全掃描均通過。此工程包是 source 等級的合成驗證，不是實機開票／列印驗收。
- 已知限制：**本次是程式碼與合成 CI／工程包，尚非酷澎 Wing／光貿官方／實際印表機／ERP 的完整端對端驗收**；消費者紙本官方 PDF 頁數及 AUTO 的雙面列印方式仍待實際檔案確認。測試與正式環境取用 CYInvoice 當前已儲存設定，AUTO 不可強制切換；PDF 只允許已開立、來源及環境吻合且狀態可安全查核的紀錄。正式 PR 合併、部署、Release 與真實開票均未執行。
