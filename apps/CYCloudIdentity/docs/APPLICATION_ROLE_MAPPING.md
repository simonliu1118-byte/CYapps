# CYCloud Identity — Legacy Application Role Mapping

> **Status: legacy implementation note. Superseded for forward design by `ROLE_AND_ACCESS_MODEL.md`.**
>
> The deployed CYCloud Identity `0.1.14` still contains this Group-based compatibility-role mechanism. It remains documented only so the current runtime can be understood and migrated safely. New product work must not extend this model.

## Legacy purpose

The existing implementation decides Application entry through a combination of:

- protected Workspace Super Admin access;
- direct Employee Application grants;
- Identity Group Application grants.

An optional `USER_ADMIN` compatibility mode then projects a coarse consumer role.

## Legacy data model

`workspace_applications` currently supports:

```text
compatibility_role_mode = NULL | USER_ADMIN
```

`identity_group_application_access` currently supports:

```text
application_role_key = NULL | USER | ADMIN
```

Under this legacy mode:

1. Workspace Super Admin resolves to `SUPER_ADMIN`.
2. An active Group mapping to `ADMIN` resolves to `ADMIN`.
3. Otherwise an active Group mapping to `USER` resolves to `USER`.
4. Otherwise a direct Employee Application grant resolves to `USER`.
5. Otherwise there is no effective Application access.

The legacy precedence is therefore:

```text
SUPER_ADMIN > ADMIN > USER
```

Ordinary Groups cannot create `SUPER_ADMIN`.

## Why this is being replaced

The approved product model has been simplified:

- CYID has exactly three Workspace roles: `SUPER_ADMIN`, `ADMIN`, `USER`;
- `Identity Admin` is an ADMIN capability, not a fourth role;
- Application Access decides **entry only**;
- consumer Apps receive the CYID Workspace role directly instead of deriving another role from Group mappings;
- App-local module/business permissions remain inside the consumer App;
- CY Web is the special multi-module core App with always-on entry and its own Module Access model.

See `ROLE_AND_ACCESS_MODEL.md` for the approved target contract.

## Migration rule

Until runtime migration is complete:

- existing Group/direct grants and `applicationRoleKey` behavior remain current implementation facts;
- do not add new Group-derived role semantics;
- do not teach new consumer Apps to reconstruct authority from Group names/mappings;
- migration must preserve a safe authorization path while Employee Workspace Role and target App Access are introduced;
- CYInvoice source/runtime remains unchanged in this workstream and will adopt the stable three-role contract only in its dedicated integration work.
