# CYInvoice V3.2：折讓正式化分階段實作規劃

更新：2026-10-19｜基準：已正式發行 **V3.1.0 Build 0**；本計畫首批工程 Patch **V3.1.1 Build 0**。此檔描述工程切片，不建立另一套永久規則；規則仍以根目錄 REPOSITORY_RULES.md 與 CYInvoice/PROJECT_RULES.md 為準。

## 經確認的需求界線

- **以光貿官方資訊判斷業務事實**：使用者點擊發票詳細視窗的折讓時，應重新執行 `invoice_query`，顯示當時官方已有折讓；管理員核准及實際發送前須再次查詢最新官方狀態與有效剩餘額。D1 離線不應阻止光貿本身仍可連線時的官方查詢。已有合法折讓不代表不可再折；使用者與管理員知情仍核准新折讓是其業務決策。金額超額、摘要缺漏、未明確完成狀態則 fail-closed。
- **本次交易結果安全**：光貿若明確以 `4040161`／`4040162` 拒絕折讓號碼，先核對原號是否已有本次成功交易，保留該次失敗及 ledger 歷史，再可受控改下一號；不把所有 API 錯誤或傳輸失敗當成撞號。HTTP timeout、斷線、送出後掉電與 `code=0` 未確認時，**只查回原號，不自動換號或再次送出**。同一申請的技術重入／意外重送仍須防護；不以「使用者與管理員可能重複做業務折讓」為理由，阻止另外一次合法人工核准。
- **先保留 Local／CYID 舊功能**：在完整 `g0401`／`g0501`、歷史資料可查及安全回滾驗證之前，不直接搬移 #440 舊人工 UI 退役片段。V3.1 的非折讓 Work Item、schema16、正式 ZIP、合併延期驗收檔案都須維持相容。
- **Cloudflare 安全邊界**：repo 只做原始碼、可回復的新 migration 草稿、SQLite 合成升級與完整 CI。任何**原 CYInvoice D1／原 Worker／Bindings／Secrets 實際操作**須先告知使用者，交由另開的**本機 Work + MCP**；不重跑 0014～0016、不操作 CYID D1、不建立替代 D1／Worker。
- **版號**：每一項獨立修改由已發布 V3.1.0 依序推 V3.1.1、V3.1.2……；同項返修才增加 Build。V3.2-A～H 全部完成且取得當次明確發行授權才可發布 V3.2.0，不能沿用 V3.1.0 的發布批准。V3.0／V3.1 真人延期驗收繼續由 [POST_RELEASE_ACCEPTANCE.md](POST_RELEASE_ACCEPTANCE.md) 唯一追蹤，不能篡改為已通過。

## 3.2-A 工程交付（V3.1.2 Build 2）

- 選擇性移植 #440 的折讓 DataGridView，保留原有含稅／未稅單價切換，依原發票 DetailVat 決定預設模式；使用者填含稅時，程式依七位固定小數換算成未稅 UnitPrice／Amount、Tax 與含稅總額，並保存原始含稅輸入供 Core 複核。允許負單價折扣，採固定七位小數及逐行整元稅額。
- 本次官方查詢 allowance 陣列必須完整，已完成／已作廢狀態須核對後才能算有效剩餘額。對無摘要、重號、未知狀態採 fail-closed，不把未知誤算成零折讓。
- 3.2-A 只保存本機人工折讓申請的編輯明細，由 Core 再算並重新查光貿。下一階段 3.2-B 才接 Local 管理員二次官方核對及單次正式 g0401；Cloudflare 最後交本機 MCP。

## 3.2-B 拆分成適中工程批次

