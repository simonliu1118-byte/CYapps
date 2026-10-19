# CYERPAutoInput WORK HANDOFF

> 更新：2026-10-17（V0.3.1 正式 Release，tag `cyerp-auto-input-v0.3.1`，由 PR #424 合併進 `main` 後發布；含自動更新 `AppUpdater`／`UpdateCheck`／`MainForm.Update`）。之後的修改依規則升 Z 或 Build。**新 session 接手先看 §0。**
> 協作方式（使用者 2026-10-06 指定）：Claude Code 主控；Claude 額度不足時由 Codex 接手，Claude 額度恢復後檢查確認 Codex 的變更。交接以本文件＋Git 狀態為準。  
> 本文件只記錄目前狀態、已驗證事實、待辦與交接注意事項，不是永久規則來源。逐版變更見 `CHANGELOG.md`，功能說明見 `README.md`。

## 0. 目前進度與下一步（2026-10-15，給下一個 session）

工作方式（使用者指定，沿用）：
- 一律正體中文。使用者實機測試、每輪傳 LOG（`logs/CYERPAutoInput_日期.log`）與截圖；修好就推到 PR #424 的 branch `cyerp-auto-input/shopee-batch`，等 CI 成功，把 Windows 測試包 Artifact 連結貼成 PR 留言（結尾加 Claude Code 簽名）並回覆使用者。Artifact 保留 3 天。
- 使用者說「先不要發，湊多一點項目」時只推 `cyerp-auto-input/next-wip`（沒有 PR、不跑 CI）。未交付的修正不加 Build（目前仍是 Build 26）。
- 只動 CYERPAutoInput 相關檔案；CYInvoice 的程式不改，要它調整就暫時在 `docs/` 寫交接檔（使用者轉給 CYInvoice session，對方做完會出工程測試包）；完成後依共通文件分工彙整回 README（功能／介面）、本文件（狀態）、CHANGELOG（變更），刪掉交接檔（使用者 2026-10-15）。
- Commit 身分依 REPOSITORY_RULES：`simonliu1118-byte <286269326+simonliu1118-byte@users.noreply.github.com>`。2026-10-14～15 有數個 Build 26 commit 誤用 `Claude <noreply@anthropic.com>`，未改寫歷史（PR 以 squash merge 合併時只留一個提交）；之後一律用規定身分。
- 本機驗證：`dotnet test tests/CYERPAutoInput.Tests`（286 項）與 `dotnet build CYERPAutoInput.csproj -c Release -r win-x64 -p:EnableWindowsTargeting=true`。

**A. MO店+ 全自動（主線）**
- 流程（`MainForm.RunMoBatchAsync` → `ShopeeBatchRunner.RunMoAsync`）：開始前共用 ERP 檢查（`ErpWindowReady`：`SelectCopi08` 選得到 COPI08 或沒開（開始時 `ResolveCopi08Async` 從作業速查開），ERP 不能有對話框；其他作業視窗不算）→ 自己的 Chrome 視窗 → 我的訂單「未出貨」匯出（Temp\mo-日期時間）→ 批次出貨：各配送方式全選 → 批次出貨 → 併箱 → 確認出貨 → 批次出貨記錄依時間讀最上面一筆 → 列印文件 → 存標籤（新竹另存出貨總表）→ 最後一種出貨完直接「我的訂單 › 配送中」匯出（還有其他配送方式時才回批次出貨頁）→ 兩個匯出檔直接帶入（`MoImportForm.Accept`，對不起來才開匯入視窗）→ CYInvoice 開發票（一律，見 B）→ 全部乾淨（都有發票、無待確認、沒輸入過、自動儲存開）就不開預覽：列印（全家／7-11 熱感、新竹 A4 放紙提醒）→ 批次 ERP → 完成清單；否則開預覽確認後同樣列印 → ERP。
- 已實機通過：到「列印標籤」存 PDF（2026-10-14）；存標籤後關「注意」、配送中匯出、帶入兩檔（2026-10-15，停在當時的提示，該提示已拿掉）。
- 待實測：整條全自動（含 CYInvoice、不開預覽、列印、批次 ERP、表格完成清單）；新竹／7-11 的批次出貨與 PDF；多種配送方式同批；7-11 熱感實印（`SlipImage.FindFramedSlips`，實檔外框 83×122 mm，原尺寸）。
- 選單：全自動／匯入訂單匯出檔、重新列印寄件單。「匯入訂單匯出檔」自 V0.3.2 起就是原「測試：發票」（`ImportMoAsync`，兩檔 → CYInvoice 查詢／開立 → 紙本背景 → 預覽 → ERP；使用者 2026-10-19）。

**B. CYInvoice 發票整合**（功能與呼叫介面見 README「MO店+ 發票（CYInvoice）」；原本三份 docs/CYINVOICE_*_HANDOFF.md 已於 2026-10-15 依使用者要求彙整回 README／本文件／CHANGELOG 並刪除，原文在 Git 歷史 commit `d5244bad` 以前）
- CYInvoice：V2.6.21 Build 0（Draft PR #445，branch `cyinvoice/feat-autoinput-interface`，commit `283e9f26`），工程測試版、未合併發行；工程包 Artifact 2026-10-18 過期（過期請 CYInvoice 端重出，不可換用正式 V2.6.19）。契約要改時在本專案寫需求交 CYInvoice（PR #445），對方做完出工程包；不在 AUTO 另寫開票。
- CYInvoice 已確認（2026-10-15）：有統編→紙本、無統編→會員載具（pdf 對 B2C 回 skipped）；`orderId` 永遠是 MO 原始編號；`totalAmount` 為 MO 應開發票整單金額；CLI 用最後儲存的環境、request 不能切換；PDF 經其 PDF Cache 驗證後原子複製、不呼叫開立、單次約 55 秒內回應。
- 使用者實測（2026-10-15，CYInvoice 測試環境）：「測試：發票」選兩個訂單檔 → 開立 → 帶回預覽 → 開始輸入 ERP 全部正確。使用者決定：全自動直接開立不詢問、最後列完成清單。
- 待實測：全自動整條串 CYInvoice、紙本 PDF → A4 列印；正式環境在 CYInvoice 正式發行後使用。測試前 CYInvoice 要切測試環境並儲存，建議獨立 portable 資料夾。

