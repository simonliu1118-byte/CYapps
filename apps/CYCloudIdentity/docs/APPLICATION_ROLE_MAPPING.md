# CYCloud Identity — Application Role Mapping

> Status: approved target design for coarse consumer-App compatibility roles. It does not replace App-local module/business permissions and is not yet an executable API contract until the corresponding schema/runtime work is implemented.

## Purpose

CYCloud Identity already decides whether an Employee may enter an enabled Application through Workspace highest authority, a direct Employee grant, or an Identity Group grant.

Some consumer Apps also need one **coarse compatibility role** after entry so they can preserve an existing simple authorization model without rebuilding a second account/role system. The first required case is CYInvoice, whose existing compatibility role is only `USER`, `ADMIN`, or protected `SUPER_ADMIN`.

The mapping must remain simple and must not turn ordinary Identity Groups into a fixed global role enum.

## Data model

`identity_group_application_access` gains one nullable field:

```text
application_role_key
```

Rules:

- the field belongs to the specific **Group + Application** grant, not to the Identity Group itself globally;
- the value is app-specific coarse compatibility metadata and is not interpreted as a universal Identity role;
- applications that do not need a compatibility role leave the field `NULL`;
- `SUPER_ADMIN` is reserved system authority and may never be assigned by an ordinary group mapping;
- disabling/removing the group Application grant also removes that mapping from effective authorization;
- Public migrations/tests use synthetic application data only; no real Workspace access matrix is seeded.

The existing `employee_application_access` direct grant remains an entry grant. It does not require another per-Employee role-setting surface for CYInvoice.

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

This gives a deterministic conflict rule: **ADMIN wins over USER**, while Workspace highest authority always wins over both.

A normal group can therefore never create or impersonate the protected Workspace highest authority.

## API projection target

After implementation, login/session resolution for an Application that uses this mapping may expose one optional normalized field to the consumer adapter:

```json
{
  "applicationRoleKey": "USER"
}
```

For CYInvoice the only effective values are:

```text
USER
ADMIN
SUPER_ADMIN
```

The value is computed server-side from current authority on login/session resolve. Consumers must not reconstruct it from editable group display names.

Applications that do not use a compatibility role may omit/null this field.

## UI behavior

CY Web Shared Identity management UI should present the CYInvoice mapping on the **Identity Group → Application Access** row, not as a second global Group role field.

Expected behavior:

- CYInvoice access off → no CYInvoice role selector;
- CYInvoice access on → require `User` or `Admin`;
- current Workspace highest authority does not need this selector to obtain `SUPER_ADMIN`;
- changing a group mapping changes the effective role on the next Identity session resolve without copying role state into CYInvoice;
- group rename does not affect the mapping because authorization references stable `group_id`.

## Boundary

This mechanism is only a coarse compatibility projection. Detailed CYInvoice business/module permissions, Device lifecycle, Local/Cloud transition and Windows offline behavior remain in the CYInvoice workstream.

Other Apps may later use the same generic `application_role_key` field if they genuinely need a coarse compatibility role, but CYCloud Identity must not grow a global fixed role enum or a universal fine-grained permission catalog.
