# CYInvoice V3.2-C：原 Cloudflare schema19 本機 Work + MCP 交接

## 2026-10-22 交接摘要（V3.1.40 Build 1）

- 儲存庫 `simonliu1118-byte/CYapps`；專案 `apps/CYInvoice`；工作分支 `cyinvoice/feat-v32-de-void-coordination`／PR #550。開始前核對該 PR 的最新完整 SHA 與該 SHA 的 Windows／Cloud／Governance CI，不以舊版本成功推定新 head 通過；不要自行合併 PR 或發布 Release。
- 先讀正式三層規則、CYID canonical consumer standard、本文件及 `V3_2_IMPLEMENTATION_PLAN.md`。共通規則目前為 2.10.0，仍須比對 AITeam main。
- 工程端已補齊非空 schema19→schema20 草案的檔案備份、注入失敗回滾、升級後全量合成資料／g0401 sending Ledger／Audit 保留、檔案重開與升級前備份完整還原。g0501 永久 Claim 的送出回覆遺失、Claim 回覆遺失、Claim 後撤權也已驗證實際關閉重開後不重送。**這些不是原 D1／Time Travel／真光貿的驗收證據。**
- **本機下一批僅做原 D1／Worker schema18→19**，依下節的額度、備份、單次 0019、DB 後驗、Worker marker 與 binding 回讀順序；遇不符先停並回報。不要直接執行 `deploy:with-migrations`，也不要先啟動 `schema_upgrade=18:19` 的 Worker 部署：此意圖要求原 D1 已完成 0019，必須先取得 DB 後驗證據。
- 若唯讀發現原 D1 已是完整 canonical schema19：不重套 0019，先查明既有維護及備份證據；若原 Worker 仍為 marker18，再依本批明確授權處理 18→19 marker。若 DB／Worker 已同為19且來源／bindings 均符合，不重部署；直接回報現況。其他 schema、不同資源／scope、未授權旗標或來源不明均停下，不追最新版本硬升級。
- **本批不套 schema20**：`cloud/drafts/0020_allowance_void_coordination.sql` 尚未升格，不能自行複製進 migrations 或手動套原 D1；Cloud g0501 沒有送出 HTTP 路由。schema19 完成後，帶回 Local 停送／未知 Claim 的可查證現況及測試公司卡是否可用（不提供秘密值），再由原開發工作線接續 schema20／送出權威與真測審查。
- 雙 Windows／不同 CYID Device 的真人操作依使用者決定於 **V3.2 上線後補驗**；不可把這項再列成本批 schema19 的前置條件。財務送出權威、測試公司卡／API 與回復證據仍未豁免。
- 完成報告須列：實際 source SHA／CI、操作前後 migration 與 Worker marker／version、四旗標及原 CYID／binding 保留結果、私有備份與可還原證據、每批真實 D1 read／write／Worker requests／剩餘額度與預估差異、未完成項。機密、真實發票／帳務及備份留在私有位置，不貼 Public GitHub。回報後即停，不接著開送出旗標、做真 g0401/g0501 或發行。

## 現行停點：0019／Worker 標記升級（V3.1.32 Source；尚未遠端執行）

0019 已有唯一 canonical 來源 `cloud/migrations/0019_allowance_manual_review_audit.sql`；原 D1／Worker 仍依先前本機回報為 schema18。本文件是下一批作業提案，**不代表使用者已授權遠端執行**。先核對本批精確 CI head／main、原 CYInvoice Account／D1／Worker，禁止新建替代資源或碰 CYID 專用 D1。

