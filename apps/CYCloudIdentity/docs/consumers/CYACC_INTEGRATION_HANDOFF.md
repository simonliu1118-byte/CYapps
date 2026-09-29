# CYAccountingWeb → CYCloud Identity Integration Handoff

> **Purpose:** CYAccountingWeb-specific migration handoff only.
>
> **Shared standard:** `../CONSUMER_INTEGRATION_STANDARD.md` — Consumer Contract `1.0.1` (minimum compatible `1.0.0`).
>
> **Provider source baseline:** CYID 0.3.4 source/docs; development runtime remains CYID 0.3.0 until a runtime-changing provider deployment.
>
> If this handoff conflicts with the shared standard or CYID canonical contracts, the canonical documents win.

This file intentionally does **not** restate shared Role, Session, App Access, first-login, password-recovery or error-normalization rules. Those live in the CYID Consumer Integration Standard and must be read first.

## 1. Current CYACC baseline

This migration map was re-checked against CYAccountingWeb V0.21.5 Build 10 on 2026-09-30. Builds 9–10 changed Mobile presentation/tools and did not change the legacy Identity implementation described below.

CYACC already has the useful high-level Cloudflare shape:

- Worker;
- D1;
- Static Assets;
- private `IDENTITY` Service Binding.

However, its Identity implementation is still the legacy CYInvoice Cloud Web Auth contract.

Current `src/app.js` still:

- calls `/v1/web-auth/login`;
- sends literal `application: "CYAccountingWeb"`;
- expects an `employee` response rather than normalized CYID `principal`;
- mints a second CYACC-local random session token;
- hashes/stores that token in CYACC D1 `web_sessions`;
- resolves `/api/auth/me` only from CYACC D1;
- logs out by deleting the local `web_sessions` row;
- calls legacy `/v1/web-auth/password-reset/challenge` and `.../confirm`;
- accepts login passwords up to 200 characters;
- constrains reset passwords to 8–200 ASCII letters/digits.

The CYID migration replaces only these Identity-specific behaviors. Accounting data/model/backups/UI remain CYACC-owned.

## 2. Required migration delta

Replace the legacy login path:

~~~text
CYACC /api/auth/login
  -> IDENTITY /v1/web-auth/login
  -> employee response
  -> mint CYACC local token
  -> INSERT web_sessions
~~~

with the current CYID login/session flow defined by the shared standard:

~~~text
CYACC /api/auth/login
  -> CYID normal permanent-password login
  -> normalized CYID principal + provider Session
  -> app-specific protected browser cookie
  -> no new CYACC-local Identity Session
~~~

The literal `APPLICATION = "CYAccountingWeb"` is not the forward authority key. Deployment supplies the registered CYID Application ID and Workspace ID.

## 3. Local `web_sessions` retirement

Migration `0002_web_sessions.sql` is historical/applied source and must not be rewritten.

Migration order:

1. implement provider login/resolve/logout;
2. prove development login/session behavior;
3. add a new forward CYACC migration that retires/drops `web_sessions`;
4. remove local session mint/hash helpers after the provider path is accepted.

Do not keep `web_sessions` as a fallback Identity authority. Provider outage fails closed.

## 4. Protected API boundary

Replace current `sessionFromRequest(request, env.DB)` Identity authority with a provider resolve helper.

Existing accounting handlers may temporarily receive a compatibility session object, but any coarse `role` field must be a direct alias of the normalized CYID principal; no second CYACC role model may be created.

Accounting/domain permissions remain CYACC-local and server-enforced.

## 5. Password-recovery migration

Replace:

~~~text
/v1/web-auth/password-reset/challenge
/v1/web-auth/password-reset/confirm
~~~

with the CYID password-recovery endpoints defined by the shared standard.

CYACC must also adopt the shared permanent-password input boundary.

The legacy CYACC UI currently expects `maskedEmail`; CYID recovery-start is intentionally non-enumerating and does not promise that field. Replace the copy with a neutral message such as:

~~~text
若帳號符合條件，驗證碼已寄至登記且已驗證的 Email。
~~~

Do not reintroduce account enumeration merely to preserve the old UI.

## 6. Tablet / Safari compatibility exception

CYAccountingWeb Build 8 intentionally changed first-party cookie behavior to `SameSite=Lax` plus `Expires` and a navigation-safe post-login flow because immediate tablet Safari session handoff had failed in prior builds.

