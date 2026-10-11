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

After success or failure, cleanup checks its unique owner nonce and exact provider binding targets before deleting only that probe Worker and confirming absence. A changed owner/target or D1 binding blocks deletion; it never guesses or deletes the provider. Configuration/keys/CLI output stay private, are removed from runner storage and are never uploaded. The workflow remains serialized; probe mode skips the original-provider deployment/post-check steps. Bounded GET observations may wait for the temporary Worker route to become available, without replaying a mutation. No paid plan or persistent resource is requested. Transport/deny proof cannot establish real Application/Workspace enablement, Employee authorization or Windows acceptance. Actual run evidence remains pending until recorded here/TODO.
