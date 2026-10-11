# CYCloud Identity — Development Deployment

> Development only. Production deployment, production DNS/custom domain and paid-tier changes remain out of scope.

## Public-repo boundary

The repository contains only a placeholder deployment template. Do not commit actual Worker names, D1 identifiers, Email sender addresses, provider credentials, OTP pepper, bootstrap secret, Workspace data, Employee data or real Application catalog/access data.

Generated `wrangler.*.generated.jsonc` files are ignored and must not be committed.

## GitHub Environment

The deployment workflow uses the existing repository Environment named `development` and CYCloud Identity-specific variable names so it does not overwrite other CYapps deployment configuration.

Existing-provider upgrade inputs:

- `CYID_WORKER_NAME` and `CYID_D1_DATABASE_ID` identify the original development resources.
- Environment secrets `CLOUDFLARE_API_TOKEN` and `CLOUDFLARE_ACCOUNT_ID` allow the provider-owned deployment audit and Worker upgrade. They are not exposed to PR validation.

Runtime vars, existing D1 metadata, rate limit namespaces/limits, service bindings and Secret names are read from the existing Worker. Existing Secret values are neither read nor replaced. The bootstrap/Email Secrets already configured on the Worker remain there; GitHub copies are not replayed during an upgrade. First provisioning and future schema migrations are separate reviewed work, outside this code-only upgrade path.

Secrets must never be pasted into source, PR descriptions, Actions inputs or chat-visible logs.

## Free-tier guard

`CYID_EMAIL_DAILY_BUDGET` is the shared provider hard ceiling used by Identity. Choose a value that remains below the approved provider Free-tier daily quota and leaves operational headroom. Workspace-level daily limits can be lower but cannot exceed this system ceiling.

The established Workspace OTP defaults remain 60-second resend cooldown, 5 verification attempts, 5 sent OTP messages per Email + purpose per hour, and Workspace daily Email limit 100 subject to the lower global ceiling. OTP validity remains 10 minutes.

## Workflow behavior

Pull requests run validation only:

1. type-check and tests;
2. render a synthetic CI config;
3. apply migrations to local D1;
4. Wrangler dry-run bundle validation;
5. Public source safety scan.

`workflow_dispatch` defaults to `preflight`. Updating `deploy/cycloudidentity-development-preflight` also runs only the read-only path:

1. require original development Worker/DB, valid current runtime settings and required Secret names;
2. require the exact canonical applied migration list; any missing/extra/pending migration blocks this code-only release;
3. compare bounded, hashed authority metadata and history counts, and check foreign keys without reading passwords, credential verifiers, Session tokens or Email addresses;
4. capture the private D1 Time Travel bookmark and previous Worker deployment reference; publish only the UTC checkpoint timestamp;
5. dry-run the source against the privately generated configuration copied from the current Worker.

Only `workflow_dispatch` mode `upgrade` or the dedicated `deploy/cycloudidentity-development` branch additionally deploys source and performs the post-check. Development runs serialize without cancelling an in-flight upgrade. No push to `main` automatically deploys CYCloud Identity.

The upgrade does not run remote migrations, bootstrap, authority writes, Secret replacement or an automatic restore. Post-check requires the source version, unchanged settings and authority metadata, nondecreasing history counts, clean FK and rejection of a synthetic invalid Session. Concurrent authority edits cause a stop for investigation, not an automatic D1 restore. History-count checks do not establish a full record-by-record backup.

Private configuration/checkpoint/log files exist only on the runner and are removed even on failure; they are never uploaded as artifacts. Cloudflare retains the Time Travel history and Worker deployment history. The recorded UTC timestamp allows an authorized operator to retrieve the recovery reference again. This is a recovery checkpoint, not a completed restore rehearsal. A failed post-check may mean code has deployed: inspect current health/config and the original checkpoint before any reviewed recovery. Do not replay bootstrap or reset the Workspace.

Health and invalid-Session checks do not prove the named private RPC, real Employee/App Access, original Windows Device continuity or production readiness; those remain separate acceptance gates. Existing consumers in the supported contract window remain on their original authority and settings.

## Verified 0.3.6 development upgrade — 2026-10-11

PR #384 source `348185fb8a5237f1491806fbe36cbd2bb16ac3e2` passed Governance, provider Check and both deployment PR validation workflows. Read-only [run #123](https://github.com/simonliu1118-byte/CYapps/actions/runs/38110828710) passed with mutation/post-check steps skipped. [Actual development upgrade #124](https://github.com/simonliu1118-byte/CYapps/actions/runs/38111211344) used `56c2768594ee7d11506d16bfa1623f2deff9f1ad`, identical tested tree, and verified 0.3.6, seven already-applied migrations, unchanged settings/Secret names/hashed authority metadata, nondecreasing history counts, FK and invalid-Session refusal. Recovery reference timestamp: `2026-10-11T04:20:13.295Z`.

