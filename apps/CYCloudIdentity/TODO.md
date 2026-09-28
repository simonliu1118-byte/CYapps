# CYCloud Identity TODO

> 本文件只記錄 current implementation status 與下一步，不是永久規則來源。

## Current checkpoint — 2026-09-29

- `CYCloudIdentity` 已完成 repository governance 登錄；目前 development work item 版本 `0.1.12`。
- CYInvoice Cloud 仍是 reference-only；Identity 工作線不直接修改 CYInvoice Device/runtime/D1。
- Workspace 最高管理 authority 已與普通 Identity Groups 分離；普通身分組為 data-driven，可新增、重新命名、停用與調整 membership，不需 schema migration。
- Application Registry 是 generic runtime registry；Public migration 不 seed 公司目前實際 App catalog，也不保存實際 Workspace/Employee access matrix。
- Application Access 支援 direct Employee grant、Identity Group grant，以及受保護 Workspace highest-authority entry。
- 新 credential 使用 `scrypt`；legacy `pbkdf2-sha256` 只在 Cloudflare production 可安全驗證的 iteration 範圍內保留 compatibility。Password 長度固定為 8–16 Unicode 字元。
- Identity-owned session create/resolve/logout 已有 executable Worker implementation；Local Wrangler + D1 acceptance 已驗證 synthetic login → resolve → logout → revoked-session rejection。
- Provider-neutral Email Sender、Email OTP、first-Workspace bootstrap、login rate limit 與 free-tier email budget protection 已實作。
- Workspace highest authority 可透過 Identity admin API 調整 OTP resend cooldown、max attempts、per-email/purpose hourly limit 與 Workspace daily email limit；所有可調值受 server-side safety bounds 限制並寫 Audit。
- OTP established defaults 維持：60 秒重寄冷卻、5 次驗證嘗試、同 Email+purpose 每小時 5 封、Workspace 每日 100 封（若 global ceiling 更低則取較低值）、OTP 10 分鐘有效。
- Employee self-service lifecycle 已實作：本人改密碼、Email 驗證／變更、Email OTP 忘記密碼 reset；credential version 變更會使舊 session 失效。
- Workspace highest-authority transfer 已實作：目前最高管理者 credential re-auth + OTP，完成時更新 authority pointer 與 Recovery Email，並撤銷舊 authority session。
- Highest-authority-only Employee administration 已實作：建立 pending Employee、更新 profile/status、Email OTP activation、由 Employee 自行設定第一次密碼；管理者不代設／寄送明文初始密碼。
- Identity Group／membership／direct+group Application Access 管理 API 已實作。
- Generic coarse Application compatibility-role mapping 已實作：`USER_ADMIN` mode 下 Group grant 可設定 `USER` / `ADMIN`，Workspace highest authority server-side 自動投影 `SUPER_ADMIN`；direct grant 沒有 Admin Group 時投影 `USER`。Login 與 session resolve 都會依目前 authority 重新計算 `applicationRoleKey`。
- CYInvoice compatibility 規則已固定為 `SUPER_ADMIN > ADMIN > USER`；普通 Group 永遠不能授予 `SUPER_ADMIN`。
- CY Web 的 OTP 設定入口只對 `isWorkspaceSuperAdmin=true` 顯示；其他 Identity Group/Employee 不呈現入口或權限錯誤流程。Identity backend 仍保留 highest-authority 驗證作為 security boundary。
- Shared provider/global Email Daily Ceiling 仍由 runtime config 保留為不可突破的系統安全上限；Workspace 管理者只能在該 ceiling 以下調整自己的 daily limit。
- CY Web 預定作為 Shared Identity 的帳號管理 UI；帳號/OTP/權限資料與規則仍由 CYCloud Identity Worker + D1 持有，不搬進 CY Web business D1。
- `docs/AUTH_CONTRACT.md` 已固定 login/session consumer contract；普通 Identity Groups 不重新退回固定 global role enum。
- Placeholder-only development deployment pipeline 已加入；Public source 不保存 remote D1 ID、Worker name、sender address、provider credential、OTP pepper 或 bootstrap secret。
- Non-production Identity Worker 與 D1 已完成 remote deployment；第一個 development Workspace 已透過 Email OTP bootstrap 完成，highest-authority login/logout 已驗收。
- CY Web V0.1.47 已切換至 CYCloud Identity development binding，實際瀏覽器 password login、F5 session resolve、logout、post-logout F5 均已驗收正常。
- Current cost assumption: stay within free Cloudflare / Email provider / Google Cloud usage; no paid-tier dependency or automatic upgrade.
- No production deployment, production Workspace, production Employee, real App access matrix or real secret is created by this work.

