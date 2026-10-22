# CYInvoice V3.2：完整折讓功能交付計畫

更新：2026-10-22（Asia/Taipei）。此檔是 **V3.2 功能範圍、執行順序與完成判準**的現行唯一規劃；原 C1～C7a 各小片的詳細紀錄可追溯 Git history／PR 與 [TODO](TODO.md)，不再把歷史子片當成待執行工作單。**產品目標不變**，只整併工作批次與重複文件維護。永久治理以根目錄 REPOSITORY_RULES.md、REPO_POLICY.md、CYInvoice/PROJECT_RULES.md 為準，本文件不另創規則。

**2026-10-22 使用者調整驗收順序：**雙 Windows／不同 CYID Device 的真人驗收改於 V3.2 正式上線後補做，不視為已通過。僅此項延期；上線前 CI、合成跨機測試、必要官方測試環境與不可盲目重送／越權／跨 Local-Cloud 單一送出權威安全門檻仍須完成。Local 舊發送者未確認停送及未知 Claim 未查明前，不得啟用 Cloud g0501 或建立送出 HTTP 路由。

**2026-10-22 使用者明確修正 F 做法：**不得修改舊發票保留／清理機制；僅在折讓清單與本機原發票對不上時，使用既有唯讀 `invoice_query` 以原發票號碼按需查回。官方不存在時必須明確顯示「查無原發票」（沿用上傳問題），資料對不上不得補造或擴大掃描。原有 150 筆折讓清單／50 張原發票每日限制不變。早先曾討論延長保留的作法不執行。

## 1. 不變的 V3.2 交付目標

1. **折讓開立**：沿原發票、完整官方明細與可用餘額建立申請；保留含稅／未稅輸入、七位固定小數、負數折扣、多次合法部分折讓與人工確認。主管在 Local／CYID 跨機範圍內完成可信覆核、核准、配號及光貿 g0401 送出。
2. **同次交易絕不盲目重送**：固定 application ID、Frozen Request、Grant、Ledger 及第一次送出權限；逾時、斷線、掉電、code=0 未確認、官方原號同內容吻合均不能單獨證明「此 POST 已成功」，不得自動重送、配新號、改成 confirmed。只有明確外部撞號、證據充分且重新授權的人工流程，才可研究受控下一號。
3. **離線協作**：CYID Cloud 暫時不可用而光貿可達時，依既有受保護、最後可信權限執行允許的 Local 業務；已知撤權或 Workspace／Device 拒絕不可當成一般斷線。恢復後依原申請／原交易證據核對，絕不將結果不明佇列自動重送。
4. **折讓作廢**：Local／CYID 均有 g0501 的申請、主管權限、唯一送出、原折讓號官方查回及 D0501 確認；不得以撤回申請代替正式作廢。
5. **官方同步與歷史**：低呼叫量的 invoice／allowance 清單、必要時逐號查詢，反映網站外部處置、跨期、PDF Cache 失效與逐筆申請／原因／核准／交易／作廢／行政結案 Audit；未知歷史保持 unknown，不推造值。
6. **正式整合**：新流程驗收成功後，才退役舊人工選號／人工完成 UI，保留舊資料唯讀查回。完成真光貿測試環境、Local／CYID、Cloudflare 及回復條件安全驗收；真人雙 Windows 驗收於 V3.2 上線後補做；正式 V3.2.0 Release 仍需使用者**當次明確授權**。

## 2. 現行真實進度（2026-10-22；分開標明證據來源）

