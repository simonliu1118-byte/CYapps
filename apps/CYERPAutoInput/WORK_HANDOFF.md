# CYERPAutoInput WORK HANDOFF

> 更新：2026-09-24  
> 目的：供下一個 ChatGPT / Work 對話直接接手目前開發狀態。  
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

Repository：

`simonliu1118-byte/CYapps`

專案：

`apps/CYERPAutoInput/`

### 正式 main

目前 `main` 仍是舊 Go 線：

- VERSION：`0.0.10`
- BUILD：`19`
- Build 19 只是 F2 單位查詢 Win32 readback 診斷，不是新的可靠單位選取方案。
- main 尚未切到 C#。
- 舊 Go V0.0.11 optical prototype PR #103 已關閉、未合併；它已被 C# rewrite 取代。

### 現行開發線

Branch：

`cyerp-auto-input/v0.1.0-csharp`

Draft PR：

`#104 CYERPAutoInput V0.1.0: C# rewrite with optical ERP targeting`

目前版本：

- VERSION：`0.1.0`
- BUILD：`0`
- 技術棧：C# / .NET 8 / WinForms
- PR 仍為 Draft，不要在尚未完成真實 ERP 驗收前直接合併 main。

本次升 Minor 是使用者已明確同意的技術棧重寫。

## 3. 為什麼由 Go 改寫 C#

舊 Go 線已證明一般 COPI08 Win32 輸入可行，但在 DevExpress virtual grid / F2 單位查詢遇到核心限制：

- `TcxGridSite` 的可見 cell 文字通常不是獨立 HWND text。
- `GetWindowText` / child control text 無法可靠讀到使用者肉眼看見的單位 cell。
- 舊 Build 18 的實機 LOG 明確顯示：F2 已開啟、焦點在 `TcxGridSite`，但 40 次 readback 仍找不到指定單位，因此舊流程在送 Enter 之前就因 `UNIT_LOOKUP_FAILED` 停止。
- 使用者已人工確認真正 ERP 行為：F2 開啟後，人工點選正確單位列，再按實體 Enter，F2 會正常關閉並完成選取。

因此 V0.1.0 改採 C#，讓 WinForms UI、Win32 interop、Windows OCR 與畫面定位集中在單一 Windows 原生開發線。

## 4. V0.1.0 已完成架構

### UI

- WinForms 主介面。
- 標準 / 進階模式。
- 上方欄位不再有「是否套用」勾選框：欄位有內容才送入 ERP。
- 商品明細使用原生 `DataGridView` 直接編輯。
- Enter / Tab 往下一格；Shift+Enter / Shift+Tab 反向。
- 任一明細列只要有資料，`品號 + 數量` 都是必填；缺少時在 ERP 自動化開始前阻擋。
- 蝦皮 / MO店+ / 酷澎商城入口保留；匯入解析尚未串接完成。
- 酷澎商城按鈕維持既定 CYINVOICE 同系視覺。

### ERP automation

- 尋找 COPI08。
- 最小化時還原並帶到前景。
- 只接受 BROWSE / INPUT / UNKNOWN 狀態判斷。
- 必要時以 OCR 找「新增」後進入 INPUT。
- 表頭 / 交易資料 / 送貨資料 / 發票資料(一) 使用 Win32 control + ERP 原生焦點/離焦/Enter/Tab 流程。
- 銷貨單號不輸入，由 ERP 自行產生。
- 目前完成後停在 ERP，**不自動儲存**。
- 全域實體 Esc 只停止 CYERPAutoInput 後續自動化，不替使用者按 ERP「取消」。
- 對 ERP 禁止 `Ctrl+A`。

### Optical / OCR

使用 `Windows.Media.Ocr`，只截取必要局部畫面，不做持續監看。

商品明細：

1. 找實際 `TcxGridSite`。
2. 截取目前 grid 畫面。
3. OCR 表頭。
4. 由實際表頭 / 格線推導欄位與可見列幾何位置。
5. 依辨識結果點 cell，進入 editor 後輸入。
6. 不再用「ERP 視窗縮放比例」硬猜明細座標。

F2 單位：