- **3.2-B1／V3.1.3 Build 0**：本機 SQLite `LocalAllowanceNumberStore` 配號與不可變管理員核准快照，完整 payload 在送出前持久化，`reserved→sending→confirmed` CAS，結果不明不重送；確定撞號僅記錄 `occupied_external`，**本片不自動加號**。加入重開、競態及稽核邊界的隔離測試。新增 Core 前置能力不等於已完成真正主管身分驗證或 g0401。
- **3.2-B2（下一張獨立 PR）**：從現行人工待辦接 Local 主管驗證、覆核當下光貿重新查詢及當次剩餘可折額驗證；批准與 Frozen DTO 必須先持久化，再由編排器單次 g0401 呼叫／原號查回；只有原號確認不是本次成功且官方明確撞號後，才可進入受控下一號。完整壓測、斷電矩陣另列清楚，不在 B1 一次包辦。

## 3.2-B2a 工程完成（V3.1.4 Build 0）

- Local 管理員在現有人工折讓待辦，以當次帳密登入並預覽 AMEGO 的最新折讓和官方剩餘額；按核准時再次查回，官方快照變動則要求重新查看，並再次驗證金額與品項。正式草稿核准時間、管理員身分與官方摘要雜湊持久化，核准後不可由舊人工流程取消或完成。
- 不新增 Cloud UI／D1，不觸發 g0401/g0501，也尚未進入 B1 的實際帶號 reserve。下一批 3.2-B2b 再把已核准草稿組為 Frozen DTO、Local 配號並實作單次官方送出和原號讀回；舊人工申請維持相容。一次一張中等大小 PR。

## 3.2-B2b 工程完成（V3.1.5 Build 1）

- 已核准的新式 Local 草稿，主管再次驗證後可在待辦「預留折讓單號（不送出）」。首次 reserve 會重新向光貿查完整官方折讓與有效剩餘額，核對與先前主管所見的官方快照；核准時另凍結原發票、買方及已編輯單價／商品摘要，內容改動拒絕配號。
- 僅將淨單價／金額／稅額轉為完整 Frozen request，再交 SQLite ledger 保留固定號碼。同工單重開／重按只回同一 reserved payload，不重新配號。這是帳務安全的準備，**尚未呼叫 g0401，不是正式折讓開立**。
- **3.2-B2c（下一批）**：設計可單次發送的 AMEGO g0401 HTTP 隔離實作與前置 fresh readback／CAS、網路結果不明只查原號、已知撞號的確認流程、真的正式查回後才 confirmed。另一張適中 PR，與 Cloudflare 跨機／g0501 分開。

## 3.2-B2c 工程完成（V3.1.6 Build 0）

- 已在 Core 新增光貿 `g0401` 單次簽名傳輸與 `allowance_query` 原號讀回，寫入前持久化 `reserved→sending` 並禁止任何自動第二次 POST（包含時間差回覆 code=15），code=0 僅待官方核對、並非完成證據。真正查回需與不可變 request 的號碼、日期、金額、買方及品項比對。
- 透過 SQLite 與假 HTTP 的隔離測試覆蓋逾時／重開／官網號碼不符／確定撞號；4040161／4040162 只鎖定原號，不自動換號。使用者要求之含稅／未稅單價切換保持，傳出 DTO 仍為未稅。
- **本片的 Core 發送能力尚未接 Windows 可操作入口，也沒有真光貿交易**。下一批 3.2-B2d 實作主管原權限＋AM EGO 最新官方 preflight 的正式 caller 接線／可取消前後控制、原號安全狀態提示；真測試環境必須明確批准並隔離，不在一般工程 CI 操作正式帳務。
- Cloudflare 仍最後由使用者本機 Work MCP，未動正式 D1／Worker／Secrets。

## 3.2-B2d 工程交付（V3.1.7 Build 1）

