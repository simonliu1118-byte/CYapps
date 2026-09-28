# CYCloud Identity — Development Deployment

> Development only. Production deployment, production DNS/custom domain and paid-tier changes remain out of scope.

## Public-repo boundary

The repository contains only a placeholder deployment template. Do not commit actual Worker names, D1 identifiers, Email sender addresses, provider credentials, OTP pepper, bootstrap secret, Workspace data, Employee data or real Application catalog/access data.

Generated `wrangler.*.generated.jsonc` files are ignored and must not be committed.

## GitHub Environment

The deployment workflow uses the existing repository Environment named `development` and CYCloud Identity-specific variable names so it does not overwrite other CYapps deployment configuration.

Environment variables:

- `CYID_WORKER_NAME`
- `CYID_D1_DATABASE_NAME`
- `CYID_D1_DATABASE_ID`
- `CYID_LOGIN_RATE_LIMIT_NAMESPACE_ID`
- `CYID_SESSION_TTL_SECONDS`
- `CYID_EMAIL_PROVIDER` (`brevo` or `resend`)
- `CYID_EMAIL_FROM`
- `CYID_EMAIL_DAILY_BUDGET`

Environment secrets:

- `CLOUDFLARE_API_TOKEN`
- `CLOUDFLARE_ACCOUNT_ID`
- `CYID_OTP_PEPPER`
- `CYID_BOOTSTRAP_SECRET`
- `CYID_BREVO_API_KEY` when using Brevo, or `CYID_RESEND_API_KEY` when using Resend

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

Development deployment runs only by explicit `workflow_dispatch` or by updating the dedicated `deploy/cycloudidentity-development` branch. It then:

1. validates required Environment values;
2. renders a temporary deployment config;
3. applies numbered migrations to remote development D1;
4. deploys the development Worker;
5. writes Worker Secrets through Wrangler without storing them in source;
6. removes the generated config.

No push to `main` automatically deploys CYCloud Identity.
