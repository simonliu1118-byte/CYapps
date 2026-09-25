# CYAccountingWeb Backup Architecture Handoff

> Status: implementation handoff / coordination note. This document is not a fourth-layer governance rules source; permanent project rules remain in `PROJECT_RULES.md`.

## 1. Production direction

Cloudflare D1 remains the authoritative accounting database. Google Cloud Storage (GCS) is the production off-site / disaster-recovery storage provider; it is not a live database or synchronization source.

CYAccountingWeb uses the same provider-neutral backup boundary as the broader Chihyuan Web architecture:

```text
Application backup semantics
        ↓
BackupService
        ↓
BackupStorageProvider
        ↓
Concrete provider (GCS in production)
```

The storage provider contract covers at least `putObject`, `getObject`, `listObjects`, and `deleteObject`. Restore orchestration remains in the service/domain layer rather than inside the GCS adapter.

## 2. Current isolation boundary

CY Web and CYAccountingWeb may use the same GCP project, but they use separate backup datasets and separate least-privilege service identities / credentials. One broad GCS credential must not be shared across applications.

Production project IDs, bucket names, service-account identities, credentials, backup files and D1 dumps are deployment/runtime data and must not be committed to Public Git.

CYAccountingWeb runtime uses only these protected configuration names:

```text
GCS_BUCKET
GCS_SERVICE_ACCOUNT_JSON
```

The Service Account JSON remains a Cloudflare Worker Secret and is never returned to the browser or stored in D1.

## 3. Portable backup set

V0.17 stores each backup as an independent backup set:

```text
CYAccountingWeb/<backup-id>/
├─ manifest.json
└─ data.json
```

`data.json` contains the portable D1 accounting representation. `manifest.json` records App/schema version, backup identifier, creation time, record counts, SHA-256 and byte size for `data.json`.

A backup is valid only after both objects are uploaded and read back from the provider, the read-back SHA-256 / byte-size checks pass, the manifest matches the backup identifier and data digest, and the record count is consistent.

## 4. Schedule and retention

- Cron: `30 19 * * *`
- Taiwan time: daily 03:30
- Retention: **14 days**
- Provider cleanup is best effort after a new backup has already passed read-back verification; cleanup failure must not retroactively invalidate the new verified backup.

A bucket-level lifecycle rule may be used as a second retention guard, but it does not replace application-level validation and retention logic.

## 5. Restore safety

Restore remains future work and must satisfy all of the following:

- `SUPER_ADMIN` only;
- server-side authorization is authoritative;
- two explicit confirmation steps;
- the second confirmation clearly states that current D1 data will be replaced;
- validate backup format, App/schema version, manifest and integrity before replacement;
- retain audit evidence including actor, time, backup identifier and result.

## 6. Future shared Backup Service

If CYAccountingWeb later becomes a module of Chihyuan Enterprise Management System, prefer this topology:

```text
CY Web ---------┐
CYAccounting ---+--> CY Backup Service / Worker --> BackupStorageProvider --> GCS
future apps ----┘
```

At that stage, direct per-App GCS credentials can be retired. Even behind a shared Backup Service, each application's backup dataset remains logically isolated and independently restorable.

Do not combine unrelated application databases into one mixed backup file solely because they share a backup service.
