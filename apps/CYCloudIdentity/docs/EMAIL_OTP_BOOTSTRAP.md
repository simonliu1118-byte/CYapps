# CYCloud Identity — Email OTP and Initial Bootstrap

> Status: current runtime contract. This public document contains binding/variable names and generic request shapes only. Actual values, Workspace/Employee/Application data, provider credentials and resource identifiers must remain outside Public source.

## Runtime configuration names

The deployed Identity Worker requires protected runtime configuration for Email OTP/bootstrap:

- `EMAIL_PROVIDER` — provider selector; current adapters support `brevo` and `resend`.
- `EMAIL_FROM` — verified sender mailbox supplied at deployment/runtime.
- `EMAIL_DAILY_BUDGET` — operator-selected daily hard cap below the approved provider allowance. Missing/invalid configuration fails closed.
- `BREVO_API_KEY` or `RESEND_API_KEY` — provider secret; never committed.
- `OTP_PEPPER` — HMAC secret, minimum 32 bytes; never committed/backed up with data.
- `BOOTSTRAP_SECRET` — one-time/temporary bootstrap authorization secret, minimum 32 bytes. Rotate/remove after initial bootstrap is accepted.
- `LOGIN_RATE_LIMITER` — Cloudflare Rate Limit binding used before login credential/D1 work.

Cloudflare resource names/IDs, rate-limit namespace IDs and all real values are Deployment Environment/runtime data, not Public source.

## Email OTP security behavior

Shared OTP behavior:

- 6 random decimal digits;
- HMAC-SHA256 digest at rest; raw OTP is never stored;
- digest binds challenge ID + purpose + scope + recipient + code;
- 10-minute expiry;
- 60-second resend cooldown for the same purpose/scope/recipient;
- maximum 5 incorrect verification attempts;
- maximum 5 successfully sent OTPs per recipient + purpose per rolling hour;
- one-time consumption;
- failed delivery never becomes a usable challenge;
- provider response bodies are not written to logs/errors;
- daily delivery reservation occurs before provider send, preventing concurrent requests from passing the configured daily budget.

Current OTP purposes are generic Identity lifecycle values: Workspace bootstrap/recovery, Employee email verification/password reset, highest-authority transfer authorization and Recovery Email change.

## First Workspace bootstrap

Bootstrap is not public registration. It is available only while the Identity database contains no Workspace and requires the protected `BOOTSTRAP_SECRET`.

### Start

`POST /v1/bootstrap/start`

Header:

```text
Authorization: Bearer <BOOTSTRAP_SECRET>
```

Generic body shape:

```json
{
  "workspace": {
    "code": "<runtime workspace code>",
    "displayName": "<runtime display name>"
  },
  "employee": {
    "employeeNo": "0001",
    "displayName": "<first employee display name>",
    "email": "<verified mailbox candidate>",
    "password": "<new password>"
  },
  "applications": [
    {
      "applicationId": "<runtime application id>",
      "displayName": "<runtime application display name>"
    }
  ]
}
```

The server generates immutable Workspace/Employee IDs, hashes the password immediately, stages only the verifier plus normalized bootstrap metadata, then sends `workspace_bootstrap` OTP to the proposed highest-authority email.

A successful start returns `202` with `bootstrapId`, `challengeId`, expiry and resend-after timestamp. It does not return the OTP or password verifier.

### Confirm

`POST /v1/bootstrap/confirm`

Header uses the same protected bootstrap bearer secret.

Generic body:

```json
{
  "bootstrapId": "<id returned by start>",
  "challengeId": "<id returned by start>",
  "code": "123456"
}
```

On successful confirmation, one D1 batch commits:

- Workspace;
- first Employee with verified Email;
- credential verifier;
- runtime-supplied Application registrations;
- Workspace Application enablement;
- Workspace highest-authority pointer;
- Workspace Recovery Email = verified highest-authority Email;
- consumed bootstrap/OTP state;
- non-secret audit event.

The transaction must not expose the plaintext password or OTP in D1/Audit/logs.

## Free-tier protection

`EMAIL_DAILY_BUDGET` is intentionally deployment-configurable rather than hard-coded to a provider marketing limit. The operator chooses a lower internal cap and can adjust it only through controlled runtime configuration. The service fails closed when the budget is exhausted and does not automatically select a paid route or alternate provider.

No uncontrolled email retry loop, high-frequency Cron or session heartbeat is part of this design.