- **工程來源**：V3.1.40 Build 1。A 折讓編輯、B Local 與 C Cloud 的申請／主管覆核、加密 Frozen／原因、Grant、原 Ledger 配號／單次 g0401、無法歸因查回、人工分類與明確外部占號後主管改配號已有工程碼及合成測試。0019 已升格為唯一 canonical migration，非空 schema18 的檔案備份／失敗回滾／一次升級／還原及明確 marker 18→19 部署護欄已有工程驗證。這些都不等於真光貿交易或真人驗收。
- **遠端基線（依使用者先前本機 MCP 回報）**：原 D1 0017／0018 各套一次、schema18；原 Worker 保留 CYID／D1／Secrets，SCHEMA_VERSION=18、ALLOWANCE_API_ENABLED=false；新六表為空，私有備份與還原證據曾核對完成。本 GitHub 工程批次沒有重新查詢或操作 Cloudflare；不重套 0014～0018、不新增替代 Worker／D1。
- **下一個操作停點**：依[本機 MCP 交接](V3_2_C_CLOUDFLARE_MCP_HANDOFF.md)，先核共用帳號餘額、有界盤點及 schema18 私有備份，再另行授權套一次 0019／部署原 Worker marker19，四道折讓旗標保持關閉。
- **B/C 尚未完成的驗收**：可歸因成功與未知結果人工處置政策、測試公司卡、真兩台 Windows／角色與 Device 撤權／併發／斷電／回覆遺失，以及真光貿測試。單靠 source、合成 CI、人工分類或原號吻合都不能結案。
- **E Local 作廢工程來源**：既有待辦已接官方預覽／原主管 15 分鐘核准、原號文字確認及 SQLite 單次 g0501；送出鎖先於 HTTP，未知結果不重送。D0501／99 只記官方狀態，無本次 POST 歸因證據而不結案。既有網站流程在尚未送出前保留；跨多原發票折讓安全拒絕，真人／真光貿尚未驗收。CYID g0501 的員工申請／主管預覽／核准／原 ID 查回已有 Source／合成 CI；Cloud 單次送出僅內部封鎖程式，無 HTTP 送出路由、遠端 schema20 未部署，D 離線／回線仍待真機驗收。
- **後續範圍**：F 原始碼與合成測試已整合；D/E 真環境切換與 Cloud g0501 啟用、F 真光貿／Cloud 資料回讀、G 最後舊介面退役、H 真實整合驗收全部仍屬 V3.2。正式 Release 維持 V3.1.0；工程 source、遠端 schema／部署、真交易、真人驗收與正式發行分開記錄。

## 3. A～H 功能與完成門檻（範圍不刪減、不改版）

| 階段 | 固定範圍 | 完成證據 |
| --- | --- | --- |
| A 折讓編輯 | 原發票帶入、官方現況、含／未稅與負數折扣、七位精度、超額保護 | 已有 Source＋CI；仍納入 H 真機／光貿整合回歸 |
| B Local 正式送出 | 主管二次驗權、Frozen／SQLite Ledger、單次 g0401、原號查回及明確外部占號後受控下一號 | 已有主管文字覆核及改配號 Source／合成測試；仍待 Local 斷電、實機與真光貿驗收 |
| C CYID 跨機折讓 | 員工／主管多機操作、身份／Device 撤權、加密原因、Grant、D1 Ledger、Audit、受控單次 g0401、未知處置與主管占號改配號 | Source 已接通且 canonical 19 已備妥；遠端仍 schema18／flag 關閉，待受控升級、歸因政策與真雙機／光貿驗收 |
| D Cloud 故障與回線 | Cloud 失聯、光貿可用時的安全授權降級、跨機競爭／撞號、復線對帳和安全衝突處理 | 雲端／光貿不同故障組合、撤權、斷電、同一申請零自動重送 |
| E 折讓作廢 | g0501 Local／CYID：申請、核准、單次送出、官方逐號查回、D0501 狀態 | Local／CYID 均有申請、主管核准及原號查回 Source／合成測試；Cloud g0501 送出維持禁用且無 HTTP 路由。真測試 API、Local→Cloud 單一權威切換、歸因政策及真人驗收未完成，未知只回查 |
| F 正式同步 | 沿現有 invoice_list／allowance_list／invoice_query 完成低頻查證、外部變更與折讓作廢、跨期原號按需補回、PDF Cache 失效及本機 SQLite append-only 官方查證歷程；不變更舊發票清理 | 工程 Source 與合成回歸整合後，真光貿／Cloud CYID 跨機資料回讀仍待受控 H 驗收；官方查證不當成本程式 POST 歸因 |
| G 舊流程退役 | 已檢視 Local／Cloud 人工折讓、人工作廢、候選選號 caller；舊網站人工入口明確標示並區隔新單次送出，歷史仍只讀 | 真光貿及角色／Claim 驗收前不能刪最後舊流程回退；通過 H 後才一次移除舊人工按鈕，保留既有紀錄與 unknown |
| H 最終驗收 | 已有 Windows Client／Windows x64／Cloud／Governance 全套 CI、合成 Local／CYID 權限／Claim／離線／重入測試；真測試公司卡、光貿 g0401/g0501、原 D1 schema19/20、全域 Local 停送確認及回退仍須實測。真人雙 Windows 於上線後補驗 | CI 綠燈僅證明 source，不能取代真實財務與權威驗收。Cloud g0501 原送出路由維持關閉，正式 V3.2.0 Release 需使用者當次授權 |

