# CYAccountingWeb — Tiered Backup Architecture Handoff

> Status: implementation handoff for the CYAccountingWeb workstream.
>
> This document does **not** authorize changes to CYAccountingWeb from the CY Web workstream. It records the agreed target architecture and migration sequence so the CYAccountingWeb conversation can implement it deliberately.

## 1. Current accepted CYAccountingWeb baseline

Read current `simonliu1118-byte/CYapps` `main` before implementation.

As of the handoff:

- CYAccountingWeb version: **V0.17.0**.
- Cloudflare D1 is the authoritative live accounting database.
- GCS production backup has completed real production acceptance.
- Existing scheduled backup time: daily 03:30 Taiwan time.
- Existing GCS retention: 14 days.
- Existing production package: `manifest.json + data.json`.
- Existing provider-neutral interface already requires `putObject`, `getObject`, `listObjects`, `deleteObject`.
- Existing GCS backup verifies read-back SHA-256, byte size, record counts and manifest compatibility.

Do **not** break or remove the accepted V0.17 GCS path while the new architecture is being introduced.

Relevant current source:

```text
apps/CYAccountingWeb/src/backup-storage-provider.js
apps/CYAccountingWeb/src/gcs-backup-provider.js
apps/CYAccountingWeb/src/v17-backup.js
apps/CYAccountingWeb/src/app-v17.js
apps/CYAccountingWeb/BACKUP_ARCHITECTURE_HANDOFF.md
apps/CYAccountingWeb/TODO.md
```

## 2. New target architecture

```text
Cloudflare D1
    ↓ authoritative live DB
CYAccountingWeb BackupService
    ↓ export once
portable CYBackupSet
    ├─ R2  — daily operational backup
    └─ GCS — lower-frequency cross-cloud DR replica
```

### Provider roles

**R2**

- daily operational backup;
- normal in-app restore source while Cloudflare is healthy;
- schedule: daily 03:30 Taiwan time;
- retention: 30 rolling days.

**GCS**

- independent cross-cloud disaster recovery;
- normal scheduled replication: Wednesday and Sunday, Taiwan local date;
- retention: 26 rolling weeks / 182 days;
- receives the exact same portable backup-set bytes as the R2 logical backup;
- must not trigger a second D1 export.

Manual / pre-restore safety backup should target both providers regardless of weekday.

## 3. Export-once requirement

One logical backup operation creates one `backupId` and one set of bytes:

```text
build data.json once
→ hash once
→ build manifest.json once
→ store/verify R2
→ store/verify same bytes on GCS when policy requires
```

Do not run `buildBackupPackage()` or an equivalent D1 export again for GCS replication.

If GCS replication fails, retry from the already-created backup set. Do not create a new payload while pretending it is the same backup event.

## 4. Shared portable backup contract

Target common outer format for CY Web and CYAccountingWeb:

```text
format = CYBackupSet
formatVersion = 1
```

Layout:

```text
<app-scope>/<backup-id>/
├─ manifest.json
└─ data.json
```

Required common manifest semantics:

```text
format
formatVersion
backupId
appId
appVersion
schemaVersion
createdAtUtc
sourceDatabaseEngine = d1
workspaceScope / tenant scope when applicable
recordCounts
totalRecordCount
dataObjectName
dataSha256
dataByteLength
```

`data.json` remains application-specific accounting data. The shared contract is the outer package/integrity contract, not a requirement that CY Web and CYAccountingWeb have identical tables.

Provider-specific copy status, bucket names, object generations, R2/GCS metadata and credentials must not be written into the portable manifest.

## 5. Compatibility with V0.17 backup format

Current V0.17 uses its accepted application-specific backup format/version. Existing GCS backups must remain readable and valid.

Therefore:

- introduce the common `CYBackupSet` format through a versioned compatibility path;
- do not rewrite old GCS objects in place;
- keep old-format validation/reader support for the accepted retention/restore compatibility window;
- only switch production output after tests prove current V0.17 backups are unaffected.

A format migration is separate from a provider migration. Do not change both destructively at the same time.

## 6. BackupStorageProvider contract

Keep the current four-operation boundary:

```text
putObject(key, bytes, metadata)
getObject(key)
listObjects(prefix)
deleteObject(key, versionToken?)
```

Normalize provider-specific metadata instead of leaking GCS/R2 details into `BackupService`.

Recommended normalized object/list fields:

```text
key
byteSize
timeCreated
versionToken?    // opaque
```

For GCS, an implementation may map `versionToken` to a generation. R2 may use no token or another opaque token. Domain code must not branch on provider-specific token semantics.

## 7. Logical backup / provider-copy catalog

Move toward one logical backup with multiple provider copies rather than one provider row pretending to be the backup identity.

Recommended semantics:

```text
backup_sets
  backup_id
  app_id
  created_at
  app_version
  schema_version
  data_sha256
  data_byte_length
  record_count
  trigger_kind

backup_copies
  backup_id
  provider
  status
  verified_at
  storage_prefix
  version_token
  last_error
```

The exact D1 schema may be adapted to existing `backup_runs`, but the business/UI behavior must be:

> one logical backup is listed once, with R2/GCS copy health beneath it.

## 8. Shared service boundary

The long-term target is:

```text
CYAccountingWeb BackupService ─┐
CY Web BackupService ──────────+→ CY Backup Service / Worker
future apps ───────────────────┘       │
                                       ├─ app-scoped R2 provider
                                       └─ app-scoped GCS provider
```

### Remains inside CYAccountingWeb