**完成清單**：所有批次（蝦皮全自動、MO店+／酷澎／蝦皮匯入）共用 `BatchSummaryForm`，2026-10-15 起為「狀態／訂單編號／說明」表格（`BatchSummary` 純邏輯，有測試），需人工的列在前、橘色；複製為 Tab 分欄。

**D. 酷澎全自動（使用者 2026-10-15 先口述流程與截圖，未開始；等真的有酷澎訂單再逐步核對實作）**
1. 後台 Coupang Wing（wing.coupang.com）。首頁可能隨機出現彈窗（2026-10-15 樣本：「出貨管理升級！設定更方便、期限更清楚」，下方有「今天不再顯示此內容」勾選與「關閉」）。使用者實測：可以不理會，直接點左側「訂單/出貨 › 我的訂單」即可。實作時先照此直接點；若點不到（被遮住、頁面沒換），再按彈窗的「關閉」。
2. 左側「訂單/出貨 › 我的訂單」，預設在「訂單確認」分頁，訂單列在下方表格：全選 → 右下角「確認訂單」（確認後狀態變為待安排配送）。
2b. 已實作（V0.3.2 Build 2，未實機）：入口「酷澎商城 › 全自動」（`MainForm.RunCoupangBatchAsync` → `ShopeeBatchRunner.RunCoupangAsync`：`ConfirmCoupangOrdersAsync`、`ArrangeCoupangShippingAsync`／`ChooseShipMethodAsync`（原生 select 用鍵盤 Home／Down＋Enter，讀 ComboBox 值核對）／`ArrangeChannelAsync`（發票提醒、`ArrangeDialog` 取貨地址＝`HctPickupAddress`、`PickupDetails` 全部成功 → `PrintCoupangSlipsAsync` 熱感 宅配 `CoupangHctLabelScale` 22%／超商 100%，`PrintDialogAsync(sheetsAllowed)`）、配送指示 `DownloadListExcelAsync`）→ `CoupangToErpAsync`（已在 ledger 的略過、CYInvoice 酷澎、非正式環境開預覽）。使用者 2026-10-19 截圖：確認接受訂單／配送處理完成／配送準備下拉（宅配-嘉里、宅配-新竹物流、便利商店-全家、便利商店-7-11）／請確認發票開立需求／安排出貨（取貨地址）／取貨詳情（列印寄件單）／配送指示（待出貨清單、下載Excel）。超商的安排出貨與寄件單畫面尚未看過。
3. 「配送準備」分頁：每筆有「安排配送」，也可右下角選配送方式（例：宅配 - 嘉里）後「批次安排配送」。細節等有訂單時使用者帶著做再告知。
4. 「配送指示」分頁：全選 → 「下載Excel」→ 存 Temp → CYInvoice 開發票 → 帶回發票 → ERP 輸入（同 MO店+）。此頁另有右下角「批次列印寄件單」（選配送方式），寄件單列印方式待確認。
- 發票：紙本或載具是 CYInvoice 依開立入口的既有規則（使用者 2026-10-15）：MO店+ 入口開會員載具，沒統編的不印；酷澎入口一律開紙本，沒統編印無統編版本、有統編印有統編版本。AUTO 目前只對有統編的訂單取 PDF（`CyInvoiceLink.IsPaper` 看 `buyerBan`，是照 MO店+ 寫的）；做酷澎時改為依來源（酷澎全部取 PDF），並先向 CYInvoice 確認 `--autoinput-pdf` 對酷澎無統編的紙本發票也回 `ready`（2026-10-15 的回報寫的是只輸出有公司統編的紙本，B2C 回 `skipped`）。
- 2026-10-15 先做的測試入口：「酷澎商城 › 測試：正在配送全選下載 Excel → 發票 → 列印 → ERP」（`ShopeeBatchRunner.DownloadCoupangExcelAsync` 只找頁面上的「下載Excel」，頁面由使用者開好；`MainForm.TestCoupangAsync`；發票走共用 `InvoiceAsync(source: coupang)`，酷澎一律紙本、全部取 PDF；沒統編的 PDF 雙面長邊列印 `SlipPrinter.PrintPages(duplex)`）。CYInvoice 已於 2026-10-15 調整（PR #445 commit `b9d06600`，V2.6.21 Build 0 工程包 run 554，2026-10-18 過期）：`--autoinput-pdf` 依本機紀錄為紙本輸出（酷澎 B2C 也 `ready`），消費者紙本一律 A4 整張（style0）並要求雙面；`orderId`＝訂單編號、`totalAmount`＝應開立予買家之發票金額合計、`source: coupang` 契約同 MO。CYInvoice 讀檔需七欄（見 README），Wing「配送指示」真實下載檔尚未比對。尚未實機：下載鈕、Chrome 另存新檔、下載檔格式、消費者紙本官方 PDF 的頁數／頁序與雙面列印（CYInvoice 提醒：正式自動列印前要用實際 PDF 確認）。原交接文件 docs/CYINVOICE_COUPANG_HANDOFF.md 已彙整回本文件與 README 後刪除（Git 歷史 commit `5c242d8c`）。
- 共用：開始前 ERP 檢查（`ErpWindowReady`）、完成清單表格、背景紙本佇列、`source: "coupang"`（CYInvoice 契約已預留）。

**C. 其他待辦**：蝦皮全自動的 7-11／全家實測、黑貓 A4（§6）、無人值守（Roadmap 5）。合併與正式發布都要使用者同意。

## 1. 接手時先讀

1. `/REPOSITORY_RULES.md`
2. `/REPO_POLICY.md`
3. `/apps/CYERPAutoInput/PROJECT_RULES.md`（目前 Governance 2.3.39）
4. 本文件 → `README.md` → `CHANGELOG.md`
5. 工作 branch `cyerp-auto-input/shopee-batch` 的實際 source（PR #424；`main` 已於 2026-10-13 合併進此 branch）

不要以舊聊天記憶覆蓋目前 Git 狀態。

## 2. Branch／PR／版本

