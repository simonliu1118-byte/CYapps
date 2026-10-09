# CYInvoice Next Chat Handoff — 2026-09-29

> **歷史快照（2026-09-29）**：保留當時整合／deployment 證據，不能作為今日 branch／版本／Artifact 指令。現行交接以 [CLOUD_WORK_HANDOFF.md](CLOUD_WORK_HANDOFF.md) 為準，待辦只在 [TODO.md](TODO.md)；Run343 等舊 Artifact 已逾 3 天保留期。

> Purpose: continue CYInvoice Cloud / identity work from the actual merged and deployed state without re-deriving the architecture or reopening completed packages.

## 1. Repository / main / release state

- Repository: `simonliu1118-byte/CYapps`
- Project: `apps/CYInvoice/`
- Authoritative source branch: `main`
- Main integration commit after the stacked Cloud line was merged: `ede1fa37cf01a6f78730f7dd656f3b3b0098c7c5`
- Engineering version: **CYInvoice V2.6.10 Build 0**
- Development Cloud: **Cloud 0.8.8 / API 1 / compatibility Schema marker 8 / storage Schema 11**
- Formal Release remains `cyinvoice-v2.4.2`; no new tag or formal Release was created.

The previous stacked PR chain has been integrated in dependency order:

```text
#197 Package 4
  -> #195 Package 3
  -> #194 Package 2
  -> #187 Package 1
  -> #121
  -> #100
  -> #73
  -> #72
  -> #71
  -> main
```

Do not restart work from any of those old package branches. Read current `main`.

## 2. Final automated acceptance before / after main merge

Package 4 final source head before stacking:

- `59e4a2175226916e63008c0384e3b10393ee38b3`
- CYInvoice Cloud Check #333: success.

Every stacked merge was revalidated before the next merge. Relevant final gates include:

- #334 through #340: stacked integration checks all green.
- Governance Check #708: success.
- CYInvoice Cloud Check #342: success.
- CYInvoice Windows Build #236: success.
- Main push CYInvoice Cloud Check #343: success.

A Governance failure immediately before main merge exposed a real gap: `.github/workflows/cyinvoice-cloud.yml` uploaded an engineering artifact without first calling the repository public-package scanner. The workflow was corrected so `scan-public-package.py` runs before upload. Governance then passed; the scanner was not weakened or bypassed.

Main engineering artifact:

- `CYInvoice_cloud-foundation_engineering-run343`
- artifact id `11015665801`
- digest `sha256:80fb15cae205bb9dbc3a0ca4b9faa7d1085bfc7756c8410eea725bc55c74dbc2`
- portable package contains `CYInvoice/CYInvoice.exe`, VERSION `2.6.10`, Runtime/WebView2, and the expected support files.

## 3. Development Cloud deployment state

The established deployment branch `cyinvoice/cloud-dev-deploy` was reset to current main and given a current staged deployment workflow rather than rerunning the stale Cloud 0.8.2 / Schema 7 workflow.

Development deploy Run #7 completed successfully.

The workflow performed, in order:

1. Cloudflare environment credential presence check.
2. required runtime secret-name check without exposing secret values.
3. TypeScript check, Cloud tests and Worker dry-run.
4. D1 time-travel restore bookmark capture.
5. pre-change row/status/role audit.
6. canonical remote migration command.
7. post-change row/status/role/FK audit.
8. Worker deploy.
9. health-contract verification.

Important observation: remote D1 already reported **no unapplied migrations** before the deployment. Therefore Run #7 did not re-apply 0010/0011; storage was already at Schema 11.

The pre/post audits remained stable at aggregate level:

- 1 Workspace.
- 2 active Devices.
- 2 central Employees.
- 1 pairing record.
- central roles: one `SUPER_ADMIN`, one `ADMIN`.
- `PRAGMA foreign_key_check` returned no rows.

No real IDs, names, Email addresses, credentials or tokens belong in public documentation.

Final live health verification from the deployment workflow:

```text
Cloud 0.8.8
API 1
compatibility Schema marker 8
storage Schema 11
storage ok
capability: device-revoke-v1
capability: device-self-status-v1
```

