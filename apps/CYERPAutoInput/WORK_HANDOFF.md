# CYERPAutoInput WORK HANDOFF

> 更新：2026-10-10（V0.2.0 Build 30）  
> 協作方式（使用者 2026-10-06 指定）：Claude Code 主控；Claude 額度不足時由 Codex 接手，Claude 額度恢復後檢查確認 Codex 的變更。交接以本文件＋Git 狀態為準。  
> 本文件只記錄目前狀態、已驗證事實、待辦與交接注意事項，不是永久規則來源。逐版變更見 `CHANGELOG.md`，功能說明見 `README.md`。

## 1. 接手時先讀

1. `/REPOSITORY_RULES.md`
2. `/REPO_POLICY.md`
3. `/apps/CYERPAutoInput/PROJECT_RULES.md`（目前 Governance 2.3.33）
4. 本文件 → `README.md` → `CHANGELOG.md`
5. PR #104 與工作 branch 的實際 source

不要以舊聊天記憶覆蓋目前 Git 狀態。

## 2. Branch／PR／版本

- Repository：`simonliu1118-byte/CYapps`，專案：`apps/CYERPAutoInput/`
- `main`：仍是舊 Go 線（`V0.0.10` Build 19）。舊 Go optical prototype PR #103 已關閉。
- 工作 branch：`cyerp-auto-input/v0.1.0-csharp`（名稱沿用以保留 PR #104）；Draft PR #104。
- VERSION `0.2.0`，BUILD 見 `BUILD`。`V0.1.0` 為 Codex C# 重寫（Build 1–27），`V0.2.0` 為使用者指定的 Minor，代表 Claude Code 接手。
- 技術棧：C# / .NET 8 / WinForms；OCR 為本機 PaddleOCR PP-OCRv5 mobile + ONNX Runtime（CPU）。由 Go 改寫是使用者同意的技術棧變更（Go 線讀不到 DevExpress virtual grid 的 cell 文字）。
- 測試包：每次 push 由 PR #104 CI 產生 Artifact（保留 3 天），連結貼在 PR #104 留言。
- 第一階段完成前 PR #104 維持 Draft；合併 `main` 後需從 `main` 手動執行一次 `CYERPAutoInput OCR Model Mirror` 建立模型 Release。

## 3. 目前功能（細節見 README）

單張 COPI08 自動打單：F5 新增 → 表頭／交易／送貨／發票欄位（課稅別、發票聯數為下拉）→ 商品明細（F2 單位、F2 批號第一筆正庫存；庫存不足提示按確定繼續）→ 選用 F12 自動儲存並讀回單號。蝦皮官方匯出檔與 MO店+ OrderExport（加密 `.xls`）匯入：預覽（MO店+ 可填物流單號與發票欄位）、單筆輸入、批次依序輸入、轉人工、本機防重複紀錄。輔助功能：ERP 狀態大標籤、診斷模式、第一列庫別預設、ERP 結構探測、清除表單、開啟 LOG。進階模式隱藏中。

## 4. 實機驗證狀態

已實機通過（使用者回報／LOG）：

- F5 新增與新單判斷（部門代號／業務人員空白）— Build 2 起。
- 表頭、交易資料、送貨資料、發票資料欄位輸入。
- 多列明細、F2 單位選取。
- 無批號品號略過（F2 無查詢視窗）；F2 批號跳過存量 0、選第一筆正庫存 — Build 7。
- F12 自動儲存並讀回同一單號 — Build 7／8 LOG（`saved and verified`）。
- 課稅別、發票聯數下拉選取正確 — Build 10。
- 狀態標籤顯示「修改」— Build 10。
- 單列單據 F12 儲存時出現庫存不足提示，按確定後完成儲存並列出待確認 — Build 10。
- 換列時庫存不足提示按確定後繼續打完並列出待確認 — Build 11。**第一階段完成（2026-10-10）。**
- 放棄本次新增與開始前提醒、蝦皮匯入預覽與輸入 — Build 13（使用者 2026-10-10 回報「看起來沒什麼問題」）。

尚未實機確認：

