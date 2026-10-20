# CYInvoice V3.2-C：原 Cloudflare D1／Worker 本機 Work + MCP 交接

狀態：**原始碼工程交接稿，不表示已在 Cloudflare 執行。** 唯一部署者為使用者另開、有 Cloudflare MCP 的本機 Work 對話；本 GitHub 工作對話不操作原 D1／Worker／Bindings／Secrets。本文是一次性交接清單，非永久治理規則。

## 工作邊界與順序

1. **僅唯讀盤點**：確認 Cloudflare Account、**原 CYInvoice** Worker 與綁定原 D1 的實際 ID、當前 Worker Version、`d1_migrations` 確實停在 `0016_non_allowance_work_items.sql`、CYID Consumer 連線、`ALLOWANCE_API_ENABLED` 仍為關閉；不要使用任何測試／替代 Worker 或 D1，也不得觸碰 CYID 專用 D1。
2. **事前額度報告**（共通規則 2.9.1 §11.3）：從使用者授權的 MCP／後台讀取 Cloudflare **共用帳號當日** D1 rows read/written、Worker requests 與方案上限／剩餘量；列出本工作預計影響的資料庫、估計讀寫列數、SQL／API 次數、額度百分比、索引及中止條件，**先向使用者說明，再進入任何大量資料作業**。不知道額度、費用或有不足／限流，**立即停止**，不重試或繞路。
3. **備份與回退判準**：在任何 DDL 前按 Cloudflare 受支援方式建立完整可驗證備份／Time Travel Bookmark，核對原 D1 的 Workspace、Device、company_settings 與現有非折讓 Work Item 基準總數及 FK／integrity（使用有限量聚合查詢／主鍵定位，不做重複全表明細掃描）。備份位置和復原程序需可由本機 Work 立即定位；不要在 GitHub 留備份或機密。
4. **遷移提案**：僅使用本 repo 經 CI 審核的 `cloud/migrations/0017_allowance_applications.sql`、`0018_allowance_frozen_ledger.sql`，依序一筆一筆對**原 CYInvoice D1** 套用，沿用正規 migration history；**絕不重套 0014～0016**，也不可用 DROP TABLE 當回退。兩筆 SQL 僅建立新表／索引／觸發器，不含存量 Work Item 修改。
5. **資料庫後驗**：以限量 schema/PRAGMA、聚合查詢確認 migration history 精確前綴為 0001～0018、新的六張折讓專屬表、四個索引、二十三個觸發器存在，FK 與原 Workspace／Device／非折讓 Work Item 未變。異常立刻停下，依既定備份／Time Travel 人工判斷復原，不直接執行下一步。
6. **Worker 部署審核**：僅在 D1 已驗證為 schema18 後，檢查 `SCHEMA_VERSION=18`、原 Worker 服務綁定（`DB`、`IDENTITY`、`IDENTITY_AUTHORITY`）、公司 AES-GCM Secret 是否已有安全配置，確保部署不覆蓋其他 runtime Secrets/Bindings。先通過 deployment preflight；`ALLOWANCE_API_ENABLED` **保持關閉**，未批准不可透過公開 route 進行折讓。
7. **分段啟用與驗收**：待另行確認可歸因的一次性 g0401／官方未確認狀態策略、授權拒絕矩陣、兩台 Windows 實機、光貿測試環境與回退條件後，才評估由使用者本機 Work 手動啟用。未經新批准不進行任何正式帳務送出、g0501、真實客戶資料探測、正式 Release。

## 事前計量預估與禁止超額規則

- **工程端離線已數得的 DDL 規模**：0017 為 2 表／2 索引／6 觸發器；0018 為 4 表／2 索引／17 觸發器；合計 **6 張新表、4 個新索引、23 個新觸發器**，合計 33 個 CREATE DDL。**兩份遷移沒有 INSERT、UPDATE 或 DELETE 舊帳務資料的語句**。
- **估計資料異動**：只建立空白折讓結構，預計沒有存量發票、Workspace、Device 或 Work Item 逐列更新。Cloudflare 帳單計數與實際讀寫量需以本機 MCP／帳戶 Dashboard 為準，不能把「0 筆商業資料更動」假設成「0 D1 計費量」。
- **查核限制**：每步固定少量索引定位／表結構檢查，絕不以全表導出或逐列密集重讀代替備份；每次查核前列出具體 SQL、預估 rows read 與停止門檻；遇 D1 配額接近上限，先暫緩、跨日作業或請示。
- **完成報告**：本機 Work 應記錄每步原 D1 schema、migration 進度、真正 D1 rows read／written 和 Worker requests（前後差）、預估差距、剩餘額度、保留的備份／rollback 路徑與 CI SHA。未在真 Cloudflare 查證的欄位請標註「未驗證」，不得填成功。

## 本 GitHub 回合資料用量

此工作只透過 GitHub 讀寫程式碼及執行 CI 合成資料庫，沒有連線使用 Cloudflare 帳號；本回合 Cloudflare **實際 D1 讀取 0、寫入 0、Worker 部署 0**（以本對話未觸發 Cloudflare 操作為準），與本機 Work 隨後的實際操作分別報告。