## 4. Product modes / recovery invariant

CYInvoice has three formal product modes:

```text
Local
Built-in Cloud / Self-hosted
CY ID Cloud (future consumer integration)
```

The lifecycle invariant is not simply “a Workspace must always have one Device”. The correct invariant is:

> An active Workspace must retain at least one independently verifiable administrative recovery path.

Therefore:

- Local: no Workspace / Device lifecycle.
- Built-in Cloud today: active Workspace cannot lose its final active Device; `LAST_ACTIVE_DEVICE` remains enforced.
- future CY ID Cloud: zero active CYInvoice Devices may be valid only after CY ID SUPER_ADMIN is a real independent recovery authority and the recovery contract is implemented.

Do not weaken `LAST_ACTIVE_DEVICE` before CY ID recovery exists.

## 5. Package 1 — Identity Foundation — complete

Completed and merged:

- canonical roles `SUPER_ADMIN / ADMIN / USER`.
- `EMPLOYEE` removed as a formal active role.
- normalized `AppRole`, `AppPrincipal`, `IIdentityProvider`.
- `LocalIdentityProvider` and `BuiltInCloudIdentityProvider`.
- centralized runtime provider selection.
- active authentication call sites routed through the provider boundary.
- Cloud authority never falls through to Local credentials.
- Local / protected Cloud cache migration and D1 role-vocabulary migration.

CY ID provider is intentionally not implemented yet.

## 6. Package 2 — Cloud Authority Freshness — complete

Completed and merged:

- protected Built-in Cloud operations fetch current Cloud Employee authority when online.
- current password / role / enabled state replaces stale cache immediately.
- new central Employee becomes visible on the next protected operation.
- genuine transport outage / request timeout may use the last trusted protected cache.
- HTTP errors, revoked Device auth, malformed data, Workspace mismatch and caller cancellation fail closed.
- reconnect immediately returns to current Cloud authority.

Automated implementation is complete. Cross-device real-machine CRUD/password/role/enabled acceptance remains a separate acceptance item.

## 7. Package 3 — Device Revoke / Retire — complete

Completed and merged:

- `GET /v1/devices` trusted Device inventory.
- `POST /v1/devices/revoke`.
- execution-time central SUPER_ADMIN credential verification.
- revoke preserves Device history and immediately invalidates the old Device token for normal Cloud operations.
- `SELF_RETIRE` and `SUPER_ADMIN_REVOKE` audit reasons.
- idempotent repeated revoke.
- successful revoke + audit committed together.
- Windows Device Management UI.
- active Built-in Workspace final Device is protected by `LAST_ACTIVE_DEVICE`.

Real A/B revoke acceptance is now possible because development Cloud 0.8.8 is deployed.

## 8. Package 4 — Cloud -> Local destructive reset — complete

Package 4 implements current **Local + Built-in Cloud** behavior only.

Completed and merged:

- explicit destructive Local reset back to first-run.
- Built-in Cloud -> Local is no longer a mode-flag flip.
- reset transaction marker is outside Data/Cache and stores no passwords or Device tokens.
- reset execution happens only after the WinForms message loop exits, so UI/background sync is stopped before destructive operations.
- current Device revoke is routed through the same shutdown -> revoke -> confirm -> wipe transaction.
- remote Device revoke remains immediate.
- local wipe is authorized only after Cloud confirms the current Device is `revoked` or the Workspace is `disabled`.
- ambiguous revoke response preserves all local data and Device token; a later startup reconciles through the narrow self-status recovery endpoint.
- `GET /v1/devices/self-status` can narrowly report the terminal Device / Workspace lifecycle state even when the old Device token is revoked; it does not restore normal authorization.
- last active Built-in Device remains blocked.
- Windows Client does not expose Workspace disable/delete/purge.

Workspace lifecycle remains a high-blast-radius central operation and stays outside the desktop client.

## 9. Immediate next gate — A/B real-machine lifecycle acceptance

Do not start a broad new package merely because CI is green. The next evidence needed is real Windows behavior against the deployed development Cloud.

