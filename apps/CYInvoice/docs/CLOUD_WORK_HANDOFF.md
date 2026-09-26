# CYInvoice Cloud Work Handoff

更新日期：2026-09-26

## 接手摘要（以此節為目前狀態；下方舊階段紀錄保留歷史脈絡）

- Repository：`simonliu1118-byte/CYapps`。目前工作為 Draft [PR #121](https://github.com/simonliu1118-byte/CYapps/pull/121)，branch `cyinvoice/feat-cloud-security-audit`，疊在 PR #100 branch `cyinvoice/fix-v264-build2-ui-review-details` 上；PR #100 再承接 PR #73。接手時先讀 Git 上最新 head、三層永久規則與本文件，不以舊階段的版本／待辦敘述覆蓋此節。
- PR #121 最新工程原始碼：**CYInvoice V2.6.6 Build 4**、Cloud `0.8.5`／API `1`／D1 migration `0009`（source storage Schema `9`）。Build 4 修正 Cloud 跨版本相容策略；CI Run #36039794681 / workflow Run #255 的 `validate`、Windows x64 build、WinForms startup smoke、Cloud contract tests、跨版本 regression 與工程包產出均成功。Artifact：`CYInvoice_cloud-foundation_engineering-run255`，SHA-256 `5ba818446d357e054dbefe13b04bca2cb6ba6f43637fbc85ccfe431bebc1ea43`。**尚未執行 Cloudflare remote migration／Worker 部署、merge 或正式 release。**
- A 機恢復與向下相容驗收均已實機通過。第一階段：原 A 機、原 Windows 使用者帳戶、原 `Data` 搭配 PR #100 V2.6.5 Build 4（Run #36015789588，Artifact `CYInvoice_V2.6.5_Build4_engineering-run229`）可正常重新連回 development Cloud。第二階段：同一 A 機以原 `Data` 搭配 PR #121 **V2.6.6 Build 4 / Run255**，也可直接連目前仍是 Schema 8 的 development Cloud，沒有 Schema incompatibility，也不需重建 Workspace／Device。
- **Run229 暫時保留作 rollback 基準，不刪除。** 建議獨立保存整個 Run229 測試資料夾與其中已驗證可用的 `Data`。等 migration `0009` + Worker `0.8.5` 上線後，A 機以 Run255 完成既有功能與重新啟動驗收，必要時再用 Run229 驗證 legacy API 1 正常連線；全部通過後才可刪除 Run229 備份。
- development 遠端目前仍以先前唯讀查核為準：1 個 Workspace、1 台 active A Device、Cloud Employee authority 已完成；遠端 D1 為 Schema 8。PR #121 的 migration `0009` 尚未套用。不要重新 bootstrap、不要清空 D1、不要重新建立 Workspace。
- **版本相容策略已修正並完成 A 機實測**：Windows `CloudCompatibility.Problem()` 不再用 D1 migration 編號完全相等作硬性 gate，仍嚴格檢查 CYInvoice service、storage readiness 與 API version。Worker `/v1/health` 在 API 1 保留 `schemaVersion=8` 作為已發布 V2.6.5 Build 4 的 legacy compatibility marker，實際 D1 migration 另以 `storageSchemaVersion` 回報，並附 capabilities／minimum client metadata。Run255 對目前 remote Schema 8 的實機成功，已證明「新 Client + 舊 Worker／Schema」的向下相容路徑可用。
- PR #121 曾在 `apiVersion=1` 下移除舊 `/v1/direct-join/*` 並改變配對授權 request shape；因此不能只放寬 Schema 檢查。Build 4 修正後，新 Worker 對已淘汰的 Workspace-ID direct join 與缺少現行超管認證的舊配對核發 shape 明確回 `CLIENT_UPDATE_REQUIRED`，不默默 404、也不降低認證要求；既有 Device 的 API 1 正常操作仍保持可用。跨版本測試同時保護「新 Client + 舊 API 1 storage schema」與「舊 Build 4 + 新 Worker health compatibility」。
- migration `0009` 為 additive migration：只替 `devices` 增加 nullable `invitation_id`，並新增 `device_invitations`、`security_audit_events` 與索引，沒有 drop／rename／改寫既有核心資料。本輪 SQLite 全 migration 驗證已成功；remote migration 與 Worker 上線仍必須分階段進行。
- **目前 deployment gate 已開啟，下一步由具 Cloudflare MCP 的本機 Codex 執行受控 development 部署。** 順序固定：① 先做 deployment 前唯讀查核，確認仍為既有 1 Workspace／1 active A Device、Employee authority 正常、remote migration 僅到 `0008`／Schema 8，並保留可回復的 pre-deploy 狀態；② **只套 migration `0009`，先不要部署 Worker**；③ migration 後唯讀核對既有 Workspace／Device／Employee 筆數與關聯不變，A 機 Run255 再開啟驗證既有 Cloud 功能；④ 通過後才部署 Worker `0.8.5`；⑤ 驗證 `/v1/health` 為 API 1、legacy compatibility `schemaVersion=8`、實際 `storageSchemaVersion=9`，並確認 capabilities／minimum client metadata；⑥ A 機 Run255 再驗既有 Workspace／Employee／發票相關操作與重新啟動；⑦ B 機可用時再驗收配對碼、邀請碼、撤銷／重寄、加入狀態及結果不明恢復。任何一步失敗立即停止後續 deployment，不用 fallback 特例繞過正式 authority model。
- 本輪只允許 development remote migration／Worker 驗證，不 merge PR、不 tag、不正式 Release。受控災難復原與安全操作紀錄查看 UI 仍是後續 TODO。

## 2026-09-25 新裝置加入設計決議

後續正式加入方式只有「配對碼」與「邀請碼」；現有「Workspace 識別碼＋超管」直接加入須移除。程式不內嵌 Cloud API 網址，Workspace 識別碼也不提供使用者手動輸入。A 機「新增雲端裝置」提供立即配對（顯示 API 網址與約 10 分鐘一次性配對碼）及寄送新裝置邀請（寄送 API 網址與 72 小時一次性邀請碼至超管已驗證 Email，可撤銷／重寄）。B 機使用配對碼，或使用邀請碼加超管帳密加入；邀請碼路徑不再寄第二封 Email 驗證碼。兩條路徑均先確認 Workspace 名稱，成功結果在 A 機視窗可查。

Cloud 安全操作紀錄涵蓋配對、邀請、撤銷與新機加入。紀錄不寫入配對碼、邀請碼、密碼、OTP 或 Device Token 原文。安全操作／稽核紀錄的查看介面列為後續版本 TODO。

目前工作分支已修改 Worker、D1 migration `0009`、Windows 加入視窗、用戶端與跨版本相容層，工程身分為 **CYInvoice V2.6.6 Build 4**、Cloud `0.8.5` / API `1` / storage Schema `9` source。Run #255 已完整通過並產生 `CYInvoice_cloud-foundation_engineering-run255`。A 機已完成 Run229 恢復驗證，且 **V2.6.6 Build 4 / Run255 對目前 Schema 8 development 的實機向下相容驗證也已通過**。本輪尚未部署 Cloudflare、合併或發版；下一步依本文件頂端的 staged deployment gate 執行。

2026-09-26 A 機相容性驗證：先前舊 Windows 客戶端因 Schema 7 vs remote Schema 8 被相容 gate 阻擋，不是 Device identity 遺失。使用者先以 PR #100 V2.6.5 Build 4 / Run229 搭配原 `Data` 成功恢復，再以 PR #121 V2.6.6 Build 4 / Run255 搭配同一份原 `Data` 成功連線 remote Schema 8。這表示 Client 端 schema hard gate 修正已達成預期；現在可進入 migration `0009` 與 Worker `0.8.5` 的分階段 development 部署。若未來所有 active Device 真正遺失且沒有有效邀請，現有兩條常規加入流程無法自行恢復，需另設受控災難復原流程。

## PR #100 最新進度

PR #100 已接到 PR #73 最新基準；目前工程原始碼為 CYInvoice V2.6.5 Build 4、Cloud `0.8.4` / API `1` / Schema `8`。雲端忘記密碼採員工編號與 Email 核對後寄送、第二步輸入 OTP 與新密碼，重寄倒數使用伺服器回傳時間。一般員工的帳號管理提供本人密碼與 Email 異動，姓名仍由管理員維護。PR CI Run #36015425647 的 Cloud validate 與 Windows client 均成功；完整 Windows Build Run #36015789588 也成功並產生 `CYInvoice_V2.6.5_Build4_engineering-run229` 測試包。此版本未部署到 Cloudflare；不得把本段當成遠端已上線狀態。

## 歷史階段紀錄：首次開啟直接加入雲端

此段描述當時 PR #73 的歷史設計，**Workspace ID＋超管直接加入已在 PR #121 移除，不能依下文重新實作或部署**。首次開啟仍先選「使用單機版」或「直接加入雲端」；現行新機加入方式以本文件頂端決議的配對碼／邀請碼為準。

本分支新增 Windows 首次開啟／直連 UI 與 Cloud 0.8.3 端點；PR #73 的 Cloud Check Run #238 已通過 Cloud type check、D1 migration、Windows build、啟動 smoke、Cloud client contract tests 與工程包上傳。測試包為 **CYInvoice V2.6.5 Build 1**，Artifact `CYInvoice_cloud-foundation_engineering-run238`，SHA-256 `b9db3a964e4ac200e8a8431486808c982588f54f1ac0693128c1f188d082e37e`。**Cloud 0.8.3 尚未部署，兩種新機直連也尚未實機驗收**。並行開發已在同一 PR 加入 Cloud migration `0008` 的密碼復原功能，故此 Windows 新版要求 Schema `8`。development Worker 的即時版本和 D1 migration 狀態仍須由本機 Cloudflare MCP 唯讀查核。新機直連要求既有 Workspace 已完成第一台的中央 Employee cutover；若尚未完成，API 拒絕加入，不重建 Workspace 或另立本機超管。先完成原交接文件中的 D1 唯讀查核與 A 機帳號轉換，再由本機已連線的 Cloudflare MCP 部署經驗證的後端。請勿把本段工程 source 狀態當作已部署狀態。

此文件供下一個長時間工作階段／ChatGPT Work 接手 CYInvoice V3 Cloud identity stage。它只描述目前狀態與下一步，不是永久規則來源；永久規則仍依 `REPOSITORY_RULES.md` → `REPO_POLICY.md` → `apps/CYInvoice/PROJECT_RULES.md`。

## 1. Git 基準

- Repository：`simonliu1118-byte/CYapps`
- 專案：`apps/CYInvoice/`
- Branch：`cyinvoice/cloud-onboarding-first-device`
- PR：#73 `CYInvoice cloud identity: Workspace, Device and central Employee authority`
- Base：`cyinvoice/cloud-foundation-d1`
- PR 狀態：Draft / Open / 未 merge
- 接手時應先重新讀取 PR #73 的最新 head；不要依本文件硬編碼 branch head。
- V2.6.4 Build 1 的 code-bearing head：`b6d0f1d8fa02d2fd182179c599208764d0320552`。
- Cloud Check Run #211 已成功，engineering 測試包已產生；development deploy Run #6 已成功。
- PR #100 最新 source compatibility：Cloud `0.8.4` / API `1` / Schema `8`；PR #73 最新為 Cloud `0.8.3` / Schema `8`，首次建立時的已驗證歷史版本為 Cloud `0.8.2` / Schema `7`。
- Forward migrations：`0001`～`0008`

禁止自行 merge、tag、Release、auto-merge；只有使用者明確授權後才可執行。

## 2. 已定案的 Employee authority 模型

CYInvoice 沒有程式啟動後持續登入的 Employee session。需要權限的操作在執行當下驗證 Employee No + Password。

- Local Mode：Local EmployeeStore 是唯一帳號 authority。
- Device 建立／加入 Workspace 後先進入 whole-device `CloudTransition`。
- 轉換時一次盤點全部既有 Local Employees。
- 全部 identity / Email / credential / conflict 都處理完成後才 cutover。
- Cutover 後 Cloud Employee 是唯一帳號 authority。
- Windows 只保存 Cloud Employee cache + protected offline credential verifier。
- Cloud Mode 斷網時仍是 Cloud Mode Offline，不切回舊 Local authority。
- Workspace-wide Employee mutations 全部 Online-only。

Identity matching：

- Employee No + Email 都不存在中央資料：驗證本人 Email後建立新 Cloud Employee。
- Employee No + Email 都命中同一 Employee：直接採用既有 Cloud Employee 資料與既有 role，不做 merge。
- Employee No only / Email only / 兩欄各撞不同 Employee：建立 pending conflict，由目前 Workspace SUPER_ADMIN 人工確認。
- Name 只作顯示，不作 identity authority。

第一台 X 成為唯一 Cloud `SUPER_ADMIN`。既有 Workspace 新機上的 Local SUPER_ADMIN Y 若是新中央 Employee，Cloud role 預設 `ADMIN`；若精確命中既有中央 Employee，保留既有 Cloud role。

## 3. 已完成的 source / UI / API foundation

目前 branch 已包含：

- First Workspace bootstrap + verified recovery Email OTP。
- Pending Device Token protected storage / retry-safe recovery。
- Existing Workspace Pairing Code Device Join。
- B 機進入 Pairing Code 前執行時驗證 Local ADMIN / SUPER_ADMIN。
- Whole-device Local → Cloud Employee Transition。
- Exact identity adoption / ambiguous conflict queue。
- `待確認帳號 N` conditional Account Management UI。
- Conflict resolution by current central SUPER_ADMIN。
- Cloud Employee cache / offline credential verifier foundation。
- Central Employee create / name / Email / role / enabled / password APIs and Windows clients。
- New Employee Email OTP；Email change verifies new Email before commit。
- Password plaintext never uploaded; Windows derives PBKDF2-SHA256 verifier。
- SUPER_ADMIN transfer：X password re-auth + X verified Email OTP + atomic X→ADMIN / Y→SUPER_ADMIN / Recovery Email→Y。
- Legacy single-account `/v1/employees/reconcile-local` mutation retired；30-minute import window retired。
- Schema compatibility aligned to 7。

PR #73 body、`CLOUD_ARCHITECTURE_STATUS.md`、`CLOUD_ROADMAP.md`、`TODO.md` 已更新成上述模型。

## 4. 最新驗證

Build 1 的 Run #211 已通過：

- TypeScript type check
- Worker dry-run bundle
- D1 migrations `0001`–`0007` local SQLite validation / invariants
- .NET Cloud contract tests
- Windows x64 build
- WinForms startup smoke
- Windows Cloud contract tests
- engineering package build/upload

2026-09-22 已完成 remote audit：development Worker Cloud 0.8.1 的 `/v1/health` 回覆 storage `ok`，D1 migrations 已到 Schema 7，Brevo bootstrap OTP 已實際寄達。第一次建立 Workspace 時 Worker INSERT SQL欄位和值數量不一致，D1 batch 回滾，卻誤回報 `WORKSPACE_ALREADY_INITIALIZED`；遠端唯讀查核確認 Workspace／Device／Employee／Pairing 筆數均為 0。V2.6.4 Build 1 / Cloud 0.8.2 修正 SQL、錯誤分類並新增直接執行正式 SQL 的 Schema 7 回歸測試。

2026-09-23 development deploy Run #6 已完成：Cloud 0.8.2 / API 1 / Schema 7 / storage `ok`；D1 無待套用 migration，部署前 Workspace／Device／Employee／Pairing 仍各為 0。這些是建立前的筆數，不可當作目前筆數。

同日使用者在 Windows V2.6.4 Build 1 重新寄送 OTP 並執行首次建立；CYInvoice 畫面回報第一個 Workspace 與 Device 建立成功，且 Device identity 驗證完成。此為 Windows client 收到的成功結果；**建立後尚未從 Cloudflare D1 獨立唯讀核對筆數與記錄，也未確認 whole-device Employee Transition / cutover 完成**。不要再次執行 bootstrap 或清除資料。

2026-09-24 本機 Codex 透過已連線的 Cloudflare MCP 唯讀查核 development D1：已有 1 個 Workspace、1 台 active Device、1 位已驗證且啟用的中央 SUPER_ADMIN；Device 與員工關聯有效，轉換待辦已完成且沒有未解決項目。使用者隨後於 A 機完成雲端帳號切換，回報目前運作正常；B 機尚未測試。Cloudflare MCP 的 HTTP fetch 對 workers.dev 回覆 403（requests to workers.dev are not allowed），所以本次未能從該工具獨立確認即時 `/v1/health`，不可沿用 2026-09-23 的 health 結果當作本次查核。不要再次 bootstrap 或清除資料。

Cloudflare API、Bindings、Builds、Observability 四個官方 MCP 端點已由使用者在本機 Codex 檢查為「已設定／已載入／連線成功／目前不需 OAuth 登入」；該檢查尚未讀取 `cyinvoice-cloud-dev` 的 Workspace／Device。網頁版 Work 對話沒有這四個工具，不能把本機設定檔已登記誤當作網頁對話可用。這次工作採網頁版為主；需要 Cloudflare 即時狀態時，由已連線的本機 Codex 讀本文件後執行限定範圍的唯讀查核，並把去識別結果帶回主要工作對話。

## 5. 歷史階段的 Work 優先順序（A 機首次建立／切換已完成）

### A. 建立後的遠端唯讀查核與 A 機帳號轉換

1. 本機 Codex 使用已連線的 Cloudflare MCP 唯讀查核 `cyinvoice-cloud-dev` 的 Worker、`/v1/health`、綁定的 development D1、migrations 與 Workspace／Device／Employee／Pairing 筆數；確認 Workspace 和首台 Device 記錄的存在及關聯。只回報必要的去識別摘要，不輸出 Email、Token、OTP、Bootstrap Key 或個資。
2. 若唯讀筆數／關聯符合首次建立結果，再由使用者在 Windows 查看「雲端帳號轉換」視窗目前顯示的 Local Employee 盤點與狀態；尚未確認前勿按「完成雲端切換」。
3. 依畫面進行 A 機 whole-device Employee Transition，逐一確認 Email／identity／credential／conflict，完成前不要宣稱 cutover 或中央 SUPER_ADMIN 已建立。
4. 若 D1 或 Windows 狀態不一致，保存錯誤碼與去識別結果先查原因；不要重複 bootstrap、清除資料或猜測遠端已完成。

不要把 client 成功訊息當作 D1 獨立查核或帳號轉換驗收。

### B. Email live test

Reference Email provider 是 Brevo。Runtime secrets 不進 GitHub source / PR / log：

- `BREVO_API_KEY`
- `EMAIL_FROM`
- `OTP_PEPPER`
- 既有 `BOOTSTRAP_KEY`

若需要使用者輸入 secret，只提供逐步操作，讓使用者自己在 Cloudflare / CLI secret prompt 輸入；不得要求使用者把 secret 貼到對話。

Development bootstrap OTP 寄信已確認；建立 Workspace 成功後再驗證 Employee Email / transfer challenge 的寄信與錯誤處理。

### C. A/B Windows end-to-end

依序驗證：

1. A 建立 Workspace + X transition + cutover。
2. B 以 Pairing Code 加入。
3. B 全 Local Employee transition：new / exact / partial / divergent 五種 identity matrix。
4. pending conflict 由 X SUPER_ADMIN 處理。
5. Central Employee create / edit / Email OTP / ADMIN↔EMPLOYEE / enabled / password。
6. SUPER_ADMIN X→Y transfer。
7. A/B Employee snapshot 一致。
8. B 斷網後用最後同步的 Cloud cache 做 execution-time auth；恢復後 Cloud authority 更新 cache。

任何實機失敗先保留 log / error code，禁止用 fallback 特例繞過正式 authority model。

### D. Identity stage 後續

A/B identity flow 穩定後再做：

- Device revoke UI/API。
- 所有 Device Token 遺失但 Recovery Email 可用時的 Recovery Device flow。
- 最終 reference-backend recovery 維運文件。

之後才進 Business Work Item / Sync / Audit / 正式折讓 API；不要在 identity 驗證尚未完成前混入下一大階段。

## 6. 安全與範圍提醒

- Public repo：不得提交真實 Email、正式 endpoint、API key、Device Token、OTP、密碼、AMEGO App Key 或 runtime company/customer data。
- Pairing Code 授權 Device，不等於 Employee role。
- Device Token 只能證明可信 Device；高權限人員操作仍需 execution-time human authentication。
- Cloud 是 coordination / identity service，不是 invoice business kill switch。
- AMEGO 仍是發票／作廢／折讓官方結果唯一真相。
- API timeout / unknown result 不得盲目重送高風險業務操作。

## 7. 歷史 Work 任務起點（目前請以文件頂端接手摘要為準）

本節所述首次 D1 回查與 A 機 Employee Transition 已於後續階段完成；最新問題與下一步見文件頂端「接手摘要」。需要 Cloudflare 即時狀態時，仍以當次唯讀查核結果為準，不沿用歷史快照。