- Repository：`simonliu1118-byte/CYapps`，專案：`apps/CYERPAutoInput/`
- `main`：`V0.2.9`（2026-10-12 由 PR #423 squash merge），正式 Release `cyerp-auto-input-v0.2.9`。`CYERPAutoInput Release` workflow：只從 `main` 手動啟動、BUILD 必須為 0、tag 不覆寫，Release note 取 CHANGELOG 該版段落中第一個「- Build」之前的摘要。舊 Release `cyerp-auto-input-v0.2.7` 依使用者要求以 `CYERPAutoInput Release Cleanup` workflow（只從 main、只接受本專案 tag）刪除。之前的合併：PR #421（V0.2.5–V0.2.7）、PR #418（V0.2.4）、PR #398（V0.2.1–V0.2.3 Build 1）、PR #104（V0.2.0 Build 31）。工作 branch 合併後刪除。下一次正式發布同樣需使用者同意。
- 目前工作：branch `cyerp-auto-input/shopee-batch`、Draft PR #424，V0.3.0 Build 26（蝦皮全自動、自動擷取改名、MO店+ 兩檔匯入、預覽勾選與共用結果清單）。使用者交代的下一輪項目已在 Build 19 完成，2026-10-14 實單測試後再決定合併與發布；合併與發布都需使用者同意，合併後刪除 branch，下一輪再從最新 `main` 開新 branch。本專案目前沒有其他未合併的 branch。
- VERSION／BUILD 見同名檔。`V0.1.0` 為 Codex C# 重寫（Build 1–27），`V0.2.0` 為使用者指定的 Minor，代表 Claude Code 接手。新功能 Z＋1（Build 歸零），返修 Build＋1；使用者說「下一版一起修」的項目不單獨發版。
- 技術棧：C# / .NET 8 / WinForms；OCR 為本機 PaddleOCR PP-OCRv5 mobile + ONNX Runtime（CPU）。由 Go 改寫是使用者同意的技術棧變更（Go 線讀不到 DevExpress virtual grid 的 cell 文字）。
- 測試包：PR 的 CI Artifact（保留 3 天），連結貼在該 PR 留言。正式版：GitHub Release（`CYERPAutoInput_VX.Y.Z.zip`＋SHA-256）。
- OCR 模型 Release：2026-10-11 使用者同意後從 `main` 執行 `CYERPAutoInput OCR Model Mirror`（之後的建置優先從該 Release 取模型）。

## 3. 目前功能（細節見 README）

- 單張 COPI08 自動打單：F5 新增 → 表頭／交易／送貨／發票欄位（課稅別、發票聯數為下拉）→ 商品明細（F2 單位、F2 批號第一筆正庫存；庫存不足提示按確定繼續；設定的折價券／運費／促銷組合品號不查批號）→ 選用 F12 自動儲存並讀回單號 → 選用自動「列印 → 直接列印」（讀「列印」周圍與 ERP 彈出視窗，最多重試 20 秒）→ 設定「儲存單據後自動清除表單」時清空（`ClearFormIfSaved` 唯一入口）。
- 輸入期間忽略實體滑鼠，Esc 立即恢復並緊急停止；輸入法自動切英數；自由文字欄位讀回比對。
- 蝦皮自動擷取輸入：按「開始」自動捲動瀏覽器訂單頁截圖（Home 回頂、像素比對捲動距離、到底即停）→ 依卡片灰色間隔切卡片、只辨識訂單編號／進帳資訊／買家實付金額三張 → 判讀並帶回；有問題時以 CY 框選截圖或 Win+Shift+S＋Ctrl+V 補圖。訂單編號 14 碼檢查、簡體字形摺疊。
- 全自動（V0.3.0，蝦皮選單）：自己開的 Chrome 視窗逐筆 安排出貨 → 寄件單列印（蝦皮店到店／店到宅、7-11、全家各自的列印設定）→ 擷取訂單頁 → ERP，直到沒有待處理訂單；畫面以 UI Automation 讀網頁原文（OCR 為退路）；無流程的物流（萊爾富）與需人工的訂單記下跳過，結束時一次列清單；見 §4。
- MO店+ 匯入（V0.3.0）：「MO店+訂單匯入」視窗放出貨前、出貨後兩份 OrderExport，依訂單編號帶入物流單號後進預覽；附說明。
- 蝦皮自動擷取（設定，原名「蝦皮全自動」）：擷取 → 帶回 → 輸入 ERP → 儲存 → 列印；全程鎖實體滑鼠並在畫面中央顯示不入鏡的「全自動運轉中／按 Esc 中止」，需要使用者確認時立即解除。
- 其他來源：蝦皮官方匯出檔、MO店+ OrderExport（加密 `.xls`，見上）、酷澎 DeliveryList。預覽、單筆、批次（開始前預檢全部）、轉人工、本機防重複紀錄；開始前檢查不通過回到同一個匯入視窗、保留已填內容。統編自動查客戶全名（經濟部 API）。
- 共用設定：折價券／運費／促銷組合品號；貨運別（物流：郵局、大榮、新竹、黑貓、宅配通、嘉里；本公司；超商 6 種）。
- 瀏覽器 F8：開啟蝦皮自動擷取輸入並開始擷取（V0.2.8）。
- 輔助：ERP 狀態大標籤、第一列庫別預設、員工代號（開程式詢問，暫時作法）、清除表單。LOG 一律診斷模式、只存本機 `logs/`（主畫面已無開啟 LOG 的連結）。進階模式隱藏中。

## 4. 實機驗證狀態

已實機通過（使用者回報／LOG）：

- 第一階段（Build 2–11）：F5 新增與新單判斷、表頭／交易／送貨／發票欄位、多列明細與 F2 單位、無批號略過與 F2 批號、F12 自動儲存讀回單號、課稅別／發票聯數下拉、修改狀態標籤、單列與換列庫存不足提示。**第一階段完成（2026-10-10）。**
- 放棄本次新增與開始前提醒、蝦皮匯入預覽與輸入（Build 13）；MO店+ 單筆輸入（Build 17）；輸入中滑鼠鎖定（Build 27）。
- 蝦皮截圖 OCR 單筆輸入（Build 22–31 依實機 LOG 修正誤讀）。
- 蝦皮訂單頁自動擷取與全自動擷取→帶回→輸入→儲存（V0.2.1 Build 4–7）；卡片切割（V0.2.2 Build 1 LOG：8 張卡片標題皆讀到）；24 件長訂單（捲動 10 次）。實測時序：短單捲完到判讀完約 5.4 s；長單進帳卡 3840px 約 13 s。
- V0.2.2 Build 2：使用者回報「目前測試很好」（簡體「訂單金额」修正、直接列印），開始大量實機測試。

