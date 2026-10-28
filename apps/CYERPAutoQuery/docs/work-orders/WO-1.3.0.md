# WO-1.3.0 庫存盤點：讀盤點單（INVR11）與寫回 ERP（INVI14／INVI06）

- 狀態：待執行
- 分支：cyerp-auto-query/stocktake（程式）、governance/cyerp-auto-query-stocktake（規則）　PR：#<編號>（執行者填）
- 依據：本專案 `PROJECT_RULES.md`；chihyuan-web `docs/development/work-orders/WO-0.9.52.md`（CY Web 端，同一份協定）

## 0. 執行限制（必讀）
- 不合併、不發 Release、不推 CY Web；只在指定分支提交並開 PR（CI 會產生測試包）。
- 不新增或修改 `.github/workflows/*`。
- 測試與文件只用假資料；不得提交 ERP 匯出檔、LOG、截圖中的營運資料。
- **本單是本程式第一次寫入 ERP**。規則修改必須先在 `governance/cyerp-auto-query-stocktake` 分支另開 PR（`PROJECT_RULES.md`＋根 `GOVERNANCE_VERSION`／`GOVERNANCE_CHANGELOG.md`，跑治理檢查），程式 PR 在完成報告註明依賴該 PR。
- 不改變已定案事項；確需偏離時在完成報告說明，交由審查決定。

## 1. 背景（使用者原話，2026-10-29）
- 讀盤點單：「同樣速查開啟INVR11，選擇盤點底稿編號這邊按他的按鈕會開啟選單…點選261001(或是新的盤點單)後，按enter或確定就會帶回來，選擇列印那邊務必選擇兩者都列印，然後按即時報表會打開報表，按左上第一個按鈕(不要按到向下三角形)，可打開欄位選單，單擊N可把他切成y，需要把儲位、批號、盤點數量打開，用一樣的excel匯出方式就可取得盤點明細檔」
- 寫回（方法 2，逐筆）：「INVI14作業，他可以針對單筆輸入，查詢->一般選項->盤點底稿編號和序號，查完會在資料瀏覽，點到詳細欄位之後按修改可以填盤點數量」「修改輸入完盤點數量，按儲存即可」
- 新增批號（方法 1）：「INVI06作業，查詢->一般選項->盤點底稿編號填入查打開該筆盤點單，按修改…如果是原本沒在底稿上的批號或品號，在盤點的最下面一列按下就會叫出新的一列，可以自由輸入品號、批號、盤點數量。輸入完畢按儲存再按確認就完成」
- 「程式核對全部相符之後按」（INVI06 確認）
- 「這兩種方法的儲位填寫都不會更新儲位，更新儲位的方式後續告知你」

## 2. 已定案事項
1. **讀盤點單**（`STOCKTAKE_SYNC`）：作業速查開 INVR11 →「選擇盤點底稿編號」右側按鈕開「F2開窗查詢」→ 讀清單（底稿編號、盤點日期）→ 對每張要讀的底稿：在清單點該列 → 確定（或 Enter）帶回，讀回欄位＝底稿編號 →「選擇列印」選「二者皆列印」並讀回 →「即時報表」→ 報表視窗按工具列**第一個按鈕的圖示本體**（不是右側向下三角形）開「欄位資訊」→ 將「儲位」「批號」「盤點數量」三列的「顯示」由 N 點成 Y（已是 Y 不點，點完讀回）→ 確定 → 用既有報表 XLSX 匯出流程取得檔案 → 解析。
2. **寫回既有列**（INVI14，逐筆）：速查開 INVI14 → F6 → 一般選項：盤點底稿編號「=」sheetNo、序號「=」seq → 確定 → 資料瀏覽恰好一筆 → 切「詳細欄位」→ 讀回底稿編號、序號、品號、批號，必須等於工作中的值（不符就不寫、該列失敗）→「修改」→ 盤點數量欄寫入 quantity 並讀回 →「儲存」→ 讀回確認已存（欄位值、畫面回到檢視狀態）。
3. **寫入新增批號**（INVI06）：開 INVI06 → 查詢一般選項「盤點底稿編號」= sheetNo → 開啟該單 →「修改」→ 表格最下面一列按下（往下）產生新列 → 填品號、批號、盤點數量（逐欄讀回）→ 全部新增完「儲存」→ 從新列讀出 ERP 產生的序號回報 `erpSeq`。
4. **核對與確認**：所有寫入完成後，以 `STOCKTAKE_SYNC` 相同流程匯出該張 INVR11，逐列核對：工作中每一列（既有列以 seq、新增列以 erpSeq）的「盤點數量」＝quantity。**全部一致才**到 INVI06 開該單按「確認」並讀回確認狀態；有任何不一致不得確認，回報 `mismatches`。
5. **可重試**：寫入相同數值是安全的；同一工作重送時，INVI14 讀回已等於目標值的列直接視為成功不再修改；新增批號列若 INVR11 已有同品號＋批號的列（例如上次已新增），不再新增、回報既有序號。
6. **只動這些**：INVI14 的「盤點數量」、INVI06 新增列的品號／批號／盤點數量、INVI06「確認」。不改儲位、備註或其他欄位；不刪除任何列；不使用「取消確認」「單據作廢」。
7. 任一步驟失敗：按「取消」離開修改模式（不留半套修改），回報失敗列與原因；不按確認。
8. **協定**：見下方，與 CY Web WO-0.9.52 完全一致；欄位名稱、上限、逾時不得自行更改。