- Build 30：使用者需求（2026-10-11）：工具列放大（`ToolbarButtonHeight`=38）；`CoupangButton` 逐字量寬連排；移除 `ErpProbe.cs`、尋找 ERP、主畫面「?」、`DiagnosticLogging`（`_log.Diagnostic = true`）；`FreightCarriers.Own`＋`Logistics` 擴充、`Aliases`（郵局↔中華郵政）。
- Build 29：使用者需求（2026-10-11，跟下一版一起發）：`UserSettings.AutoPrint`；`ErpAutomationService.PrintDocumentAsync`（Ribbon 上方 220px OCR「列印」→ 點 label → 下方 360×200 OCR「直接列印」→ 點）；失敗 `PrintProblem`，Esc 關選單。待實機：split button 點 label 是否開選單、DPI 下擷取範圍。
- Build 28：Build 27 實測 LOG（診斷模式 box）：編號 "-"／"乙" 誤讀、縮圖 "5" 被當編號。`TryIndexDigits` 字形對照；`IndexAnchors` 要求 `貨號行.Left − CenterX ≥ 4×字高`。實機：滑鼠鎖定 blocked_events=233，正常解除。
- Build 27：使用者需求（2026-10-11）：`PhysicalMouseLock`（WH_MOUSE_LL 丟棄非 injected 事件；同執行緒 WH_KEYBOARD_LL 實體 Esc 即解除，不依賴 UI 執行緒），包住 `RunAsync`／`AbandonInputAsync`，對話框出現前已解除。截圖帶入無 notes 不跳視窗。`StartAutomationAsync` 完成且無 warnings／notes／SaveSkippedReason 時 `ResetForm()`。待實機：滑鼠鎖定與 Esc 解除。
- Build 26：Build 25 實測（使用者 2026-10-11）：(1) 劃線原價漏讀 → 原價合計 3716≠3800，促銷組合被擋；群組只讀到一個小計且 Σ單價×數量 > 小計、≤ 2 倍時以 Σ 為原價。(2) 有表頭的截圖仍出現「第 5 項讀不到小計」；`IndexAnchors` 改為須在表頭下方且與小計欄數字垂直對齊（無小計欄時需靠近貨號行）。診斷模式記錄 OCR 框（`  box x= y= w= h= text=`）。
- Build 25：Build 24 實測（使用者 2026-10-11）：三組多品項促銷訂單明細與促銷組合正確，但跳「第 4 項讀不到小計」；縮圖小字被讀成編號。`ShopeeScreenshot.IndexAnchors` 只取最左欄（表頭「編」或最小 x，±15px）。
- Build 24：Build 23 實測（使用者 2026-10-11）：明細截圖無表頭＋數量「1」漏讀 → `probes=0`，小計欄被當數量，促銷組合 = 450×450−450。`ShopeeScreenshot.ResolveColumns` 統一欄位判定（表頭優先；兩欄時以欄距 >7.5 字高或劃線原價判定右欄為小計）；仍缺數量時單一商品編號以小計÷單價推算；Σ單價×數量 ≠ Σ小計原價時不加促銷組合列。
- Build 23：Build 22 實測（使用者 2026-10-11）：C# 偵測漏掉明細第一列的編號「1」與數量「1」（Python 同模型讀得到），改為缺格裁切重讀（recognizer-only）；LOG `detail cells re-read probes= recovered=`。
- Build 22：蝦皮截圖輸入（OCR）。開發環境以使用者提供的 5 組真實截圖驗證解析（含促銷組合多商品、套組 x2／x3、蝦幣、OCR 把「貨」讀成「货」、「選」讀成「项」、0 讀成 O）；實機 OCR 走 C# RapidOCRSharp，字框粒度可能不同，以 MergeLines 合併。套組規則使用者表示之後有訂單再補充。員工代號開程式詢問。
- Build 21：統編查名（經濟部公司 API 已在開發環境以公開大公司統編驗證可回傳名稱；商號 API 當時網站忙碌未驗證，欄位依官方名稱 President_No／Business_Name，查無即留空）、客戶全名不強制、設定 Enter 跳欄、貨運別排序。
- Build 20：酷澎匯入（未實機）。使用者規則 2026-10-11：單價先用銷售價格、免運時運費為 0（樣本少，有問題再改）；應開立予酷澎之發票金額、配送訊息、捆綁配送忽略；沒有統編時統一編號欄空白；預覽可填發票日期／號碼。多品項格式待使用者取得更多訂單後上傳確認。
- Build 19：按鈕統一 2px 圓角（使用者 2026-10-11 選擇「全部統一 2px」）。設定分頁重整、折價券／運費品號與貨運別改共用、貨運別 9 種（使用者 2026-10-11 指定名單）。
- Build 18：日期選擇器、預覽發票日期自動加「/」、表頭欄寬修正、按鈕 6px 圓角、匯入預覽欄寬列高固定。貨運別設定改固定三列；欄位讀到與表單相同的值也重打（實測 ERP 畫面空白但 Win32 讀到舊值「11」，CY 跳過導致沒輸入）。
- Build 17 實測（使用者 2026-10-11）：MO店+ 單筆輸入完成，狀態列「輸入完成」，表單帶入備註中文、送貨地址(一)、發票日期與號碼。
- Build 17：輸入前關閉 ERP 欄位的中文輸入法、自由文字欄位讀回比對（Build 16 前實測：備註欄出現輸入法組字虛線，字被帶到送貨地址(一)；使用者截圖確認）。
- Build 16：送貨地址(一) 覆蓋 ERP 既有內容（Build 15 實測：ERP 畫面空白但 CY 讀到內容而停止，原因未明；請以診斷模式 LOG 的 `replacing ERP value key=ship_addr1 … codes=` 確認讀到什麼）。
- Build 15：工作列圖示已實測正常（2026-10-11）；介面重排、明細 10 列無捲軸與固定欄寬列高、運費品號自動回填運費欄。
- Build 14 實測（使用者 2026-10-10）：MO店+ 單筆輸入，CY 狀態列顯示輸入完成（自動儲存關）；表單帶入品號、負數折價券列、代收貨款、貨運別。ERP 端各欄是否正確尚未回報。