尚未實機確認：

- V0.2.3 Build 0（V0.2.4 實測通過）：全自動滑鼠全程鎖定與中央提示（V0.2.4 起改名 `AutomationSession`／`AutomationOverlay`：WS_EX_LAYERED|TRANSPARENT|NOACTIVATE、`SetWindowDisplayAffinity(WDA_EXCLUDEFROMCAPTURE)`，失敗就不顯示）。確認提示不入鏡、轉圈順暢、任何提示視窗前都已解除鎖定。
- V0.2.3 Build 1：儲存後清除設定（批次每張、MO店+ 批次最後一張）；MO店+／酷澎開始前檢查不通過回到匯入視窗並保留內容；批次預檢。
- 「直接列印」後 ERP 是否另跳印表機視窗（目前只記 LOG、不處理）；非 100% DPI 下列印選單與框選截圖位置。
- 蝦皮截圖：套組訂單（規則待使用者補充）。
- 酷澎匯入（多品項格式待使用者取得訂單後確認）；統編查名的商號 API（公司 API 已驗證）。
- 蝦皮匯出檔批次多張、批次失敗放棄後繼續、修改狀態下的放棄確認文字。
- 付款條件、貨運別、發票客戶全名、統一編號覆蓋 ERP 依客戶帶出的值。
- MO店+ ERP 端（負數單價、運費品號、代收貨款、貨運別、統編與發票欄位）；防重複提醒與擋下。
- 非 100% 顯示比例、不同電腦／字型（使用者表示只用 100%）。

- V0.2.4–V0.2.9：已實測通過並隨 Release V0.2.9 發布；逐版內容見 `CHANGELOG.md`。0／O 字寬判斷（V0.2.5）尚未遇到實例，日後有 `order number 0/O` LOG 再核對。

V0.3.0（Build 0–26，未發布）現況：

- 實機已確認（使用者 2026-10-13 LOG）：網頁原文讀取（清單、明細、寄件編號視窗、出貨文件頁、列印視窗）；蝦皮店到店連續 3 筆完成（每筆約 1 分鐘，16:06–16:09），第 4 筆 ERP 儲存列印後停止（原因未查）；MO店+ 匯入視窗以兩份真實匯出檔帶入 2 筆物流單號。
- 未實機確認：7-11 與全家寄件單流程（列印視窗頁面／縮放下拉的實際值）、捲動找下一筆、萊爾富跳過與結束清單、Build 15 起的計時提示、Build 1 明細欄位順序學習（另一台電腦欄位排法）、Build 18 兩項修正、MO 匯入的全家／7-11 單號帶入、Build 19 預覽勾選與結果清單。
- 架構重點：
  - `ShopeeBatchRunner`（流程與操作）＋`ShopeeBatchScreens`（純判讀，有單元測試）。`ReadAsync` 先讀 `ChromePageText`（UI Automation，`CacheRequest`），沒有 Document 才整窗 OCR（`RecognizeLinesAsync` 行模式引擎；逐字模式整窗會在 RapidOCR `GetWordInfo` IndexOutOfRange）。`WaitAsync`：probe 成功、與上一讀相同、無 Loading 才行動。
  - 物流只看訂單明細頁「包裹1」（清單列文字可能截斷）；安排出貨按鈕需完整讀到、與「不成立訂單」間距 ≥3 px、橘色取樣 8 點 ≥3 點。全家先按「申請寄件編號」。寄件單：蝦皮出貨文件頁等到寄件單訂單編號出現才核對（Build 18）；7-11、全家用 Ctrl+P；`PrintDialogAsync` 主動設定頁面與縮放（Home／End＋Enter、輸入後讀回），核對印表機與「1 張紙」。
  - 訂單完成後點頁面左側＋Home、核對訂單頁，再 Ctrl+W 與 F5。ERP 走 `ShopeeScreenshotForm(browser: 自己的視窗)`（`TryPageTextAsync` 先讀網頁原文）→ `StartAutomationAsync(batch: true)`；`EnterShopeeBatchOrderAsync` 回傳 note（記下繼續）或停止（轉人工／未儲存）。
  - 提示：`AutomationSession.Pin/Unpin`＋計時；標題三種（自動輸入中／自動擷取輸入中／全自動運轉中）。
  - ERP 端：`PrimeDetailGridAsync` 以表頭「品號」定位第一列並學欄位順序，列高 22 px；`SetDetailCellAsync` 3 次重試；COPI08 選取與開啟對齊 CYERPAutoQuery（`SelectCopi08`／`ResolveCopi08Async`／`OpenCopi08Async`，2026-10-17，未實機測試）；欄位與明細格以 `PasteText`（EM_SETSEL＋WM_PASTE）貼上，`RunAsync` 結束時 `RestoreClipboard`（2026-10-16，未實機測試）。
  - MO店+：`MoImportForm` 依內容分類（過半訂單有物流單號＝出貨後），`MoOrderImport.FillTracking` 只填單一號碼、其餘列在預覽下方；寄件單 PDF 路線試過後移除（全家寄件單是圖片且沒有 14 碼訂單編號）。說明視窗共用 `HelpForm`。