CY Web canonical mirror 1.0.3 sync merged in #145; its runtime remains consumer 1.0.2 within the supported window. No production deployment, bootstrap, new account/Session, binding/scope change or restore was performed. Actual named RPC transport, original CYInvoice authorization/Device continuity and Windows acceptance remain pending.

## Isolated private binding transport probe

`binding-probe` dispatch mode or `deploy/cycloudidentity-development-binding-probe` performs the original read-only provider preflight, then creates one random, short-lived consumer Worker with default `IDENTITY` and named `IDENTITY_AUTHORITY` (`ConsumerAuthoritySync`) bindings to that same development provider. It has no D1, account catalog or authority mutation API. A fresh secret gates the only probe route; the secret belongs only to the temporary Worker, never the original provider. Fixed synthetic requests check health, invalid Session refusal and unavailable-scope cache invalidation; no Login/bootstrap/Email/Session/grant is created.

After success or failure, cleanup checks its unique owner nonce and exact provider binding targets before deleting only that probe Worker and confirming absence. A changed owner/target or D1 binding blocks deletion; it never guesses or deletes the provider. Configuration/keys/CLI output stay private, are removed from runner storage and are never uploaded. The workflow remains serialized; probe mode skips the original-provider deployment/post-check steps. Bounded GET observations may wait for the temporary Worker route to become available, without replaying a mutation. No paid plan or persistent resource is requested. Transport/deny proof cannot establish real Application/Workspace enablement, Employee authorization or Windows acceptance. Actual transport success remains pending; current run evidence is below.

## Private binding probe blocker — 2026-10-11

The controlled upload path `4dc28ff6` merged in #391. Current diagnostic source `5dc0c4f77c50cf7bbc62c87cf4d3abadff13471b` merged in #393 as `8c036f0d4cea464a8cd0885a70898859e5de1ae3`, with the same tested tree `f85c3a920bd74095aed4e8a98616e0ad4961af6d`. All four exact-head PR workflows passed: Governance #1227, Provider Check #119, development validate #138 and production validate #25 (PR deployment jobs skipped). TypeScript, all 78 Provider tests and real local D1 acceptance passed; 21 focused probe regressions and local probe bundle cover diagnostics, privacy, optional policy refusal, exact bindings, partial creation cleanup and changed-scope deletion refusal. Product/contract versions are unchanged.

