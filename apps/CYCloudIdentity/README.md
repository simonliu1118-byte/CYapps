# CYCloud Identity

CYCloud Identity（CYID）是志遠 Cloud App 共用的 Identity authority，負責 Workspace、Employee、Credential、Workspace Role、Identity Admin capability、Application Access、Session、Email verification、OTP、Recovery 與 Identity security audit。

各 consumer App 保留自己的 business data、module access 與 domain authorization。CY Web 是核心帳號管理 App；CYAccountingWeb（CYACCweb）已使用 CYID canonical login/session/logout contract。CYInvoice 的 CYID 模式與 CYERPAutoInput 等後續 desktop consumers 尚未完成接入；現況及下一個 consumer 準備事項只在 `TODO.md` 追蹤。

## Active documentation map

為避免重複 handoff 與平行規格，active 文件只按下列職責維護：

1. `PROJECT_RULES.md` — **唯一 project-level 永久規則來源**。
2. `docs/CONSUMER_INTEGRATION_STANDARD.md` — **所有 CYID consumer 的唯一 shared technical integration standard**。
3. `CONSUMER_CONTRACT_VERSION` / `CONSUMER_MIN_COMPATIBLE_VERSION` — machine-readable consumer compatibility window。
4. `CONSUMER_SYNC_MANIFEST.json` — 跨 repository consumer 必須同步的 canonical contract package membership。
5. `docs/CONSUMER_CONTRACT_CHANGELOG.md` — consumer-visible contract revision history。
6. `docs/ROLE_AND_ACCESS_MODEL.md` — Workspace Role、Identity Admin、Application Access 與 lifecycle product contract。
7. `docs/AUTH_CONTRACT.md` — provider login/session/first-login/Email verification API contract。
8. `docs/UI_ACCESS.md` — account-management UI terminology、visible actions 與權限矩陣。
9. `docs/ARCHITECTURE.md` — technical architecture、data/runtime ownership 與 migration boundary。
10. `docs/OTP_SECURITY.md` — OTP/security-policy contract。
11. `docs/EMAIL_OTP_BOOTSTRAP.md` — first Workspace bootstrap 專用流程。
12. `docs/DEVELOPMENT_DEPLOYMENT.md` — development deployment procedure。
13. `docs/consumers/CYACC_INTEGRATION_HANDOFF.md` — **只保存 CYACC 現況差異／遷移／app-specific acceptance**，不得重複 shared standard。
14. `TODO.md` — **唯一 current implementation status / next-work tracker**。
15. `docs/COMPATIBILITY_REVIEW.md` — historical compatibility evidence and dated follow-up findings；不作 shared consumer contract 或第二份進度表。

`migrations/` 與 current source 是 executable implementation evidence；已套用 migration 不重寫。

## Documentation discipline

- 不再在 active tree 維護 dated conversation handoff。需要追溯舊 checkpoint 時使用 Git history。
- `TODO.md` 不重複保存永久規則；contract 文件也不維護第二份進度表。
- Shared consumer rules/transport semantics 一律集中在 `docs/CONSUMER_INTEGRATION_STANDARD.md`；consumer-specific handoff 只記錄現況差異、遷移步驟、app-specific constraint 與 acceptance，不得複製 shared contract 成為平行規格。
- 跨 repository consumer 依 `CONSUMER_SYNC_MANIFEST.json` 保存 read-only contract mirror 並逐檔同步；同 repo consumer 直接讀 canonical files。
- 舊 Identity Group／Group-derived role 文件只屬歷史實作，不得作 forward authority。

## Public repository boundary

`CYapps` 是 Public repository。Public source 只保存 generic schema、contract、placeholder、adapter、deployment logic 與 synthetic test data。實際 Workspace／Employee／Email、Cloudflare resource IDs、provider targets、access matrices、API keys、OTP pepper、session／credential material 等都不得 commit。

## Cost mode

架構以可用免費額度為預設，不自動啟用付費方案。實際 quota、deployment state 與 rollout gate 只記錄在 `TODO.md`／deployment docs。
