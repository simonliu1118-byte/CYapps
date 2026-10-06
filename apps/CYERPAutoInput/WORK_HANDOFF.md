# CYERPAutoInput WORK HANDOFF

> 更新：2026-10-06（V0.2.0）  
> 目的：供後續 AI 對話直接接手目前開發狀態。  
> 協作方式（使用者 2026-10-06 指定）：Claude Code 主控；Claude 額度不足時由 Codex 接手，Claude 額度恢復後檢查確認 Codex 的變更。交接一律以本文件 + Git 狀態為準。  
> 本文件只記錄「目前狀態、已驗證事實、待辦與交接順序」，不是永久規則來源。

## 1. 接手時先讀

依 repository 治理順序：

1. `/REPOSITORY_RULES.md`
2. `/REPO_POLICY.md`
3. `/apps/CYERPAutoInput/PROJECT_RULES.md`
4. 本 `WORK_HANDOFF.md`
5. `README.md`
6. PR #104 與目前工作 branch 的實際 source

不要依舊聊天記憶覆蓋目前 Git 狀態。


## 2. Repository / branch / PR 現況

Repository：`simonliu1118-byte/CYapps`，專案：`apps/CYERPAutoInput/`

### 正式 main

`main` 仍是舊 Go 線（VERSION `0.0.10` / BUILD `19`），尚未切到 C#。舊 Go V0.0.11 optical prototype PR #103 已關閉、未合併。

### 現行開發線

- Branch：`cyerp-auto-input/v0.1.0-csharp`
- Draft PR：`#104 CYERPAutoInput V0.1.0: C# rewrite with optical ERP targeting`
- VERSION：`0.2.0`；BUILD：見同目錄 `BUILD` 檔（2026-10-06 升 Minor 時歸 `0`）。
- 版本沿革：`V0.1.0` 為 Codex C# 重寫（Build 1–27）；`V0.2.0` 為使用者指定的 Minor，代表 Claude Code 接手後的版本（含原 V0.1.0 Build 26／27 的變更）。Branch 名稱沿用 `cyerp-auto-input/v0.1.0-csharp` 以保留 PR #104。
- 技術棧：C# / .NET 8 / WinForms；OCR 為本機 PaddleOCR PP-OCRv5 mobile + ONNX Runtime（CPU），詳見 `README.md`。
- 本次升 Minor 是使用者已明確同意的技術棧重寫。
- PR 仍為 Draft；未完成真實 ERP 驗收前不得合併 main。

## 3. 為什麼由 Go 改寫 C#

舊 Go 線已證明一般 COPI08 Win32 輸入可行，但在 DevExpress virtual grid / F2 單位查詢遇到核心限制：

- `TcxGridSite` 的可見 cell 文字通常不是獨立 HWND text。
- `GetWindowText` / child control text 無法可靠讀到使用者肉眼看見的單位 cell。
- 舊 Build 18 的實機 LOG 明確顯示：F2 已開啟、焦點在 `TcxGridSite`，但 40 次 readback 仍找不到指定單位，因此舊流程在送 Enter 之前就因 `UNIT_LOOKUP_FAILED` 停止。
- 使用者已人工確認真正 ERP 行為：F2 開啟後，人工點選正確單位列，再按實體 Enter，F2 會正常關閉並完成選取。

因此 V0.1.0 改採 C#，讓 WinForms UI、Win32 interop、本機 OCR 與畫面定位集中在單一 Windows 原生開發線。


## 4. C# 版已完成架構

目前實際流程、UI、OCR、F2 單位、F2 批號、本機資料與安全設計以 `README.md` 為準，此處只列交接重點：

