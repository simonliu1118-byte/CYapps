# CYAccountingWeb TODO

## V0.21.6 CYID integration

- [x] 採用 `CYID_CONSUMER_VERSION=1.0.1` 並加入 provider support-window validator。
- [x] Login / Session Resolve / Logout / Password Recovery 改走 CYCloudIdentity private Service Binding。
- [x] 移除 active Worker 對 local `web_sessions` 的 Identity authority 依賴；V0.21.6 cutover 暫不 DROP 舊表，以保留 deploy failure rollback safety。
- [x] 建立獨立 `/login`；未登入不載入完整帳務 App，`/` / `/index.html` 由 Worker 先驗證 Session。
- [x] CYID App Access 可讓 `USER` 進入 CYACC；CYACC server-side business gate 固定 USER 只能讀取與匯出 Excel。
- [x] PC / Tablet / Mobile 共用同一登入與 Session authority；RWD 只負責 presentation。
- [x] Deployment config 加入 Application ID / Workspace ID runtime-only gate，不把實際值寫入 Public Git。
- [x] 建立隔離 CYID development deploy path：dedicated Worker / D1 / CYID development Service Binding，無 production route、Backup binding 或 Cron，並接 live smoke。
- [x] Development CYID registry 已建立 CYACC Application / Workspace enablement，isolated preview + runtime Service Binding 已部署並驗證。
- [x] Development environment live acceptance：登入／Session resolve／logout、USER 唯讀、Excel、USER→ADMIN Role change、App Access revoke/restore 與 Session invalidation 已於 CYID development run #96 通過。
- [ ] Password Recovery real Email/browser delivery acceptance；不因上述 Session/App Access acceptance 自動視為完成。
- [ ] iPad / Android Tablet 真機登入與操作驗收。
- [x] Production CYID runtime cutover 已隨 V0.21.6 Build 8 部署；正式 Worker 主路徑使用 CYID provider Session / Service Binding，不再以 local `web_sessions` 作 Identity authority。這只代表 runtime cutover 已發生，不等於所有 production acceptance gate 已完成。
- [ ] Production post-cutover acceptance：Password Recovery real Email/browser 與 Mobile/Tablet 真機仍需完成；歷史帳務 bootstrap 卡住已由後續修正版處理，不再視為目前阻擋。不得因 CI 綠燈直接標為 fully accepted。
- [ ] CYID production cutover 穩定並完成 post-cutover acceptance 後，以獨立 forward migration 退休 `web_sessions` 實體 table；不得和 authority cutover 綁成同一步驟。

本文件只記錄待辦、後續方向與未來評估項目，不作為永久規則來源。

> 目前工作交接：[`HANDOFF_2026-10-02.md`](./HANDOFF_2026-10-02.md)

## Current checkpoint — 2026-10-02

- 正式前版 V0.21.10 Build 0：`f701d54`，production deploy #346 成功；帳戶封存／解封、SUPER_ADMIN 硬刪除架構、歷史帳戶回填、分類－科目單一階層 renderer、收入優先排序已完成。
- V0.21.11 Build 0 完成自動期初服務、理由與 append-only audit、CYID actor、恢復自動值、Excel 共用計算、備份 inner v2、桌面移轉 override＋audit、最新零餘額刪除與歷史資料保留。
- Migration 0006、實際 SQL/API 權限與鎖帳、Excel parity、帳戶刪除、舊備份相容、雙 provider export-once、最大 D1 移轉批次回歸已通過本機驗證；PR／production 結果以對應合併提交的 Actions 為準。
- 共通規則與 AITeam 同步於 2.7.0；期初／人工基準政策經 governance PR #280 更新，GOVERNANCE_VERSION=2.3.27。
- CYID current contract 1.0.2／minimum 1.0.0；CYACC 1.0.1 使用 direct principal/session，仍在相容範圍。
- 舊 #252 架構清理與啟動卡住問題已由後續版本處理；不得把歷史交接中的分支或停點當作現況。
- 真機 Mobile／Tablet／Desktop、Password Recovery Email、Backup Phase C production x/14 證據仍是獨立驗收，不由自動測試代替。

## 電腦版核心功能移植