1. **先核共用帳號額度**：取得當日全帳號 D1 read／write、Worker requests、方案餘額及其他服務預留量。未知、限流、接近配額時停止；先向使用者報預估與批次上限，再開始資料作業。
2. **有界盤點與預估**：migration history 依 id 最多 20 筆，必須精確為 0001～0018；按主鍵盤點原 preflight 六表及公司卡、六張折讓表，每表最多取 101 個 opaque ID，只在私有位置計數／比對，不匯出明細。任一表超過 100 筆，先停止重估，不接著掃全表。初盤提案上限約 1,500 rows read；整批提案上限 **5,000 read／500 write／30 Worker requests**，須對照實際餘額獲准，不能當成服務實測保證。
3. **私有 schema18 備份／回復**：在 DDL 前用本機 MCP 保存原 D1 Time Travel bookmark、可驗證加密備份、原 Worker version／完整 binding 與非折讓資料指紋，實際確認還原方式可用。這份 schema18 bookmark 與稍後 schema19 preflight 產生的 bookmark 必須分開標示；不得把事後 bookmark 冒充升級前備份。
4. **僅套一次 0019**：走正規 migration history，不重套 0014～0018。0019 為 1 個 ALTER／1 表／3 索引／4 觸發器；會掃描既有 reconciliation rows 作欄位檢查／索引建立，不能把沒有商業 INSERT 誤當零計費。若既有證據 N≤100，DDL 初估額外 read 約 2N＋metadata、write 約 N＋metadata，真實計量以 D1 meta 為準。失敗立即停，不以重試／DROP TABLE 代替回復。
5. **DB 後驗再改 Worker marker**：確認完整 canonical 0001～0019、舊資料／證據內容不變、新欄位／索引／觸發器、FK／integrity 及新人工 Audit 表為空。原 Worker 仍標記 18 時，才在原部署 owner 明確選 `deploy`＋`CYINVOICE_SCHEMA_UPGRADE=18:19`（workflow 對應 `schema_upgrade=18:19`）。before／deploy／after 三段使用相同意圖與 private checkpoint；未套完 0019、來源不為 18、未知跳版、checkpoint 不符或未真正部署一律拒絕。
6. **僅 marker 變為 19**：原 CYID／Device scopes、D1／Services／Secrets／Rate limits、自訂 vars、公司卡及其他設定必須完整繼承原 active version。`ALLOWANCE_API_ENABLED`、`ALLOWANCE_G0401_SEND_ENABLED`、`ALLOWANCE_MANUAL_REVIEW_ENABLED`、`ALLOWANCE_EXTERNAL_COLLISION_ENABLED` 均須不存在或 `false`；本批不啟用折讓、不做光貿 g0401／g0501，也不發布 Release。

預檢會逐次核對精確 source identity、20 分鐘內的 checkpoint、原 active version 及部署後 binding readback；逾時／失去回覆時先唯讀確認，不能自動再 upload／deploy。新標記已為 19 時不可重用 18:19 意圖；之後一般 code-only 部署維持既有保留語意。

**回復與完成紀錄**：異常先停止並保留私有證據。Worker 回退使用保存的原 version；D1 已是 additive schema19 時，不自動反向 SQL 或還原整庫，須先評估期間其他寫入、備份與業務影響，再由使用者決定。完成報告逐批列真實 rows read／written、Worker requests、預估差異、剩餘額度、原／新版本、備份位置及每個驗證結果。未實測欄位標未驗證。

本 GitHub 工程批次 Cloudflare 實際 D1 read **0**／write **0**／Worker deploy **0**。C7 可歸因成功政策、公司卡及雙 Windows 真光貿驗收仍未完成；正式 Release 仍 V3.1.0。

## 歷史：schema16→18 已完成的作業紀錄（禁止重跑）

**歷史一次性升級清單 — 已由使用者在 2026-10-21 本機 Work＋Cloudflare MCP 完成並回報。以下 0017／0018 步驟不得再執行！**

依使用者提交的本機驗收報告，原 CYInvoice D1 已從 0016 受控升至 schema18（0017、0018 各一次）；原 Worker 已部署 SCHEMA_VERSION=18，保留 CYID、Secrets、Bindings，`ALLOWANCE_API_ENABLED=false`，九條折讓入口仍關閉。六張新表皆空；私有 DPAPI 備份／還原、FK、quick_check 與 runtime 驗證已報告通過。帳號層級 read +1711、write +55、Worker request +12，未超預算。**本文件記錄的是「本機 MCP 回報」，非本 GitHub 工作對話獨立遠端驗證。**

下方舊步驟僅供稽核／回顧，**不得重套任何 0014～0018 Migration，也不得重建測試 D1／Worker**。C7 與 V3.2 其餘功能仍未通過真光貿／雙機驗收；任何新 D1／Worker／公司卡／光貿操作需另由使用者本機 Work 按共同規則重新評估與授權。最新交付範圍與順序見 [V3.2 完整計畫](V3_2_IMPLEMENTATION_PLAN.md)。

### 歷史工作邊界與順序（0017／0018，不再執行）

