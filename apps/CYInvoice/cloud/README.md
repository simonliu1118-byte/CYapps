# CYInvoice Cloud Foundation

Cloudflare Worker + D1 backend foundation for CYInvoice.

This directory is isolated from the Windows client. AMEGO remains the authoritative source for invoice and allowance state, and AMEGO App Keys remain local to each Windows machine.

## Development resources

- Worker: `cyinvoice-cloud-dev`
- D1 binding: `DB`
- D1 database: `cyinvoice-cloud-dev-db`
- Environment: `development`

The D1 database UUID is an identifier, not an authentication secret. Cloudflare API tokens, account keys, AMEGO App Keys, passwords, device tokens, bootstrap keys, and production data must never be committed here.

## Commands

```bash
npm install
npm run check
npm run db:migrate:local
npm run dev
```

The development Worker currently uses this deploy command in Cloudflare Workers Builds:

```bash
npm run deploy:with-migrations
```

That command applies only pending Wrangler D1 migrations and then deploys the Worker. This is intentional for `cyinvoice-cloud-dev` while the schema is still changing frequently, so routine development does not require repeatedly editing Cloudflare build settings.

The underlying commands remain available separately:

```bash
npm run db:migrate:remote
npm run deploy
```

When a production Worker/database is introduced, production schema migration must be separated from routine Worker deployment and handled as an explicit controlled operation.

## Cloud bootstrap secret

The first workspace bootstrap endpoint is disabled unless the Worker has a secret named `BOOTSTRAP_KEY`.

Set it through Cloudflare without committing or sharing the value:

```bash
npx wrangler secret put BOOTSTRAP_KEY
```

The bootstrap key is only for creating the first workspace and first trusted device. After a workspace exists, the endpoint refuses a second initialization even if the key is correct. The Windows client never persists the bootstrap key; it only stores the returned device token using the existing protected settings mechanism.

## API endpoints

Public health/version endpoints:

- `GET /health`
- `GET /v1/health`
- `GET /v1/health/db`
- `GET /v1/version`

Foundation device endpoints:

- `POST /v1/bootstrap`
  - requires `X-Bootstrap-Key`
  - creates the first workspace and first trusted device
  - returns the first device token once
- `GET /v1/device`
  - requires `Authorization: Bearer <device-token>`
- `POST /v1/device-pairings`
  - requires an authenticated active device
  - returns a one-time pairing code valid for 10 minutes
- `POST /v1/device-pairings/claim`
  - claims a valid pairing code for a new device
  - returns the new device token once

Device tokens and pairing codes are stored only as SHA-256 hashes in D1. Plaintext device tokens are returned only at creation/claim time and must be protected by the Windows client before persistence.

The public health endpoints intentionally expose no workspace, device, invoice, allowance, credential, or row-count data.

Phase 1 still contains no employee authentication, invoice data, allowance data, AMEGO proxying, or cross-device work items. Those are added in separate reviewed batches.

## Current state

Schema version `2` (`0002_device_pairing.sql`) is active in the development D1 database. The Windows client now contains a guided cloud setup flow for health checking, first-device bootstrap, pairing-code creation, and second-device claim while preserving local-only mode as the default until a device is successfully registered.
