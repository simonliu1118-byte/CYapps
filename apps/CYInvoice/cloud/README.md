# CYInvoice Cloud Foundation

Cloudflare Worker + D1 backend foundation for CYInvoice.

This directory is isolated from the Windows client. AMEGO remains the authoritative source for invoice and allowance state, and AMEGO App Keys remain local to each Windows machine.

## Development resources

- Worker: `cyinvoice-cloud-dev`
- D1 binding: `DB`
- D1 database: `cyinvoice-cloud-dev-db`
- Environment: `development`

The D1 database UUID is an identifier, not an authentication secret. Cloudflare API tokens, account keys, AMEGO App Keys, passwords, device tokens, and production data must never be committed here.

## Commands

```bash
npm install
npm run check
npm run db:migrate:local
npm run dev
```

Remote deployment:

```bash
npm run deploy
```

`npm run deploy` applies pending remote D1 migrations before deploying the Worker.

## Initial endpoints

- `GET /health`
- `GET /v1/health`
- `GET /v1/health/db`
- `GET /v1/version`

The public health endpoints intentionally expose no workspace, device, invoice, allowance, credential, or row-count data.

Phase 1 batch 1 intentionally contains no employee authentication, device token issuance, invoice data, allowance data, or AMEGO proxying. Those are added in separate reviewed batches.
