# CYAccountingWeb Backup Architecture Handoff

> Status: implementation handoff / coordination note. This document is not a fourth-layer governance rules source; permanent project rules remain in `PROJECT_RULES.md`.

## 1. Why this handoff exists

CYAccountingWeb already has a V0.16.0 backup implementation targeting Google Drive. That work contains useful D1 export, integrity-verification, retention and scheduling logic, but the Chihyuan Web projects have now aligned on a different long-term production storage target:

> **Google Cloud Storage (GCS) is the initial Chihyuan production off-site backup provider.**

The reason is provider isolation: live application data is on Cloudflare D1, while disaster-recovery copies live outside the Cloudflare provider boundary.

The storage-provider change does **not** make GCS a live database or synchronization source. D1 remains authoritative.

## 2. Shared architecture: service layer vs storage provider

CYAccountingWeb and CY Web should use the same two-layer backup boundary:

```text
Application / D1 backup semantics
        ↓
BackupService
        ↓
BackupStorageProvider
        ↓
Concrete provider implementation
        ↓
GCS in Chihyuan production
```

### `BackupService`

Application-level backup orchestration owns these semantics:

- create backup;
- list valid backups;
- verify backup integrity and compatibility;
- restore a selected backup;
- apply retention policy;
- enforce authorization and restore safety;
- create backup / restore audit evidence.

Conceptually:

```text
BackupService
- createBackup()
- listBackups()
- verifyBackup(backupId)
- restoreBackup(backupId)
- applyRetention()
```

### `BackupStorageProvider`

Storage-specific code only stores and retrieves opaque objects:

```text
BackupStorageProvider
- putObject(key, bytes, metadata)
- getObject(key)
- listObjects(prefix)
- deleteObject(key)
```

`BackupStorageProvider` must **not** own D1 restore logic, `SUPER_ADMIN` checks, schema migration rules, or application-domain decisions.

This is intentional: GCS can later be replaced by another object-storage backend without rewriting Accounting restore semantics.

## 3. Portable backup set format

A logical backup is a backup **set**, not merely one arbitrary file:

```text
<app-scope>/<backup-id>/
├─ manifest.json
└─ data.json
```

`backup-id` is a stable unique identifier for one backup operation and is not inferred solely from a timestamp.

### `manifest.json`

The portable manifest should contain at least:

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
tableCounts / recordCounts
dataObjectName
dataSha256
dataByteLength
backupStatus
```

Do not require production D1 database IDs, Cloudflare account IDs, GCS credentials, secrets, or other infrastructure-specific sensitive values inside the portable manifest.

Only a backup that has completed upload **and read-back verification** may be cataloged or displayed as valid/restorable.

### `data.json`

`data.json` contains the portable application-data snapshot needed to reconstruct the Accounting database.

Requirements:

- UTF-8 JSON;
- explicit format version;
- entities/tables identified explicitly rather than depending on source row order;
- preserve IDs and relationships needed for reconstruction;
- preserve accounting decimal/date/text semantics without binary-float reinterpretation;
- exclude password plaintext, OTPs, sessions, OAuth refresh tokens, API tokens and other credential material;
- reject unsupported future/legacy format versions rather than guessing.

Accounting-specific tables and restore ordering remain the responsibility of CYAccountingWeb, not the shared storage layer.

## 4. Integrity sequence

Application-level integrity uses SHA-256 over the exact stored `data.json` bytes. Store the digest and byte length in `manifest.json`.

A backup is successful only after:

```text
export D1
→ build data.json
→ calculate data SHA-256
→ build manifest.json
→ upload both objects
→ read data.json back from provider
→ recompute SHA-256
→ verify byte length / counts / manifest
→ mark backup valid
```

Provider-native checksums may be used as additional transport verification, but they do not replace the portable application-level SHA-256 contract.

## 5. Current infrastructure topology

The two applications may use the **same Google Cloud project**, but they must not share one broad GCS credential.

Preferred current topology:

```text
One Chihyuan GCP project (allowed)
│
├─ CY Web backup bucket / isolated namespace
│  └─ CY Web dedicated least-privilege service identity
│
└─ CYAccountingWeb backup bucket / isolated namespace
   └─ CYAccountingWeb dedicated least-privilege service identity
