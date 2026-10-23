# CYInvoice Cloud Reference Backend

目前 canonical migration 為 0020，原始碼 SCHEMA_VERSION=20。原 D1／Worker 仍以本機即時盤點為準。受控升級依序 18→19→20，每段先驗證 D1 的精確 migration 前綴及資料不變，再指定 CYINVOICE_SCHEMA_UPGRADE=18:19 或 19:20，完成 before／deploy／after。canonical20 下仍支援精確 schema19 中繼，不允許跳版或以一般 code-only 改 marker。四道折讓旗標關閉，原 CYID、Secrets、Bindings 及其他 vars 完整繼承。結構升級不需等待 Local 停送；g0501 仍無送出路由，啟用前另核對 Local 停送、未知 Claim、單一權威與測試公司卡。操作清單見 ../docs/V3_2_C_CLOUDFLARE_MCP_HANDOFF.md。

> **2026-10-18 最新 Source 狀態**：CYInvoice V3.0.0 Build 0 已正式發布；3.1 的非折讓 Work Item 程式已按 #482／#483／#485／#486／#487／#489／#490／#491 合併 `main`，包括 VOID／UPLOAD_ISSUE、管理員跨機待辦與 append-only 稽核草稿。**3.1 的 `cloud/drafts/0016_non_allowance_work_items.sql` 尚未成為正式 migration、遠端原 D1 未因 Source 合併而升級，`WORK_ITEMS_ENABLED` 預設關閉。** 3.1 真正 D1／Worker 升級要留到開發最後，由使用者本機 Cloudflare MCP 處理。以下 V2.6.19／Cloud 0.9.3 段落為歷史快照；現況以 [3.1 交接](../docs/V3_1_HANDOFF.md) 為準。

Cloudflare Worker + D1 reference implementation for CYInvoice coordination. **V3.0 source supports CYID-only online Employee authority; Local requires no Worker.** Requests with missing or disabled CYID authority fail closed before any Device or legacy account mutation. Built-in/self-hosted deployments are historical and are no longer available as current V3.0 Worker operation. Prior releases, migrations and historical records remain preserved; this code change does not delete any D1 tables.

AMEGO remains authoritative for invoice / void / allowance business state. Existing Windows business calls still use the original local DPAPI-protected App Key. V3.0 introduces a staged encrypted CYID company-card store, but the Worker API is DISABLED by default and requires COMPANY_SETTINGS_ENABLED=true and a reviewed schema >=15; the company-card GET/UPDATE API only returns safe status, never secrets. Secure use of the new card by Windows AMEGO/MO paths is not yet shipped and must not be inferred from this route.

The identity contract is defined in `../docs/CLOUD_IDENTITY_LIFECYCLE.md`; engineering status is in `../docs/CLOUD_ARCHITECTURE_STATUS.md`. Windows V2.6.19 Build 0 is released; source and the original development Worker are Cloud 0.9.3 with CYID_ENABLED=true and the company D1 in CYID_ONLY profile. Predecessor PRs #216/#379 were merged through #380. Deployment and private maintenance evidence are summarized in [the current handoff](../docs/V3_1_HANDOFF.md); remaining acceptance/implementation is tracked only in [TODO](../docs/TODO.md).

## CYID-only D1 Employee table retirement (company maintenance completed 2026-10-13)