- 蝦皮批次多張連續輸入、批次失敗放棄後繼續、修改狀態下的放棄確認文字。
- 付款條件、貨運別、發票客戶全名、統一編號覆蓋 ERP 依客戶帶出的值（Build 14；後三者 ERP 是否會自動帶出未知）。
- MO店+ 匯入全流程（Build 14）：真實匯出檔解密與解析已在開發環境以使用者提供的兩個檔案逐格比對 msoffcrypto＋xlrd 一致、18 張訂單金額核對全過（檔案只留本機）；ERP 端（負數單價、運費品號、代收貨款、貨運別、統編與發票欄位）未實測。
- 防重複紀錄的提醒與擋下（Build 14）。
- 蝦皮批次略過有備註的訂單並列為需人工處理（Build 14）。
- 非 100% 顯示比例、不同電腦／字型（使用者表示只用 100%）。

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
- Ribbon 按鈕、明細 cell、F2 表格內容、訊息框文字都沒有暴露給 Win32／MSAA（結構探測確認）。
- 放棄本次新增（使用者 2026-10-08／10 說明，Build 13 實作 `AbandonInputAsync`）：焦點須在表頭（不能在表身輸入中）→ 點「客戶代號」→ `Esc` → 「是否放棄本次新增?」（OK／Cancel，預設 OK）→ OK 回到檢視。CY 送出的 Esc 以時間記號讓全域 Esc 監看忽略。修改狀態的確認文字尚未實機確認（比對「放棄」片段）。
- 尚未實作、使用者已說明的 ERP 操作：
  - （Build 13 已實作，見下方）
  - 鍵盤進入明細：在「客戶描述」欄位輸入中按 `Tab` 帶出明細、焦點在數量；再 `Tab` 回欄位選擇，左移 3 格到品號，`Enter` 開始輸入。使用者不確定各電腦是否一致，需實機確認後才可取代「點一次明細區」。