Pinned Wrangler performs only a local dry-run bundle. The owner-scoped controller uploads the single module through the [documented multipart Worker API](https://developers.cloudflare.com/workers/configuration/multipart-upload-metadata/), configures its own secret and enables only its [workers.dev route with previews disabled](https://developers.cloudflare.com/api/resources/workers/subresources/scripts/subresources/subdomain/). Raw private CLI output and API error messages are never published. The intermediate CLI hint/classification path has been removed.

The same controller now projects failed platform responses to a fixed stage, HTTP status/numeric API code, JSON/HTML/text category and boolean envelope/Cloudflare/intermediary-denial/error-document indicators. It never copies body text, header values, documentation URLs, paths or resource identifiers into logs. Before upload, optional read-only [account-token verification](https://developers.cloudflare.com/api/resources/accounts/subresources/tokens/methods/verify/) or [user-token verification](https://developers.cloudflare.com/api/resources/user/subresources/tokens/methods/verify/) reports only the recognized status. Only the verified current token's details may be read; no token list, creation, modification or secret retrieval is performed. An active token is not proof of upload permission. Unreadable verification/policy remains unknown and does not request broader token-read access; an explicitly disabled/expired token stops before mutation. An exact Worker-write permission name on a matching allow scope is only a diagnostic hint: names are cosmetic, deny policies/conditions may apply, and only the actual controlled request establishes capability. The 21 focused regressions cover these distinctions and privacy boundaries; actual probe evidence is tracked separately below.

[Actual development probe #136](https://github.com/simonliu1118-byte/CYapps/actions/runs/38113320985), exact source `4dc28ff6`, passed source validation and the original Provider preflight (0.3.6, seven applied migrations, no pending migrations, original settings/Secret names and FK clean). Temporary Worker upload returned **HTTP 403 with no numeric API code**. Its absence was confirmed afterward; local private files were removed. Original Provider upgrade/post-check steps were skipped, and no authority/Session/Secret/schema write was made to that Provider. Earlier #126/#128/#130/#132/#134 runs are failed diagnostic history, not transport success.

[Actual development probe #139](https://github.com/simonliu1118-byte/CYapps/actions/runs/38119760514), exact diagnostic source `5dc0c4f7`, passed validation and the original Provider preflight. Account-owned token verification returned **active**. Reading that current token's own policy returned **HTTP 403 / API code 9109**; therefore its policy and Worker-write scope are unknown, not known absent. Temporary Worker upload again returned **HTTP 403**: valid JSON envelope, Cloudflare server/Ray markers present, no numeric API code or intermediary-denial header. These facts narrow the observation beyond #136 but do not identify the specific permission/policy causing rejection. No binding invocation happened. Temporary Worker absence was confirmed, original-provider upgrade/post-check steps were skipped and private files were removed.

The separate temporary-Worker path requires a Cloudflare administrator to inspect the protected development credential's target account, Worker upload/delete authorization and relevant API rejection record. The connected tools cannot inspect or change token administration; no credential should be copied into chat, public source or artifacts. Do not add token-read permission merely for diagnostics, infer Worker-write denial from policy-read refusal, broaden permissions automatically or repeat uploads without a reviewed configuration/capability change. Do not redeploy the original Provider as a probe or replace a live consumer handler. The user subsequently authorized controlled original-development verification while all devices are closed; the undeployed-version alternative below preserves the active deployment. Otherwise, once isolated upload/cleanup capability is confirmed, rerun this same serialized controller. Transport/deny remains unverified until a successful actual run and cleanup; positive Application/Workspace/Employee authorization, cost, original Windows continuity and production approval remain separate gates.


## Authorized existing-consumer version verification — 2026-10-11

The user confirmed all current devices are closed and authorized verification against the original development consumer. The existing `cyinvoice-cloud-dev-deploy.yml` owns `binding-version` dispatch mode and the dedicated `cyinvoice/cloud-dev-binding-version` branch. It reuses this project's single probe controller and module; it does not create a second authority or replace the active business handler. Source validation is separate from actual remote acceptance.

After the original consumer's read-only recovery/history checkpoint, the controller requires the same development Worker, original consumer D1, Built-in flag, current bindings and traffic deployment. Existing Version URLs must already be enabled; it does not enable global previews or change routes. Pinned Wrangler bundles locally. The [Workers version API](https://developers.cloudflare.com/api/resources/workers/subresources/beta/subresources/workers/subresources/versions/methods/create/) accepts an existing Worker name and JSON/base64 modules; the request explicitly sets `deploy=false`. The version has only fixed probe vars, the two private bindings to the same development Provider, and one fresh probe-only secret. It has no D1 or original runtime Secrets. Its authenticated route expires after ten minutes. No consumer/Provider migration, login, bootstrap, authority write, business deployment or CYID flag switch occurs.

Default Fetcher health/invalid Session denial and named RPC unavailable-scope invalidation establish transport only. If all three approved scope variables are configured in the protected development environment, the original CYInvoice Workspace ID must match the existing checkpoint fingerprint before upload. A zero-authority RPC additionally checks approved CYID Workspace/Application enablement, returning booleans only. Missing configuration remains unknown; scope availability does not establish Employee/App Access/Role or Device Token authorization. No scope is guessed or registered.

Success and failure cleanup discover only the nonce-annotated version, verify exact private binding targets and no D1, and refuse to delete an in-use or changed-owner version. Only the [owned unused version](https://developers.cloudflare.com/api/resources/workers/subresources/beta/subresources/workers/subresources/versions/methods/delete/) is deleted; never the original Worker. Active deployment, original settings/Secret names and routes must still match. The workflow's always-run consumer post-check verifies original Workspace/Device token hashes and history, then removes private runner files without publishing them. If upload capability, existing previews or cleanup is refused, stop and report the fixed diagnostic; do not broaden credentials or force rollback. Windows portable backups, original-device positive authorization, reconnect/revoke behavior, cost and final cutover remain separate gates.

Actual existing-version #17 evidence is recorded below. Historical temporary-Worker #139 remains a failed upload, not transport proof. Product VERSION/BUILD and contract are unchanged by this tooling.


## Existing-version cleanup refusal and active-deployment audit correction

PR #395 source `b3e60f7050cee2b961cebfbc78c5c22b4423c5f7`, tree `11f7ee2ff569b577701a3058aca5797dba17b659`, passed all six relevant PR workflows (including Windows Cloud contract/startup/package) and merged as `6b61b0ef18b6defdf2c985b1f5143f057d23eb0a`. Local validation included 41 focused/98 total Provider tests. [Actual CYInvoice development #17](https://github.com/simonliu1118-byte/CYapps/actions/runs/38121460188) used that exact source and passed validation/original preflight: Cloud 0.9.2, storage 13, no pending migration, one Workspace/four Device records, Built-in/original D1/FK clean. Recovery UTC `2026-10-11T07:28:33.110Z`.

The undeployed version upload succeeded. Ownership/bindings/no-D1 and not-in-use checks permitted its cleanup attempt, but version DELETE returned Cloudflare JSON HTTP 403 with no numeric API code or intermediary-denial marker. Absence is **not confirmed**; the probe version remains to be reviewed. The cleanup exception obscures the request result in this source, so this run is not recorded as transport/scope success. Normal deployment/migration/authority-switch steps were skipped and private runner files were removed. The old post-check failed `ORIGINAL_DATABASE_BINDING_REQUIRED` after reading `/settings`, which follows the latest upload rather than necessarily the traffic deployment; this is not evidence of a D1 deletion.

The existing consumer audit now obtains the single 100%-traffic deployment, reads that version's bindings, strips any secret values and rechecks deployment stability. It no longer derives the runtime D1/vars/Secret names from latest-upload settings. The shared probe controller reuses this audit helper and refuses another upload while a prior probe version remains. Read-only preflight now also performs a history/health post-check and classifies retained probes by nonce, exact Provider bindings, no D1, unused status and expiration. Ordinary deployment is blocked before migration/upload while a retained probe could contaminate secret inheritance. Request proof is logged separately from cleanup in future runs. No forced restore/redeployment or credential widening is performed. Local 43 focused/100 Provider tests and consumer audit regressions pass; exact source CI and active-service readback passed as recorded below.

Read-only active-deployment/history verification is complete. Resolve the existing owned unused version's deletion refusal with Cloudflare administration before another upload or ordinary deployment. Never delete the Worker, reset/rejoin a Device or automatically restore D1. Real Employee/App Access/Role, original Windows authorization and final cutover remain pending.


## Active-service readback passed; cleanup still blocked

PR #396 source `3b0a8c629d9de80e98ad0bd45ff63fa3cec9ddac`, tree `869575058427fa61218b095c2cff0e5f5fb23c21`, passed all six distinct required workflows (Governance #1233/#1234, Provider Check #122, development validate #142, production validate #28, consumer development validate #18 and Cloud Check #397 including Windows). It merged as `f57c186b2afe879a89b8c150ce86eb2264053c46`, identical tested tree. Product/contract VERSION/BUILD unchanged.

[Actual read-only CYInvoice development #19](https://github.com/simonliu1118-byte/CYapps/actions/runs/38122167283) ran continuity commit `9724611df6b28ce2bd4f7313b5a20fbf489fdc2f` with that same tree on the existing preflight branch. Validation, original preflight and active Cloud/history post-check passed. Recovery UTC `2026-10-11T07:41:20.449Z`. The active business version is still Cloud 0.9.2/API 1/marker 8/storage 13 with runtime-sync capability, Built-in and CYID disabled. Original D1/FK are healthy; one Workspace/four Device records were observed, and captured Workspace/Device token-hash/employee/history fingerprints were retained across this **read-only #19** window. #17's private pre-upload checkpoint was removed, so this does not claim a fresh byte-for-byte comparison against its lost snapshot or Windows Device authentication.

One retained probe version was confirmed **expired, undeployed, correctly nonce-annotated/bound, with no D1**. #19 did not upload, delete, migrate, deploy, restore, change secrets or switch authority; all mutation/probe steps were skipped and private runner files removed. The original Built-in service can continue to be used. The 403 deletion refusal and retained-version cleanup remain open; neither #17 nor #19 is a completed private transport/positive authorization acceptance.

An authorized Cloudflare administrator must resolve deletion of only that annotated unused probe version (find its private reference in the original consumer's version list), retaining the original Worker and active deployment. Do not rerun historical #17, use latest-upload metadata for runtime recovery, broaden credentials blindly or erase the Workspace. Once absence is verified, use the current guarded owner for request proof/cleanup, then approved scope and original Windows Employee/App Access/Device acceptance. Final authority cutover, portable backup/restore rehearsal and Release remain unapproved gates.


2026-10-11 管理端受控清理接續：使用者明確同意並已儲存暫時 Workers Admin 權限，本次作業完成後移除；這是一次性操作授權，不是永久 Token policy。截圖顯示原 Workers Editor 可上傳既有版本但未含 Admin，無 expiry／IP 限制；不可先把單一版本 DELETE 403 宣稱已解決。沿用唯一 private-binding-probe controller，新增 retained-version-cleanup CLI 與同一 CYInvoice development workflow 的 binding-cleanup mode／專用 branch。此入口不 bundle／upload／deploy，只允許清理 checkpoint 中唯一已過期、nonce／完整 UUID／同 Provider 雙 bindings／僅 probe Secret／無 D1 的未部署版本，刪除前重查 original 100% traffic，刪除後確認 GET 404／清單 absence／原 active bindings及routes，workflow always-run history／health 核對。Fresh probe 與 retained cleanup 共用同一刪除 owner，不重跑歷史 #17、不刪 Worker、不重設 Workspace／Device、不切 CYID。先完成精確 source CI，再執行 actual cleanup 與 current guarded transport proof；真實權限／Windows／最終切換仍待驗收，VERSION／BUILD／consumer contract 不變。