This is an app-specific browser compatibility constraint.

During CYID migration:

- keep raw provider token HttpOnly + Secure;
- keep token out of JS/localStorage/URL/logs;
- preserve the known working Lax/navigation-safe behavior until real-device acceptance proves a stricter policy safe;
- do not copy CY Web's cookie presentation blindly.

This exception changes browser transport presentation only; CYID remains the Session authority.

## 7. Provider Application registration prerequisite

Before the **first end-to-end CYACC login/App Access acceptance**, the CYID workstream must prove that:

1. a registered CYACC Application exists in CYID `applications`;
2. that Application is enabled for the target Workspace in `workspace_applications`;
3. the development CYACC deployment receives that registered Application ID and Workspace ID;
4. eligible Employee App Access is then managed through CYID authority.

The current CYID schema/runtime supports generic Application registry and direct App Access, but post-bootstrap Application registration is a **provider operational prerequisite** and must not be emulated by CYAccountingWeb writing CYID D1 directly.

If the development Workspace does not already contain CYACC, complete the governed provider-side registration/provisioning step in the CYID workstream before attempting real login. Do not hard-code actual Workspace/Application IDs into Public source.

This prerequisite does **not** block CYACC from implementing its provider adapter, login/resolve/logout/recovery code and deployment placeholders in development.

## 8. Deployment-template delta

CYACC already declares an `IDENTITY` Service Binding.

The integration work must add/inject the runtime contract values required by the shared standard, including the registered CYACC Application ID and Workspace ID.

The binding target must be changed from the legacy CYInvoice Cloud identity provider to CYCloud Identity only through the governed **development** deployment first.

Do not commit resolved provider service names, Workspace IDs or registered Application IDs to Public source.

## 9. Scope that stays untouched

Identity migration must not alter as a side effect:

- authoritative accounting D1;
- transaction/account/category/opening-balance/lock semantics;
- SQLite import;
- Excel import/export;
- R2/GCS backup topology;
- production custom domain `acc.chihyuancm.com`;
- Desktop/Tablet/Mobile bookkeeping presentation.

Identity cutover and accounting behavior are separate acceptance dimensions.

## 10. CYACC implementation sequence

1. read current `../CONSUMER_INTEGRATION_STANDARD.md` and consumer contract changelog;
2. confirm CYACC `CYID_CONSUMER_VERSION=1.0.1` is inside the current provider support window;
3. add/adapt a provider-neutral Identity adapter inside CYACC;
4. render development runtime Application/Workspace IDs from deployment environment;
5. replace legacy login with CYID permanent-password login;
6. replace local `/api/auth/me` authority with CYID Session resolve;
7. replace local-only logout with provider logout + cookie cleanup;
8. replace legacy password reset with CYID recovery;
9. before first end-to-end login, prove/provision CYACC Application registry + Workspace enablement on the CYID provider side;
10. prove the provider path in development;
11. retire `web_sessions` via forward migration;
12. retain CYACC business authorization locally;
13. run CYACC Desktop/Tablet/Mobile acceptance;
14. production cutover only after explicit approval.

## 11. CYACC-specific acceptance

In addition to the shared acceptance matrix, CYACC must prove:

- current Tablet Safari login handoff still works after provider-session migration;
- reload/back/forward navigation does not lose a valid provider Session;
- accounting requests fail closed when CYID cannot resolve authority;
- legacy local `web_sessions` no longer authorizes after cutover;
- accounting CRUD/import/export/backup behavior is unchanged by Identity migration.

## 12. Current known acceptance gap

The controlled real new-Employee Email/browser lifecycle is temporarily deferred by current test conditions.

That gap does not reopen the contract. CYACC development integration may continue, but final production acceptance still requires the shared lifecycle evidence defined by CYID.

## 13. Canonical references

Read in this order:

1. `../../PROJECT_RULES.md`
2. `../../../CYAccountingWeb/CYID_CONSUMER_VERSION`
3. `../CONSUMER_INTEGRATION_STANDARD.md`
4. `../CONSUMER_CONTRACT_CHANGELOG.md`
5. `../ROLE_AND_ACCESS_MODEL.md`
6. `../AUTH_CONTRACT.md`
7. `../ARCHITECTURE.md`
8. this handoff.

This handoff is intentionally app-specific and must remain smaller than the shared standard.