- [x] 基本記帳新增、編輯、刪除。
- [x] 帳戶、收入／支出科目、常用科目管理。
- [x] 期初餘額與月份鎖帳。
- [x] 常用摘要基本版（帳戶＋收支＋科目）。
- [x] CYCloud Identity 共用員工登入、Session 與 Email 忘記密碼；CYACC 只保留 accounting/business authorization。
- [x] 記帳資料表月份前後切換、摘要搜尋、完整月統計、逐筆餘額與帳戶分組檢視（V0.6.0）。
- [x] 記帳資料表直接欄位編輯：按編輯後原列直接切換為輸入控制，Enter 儲存、Esc 取消；鎖帳列不可編輯（V0.14.0）。
- [x] 輸入確認區：最近 10 筆存檔結果、成功／失敗狀態與千分位顯示（V0.7.0；後續 Desktop presentation 已再重整）。
- [x] 常用摘要設定：統計依據（帳務日期／近期輸入）、最近 N 筆、最低出現次數（V0.11.0）。
- [x] 帳戶、大分類、科目排序與科目跨大分類移動：早期以 ↑／↓ / move controls 實作；V0.21.x Desktop manager 已加入 drag reorder / cross-group move 與 optimistic UI，後端保留 reorder contract。
- [x] 日期輸入桌面快速操作：YYYYMMDD、MMDD 四碼重填、Ctrl+↑／↓ 日期步進（V0.11.0）；V0.21.1 起 Desktop 日期／年月 presentation 已改用自製 picker，若原生 browser picker 再出現視為 regression。
- [x] 逐月鎖帳操作流程（V0.12.0）；設定頁管理介面沿用相同月份語意並使用自製年月選擇器。
- [x] 單月 Excel 匯出：直接產生標準 `.xlsx`，包含月統計、逐筆餘額與期初餘額工作表（V0.13.0）。
- [x] Excel 匯入：`.xlsx` 工作表選擇、標題列／欄位對應、預覽、7 位數驗證、鎖帳檢查、重複略過與確認後寫入（V0.15.0）。
- [x] **CYAccounting SQLite 帳本匯入／遷移工具（V0.19.0）**：實作已完成瀏覽器本機 SQLite 解析、schema/integrity 驗證、SUPER_ADMIN server-side 權限、保守合併預覽、transaction occurrence dedupe、期初餘額／科目結構衝突阻擋、鎖帳只取較嚴格月份與 D1 atomic commit；原始 `.db` 不上傳。V0.19.0 Build 1 已修正 D1 bound parameter／query／payload limit並正式部署；**本 workstream 已成功執行真實 production migration，使用者於 2026/10/03（日本時間）明確確認移轉帳本內容 OK，最終內容驗收通過。**Public Git 不記錄 production accounting counts/values/evidence。

## Production / Domain

- [x] 正式使用者 hostname：`https://acc.chihyuancm.com`（2026-09-29 Custom Domain rollout 完成，使用者 smoke test 通過）。
- [x] GitHub Deployment Environment 使用 App-scoped variable `CF_CYACCOUNTINGWEB_CUSTOM_DOMAIN`，避免與其他 CY App 的 hostname 變數衝突。
- [x] `workers.dev` 技術備援網址在 Custom Domain 上線後仍明確保留（PR #207 / `workers_dev: true`）。
- [ ] 未來若要停用 workers.dev fallback，必須另做明確 production acceptance / cutover 決策；目前不得因 Custom Domain 已可用就自動關閉。

## 備份／復原與高風險操作