- WinForms 沿原 Local 核准待辦入口，按 SQLite ledger 的 reserved 進入「正式送出折讓（單次）」、sending 只開放原號唯讀查回；未預留仍可預留，confirmed／occupied_external 不開放再次送出。
- 同一位原核准管理員重新輸入帳密並明確確認實際交易後，Core 再檢查同一未結案工作項目／公司／環境／主管／核准時間與不可變草稿；發送前另以光貿 invoice_query 查官方完整折讓及餘額。凡摘要、明細或帳務事實不同即在 reserved 停下，禁止 POST。
- B2c LocalAllowanceOneShotService 為唯一正式單次發送 owner：SQLite CAS reserved→sending 完成後才向光貿送出一次 g0401。回覆不明、斷線與重開只允許以原號 allowance_query 查回，不重送或自動加號。confirmed 只代表官方原號與凍結明細吻合，不自動結案人工待辦。
- 新隔離測試覆蓋無權限、官方折讓歷史變動、未知結果重入禁止與原號查回。Windows 真機、光貿測試環境、占號後受控換號、CYID Cloud、g0501、正式部署／Release 留後續批次，沒有執行真實財務交易。

## 3.2-C1 工程草稿（V3.1.8 Build 0）

- 在正式 schema16 之上設計獨立 `allowance_applications` 和 append-only `allowance_application_audit_events`；不擴張非折讓 `work_items.kind`，不保留真發票、買方或密鑰。
- 審查 SQL 暫置 `cloud/drafts/0017_allowance_applications.sql`，尚不升格 canonical migration。用 SQLite 合成升級、失敗回滾、CAS 和備份還原測試審查資料語意；原 Worker/D1 不變。
- C2 再確認 Cloud app 層 CYID 角色／active Device／Workspace fencing、配號 Ledger 與正式 migration；B2e 的 Local 撞號、3.2-D～H 的業務驗收並未因本草稿完成。

## 3.2-C2 工程草稿（V3.1.9 Build 0）

- 使用 #440 的 `0016_allowance_number_ledger.sql`、`0018_allowance_applications.sql`、`allowance-number-ledger.ts` 及合成測試當作**設計來源**，接到現行 `schema16 → drafts/0017`，另增 `drafts/0018_allowance_frozen_ledger.sql`。不能搬回舊 `work_items.kind='ALLOWANCE'`、舊 migration 編號或明文發票細節。
- 加密 Frozen Request、主管官方快照 Grant、原號永久 Ledger 和原號核對 Audit 為獨立表，使用 Request Hash 與 Workspace／Device／公司卡版次作護欄；正式 API 必須在 Worker 執行 AES-GCM、CYID 重驗與官方查回，不可信任 SQL 內任一摘要作為業務成功證據。
- 以隔離 SQLite 驗證狀態單向、同一申請零重送、同張發票合法不同申請互斥不明 sending、確定撞號後下一 attempt、舊 schema16 完整保留、交易回滾及備份還原。C2 不掛 routes、不自動套正式 D1。
- **C3 下一步**：審查 #440 服務端加解密／role／最新 AMEGO readback／ledger 執行 CAS，對照 C1/C2 最終定稿成 canonical migration，經 CI 及 Cloudflare 本機 Work MCP 另行審核後才考慮上線。

## 3.2-C3a 工程切片（V3.1.10 Build 1）

- 保留 #440 舊 Cloud `allowance-applications.ts` 的加密封存／失敗回讀思路，改由獨立 `allowance-frozen-store.ts` 寫入 C1/C2 `allowance_applications`、`allowance_frozen_requests`，不增添第二個 owner，也不動 schema16 現有非折讓 Work Items。
- 加密一律呼叫正式公司卡 AES-GCM owner，AAD 綁 Workspace／application_id／請求 hash；來源、原發票 serial fingerprint 用注入金鑰分域 HKDF-HMAC，禁止直接 SHA256 原發票號或明文存入 D1。
- 建立為單一 D1.batch、查回原 ID 不重建；建立和解密前後重驗內部 CYID actor 和 Device／Workspace，主管及本人僅以 server-owned 身分檢視、不得暴露原文於公開 endpoint。實際 request Device-token、CYID session 在後續 C3b route 層綁定。
- 只做內部儲存／讀取 service；不執行任何光貿 official claim、配號、g0401/g0501，不能將 callback/SQL 保護誤當已驗證正式 CYID 身分。維持 SQL draft，正式 D1/Worker 須待本機 Work MCP 受控批准。
- **C3b 待做**：#440 Cloud manager review/official verifier + scoped routes，對齊現行 app/Device/CYID consumer contract，完整 fail-closed 官方餘額與授權；另拆 C3c 配號/one-shot 和 C4 Windows UI。