## 6. Roadmap（使用者 2026-10-09 確定）

1. 自動輸入核心穩定 — 2026-10-10 完成。
2. 平台訂單匯出檔 → 自動打單 — 進行中（蝦皮 Build 12 起，MO店+ Build 14）。使用者 2026-10-10 指定對應：銷貨單別、客戶代號為使用者指定的固定值（存本機設定，不寫入 repo）、備註「蝦皮訂單」＋order_sn、送貨地址(一)＝tracking_number、明細由 product_info 的商品選項貨號／數量／價格帶入；有 remark_from_buyer 或 seller_note 時打完單頭（進品號前）停止轉人工。目前的匯出檔由蝦皮官方提供，日後若改用中介平台處理檔案格式會變，到時再調整。
   MO店+（使用者 2026-10-10 指定）：前綴＋訂單編號、另一組單別／客戶代號；商品原廠編號→品號、商品售價→單價；折價券＝Σ售價×數量−Σ開立發票金額，打折價券品號數量 1 單價取負；運費打運費品號並填運費欄；物流商→貨運別用對照表（例：7-11、全家、新竹物流各一個代號，存本機設定）；物流單號＝送貨地址(一)，MO店+ 要匯出後才能取號，所以預覽可手填，可空白但最後列出待回補；代收貨款＝開立發票金額；發票開立統編→統一編號，有統編時客戶全名（收件人）必填，預覽可填客戶全名、發票日期、發票號碼；店家備註轉人工；原廠編號空白或「A + B」組合轉人工（批次略過）；同品號合併；訂單狀態、支付方式、代收金額、應免稅不管。ERP 單價可輸入負數（使用者確認）。讀檔由使用者選擇自己寫解密（不用 Excel COM；CYInvoice 用 Excel COM）。
   防重複（使用者 2026-10-10）：以蝦皮／MO店+ 訂單編號為準；CY 已儲存→擋下（批次略過）；未開自動儲存的單與只打單頭轉人工的單也記錄，但只提醒不擋。待辦：實機驗證。
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

- PR #104 有兩個早期提交說明含真實品號（使用者 2026-10-10 決定）：合併時用 squash merge，合併後刪除 `cyerp-auto-input/v0.1.0-csharp` 分支，一併清除。之後的提交說明與文件一律只用假資料。
- Commit author／committer 必須是 `simonliu1118-byte <286269326+simonliu1118-byte@users.noreply.github.com>`。
- `PROJECT_RULES.md`、`GOVERNANCE_*`、`.github/scripts/**` 只能在 `governance/*` branch 修改並更新 `GOVERNANCE_VERSION`／`GOVERNANCE_CHANGELOG.md`，不可混進 PR #104。
- 返修測試版 BUILD + 1；同一 Build 尚未交付使用者前的修正不另加 Build。
- 不得提交 `bin/`、`obj/`、`publish-staging/`、`runtime/ocr/`、模型或 build 輸出。
- 推送前本機至少執行 `dotnet test tests/CYERPAutoInput.Tests` 與 `dotnet build CYERPAutoInput.csproj -c Release -r win-x64 -p:EnableWindowsTargeting=true`。
- 實機問題請使用者開啟診斷模式重現並提供 `logs/CYERPAutoInput_日期.log`；非診斷模式下 OCR 文字只記長度。
- CI 成功只代表 Windows 編譯、self-test 與封裝成功，不等於通過真實 ERP 驗收。
- F2 與下拉選取必須保留焦點檢查、視窗關閉確認與讀回比對，不得退回「看起來點到了就當成功」。

## 8. Public repository 資料安全

本 repo 為 Public。不得提交：真實客戶代號、品號、倉別、部門、人員、訂單／發票／交易資料、公司內部路徑、帳密與 token、使用者提供的 LOG 或 ERP 截圖、`Data/settings.json`、`logs/`、匯入資料。ERP 下拉選項除 PROJECT_RULES §3 核准的課稅別、發票聯數外，不得寫入 source。診斷 LOG 只在使用者本機產生、由使用者主動提供分析。
