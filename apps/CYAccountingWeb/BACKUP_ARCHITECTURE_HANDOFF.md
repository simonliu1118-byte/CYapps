# CYAccountingWeb — Tiered Backup Architecture / Operational Handoff

> Updated: 2026-09-27（Taiwan time）
>
> Current formal baseline: **V0.18.1 Build 0**
>
> Current migration phase: **Phase C production acceptance in progress**
>
> This document consolidates the earlier V0.17 architecture handoff and the 2026-09-27 R2 Phase C operational handoff. Completed C2/C3 wiring steps are historical record only and must not be repeated as fresh setup work.

## 1. Source-of-truth order for continuation

Before changing backup infrastructure, read current `main` and use this order:

1. current explicit user instruction;
2. `apps/CYAccountingWeb/PROJECT_RULES.md`;
3. repository governance / public-repo policy;
4. current `VERSION` / `BUILD` / migrations / source;
5. this handoff and `TODO.md` as current-state / migration documents.

`README.md`, `TODO.md` and this handoff are state/reference documents, not permanent governance rules.

Cross-application infrastructure decisions involving Identity / SSO, shared Workers, database ownership or shared Backup Service must also be synchronized with the latest **CY-WEB workstream** decision before implementation.

## 2. Current accepted production baseline

As of 2026-09-27:

- CYAccountingWeb version: **V0.18.1 Build 0**.
- Cloudflare D1 is the authoritative live accounting database.
- V0.17 GCS production backup path has completed real production acceptance and remains the rollback / safety path during migration.
- Phase A is complete.
- Phase B is complete.
- Phase C implementation is deployed.
- Phase C manual paired R2 + GCS production acceptance passed on **2026-09-27**.
- Phase C scheduled acceptance counter is implemented and shown as `x / 14`.
- Phase D is blocked until the scheduled gate reaches `14 / 14`.
- Restore remains future/high-risk work and is not exposed as a completed user-facing capability.

Current implementation files include:

```text
apps/CYAccountingWeb/src/app-v18.js
apps/CYAccountingWeb/src/v18-backup.js
apps/CYAccountingWeb/src/r2-backup-provider.js
apps/CYAccountingWeb/src/gcs-backup-provider.js
apps/CYAccountingWeb/src/v17-backup.js
apps/CYAccountingWeb/migrations/0004_tiered_backup_catalog.sql
apps/CYAccountingWeb/tests/v18-tiered-backup.mjs
apps/CYAccountingWeb/tests/v18-backup-ui.mjs
apps/CYAccountingWeb/tests/deploy-config.mjs
apps/CYAccountingWeb/wrangler.template.jsonc
```

## 3. Current Phase C topology

Production Phase C behavior is:

```text
Cloudflare D1
    ↓ authoritative live DB
build one immutable backup set
    ↓ export once
one logical backupId + one package digest
    ├─ Cloudflare R2 verified copy
    └─ Google Cloud Storage verified copy
```

Current topology values supported by the deployed Phase C code:

```text
legacy_gcs
parallel_dual_provider
```

`legacy_gcs` remains the rollback-compatible mode.

`parallel_dual_provider` is the active Phase C model used for paired production acceptance.

Do not invent or enable future topology names until the corresponding implementation phase exists.

## 4. Deployment contracts

Public source may contain binding / contract names, but not production resource identifiers.

Current contracts:

```text
DB               Cloudflare D1 binding
IDENTITY         private employee-identity service binding
BACKUP_R2        Cloudflare R2 binding
BACKUP_TOPOLOGY  backup rollout mode
```

Current Worker entrypoint:

```text
src/app-v18.js
```

Current cron:

```text
30 19 * * *
```

= daily 03:30 Taiwan time.

Production Worker name, D1 database ID/name, Identity service actual name, R2 bucket actual name and similar deployment-specific identifiers are injected by GitHub Deployment Environment / Secrets through generated deploy config. Do not hard-code them into public source merely because an earlier operational handoff recorded a real resource name.

## 5. Phase C provider roles and retention

During Phase C acceptance:

### R2

- role: operational backup copy;
- schedule: daily 03:30 Taiwan;
- application retention: **30 rolling days**;
- accessed through Worker R2 binding;
- no public R2 endpoint is required;
- no application-level S3 access key / R2 API token is required for the Worker binding path.

### GCS

- role during Phase C: paired cross-cloud validation + accepted V0.17 rollback/safety copy;
- schedule: still **daily** during Phase C;
- application retention: still **14 days** during Phase C;
- current accepted GCS credential / provider path remains available through the gate.

Do **not** switch GCS to Wednesday/Sunday or 182-day retention before Phase C completes.

## 6. Export-once / immutable package requirement

One logical backup operation creates one `backupId` and one set of immutable bytes:

```text
D1 export once
→ build data.json once
→ build manifest.json once
→ calculate package digest
→ write/read-back verify R2
→ write/read-back verify GCS using the exact same package bytes
```

Phase C code must not perform a second D1 export merely to create the GCS copy.

