# CYERPAutoInput WORK HANDOFF

> 更新：2026-10-12（V0.2.9 由 PR #423 合併進 `main`，發布正式 Release `cyerp-auto-input-v0.2.9`；舊 Release V0.2.7 依使用者要求刪除）  
> 協作方式（使用者 2026-10-06 指定）：Claude Code 主控；Claude 額度不足時由 Codex 接手，Claude 額度恢復後檢查確認 Codex 的變更。交接以本文件＋Git 狀態為準。  
> 本文件只記錄目前狀態、已驗證事實、待辦與交接注意事項，不是永久規則來源。逐版變更見 `CHANGELOG.md`，功能說明見 `README.md`。

## 1. 接手時先讀

1. `/REPOSITORY_RULES.md`
2. `/REPO_POLICY.md`
3. `/apps/CYERPAutoInput/PROJECT_RULES.md`（目前 Governance 2.3.33）
4. 本文件 → `README.md` → `CHANGELOG.md`
5. `main` 的實際 source（PR #104、PR #398、PR #418、PR #421、PR #423 已合併，工作 branch 已刪除）

不要以舊聊天記憶覆蓋目前 Git 狀態。

## 2. Branch／PR／版本

- Repository：`simonliu1118-byte/CYapps`，專案：`apps/CYERPAutoInput/`
- `main`：`V0.2.9`（2026-10-12 由 PR #423 squash merge），正式 Release `cyerp-auto-input-v0.2.9`。`CYERPAutoInput Release` workflow：只從 `main` 手動啟動、BUILD 必須為 0、tag 不覆寫，Release note 取 CHANGELOG 該版段落中第一個「- Build」之前的摘要。舊 Release `cyerp-auto-input-v0.2.7` 依使用者要求以 `CYERPAutoInput Release Cleanup` workflow（只從 main、只接受本專案 tag）刪除。之前的合併：PR #421（V0.2.5–V0.2.7）、PR #418（V0.2.4）、PR #398（V0.2.1–V0.2.3 Build 1）、PR #104（V0.2.0 Build 31）。工作 branch 合併後刪除。下一次正式發布同樣需使用者同意。
- 下一輪修改從最新 `main` 開新 branch（例如 `cyerp-auto-input/<type>-<summary>`），開新 PR。
- VERSION／BUILD 見同名檔。`V0.1.0` 為 Codex C# 重寫（Build 1–27），`V0.2.0` 為使用者指定的 Minor，代表 Claude Code 接手。新功能 Z＋1（Build 歸零），返修 Build＋1；使用者說「下一版一起修」的項目不單獨發版。
- 技術棧：C# / .NET 8 / WinForms；OCR 為本機 PaddleOCR PP-OCRv5 mobile + ONNX Runtime（CPU）。由 Go 改寫是使用者同意的技術棧變更（Go 線讀不到 DevExpress virtual grid 的 cell 文字）。
- 測試包：PR 的 CI Artifact（保留 3 天），連結貼在該 PR 留言。正式版：GitHub Release（`CYERPAutoInput_VX.Y.Z.zip`＋SHA-256）。
- OCR 模型 Release：2026-10-11 使用者同意後從 `main` 執行 `CYERPAutoInput OCR Model Mirror`（之後的建置優先從該 Release 取模型）。

## 3. 目前功能（細節見 README）

