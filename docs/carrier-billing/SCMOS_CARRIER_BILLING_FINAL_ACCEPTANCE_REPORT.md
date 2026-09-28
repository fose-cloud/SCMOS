# SCMOS Carrier Collaboration + Billing — Final Acceptance & Hardening

28 September 2026 · implementation branch `codex/carrier-billing-final-acceptance`

## Scope

The Master Implementation Specification defines implementation Phases 1–11.
There is no Phase 12. This final work package applies sections 42–58: cross-phase
testing, end-to-end acceptance, auditability, tenant isolation, reliability,
migration safety, backward compatibility, observability and the implementation
quality gate.

## Implemented

- Added one deterministic final acceptance check that composes the same domain
  rules used by the Carrier Portal, Carrier API, Billing and Finance services.
- Covered the continuous path from Carrier acceptance through Delivery Complete,
  Billing eligibility/SLA, deterministic validation, Subcontract approval,
  Original receipt, Finance release, reconciliation, payment and close.
- Added direct known-ID isolation assertions for Carrier A versus Carrier B.
- Added EF model assertions proving the relational trace from the original SCMOS
  Job and Carrier assignment through Invoice, validation, review, Original
  receipt, Finance record, payment and reconciliation.
- Added reliability assertions for replay, outbox idempotency and bounded retry.
- Registered the acceptance check in the API startup check dispatcher and the
  mandatory GitHub Actions rule-check loop.

## Reused

`OperationJob`, `SupplierRequest`, `CarrierAssignment`, `CarrierOperations`,
`BusinessCalendar`, Billing validation/review/readiness rules, `CarrierTenantPolicy`,
the Phase 11 canonical Finance model/internal queue, EF model metadata and the
existing API workflow. No parallel Job, Carrier, Invoice, Finance or test runtime
was introduced.

## Created

- `CarrierBillingFinalAcceptanceCheck`
- `tests/carrierBillingAcceptance.test.mjs`
- this final acceptance report

## Database changes

None. The check inspects the compiled EF model and never connects to a database.
No migration is required.

## API and UI changes

None. Existing API contracts and UI behavior remain unchanged. The new CLI gate
is `--check-carrier-billing-acceptance`.

## Security

The acceptance gate checks Carrier identity from stable server-owned Supplier
identity, rejects another Carrier's known identifier, confirms the auditable
schema relationships and verifies that internal Finance/payment states remain
masked from Carrier projections.

## Production safety

The check is offline and deterministic. It does not read or write Production
data, invoke an external Finance system, use OpenAI, retrieve a secret or create
a database. It adds no runtime side effect and is safe to execute on every API
build before deployment.

## Tests

- .NET Release build with warnings as errors: passed, 0 warnings / 0 errors.
- All API offline rule checks, including Phases 1–11 and final acceptance: passed.
- Final composed domain/schema acceptance assertions: 17/17 passed.
- Final acceptance wiring tests: 4/4 passed.
- Full frontend regression suite: 683/683 passed.
- TypeScript typecheck and Next.js Production build: passed.
- EF pending-model check: no model changes since the last migration.
- ESLint: 0 errors, 3 pre-existing warnings.
- Diff whitespace check: passed.

## Known limitations

- The approved Finance target remains the SCMOS internal Finance queue; no
  external ERP contract has been supplied.
- Carrier payment visibility remains intentionally disabled until the business
  confirms that policy.
- This gate verifies the composed domain contract and EF topology offline. It
  does not mutate a real Production Job or Invoice merely to prove the flow.

## Completion decision

All implementation phases defined by the Master Specification are complete.
This final gate makes the cross-phase acceptance contract mandatory in CI so a
later change cannot silently break the lifecycle while individual phase tests
still pass.
