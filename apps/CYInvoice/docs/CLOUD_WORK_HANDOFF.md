# CYInvoice Cloud Work Handoff

更新日期：2026-09-24

## 最新工作：首次開啟直接加入雲端

使用者已定案：首次開啟先選「使用單機版」或「直接加入雲端」。單機版仍先建立本機超管，之後加入既有 Workspace 維持原本的本機管理員驗證＋配對碼＋全機帳號轉換。全新安裝直接加入不建本機帳號，可選：(1) 既有可信裝置產生的短效配對碼；(2) Workspace ID＋該 Workspace 的中央 SUPER_ADMIN 員工編號與密碼，再以其已驗證 Email OTP 確認。兩條路徑先確認 Workspace 名稱，加入後取得 Device identity、中央 Employee snapshot 與受保護快取，才切 Cloud authority。相同 Email／帳密在不同 Workspace 仍是各自獨立的員工帳號；以 Workspace ID 指定目標。Cloud 參考實作目前仍只允許 bootstrap 一個 Workspace，不宣稱已完成多 Workspace 實測。

本分支新增 Windows 首次開啟／直連 UI 與 Cloud 0.8.3 端點；**尚未部署 Cloud 0.8.3，也尚未通過 Windows CI 或實機驗收**。並行開發已在同一 PR 加入 Cloud migration `0008` 的密碼復原功能，故此 Windows 新版要求 Schema `8`。development Worker 的即時版本和 D1 migration 狀態仍須由本機 Cloudflare MCP 唯讀查核。新機直連要求既有 Workspace 已完成第一台的中央 Employee cutover；若尚未完成，API 拒絕加入，不重建 Workspace 或另立本機超管。先完成原交接文件中的 D1 唯讀查核與 A 機帳號轉換，再由本機已連線的 Cloudflare MCP 部署經驗證的後端。請勿把本段工程 source 狀態當作已部署狀態。

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
- 最新 source compatibility：Cloud `0.8.3` / API `1` / Schema `8`；首次建立時的已驗證歷史版本仍為 Cloud `0.8.2` / Schema `7`。
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

2026-09-22 已完成 remote audit：development Worker Cloud 0.8.1 的 `/v1/health` 回覆 storage `ok`，D1 migrations 已到 Schema 7，Brevo bootstrap OTP 已實際寄達。第一次建立 Workspace 時 Worker INSERT SQL 欄位和值數量不一致，D1 batch 回滾，卻誤回報 `WORKSPACE_ALREADY_INITIALIZED`；遠端唯讀查核確認 Workspace／Device／Employee／Pairing 筆數均為 0。V2.6.4 Build 1 / Cloud 0.8.2 修正 SQL、錯誤分類並新增直接執行正式 SQL 的 Schema 7 回歸測試。

2026-09-23 development deploy Run #6 已完成：Cloud 0.8.2 / API 1 / Schema 7 / storage `ok`；D1 無待套用 migration，部署前 Workspace／Device／Employee／Pairing 仍各為 0。這些是建立前的筆數，不可當作目前筆數。

同日使用者在 Windows V2.6.4 Build 1 重新寄送 OTP 並執行首次建立；CYInvoice 畫面回報第一個 Workspace 與 Device 建立成功，且 Device identity 驗證完成。此為 Windows client 收到的成功結果；**建立後尚未從 Cloudflare D1 獨立唯讀核對筆數與記錄，也未確認 whole-device Employee Transition / cutover 完成**。不要再次執行 bootstrap 或清除資料。

Cloudflare API、Bindings、Builds、Observability 四個官方 MCP 端點已由使用者在本機 Codex 檢查為「已設定／已載入／連線成功／目前不需 OAuth 登入」；該檢查尚未讀取 `cyinvoice-cloud-dev` 的 Workspace／Device。網頁版 Work 對話沒有這四個工具，不能把本機設定檔已登記誤當作網頁對話可用。這次工作採網頁版為主；需要 Cloudflare 即時狀態時，由已連線的本機 Codex 讀本文件後執行限定範圍的唯讀查核，並把去識別結果帶回主要工作對話。

## 5. Work 接手後的優先順序

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

## 7. 目前適合的 Work 任務起點

Work 接手後，先完成 **首次 Workspace／Device 建立後的 D1 唯讀回查**；Windows client 已回報建立及 Device identity 成功，Cloud 0.8.2 已部署且 CI 通過，但帳號轉換仍未驗收。核對後繼續 A 機 Employee Transition；不要先混入下一階段功能。一般程式／文件工作可留在網頁版，本機 Codex 負責已授權的 Cloudflare MCP 即時查核。