- [x] 備份架構：Cloudflare D1 為唯一正式帳務資料來源；異地備份不作 live database 或雙向同步資料庫（V0.16.0 起）。
- [x] V0.16.0 曾完成 Google Drive OAuth／自動備份技術基礎；正式異地備份已改採 Google Cloud Storage。PR #252 已把 V16 Drive active runtime route 移出 current baseline，只保留 provider-independent D1 backup package 建構邏輯。
- [x] V0.17.0 provider-neutral boundary：`BackupService` 與 `BackupStorageProvider` 分離；GCS adapter 實作 `putObject`、`getObject`、`listObjects`、`deleteObject`。
- [x] V0.17.0 portable backup set 改為 `manifest.json` + `data.json`；包含 App/schema version、資料筆數、SHA-256 與 byte size，上傳後兩檔均需 read-back 驗證成功才記為有效備份。
- [x] 備份排程維持每日 03:30（台灣時間）。
- [x] GCS 基礎設施準備：CYAccountingWeb 使用獨立 backup dataset 與獨立 least-privilege Service Account；bucket-scoped IAM 已驗證。Public Git 不保存正式 resource identifiers 或 credential。
- [x] **自動備份上線驗收（2026-09-26）**：V0.17.0 正式 Worker 首次實機備份成功；GCS 實際建立 `data.json` + `manifest.json`，Web UI 成功紀錄與 GCS 物件均確認存在，read-back SHA-256、byte size、row count、App/schema version 與 manifest 驗證均通過。
- [x] **Tiered Backup Phase A**：凍結已驗收 V0.17 GCS production path；GCS provider、Secrets、每日 03:30 與 14 天 retention 在 Phase C acceptance gate 前均保留，作為 rollback / safety path。
- [x] **Tiered Backup Phase B**：package creation 與 provider storage execution 分離；單一 BackupSet 可重複交給 provider 寫入而不重新 export D1；GCS generation 已封裝為 opaque `versionToken`；新增 V0.17 format compatibility 與 export-once tests。Production output 仍維持 `CYAccountingWebBackupSet / formatVersion 2`。
- [ ] **Tiered Backup Phase C**：R2 provider、`parallel_dual_provider`、logical backup / provider-copy catalog 與 topology-aware UI 已上線；2026-09-27 manual paired production acceptance 已通過，同一 logical backup 的 R2/GCS copies 均 read-back 驗證成功且共用同一 package digest。Scheduled acceptance gate 由 D1 catalog 自動推導並在設定頁顯示 `x/14`：只有 `trigger = scheduled` 的每日 03:30 production backup 可計數，手動測試不計；每次都必須同一 `backupId`、R2/GCS 兩 copy `success` 且存在有效 package digest。達成連續 14 次前不得進 Phase D。Phase C 期間 GCS 仍每日／14 天，R2 30 天。**目前 `x/14` 不在文件內猜測，需讀 production catalog / UI。**
- [ ] **Tiered Backup Phase D**：Phase C acceptance 後才切成 R2 每日、GCS 每週三／週日 cross-cloud DR replication，GCS retention 26 週／182 天；不得 cutover 當天大量刪除既有 V0.17 daily GCS objects。
- [ ] **Tiered Backup Phase E**：per-App tiered model 穩定且 CY Web 準備完成後，再逐步導入 shared `CY Backup Service / Worker`；direct GCS path 在 shared-service acceptance 前保留 rollback 能力。
- [x] Logical backup / provider-copy catalog：一個 logical backup 只列一次，R2/GCS copy health 分開呈現；現行 schema 由既有 `backup_runs` 漸進擴充，legacy GCS run 紀錄仍保留作相容／rollback evidence。
- [ ] 共通 outer format `CYBackupSet / formatVersion 1` 僅能走 versioned compatibility path；既有 V0.17 GCS objects 不改寫，舊格式 reader/validator 保留到 compatibility window 結束。
- [ ] Google Cloud Billing 設定每月低額預算警示（目標 NT$100）。
- [ ] GCS provider lifecycle 僅作第二層 guard；若啟用，期限必須長於當前 application policy，且 migration 期間不得提前清除仍可能需要 replication／rollback 的來源備份。
- [ ] Cloud Storage 復原功能 **僅 `SUPER_ADMIN` 可執行**；其他角色均不可復原，最終角色名稱需與當時 CYID contract 對齊。
- [ ] 復原採雙重確認；第二次必須明確提示將覆蓋目前 D1 資料，且 Worker/API server-side authorization 為權威，不得只靠 UI 隱藏。
- [ ] 復原前驗證備份格式、App/schema version、manifest 與完整性；復原操作需留下操作者、時間、backup identifier 與結果等 audit evidence。
- [ ] 驗證「R2 不可用時可由有效 GCS copy 載入」與「Cloudflare/D1 故障後，以 GCS cross-cloud DR 備份重建新 D1」的完整災難復原演練。
- [ ] Web UI **不提供「清除全部帳務資料／期初餘額」功能，也不提供對應一般應用 API**。若真的需要整庫清理，視為平台管理／維運操作，直接在 Cloudflare／D1 管理層處理。

## UI／UX 與多裝置支援