## 3.2-C3b1 工程切片（V3.1.11 Build 0）

- 從 #440 移植現有唯讀 `amego-invoice-query.ts`（含測試），不抄舊 `work_items.ALLOWANCE` 路由；新 `allowance-manager-preview.ts` 只在內部對 C3a encrypted application／C2 review+revision 實作 CYID 管理員、active Device 與公司卡前後檢查。
- `invoice_query` 的 `allowance[]` 是可顯示的官方摘要，可能跨原發票且來源摘要不完整，**不**當成單張原發票確切已折金額。管理員預覽必須 `canApprove=false`、`canAllocateNumber=false`、`safeToInferRemainingBalance=false`，不存核准、不配號，也不執行 g0401/g0501。
- **C3b2 下一片**：仍利用 #440 官方驗證來源，但要先建立每筆原號／原發票配對的完整安全證據，再對 frozen items、含稅有效餘額及 manager review 作 CAS 授權與 immutable Grant；必須由 Worker trusted gateway 綁現行 CYID/Device，不能直接相信前端 supplied evidence。
- **C3c** 才可接 Ledger `reserved→sending`／單次傳輸與原號唯讀查回；C4 接 Windows 跨機 UI；Cloudflare D1／Worker 操作另經本機 MCP 批准。

## 3.2-C3b2a 官方逐原發票驗證（V3.1.12 Build 2）

- 依 #440 舊 `amego-allowance-query.ts`、最新光貿文件新增可解析官方折讓完整明細的**唯讀**介面。對同一 `invoice_query` 列出各折讓號逐一執行 `allowance_query`，按每筆原發票號與日期取未稅金額／稅額；不可將可能混合多張原發票的摘要總額直接當本發票的有效已折額。
- 原申請 frozen 資料要核對原發票日期、所有編輯行、七位數金額及合計；官方折讓編號、日期、全部行合計、狀態與已作廢／待處理都須嚴格核對，才產出內部 evidence hash。未知歷史、pending、不同來源或超過可折上限一律 fail-closed；最多查詢 16 筆歷史以限制每次執行成本，超出需另設受控批次，不得默默忽略。
- **本批不做主管核准授權**：Evidence 不含核准位元，回傳 `approvalRecorded=false`、`numberReserved=false`；下一批 C3b2b 以受控 Worker 端 CYID Admin／active Device 前後驗權，合併 evidence、原本 review revision CAS 與 immutable Grant，確認再進 C3c 單次配號／發送。
- `drafts/0017`／`0018` 仍不是 canonical migrations，正式 D1 原 schema16、原 Worker、Secrets、Bindings 不動；沒有真光貿財務交易。真環境驗收留 H。

## 3.2-C3b2b 主管核准 CAS（V3.1.13 Build 1）

- 使用 #440 主管決策模式，改接 schema16＋`drafts/0017`／`0018` 的獨立 `allowance_applications`、`allowance_execution_approvals`，**絕不把舊 work_items.ALLOWANCE 帶回來**。
- Manager 需由未來 Worker 真實 CYID Consumer session + verified Device 組成可信 authority；內部 service 前後重驗同一 ADMIN/SUPER_ADMIN 員工、active Device、Workspace、公司卡修訂及當前 review revision。官方核對直接重執行 C3b2a 完整逐原發票核算（不是接受前端 evidence 或 C3b1 預覽作成功旗標），失敗／逾時／待確認則不得寫 Grant。
- 核准 Grant 使用 `D1.batch` 交易內的 `INSERT SELECT` CAS 約束，後續 `UPDATE application status=approved, revision+1` 必須看到同筆 immutable grant，失敗不重送；對不確定結果只進入原 ID readback，禁止產生額外配號／g0401。SQLite／合成 HTTP 測試覆蓋角色、revocation、公司卡輪替與稽核不可變。
- **這還不是公開 Cloud API**：C3c 再回收 #440 受控 Ledger／單次送出，HTTP route 必須待 trusted CYID/Device 驗證相容並另批測試；正式 schema17／18 升格與 Cloudflare 原 D1/Worker 由使用者本機 MCP 受控分開完成。真實 AMEGO 驗收留 H。