- 單張 COPI08 自動打單：F5 新增 → 表頭／交易／送貨／發票欄位（課稅別、發票聯數為下拉）→ 商品明細（F2 單位、F2 批號第一筆正庫存；庫存不足提示按確定繼續；設定的折價券／運費／促銷組合品號不查批號）→ 選用 F12 自動儲存並讀回單號 → 選用自動「列印 → 直接列印」（讀「列印」周圍與 ERP 彈出視窗，最多重試 20 秒）→ 設定「儲存單據後自動清除表單」時清空（`ClearFormIfSaved` 唯一入口）。
- 輸入期間忽略實體滑鼠，Esc 立即恢復並緊急停止；輸入法自動切英數；自由文字欄位讀回比對。
- 蝦皮光學辨識輸入：按「開始」自動捲動瀏覽器訂單頁截圖（Home 回頂、像素比對捲動距離、到底即停）→ 依卡片灰色間隔切卡片、只辨識訂單編號／進帳資訊／買家實付金額三張 → 判讀並帶回；有問題時以 CY 框選截圖或 Win+Shift+S＋Ctrl+V 補圖。訂單編號 14 碼檢查、簡體字形摺疊。
- 蝦皮全自動（V0.3.0 開發中，選單）：自己的 Chrome 視窗逐筆 安排出貨 → 寄件單列印 → 光學辨識 → ERP，直到沒有待處理訂單；見 §4。
- 蝦皮自動擷取（設定，原名「蝦皮全自動」）：擷取 → 帶回 → 輸入 ERP → 儲存 → 列印；全程鎖實體滑鼠並在畫面中央顯示不入鏡的「全自動運轉中／按 Esc 中止」，需要使用者確認時立即解除。
- 其他來源：蝦皮官方匯出檔、MO店+ OrderExport（加密 `.xls`）、酷澎 DeliveryList。預覽、單筆、批次（開始前預檢全部）、轉人工、本機防重複紀錄；開始前檢查不通過回到同一個匯入視窗、保留已填內容。統編自動查客戶全名（經濟部 API）。
- 共用設定：折價券／運費／促銷組合品號；貨運別（物流：郵局、大榮、新竹、黑貓、宅配通、嘉里；本公司；超商 6 種）。
- 瀏覽器 F8：開啟蝦皮光學辨識並開始擷取（V0.2.8）。
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

