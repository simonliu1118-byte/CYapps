# CYCloud Identity TODO

> 本文件只記錄 current implementation status 與下一步，不是永久規則來源。

## Current checkpoint — 2026-09-28

- `CYCloudIdentity` 已完成 repository governance 登錄；目前 shared-auth/security-policy work item 版本 `0.1.4`。
- CYInvoice Cloud 是 reference-only，第一階段保持 source/runtime/D1 不動。
- Workspace 最高管理 authority 已與普通 Identity Groups 分離；普通身分組為 data-driven，可新增、重新命名、停用與調整 membership，不需 schema migration。
- Application Registry 是 generic runtime registry；Public migration 不 seed 公司目前實際 App catalog，也不保存實際 Workspace/Employee access matrix。
- Application Access 支援 direct Employee grant、Identity Group grant，以及受保護 Workspace highest-authority entry。
- PBKDF2-SHA256 credential compatibility、Identity-owned session create/resolve/logout 已有 executable Worker implementation。
- Local Wrangler + D1 acceptance 已驗證 synthetic login → resolve → logout → revoked-session rejection；不依賴遠端資源或秘密。
- Provider-neutral Email Sender、Email OTP、first-Workspace bootstrap、login rate limit 與 free-tier email budget protection 已實作。
- Workspace highest authority 可透過 Identity admin API 調整 OTP resend cooldown、max attempts、per-email/purpose hourly limit 與 Workspace daily email limit；所有可調值受 server-side safety bounds 限制並寫 Audit。
- Shared provider/global Email Daily Ceiling 仍由 runtime config 保留為不可突破的系統安全上限；Workspace 管理者只能在該 ceiling 以下調整自己的 daily limit。
- CY Web 預定作為 Shared Identity 的帳號管理 UI；帳號/OTP/權限資料與規則仍由 CYCloud Identity Worker + D1 持有，不搬進 CY Web business D1。
- `docs/AUTH_CONTRACT.md` 已固定目前 login/session consumer contract；普通 Identity Groups 不重新退回固定 role enum。
- 初始 Identity D1 尚未第一次 remote deployment；remote baseline 接受後所有 schema 變更只用 numbered forward migrations。
- Current cost assumption: stay within free Cloudflare / Email provider / Google Cloud usage; no paid-tier dependency or automatic upgrade.
- No production deployment, production Workspace, production Employee, real App catalog or real secret is created by this work.

## Active sequence

1. [x] Audit CYInvoice Cloud Identity lifecycle, Web Auth, Email provider, password recovery and highest-authority transfer reference behavior.
2. [x] Freeze CYCloud Identity ownership boundary and extraction architecture.
3. [x] Stage clean initial D1 relational schema for Workspace / Employee / Credential / Application Access / Session / OTP / Audit.
4. [x] Add a zero-secret local schema acceptance gate for critical Workspace / cross-Workspace invariants.
5. [x] Refine the pre-deployment foundation so ordinary Identity Groups are extensible and the Public migration contains no real application catalog/access matrix.
6. [x] Implement PBKDF2-SHA256 credential hashing/verification compatibility.
7. [x] Implement application-aware login plus Identity-owned session create/resolve/logout.
8. [x] Add actual local Wrangler + D1 authentication roundtrip acceptance using synthetic-only runtime data.
9. [x] Add login rate/abuse protection and stable `429` boundary.
10. [x] Implement provider-neutral Email Sender with runtime provider adapters and free-tier send-budget protection.
11. [x] Implement Email OTP runtime: purpose isolation, 6-digit code, HMAC digest, expiry, cooldown, attempt limits and one-time consumption.
12. [x] Implement one-time Workspace + first highest-authority Employee bootstrap with Email OTP.
13. [x] Add highest-authority editable Workspace OTP security policy with hard server-side bounds and a non-editable global email ceiling.
14. [ ] Implement Employee Email verification/change, self password change and Email OTP password recovery using Workspace security policy.
15. [ ] Implement highest-authority transfer with atomic Workspace authority pointer + Recovery Email update.
16. [ ] Implement Identity Group create/rename/disable/membership management and direct/group Application Access management.
17. [ ] Add Worker runtime acceptance tests for all remaining critical invariants and abuse limits.
18. [ ] Add placeholder-only development deployment pipeline; no real resource IDs in Public source.
19. [ ] Create non-production Identity Worker/D1 and bootstrap the first development Workspace through controlled runtime configuration.
20. [ ] Switch CY Web development Identity binding from the temporary provider to CYCloud Identity and accept login/session/logout/recovery.
21. [ ] Add CY Web Shared Identity account-management UI (Employees, Groups, Application Access, Recovery Email, OTP policy), backed only by CYCloud Identity admin APIs.
22. [ ] Publish stable consumer handoff document for ACC Web and CYInvoice workstreams after CY Web acceptance.
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