- [x] Desktop UI/UX Phase 3：收斂 Header 與主畫面垂直空間、固定記帳資料表欄寬比例、加強金額／餘額掃讀與列 hover、收窄操作欄，並提高設定視窗管理密度（V0.10.0）。
- [x] Desktop UI/UX Phase 2：主工作區縮窄約 20%、收支改雙態切換、輸入確認改右側 edge sidebar、期初餘額移至記帳資料區、重整記帳工具列（V0.9.0）。
- [x] Desktop UI/UX Phase 1：輸入確認改為右側可收合 drawer、修正版本顯示單一來源、提高記帳資料表桌面資訊密度（V0.8.0）。
- [x] V0.18.1 備份／復原頁「最近 logical backup」收斂為單行六欄顯示（時間、方式、Backup ID、R2、GCS、資料），移除 desktop 水平捲動；同版加入 Phase C scheduled acceptance `x/14` 進度。
- [x] **V0.20.0 RWD / Adaptive UI Phase 1**：建立正式 Desktop `>=1024px`、Tablet `768–1023px`、Mobile `<768px` presentation 分層；此項為程式／CI 完成狀態，不代表三種裝置的人工視覺 acceptance 都完成。
- [x] **V0.21.x Desktop Business UI**：Desktop 已轉為現代、精緻、商務、簡潔方向；帳戶／科目管理、Custom picker、Header、compact dialogs、optimistic drag 等已進入 V0.21.2 階段。
- [ ] **Desktop real-bookkeeping acceptance**：目前空狀態與管理介面做到階段性停點；仍需實際開始記帳、累積真實資料後再看 table / recent-input / edit / long-content 等實際樣式，不以空畫面宣告最終完成。
- [x] 採單一網站的 RWD 為基礎，不另做獨立 PC／手機兩套網站。
- [x] 在 RWD 基礎上加入 Adaptive UI：相同資料與功能依裝置使用不同 presentation，而非只把桌面版等比例縮小。
- [x] breakpoint：Desktop `>= 1024px`、Tablet `768–1023px`、Mobile `< 768px` 已落地；後續依真實裝置驗收可微調數值。
- [x] **Mobile Build 10 overall direction**：使用者已確認大方向可接受；新增記帳／記帳資料分成不同頁，帳戶採點擊後展開 chooser/sheet，不採橫向可滑按鈕列。Build 10/11 後手機工具入口收斂為：`餘額` = 期初餘額設定、`更多` = 帳戶設定／科目設定／月份鎖帳／匯出 Excel、右上角使用者選單 = 身分資訊＋登出。
- [ ] **Mobile refinement / final acceptance**：V0.21.6 Build 8 保留既有 Build 10/11 mobile behavior；仍需真實手機逐項驗收月份列、期初餘額手機介面、Account/Category focused settings、direct Month Lock dialog、user menu logout-only、slider corner、swipe edit/delete 與 edit cancel/return context，不標為最終完成。
- [ ] **Tablet visual / interaction / login acceptance**：真實平板測試已確認目前狀態不可接受：觸控/focus 很差且登入仍不可靠。Build 6/8 legacy auth hotfix 未解決問題；先停止 Tablet-specific legacy auth 疊 patch，待 CYID governed integration handoff / development path 收斂後再做真實裝置 login + post-login acceptance。
- [ ] Desktop 仍以高資訊密度與鍵盤高效率輸入為主要操作模式；Mobile 以查詢、確認、快速輸入與簡單修改為優先。待 Desktop/Tablet/Mobile 實機交叉驗收後再勾選完成。
- [ ] 若未來出現掃碼、拍攝單據、離線作業、Push Notification 等強烈行動裝置需求，再評估 PWA 或原生 App；目前不提前拆成第二套前端。

## 長期整合方向

- [ ] 未來可評估將 CYAccountingWeb 納入 **Chihyuan 企業管理系統**，作為其中的記帳／財務模組之一。
- [ ] 在真正整合前，CYAccountingWeb 仍維持獨立部署、獨立帳務資料庫、獨立 backup dataset／storage identity 與清楚 API 邊界，避免為尚未定案的企業入口過早耦合。
- [ ] 若未來 Chihyuan 企業管理系統整合多個 CY 工具，再統一規劃入口、導覽、角色／App 權限與共用帳號體驗。
- [ ] Backup 整合優先採「各 App → 共用 CY Backup Service／Worker → app-scoped R2/GCS」；不以直接共用同一把廣權限 storage credential 作為整合方式。
- [ ] 即使改由共用 Backup Service 管理，各 App 的備份資料仍維持邏輯隔離與獨立還原能力；caller identity 必須由 server-side mapping 決定可存取 dataset，不得只信任 caller 傳入的 `appId`。
- [x] 共用員工帳號權威已切到 **CYCloud Identity (CYID)**；CYAccountingWeb 不直接讀寫 CYID D1，只透過 canonical `IDENTITY` Service Binding contract 使用 Identity 能力。
- [x] CYID consumer integration 依 canonical `apps/CYCloudIdentity/docs/CONSUMER_INTEGRATION_STANDARD.md` 與 `docs/consumers/CYACC_INTEGRATION_HANDOFF.md`；current contract `1.0.2` / minimum compatible `1.0.0`，CYACC 採用 `1.0.1` 仍相容。舊 dated CYID handoff 不得作 current authority。
- [x] 歷史 PR #252 結構清理已合併，現行 source 已無版本殼：移除版本 wrapper/shell、改成功能模組，`CYID Consumer Impact: NONE`。功能模組使用 semantic identifiers，CI 阻止版本殼回流。
- [ ] 涉及 Identity authority、跨 App 帳號／角色、shared account database、Service Binding、shared Worker、跨 App D1 ownership 或 shared Backup Service routing 等底層變更時，實作前必須先同步 CY-WEB / CYCloudIdentity 最新決策，不由 CYAccountingWeb 單獨先行定義。
- [ ] 在 shared Identity 正式遷移完成前，CYAccountingWeb 仍維持自己的帳務 D1 與 application session 邊界；共用帳號不代表合併 runtime database。
