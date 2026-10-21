# CYERPAutoQuery Work Handoff

> 更新：2026-10-21（V1.0.0 正式 Release，tag `cyerp-auto-query-v1.0.0`：彙整 V0.10.2 Build 1–4 的進貨輔助、批次查庫存、品號可含「-」）。之後的修改依規則升 Z 或 Build；需要轉交 GPT 時依 `REPOSITORY_RULES.md` §11.4 與 `docs/work-orders/README.md`。

## 進貨輔助（V0.10.2 Build 1，2026/10/20）

- CY Web 類型 `RESTOCK`（dateFrom／dateTo 必填）：`ItemQueryService.Restock.cs` 先以 `SalesQuery(string.Empty, …)`（`AllCustomers`）跑 COPR15／COPR16，`FillReportBasicAsync` 清空客戶代號起訖（`ClearReportTextAsync` 讀回確認），`SalesExport.Parse` 對全部客戶不核對客戶代號；`Restock.Summarize`（`RestockQuery.cs`，純邏輯、有測試）依品號／日期加總；再以 `ListItemsAsync`（`ItemCodeConditions`：進階設定 `(INVMB.MB001 = N'…')` OR 連接，與品名搜尋共用 `ListByConditionAsync`）批次讀 INVI02 資料瀏覽的庫存數量；`FillAdvancedConditionAsync` 先讀條件框 EM_GETLIMITTEXT，放不下或讀回不同丟 `ConditionNotTakenException`，批次減半。回報 `ErpAgentProtocol.RestockBody`。
- 尚未實機：全部客戶的 COPR15／COPR16 清空客戶代號後是否照預期、條件框實際能放多少字（LOG `condition box limit=`）、一批 100 個品號的查詢時間（LOG `restock` 有記錄）。

## 供應廠商與進貨退貨單查詢（V0.9.0 Build 1，2026/10/18）

- 新增 PURI01 供應廠商查詢，與 COPI01 共用 `PartyQuery.cs`（`ErpParty.Customer`／`ErpParty.Supplier`：表、簡稱／全名欄、顯示欄位、名稱清單欄位）與 `ItemQueryService.QueryPartyAsync`／`SearchPartiesAsync`。CY Web 類型 `SUPPLIER_NO`（`supplierCode`）／`SUPPLIER_NAME`（`keyword`）；CY Web 端尚未實作（claim 會送出這兩個類型，CY Web 不認識的類型不會派工）。
- 尚未實機：PURI01 視窗類別（猜 TfrmPuri01，不符時以標題辨識）、頁籤座標沿用 COPI01（不符時 OCR）、資料瀏覽「轉出全部」的欄位名稱（名稱清單讀 廠商代號、簡稱、公司全名、TEL、FAXNO、統一編號）。
- 進貨退貨單（同版）：`PurchaseQuery.cs`（PURR18／PURR22 解析）、`ItemQueryService.Purchases.cs`、`MainForm.Purchases.cs`；CY Web 類型 `PURCHASES`。尚未實機：PURR18／PURR22 頁籤座標（量自截圖，不符時 OCR）、PURR22 進階選項內容、「驗收日」選項與「僅列印…」勾選狀態能否以 BM_GETCHECK 讀到（LOG 有記錄）。

## 現況（V0.4.0 Build 1，2026/10/16）

- 新增品名查詢（`ItemNameSearch.cs` 純規則與解析、`ItemQueryService.SearchByNameAsync`）。條件文字貼到進階設定條件框是使用者提議的做法，尚未實機驗證 ERP 確定時是否採用條件框文字；程式以「每筆品名都含關鍵字」驗證，不符就不採用。待實機：LOG `search: advanced condition pasted`、`after query 資料瀏覽 shown=`、`done items=`；若條件框不能輸入，LOG 會列出進階設定子視窗。
- Web 品名查詢的補充交接待寫入 chihyuan-web（queryType ITEM_NAME、keyword、items）。

## 現況（V0.5.0 Build 1，2026/10/16）

- 新增 COPI01 客戶查詢（`CustomerQuery.cs` 規則、欄位、解析；`ErpForm.cs` 讓 INVI02／COPI01 共用查詢流程）。尚未實機：COPI01 視窗類別（程式猜 TfrmCopi01，不符時以標題辨識）、頁籤座標（基本資料中心約距頁籤控制項 72 px、間距 99 px，不符時 OCR）、一般選項客戶代號的條件下拉順序（程式以 Home＋下×2 設成「=」並讀回）、轉出設定是否有單頭／單身頁籤、送貨地址的郵遞區號是否為第三欄。

## CY Web 對接（V0.7.1，2026-10-17）