## 4. 新的工作安排：以「完整可驗證功能」聚合

| 批次 | 一次完成的成果 | 工程／驗收邊界 |
| --- | --- | --- |
| **批次 1：B/C 剩餘整合** | 將既有 Local／Cloud 開立工作流整合成可用流程：管理員明確授權的**唯一** g0401 執行入口、安全撞號與人工異常處置、可信證據及 append-only Audit、WinForms 操作與權限／競態／回覆遺失的合成測試 | 以 **1 個主要功能 PR** 為目標；必要時安全獨立的 D1 契約可額外拆出，但不按「每個 DTO／按鈕／測試」開 PR。任何真光貿測試、公司卡、Worker flag 啟用另設停點 |
| **批次 2：D/E 離線與作廢** | 整合離線／回線安全流程、合法受控碰撞處理及 Local／Cloud g0501 全流程，同一套身份、Ledger、官方原號安全語意 | 依實際程式相依在 **1～2 個功能 PR** 完成；不能把已知撤權視為斷線，不能預先對正式光貿執行 |
| **批次 3：F/G 同步與舊介面收斂** | 官方低頻對帳、舊折讓逐筆歷史及 Audit 可查、資料遷移／保留、刪除被新流程取代的人工入口 | **1 個整合功能 PR** 為目標，舊 UI 僅在新流程驗收後退役 |
| **批次 4：H 集中驗收** | 既有完整回歸 CI、必要修正、真測試環境、Local／CYID 安全門檻、受控 Cloudflare MCP 與回復證據；真人雙 Windows 上線後補驗 | 不為每個驗收案例重開 PR；缺陷集中修正。Release 最後另經使用者批准，延期項保留追蹤 |

這是**工作組織上限目標，不是承諾固定 PR 數或省略安全測試**。同一成果未完成的修正留在同一分支，直到精確 head CI 通過再合併；不用每修一個欄位就升 Patch。一般工程版本仍遵守共通規則 VERSION／BUILD；純規劃文件改寫不改產品版號。GitHub PR、正式 Cloudflare 操作與正式 Release 是三個獨立行為。

**批次 1 現行停點**：工程來源已整合到上述 B/C 主路徑；不再把已完成的 DTO／按鈕／查證子片當作新工作單。接著是原 schema19／Worker 的另行授權受控升級，以及歸因政策和真實驗收。歷史各版工程證據只由 CHANGELOG／PR 追溯，不在本計畫重複逐版報告。

## 5. 不可跳過的整合安全門檻