- 欄位有內容才送入 ERP；明細有資料的列必須有「品號 + 數量」。
- 表頭 / 交易 / 送貨 / 發票使用 Win32 control + ERP 原生焦點、離焦、Enter／Tab。
- 商品明細只點一次 `TcxGridSite` 建立第一列，再以目前 Grid 幾何 + OCR 定位 cell。
- F2 單位：以 OCR 定位「換算單位」欄中指定單位，點選後送一次 Enter，並確認 F2 關閉。
- F2 批號：明確空白不開 F2；有標記才開；查詢內只選由上往下第一筆可確認 `現有存量 > 0` 的批號，無法確認就停止（使用者已確認此業務規則）。
- 銷貨單號由 ERP 產生，CY 只讀取並驗證 `YYYYMMDDXXX` 格式。
- 完成後停在 ERP，**不自動儲存**；全域 Esc 只停止 CY，不按 ERP「取消」；不送 `Ctrl+A`。
- 仍需人工確認的狀況累積在 `AutomationRunResult.Warnings`，完成時統一顯示筆數。
- V0.2.0 Build 1：設定視窗「ERP 結構探測」（`ErpProbe`，唯讀、有界）：輸出 COPI08 與其他可見 ERP 視窗（含 F2 查詢）的 Win32 控制項樹與 MSAA 樹（含鍵盤快捷鍵）到本機 `logs/erp-probe_*.txt`；非診斷模式時可能含單據資料的文字只記長度（`ProbeRedaction`）。目的：使用者 2026-10-06 指示尋找比座標／OCR 更好的做法；不碰 SQL（使用者：有合約問題）。
- Build 27（使用者 2026-10-06 指示「先把介面和基礎自動打單完成」）：
  - 自動儲存（設定開關，預設關閉）：只限 CY 新增或開始時單號為空的單據，且無需人工確認項目；以 Ribbon「儲存」精確文字定位（Win32 caption → Ribbon OCR，不做全視窗搜尋）；儲存後須回到檢視並讀回同一單號，出現任何 ERP 訊息視窗即停止。實機尚未驗證。
  - 工具列「清除表單」；狀態列「開啟 LOG 資料夾」；完成後需人工確認／未自動儲存原因以 MessageBox 列出。
  - 設定視窗不顯示 App icon（視覺準則 §11.1）。
  - CI 以 `--ui-snapshot` 產生介面 PNG Artifact 供版面檢查。
- Build 26（使用者 2026-10-06 確認的整理項目）：
  - 主畫面 ERP 單據狀態大標籤（檢視／新增／修改），由 `ErpDocumentStateTracker` 依「檢視→輸入瞬間單號是否被清空」判斷；只提示、不阻止（使用者決定不自動停止）。
  - 多個 COPI08 視窗時不開始輸入。
  - 顯示比例只支援 100%，以狀態列文字提示（使用者確認無 125%／150% 需求）。
  - 診斷模式：預設 LOG 不記錄實際品號／單號／OCR 文字（只記長度），設定勾選後才記錄。
  - 銷貨單號改以 `WM_GETTEXT`（含逾時）讀取，剪貼簿仍為後備。
  - 純規則抽到 `InputRules.cs`／`ErpDocumentState.cs`，新增 `tests/CYERPAutoInput.Tests`（CI 執行）。
  - `settings.example.json` 改為 C# 設定格式；移除註解中的 Build 版本號與 `WindowsOcrService` 相容命名（改名 `PaddleOcrService`）。

## 5. 已確認的 ERP 實機事實

以下是舊 Go prototype 實機測試累積出的有效行為，C# rewrite 應保留：

- COPI08 是 Delphi / DevExpress UI。
- 常見 class：`TDBEdit`、`TcxDBImageComboBox`、`TcxCustomComboBoxInnerEdit`、`TcxPageControl`、`TcxTabSheet`、`TcxGrid`、`TcxGridSite`。
- 明細 active editor 可見 `TcxCustomInnerTextEdit`。
- BROWSE / INPUT 可由上方 TDBEdit readonly / writable 狀態判斷；不可只看畫面文字。
- 日期輸入 raw `YYYYMMDD`，離焦後 ERP 可正規化成 `YYYY/MM/DD`。
- lookup 類欄位必須真的觸發 ERP leave / validation。
- Unicode 直接輸入比依賴中文 IME 穩定。
- 銷貨單別舊實機已確認 `End -> Backspace x4` 可可靠清除後重輸。
- 代收貨款 / 運費舊實機已確認採 click -> type -> leave，不先清除。
- 多列商品明細在舊 Go Build 11 已實機確認可工作。
- F2 單位查詢視窗 title 是 `F2開窗查詢`。
- F2 內 grid 為 DevExpress virtual grid。
- 使用者已人工確認：**點到正確單位列後，按 Enter 就能正常完成選取並關閉 F2。**
- 不得硬編碼某個單位固定在第幾列；不同品號的單位/換算列可能不同。