Provider retry / future replication should reuse the already-created backup set whenever the logical backup event is intended to remain the same.

## 7. Current backup format compatibility

Current production output remains the accepted V0.17 application-specific package:

```text
format = CYAccountingWebBackupSet
formatVersion = 2
```

Package layout:

```text
CYAccountingWeb/<backup-id>/
├─ manifest.json
└─ data.json
```

The future common outer contract:

```text
format = CYBackupSet
formatVersion = 1
```

is **not** a completed production format migration.

Rules:

- do not rewrite existing V0.17 GCS objects;
- keep old-format reader / validator support through the compatibility window;
- provider migration and format migration remain separate changes;
- do not combine a destructive provider cutover with a destructive format cutover.

## 8. Logical backup / provider-copy catalog — implemented

Migration `0004` introduced the additive logical catalog:

```text
backup_sets
backup_copies
```

Current semantics:

```text
one logical backupId
  ├─ one R2 copy status
  └─ one GCS copy status
```

The UI/business meaning is one backup listed once, with provider health shown separately.

Legacy GCS `backup_runs` evidence is still written for compatibility / rollback and must not be removed during Phase C.

## 9. Manual paired production acceptance — completed

The first real `parallel_dual_provider` production acceptance completed on **2026-09-27**.

Accepted behavior includes:

- one logical `backupId`;
- one logical backup-set catalog row;
- separate R2 / GCS provider-copy rows;
- R2 copy verified success;
- GCS copy verified success;
- same logical backup ID for both providers;
- same package digest / same portable package bytes;
- legacy GCS compatibility evidence retained;
- UI presents one logical backup with per-provider health.

This manual acceptance proves the Phase C production wiring works, but **does not count toward the 14 scheduled-backup gate**.

## 10. Scheduled Phase C acceptance gate — current active work

Required gate:

```text
14 consecutive scheduled production backups
```

Only backups where:

```text
trigger_kind = scheduled
```

are eligible.

Each passing run must prove:

- one logical backup ID;
- R2 copy status = `success`;
- GCS copy status = `success`;
- a valid 64-hex package SHA-256 digest;
- same logical package represented by both provider copies.

The application derives the current consecutive count from D1 catalog evidence and exposes it as:

```text
x / 14
```

Rules:

- manual runs do not count;
- a partial provider run does not count;
- the consecutive counter stops at the first failed scheduled backup in the latest sequence;
- Phase D remains blocked until `14 / 14` is reached.

## 11. Failure isolation

Runtime behavior is provider-specific:

- valid R2 success must remain valid when GCS fails;
- valid GCS success must remain valid when R2 fails;
- provider failures are stored and displayed independently;
- one provider failure must not cause deletion of the other provider's verified copy.

Current automated regression coverage explicitly verifies the `R2 success / GCS failure` direction and confirms the R2 copy remains intact.

The runtime structure supports the mirror direction as well; however, a dedicated automated `R2 failure / GCS success` mirror test is still a useful small test-debt item for the next backup-code change. It is not a reason to interrupt the current production acceptance gate by itself.

Do not intentionally damage the accepted production GCS dataset merely to simulate failure.

## 12. Phase A / B / C status

### Phase A — complete

Accepted V0.17 GCS production path frozen as safety / rollback path.

Still retained through Phase C:

- GCS provider;
- GCS runtime credentials;
- daily 03:30 behavior;
- 14-day GCS application retention;
- legacy compatibility evidence.

### Phase B — complete

Completed requirements:

- package creation separated from provider storage execution;
- single BackupSet reusable by multiple providers;
- GCS generation normalized behind opaque `versionToken` semantics;
- V0.17 compatibility tests;
- export-once tests;
- no production format cutover.

### Phase C — implementation complete, acceptance in progress

Completed implementation:

- R2 provider;
- R2 Worker binding contract;
- `parallel_dual_provider`;
- logical backup / provider-copy catalog;
- export-once paired storage;
- digest parity checks;
- provider failure isolation behavior;
- topology-aware backup UI;
- manual paired production acceptance;
- scheduled `x / 14` acceptance counter.

Remaining Phase C work:

- accumulate **14 consecutive successful scheduled production paired backups**.

## 13. Phase D — blocked until 14/14

Only after Phase C gate passes:

```text
R2
  daily 03:30 Taiwan
  30-day application retention

GCS
  Wednesday + Sunday replication
  26 weeks / 182 days
```

Phase D requirements:

- GCS must reuse already-created backup-set bytes;
- GCS replication must not trigger a second D1 export;
- do not bulk-delete old daily GCS objects on cutover day;
- let pre-cutover daily objects age out under an explicit compatibility cleanup policy;
- keep rollback ability until the new policy is accepted.

Before `14 / 14`, do **not**:

- change GCS to Wednesday/Sunday;
- change GCS retention to 182 days;
- remove the legacy GCS path;
- remove direct GCS rollback capability;
- rewrite/delete accepted V0.17 backup objects;
- remove legacy `backup_runs`;
- move production storage ownership to a shared Backup Worker.

