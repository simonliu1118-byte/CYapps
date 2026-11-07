# CYERPAutoQuery：ERP 盤點完整清單需求交接（交 Claude，未實作）

- 日期：2026-11-07（Asia/Taipei）；相關 [WEB PR #443](https://github.com/simonliu1118-byte/chihyuan-web/pull/443)。
- **使用者最新指示：CYERPAutoQuery 只寫 handoff，交由 Claude 實作；本對話僅可合併部署 WEB，不得合併、發布或更新 Agent。**
- GPT 先前超出範圍的 Agent 程式、測試、VERSION、README、CHANGELOG 變更已撤回。本 PR #604 相對原 main 僅保留專案內交接文件與 WORK_HANDOFF 索引；既有程式及 VERSION 回到原基準，不代表任何新版本已完成。

## WEB 已提供的契約

WEB 完整同步的完成回報新增 `listComplete` 布林欄位。僅當 full sync 成功且明確回報 `listComplete:true`，WEB 才會把請求當下已知、卻不在完整 ERP 清單的同代底稿轉入盤點紀錄；單張 refresh、FAILED、缺旗標或 false 都不歸檔。

- `sheets` 必須包含 ERP 所有底稿，包括已讀取／被 skip 的底稿，不能只列這次有讀明細的單。
- 已有單全部 skip：可以 `NOT_FOUND`＋完整 `sheets`＋空 `details`＋`listComplete:true`。
- 真正清空：可以 `NOT_FOUND`＋空 `sheets`＋空 `details`＋`listComplete:true`，但一定要先證實 ERP 確實 0 張。
- WEB server-only `knownSheets` 不在 claim 傳給 Agent。歸檔規則及資料保存由 WEB 負責，Agent 只回報完整事實。
- 現有 Agent 未提供旗標，部署新版 WEB 後仍能同步，但 **不會啟動 ERP 清除後自動歸檔**。須待 Claude 完成 Agent、測試及使用者授權正式發行／升級後才生效。

## Claude 請確認及實作

1. 查 `ItemQueryService.Stocktake.cs` 的 INVR11／F2 清單讀取：目前 OCR footer 總筆數未知時可能接受第一頁；零張又會失敗。必須區分「無法讀出總數／只讀一部分」和「明確 0 張」，不得把空畫面或第一頁視為完整。
2. 以既有有上限 polling 等待 footer。總數未知時重讀，不先翻頁或回成功；已知總數與實讀去重張數完全一致才完成。頁面無進度、總數中途變動、缺頁、超過既有 200 張上限均停止，不送完整旗標。重讀同頁等待 footer 不應誤判為已翻頁無進度。
3. 依實際 ERP 版本驗證明確 0/0 footer。只有可靠確認零張才可成功回傳完整空清單；無法證明時維持失敗、留待下一次同步。
4. `StocktakeQuery.cs` 可集中完整性驗證；`ErpAgentProtocol.Stocktake.cs` 的成功 STOCKTAKE_SYNC body 只在讀取流程證實完整時加 `listComplete:true`。核對 `StocktakeExecution.cs`，明確空清單不可再要求讀已不存在的單。
5. 新增既有測試框架的純邏輯與執行測試：未知／部分／總數變動／重複頁／超額／完整多頁／明確零張、全部 skip 的完整清單、成功 body 旗標、失敗不聲稱完整。Windows 實機驗證 OCR、翻頁、0/0 與既有寫回不退化。
6. 由 Claude 依當時 main VERSION／BUILD、專案規則決定版本及發布流程，不沿用先前撤回提交當作版本權威。程式完成後更新本交接、WORK_HANDOFF、CHANGELOG 及 README，再依使用者授權決定是否發行。

## 界線與驗收

- 不新增 ERP 清除／刪除、不改 STOCKTAKE_WRITE、不新增排程或平行查詢流程。
- 舊 Agent＋新 WEB：同步可用、歸檔暫不啟用。新 Agent＋舊 WEB：可忽略新增欄位。兩端更新後才啟用歸檔。
- 僅使用合成 fixture；不得把 ERP 正式匯出、顧客資料、憑證或盤點資料送入公開 source／log／artifact。CYID Consumer Impact: NONE。
- 先前撤回實作曾跑 CI，不是目前文件交接已完成實作的證據，也不是 Claude 審查通過。此 handoff 狀態：**待 Claude 實作、待測試、未合併、未發行、未更新伺服器 Agent**。
