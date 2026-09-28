# CYCloud Identity TODO

> 本文件只記錄 current implementation status 與下一步，不是永久規則來源。

## Current checkpoint — 2026-09-28

- `CYCloudIdentity` 已完成 repository governance 登錄；正式版本起點 `0.1.0`。
- CYInvoice Cloud 是 reference-only，第一階段保持 source/runtime/D1 不動。
- 初始架構保留 Workspace-scoped Employee authority、每 Workspace 唯一 SUPER_ADMIN、Recovery Email、Email OTP 與 credential semantics。
- 新架構加入 CY Web / CYAccountingWeb 所需的明確 Application Access 與 app-scoped browser session。
- Current cost assumption: stay within free Cloudflare / Brevo / Google Cloud usage; no paid-tier dependency or automatic upgrade.
- No production deployment, production Workspace, production Employee or real secret is created by this foundation work.

## Active sequence

1. [x] Audit CYInvoice Cloud Identity lifecycle, Web Auth, Email provider, password recovery and SUPER_ADMIN transfer reference behavior.
2. [x] Freeze CYCloud Identity ownership boundary and extraction architecture.
3. [x] Stage clean initial D1 relational schema for Workspace / Employee / Credential / Application Access / Session / OTP / Audit.
4. [x] Add a zero-secret local schema acceptance gate for the critical Workspace / SUPER_ADMIN / cross-Workspace invariants.
5. [ ] Implement provider-neutral Email Sender with Brevo as current runtime adapter and Resend as optional adapter.
6. [ ] Implement credential hashing/verification compatibility and login rate/abuse protection.
7. [ ] Implement one-time Workspace + first SUPER_ADMIN bootstrap with Email OTP.
8. [ ] Implement application-aware Web Auth compatibility endpoint plus Identity-owned browser session create/resolve/logout.
9. [ ] Implement Employee Email verification/change, self password change and Email OTP password recovery.
10. [ ] Implement SUPER_ADMIN transfer with atomic role + Workspace Recovery Email update.
11. [ ] Add Application Access management for ADMIN / EMPLOYEE while SUPER_ADMIN retains Workspace app administration access.
12. [ ] Add Worker runtime acceptance tests for all critical invariants and abuse limits.
13. [ ] Add placeholder-only development deployment pipeline; no real resource IDs in Public source.
14. [ ] Create non-production Identity Worker/D1 and bootstrap a test Workspace/SUPER_ADMIN.
15. [ ] Switch CY Web development `IDENTITY` Service Binding from temporary CYInvoice provider to CYCloud Identity and accept login/session/logout/recovery.
16. [ ] Switch CYAccountingWeb development to CYCloud Identity and accept its login/session/recovery path.
17. [ ] Add low-frequency encrypted/logical backup + restore acceptance compatible with free Google Cloud usage before production rollout.
18. [ ] Only after CY Web + CYAccountingWeb are stable, hand the stable contract to the CYInvoice workstream for its separate migration/cutover.

## Explicitly deferred from this workstream

- CYInvoice Device pairing / Device Token authority.
- CYInvoice Local → Cloud Employee transition.
- CYInvoice Windows offline credential cache.
- CYInvoice source/runtime changes.
- Production DNS/custom domain.
- Paid Cloudflare/Brevo/Google Cloud plans.
