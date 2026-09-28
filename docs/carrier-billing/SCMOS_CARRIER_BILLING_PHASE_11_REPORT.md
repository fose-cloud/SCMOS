# SCMOS Carrier Collaboration + Billing — Phase 11 Finance Integration

28 September 2026 · implementation branch `codex/carrier-billing-phase11-finance`

## Delivered scope

Phase 11 extends the existing Billing domain from `READY_FOR_FINANCE` through a
safe Finance integration boundary. It adds:

- a versioned canonical Finance invoice model;
- a deterministic release gate that rechecks Online Approval, physical
  Original receipt and blocking validation exceptions at send time;
- a Finance adapter interface, an unconfigured production adapter and a
  deterministic offline mock adapter;
- a transactional outbox committed with the Finance record and Billing status;
- idempotent, leased delivery with bounded retry and visible dead-letter state;
- Finance response, rejection, payment and close reconciliation;
- internal Billing Control actions and status visibility; and
- audit records for release, retry, adapter response and reconciliation.

The same SCMOS Job, Billing Case, Invoice, validation, approval, original
document, supplier and audit records remain authoritative. No parallel Job,
Carrier, Invoice or Finance master was introduced.

## Finance boundary and safety

The actual LESCHACO Finance/Accounting target and field mapping remain `TBD`.
This phase therefore does not invent a vendor API, database connection,
credential, endpoint or vendor DTO. `IFinanceAdapter` is the extension point for
the confirmed target later.

The canonical payload contains the reviewed SCMOS identity and evidence:

- carrier code/name;
- invoice number/date/currency;
- linked Billing Case and Job identities;
- base cost and approved additional charges;
- tax and grand total;
- Online Approval timestamp/reference; and
- Original Received timestamp.

The payload JSON and SHA-256 hash are persisted before dispatch. The dispatcher
refuses a changed payload or an adapter that no longer matches the adapter under
which the item was queued. Every retry uses the original idempotency key.

## Authorization and carrier isolation

Release, manual retry and reconciliation require all of:

- an authenticated internal account;
- the existing `ReviewBilling` capability and MFA policy; and
- a role explicitly listed in Finance integration configuration.

There is no guessed default release role. Carrier accounts cannot release,
retry, reconcile or list Finance records. Because carrier payment visibility is
still a business decision, Carrier Portal and Carrier API keep showing the last
public state, `READY_FOR_FINANCE`; internal processing, rejection, payment,
references and close states are not returned. Paged status filtering applies
the same masking and cannot be used to infer a private transition.

## State and reliability

Finance record states:

`QUEUED → PROCESSING → SUBMITTED/ACCEPTED → PAID → CLOSED`

Alternative states are `RETRYING`, `FAILED` and `REJECTED`. Invalid transition
skips are refused. Retry waits 1, 5, 15, 60 and 240 minutes, then ends in a
visible dead letter that requires an authorized manual retry.

The invoice, Billing Case, Finance record and outbox event are created/updated
inside one SQL transaction. Row-version concurrency protects both Finance and
outbox records. A dispatcher crash after an external call retries with the same
idempotency key rather than creating a new logical Finance transaction.

## API and UI

- `POST /api/carrier-billing/finance/invoices/{invoiceId}/send`
- `GET /api/carrier-billing/finance/records`
- `POST /api/carrier-billing/finance/records/{recordId}/retry`
- `POST /api/carrier-billing/finance/records/{recordId}/reconcile`

Billing Control displays configuration/readiness, Finance status, attempts,
responses and reconciliation data, and exposes send/retry/reconcile actions
only when the server reports that the signed-in account may release.

## Database and migration

EF migration `20260928015421_CarrierBillingPhase11Finance` adds:

- `billing_finance_records` with one record per invoice, unique idempotency key,
  canonical snapshot/hash, response/payment references and row version;
- `integration_outbox` with unique idempotency key, lease, attempt schedule,
  correlation ID, error state and row version; and
- the internal Billing Case states `FINANCE_PROCESSING`, `FINANCE_REJECTED`,
  `PAID` and `CLOSED`.

The migration is additive apart from replacing the existing Billing Case status
check constraint with its strict superset. It does not rewrite or delete existing
Billing, Job, supplier, document or audit data. EF reports no pending model
changes.

## Configuration

The feature is fail-closed by default:

```text
CarrierBilling__FinanceIntegration__Enabled=false
CarrierBilling__FinanceIntegration__Adapter=None
CarrierBilling__FinanceIntegration__ReleaseRoles__0=<explicit approved role>
```

`Adapter=Mock` is accepted only in Development or the Test environment. It is
never selected in Production. Until a real adapter and approved roles are
configured, Production can safely carry the schema and code without sending a
Finance transaction.

## Verification

- API Release build with warnings as errors: passed;
- all API offline rule checks, including Phases 9–11: passed;
- Phase 11 domain/security/retry/mock checks: passed;
- EF pending-model check: passed;
- frontend typecheck and production build: passed;
- frontend test suite: 671/671 passed;
- focused Phase 11 tests: 6/6 passed;
- AI foundation/Operations/audit checks: 1,018/1,018 passed, offline;
- calculated quotation checks: passed; and
- lint: 0 errors, 3 pre-existing warnings.

No verification call used Production data, a live Finance system or OpenAI.

## Remaining production decision

Before enabling this feature, LESCHACO must confirm the target Finance platform,
authentication/secret storage, vendor mapping, accepted response/callback
contract, approved release roles and which payment details carriers may see.
Those decisions belong in the real adapter and controlled configuration; they
are intentionally not guessed in Phase 11.
