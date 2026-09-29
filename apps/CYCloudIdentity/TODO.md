# CYCloud Identity TODO

> 本文件只記錄 **current implementation status 與下一步**；不是永久規則來源。永久規則讀 `PROJECT_RULES.md`，產品/API/UI contract 讀 `README.md` 所列 active documents。

## Current checkpoint — 2026-09-30

- Current formal source baseline is **CYCloudIdentity 0.3.3 Build 0**；本 patch 建立 governed shared consumer standard / compatibility lifecycle，development runtime 仍是已部署的 **0.3.0 Build 0**，不因治理／文件調整自動重部署。
- CY Web formal source/development baseline is **0.4.0 Build 0**，已接上 CYID 0.3 first-login Email verification，並完成 direct Employee × Module Access management / server-check foundation。固定入口仍為 `https://admin.chihyuancm.com`。
- 0.2 direct Workspace Role / Identity Admin / direct App Access model 維持不變；0.3 的主要新增是新 Employee 首次 Email 驗證 credential flow。
- 新 Employee 建立後，CYID 自動寄出 **Email 驗證**郵件與 8 字元一次性首次登入密碼。首次登入密碼：
  - 只允許 CY Web core account application；
  - 24 小時 expiry；
  - 真正 single-use，成功驗證一次後不得再換第二張 ticket；
  - 不建立 normal Identity Session。
- 成功驗證首次登入密碼後，只簽發 10 分鐘的 opaque first-login ticket。ticket 不是 Session，normal session resolve 必須拒絕。
- first-login completion 建立正式 scrypt credential、完成 Email verification / durable first lifecycle、刪除 temporary credential/ticket，回傳 `reloginRequired: true`，**不發 normal Session**。
- 使用者必須回到 CY Web 一般登入頁，使用剛設定的正式密碼重新登入後，才會得到一般 CYID Session。
- `重寄驗證 Email`、pending Email 修改都會先輪替 temporary credential 並使舊密碼/ticket 立即失效；pending Email 修改不受舊信 resend cooldown 阻擋。
- Email delivery / Email-budget reservation 失敗時，不留下「未寄出但仍可登入」的新 temporary credential；Employee 保持 Email 未驗證並可由管理員重寄。
- Provider local acceptance 已證明：expiry、single-use、core-app-only、ticket-not-session、completion-no-session、ticket replay rejection、explicit re-login、resend/edit invalidation。
- CYID 0.3 development deploy 已成功完成 remote migration `0007_initial_email_password.sql`、Worker deploy 與 Identity secret configuration。
- Controlled real Email/browser lifecycle 驗收依使用者目前條件暫緩；這不重開已定案 contract，但仍是 production 前必要 acceptance。
- CYID shared consumer contract 已正式版本化為 **Consumer Contract 1.0.0 / Minimum Compatible 1.0.0**；所有完成接入的 consumer 必須宣告自己的 `CYID_CONSUMER_VERSION` 並維持在支援窗內。
- CYAccountingWeb（CYACCweb）handoff 已收斂為 app-specific migration guide：`docs/consumers/CYACC_INTEGRATION_HANDOFF.md`；共同 Role / Session / App Access / first-login / recovery 規範只讀 `docs/CONSUMER_INTEGRATION_STANDARD.md`。
- Cross-repository contract mirror 已定義 manifest + exact-sync 規則：CY Web 等外部 repo 必須鏡像 manifest 所列 7 個 artifacts 並在 governance/CI/deploy 前 byte-compare；同 repo consumer 直接讀 canonical files。
- Production、backup rollout 與其他 consumer production cutover 均未進行。

## Documentation consolidation

