# CYID compatibility and deployment review

Evidence review: 2026-10-01 (Asia/Tokyo). This is an implementation review, not a permanent rule or a second progress tracker. Current status remains in `../TODO.md`; shared semantics remain in the canonical consumer contracts.

## Baselines and evidence

- CYapps main: `2a95557cf9da9d0553a1b8c39336ea002e17ff2b`; CYID source `0.3.3`, BUILD `0`.
- Production provisioning branch: `deploy/cycloudidentity-production`, commit `0e9dc3342cd581bf41090012aae0759bce026329`. Its CYID `src/` has no diff from the reviewed main source. The branch contains deployment work that is not integrated into main and has older unrelated consumer files; do not merge the entire branch into current main.
- [Production Provisioning run #12](https://github.com/simonliu1118-byte/CYapps/actions/runs/36703608442) completed successfully. Both jobs and the individual continuity, runtime deploy, secret configuration, provider verification and final resource gate steps succeeded; these steps were not skipped.
- The accepted provisioning establishes distinct production Worker/D1, durable authority continuity, CYACC registry/Workspace enablement and runtime Application/Workspace/Service Binding values. Transient sessions/OTP are excluded from continuity. The runtime probe injects a temporary hashed Session into D1 and exercises resolve/logout/revoke; it does **not** prove password login, Email delivery or browser acceptance.
- Production identifiers and credentials are intentionally absent from this report. Runtime state can change after a successful historical run; no current Cloudflare mutation or live re-provisioning was performed for this review.

## Conclusion

CYID does not have a version-numbered Worker wrapper chain. There is one `src/index.ts` dispatch entry. It nevertheless retains obsolete APIs, unreachable lifecycle implementations and duplicated active authority checks. These are concrete maintenance costs, not merely names containing `legacy`.

## Findings

| Finding | Executable evidence | Effect and disposition |
| --- | --- | --- |
| Active obsolete Group mutation API | `src/index.ts` still routes Group create/update/membership/access and Application compatibility-role-mode mutations into `src/identity-admin.ts` | The direct role/access model no longer reads Group grants for entry authority. Current CY Web UI does not use these operations, although its Worker still proxies them. Retire consumer proxies and provider routes together after a consumer inventory; retain applied schema migrations/history. |
| Obsolete projections still queried | `src/role-admin.ts:handleRoleAccessSnapshot` reads groups, memberships and groupAccess in addition to the direct model; `auth.ts` and `session-auth.ts` query Group keys | Snapshot incurs three legacy collection queries; Session resolve also performs a descriptive Group query. CY Web currently rejects principals without valid `groupKeys`, so remove that consumer dependency before retiring provider fields/queries. |
| Two active Session authority implementations | `auth.ts:sessionAuthority/resolvedPrincipal` serves the resolve endpoint; `session-auth.ts:requireIdentitySession` separately reimplements Session SQL and checks for management/self-service APIs | Same authority store, but duplicated logic can drift. Use one internal resolver and one principal construction path; keep endpoint serialization and admin capability checks as separate responsibilities. This is internal consolidation, not a second Session or a new cache. |
| Initial-login wrapper around permanent-login HTTP handler | `initial-access.ts:handleLoginWithInitialPassword` parses the cloned request and reads Employee/initial credential state, then calls `auth.ts:handleLogin` for ordinary login | Permanent login reparses and requeries through a second handler. Temporary and permanent credentials require distinct security branches, but one parsed dispatcher can select credential class without delegating HTTP handlers. Preserve ticket-not-Session, single-use, expiry and explicit re-login. |
| Employee PATCH split across new and old handlers | `initial-access.ts:handleEmployeeUpdateWithInitialPassword` delegates activated/non-pending targets to `employee-lifecycle.ts:handleUpdateEmployee` | This is one real wrapper with duplicated validation/policy/state queries, not an unbounded version chain. Consolidate lifecycle-specific behavior under one update operation; do not preserve the old pending OTP resend path as a fallback. |
| Unreachable old lifecycle and snapshot exports | No TypeScript call sites for `handleCreateEmployee`, `handleResendEmployeeActivation`, `handleStartEmployeeActivation`, `handleConfirmEmployeeActivation`, `handleIdentityAdminSnapshot`, `handlePutEmployeeApplicationAccess` | Old OTP-based first activation code and old snapshot/direct-access handlers remain in source but are not dispatched by current entry. Remove unused exports/helpers after checking the module dependency closure; do not delete the whole employee-lifecycle module because active delete/capability/Email recovery operations still use it. |
| Email budget SQL duplicated | `initial-access.ts` reserves/settles global and Workspace delivery budgets independently from `otp.ts` | Initial-password and OTP messages must share the same quota authority. Extract one budget operation if consolidating; do not merge credential purposes or weaken one-time/expiry rules. |
| Technical aliases and legacy credential verifier | `activation/resend`, `activationDelivery`, `applicationRoleKey`; `crypto.ts` accepts bounded PBKDF2 and creates new scrypt verifiers | Aliases are transport/data duplication, not another authority. `activation/resend` is still called by CY Web. PBKDF2 cannot be declared unused without a protected aggregate inventory of production credential algorithms. Coordinate field/endpoint retirement; never export actual credentials. |

The reviewed current CYACC Identity adapter does not reference `groupKeys` or `applicationRoleKey`. This does not prove that every deployed/external consumer is independent of them.

## Production provisioning is a one-time continuity operation

The workflow at the provisioning commit is **not suitable for ordinary production redeployment**:

1. It captures authority tables from development on every run. Import is gated on an empty production Workspace table, but the later `cyid-prod-employee-repair.sql` performs an **unconditional UPDATE of existing production Employees using development rows**. Re-running can revert production Employee attributes/lifecycle/role/capability state. Whole-table continuity comparison also assumes production still matches development.
2. It carries Group tables as continuity data. This is not ongoing Group authorization, but it preserves obsolete material and is not a reason to keep Group APIs indefinitely.
3. Its final probe requires the total `identity_sessions` table count to be zero, rather than only proving the temporary probe row was removed. That assertion becomes invalid once legitimate production users have Sessions. The workflow shown does not delete all legitimate Sessions, but it can fail after successful deployment.
4. Logout verification retries resolve up to ten times after database revocation. Historical success proves eventual rejection in this probe; it does not prove first subsequent request invalidation. Preserve strict immediate invalidation in future acceptance rather than treating the retry as a new allowed authority window.
5. Resource names are derived from protected development values and the job uses the `development` GitHub Environment, while targeting isolated resources with `APP_ENV=production`. Environment naming is not evidence of sharing the development Worker/D1, but bootstrap/continuity and routine production deployment need distinct operational entry points.

Next deployment work should retire the continuity/Employee-repair operation from the routine path and introduce one production deployment path using protected production configuration. Do not replay this provisioning branch to refresh status, and do not apply an entire older deployment branch over main. This report makes no remote data/configuration changes.

## Cleanup order

1. Address production workflow replay risk before the next production deploy.
2. Remove unreachable old handlers and consolidate internal Session checks; preserve existing consumer-visible behavior.
3. Make CY Web independent of descriptive Group fields and retire its unused Group proxies/types.
4. Inventory affected deployed consumers; update shared contract/changelog/version only when consumer-visible obligations change. Then retire provider Group endpoints/projections and obsolete aliases in the same coordinated work item. A compatibility version window is release governance, not a justification for arbitrary runtime wrappers.
5. Consolidate Employee/login dispatch and Email budget operations without changing credential-class/security boundaries. Applied migrations remain immutable; schema retirement, if necessary, is a separate forward migration.

## Verification and limits

Read dispatch/import/call sites, consumer routes/types, deployed-workflow source and run/job/step summaries. Locally ran CYID schema, bootstrap schema, security-policy and historical role-mapping validators successfully. The role-mapping validator tests historical schema constraints; passing it does not make Group grants current authority. This review did not execute production login, Email, browser/device, restore or protected credential-algorithm inventory and did not change runtime code.

## First remediation — CYID 0.3.4

- Resolve API and management/self-service guards use one `resolveIdentitySession` implementation. `requireIdentitySession` only selects the validated principal; it has no independent query/check path.
- Removed undispatched `handleCreateEmployee`, `handleResendEmployeeActivation`, `handleStartEmployeeActivation`, `handleConfirmEmployeeActivation`, `handleIdentityAdminSnapshot`, and `handlePutEmployeeApplicationAccess`. Active Employee management and first-login handlers remain.
- Production replay is disabled on the actual deployment branch by PR #254 and the same retired gate is recorded in main. No Secrets, imports, D1 writes, migrations, probes or deploy remain in that workflow. Production Worker source/data are unchanged.
- Provider public Group fields/routes remain pending consumer migration; no shared consumer semantic contract changed. Initial-access HTTP-handler chaining, shared budget operations and a routine production deployment workflow remain outstanding.

## Final source remediation — CYID 0.3.5 / Consumer 1.0.2

The findings above describe the reviewed historical baseline. The remaining source cleanup is now implemented:

- One Login HTTP handler parses and loads Employee/credential state once, then invokes permanent or initial credential operations. One Employee-update handler handles pending and activated lifecycles without re-entering another authenticated HTTP handler.
- Initial credential and OTP delivery share one budget reservation/settlement implementation, including global rollback when Workspace reservation fails and atomic sent/reserved settlement using the reservation date.
- Removed Group/role-mode dispatch and projections plus `applicationRoleKey`, `activationDelivery` and `/activation/resend`. Current direct Role/App Access fields and `/email-verification/resend-initial` are canonical. Historical physical tables are retained.
- CY Web 0.7.0 coordinates 1.0.2 adoption. Existing CYACC direct 1.0.1 integration does not consume retired fields/routes and remains in the supported window.
- Added a separate main-only manual production release workflow: read existing production settings and bound DB metadata, fail closed on isolation/binding/config drift, require deployed core consumer adoption, validate bundle, then forward migrations and source deployment. No development authority export/import, Employee repair, resource creation, secret replacement or synthetic Session insertion.

38 Node tests and TypeScript checks pass locally. CI must additionally pass actual Worker/D1 auth and first-login acceptance. Production release is not claimed: core-consumer production readiness, real Email/browser lifecycle and backup/restore retain their independent gates.

## Release evidence — 2026-10-01

CYID 0.3.5 and CY Web 0.7.0 are development-deployed, with actual version/binding/D1-health/invalid-Session readback in runs 36821423383 and 36821410198. Routine production readiness also checks the protected existing core Worker Service Binding, Application ID and consumer declaration; development consumer health alone is insufficient. No production release or real Email/browser acceptance is claimed.

## GitHub source follow-up — 2026-10-10

This section is a dated evidence addendum to the 2026-10-01 review, not a replacement of historical baseline conclusions. CYID `main` remains source version `0.3.5`; shared consumer contract remains `1.0.2` (minimum `1.0.0`). No provider/runtime/schema change was made in this review; **CYID Consumer Impact: NONE**.

| Source-verified concern | CY Web | CYACCweb | Interpretation |
| --- | --- | --- | --- |
| Transport | `IDENTITY` Service Binding / `env.IDENTITY.fetch()` | Same | Same private Worker-to-Worker architecture; no direct CYID D1 binding in consumer |
| Core endpoints | `/v1/identity/login`, `/v1/identity/session/resolve`, `/v1/identity/logout` | Same | Shared canonical CYID API |
| Session resolve header | Bearer token + `x-identity-application` | Same | CYID validates current app-scoped authority |
| Principal validation | Strict required field/role/capability and revision checks | More permissive booleans and version coercion | Harden CYACC consumer; provider currently constructs normalized principal |
| Login path | Set Cookie after provider Login | Login + immediate provider Resolve + Cookie | Extra CYACC resolve is not a second authority; measure before simplifying |
| Cookie attributes | HttpOnly, Secure, `SameSite=Strict` | HttpOnly, Secure, `SameSite=Lax`, explicit `Expires` | Intentional app-specific Tablet/Safari transport difference |
| Timeout | Default 5 s, covers provider body | Default 5 s configurable, primarily fetch promise | Align failure semantics without adding wrapping layers |
| Logout | Provider revocation before success; currently non-5xx response treated as success | Worker returns `ok: true` on provider failure and clears browser cookie; browser always navigates after attempt | Preserve clear-cookie safety; distinguish revoked/unconfirmed/request-failed states and test them |

Relevant read-only source: `chihyuan-web/worker/identity/cycloud-identity-adapter.ts`, `provider-transport.ts`; `CYapps/apps/CYAccountingWeb/src/identity-adapter.js`, `src/app.js`, `public/auth.js`; CYID `src/session-auth.ts`. Templates establish expected `IDENTITY` Binding but **do not prove** deployed production Binding points at the same provider or D1. Cloudflare current runtime bindings, actual quota headroom, real email/device and production rollout remain separate verification tasks (tracked once in `../TODO.md`).

Next Consumer: CYInvoice's planned `CyIdIdentityProvider` still needs Device/local/offline contract design; CYERPAutoInput has no existing CYID implementation and may not need its own cloud business D1. No selection/implementation is inferred here. Existing standard `CONSUMER_INTEGRATION_STANDARD.md` is unchanged; this report does not create an alternative contract or new permanent rules.
