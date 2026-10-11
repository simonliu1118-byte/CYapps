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