- chihyuan-web `cyweb/feat-erp-customer-sales-receivable`（CY Web 0.8.0）已實作：單一「ERP 查詢」入口（桌機頁籤、手機下拉），商品／客戶（含銷貨銷退單）／未收款各自 Module Access，另有「ERP 商品成本」權限控制標準進價；品號／客戶代號／業務員代號只收英數；歷史異動與單據日期最多三年由 CY Web 負責；銷退↔原銷貨單連結與未收款分組在 CY Web。協定見該 repo `docs/architecture/ERP_ITEM_QUERY_BRIDGE_CONTRACT.md` V0.8.0 節。
- 待辦：兩邊 PR CI、CY Web 部署（含 migration 0012）、實機以 Web 送出四種新查詢驗收。

## INVQ01 歷史異動日期輸入（V0.3.0 Build 2～8，使用者確認）

- 起迄都有：點起日年份位置 → Home → 起日 `yyyyMMdd` → Enter（自動跳到迄日，不點）→ Home → 迄日 `yyyyMMdd`。
- 只有起日：點起日 → Home → `yyyyMMdd`。只有迄日：直接點迄日 → Home → `yyyyMMdd`。
- 兩個都沒填：本機查詢不動日期欄；CY Web 由 Worker 補齊起迄後傳入。
- Home：日期欄點過一次後會顯示「    /  /  」遮罩，點擊可能落在第二位以後（使用者 2026-10-16）。輸入後讀回，不符即停止、不按開始查詢。
- 實機慢點（V0.3.0 LOG）：整條 Ribbon OCR 約 2 秒、整窗頁籤 OCR 約 2.6 秒；Build 6～7 改為只讀按鈕所在群組（並記住群組）與頁籤先點座標。

## 先前狀態（V0.3.0）

- 新增 INVQ01（使用者 2026-10-16）：批號管理不是 N 時查各批庫存量；勾「查歷史異動」時查歷史異動（日期選擇器，未填最近一年、最多三年）；INVQ01 用完關閉。程式在 `ItemQueryService` 的 INVQ01 區段，純規則在 `Invq01Layout.cs`（頁籤中心、標題、日期、`HistoryRange`），解析在 `Invq01Export`。已用使用者提供的 INVQ01_5／INVQ01_4 真實檔在本機確認解析（未提交）；歷史異動 1,621 列約 144 KB JSON，Web 回報上限 2,000 列（保留最新），CY Web 需放寬 96 KiB 上限（補充交接見 chihyuan-web `docs/handoff/ERP_ITEM_QUERY_INVQ01_HANDOFF.md`）。
- 尚未實機看過：INVI02 Ribbon「串查」下拉、INVQ01 頁籤是否為視窗（不是時改 OCR 並以標題確認）、「轉出到Excel」的設定視窗（程式自動辨識 ERP 匯出設定或 Windows 另存新檔，並在 LOG 列出子視窗）、轉出後是否有「檢視Excel資料?」（有就按 No，沒有則以檔案寫好且 ERP 閒置 1.5 秒為完成）、歷史異動日期欄的輸入方式、批號為零時 ERP 的反應。
- V0.2.0 Build 4 已實機：本機與 Web 查詢都成功。

## 先前狀態（V0.2.0 Build 0）

- CY Web Agent 完成（`ErpAgentProtocol`／`ErpAgent`（client + loop，純 .NET、有單元測試）／`ErpAgentSettings`（DPAPI）／`ErpAgentSettingsForm`）；本機與 Web 查詢共用 `MainForm.ExecuteQueryAsync` → `ItemQueryService`，不另寫第二套流程。已對本機 `wrangler dev` 執行的 chihyuan-web #169 Worker（local D1、假工作）實測 claim → complete(FOUND) 成功；`tests/.../LiveCyWebTests.cs` 為 opt-in 測試（需 CYQ_LIVE_BASE／CYQ_LIVE_TOKEN）。
- 待：chihyuan-web #169 合併並部署後，用真的 CY Web 網址與 Token 實機端對端測試。
- V0.1.0 Build 3 實機：查詢、基本資料1、Ribbon Excel（OCR）、轉出至 Excel（OCR）都成功；匯出設定的單頭／單身頁籤與全選不是視窗控制項，V0.2.0 改為 OCR 備援。

## 先前狀態（V0.1.0 Build 2）

- 欄位已定（使用者 2026-10-16）：`ItemFieldCatalog` 是唯一欄位清單，本機畫面與 Web 白名單共用；已用真實匯出檔在本機確認每個欄位都對得到（未提交）。

