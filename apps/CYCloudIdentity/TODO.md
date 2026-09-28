# CYCloud Identity TODO

> 本文件只記錄 current implementation status 與下一步，不是永久規則來源。

## Current checkpoint — 2026-09-28

- New sibling project staged under `apps/CYCloudIdentity/`.
- CYInvoice Cloud is reference-only and remains untouched.
- Initial architecture preserves Workspace-scoped Employee authority, one SUPER_ADMIN per Workspace, Recovery Email, Email OTP and credential semantics.
- Initial design adds explicit Application Access and app-scoped browser sessions required by CY Web / CYAccountingWeb.
- Current cost assumption: stay within free Cloudflare / Brevo / Google Cloud usage; no paid-tier dependency or automatic upgrade.
- No production deployment, production Workspace, production Employee or real secret is created by this foundation work.

## Active sequence

1. [x] Audit CYInvoice Cloud Identity lifecycle, Web Auth, Email provider, password recovery and SUPER_ADMIN transfer reference behavior.
2. [x] Freeze CYCloud Identity ownership boundary and extraction architecture.
3. [x] Stage clean initial D1 relational schema for Workspace / Employee / Credential / Application Access / Session / OTP / Audit.
4. [ ] Implement provider-neutral Email Sender with Brevo as current runtime adapter and Resend as optional adapter.
5. [ ] Implement credential hashing/verification compatibility and login rate/abuse protection.
6. [ ] Implement one-time Workspace + first SUPER_ADMIN bootstrap with Email OTP.
7. [ ] Implement application-aware Web Auth compatibility endpoint plus Identity-owned browser session create/resolve/logout.
8. [ ] Implement Employee Email verification/change, self password change and Email OTP password recovery.
9. [ ] Implement SUPER_ADMIN transfer with atomic role + Workspace Recovery Email update.
10. [ ] Add Application Access management for ADMIN / EMPLOYEE while SUPER_ADMIN retains Workspace app administration access.
11. [ ] Add local D1 + Worker acceptance tests for all critical invariants and abuse limits.
12. [ ] Add placeholder-only development deployment pipeline; no real resource IDs in Public source.
13. [ ] Create non-production Identity Worker/D1 and bootstrap a test Workspace/SUPER_ADMIN.
14. [ ] Switch CY Web development `IDENTITY` Service Binding from temporary CYInvoice provider to CYCloud Identity and accept login/session/logout/recovery.
15. [ ] Switch CYAccountingWeb development to CYCloud Identity and accept its login/session/recovery path.
16. [ ] Add low-frequency encrypted/logical backup + restore acceptance compatible with free Google Cloud usage before production rollout.
17. [ ] Only after CY Web + CYAccountingWeb are stable, hand the stable contract to the CYInvoice workstream for its separate migration/cutover.

## Explicitly deferred from this workstream

- CYInvoice Device pairing / Device Token authority.
- CYInvoice Local → Cloud Employee transition.
- CYInvoice Windows offline credential cache.
- CYInvoice source/runtime changes.
- Production DNS/custom domain.
- Paid Cloudflare/Brevo/Google Cloud plans.