- **所有交易權威來自光貿**：code=0、相同折讓號、同內容、Cloud 回覆或管理員「已處理」都不單獨代表本次 g0401／g0501 已正式完成。若光貿不提供可信 transaction attribution，就必須明確標示「無法歸因」、走人工查證，不創造假的 confirmed，也不為達標自動再送一次。
- **Cloud 是協調，不是新官方資料來源**：全程沿用 CYID canonical Role／Workspace／Device contract；應用程式不保存第二份身份 authority、不把一般 work_items.ALLOWANCE 復活、不寫 D1 原始買方與 App Key。結果不明時原號、原 application ID 與 sending ledger 永久安全保留，後續人工處置必須可稽核。
- **本機回退防線（V3.1.34 Source）**：在既有 SettingsStore 的 settings.json 持續保存 Cloud 曾參與的 Local g0501 禁送標記，僅阻止本機切換回 LocalOnly 後重新核准／送出；已加入重啟及舊設定回寫測試。此記號不是全球租約，無法排除另一台未入網 Local 裝置，不能當作 D/E 全域互斥完成；Cloud Worker g0501 不得因此啟用。
- **員工原號固定 ID（工程來源）**：在既有 CYID 跨機折讓 WinForms 以員工密碼申請作廢；`CloudAllowanceClient` 將 Workspace／環境／原折讓號映射為固定 SHA-256 申請 ID，先從原 ID 唯讀查回，僅在正式 `VOID_APPLICATION_NOT_FOUND` 時建立一次。斷線、回覆遺失、重啟或改用其他 Device，均不可產生新的申請 ID 補送；由原號重新產生 ID 查回。此設計不驗證全部舊 Local 安裝且不授權 g0501。
- **主管與 WinForms 工程來源**：既有 `allowances-route` 新增 schema20 限定的 `void-preview`／`void-approve`，用原 CYID Device/主管驗權、雙次官方查證與 D1 原子 Grant；WinForms 主管視窗用原申請 ID 預覽並強制輸入「作廢 原號」，沒有 Cloud g0501 送出入口。此處僅代表 Source／Synthetic CI，遠端仍未升級或驗收。
- **跨機入口工程來源**：沿既有 CYID／Device 路徑接 schema20 限定的 `void-create`／`void-status` 和 Windows Client；不提供 g0501 發送路由。主管預覽／核准與 WinForms 已接好；A／B 裝置原 ID 查回、主管 B 撤權、公司卡換版及錯環境拒絕已通過合成 CI。下一步先做原 schema19 受控升級、測試環境與 Local 停送切換核對；雙 Windows 真人驗收依使用者決定於上線後補做，不再建清冊平台。
- **簡化後的單一控制權**：Cloud g0501 僅透過既有 CYID Workspace／Device 的 D1 唯一 Claim 與單一 Worker 協調器送出；斷線只能原 ID 查回，不進行離線 g0501。schema20 不再設無法驗證離線舊安裝的交接 Gate；單一 Cloud g0501 協調器預設禁用且**沒有** Worker 送出路由。待現有 Local 停送、舊 Claim 人工核對並另經使用者授權後，才可另審受控啟用；不採用設備報表雜湊，也不新增清冊平台。此批無 Cloudflare 變更、沒有正式路由。
- **跨 Local／Cloud 作廢互斥是啟用前阻塞條件**：兩台裝置在網路分割時，僅靠各自的 SQLite／D1 claim 無法證明全域唯一送出。g0501 必須有同公司／環境／官方原號的可信單一 owner 與持久、可查證的控制權交接；已知撤權、Cloud 查詢失敗或原號未記 Claim 均不可推定其他裝置未送，回線只按原申請查回且不自動補發。跨模式 owner 無法證明互斥時，作廢送出須拒絕，不能為離線可用性放寬。Cloud g0501 路由及 schema20 升格留待該防線與獨立授權完成。
- **D1 與 Worker 分離**：依使用者回報 schema18 已完成；以後任何新的原 D1／Worker／Bindings／Secret／公司卡操作均須先通知使用者，交由其有 Cloudflare MCP 的本機 Work；事前依共通規則 §11.3 說明預估 rows read/written、呼叫次數、備份／餘額／停止條件，完成後記錄真實計量。不建立試驗 Worker／D1，且不碰 CYID 專用 D1。
- **D／E 官方測試及受控切換停點（雙 Windows 真人驗收已延期）**：上線前先完成原 D1 schema19／Worker 的獨立授權維護與回讀，再受控核對舊 Local 安裝停送及未知 Claim、測試公司卡與單一送出權威。schema20 升格／套用、Cloud g0501 路由與真測試交易須另經審查與授權；未完成前維持關閉。雙 Windows／不同 CYID Device 的角色、撤權、斷線／回覆遺失與原 ID 查回真人操作，於 V3.2 上線後補驗，不再排成這一批的前置條件。合成測試不等同這些真人證據。
- **驗收分層**：Source＋CI、原 D1 schema／部署、測試公司卡與光貿真交易、雙機真人操作、正式 Release 各有獨立證據；任何一層未通過只能標為「待驗」或「不可啟用」，不把 CI 視為正式財務驗收。
- **歷史資源**：PR [#440](https://github.com/simonliu1118-byte/CYapps/pull/440) 仍可逐檔回收 g0501、離線碰撞與測試程式；不能整包合併、帶回舊 migrations／第二套 Work Item owner，或重用不相容序號。

**F 源碼收尾進度（2026-10-22）**：低頻折讓與舊折讓作廢輪巡、未配對原發票按號唯讀補回及查無提示、完整官方摘要的逐次 immutable 觀察歷程已整合至原 SQLite／操作歷程。原發票清理不變，舊歷史不偽造；雲端 CYID/D1 真實讀回、光貿測試回覆與跨機驗收仍須取得遠端授權後核實。目前每日查詢上限為 150 筆折讓清單／50 張原發票（含已知舊折讓輪巡），超限在「上傳問題」提示回來修改程式上限；不將當日對帳標記為完成。

**G/H 集中收尾（2026-10-22）**：已完成舊人工入口 caller 檢視、舊網站流程可見區隔及歷史唯讀；合成回歸依原 Windows／Cloud CI。尚未真光貿驗收，故 **G 最後刪除舊人工入口、H 真交易與全域權威驗收仍未完成**；不另建無用系統、不拆出重複工作單、不把需求移至 3.3。實際通過後在現有 PR 一次移除舊按鈕與其直接 caller（不刪歷史），再獲明確正式 Release 授權。雙 Windows 真人驗收已批准上線後補做。

**H 可離線完成部分（V3.1.40 Build 1）**：已在既有 Cloud 測試加入非空 schema19 的檔案備份、schema20 草案交易失敗／回滾、升級成功且所有舊資料／g0401 sending Ledger／Audit 不變、檔案重新開啟與升級前備份完整還原；g0501 三類永久 Claim 也通過實際關閉重開後零重送。這些只證明隔離 SQLite／Fake HTTP 行為，不代表遠端 D1、Time Travel 或真光貿已驗收。下一個遠端操作仍以 [MCP 交接](V3_2_C_CLOUDFLARE_MCP_HANDOFF.md) 的 schema19 批次為限；schema20 不升格、無 Cloud g0501 路由。

## 6. 3.3 與後續版本邊界不變

- **V3.2 的 A～H 不准挪到 3.3**：g0401、g0501、折讓完整歷史、Cloud／Local、離線、對帳、舊 UI 收斂與真測都要在 V3.2 完成。
- **V3.3 繼承既定的「其他尚未完成項目」**：包括已在 [TODO](TODO.md) 登記的非折讓跨機 OrderID 防撞、剩餘 Work Item／Audit viewer、同步和先前延期的一般功能／樣本議題；不因重新切片新增需求，也不將所有長期選配議題自動變成 3.3 的必要 Release blocker。V3.3 開發開始時，從 TODO 唯一來源確認各項目前進度與驗收條件，再聚合成數個可使用的成果。
- **後續大版本／延期不變**：[CLOUD_ROADMAP](CLOUD_ROADMAP.md) 的多統編 Company／跨公司權限、對外販售或技術中立自架相容，以及依需求評估的營運摘要、125%／150% DPI、快取完整性簽章等不因本次重排被提前開發或取消。原本屬延期項者維持延期。
- V3.0／V3.1 發行後的真實裝置與角色驗收仍由 [POST_RELEASE_ACCEPTANCE](POST_RELEASE_ACCEPTANCE.md) 唯一追蹤，不混進 V3.2 的「已完成」數字。

## 7. 文件責任（避免再次複雜化）

- **本檔**：V3.2 A～H 範圍、最新批次順序、不可跳過的完成門檻；不再逐個 PR 複製完成報告。
- **[TODO](TODO.md)**：一頁式「現況／下一批／阻塞」看板與歷史完成索引；不建立第二份 C7 pending 清單於公開 Git。
- **[CLOUD_ROADMAP](CLOUD_ROADMAP.md)**：3.3 與更長期的架構方向／原始願景；舊 Phase A～E 是歷史概念，不再當作目前可執行順序。
- **[POST_RELEASE_ACCEPTANCE](POST_RELEASE_ACCEPTANCE.md)**：僅 V3.0／V3.1 已准延期的真人實機驗收。
- 私有公司卡、備份、MCP 回復步驟和真光貿紀錄只在使用者授權的私有位置保留，不提交 Public GitHub。