## 3.2-C3c1 首次 Cloud Ledger 預留（V3.1.14 Build 0）

- 使用歷史 #440 的每日序號編碼（999 後以 A00 到 ZZZ 繼續），重接正式 schema16＋review `drafts/0017`／`0018` 的 `allowance_number_ledger`，僅允許已 approved revision＋有效且相同 CYID 主管/Device 的 Grant 進行第一次 attempt=1，使用 D1 單 statement INSERT SELECT + SQL-time MAX(sequence)+1 + UNIQUE 做跨申請序號 CAS。
- 保存相同 application_id 的首次 reserved 只可回讀原號；`sending`、`confirmed`、`occupied_external` 及其他未確認結果在本片全部不得另配新號。只有後續受控撞號確認、官方原號唯讀查回與再次核准證據全部成立時，C3c2+ 才能研究 attempt2；絕不自動換號。
- 以 Synthetic SQLite 檢查同張發票合法不同申請、跨裝置搶當日序號、字母尾號界線、Grant 過期/公司變更/Device 撤權/不同主管拒絕與失去寫入回覆時 readback。預留**不是實際送出授權**，下階段需先重新查最新官方餘額、重新驗權後 CAS `reserved→sending`，再單次 g0401 和原號證據查回。
- 暫不安裝 HTTP route、正式 D1 Migration、任何 AMEGO transaction、Cloudflare Worker／Secrets 變更；真環境 H 驗收與 V3.2.0 Release 另行批准。

## 3.2-C3c2 單次送出 CAS 工程切片（V3.1.15 Build 1）

- 此批完成的是內部、**不能獨立呼叫 g0401** 的 Cloud Ledger 狀態閘門；從 #440 借用一次性 claim 的防重送模式，改接現行 `schema16 → drafts/0017 → drafts/0018`，不恢復舊 `work_items.ALLOWANCE`。
- 以原申請 AES-GCM Frozen request＋C3b2b immutable Grant／核准官方 evidence hash 為唯一來源；再次查 `invoice_query` 與既有折讓每個號碼的 `allowance_query` 全項明細，若官方變動、仍待定或任何金額超額則拒絕；主管／Device、公司卡與 revision 皆前後驗證。
- 使用唯一 SQL-time `UPDATE allowance_number_ledger SET status='sending' WHERE status='reserved'` CAS 並關聯核准 Grant、source fingerprint 衝突、當下 active Device/Workspace 與公司卡 revision；成功仍只能回傳 `providerPostPerformed=false`／`mayPostG0401FromThisResponse=false`。一旦進入 sending，即使 D1 timeout／回覆遺失，也只能以原號查回，不能再次 CLAIM 或改配號。
- **下一批 C3c3** 才將 CAS 和簽名 `g0401` 的單次 POST 合成同一個受控 Worker owner（不能把 C3c2 回傳的 flag 當通用送出許可），並在成功／未知／已知撞號時使用官方原號查詢＋證據逐項確認，繼續 Synthetic Fake HTTP 和不明結果測試。未獲批准不得真送。
- 正式 `cloud/migrations` 仍以 schema16 為末筆，所有 Cloudflare Worker／D1／Bindings／Secrets 操作待使用者本機 MCP 另行逐步批准，正式 AMEGO 測試與 Release 保留 H。

