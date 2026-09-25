# SCMOS Carrier Billing — Phase 5

Phase 5 adds a deterministic, server-side validation gate to Carrier Submit. It deliberately stops at `VALIDATED` or `BLOCKED`; review, return, dispute, and approval remain Phase 6 responsibilities.

## Delivered

- Effective-dated Billing Requirement Rules with customer, carrier, service, shipment, charge, priority, blocking, and specific-requirement dimensions.
- Immutable rule-selection snapshots per invoice. A resubmission rechecks whether the snapshotted requirement is satisfied without silently adopting a newer rule.
- Effective-dated Tax Rules. No VAT percentage is hard-coded or inferred.
- Structured Additional Charges with requested and approved values, evidence reference, identities, dates, and status.
- Duplicate controls for carrier/invoice number, fully billed jobs, conflicting/risk claims, and same amount/date warning.
- Contract-cost resolution over the existing `RateLane`, `RatePrice`, and `FuelBand` data. Evidence retains Rate Price id, source file/promotion version, effective job date, and match context.
- Persisted validation runs and individual results using `PASS`, `WARNING`, `EXCEPTION`, and `BLOCKED`.
- Carrier portal actions for structured additional-charge requests, Submit Validation, and visible per-rule results.
- Administration APIs for Requirement and Tax configuration protected by `AdministerData` and the existing MFA policy.

## Validation order

1. Job eligibility
2. Carrier ownership
3. Duplicate detection
4. Document requirement snapshot/check
5. Structured additional-charge approval check
6. Contract Rate check
7. Tax check
8. Invoice total reconciliation
9. Persisted summary and status transition

Missing or ambiguous Rate/Tax configuration fails closed. Production receives no guessed default Requirement, Rate, or VAT rule.

## Verification

- `dotnet build server/Scmos.Api/Scmos.Api.csproj --no-restore`
- `dotnet run --project server/Scmos.Api/Scmos.Api.csproj --no-build -- --check-carrier-billing-phase5`
- `npm run typecheck`
- `npm run lint`
- `npm test` (646 tests)
- `npm run build`

The Phase 5 check covers every required scenario: missing document, rate match/mismatch/multiple/missing, approved/unapproved charge, tax match/mismatch, duplicate invoice, and job already billed.