- 設定：`ShopeeLabelPrinter`、`ShopeeLabelScale` 110、`SevenLabelScale` 100、`FamilyLabelScale` 165、`FamilyLabelPages` "2"、`HctLabelScale` 95、`HctPickupAddress`（只存本機）、發送訊息規則 `ShopeeChatRules`（MO店+／酷澎預留）、滿件贈送 `GiftRules`；Chrome 網頁文字診斷在「設定 › 一般」。
- V0.3.0 Build 26（2026-10-14 打包測試；`cyerp-auto-input/next-wip` 目前與 `cyerp-auto-input/shopee-batch` 同步，作為不跑 CI 的暫存分支）：`ChatRules`（`ChatRule` Keywords／Message、`Problems` 跨則重複關鍵字與半填、`Matches`、`Clean`、`OneLine`，有單元測試）；`UserSettings.ShopeeChatRules`／`MoChatRules`／`CoupangChatRules`，Build 25 的 `ShopeeChatKeywords`／`ShopeeChatMessage` 讀一次後 `MigrateLegacyChat`；`SettingsForm.MessagesPage`＋`ChatRuleEditor`（MO店+／酷澎鎖住），儲存前檢查；`ShopeeBatchChat(Rules)`，`SendChatReplyAsync(messages)` 依序送出多則。 全家：`PrintFamilySlipAsync` 網頁文字 ≤3 行（圖片頁）2 秒後改 `ReadAsync(ocr: true)`，訂單編號用 `ShowsOrderSnLoosely`；`ShipAsync` 申請按鈕分「查看寄件編號內」與「取消右側」兩種各按一次（`ApplyBesideCancel`）。 滿件贈送：`GiftRules`（`GiftRule` ItemCodes（多品號合計）／Every／Gift／GiftItem（空白＝件數最多，同數依卡片順序）／Platforms；`Gifts` 寫在受贈品號第一列，指定品號不在訂單時改送件數最多（使用者 2026-10-14 選 b），寫 LOG；`Problems` 同平台同品號跨卡片（不同平台可各一張）、缺值、指定品號不在卡片內；有單元測試）；`UserSettings.GiftRules`；`SettingsForm.GiftPage`＋`GiftRuleEditor`；`MainForm.LoadOrder` 依 `order.Source` 填 `GiftQuantity`（ERP 端原本就會輸入贈/備品量欄）。 全家裁切列印：`SlipImage`（純邏輯、有單元測試：`FindSlips` 直橫虛線都有才算四格、虛線須細於 8 px；`BarcodeModule`；`Plan` 整數倍放大或整除線寬的縮小；`Render` 純黑白）、`SlipPrinter`（Windows：`Installed` 對應實際印表機名稱、`Label` 由驅動讀 dpi、`LargestPicture` 讀存下的圖檔或網頁內 data: 圖、`Print` 以 `PrintDocument` 1 像素＝1 點置中列印、無進度視窗）；`ShopeeBatchRunner.PrintFamilyCutAsync` 在核對訂單後 Ctrl+S 存到 程式同一層的 `Temp\slip-*`（處理 Chrome 另存新檔對話框，用完刪除），一張、有條碼、裁切圖 OCR 讀得到寄件編號才印，否則回到 Ctrl+P 舊路；送印時出錯則停止並請人確認是否已印。實際蝦皮全家頁存檔會是什麼格式（圖片／網頁／PDF）尚未在實機確認，LOG 有 `family page saved files=` 可查。 Chrome 列印目的地：`PrintDialogAsync` 先檢查目的地，不同時 `ChoosePrinterAsync` 點開清單、OCR 找 `ShopeeBatchScreens.PrinterOption` 點選（最多 2 次）。
- V0.3.0 Build 25：聊聊通知。`ShopeeBatchChat(Keywords, Message)`（設定 `ShopeeChatKeywords`、`ShopeeChatMessage`，只存本機）；`ShipAsync` 讀到 `BuyerNote` 含關鍵字 → `SendChatReplyAsync`（`ChatButton` 關注右側、捲動到可見；`ChatInput`「輸入文字」；`UnicodeText` 訊息；`ChatInputHolds` 確認；Enter；輸入框清空才算送出；`FoldChatAsync` 按 `ChatCollapseButton`（網頁版聊聊右側）或 Esc）→ `BackToOrderTopAsync` → 重讀安排出貨按鈕。失敗不停批次，記 Notes。`runner.Chats` 進結束清單。未實機測試。
- V0.3.0 Build 24：`ApplyTrackingButton` 只取「取消」右側同列者（或查看寄件編號視窗內標題下方唯一一顆）；`ShipAsync` 每顆申請按鈕按一次、最多兩次（2026-10-14 LOG：點到標題 714,440）。`CarrierKind`「店到家」→ Shopee（店到家宅配）。
- V0.3.0 Build 23：新竹物流。`ShopeeBatchCarrier.Hct`（新竹／HCT）；`ShipAsync` 按安排取件後 `ConfirmPickupAsync`：`PickupConfirmButton`（取消右側的確認）、`PickupAddressMatches`（「快遞向你取件的地址」下方卡片含設定 `HctPickupAddress`，空白不計）；不符按取消並 `ShopeeBatchSkip`。`TrackingDialogTitle` 加「快遞取件詳情」（號碼由標題下方 `OtherTrackingPattern` 讀）。寄件單沿用 `PrintShopeeLabelAsync`，號碼無標籤時 `ShowsOrderSn`／`ShowsNumber`（OCR 用 `ShowsOrderSnLoosely`，O／Q／0），縮放 `HctLabelScale` 95。未實機測試：安排取件視窗的網頁文字、快遞取件詳情的號碼、寄件單 PDF 文字。
- V0.3.0 Build 22：Build 21 的 `awb box` LOG：隔日到貨標籤的 PDF 文字層沒有「蝦皮訂單編號」（只有寄件編號、日期、「門市交寄 隔日到貨」、「第 1 頁」頁框、「這份 PDF 無法存取…」）。`PrintShopeeLabelAsync` 等待條件改為 `LabelShown`（「第 N 頁」頁框或標籤字）＋下載完成；文字沒有訂單編號但寄件編號與 `order.Tracking` 相符即通過（使用者：文字快、OCR 慢）；兩者都無法確認時才 `ReadLabelAsync`（頁框裁切、`RecognizeLinesAsync` maxScale 2）以 OCR 取兩個號碼，`SameOrderSn`（O/I/L 對 0/1）。本機以使用者截圖＋PP-OCRv5 mobile 驗證兩號正確。移除 Build 21 的 awb box 傾印。
- V0.3.0 Build 21：`ShopeeBatchScreens.LabelValue`（標籤所在文字自標籤起、同列向右合併、下一行）供 `LabelOrderSn`／`LabelTracking`；`PrintShopeeLabelAsync` 下載完成但讀不到訂單編號時，第一次把 awb 頁 box 寫進 LOG（`awb box`）。原因：2026-10-14 LOG 隔日到貨標籤 18 行仍讀不到（Build 18 的修正對一般店到店有效）；實際版面待下一份 LOG 確認。
- V0.3.0 Build 20：`Program.Main` 以 `Local\CYERPAutoInput` Mutex 只允許一個（self-test／ui-snapshot 不受限），第二個提示後結束。
- V0.3.0 Build 19（使用者 2026-10-13 交代的下一輪項目，已完成、待實機）：`OrderImportForm` 勾選框欄 `Pick`（已儲存 ledger＝唯讀並畫「已輸入過」；HandoffReason＝紅字、預設不勾、原因併入下方提示）、單一「開始輸入」（1 張＝`StartSelected`，多張＝`RunAll`＋`SelectedOrders`，需自動儲存），拿掉轉人工原因／輸入紀錄欄；`BatchSummaryForm`（標題、headline、`BatchSummarySection`、停止說明）取代 MessageBox 與蝦皮全自動的 `ShowBatchSummary`；`MoImportHelpForm` 加 `mo-export-before.png`／`mo-export-after.png`。