## 3.2-C3c3a 單次官方傳輸＋完整原號讀回（V3.1.16 Build 0）

- 直接移植 #440 的單次簽名 g0401 transport 與 synthetic contract tests，但**尚不掛 Cloud Worker 路由、不呼叫 C3c2 claim、不觸碰真光貿**。強制已封存、已配號的單筆請求；timeout／斷線／bad response 永遠 unknown，沒有 built-in retry；即使 code=0 仍只算 accepted pending。
- 新增與 Local B2c 語意一致的 full-frozen readback 檢查：簽名唯讀 allowance_query 以原折讓號讀回 buyer identifier/name、status、日期、正式稅別、所有商品原發票／日期／描述／數量／單價／金額／稅別／稅額，任何缺漏都不視為已完成。保守地視全項吻合為 **unattributed**（相同號碼可能被外部使用），單獨查回結果一律 `safeToAutoConfirm=false`、`canReallocateNumber=false`。
- **C3c3b 下一批**：設計唯一的受控 Cloud Coordinator，將 C3c2 `reserved→sending` 成功權限與單次 g0401 POST 原子語意耦合；保存 append-only official reconciliation，對 timeout/crash 等未知結果只讀回不重送。已知碰撞與 full-payload match 不可單靠編號自動結案，需具可信發送 attempt 證據。仍不得新增公開路由、改正式 Migration 或真光貿驗證，除非另經批准。
- 原 schema16、draft0017/0018、CYID Consumer contract、Local 既有入口與正式 Release 均維持原狀；正式 Cloudflare Worker／D1 改動需使用者本機 MCP 獨立工作單。

## 3.2-C3c3b Cloud 單次送出協調器（V3.1.17 Build 0）

- 內部 `allowance-one-shot-coordinator.ts` 是唯一 **Cloud service** 封裝 C3c2 `reserved→sending` 和 C3c3a g0401 及原號查回的執行路徑，沒有掛 app.ts 路由。CAS 成功後若斷電，Ledger 已是 sending，下一次只能唯讀原號查回；即使 g0401 code=0，仍需可歸因的證據才允許 confirmed。
- 原號全項吻合也保留 `full_match_unattributed`，因為完全相同號碼和內容仍不足以判斷是否由此次 POST 建立；append-only Audit 只保存官方快照 Hash、原號、稽核人／裝置，不把同號回讀視為正式核准。外部撞號與結果不明永遠禁止自動換號／重送。
- **後續 C 階段仍待完成**：正式 CYID Employee / Device 可信 HTTP route、跨裝置申請／主管覆核、Windows UI、安全歸因/人工覆核政策、draft0017/0018 升格並完成 schema16 升級／回退測試；真 Cloudflare 更新必須交使用者本機 MCP，另需真 Windows／AMEGO 測試批准。

## 工作切片與交付判準（由相依順序推進）