### 協定（兩張工單共用，兩邊必須完全一致）

新增兩種 ERP 查詢類型，走既有 `/api/erp-agent/item-queries/claim`／`/complete` 與 CY Web 排程佇列（每人 3 筆、依送入時間、暫停時可加入佇列等既有規則一律適用）。所有數量在 JSON 中一律為**數字字串**（ERP 顯示值，可為負數、可有小數），日期 `YYYY-MM-DD`。

**`STOCKTAKE_SYNC`：讀盤點單（INVR11）**

- claim：`{ id, queryType:"STOCKTAKE_SYNC", expiresAt, skipSheets: string[], onlySheet: string|null }`
  - `skipSheets`：CY Web 已讀過或設為忽略的底稿編號（最多 500 個）。
  - `onlySheet`：不為 null 時是「重新整理」，只讀這一張（即使它在 skip 內）。
- complete（`outcome` 為 `FOUND`／`NOT_FOUND`／`FAILED`，共通欄位同其他類型）：
  - `sheets: [{ sheetNo, date }]`：F2 開窗清單看到的**所有**底稿（最多 200 筆）。`onlySheet` 模式可只回該張。
  - `details: [{ sheetNo, rows: [...] }]`：這次實際讀了明細的底稿（`onlySheet` 模式恰好一張；更新模式＝清單中不在 skip 的每一張）。
  - `rows` 每列（最多 3,000 列／張）：`{ seq, itemCode, itemName, spec, warehouseName, location, unit, batch, bookQty }`。`seq` 為 INVR11 的「底稿序號」（5 位數字字串，張內唯一）；`bookQty` 為 INVR11 的「盤點數量」（ERP 預設帶入帳面數量）；`location`、`batch` 可為空字串。
  - `NOT_FOUND`：沒有任何需要讀的底稿（清單全在 skip 內）；此時仍回 `sheets`、`details:[]`。
- RUNNING 逾時 1,800 秒；complete 上限 900 KiB。

**`STOCKTAKE_WRITE`：寫回 ERP（INVI14 逐筆、INVI06 新增批號與確認）**

- claim：`{ id, queryType:"STOCKTAKE_WRITE", expiresAt, sheetNo, rows: [...] }`
  - `rows` 每列（最多 3,000）：`{ key, seq, itemCode, batch, quantity }`。
    - 既有列：`seq` 為底稿序號、`key` 同 `seq`。
    - 新增批號：`seq` 為 null、`key` 為 CY Web 的列鍵（例如 `N0001`）。
  - 只送「實際數量 ≠ 帳面數量」的既有列，以及全部新增批號列（規則見 CY Web WO-0.9.52 §2）。
- complete：`{ outcome:"FOUND"|"FAILED", sheetNo, results:[{ key, ok, erpSeq, message }], confirmed, mismatches:[{ key, seq, itemCode, batch, expected, actual }] }`
  - `results` 每列一筆：`ok` 表示已寫入並讀回一致；新增批號列的 `erpSeq` 為 ERP 新產生的序號（既有列同 `seq`）；失敗時 `message` 為給人看的短句（不含路徑或帳密）。
  - `confirmed`：INVR11 核對全部一致，且已在 INVI06 按「確認」。
  - `mismatches`：INVR11 核對時不一致的列（有任何一列就不得按確認）。
- RUNNING 逾時 3,600 秒；complete 上限 900 KiB。
- Agent 每寫完一列以既有 `POST /api/erp-agent/item-queries/{id}/progress` 回報 `{ stage:"STOCK", done, total }`，CY Web 顯示「已寫入 x / y 列」。

## 3. 範圍
1. 規則 PR（`governance/cyerp-auto-query-stocktake`）：`PROJECT_RULES.md` 「只讀」條目改為「只讀，**例外**：CY Web 庫存盤點的寫回只允許 §2.2～2.6 列出的作業、欄位與按鈕；每次寫入前核對識別欄位、寫入後讀回；確認前以 INVR11 全數核對」。同步 `GOVERNANCE_VERSION`（2.3.57 → 下一版）與 `GOVERNANCE_CHANGELOG.md`。
2. 協定：`ErpAgentProtocol` 新增兩種類型、claim 解析（skipSheets、onlySheet、rows）、complete 組裝；`ErpAgent` 的 `queryTypes` 加入兩者；寫回時每完成一列回報 progress。
3. 純邏輯（可測）：`StocktakeQuery.cs`（新檔）——INVR11 匯出檔解析（欄位：底稿編號、底稿序號、品號、品名、規格、庫別名稱、儲位、單位、批號、盤點數量；「初盤數量」那欄是底線，忽略）、F2 清單 OCR 結果解析（底稿編號 6 位以上數字、日期 yyyy/MM/dd）、核對（工作 rows 對 INVR11 rows → mismatches）、重試時的略過判斷。
4. ERP 操作：`ItemQueryService.Stocktake.cs`（新檔）——讀盤點單、INVI14 逐筆寫入、INVI06 新增批號、INVI06 確認。沿用既有 `OpenFormAsync`（作業速查）、F6 查詢條件、`RibbonClickAsync`、報表 XLSX 匯出、`SetControlText`／讀回、`GuardErpMessagesAsync`、閒置／授權訊息處理、滑鼠鎖。
5. MainForm：本機視窗不必新增手動入口（只給 Agent 用）；狀態列顯示「盤點更新／盤點寫回」。
6. 文件：CHANGELOG、README（盤點一節、寫入範圍）、WORK_HANDOFF（現況、待實機）；版本 `VERSION`=1.3.0、`BUILD`=0（工程驗證，未發行）。

