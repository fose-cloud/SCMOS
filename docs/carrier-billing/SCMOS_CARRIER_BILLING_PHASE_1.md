# SCMOS Carrier Collaboration + Billing — Phase 1

25 September 2026 · implementation branch `codex/carrier-billing-phase1`

## Implemented

- Central carrier tenant resolution from the authenticated subcontractor's active `StaffMember.SupplierId`.
- Fail-closed supplier membership and carrier-name/alias ownership rules.
- Server-side tenant enforcement for generic document list, upload, expiry update, and content routes.
- Carrier document listing is scoped in SQL before the 500-row bound and includes owned supplier, job, and driver documents.
- Effective-dated billing SLA configuration for an explicit Day 0 or Day 1 start and a 3- or 4-working-day target.
- Business calendar exceptions for working days, weekends, public holidays, company holidays, and special working days.
- Atomic audit events for calendar and SLA configuration changes.

## Reused

- `OperationJob` remains the transportation source of truth.
- `Supplier`, `SupplierAlias`, `StaffMember`, and `Driver` remain the carrier and membership masters.
- Existing roles, capabilities, MFA guard, API error conventions, `DocumentService`, blob storage, and `AuditService` are extended rather than replaced.
- Existing EF Core migration and GitHub Actions API pipelines are retained.

## Created

- `CarrierTenantContext`, `CarrierTenantPolicy`, and `CarrierDocumentAccess`.
- `BusinessCalendar`, `BusinessCalendarService`, and SLA validation rules.
- `BusinessCalendarDay` and `BillingSlaRule` entities.
- Phase 1 configuration endpoints and no-database foundation checks.

## Database changes

Migration `CarrierBillingFoundation` adds only:

- `business_calendar_days`, keyed by date with a calendar-kind check constraint.
- `billing_sla_rules`, with effective dates, active state, Day 0/Day 1 and 3/4-day check constraints, plus unique and lookup indexes.

No existing table or column is removed or renamed. No default SLA rule is seeded because the Day 0/Day 1 choice remains a confirmed TBD.

## API changes

- `GET /api/carrier-billing/foundation/calendar?from=yyyy-MM-dd&to=yyyy-MM-dd`
- `PUT /api/carrier-billing/foundation/calendar/{date}`
- `DELETE /api/carrier-billing/foundation/calendar/{date}`
- `GET /api/carrier-billing/foundation/sla`
- `PUT /api/carrier-billing/foundation/sla/{code}/{effectiveFrom}`

Configuration writes require `AdministerData` and the existing second-factor policy. Reads require an authenticated account. Existing `/api/documents` contracts are unchanged; carrier responses and direct-ID operations are now tenant-scoped.

## UI changes

The existing Administration screen now contains a small Carrier Billing Foundation section that:

- lists and maintains business-calendar exceptions by year;
- lists and maintains effective-dated SLA rules;
- forces the administrator to choose Day 0 or Day 1 explicitly;
- is read-only for accounts without `AdministerData`.

The full Billing UI remains deferred as required by Phase 1.

## Security and audit

- Supplier identity comes only from server-owned staff membership, never from a request value.
- Missing, inactive, non-carrier, or mismatched membership returns no tenant data.
- Known IDs for another supplier/job are refused on upload, metadata edit, and content access.
- Internal users keep their existing capability behavior.
- Calendar and SLA mutations stage their audit row in the same `SaveChanges` transaction as the business row.

## Verification

- API Release build with warnings as errors.
- All API rule checks, including `carrier-api` and `carrier-billing-foundation`.
- 624 web tests, including the Phase 1 Admin/API wiring and tenant document query order.
- TypeScript typecheck.
- ESLint for the Phase 1 UI and its Administration integration.
- Next.js production build.
- 24 quotation checks.
- 1,017 AI foundation/Operations/audit checks.
- CAR/PAR incident import checks.

## Known limitations

- No Billing Case, invoice, validation, original-document, finance, dashboard, or billing UI is introduced in Phase 1.
- An administrator must create an effective SLA rule before calculation succeeds; missing or overlapping configuration fails closed.
- Other specification items marked configurable (for example maximum file size, acceptance SLA, reminder timing, and finance mapping) remain unset until their owning phase has a real consumer.
- No production migration or deployment is part of this implementation branch.

## Next phase

Phase 2 should extend the existing `SupplierRequest` and carrier portal flow with durable assignment history, pending acceptance, accept/reject/reassign rules, one-active-assignment integrity, idempotency, and superseded-assignment protection. It must continue referencing the existing `OperationJob` identity.