## 5.1 C# 版實機測試紀錄

- 2026-10-06 使用者回報：最近一次 C# 測試包實測進行到「F2 選批號」步驟（Build 號與結果細節未記錄）。
- Build 26 起，F2 批號查詢中「現有存量」無法辨識的列不再跳過：在找到第一筆正庫存之前遇到無法辨識的列即停止，避免跳過較早批號。

## 5.2 下一輪驗收清單（V0.2.0）

1. 啟動：版本顯示 `V0.2.0`（或之後的 `V0.2.0 Build N`）；狀態列顯示「顯示比例 100%」。
2. 狀態標籤：ERP 檢視時顯示「檢視」；手動按「新增」顯示「新增」；手動按「修改」顯示「修改」；關閉 COPI08 顯示「未開啟」；開兩個 COPI08 顯示「多個 COPI08」且無法開始輸入。
3. 找窗／新增：ERP 在檢視狀態按「開始輸入」，程式自動按新增，標籤變「新增」。
4. 表頭／交易／送貨／發票欄位逐一確認。
5. 商品明細：第一列、第二列；有指定單位的列 F2 單位選取並關閉。
6. F2 批號：有批號標記的品號選到由上往下第一筆現有存量 > 0 的批號並關閉 F2；無批號的品號不開 F2。
7. 完成後停在 ERP、未儲存；過程中按 Esc 只停止 CY。
8. 失敗時提供 `logs/CYERPAutoInput_日期.log`（必要時先在設定開啟診斷模式重現）。
9. ERP 結構探測（V0.2.0 Build 1 起，唯讀）：在 ERP 測試公司別，設定勾選診斷模式並儲存 → 再開設定按「ERP 結構探測」兩次：(a) COPI08 在新增狀態、明細有一列資料；(b) 明細單位欄開著 F2 查詢視窗。提供 `logs/erp-probe_*.txt`。用來評估 MSAA／控制項層級操作能否取代座標與 OCR。

## 6. CI 狀態

- PR #104 最後一次成功的 CYERPAutoInput Build 是 run #79（head `ddfb236`，2026-09-25）。
- 之後 `main` 新增公開套件機密掃描（`.github/scripts/scan-public-package.py`），PR 與 main 在 workflow 產生衝突，`pull_request` CI 因此未在之後 13 個 commit 上執行。
- 2026-10-06 已合併 main 並解決衝突：保留 .NET 8 build／self-test／publish／package 驗證，加回 `setup-python` 與機密掃描步驟。合併後需確認 CI 重新通過。
- 2026-10-06 合併 main 後的 run：build／self-test／publish／package 驗證通過，但機密掃描把 OpenCV 官方 DLL 內資料誤判為 Google refresh token；修正於治理 PR #333（Governance 2.3.29），合併後需把 main 再合併進本 branch。
- OCR 模型改由 `tools/ocr-models.json` 固定 SHA-256，優先從本 repo 模型 Release 下載；PR #104 合併後需從 main 手動執行 `CYERPAutoInput OCR Model Mirror` 一次建立該 Release。
- CI 成功只代表 Windows 編譯、self-test、publish 與 package 成功，**不等於已通過使用者真實 SMART ERP 驗收**。


## 7. 目前尚未完成 / 不得誤判為已完成

- C# V0.1.0 尚未完成使用者真實 ERP 全流程驗收；Build 20 之後各版的實機結果需向使用者確認。
- PR #104 尚未合併；目前仍不自動 Save；匯入解析（蝦皮／MO店+／酷澎商城）尚未完成。
- OCR 在不同 Windows DPI、ERP 視窗大小、字型下仍需實機驗證。
- F2 單位與批號流程必須保留 focus gate 與「F2 必須關閉」驗證；不得退回「看起來點到了就當成功」。
- 不得把舊 Go Build 19 的診斷 readback 當成 C# 正式選取方法。
- 下拉選項讀取（`PROJECT_RULES.md` §5）尚未實作：Codex 時期嘗試讀不到 DevExpress `TcxDBImageComboBox` 選項；目前設定只能手動輸入本機預設值。需在真實 ERP 上做有界診斷後再決定作法。
- 其餘規劃中功能（跨批號拆列、批次 fault isolation、批次結果總表、自動儲存）列在 `README.md` TODO。


