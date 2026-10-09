# CYAccountingWeb → CYCloud Identity Integration Handoff

> **Purpose:** CYAccountingWeb-specific migration handoff only.
>
> **Shared standard:** `../CONSUMER_INTEGRATION_STANDARD.md`
>
> **Provider source baseline:** CYID 0.3.x.
>
> If this handoff conflicts with the shared standard or CYID canonical contracts, the canonical documents win.

This file intentionally does **not** restate shared Role, Session, App Access, first-login, password-recovery or error-normalization rules. Those live in the CYID Consumer Integration Standard and must be read first.

## 1. Current CYACC baseline

**2026-10-10 follow-up (GitHub `main` source, not Cloudflare runtime):** This section preserves the **historical pre-cutover baseline** checked against CYAccountingWeb V0.21.5 Build 8 on 2026-09-30. Do not treat the legacy `/v1/web-auth`, `web_sessions` and old login paths described below as today's active implementation.

The currently reviewed CYACC `src/identity-adapter.js` uses `env.IDENTITY.fetch()` and CYID canonical `/v1/identity/login`, `/v1/identity/session/resolve`, `/v1/identity/logout`, with provider app-scoped Bearer token/`x-identity-application` and browser HttpOnly/Secure `SameSite=Lax` cookie. `src/app.js` enforces protected business API resolution and USER read-only policy. Consumer version declaration on main is `1.0.1`, supported by CYID current `1.0.2`/minimum `1.0.0`. No CYACC-local Session is minted or read as an authority in the reviewed path.

App-specific review gaps (not evidence of an exploit or a second authority): `normalizePrincipal` coercion of CYID boolean/version fields is weaker than CY Web's strict invariants; CYACC performs an extra fresh Session Resolve during login; `identityFetch` timeout mainly covers `fetch()` response rather than response-body parsing. For logout, CYACC's Worker reports HTTP 200/`ok: true` with `providerRevoked: false` if provider revoke fails, and `public/auth.js` navigates to `/login` even if the logout request itself fails. Fix at CYACC's canonical adapter/app/browser lifecycle, with regression tests; do not add local Session authority, compatibility fallback or another logout subsystem.

Current production Service Binding targets, real-Tablet acceptance and usage metrics were **not read back** during this review. This document does not claim production deployment/change. Details and pending next-consumer work are in `../../TODO.md`.

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

1. implement provider login/resolve/logout and remove active local session mint/hash/resolve authority;
2. prove the provider path in an isolated development deployment;
3. cut over production only after explicit approval, while keeping the physical legacy `web_sessions` table temporarily available for rollback safety;
4. after stable production acceptance, add a separate forward CYACC migration that retires/drops the physical `web_sessions` table.

The retained table must never remain an authorization fallback after source cutover. This staging exists only because CYACC applies D1 migrations before Worker deployment; dropping the table in the same first-cutover deployment could break the previous Worker if the new Worker deployment failed. Provider outage still fails closed.

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

## 7. Deployment-template delta

CYACC already declares an `IDENTITY` Service Binding.

The integration work must add/inject the runtime contract values required by the shared standard, including the registered CYACC Application ID and Workspace ID.

The binding target must be changed from the legacy CYInvoice Cloud identity provider to CYCloud Identity only through the governed **development** deployment first.

Do not commit resolved provider service names, Workspace IDs or registered Application IDs to Public source.

## 8. Scope that stays untouched

Identity migration must not alter as a side effect:

- authoritative accounting D1;
- transaction/account/category/opening-balance/lock semantics;
- SQLite import;
- Excel import/export;
- R2/GCS backup topology;
- production custom domain `acc.chihyuancm.com`;
- Desktop/Tablet/Mobile bookkeeping presentation.

Identity cutover and accounting behavior are separate acceptance dimensions.

## 9. CYACC implementation sequence

1. read current `../CONSUMER_INTEGRATION_STANDARD.md` and consumer contract changelog;
2. when CYID integration becomes active, add CYACC `CYID_CONSUMER_VERSION` for the contract revision being implemented;
3. add/adapt a provider-neutral Identity adapter inside CYACC;
4. render development runtime Application/Workspace IDs from deployment environment;
5. replace legacy login with CYID permanent-password login;
6. replace local `/api/auth/me` authority with CYID Session resolve;
7. replace local-only logout with provider logout + cookie cleanup;
8. replace legacy password reset with CYID recovery;
9. prove the provider path in isolated development;
10. retain CYACC business authorization locally;
11. run CYACC Desktop/Tablet/Mobile acceptance;
12. production authority cutover only after explicit approval, without dropping the legacy table in the same deployment;
13. after stable production acceptance, retire the physical `web_sessions` table via a separate forward migration.

## 10. CYACC-specific acceptance

In addition to the shared acceptance matrix, CYACC must prove:

- current Tablet Safari login handoff still works after provider-session migration;
- reload/back/forward navigation does not lose a valid provider Session;
- accounting requests fail closed when CYID cannot resolve authority;
- legacy local `web_sessions` no longer authorizes after cutover;
- accounting CRUD/import/export/backup behavior is unchanged by Identity migration.

## 11. Development acceptance state

CYAccountingWeb **V0.21.6 Build 1 / Draft PR #243** completed isolated provider-side live acceptance on CYID development run **#96**.

Proven live against the development provider and isolated CYACC D1/Worker Preview:

- permanent-password consumer login and Session resolve;
- direct USER projection with CYACC App Access;
- CYACC server-side USER read-only enforcement;
- valid Excel export for USER;
- USER → ADMIN Role update through CYID admin authority revokes the old Session;
- ADMIN re-login gains CYACC write authority and completes an isolated create/delete transaction roundtrip;
- direct CYACC App Access revoke revokes the active Session and denies fresh login;
- App Access + USER Role restore succeeds;
- provider logout invalidates the consumer Session.

Synthetic acceptance identities are development-only, use runtime-random masked credentials, and are restored to USER/App-Access-enabled baseline with test Sessions revoked during cleanup.

Still separate from this acceptance: Tablet real-device behavior, real Password Recovery Email/browser delivery, the broader CYID new-Employee Email lifecycle matrix, and production cutover approval.

## 12. Canonical references

Read in this order:

1. `../../PROJECT_RULES.md`
2. `../CONSUMER_INTEGRATION_STANDARD.md`
3. `../CONSUMER_CONTRACT_CHANGELOG.md`
4. `../ROLE_AND_ACCESS_MODEL.md`
5. `../AUTH_CONTRACT.md`
6. `../ARCHITECTURE.md`
7. this handoff.

This handoff is intentionally app-specific and must remain smaller than the shared standard.
