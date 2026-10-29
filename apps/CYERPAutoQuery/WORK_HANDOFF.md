# CYERPAutoQuery Work Handoff

> 更新：2026-10-29。目前正式版 **V1.3.0**（`VERSION=1.3.0`、`BUILD=0`，tag `cyerp-auto-query-v1.3.0`）；較舊的 Release 已依使用者要求刪除，各版內容在 `CHANGELOG.md`。本文件只描述現況與待辦；規則以根 `REPOSITORY_RULES.md`、`REPO_POLICY.md` 與本專案 `PROJECT_RULES.md` 為準，各版變更見 `CHANGELOG.md`。之後的修改依規則升 Z 或 Build；使用者指示轉交 GPT 時依 `REPOSITORY_RULES.md` §11.4 與 `docs/work-orders/README.md`。

## 現況

- SMART ERP 唯讀查詢工具，本機視窗與 CY Web Agent 共用同一條查詢流程（`MainForm.ExecuteQueryAsync` → `ItemQueryService`），不另寫第二套。
- 部署在公司伺服器（Windows 工作階段 1），作為 CY Web 的查詢 Agent；開機自動啟動、ERP 沒開時自動開啟並登入，閒置自動關閉。
- CY Web 端（chihyuan-web，目前 V0.9.43）：ERP 查詢頁（商品、客戶、供應商、進貨輔助、未收款）、排程佇列（每人最多 3 筆）、ERP 關閉／暫停（含「關閉到…」與暫停時加入佇列）；協定見該 repo `docs/architecture/ERP_ITEM_QUERY_BRIDGE_CONTRACT.md`。本程式只依 claim 回應的 `control.holdUntil` 決定暫停期間不開 ERP，暫停中排隊的工作由 CY Web 在暫停結束後才派發。
- 自動更新只認本專案正式 Release。V1.1.0 Build 3 → V1.2.0 的自動更新已在伺服器實際完成（2026-10-28 LOG）。

## V1.3.0 — 庫存盤點（已發行，待實機）（[WO-1.3.0](docs/work-orders/WO-1.3.0.md)，[PR #572](https://github.com/simonliu1118-byte/CYapps/pull/572)）

- GPT 完成後由 Claude 審查並重寫 Win32 點選部分，依據是使用者截圖 104–115，見 `docs/handoff/CYERPAutoQuery-WO-1.3.0-CLAUDE-REVIEW.md` §9。
- 程式在 `ItemQueryService.Stocktake.cs`（INVR11）與 `ItemQueryService.StocktakeWrite.cs`（INVI14、INVI06）；流程協調在 `StocktakeExecution.cs`。
- 前置條件是治理 [PR #571](https://github.com/simonliu1118-byte/CYapps/pull/571) 和 CY Web WO-0.9.52 [PR #307](https://github.com/simonliu1118-byte/chihyuan-web/pull/307)。
- 尚未在實機上跑過（使用者 2026-10-29 同意直接發行）。第一次使用請用一張測試底稿，依序驗證：讀取、單筆寫入、新增批號、確認；失敗看 LOG 裡 OCR 讀到的文字。

## 功能與實機紀錄（2026-10-28 整理）

| 功能 | ERP 作業 | CY Web 類型 | 實機紀錄 |
|---|---|---|---|
| 品號查詢 | INVI02 | `ITEM_NO` | 伺服器正常（2026-10-28：A00124、C00154） |
| 品名查詢 | INVI02 進階設定 | `ITEM_NAME` | 已實機：「針灸針」轉出 81 筆（V0.3.0 Build 8） |
| 批號／歷史異動 | INVQ01 | `ITEM_NO`（`includeHistory`） | 伺服器正常（2026-10-28：A00124 批號 3 筆） |
| 客戶資料 | COPI01 | `CUSTOMER_NO`／`CUSTOMER_NAME` | AC0007 正常；AC0001 曾在 V1.2.0 找 Ribbon「Excel」時 OCR 失敗，V1.2.1 修正（待觀察） |
| 供應廠商資料 | PURI01 | `SUPPLIER_NO`／`SUPPLIER_NAME` | 伺服器正常（2026-10-28：B0003） |
| 銷貨銷退單 | COPR15／COPR16 | `SALES` | 使用者 2026-10-28 確認正常 |
| 單筆銷貨／銷退單 | COPR15／COPR16 | `ORDER` | 文件無實機紀錄 |
| 進貨退貨單 | PURR18／PURR22 | `PURCHASES` | 使用者 2026-10-28 確認正常 |
| 未收款 | ACRR18 | `RECEIVABLE` | 伺服器正常（2026-10-28：全部客戶 131 張結帳單） |
| 進貨輔助 | COPR15／COPR16＋INVI02 批次 | `RESTOCK` | 伺服器正常（2026-10-28：2 天 217 個品號，約 2 分 15 秒；V1.1.0 Build 2 為 6 分半） |
| 自動登入、閒置關閉 | ERP 登入畫面 | — | 自動登入正常；作業閒置 10 分鐘關閉正常（2026-10-28 LOG） |
| CY Web 關閉／暫停 | — | claim `control` | 立即關閉、關閉 15 分鐘正常（使用者 2026-10-28）；強制關閉未遇到 |
| 自動更新 | GitHub Release | — | V1.1.0 Build 3 → V1.2.0 正常（2026-10-28 LOG） |

「文件無實機紀錄」不代表失敗，只是沒有留下結果；使用者確認後更新本表。

## 待實機確認

- V1.2.1：COPI01 找 Ribbon「Excel」，LOG 應為 `clicked Excel in group=Excel`；若仍出現 `whole Ribbon OCR failed`，看之後的逐群組結果。
- ERP 人數已滿（LOG `ERP 人數已滿`）、強制關閉 ERP（LOG `ERP window owner`／`ERP process closed`）、閒置登出授權訊息（LOG `licence`）：遇到時對照 LOG，不刻意製造。
- 單筆銷貨／銷退單（`ORDER`，未收款憑證另開視窗）。
- 進貨輔助條件框實際字數上限（LOG `condition box limit=`）。
- 無人值守電腦（OpenOffice）上 ERP 轉出是否正常；Icon 在檔案總管／工作列的顯示。

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

- 伺服器（使用者 2026-10-26～27）：MainMenu 在 `D:\iSmartERPX64\C_dsbin\`（工作階段 1）；品號、銷貨單、進貨單都開在 `S_dsbin\SMCOPIS01S.exe`；另有 SMDispatcher、SMCounterS、AUC 等 ERP 關著也在跑的服務（強制重開不碰）。伺服器有 AnyDesk，剪貼簿會被遠端桌面同步換掉，所以輸入以直接寫入欄位為主。報表「匯出成功」的檔案存在桌面。

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
- 實機慢點（V0.3.0 LOG）：整條 Ribbon OCR 約 2 秒、整窗頁籤 OCR 約 2.6 秒；Build 6～7 改為只讀按鈕所在群組（並記住群組）與頁籤先點座標。V1.2.1 起找不到記住的群組時先試同名群組，整條 Ribbon 只截到群組下緣。

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