## 14. Phase E — future shared CY Backup Service

Long-term target:

```text
CYAccountingWeb BackupService ─┐
CY Web BackupService ──────────+→ CY Backup Service / Worker
future CY apps ────────────────┘       │
                                       ├─ app-scoped R2
                                       └─ app-scoped GCS
```

This phase starts only after:

1. the per-App tiered model is stable;
2. Phase C / D acceptance permits it;
3. CY-WEB workstream has finalized the relevant shared-infrastructure contract.

### Remains inside CYAccountingWeb

- accounting D1 export semantics;
- accounting schema / record validation;
- authorization;
- restore compatibility checks;
- double-confirmation restore flow;
- accounting D1 restore ordering / writes;
- accounting-specific audit evidence.

### May move to shared Backup Service

- provider adapters;
- object read/write / read-back verification;
- provider-copy health/catalog responsibility;
- R2 → GCS replication;
- retention;
- retry orchestration;
- app-scoped storage routing.

The shared Backup Worker must **not** perform accounting D1 restore writes.

## 15. Dataset / credential isolation

CYAccountingWeb and CY Web must remain isolated at the storage-data level.

CYAccountingWeb keeps its own app-scoped:

- R2 dataset / binding;
- GCS dataset;
- least-privilege GCS identity / credential.

CY Web uses separate equivalents.

Future shared services do not mean one broad cross-app credential.

Caller identity must be mapped server-side to the permitted app dataset. Do not trust a caller-supplied `appId` by itself as authorization.

## 16. Identity / database boundary with CY-WEB

Current account state is transitional:

- CYAccountingWeb currently consumes the shared employee account authority hosted by **CYInvoice Cloud** through the private `IDENTITY` Service Binding contract;
- CYAccountingWeb does **not** directly access CYInvoice D1 for login or employee lookup;
- after identity validation, CYAccountingWeb maintains its own application `web_sessions` in its own D1;
- CYAccountingWeb accounting data remains in the independent CYAccountingWeb D1.

The shared Identity / SSO extraction is being planned progressively by the **CY-WEB workstream**.

Therefore, before changing any of the following, synchronize the latest CY-WEB decision first:

- Identity authority or SSO topology;
- cross-App employee / role / access model;
- shared account database ownership;
- private Service Binding topology;
- shared Worker responsibilities;
- cross-App D1 ownership;
- shared Backup Service routing.

Do not treat the present CYInvoice-hosted identity implementation as permanent architecture, and do not pre-emptively merge CYAccountingWeb accounting D1 with another App's database.

## 17. Restore — future / high risk

Restore is not yet a completed user-facing feature.

Required target flow:

```text
list logical backups
→ select backupId
→ prefer valid R2 copy
→ fallback to valid GCS copy
→ verify package integrity
→ verify app/schema compatibility
→ SUPER_ADMIN server-side authorization
→ double confirmation
→ optional pre-restore backup to both providers
→ controlled D1 restore
→ reconciliation
→ audit result
```

Rules:

- only `SUPER_ADMIN` may restore;
- `ADMIN` / `EMPLOYEE` must not gain restore permission;
- second confirmation must explicitly state that current D1 data will be overwritten;
- Worker/API authorization is authoritative; UI hiding is insufficient;
- record operator, time, backup ID and outcome as audit evidence;
- Web UI must not expose a general-purpose “clear all accounting data / opening balances” action.

Disaster-recovery exercises still required:

- R2 unavailable → restore/load from valid GCS copy;
- Cloudflare/D1 failure → rebuild a new D1 using valid GCS cross-cloud DR backup.

## 18. Future common outer format

Target common contract remains:

```text
CYBackupSet / formatVersion 1
```

This is a future versioned compatibility path shared conceptually with CY Web, not a reason to rewrite accepted V0.17 production objects.

App-specific `data.json` schemas remain app-specific.

Provider metadata, bucket names, generations, R2 metadata and credentials must not be written into the portable manifest.

## 19. Remaining non-code operational items

Still pending / future:

- Google Cloud Billing low-budget alert target: NT$100 per month;
- GCS provider lifecycle only as a second guard, configured longer than application retention;
- Phase C `14 / 14` scheduled gate;
- Phase D tiered schedule / long GCS retention;
- full restore implementation;
- full DR drills;
- future common `CYBackupSet` compatibility path;
- Phase E shared Backup Service after CY-WEB contract is ready.

## 20. Continuation instruction

The old operational sequence:

```text
add BACKUP_R2
→ wire app-v18.js
→ deploy legacy_gcs
→ smoke test GCS
→ switch parallel_dual_provider
→ manual paired acceptance
```

is **already completed** and must not be repeated as if R2 were not active.

The correct continuation point is:

```text
V0.18.1 Build 0
Phase C parallel dual-provider deployed
manual paired acceptance passed 2026-09-27
↓
observe scheduled production backups
↓
require 14 consecutive paired successes
↓
only then plan / execute Phase D
```

Until that gate passes, preserve the accepted GCS rollback path and avoid unrelated backup-topology churn.