## 4. 不做
- 第二階段：盤點盈虧明細表、盤點資料更新作業、核對庫存異動、盤點資料清除作業。
- 儲位更新（使用者之後另外說明）。
- 本機視窗的盤點功能。

## 5. 實作指引
- F2 開窗清單是 Delphi grid，非 Win32 清單：以 OCR 讀可見列；列數多於可見範圍時用下方「1 / N」與下一頁／捲動讀完（LOG 記錄讀到的筆數與 N）。點列時以 OCR 找到的底稿編號文字中心為點擊點，帶回後必須讀回欄位值。
- 「選擇列印」是下拉：沿用 `NativeMethods.ComboItems`（CB_GETLBTEXT）找「二者皆列印」，讀回確認。
- 報表工具列第一個按鈕：在報表視窗工具列以相對位置點圖示左半（避開右側三角形），等「欄位資訊」視窗（標題「欄位資訊」）出現；在其表格以 OCR 找「儲位」「批號」「盤點數量」所在列，點同列「顯示」欄；每點一次 OCR 讀回為 Y；已是 Y 不點（不要把 Y 點回 N）。欄位資訊會被 ERP 記住，之後可能已是 Y。
- INVI14 詳細欄位的欄位以 `EnumerateChildren`＋標籤相對位置辨識；寫入用 `SetControlText` 後讀回，必要時再用打字。按「修改」「儲存」用 Ribbon OCR（只辨識按鈕群組，比照 V1.2.1）。
- INVI06 表格新增列：在最後一列「盤點數量」欄點一下後按 ↓ 產生新列；逐欄輸入後讀回（表格是 cx grid，讀回以 OCR 該列）。新增完按「儲存」，再讀新列序號。
- 每個寫入步驟 LOG：`stocktake: write seq=… item=… batch=… from=… to=… ok`（數量以長度或遮蔽方式記錄，比照既有不記品號內容的 LOG 規則；品號可記，數量不記）。
- 寫入期間不得被閒置關閉或 CY Web「關閉 ERP」打斷（這兩者本來就只在沒有工作時處理）。

## 6. 驗收清單
- [ ] 規則 PR：`PROJECT_RULES.md` 寫入例外範圍、`GOVERNANCE_VERSION`、`GOVERNANCE_CHANGELOG.md`，治理檢查通過。
- [ ] `STOCKTAKE_SYNC`：更新模式只讀不在 skip 的底稿；`onlySheet` 只讀該張；回報格式符合協定。
- [ ] 欄位資訊只把 N 點成 Y，讀回確認；已是 Y 不動。
- [ ] `STOCKTAKE_WRITE`：INVI14 每列寫入前核對底稿編號、序號、品號、批號；不符該列失敗、不寫。
- [ ] 新增批號在 INVI06 新增列並回報 erpSeq；重試不重複新增。
- [ ] INVR11 全數核對一致才按 INVI06 確認；有不一致回報 mismatches、不確認。
- [ ] 失敗時離開修改模式，不留半套修改。
- [ ] `dotnet test tests/CYERPAutoQuery.Tests` 通過，`StocktakeQuery` 有解析、核對、重試略過的測試（假資料）。
- [ ] `dotnet build` 無錯誤；PR 的 CYERPAutoQuery Build 成功並產生測試包。
- [ ] `VERSION`／`BUILD`、`CHANGELOG.md`、`README.md`、`WORK_HANDOFF.md` 已更新。
- [ ] commit 身分使用 `simonliu1118-byte`。

## 7. 實機驗證要求
（CY Web 0.9.52 部署後，由使用者在伺服器操作；寫回請先用一張測試用底稿或確認可承受的盤點單。）
1. 先在 CY Web 把舊底稿設為忽略 →「更新盤點單」：LOG `stocktake: sheets=… read=…`、欄位資訊點選結果。
2. 填完初盤、兩次確認 → 自動寫回：LOG 每列 `stocktake: write …`、INVR11 核對結果、`confirmed`。
3. 刻意讓一列在 ERP 端不一致（例如寫回前人工改掉）→ 應回報 mismatches 且不確認。
4. 附 LOG 中 `stocktake`、`report`、`ribbon`、`export` 段落。

## 8. 完成報告（執行者填）

## 9. 審查紀錄（審查者填）
