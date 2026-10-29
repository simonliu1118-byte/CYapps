# CYERPAutoQuery WO-1.3.0 — GPT 實作／Claude 最終審查 Handoff

> **狀態：Claude 審查並重寫 Win32 部分（§9）；#571、#572、#565 已合併，V1.3.0 已發行（2026-10-29），待實機測試。** 本檔為所有 GPT 修改的唯一逐檔索引及後續 Claude 審查入口；Claude 最終意見在 §9 填寫，GPT 不代填。

## 1. 來源、規則與工作分支

- 核准工作單：[PR #565](https://github.com/simonliu1118-byte/CYapps/pull/565)，`apps/CYERPAutoQuery/docs/work-orders/WO-1.3.0.md`，HEAD `9a101c26e4a4704186bbbb15665d808958bc3eda`。已全文查閱 §0–§9，兩張工單的 `STOCKTAKE_SYNC`／`STOCKTAKE_WRITE` 協定字句比對一致。
- CY Web 對端：[chihyuan-web PR #307](https://github.com/simonliu1118-byte/chihyuan-web/pull/307)，WO-0.9.52 已完成離線驗收交 Claude，尚未據此取得 ERP 真機操作許可。
- 共通規則：`REPOSITORY_RULES.md` → `REPO_POLICY.md` → `apps/CYERPAutoQuery/PROJECT_RULES.md`。CYapps／AITeam `COMMON_RULES_VERSION=2.10.0`。Public repo 只能提交假資料。
- **前置治理 PR [#571](https://github.com/simonliu1118-byte/CYapps/pull/571)**：`governance/cyerp-auto-query-stocktake` 只更新 `PROJECT_RULES.md` 的有界寫入例外、根 `GOVERNANCE_VERSION=2.3.58`／`GOVERNANCE_CHANGELOG.md`。Governance Check 已成功；請 Claude 確認此 PR 後，才能批准實作 PR 合併。
- **程式 Draft PR [#572](https://github.com/simonliu1118-byte/CYapps/pull/572)**：`cyerp-auto-query/stocktake`；target 版本 `V1.3.0 Build 0`，不合併、不發 Release、不修改 workflow；為進行 Windows 自動 Build，曾暫將 PR base 調向 `main`（Windows Build workflow 僅處理針對 main 的 PR），最後交 Claude 前應恢復成 `cyerp-auto-query/wo-1.3.0-stocktake` 讓審核只看實作差異。

## 2. 本次 GPT 逐檔修改紀錄

| 位置 | 內容與責任 |
| --- | --- |
| `apps/CYERPAutoQuery/StocktakeQuery.cs` | 單一純邏輯來源：F2 OCR 底稿編號／日期解析、INVR11 XLSX 明細欄位／底稿序號／數量字串驗證；`Quantity` 拒絕超精度；同一欄不混入初盤欄；`AlreadyApplied` 重試判斷；`Verify` 與 `VerifyEntireSheet` 逐列核對整張底稿（含未寫入列與意外多出／消失的列，後者 fail closed）。 |
| `apps/CYERPAutoQuery/StocktakeExecution.cs` | 唯一工作協調器與 `IStocktakeErpDriver` 安全介面：SYNC skip／only 模式、WRITE 前檢查 seq／itemCode／batch、相同值跳過、已存在新批號時避免重複新增、逐列進度、INVR11 全張前後對照，任何失敗不呼叫 `ConfirmAsync`；返回每一列結果供 CY Web `WRITE_FAILED` 顯示。 |
| `apps/CYERPAutoQuery/ErpAgentProtocol.cs` | 增加兩種型別常數、job typed 欄位、ParseStocktakeClaim 入口與顯示文字。**安全 gate：尚未有已驗收的 Win32 driver，`SupportedTypes` 暫不對 CY Web 宣告這兩種工作**，避免工程包意外觸發真 ERP 修改。 |
| `apps/CYERPAutoQuery/ErpAgentProtocol.Stocktake.cs` | Stocktake claim 型別／範圍校驗、同步與寫回 complete JSON、900 KiB 限額、既有／新批號 ERP 序號讀回、confirmed 只有結果全成功才可為 true。 |
| `apps/CYERPAutoQuery/tests/CYERPAutoQuery.Tests/{StocktakeTests.cs,StocktakeExecutionTests.cs}` | 假資料 xUnit：F2 解析、INVR11 數量與欄位、strict claim、完整 mismatch／unexpected rows、write `erpSeq`、skip/only、假 Driver idempotent、新批號重試不重複、識別錯誤零變更、未寫列變動不確認。 |
| `apps/CYERPAutoQuery/tests/CYERPAutoQuery.Tests/CYERPAutoQuery.Tests.csproj` | Linked 純邏輯新檔，使 Windows CI 的 `dotnet test` 覆蓋它們；不改既有 workflow。 |
| `apps/CYERPAutoQuery/VERSION`, `BUILD` | 工程版 V1.3.0 Build 0（原版 1.2.1；BUILD 原為 0）。 |
| `apps/CYERPAutoQuery/{README.md,CHANGELOG.md,WORK_HANDOFF.md}` | 標明首次 ERP 限定寫入例外、治理 PR 依賴、未實機／未 Release。 |
| 本 handoff、工單 §8 | 完整交接、測試與未完成清單，交 Claude 最終審查。 |

## 3. 本次完成與未完成界線

**已完成的 pure contract／orchestration：**
- `STOCKTAKE_SYNC`／`STOCKTAKE_WRITE` typed claim 與 complete JSON 的欄位、數量字串、序號、限額、結果一致性驗證。
- F2 多頁 OCR 文字清單的去重與 yyyy/MM/dd 解析；INVR11 表格必需 10 欄（含儲位／批號／盤點數量）、排除「初盤數量」；不接受其他底稿與重複 seq。
- WRITE 屬於**狀態改變風險**，執行協調器先檢查每列原識別，再交由獨立 driver 做有界修改、讀回、整張底稿核對。若失敗保留結果，不會呼叫最後的 ERP Confirm。重試相同數量避免再修改，原本已新增批號不再重複新增。

**仍須實作，尚不可交最終審：**
- [ ] `ItemQueryService.Stocktake.cs` 真 Windows Win32 driver（INVR11 作業速查、F2 OCR 全頁／頁尾、選二者皆列印、即時報表、工具列第一圖示、欄位資訊 N→Y、XLSX 匯出）。
- [ ] INVI14 F6 一般選項、依 sheet+seq 唯一定位、切詳細欄位、四識別欄精確讀回；修改盤點數量並讀回、儲存後再次核對。
- [ ] INVI06 同底稿新增批號、逐欄 OCR 讀回、儲存取得 ERP seq，不能使用未驗證固定座標。
- [ ] INVR11 全表對照一致後，INVI06 確認並讀回確認狀態；未知視窗／選單一律 fail closed，不猜測點擊。
- [ ] Agent 主執行入口與 `STOCKTAKE_WRITE` 逐列進度接線；**只在 driver 經 Windows CI 與安全驗證後才把兩種 queryTypes 加到 SupportedTypes**。
- [ ] 對新程式最新 HEAD Windows x64 `dotnet test`、`dotnet build`、Artifact 結果與 Claude 覆核。
- [ ] 使用者另行批准於實際 Windows ERP 以測試底稿做只讀／寫入驗收，並提供脫敏的操作回饋。不能當成已發行或已部署。

## 4. 安全與風險

- 根因先行：這是 CYERPAutoQuery **首次操作 ERP 寫入**，既有 Project Rules 原本全面只讀，必須先有 PR #571 作正式授權界線；任何無人值守環境不可在未驗證視窗識別／讀回前按修改或確認。
- 正式 CY Web、Cloudflare、ERP：**本次未連線、未查正式資料、未修改任何資源或數值**。GitHub 的 Public 原始碼與 CI 只包含合成資料。
- 完整核對多於 WO 指定的工作 rows：不僅檢查寫入列，還要確認本次未寫入的原底稿列不意外改變；此為建議 Claude 檢查的防誤確認安全加強。
- CY Web 的 `mismatches[].expected/actual` 必須是合法數字字串；遇到列遺失／身份改變／意外新列時採 fail closed，**不可回空字串或假裝數量是 0**，而是整筆 WRITE_FAILED 交主管核對。
- 已成功的 ERP 寫入不能假定可回滾；失敗後必須使用 read-back/retry idempotency。最終 `INVI06` Confirm 後，不再送「取消」按鈕。
- Public Git 不可保存正式匯出、客戶或員工資料、真內部路徑、Token／Secret；Log 若需要序號／品號／批號仍不得記錄數量。

## 5. GPT 進度／CI

- 工單來源與共通規則：已核對。
- 治理 [PR #571](https://github.com/simonliu1118-byte/CYapps/pull/571) Governance Check：成功；尚未合併。
- 實作 [Draft PR #572](https://github.com/simonliu1118-byte/CYapps/pull/572)：C# 純邏輯、協定、可重試協調器與 xUnit 正在實作，**不代表可合併**。
- **Windows CI 實際成功**：[CYERPAutoQuery Build #40755553029](https://github.com/simonliu1118-byte/CYapps/actions/runs/40755553029)，對本輪**程式提交** `e6d7403e6cd6d902d4fa83800b2373e45998c4e1`，`windows-x64` job **success**：xUnit **237 passed／0 failed／0 skipped**；.NET 8 Windows x64 Restore、Build、self-contained Publish、工程包結構核對、Public Package 安全掃描與 Artifact 上傳全部通過。
- **工程 Artifact（不等於正式 Release）**：`CYERPAutoQuery-v1.3.0-windows-x64-run111`，[GitHub Artifact #12844799841](https://github.com/simonliu1118-byte/CYapps/actions/runs/40755553029/artifacts/12844799841)，約 149.7 MB、保留 3 日。此包因 `SupportedTypes` 未開放 STOCKTAKE 類型而不會主動領取盤點寫入工作；**不可把包當成完整可用的 ERP 庫存寫回版本**。
- **Build 修正歷程**：首輪新 test C# `List<string[]>` 初始化語法錯誤已修正；下一輪 236 test 中 235 通過、1 失敗，原因是 `WrongIdentity` 測試預期 throw，但協調器設計為返回 FAILED，改測 `Confirmed=false`＋`Changes=0`＋`Confirms=0`，另加新批號只允許原底稿已有批號管理品號之獨立安全門檻。最終程式 HEAD Windows 237/237 全綠。
- **文件後續提交**只補 handoff／WO §8 等 WIP 記錄；Claude 審查時須核對實作工作樹及 `#40755553029` 的 source SHA，不把合成 CI 當 ERP 實機測試。
- 建議 Claude 對 `IStocktakeErpDriver` 的 read-before-write／full compare／Confirm gate、F2 多頁 OCR 的可用性做重點複審。

## 6. PR 差異整理（2026-10-29）

- 初次 PR #572 分支以較新的 `main` 建立，當 base 轉回工單 PR #565 時，誤顯示其他專案 `CYAccountingWeb` 的 17 項歷史變動。**已立即修正差異基準**：用原工單 SHA `9a101c26e4a4704186bbbb15665d808958bc3eda` 的 tree 作唯一 base，以當前程式分支**完全相同的 14 個 app blob SHA** 建立乾淨單一提交 `c48d7285df2d669727d1c193c92365f941d10bab`，透過僅此開發分支、核對 expected SHA 的 Git ref lease 更新。原先程式與測試檔內容不變。
- 重讀 [PR #572](https://github.com/simonliu1118-byte/CYapps/pull/572) patch：現在只含 **13 個 `apps/CYERPAutoQuery/**` 檔案**，沒有任何其他專案或 `.github/workflows/*` 修改。這是為了讓 Claude 審查時只看到本張工單程式變更；主分支／原規格分支／治理 PR 均未改動。
- PR #572 base 已恢復成 `cyerp-auto-query/wo-1.3.0-stocktake`，保持 Draft。Windows CI #40755553029 對整理前的程式提交 `e6d7403e6cd6d902d4fa83800b2373e45998c4e1` 取得 237/237；本次重建的 C#／測試檔 **blob 完全照原版本複製**，因此此證據支援相同程式內容的編譯，但不能把 `c48d7285` 稱為取得另一次 CI run。

## 8. GPT 最終完成報告（待全部 Windows UI 程式與測試補齊）

- 最終程式 SHA：**待完成**
- Windows CI／工程 Artifact：**待完成**
- 真 ERP 控制項讀回與安全驗收：**待完成**
- 風險與建議：§4

## 9. Claude 最終審查（僅 Claude 填）

- 審查日期：2026-10-29。程式分支 `cyerp-auto-query/stocktake`，最新 SHA 見 PR #572。
- **結論：需修正，已由 Claude 直接修改。** GPT 沒有拿到截圖，所有點選位置都不對，加上大量多餘的防護與測試，因此依使用者指示由 Claude 重寫。

### 9.1 GPT 版本的錯誤（依截圖 104–115 對照）

**INVR11**
- 條件視窗標題是「品號盤點清單[公司]」，不含 INVR11，原程式用 INVR11 找視窗會永遠找不到。報表名稱寫成「盤點底稿明細表」也不對。
- F2 清單頁尾的「1 / 4」意思是第 1 筆、共 4 筆，原程式卻當成多頁直接拋錯。只要有一張以上底稿就一定失敗。
- 「選擇列印」的選項是「二者皆列印」，原程式搜尋「兩者」，永遠找不到。
- 完全沒有做報表「欄位資訊」把儲位、批號、盤點數量切成 Y 的步驟，所以匯出檔缺欄位。
- 沒有沿用既有的 `RunReportAsync`（關閉殘留視窗、按三次即時報表、匯出、關窗），另寫了一套。

**INVI14**
- 沒有切到「詳細欄位」頁籤。
- 用 `WM_SETTEXT` 寫入資料感知欄位，ERP 可能不會把值當成修改。
- 欄位定位要求「Enabled」，但唯讀的識別欄位會被排除在外。

**INVI06**
- 新增列用「欄位標籤＋右邊輸入框」去找，但表格的格子不是視窗，這樣找不到。
- 確認後去讀一個不存在的「確認狀態」欄位。
- `ErpAutomationGate` 有巢狀使用。
- 寫入動作沒有包在 `GuardErpMessagesAsync` 裡。

**流程與協定**
- 協定、流程協調和測試裡有大量重複驗證，例如序號碰撞、整張底稿外的新列、200 張上限、空白寫回禁止確認等，對實際流程沒有幫助。依使用者 2026-10-29 指示刪除。

### 9.2 Claude 的修改

**INVR11**（`ItemQueryService.Stocktake.cs`）
- 改用 `RunReportAsync`，報表名稱改為「品號盤點清單」。新增 `prepare` hook，在匯出前開「欄位資訊」。
- F2 清單用 OCR 逐行讀出底稿編號和盤點日期，並讀頁尾的總筆數。讀到的張數少於總數時往下翻頁，最後以點擊該列加按「確定」選取底稿，並讀回輸入框內容核對。
- 「選擇列印」是基本選項右側的下拉選單，「二者皆列印」是最後一項，沿用 `SetComboLastAsync` 設定。
- 「欄位資訊」從報表左上第一個按鈕開啟（避開旁邊的 ▼）。用 OCR 找「顯示」欄與儲位、批號、盤點數量三列，是 N 的就點一下。點完重新讀一次，確認三列都是 Y 再按「確定」。

**INVI14**（`ItemQueryService.StocktakeWrite.cs`）
- F6 一般選項填兩列條件：盤點底稿編號和序號。查詢完切到「詳細欄位」。
- 詳細欄位依列位置對應欄位：第 1 列底稿編號＋序號、第 2 列盤點日期＋品號、第 3 列盤點數量＋品名、第 6 列批號＋庫別。
- 先核對序號、品號、批號，再按修改，點盤點數量，用 End＋Backspace 清空後輸入並按 Tab，接著儲存。儲存完讀回數量；中途失敗就按「取消」。

**INVI06 新增批號**
- 用 OCR 讀表格標題，取得序號、品號、批號、盤點數量的欄位位置。比對時避開「批號盤點」，以免和「批號」混淆。
- 點第一列序號，按 Ctrl+End 到最後一列，再按「下」產生新列。以「最下方序號比上一列大」判定新列確實產生。
- 依序點品號、批號、盤點數量格子輸入。盤點數量格預設是 0，所以點格子右側，讓游標落在 0 後面。不按 End 或 Ctrl+A，因為不在編輯狀態時，這兩個鍵會跳到別欄。
- 填完用 OCR 讀回新列，再按儲存。

**INVI06 確認**
- 若「取消確認」已經是深色，代表已確認，直接回報成功。
- 否則按「確認」；過程中出現詢問框就按「是」，出現其他訊息就停下。之後等「取消確認」由灰轉深，判定確認成功。

**流程**（`StocktakeExecution.cs`）
1. 讀 INVR11。
2. 逐列處理：數量已一致的跳過；原有列用 INVI14 寫；之前已新增過的批號改用 INVI14 寫，避免重複新增；其他新增批號用 INVI06。
3. 任一列失敗就停止，其餘列標為「未執行」。
4. 重讀 INVR11，每一寫入列都相符才確認。新增列的 ERP 序號從這次重讀取得。

**其他**
- `SupportedTypes` 已加入兩種盤點類型。實際是否派工由 CY Web 端決定，CY Web WO-0.9.52 需另外部署。
- 測試從 GPT 的 23 項精簡為 13 項，涵蓋協定、數量格式、F2 與表格 OCR 解析、匯出、寫回流程。全部 235 項通過（Linux `dotnet test`）。

### 9.3 治理 PR #571

方向同意。條文中的「INVR11 全表一致」已改成「所有寫入列一致」，並寫明確認時會回答 ERP 的詢問框，以符合實作。

### 9.4 尚未驗證（需實機）

以下推定都還沒在實機確認：
- F2 視窗與表格的 OCR 結果。
- 「欄位資訊」按鈕的位置。
- 單擊 N 是否確實切成 Y。
- INVI06 的 Ctrl+End 與按「下」新增列。
- 盤點數量格子的點擊位置。
- 「取消確認」顏色的判定。

建議用一張測試底稿依序實測：讀取、單筆寫入、新增批號、確認。失敗時把 LOG 交回（LOG 會記錄 OCR 讀到的文字）。

- 是否允許合併：先合併治理 PR #571，再合併本 PR；發 Release 前先做上述實機測試。
