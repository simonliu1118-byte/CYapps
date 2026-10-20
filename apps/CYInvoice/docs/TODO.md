# CYInvoice 待辦與驗證

更新日期：2026-10-20（Asia/Taipei）。本檔保留工程／產品待辦與歷史 checkpoint；**V3.1.0 Build 0 已取得使用者當次正式發布授權**，精確 Release 結果以 GitHub tag／CI 為準。V3.0+V3.1 真人未驗收項**僅**在 [唯一的發行後待驗收清單](POST_RELEASE_ACCEPTANCE.md) 勾選及追蹤，本檔／舊 RC 表不得代替真人已完成證據。下方較舊 V3.0.0／schema15／0016 草稿描述保留歷史，不是目前狀態。

- [V2.6.19 歷史 Release](https://github.com/simonliu1118-byte/CYapps/releases/tag/cyinvoice-v2.6.19) 與 [歷史 Release CI #38336787968](https://github.com/simonliu1118-byte/CYapps/actions/runs/38336787968) 已完成。沿用使用者接受本批本地／隔離測試及精確 source CI 的決定；人工折讓歷史缺欄位留 V3.0，不重新打開本批 Release gate。
- 本公司 CYID activation 已完成；2026-10-13 另經獨立批准完成私有加密備份／本地還原核對 → 原 Worker 相容升級 → 原 D1 四張舊帳號／轉換表退役 → 保留資料逐筆核對。部署證據：[Development Deploy #38396129643](https://github.com/simonliu1118-byte/CYapps/actions/runs/38396129643)；完成範圍見 §7.7。這是原 development 維護，不是另一個 production 環境或新 Windows Release。
- 本公司已使用 CYID。**以下為歷史 V2 註記**：其他公司舊 Built-in／Self-hosted 如需留存須用各自原 V2 相容配置；**V3.0 新版只支援 Local／CYID Cloud**，不再提供 Built-in／Self-hosted 現役模式，也不能復活本公司已退役的舊帳號表。
- 現行交接：[V3_1_HANDOFF.md](V3_1_HANDOFF.md)；**V3.0／V3.1 發行後待驗收唯一清單**：[POST_RELEASE_ACCEPTANCE.md](POST_RELEASE_ACCEPTANCE.md)。[RC_TEST.md](RC_TEST.md) 保留詳細歷史案例供操作參考，不另做第二份未驗收統計；私人資料不能放入公開 repo。
- 不建立／上傳／部署 Cloudflare 測試 Worker、測試版本或 handler；舊測試資源不重試刪除，不為補驗變更真實帳號或撤銷原裝置。本次維護授權不延伸為持續刪表／發布授權。

## CYInvoice 3.2：完整折讓 API／跨機／離線／同步（正在開發）

- [ ] **3.2-A～H**：正式需求、相依階段、原始碼移植及驗收門檻見 [V3_2_IMPLEMENTATION_PLAN.md](V3_2_IMPLEMENTATION_PLAN.md)。僅使用既有光貿官方查詢判斷當時折讓事實；兩筆合法人為折讓不能只因發票相同被自動禁止；程式必須防止同一次送出遭盲目重送。
- [x] **3.2-00／V3.1.1 Build 0 原始碼第一片**：開啟折讓申請前，新增光貿 `invoice_query` 新鮮回查，顯示官方折讓清單並保留 SubmitAsync 提交時二次查回；官方不可用時不開啟提交視窗，沒有 g0401／g0501 真交易。WinForms smoke 補官方摘要斷言。PR 與 exact-head CI 依本次工作結果登記。
- [x] **3.2-A／V3.1.2 Build 2 Source**：原發票折讓商品編輯、含稅／未稅單價切換、含稅自動換算未稅送出明細與總額、負數折扣、七位固定小數、完整官方有效剩餘額 fail-closed，申請時重新查光貿並由 Core 核對草稿；尚不接 g0401/g0501，也不將 CI 當真人實機驗收。
- [x] **3.2-B2d／V3.1.7 Build 1 Source**：在既有 Local 折讓待辦安全接通管理員重新驗權、正式交易確認、光貿最新完整回查與額度核對、單次 g0401 與原號唯讀查回；Core 再查凍結核准資料，結果不明或撞號絕不自動重送／加號。新增無權限、官方資料變動、結果不明後零重送等隔離測試。真 Windows／光貿測試環境仍未驗收，不代表實際開立成功。
- [x] **3.2-C1／V3.1.8 Build 0 Source 草稿**：以現行 schema16 設計 Cloud 折讓申請及 append-only 稽核初版，drafts/0017 未列入正式 migrations；SQLite 隔離 schema16→草稿、失敗回滾、版次 CAS、同發票來源多次合法申請及 FK 驗證。沒有配號、Cloud API 或正式 D1 操作，待 CI 驗證後才能列為 Source 驗收。
- [x] **3.2-C2／V3.1.9 Build 0 Source 草稿**：以 #440 加密 Frozen Request、Manager Approval、配號 Ledger 與唯讀 Audit 為原始素材，重排到 schema16＋C1 0017 後的 drafts/0018，不把舊 `work_items.ALLOWANCE` 帶回；新增獨立 SQLite 回滾、加密合成資料、雙申請防同時發送及撞號後保留原 attempt 測試。**只是 Source/CI 待核對，沒有正式套 D1，也沒有使用真 AMEGO 或 Worker 執行角色。**
- [x] **3.2-C3a／V3.1.10 Build 1 Source**：從 #440 內部折讓服務選擇性改接獨立 application_id，正式程式使用 Workspace＋申請 ID＋請求雜湊 AES-GCM AAD 加密 Frozen template、domain-separated HMAC 原發票 fingerprint、單一 D1.batch 原子儲存，以及讀回時 CYID actor／Device 二次檢查。透過 schema16→草稿17／18 合成 SQL 與 esbuild 隔離測試驗證越權、撤銷、內容綁定、公司版次競爭及無敏感明文。**沒有 Cloud route／正式 AMEGO／真 D1 或 Worker 操作，待 PR exact-head CI 驗證。**
- [x] **3.2-C3b1／V3.1.11 Build 0 Source**：從 #440 恢復 AMEGO `invoice_query` 唯讀簽名 parser／isolated tests；內部 Cloud 主管以 CYID 角色與 active Device 查閱 C3a Frozen request，前後比對公司卡、rev CAS、當次官方摘要。所有預覽標示 **不可直接核准、不可配號、不可從官方摘要推論有效餘額**。C3b2 再做完整官方逐項核對及持久核准，C3c 再接單次執行。仍未掛 Cloud route、D1 正式 migration 或真 AMEGO 請求；Source 完成須以 exact-head CI 為準。
- [x] **3.2-C3b2a／V3.1.12 Build 2 原始碼切片**：從 #440 回收光貿 `allowance_query` 的唯讀協定，嚴格逐號查完整品項與原發票對應；有效餘額不使用 `invoice_query` 可能跨發票的折讓摘要金額，而以每張折讓官方明細原發票／未稅額／稅額、7 位固定小數核算。缺漏、結果未定或超額 fail-closed。新 isolated provider regression 僅產證據，不寫 Grant、不配號、不送 g0401/g0501。C3b2b 再接可信完整核對與主管二次驗權／永久核准 CAS，須先確認本批 CI。未動正式 D1、Worker、真 AMEGO。
- [x] **3.2-C3b2b／V3.1.13 Build 1 Source**：從 #440 的核准概念改接獨立折讓申請與 C2 immutable Grant；Cloud 主管重新驗權／active Device 與 Workspace/公司卡核對，重新查完整光貿證據並確認 Frozen request 與 revision，再單一 D1.batch INSERT Grant＋CAS 改狀態；歧義回覆只讀回原 application_id，不盲目再核准。新增 SQLite schema16＋draft17/18 合成 role/revoke/company rotation/revision race/append-only 驗證，仍未掛 Cloud HTTP route 或套正式 D1。完成狀態以 exact-head CI 為準。
- [x] **3.2-C3c1／V3.1.14 Build 0 Source**：從 #440 只回收每日單號編碼與 SQL-time MAX(sequence)+1 配號 CAS，改接 C1/C2 approved application＋manager grant＋Frozen Request/公司卡版本；Manager 與 Device 前後檢查，並確保同申請 reserved 可讀回但不二次配號；明確碰撞與 sending 永不自動加號。Schema16→draft17/18 隔離測試覆蓋跨申請搶序號、回覆遺失、999/1000 編碼、過期核准及撤權。尚無 g0401、公開 Cloud route、正式 Migration／Worker；下一片 C3c2 才實作單次 `reserved→sending` CAS 與官方讀回。以 PR exact-head CI 為準。
- [x] **3.2-C3c2／V3.1.15 Build 1 Source**：參照 #440 的 one-shot CAS，為現行 C1/C2 Cloud Ledger 新增內部 `reserved→sending`：原號完整性、最新逐原發票官方餘額、核准官方證據 Hash、CYID 主管/Device 前後驗權及同申請唯一 UPDATE CAS。官方資訊變動、同一原發票另一案 sending、回覆遺失或撤權都不得盲目重試。SQLite＋合成 signed HTTP 測試包含 race/timeout；**本批沒有實際光貿 g0401、沒有 Cloud Worker 路由或正式 D1 Migration**。下一片 C3c3 才把 CAS 與 g0401 單次實際呼叫及原號查回整合在同一受控服務。須以 exact-head CI 為準。
- [x] **3.2-C3c3a／V3.1.16 Build 0 Source**：保留 #440 的 Cloud g0401 單次簽名傳輸（不重送、code=0 待核對、撞號不自動加號），另對照 Local B2c 的官方原號逐商品核對建立強制 buyer／每筆明細／稅額／日期一致的唯讀 readback；精確內容吻合仍不直接授權 Ledger confirmed，以防外部佔號冒充本次交易。新 isolated Fake HTTP regressions，沒有 app.ts 路由、CAS caller、真 g0401、正式 D1/Worker。**C3c3b 下一批**只接單一受控 coordinator 的 C3c2 CAS→單次 g0401→原號查回與 confirmed 的最後安全政策。以 exact-head CI 為準。
- [x] **3.2-C3c3b／V3.1.17 Build 0 Source**：Cloud 內部唯一 coordinator 串 C3c2 fresh AMEGO 預檢／CAS→單次 g0401→原號唯讀查回與 append-only reconciliation Hash Audit。全面驗證斷網/撞號/撤權/Crash 結果不明只讀原號，保守地不將 readback full match 直接寫成 confirmed（外部同號歸因不足）。合成 SQLite＋Fake HTTP 全跑；無正式 Worker route／D1 操作／真 g0401。
- [x] **3.2-C4a／V3.1.18 Build 1 Source**：新 Cloud Allowance API（申請建立、本人／主管查詢、主管 review CAS／官方唯讀 preview／正式 Grant 核准／Ledger reserve）與現有 CYID + 活動 Device 身份閘門相接，對 schema18／`ALLOWANCE_API_ENABLED` 雙重上鎖；入口大小上限及錯誤不洩漏 Buyer/AppKey/Frozen 明文。用合成 SQLite、假 CYID 與簽名假 AMEGO 測試。原 D1 schema16／Worker flag 仍未更動，沒有任何公開 g0401 路由。C4b 繼續 Windows 跨裝置 UI，C4c 才評估可歸因發送入口及正式 D1 升格。
- [x] **3.2-C4b／V3.1.19 Build 1 Source**：由 #522 的安全 CloudAllowanceClient 與合成 HttpClient 測試於 #525 之後的乾淨 main 重建；復用 #440 保留原申請 ID／未知結果唯讀查回的思路，不帶回舊 Work Item 折讓 owner。Create/List/Review/Approve/Reserve/Status 均不直接發送 g0401；跨機 WinForms、正式 migration、本機 MCP 與真人雙機驗收後續批次處理。CI 仍須精確 head 驗證。
- [x] **3.2-C4c／V3.1.20 Build 0 Source**：從 #523 選擇性接 WinForms CYID 跨機折讓主管清單、相同 ID 查回、覆核、官方唯讀預覽、核准、預留單號與無轉址 HTTPS；保留非折讓跨機待辦；以 WinForms smoke／Fake HTTP CI 驗證。不提供 g0401 送出或未知結果重試；C4d 申請編輯器與 C5 migration 後續分批，遠端 Cloudflare 未動。
- [x] **3.2-C5／V3.1.21 Build 0 Source 演練**：選擇性回收 #524 獨立非空 CYID-only schema16 合成 SQLite 備份／17+18 草稿套用、各階段中途 SQL 失敗 rollback、FK／integrity／舊表不變與備份復原；不升格 migrations、不中斷原 Worker、不碰真 D1。下一步需正式 0017/0018 升格與 Cloudflare 專屬預算估算、C4d 申請編輯器本機安全持久化；實際遠端操作僅由使用者本機 Work + MCP 執行。
- [x] **3.2-C4d／V3.1.22 Build 1 Source**：從原折讓編輯器加入 CYID Cloud 提交，先以 1 筆 bounded list 確認 API ready，光貿最新餘額及 Frozen 明細再驗算；本機僅持久化唯一 application ID／scope／payload SHA256，之後一次 HTTP create，結果不明只查原 ID。無非折讓 Work Items owner 混用、無 g0401 送出。**仍待** Cloud 申請原因加密展示、正式 0017/0018 升格與真實雙機端對端驗收，不能稱整個 C 完成。
- [x] **3.2-C4e／V3.1.23 Build 0 Source**：員工折讓原因與純光貿 JSON 共同綁進 AES-GCM Frozen Envelope v1，HMAC 涵蓋原因；Cloud 端檢查 reason 非空且有界，通過 CYID 本人／主管驗權後 GET 才解封；主管介面可看原因，list/status 無原因或敏感模板。原正式 schema16／Worker 不變，仍需 canonical migration、受控首次送出策略與雙機真測。
- [x] **3.2-C6／V3.1.24 Build 1 Source**：將審查完成的 0017／0018 唯一升格到 canonical migrations 並移除舊 drafts；全套 Cloud／Windows／Governance CI 須驗證 schema16→18 rollback、舊 Work Item／CYID 專屬護欄與 read-only preflight；`SCHEMA_VERSION=18` 僅是待部署原始碼設定，不代表遠端已改動。真正 D1/Worker 依 [V3.2-C 本機 MCP 交接清單](V3_2_C_CLOUDFLARE_MCP_HANDOFF.md) 審核／執行。
- [ ] **3.2-C7 Cloud 安全送出歸因／驗收**：C3c3b 既有單次 coordinator 不可把完整原號讀回誤當可歸因成功。仍需經安全審查的實際送出確認及 unknown 狀態人工歸因、跨裝置併發矩陣、真 Cloudflare／光貿測試環境和 WinForms 驗收才可完成 C；不得提前開放旗標。
- [ ] **3.2-B2e～H 正式折讓執行**：B2d Source 入口已完成；接下來保留占號後受控換號、安全並發／斷電矩陣、Cloud／g0501／同步、實機與光貿測試環境驗收及正式發行，依 V3.2 計畫分批處理。不得以 CI 冒充真業務驗收。
- **Cloudflare MCP 停點**：正式 CYInvoice D1 已是 3.1 schema16，3.2 的新 migration 必須從此基準另設新序號並先做隔離回滾測試。需要套用原 D1／更新原 Worker／Bindings／Secrets 時，另通知使用者開本機 Work；目前**不需要**動 Cloudflare。V3.0+3.1 延後實機驗收仍只在 [POST_RELEASE_ACCEPTANCE.md](POST_RELEASE_ACCEPTANCE.md) 記錄，不在本節重設勾選狀態。

## CYInvoice 3.1：非折讓跨機工作項目（Minor V3.1.0；2026-10-19）

- [x] **V3.1-D Source／V3.0.4 Build 0**：0016 由草稿提升為 canonical migration，SQL 行為不變；schema source marker 為 16，Work Item 隔離測試從正式 migration 恰好執行一次，V3.0 歷史測試仍鎖在 schema15。尚未因 source 提交啟用遠端功能，實際本機 MCP 結果以私有 readback 為準。以下草稿／V3.0.3 狀態保留為歷史，不覆蓋此項。
- [x] **V3.1-D 本機 MCP 遠端切換（使用者完成回報，不是本對話獨立遠端 readback）**：原 CYInvoice D1 先核對原 schema15/CYID_ONLY 與 15 筆 migration、加密備份還原及回滾演練，再只套用 canonical 0016 一次，更新原 Worker schema16／WORK_ITEMS_ENABLED=true；前後資料與約束回查通過，私有證據保留本機、不含於 GitHub。禁止重跑 0014／0015、建立新 Worker／D1 或操作 CYID D1。

- **3.1 正式發行基準**：`VERSION=3.1.0`、`BUILD=1`，使用者 2026-10-19 明確批准先發布、雙 Windows／CYID 角色等留發行後。發行工作流程必須從 main 重驗並產 ZIP／SHA-256；上一正式版 V3.0.0 的 Release 只在新版本發布成功後才依使用者要求另行移除，tag／Git 歷史不受影響。
- **使用者確認**：3.0 Cloudflare 安全升級已在本機處理；本次沒有本機 MCP 遠端 readback，因此標記「使用者確認」，**不冒充這輪重新驗證**。3.0 真 Email／雙 Windows 災復實測留後續，不作為 3.1 Source 開發的阻塞。
- **固定 3.1 範圍**：僅 `VOID`（未收紙本作廢的人工覆核）、`UPLOAD_ISSUE`（上傳／官方結果不明的本機例外）、跨 Device 管理員待辦、人工審查及 Audit。Work Item `closed` 只表示人工流程封存，**不表示 AMEGO 官方作廢、上傳成功或允許重送**；一般正常發票／買方細節不進 D1。折讓與 g0401/g0501 **全部在 3.2**，其他延後 3.3。
- [x] **3.1-A 已合併** [#482](https://github.com/simonliu1118-byte/CYapps/pull/482)：CYID-only Work Item 資料層、Workspace／active Device SQL fencing、來源匿名雜湊、修訂版 CAS、隔離 SQLite 測試；`cloud/drafts/0016_non_allowance_work_items.sql` **仍是草稿，未加入 canonical migrations**。
- [x] **3.1-B1 已合併** [#483](https://github.com/simonliu1118-byte/CYapps/pull/483)：SUPER_ADMIN／ADMIN 唯讀 `get/list` API、最小欄位、游標分頁、權限重驗；`WORK_ITEMS_ENABLED=true` 且 `SCHEMA_VERSION>=16` 才能進入，預設拒絕。
- [x] **3.1 Windows 唯讀 Client 已合併** [#485](https://github.com/simonliu1118-byte/CYapps/pull/485)：`CloudWorkItemClient` Get／List、回覆驗證與 Windows 契約測試。
- [x] **3.1-B2 API 已合併** [#486](https://github.com/simonliu1118-byte/CYapps/pull/486)：USER 可建立 VOID 待審項目及上傳問題報告；只有管理員／超管能以 `open→review→closed` 和 expectedRevision 做人工覆核；再次驗 CYID／active Device、失敗查回、不盲目重送、不呼叫 AMEGO。隔離 API／SQLite 測試通過。
- [x] **3.1-B2 Windows 寫入 Client 已合併** [#487](https://github.com/simonliu1118-byte/CYapps/pull/487)：僅傳匿名來源識別／必要安全欄位、嚴格比對 `recorded` 但 `officialVerified=false`、`providerActionPerformed=false`，不接受假官方成功回覆，不自動重送。
- [x] **3.1-C1 管理員 WinForms 已合併** [#489](https://github.com/simonliu1118-byte/CYapps/pull/489)：CYID Cloud 限定「跨機待辦」、人工覆核／封存、逾時回讀；Local 不顯示，既有發票操作不變。與它重疊的 [#488](https://github.com/simonliu1118-byte/CYapps/pull/488) 未合併並保留歷史。
- [x] **3.1-C2 本機例外回報已合併** [#490](https://github.com/simonliu1118-byte/CYapps/pull/490)：原上傳問題視窗需使用者明確確認才回報，HMAC-SHA256 穩定 opaque Work Item ID／referenceHash，禁止外傳發票／OrderID／買方／錯誤原文，回覆不明顯示同 ID 查回。
- [x] **3.1-C3 稽核草稿已合併** [#491](https://github.com/simonliu1118-byte/CYapps/pull/491)：`work_item_audit_events` append-only，同筆 SQLite transaction 記錄 actor／Device／revision／封存原因，阻止異常轉態與修改／刪除；隔離測試已通過，**並非正式 D1 已套用**。
- [x] **已合併 Source 的 CI 基準**：[#487](https://github.com/simonliu1118-byte/CYapps/pull/487)、[#489](https://github.com/simonliu1118-byte/CYapps/pull/489)、[#490](https://github.com/simonliu1118-byte/CYapps/pull/490) 的 Windows／Cloud／Governance 檢查成功；[#491](https://github.com/simonliu1118-byte/CYapps/pull/491) 的 Cloud／Governance／預檢成功，合併 main [Cloud Check #39112276145](https://github.com/simonliu1118-byte/CYapps/actions/runs/39112276145) 成功。**CI 不是雙機實測，也不是 Cloudflare 部署證據**。
- [x] **3.1 Source 收尾第 1 批（PR [#493](https://github.com/simonliu1118-byte/CYapps/pull/493)，已合併，Windows／Cloud／Governance CI 全成功）**：逐段核對本機匿名 HMAC 來源→Worker CYID／Device／Workspace 驗權→管理員 open/review/closed CAS→append-only Audit 的既有隔離測試；WinForms smoke 改用真正跨機待辦介面，查詢權限失效／結果不明清除舊管理員清單。過時 9/29 對話交接與已完成 3.0 MCP 操作清單移出現行文件，歷史留 Git。
- [x] **3.1 結果不明人工查回 Source 補修（PR [#494](https://github.com/simonliu1118-byte/CYapps/pull/494)，已合併，Windows／Cloud／Governance CI 全成功）**：原跨機管理員視窗可直接輸入匿名 Work Item ID，重新完成當次 CYID 管理員驗證後只做 `get`；可核對已封存項目及版次，失權／缺項不冒充成功。覆核結果不明會保留原 ID 供查回，不重新送出轉態或任何 AMEGO 操作；Windows smoke／Client contract 補測。
- [x] **3.1 Source 端到端契約／CI 收尾**：已沿 #482～#491 真實 Worker、Windows、D1 草稿與隔離測試核對匿名來源／CYID 角色／active Device／Workspace／CAS／append-only Audit、Local／CYID 分流、重複及不明回覆；由 #493 補真正 WinForms 控制項測試及失權清除舊列表、#494 補 ID 純讀回查。#493 [Windows CI #39128414131](https://github.com/simonliu1118-byte/CYapps/actions/runs/39128414131)／[Cloud #39128414196](https://github.com/simonliu1118-byte/CYapps/actions/runs/39128414196)／Governance 成功；#494 [Windows #39135608021](https://github.com/simonliu1118-byte/CYapps/actions/runs/39135608021)／[Cloud #39135608039](https://github.com/simonliu1118-byte/CYapps/actions/runs/39135608039)／Governance 成功。這是**隔離 Source 驗證，不等同真正多 Windows／實際 DPI／線上 D1 已驗收**。
- [x] **過時交接清理**：#493 刪 2026-09-29 舊聊天交接及 3.0 歷史 MCP 切換清單；[#495](https://github.com/simonliu1118-byte/CYapps/pull/495) 刪重複 400 行的舊 Cloud 工作交接並更新 12 處目前文件入口。現在僅 [V3_1_HANDOFF.md](V3_1_HANDOFF.md) 為此階段交接、[TODO.md](TODO.md) 為唯一待辦；原歷史在 Git 可稽核。
- [x] **3.1 唯讀回查資料最小化（[#497](https://github.com/simonliu1118-byte/CYapps/pull/497)）**：Worker 的管理員單筆 `get` 僅回傳 Windows CAS 所需七欄，內部員工／Device／審核與時間等資料留在原 D1 的稽核事件及工作項目表，不外傳；兩條隔離 API 測試都要求固定欄位集。本項僅 Source，並未套用 0016 或啟用 Worker。
- [x] **恢復版本遞進／工程版 V3.0.1 Build 0**：此前 #482～#497 累積 3.1 Source 但漏推 VERSION；自 #498 工程批次補登為一個可追溯 Patch，不補造假歷史。已同步 VERSION、CHANGELOG、使用說明、README、工程版內容及交接；往後獨立工作 Patch +1、同項返修 Build +1、原版本重跑 CI 不升版；**正式 Release 仍停在 V3.0.0**。
- [x] **3.1 單筆管理員查回資料最小化**：[#497](https://github.com/simonliu1118-byte/CYapps/pull/497) 已完成 Worker `get` 僅需七欄，Windows client 拒絕多餘員工／Device／audit 欄位，完整 D1 Audit 原樣保留。Cloud／Windows／Governance CI 全通過，無 Cloudflare 部署。
- [x] **V3.0.2／跨機待辦視窗欄寬**：獨立 Patch（非 V3.0.1 返修），將七欄舊固定寬度改為按 ListView 實際寬度配置；WinForms 真實控制項 smoke 驗證預設 920×500 和最小 740×400 視窗、保留 ID 欄與捲軸空間。僅工程 CI，不冒充 DPI 或真人驗收。
- [x] **V3.0.3／非折讓跨機回報按鈕資格判定**：檢視紙本未收回作廢／上傳技術異常的實際本機流程，兩者已接入「上傳問題」需使用者明確確認的跨機回報。修正 Cloud 模式下不支援的折讓／已結案／未知項目曾能誤按「回報跨機」的 UI 缺口，只讓 V3.1 支援的未結案非折讓例外且 Device／Workspace／CYID 員工權限準備好時啟用；Core 契約測試涵蓋所有允許／拒絕類型。獨立 Patch V3.0.3 Build 0。
- [ ] **3.1 真人驗收（使用者批准發行後續補）**：統一由 [POST_RELEASE_ACCEPTANCE.md](POST_RELEASE_ACCEPTANCE.md) 管理；不要因正式發行而勾成完成。
- [x] **3.1-D 已由本機 MCP 回報完成**：原資料庫升至 schema16／原 Worker 功能受控啟用；本段是完工摘要，細節只留私有證據，不重做遷移。真人跨機驗收留 [合併清單](POST_RELEASE_ACCEPTANCE.md)。
- [x] **V3.1.0 正式發行已獲當次明確授權，開始發布準備**：從 `main` 重驗 VERSION／BUILD、Windows／Cloud／治理／正式封裝和 SHA-256，`cyinvoice-v3.1.0` tag／Release 由正式 workflow 建立；成功事實以該 run 為準，不因打勾就宣稱 Release 已存在。
- **歷史分支處理**：來源 [#440](https://github.com/simonliu1118-byte/CYapps/pull/440) 仍是未合併 Draft，僅供選擇性復用，**不得整包合併**；重疊試作 [#481](https://github.com/simonliu1118-byte/CYapps/pull/481)／[#484](https://github.com/simonliu1118-byte/CYapps/pull/484) 均已關閉未合併。

## 2026-10-17 V3.0 版本切片與接續（3.0 已合併 main；以下為正式切換追蹤）

> **以下 V3.0 段落為當時的 Source／部署前歷史 checkpoint**，包括「當時尚未正式發行」「當時 Worker schema13」等敘述；現行事實以本檔最前段 3.0 Release／3.1 Source 進度及使用者的本機 MCP 回報為準，**不把舊未執行清單拿來重跑 0014／0015 或 CYID 退役**。


### 2026-10-18 最新停點：Cloudflare 改由本機 MCP，其他 GitHub 工程先收斂

- **使用者決定**：Cloudflare 原 D1、Worker、Binding、Secret 與實際部署**只在本機 MCP** 執行；GitHub 端可繼續程式、測試、合併程式碼與文件，但本項不暗示授權任何正式 D1 寫入／部署或 Windows 正式 Release。3.0 的舊本機切換清單已歸檔於 Git 歷史，勿重跑已完成升級；現行待辦與必要私有 readback 門檻以本檔最新 3.1 章節為準。
- [x] **CYWEB Source 已完成並合併**：復原整合 [#192](https://github.com/simonliu1118-byte/chihyuan-web/pull/192) 已以 commit `7ba87b8b30b58af935fef316bd61cfb3d55792ec` 進 CYWEB `main`，Governance／Runtime Check／deployment-config CI 通過；但 `CF_CYINVOICE_RECOVERY_SERVICE` 預設未連線，不能宣稱新的 Recovery UI 已部署或可使用。舊 #151 不再是待合併工作。
- [x] **復原過期自動結清**：10 分鐘失效；新 `operationId` 的已授權簽發，先在相同 D1 batch 中將已過期尚未消耗的邀請標成 revoked／delivery failed，寫 `AUTO_EXPIRED` Audit，再創建新 pending 邀請；read-only status 不更動 D1、舊 Device 不因到期被撤銷，防止永久佔用邀請名額；舊 operation 不自動重送。
- [x] **3.0-A 雙模式安全收斂**：舊 Built-in Worker 沒有明確 `identityProvider=CYID` health 標記時，Windows 在建立 Device 前拒絕加入，不回退舊 Employee authority／Snapshot；CI smoke 涵蓋 CYID 正常 JOIN／斷線後原 Pending Token 找回／舊 Worker 拒絕。既有 API 1 相容層 marker8 不被誤認為 D1 schema。
- [x] **Source／CI 已合併交付**：PR [#476](https://github.com/simonliu1118-byte/CYapps/pull/476) exact head `e0c35b38038f8df73866228716e590d1e119afb8` 的 Governance、Cloud Check（SQLite／0014+0015／過期自動結清與回滾）、Development Cloud Validate、Windows x64 Build／可攜套件／敏感資訊掃描**四項全成功**；Source merge `f546b2a550be21fd1b49583c8343d1ca8e599d52` 已進 `main`。**Cloudflare 遠端部署步驟跳過**，工程包不是正式 Release。
- [ ] **本機 MCP 與真人驗收**：按新交接逐步處理原業務 D1 的 0014／0015、獨立 issuer profile 維護、Worker 私有 RPC/flags、CYWEB 可選 Binding，及真 Email、雙 Windows、DR-01～13、Local／CYID／現有 V2 業務回歸；完成前 V3.0 **未上線**。原 2026-10-13 `CYID_ONLY` 退役已完成，不要重跑。


### 2026-10-18 CYWEB V0.9.0 介面重整／恢復整合（歷史來源，已由上段更新）

- [x] 已核對 CYWEB `main` `a46f5666`：UI 重整 #191 合併，正式 CY Web Deploy run `39062483770` 成功；不覆寫新設計系統或重新合併 0.7 版舊程式。
- [x] CYWEB 新版復原整合 [PR #192](https://github.com/simonliu1118-byte/chihyuan-web/pull/192)（`cyweb/cyinvoice-recovery-v30-current-ui`）以 V0.9.0 主幹重新移植 [#151](https://github.com/simonliu1118-byte/chihyuan-web/pull/151) 的 SUPER_ADMIN 復原 Panel、checkpoint、同源／Origin／CYID 驗證、私有 named RPC `CyInvoiceRecoveryEntrypoint`、readback 和測試；exact head `ac9cfdeea65eac9b302ddc84240ac18cd8ffe34e` 的 Governance／Runtime Check／部署設定驗證 **全通過**。該 PR 後續已合併至 CYWEB `main`（`7ba87b8`），原本這個來源階段尚未開啟實際 Recovery Service Binding；CYWEB 0.9 的 ERP、備份及介面主幹保持原樣。
- [x] CYInvoice `cloud/src/app.ts` 已有 `CyInvoiceRecoveryEntrypoint` 的私有 RPC 三方法（issue/status/revoke）與 `RECOVERY_ENABLED`、schema >= 15 的 fail-closed gate；**不必重寫或另建 RPC**。#192 的 `CF_CYINVOICE_RECOVERY_SERVICE` binding **預設未啟用**，現行 CYWEB 正式服務沒有新 Recovery RPC。
- [x] CYInvoice V3.0 當前來源 `e4390290` 的 Governance、Development Cloud 預檢、Cloud Check（含 0014/0015 隔離 SQLite 演練）、Windows Build **四項全部通過**；發版與真實接線仍尚未發生。
- [ ] **唯一剩餘跨系統上線收尾**：先做好原 CYInvoice D1 的獨立私有備份與回復驗證，經核准逐項執行 0014／0015 Migration 及必要的 CYWEB_RECOVERY issuer profile；更新原 Worker 並驗證功能旗標、保留現有 ID/Token/history；再經獨立檢核部署新 CYWEB（只新增命名 RPC binding）及新 Windows，執行真 Email、兩台 Windows、DR-01～13、舊/新 Windows 與 V2 業務回歸。
- **安全界線**：不要因 CI 綠燈直接在原 D1 上批次遷移、合併／部署 PR 或撤銷現有裝置；正式資料操作需先有可核對的實際備份／回復證據及部署授權。3.1／3.2 的工作流程、折讓 API 不提前塞進 3.0。

> 新規劃：3.0 僅 Local／CYID 雙模式、公司卡、災難復原、必要 D1；3.1 非折讓跨機；3.2 完整折讓 API／折讓跨機；3.3 其餘。舊段落的「V3.0 折讓」是過時版本分類。**完整已做程式、延後問題與來源索引仍在 [#440 TODO](https://github.com/simonliu1118-byte/CYapps/blob/cyinvoice/v3-recovery-invitation-schema/apps/CYInvoice/docs/TODO.md)**，來源 SHA `fe0220c567a5a481daf44ee13c6898340b4d1bd1`；CYWEB Recovery 草稿 [#151](https://github.com/simonliu1118-byte/chihyuan-web/pull/151)。按版回接，勿重寫。

- [x] **3.0-A 第一小步（工程提交）**：沿用 #440 的 `CloudSetupForm.cs` CYID-only Cloud 連線設定、將 `SettingsForm.OpenCloudSettings` 未加入的 Device 導向既有 `CloudDirectJoinForm`；舊非 CYID Identity 明確拒絕而不刪既有資料。本步只動 Windows UI，不改 Worker／D1／Local Core／發票與折讓。
- [x] **3.0-A 第二小步（此分支）**：沿用 #440 `cloud/src/app.ts` 的 CYID-only fail-closed 檢查，但只提 3.0 所需的 runtime/device dispatch，**不搬** 3.1 Work Item／3.2 allowance routes；未設 `CYID_ENABLED`／為 false／設定無效時，health、Device、舊 Built-in mutation 均不會觸發退役 Worker 行為。`cloud/tests/cyid.test.mjs` 沿用 #440 既有測試，補未設定和無效設定回歸；`cloud/README.md` 同步標明 3.0 的 Worker 單模式。此項 **source／合成測試待新 exact-head CI**，沒有修改真 Worker／D1。
- [x] **3.0-B 公司卡 App Key 精確值防誤標（Source＋隔離測試）**：既有 Windows SettingsStore 儲存 App Key 時會 Trim；若雲端公司卡含前後空白，可能「套用」後值不同卻仍記錄雲端版次。本片在 Cloud 寫入時拒絕此類 App Key，Windows Provision 再檢查一致才允許 DPAPI 套用；MO店+ 密碼保留有意義的空白、不自動 Trim。隔離 D1 加上已撤銷 Device 在解密期間也不能取得憑證的競態回歸；正式 D1／Worker 不動。
- [x] **3.0-A Windows 主畫面退役入口收斂（Source＋WinForms smoke）**：Local 帳號管理保留，CYID Cloud 帳號由 CYID 管理；歷史 Built-in／CloudTransition 不再顯示／開啟舊 Cloud 帳號管理，舊雲端密碼復原不再呼叫已退役 API，主畫面提示舊模式不支援並保留原資料。舊 Windows／Worker 合成與真裝置相容驗收仍待完成。
- [ ] **3.0-A 待續**：其餘已退役 Built-in／Self-hosted 原始執行程式清理、舊 Windows／新 Worker 相容性、真 Device／CYID 帳號與歷史資料驗收；不可因 runtime gate 已提交就視為完整雙模式驗收。
- [x] **3.0-B 公司設定第 1 片（僅隔離 Source／合成測試）**：從 #440 精確來源 `fe0220c5` 接回 `cloud/src/company-settings.ts` 的 AES-GCM／原子 revision／append-only Audit、`company-settings-route.ts` 的 SUPER_ADMIN 修改／ADMIN 唯讀狀態／USER 禁止、`cloud/drafts/0017_company_settings.sql` 及原來的 `cloud/tests/company-settings.test.mjs`，並加入 npm test。**route 尚未在 `app.ts` 註冊、SQL 不在 `migrations/`、不會動真 D1**。此為驗證可沿用程式，非完整公司卡交付；CI 以新 HEAD 為準。
- [x] **3.0-B 公司設定第 2 片（Windows Client／設定畫面，待 CI）**：從 #440 接回 `src/CYInvoice.Core/CloudCompanySettingsClient.cs` 和 `SettingsForm.cs` 的 CYID 公司卡，沿用既有 Settings 介面，提供已設定狀態、超管送出更改／明確保留或清除秘密、revision 衝突／未知回覆唯讀查回；介面不顯示原密鑰、不自動重送 UPDATE、不影響原本 Local 欄位。保留 WinForms smoke 的布局與按鈕狀態驗證。**這是 Draft Client/UI，原 D1 尚未加表，Worker 尚未掛此 API，不能宣稱已可實際使用。**
- [x] **3.0-B 第 3 片（Worker 入口整合，待 CI）**：把 #440 既有 `handleCompanySettings` 掛回 3.0 `cloud/src/app.ts` 的原 CYID Device＋每次獨立 Employee Session；只新增公司設定 GET／UPDATE，不帶入 3.1 Work Item 或 3.2 折讓路由。加兩道保護：`COMPANY_SETTINGS_ENABLED=true` 且 `SCHEMA_VERSION>=15` 才接收；現行原 Worker Schema13、未設 feature flag 時一律 503 NOT_READY，沒有新表也不存取 D1 公司卡；`cyid.test.mjs` 從真 Worker dispatch 合成驗證預設關閉、超管修改、ADMIN 唯讀、USER 拒絕、revision 衝突、無秘密回覆。**僅 Draft source，既有 D1／Worker 未部署；測試須由新 SHA CI 判定。**
- [x] **3.0-B 第 4 片（公司卡／本機現有設定一致性揭露）**：`CloudCompanySettingsUsage` 只讀檢查雲端公司卡的統編與 Windows 現有開票統編是否一致，以及**本機** App Key／MO 密碼有無加密存檔；查閱公司卡及更新成功時顯示目前實際使用的是本機，遇到統編不同或雲端有密碼、本機沒有時提示。**不因雲端狀態已設定就冒充本機可用，更不解密／搬移／覆寫憑證**。新增 `CYInvoice.Cloud.Tests` 合成回歸。
- [x] **3.0-B 第 5 片（由超管確認後套用 Windows 本機憑證；source+CI）**：沿用原 CYID Device＋每次 CYID SUPER_ADMIN 身分重新驗證，新增明確 `POST /v1/company-settings/provision`；只有整張雲端卡已具備統編、App Key 與 MO店+ 密碼才在 HTTPS／no-store 回傳給發起授權的 Windows，原一般 `get/update` 仍只回狀態。Windows 現有設定頁經**第二次明確確認**後才取回，核對同版次、同業務統編、同 Device 身分，再用現有 SettingsStore／DPAPI 原子儲存；失敗不改原本本機憑證，不自動重試或記錄敏感值。SUPER_ADMIN 以外只可讀狀態，不能取回；其他電腦需各自經超管套用。現有 Worker Schema13 仍停用，正式 D1／Worker 尚未變更。**跨機已撤銷 Device 的快取清理、離線撤銷與實機安全驗收仍是上線阻塞，不可因此宣稱已可上線**。
- [x] **3.0-B 第 6 片（公司卡版本／裝置來源防誤判）**：公司卡經超管手動套用後只記非秘密的版次、Workspace／Device／統編；查看雲端卡時顯示這台電腦的套用來源是否匹配，改版、跨裝置時明確警告。手動修改 App Key／MO 密碼／統編即清除此標記，避免把本機新設定誤當雲端同步；舊 Settings 視窗不允許覆寫另一視窗已更新的公司憑證。撤銷 Device 的原有 CloudRejected／LocalReset 保護沿用、不重做；離線時**不能即時驗證遠端撤銷或公司卡是否改版**，此點仍需受控實機驗收。
- [ ] **3.0-B 待續**：完成公司表正式 migration 與原 D1 備份／實機升級驗收後再獲明確授權開啟路由；設計與驗收 App Key／MO 密碼在 Windows 原開票／匯入的**每 Device 授權安全取用、DPAPI 快取／更新失效／已撤銷裝置禁用／雲端中斷的原本降級能力**。公司卡 readback 僅提供狀態，不回傳秘密；此功能尚未自動派送，不能把雲端已設定當成本機可用。既有 CYID **不重新接／重寫身分與裝置**。
- [x] **3.0-C D1 新增結構候選（本輪工程）**：原業務 D1 storage13／CYID_ONLY 已完成、**不重建**。從 #440 原 recovery migration 精確接回 `cloud/drafts/0014_invitation_recovery_metadata.sql`，把公司卡 schema 改排 `cloud/drafts/0015_company_settings.sql`（原 Draft 0017，業務表內容沿用）；`cloud/tests/v3.0-migration-rehearsal.test.mjs` 在隔離 SQLite 重現既有 CYID_ONLY 退役後再加 0014→0015、歷史 Workspace／Device／邀請／Audit 保留、失敗交易 Rollback、可還原備份與 FK／integrity。此項當時兩份 SQL 仍僅在 drafts，後續已由本段最新記錄轉入 canonical migrations；**原 D1 尚未變更**，`SCHEMA_VERSION` 仍為 13。後續 3.1／3.2 的舊 Draft 0015～0019 要在接回時依實際已部署 schema **重新接續編號**，不直接套舊編號。
- [x] **3.0-C 復原 Worker 接回既有 #440**：保留原來 `recovery-authority/issuer/claim/routes`、CYWEB 專用 Private Service Binding、原五組安全測試及 `maintenance/enable-cyweb-recovery-issuer.sql`，沒有重寫。Worker 公開 Preview／Claim 與私有 issuer **預設關閉**；需要 `RECOVERY_ENABLED=true` 且原 D1 schema 已升到 15，才可連接。只有復原流程在 CYID SUPER_ADMIN 重新驗證後允許沒有舊 Device Token 的新裝置。正式 D1、Worker、CYWEB 均**未部署**；source / CI 通過才算這一項完成。
- [x] **3.0-C 復原的舊裝置競爭防護**：沿用 #440 的 `cloud/src/index.ts`，新增配對碼、邀請發送、普通 JOIN 的提交當下 Device 狀態保護；舊裝置一旦因災難復原撤銷，不得以稍早的請求再建立授權。原 `recovery-cutover.test.mjs` 為不可降低的安全門檻，未通過不得發行。只調整既有 Device 安全性，不新增資料庫或跨機／折讓功能。
- [x] **3.0-C 復原 Windows UI／Pending Token 從 #440 接回**：`SettingsStore`、`SettingsModels` 的 DPAPI Pending Recovery／同 scope 原 Token 保存，以及原 `CloudRecoveryClient`／`CloudRecoveryForm`、從直接加入雲端開啟復原、WinForms 預設禁用不可逆接管的 smoke；延用 #440 程式，不重寫 CYID 或 D1。尚未部署，不能宣稱真 Email／雙機驗收完成。
- [x] **3.0-C migration 升級為正式來源（尚未遠端執行）**：沿用隔離 SQLite 已驗證的復原 0014／公司設定 0015 原 SQL，納入 `cloud/migrations/` 單一版本化來源，移除 `drafts` 重複副本；更新退役 CYID_ONLY、復原邀請／公司卡與 3.0 可回復演練測試，確保先以舊 0013 建模，再追加 0014→0015。**仍未連線或修改遠端 D1**、Worker `SCHEMA_VERSION` 仍為13、兩項功能預設停用；部署稽核的現有 pending-migration gate 未開放，須有私有備份及獨立上線授權才可解開。此 PR 僅為可審查的發行程式來源。
- [ ] **3.0-C 待續（本機 MCP＋實機）**：Windows `CloudRecoveryClient`、`CloudRecoveryForm`、DPAPI Pending Token **均已接回**，CYWEB #192 **已合併 Source**；本行為 3.0 發布前歷史停點：當時剩下本機 Cloudflare D1 備份／0014+0015／issuer profile／Worker Binding／flag 與真 Email、雙 Windows、DR-01～13。使用者現已確認 3.0 Cloudflare 升級完成，勿據此重做遷移；未完成的真人驗收仍見 [RC_TEST.md](RC_TEST.md)。
- [ ] **3.0 驗收／發行**：新 HEAD CI、Local/CYID 原功能及 Windows 實機確認；僅在明確授權後合併／部署／正式 Release。
- **禁止提前帶入** #440 的 0015 Work Item、0016／0018／0019 折讓 D1、g0401／g0501、`0d651688` 舊人工折讓 UI 退役；3.0／3.1 保留 V2 人工折讓與作廢。
- [x] **3.0-A/B/C Windows 敏感 Cloud HTTP 防轉址（Source＋回歸）**：CYID 公司卡查詢／更新／手動套用、直接加入、災難復原 Preview／Claim 均不自動跟隨 HTTP 307／308 等重新導向；避免含 CYID 帳密、邀請碼、Device Token 或業務密鑰的 POST 被原 Cloud URL 的 Location 轉送其他網域。只在既有 Windows 呼叫端設置 `HttpClientHandler.AllowAutoRedirect=false`，不改 API、設備加入資料或正式雲端；在既有 Cloud client compatibility regression 加防回歸源碼檢查。仍需最新 head CI 與真實環境驗證。
- [x] **3.0 安全收尾：請求前置容量限制**：公司卡 4096 bytes、復原 Preview/Claim 2048 bytes；Worker 在登入 CYID 與查 Device 前已依實際串流位元組數拒絕超量請求（含無 Content-Length），並補合成測試，避免只依靠路由後段的 body cap。這僅是 Source／CI 變更。
- 目前已完成 3.0-A/B/C 的多個工程切片，但**不是整個 3.0，也不是正式部署**；Worker／原 D1／CYWEB／真光貿未變更。

## 0. 接續優先順序

| 順序 | 工作 | 範圍與依據 |
| --- | --- | --- |
| 已完成 | V2.6.19 Release、CYID 啟用、Cloud0.9.3 與原 D1 舊帳號退役 | 正式 CI／部署與本次私有維護 read-back；不再列為待切換 |
| V3 接續 1 | 多機 Device／邀請與 CYWEB 超管復原 | §7.4／§7.5；使用者決定災復併入 V3，由 CYWEB 簽短效一次性復原邀請，沿用原 Workspace；設計完成，功能尚未實作 |
| V3 接續 2 | Work Item／revision／跨機 OrderID 防撞 | §7.5；與同一 Device／actor 模型接續，跨機原子性與結果不明，不將 Cloud Lock 變成所有業務前置條件 |
| V3 接續 3 | V3 折讓 API／不可覆寫逐筆歷史 | §1.1／§6／§7.5；依使用者決定整體重做，不回頭修 V2.6.19 |
| V3 接續 4 | 業務 Audit／查看介面 | §7.4／§7.6；現有 Device audit 保留，新增業務事件與 actor provider |
| 待補證據 | 原裝置權限刷新、斷線／恢復、撤銷中斷與 UI／AMEGO 行為 | §1／§4／§7.2／§7.3；缺實測證據不等於未實作或已發現 defect |
| 後續／延後 | 酷澎特殊樣本、自架手冊、多公司、營運摘要、高 DPI／cache 簽章 | §5／§8～§11；維持原延後與非本次阻塞決定 |

- [x] V2.6.19 正式 tag／ZIP／SHA-256 與 Windows／Cloud Client／package／安全掃描已完成。
- [x] PR #216／#379 head 已由 #380 合併；裝置管理、邀請、分流工程不重列為未實作。
- [ ] **待補實機證據 RC Y：**96 DPI 介面；125／150 DPI 與 High Contrast 尚無證據。使用版本／最後使用為成功啟動回報，持續 heartbeat 不在本次範圍。
- [ ] **待補實機證據 RC W：**等待→完成／錯誤→恢復、數量／公司隔離、舊折讓 null 分類與管理員結案。
- [ ] **待補實機證據 RC Z：**active 清單、新增視窗、A/B 啟動回報、離線值、超管改名與 revoked 歷史。

未勾選實機項目表示尚無可引用的 live 證據；本批發行已經使用者接受本地／CI 驗收，不要求重新逐項實機操作。新增功能依實際風險驗證，不能把 CI、health 或部署成功勾成真人全矩陣通過。

## 1.1 折讓人工處理歷史缺欄位（V3.0 整體重做；本批不修）

2026-10-13 使用者決定停止 V2.6.x 的這項缺陷修正，併入 V3.0 折讓／Cloud Work Item／Audit 正式重做；**此處不是已修復或已驗收**。V2.6.19 已知缺陷保留，不作本批 Release 阻塞。不可自行補造原本機遺失的人員、日期、原因、金額，也不因延期清空既有資料。

- [x] 既有 screenshot／source 差異與不確定根因已記錄在 CLOUD_WORK_HANDOFF 歷史停點；原機 DB／備份未取得，不宣稱恢復成功。
- [x] 使用者明確將本項留待 V3.0 重做；不在 V2.6.19 變動 metadata／sync_issues／UI／schema／VERSION。
- [ ] V3.0 設計時重新確認多機 Work Item、正式折讓 API、不可覆寫逐筆歷史、申請／處理 actor／時間／原因／金額、結束結果、同步／中斷與 retention；落實詳情狀態區別與可靠的舊資料處理。
- [ ] 對 V3.0 新流程提供對應的本地／整合與必要實機驗收。原 RC AD 轉成 V3 需求參考，不能把 V2 既有缺陷當作通過測試。

本批 Release 判斷依本地／隔離驗證及既有精確 source GitHub CI；不得上傳任何 Cloudflare 測試 Worker／版本，正式／development 雲端資料與裝置不因驗收而變更。

## 1. V2.6.x 實機與光貿驗證

- [ ] V2.6.18：原訂單再次送出由光貿拒絕；第一筆已開立保持不變，第二筆 Failed 只在上傳問題，重新整理／重啟不變成成功；手動與匯入批次成功／失敗計數正確。自動化與精確 head Windows CI 以 #380 checks 為準。
- [x] V2.6.18 Build 1 source：使用者核准受限舊雙成功修復，原同步 owner 回查同公司／環境／官方號碼／OrderID／完整內容，原生成 ID 與 SentAt 一致且官方開立時間介於兩次送出才決定先後；完整 SQLite 備份、快照競態保護、原子寫入及安全重試。只將確認的多餘嘗試轉 Failed，不按排序刪資料。自動化／Windows 結果見精確 head #380 checks。
- [ ] V2.6.18 Build 1 原機驗收：保留原 Data／Cache／Logs 升級，重新整理後核對一筆原成功與一筆上傳問題失敗、Data/RepairBackups 可讀，重啟／再整理／清除失敗不影響原發票。未取得原 Data，不宣稱已修復使用者資料；證據不足的個案保留歧義再核對。

- [ ] **Local／Built-in 專用證據：**全新首次設定建立超管、密碼規則、復原碼與重啟；本公司 CYID 帳號由中央管理。
- [ ] 首次設定尚未完成、尚未建立超級管理員時，主畫面右上角光貿連線 Tag 先顯示灰色「連線中」；完成首次設定後才執行既有光貿 API 連線檢查，再切換為綠色「光貿連線正常」或紅色「光貿連線異常」。
- [ ] 帳號管理／權限驗證：一般使用者、管理員、超級管理員的建立、修改、停用、重設與禁止操作邊界。
- [ ] MO店+ 未設定 Excel 密碼時，按 MO店+ 必須先提示設定且不得先開選檔視窗。
- [ ] 設定選單持續開啟至少 30 秒，確認不再連續閃爍。
- [ ] `設定 → 系統診斷`：重新檢查、API／SQLite／WebView2／Excel／印表機／同步狀態與複製摘要均符合實機狀態，且不顯示 App Key、密碼、發票內容或完整本機路徑。
- [ ] 測試環境實際送出直接作廢 `3015-消退` 與管理員覆核作廢 `3001-3015-消退`。
- [ ] 保留完整 `invoice_query` JSON，確認 AMEGO 是否回傳可解析的 `CancelReason`／作廢原因；目前不得假設未文件化欄位存在。
- [ ] 實測紙本未收回人工確認：一般使用者不能批准，管理員可批准／取消，批准後才送作廢。
- [ ] 實測作廢確認開啟期間，背景已開立清單所有可見發票號碼持續隱藏，即使背景重新整理也不會重新顯示。
- [ ] 實際建立一筆人工折讓，以 `invoice_query.allowance[]` 驗證同日舊／新折讓辨識、含稅金額比對與多候選保守停止。
- [ ] `/json/allowance_file` 三種版型 A4、A4 (地址+A5)、A5 均能取得有效 PDF 並由既有 WebViewer 正常開啟／列印。
- [ ] 折讓官方資料發生狀態／日期／類型／稅額／金額變動後，再次開啟 PDF 必須使用新官方資料指紋，不得命中舊 Cache。
- [ ] 「折讓作廢」暫行人工流程：一般使用者可提出，管理員可於「上傳問題」完成或取消退回，現階段不得直接呼叫 `/json/g0501`。
- [ ] 「上傳問題／處理中」兩清單正確分流、數量與明細一致；只有上傳問題內 Failed 可勾選清除，正常等待不能刪除／手動標成功。
- [ ] 超過兩期的發票作廢、折讓、折讓作廢待辦均顯示「可結案」，只有 ADMIN／SUPER_ADMIN 可結案；結案後只停止本機追蹤並恢復 retention 清理資格，不代表光貿已完成。

完整操作步驟見 `RC_TEST.md`。

## 2. 待取得實機資料後再決定

- [ ] **折讓作廢人工完成後的官方確認策略。** 目前由管理員確認已完成光貿網站操作後按「已人工處理」結束本機待辦；取得真實 `invoice_query.allowance[]` 折讓作廢樣本後，再判斷能否可靠地先回查官方狀態，無法確認時維持 pending。
- [ ] **CancelReason 回查。** 取得真實 invoice_query 樣本後，再決定完成作廢歷史能否跨重新啟動解析使用者／覆核管理員／原因；沒有官方欄位就不自行保存一套永久作廢歷史。

## 3. 已完成工程證據（不重排為 TODO）

V2.6.14 功能 source db1a3b95：Governance #1171、Cloud #377、Windows #266 全部通過，含 client contracts、WinForms label 測量、完整回歸、packaged smoke／safety scan。Development staged Run #8 attempt 2 已驗證 Cloud 0.8.9／storage 12／usage／rename；0012 一次套用，撤銷歷史保留。後續僅文件更新不推進 VERSION／BUILD，精確 head 以 PR checks 為準。


功能 commit 66ec3671 已通過 Governance #1137、Cloud #369、Windows #258。Core／Void／Employee void workflow（29/29）／Allowance／SQLite／Sync／SyncCoordinator 與 Windows smoke／package 全綠；歷史行政結案契約已修正，source TODO／FIXME／NotImplementedException 盤點沒有另找到明確未實作 placeholder。

人工折讓／折讓 PDF／人工折讓作廢／系統診斷已實作；Identity Foundation、Online Authority Freshness、Device inventory／revoke、crash-safe Cloud → Local reset 已合併。Built-in Cloud Email 忘記密碼已有 API／Windows UI／client，不再列為未實作，仍需 live 驗收。版本／CI／deployment 的精確證據與功能邊界見 V3_1_HANDOFF.md。

## 4. V2.4.x／既有同步與 Cache 實機回歸

- [ ] 以 V2.3.0 真實 `Data` 備份升級到現行 SQLite，確認 DB 建立、舊 JSON 保留、資料筆數與內容一致。
- [ ] 正式環境 recent 3-day sync，確認可抓到另一台電腦／光貿端合法更新。
- [ ] 程式持續開啟超過 5 分鐘，確認啟動同步／每 5 分鐘同步不重疊，手動重新整理維持 30 秒冷卻。
- [ ] 每日第一次同步執行目前＋上一期完整校對，不影響 recent 3-day 核心。
- [ ] 雙擊不同日期發票都先 `invoice_query`；query 無法確認時不得把舊 Cache 冒充最新資料。
- [ ] 測試環境 namespaced OrderID 不與共享測試池其他資料撞號，UI 仍顯示原始可讀 OrderID。
- [ ] 發票 PDF／Preview Cache 不保存短效 `file_url`，retention 清理時同步移除對應 Cache。

## 5. 酷澎樣本補齊

- [ ] 未出貨／不同狀態樣本。
- [ ] 公司統編訂單樣本。
- [ ] 多商品訂單樣本。
- [ ] 數量大於 1 樣本。
- [ ] 折扣／負數調整樣本。
- [ ] 取得樣本後補 parser、金額、防重與測試案例。

## 6. 正式折讓 API（V3.0 雲端化階段）

目前仍以人工折讓／人工折讓作廢待辦為主；完成紀錄以 `invoice_query.allowance[]` 為官方依據。2026-10-13 使用者決定目前人工折讓歷史缺欄位不單獨修補，V3.0 隨正式折讓及 Cloud Work Item／Audit 重做；既有 V2 缺陷不因此視為修復。

- [ ] `/json/g0401` 折讓開立：CYInvoice 直接送出，不再要求管理員至光貿網站人工建立。
- [ ] `/json/g0501` 折讓作廢：正式自動化後只允許 ADMIN／SUPER_ADMIN 直接執行；送出前重新確認最新折讓狀態，結果不明不得自行標成成功。
- [ ] `AllowanceNumber` 由 CYInvoice 自動產生，建立全域唯一編號與跨裝置防重機制。
- [ ] `allowance_query`／`allowance_status` 正式查詢與同步模型；目前刻意共用 `invoice_query`。
- [ ] 部分折讓、多次折讓、品項／數量／金額規則。
- [ ] 發票與折讓之間的 idempotency、timeout、結果不明與同步安全規則。
- [ ] 自動折讓／折讓作廢時，確認 AMEGO 正式可保存且可依折讓單號查回的欄位，再決定如何保存 `管理員編號-使用者編號-原因`；若官方沒有合適欄位，不濫用未文件化欄位。
- [ ] 雲端化完成後移除暫行人工折讓與人工折讓作廢路徑，收斂為正式 API 流程。

`/json/allowance_file` 折讓 PDF 已完成工程實作，後續只需實機驗證與雲端化時確認多裝置 Cache 行為。

## 7. CYInvoice V3.0 雲端協同

完整長期定位與分期見 `CLOUD_ROADMAP.md`；Workspace／Device／Token／Employee authority 定案見 `CLOUD_IDENTITY_LIFECYCLE.md`；CY ID consumer 邊界見 `CY_ID_INTEGRATION.md`。

### 7.1 已完成 foundation

Workspace／protected Device identity、pairing／invitation、whole-device transition、中央 Employee CRUD／OTP／transfer／password recovery、USER role migration、provider abstraction、freshness、revoke 與 reset 均已實作。9/29 development staged deploy Run #7 與 migration／aggregate／FK audit 有既有證據；A 機連線與 B 機 Run255 pairing 曾有人工作業證據。後續驗收必須用最新包，不沿用歷史版本成功推定新版本全通過。

### 7.2 目前剩餘驗證／修正

- [ ] **待補 A/B baseline 證據：**兩台既有 Device 保留原安裝升級現行 V2.6.19 後仍可連線，Device Management 清單、一般同步／開票既有路徑無回歸。
- [ ] V2.6.12 邀請第一次加入與首次說明實機驗收（RC X）。10/09 舊包已取得「第一次錯誤、重開成功」證據，已定位 password.Clear → TextChanged → preview=null；修正版單次完成尚待使用者驗證。邀請撤銷／重寄與其他不明結果仍待實測。
- [ ] **Built-in／Self-hosted 專用證據（其他公司獨立 D1）：**精確命中、全新 Employee、Employee No only、Email only、兩欄各撞不同人的實機／integration 測試。
- [ ] **Authority freshness A/B 實機驗收：**A 修改中央 Employee 密碼／role／enabled 後，B 不等待背景同步或重開即可在下一次 protected operation 套用最新 authority；雲端暫時不可用且光貿正常時沿用原降級可信 cache，恢復連線後 Online authority 重新覆蓋。
- [ ] **Built-in／Self-hosted 專用證據（其他公司獨立 D1）：**Built-in Employee CRUD、Email OTP、password、enabled、role 在 A/B 間即時與背景 snapshot 同步實機測試。
- [ ] **Built-in／Self-hosted 專用證據（其他公司獨立 D1）：**Built-in Cloud「忘記密碼」員編／已驗證 Email challenge、重寄倒數、OTP／新密碼 confirm 及 A/B 舊密碼失效實機驗收；工程已實作。
- [ ] **Built-in／Self-hosted 專用證據（其他公司獨立 D1）：**SUPER_ADMIN transfer 的 execution-time re-auth／OTP／原子 X→ADMIN、Y→SUPER_ADMIN／Recovery Email 與 A/B authority 驗收。
- [ ] **Cloud → Local 實機驗收：**優先用可拋棄的 Device C，在 A/B 仍保留 Workspace recovery path 的前提下測 current-device reset；確認 A/B、Workspace、中央 Employees 完整保留。
- [ ] **Device revoke A/B/C 實機驗收：**新增可拋棄 Device C，從 A/B 遠端 revoke C，確認 C 舊 Token 立即失效、history 保留 revoked、重新加入產生新 Device identity。
- [ ] **LAST_ACTIVE_DEVICE 實機驗收：**只在專用可拋棄 Workspace／環境驗證，不為測試而破壞目前 A/B 唯一 recovery topology。
- [ ] **Ambiguous revoke fault-injection：**模擬 Cloud 已執行但 response 遺失／狀態暫時無法確認，確認不 wipe、本機資料／Token 保留，下一次啟動由 self-status 收斂。
- Recovery Device flow 與 Token／recovery path 同時失效的人工維運程序，統一由 §7.4 追蹤。
- [ ] 驗收 fresh-install 首次分流。
- [ ] **延後／非目前阻塞：Cloud Employee offline cache 完整性簽章。** 未來可評估 server-signed snapshot／等效完整性保護；沒有實際竄改事件、威脅模型提高或稽核需求時長期擱置。

### 7.3 CYID Consumer（本公司已啟用；未取得的 live 證據保留）

- [x] activate-cyid #42 已啟用本公司 development CYID；Application／Workspace／private bindings 與原 business Workspace 已配置，原 Windows 帳密進設定有使用者回報。
- [x] 2026-10-13 原 Worker 升級0.9.3，保持 CYID 與全部 bindings；原 D1 四張舊表退役及私有備份／保留資料／FK 核對完成，見 §7.7。
- [ ] **待補證據 RC AA：**Account Management／settings／高權限操作與 App Access、Role、enabled、密碼變更後的原裝置更新；單台成功不代表全角色矩陣。
- [ ] **待補證據 RC AB：**四種服務狀態、原離線行為、恢復與輸入／Workspace 保留；帳密／權限／Device 拒絕不降級，不自動重送。
- CYID 0-active-Device recovery 與 LAST_ACTIVE_DEVICE 後續設計統一在 §7.4，不重開已完成的本公司 activation。

### 7.3.1 V2.6.16 服務連線收斂

- [x] 依使用者四點定案保留原有降級，加入 transient 503／HTML 503；共享光貿探測與核心入口防護，四狀態及純 Local 回歸。
- [x] MainForm 一個 15 秒檢查／modal blocker，重新檢查與恢復沿用原設定／輸入； known deny 後 503 不復活。Windows 原生視窗 smoke source 已加入，CI 結果以 #380 精確 head 為準。
- [x] VERSION 2.6.16／BUILD 0、Cloud source 0.9.1；無新 schema 或遠端部署，文件／CHANGELOG 同步。
- [ ] 待補 RC AB 原安裝四種狀態／純 Local／雙斷線提示／恢復證據；RC AA 角色矩陣仍未全驗，本公司 activation 已完成。

### 7.3.2 裝置／權限同步與撤銷自動重設（V2.6.17 source）

設計 CY_ID_INTEGRATION §14.6；停點 CLOUD_WORK_HANDOFF；人工 RC_TEST AC。不得拿 V2.6.16 CI 當成本批完成。

- [x] 盤點 portable package／全部 runtime 路徑及 canonical owner，確認 final-operation 入口與 in-flight AMEGO 結果查核；原 Workspace、降級及光貿同步週期保留。
- [x] CYID canonical private service-level change contract／實作／回歸，涵蓋 enabled、刪除、Role、App Access、credentialVersion／漏同步；同步版本、changelog、Consumer Impact／支援範圍，不直接讀 CYID D1 或以 login 輪詢替代。
- [x] 一次 aggregate Device self-status／binding／Provider change 請求，分項故障；revoked Token 只回自身清除證據，CYID outage 不掩蓋 revoke。
- [x] MainForm 啟動／60 秒／重連單一同步 lifecycle，Built-in 收斂；去重、stale response／known denial 保護，15 秒連線探測不重跑完整 feed，光貿 sync 不加頻。
- [x] 原 cache owner invalidation 涵蓋角色／停用／刪除／App Access／密碼及漏同步；不把新 principal 接到舊 password proof，新成功驗證才建立新可信 entry。
- [x] 收斂 dialog／core，同一最終操作 Device＋Employee＋最新 App Access／Role；不跳過執行前檢查，不保留跨操作 Session。
- [x] explicit remote revoke 接既有 LocalResetCoordinator／Program recovery；先 durable marker 再 shutdown／wipe／restart，補 Schedule memory-only kill 缺口，啟動不因空員工 cache 卡住。
- [x] Logs 及 root／subdirectory 所有程式管理 runtime 檔案完整清除，portable 回發行初始狀態；marker 最後刪，kill／partial wipe／檔案占用續清，停止背景寫回。Unknown Token／401／Workspace disabled／scope／503 不誤 wipe。
- [x] 本地回歸覆蓋 single request、真實 Provider／Built-in／Local、startup、cache／credential／permission／deleted employee、stale response、CYID outage＋revoke、重連、Logs／模擬程序中斷後 partial wipe；真實 kill／檔案占用另由 RC AC 驗收。
- [x] V2.6.17 Build 2 source 64f23381 的 Windows #275／Cloud #386／Governance #1184／CYID #105 與 engineering Artifact 通過；最新 head 結果仍以 PR #380 checks 為準。
- [ ] 待補 RC AC 實機撤銷／關閉／kill／下次啟動／Logs／初始設定／光貿結果不明，以及 RC AA／AB 全矩陣；不重開本公司已完成的切換。
- [x] 開始 runtime 新工作時按共通規則推進版本、同步 CHANGELOG／狀態／設計／驗收；版本為 2.6.17／BUILD 2，人工／staging 不冒充完成。

### 7.3.3 已完成部署與後續證據邊界（2026-10-13）

- [x] private IDENTITY Fetcher／IDENTITY_AUTHORITY RPC 配置、CYID activation 與0.9.3 原 Worker upgrade 有實際 read-back；配置／health 不代表完整正向權限實測。
- [ ] **待補證據：**真實 consumer named RPC、Employee／App Access／Role／verified Email、Free 用量與 Windows 多裝置 acceptance；合併追蹤 §7.3／RC AA～AC，不新增遠端測試 handler。
- [ ] **維運權限核對：**既有 temporary Workers Admin 是否仍需保留；不能由本次 MCP read/write 成功或拒絕推斷全部管理權限已回收。
- 初次切換前 Built-in continuity／staging／activation 條件留在交接歷史；本公司現為 CYID_ONLY，不能以關閉 flag 當回復指令。另一個 production promotion／Windows Release 仍屬獨立範圍。

### 7.4 V3 多機裝置復原、備份與安全紀錄

2026-10-13 使用者決定：**災難復原與 V3 多機協同一起做，從 CYWEB 由 CYID 超管簽發復原邀請碼**。裝置／邀請／協同資料上 CYInvoice 業務 D1，CYID 身分權威與其 D1 分離。詳細見 [V3 多機與復原設計](CLOUD_DISASTER_RECOVERY.md)；不再要求獨立災復功能先於 V3 完成。

- [x] **設計與 source 核對：**區分可用舊裝置、有效加入碼、所有 Token 遺失及服務／資料毀損；確認 active 筆數不是可用 Token 數、現有 issuer Device FK 與 app-scoped Session 邊界。
- [ ] **V3 CYWEB 管理接線：**核對實際 CYWEB source／core Session／CSRF owner；超管簽發／撤銷／狀態查核經專用 private capability 與當次 CYInvoice App authority，不轉用 core token、不要求舊 Device。
- [ ] **V3 邀請與 Audit schema：**forward migration 擴充 issuer channel／RECOVERY purpose／nullable CYWEB Device issuer 與 constraint，保留歷史 actor；原子單次 claim／Audit、失敗及 delivery 不明安全收斂。
- [ ] **V3 新裝置復原加入：**沿用 protected Pending Token owner，固定兩 Workspace／Application scope；同 Token retry／重啟／scope mismatch／revoked 不復活，成功加入原 Workspace 後才明確撤銷選定失聯舊 Device。LAST_ACTIVE_DEVICE 維持。
- [ ] **私有備份異機回復工具與保管：**設計建議 age recipient encryption；仍待核准 recipient／獨立金鑰及備份位置、工具、RPO／RTO／retention 與真正異機 drill。現有 DPAPI 只證明原帳戶／機器本地回復。
- [ ] **人工維運回復：**CYWEB／CYID／D1 故障先由各 owner 恢復服務；先保留目前 snapshot／新資料，核對 storage profile 與相容 Worker，遠端 restore 另以具體範圍批准。回復後舊 revoked Token／已用邀請不得倒退復活。
- [ ] **RC AE／DR-01～DR-13：**本地／CI 與必要實機驗證；目前均未執行。業務 Audit／viewer 由 §7.6 共用追蹤。


### 7.5 跨機 Work Item / Sync

災難復原不獨立先行，與 §7.4 的 CYWEB 邀請／Device registry／actor 共用 V3 設計。新機加入不代表未上雲的舊機 pending 已找回，也不觸發 AMEGO 自動重送。

- [ ] V3.0 重做折讓歷史與狀態：逐筆申請／處理／官方結果／取消／行政結案可追溯；不能僅因 issue resolved 即顯示「已人工處理」，也不能以雲端同步推斷官方成功。
- [ ] 作廢／折讓／折讓作廢／管理員結案 Work Item 上 Cloud。
- [ ] Work Item 原子 state transition／optimistic revision，避免多機同時結案。
- [ ] Cloud coordination 只用於真正需要跨機原子性的點，不把 Cloud Lock 變成所有業務前置條件。
- [ ] 離線期間 AMEGO 結果不明操作不得因恢復連線而自動重送。
- [ ] 解決多機離線自動 OrderID 撞號；正式多機上線前需 Device namespace／短碼或等效方案。

### 7.6 Cloud Audit

- [ ] 補齊 Employee、Work Item、SUPER_ADMIN transfer 與維運事件的必要摘要；現有 Device pairing／invitation／join／revoke audit 不重做。

Audit 沿用既有公開／敏感資料邊界，不保存密碼、Recovery Code、OTP、Device Token、App Key 或不必要的完整發票內容；這是持續約束，不另列一個可勾選功能。

### 7.7 CYID 上線後本公司 D1 舊帳號退役（2026-10-13 已完成）

本公司原 D1 經獨立授權完成一次性 CYID_ONLY 維護；其他公司 Built-in／Self-hosted 保留自己的新 D1／原 migrations／原程式。§19 為 credential-only 歷史提案，現行實作與完成證據見 CLOUD_IDENTITY_LIFECYCLE §20。

- [x] **原 D1 唯讀盤點：**活躍 Worker／D1／business Workspace、CYID=true、實際筆數／verifier、transition、OTP、invitation actor 與 FK 已核對，未由 source 猜測即時資料。
- [x] **雙 storage-profile 回歸：**PR #432／Cloud Check #38340836036 驗證 CYID-only 刪表／資料保留、Built-in schema 保留及錯 scope／未完成 transition 拒絕；本次真實私有備份另外還原 SQLite、逐筆比對及模擬同一 guarded SQL。
- [x] **相容 source 已合併：**維護 SQL 不進一般 migrations；CYID rename 不再解析已退役表，history audit 依 storage profile 保持保護。
- [x] **原 Worker 0.9.3 已部署：**[Development Deploy #38396129643](https://github.com/simonliu1118-byte/CYapps/actions/runs/38396129643) 成功，保持 CYID=true／全部原 bindings／API1／marker8／storage13，storage health 正常。
- [x] **備份／授權／一次性原子維護：**私有 DPAPI 備份及回復 checkpoint 保存，解密／本地還原／完整資料核對成功後才清理原 D1；私人資料／回復資訊不進 Public repo。
- [x] **真實 D1 與後驗證：**四張員工／轉換表物理移除，CYID_ONLY marker 對應原 Workspace；invitation 舊 actor 保留、舊員工 FK 移除；Device／Token／pairing／invitation／audit／migration history 逐筆符合預期；pairing OTP 保留、舊帳號 OTP／recovery fields 清理；foreign_key_check 無違規、quick_check ok。Provider 未變、未操作 CYID D1。
- **驗收限制：**identity ready、舊 Built-in route 拒絕與缺 Device 授權拒絕已檢查；真人帳密／多裝置矩陣仍依 §7.3／RC_TEST 保留未驗，不冒稱全矩陣通過。
- [ ] **V3 actor ownership：**Work Item／Audit 區分 CYID 外部 actor 與 Built-in 歷史，不靠員編猜同人；合併追蹤 §7.5／§7.6。

本次不取消其他公司的 Built-in handler／migrations，也不代表新 Windows Release 或另一環境的部署授權。

## 8. 後續大版本：多公司 Workspace

不列入 V3.0 阻塞項目。

- [ ] 真正有不同統編需求時再新增 Company entity／`company_id`。
- [ ] V3 單公司 Workspace 升級時自動建立第一個 Company。
- [ ] 視實際需求再做 Employee ↔ Company scope、Device 預設 Company、公司切換 UI、跨公司待辦／查詢／報表。

## 9. 對外相容／自架準備

- [ ] Cloud API 穩定後撰寫 CYInvoice Self-hosted / Cloud Integration User Manual。
- [ ] Guide 定義 Built-in Cloud 部署需求、endpoint、schema、Device auth、Employee authorization、錯誤碼、版本、idempotency／reconciliation 語意；不得要求 CY ID 才能完成基本部署。

## 10. 後續增強

Built-in Cloud Email password recovery 工程已完成，實機驗收見 7.2。

- [ ] MO 密碼安全 Cloud sync。
- [ ] 小型營運摘要：今日／本月開票張數與金額、待處理工作數、同步異常數。

AMEGO App Key 不列入 Cloud sync，仍由各電腦自行設定並以 Windows secure storage 保護。

## 11. 延後／非本次阻塞

- [ ] 125%／150% DPI 人工驗證：依 repository visual 決策維持 Deferred；本次 96 DPI 分流驗收不能冒充其他縮放通過。

Offline cache 整體簽章（7.2）、多公司（8）與營運摘要（10）保持既定延後範圍，沒有本次完成承諾。

## 12. 單機版明確不排入

- 本機操作／稽核紀錄：不做；等 Cloud backend 後做跨機 Audit。
- 本機備份／還原：不做；CYInvoice 是中介層，官方資料以光貿為準。
- 發票 Excel／CSV 匯出：不做；需要時使用光貿網站。
- 管理員開機待辦提醒：不做；沒有持續登入，無法可靠知道開程式的人是管理員。

工程 merge 與 development Cloud deployment 在達到對應 CI／Governance／安全驗證 gate 後可依技術需要進行，不應讓已完成的 stacked work 長期停放；**正式 tag / Release 仍視為獨立 promotion 行為，需另行明確決定。**
