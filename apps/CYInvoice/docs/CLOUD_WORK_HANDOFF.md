# CYInvoice Cloud Work Handoff

更新日期：2026-09-22

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
- Cloud compatibility：Cloud `0.8.2` / API `1` / Schema `7`
- Forward migrations：`0001`～`0007`

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

2026-09-23 development deploy Run #6 已完成：Cloud 0.8.2 / API 1 / Schema 7 / storage `ok`；D1 無待套用 migration，部署前 Workspace／Device／Employee／Pairing 仍各為 0。下一步由使用者在 Windows 使用 Build 1 測試包重新寄送 OTP，建立第一個 Workspace；目前不得宣稱 live 建立成功。

## 5. Work 接手後的優先順序

### A. Windows live 建立第一個 Workspace

1. 使用 Build 1 engineering package，於「雲端連線設定」測試連線並確認 Cloud 0.8.2 相容。
2. 重新寄送 bootstrap OTP；舊 OTP 已消耗，不得重用。由使用者自行在 Windows 輸入驗證碼與初始化碼，勿貼進對話。
3. 建立第一個 Workspace，應收到 Device identity 驗證完成訊息並進入 Employee transition。
4. 成功後再以遠端唯讀查詢核對 Workspace／Device 各 1 筆；失敗則保留錯誤碼與畫面，不重複提交。

不要把 remote 狀態猜成已完成。

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

Work 接手後，先完成 **V2.6.4 Build 1 的 Windows live Workspace 建立及 D1 唯讀回查**；Cloud 0.8.2 已部署且 CI 通過。成功後才繼續 A 機 Employee transition；不要先混入下一階段功能。