```

Requirements:

- CYAccountingWeb credential may access only the Accounting backup dataset it requires.
- CY Web credential may access only the CY Web backup dataset it requires.
- One compromised App credential must not automatically grant access to the other App's backup set.
- Each backup set must be independently listable, verifiable and restorable.
- Project ID, bucket name, service identity, credentials and other production resource values are deployment/runtime configuration; they do not belong in Public Git.

Separate buckets are the clearest operational boundary. A strongly isolated namespace is acceptable only if IAM and restore selection remain unambiguous.

## 6. What to keep from the existing Google Drive V0.16 work

Do not throw away the useful parts of the existing implementation merely because the provider changes.

Preserve/refactor where appropriate:

- scheduled backup flow (currently 03:30 Taiwan time);
- portable D1 JSON representation;
- application/schema/version metadata;
- record/row counts;
- manifest concept;
- data SHA-256 and file/integrity verification;
- upload-then-read-back integrity verification;
- retention policy (currently latest 30 backups);
- disaster-recovery restore validation.

Replace the provider-specific layer:

```text
Google Drive API / OAuth / refresh-token storage
                ↓
BackupStorageProvider
                ↓
             GCS adapter
```

After GCS production acceptance, remove obsolete Drive production credential and OAuth paths rather than maintaining two permanent production backup providers without a separate business requirement.

## 7. Restore safety boundary

CYAccountingWeb should align with CY Web's confirmed high-risk backup semantics:

- restore is `SUPER_ADMIN` only;
- `ADMIN` and ordinary employees cannot restore;
- restore requires two explicit confirmation steps;
- the second confirmation must clearly state that current D1 data will be replaced and that this is a destructive/high-risk action;
- Worker/API authorization is authoritative; UI hiding alone is insufficient;
- backup and restore operations produce audit evidence including actor, time, backup/version identifier and result.

Before any destructive D1 write begins:

```text
load manifest
→ verify appId / formatVersion / schema compatibility
→ load data
→ verify byte length + SHA-256 + record counts
→ only then begin controlled restore
```

Where supported, create/retain a pre-restore recovery point before replacing current D1 data. After restore, run application-level reconciliation before recording success.

## 8. Cross-application restore isolation

The shared outer contract does **not** make backup files interchangeable.

A CY Web backup must never appear as a valid Accounting restore candidate, and vice versa.

At minimum, candidate selection and restore validation must match the current application's:

```text
appId
formatVersion
schemaVersion compatibility
workspace / tenant scope where applicable
```

## 9. Future integration into Chihyuan Enterprise Management System

CYAccountingWeb may later become the Accounting/Finance module inside CY Web. Do **not** pre-empt that integration by sharing one GCS key today.

Preferred future topology:

```text
CY Web ---------┐
CYAccounting ---+--> CY Backup Service / Worker --> BackupStorageProvider --> GCS
future apps ----┘
```

At that stage:

- Apps call a shared internal Backup Service / Worker through a service/API boundary;
- only that backup service needs direct GCS authorization;
- current per-App direct GCS credentials can be retired;
- the shared service may centralize storage access, cataloging, retention, common manifest vocabulary and integrity verification;
- App-specific exporters/restorers still own their database schema semantics.

Even after consolidation, backup datasets remain logically separated:

```text
CY Web backup set        -> independently restorable
Accounting backup set    -> independently restorable
```

Do not combine unrelated application databases into one mixed backup file solely because they share a backup service.

## 10. Coordination source

The matching CY Web architecture decisions are:

- `BD-044`: in-app backup/restore is Super Admin-only and restore requires double confirmation.
- `BD-045`: Chihyuan production initially uses GCS; CY Web and CYAccountingWeb use separate current identities/datasets and may later converge behind a shared Backup Service.
- `BD-046`: separates `BackupService` from `BackupStorageProvider` and fixes the portable `manifest.json + data.json` backup-set and verification semantics.

When implementation begins, read the latest versions of both projects' TODO / project rules before changing runtime code. If the projects differ on a genuinely accounting-specific requirement, keep that difference explicit rather than forcing artificial code sharing.
