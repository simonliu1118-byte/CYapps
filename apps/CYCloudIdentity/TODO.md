# CYCloud Identity TODO

> 本文件只記錄 **current implementation status 與下一步**；不是永久規則來源。永久規則讀 `PROJECT_RULES.md`，產品/API/UI contract 讀 `README.md` 所列 active documents。

## Current checkpoint — 2026-09-30

- Formal `main` 與目前 development deployment 為 **CYCloudIdentity 0.2.1 Build 0**。
- 0.2.x 已完成 direct Workspace Role `SUPER_ADMIN / ADMIN / USER`、ADMIN-only Identity Admin capability、direct Employee App Access、durable first-lifecycle state、pending lifecycle、forced activated-account Email recovery，以及 Group-derived authority retirement。Legacy Group structures只可作 compatibility/history，不是 forward authority。
- CY Web `0.2.4` 已在 development 接入 0.2 principal/session model，`https://admin.chihyuancm.com` 為固定 canonical user-facing URL。
- PR `#214` / branch `feature/cycloudidentity-first-login-password` 正在形成 **CYID 0.3.0** first-login Email verification contract。CI validation 已通過；PR event 不會真正 deploy development，development 仍是 0.2.1。
- 已定案的 0.3 產品流程：新增 Employee -> 自動寄出 **Email 驗證**郵件與一次性首次登入密碼 -> 使用者從 CY Web 一般登入口登入 -> 強制設定正式密碼 -> CYID 完成 Email verification / first lifecycle -> **不建立一般 Session** -> 回到登入頁 -> 使用正式密碼重新登入。
- 首次登入密碼必須有 expiry；逾期或管理員重寄驗證 Email 時，舊首次登入密碼立即失效並產生新密碼／新 expiry。
- CY Web 不再保留獨立「啟用帳號」入口；對外名稱維持 Email 驗證。技術欄位或 legacy endpoint 名稱若暫時保留，不得反向決定 UI/product terminology。
- PR #214 目前 source 已具 initial credential、first-login ticket、forced first password 基礎，但仍需對齊最終 contract：**initial password expiry**、**first-login complete 不得直接發 normal session**、consumer-visible Email verification wording。
- Production、backup rollout 與 consumer production cutover 均未進行。

## Active next sequence

1. [x] 建立獨立 CYCloud Identity authority 與 initial Workspace/Employee/Credential/Session/OTP/Audit foundation。
2. [x] 完成並 development-deploy 0.2 direct Role / Identity Admin / direct App Access migration。
3. [x] 修正 0.2.1 first-lifecycle Email verified durability race 並 development-deploy。
4. [x] 將 CYID active documentation 收斂為 Rules / Role+Access / Auth / UI / Architecture / TODO，移除 dated handoff 與 obsolete role-mapping 文件。
5. [ ] 對齊 PR #214 source：加入首次登入密碼 expiry 與 resend invalidation contract。
6. [ ] 對齊 PR #214 source：first-login complete 成功後不建立 normal Identity session，僅回傳 completion/relogin-required result。
7. [ ] 對齊 0.3 API/UI wording 與 management response，使對外流程統一為 Email 驗證。
8. [ ] Merge CYID 0.3.0 to `main`，再經 governed `deploy/cycloudidentity-development` 部署 development。
9. [ ] 驗證 development migration/Worker health、現有 Super Admin 正常正式密碼登入與 session resolve。
10. [ ] 由 CY Web consumer 實作單一登入入口 + first-login password-change flow + 完成後回登入頁。
11. [ ] 建立 controlled USER，驗收新增 -> 驗證 Email -> 首次登入密碼 -> 強制正式密碼 -> 回登入 -> 正式密碼登入全流程。
12. [ ] 驗收首次登入密碼 expiry、重寄驗證 Email、pending Email 修改、舊 initial credential 立即失效與寄送失敗 recovery。
13. [ ] 驗收普通 USER self-service、正常 ADMIN USER lifecycle、Identity Admin role/App Access/Email recovery/anti-self-escalation、Super Admin-only security controls。
14. [ ] 驗收 role/App Access change 的 session invalidation、literal expired-session evidence、forgot-password、own Email change 與 controlled Super Admin transfer。
15. [ ] Shared contract 驗收穩定後，產出 CYAccountingWeb 與 CYInvoice consumer integration handoff；實作留在各自工作線。
16. [ ] Production 前完成 low-frequency backup + restore acceptance；production cutover 需使用者另行明確同意。

## Explicitly deferred

- Non-ADMIN HR Identity capability，直到有真實 HR workflow。
- Universal fine-grained business permission catalog inside CYID。
- CYInvoice Device pairing / Device Token / Local→Cloud / Windows offline credential cache。
- Production Identity rollout、paid Cloudflare/Email/Google Cloud plans。

## Reading order for current work

`PROJECT_RULES.md` -> `README.md` -> relevant active contract -> this `TODO.md` -> current source/tests/migrations。

不再讀 dated handoff 作 current state；需要歷史 checkpoint 時看 Git history。