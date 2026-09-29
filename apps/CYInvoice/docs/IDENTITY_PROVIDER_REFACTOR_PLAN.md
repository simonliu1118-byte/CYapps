# CYInvoice Identity Provider Refactor — Implementation Plan

更新日期：2026-09-29

> Status: approved planning baseline. This document defines the next CYInvoice implementation sequence only. It does not authorize source changes, deployment, merge, tag or Release by itself.

## 1. Why this work comes first

CYInvoice now has three accepted product modes:

- Local;
- Built-in Cloud / Self-hosted;
- future CY ID Cloud.

The application must therefore stop assuming that every Employee credential and role comes from one hard-coded store. Authentication source selection belongs behind one desktop identity boundary, while CYInvoice business authorization remains inside CYInvoice.

The shared desktop reference is `simonliu1118-byte/AITeam:docs/DESKTOP_IDENTITY_PROVIDER_GUIDE.md`. CYInvoice-specific product boundaries are in `CY_ID_INTEGRATION.md`.

## 2. Fixed product decisions before implementation

- CYInvoice formal roles are `SUPER_ADMIN`, `ADMIN`, `USER`.
- `EMPLOYEE` is no longer a role name.
- There is no persisted legacy `EMPLOYEE` role dataset requiring migration.
- Local mode keeps CYInvoice Local EmployeeStore authority.
- Built-in Cloud / Self-hosted keeps CYInvoice-owned Workspace / Employee / Credential authority.
- CY ID Cloud will use CY ID as Employee / Credential authority while CYInvoice keeps its own Workspace / Device / business coordination.
- CY ID mode will hide CYInvoice Account Management; account CRUD is owned by CYWEB / CY ID.
- CY ID internal schema, Group model and Application Access implementation are outside this workstream.
- Device identity is separate from Employee identity in every mode.

## 3. Package 1 — Identity Foundation Refactor

This is the next source work package. Its goal is architectural separation with **no intended functional behavior change**.

### 3.1 Role vocabulary cleanup

Replace the CYInvoice role vocabulary:

```text
SUPER_ADMIN
ADMIN
EMPLOYEE
```

with:

```text
SUPER_ADMIN
ADMIN
USER
```

Apply consistently to source, tests, synthetic fixtures, schema/contract constants where applicable, UI labels and documentation. Do not add a permanent `EMPLOYEE == USER` compatibility alias because no real persisted legacy role requires it.

### 3.2 Normalized principal

Introduce one application-facing principal model. Exact class names may vary after source inspection, but the semantic boundary should be equivalent to:

```csharp
AppPrincipal
- StableEmployeeId
- EmployeeNo
- DisplayName
- Role: USER | ADMIN | SUPER_ADMIN
- Enabled
- AuthoritySource / ProviderKind
```

Business workflows must consume the normalized principal rather than inspect Local/Cloud credential stores directly.

### 3.3 Minimal identity provider boundary

Introduce a small interface such as `IIdentityProvider`. Do not build a generic plugin ecosystem.

The minimum responsibilities are:

- identify the active authority kind;
- authenticate an Employee for an execution-time operation;
- return a normalized current principal;
- expose the account-management ownership needed by application composition/UI.

Conceptual shape only:

```csharp
public interface IIdentityProvider
{
    IdentityProviderKind Kind { get; }
    bool OwnsAccountManagement { get; }

    Task<AuthenticationResult> AuthenticateAsync(
        AuthenticationRequest request,
        CancellationToken cancellationToken);

    Task<PrincipalResult> RefreshPrincipalAsync(
        string employeeNo,
        CancellationToken cancellationToken);
}
```

The implementation is free to use more suitable names/types after code review. The invariant is the boundary, not this exact signature.

### 3.4 Initial providers

Package 1 implements only:

```text
LocalIdentityProvider
BuiltInCloudIdentityProvider
```

Do **not** implement `CyIdIdentityProvider` yet.

`LocalIdentityProvider` wraps the existing Local Employee authority.

`BuiltInCloudIdentityProvider` initially wraps existing Built-in Cloud behavior. Package 1 must avoid silently changing online/offline semantics while the call sites are being centralized.

### 3.5 Centralized provider selection

Provider selection must occur at one composition/runtime boundary. Avoid scattering checks such as:

```text
if local ...
if cloud ...
if CY ID ...
```

through Forms and business workflow classes.

UI and business code should request the active identity service/provider through the central application composition path.

### 3.6 Route existing execution-time authentication through the provider

Inventory and migrate all current security-sensitive Employee verification paths, including at least:

- account-management authentication where still applicable;
- void / cancellation approval;
- allowance / allowance-void approval;
- manual-review manager approval;
- administrative closure;
- Device / Workspace administration;
- SUPER_ADMIN-only operations;
- any direct call to `AuthenticateEmployee`, `CloudEmployees.Authenticate`, Local EmployeeStore password verification, or equivalent direct credential check.

