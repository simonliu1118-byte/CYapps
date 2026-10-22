# CYInvoice V3.2：原 Cloudflare schema20 本機 Work + MCP 交接

## 2026-10-22 交接摘要（V3.1.41，取代同日 schema19 限制）

本文件為一次性操作交接，不是永久規則。使用者本批授權先完成 Source／精確 head CI，再以本機 Cloudflare MCP 分段完成原 D1／Worker **18→19→20**；結構升級與財務送出啟用分開。原基準為 `0b6d450d`（V3.1.40 Build 1）；工作分支 `cyinvoice/feat-v32-de-void-coordination`／PR #550。操作前核對最新完整 SHA 與 Windows／Cloud／Governance CI，不合併、不發布 Release。

1. 先完整閱讀正式三層規則、CYID canonical consumer standard 與 `V3_2_IMPLEMENTATION_PLAN.md`，共通規則 2.10.0 須比對 AITeam main。
2. 核對原 Account／D1／Worker 與全帳號當日額度。整批上限 **5,000 D1 read／500 write／30 Worker requests**；不足先重估，不自行提高；額度未知、限流或費用告警即停，不重試繞過。用量包含備份、盤點、兩段 DB／Worker 驗證，分列 SQL meta 與帳號差額。
3. 有界盤點：migration history 按 id 最多21筆（20加1筆溢位偵測），必須是精確 canonical 前綴；原 preflight 六表依 CYID_ONLY 跳過已退役 cloud_employees，另查公司卡、六張折讓表與已存在的人工 Audit／作廢四表。每表按主鍵最多101筆 opaque ID，只在私有位置計數／比對。任一表超過100筆即停重估，不繼續全表掃描。初盤 read 上限約1,500。
4. DDL 前保存 **schema18 升級前** Time Travel bookmark、完整加密 SQL 私有備份、原 Worker version／Bindings 及舊資料指紋；實際還原到隔離本機資料庫驗證 schema、歷史與內容。每段 DB 後的 preflight bookmark 分別標示，不冒充升級前備份。schema19→20 前另保留已驗證的 schema19 還原點及前一 Worker version；新寫入不明即停。
5. **先 0019**：唯一來源 `cloud/migrations/0019_allowance_manual_review_audit.sql`；只套一次，沿正規 history，不重跑0014～0018。規模1 ALTER／1表／3索引／4觸發器；既有 reconciliation N≤100 時估計額外 read 約2N+metadata、write約N+metadata。DB 後驗精確0001～0019、舊內容不變、新欄位／結構、FK／integrity、人工 Audit 為空；通過才在原唯一部署 owner 使用 deploy＋`CYINVOICE_SCHEMA_UPGRADE=18:19` 更新原 Worker marker19。
6. **再 0020**：唯一來源 `cloud/migrations/0020_allowance_void_coordination.sql`，無 drafts 副本。只在完整schema19且原 Worker marker19已驗證後套一次；新增4張空表／3索引／15觸發器，永久 Claim 與不可改寫 Audit 不授權任何送出。DB 後驗精確0001～0020、舊資料／g0401 sending Ledger／Audit 不變、新表皆空、FK／integrity。通過才使用 deploy＋`CYINVOICE_SCHEMA_UPGRADE=19:20` 更新原 Worker marker20。
7. 每段 before／deploy／after 使用相同升級意圖、精確 source identity 與私有 checkpoint；before／deploy 20分鐘限制、after 30分鐘限制不變。canonical20 的18:19只接受完整0001～0019，不要求提前套0020；19:20只接受完整0001～0020。跳版、缺遷移、已升級重用意圖、不同checkpoint、來源／scope／active version／bindings不符、未真正部署均拒絕。勿執行 `deploy:with-migrations`。失去回覆先唯讀確認，不自動再upload／deploy。
8. 原 CYID／Device scopes、D1／Services／Secrets／Rate limits、自訂vars、公司卡及其他設定完整繼承；每段只改對應 SCHEMA_VERSION。`ALLOWANCE_API_ENABLED`、`ALLOWANCE_G0401_SEND_ENABLED`、`ALLOWANCE_MANUAL_REVIEW_ENABLED`、`ALLOWANCE_EXTERNAL_COLLISION_ENABLED` 均不存在或false。g0501無送出HTTP路由，不新增路由、不啟用內部sender、不做真光貿交易。

## 同日後續產品規劃（本批遠端維護範圍不變）

使用者已定案的後續簡化，統一見 [V3.2 計畫](V3_2_IMPLEMENTATION_PLAN.md)：既有「處理中／上傳問題」、主管一次核准、自動佔號加號無次數上限、簡化作廢與問題診斷。**等本機本批完成回報交回後才實作**，本機現在不順手改UI、送出行為、0020約束或開旗標。

0020既有15分鐘與永久Claim是當前Source結構，不代表使用者要求永久保留；若本批已套用，後續由新的forward migration調整，不能回寫／重套0020。舊「跨Local停送／永久單次作廢／無POST歸因不結案」不再作後續產品目標；目前未實作的新流程也不構成現在啟用財務送出的授權。

## 停點、回復與完成報告

- 已完整升級的段落不重套；DB／Worker已同為20且來源／bindings符合則不重部署。若既有schema19／20來自其他維護，先核對其來源、備份與後驗證據，不能追最新硬升。其他schema／scope／未授權旗標／來源不明立即停。
- **Local停送不是schema20結構升級前置條件。** 本批仍只升結構、旗標全關；後續g0501的永久單次／全域停送門檻已由使用者新流程取代，按V3.2計畫實作及驗收後再審啟用。未知歷史不得假造，測試公司卡／API與真環境驗收仍須完成。schema完成不代表產品簡化或H已結案；雙Windows真人驗收於V3.2上線後補做，不作本批前置條件。
- 異常停止並留私有證據；Worker以保存的原immutable version回退，D1不隨Worker回退。不得反向DROP／自動還原整庫；先評估期間其他寫入及備份／業務影響，再由使用者決定。
- 回報實際source SHA／最新必要CI、每段migration與Worker marker／version、四旗標、原CYID／Bindings保留、私有備份／實際還原證據、SQL計費meta與全帳號差額／餘額及預估差異。帶回Local停送／未知Claim的可查證現況與測試公司卡是否可用，不提供秘密值。未實測標未驗證。**回報後停下，交回原開發對話。**
- 本Source批次未執行D1 SQL／部署；正式Release仍V3.1.0，原D1／Worker實況另以本機MCP完成報告為準。

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
