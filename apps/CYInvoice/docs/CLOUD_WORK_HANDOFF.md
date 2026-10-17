# CYInvoice 現行工作交接

更新日期：2026-10-13（Asia/Taipei）

本文件保存目前工作停點與驗證證據；唯一待辦清單為 [TODO.md](TODO.md)，實機步驟為 [RC_TEST.md](RC_TEST.md)。永久規則仍依 repository REPOSITORY_RULES.md → REPO_POLICY.md → apps/CYInvoice/PROJECT_RULES.md；本文件不是額外規則層。

## 最新停點：V3.0 分版工程（2026-10-18，僅 Source／CI）

- **2026-10-18 最新優先序（使用者指定）**：CYWEB 正由 Claude 進行介面重整，**暫不修改 CYWEB repo／#151、不合併、不部署、不處理介面串接**；目前專注 CYInvoice 單 Repo 的 CI、3.0-A 退役入口與舊版相容、3.0-B 公司卡 DPAPI／撤銷與實際業務憑證來源、3.0-C Worker Recovery 契約與隔離 D1 migration／rollback。CYWEB 復原串接排後段，等新介面基準穩定才核對 #151 並做真 Email／雙機／DR-01～13。此為**排程調整，不取消 3.0 Recovery 驗收門檻或開啟生產部署權限**；詳見 TODO 最前段。
- 當前開發 PR [#476](https://github.com/simonliu1118-byte/CYapps/pull/476) `cyinvoice/v3.0-mode-setup`；3.0 僅 Local／CYID 雙模式、公司卡、災難復原、必要 D1；3.1 非折讓跨機、3.2 折讓 API／跨機、3.3 其餘。不要將下方 2026-10-13 歷史階段重新當成待實作清單。
- 3.0-A 新增 MainForm 退役入口 fail-closed：在歷史 Built-in Cloud 舊設定下隱藏帳號管理、阻止舊 Cloud 密碼復原 API，顯示 Local／CYID-only 提示且不刪歷史或資料；加 WinForms smoke。Local 帳號管理、CYID 外部權威不變。
- 3.0-A Windows CYID-only 入口與 Worker fail-closed；3.0-B 公司卡 API／Windows DPAPI 手動套用與版本標記；3.0-C 復原 Worker／Windows Pending Token／UI、0014/0015 canonical migrations 均已有工程實作。已完成／未完成逐項只看 `TODO.md` 最前段與最新精確 head CI。
- 3.0-A/B/C 新增 Windows 防重新導向收尾：SettingsForm 公司卡、CloudDirectJoinForm 加入及 CloudRecoveryForm 復原的敏感 CYID HTTP POST 禁止自動 307／308 轉送，防止密碼／復原碼／Device Token／業務密鑰外洩；既有 Cloud client regression 加 Source guard。只動 CYInvoice；CYWEB 仍暫緩，原 Worker／D1 不動。
- 本輪安全收尾：公司卡／復原帶 CYID 密碼的輸入在 Worker 開始 Session／Device 核對**之前**執行串流實際位元組容量限制，不以可能缺少的 Content-Length 判斷；僅新增隔離回歸，原 V2 發票、人工折讓和 Local 不改。
- **遠端不動**：原 CYID activation／CYID_ONLY D1 已完成，不重做；3.0 的 0014/0015 目前只在 Git source，正式原 D1 schema 13、Cloud Worker 0.9.3 保留。兩項功能 feature flag 預設關閉；需獨立核准備份、受控 migration、Worker/CYWEB 接線及真實 DR-01～13／兩模式驗收，不能憑 CI 綠燈宣稱上線。
- 保留前期完整版程式 [#440](https://github.com/simonliu1118-byte/CYapps/pull/440) 作後續分版來源；CYWEB 復原草稿 [#151](https://github.com/simonliu1118-byte/chihyuan-web/pull/151) 尚未合併。不要重寫既有 CYID、Device、D1。正式 Release 必須另有當次授權。

## 歷史停點：災難復原併入 V3；CYWEB 超管復原邀請設計（2026-10-13）

- **使用者方向**：災難復原與 V3 多機協同一起做；由 CYID 超管在 CYWEB 簽發短效一次性復原邀請，新機加入原 CYInvoice business Workspace，不靠遺失的舊 Token。
- **設計已整理**：[CLOUD_DISASTER_RECOVERY.md](CLOUD_DISASTER_RECOVERY.md) 定義情境、CYWEB/core 與 CYInvoice App scoped 授權、private issuer、現有 invitation／Audit migration、protected Pending Token／scope／結果不明、舊 Device history、V3 Work Item 重整及服務故障備份邊界。未增加永久規則或 shared Identity authority。
- **source 事實**：現行 issueInvitation／createPairing 要舊 Device；已有有效 code 可走 join，但沒有無碼復原入口。D1 active 數不是可用 Token 數；issuer Device FK 需 forward migration。CYID Session 綁原 Application，不能將 CYWEB core token 改 header 後當 CYInvoice Session。建議簽發時重新驗超管帳密，沿用現有 canonical Login／Resolve／finally Logout。
- **當前未完成**：CYWEB 本輪實際 source／CSRF owner 尚未取得；跨專案接線、schema／runtime／UI、復原加入、DR-01～DR-13、本地／CI／實機與可攜備份工具／獨立保管／異機 drill 皆未實作或未執行。產品設計完成不代表服務已有復原能力。
- **維運界線**：CYWEB／CYID／Invoice／D1 都需可用才可簽發兌換；邀請只恢復 Device 信任，不還原整庫或找回未上雲的舊機輸入。D1 restore 要先保留新資料／撤銷證據與 profile 相容性，再按當次具體範圍批准。
- **原完成基準保留**：V2.6.19 Release、原 Worker0.9.3／CYID=true／CYID_ONLY 與私有 D1 維護證據維持。此批僅設計文件，未連線變更真實 D1／Worker／CYWEB／CYID，未新增 Release。唯一待辦在 TODO §7.4／§7.5。

## 已完成基準：CYID_ONLY 維護（2026-10-13）

- **實際維護已完成**：使用者獨立批准私有備份、原 Worker 相容部署及指定 D1 退役。Cloud0.9.3／API1／marker8／storage13 由 [Development Deploy #38396129643](https://github.com/simonliu1118-byte/CYapps/actions/runs/38396129643) 部署，保持 CYID=true／原全部 bindings；storage health 正常。
- **原 D1 已清理**：四張 Built-in 員工／轉換表物理移除，CYID_ONLY marker 正確；invitation 歷史 actor 保留並移除舊員工 FK；Workspace／Device／Token／pairing／invitation／audit／migration history 逐筆符合預期；共用 pairing OTP 保留，舊帳號 purpose／recovery fields 已清。foreign_key_check 無違規、quick_check ok。Provider 未變，未操作 CYID D1、未建測試 Worker。
- **私有回復證據**：直接唯讀取得完整應用程式邏輯備份，前後 bookmark 一致；DPAPI CurrentUser 加密、解密／SQLite 還原／完整資料核對與真實資料模擬通過後才執行維護。Cloudflare 內部系統表不在應用程式 dump；原始筆數、ID／hash、備份／Time Travel bookmark／本機路徑留在私有紀錄，未加密暫存已移除。DPAPI 依賴原 Windows 使用者／機器，異機災復尚未完成。
- **執行路徑**：API MCP 的 export／Worker write 權限不可用，備份採唯讀 query，升級沿用原 GitHub workflow；原 D1 由已連線 Bindings MCP 一次 batch 清理，再由 API MCP 逐筆 read-back。沒有增加測試 handler 或自動清表 workflow。
- **驗收界線**：Provider ready、舊 Built-in route／未授權 Device 拒絕已檢查，沒有真人帳密／多裝置全矩陣。保留本批本地／CI 驗收決定；V2.6.19 已發布，折讓歷史缺欄位仍留 V3.0。
- **接續順序（已依後續使用者決定更新）**：TODO §7.4／§7.5 在 V3 同批整合 CYWEB 裝置復原邀請、Device／actor 模型、Work Item／revision／OrderID，再接正式折讓／逐筆歷史及業務 Audit／viewer；可攜備份與維運另依資料回復需要接續。Recovery Device flow 尚未實作，LAST_ACTIVE_DEVICE 保護維持。
- **當前適用範圍**：本公司 CYID_ONLY；其他公司 Built-in／Self-hosted 使用自己的新 D1。本次成功不是關回 Built-in、重跑一次性 DDL或另一環境變更的授權。

## 歷史停點：source 已合併、原 D1 當時未清（2026-10-13，維護前）

- **已完成**：[CYInvoice PR #432](https://github.com/simonliu1118-byte/CYapps/pull/432) 經 squash `0cc97c9a5cf2e63cd6bf887510508f301163c9f4` 合併 main。Cloud source version **0.9.3**；本公司 CYID-only 的帳號表實體移除 maintenance SQL、歷史 invitation actor 脫鉤、配對 OTP 保留、Workspace recovery 清理、CYID Device rename SQL 分流與雙 storage-profile history audit 已入庫。一般 migrations 與 Built-in/self-hosted 的新 D1 架構保持原樣。
- **已驗證**：[CYInvoice Cloud Check #38340836036](https://github.com/simonliu1118-byte/CYapps/actions/runs/38340836036) Linux／SQLite／CYID real-provider／Windows Client／WinForms Smoke／工程包與安全掃描成功；[Cloud Development Validate #38340836071](https://github.com/simonliu1118-byte/CYapps/actions/runs/38340836071) validate 成功，**遠端 deployment skipped**；[Governance #38340836060](https://github.com/simonliu1118-byte/CYapps/actions/runs/38340836060) 成功。
- **真正雲端還沒動**：上述只在 GitHub CI 使用 synthetic／local SQLite。原 CYInvoice development Worker 仍保留先前活躍版本與 CYID 配置；**未進行 Cloudflare upload／deploy、未對原 D1 讀取即時 counts 或執行 DROP/DELETE、未改動 CYID D1、未建立任何測試 Worker**。V2.6.19 正式 ZIP／tag 原樣保留。
- **唯一未完 Gate**：取得原 business D1 真正 read-only inventory（不要公開員工個資或原 IDs）、可回復的私有備份與執行窗口 → 原 Worker 0.9.3 code-only 受控 upgrade（`CYID_ENABLED=true`／Device／service bindings 一致）→ 另外核准的原子化 D1 maintenance → FK／Device／Invitation／Audit／CYID 全面 read-back。專用 SQL 非一般 migration，不得誤用於 Built-in 新資料庫。詳細見 `CLOUD_IDENTITY_LIFECYCLE.md` §20 和 `TODO.md` §7.7。

## 歷史停點：本公司 CYID-only D1 實體移除舊員工表 — source 實作中（2026-10-13）

- 使用者澄清 **Built-in 與 CYID 二選一**；本公司已全面採 CYID，故本公司 **原 CYInvoice D1** 不需再保留 Built-in Employee 帳號／密碼／轉換表；其他公司使用 Built-in 時應以自己全新獨立 D1 的原 migrations 建立中央帳號，絕不共用本公司原 D1。Built-in 程式仍維持正式支援。
- 已開始實作 Cloud source 0.9.3 候選：獨立 `cloud/maintenance/retire-built-in-for-cyid-only.sql` 物理移除 `cloud_employees`、`device_employee_links`、`employee_link_candidates`、`employee_transition_items`（**不納入一般 migrations**）；重建舊 `device_invitations` 保留 original actor text 但移除對舊員工的 FK；共用配對 OTP、Workspace、Device／Token、Security Audit 均保留。唯一 profile marker `cyid_storage_profile` 註記只針對 CYID-only 的 D1。
- CYID mode 的 Device rename query 已不再解析舊 `cloud_employees`，Built-in 版本仍查驗原員工憑證。部署快照依 profile 區分並維持 row fingerprint／FK fail-closed，不允許一般 Cloud deploy 隱含移除表；Cloud/SQLite tests 由 PR CI 驗證。
- **遠端尚未執行**：沒有即時 D1 統計／可確認的私有備份，沒有 Cloudflare D1 UPDATE/DELETE/DROP、Cloudflare Worker upload/deploy、CYID D1 修改、測試 Worker 新建／刪除重試。所有新 source 與 CI 為本地／GitHub 隔離執行。
- **接續 gate**：PR check 全綠 → 原 business D1 經受控唯讀核對（含 `CYID_ENABLED=true`、唯一 Workspace／Device／FK／pending transition／invite／OTP）→ 核准原 Worker 相容 code-only 部署，保持現行 CYID 與所有 binding → 明確 D1 備份／rollback 與當次授權 → 原 D1 上單獨原子 schema retirement → 後驗證。**順序不得顛倒。** 詳細設計以 `CLOUD_IDENTITY_LIFECYCLE.md` §20 為準，原 §19 僅作歷史參考。
- 本工作不意味新 Windows Stable Release 授權。V2.6.19 正式 tag 保留；原 TODO 已勾選項目視為完成，其他未勾選的真實驗收不得自動勾成成功。

## 歷史停點：規劃 CYID 時代舊 D1 帳號資料退場（**未刪除**）— 2026-10-13

- 使用者明確表示 TODO **已勾選項目驗收完成**，授權開始規劃下一階段，並要求調查 CYInvoice 自身 D1 原有 Built-in 帳號資料可否在員工權威改由 CYID 後退役。保留已完成勾選項，不把前次發布完成項目重新打開。本工作只做 **GitHub 原始碼／SQL 外鍵與現有部署證據的唯讀調查 + 文件規劃**；尚未連線做即時原 D1 查詢，沒有任何實際 D1 UPDATE／DELETE、migration、Worker deploy 或 CYID D1 操作。
- **原理**：2026-10-12 的 CYID activation #42 有 `CYID_ENABLED=true`、原 1 Workspace／4 Device 連續性；`cloud/src/app.ts` 在 CYID mode 排除舊 Employee／Web Auth，`runtime-sync.ts` 改由 CYID 查 employee 權限。但這是**目前身分權威已轉向 CYID**，**不是舊 CYInvoice D1 員工列全部已搬移／安全可刪**的證據，且無本次直接讀取原 D1 帳號筆數。
- **立即清理風險**：`cloud_employees` 為 `device_employee_links`、`employee_link_candidates`、`employee_transition_items` 及舊 `device_invitations.issued_by_employee_id` 的 FK 來源；舊 `security_audit_events.actor_employee_id` 有歷史身分語意。`email_otp_challenges` 在 CYID mode 配對授權仍使用，**不是全部可刪的舊帳號資料**。原 `workspaces`／`devices`／token hashes／邀請歷史／audit 保留；reference source 仍需支持 Built-in Cloud／Self-hosted，不能全域刪 schema。
- **清理優先順序**：先受控只讀盤點當前正式綁定原 D1、目標 Workspace 的 `credential_verifier` 非 NULL 數、未結束 transition、FK、invitation legacy actor、有效 OTP、Audit，並做本地雙模式 SQLite fixtures 測試；**第一批僅規劃清除已切 CYID Workspace 的舊 credential material**，既有帳號身份行與外鍵不動。之後才獨立設計／批准歷史個資最小化與真的資料銷毀。
- **目前沒有直接 Cloudflare D1 查詢連接能力**；不可由 GitHub source 推定即時筆數，也不可上傳遠端測試 handler 繞過。規劃全文：[`CLOUD_IDENTITY_LIFECYCLE.md` §19](CLOUD_IDENTITY_LIFECYCLE.md)；唯一核准前待辦：[`TODO.md` §7.7](TODO.md)。
- 本地／CI 的既有工程測試與新設計需遵守絕對禁止建立、上傳或部署任何 Cloudflare **測試 Worker**（舊失敗 DELETE 不再重試），不更換原業務 Workspace／Device／Token、不動 CYID D1。**實際不可逆 D1 清理** 必須在 scope、備份、保留與停損條件確定後，另取使用者明確批准；當前本節絕不代表已授權清理。
- 後續施工方向：先保護已運作的 CYID + Device 基礎，再做 0-Device Recovery、V3 Cloud Work Item／revision／OrderID 防撞／Audit、正式折讓 API 與逐筆歷史；其他原 TODO 未勾選者保留原待辦語意，只有使用者另有指示才調整。

## 歷史停點：CYInvoice V2.6.19 正式發布成功 — 2026-10-13

本批正式 Windows 版本 **V2.6.19 Build 0** 已由使用者 2026-10-12 的發行授權及 2026-10-13 的本地／CI 驗收決策完成發布：

- [正式 GitHub Release：CYInvoice V2.6.19](https://github.com/simonliu1118-byte/CYapps/releases/tag/cyinvoice-v2.6.19)；tag `cyinvoice-v2.6.19`，正式 ZIP `CYInvoice_V2.6.19.zip`（81,502,918 bytes）、同名 `.zip.sha256` 已存在，非 draft/pre-release。
- 成功的 [CYInvoice Stable Release #38336787968](https://github.com/simonliu1118-byte/CYapps/actions/runs/38336787968)：從精確 `main` commit `95fa5deab097c262d55e67391f32693d68573954` 重建；Windows Build、核心／Void／Allowance／SQLite／Sync／SyncCoordinator／Cloud Client 測試、package 與 PE 驗證、portable 啟動、安全掃描與 checksum，全部 27 個 GitHub job steps 成功。這是 Windows CI 證據，不等於沒有執行的真實多角色／多裝置／AMEGO live 驗收。
- 前一次 [Release #38336017123](https://github.com/simonliu1118-byte/CYapps/actions/runs/38336017123) 因 `CYInvoice.Cloud.Tests` 不在主 solution、卻以 `--no-build` 執行而無 exe，於發行前失敗，當次沒有建立 tag／Release。治理 [PR #428](https://github.com/simonliu1118-byte/CYapps/pull/428) 建立 main-only 指定發行分支；[PR #429](https://github.com/simonliu1118-byte/CYapps/pull/429) 更正測試執行，驗證失敗原因後用 `release/cyinvoice-v2.6.19-retry1` 精確指向最新 main，一次觸發成功。沒有使用 Browser Use，也沒有新增或上傳任何 Cloudflare 測試 Worker／版本／handler。
- 使用者決定的人工折讓逐筆歷史／狀態缺陷仍為 **V3.0 重做項目**，已於 Release notes 明列已知限制，不假稱修復。其他真人 AMEGO、DPI、CYID role／A/B/C／60 秒 refresh／revoke 等驗收未取得 live evidence，TODO／RC_TEST 保留供後續迭代；不是本次完成證據。
- 現有 Cloudflare development Worker 0.9.2/storage13、原 D1／Workspace／Device／Token／CYID 啟用狀態未因本次 Windows Release 被改動。**不可再創建／上傳／部署任何 Cloudflare 測試 Worker，既有無法刪除紀錄不重試清理。**
- 下方所有「Release 暫停／尚未建立／需要手動 dispatch」敘述均屬**發布前歷史停點**，不能覆蓋本節成功結果；本批的發布授權已用於本次 `cyinvoice-v2.6.19`，**不延伸為往後版本自動發布許可**。現行發布 owner 仍是 `cyinvoice-release.yml`，允許治理核准的精確 main／一次性版本發行分支，其他 PR／main push 不自動 Release。

## 歷史停點：V2.6.19 本批改本地驗收，折讓歷史改列 V3.0 — 2026-10-13

2026-10-13 使用者決定：折讓人工處理的逐筆歷史／狀態缺陷**本版不修**，隨 V3.0 正式折讓流程與 Cloud Work Item／Audit 重新設計；已知資料缺漏不標示已修復、也不阻擋本批 Release。其餘待驗項目由 AI 以**本地／隔離自動測試與已通過的精確 source GitHub CI** 作本批接受判斷，無須使用者再逐項進行 Windows、光貿、CYID 多裝置實測；過去未取得的 live 證據仍保持未驗，不將 CI 冒充真實裝置的結果。這是本批驗收例外，不自行修改永久規則或直接代表正式環境全部驗收。

**強制雲端安全邊界：不得新建、上傳或部署任何 Cloudflare 測試 Worker、遠端測試版本或 handler。** 以前建立的測試 Worker／版本有無法刪除的紀錄；停止刪除重試，亦不得以新測試 Worker 補驗。不改動原 development／production Worker、D1、Workspace、Device、Token、使用者資料或 CYID 真實帳號。驗收限本地/CI isolated fixtures；正式 Release workflow 是只從 main 建置 Windows 套件的獨立流程，不是 Cloudflare deploy。

### 本批已核對之可重用工程證據

- V2.6.19 買方名稱／Windows 工程原始碼 `6c499108`：[Windows Build #281](https://github.com/simonliu1118-byte/CYapps/actions/runs/38111161002) job `build-windows-x64` success；[Cloud Check #393](https://github.com/simonliu1118-byte/CYapps/actions/runs/38111161004) jobs `validate`、`windows-client` success；Governance 成功。涵蓋 Windows Build、核心測試、WinForms smoke、工程包與公共安全掃描等 CI steps，不等於使用者機器的 UI 實測。
- CYID 啟用準備與 runtime contract source `70514413`：[Cloud Check](https://github.com/simonliu1118-byte/CYapps/actions/runs/38206171458) 的 `validate`、`windows-client` 全成功，[Governance](https://github.com/simonliu1118-byte/CYapps/actions/runs/38206171482) 成功；實際 development 啟用 #42 保留原 Workspace／4 Device／Token hashes／history／FK。原 Windows CYID 帳密進設定成功有使用者先前回報；不擴大成全部角色與裝置實測。
- main 現行產品仍 **V2.6.19 Build 0**；本輪只做驗收／交接／V3 路線圖文件，不改產品核心、schema、Cloud 套件或版本。V2.6.19 本批不為人工折讓已知缺陷開新修補分支或升版。
- 本會話的執行容器沒有 Windows／.NET SDK，且不能透過網路 clone；因此沒有在此容器重跑 WinForms 或 .NET。採用上述**已完成、且對應既有 source 的實際 GitHub Actions CI**作可核對的本批驗收證據，不編造新本地測試結果。
- V2.6.19 不是宣告支援零裝置復原；LAST_ACTIVE_DEVICE、已知拒絕不離線降級、AMEGO 不明結果不重送等安全不變量不改。AA／AB／AC、DPI、真實 AMEGO 等未跑項目在 RC_TEST 仍不勾選，列為後續 live evidence／V3 評估，不阻擋此輪經使用者接受之工程版發行。

原先 **2026-10-12 23:22** 的本批正式 Release 請求保持記錄；使用者本次允許調整驗收門檻，不代表改為自動／持續發布。正式發行仍依 PROJECT_RULES 只能從 `main` 手動啟動 `cyinvoice-release.yml`，重新建置、掃描、測試、產生 ZIP 與 SHA-256；如當前工具無權 `workflow_dispatch`，不可把工程包偽裝成正式 Release。尚未執行的正式發布步驟必須明確記錄。

### 之前的調查紀錄（現已改為 V3.0 待辦）

## 歷史停點：CYID 原機驗證成功；折讓歷史未修，當時 Release 暫停 — 2026-10-13

本輪只更新文件。Windows 仍 V2.6.19 Build 0，沒有本項折讓修正 commit、沒有新 tag／Release。文件基準 main d4552bec4684d93372d90f87a1e10090e4a6a427；#420 啟用及 #422 證據文件已合併，本輪不修改其他專案。

2026-10-12 16:52／16:59 Asia/Taipei 使用者回覆「可以，沒問題」，確認原 V2.6.19 安裝重開後能以 CY Web／CYID 員工帳密進入設定並確認 CYID，原資料正常延續。這是單台原裝置的使用者回報；沒有原機 DB／HTTP trace，不勾選完整角色／多裝置／private RPC／60 秒刷新／斷線／撤銷清除。啟用 #42 的原 Workspace／Device Token hashes／history 保留證據仍在下段；不重跑舊 Built-in deployment 或 activation。

### 折讓人工處理詳情缺欄位：接續第一優先

2026-10-12 23:44 使用者截圖顯示折讓待辦「已人工處理」，但申請員工／時間／原因、處理管理員／時間、金額均為空白占位，官方折讓資料亦顯示無。截圖、真實發票／訂單識別不進 Public repo。使用者強調：這些人工資料只由 CYInvoice 本機保存，光貿不能補回。

已確認 source 事實；不是已證實該筆唯一根因：

- EmployeeAllowanceWorkflowService 正常官方確認成功會將 review 存入 invoices.extra_json 的 cyinvoice_allowance_handled_review，再清 active review；該 key 只保留最後一筆，不能宣稱所有完成路徑都沒寫入。
- CancelManualReview 清 active review／pending 並 Resolve；InvoiceAdministrativeClosureService.ClearActiveWorkMetadata 亦清人工 metadata，沒有逐筆完整封存。
- sync_issues 只存類型／訊息／建立與結束時間，沒有上述申請／處理快照。SyncIssuesForm.AllowanceDetailRows 讀目前 active 或最後 handled review，並以 RequestedUtc 與 issue.CreatedUtc 相差五分鐘作排除；後續同發票新申請可能使舊詳情空白／錯配，沒有可靠的逐筆歷史對應。
- resolved 折讓／折讓作廢一律被顯示成「已人工處理」，無法區分官方完成、取消退回、行政結案；舊訊息也可能仍是等待管理員。

尚未取得這筆操作路徑（按「已人工處理」或「取消退回／手動結案」）、原 SQLite／備份。已問操作路徑，使用者接著詢問 DB 用途而未回答。不能判定該筆真的未寫入、被清除或只是讀取對應失敗。尚未改程式／VERSION／BUILD／schema、尚未跑本項回歸。

接續先盤點既有 workflow／issue store／invoice metadata owner，決定穩定的逐筆對應與保存位置，完整保存申請人／時間／原因／金額、處理人／時間、結束結果；取消及行政結案也應保留可追溯紀錄。這是待實作方向，沒有定案新增表／欄位／migration，不另建平行待辦或身分 authority。先重現正常完成、取消、行政結案、同發票第二次申請／五分鐘對應、重啟與同步，再修 core 保存及 UI 狀態。舊資料只能依可核對的本機 metadata／備份恢復；缺失明確顯示，不捏造或用光貿拼湊。保存期限仍依原 retention；本輪不變更。

### Release 與下一段順序

2026-10-12 23:22 使用者已要求本批 Release，隨後詢問未完成項目並回報折讓缺陷；目前先處理缺陷與驗收。尚未啟動 cyinvoice-release.yml、建立 cyinvoice-v2.6.19 或發布，最後查到正式版仍 cyinvoice-v2.4.2。這次發布要求已記錄，同一工作接手不要求重複授權，也不延伸成日後每版自動發布。開始修新獨立 bug 才按現行規則判斷 Patch／Build；本文件整理不推進版本。

接手順序：三層規則與本段 → 修折讓逐筆歷史／狀態與回歸 → 原機驗收 → 一般員工授權、60 秒刷新／重連、四種服務狀態、有備份的指定裝置撤銷中斷續清 → main 正式 Windows Release workflow 重建／掃描／封裝並發布。多公司、跨機 Work Item、0-Device recovery 為獨立後續項目，不擴成本輪全部功能開發。

本會話 GitHub connector 能建 PR／合併與讀 Actions，沒有 workflow_dispatch／建立 Release／上傳資產工具；正式 workflow 僅 main 手動觸發。發布時先查當時可用工具，必要時引導 Actions → CYInvoice Stable Release → Run workflow → main；不為觸發改成 push 自動發布、重用 engineering 包或另建發布 owner。這是此次能力記錄，不是新增規則。

本機 Data/CYInvoice.db 保存 invoices／invoice_items／buyer_names／sync_state／sync_issues、Local employees／Built-in cache；人工 metadata 在 invoices.extra_json。設定／加密 Device 憑證在 Data/settings.json，CYID protected offline cache 在 Data/cyid_offline_cache.json，Cache／Logs 另存檔案；中央帳号仍由 CYID 管理。唯一待辦為 TODO，唯一人工驗收清單為 RC_TEST。

## CYID 啟用證據 — 2026-10-12

2026-10-12 12:52 Asia/Taipei 使用者確認原安裝升級後發票／設定都在，且原帳密可開設定，授權繼續。PR #420 source `70514413b9a3863e1616ecb919a755e1fa30e6bf`／tree `c8fb38c1ce2253b17769aa18f577cf9b7009d58f` 的全部 checks（Governance、deployment validate、Cloud Worker／Windows）通過，合併 `2543222d0a7c9ccd402740477fd3793d123aaeba` 為相同 tree。

[實際 activate-cyid #42](https://github.com/simonliu1118-byte/CYapps/actions/runs/38206372325) source `8488ce3d547d671317cb55f1dd27198506462d05`／同已測 tree 成功：before 原 Built-in／完整 scope，exact active inheritance／uploaded readback／100% business 部署與 after 檢查確認 **CYID_ENABLED=true**。Cloud0.9.2／API1／marker8／storage13、1 Workspace／4 Device records、原 Device Token hashes／employee／history／FK／routes 保留；migration skipped／private files cleanup 通過。Recovery UTC `2026-10-12T05:05:10.603Z`，不是 restore rehearsal；無新 Workspace／Device／資料移轉、Secret replacement、測試程式上傳或 production 部署。原 V2.6.19 可直接續用。

原機 CYID 帳密進設定已由使用者回報成功，範圍見最新停點；其餘角色／装置、private RPC、AA／AB／AC 仍待驗收。已確認 CYID 的 client 不自動退回 Built-in，不以 reset／rejoin 修復認證錯誤。產品 Windows2.6.19／Cloud0.9.2／Consumer1.0.3／canonical1.0.4 不變。下方 #38/#39 為啟用前歷史證據。

使用者在既有 CY Web development 中央管理完成 CYInvoice 登記／目前 CYID Workspace 啟用；畫面已出現 CYInvoice 欄，Super Admin 永遠允許，一般員工 App Access 尚未確認。使用者新增 Provider runtime 的 CYINVOICE_APPLICATION_ID／CYINVOICE_WORKSPACE_ID，後者與既有 core portal 身分 Workspace 一致；實際 ID、帳號及 Device Token 不寫 Public 文件。此身分綁定不替換原 CYInvoice business Workspace。

[唯讀 #38](https://github.com/simonliu1118-byte/CYapps/actions/runs/38203645406)（source `ce6528900c7101485424c5c989fe437ae4881d89`）確認 scope configured=true，Consumer 不讀 CYID D1。[prepare-cyid #39](https://github.com/simonliu1118-byte/CYapps/actions/runs/38203790273)（source `2e21c8709f0ef35e665de4d48d4a069b310d1257`）以 main 基準 `2c3071c8b8ed30b5b4898bcf95ca8b174a7b5a99`／tree `c1d303e4d3653c9bd03a034be335bb740c6c2101` 完成真實 business bundle 上傳／100% 部署；IDENTITY Fetcher、IDENTITY_AUTHORITY 的 ConsumerAuthoritySync entrypoint 與三個 approved scope vars readback 通過。原 Cloud0.9.2／API1／marker8／storage13、1 Workspace／4 Device records、原 Token hashes／employee／history／FK／routes 保留，migration skipped、private files cleanup 通過；CYID_ENABLED=false／Built-in 保留。Provider0.3.7／canonical1.0.4、minimum1.0.0。Private recovery reference UTC `2026-10-12T04:27:16.969Z`；不是 restore rehearsal。無測試程式上傳、Secret replacement、重建 Workspace 或 authority switch。

配置 readback 不等於 private Fetcher／RPC／Employee／App Access／Role 已實際驗收。原安裝 Built-in continuity 已由使用者回報、development 啟用已通過 #42；單台中央帳密進設定已回報成功，其餘 AA／AB／AC 仍待完成。下方既有日期段落為歷史證據，過往 scope=false／未部署／未合併敘述不代表現在停點。

現行 [V2.6.19 Build 0 工程包](https://github.com/simonliu1118-byte/CYapps/actions/runs/38198294935/artifacts/11724462289)：Cloud Check #412、source `2230ec2b88d2c8835a20f374f08abfc66102a70d`，81,093,499 bytes，archive SHA-256 `aa97e42368f38828b89c1206f6454b7b39ac44420bd188e7bbe1c36107dc32bc`，到期 2026-10-15 11:06 Asia/Taipei。Invoice subtree 與本輪 main 基準完全一致；依 RC_TEST 首段升級原 portable folder，保留原資料，不重新加入。

## 1. Git／版本／發布狀態

| 項目 | 已核對狀態 |
| --- | --- |
| Repository／專案 | simonliu1118-byte/CYapps／apps/CYInvoice/ |
| main | `2c3071c8b8ed30b5b4898bcf95ca8b174a7b5a99`（本轮配置證據基準）；#380／#416／#417 等已合併，CYInvoice V2.6.19 Build 0／canonical 1.0.4；本輪不修改 CYERPAutoInput |
| 已完成工程 PR | #380 工程基準／#385 買方名稱修改已合併；最新 Windows 功能 head `6c499108` 的 Windows #281／Cloud #393／Governance #1201 通過，工程包見下方 |
| 前置介面 PR | #216／#379 的 head 已包含於 #380 ancestry 並進入 main；不重複合併 |
| 本輪工作線 | CYInvoice 功能已合併 main；CYID 綁定準備獨立接續，不再把歷史 feature branch 當未合併停點 |
| 工程版本 | **V2.6.19 Build 0**；查無買方名稱可留空，來源為 ../VERSION、../BUILD |
| 先前分流已驗證 commit | `66ec3671d5812efce9c97d6b6796585eed9a638f` |
| 當時最新正式 Release（歷史） | `cyinvoice-v2.4.2`；本批當時未建立新 tag／Release，已由本文最新停點更新 |

前置獨立裝置管理需求推進 V2.6.14／BUILD 0；保留 V2.6.11～13。CI／工程包請核對 [PR #216 精確 head checks](https://github.com/simonliu1118-byte/CYapps/pull/216/checks)，不能拿 V2.6.13 Windows #265 的綠燈當新版通過。正式 Release 未授權、未建立。

## 買方名稱查無資料 — 2026-10-11

使用者確認光貿接受 BuyerName 等於統編；V2.6.19 Build 0 調整手開／全部匯入共用送出 owner。正常查無資料提示人工輸入，留空以統編送出且本機紀錄相同；預設值不寫人工名稱記憶。技術／權限錯誤仍依原規則阻擋，測試池隱私不放寬，無新表／timer／authority。功能 source **6c49910836bb1e7306881c0c627ce868d09f9d04** 通過 Windows #281／Cloud #393／Governance #1201，#385 已合併。實機 RC D.12 仍待驗收。CYID 接續 work 保留；Provider development 保留設定 upgrade PR #384 已通過四項 CI 並合併，遠端 preflight #123／actual upgrade #124 已通過；Provider development 0.3.6 已部署，CY Web #145 canonical mirror 同步完成。private binding／實機／正式切換仍待驗收。

[Windows V2.6.19 Build 0 engineering package](https://github.com/simonliu1118-byte/CYapps/actions/runs/38111161002/artifacts/11692290244)：Artifact 11692290244，81093610 bytes，SHA256 `a4ca85b4d452ffbfc0c01b92649208d6ee3942fe12965ed47cbb8c6e3ecbef40`，head 對應 6c499108，2026-10-14 12:21 Asia/Taipei 到期。Native startup、DPAPI／business suites、PE／layout／packaged startup 與安全掃描通過；完整程式包覆蓋原 portable folder，保留 Data／Cache／Logs／Workspace。空白／統編預設值不建立 **buyer_names 人工名稱記憶**；發票清單仍保存實際送出名稱，不改成缺少 BuyerName 的發票快照。結果不明再回查成功亦不會誤存預設值。

## 重複開立失敗修正 — 2026-10-11

使用者確認光貿未重開，較新的本機雙成功列是錯誤紀錄；維持第二次送原 API OrderID、由光貿判定，收到拒絕應記開立失敗。修正共用 AmegoClient decoder，整數字串 code 不再在校時檢查 GetInt32 拋例外後進入回查；缺失／不合法 code 不默認成功。HTTP 修正前測試能重現舊發票被記成第二筆成功，但未取得原 Logs，不把模擬路徑宣稱為該次事件唯一根因。

InvoiceService 保留送出／未知結果回查 owner，明確拒絕走既有 Failed 保存；已知同環境／API OrderID／發票號碼的舊發票不能算新增成功。官方同步／詳細回查不覆寫 Failed。RecordsControl 唯一主清單 owner 隱藏失敗，SyncIssuesForm 仍顯示與清除原 invoices 的 Failed，無新增 table／wrapper／timer／provider。結果不明仍保留主清單與禁止盲目重送。

V2.6.18／BUILD 1，同一開票 bug 補修；本地回歸與精確 head CI／工程包結果以 PR #380 checks 為準，Windows UI／官方 API 實機仍需 RC D.9。原 development Cloud 0.9.2／Built-in 保留；本批不部署、切 CYID、merge 或 Release。使用者已核准在既有同步加入受限舊資料修復：同公司／環境、同官方號碼及 API OrderID，兩筆完整內容一致、沒有人工／作廢／折讓 metadata，且原生成 ID 的 UTC 時序與 SentAt 一致、官方開立時間介於兩次送出才修復。先唯讀回查、完整 SQLite online backup（Data/RepairBackups；完整性／FK／快照檢查及 flush），寫入交易內再比對原發票／明細快照；併發改動、備份失敗或取消不改資料。原成功保留，多出的嘗試轉 Failed 並清空號碼／綠燈，仍在原 invoices／上傳問題；修復與本輪同步原子提交，失敗可重試，成功後舊 ambiguous issue 依原路徑解決。無新 owner／table／timer；未取得使用者實際 Data，不能宣稱他的資料已修好。缺證據／第三筆／差異仍保留歧義，需原本機核對。

## CYID Consumer／服務連線現行停點

### Provider development 升級準備 — 2026-10-11

#380 精確功能 head 46723a89 七項 CI 全綠後合併 main 59d63b67；Windows 工程包 #280 是買方名稱修改前版本。#384 deployment audit source 348185fb 四項 CI 通過並合併，development preflight #123 與 actual code-only upgrade #124 成功；部署 commit 56c27685 與 tested tree 相同。Provider development **0.3.6**／canonical **1.0.3**／minimum **1.0.0**，七個原 migration 無 pending、未寫 schema，原 vars／DB／rate limits／bindings／Secret names／hashed authority metadata／history counts／FK 保留，synthetic invalid Session 拒絕正常。UTC recovery reference 2026-10-11T04:20:13.295Z，可回查 Cloudflare Time Travel／Worker history，非 restore rehearsal；無 bootstrap／Secret replacement／authority 切換。

CY Web governance #145 canonical mirrors1.0.3已同步，consumer1.0.2相容；Provider0.3.6 development #124已部署且原authority保留。CYInvoice Cloud0.9.2/storage13維持Built-in，private binding／真實授權／Windows／cutover未完成。

2026-10-11 使用者定案：保留刪除失敗的 expired／undeployed／no-D1 測試紀錄，停止 DELETE 重試／擴權，後續不再把測試 Worker／測試版本上傳 Cloudflare。遠端 probe／cleanup CLI、專用 branch push triggers／dispatch modes及測試模組已移除；本地／CI synthetic Worker 與真實 core regression 繼續。舊 branches／歷史 run 不能作接續入口。

原 CYInvoice development deployment owner 改用 modern Worker version API，所有非 plain-text bindings（包含 Secrets、原 DB、rate limits、services）以 exact active version UUID 繼承；不從 latest upload 繼承、不讀／重設 Secret 值。僅上傳本次受檢查的真實 app.js，保留 Built-in／CYID_ENABLED=false；上傳後讀回完整 binding names/types/targets/vars與compatibility，重查原100%流量與routes穩定，再明確部署該新UUID。未知resource、split traffic、過期checkpoint、任一readback不符皆停止；response loss不自動重試，always-run核對實際active version／原history／health。過期紀錄存在不再單獨阻擋這條 exact-source deployment。原 D1／Workspace／Device／Token 不 reset 或 rejoin。

本輪已完成：PR #402 source `274e41be4f887df8d52c1c22bb67158d8e42e301`／tree `97c587033c02524aae5d81091b1a2eaaf67ef818` 六項 exact-head CI（Governance #1252、Provider Check #127、Provider development #147、Provider production #33、CYInvoice development validate #24、Cloud Check #399含Windows）全通過，合併 `b7acdbf6f9079c9b72f507a3f14c912eaf738eb1` 為同一tree。[Actual development #25](https://github.com/simonliu1118-byte/CYapps/actions/runs/38134710138) 以continuity source `aeb3434fe2ff3bec1fad2a726c8166066bcd333a`／同一已測tree完成真實business版本上傳、exact-active-source bindings／compatibility readback及100% deployment。原Cloud0.9.2／API1／marker8／storage13、1 Workspace／4 Device records、Token hashes／employee／history／FK及routes保留，Built-in／CYID false；pending migrations0，沒有Secret replacement／authority切換／test upload／DELETE／restore。1個expired未部署no-D1測試版本在before及after均仍存在，已證明不再阻擋部署。私有recovery reference UTC `2026-10-11T11:13:08.522Z`；runner private files移除通過。這是本次窗口的readback，不宣稱讀到Secret值或完成真實Windows登入／restore rehearsal。

原三個probe／cleanup branch refs已fast-forward停放到#402同一tree，controller／測試模組與push trigger均不存在，不重用歷史run。Temporary Workers Admin取消仍待使用者確認，不需增加權限。

CYID private transport、真實 Application／兩 Workspace binding／Employee App Access與Role、原Windows裝置認證、RC AA／AB／AC、實際Free用量、portable清除中斷復原與最終authority cutover保持待驗收。後續透過真實consumer已存在的功能驗收，不新增遠端測試handler或帳號authority；production切換／Release仍獨立核准。產品VERSION／BUILD、Cloud0.9.2、Provider0.3.6、canonical1.0.3／minimum1.0.0不變。



### CYID 綁定準備／共通設定已驗證 — 2026-10-12

#413 source `8d3ed4366038b511e0c1f5ce50395907529030a2`／tree `a14f00aac19643e3d7e9145b9c26e8d87356ef05` 七項 exact-head CI 全通過，合併 main `4655460c3362232fd1711295f3565f00b464bdf9`（同一 tree）。使用者已完成共通 Secret；[唯讀 preflight #29](https://github.com/simonliu1118-byte/CYapps/actions/runs/38191352230) 以 continuity `ea0d55a3c95826e4da652d0cf03be3a5a0c00aac`／同一 tree 通過，原 Cloud 0.9.2／API1／marker8／storage13／1 Workspace／4 Device records／Token hashes／history／FK／routes 保留、CYID false；expired undeployed no-D1 probe 仍1個。bundle／migration／deploy 三步均 skipped、private cleanup成功，沒有測試上傳、Identity D1 讀取或權威切換。CYACC main 的 [Validate and Deploy #608](https://github.com/simonliu1118-byte/CYapps/actions/runs/38191534386) validate／deploy 都通過，證明新的共通 input 在既有 production resolver 可用；不表示 Invoice 已接入 production。

#29 before/after 均確認 Provider 的 CYInvoice scope configured=false；CYINVOICE_APPLICATION_ID／CYINVOICE_WORKSPACE_ID 尚未配置，不能啟動 prepare-cyid 或開 CYID flag。尚未取得現行中央管理的 Application registry／Workspace enablement 證據，不能把缺部署 metadata 等同「App 尚未註冊」。source 現行 admin snapshot 可查看 Actor Workspace 的 apps／員工 App Access；現行已部署版本尚無後加 Application／Workspace enablement 管理 route；Provider source 0.3.7／contract 1.0.4 補上中央 core-portal Super Admin 登記，開發部署已於 Provider #158／Web #138 完成，真人執行仍待完成，bootstrap 仍只限空 Identity DB。下一步由 CYID 已授權中央管理流程確認既有註冊／核准 scope，不能重播 bootstrap、建立替代 Workspace、借 CYACC production scope 作 development 對應或由 Consumer 直讀／寫 Identity D1。實際 private transport、Employee/App Access／Role、原 Windows與正式切換仍待驗收。

Variable retirement 已完成：官方 active／retired refs 已同步 Secret，使用者於 2026-10-12 確認刪除同名 Variable；刪除後唯讀 Invoice #34（38195315134）完整通過。歷史 gate 以 Git 追溯，不重跑舊 provisioning／probe runs。

原 Workspace／Builtin／Device 延續設計與唯一部署 owner 保留，Provider metadata resolver 不成為 Employee authority；技術操作見 [Cloud README](../cloud/README.md#prepare-the-existing-cyid-binding-built-in-retained)。產品／canonical 版本不變，最新 Invoice business code 實際部署仍 #25；#29 為唯讀檢查。

### 分段上線準備 — 2026-10-11

使用者委託逐步推進至 CYID 上線；先 preflight／保留 Built-in 的 additive Cloud，再隔離 staging／Windows 實機及可檢視的正式切換方案。PR #380 工程基準已合併；未因本次委託略過原 Workspace continuity、帳號／App Access／Role、private binding、回復及 Release gates。

沿用 cyinvoice-cloud-dev-deploy.yml 為唯一 development owner，修正 health 舊 0.9.0 hard-code；preflight 專用 branch 只讀遠端。部署前固定 original D1／development／CYID disabled，核對 immutable migration prefix（僅允許 pending 0013）、FK、必要 runtime secret names／rate limit、private Time Travel／Worker recovery checkpoint。暫存 generated config 保留原 vars/services/secrets；after 核對 source version、storage 13、runtime-sync capability 與原 Workspace／Device Token hashes／員工／邀請／audit 歷史。敏感值不寫 Public log／Artifact；恢復點 UTC timestamp 可回查 bookmark，不自動 D1 restore。

本機 TypeScript／完整 Worker 回歸／bundle 與 deployment audit 通過，含真實 SQLite 0013 前後歷史延續及 Token hash 異動拒絕。Windows／Worker business runtime 未改，VERSION／BUILD 維持 V2.6.17 Build 2。唯一 checklist 見 TODO §7.3.3；執行／回復說明見 cloud/README。

Source **82080dfa1939c2ea5873aa3902d5ff46fb509a5b** 的七項 CI 全部通過（Windows #277、Cloud #388、Governance #1187、CYID #107，以及三個 deployment workflow 的 PR validate；PR deployment jobs 均 skipped）。[Windows #277 工程包](https://github.com/simonliu1118-byte/CYapps/actions/runs/38076799208/artifacts/11678896282)，81,087,619 bytes，archive SHA-256 cc00f5534b41913681f4889a9e67c5cf829c24c9ccf9f169e66f4bba72c989eb，expiry 2026-10-14 02:44 Asia/Taipei。較早 #275／#276 包保留歷史追溯，本段才是本輪 source 證據。

[唯讀 preflight #11](https://github.com/simonliu1118-byte/CYapps/actions/runs/38076867183)（cbb26758；tree 與 82080dfa 完全相同）通過：原 Cloud 0.8.9／storage 12、1 Workspace／4 Device records、僅 pending 0013、FK 正常及 private Time Travel／prior Worker checkpoint。所有 mutation 步驟 skipped。

**最新 development deployment 為 [#12](https://github.com/simonliu1118-byte/CYapps/actions/runs/38077157550)**，commit 38163fcc8b755d5a9af97773a86c82f54e052ede，tree 與 82080dfa 相同。原部署 branch fast-forward，沒有 force push；preflight 再次通過後只套用 0013，再部署同一 Worker／D1。after 通過 Cloud **0.9.2／API 1／marker 8／storage 13**、runtime-sync capability、原 Workspace／Device Token hashes／employee／pairing／invitation／audit 歷史延續及 FK。**CYID disabled，仍用 Built-in；未改正式 provider、未切 authority、未建立 tag／Release。** 恢復 checkpoint timestamp：2026-10-10T18:47:23.452Z；D1 restore 不自動執行，先停寫並稽核 checkpoint 後的資料。

CYID development 已在 #124 升級並驗證 **0.3.6**；named RPC source 已部署，但實際綁定及正向權限驗收仍待完成。原 development business target 不等於隔離 CYID sandbox。CY Web main 七個 manifest mirror 與 canonical 1.0.3 逐 byte 一致；declared consumer 1.0.2 仍在支援窗內。這不代替 Employee／App Access／Role／verified Email、核准的兩 Workspace 配對或正式 scope 稽核。

接續：保留現有 provider 設定／Secrets 的更新流程與隔離 staging 資源／用量核對，確認批准的 Application／兩 Workspace 配對及 private IDENTITY／IDENTITY_AUTHORITY，完成實際 gateway 授權／invalidation；再做 Windows RC AA／AB／AC 與原 A/B/C。正式切換方案另列原 D1／Device continuity、備份、舊 client／回復 gate；不能用重新加入／reset 修復錯綁定。Windows 可先以原 Built-in 安裝測新同步與一般回歸，真實 UI、DPAPI、kill／檔案占用及人工操作仍不能由 Linux 或 CI 冒充。

### V2.6.17 source checkpoint — 2026-10-10

本批實作取代先前設計-only checkpoint（歷史見 Git）。ServiceConnectivity／MainForm 沿用原 lifecycle，POST runtime/sync 合併 Device＋權限，60 秒完整同步、15 秒 liveness、故障後重連立即同步；不改五分鐘光貿 invoice sync。CYID private named RPC 只 invalidation，不登入／不建立 Session／不輸出 verifier。Built-in snapshot revision 與 rows 同次讀取，local revision 防倒退；known deny 不因後續 503 復活。

LocalResetCoordinator 同裝置 explicit revoked 先存 durable marker；MainForm 停止工作退出，新程序等待 parent 結束才 recover。Program 每 portable folder 單程序 guard，清除前不 Open repository；保留 package-files.json 的原發行檔案，Data／Cache／Logs／root runtime／其他新目錄全清，marker 最後移除；partial wipe／lock／kill 留 marker 阻擋直到續清。一般401／403、Workspace disabled、scope mismatch、503不自動 wipe。手動 Local reset 的原 Logs 保留契約不變。系統 temp 共享 Preview 不列入 portable reset；原 finally 清理仍保留，不能任意刪 portable 外檔案。

人工结案／折讓及 Built-in 中央 mutation dialog 只收輸入，最終 core／server 完整 Device＋Employee 驗證。設定讀取／儲存是分開的受保護操作，儲存重驗且拒絕舊 provider binding 覆寫。OTP 多階段每階段保留當次驗證，沒有可重用 Session。CYID 1.0.2/1.0.3 同scope快取接續，不改 Workspace／Device／Token／資料。

版本 CYInvoice 2.6.17／BUILD 2、Cloud 0.9.2、CYID provider 0.3.6／BUILD 0、canonical 1.0.3／minimum 1.0.0，Consumer Impact BACKWARD_COMPATIBLE。新 private IDENTITY_AUTHORITY binding 配置示例是 placeholder，尚未部署。TODO §7.3.2 source 項與 RC AC／原 RC AA、AB／staging／cutover 分開；正式 Release 未授權。

本地證據：aggregate single-request／60秒節流／重連、原連線矩陣、malformed／scope／Workspace-disabled不誤wipe、explicit revoke durable marker、模擬程序中斷後 partial recovery 與 Logs 清除；real-provider D1 invalidation（停用／Role／App Access／credential／deleted ID）及CYID outage＋Device revoke。完整八套 C# 回歸、CYID 44 項測試、Worker TypeScript／完整測試／bundle dry-run、tracked-source public scan 通過；Linux WinForms 編譯 0 errors（既有 WebView2 WPF MSB3277 warning）。Windows 原生 smoke／DPAPI／package／精確 head Artifact 於 CI 核對，不引用V2.6.16綠燈。

本輪 [#380 checks](https://github.com/simonliu1118-byte/CYapps/pull/380/checks) 是最新 CI 結果入口；在該精確 head 的 Windows Build 成功後，從 run 的 Artifacts 取得 V2.6.17 工程包（保留 3 天）。不以此文件的歷史 run 代替最新版驗收。

### V2.6.16 source 快照（由 V2.6.17 取代；歷史證據）

使用者 2026-10-10 授權開始 consumer 開發；新獨立 Patch／BUILD 0。整合 main 與兩支未合 PR 以保留最新介面和設計，不自動合併／關閉前置 PR。前置 PR #216 精確 head 1e6d4137 的 Governance #1172／Cloud #378／Windows #267 全綠只是 V2.6.14 證據，不能冒充本輪 CI。

本輪完成 CyIdIdentityProvider、集中選擇及 authenticated discovery／DPAPI binding、private IDENTITY gateway、Device 與 Employee／App Access／Role 分離、兩 Workspace 固定綁定、原有降級 cache／reconnect 更新（V2.6.16 納入暫時 503）。Rename／revoke／pairing／invitation 保留原 handler 及 audit owner；0013 新增 external invitation actor 並保留 Built-in FK／歷史，沒有匯入或讀 CYID D1。帳號管理隱藏／復原提示 CY Web；CYID 不讀 Built-in employee snapshot。詳見 CY_ID_INTEGRATION §14。

Source Cloud 0.9.1／API 1／marker 8／storage 13／cyid-consumer-v1；預設 CYID 關閉，runtime binding 與實際 Application enablement 未核對。本輪沒有遠端 migration、部署、正式切換、tag／Release；目前最後有效 development deployment 仍是下方 Cloud 0.8.9／storage 12 Run #8 attempt 2。

本機驗證：TypeScript、完整 npm test（bootstrap／lifecycle／真實 CYID provider，各自 synthetic DB 與 migrations）與 bundle dry-run 通過；C# Core Release build 與 Cloud contracts 通過，Void workflow 29／Allowance 12／SQLite migration 13 通過。WinForms Linux cross-build 只作編譯預檢，Windows-only smoke／DPAPI／PE／封裝仍由本輪 PR CI 驗證，不引用舊包。新增 Windows join smoke 包含 CYID invitation／pairing／mismatched binding，確認不下載 Built-in verifier。

Architecture Exception：依使用者保留既有 last-trusted offline 行為，整筆 scope／principal／本機 proof DPAPI 保護，沒有新增 TTL；離線無法即時觀察中央撤銷，server mutation 仍在線驗證。Logout loss 不 replay、不假稱已撤銷，provider expiry 負責 orphaned Session（目前 default 8h）。每個 server mutation 前 Resolve；CYID／consumer D1 不構成跨庫 atomic transaction。Local／Built-in 是獨立正式模式，沒有在 CYID 失敗時自動啟用。Rolling-deployment 404 discovery reader 只供未確認 CYID 的舊 Built-in Worker。

剩餘人工／切換驗收只在 TODO §7.3、RC AA／AB 追蹤：Windows 實機及舊 A/B/C、staging bindings、受控 migration／actor audit／rollback。0-active-Device recovery 未做，LAST_ACTIVE_DEVICE 保護維持；125／150 DPI 仍 Deferred。

2026-10-10 使用者補充：切 CYID 必須在原 CYInvoice Workspace 接續工作。既有 Confirm binding 路徑只更新 authority，拒絕不同 Workspace；本輪補 source continuity regression（兩台原 Device Token、consumer 歷史 rows、本機 SQLite／settings 切換後重開及 pending／PDF／protected credentials 保留），不新增 runtime path／schema／版本。切換前所有 active Windows 升級、CYID accounts/access/roles/email 與原裝置 staging 業務驗收未完成時維持 Built-in；禁止重建／重新加入／reset 代替接續。詳見 integration §14.5、TODO 7.3、RC AA。

2026-10-10 使用者最終定案（本輪新功能 V2.6.16／BUILD 0）：雲端連線失敗／timeout／暫時 503 且光貿正常，沿用原有降級單機；帳密錯誤／權限拒絕／Device 撤銷不降級。光貿不可達僅 Cloud 可用時停用光貿功能；雙斷線一個 modal 阻擋全部業務，MainForm 15 秒檢查／手動重查，任一恢復回對應狀態。純 Local 只依光貿正常／阻擋至恢復。保留 Workspace、Device／Token、資料及輸入，不 reset、不自動重送。光貿 guards 在業務請求與本機變更前；known Device denial 清身分快取，後續 503 不能復活。Cloud 0.9.1 discovery 經原 private IDENTITY 呼叫 canonical health，不新增 authority、schema、Session 或 migration。原 Workspace continuity 與前置介面回歸保留。矩陣以 integration §14.2 描述。

本輪本機驗證與後續 CI：八套 C# business／Cloud regressions、TypeScript／Worker tests 及 Linux WinForms 編譯預檢；Windows 原生阻擋／恢復 smoke、DPAPI、封裝／public safety scan 以本輪 #380 精確 head CI 為準。上一版 4930de4b 的 Windows #271／Cloud #382 全綠只證明 V2.6.15，不可替代本輪服務連線驗收；尚無 V2.6.16 人工／live staging 結果。

## V2.6.14 裝置管理

主視窗只管 active inventory／selection／rename／revoke；原 tabs 及新增流程已移除。獨立 CloudAddDeviceForm 是新增／ticket／timer／lifetime owner，保留同一 CloudClient、既有 pairing／invitation authorization 與重開狀態恢復。返回主窗重新載入 inventory；不建立第二套加入路徑。

主窗上方顯示 active 數量及原撤銷說明；四欄為裝置名稱／使用版本／加入時間／最後使用時間，撤銷與確認撤銷採共通 Danger。Cloud inventory API 保留歷史供管理查詢，Windows 只篩選 active。雲端不刪除 revoked Device，既有 self-reset／LAST_ACTIVE_DEVICE 不變。

MainForm 啟動在既有 lifetime 下回報自己的 VERSION／BUILD；POST /v1/devices/usage 只依 token 身分更新本 Device，server UTC 記錄最後使用。離線不阻擋正常工作、不修改原 authority/cache；舊版或未成功回報資料保留，不宣稱 heartbeat 或目前在線。POST /v1/devices/rename 經中央 SUPER_ADMIN 執行時驗證，同一 Workspace active Device、actor／workspace／credential 在 mutation 交易再檢查，並與 device_renamed audit 原子保存。0012 forward migration 只擴充 audit vocabulary，保留歷史；沒有新增 Device metadata table。

本機 TypeScript、bootstrap SQL、實際 lifecycle handler／SQL 回歸及 bundle dry-run 已通過。回歸含 own-device 更新、server time、空白／控制字元／長度、撤銷／disabled Workspace、credential race、audit failure rollback 及歷史保留；C# client 增加 wire contract／錯誤回傳拒絕，Windows smoke 增加四欄／active filtering／最後一台防護／新增視窗標籤測量。Windows CI 與工程包結果以 PR 精確 head 為準；實機 RC Z、125／150 DPI 尚未取得證據。

Development staged [Run #8 attempt 2](https://github.com/simonliu1118-byte/CYapps/actions/runs/37983363509) 已成功；deploy commit 3b7325ea 與功能 source db1a3b95 的 tree 相同。先 capture bookmark／aggregate audit，0012 一次套用；Device 總量及 active／revoked 分組前後保持，仍存在 revoked 歷史，FK 無異常。第一次 immediate health 尚讀到 0.8.8；完全相同 source 重跑後無 pending migration，health 已驗證 Cloud 0.8.9／API 1／marker 8／storage 12／storage ok 及 usage／rename capabilities。未重跑 0010／0011，未操作真人裝置名稱或憑證。

### V2.6.14 自動化與工程包

功能 source **db1a3b9560510a94400ffb308a1536a3a1ef8754**：

- [Governance #1171 通過](https://github.com/simonliu1118-byte/CYapps/actions/runs/37982871612)。
- [Cloud #377 通過](https://github.com/simonliu1118-byte/CYapps/actions/runs/37982871621)，含 lifecycle SQL／migration／Linux 及 Windows client contracts。
- [Windows #266 通過](https://github.com/simonliu1118-byte/CYapps/actions/runs/37982871637)，含編譯、全部回歸、startup／packaged smoke、PE／VERSION／BUILD 及 public-package scan。
- 當輪 [V2.6.14 工程包](https://github.com/simonliu1118-byte/CYapps/actions/runs/37982871637/artifacts/11642360855)，SHA-256 4f150a845bb8eaa119e9ab5a8d348dfa278e12d814e1393aab3e4c6a592052c6，原 Artifact 至 2026-10-13 03:53（Asia/Taipei）。

本次收尾只更新部署／CI 證據文件，不推進 VERSION／BUILD；下載最新版及精確 head checks 以 PR #216 為準。功能 source 的 CI 不冒充後續文件 head 已完成檢查。

## V2.6.13 歷史介面停點（版本快照已由 V2.6.14 取代）

共用 RoundedButton 取代舊無焦點點線 Button 及重複品牌／Primary／Danger renderer，保留 native Button 的 action／DialogResult／keyboard／accessibility owner，PDF 版型只自訂顏色，商品列刪除共用同一 surface。使用者要求所有方形按鈕圓角，屬本次 Secondary 外觀例外，不變更共通永久規則。

SyncIssuesForm 是兩清單的唯一 state／height owner，重用 NativeListViewHost 的原生列高與捲軸，零筆顯示無資料、依列數縮放最多十二列。InvoiceOperationHistoryControl 重用同一 host，RecordDetailForm 分配固定五列，其餘給發票資訊；刪除原有 Buffered／History ListView 重複類別與 header cursor。

版本 owner 收斂為 ApplicationVersion（VERSION／BUILD）；Cloud 加入 client 改傳可讀版本，Device Management 明確標示加入時快照。舊 +SHA 去除，舊 Build 不補猜。Cloud API／schema 不變，不提供 heartbeat 或當前版本刷新。

最新 CI／engineering Artifact 以 [PR #216 checks](https://github.com/simonliu1118-byte/CYapps/pull/216/checks) 精確 head 為準；V2.6.12 Windows #260 通過是上一版證據，不能當新介面驗收。自動 smoke 覆蓋動態列數、固定五列原生捲軸、鍵盤／disabled／圖片與版本身分；實機仍待 RC Y，125／150 DPI 尚未驗收。

## 2. V2.6.11 已完成的變更

「上傳問題」旁新增「處理中」按鈕與數量，兩者使用同一清單／雙擊明細實作。

| 分類 | 顯示內容 |
| --- | --- |
| 處理中 | 正常官方上傳狀態 1／2／3／31／32、光貿明確確認處理中的作廢、人工操作完成且正常等待官方確認的折讓 |
| 上傳問題 | 查詢／同步／解析失敗、結果不明、開立失敗、尚需人工操作的作廢／折讓／折讓作廢、折讓候選衝突或金額不符 |
| 完成 | 從處理中移除；歷史已解決 issue 仍依既有流程保留 |

`InvoiceWorkQueue` 只投影目前 invoice metadata／workflow／sync issues，不保存第二套 pending store，不新增同步或重送路徑。真正 unresolved 技術問題優先於可能過時的正常等待狀態。兩清單限目前公司／環境；舊折讓沒有明確分類欄位時先留上傳問題，正常回查後再分類。

處理中不能勾選刪除或手動標成功。超過兩期的作廢／折讓仍可由 ADMIN／SUPER_ADMIN 經既有核心結案；只停止本機追蹤，不能宣稱官方成功。已修正舊行政結案測試依賴 void_pending_confirmation 必須出現在 active issue 的過時契約；一般 QueryFailed 不能藉結案清掉業務 pending。

保留原 PR 修正：作廢確認移除黑色號碼遮罩但文字／預覽仍隱藏，等待作廢使用半形括號，折讓「已人工處理」使用共通 Danger button。

## 3. V2.6.12 邀請加入停點

舊包第一次 claim 已讓 Cloud 建立 Device 並使用邀請；password.Clear 觸發 TextChanged → ResetAuthorization，把 preview 清空，下一行讀取 preview.WorkspaceId 發生空物件錯誤。再次 preview 已使用邀請因而 unavailable；關閉再開沿用 protected Pending Token 找回原 Device，使用者已回報成功。

修正由 CloudDirectJoinForm 保留每次操作已確認的 Workspace，仍清密碼與使可編輯授權失效，保留原本 Device／authority／snapshot 與 protected persistence 驗證。State／render／lifecycle／mutation owner 仍為同一 Form 與既有 client/store；不新增 retry／第二加入路徑／第二 authority。HTTP transport 與原生通知僅在 offline smoke fixture 替換，正常程式仍使用同一流程。

首次說明改為左對齊兩點並於語句邊界折行；Windows startup smoke 覆蓋邀請首次完成、配對完成、Workspace mismatch、claim 後 snapshot 中斷及重開復原、修改輸入後禁止未確認 claim。修正版實機首次成功仍需 RC X；舊包重開成功不視為修正版已驗收。

目前 V2.6.12 精確 source CI／engineering Artifact 請看 [PR #216 checks](https://github.com/simonliu1118-byte/CYapps/pull/216/checks)，不得使用下段 V2.6.11 舊包驗收此修正。未改 Cloud source，不需為本次 Windows UI 修正部署 Worker／migration。

## 4. 先前 V2.6.11 自動化證據與測試包

以下全部屬功能 commit 66ec3671，不是 Windows 人工／AMEGO live 驗收：

| Check | 結果／連結 |
| --- | --- |
| Governance Check #1137 | [通過](https://github.com/simonliu1118-byte/CYapps/actions/runs/37916154874) |
| CYInvoice Cloud Check #369 | [通過](https://github.com/simonliu1118-byte/CYapps/actions/runs/37916154522) |
| CYInvoice Windows Build #258 | [通過](https://github.com/simonliu1118-byte/CYapps/actions/runs/37916154524) |

Windows 已完成 warnings-as-errors build、startup smoke、Core parity 62／Void 13／Employee void workflow 29／Allowance 12／SQLite 13／Sync 15／SyncCoordinator 15 項回歸、x64 package、PE／layout、packaged startup smoke 與 public-package safety scan。Cloud check 已完成 Worker／D1 local migration／contract 與 Windows Cloud contract／package 驗證。

Windows Artifact：[CYInvoice_V2.6.11_Build2_engineering-run258](https://github.com/simonliu1118-byte/CYapps/actions/runs/37916154524/artifacts/11608879449)。Actions 保留 3 天；到期時間 **2026-10-12 18:15（Asia/Taipei）**。下載 ZIP SHA-256：`b08fb736fecb91a12361a20b11d41a8de6d03bc0b4cb29c0cbb25bb70dfc6158`。這包出自功能 commit，尚未包含本次文件整理；可執行程式與本次文件整理後的 source 相同。

## 5. Cloud 工程與遠端部署的界線

Reference backend source：Cloud **0.8.9**／API **1**／legacy compatibility schemaVersion=8／actual storage Schema **12**；forward migrations 0001～0012。不可把 compatibility marker 與 storage migration progress 當同一欄位。

最新 development 遠端證據為本文件上方 2026-10-11 [Run #12](https://github.com/simonliu1118-byte/CYapps/actions/runs/38077157550)：Cloud 0.9.2／storage 13／CYID disabled。先前 Run #8 attempt 2／Cloud 0.8.9／storage 12 保留 V2.6.14 歷史；9/29 Run #7／Cloud 0.8.8／storage 11 為 [歷史快照](NEXT_CHAT_HANDOFF_2026-09-29.md)。

以下四包已合併進 main，不再列為未實作：

1. V2.6.7：AppPrincipal／AppRole／IIdentityProvider、Local／Built-in provider、集中選擇，正式 role 為 SUPER_ADMIN / ADMIN / USER；Local／cache 與 Cloud forward migration 已處理舊 role。
2. V2.6.8：Online operation 取最新中央 credential／role／enabled；只有真正 transport outage／timeout 可用最後可信 protected cache。HTTP 拒絕、revoked Device、malformed／Workspace mismatch／caller cancellation fail closed。
3. V2.6.9：Device inventory／revoke／history／audit 與 Windows 裝置管理；最後一台 Built-in active Device 保留 LAST_ACTIVE_DEVICE。
4. V2.6.10：Cloud → Local 雙重確認、UI／同步停止後 revoke／self-status 確認再清目前安裝的 Data／Cache／identity；不明結果保留資料／Token，下一次啟動恢復。Windows 不停用／刪除中央 Workspace。

2026-09-28～29 的 A 機連線與 B 機 Run255 配對加入已有人工作業證據；**最新版 A/B authority freshness 與 A/B/C revoke／reset 尚未取得人工驗收結果**。Run229／255／343 僅為歷史驗證編號，舊 Actions Artifact 已逾保留期，不能當目前下載包。

## 6. CY ID 與後續實作停點

CYID 已有 canonical [Consumer Integration Standard](../../CYCloudIdentity/docs/CONSUMER_INTEGRATION_STANDARD.md)，contract **1.0.2**、minimum **1.0.0**。不再以「尚未發布共同 contract」作為唯一等待理由。同 repo 直接讀 canonical files，不複製另一份 shared contract。

V2.6.15 source 已實作 CyIdIdentityProvider、Workspace binding、CYID App Access／Session 與 Windows offline 接線；實機／staging／正式切換尚待驗收，0-active-Device recovery 未實作。具體限制見 §14；不把 Web Session／HttpOnly-cookie 語意直接套進 WinForms，不自訂第二套 CYID role／access／credential contract。帳號管理 visibility 與 business／Device ownership 依 [CY_ID_INTEGRATION.md](CY_ID_INTEGRATION.md)。

## 7. 接續工作順序

唯一可勾選的進度表為 [TODO.md](TODO.md)。先驗收 RC X 的邀請首次完成／說明，再驗收 RC W 的分流與舊結案，再做 Q／R 的 A/B 基線／即時權限，之後用可拋棄 C 做 S／T／U 的撤銷、重置與不明結果恢復。再處理 invitation／Employee identity matrix／OTP／transfer 的跨機驗收。

CYID Consumer 後續是 staging／實機及受控切換；尚未實作的主線為 all-device-loss recovery、Cloud Work Item 原子轉移／revision、多機離線 OrderID 防撞、audit viewer、正式折讓 API 與自架手冊。Offline cache 完整性簽章、125%／150% DPI、多公司與營運摘要保持原定延後範圍，不自動升為本次阻塞。

正式發布仍依三層規則及使用者當次明確 release 指示處理；PR／CI／文件更新不代表完成正式發布。

### 本輪治理前置與自動化證據

初次 #380 source head `0b7e343287860de1778bb0f101a26e4d28ae0d0e` 的 [Cloud #379](https://github.com/simonliu1118-byte/CYapps/actions/runs/38038565641) 通過；[Windows #268](https://github.com/simonliu1118-byte/CYapps/actions/runs/38038565637) 的 warnings-as-errors build／startup smoke／全部 business regressions 通過，完整 package 結果仍以 run 本身為準。Governance #1173 指出 PROJECT_RULES 缺 canonical consumer adoption 引用；依治理規則另建 [#381](https://github.com/simonliu1118-byte/CYapps/pull/381)，[Governance #1174](https://github.com/simonliu1118-byte/CYapps/actions/runs/38038662600) 通過後合併 main，GOVERNANCE_VERSION 2.3.34。僅採用既有 canonical standard，不新增 shared identity 語意、不停用檢查。

#380 同步 main adoption 後重新跑精確 head 全部 CI；功能 code 與 0b7e3432 相同，新增差異只有治理同步／狀態文件。最終結果與最新版工程包依上方 #380 checks，不以初次失敗 run 代替最終驗收。正式 deployment 仍未更動。


## V2.6.17 Build 2 CI 收斂

Build 0 source 187f2b5b 已通過 Windows warnings-as-errors／startup smoke／各業務測試／manifest package／packaged smoke；最後 upload 結果仍以 run 38063846171 核對。CYID check／兩個 deployment workflows 的 PR-only validate 通過，沒有執行 deployment。Governance 1182 因 PR Consumer Impact 使用 Markdown 粗體而未匹配純文字格式，已修 PR body；不改治理規則。

Build 1 補舊 Gateway 404 的明確升級診斷及 unknown token 不清除 regression。源頭未改原連線矩陣，仍沿用原降級；先以 CYID disabled 在原 Workspace 升級 additive Cloud 0.9.2，再升級 Windows，private RPC／原裝置 staging 之後才談切換。CI／工程包核對最後 source head，不拿較早 Build 0 綠燈替代。本批尚未合併／部署／正式切换／Release。

Build 2 補最後撤銷邊界：aggregate 共用原 self-status parser（含 revokedAt／合法狀態），缺完整證據不清除；手動自己撤銷／已撤銷退出同樣只落 authorized marker，等 fresh parent-exit recovery 清除包含 Logs，避免舊程序背景工作在 wipe 後寫回。手動純 Local／Workspace disabled 保留原不同契約。新增真实 SettingsStore＋既有 ExecuteAsync／RecoverPendingAsync 合成 HTTP 回歸，未部署／實機驗收。


## V2.6.17 Build 2 已驗證工程包

功能 source **64f233810bc5066db1a92f27e95552974a105a63** 六項 CI 全部通過：

- [Windows #275](https://github.com/simonliu1118-byte/CYapps/actions/runs/38064658727)：warnings-as-errors、原生／packaged startup smoke、業務 regressions、manifest／PE／封裝及 public-package scan。
- [Cloud #386](https://github.com/simonliu1118-byte/CYapps/actions/runs/38064658712)：Worker／real-provider synthetic D1、Linux／Windows contracts 與 engineering package scan。
- [Governance #1184](https://github.com/simonliu1118-byte/CYapps/actions/runs/38064658742)。
- [CYID Check #105](https://github.com/simonliu1118-byte/CYapps/actions/runs/38064658743)。
- [Development workflow #116](https://github.com/simonliu1118-byte/CYapps/actions/runs/38064658721)／[Production workflow #11](https://github.com/simonliu1118-byte/CYapps/actions/runs/38064658719) 只完成 PR validate，部署 job skipped；沒有遠端部署。

[下載 V2.6.17 Build 2 engineering](https://github.com/simonliu1118-byte/CYapps/actions/runs/38064658727/artifacts/11674241804)，81,087,634 bytes；Artifact archive SHA-256 `368ff2aab2a99885f43e291e6a53d828bab3f3d08ae6b8cdd45d1a527974d9b9`，有效至 **2026-10-13 23:45 Asia/Taipei**。Artifact head 已核對等於功能 source。

升级原可攜資料夾時覆蓋整包的程式檔，包含 package-files.json／Runtime；保留原 Data／Cache／Logs 與原 Workspace。不要僅換 EXE 而漏 manifest。雲端目前仍未部署 additive 0.9.2／private RPC；新同步在隔離 staging 更新 Gateway 後才可驗收。

本次收尾只補 CI／Artifact 證據文件，不推進 VERSION／BUILD。後續純文件 head 不冒充本段功能 source 已測；最新 PR checks／engineering artifact 為即時入口。未完成：RC AA／AB／AC 與前置 A/B/C 實機、隔離 binding／帳號／App Access／Workspace continuity／用量量測、正式切換 gate、0-Device recovery 等原 TODO；未合併／部署／正式 Release。


### 共通 Variable 退場已就緒 — 2026-10-12

#414 證據文件 source e0050ac3／tree97ec5b3f 七 CI 通過、合併 main7ac06e7a。相同已測 tree 同步至部署入口；Provider development #153（38192970528）原0.3.6／authority metadata／history／runtime vars與Secrets保留，Provider唯讀 #154（38193349461）通過。Invoice #31（38192965762）在 Provider 正切換 active version 時，首個穩定性核對於upload前阻擋（ACTIVE_DEPLOYMENT_CHANGED）；after回CYID_PROVIDER_CHANGED，沒有新增版本上傳／部署。待Provider完成後重新取快照的Invoice #32（38193198316）成功：原Cloud0.9.2／API1／marker8／storage13、1 Workspace／4 Device records／Token hashes／history／FK／routes及Builtin保留，exact-active inheritance／uploaded與active readback／100% real-business部署通過，CYID false。Expired undeployed no-D1 probe仍1個，未上傳測試程式、重設Secret或restore。這是code/config入口同步，不是CYID authority切換。

已read-back核對官方active與retired refs均改讀Secret：Invoice deploy e796909e、preflight ea0d55a3；Provider development a0209a16、preflight f22978e9；retired production b3184e15；retired Invoice binding-version910dd5b5／cleanup7c6e9df3／Provider probe3849fb80。retired production #14（38192977888）只執行無Secrets的retirement error gate，沒有重新provision；三個retired probe/cleanup refs無push runtime入口，不重跑歷史runs。main四個deployment owners相同Secret來源。使用者已確認刪除 development 同名 CYID_WORKER_NAME Variable 並保留 Secret；刪除後唯讀 #34（38195315134）完整通過，原 Cloud／Workspace／Device／Builtin／FK 保留，無 upload／Identity D1 access。

Provider兩項Invoice scope仍未配置；中央registry/Workspace enablement尚未確認，不能就此啟用CYID。已核對CY Web main的帳號管理UI：超級管理員或Identity Admin於「帳號與權限 → 系統使用權」查看app清單；先只查看CYInvoice欄，不變更勾選，並核對該Portal的實際Provider/environment。不得把production清單直接當成development授權或缺metadata當成未註冊。

## Central registration rollout checkpoint — 2026-10-12

CYID #416 final head cbae06a0 / tree8fe98f7c passed all eight checks (including actual local Worker/D1 registration/auth and Windows), merged d3411e2. Development #158 (38197480766), continuity source3eac1a22 / same tested tree, successfully upgraded to0.3.7/contract1.0.4 while preserving original authority/history/settings/Secrets/seven migrations/FK. Private recovery reference UTC2026-10-12T02:52:53.940Z; generated private files removed. This was normal business code, no remote fixture/handler upload, bootstrap replay, registration/grant or production change.

CY Web #146 head8aad63d9 / treea02fe9d4 passed Governance244, Runtime160 and deployment contract137, merged2dfd374e. Actual development #138 (38197908123), continuity source5325ef77 / same tested tree, serves0.7.23/consumer1.0.4; canonical and workers.dev health/source/D1 and invalid Session acceptance passed. Existing sync script synchronized all seven canonical artifacts. The new Super Admin registration UI is available at the existing development account portal.

Actual approved CYInvoice registration/enablement and Employee App Access, Provider runtime scope metadata, private bindings, original Windows acceptance and separately approved cutover remain pending. The agent browser has no authenticated Session; an authorized existing Super Admin must execute the current-Workspace registration through the normal UI. No Invoice Workspace, Device Token, business data or CYID flag changed.
