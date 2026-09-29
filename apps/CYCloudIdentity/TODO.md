# CYCloud Identity TODO

> 本文件只記錄 current implementation status 與下一步，不是永久規則來源。

## Current checkpoint — 2026-09-29

- Current deployed development baseline remains `CYCloudIdentity` `0.1.14`.
- **Approved forward Identity model is now finalized** in `PROJECT_RULES.md` and `docs/ROLE_AND_ACCESS_MODEL.md`: Workspace roles are exactly `SUPER_ADMIN / ADMIN / USER`; `Identity Admin` is an ADMIN capability, not a fourth role; Application Access is entry authorization; App-local business/module permissions stay in each App.
- The deployed `0.1.14` runtime is **not yet migrated** to that model. It still contains extensible Identity Groups, Group/direct Application Access and optional `USER_ADMIN` compatibility-role projection. Those mechanisms are legacy implementation to be migrated, not a direction to extend.
- CYInvoice remains reference-only in this workstream; no CYInvoice source/runtime/D1 change is authorized here.
- Super Admin remains the protected Workspace authority pointer. Consumer-facing terminology is `超級管理員 (Super Admin)`; stable protocol fields may remain during migration.
- Target Employee role creation: normal ADMIN can create USER only; Identity Admin / Super Admin can create USER or ADMIN directly. Identity Admin capability itself is granted/revoked only by Super Admin.
- Target Access management: normal ADMIN has no App/Module Access configuration capability. Identity Admin / Super Admin manage eligible Employee Access; Identity Admin cannot modify its own Access or Super Admin Access.
- CY Web is the mandatory core account-management App: every valid Employee must retain CY Web entry access even with no business-module access. Other App Access defaults ungranted and is configured after Employee creation.
- CY Web Module Access remains CY Web-local. Super Admin gets all modules automatically; Identity Admin / Super Admin can configure eligible USER/ADMIN/other Identity Admin module access; an ADMIN with a module has full management authority in that module.
- Target role projection for all CYID consumers is direct: `SUPER_ADMIN -> SUPER_ADMIN`, `ADMIN -> ADMIN`, `USER -> USER`. Group-derived consumer roles are being retired.
- Future HR note is explicitly deferred: if a non-ADMIN HR role later needs Identity lifecycle authority, decompose narrower Identity capabilities then; do not add a fourth Workspace role now.
- Employee lifecycle target remains distinct: first-time `尚未驗證／待啟用`, activated `啟用`, previously activated `停用`, plus activated `Email 待驗證` when an authorized Email recovery is in progress.
- Only never-activated Employees may be physically deleted. Activated Employees are retained and may only be disabled/re-enabled.
- Target activated-account Email recovery: Identity Admin / Super Admin may replace unusable Email, keep the password/account activated, mark the new Email unverified, revoke existing sessions and resend verification. Normal ADMIN may not perform this.
- **Current first-activation email behavior is incomplete:** `handleCreateEmployee()` creates a pending Employee but does not send Email. Email OTP is currently sent only when the Employee starts activation. This explains why a newly created Employee produces no Brevo event. Target behavior is create -> automatically send first activation email containing a direct link to the CY Web activation flow; send failure must keep the pending Employee and allow resend.
- New credentials use `scrypt`; password boundary remains 8–16 Unicode characters. Session/OTP/provider-neutral Email security rules remain in force.
- Invalid provider-session handling through CY Web is already accepted; literal expired-session evidence remains outstanding.
- Production remains untouched.

## Active next sequence

1. [x] Audit the original Identity lifecycle and establish a dedicated CYCloud Identity authority.
2. [x] Implement/deploy the initial Workspace / Employee / Credential / Application / Group / Session / OTP / Audit foundation.
3. [x] Cut CY Web login/session/logout to CYCloud Identity and accept the first development Super Admin login/F5/logout path.
4. [x] Implement initial Employee activation/self-service/recovery/Super Admin transfer and initial management UI.
5. [x] Add pending-first-activation deletion and protect current Super Admin from disable/delete.
6. [x] Finalize the replacement role/access product contract: `SUPER_ADMIN / ADMIN / USER` + Identity Admin capability + independent App Access + CY Web multi-module exception.
7. [ ] Design a safe forward D1 migration from legacy Group-derived role authority to Employee Workspace Role + Identity Admin capability without breaking the existing development authority path.
8. [ ] Update CYID principal/login/resolve authorization to return/enforce the direct Workspace role and remove dependency on Group-derived `applicationRoleKey` as the forward authority.
9. [ ] Implement management authorization boundaries: normal ADMIN USER-lifecycle only; Identity Admin role/access/Email-recovery capabilities; Super Admin-only Identity Admin grant/revoke, Recovery/security policy and transfer.
10. [ ] Implement create-time Role selection and automatic first activation email with direct CY Web activation link; add pending edit/resend/send-failure state.
11. [ ] Implement activated-account forced Email recovery + re-verification + required session revocation.
12. [ ] Implement target Application Access semantics: CY Web locked entry TRUE; other Apps default ungranted; Super Admin automatic access; Identity Admin anti-self-escalation.
13. [ ] Migrate/remove legacy Identity Group role projection and `USER_ADMIN` compatibility-role UI/runtime once target replacement is validated. Do not remove data prematurely before migration acceptance.
14. [ ] Update CY Web management UI/adapter to the new Role + Identity Admin + Access contract, including CY Web Module Access management.
15. [ ] Create controlled ADMIN / Identity Admin / USER development accounts and accept each management boundary in the browser.
16. [ ] Accept activation email delivery/resend through the configured provider and confirm the direct CY Web activation link flow.
17. [ ] Accept role/access changes and immediate session invalidation behavior, including self-escalation rejection.
18. [ ] Obtain literal expired-session handling evidence through CY Web; invalid-session handling is already accepted separately.
19. [ ] Manually accept self-service forgot-password / own Email-change and protected Super Admin transfer without risking lockout.
20. [ ] Remove post-bootstrap operational debt and re-tighten the development Cloudflare deployment token after the new model is stable.
21. [ ] Publish stable consumer handoffs for CY Accounting Web and CYInvoice; implementation happens in their own workstreams.
22. [ ] Add low-frequency backup + restore acceptance before production rollout.

## Explicitly deferred

- Non-ADMIN HR Identity capability model until a real HR workflow exists.
- Fine-grained universal permission catalog inside CYID.
- CYInvoice Device pairing / Device Token authority.
- CYInvoice Local -> Cloud Employee transition and Windows offline credential cache.
- CYInvoice source/runtime changes before its dedicated migration workstream.
- Production DNS/custom domain and production Identity rollout.
- Paid Cloudflare/Email/Google Cloud plans.

## Continuity note

Read `docs/ROLE_AND_ACCESS_MODEL.md` before implementing further Identity authorization work. `docs/APPLICATION_ROLE_MAPPING.md` is now a legacy implementation note only. For AI/conversation continuity, read `docs/HANDOFF_2026-09-29.md` after this TODO.
