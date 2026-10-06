# CYERPAutoInput WORK HANDOFF

> 更新：2026-10-06  
> 目的：供後續 AI 對話直接接手目前開發狀態（2026-10-06 起由 Claude Code 接手）。  
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
- VERSION：`0.1.0`；BUILD：見同目錄 `BUILD` 檔（2026-10-06 時為 `26`）。
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


## 4. V0.1.0 已完成架構

目前實際流程、UI、OCR、F2 單位、F2 批號、本機資料與安全設計以 `README.md` 為準，此處只列交接重點：

- 欄位有內容才送入 ERP；明細有資料的列必須有「品號 + 數量」。
- 表頭 / 交易 / 送貨 / 發票使用 Win32 control + ERP 原生焦點、離焦、Enter／Tab。
- 商品明細只點一次 `TcxGridSite` 建立第一列，再以目前 Grid 幾何 + OCR 定位 cell。
- F2 單位：以 OCR 定位「換算單位」欄中指定單位，點選後送一次 Enter，並確認 F2 關閉。
- F2 批號：明確空白不開 F2；有標記才開；查詢內只選由上往下第一筆可確認 `現有存量 > 0` 的批號，無法確認就停止（使用者已確認此業務規則）。
- 銷貨單號由 ERP 產生，CY 只讀取並驗證 `YYYYMMDDXXX` 格式。
- 完成後停在 ERP，**不自動儲存**；全域 Esc 只停止 CY，不按 ERP「取消」；不送 `Ctrl+A`。
- 仍需人工確認的狀況累積在 `AutomationRunResult.Warnings`，完成時統一顯示筆數。
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


## 8. 下一步建議順序

1. 先讀治理三層 + 本文件 + PR #104 最新 source。
2. 確認合併 main 後的 CI 通過；失敗時先修 CI，不疊加新功能。
3. 向使用者確認最近一版工程 Artifact 的真實 ERP 測試結果，整理成驗收清單（啟動／找窗／新增／表頭／多列明細／F2 單位／F2 批號／NO SAVE）。
4. 依實機結果修正同一 V0.1.0 工作項目；同一 rewrite 驗收返修只增加 BUILD，不另升 Patch。
5. 真實 ERP 驗收通過後，再決定 PR #104 Ready / merge。

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
