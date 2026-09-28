# CYCloud Identity — Application Role Mapping

> Status: executable coarse consumer-App compatibility-role contract. It does not replace App-local module/business permissions.

## Purpose

CYCloud Identity decides whether an Employee may enter an enabled Application through Workspace highest authority, a direct Employee grant, or an Identity Group grant.

Some consumer Apps also need one **coarse compatibility role** after entry so they can preserve an existing simple authorization model without rebuilding a second account/role system. The first required case is CYInvoice, whose existing compatibility role is only `USER`, `ADMIN`, or protected `SUPER_ADMIN`.

The mapping remains Application-scoped and does not turn ordinary Identity Groups into a fixed global role enum.

## Data model

`workspace_applications` owns the optional compatibility mode:

```text
compatibility_role_mode = NULL | USER_ADMIN
```

`identity_group_application_access` owns the per-Group, per-Application mapping:

```text
application_role_key = NULL | USER | ADMIN
```

Rules:

- the role mapping belongs to the specific **Group + Application** grant, not to the Identity Group globally;
- Applications that do not need a compatibility role keep `compatibility_role_mode = NULL` and omit `applicationRoleKey` from the principal;
- when `USER_ADMIN` mode is enabled, active Group grants require `USER` or `ADMIN`;
- `SUPER_ADMIN` is reserved Workspace authority and may never be stored by an ordinary Group mapping;
- disabling/removing a Group Application grant removes that mapping from effective authorization;
- Public migrations/tests use synthetic Application data only; no real Workspace access matrix is seeded.

The existing `employee_application_access` direct grant remains an entry grant. Under `USER_ADMIN` mode a direct grant without a qualifying Group role resolves to `USER`, so there is no second per-Employee Admin/User configuration surface.

## CYInvoice mapping

For a normal Identity Group that has CYInvoice Application Access, the management UI shows exactly one field:

```text
CYInvoice 權限
- User
- Admin
```

Stored values:

```text
USER
ADMIN
```

There is no `SUPER_ADMIN` option.

### Effective CYInvoice role

Resolve in this order:

1. `isWorkspaceSuperAdmin = true` → `SUPER_ADMIN`.
2. Otherwise, if any active CYInvoice-enabled Identity Group for the Employee maps to `ADMIN` → `ADMIN`.
3. Otherwise, if any active CYInvoice-enabled Identity Group maps to `USER` → `USER`.
4. Otherwise, if the Employee has an active direct CYInvoice Application grant → `USER`.
5. Otherwise → no CYInvoice access.

This gives a deterministic conflict rule: **SUPER_ADMIN > ADMIN > USER**.

A normal Group can therefore never create or impersonate the protected Workspace highest authority.

## API projection

For an Application with `compatibility_role_mode = USER_ADMIN`, both login and session resolve return the current effective role in the normalized principal:

```json
{
  "principal": {
    "applicationRoleKey": "USER"
  }
}
```

Allowed values are:

```text
USER
ADMIN
SUPER_ADMIN
```

The value is computed server-side from **current** Workspace authority, active Group memberships, Group Application grants and direct Application access. Session resolve recomputes the projection; consumers must not reconstruct it from editable Group display names or cache it as an independent authority source.

Applications without compatibility-role mode omit the field.

## UI behavior

CY Web Shared Identity management UI presents the mapping on the **Identity Group → Application Access** row, not as a second global Group role field.

Expected behavior:

- Application compatibility mode off → no role selector;
- `USER_ADMIN` mode on + Group Application access on → require `User` or `Admin`;
- current Workspace highest authority does not need this selector to obtain `SUPER_ADMIN`;
- changing Group membership or mapping changes the effective role on the next Identity session resolve without copying role state into the consumer App;
- Group rename does not affect the mapping because authorization references stable `group_id`.

## Boundary

This mechanism is only a coarse compatibility projection. Detailed CYInvoice business/module permissions, Device lifecycle, Local/Cloud transition and Windows offline behavior remain in the CYInvoice workstream.

Other Apps may use the same generic `application_role_key` field only when they genuinely need a coarse compatibility role. CYCloud Identity must not grow a global fixed role enum or a universal fine-grained permission catalog.
