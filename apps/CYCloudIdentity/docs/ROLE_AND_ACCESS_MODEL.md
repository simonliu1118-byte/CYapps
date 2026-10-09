# CYCloud Identity — Role and Access Model

> **Status:** current product model for CYID `0.3.x`. Direct Role / Identity Admin / App Access and first-login Email verification are deployed to development; exact rollout and acceptance status is tracked in `../TODO.md`.

## 1. Scope

CYID answers: who is this Employee, what is the Workspace role, and may the Employee enter this CY Application. CYID is not a universal business-permission catalog.

## 2. Workspace roles

Exactly three effective roles exist:

~~~text
SUPER_ADMIN
ADMIN
USER
~~~

- Super Admin: exactly one active authority per Workspace; protected pointer, not ordinary mutable role; only protected transfer may replace it.
- Admin: ordinary Workspace administrator. A normal ADMIN may manage ordinary USER lifecycle but has no App/Module Access-management authority.
- User: normal Employee role; App-local finer permissions remain consumer-owned.

## 3. Identity Admin capability

Identity Admin is an `ADMIN` capability, never a fourth role.

Identity Admin may create USER/ADMIN, perform eligible USER<->ADMIN changes, manage eligible direct App Access, manage CY Web Module Access through CY Web and perform forced Email recovery.

Identity Admin may not change its own App/Module Access, grant/revoke Identity Admin, demote another Identity Admin directly to USER, or alter Super Admin protected state. Only Super Admin grants/revokes Identity Admin.

## 4. Employee creation and Email verification

Role is selected during creation:

- normal ADMIN -> USER only;
- Identity Admin / Super Admin -> USER or ADMIN;
- Identity Admin capability is never assigned in create.

New Employee lifecycle uses **Email 驗證** terminology:

1. create Employee as Email-unverified / first-login incomplete;
2. CYID automatically sends a verification Email containing a one-time first-login password;
3. user enters through the normal CY Web login screen; there is no separate「啟用帳號」entry;
4. valid initial password yields only a short-lived first-login ticket;
5. user must set an 8–16 character permanent password;
6. CYID marks Email verified / durable first lifecycle complete and invalidates temporary credential/ticket;
7. no normal session is issued by completion; user returns to login and authenticates again with the permanent password.

Initial password has an expiry. `重寄驗證 Email` replaces the previous initial credential and expiry immediately. Editing a pending Employee Email also invalidates the old initial credential and sends verification to the new Email.

Pending actions:

~~~text
編輯 | 重寄驗證 Email | 刪除
~~~

Email send failure preserves the Employee and exposes resend recovery.

## 5. Lifecycle states

Consumer UI distinguishes:

- `Email 未驗證` — first Email verification / permanent-password setup incomplete;
- `啟用` — first lifecycle complete and Employee enabled;
- `停用` — previously completed lifecycle but currently disabled;
- `啟用 · Email 待驗證` — activated account after authorized Email replacement/recovery.

`activated_at` or equivalent internal marker may preserve historical first-lifecycle completion. Only an Employee who never completed first verification/permanent credential creation may be physically deleted.

## 6. Activated-account Email recovery

Identity Admin / Super Admin may replace an activated non-Super-Admin Employee Email. Password and activated history remain, new Email becomes unverified and existing sessions are revoked. This is a re-verification flow, not a return to first login.

## 7. Application Access

Role and App Access are independent and role changes preserve existing grants.

### CY Web core entry

Every valid Employee has `CYWEB = TRUE` locked/non-revocable so account self-service remains reachable even with zero business modules. First-login temporary credential is also accepted only through this core account application.

### Other CY Apps

- new Employee ordinary App Access defaults ungranted;
- normal ADMIN cannot manage App Access;
- Identity Admin manages eligible USER/ADMIN/other Identity Admin Access except its own;
- Super Admin automatically enters all Workspace-enabled Apps;
- consumers receive direct `SUPER_ADMIN / ADMIN / USER` role; no Group-to-App role mapping.

## 8. CY Web multi-module exception

CY Web owns Customer/Order/Item/Outsourcing/WorkLog/future Module Access.

- Super Admin: all modules implicitly allowed;
- Identity Admin / Super Admin: manage eligible Employee module access;
- normal ADMIN: no module-access administration;
- ADMIN with a module has complete administration authority inside that module;
- USER finer permissions, if needed, remain CY Web-local.

## 9. Immediate authority effect

Disable, role change, App Access revoke, permanent credential changes, forced Email recovery and Super Admin transfer must invalidate/re-resolve affected sessions server-side. CY Web Module Access change takes effect on subsequent protected CY Web requests.

## 10. Super Admin-only operations

Only Super Admin controls Identity Admin grant/revoke, Super Admin transfer, Workspace Recovery/final-control recovery and Workspace security-core/OTP policy.

## 11. Legacy boundary

Old Identity Group memberships, Group Application grants and `USER_ADMIN` compatibility projection may remain in schema/history during cleanup but are not forward authority. New work must not extend them.

## 12. Consumer boundary

All consumers implement shared Identity through `CONSUMER_INTEGRATION_STANDARD.md` and the machine-readable compatibility window. CY Web is the first consumer/core account portal. CYAccountingWeb and CYInvoice adopt the same shared role/session/App Access contract in their own workstreams. Consumer-specific business/module permissions stay app-local, and CYInvoice-specific Device/local/offline behavior remains outside CYID.
### 1.0.2 runtime cleanup

Legacy Group endpoints/projections and compatibility role aliases are retired from runtime. Applied migrations and historical tables remain intact. Login parses/reads Employee authority once and selects permanent credential or purpose-scoped first-login exchange; Employee PATCH uses one guarded handler for pending and verified lifecycle. Email OTP and initial Email delivery share one global/Workspace budget reservation/settlement operation. No HTTP handler re-enters another handler.