## 8. 目前工作佇列（接手者從第一個未完成項目開始）

1. [完成 2026-10-06] Governance PR #333（公開套件掃描誤判 OpenCV DLL）已 squash merge（Governance 2.3.29）。
2. [完成 2026-10-06] PR #104 CI 全綠；Build 26 Artifact `CYERPAutoInput-v0.1.0-build26-windows-x64-run92`（run 37403614481，2026-10-20 到期；見 PR #104 留言）。
3. [待使用者] 使用者出差返回（約 2026-10-08）後依 §5.2 實測 V0.2.0 最新 Artifact（自動儲存先保持關閉，最後再開啟測一張）；依結果在同一 V0.2.0 返修（BUILD + 1）。V0.1.0 Build 26 Artifact（run92）仍可作為對照。
4. [待使用者決策後] 下拉選項讀取（打開下拉 → OCR → 以同一鍵關閉，不送 Esc、不改值，有界）。Build 26 實測後再做。
5. [暫緩，使用者 2026-10-06 指示] Excel 標準匯入格式細節：等基礎自動打單（單張含自動儲存）實機完成後再與使用者討論；之前不要自行定案範本欄位。
5.1 [完成，待實機] V0.2.0：介面補齊（依 CY 視覺準則，CI 介面截圖檢查）與選用自動儲存（見 §4）；自動儲存需實機驗證。
6. [完成 2026-10-06] 使用者已刪除遠端舊分支（`ci-cyerp-build*`、舊 Go 分支）。
6.1 [完成 2026-10-06] Governance PR #334（2.3.30）已合併：`PROJECT_RULES.md` §1 允許批次中放棄 CYERPAutoInput 自行新增且失敗的單據（須先確認仍在該張新增單據的輸入狀態）。
7. 真實 ERP 驗收通過後才把 PR #104 轉 Ready／合併；合併後從 `main` 手動執行一次 `CYERPAutoInput OCR Model Mirror` 建立模型 Release。

接手注意事項（本專案近期實際踩過的問題）：

- 新 commit author／committer 必須是 `simonliu1118-byte <286269326+simonliu1118-byte@users.noreply.github.com>`（REPOSITORY_RULES §4）。
- `.github/scripts/**`、`GOVERNANCE_*`、`RULES_INDEX.md`、`PROJECT_RULES.md` 屬治理範圍，只能在 `governance/*` branch 修改並更新 `GOVERNANCE_VERSION`／`GOVERNANCE_CHANGELOG.md`；不可混進 PR #104。
- 不得提交 `bin/`、`obj/`、`publish-staging/`、`runtime/ocr/`、`__pycache__/`、模型或任何 build 輸出；`git add` 前先看 `git status`。
- 推送前本機至少：`dotnet test tests/CYERPAutoInput.Tests`；可用 `dotnet build CYERPAutoInput.csproj -c Release -r win-x64 -p:EnableWindowsTargeting=true` 在非 Windows 編譯檢查。

## 9. Public repository / 資料安全

這個 repo 是 Public。

禁止提交：

- 真實客戶代號
- 真實品號
- 倉別 / 部門 / 人員
- ERP 實際下拉選項
- 訂單 / 發票 / 交易資料
- 公司內部路徑
- 帳密 / token / secret
- 使用者提供的 runtime log
- ERP 截圖
- `config/settings.json`
- `logs/`
- runtime cache / 匯入資料

診斷 log 可在使用者本機產生並由使用者主動提供給對話分析，但不得自動提交 GitHub。

## 10. 重要原則

不要因 C# rewrite 就丟掉舊實機已驗證的 ERP 行為。

這次 rewrite 的目的不是重新猜 ERP，而是：

`保留已驗證 ERP interaction semantics + 用 C# / WinForms / 本機 OCR 取代舊 Go 在 virtual grid / OCR / UI 維護上的弱點。`

目前最重要的驗收關卡仍是 **F2 單位選取**；使用者已證實「正確點列 + Enter」本身可行，因此下一步應驗證 C# OCR 是否能可靠完成「找到正確列並真的把 grid focus 放上去」。

## 11. Roadmap（2026-10-06 提案，待使用者決策）