- 資料改由 ERP「轉出至 Excel」取得（使用者發現匯出更乾淨、不會錯），取代 Build 0 的逐頁籤讀畫面欄位；Build 0 未實機驗收。
- 流程：找 INVI02 → 最大化 → F6 → 一般選項 → 品號 = → 確定 → 核對品號（查無即結束）→ 基本資料1 → Ribbon Excel（OCR）→ 轉出至 Excel（OCR，layered popup）→ 匯出設定單頭／單身各按全選、填 TEMP 路徑 → 確定 → 檢視Excel資料? No → 解析 xlsx → 刪檔。
- 解析已用使用者提供的兩個真實匯出檔在本機驗證（未提交）：從資料瀏覽轉出只有「單頭資料」，從其他頁籤轉出有「單頭資料」＋「單身資料」，單頭內容逐字相同；156 欄。
- 介面比照 CYERPAutoInput（使用者 2026-10-16：未來合併成微服務，兩邊不能差太多）；查詢期間鎖實體滑鼠＋中央「自動查詢中」。
- 程式結構：
  - `ItemQueryService`：唯一的查詢流程 owner。之後 Web 入口直接呼叫它。
  - `Invi02Export`／`XlsxReader`：匯出檔解析（純邏輯，測試以程式產生的假匯出檔）。
  - `ItemFieldCatalog`：要交付的欄位與顯示名稱（唯一來源）。
  - `Invi02Layout`／`ItemCodeRules`／`TabStrip`：純規則，有單元測試。
  - `ExportFiles`：TEMP 路徑、等待寫完、讀取、刪除與啟動清理。
  - `Win32Automation`／`NativeMethods`／`VisionServices`／`OcrWordFinder`／`XlsxReader`／`AutomationSession`／`PhysicalMouseLock`／`CyVisual*`：由 CYERPAutoInput 複製的必要部分（使用者決定先複製，日後抽共用庫）。

## 已知 ERP 行為（使用者提供）

- 查詢快捷鍵 F6，開啟「設定查詢條件」；視窗會記住上次使用的頁籤（一般選項／進階設定）。
- 已在一般選項時點一般選項不會有反應（正常）。
- 查無品號：按確定後 ERP 轉到「資料瀏覽」，清單與表頭都是空的（不跳訊息框）。查到時同樣停在資料瀏覽。
- 最大化後頁籤座標經使用者截圖驗證（2026-10-16，1919 寬）：頁籤中心 = 頁籤控制項左緣 +56 +79i。
- 只有品號用一般選項查；其他條件要用進階設定：先「清除」舊條件，再選欄位、條件、輸入值後按「加入」。要用哪些條件之後決定。
- 一般選項條件順序：`<`、`<=`、`=`、`>=`、`>`、`%like%`、`like%`、`%like`。進階設定條件順序不同：`=`、`>=`、`<=`、`>`、`<`、`<>`、`%like%`、`like%`、`%like`。
- 轉出 Excel：Ribbon「Excel」下拉 →「轉出至 Excel」／「轉出全部符合查詢條件的資料」，無快捷鍵。匯出設定視窗標題與 INVI02 相同；檔案格式下拉、儲存檔案（黃色欄位）、單頭／單身欄位勾選（「儲存設定」會記住），確定要用滑鼠點。完成後跳「檢視Excel資料?」Yes／No，Yes 會開檔。
- 在「資料瀏覽」頁籤轉出不含單身（庫別）；其他頁籤轉出含單身，統一用「基本資料1」。
- 匯出檔由 ERP 自行產生（xlsx 內無 docProps，非 Excel 存檔），推測不需要安裝 Excel；無人值守電腦將裝 OpenOffice，需實機確認匯出正常。
- INVQ01（使用者截圖 2026-10-16）：從 INVI02「串查」→「庫存狀況查詢作業」開啟，已帶出該品號。下方頁籤：查各庫庫存量、查庫存可用量、查未來異動量、查歷史異動、查各批庫存量、查批號異動、暫出/入狀況查詢、品號廠商價格查詢、查詢結果；寬度隨文字（中心距頁籤控制項左緣 59、148、237、320、403、486、578、689、778）。開始查詢後自動跳到「查詢結果」，標題由「庫存狀況查詢作業(INVQ01)[公司]」變成「庫存狀況查詢作業[公司]-查各批庫存量」。查歷史異動頁有選擇庫別、選擇日期起／迄、庫別性質。
- INVQ01 轉出檔：單一工作表「查詢資料」，第 3 列標題（部分帶空白），A～X 是品號資料（只有第一列有值），查各批庫存量從「批號」欄起、查歷史異動從「日期」欄起每列一筆；品號資料與明細都有「庫存數量」，以明細起始欄切開。
- INVI02 頁籤順序：基本資料1、基本資料2、採購生管1、採購生管2、售價、成本、標準包裝、屬性類別頁、產品圖示、資料瀏覽。

