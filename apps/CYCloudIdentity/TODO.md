# CYCloud Identity TODO

> 本文件只記錄 current implementation status 與下一步，不是永久規則來源。

## Current checkpoint — 2026-09-29

- Current development baseline: `CYCloudIdentity` `0.1.14` on `main` and deployed in development.
- CYInvoice Cloud remains reference-only in this workstream; do not modify CYInvoice Device/runtime/D1 until its dedicated migration conversation.
- Workspace Super Admin is a protected authority pointer and `isWorkspaceSuperAdmin` signal, separate from ordinary extensible Identity Groups. Consumer-facing management UI uses `超級管理員 (Super Admin)` while stable protocol/storage field names remain unchanged.
- Application Registry is generic runtime data. Public source does not seed or disclose the real App catalog, Workspace/Employee access matrix, Cloudflare resource IDs, sender addresses, credentials, OTP pepper or bootstrap secret.
- New credentials use `scrypt`; bounded legacy `pbkdf2-sha256` verification remains compatibility-only. Password boundary is 8–16 Unicode characters.
- Identity-owned login/session/resolve/logout is implemented and deployed in development. Session TTL is runtime-configured and the accepted development path uses an 8-hour session.
- Provider-neutral Email Sender, Email OTP, login abuse protection, first-Workspace bootstrap and free-tier email budget controls are implemented.
- OTP defaults remain 60-second resend cooldown, 5 attempts, 5 sends per Email+purpose/hour, Workspace daily limit 100, 10-minute validity, with a lower runtime global ceiling taking precedence.
- Employee lifecycle is implemented: Super Admin creates a pending Employee; Employee activates by Email OTP and sets the first password; self password change, Email change and Email OTP password recovery are implemented.
- Employee lifecycle semantics now explicitly distinguish first-time `尚未驗證／待啟用`, activated `啟用`, and previously activated `停用`. A pending first-time Employee may be deleted by the Workspace Super Admin; the provider rejects deletion after activation and rejects deletion of the current Super Admin.
- Super Admin transfer is implemented with current-password re-auth + OTP. Completion moves the authority pointer and Recovery Email and revokes the previous authority sessions.
- Super Admin lifecycle protection is implemented server-side: the current Super Admin Employee cannot be disabled or deleted before authority transfer.
- Super Admin summary API remains available for consumer use, including Recovery Email verification state and Workspace revision.
- Identity Group create/update/disable, membership management, direct/group Application Access and coarse per-Application compatibility-role mapping are implemented.
- `USER_ADMIN` compatibility projection is recomputed during login/session resolve. CYInvoice compatibility is fixed as `SUPER_ADMIN > ADMIN > USER`; ordinary Groups can only project `USER` or `ADMIN`; direct access defaults to `USER`; Workspace Super Admin always projects `SUPER_ADMIN`.
- CY Web is the first accepted Shared Identity consumer. Login, F5 session resolve, logout and post-logout F5 were manually accepted with the first development Super Admin account.
- CY Web `0.1.50` invalid-provider-session runtime gate was accepted in Development Deploy run `#20`.
- CY Web `0.1.51` is merged and awaits manual development deployment/browser acceptance. It presents the three Employee lifecycle states, pending-only delete, and Super Admin transfer from the Employee table in a modal instead of a standalone authority block.
- CY Web only renders Workspace management / OTP settings when `isWorkspaceSuperAdmin=true`; backend authorization remains authoritative.
- Latest CYCloud Identity `0.1.14` development deployment completed successfully, including validation, remote migrations, Worker deployment and secret configuration.
- Current cost assumption remains free Cloudflare / Email provider / Google Cloud usage; no paid-tier dependency or automatic upgrade.
- Production remains untouched.

## Active next sequence

1. [x] Audit CYInvoice Cloud Identity lifecycle and freeze the Shared Identity ownership boundary.
2. [x] Implement Workspace / Employee / Credential / Application / Group / Session / OTP / Audit foundation and local schema/auth acceptance.
3. [x] Implement `scrypt` credential flow, application-aware login, Identity sessions and login rate limiting.
4. [x] Implement provider-neutral Email Sender, Email OTP, bootstrap and configurable Workspace OTP security policy.
5. [x] Implement Employee activation, self password/Email lifecycle and password recovery.
6. [x] Implement Super Admin transfer + Recovery Email update + previous-authority session revocation.
7. [x] Implement Group/membership/direct+group Application Access and `USER_ADMIN` compatibility projection.
8. [x] Add development deployment pipeline and deploy/bootstrap the first development Workspace.
9. [x] Cut CY Web development login/session/logout over to CYCloud Identity and accept it in the browser.
10. [x] Add CY Web Shared Identity management UI and Super-Admin-only OTP settings.
11. [x] Protect the current Super Admin Employee from direct disable/delete and add pending-first-activation Employee deletion.
12. [ ] Manually deploy and accept CY Web `0.1.51` Employee lifecycle/Super Admin UI behavior.
13. [ ] Expand executable Worker acceptance for Employee activation, Email change/recovery, Super Admin transfer, Group/access mutations, live-session role changes, abuse limits and cross-Workspace rejection.
14. [ ] Add one ordinary non-Super-Admin development Employee and manually accept activation, login, Group/direct access, hidden Super Admin UI, and compatibility role projection.
15. [ ] Accept literal expired Identity session handling through CY Web; invalid-session handling is already accepted separately.
16. [ ] Manually accept CY Web self-service recovery/Email-change paths and, with a controlled test Employee, Super Admin transfer without risking lockout.
17. [ ] Remove post-bootstrap operational debt: change deployment so the one-time bootstrap secret is no longer required/rewritten after initialization, then remove it from runtime/GitHub only after that change is accepted.
18. [ ] Re-tighten the development Cloudflare deployment token to minimum permissions after deployment behavior is stable.
19. [ ] Publish the stable consumer handoff for CY Accounting Web and CYInvoice after the remaining CY Web acceptance items above are complete.
20. [ ] Switch additional development Apps to CYCloud Identity and accept their login/session/recovery paths.
21. [ ] Add low-frequency backup + restore acceptance compatible with the approved free Google Cloud usage envelope before production rollout.
22. [ ] CYInvoice performs its separate Device/desktop-safe migration and removes duplicate account-management ownership only in its own workstream.

## Explicitly deferred

- CYInvoice Device pairing / Device Token authority.
- CYInvoice Local → Cloud Employee transition.
- CYInvoice Windows offline credential cache.
- CYInvoice source/runtime changes before its dedicated migration workstream.
- Production DNS/custom domain and production Identity rollout.
- Paid Cloudflare/Email/Google Cloud plans.

## Continuity note

For AI/conversation handoff only, read `docs/HANDOFF_2026-09-29.md` after this TODO. The handoff is non-canonical and must not override `PROJECT_RULES.md`, architecture/contracts or later Business Decisions.