- accounting D1 export;
- accounting schema compatibility;
- accounting record-count/data validation;
- user/SUPER_ADMIN authorization;
- double-confirmation restore;
- accounting D1 restore ordering and writes;
- accounting audit evidence.

### Moves to shared CY Backup Service / Worker

- R2/GCS provider adapters;
- object storage/read-back verification;
- provider-copy catalog;
- R2→GCS replication;
- provider retention;
- retry/copy health;
- app-scoped storage routing.

The shared Backup Worker must not perform accounting D1 restore writes.

## 9. Dataset / credential isolation

Isolation remains required throughout migration.

CYAccountingWeb must have its own:

- R2 bucket/dataset binding;
- GCS bucket/dataset;
- least-privilege GCS identity/credential.

CY Web keeps separate equivalents.

When the future shared CY Backup Worker is introduced, it still uses separate app-scoped bindings/credentials. Do not replace two isolated credentials with one broad cross-application credential.

Caller identity must be mapped server-side to the allowed application dataset. A caller must not gain CY Web access merely by changing an `appId` parameter.

## 10. Migration plan — do not skip phases

### Phase A — freeze V0.17 GCS production path

Current GCS implementation remains the production safety path.

Do not remove:

- current GCS provider;
- current GCS secrets;
- current daily schedule;
- current 14-day retention behavior;

until the later acceptance gate explicitly permits cutover.

### Phase B — separate package creation from storage execution

Refactor only enough to guarantee:

```text
D1 export → BackupSet object/bytes → provider writes
```

The package builder must be callable once and reused by more than one provider.

Add compatibility tests for existing V0.17 format before switching to the common outer format.

### Phase C — add R2 in parallel

Implement `R2BackupStorageProvider` using the same provider contract.

During production parallel validation:

- schedule remains daily 03:30;
- export D1 once;
- write/verify R2;
- write/verify GCS using the exact same backup-set bytes;
- GCS remains daily and retains the current 14-day policy during this phase;
- R2 retention is 30 days.

**Acceptance gate: at least 14 consecutive scheduled production backups** where both provider copies verify successfully and paired copies have the same logical backup ID and package digest.

Also test provider-specific failure isolation: a GCS copy failure must not invalidate a valid R2 copy, and an R2 copy failure must be reported distinctly.

### Phase D — enable tiered schedule

Only after Phase C acceptance:

- keep R2 daily 03:30;
- change GCS from daily backup target to Wednesday/Sunday replication;
- change GCS retention target to 26 weeks;
- GCS replication reuses the existing backup-set bytes;
- do not bulk-delete pre-cutover daily GCS objects on cutover day;
- let old daily copies age out through an explicit compatibility cleanup policy.

### Phase E — shared CY Backup Service / Worker

Do this after the tiered per-App model is stable and CY Web is ready to consume the same contract.

Migration order:

1. create shared service with app-scoped provider configuration;
2. route a non-destructive copy/verification path through it;
3. compare shared-service results with current direct App provider path;
4. move replication/retention/catalog into shared service;
5. move storage writes/reads behind private Service Binding;
6. keep direct GCS path available as rollback during acceptance;
7. only after acceptance remove direct R2/GCS bindings/credentials from the Accounting Worker.

Do not make the shared service a prerequisite for the initial R2 tier if that would delay or destabilize the accepted V0.17 production backup.

## 11. Suggested topology switch

A deployment/runtime mode is recommended so rollout is reversible:

```text
legacy_gcs
parallel_dual_provider
tiered_direct
tiered_shared_service
```

Exact variable naming is an implementation detail, but the rollout must provide an explicit rollback switch rather than requiring emergency source edits.

## 12. Retention details

Application policy:

```text
R2  30 days
GCS 182 days / 26 weeks
```

Rules:

- cleanup occurs after successful verification;
- cleanup failure is warning/follow-up, not new-backup failure;
- a source backup required for pending replication cannot be cleaned first;
- provider lifecycle may be a second guard only when configured longer than the application policy;
- old V0.17 GCS daily backups are not destructively rewritten or mass-deleted during migration.

## 13. Restore behavior

Restore remains future/high-risk work.

Required flow:

```text
list logical backups
→ select backupId
→ prefer valid R2 copy
→ fallback to valid GCS copy
→ verify package integrity
→ verify app/schema compatibility
→ SUPER_ADMIN authorization
→ double confirmation
→ optional pre-restore backup to both providers
→ controlled D1 restore
→ reconciliation
→ audit result
```

For Cloudflare-wide disaster recovery, GCS is the independent recovery source.

## 14. Acceptance tests

Do not consider the new architecture complete until all are verified:

1. one D1 export creates one logical backup ID;
2. R2 upload/read-back SHA-256 passes;
3. GCS receives identical portable bytes for an eligible backup;
4. paired R2/GCS copy digests are identical;
5. GCS retry does not re-export D1;
6. R2 30-day retention works;
7. GCS 26-week retention works after tiered cutover;
8. cleanup failure does not invalidate a new verified copy;
9. one logical backup appears once in UI/catalog;
10. provider-copy health is visible separately;
11. R2-unavailable restore can load the valid GCS copy;
12. cross-app dataset access is rejected;
13. existing V0.17 GCS backup objects remain readable/valid;
14. rollback to current accepted GCS topology remains possible through the migration window.

## 15. Explicit instruction to the CYAccountingWeb workstream

Start by reading current `main`, especially V0.17 backup source and this handoff. Do not immediately replace the working GCS implementation.

Implement the migration in the phases above, with V0.17 GCS kept as the accepted rollback path until the parallel 14-backup acceptance gate has passed.