## 實機驗收重點

INVQ01（V0.3.0）：

1. LOG `invq01: 串查 庫存狀況查詢作業 clicked`、`INVQ01 opened`；若出現 `INVQ01 tab sheets not found as windows`，請回報（改走 OCR）。
2. `開始查詢 clicked tab=…`、`query shown tab=… after_ms=`。
3. `轉出到Excel clicked`，之後 `dialog: export-dialog: child …` 會列出轉出設定視窗的內容，請把 LOG 傳回來核對。
4. 歷史異動：`history range shown from=… to=…` 兩個日期都要對。
5. 最後 `INVQ01 closed`，ERP 畫面不應留下 INVQ01。

INVI02：

1. LOG 的 `INVI02 found class=`：確認視窗類別（程式預期 `TfrmInvi02`，不同也能用，但請回報）。
2. 頁籤：LOG `tab ... by=geometry` 表示座標命中（一般選項、基本資料1）。
3. 「一般選項」條件設為 `=` 是否成功（LOG `operator set shown=`）。
4. 查無品號是否 1 秒左右就顯示「查無此品號」。
5. 轉出：LOG `ribbon Excel clicked`、`轉出至 Excel clicked`、`全選 clicked tab=單頭／單身`（單頭／單身頁籤 `by=geometry`）、`匯出設定 確定 clicked`、`檢視Excel資料? answered No`；失敗時 LOG 會列出 OCR 讀到的 Ribbon／選單文字。
6. 程式旁 `TEMP` 資料夾查完應該是空的。
7. 主視窗列出的欄位與庫別是否正確。
8. 無人值守電腦（OpenOffice）上 ERP 轉出是否正常。

## 產品規劃（使用者 2026-10-16）

- **短期**：在本機把查詢做好——決定要取用的欄位、基本介面。欄位一旦命名即為之後回傳 CYWEB 的資料契約，介面只是顯示它。
- **中期**：CYWEB 提供品號查詢介面，只有有權限的使用者可操作；CYWEB 送查詢請求給本程式，本程式查完回覆到 CYWEB 頁面。本機在內網，預定由本程式主動連出（輪詢或長連線）取得請求，不對外開埠；本程式需自己的裝置憑證（本機安全儲存，不進 source）；請求依序單筆處理。
- **長期**：與 CYERPAutoInput 結合成同一套自動化程式（抽共用庫、共用 ERP 操作鎖）。
- **分支：ERP 資料庫重整**：新資料庫重新編碼品號、品名等。需要以本程式查詢舊資料庫，把資料傳給另一台電腦上的 CYERPAutoInput 輸入新資料庫。待決事項：
  - 以 ERP 標題的公司／資料庫名稱限定只查舊資料庫（新舊名稱是否不同待確認）。
  - CYERPAutoInput 需新增 INVI02 新增品號的能力（目前只做 COPI08），屬寫入 ERP 的新功能，需另定安全規則。
  - 舊→新品號對照表的產生方式與存放位置。
  - 搬移需要的欄位範圍（可能比日常查詢多）。
  - 大量搬移的效率：畫面自動化每筆數秒且全程鎖滑鼠；一次性搬移可評估 ERP 的 Excel 匯出或唯讀查詢舊資料庫，畫面查詢留給日常單筆與補查（由使用者決定）。

## 後續工作

- CY Web 端：依補充交接加上歷史異動選項（日期選擇器）、批號與歷史異動顯示、放寬回報大小上限；結果保留改為 5 分鐘，每次查詢一律重跑（使用者 2026-10-16）。

- CY Web 接口：交接文件在 chihyuan-web `erp-item-query/handoff` 分支 `docs/handoff/ERP_ITEM_QUERY_BRIDGE_HANDOFF.md`；等 CY Web 端完成報告（`docs/handoff/ERP_ITEM_QUERY_BRIDGE_REPORT.md`）後實作 Agent 連線並核對。
- 進階設定查詢（清除 → 選條件 → 加入）。
- Web 入口（呼叫 `ItemQueryService`）。
- CYERPAutoInput 也取用 `Local\Chihyuan.SmartErp.UiAutomation` 鎖，兩支程式同機使用才真正互斥（目前只有 CYERPAutoQuery 會取用）。
- Icon：已採用 AITeam Icon Family `apps/erp-autoquery/AutoQ.ico`（AITeam main 3ec4ed8）；Windows 檔案總管／工作列顯示待實機確認。
- 兩支程式穩定後抽出共用庫，移除複製的程式碼。