Use the **Run343 V2.6.10 engineering artifact**, not an older build.

Preferred low-risk sequence:

### A. Baseline

- Confirm both existing A/B Devices still connect after Cloud 0.8.8 deployment.
- Confirm Device Management sees the expected active Device inventory.
- Confirm ordinary invoice/sync behavior is not regressed.

### B. Authority freshness

On A, perform one reversible central Employee change; on B, immediately exercise the corresponding protected authentication/authorization path without waiting five minutes or restarting.

Verify:

- latest Cloud authority is effective immediately;
- stale password/role/enabled state does not remain authoritative while Cloud is reachable;
- reconnect returns to latest Cloud authority after a true Offline interval.

### C. Device lifecycle without risking the existing A/B pair

Prefer creating a temporary fresh Device C by the already accepted pairing path.

Then verify:

1. A sees C as active.
2. A remotely revokes C with SUPER_ADMIN re-authentication.
3. C can no longer perform normal Device-authenticated Cloud operations.
4. Device history still shows C as revoked rather than hard-deleted.
5. Rejoining the same physical test installation creates a fresh Device identity rather than reviving the revoked token.

### D. Current-Device Cloud -> Local reset

With A/B still providing recovery for the Workspace, perform the destructive reset on the disposable C installation:

```text
confirm twice
-> close application
-> revoke current C Device
-> Cloud confirms terminal state
-> clear only C local Data/Cache/identity
-> restart first-run
```

Verify A/B and central Workspace/Employees remain intact.

### E. Last-active protection

Automated tests cover `LAST_ACTIVE_DEVICE`. Do not intentionally destroy the current A/B recovery topology merely to prove this manually. A dedicated disposable Workspace/environment is preferred for a destructive real-machine final-Device test.

### F. Ambiguous-response recovery

Keep as a focused fault-injection acceptance case. Do not simulate it by randomly killing the network on a machine carrying the only recovery path. The expected rule remains: uncertain Cloud outcome -> no local wipe -> preserve token/data -> reconcile on next startup.

## 10. What can proceed after the A/B gate

After the lifecycle acceptance above is clean, continue the remaining Built-in Cloud acceptance / lifecycle consolidation before CY ID:

- invitation-code join / revoke / resend / result-unknown real-machine acceptance;
- central Employee CRUD / Email OTP / password / enabled / role A/B acceptance;
- fresh-install first-use branching acceptance;
- controlled recovery when all normal Device tokens are lost but a valid recovery path remains;
- security-audit viewer / central management surface as separately scoped work;
- later V3 cross-device Work Item / business coordination.

Do not introduce CY ID-specific calls into existing forms until CYCloudIdentity/CYWEB publishes a stable consumer contract. When it does, CYInvoice should add a provider/adapter behind the existing normalized identity boundary rather than create dual authority.

## 11. Built-in Cloud final-Device / Workspace handling

Built-in Cloud final active Device:

- Workspace retained -> add another active Device first.
- Workspace no longer needed -> manually disable/archive Workspace in the external central management surface; Windows Client may verify inactive state and then wipe local state.

The desktop client must not gain Workspace disable/delete/purge controls.

Future CY ID Cloud may allow Device count to reach zero only because CY ID SUPER_ADMIN can independently authorize a fresh Device into the same bound CYInvoice Workspace.

## 12. Public-repository privacy boundary

`CYapps` is Public. Never commit real:

- Workspace / Device / Employee identifiers;
- Employee names / Email addresses;
- AMEGO credentials or customer/invoice data;
- Cloud endpoints treated as private runtime configuration;
- API keys, passwords, OTPs, invitation/pairing codes, Device Tokens;
- production resource IDs;
- real credential verifier material.

Use only synthetic fixtures/placeholders and aggregate non-identifying acceptance facts.

## 13. Governance / promotion rule

Engineering work may be merged/deployed when it reaches a technically appropriate verified node; do not leave completed stacked work indefinitely unintegrated.

Still keep these separate concepts explicit:

```text
merge source
!= deploy development Cloud
!= create formal tag / Release
```

No new formal CYInvoice tag/Release has been created from V2.6.10 yet.
