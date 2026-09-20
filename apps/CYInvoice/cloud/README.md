# CYInvoice Cloud Foundation

Cloudflare Worker + D1 backend foundation for CYInvoice.

This directory is isolated from the Windows client. AMEGO remains the authoritative source for invoice and allowance state, and AMEGO App Keys remain local to each Windows machine.

## Development resources

- Worker: `cyinvoice-cloud-dev`
- D1 binding: `DB`
- D1 database: `cyinvoice-cloud-dev-db`
- Environment: `development`
- Workers Builds production branch: `cyinvoice/cloud-foundation-d1`
- Workers Builds root directory: `apps/CYInvoice/cloud`

The D1 database UUID is an identifier, not an authentication secret. Cloudflare API tokens, account keys, AMEGO App Keys, passwords, device tokens, and production data must never be committed here.

## Commands

```bash
npm install
npm run check
npm run db:migrate:local
npm run dev
```

Routine remote deployment:

```bash
npm run deploy
```

Remote schema changes are intentionally separate from routine Worker deployment:

```bash
npm run db:migrate:remote
```

A maintainer with explicit D1 write permission must apply pending migrations before deploying code that depends on them. This avoids granting ordinary Worker Builds more database privileges than necessary. For a controlled one-off deployment with a D1-capable credential, `npm run deploy:with-migrations` is available.

## Initial endpoints

- `GET /health`
- `GET /v1/health`
- `GET /v1/health/db`
- `GET /v1/version`

The public health endpoints intentionally expose no workspace, device, invoice, allowance, credential, or row-count data.

Phase 1 batch 1 intentionally contains no employee authentication, device token issuance, invoice data, allowance data, or AMEGO proxying. Those are added in separate reviewed batches.
