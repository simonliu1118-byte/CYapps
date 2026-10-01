# CYAccountingWeb — Backup Architecture / Operational Handoff

> Updated: 2026-10-01
>
> Current application baseline: **V0.21.6 Build 8**
>
> This document describes the current backup architecture. Version-numbered source wrappers from the pre-launch development history are not part of the active architecture.

## 1. Source of truth

Before changing backup behavior, use this order:

1. current explicit user instruction;
2. `apps/CYAccountingWeb/PROJECT_RULES.md`;
3. repository governance / public-repo policy;
4. current source, migrations, `VERSION` and `BUILD`;
5. this handoff and `TODO.md`.

Production resource identifiers, employee data, credentials, Session values and provider secrets must not be committed to public Git.

## 2. Current code structure

The Worker has one entrypoint:

```text
src/app.js
```

Backup implementation is split by responsibility:

```text
src/backup-package.js         D1 → portable accounting backup data
src/backup-storage-provider.js
src/gcs-backup-provider.js    GCS provider adapter
src/r2-backup-provider.js     R2 provider adapter
src/gcs-backup.js             accepted GCS backup-set format and operations
src/backup-service.js         topology, API routing and scheduled orchestration
```

There is no active `app-v17 → app-v18 → app-v19` Worker chain.

The former Google Drive V16 backup route is not part of the active baseline. Only its provider-independent D1 package-building behavior was retained and extracted into `backup-package.js`.

## 3. Current topology

Supported topology values remain:

```text
legacy_gcs
parallel_dual_provider
```

`legacy_gcs` keeps the accepted GCS path.

`parallel_dual_provider` creates one logical backup package and stores verified copies in both R2 and GCS.

The required invariant is:

```text
Cloudflare D1
  ↓ export once
one immutable backup set
  ├─ verified R2 copy
  └─ verified GCS copy
```

A provider copy must never trigger a second accounting D1 export for the same logical backup event.

## 4. Deployment contracts

Public source may contain binding names, but not production identifiers.

Current contracts:

```text
DB               Cloudflare D1 binding
IDENTITY         CYID production Service Binding
BACKUP_R2        Cloudflare R2 binding
BACKUP_TOPOLOGY  backup rollout mode
```

Current Worker entrypoint:

```text
src/app.js
```

Current cron:

```text
30 19 * * *
```

This is daily 03:30 Taiwan time.

Actual Worker names, D1 identifiers, CYID Application/Workspace identifiers, Service Binding targets, R2 bucket names and credentials remain deployment/runtime configuration only.

## 5. Backup format compatibility

The accepted application backup-set format remains:

```text
format = CYAccountingWebBackupSet
formatVersion = 2
```

Layout:

```text
CYAccountingWeb/<backup-id>/
├─ manifest.json
└─ data.json
```

Keeping this format is **data compatibility**, not a reason to keep version-numbered source wrappers.

Existing accepted backup objects must not be rewritten merely because source modules were consolidated.

A future common outer `CYBackupSet` contract, if introduced, must be handled as a separately versioned format migration.

## 6. Logical backup catalog

The additive logical catalog remains:

```text
backup_sets
backup_copies
```

Semantics:

```text
one logical backupId
  ├─ one R2 copy status
  └─ one GCS copy status
```

Legacy `backup_runs` evidence remains for accepted backup-history compatibility. It is data/history compatibility, not application routing compatibility.

## 7. Provider failure isolation

Provider copies are independent:

- valid R2 success remains valid if GCS fails;
- valid GCS success remains valid if R2 fails;
- provider errors are recorded independently;
- failure of one provider must not delete a verified copy on the other provider.

Automated backup-service regression tests protect export-once behavior, provider isolation, digest parity and topology selection.

## 8. Authorization

Backup management is server-authorized.

- `SUPER_ADMIN`: may access backup management routes.
- `ADMIN` and `USER`: may not manage backup or restore.
- UI visibility is not authorization.

CYACC consumes CYID as the sole employee/session authority. CYACC does not create a second local identity session after the CYID cutover.

## 9. Restore boundary

Restore remains high risk and must remain accounting-owned.

Required future flow:

```text
select logical backup
→ obtain verified provider copy
→ verify package integrity
→ verify application/schema compatibility
→ SUPER_ADMIN authorization
→ explicit destructive confirmation
→ optional pre-restore backup
→ controlled D1 restore
→ reconciliation
→ audit outcome
```

A future shared backup storage service may own provider storage mechanics, but must not perform CYAccountingWeb D1 restore writes.

## 10. Cross-application isolation

CYAccountingWeb backup data must remain app-scoped.

A future shared provider service does not imply shared unrestricted credentials or shared datasets. Caller identity must be mapped server-side to the permitted application dataset.

## 11. Current continuation rule

Do not add a new versioned backup wrapper such as `app-v20.js` or `v20-backup.js`.

New backup behavior must be integrated into the semantic modules above. If a change needs backward data compatibility, implement an explicit format reader/adapter at the data boundary instead of wrapping the entire Worker.

The backend baseline rule is:

```text
one Worker router
→ one current backup service
→ provider adapters
→ explicit data-format compatibility only where required
```
