# SCMOS Carrier Collaboration + Billing — Phase 4 Completion Report

## Outcome

Phase 4 introduces the first real carrier billing lifecycle: one Billing Case is opened automatically when a confirmed carrier completes delivery, the configured working-day SLA is snapshotted, the carrier can create and edit an Invoice Draft and upload multiple supporting documents, and internal users can monitor the resulting cases in Billing Control.

The previous generated Billing Aging data and duplicate billing table logic were removed. Both the internal screen and Carrier Portal now read the same API and persisted records.

## Existing Components Reused

- `OperationJob` remains the job and delivery source of truth.
- The confirmed `SupplierRequest` remains the carrier-assignment authority.
- `Supplier` and `CarrierTenantContext` remain the stable carrier identity and tenant boundary.
- `BusinessCalendarService`, `BillingSlaRule`, and `BusinessCalendarDay` remain the working-day/SLA configuration.
- `StoredDocument`, `DocumentService`, and the existing private Blob Storage service remain the document store.
- `AuditService` remains the mutation audit trail.

No parallel Job, Carrier, assignment, calendar, SLA, audit, or file-storage model was introduced.

## Billing Workflow

1. A confirmed carrier records Delivery Complete through the existing operational status flow.
2. The same transaction stages one Billing Case for the original job and carrier. A unique JobKey constraint is the database guarantee against duplicate cases.
3. The active `STANDARD` SLA rule is calculated against the configured business calendar and copied onto the case as an immutable snapshot.
4. If SLA configuration is missing, delivery is not blocked or guessed: the case records a visible configuration issue and a later idempotent replay can repair the missing snapshot.
5. The carrier opens the Billing tab and creates one Invoice Draft for an eligible Billing Case.
6. The carrier records invoice number, invoice date, currency, subtotal, and tax; total is calculated on the server.
7. The carrier can attach multiple invoice/supporting documents. Every document retains the original Job, Billing Case, and Invoice identities.
8. Internal users with rate visibility monitor case, carrier, SLA, invoice, amount, and documents through Billing Control.

Hyphenated, spaced, and underscored Delivery Complete event names are normalized to the same idempotent trigger. A replay uses the original Closed workflow-event time rather than allowing a later request to move the SLA date.

## API Additions

- `GET /api/carrier-billing/cases`
- `POST /api/carrier-billing/cases/{caseId}/draft`
- `PUT /api/carrier-billing/invoices/{invoiceId}`
- `POST /api/carrier-billing/invoices/{invoiceId}/documents`

Carrier mutations never accept a supplier ID. Supplier scope is derived from the authenticated carrier tenant on the server. Internal reads require the existing `ViewRates` capability; document uploads also require the existing `UploadDocuments` capability.

## Database Migration

Migration `CarrierBillingPhase4` is additive and creates:

- `billing_cases`, with unique JobKey and supplier/status and due/status indexes;
- `billing_invoices`, with non-negative amount enforcement and carrier-scoped invoice-number uniqueness;
- `billing_invoice_job_links`, an explicit Invoice-to-Billing-Case bridge with one current invoice per case;
- nullable Billing Case and Invoice foreign keys on the existing `documents` table.

No existing table or column is removed or renamed. The explicit bridge supports a later configured multi-job invoice without replacing current identities.

## UI Changes

- Billing Control now displays real Billing Eligible Jobs, waiting cases, drafts, overdue SLA count, invoice totals, and documents.
- The generated Billing Aging cards/table and their misleading demo warning were removed.
- Carrier Portal now includes a Billing tab for draft creation, editing, and repeated document upload.
- The internal billing table uses the shared scroll/zoom container, preserving usability on wide screens.

## Automated Verification

- One Billing Case per Job is enforced and checked.
- Duplicate Invoice Draft links per Billing Case are prevented and checked.
- Carrier eligibility and tenant isolation are checked.
- Day 0/Day 1, weekend, holiday, and special-working-day behavior remains covered by Phase 1 checks.
- SLA states and missing-configuration behavior are checked in Phase 4.
- Billing document ownership links are checked.
- Delivery Complete normalization and Phase 3 operational regression are checked.
- The Phase 4 rule/schema check is part of the API deployment workflow.

Final local results:

- Web tests: 646 passed, 0 failed.
- TypeScript typecheck: passed.
- Next.js production build: passed.
- API Release build with warnings treated as errors: passed.
- ESLint: 0 errors; 3 pre-existing warnings outside the Phase 4 files.
- Phase 1–4 carrier/billing rule checks: passed.
- EF Core pending model check: no model changes since the Phase 4 migration.

## Release Boundary

The Phase 2/3 Production release completed before this work. Phase 4 is maintained and released separately from `codex/carrier-billing-phase4`, so its migration and Web/API deployment are attributable to their own commit and workflow runs rather than being mixed into the earlier release.

## Deliberate Boundaries

- No default SLA rule is invented. An administrator must explicitly configure Day 0 or Day 1 and the allowed working-day target.
- Phase 4 stops at Invoice Draft and document collection. Submission, validation, rejection/resubmission, finance handoff, and paid states belong to later phases.
- The internal Billing Control is read-only in this phase; carrier-owned draft mutations stay in Carrier Portal.
