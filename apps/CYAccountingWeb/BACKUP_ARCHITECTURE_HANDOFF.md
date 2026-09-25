# CYAccountingWeb Backup Architecture Handoff

> Status: implementation handoff / coordination note. This document is not a fourth-layer governance rules source; permanent project rules remain in `PROJECT_RULES.md`.

## 1. Why this handoff exists

CYAccountingWeb already has a V0.16.0 backup implementation targeting Google Drive. That work contains useful D1 export, integrity-verification, retention and scheduling logic, but the Chihyuan Web projects have now aligned on a different long-term production storage target:

> **Google Cloud Storage (GCS) is the initial Chihyuan production off-site backup provider.**

The reason is provider isolation: live application data is on Cloudflare D1, while disaster-recovery copies live outside the Cloudflare provider boundary.

The storage-provider change does **not** make GCS a live database or synchronization source. D1 remains authoritative.

## 2. Shared contract, separate current deployments

CYAccountingWeb and CY Web should use the same conceptual provider-neutral backup boundary:

```text
Application / domain backup logic
        ↓
BACKUP_PROVIDER
        ↓
Concrete provider implementation
        ↓
GCS in Chihyuan production
```

The shared contract should cover at least:

- create backup;
- list backups;
- verify backup;
- restore backup;
- apply retention / delete expired backups.

Exact TypeScript names may differ during implementation, but storage-provider-specific code must stay behind this boundary.

## 3. Current infrastructure topology

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

## 4. What to keep from the existing Google Drive V0.16 work

Do not throw away the useful parts of the existing implementation merely because the provider changes.

Preserve/refactor where appropriate:

- scheduled backup flow (currently 03:30 Taiwan time);
- portable D1 JSON representation;
- application/schema/version metadata;
- record/row counts;
- manifest concept;
- data SHA-256 and file SHA-256 verification;
- upload-then-read-back integrity verification;
- retention policy (currently latest 30 backups);
- disaster-recovery restore validation.

Replace the provider-specific layer:

```text
Google Drive API / OAuth / refresh-token storage
                ↓
             GCS provider
```

After GCS production acceptance, remove obsolete Drive production credential and OAuth paths rather than maintaining two permanent production backup providers without a separate business requirement.

## 5. Restore safety boundary

CYAccountingWeb should align with CY Web's confirmed high-risk backup semantics:

- restore is `SUPER_ADMIN` only;
- `ADMIN` and ordinary employees cannot restore;
- restore requires two explicit confirmation steps;
- the second confirmation must clearly state that current D1 data will be replaced and that this is a destructive/high-risk action;
- Worker/API authorization is authoritative; UI hiding alone is insufficient;
- backup and restore operations produce audit evidence including actor, time, backup/version identifier and result.

A backup must be validated before restore: expected app/schema version, manifest, integrity checksum and other required metadata must pass.

## 6. Future integration into Chihyuan Enterprise Management System

CYAccountingWeb may later become the Accounting/Finance module inside CY Web. Do **not** pre-empt that integration by sharing one GCS key today.

Preferred future topology:

```text
CY Web ---------┐
CYAccounting ---+--> CY Backup Service / Worker --> GCS
future apps ----┘
```

At that stage:

- Apps call a shared internal Backup Service / Worker through a service/API boundary;
- only that backup service needs direct GCS authorization;
- current per-App direct GCS credentials can be retired;
- the backup service may standardize manifest, checksum, retention, audit and restore orchestration.

Even after consolidation, backup datasets remain logically separated:

```text
CY Web backup set        -> independently restorable
Accounting backup set    -> independently restorable
```

Do not combine unrelated application databases into one mixed backup file solely because they share a backup service.

## 7. Coordination source

The matching CY Web architecture decisions are:

- `BD-044`: in-app backup/restore is Super Admin-only and restore requires double confirmation.
- `BD-045`: Chihyuan production initially uses GCS through a provider-neutral backup contract; CY Web and CYAccountingWeb use separate current identities/datasets and may later converge behind a shared Backup Service.

When implementation begins, read the latest versions of both projects' TODO / project rules before changing runtime code. If the projects differ on a genuinely accounting-specific requirement, keep that difference explicit rather than forcing artificial code sharing.