- `PROJECT_RULES.md` 是唯一 project-level 永久規則來源。
- `docs/CONSUMER_INTEGRATION_STANDARD.md` 保存所有 CYID consumer 的 shared technical contract；`CONSUMER_CONTRACT_VERSION` / `CONSUMER_MIN_COMPATIBLE_VERSION` 保存 machine-readable compatibility window。
- `docs/CONSUMER_CONTRACT_CHANGELOG.md` 保存 consumer-visible contract revision history。
- `docs/ROLE_AND_ACCESS_MODEL.md` 保存 Role / Identity Admin / App Access / lifecycle product contract。
- `docs/AUTH_CONTRACT.md` 保存 login/session/first-login/Email verification consumer contract。
- `docs/UI_ACCESS.md` 保存 account-management UI terminology、action 與 authority matrix。
- `docs/ARCHITECTURE.md` 保存 technical architecture / ownership / migration boundary。
- `docs/OTP_SECURITY.md` 保存 OTP/security-policy contract。
- `docs/consumers/CYACC_INTEGRATION_HANDOFF.md` 是 CYACC implementation guide；若與 canonical contract 衝突，以 canonical contract 為準。
- `TODO.md` 是唯一 current status / next-work tracker。
- Dated conversation handoff 與 obsolete Application Role Mapping 不留在 active tree；歷史用 Git history 追溯。

## Active next sequence

1. [x] 建立獨立 CYCloud Identity authority 與 initial Workspace/Employee/Credential/Session/OTP/Audit foundation。
2. [x] 完成並 development-deploy 0.2 direct Role / Identity Admin / direct App Access migration。
3. [x] 修正 0.2.1 first-lifecycle Email verified durability race並 development-deploy。
4. [x] 收斂 active documentation，移除 dated handoff 與 obsolete role-mapping 文件。
5. [x] 實作首次登入密碼 expiry、single-use、resend/pending-email-change invalidation。
6. [x] first-login complete 改為只回 completion + `reloginRequired`，不建立 normal Identity Session。
7. [x] 對外 UI/API wording 收斂為 Email 驗證，保留必要 legacy technical alias 但不再作產品語意。
8. [x] Merge CYID 0.3.0 to `main` 並經 `deploy/cycloudidentity-development` 成功部署 development。
9. [x] 驗證 0.3 migration、Worker bundle、auth-core、local login/first-login acceptance 與 development deployment health。
10. [x] CY Web 0.3 consumer source完成並 development-deploy：單一登入入口、HttpOnly first-login ticket transport、forced permanent-password screen、完成後回登入。
11. [ ] 建立 controlled USER，實際瀏覽器驗收：建立 -> 收驗證 Email -> 首次登入密碼 -> 設正式密碼 -> 回登入 -> 正式密碼登入。
12. [ ] 實際驗收首次登入密碼 expiry、重寄驗證 Email、pending Email 修改、舊 credential 失效與 delivery-failure recovery。
13. [ ] 實際驗收既有 Super Admin permanent-password login/F5/session resolve，並完成 USER self-service、正常 ADMIN、Identity Admin、Super Admin 權限矩陣。
14. [ ] 驗收 role/App Access change session invalidation、literal expired normal Session、forgot-password、own Email change 與 controlled Super Admin transfer。
15. [x] 建立 governed CYID Consumer Integration Standard + contract compatibility versions + PR consumer-impact gate；再加入 cross-repository sync manifest / exact mirror requirement；CYACC handoff 收斂為 app-specific migration guide。
16. [ ] CYAccountingWeb development integration 開始時加入 `CYID_CONSUMER_VERSION` 並依 standard 遷移；real Email/browser lifecycle acceptance 仍是 production gate。
17. [ ] 在 CYInvoice 工作線適合的接入點，以同一 consumer standard 建立其 app-specific migration handoff；Device/local/offline 邊界仍由 CYInvoice 自己管理。
18. [ ] Production 前完成 low-frequency backup + restore acceptance；production cutover 需使用者另行明確同意。

## Explicitly deferred

- Non-ADMIN HR Identity capability，直到有真實 HR workflow。
- Universal fine-grained business permission catalog inside CYID。
- CYInvoice Device pairing / Device Token / Local->Cloud / Windows offline credential cache。
- Production Identity rollout、paid Cloudflare/Email/Google Cloud plans。

## Reading order for current work

`PROJECT_RULES.md` -> `README.md` -> relevant active contract -> this `TODO.md` -> current source/tests/migrations。

不讀 dated handoff 作 current state；需要歷史 checkpoint 時看 Git history。