核心架構：所有來源（手動 UI、Excel、平台訂單、CYweb 工單）先轉成同一份標準銷貨單資料，再走同一條「驗證 → ERP 輸入／儲存 → 結果」路徑；新增來源只新增 adapter，不另寫輸入流程。

- 階段 0（V0.1.x）：單張輸入穩定 — Build 26 實機驗收、F2 單位／批號、下拉選項讀取。
- 階段 1（第一個正式版）：使用者匯入程式提供格式的 Excel，ERP 自動輸入完成 — 程式產生範本（建議「單頭」「單身」兩個工作表以來源單號對應）、匯入前完整驗證與預覽、自動儲存並回讀單號、逐張失敗隔離、結果 Excel。
- 階段 2：串接 CYweb 工單系統 — 地端主動 HTTPS 拉單、回報結果；資料格式沿用標準銷貨單資料；工單 ID 冪等防重；若接 CYID 依 REPO_POLICY §4.1。
- 階段 3：地端無人值守 — 專用電腦自動登入／不鎖定／開機啟動、ERP 異常重啟與登入、心跳與異常通知。
- 階段 4：辨識查詢單據 — 表頭以 `WM_GETTEXT` 讀取、明細以 OCR；先只讀不改。

使用者決策（2026-10-06）：

1. 階段 1 完成時升 `V1.0.0`（第一個正式版）。開發期間依版本規則先用 `0.Y.Z`。
2. 採 (b)：批次中單據失敗時，可按 ERP「取消」放棄 CYERPAutoInput 自行新增的該張單據後繼續；需先確認 ERP 仍在該張新增單據的輸入狀態，否則停止整批。規則變更走 Governance PR #334。
3. Excel 範本先只放標準模式欄位，其他欄位用本機預設值；之後再擴充。
4. 階段 2 對接 CYWEB 的「訂單工單模組」（CYWEB 不在本 repo；接入前先確認其 API 與是否走 CYID）。
5. 地端專用電腦可設定自動登入、不鎖定；ERP 欄位一律由程式定位輸入（不改用 ERP API）。

風險評估後的使用者回覆（2026-10-06）：

- SMART ERP 沒有銷貨單匯入功能（只有匯出），模擬操作是唯一路線。
- 有 ERP 測試環境；實測由使用者處理。
- 速度（OCR 逐格）之後再優化。
- ERP 內通常不存來源單號；防重複打單（本機進度紀錄、ERP 查單依據）做到批次／工單階段再討論細節。
- 階段 2 之後會接 CYID。
- ERP 授權沒有問題。
- 後續階段：在裝有 ERP 的 Windows 電腦上執行 Claude Code（Claude Desktop app，或在 repo 資料夾執行 `claude remote-control`），由 AI 直接建置、執行、截圖、讀 LOG 做實測；不採用 Public repo 的 self-hosted GitHub Actions runner（安全風險）。

ERP 電腦實測操作清單（後續階段使用）：

1. 只用 ERP 測試公司別；開始前確認 COPI08 視窗標題為測試公司。
2. 測試期間該電腦不得有人同時操作；Windows 顯示比例 100%；不鎖定螢幕。
3. 建置：`dotnet test tests/CYERPAutoInput.Tests`、`dotnet build CYERPAutoInput.csproj -c Release -r win-x64`，再執行 `tools/fetch-ocr-models.ps1` 取得模型。
4. 觀察：以 PowerShell `System.Drawing` 擷取全螢幕 PNG 存到 repo 外的暫存資料夾檢視；LOG 在 exe 同層 `logs/`。截圖與 LOG 含 ERP 資料，只留本機、不提交 Git（PROJECT_RULES §3／§4）。
5. 需使用者先同意：開啟自動儲存、批次中按「取消」放棄單據、任何會在 ERP 產生或修改資料的新操作。
6. 修正照常推送到工作 branch，由主控方（Claude）審查。

其他已知事實：

- `SMARTCOPIConverter` 是 CYInvoice 上線前的 ERP 匯出轉 POS 發票方案，已停用；與本專案無相依。
- 使用者同意在需要時使用臨時 GitHub Actions workflow（例如在 Windows runner 做一次性驗證）；用完需移除，不得留在 `main`，若會上傳 Artifact 必須含公開套件機密掃描。
