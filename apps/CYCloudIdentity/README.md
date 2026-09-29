# CYCloud Identity

CYCloud Identity（CYID）是志遠 Cloud App 共用的 Identity authority，負責 Workspace、Employee、Credential、Workspace Role、Identity Admin capability、Application Access、Session、Email verification、OTP、Recovery 與 Identity security audit。

各 consumer App 保留自己的 business data、module access 與 domain authorization。CY Web 是核心帳號管理 App；CYAccountingWeb（CYACCweb）與 CYInvoice 之後以同一 shared contract 接入。

## Active documentation map

為避免重複 handoff 與平行規格，active 文件只按下列職責維護：

1. `PROJECT_RULES.md` — **唯一 project-level 永久規則來源**。
2. `docs/ROLE_AND_ACCESS_MODEL.md` — Workspace Role、Identity Admin、Application Access 與 lifecycle product contract。
3. `docs/AUTH_CONTRACT.md` — login/session/first-login/Email verification 的 consumer API contract。
4. `docs/UI_ACCESS.md` — account-management UI 的 terminology、visible actions 與權限矩陣。
5. `docs/ARCHITECTURE.md` — technical architecture、data/runtime ownership 與 migration boundary。
6. `docs/OTP_SECURITY.md` — OTP/security-policy contract。
7. `docs/EMAIL_OTP_BOOTSTRAP.md` — first Workspace bootstrap 專用流程。
8. `docs/DEVELOPMENT_DEPLOYMENT.md` — development deployment procedure。
9. `TODO.md` — **唯一 current implementation status / next-work tracker**。

`migrations/` 與 current source 是 executable implementation evidence；已套用 migration 不重寫。

## Documentation discipline

- 不再在 active tree 維護 dated conversation handoff。需要追溯舊 checkpoint 時使用 Git history。
- `TODO.md` 不重複保存永久規則；contract 文件也不維護第二份進度表。
- 若 consumer integration 需要交接，交接內容由上述 canonical contract 產生，放到目標 consumer 工作線；不得建立新的 master handoff 取代 CYID contract。
- 舊 Identity Group／Group-derived role 文件只屬歷史實作，不得作 forward authority。

## Public repository boundary

`CYapps` 是 Public repository。Public source 只保存 generic schema、contract、placeholder、adapter、deployment logic 與 synthetic test data。實際 Workspace／Employee／Email、Cloudflare resource IDs、provider targets、access matrices、API keys、OTP pepper、session／credential material 等都不得 commit。

## Cost mode

架構以可用免費額度為預設，不自動啟用付費方案。實際 quota、deployment state 與 rollout gate 只記錄在 `TODO.md`／deployment docs。