After Package 1, normal Form/business workflow code must not choose Local vs Cloud credential authority itself.

### 3.7 Account-management ownership

Support the already-approved policy without implementing CY ID yet:

```text
Local provider        -> Account Management owned by CYInvoice
Built-in Cloud        -> Account Management owned by CYInvoice
CY ID provider future -> Account Management external; CYInvoice hides the feature
```

Do not add a half-functional read-only account CRUD screen for CY ID mode.

## 4. Package 1 acceptance gates

Package 1 is accepted only when all of the following hold:

- formal role vocabulary is `SUPER_ADMIN / ADMIN / USER` throughout the active code path;
- existing Local behavior remains functionally unchanged;
- existing Built-in Cloud behavior remains functionally unchanged;
- security-sensitive business workflows obtain Employee authentication/role through the provider boundary;
- Forms/business workflows do not directly decide which credential store to verify against;
- provider selection is centralized;
- existing ADMIN/SUPER_ADMIN protection rules still pass automated tests;
- USER cannot gain manager-only operations after the rename;
- Windows build and startup smoke remain green;
- no real credential, Employee, Workspace, Email, token, endpoint or production data is added to Public source/tests/logs.

## 5. Package 2 — Built-in Cloud online authority freshness

Only after Package 1 is stable, fix the already-reproduced B-device stale Employee snapshot defect inside the Built-in Cloud provider boundary.

Target behavior:

```text
Sensitive operation
  ↓
Cloud reachable?
  ├─ yes -> verify against current Cloud authority, then refresh protected local cache
  └─ no  -> use last trusted protected offline cache
```

A merely stale cache or missed 5-minute background refresh must not be treated as Offline.

Acceptance includes A/B real-device checks for:

- newly-created Employee usable on B at the next protected operation;
- role change effective on B at the next protected operation;
- enabled/disabled change effective on B at the next protected operation;
- password change effective on B at the next protected operation;
- true network outage still permits only the approved offline behavior;
- reconnect restores current Cloud authority.

## 6. Package 3 — Device revoke / retire

Implement Device revoke/retire API + UI + token invalidation + security audit before destructive Cloud -> Local reset.

Required invariant:

```text
Device revoked
-> old Device Token becomes unusable
-> history may remain
-> rejoin creates a new Device identity
```

## 7. Package 4 — Cloud -> Local destructive reset

Depends on Package 3.

Implement the already-approved double-confirmation reset:

```text
confirm data wipe twice
-> stop sync/sensitive work
-> safely revoke/retire current Device
-> clear current-PC CYInvoice Data/Cache/settings/Employee stores/Cloud cache/identity/token/pending state
-> restart into first-run
-> recreate Local SUPER_ADMIN
```

Do not delete the Cloud Workspace, other Devices or central Employees.

## 8. Package 5 — complete existing Cloud acceptance

After the identity/refactor/reset foundations are stable, complete:

- invitation-code join real-machine acceptance;
- invitation revoke/resend;
- result-unknown recovery;
- A/B USER/ADMIN/SUPER_ADMIN behavior;
- Employee CRUD/password/enabled/role synchronization;
- Offline -> reconnect acceptance;
- backend Device/audit verification as needed.

## 9. Package 6 — CY ID integration

Do not start until CYCloudIdentity/CYWEB publishes a stable consumer handoff/contract.

Expected CYInvoice consumer inputs are intentionally small:

```text
stable Employee identity
enabled state
CYInvoice App Access result
CYInvoice role: SUPER_ADMIN | ADMIN | USER
```

Then implement `CyIdIdentityProvider` (or equivalent adapter) without changing CYInvoice business workflows already migrated to the provider boundary.

CY ID mode requirements:

- Account Management hidden in CYInvoice;
- CY ID role maps 1:1 to CYInvoice role;
- App Access is consumed as an upstream allow/deny result, not re-managed inside CYInvoice;
- Device/Workspace/business/offline responsibilities remain CYInvoice-owned;
- final Windows offline credential/cache protocol is designed at integration time and must not create dual authority.

## 10. Package 7 — V3 business coordination

After identity/device foundations are stable, continue with:

- Cloud Work Items;
- business audit viewer;
- Recovery Device flow;
- allowance/void coordination;
- multi-device OrderID collision solution;
- remaining business synchronization.

## 11. Dependency order

```text
Role rename + provider abstraction
        ↓
Built-in Cloud current-authority authentication
        ↓
Device revoke / retire
        ↓
Cloud -> Local destructive reset
        ↓
remaining Cloud acceptance
        ↓
CY ID adapter after stable external contract
        ↓
V3 business coordination
```

Do not reverse this order by wiring CY ID directly into Forms/workflows before the provider boundary exists.

## 12. Version / release boundary

This plan does not itself decide the next VERSION/BUILD value. Before source work starts, re-read repository/project version rules and classify the authorized work package. No merge, tag or formal Release without explicit user authorization.
