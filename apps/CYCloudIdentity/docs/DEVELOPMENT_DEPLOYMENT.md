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

## Current CYInvoice deployment path — 2026-10-11

2026-10-11 使用者定案：保留刪除失敗的 expired／undeployed／no-D1 測試紀錄，停止 DELETE 重試／擴權，後續不再把測試 Worker／測試版本上傳 Cloudflare。遠端 probe／cleanup CLI、專用 runtime branches／dispatch modes及測試模組已移除；本地／CI synthetic Worker 與真實 core regression 繼續。舊 branches／歷史 run 不能作接續入口。

原 CYInvoice development deployment owner 改用 modern Worker version API，所有非 plain-text bindings（包含 Secrets、原 DB、rate limits、services）以 exact active version UUID 繼承；不從 latest upload 繼承、不讀／重設 Secret 值。僅上傳本次受檢查的真實 app.js，保留 Built-in／CYID_ENABLED=false；上傳後讀回完整 binding names/types/targets/vars與compatibility，重查原100%流量與routes穩定，再明確部署該新UUID。未知resource、split traffic、過期checkpoint、任一readback不符皆停止；response loss不自動重試，always-run核對實際active version／原history／health。過期紀錄存在不再單獨阻擋這條 exact-source deployment。原 D1／Workspace／Device／Token 不 reset 或 rejoin。

本輪本地／PR／實際 business deployment 驗證待精確 source 與 run 證據；不把官方API支援或 synthetic tests 當遠端通過。原最新實際安全核對 #23（38129363451）仍是 Built-in Cloud0.9.2／storage13，1 Workspace／4 Device records、history／FK核對通過；DELETE404／10339原因未知，1個expired測試版本仍在。私有recovery UTC2026-10-11T09:43:50.118Z。Temporary Workers Admin移除尚待使用者確認，無進一步擴權需求。

CYID private transport、真實 Application／兩 Workspace binding／Employee App Access與Role、原Windows裝置認證、RC AA／AB／AC、實際Free用量、portable清除中斷復原與最終authority cutover保持待驗收。後續透過真實consumer已存在的功能驗收，不新增遠端測試handler或帳號authority；production切換／Release仍獨立核准。產品VERSION／BUILD、Cloud0.9.2、Provider0.3.6、canonical1.0.3／minimum1.0.0不變。

The [modern version API](https://developers.cloudflare.com/api/resources/workers/subresources/beta/subresources/workers/subresources/versions/methods/create/) documents `type: inherit` plus an exact `version_id`. Only modern Worker version endpoints accept a concrete source UUID; legacy script uploads accept only `latest`. The [deployment API](https://developers.cloudflare.com/api/resources/workers/subresources/scripts/subresources/deployments/methods/create/) explicitly selects the reviewed version at 100%. Source/CI verification precedes any actual upload. Upload/readback failure leaves a real business version inactive; no DELETE or automatic rollback is attempted.

## Historical transport / cleanup evidence (retired execution paths)

These records explain the retained version; none is a current retry instruction or successful transport/positive authorization acceptance.

- #391 / #393 source `5dc0c4f7`: controlled temporary probe and private diagnostic implementation passed four exact-head workflows, 78 Provider tests and 21 focused regressions. Those probe-only modules/tests have now been removed; real provider auth/role/session/Email/deployment regression coverage remains.
- [Provider #139](https://github.com/simonliu1118-byte/CYapps/actions/runs/38119760514): token active, own-policy GET403/9109 unreadable, temporary Worker upload403 without numeric code; absence confirmed and original Provider deployment skipped. No binding invocation.
- #395 source `b3e60f70`, six CI checks; [consumer #17](https://github.com/simonliu1118-byte/CYapps/actions/runs/38121460188): `deploy=false` probe upload succeeded, version DELETE403 failed. Original service still100%. No independent request proof; cleanup exception obscured the request result. Old post-check used latest-upload `/settings`, which contained noD1; this was not deletion of the original D1. Private #17 before-checkpoint was removed, so later checks cannot claim comparison against that lost snapshot.
- #396 source `3b0a8c629d9de80e98ad0bd45ff63fa3cec9ddac`, tree `869575058427fa61218b095c2cff0e5f5fb23c21`, six distinct workflows, 43 focused/100 Provider tests; merge `f57c186b2afe879a89b8c150ce86eb2264053c46`. Active-version audit replaced latest-upload metadata. [Read-only #19](https://github.com/simonliu1118-byte/CYapps/actions/runs/38122167283), identical-tree continuity source `9724611df6b28ce2bd4f7313b5a20fbf489fdc2f`, passed original Cloud0.9.2/API1/marker8/storage13, Built-in/D1/FK/history preservation and one expired, undeployed probe with noD1. Recovery UTC `2026-10-11T07:41:20.449Z`.
- #399 source `a91bc561bc47dd06956fa18078104eefdcb5d6ea`, tree `a934ed2bb2b9406f6bdb1fb04dc12c7befda7eb3`, five CI checks, 57 focused/114 Provider tests; merge `ce2db3dd832886a25510ad4299a988372d4e12c7`. User confirmed temporary Workers Admin saved. [Cleanup #21](https://github.com/simonliu1118-byte/CYapps/actions/runs/38129017243) passed nonce/expired/unused ownership checks but DELETE returned404/10339; original post-check/private cleanup passed. Recovery UTC `2026-10-11T09:37:57.160Z`.
- #400 source `c53893aec3b54dcc102afc72ffbc487406083540`, tree `e5f6f6e537ae7421cc459a667d859e2827d7234d`, five CI checks, 60 focused/117 Provider tests; merge `53d583b511c881bd9eff04c93831eeec1cbf5310`. [Cleanup #23](https://github.com/simonliu1118-byte/CYapps/actions/runs/38129363451) verified immutable Worker ID/name and the same full version before DELETE, which still returned404/10339. Absence not established. Original business service/history/FK and private file cleanup passed. Recovery UTC `2026-10-11T09:43:50.118Z`.

Cloudflare's precise 10339 deletion refusal remains unknown. GET readback succeeded, so DELETE404 is not evidence that the Worker/version is missing. Admin did not resolve deletion. The current plan retains this expired history and replaces implicit latest inheritance; it does not delete the original Worker, promote a probe, restore D1 or recreate Workspace/Devices.