後續可能（未排程）：OCR 多引擎平行加速長訂單；CYID 串接。

## 5. 已確認的 ERP 行為（實作依據）

- COPI08 為 Delphi／DevExpress：`TfrmCopi08`、`TDBEdit`、`TcxDBImageComboBox`（內嵌 `TcxCustomComboBoxInnerEdit`）、`TcxPageControl`／`TcxTabSheet`、`TcxGridSite`（明細編輯中為 `TcxCustomInnerTextEdit`）。訊息框為 `TMessageForm`，標題與 COPI08 相同。
- 檢視／輸入狀態由表頭 `TDBEdit` 的唯讀／可寫數量判斷。
- 快捷鍵：檢視狀態 `F5` 新增；編輯狀態 `F12` 儲存。Ribbon「儲存」是群組名稱，群組內含「儲存」與「取消」，點群組中心會按到「取消」。
- 按新增後 ERP 立即帶出今日日期與銷貨單號；交易資料部門代號／業務人員新單必定空白、舊單必定有值。
- 日期輸入 `YYYYMMDD`，離焦後 ERP 轉為 `YYYY/MM/DD`；lookup 欄位必須真的離焦觸發驗證；Unicode 直接輸入比 IME 穩定。銷貨單別以 `End` + `Backspace×4` 清除後重輸；代收貨款／運費 click → type → leave。
- COPI08 欄位的 Win32 文字可能是舊值：新單畫面空白時讀到上一張的貨運別「11」與送貨地址（Build 17 LOG，2026-10-11）。輸入不可依賴「讀到相同值就跳過」。
- ERP 電腦可連外網（使用者 2026-10-11），統編查名使用經濟部商工登記公示資料 API。
- 統一編號離開後 ERP 會檢核；非營利等統編跳出 TMessageForm「統一編號輸入錯誤！」（OK／Cancel，焦點在 OK），屬正常，按 Enter 即可（使用者 2026-10-11 截圖）。
- 明細新增列會沿用上一列庫別；未指定時 ERP 帶預設庫別（可能無庫存）。
- F2 查詢視窗標題 `F2開窗查詢`，每次重新開啟；點到正確列後按 Enter 即選取並關閉。不得假設單位固定在第幾列。
- 是否需要批號：ERP 對無批號品號按 F2 不會開查詢視窗。目前列每格右側都有「…」按鈕，不能當成批號標記。
- F2 批號查詢：第 1 列為篩選列（`=` 與 ABC 圖示）；數字字型為斜線 0；剛開啟時可能尚未繪製。
- 「庫存量或批號量不足！」為 ERP 自繪訊息框（OK／Cancel，焦點預設 OK），文字不在 Win32 控制項，OCR 會把「庫」「號」讀錯。換列時出現；單列單據在 F12 儲存時出現。漏填庫別時按確定後會停在原格。
- 下拉（課稅別、發票聯數）的選項順序見 `Models.cs` 的 `ErpComboOptions`。
- Ribbon 按鈕、明細 cell、F2 表格內容、訊息框文字都沒有暴露給 Win32／MSAA（早期結構探測確認）。Ribbon「列印」為下拉按鈕，選單有「直接列印」「預覽列印」（使用者 2026-10-11 截圖）。
- 蝦皮訂單頁在使用者電腦的 C# OCR：單獨的「1」常被讀成「一」「-」或漏讀，「2」讀成「乙」；商品縮圖上的小字會被讀成數字；劃線原價可能漏讀（Build 23–31 LOG）。
- 放棄本次新增（使用者 2026-10-08／10 說明，Build 13 實作 `AbandonInputAsync`）：焦點須在表頭（不能在表身輸入中）→ 點「客戶代號」→ `Esc` → 「是否放棄本次新增?」（OK／Cancel，預設 OK）→ OK 回到檢視。CY 送出的 Esc 以時間記號讓全域 Esc 監看忽略。修改狀態的確認文字尚未實機確認（比對「放棄」片段）。
- 尚未實作、使用者已說明的 ERP 操作：
  - （Build 13 已實作，見下方）
  - 鍵盤進入明細：在「客戶描述」欄位輸入中按 `Tab` 帶出明細、焦點在數量；再 `Tab` 回欄位選擇，左移 3 格到品號，`Enter` 開始輸入。使用者不確定各電腦是否一致，需實機確認後才可取代「點一次明細區」。

## 6. Roadmap（使用者 2026-10-09 確定）

