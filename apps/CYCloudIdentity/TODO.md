# CYCloud Identity TODO

> 本文件只記錄 current implementation status 與下一步，不是永久規則來源。

## Current checkpoint — 2026-09-28

- `CYCloudIdentity` 已完成 repository governance 登錄；目前 foundation work item 版本 `0.1.1`。
- CYInvoice Cloud 是 reference-only，第一階段保持 source/runtime/D1 不動。
- Workspace 最高管理 authority 已與普通 Identity Groups 分離；普通身分組改為 data-driven，可新增、重新命名、停用與調整 membership，不需 schema migration。
- Application Registry 改為 generic runtime registry；Public migration 不再 seed 公司目前實際 App catalog，也不保存實際 Workspace/Employee access matrix。
- Application Access 保留 direct Employee grant 與 Identity Group grant 兩條 coarse-grained 路徑；App 內細部 permission 仍由 App 自己管理。
- 初始 schema 仍處於 remote deployment 前 foundation 階段，因此可在第一次遠端 D1 套用前修正 `0001`；遠端基準一旦接受後，後續才改用 numbered forward migrations。
- Current cost assumption: stay within free Cloudflare / Email provider / Google Cloud usage; no paid-tier dependency or automatic upgrade.
- No production deployment, production Workspace, production Employee, real App catalog or real secret is created by this foundation work.

## Active sequence

1. [x] Audit CYInvoice Cloud Identity lifecycle, Web Auth, Email provider, password recovery and highest-authority transfer reference behavior.
2. [x] Freeze CYCloud Identity ownership boundary and extraction architecture.
3. [x] Stage clean initial D1 relational schema for Workspace / Employee / Credential / Application Access / Session / OTP / Audit.
4. [x] Add a zero-secret local schema acceptance gate for critical Workspace / cross-Workspace invariants.
5. [x] Refine the pre-deployment foundation so ordinary Identity Groups are extensible and the Public migration contains no real application catalog/access matrix.
6. [ ] Implement provider-neutral Email Sender with the approved runtime provider adapter(s).
7. [ ] Implement credential hashing/verification compatibility and login rate/abuse protection.
8. [ ] Implement one-time Workspace + first highest-authority Employee bootstrap with Email OTP.
9. [ ] Implement application-aware Web Auth compatibility endpoint plus Identity-owned browser session create/resolve/logout.
10. [ ] Implement Employee Email verification/change, self password change and Email OTP password recovery.
11. [ ] Implement highest-authority transfer with atomic Workspace authority pointer + Recovery Email update.
12. [ ] Implement Identity Group create/rename/disable/membership management and direct/group Application Access management.
13. [ ] Add Worker runtime acceptance tests for all critical invariants and abuse limits.
14. [ ] Add placeholder-only development deployment pipeline; no real resource IDs in Public source.
15. [ ] Create non-production Identity Worker/D1 and bootstrap synthetic/test runtime data through controlled configuration.
16. [ ] Switch the first development App Identity binding from the temporary provider to CYCloud Identity and accept login/session/logout/recovery.
17. [ ] Switch additional development Apps to CYCloud Identity and accept their login/session/recovery paths.
18. [ ] Add low-frequency encrypted/logical backup + restore acceptance compatible with the approved free Google Cloud usage envelope before production rollout.
19. [ ] Only after shared Identity consumers are stable, hand the stable contract to the CYInvoice workstream for its separate migration/cutover.

## Explicitly deferred from this workstream

- CYInvoice Device pairing / Device Token authority.
- CYInvoice Local → Cloud Employee transition.
- CYInvoice Windows offline credential cache.
- CYInvoice source/runtime changes.
- Production DNS/custom domain.
- Paid Cloudflare/Email/Google Cloud plans.
