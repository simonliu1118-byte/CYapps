# CYERPAutoInput WORK HANDOFF

> 更新：2026-10-10（V0.2.0 Build 12）  
> 協作方式（使用者 2026-10-06 指定）：Claude Code 主控；Claude 額度不足時由 Codex 接手，Claude 額度恢復後檢查確認 Codex 的變更。交接以本文件＋Git 狀態為準。  
> 本文件只記錄目前狀態、已驗證事實、待辦與交接注意事項，不是永久規則來源。逐版變更見 `CHANGELOG.md`，功能說明見 `README.md`。

## 1. 接手時先讀

1. `/REPOSITORY_RULES.md`
2. `/REPO_POLICY.md`
3. `/apps/CYERPAutoInput/PROJECT_RULES.md`（目前 Governance 2.3.32）
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

單張 COPI08 自動打單：F5 新增 → 表頭／交易／送貨／發票欄位（課稅別、發票聯數為下拉）→ 商品明細（F2 單位、F2 批號第一筆正庫存；庫存不足提示按確定繼續）→ 選用 F12 自動儲存並讀回單號。蝦皮官方匯出檔匯入（預覽、單張載入、批次依序輸入、有備註只打單頭轉人工）。輔助功能：ERP 狀態大標籤、診斷模式、第一列庫別預設、ERP 結構探測、清除表單、開啟 LOG。進階模式隱藏中。

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

尚未實機確認：

- 蝦皮匯入（Build 12）全部：讀檔與預覽、單張載入、批次依序輸入與結果清單、有備註只打單頭轉人工、送貨地址(一) 輸入（此欄位首次使用）。
- 非 100% 顯示比例、不同電腦／字型（使用者表示只用 100%）。

## 5. 已確認的 ERP 行為（實作依據）

- COPI08 為 Delphi／DevExpress：`TfrmCopi08`、`TDBEdit`、`TcxDBImageComboBox`（內嵌 `TcxCustomComboBoxInnerEdit`）、`TcxPageControl`／`TcxTabSheet`、`TcxGridSite`（明細編輯中為 `TcxCustomInnerTextEdit`）。訊息框為 `TMessageForm`，標題與 COPI08 相同。
- 檢視／輸入狀態由表頭 `TDBEdit` 的唯讀／可寫數量判斷。
- 快捷鍵：檢視狀態 `F5` 新增；編輯狀態 `F12` 儲存。Ribbon「儲存」是群組名稱，群組內含「儲存」與「取消」，點群組中心會按到「取消」。
- 按新增後 ERP 立即帶出今日日期與銷貨單號；交易資料部門代號／業務人員新單必定空白、舊單必定有值。
- 日期輸入 `YYYYMMDD`，離焦後 ERP 轉為 `YYYY/MM/DD`；lookup 欄位必須真的離焦觸發驗證；Unicode 直接輸入比 IME 穩定。銷貨單別以 `End` + `Backspace×4` 清除後重輸；代收貨款／運費 click → type → leave。
- 明細新增列會沿用上一列庫別；未指定時 ERP 帶預設庫別（可能無庫存）。
- F2 查詢視窗標題 `F2開窗查詢`，每次重新開啟；點到正確列後按 Enter 即選取並關閉。不得假設單位固定在第幾列。
- 是否需要批號：ERP 對無批號品號按 F2 不會開查詢視窗。目前列每格右側都有「…」按鈕，不能當成批號標記。
- F2 批號查詢：第 1 列為篩選列（`=` 與 ABC 圖示）；數字字型為斜線 0；剛開啟時可能尚未繪製。
- 「庫存量或批號量不足！」為 ERP 自繪訊息框（OK／Cancel，焦點預設 OK），文字不在 Win32 控制項，OCR 會把「庫」「號」讀錯。換列時出現；單列單據在 F12 儲存時出現。漏填庫別時按確定後會停在原格。
- 下拉（課稅別、發票聯數）的選項順序見 `Models.cs` 的 `ErpComboOptions`。
- Ribbon 按鈕、明細 cell、F2 表格內容、訊息框文字都沒有暴露給 Win32／MSAA（結構探測確認）。
- 尚未實作、使用者已說明的 ERP 操作：
  - 放棄本次新增：焦點須在表頭（不能在表身輸入中）→ 點「客戶代號」→ `Esc` → 「是否放棄本次新增」預設焦點在確定 → 確定。實作時須先暫停 CY 自己的全域 Esc 監看（`GetAsyncKeyState`），且只限 PROJECT_RULES §1 的批次放棄例外。
  - 鍵盤進入明細：在「客戶描述」欄位輸入中按 `Tab` 帶出明細、焦點在數量；再 `Tab` 回欄位選擇，左移 3 格到品號，`Enter` 開始輸入。使用者不確定各電腦是否一致，需實機確認後才可取代「點一次明細區」。

## 6. Roadmap（使用者 2026-10-09 確定）

1. 自動輸入核心穩定 — 2026-10-10 完成。
2. 蝦皮訂單匯出檔 → 自動打單 — 進行中（Build 12）。使用者 2026-10-10 指定對應：銷貨單別、客戶代號為使用者指定的固定值（存本機設定，不寫入 repo）、備註「蝦皮訂單」＋order_sn、送貨地址(一)＝tracking_number、明細由 product_info 的商品選項貨號／數量／價格帶入；有 remark_from_buyer 或 seller_note 時打完單頭（進品號前）停止轉人工。目前的匯出檔由蝦皮官方提供，日後若改用中介平台處理檔案格式會變，到時再調整。待辦：實機驗證；批次中失敗單據自動放棄後接續（PROJECT_RULES §1 例外，需先實作「放棄本次新增」操作，見 §5）；防重複打單（同一 order_sn 已打過的判斷）。
3. 串接 CYID（依 REPO_POLICY §4.1 與 CYID consumer 標準）。TODO：員工代號改為登入的員工代號（目前取表單／本機預設值）。
4. CYWEB 訂單工單系統完成後，撈單自動打單並寄信通知。
5. 辨識查詢單據，完成地端無人值守（專用電腦自動登入、不鎖定、ERP 異常處理、心跳與通知）。

版本：第一個正式版 `V1.0.0` 的時點待使用者依上述階段決定（2026-10-06 原定為「Excel 匯入完成」，已由本 roadmap 取代）。

背景決策（2026-10-06，仍有效）：

- SMART ERP 沒有銷貨單匯入功能，模擬操作是唯一路線；不碰 SQL（合約問題）。
- 所有來源先轉成同一份標準銷貨單資料，再走同一條「驗證 → ERP 輸入／儲存 → 結果」路徑；新增來源只新增 adapter。
- 批次中單據失敗時可放棄 CY 自行新增的該張單據後繼續（Governance 2.3.30）。
- ERP 內通常不存來源單號；防重複打單到批次階段再討論。
- 速度（OCR 逐格）之後再優化；mobile 與 server 模型的準確度／速度比較留待 ERP 電腦實測時做。
- 後續可在裝有 ERP 的 Windows 電腦執行 Claude Code 直接實測；不採用 Public repo 的 self-hosted runner。實測只用 ERP 測試公司別、顯示比例 100%、測試期間無人操作；開啟自動儲存、批次放棄單據等會在 ERP 產生或修改資料的新操作須先經使用者同意；截圖與 LOG 只留本機。
- `SMARTCOPIConverter` 已停用，與本專案無相依。

## 7. 接手注意事項

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
