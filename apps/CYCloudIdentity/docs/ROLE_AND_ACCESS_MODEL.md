# CYCloud Identity — Role and Access Model

> **Status: approved target product contract; runtime migration pending.**
>
> This document records the finalized CYID Workspace Role / Identity Admin / Application Access model confirmed on 2026-09-29. The currently deployed `0.1.14` runtime still contains the older Identity Group + compatibility-role implementation. New development must migrate toward this contract rather than extend the legacy Group projection.

## 1. Scope

CYCloud Identity answers three shared questions:

1. **Who is this Employee?**
2. **What is the Employee's Workspace-level role?**
3. **May the Employee enter this CY Application?**

CYID does **not** become a universal catalog of each App's internal business permissions.

## 2. Workspace role model

The Workspace role model is intentionally limited to:

```text
SUPER_ADMIN
ADMIN
USER
```

### SUPER_ADMIN

- Exactly one active Super Admin exists per active Workspace.
- Super Admin is represented by the protected Workspace authority pointer, not by an ordinary mutable Employee role field.
- Effective role is `SUPER_ADMIN` whenever `employee_id == workspace.super_admin_employee_id`.
- Super Admin cannot be demoted, disabled, deleted or stripped of control by another Employee.
- Super Admin transfer requires the protected transfer workflow.

### ADMIN

- Workspace administrator for ordinary Employee lifecycle and, when granted App/Module Access, business-system administration.
- A normal ADMIN may create USER accounts and manage ordinary USER lifecycle, but has no Access-management capability.

### USER

- Normal employee role.
- USER receives only the Applications and App-local permissions explicitly granted under the applicable consumer contract, except the mandatory CY Web core entry described below.

## 3. Identity Admin capability

`Identity Admin` is **not** a fourth Workspace role. It is a special capability attached to an `ADMIN`.

Conceptually:

```text
role = ADMIN
identity_admin = true | false
```

### Identity Admin may

- create either `USER` or `ADMIN` Employees;
- perform `USER ↔ ADMIN` changes for non-protected Employees;
- manage Application Access for USER, ADMIN and other Identity Admin accounts;
- manage CY Web Module Access through the CY Web authorization surface;
- perform forced Email recovery for activated Employees;
- perform all ordinary ADMIN Employee-lifecycle actions.

### Identity Admin may not

- modify its own Application Access;
- modify its own CY Web Module Access;
- grant or revoke `identity_admin` for itself or anyone else;
- demote another Identity Admin to USER;
- modify the Super Admin's Role, Access, Email or protected authority state;
- perform Super Admin transfer or Workspace security-core operations reserved to Super Admin.

An Identity Admin's own Access may be changed by another Identity Admin or by Super Admin.

### Grant / revoke

Only Super Admin may grant or revoke `Identity Admin` capability.

If an Identity Admin must become USER, Super Admin first removes the Identity Admin capability, then changes the Employee role to USER. The current model does not allow `USER + identity_admin`.

## 4. Future HR note

A future company HR role may need to manage Employee identity lifecycle without being a business-system ADMIN.

The approved direction is **not** to add a fourth Workspace role now. If that need becomes real, CYID may decompose Identity operations into narrower capabilities so a USER/HR identity can receive limited Employee-lifecycle authority without receiving business-admin authority.

This is deliberately deferred and must not complicate the current implementation.

## 5. Employee creation

Role is selected when the Employee is created.

- normal ADMIN: may create `USER` only;
- Identity Admin: may create `USER` or `ADMIN`;
- Super Admin: may create `USER` or `ADMIN`;
- no create flow directly grants Identity Admin capability.

Application and CY Web Module Access are configured after Employee creation; role selection does not implicitly grant ordinary Apps or Modules.

## 6. First activation and activation email

A newly created Employee starts as first-time pending activation.

The management UI state is:

```text
尚未驗證／待啟用
```

Creation flow:

1. create pending Employee;
2. automatically send the first activation email;
3. activation email contains a direct link that opens the CY Web account-activation flow;
4. the link itself is not an authentication credential and must not bypass Email verification, OTP or first-password setup;
5. Employee verifies Email and sets the first password;
6. account becomes activated/enabled.

Pending-row management actions include at least:

```text
編輯
重寄啟用信
刪除
```

Changing a pending Employee's Email causes activation to continue against the new Email and a new activation message to be sent.

If the Email provider fails after the Employee was successfully created, do not roll back the Employee. Preserve the pending account, expose a send-failure state and permit resend.