1. **僅唯讀盤點**：確認 Cloudflare Account、**原 CYInvoice** Worker 與綁定原 D1 的實際 ID、當前 Worker Version、`d1_migrations` 確實停在 `0016_non_allowance_work_items.sql`、CYID Consumer 連線、`ALLOWANCE_API_ENABLED` 仍為關閉；不要使用任何測試／替代 Worker 或 D1，也不得觸碰 CYID 專用 D1。
2. **事前額度報告**（共通規則 2.9.1 §11.3）：從使用者授權的 MCP／後台讀取 Cloudflare **共用帳號當日** D1 rows read/written、Worker requests 與方案上限／剩餘量；列出本工作預計影響的資料庫、估計讀寫列數、SQL／API 次數、額度百分比、索引及中止條件，**先向使用者說明，再進入任何大量資料作業**。不知道額度、費用或有不足／限流，**立即停止**，不重試或繞路。
3. **備份與回退判準**：在任何 DDL 前按 Cloudflare 受支援方式建立完整可驗證備份／Time Travel Bookmark，核對原 D1 的 Workspace、Device、company_settings 與現有非折讓 Work Item 基準總數及 FK／integrity（使用有限量聚合查詢／主鍵定位，不做重複全表明細掃描）。備份位置和復原程序需可由本機 Work 立即定位；不要在 GitHub 留備份或機密。
4. **遷移提案**：僅使用本 repo 經 CI 審核的 `cloud/migrations/0017_allowance_applications.sql`、`0018_allowance_frozen_ledger.sql`，依序一筆一筆對**原 CYInvoice D1** 套用，沿用正規 migration history；**絕不重套 0014～0016**，也不可用 DROP TABLE 當回退。兩筆 SQL 僅建立新表／索引／觸發器，不含存量 Work Item 修改。
5. **資料庫後驗**：以限量 schema/PRAGMA、聚合查詢確認 migration history 精確前綴為 0001～0018、新的六張折讓專屬表、四個索引、二十三個觸發器存在，FK 與原 Workspace／Device／非折讓 Work Item 未變。異常立刻停下，依既定備份／Time Travel 人工判斷復原，不直接執行下一步。
6. **Worker 部署審核**：僅在 D1 已驗證為 schema18 後，檢查 `SCHEMA_VERSION=18`、原 Worker 服務綁定（`DB`、`IDENTITY`、`IDENTITY_AUTHORITY`）、公司 AES-GCM Secret 是否已有安全配置，確保部署不覆蓋其他 runtime Secrets/Bindings。先通過 deployment preflight；`ALLOWANCE_API_ENABLED` **保持關閉**，未批准不可透過公開 route 進行折讓。
7. **分段啟用與驗收**：待另行確認可歸因的一次性 g0401／官方未確認狀態策略、授權拒絕矩陣、兩台 Windows 實機、光貿測試環境與回退條件後，才評估由使用者本機 Work 手動啟用。未經新批准不進行任何正式帳務送出、g0501、真實客戶資料探測、正式 Release。

### 歷史事前計量預估與禁止超額規則

- **工程端離線已數得的 DDL 規模**：0017 為 2 表／2 索引／6 觸發器；0018 為 4 表／2 索引／17 觸發器；合計 **6 張新表、4 個新索引、23 個新觸發器**，合計 33 個 CREATE DDL。**兩份遷移沒有 INSERT、UPDATE 或 DELETE 舊帳務資料的語句**。
- **估計資料異動**：只建立空白折讓結構，預計沒有存量發票、Workspace、Device 或 Work Item 逐列更新。Cloudflare 帳單計數與實際讀寫量需以本機 MCP／帳戶 Dashboard 為準，不能把「0 筆商業資料更動」假設成「0 D1 計費量」。
- **查核限制**：每步固定少量索引定位／表結構檢查，絕不以全表導出或逐列密集重讀代替備份；每次查核前列出具體 SQL、預估 rows read 與停止門檻；遇 D1 配額接近上限，先暫緩、跨日作業或請示。
- **完成報告**：本機 Work 應記錄每步原 D1 schema、migration 進度、真正 D1 rows read／written 和 Worker requests（前後差）、預估差距、剩餘額度、保留的備份／rollback 路徑與 CI SHA。未在真 Cloudflare 查證的欄位請標註「未驗證」，不得填成功。

### 歷史 GitHub 回合資料用量

此工作只透過 GitHub 讀寫程式碼及執行 CI 合成資料庫，沒有連線使用 Cloudflare 帳號；本回合 Cloudflare **實際 D1 讀取 0、寫入 0、Worker 部署 0**（以本對話未觸發 Cloudflare 操作為準），與本機 Work 隨後的實際操作分別報告。