[Original Worker deployment](https://github.com/simonliu1118-byte/CYapps/actions/runs/38396129643) completed at 0.9.3 with CYID_ENABLED=true and all bindings preserved. Separately approved company D1 maintenance is complete: private encrypted backup, local restore and maintenance rehearsal, physical retirement, exact retained-record comparisons and clean FK/health readback. Sensitive IDs, data, backup files and recovery details remain private. The full live Windows role/multi-device matrix is still unverified; this status does not authorize maintenance on another D1.

The Chihyuan CYInvoice installation has switched to CYID. **Its own** D1 no longer needs the Built-in Employee account tables, but it must preserve its Workspace, Devices, token hashes, pairing/invitations, shared pairing OTP, and security audit history. The original maintenance operation did not remove Built-in functionality from historical releases; **the V3.0 Worker entrypoint now retires that runtime mode**. This branch does not issue or deploy another Worker, remove historical account records, or modify previously applied migrations.

- New optional maintenance SQL: `maintenance/retire-built-in-for-cyid-only.sql`. Deliberately **outside** `migrations/`, so normal Built-in and deployment migrations NEVER remove an Employee table.
- It preserves legacy invitation issuer IDs as **historic opaque strings**, removes the old Employee FK before removing the employee/transition tables, and keeps CYID issuer IDs separate. Shared `email_otp_challenges` remains for Device pairing; only account-specific challenges are removed.
- A `cyid_storage_profile` marker identifies an intentionally retired CYID-only schema. Deployment audits support both original Built-in and CYID-only profiles, fail closed on mismatches, and refuse ordinary deployments that change profile.
- Runtime compatibility removes a hidden `cloud_employees` SQL reference in CYID Device rename; Built-in Device rename still checks the Built-in Employee table.
- Maintenance is **not** run by any CI, GitHub workflow or cloud deployment. The prerequisite is a separately approved, private, atomic and recoverable operation against the verified original business D1, after a compatible Worker deployment and read-only inventory. The source change itself does not execute maintenance; the company operation was separately approved and completed on 2026-10-13 without a test Worker. See `../docs/CLOUD_IDENTITY_LIFECYCLE.md` §20.

## Current compatibility

V3.1-D source now includes canonical `0016_non_allowance_work_items.sql` and storage marker 16. The SQL behavior is unchanged from the reviewed draft. Work Item routes remain opt-in; a source merge does not enable `WORK_ITEMS_ENABLED` or deploy resources. Apply only to the verified original schema15/CYID_ONLY database after a private recoverable backup and continuity checks; do not replay 0014/0015. Actual remote state is recorded in private local MCP readback. The storage13 deployment details below are historical checkpoints.

- Service: `cyinvoice-cloud`
- Cloud implementation source and original development Worker: `0.9.3`, CYID_ENABLED=true; company storage profile `CYID_ONLY`.
- API: `1`
- Legacy API compatibility marker: `schemaVersion=8`
- Actual storage schema: `storageSchemaVersion=13`
- Migrations: `0001` through `0013`
- Worker entrypoint: `src/app.ts`

`wrangler.jsonc` contains a local/CI D1 placeholder and the source storage configuration. Remote development uses the existing Worker's bound D1 after verifying its deployment environment and database metadata; its concrete ID is injected only into a private generated config. Applied migrations are immutable; future changes must use new forward migrations.

> Current development [deployment](https://github.com/simonliu1118-byte/CYapps/actions/runs/38396129643) verified Cloud 0.9.3 / API 1 / compatibility marker 8 / storage 13 with CYID enabled and original bindings preserved. Separately approved CYID_ONLY D1 maintenance retained original business history and Device Token hashes with clean FK readback. Runs #8/#12/#39/#42 below are historical rollout/preparation/activation checkpoints. Full live acceptance and 0-Device recovery remain pending.

## Development commands

```bash
npm install
npm run check
npm test
npm run deploy:dry-run
npm run db:migrate:local
```

Remote actions must be deliberate:

Use the reviewed development workflow below. The default config has no runnable remote D1 ID; do not replace its placeholder with a real identifier in Git. Direct remote commands require the preflight-generated private config and the same deployment/history gates.

Do not infer remote deployment state from source or CI alone.

### Preserving an already activated installation

The source storage template targets migration 0015. A code deployment of an
already CYID-enabled installation preserves its active schema marker, company
settings/recovery flags, fixed consumer scopes, service bindings and secrets.
Changing those protected values requires a separate maintenance operation; the
deployment audit rejects a stale generated configuration before upload.

The protected `development` Deployment Environment can select the provider with
the consumer-specific secret `CYINVOICE_CYID_WORKER_NAME` (falling back to the
existing `CYID_WORKER_NAME` secret). `CYINVOICE_CYID_ENVIRONMENT` must explicitly
select `production` when appropriate; otherwise it defaults to `development`.
This selects the Identity provider environment, not the Invoice Worker's
environment. Production selection requires an already activated consumer and
does not perform an authority switch. Its two active service targets, scope and
original business Workspace must still match. Provider metadata is not App
Access authority: a missing legacy static scope does not imply a missing
registry entry and does not authorize registration or direct Identity D1 reads.

If retained, expired probe versions belong to an earlier provider, pin that
provider separately with `CYINVOICE_RETAINED_PROBE_WORKER_NAME`. This input only
checks historical inactive versions; it never changes the active service target,
executes a probe, or deletes a version. Keep all actual service names and resource
identifiers in protected deployment inputs, never in public source or artifacts.

Use the workflow's default `preflight` mode for the first remote verification.
It reads metadata and the original business D1, writes a private runner checkpoint,
and performs health checks. It does not migrate, upload or deploy a Worker,
issue/redeem invitations, or revoke Devices. Live role, Email and multi-device
recovery acceptance are separate from deployment checks.

## Runtime secrets

Secrets must be configured as Worker runtime secrets and never committed:

- `BOOTSTRAP_KEY`
- `OTP_PEPPER`
- `BREVO_API_KEY` or another provider credential
- sender identity / `EMAIL_FROM`

Do not commit Cloudflare API tokens, account keys, passwords, Device Tokens, OTP values, recovery codes, AMEGO credentials, real Employee Email addresses, or production data.

## Identity model

### Device

- Workspace ID: Cloud-generated.
- Device ID: Cloud-generated.
- Device Token: Windows-generated before the request, protected locally before network submission.
- Cloud stores only Device Token hash.
- Pairing Code authorizes a Device to join a Workspace; it does not grant Employee role.
- New devices join by a 10-minute one-use pairing code or a 72-hour one-use emailed invitation code. The latter also requires the current verified SUPER_ADMIN credentials. Neither path asks the new device to enter a Workspace ID.
- The API URL is entered by the new device from the connected machine or invitation email. It is not embedded in the Windows client.
- Pairing and invitation issuance, verification, denial, revocation, and device joining write Cloud security audit events without storing code, password, OTP, or Device Token plaintext. The audit viewing UI is deferred.

### Employee

Before cutover, Local EmployeeStore remains the single authority. Device onboarding then runs a whole-device Employee Transition over all existing Local Employees.

Identity matching:

```text
Employee No absent + Email absent
  → new Cloud Employee after own Email verification

Employee No + Email both identify the same existing Employee
  → directly adopt that Cloud Employee

partial or divergent match
  → pending conflict for current Workspace SUPER_ADMIN
```

Name is display data, not matching authority.

After cutover:

```text
Cloud Employee = only account authority
Local data = synchronized cache + protected offline credential verifier
```

A network outage does not switch the machine back to the old Local Employee authority.

## Execution-time authorization

CYInvoice has no persistent application login. Sensitive Cloud operations include Employee No + password at execution time; the backend verifies the credential and current role for that request.

Central account mutations are Online-only:

- Employee create.
- name / Email update.
- `ADMIN ↔ USER`.
- enabled state.
- password change / reset.
- pending identity resolution.
- SUPER_ADMIN transfer.

New or changed Email is committed only after OTP verification where required.

Online desktop authentication refreshes current central authority before each protected operation. Only transport outage/timeout permits the last trusted protected cache; HTTP rejection, revoked Device, malformed data, Workspace mismatch and caller cancellation fail closed.

## Device lifecycle

Revoke preserves Device history and audit while invalidating the old Token. Rejoining creates a fresh identity. Built-in active Workspaces keep LAST_ACTIVE_DEVICE protection. The Windows Cloud-to-Local reset uses double confirmation, shutdown, revoke/self-status confirmation and local wipe; unknown results preserve local state for startup recovery. Windows does not disable/delete/purge Workspaces.

USER is the current role vocabulary. Forward migration 0010 converts legacy EMPLOYEE storage; 0011 adds revoked-device lifecycle state. No active role alias is retained.

## SUPER_ADMIN

Each Workspace has exactly one enabled `SUPER_ADMIN`.

Transfer is a dedicated high-privilege operation:

```text
current X password re-auth
  → OTP to X verified Email
  → backend rechecks X and target Y
  → atomic X=ADMIN, Y=SUPER_ADMIN
  → Workspace Recovery Email moves to Y verified Email
```

Normal role update cannot create or remove `SUPER_ADMIN`.

## Main endpoint groups

Foundation / Device:

- `GET /v1/health`
- `GET /v1/onboarding/status`
- `POST /v1/onboarding/bootstrap-email`
- `POST /v1/bootstrap`
- `GET /v1/device`
- `GET /v1/devices` (inventory)
- `POST /v1/devices/revoke` (execution-time SUPER_ADMIN)
- `GET /v1/devices/self-status` (narrow terminal-state recovery)
- Device pairing authorization / create / claim routes

Employee Transition:

- whole-device transition inspection / actions
- Employee Email verification
- pending conflict list / resolution
- authority snapshot / cutover

Central account management:

- Employee create challenge / confirm
- Employee update challenge / confirm
- enabled update
- password update
- SUPER_ADMIN transfer challenge / confirm

The legacy single-account mutation route:

```text
POST /v1/employees/reconcile-local
```

is retired and fails closed with `LEGACY_EMPLOYEE_RECONCILIATION_RETIRED`. The arbitrary old Employee import window is no longer part of the reference backend.

`GET /v1/employees` remains read-only compatibility only; current Windows Cloud authority/cache flow uses the Employee Authority snapshot contract.

## Email / OTP

The backend uses a provider-neutral Email adapter. Brevo is the current development reference provider; a Resend adapter is retained for later use.

OTP requirements include:

- Web Crypto random 6-digit code.
- HMAC-SHA256 digest at rest.
- 10-minute expiry.
- resend cooldown.
- attempt limit.
- rate limit.
- one-time consumption.
- scope binding for the intended operation.

No live Email-delivery claim should be made until the development deployment has the required runtime secrets and is tested end-to-end.

## Public repository boundary

This directory is public source. Keep implementation provider-neutral at the Windows boundary and do not place runtime secrets or operational customer data in source, PR text, logs, or engineering artifacts.


## Device metadata (Windows V2.6.14 / Cloud 0.8.9)

- `POST /v1/devices/usage` accepts `{clientVersion}` with an active Device Token. It updates only that Device's version and server UTC `last_seen_at` on startup. Client timestamps/target IDs cannot redirect the update. Offline/older clients retain their last confirmed values; this is not a live presence heartbeat.
- `POST /v1/devices/rename` accepts `{targetDeviceId, displayName, employeeNo, password}`. Current central SUPER_ADMIN credentials and an active same-Workspace target are required. The transaction rechecks actor, Workspace and credential freshness and records `device_renamed` atomically.
- Migration `0012_device_rename_audit.sql` retains all prior audit rows and adds the rename vocabulary. Metadata columns already exist. API compatibility marker stays 8; storage schema advances to 12.
- Desktop management filters active Devices and hides status/revocation columns. Cloud inventory and DB retain revoked history. Existing last-active protection and self-reset remain intact.
- Local `npm test` executes real lifecycle handlers/SQL, including auth/race rejection and audit-failure rollback. Remote deployment evidence is recorded separately in the handoff/PR.

## CYID Consumer candidate (disabled by default)

Canonical identity semantics come directly from `../../CYCloudIdentity/docs/CONSUMER_INTEGRATION_STANDARD.md`; this consumer declares `../CYID_CONSUMER_VERSION=1.0.3`. No mirror or CYID D1 binding is added.

An independently reviewed deployment injects `CYID_ENABLED=true`, private `IDENTITY` Service Binding, and runtime-only `IDENTITY_APPLICATION_ID`, `IDENTITY_WORKSPACE_ID`, `IDENTITY_CYINVOICE_WORKSPACE_ID`. The two Workspace IDs are distinct scopes; this Worker binds exactly one CYInvoice Workspace. Missing/mismatched configuration fails closed. No real deployment binding or CYID application enablement was verified in this source task. With the flag absent/false, Built-in remains available.

`IDENTITY_CYINVOICE_WORKSPACE_ID` is the existing business Workspace ID, not a new Workspace. Cutover retains the current consumer D1, Workspace, Devices and Tokens; existing installations must not rejoin or reset. Upgrade all active Windows installations to CYID-capable clients (current batch: V2.6.17), prepare Employee activation/App Access/Role/verified Super Admin Email, and prove continuity in isolated staging before enabling the flag. Until those gates pass, retain the Built-in deployment. Windows offline authentication is not offline invoice issuance: AMEGO still requires connectivity; Cloud transport/timeout/temporary 503 with reachable AMEGO uses the existing protected fallback. Credential/permission/Device rejection never permits fallback. Both services unavailable blocks all business; pure Local requires only AMEGO. See CY_ID_INTEGRATION §14.2. Authenticated discovery now checks canonical private `/v1/health` via the existing Binding; this is liveness only, creates no Session, and never grants Employee/App Access authorization. Source Cloud 0.9.1 adds no schema/migration or deployment.

`GET /v1/identity-provider` requires an active Device Token and returns the confirmed provider/binding. `POST /v1/cyid/authenticate` accepts only per-operation employeeNo/password plus Device Token. The gateway owns Login → Resolve → result → finally Logout, returning normalized principal without Session/verifier. CYID owns Employee/enabled/Role/App Access; CYInvoice owns Device and business permission. Legacy account/bootstrap routes reject in CYID mode.

Device rename/revoke, pairing issuance and invitation issue/preview/claim/revoke reuse the existing handler/mutation owners with a request-local CYID context. Invitation requires current SUPER_ADMIN credentials; pairing claim uses the existing one-time ticket issued after SUPER_ADMIN + Email OTP and grants Device membership only. Verified owner Email is read transiently from the authorized CYID admin snapshot, never imported into consumer authority/cache. Migration 0013 retains old invitation history/FKs and distinguishes Built-in from external CYID actors.

Logout is best effort in finally. Response loss/crash can leave a CYID Session active until provider revocation/expiry (current default 8 hours); no consumer token is persisted and no completed mutation is retried. No distributed transaction is claimed between CYID Resolve and consumer D1 mutation. Windows uses a subordinate DPAPI cache only after transport failure, never after an HTTP authority error; offline server administration is unavailable. See CY_ID_INTEGRATION §14 for acceptance and cutover gates.


### V2.6.17 aggregate runtime synchronization (Cloud 0.9.2)

`POST /v1/runtime/sync`, authenticated by the original Device Token, accepts `{synchronize, authorities}`. It returns self Device status and fixed consumer/provider binding, plus provider-specific permission results on a full refresh. Revoked tokens expose only their authenticated self-state; unknown tokens do not authorize reset. A final self-state check runs even after provider failure. The normal 60-second refresh reuses Built-in snapshot or canonical CYID private invalidation; liveness-only calls create no employee authorization. No migration or deployment is performed by this source change.

CYID mode additionally requires the named RPC binding below, targeting the same approved provider as `IDENTITY`; deployment substitutes a private service name. These are placeholders, not runnable production configuration:

```json
{"services":[
  {"binding":"IDENTITY","service":"PROVIDER_SERVICE_PLACEHOLDER"},
  {"binding":"IDENTITY_AUTHORITY","service":"PROVIDER_SERVICE_PLACEHOLDER","entrypoint":"ConsumerAuthoritySync"}
]}
```

Canonical capability semantics stay in CONSUMER_INTEGRATION_STANDARD §11.1. Consumer Impact BACKWARD_COMPATIBLE; existing Fetcher consumers keep operating. Missing/mismatched binding blocks validation rather than silently switching authority. Staging must exercise the named RPC, Device revoke during CYID outage, original Workspace continuity and usage/quota; production cutover remains separately authorized.

## Development rollout and recovery checkpoint (2026-10-11)

The existing `cyinvoice-cloud-dev-deploy.yml` is the only development deployment owner. PRs validate without credentials. A push to `cyinvoice/cloud-dev-preflight`, or dispatch with `mode=preflight`, performs remote reads only: existing settings/D1 binding, Built-in mode, canonical migration prefix, FK check, bounded history fingerprints, health, Time Travel checkpoint and prior Worker deployments. It creates no Workspace, Employee, Device or Session and sends no Email. Runtime data/resource identifiers stay in private runner temporary files and are never uploaded.

After a successful preflight, the existing `cyinvoice/cloud-dev-deploy` branch or explicit dispatch `mode=deploy` applies only reviewed pending migration 0013 (or no migration when already applied). A private generated config preserves the original D1, runtime vars, Service Bindings and secrets, explicitly retains `CYID_ENABLED=false`, and updates storage configuration to 13. Unknown resource bindings, changed rate limits, wrong environment/DB, enabled CYID, missing secrets, unexpected migrations or dirty FK stop before mutation. Source version is read from package.json rather than an old hard-coded 0.9.0.

Post-deployment verifies Cloud source version, API 1, compatibility marker 8, storage 13 and runtime-sync capability. It compares old Workspace, Device IDs/Token hashes/status, employee IDs/roles/revisions and pairing/invitation/audit history. New audit events may append; missing/changed captured rows fail acceptance. A legitimate concurrent account/Device change can also fail this conservative gate and requires diagnosis rather than automatic retry or restore. These are infrastructure/history checks; they do not replace original Device authentication and Windows acceptance.

Recovery does not replay an authority switch or automatically restore D1. Keep CYID disabled, diagnose the current Worker and migration state, and use the previously captured Worker deployment/version for an approved code rollback if necessary. Schema 13 retains Built-in invitation fields and historical actor FK; validate the old runtime against the forward schema before rollback. Restoring D1 by the logged checkpoint timestamp is a last resort after stopping writes and accounting for newer writes; otherwise recovery could erase legitimate activity. The bookmark and settings remain temporary/private; the checkpoint UTC timestamp is recorded in the job log so the original D1 bookmark can be retrieved again through Cloudflare. Recovery must happen within the applicable Time Travel retention window.

This original development target may contain the existing business Workspace; it is **not** automatically an isolated CYID sandbox. Separate staging targets and reviewed Application/Workspace bindings must be verified before creating synthetic accounts or switching authority. All active Windows installations remain in their original folders, are upgraded with the complete package, and retain Data/Cache/Logs. No production deployment, CYID flag switch or Release is implied by the development workflow.


## Exact active-version deployment; remote probes retired

The existing deployment owner now uploads only the reviewed real `app.js` through the modern Worker version API with `deploy=false`. Every original non-plain-text binding is inherited using the exact UUID of the original version serving100% traffic; runtime vars retain the original values plus reviewed configuration and `CYID_ENABLED=false`. Secrets are neither read nor replaced. Binding/compatibility readback, unchanged original traffic/routes, a fresh private checkpoint and canonical complete migrations are required before explicitly deploying the uploaded business version at100%. Always-run post-checks verify active settings, health, original Workspace/Device token hashes/employee/history and FK. Response loss is not automatically retried or restored.

Per the user's2026-10-11 decision, the expired undeployed no-D1 probe may remain in version history. Its deletion is no longer a prerequisite for exact-source deployment. Remote probe/cleanup controllers, modules, dispatch modes and dedicated push triggers have been removed; synthetic tests stay local/CI. Do not replay old deployment branches or runs. Real private binding/Employee authorization, Windows continuity, offline/revoke recovery, usage and final CYID cutover remain separate pending gates. Current evidence is in CYCloudIdentity `docs/DEVELOPMENT_DEPLOYMENT.md` and CYInvoice `docs/TODO.md`§7.3.3; product/contract versions unchanged.


### Prepare the existing CYID binding (Built-in retained)

The same development workflow offers `mode=prepare-cyid` (or the reviewed `cyinvoice/cloud-dev-prepare-cyid` branch). It deploys the real business bundle with `CYID_ENABLED=false`; it adds only the two approved CYID Service Bindings and the three scope vars. Original Secrets, D1, rate limits and unrelated services inherit from the exact active version. Existing matching bindings are inherited; conflicting targets, entrypoints or scope vars stop before upload. Ordinary `deploy` cannot introduce these bindings or change the scope.

The common Cloudflare Account/API Token Secrets remain unchanged. All CYInvoice, CYID development/production and CYACCweb deployment workflows read one `development` Environment Secret, `CYID_WORKER_NAME`. This is the same original development provider name used by the previous variable; copy its existing value once to the same-named Secret before merging the shared-input change. The CYACC resolver still derives and verifies its production target; sharing the base input does not bind CYInvoice to production or share Application IDs.

CYInvoice reads the configured Provider's exact active version through the Cloudflare deployment API, never Identity D1 or latest-upload settings. It verifies development/API v1 and the Provider's original DB binding, using that ID only to exclude it from Consumer database access. It then reads the CYInvoice-specific deployment metadata:

| Existing Provider runtime variable | Purpose |
| --- | --- |
| `CYINVOICE_APPLICATION_ID` | Registered, approved CYInvoice Application; not CYACC's Application ID |
| `CYINVOICE_WORKSPACE_ID` | Approved existing CYID Workspace for CYInvoice employees |

`IDENTITY_APPLICATION_ID` and `IDENTITY_WORKSPACE_ID` are generated privately from those two fields. `IDENTITY_CYINVOICE_WORKSPACE_ID` comes from the one active Workspace in the original CYInvoice D1; zero/multiple active Workspaces stop, never choose or create a replacement. No per-consumer copies of these IDs or Provider D1 ID are required in GitHub Secrets. Missing Provider scope is reported as unavailable by read-only preflight and blocks `prepare-cyid`; partial/invalid scope always fails. CYACC-only metadata cannot satisfy CYInvoice readiness. These fields are deployment configuration, not an Application registry or a positive Employee/App Access grant.

The shared Secret was validated before #413 merged, since CYACC main pushes deploy automatically. Future changes to this input should be checked through the credential-protected read-only preflight. Official active/retired deployment refs were synchronized and validated; the user removed the same-named Variable while retaining the Secret, and read-only #34 passed. Do not replay historical/retired runs. Scope configuration follows authorized CYID central registration/enablement; do not register synthetic accounts or point to a different environment to satisfy this check.

Preflight requires complete reviewed schema for preparation; this mode runs no migrations. Original Worker routes/traffic/history and Provider active version/binding digest must remain stable through upload and deployment. Generated mappings and targets stay in private runner files; public logs contain fixed outcomes and scope presence only. No remote test code, synthetic accounts, CYID D1 access or authority switch is performed.

Historical binding-preparation evidence is [prepare-cyid #39](https://github.com/simonliu1118-byte/CYapps/actions/runs/38203790273), source `2e21c8709f0ef35e665de4d48d4a069b310d1257`, tree `c1d303e4d3653c9bd03a034be335bb740c6c2101`. The user completed central CYInvoice registration/current Identity Workspace enablement and configured the two Provider runtime scope variables; read-only #38 confirmed scope configured=true. #39 verified both approved private service targets and three scope vars, exact inherited settings, real business upload/100% deployment, Cloud0.9.2/API1/marker8/storage13, original Workspace/Device Token hashes/employee/history/FK/routes, and private-file cleanup. No migrations or remote test upload were performed. Recovery reference UTC `2026-10-12T04:27:16.969Z`; not a restore rehearsal. CYID remains disabled and Built-in remains active. #32/#29 are historical checkpoints.

Central application registration is a separate Provider-owned core-portal Super Admin operation (canonical 1.0.4 §17.1, source 0.3.7). It does not populate deployment scope or activate CYID. Invoice retains the original business Workspace and Device Tokens through binding preparation. Shared-name Variable removal was confirmed; read-only #34 (38195315134) passed after removal with the shared Secret.

Development Provider0.3.7 (#158, 38197480766) and CY Web0.7.23 (#138, 38197908123) are deployed with canonical1.0.4. Registration/scope/binding configuration is complete; actual private Fetcher/RPC, Employee/App Access/Role/Email readiness, original Windows continuity, RC AA/AB/AC and reviewed authority cutover remain pending. Configuration success does not prove authorization. Preserve the original portable installations/data and validate Built-in continuity before a controlled activation; no new Invoice Workspace or rejoin is required. Production cutover/Release remain separately approved.

### Activate the prepared development CYID authority

Historical activation checkpoint: #420 source70514413/treec8fb38c1 passed all checks and merged2543222d. [Development activation #42](https://github.com/simonliu1118-byte/CYapps/actions/runs/38206372325), source8488ce3d/same tested tree, passed before/upload/100% deployment/after readback with CYID_ENABLED=true. Original Cloud0.9.2/API1/marker8/schema13, Workspace1/Device4/Token hashes/employee/history/FK/routes retained; migration skipped/private cleanup passed. Recovery UTC2026-10-12T05:05:10.603Z. The user subsequently reported successful CYID credentials access to settings on the original Windows installation; the full role/multi-device matrix remains pending. The current deployment and company storage status are the 0.9.3/CYID_ONLY baseline above. The #39 paragraph above is the prior Built-in preparation checkpoint, not the current authority flag.

On 2026-10-12 the user confirmed the upgraded original Windows installation retained invoices/settings and accepted the original credentials, then authorized continuation. The same deployment owner adds `mode=activate-cyid` / reviewed `cyinvoice/cloud-dev-activate-cyid` branch. After exact-head PR checks, activate only the approved development target. It requires Built-in initially, complete schema13, exactly one original active business Workspace, matching Provider scope and both already-prepared private bindings. Activation cannot create/repair a binding or change another runtime variable: only `CYID_ENABLED` changes to `true`. Original resources/Secrets inherit from the active version; checkpoint, Provider/Consumer drift guards, readback, 100% deployment, history/Token hashes/FK and private cleanup remain. No migration, authority table copy, new Workspace/Device, test upload or automatic retry/restore.

Read-only `preflight` can audit either authority and validates the complete original mapping when CYID is active. Built-in `deploy`/`prepare-cyid` and repeated activation reject an already-enabled target, preventing an accidental downgrade. Ordinary deployments after activation require a separately reviewed authority-preserving update; do not reuse the Built-in deployment branch.

First live acceptance uses the existing CYID Super Admin (central App Access is always allowed); ordinary employees need their current CYInvoice App Access/Role. Restart the original V2.6.19 installation, confirm CYID discovery/account-management hiding and use CY Web/CYID employee credentials for a protected settings read, without issuing an invoice or changing data. Discovery/health/configuration is not positive Employee authorization. Continue RC AA/AB/AC, ordinary users and usage acceptance before production/Release.

If activation fails, inspect always-run readback and the private recovery reference first: a lost response can mean the new version is active. Do not rerun blindly, restore D1, reset/rejoin or wipe client settings. A client that confirmed CYID will not silently accept Built-in afterward. Any reviewed recovery must coordinate the exact prior Worker configuration and the backed-up original client state while preserving current business data/Device Tokens; no automatic rollback is implemented.