## Active sequence

1. [x] Audit CYInvoice Cloud Identity lifecycle, Web Auth, Email provider, password recovery and highest-authority transfer reference behavior.
2. [x] Freeze CYCloud Identity ownership boundary and extraction architecture.
3. [x] Stage clean initial D1 relational schema for Workspace / Employee / Credential / Application Access / Session / OTP / Audit.
4. [x] Add a zero-secret local schema acceptance gate for critical Workspace / cross-Workspace invariants.
5. [x] Refine the pre-deployment foundation so ordinary Identity Groups are extensible and the Public migration contains no real application catalog/access matrix.
6. [x] Implement current credential hashing/verification compatibility (`scrypt` for new credentials; bounded legacy PBKDF2 verification only).
7. [x] Implement application-aware login plus Identity-owned session create/resolve/logout.
8. [x] Add actual local Wrangler + D1 authentication roundtrip acceptance using synthetic-only runtime data.
9. [x] Add login rate/abuse protection and stable `429` boundary.
10. [x] Implement provider-neutral Email Sender with runtime provider adapters and free-tier send-budget protection.
11. [x] Implement Email OTP runtime: purpose isolation, 6-digit code, HMAC digest, expiry, cooldown, attempt limits and one-time consumption.
12. [x] Implement one-time Workspace + first highest-authority Employee bootstrap with Email OTP.
13. [x] Add highest-authority editable Workspace OTP security policy with hard server-side bounds and a non-editable global email ceiling.
14. [x] Implement Employee Email verification/change, self password change and Email OTP password recovery using Workspace security policy.
15. [x] Implement highest-authority transfer with Workspace authority pointer + Recovery Email update and previous-authority session revocation.
16. [x] Implement Identity Group create/rename/disable/membership management, direct/group Application Access management, and coarse per-Application role mapping; `USER_ADMIN` compatibility projection is recomputed on login/session resolve.
17. [ ] Expand Worker runtime acceptance for Employee activation, Email change/recovery, highest-authority transfer, Group/access mutation, role changes during live sessions, abuse limits and cross-Workspace rejection.
18. [x] Add placeholder-only development deployment pipeline; no real resource IDs in Public source.
19. [x] Create non-production Identity Worker/D1 and bootstrap the first development Workspace through controlled runtime configuration.
20. [ ] CY Web development login/session/logout cutover is accepted; finish invalid/expired-session acceptance and add recovery/self-service UI before calling the consumer path complete.
21. [ ] Add CY Web Shared Identity account-management UI (Employees, Groups, Application Access, Recovery Email, OTP settings), backed only by CYCloud Identity admin APIs; OTP settings entry is highest-authority-only.
22. [ ] Publish stable consumer handoff document for ACC Web and CYInvoice workstreams after CY Web recovery/management contract is stable.
23. [ ] Switch additional development Apps to CYCloud Identity and accept their login/session/recovery paths.
24. [ ] Add low-frequency encrypted/logical backup + restore acceptance compatible with the approved free Google Cloud usage envelope before production rollout.
25. [ ] After shared Identity consumers are stable, CYInvoice performs its separate Device/desktop-safe migration and removes duplicate account-management ownership.

## Explicitly deferred from this workstream

- CYInvoice Device pairing / Device Token authority.
- CYInvoice Local → Cloud Employee transition.
- CYInvoice Windows offline credential cache.
- CYInvoice source/runtime changes before its dedicated migration workstream.
- Production DNS/custom domain.
- Paid Cloudflare/Email/Google Cloud plans.