1. 自動輸入核心穩定 — 2026-10-10 完成。
2. 平台訂單 → 自動打單 — 進行中（蝦皮匯出檔 Build 12、MO店+ Build 14、酷澎 Build 20、蝦皮截圖 OCR Build 22 起）。使用者 2026-10-10 指定對應：銷貨單別、客戶代號為使用者指定的固定值（存本機設定，不寫入 repo）、備註「蝦皮訂單」＋order_sn、送貨地址(一)＝tracking_number、明細由 product_info 的商品選項貨號／數量／價格帶入；有 remark_from_buyer 或 seller_note 時打完單頭（進品號前）停止轉人工。目前的匯出檔由蝦皮官方提供，日後若改用中介平台處理檔案格式會變，到時再調整。
   MO店+（使用者 2026-10-10 指定）：前綴＋訂單編號、另一組單別／客戶代號；商品原廠編號→品號、商品售價→單價；折價券＝Σ售價×數量−Σ開立發票金額，打折價券品號數量 1 單價取負；運費打運費品號並填運費欄；物流商→貨運別用對照表（例：7-11、全家、新竹物流各一個代號，存本機設定）；物流單號＝送貨地址(一)，MO店+ 要匯出後才能取號，所以預覽可手填，可空白但最後列出待回補；代收貨款＝開立發票金額；發票開立統編→統一編號，有統編時客戶全名（收件人）必填，預覽可填客戶全名、發票日期、發票號碼；店家備註轉人工；原廠編號空白或「A + B」組合轉人工（批次略過）；同品號合併；訂單狀態、支付方式、代收金額、應免稅不管。ERP 單價可輸入負數（使用者確認）。讀檔由使用者選擇自己寫解密（不用 Excel COM；CYInvoice 用 Excel COM）。
   防重複（使用者 2026-10-10）：以蝦皮／MO店+ 訂單編號為準；CY 已儲存→擋下（批次略過）；未開自動儲存的單與只打單頭轉人工的單也記錄，但只提醒不擋。待辦：實機驗證。
   MO店+／酷澎全自動（使用者 2026-10-13 規劃，未開始；酷澎等有訂單再做）：AutoInput 一次完成 1 未出貨匯出 → 2 批次出貨取寄件單 PDF → 3 配送中匯出 → 4 呼叫 CYInvoice 開立 → 5 取回發票號碼／日期 → 6 兩檔帶入（已完成）→ 7 ERP → 8 列印寄件單 → 9 清除。決定：
   - 下載檔一律放 AutoInput 自建的暫存資料夾，全部成功後連資料夾刪除；有要人工處理的訂單就保留。
   - CYInvoice：已接上（現況見 `README「MO店+ 發票（CYInvoice）」`）；CYInvoice.exe 位置用檔案選擇器選（使用者 2026-10-14）。
   - 寄件單：某張訂單失敗仍整份列印（出貨一定要印）。全家／7-11 是 A4 切四格，要轉成熱感紙一張一格（解密 PDF → 轉圖 → 切四格去空白 → 逐張列印）；新竹印單前暫停提示人工放紙，按「繼續」才印（暫停不是中止）。印表機細節等下次實單跑一次再定。
   - 網頁端 1–3 步比照蝦皮全自動（自己的 Chrome 視窗、讀網頁原文），待使用者實單帶一次流程。
   MO店+ 網頁操作步驟（使用者 2026-10-14 實機帶；Build 26 已實作為「MO店+ › 全自動」：`ShopeeBatchRunner.Mo.cs`（`RunMoAsync`，與蝦皮共用 Chrome 讀取／點擊／另存新檔 `SaveFromDialogAsync`）、`MoBatchScreens`（純判讀，有測試）、`MainForm.RunMoBatchAsync`（`MoImportForm.Preload`→預覽填發票→按開始後列印（`PreviewAndRunAsync` 的 `beforeErp`，只印一次；使用者 2026-10-14：紙本發票也在這步印）→ERP）；尚未實機跑過，網頁文字的判讀依截圖寫成，待 LOG 校正。使用者 2026-10-14 決定：轉人工的訂單照樣出貨列印、只跳過 ERP；明細不印；批次出貨記錄非全部成功即停止；未登入停下請人登入）：
   1. 開程式自己的 Chrome 新視窗。訂單 › 訂單管理 › E001訂單管理 → 我的訂單「未出貨(n)」，處理提醒點「全部」、配送方式點「全部」。
   2. 「匯出報表」→「匯出提醒」視窗列出 OrderExport_*.xls 已完成 → Chrome 另存新檔：上方路徑欄（Alt+D）切到程式的 Temp 子資料夾再存（同蝦皮全家寄取貨單的做法）→ 關閉匯出提醒。這份是還沒開發票、沒有寄件資訊的初始訂單檔，以 MO 密碼開啟。
   3. 初始訂單檔交 CYInvoice 開立並取回發票資訊——實作在配送中匯出之後、預覽之前（見 §0 B）。
   4. 左側「批次出貨」→「未出貨(n)」，處理提醒「全部」；配送方式按鈕上有 (n) 的才要處理。目前只會有「第三方物流-新竹貨運」「7-11店出」「全家店出」三種，做法相同。
   5. 每種配送方式：點該按鈕 → 勾「已選 0 個訂單」旁的全選框（全選該配送方式所有訂單）→「批次出貨」→「批次出貨（共 n 筆訂單）」頁核對訂單編號、選「併箱出貨」→ 往下按「確認出貨」→「批次出貨中」彈窗按「關閉」。
   6. 右上「批次出貨記錄」：最新一筆就是剛處理的（出貨時間、配送方式、筆數、狀態「全部成功」）→「列印文件」→「注意」視窗：全家、7-11 有「列印標籤」「列印明細」；新竹另有「列印出貨總表」。「列印標籤」與「列印出貨總表」都會跳另存新檔存 PDF（LogisticsLabel_*.pdf 等），同樣切到 Temp 存。所有檔案都用同一個 MO 密碼。
   7. 三種配送方式都出貨、PDF 都存好後（使用者 2026-10-15 更正：最後一種出貨完不再回批次出貨頁，直接進第 8 步；只有還有其他配送方式時才回去）。
   8. 回「我的訂單」，切到「配送中」分頁再按一次「匯出報表」（使用者 2026-10-14 更正：在配送中分頁按），存下出貨後的訂單檔（已有寄件單號）。
   9. 預覽填好發票、按開始後、進 ERP 打單前把該印的文件印完（使用者 2026-10-14／15：紙本發票也在這步）：全家／7-11 標籤 PDF 切成熱感標籤；新竹寄件單與到廠取貨總表印在 A4 印表機（設定「A4 印表機」，目前是 Brother），各自先跳超大字「請在印表機放入寄件單用紙」／「請在印表機放入取貨總表用紙」，按「好了」才印（`PaperPromptForm`、`MoSlipPdf.RenderPagesAsync`、`SlipPrinter.PrintPages`；使用者 2026-10-14 以 Temp\test1 測試通過）；總表全部印過才不重印、部分印過照印並提醒作廢舊總表（`MoPickupSummary.Decide`、`PickupSummaryLedger`，Data\pickup_summaries.json 保留 7 天）。
   印過的文件保留 7 天供「重新列印寄件單」（`MoPrintedDocuments`、`MoReprintForm`；使用者 2026-10-14：不追蹤印表機狀態，出錯由人發現後重印）。
   10. 接上已完成的 MO店+ 兩檔流程（初始訂單檔＋出貨後訂單檔帶物流單號，加上 CYInvoice 的發票資訊）→ ERP 打單＋列印 → 全部成功後刪 Temp 子資料夾。
   寄件單 PDF：MO 全家 PDF 每頁是一張 2615×4271 原圖（與蝦皮全家同一種四格版面）；MO 7-11 PDF 是向量頁面、A4 左上一張實線外框，依外框切、以印表機 dpi 原尺寸印（`PrintMoSlipsAsync`）。
   A4 列印專用紙的寄件單（使用者 2026-10-14 交代記下，未開始）：
   - 蝦皮「黑貓」與 MO店+「新竹物流」的寄件單要印在 A4 列印專用紙上，不是熱感標籤紙（蝦皮全自動目前遇到黑貓會跳過轉人工）。
   - 現階段全自動執行時人都在電腦前：輪到這類寄件單時發出提示（主機沒接喇叭，嗶聲之外要有工作列閃爍、置頂視窗），**暫停**等人把 A4 專用紙放進印表機，按「繼續」才印；暫停不是中止，按 Esc 才停止。
   - 未來做成無人值守全自動時（Roadmap 5）再決定：擺多紙匣印表機、程式自動選紙匣（熱感／A4 分開），或遇到這類寄件單就跳過轉人工。
