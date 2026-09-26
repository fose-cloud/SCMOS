# SCMOS Carrier Collaboration + Billing — Phase 9 Completion Report

## Current-state audit

SCMOS already had a versioned Carrier TMS API at `/api/carrier/v1`, API keys
hashed in `carrier_api_clients`, supplier binding on each key, fixed-window
rate limiting, correlation IDs, RFC 9457 errors, a 30-day persistent
idempotency ledger, TMS audit identities and assignment/truck/status/webhook
routes. Phases 4–8 already supplied the Billing Case, Invoice, document,
validation, review, original-document and dashboard domains. Creating another
API key table, carrier identity, invoice table, validation engine or document
store would have duplicated authoritative SCMOS components.

| Requirement | Existing component | Decision |
| --- | --- | --- |
| Receive jobs | Carrier API assignments | REUSE |
| Accept / reject | `CarrierService` + v1 writes | REUSE |
| Truck / driver | `CarrierService` + v1 truck route | REUSE |
| Update status | Carrier events and operational ladder | REUSE |
| Send POD | `DocumentService` + carrier ownership | EXTEND |
| Billing eligible jobs | `BillingCase` projection | EXTEND |
| Create / edit / submit Billing | `CarrierBillingService` | EXTEND |
| Validation and status | persisted Phase 5–7 projections | EXTEND |
| Authentication / isolation | `CarrierApiAuth` key-to-supplier binding | REUSE |
| Rate limiting / idempotency | v1 pipeline and `carrier_api_requests` | REUSE |

## Implemented

- Added carrier-owned POD upload.
- Added paged Billing Eligible jobs.
- Added idempotent draft creation and draft update.
- Added idempotent billing-document upload and Billing submit.
- Added Billing status/detail and latest validation reads.
- Added supplier-resolved service entry points shared with Carrier Portal.
- Added byte-level SHA-256 upload fingerprints for idempotent multipart calls.
- Marked API-originated financial audit rows with source `TMS`.

## Database changes

None. Phase 9 reuses `carrier_api_clients`, `carrier_api_requests`,
`billing_cases`, `billing_invoices`, links, validation rows, documents and
audit events. No migration or backfill is required.

## API changes

- `POST /api/carrier/v1/assignments/{jobKey}/pod`
- `GET /api/carrier/v1/billing/eligible`
- `POST /api/carrier/v1/billing/cases/{caseId}/draft`
- `PUT /api/carrier/v1/billing/invoices/{invoiceId}`
- `POST /api/carrier/v1/billing/invoices/{invoiceId}/documents`
- `POST /api/carrier/v1/billing/invoices/{invoiceId}/submit`
- `GET /api/carrier/v1/billing/invoices/{invoiceId}`
- `GET /api/carrier/v1/billing/invoices/{invoiceId}/validation`

## UI changes

None. API keys continue to be managed from the existing Carrier API screen.

## Security

- Supplier scope comes only from the authenticated key's server-side row.
- Request DTOs contain no supplier selector.
- Direct known IDs are filtered by supplier and answer `404` across tenants.
- Every write requires persistent idempotency; upload fingerprints include bytes.
- All endpoints inherit the existing per-key rate limiter and correlation ID.
- Document answers omit private Blob URLs and object keys.
- Existing storage size/path controls and deterministic billing validation apply.

## Tests

- Deterministic Phase 9 rule/security check added to API CI.
- Static contract regression verifies routes, v1 pipeline, idempotency, tenant
  scoping, private document fields and TMS audit source.
- Existing Carrier API, Billing Phase 1–8 and application regression suites
  remain authoritative and are run before release.

## Known limitations

- Carrier status events retain their existing governance: untrusted keys queue
  status changes for the job owner; only a key marked for auto-apply while the
  deployment switch is on writes immediately.
- Finance posting/payment remains Phase 11 and is not exposed to carriers.
- The API does not add multi-job invoices; that business choice remains
  configurable/TBD and the current one-case draft rule is preserved.

## Next phase

Phase 10 may add AI assistance behind feature flags, but deterministic billing,
document, validation and human approval remain authoritative.