1. 點單位 cell。
2. 送 F2。
3. 等待 top-level `F2開窗查詢`。
4. 截取 lookup。
5. OCR 找使用者要求的單位文字。
6. 點 OCR 回傳的實際位置。
7. 最新版本額外確認 foreground focus 確實是 `TcxGridSite`；第一次點擊未取得 grid focus 時會再點一次。
8. 只有確認 focus 是 `TcxGridSite` 才送一次 physical Enter。
9. Enter 後必須確認 F2 視窗真的關閉；未關閉即停止，不猜、不改按其他確認鍵。
10. F2 關閉後重新把 COPI08 帶回前景。

這是目前取代舊 Go「Down + GetWindowText 判斷」的正式方向。

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

## 6. 最新 CI 狀態

PR #104 最新確認 head：

`9a9be60c08252a873f70c879592f7af7c02ea367`

GitHub Actions：

- Governance Check run #445：success
- CYERPAutoInput Build run #58：success
- Windows runner 已完成 .NET 8 restore / build。
- optical pipeline self-test：success。
- self-contained win-x64 single-file publish：success。

最新工程 Artifact：

`CYERPAutoInput-v0.1.0-windows-x64-run58`

- Artifact ID：`10788702353`
- Size：`74,066,324 bytes`
- SHA-256（artifact archive digest）：
  `567df75b51ba53d4d4e6dfdcc43df7619520523d65ab9d7e1ec77e911e2f87dc`
- GitHub 顯示到期日：2026-10-08。

注意：CI 成功只代表 Windows 編譯、self-test、publish 與 package 成功，**不等於已通過使用者真實 SMART ERP 驗收**。

## 7. 目前尚未完成 / 不得誤判為已完成

- C# V0.1.0 尚未完成使用者真實 ERP 全流程驗收。
- PR #104 尚未合併。
- 目前仍不自動 Save。
- 匯入解析尚未完成。
- Batch 明細自動化仍 deferred。
- OCR 在不同 Windows DPI、ERP 視窗大小、字型、中文 OCR language availability 下仍需實機驗證。
- F2 OCR 能否在使用者真實 lookup 畫面穩定找到指定單位，仍是第一優先實機驗收點。
- 即使 OCR 點到正確列，也必須保留目前的 `TcxGridSite` focus gate 與「F2 必須關閉」驗證；不得退回「看起來點到了就當成功」。
- 不得把舊 Go Build 19 的診斷 readback 當成新 C# 正式選取方法。

## 8. 下一個對話建議接續順序

下一個對話不要再重做技術選型，直接接 PR #104：

1. 先讀治理三層 + 本文件 + PR #104 最新 source。
2. 確認 PR #104 head 與 CI 是否仍為最新；若 branch 有新 commit，以新 head 為準。
3. 不要重跑無必要 CI；目前 run #58 已成功。
4. 準備 C# V0.1.0 工程 Artifact 給使用者做第一輪真實 ERP 測試。
5. 第一輪實測優先驗證：
   - 啟動 / UI / 標準進階模式。
   - COPI08 找窗、還原、foreground。
   - BROWSE -> 新增 -> INPUT。
   - 已驗證過的表頭欄位。
   - 第一列 / 第二列商品明細。
   - **有單位的明細：F2 -> OCR 點指定單位 -> grid focus -> Enter -> F2 關閉。**
   - 全流程完成後仍 NO SAVE。
6. 若 F2 失敗，先看本機 log，判斷失敗點屬於：
   - OCR 沒找到單位；
   - OCR 座標錯；
   - click 後 focus 不是 TcxGridSite；
   - Enter 已送但 F2 未關；
   - 或 F2 成功但回 ERP 後續欄位失敗。
   不要再用舊 Go 的 `GetWindowText` cell readback 方案。
7. 依第一次 C# 實機結果修正同一 V0.1.0 工作項目；若只是同一 rewrite 驗收返修，依版本規則增加 BUILD，不另升 Patch。
8. 真實 ERP 驗收通過後，再決定 PR #104 Ready / merge；不要提前合併。

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

`保留已驗證 ERP interaction semantics + 用 C# / WinForms / Windows OCR 取代舊 Go 在 virtual grid / OCR / UI 維護上的弱點。`

目前最重要的驗收關卡仍是 **F2 單位選取**；使用者已證實「正確點列 + Enter」本身可行，因此下一步應驗證 C# OCR 是否能可靠完成「找到正確列並真的把 grid focus 放上去」。
