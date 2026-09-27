# SCMOS Carrier Collaboration + Billing — Phase 10 AI Assistance

27 September 2026 · implementation branch `codex/carrier-billing-phase10-ai`

## Delivered scope

Phase 10 adds five optional AI suggestions to the internal Billing Control review:

- document classification;
- invoice field extraction;
- POD field extraction;
- billing risk summary; and
- deterministic validation/rate variance explanation.

The existing Billing Case trigger, validation engine, rate/tax rules, document
requirements, review transitions, original-document control and Finance gate
remain authoritative.

## Safety boundary

AI output is written to `billing_ai_analyses` as `SUGGESTED`, with the model,
confidence, evidence reference/version, requester and timestamp. A reviewer may
mark the suggestion `CONFIRMED` or `REJECTED` (with MFA where required). That
decision updates only the suggestion row; it does not update invoice amounts,
rates, taxes, document receipt, additional-charge approval, review status,
Finance readiness or payment state.

Document analysis is restricted to an uploaded document belonging to the same
invoice, is limited to PDF/supported images and 12 MB, and records a SHA-256
content version. Carrier accounts cannot call the Phase 10 endpoints. Provider
runs use the shared concurrency/rate limiter and durable AI execution audit.

## Feature flags and secrets

Both switches are independently default-off:

- `AI__DocumentAiEnabled=true`
- `AI__BillingAiEnabled=true`

The global `AI__Enabled` switch still wins. Production reuses the existing
server-side `OpenAI__ApiKey` and `OpenAI__Model`; no key is accepted from or
returned to the browser. Local verification uses `AI__MockMode=true` and does
not need or call a provider key.

## API and database

- `GET /api/carrier-billing/invoices/{invoiceId}/ai`
- `POST /api/carrier-billing/invoices/{invoiceId}/ai/analyze`
- `POST /api/carrier-billing/invoices/{invoiceId}/ai/{analysisId}/decision`
- EF migration: `20260927141921_CarrierBillingPhase10Ai`

## Offline verification

- `dotnet build server/Scmos.Api/Scmos.Api.csproj --no-restore`
- `dotnet run --project server/Scmos.Api/Scmos.Api.csproj --no-build --no-launch-profile -- --check-carrier-billing-phase10`
- `node --disable-warning=MODULE_TYPELESS_PACKAGE_JSON --test tests/carrierBillingPhase10.test.mjs`
- `dotnet ef migrations has-pending-model-changes ...` reports no pending model changes
- normal frontend lint, typecheck, test and production build

No local test invokes OpenAI or reads the Production secret.