3. 串接 CYID（依 REPO_POLICY §4.1 與 CYID consumer 標準）。TODO：員工代號改為登入的員工代號。暫時作法（使用者 2026-10-11）：開程式時詢問員工代號、本次開啟期間使用、不存檔。
4. CYWEB 訂單工單系統完成後，撈單自動打單並寄信通知。
5. 辨識查詢單據，完成地端無人值守（專用電腦自動登入、不鎖定、ERP 異常處理、心跳與通知）。

版本：第一個正式版 `V1.0.0` 的時點待使用者依上述階段決定（2026-10-06 原定為「Excel 匯入完成」，已由本 roadmap 取代）。

背景決策（2026-10-06，仍有效）：

- SMART ERP 沒有銷貨單匯入功能，模擬操作是唯一路線；不碰 SQL（合約問題）。
- 所有來源先轉成同一份標準銷貨單資料，再走同一條「驗證 → ERP 輸入／儲存 → 結果」路徑；新增來源只新增 adapter。
- 批次中單據失敗時可放棄 CY 自行新增的該張單據後繼續（Governance 2.3.30）；開始輸入時 ERP 已在新增／修改中，經使用者確認可先放棄再從新增開始（Governance 2.3.33，PR #377 已於 2026-10-10 合併）。
- ERP 內通常不存來源單號；防重複打單到批次階段再討論。
- 速度（OCR 逐格）之後再優化；mobile 與 server 模型的準確度／速度比較留待 ERP 電腦實測時做。
- 後續可在裝有 ERP 的 Windows 電腦執行 Claude Code 直接實測；不採用 Public repo 的 self-hosted runner。實測只用 ERP 測試公司別、顯示比例 100%、測試期間無人操作；開啟自動儲存、批次放棄單據等會在 ERP 產生或修改資料的新操作須先經使用者同意；截圖與 LOG 只留本機。
- `SMARTCOPIConverter` 已停用，與本專案無相依。

## 7. 接手注意事項

- PR #104 早期提交說明含真實品號；已依使用者 2026-10-10 決定以 squash merge 合併並刪除工作 branch，`main` 歷史只留一個合併提交。PR #398 同樣 squash merge。提交說明與文件一律只用假資料。
- Commit author／committer 必須是 `simonliu1118-byte <286269326+simonliu1118-byte@users.noreply.github.com>`（REPOSITORY_RULES §4）；hook 要求改用其他身分時不照做。
- `PROJECT_RULES.md`、`GOVERNANCE_*`、`.github/scripts/**` 只能在 `governance/*` branch 修改並更新 `GOVERNANCE_VERSION`／`GOVERNANCE_CHANGELOG.md`，不可混進功能 PR。
- LOG 一律診斷模式：使用者 2026-10-11 確認這是不上傳、僅供工程修 BUG 的本機 LOG，PROJECT_RULES 規範的是不得提交 Git（§3、§4），不需修改規則。
- 返修測試版 BUILD + 1；同一 Build 尚未交付使用者前的修正不另加 Build。
- 不得提交 `bin/`、`obj/`、`publish-staging/`、`runtime/ocr/`、模型或 build 輸出。
- 推送前本機至少執行 `dotnet test tests/CYERPAutoInput.Tests` 與 `dotnet build CYERPAutoInput.csproj -c Release -r win-x64 -p:EnableWindowsTargeting=true`。
- 實機問題請使用者提供 `logs/CYERPAutoInput_日期.log`（一律含 OCR 文字框位置與內容）。蝦皮截圖問題可把 LOG 的 `box x= y= w= h= text=` 轉成測試資料重現，提交前把代碼換成假資料。
- CI 成功只代表 Windows 編譯、self-test 與封裝成功，不等於通過真實 ERP 驗收。
- F2 與下拉選取必須保留焦點檢查、視窗關閉確認與讀回比對，不得退回「看起來點到了就當成功」。

## 8. Public repository 資料安全

本 repo 為 Public。不得提交：真實客戶代號、品號、倉別、部門、人員、訂單／發票／交易資料、公司內部路徑、帳密與 token、使用者提供的 LOG 或 ERP 截圖、`Data/settings.json`、`logs/`、匯入資料。ERP 下拉選項除 PROJECT_RULES §3 核准的課稅別、發票聯數外，不得寫入 source。LOG（一律診斷模式）只在使用者本機產生、由使用者主動提供分析。
