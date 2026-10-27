# CYERPAutoQuery Work Handoff

> 更新：2026-10-26。工程版 **V1.1.0**（未發行；V1.0.1 Build 2、V1.0.2 已合併）；目前正式版 **V1.0.0**（`VERSION=1.0.0`、`BUILD=0`，tag `cyerp-auto-query-v1.0.0`，2026-10-21 發布）。本文件只描述現況與待辦；規則以根 `REPOSITORY_RULES.md`、`REPO_POLICY.md` 與本專案 `PROJECT_RULES.md` 為準，各版變更見 `CHANGELOG.md`。之後的修改依規則升 Z 或 Build；使用者指示轉交 GPT 時依 `REPOSITORY_RULES.md` §11.4 與 `docs/work-orders/README.md`。

## 現況

- SMART ERP 唯讀查詢工具，本機視窗與 CY Web Agent 共用同一條查詢流程（`MainForm.ExecuteQueryAsync` → `ItemQueryService`），不另寫第二套。
- CY Web 端（chihyuan-web）已上線「ERP 查詢」（商品、客戶、廠商、未收款）與「進貨輔助」；協定見該 repo `docs/architecture/ERP_ITEM_QUERY_BRIDGE_CONTRACT.md`。
- 自動更新只認本專案正式 Release；V0.10.1 是第一個正式版，V1.0.0 為目前最新。

## 進行中：V1.1.0 — CY Web 關閉／暫停 ERP、閒置登出授權

- CY Web 0.9.38（chihyuan-web PR）新增 `erp_item_agent_control`；claim 回應帶 `control:{request,holdUntil}`，Agent 沒有工作時處理並 `POST /api/erp-agent/control` 回報。暫停期間 `ItemQueryService.HoldUntil` 讓自動登入不開 ERP。
- 閒置登出授權訊息由 `ItemQueryService.AnswerLicenceBoxesAsync`（GuardErpMessagesAsync 前後）回答。待實機：LOG `licence`、`control`。

## V1.0.6 — 報表「存成EXCEL檔」格式下拉是標準 Windows 下拉

- V1.0.5 實機：品號（含 INVQ01）全部成功；COPR15 切 xlsx 失敗。V1.0.6 以 CB_GETLBTEXT 讀選項、上下鍵選 xlsx。待實機：LOG `format items=… current=… xlsx=…`、`format shown=`。

## V1.0.5 — 伺服器剪貼簿被遠端桌面同步換掉（已實機修正）

- V1.0.4 實機（2026-10-27）：自動登入、INVI02 查詢成功；匯出路徑貼上讀回為空（INVI02 重試成功、INVQ01 兩次失敗），失敗後匯出設定視窗留著讓 ERP 卡住。推定是 AnyDesk 剪貼簿同步。V1.0.5 貼上前確認剪貼簿內容，失敗時取消匯出設定視窗。待實機：LOG 是否出現 `clipboard not holding the text`；若仍常發生，請在 AnyDesk 關閉剪貼簿同步。

## V1.0.4 — ERP 人數已滿（CY Web 0.9.37 已部署）

- 使用者 2026-10-27：登入後跳「Leader作業元件」「人數已滿!!」，按 OK 後 ERP 自行關閉。V1.0.4 偵測後按掉並回報 `ERP_SEATS_FULL`；CY Web 須先部署認得此代碼的版本（chihyuan-web 0.9.36）。待實機：LOG `ERP 人數已滿`。

## 進行中：V1.0.3 — 伺服器上報表匯出格式預設 xls

- V1.0.2 伺服器實機（2026-10-27）：COPR15 按 XLSX 後「存成EXCEL檔」顯示 xls 而停止。V1.0.3 會切換到 xlsx。待實機：進貨輔助或銷貨銷退查詢，LOG `format items=… shown=…`。若 LOG 顯示 `XLSX clicked by=geometry` 卻開的是 XLS 按鈕，需改按鈕位置（伺服器顯示比例可能不同）。
- 另一次「按「作業速查」後游標不在 ERP 的搜尋框」出現在前一次失敗後，下一次查詢即正常，先觀察。

## V1.0.2 — 自動登入把啟動畫面當主視窗（已實機：伺服器自動登入成功）