## 7. Activated / disabled lifecycle

Consumer-visible states remain distinct:

- `尚未驗證／待啟用` — first activation incomplete;
- `啟用` — activation complete and Employee enabled;
- `停用` — Employee was activated previously and is currently disabled.

Only an Employee that has **never completed first activation** may be physically deleted.

Once activation has completed, the Employee record must be retained for history/Audit and may only be disabled/re-enabled.

## 8. Forced Email recovery

For an activated Employee whose Email is banned, lost or otherwise unusable:

- Identity Admin or Super Admin may replace the Employee Email;
- normal ADMIN may not;
- account remains an activated account;
- existing password is preserved;
- new Email becomes unverified;
- existing Employee sessions are revoked;
- verification can be resent to the new Email;
- UI may display `啟用 · Email 待驗證`;
- the account must not be confused with first-time `尚未驗證／待啟用`.

Self-service Email change continues to use the normal Employee-owned verification flow.

## 9. Application Access

Role and Application Access are separate dimensions.

Role changes preserve existing App Access; they do not silently add or remove grants.

### CY Web core entry

CY Web is the core account-management application.

Every valid Employee has CY Web entry access:

```text
CYWEB = TRUE (locked / non-revocable)
```

This allows an Employee with no business-module access to log in and manage their own password/Email/account state.

### Other CY Apps

Other Application Access defaults to not granted after Employee creation and is configured later.

- normal ADMIN cannot manage Application Access;
- Identity Admin may manage Application Access for USER, ADMIN and other Identity Admin accounts, except its own;
- Super Admin may manage all non-protected Employee grants;
- Super Admin automatically has entry access to every Workspace-enabled CY App and that access cannot be revoked through ordinary grants.

## 10. Consumer role projection

All CYID-integrated CY Apps initially consume the same Workspace role directly:

```text
CYID SUPER_ADMIN -> App SUPER_ADMIN
CYID ADMIN       -> App ADMIN
CYID USER        -> App USER
```

There is no additional Group-to-App role mapping in the target model.

For a normal single-purpose App:

- `SUPER_ADMIN` with App availability -> full protected administration;
- `ADMIN` with App Access -> App Admin;
- `USER` with App Access -> App User, with any finer permissions owned by that App.

App-local detailed permissions primarily apply to USER-level operation unless a specific future App contract says otherwise.

## 11. CY Web multi-module exception

CY Web is both the core account shell and a multi-module business system.

CYID owns only the always-on CY Web entry identity. CY Web itself owns module access such as:

- Customer
- Order
- Item
- Outsourcing
- WorkLog
- future CY Web modules

Rules:

- Super Admin: all CY Web Module Access is automatically TRUE / locked;
- Identity Admin: may configure USER, ADMIN and other Identity Admin Module Access, except its own;
- Super Admin: may configure non-protected Employee Module Access;
- normal ADMIN: no Module Access administration capability;
- an ADMIN with a specific Module Access has complete management authority within that module;
- USER-level finer business permissions, if later needed, remain a CY Web concern rather than CYID schema.

## 12. Immediate effect and sessions

Authorization changes are server-side authority changes, not UI preferences.

At minimum:

- Employee disabled -> all current sessions invalidated;
- Workspace role changed -> current sessions invalidated/re-resolved against the new role;
- Application Access revoked -> sessions for that Application cease to authorize access;
- forced Email recovery -> current Employee sessions invalidated;
- CY Web Module Access changes -> protected CY Web APIs enforce the new module authorization on subsequent requests.

A browser menu that has not refreshed is never an authorization source.

## 13. Super Admin-only security operations

The following remain reserved to Super Admin:

- grant/revoke Identity Admin capability;
- modify protected Identity Admin status;
- Super Admin transfer;
- Workspace Recovery Email / final-control recovery;
- Workspace security-core / OTP policy operations designated as Super Admin-only;
- any operation that can remove or replace the Workspace's final authority path.

## 14. Legacy implementation boundary

The deployed CYCloud Identity `0.1.14` still contains:

- extensible Identity Groups;
- Group memberships;
- Group/direct Application Access;
- optional `USER_ADMIN` compatibility-role projection.

Those are current implementation facts, not the approved forward role model.

Do not add new product behavior that depends on Group-derived `ADMIN`/`USER` projection. The next implementation phase must migrate schema/runtime/UI/tests toward this document, while preserving safe data transition and consumer compatibility during cutover.