| 階段 | 原始碼工作 | 交付判準 |
| --- | --- | --- |
| 3.2-00／V3.1.1 | 用現行正式 WinForms 第一次折讓申請入口直接執行光貿最新查詢，顯示已有折讓原號、日期、類型、官方狀態及原始未稅／稅額，送出時再次獨立查詢；合併後仍只建立既有人工申請，不提前啟用 g0401 | 真 WinForms smoke、Windows／Cloud／治理 CI；無法查詢即不開啟可提交申請的視窗，不把不完整摘要顯示成零剩餘。 |
| 3.2-A | 從舊草稿精選移植原發票帶入編輯、品項新增刪除、**含稅／未稅單價可切換**／含稅輸入由程式換算未稅送出明細與含稅總額、正負折扣、7 位 decimal 與各稅別；官方有效剩餘額為**整張含稅總額上限**，不以歷史逐品項數量作另一項封鎖 | 固定官方狀態完整性、累計金額、進位與錯誤輸入測試；非完整／待確認的折讓摘要不能當成零。 |
| 3.2-B | Local 管理員覆核時再查光貿，凍結核准及含時區核准時間先持久化，再依 Local SQLite ledger 固定唯一原號，執行 `g0401` 單次 `reserved→sending→confirmed`，timeout 只查原號；保留合法多次部分折讓 | 斷電所有交錯、原號撞號兩種代碼、未知結果不換號、使用者撤回只允許**管理員尚未核准**、真 Windows 自動化／隔離 HTTP。 |
| 3.2-C | 在現行正式 **schema16** 上**重新**設計折讓申請、獨立配號 ledger、核准／稽核／折讓作廢的後續 migration；依審查分段移植 #440 的 Cloud routes、CYID 管理員 WinForms、CAS／Device／Workspace fencing | 合成 16→新 schema、失敗回滾、FK／trigger／Audit、任務跨兩裝置並發及角色拒絕測試；舊草稿序號不可當成已可直接套正式 D1。 |
| 3.2-D | Cloud 離線而光貿可用時，使用原有本機可用的授權來源與核准資訊執行 g0401；**明確撞號**可查原號並持久化碰撞後嘗試新號；回線後核對官方紀錄與 D1 Audit，不能把已知撤權當普通斷線 | 多裝置同號先後成功／碰撞、同 Work Item 複製、回覆不明、兩種服務分開故障與權限改變測試。**不能只靠光貿拒絕同號**來防止對同一筆執行動作自動重送。 |
| 3.2-E | Local 與 CYID 正式接通 g0501 申請／管理員核准／一次送出／原折讓單號官方狀態讀回／D0501 完成證據；不把開立成功的折讓以撤回申請消除 | 真 API 測試環境、結果不明只回查、不重送；非授權人不可作廢。 |
| 3.2-F | 定期低呼叫量 `invoice_list` + `allowance_list`，必要時以 `invoice_query`／`allowance_status` 核對，反映光貿網站外部處置、跨期回掃、PDF Cache 失效；不杜撰官方 Audit | 光貿失聯→網站人工處理→回線後原 SQLite 自動顯示官方最新資料；不刪舊歷史。 |
| 3.2-G | 新 API 流程驗收後才移除一般人工選號／已人工處理入口，確保舊待辦與舊人工折讓可唯讀查回 | 最後 caller 查核、舊資料遷移／回退；缺少歷史欄位一律保留 unknown，不能補假值。 |
| 3.2-H | 光貿**測試環境**真 g0401／g0501、兩模式、部分及多次折讓、負數折扣、CAS、離線、真 Windows，原 D1 的安全備份／migration／Worker 本機 MCP，最終版身分／CI／ZIP | 正式 V3.2.0 Release 另行請示；不得以合成 CI 代替影響正式帳務的 API 驗證或遺留 3.2 業務核心至 3.3。 |

## 已有來源與不可直接合併的部分

- 舊 [PR #440](https://github.com/simonliu1118-byte/CYapps/pull/440) 的 `cyinvoice/v3-recovery-invitation-schema` 草稿，有原發票品項編輯、官方餘額計算、Local `g0401`、Cloud 送審／配號／單次送出、`g0501`、跨機 manager UI、雙日期官方對帳及原始合成測試；**不是**已上線且不得整包合併。
- 舊 `drafts/0016_allowance_number_ledger.sql`、`0018_allowance_applications.sql`、`0019_allowance_void_workflow.sql` 為歷史依賴與命名；正式環境已有完全不同內容的 canonical **0016**。應從當下 main schema16 建新序號、重整外鍵／kind CHECK 與 Apply／Rollback 前置條件，並先以 isolated SQLite 驗證。
- 用戶端仍需避免混淆「已有合法折讓，再開另一筆是業務決策」與「同一筆未確認送出遭網路中斷而程式自動重送是技術缺陷」。管理員每次最終核准都須看到重新查詢的官方狀態，但官方查詢不是可替代 SQLite／D1 compare-and-set 的原子鎖。