- V1.0.1 Build 2 實機 LOG（2026-10-26）：ERP 沒開時查詢，啟動 MainMenu.exe 後 1.9 秒就把啟動畫面 `TfrmLogo`（標題 frmLogo）當成主視窗，跳過登入。V1.0.2 備援判斷只認 `SmartERP_iSM_MainMenu`。待實機：ERP 沒開時從 Web 查一筆，LOG 應出現 `login form class=…`、`logged in after_ms=`。

## V1.0.1 Build 2 — ERP 行程診斷與強制重開收斂（PR #555）

- Build 0／1 由 GPT 實作（使用者 2026-10-22 要求避免誤關共用行程），報告見 `docs/handoff/ERP_PROCESS_DIAGNOSTICS_CLAUDE_HANDOFF_20261023.md`。Claude 審查發現 Build 1「只處理 AutoQuery 親自啟動的 ERP」會讓閒置關閉與無回應重開在 ERP 人工開啟或 AutoQuery 自動更新後失效，且作業視窗不在 MainMenu 行程。
- 使用者 2026-10-26 以伺服器工作管理員確認：MainMenu 在 `D:\iSmartERPX64\C_dsbin\`（工作階段 1）；品號、銷貨單、進貨單都開在 `S_dsbin\SMCOPIS01S.exe`；另有 SMDispatcher、SMCounterS、AUC 等 ERP 關著也在跑的服務。
- Build 2：強制重開改為以視窗找程式（`ErpRestartTargets`，有單元測試），閒置關閉恢復 V1.0.0 行為，診斷另列擁有 ERP 視窗的行程。
- 待實機：一般查詢、閒置關閉不受影響；ERP 真的卡住時看 LOG `ERP window owner pid=… name=… session=… main=…` 與 `ERP process closed／killed`。不刻意弄當機測試。

## 功能與實機紀錄

| 功能 | ERP 作業 | CY Web 類型 | 實機紀錄（依 CHANGELOG） |
|---|---|---|---|
| 品號查詢 | INVI02 | `ITEM_NO` | 已實機：本機 A00124、Web C00154（V0.2.0 Build 3–4） |
| 品名查詢 | INVI02 進階設定 | `ITEM_NAME` | 已實機：「針灸針」轉出 81 筆（V0.3.0 Build 8） |
| 批號／歷史異動 | INVQ01 | `ITEM_NO`（`includeHistory`） | 已實機：A00124 歷史異動 426 筆（V0.3.0 Build 8） |
| 客戶資料 | COPI01 | `CUSTOMER_NO`／`CUSTOMER_NAME` | 已實機至開啟、F6、頁籤（V0.5.0 Build 4），其後修正未另記錄結果 |
| 供應廠商資料 | PURI01 | `SUPPLIER_NO`／`SUPPLIER_NAME` | 文件無實機紀錄（視窗類別、頁籤座標沿用 COPI01 推測） |
| 銷貨銷退單 | COPR15／COPR16 | `SALES` | 已實機：客戶 AC0016，依使用者 LOG 修正至 V0.9.0 Build 8（含「報表沒有資料」處理） |
| 單筆銷貨／銷退單 | COPR15／COPR16 | `ORDER` | 文件無實機紀錄 |
| 進貨退貨單 | PURR18／PURR22 | `PURCHASES` | 已實機：廠商 B0009（V0.9.0 Build 2–3） |
| 未收款 | ACRR18 | `RECEIVABLE` | 文件無實機紀錄 |
| 進貨輔助 | COPR15／COPR16＋INVI02 批次 | `RESTOCK` | 使用者以 CY Web 實際查詢並回饋，修正至 V0.10.2 Build 3–4（批次查庫存、含贈品量、品號含「-」） |
| ERP 自動登入／閒置關閉／開機自動開啟 | ERP 登入畫面 | — | 自動登入已實機（V1.0.2，伺服器 2026-10-27）；閒置關閉、ERP 關閉確認訊息尚未實機 |
| 自動更新（含提示條、倒數、還原） | GitHub Release | — | V0.10.1 起以正式 Release 實測；文件未記錄結果 |

「文件無實機紀錄」不代表失敗，只是 CHANGELOG／交接沒有寫下結果；使用者確認後更新本表。

## 待實機確認

- 閒置關閉：作業與 ERP 的閒置關閉、ERP 離開確認訊息（LOG `idle`）。
- 進貨輔助：條件框實際字數上限（LOG `condition box limit=`）、每批 100 個品號的耗時（LOG `restock`）。
- PURI01：視窗類別（預期 `TfrmPuri01`，不符時以標題辨識）、頁籤座標（不符時 OCR）、資料瀏覽「轉出全部」欄位名稱（廠商代號、簡稱、公司全名、TEL、FAXNO、統一編號）。
- PURR18／PURR22：頁籤座標（量自截圖，不符時 OCR）、PURR22 進階選項內容、「驗收日」選項與「僅列印…」勾選狀態能否以 BM_GETCHECK 讀到（LOG 有記錄）。
- 無人值守電腦（OpenOffice）上 ERP 轉出是否正常。
- Icon：AITeam Icon Family `apps/erp-autoquery/AutoQ.ico` 在 Windows 檔案總管／工作列的顯示。

## 程式結構

- **查詢流程 owner**：`ItemQueryService`（品號、品名、INVQ01、建立作業共用流程）；`.Sales`（COPR15／COPR16、單筆單據）、`.Purchases`（PURR18／PURR22）、`.Receivables`（ACRR18）、`.Restock`（進貨輔助）、`.Login`（自動登入、閒置關閉、無回應重開）。
- **純規則／解析（有單元測試）**：`Invi02Layout`（含 `ItemCodeRules`、`TabStrip`）、`Invq01Layout`、`ItemNameSearch`（含 `ItemCodeConditions`）、`PartyQuery`（COPI01／PURI01）、`SalesOrderQuery`、`PurchaseQuery`、`ReceivableQuery`、`RestockQuery`、`ErpReport`、`ErpForm`、`ItemQueryModels`、`ItemFieldCatalog`（唯一欄位清單，也是 Web 白名單）、`XlsxReader`、`UpdateCheck`。
- **CY Web Agent**：`ErpAgentProtocol`（查詢類型、回報格式）、`ErpAgent`（claim／complete／progress 迴圈）、`ErpAgentSettings`／`ErpAgentSettingsForm`（DPAPI Token）。
- **設定與更新**：`ErpLoginSettings`／`ErpLoginSettingsForm`、`StartupRegistration`、`AppUpdater`、`UpdateVisuals`、`MainForm.Update`、`AppVersionInfo`。
- **畫面**：`MainForm`（`.Erp`、`.Purchases`、`.Update`）、`OrderDetailForm`（未收款憑證另開視窗）。
- **由 CYERPAutoInput 複製（日後抽共用庫）**：`Win32Automation`、`NativeMethods`、`VisionServices`、`OcrWordFinder`、`OcrTextNormalizer`、`AutomationSession`、`PhysicalMouseLock`、`CyVisualControls`、`CyVisualTheme`、`UpdateVisuals`／`MainForm.Update`（兩支程式同一套更新顯示）。
- **檔案**：`ExportFiles`（TEMP 路徑、等待寫完、讀取、刪除、啟動清理）、`AppLogger`。
- **測試**：`tests/CYERPAutoQuery.Tests`（純邏輯，任何 OS 可跑）；`LiveCyWebTests` 為 opt-in（需 `CYQ_LIVE_BASE`／`CYQ_LIVE_TOKEN`）。

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

## INVQ01 歷史異動日期輸入（使用者確認，V0.3.0 Build 2～8）

- 起迄都有：點起日年份位置 → Home → 起日 `yyyyMMdd` → Enter（自動跳到迄日，不點）→ Home → 迄日 `yyyyMMdd`。
- 只有起日：點起日 → Home → `yyyyMMdd`。只有迄日：直接點迄日 → Home → `yyyyMMdd`。
- 兩個都沒填：本機查詢不動日期欄；CY Web 由 Worker 補齊起迄後傳入。
- Home：日期欄點過一次後會顯示「    /  /  」遮罩，點擊可能落在第二位以後（使用者 2026-10-16）。輸入後讀回，不符即停止、不按開始查詢。
- 實機慢點（V0.3.0 LOG）：整條 Ribbon OCR 約 2 秒、整窗頁籤 OCR 約 2.6 秒；Build 6～7 改為只讀按鈕所在群組（並記住群組）與頁籤先點座標。

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

- 依「待實機確認」逐項補實機結果，更新上方功能表。
- CYERPAutoInput 尚未取用 `Local\Chihyuan.SmartErp.UiAutomation` 鎖；兩支程式同機使用時要真正互斥，需在 CYERPAutoInput 加上。
- 兩支程式穩定後抽出共用庫（Win32、OCR、滑鼠鎖、視覺主題、更新顯示），移除複製的程式碼。
- ERP 資料庫重整的搬移需求（見產品規劃）待使用者決定。