- V0.2.4：使用者 2026-10-12 實測通過（「測試沒問題」），已合併；Release 延到 V0.2.5。使用者回報訂單編號／運送單號末段英數混合處偶有 0／O 誤讀。
- V0.2.5 Build 0：`ShopeeScreenshot.OrderSnDateDigits`（前 6 碼 O→0）、`TrackingDigits`（TW＋12 碼數字段、全數字單號 O→0）、`ResolveZeroO`（後 8 碼依字寬：DP 把 ink run 對齊字元，單獨成塊者與其他數字中位寬比較，≤1.12→0、≥1.25→O）；`ShopeeScreenshotForm.ResolveOrderSnZeroO` 量測 `GlyphRunWidths`（以最常見灰階為背景、差 >70 為字），LOG 記 `order number 0/O runs=` 與判斷。依使用者截圖量測：蝦皮字型 0 與數字同寬約 7px，C／Q 約 9px。隨 V0.2.7 發布；0／O 判斷尚未遇到實例，之後有 `order number 0/O` LOG 再核對。
- V0.2.6 Build 0：V0.2.5 實測 12 項訂單在第 7 列失敗（`editor not ready row=7 col=0 point=128,620`），所有歷史 LOG 都沒有成功過第 7 列。使用者截圖量得 ERP 明細列高 22 px，`ProvenDetailRowPitch`=24 逐列累積誤差。`ErpAutomationService._editorRowCenter` 記錄每列編輯框實際中心（相對明細頂端，新單據 geometry ready 時清空）；`DetailRowPitch()` 以前 6 列中最先與最遠兩列量測（16–36 px 才採用）；第 1 列錨點也用實際編輯框；`DetailRowExistsAsync` 共用。未就緒的 LOG 加 `focus_rect`、`grid_top`。實測通過：12 項訂單 `pitch=22`、第 12 列 `editor ready`。
- V0.2.7 Build 0：單品訂單 OCR 把貨號標籤切成「商品選項」＋「號：A0…」（漏「貨」）→ `CodeLabelPattern` 加 `(^|選項)號(?=[:：])`。使用者 2026-10-12 實測通過，曾發布為正式 Release（後由 V0.2.9 取代並刪除）。
- V0.2.7 Build 1（使用者指定小修只加 Build，原編 V0.2.8 Build 0）：`ImportedItem.InEntryOrder`（`MainForm.LoadOrder` 使用）依品號排序，設定的折價券／運費／促銷組合品號排最後；`TrackingDigits`／`OrderSnDateDigits` 的數字段 I、L→1（使用者截圖：`#TW266188241759I` 最後一碼為 I）。使用者確認店到店單號最後一碼可能是數字或字母，維持照 OCR。使用者 2026-10-12 實測通過（隨 V0.2.9 發布）。
- V0.2.8 Build 0：`BrowserHotkey`（WH_KEYBOARD_LL 獨立執行緒，只在前景為 Chrome_WidgetWin_1／MozillaWindowClass 且非 CY、無 Ctrl／Alt／Shift 的實體 F8 時攔下）→ `MainForm.OnBrowserHotkey` → `ImportShopeeScreenshotsAsync(startScan: true)`（全自動與否照設定）；忙碌（輸入中或有其他可見視窗）時忽略；`UserSettings.BrowserHotkey` 預設開。使用者 2026-10-12 實測通過（隨 V0.2.9 發布）。
- V0.2.8 Build 0（續）：`AutomationSession.End(AutomationOutcome)`：Done（綠勾、0.5 s）、NeedsAttention／Stopped／Paused（黃三角、1.5 s）；`RunDocumentAsync` 依 HandedOff／Warnings／SaveSkippedReason／PrintProblem 判斷；無參數 `End()` 為暫停詢問。UI 訊息與 README 不再以「CY」稱呼本程式（使用者 2026-10-12：程式名稱 ERP AutoInput）。 `FreightCarriers.Aliases` 加 7-11←7-ELEVEN／統一超商、全家←FamilyMart、萊爾富←Hi-Life。
- V0.2.8 Build 1：實測數量「2」讀成「7」→ `ShopeeScreenshot.SuggestQuantity`（ParseItems 最後）只在提醒中指出唯一能對上小計原價的品號與數量，不改資料（使用者 2026-10-12 明確要求：不能自動改數量／品號）。使用者 2026-10-12 實測通過（隨 V0.2.9 發布）。
- V0.2.8 Build 2：`AutomationOutcome.Cancelled`（Esc：`RunDocumentAsync` 捕捉 OperationCanceledException、擷取 `result.Cancelled`）→ 紅色圓形白橫條「使用者已停止」。使用者 2026-10-12 實測通過（隨 V0.2.9 發布）。
- V0.2.8 Build 3：編號補讀信心 <0.8 時，若數字等於 `ShopeeScreenshot.ExpectedIndexAt`（上一個＋1／下一個－1，兩者都有須一致）才採用（在所有補讀完成後判斷）。實例：「1」信心 0.289。使用者 2026-10-12 實測通過（隨 V0.2.9 發布）。
- V0.2.8 Build 4：`PhysicalMouseLock.EscapePressed`（hook 看到實體 Esc 即在 pool thread 觸發一次）→ `AutomationSession.OnEscape`：取消 `EscapeToken` 並 Post `End(Cancelled)`；`RunDocumentAsync` 以 EscapeToken 取消 `_automationCts`；`ShopeeScreenshotForm.AutoScanAsync` 以 stop token 傳入 `ScanAsync`（含 ReadCardsAsync）。`ShopeeScreenshotHelpForm` 改寫為自動擷取流程；選單「光學辨識輸入 (F8)」。使用者 2026-10-12 實測通過（隨 V0.2.9 發布）。
- V0.2.9 Build 0：蝦皮套裝 `ShopeeScreenshot.BundleParts`（+／＋／十）→ `ParseItems.AddItem` 拆品號（ShopeeShotItem 首項帶套裝金額供核對）；`Combine` 產生的表單品項單價留空、首項 `BundleTotal`、促銷組合列 `TakesBundleGap`（有套裝必加）；表單隱藏欄 BundleKey／BundleTotal／BundleGap → `DetailRow`；`ErpAutomationService.FillDetailsAsync` 對套裝品項以 `ReadDetailUnitPriceAsync`（開啟單價編輯框 WM_GETTEXT，失敗 OCR 編輯框，再 Enter）讀 ERP 原價，促銷組合單價＝基礎值−Σ(原價×數量−套裝售價)。讀不到或順序不對即停止（不猜）。使用者要求報表金額正確，不採「首項全價、其餘 0」。共用 `Bundles.Expand`（ImportedOrder.cs）套用於 MO店+、酷澎、蝦皮匯出檔（mapping 加 `PromotionItemCode`）；沒設促銷組合品號才轉人工。ERP 原價合計 < 套裝售價時停止轉人工。使用者 2026-10-12 實測通過（隨 V0.2.9 發布）。
- V0.2.4 Build 0 實作：一般輸入也顯示「自動輸入中」（`MainForm.RunDocumentAsync` 一律 `Session.Begin(title)`／`End()`，`AutomationSession` 取代原本直接 `PhysicalMouseLock`）；提示框寬度依文字；光學辨識進度改為 `IProgress<double>`（依要辨識的列數）＋百分比、`ShowProgress` 以 Value+1 再回設跳過主題動畫；`ParseItems` 沒有任何編號且小計欄 1–2 個數字時視為第 1 項（單品促銷誤報）；設定兩處文字。
- V0.3.0 Build 0（branch `cyerp-auto-input/shopee-batch`）：蝦皮全自動批次。`ShopeeBatchRunner`：`chrome.exe --new-window <ListUrl>`（App Paths 登錄或預設安裝路徑），以前後 `Chrome_WidgetWin_1`（chrome 程序）視窗差異找出自己的視窗並最大化；每一步整窗擷取＋PaddleOCR（maxScale 1.0）讀畫面，`ShopeeBatchScreens`（純邏輯，有單元測試）判斷頁面與點擊目標：清單（我的銷售／待處理 (n)／建議出貨期限列的「全部」／表頭「買家實付金額」下第一個「待出貨」、上方訂單編號、物流方式欄）、明細（訂單編號、包裹1 物流、`ShipButton` 防「不成立訂單」＋橘色像素檢查）、查看寄件編號（申請中→TW 號碼、列印寄件單，看不到就滾輪）、預覽列印（下載完成 1/1/0、寄件單上的訂單編號與寄件編號）、Chrome 列印（目的地、縮放「自訂」下的數字、「取消」左側的「列印」）、× 以像素找（標題上方一列找對話框右緣，標題右側唯一墨跡）。之後 Ctrl+W 回訂單頁（找不到用 Ctrl+Tab 最多 4 次），ERP 走 `ShopeeScreenshotForm(autoStart, session, browser: 自己的視窗)` → `StartAutomationAsync(fullAuto, batch: true)`（`_lastRunResult`）；有提醒／警告／轉人工／未儲存／未列印即 `ShopeeBatchStop`。完成後 Ctrl+W 訂單分頁、F5 清單。結束條件：待處理 (0)，或「全部」找不到訂單且待處理不為正數。`MainForm.Alarm`（SystemSounds.Hand＋Console.Beep×3）。設定 `ShopeeLabelPrinter`（預設 GG-D410 Printer）、`ShopeeLabelScale`（110）。**全部未實機測試**；超商（7-11／全家／萊爾富等）流程使用者之後提供；「安排取件」後的畫面未見過，可能需要補。
- V0.3.0 Build 1：`ErpAutomationService.PrimeDetailGridAsync` 先以 `FastDetailGridVisionService.FindFirstItemCellAsync`（表頭 OCR 找「品號」、垂直格線吸附欄中心、表頭下緣橫線＋半列 11 px）點第一列，讀不到才用舊的 5.2%／y=33；`GridVisionService.LearnColumnOrder`：同一次表頭讀到的欄位依畫面 X 重新分配順序槽，`TryGetLogicalOrder` 優先用學到的順序（水平捲動方向、格線推欄）；`FindExactPhrase` 由左而右、`IndexOfHeader` 排除「庫別名稱」；`ProvenDetailRowPitch`＝22（使用者：列高不會變）。待實測（使用者另一台欄位排法：序號、品號、品名、規格、數量、類型、贈/備品量、單位、庫別、庫別名稱、批號、單價…）。
- V0.3.0 Build 2：實測第一個清單畫面 12 秒後 `IndexOutOfRangeException`（第一次整窗 OCR 1920x1048 成功 111 tokens，約 5 秒；之後沒有 OCR completed 紀錄，推測引擎內部）。`ShopeeBatchRunner.ReadAsync` 失敗時 `ReadInHalvesAsync`；`AppLogger.Error(area, ex)` 附 StackTrace。待實測，若仍失敗以堆疊定位。
- V0.3.0 Build 3：Build 2 堆疊：`RapidOCRSharpOnnx.Inference.TextCalRecBox.GetWordInfo` IndexOutOfRange（ReturnWordBox 的逐字計算）。`PaddleOcrService.RecognizeLinesAsync`：第二個引擎 `CreateEngine(wordBoxes: false)`，一行一個 box（WordResults 空時改用 DetResult.DetItems＋RecResult.Label）；`ShopeeBatchRunner.ReadAsync` 改用它（移除切半重試），每次讀畫面記 `screen read lines=… list= detail= dialog= awb= print=`。以使用者截圖（本機 python rapidocr 同模型、整行）驗證 `ShopeeBatchScreens`：內→内（`Compact` 加摺疊）、待虚理（`IsOrderList` 不再要求待處理、ChipCount 接受虚）、包里、物流方式在待出貨上方（改以訂單編號到下一筆訂單編號之間）、寄件編號末碼 O／0 等價、`DialogCloseMark`（OCR 讀到 X）。截圖與辨識結果只在本機，未進 Git。
- V0.3.0 Build 4：7-ELEVEN。`ShopeeBatchScreens.CarrierKind`（Shopee／SevenEleven／Other；7ELEVEN／711／統一超商）、`TrackingIn` 加交貨便代碼 `[A-Z][0-9OIL]{11}`、`IsSevenLabelPage`／`SevenLabelCode`、`IsPrintDialog` 改為 目的地＋（縮放比例或顯示更多設定）、`MoreSettings`。`ShopeeBatchRunner.ShipAsync` 依 Kind 分 `PrintShopeeLabelAsync`／`PrintSevenSlipAsync`（等 7-11 分頁、核對寄貨訂單編號與服務代碼、Ctrl+P），共用 `PrintDialogAsync(scale)`（讀不到縮放比例先點一次顯示更多設定）。設定 `SevenLabelScale`=100。以使用者 7-11 截圖（本機）驗證。待確認：7-11 訂單在蝦皮訂單頁的物流單號是否等於交貨便代碼（ERP 前會比對，不同則停止）。全家／萊爾富待使用者提供。
- V0.3.0 Build 5：全家。`CarrierKind` FamilyMart（全家／FamilyMart）、`ApplyTrackingButton`（申請寄件編號，WaitAsync 內按一次）、`DialogTracking` 其他格式退而讀標題下方 8–20 碼英數（≥8 數字）、`IsFamilyLabelPage`、`ShowsOrderSn`（全家把訂單編號印在下一行）、`SheetCount`、`PrintRowValue`、`PrintScaleBox`、`PrintPagesBox`。`ShopeeBatchRunner.PrintDialogAsync(scale, pages)` 改為主動設定：頁面全部／自訂＋頁碼、縮放比例自訂＋數字（`ChooseAsync` 開下拉後只點新出現的選項，讀畫面與點擊時不 Front 以免關掉下拉；`TypeIntoAsync` 點、Ctrl+A、輸入、Tab），最後確認目的地、1 張紙；超過 10 次操作或 40 秒即停止。`ShopeeBatchPrint` record 帶印表機與三種縮放、全家頁碼。以使用者全家截圖（本機）驗證辨識；下拉清單的實際樣子未見過，實測時注意 LOG 的 `click 列印設定`／`選 …`／`type …`。
- V0.3.0 Build 6：`ShopeePageScan.Sections` 商品表 bottom 不超過其下第一個「買家實付金額」上緣（讀不到隱藏入帳明細時，`stop` 會落到買家卡片的「訂單金額」）；`ShopeeScreenshotForm.AddAsync(..., kind)` 沿用 `ShopeePageSection.Kind`。全家寄件編號 11 碼數字＝寄取貨單「訂單編號」，`ShowsNumber` 核對。
- V0.3.0 Build 7：`ShopeePageScanner.ReadCardsAsync` 進帳資訊的讀取範圍延伸到下一個 wanted 卡片上緣（仍在讀到入帳明細時停止）。依據：LOG 卡片 `進帳資訊 top=1197 h=1186` 之後 `top=2385 h=598 title=買10送1…`（同一商品表被灰線切開）。
- V0.3.0 Build 8：`SetDetailCellAsync` 3 次嘗試，重試前等 500×attempt ms（LOG 14:03 `editor not ready row=1 col=1`，focus 仍在品號編輯框；使用者重跑即成功）。
- V0.3.0 Build 9：`CloseOrderTabAsync` 先點頁面左側＋Home 再核對（LOG 14:44 第 1 筆完成後頁面停在底部）。速度：`PageArea()`（最大的 Chrome_RenderWidgetHostHWND）只讀網頁；`ReadAsync(part, reuse)`＋`_inputs` 計數（Click／Keys／Type／Wheel／ERP 後遞增），`WaitAsync(reuseFirst, part)`；列印視窗 `part` 右半（0.5–1.0）。實測 Build 8：一筆蝦皮端 14:41:59→14:43:53（約 2 分），ERP 約 50 秒。
- V0.3.0 Build 10：`ChromePageText.Read`（System.Windows.Automation，`CacheRequest` 一次取 Name／Value／BoundingRectangle／ControlType，空結果重讀最多 3 次）；`MainForm.DiagnoseChromeTextAsync`。csproj 加 `UseWPF`（只為 UIAutomationClient）並補回 `System.IO`、`System.Net.Http` 全域 using。待使用者在各畫面實測 LOG；可行則下一步以網頁文字取代蝦皮全自動與訂單頁的 OCR（OCR 留作退路）。
- V0.3.0 Build 11：網頁原文（UI Automation）取代 OCR。`ChromePageText.Read`（ComboBox／Spinner／Edit 取 Value、無名 Button 記為 `ShopeeBatchScreens.NamelessButton`、無 Document 重讀）、`PageBoxes`（去 TabItem／ToolBar／瀏覽器列、相對頁面區）。`ShopeeBatchRunner.ReadAsync` 先讀網頁原文（同時截圖供橘色與 × 像素判斷），無 Document 才 OCR；`Click` 只點可見範圍內；`ChooseAsync` 改 Home／End＋Enter；列印視窗下拉讀不到值（只有標籤名）即改 OCR。`ShopeeBatchScreens`：分開的「待處理」「(19)」、清單整列連結（`FirstOrderRow`，點狀態欄、列頂下 52 px）、整顆按鈕間距 7 px 的防護改為 3 px、「 列印出貨文件」前綴圖示。`ShopeePageScanner.ReadPageText`＋`TextImage`、`ShopeeScreenshotForm.TryPageTextAsync`（三段齊全才採用，`fromText` 不做補讀與 0／O 字寬）。以使用者診斷 LOG（本機）驗證清單、明細、寄件編號視窗、預覽列印、列印視窗與訂單頁解析。
- V0.3.0 Build 12：`WaitAsync` 需 probe 成功＋與上一讀文字相同＋無 Loading／載入中（LOG 15:30 明細頁 `包裹1: Loadingshippingchannel`）；成功後 250 ms、否則 500 ms 再讀。
- V0.3.0 Build 13：清單列文字可能被截斷（長訂單）；清單不再讀物流（使用者：所有物流之後都會支援），`ShipAsync` 以明細頁 `DetailCarrier` 決定 `order.Kind`（Other＝尚無流程，安排出貨之前停止）。
- V0.3.0 Build 14：`ShopeeBatchRunner.Orange` 取樣 8 點（框外 4＋框內緣 4），≥3 點橘色（Build 13 實測：整顆按鈕的 page-text 框外為背景色）。

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
