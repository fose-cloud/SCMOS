# SCMOS Carrier Collaboration + Billing — Phase 9 Completion Report

Carrier External API. Started 26 Sep 2026 (commit `fb696a1`: POD and online
billing through the v1 door); completed 27 Sep 2026 (the portal's operations
through the same door, the remaining billing routes, upload controls, and two
transaction defects found by the end-to-end run).

## Current-state audit

SCMOS already had a versioned Carrier TMS API at `/api/carrier/v1`, API keys
hashed in `carrier_api_clients`, supplier binding on each key, fixed-window
rate limiting, correlation IDs, RFC 9457 errors, a 30-day persistent
idempotency ledger, TMS audit identities and assignment/truck/status/webhook
routes. Phases 2–8 already supplied the assignment, resource, operational
status, Billing Case, Invoice, document, validation, review, original-document
and dashboard domains. Creating another API key table, carrier identity,
status model, invoice table, validation engine or document store would have
duplicated authoritative SCMOS components.

| Minimum flow (spec §41 Phase 9) | Existing component | Decision |
| --- | --- | --- |
| Receive jobs | `GET /assignments` over `CarrierService.ReadForAsync` | REUSE |
| Accept / reject | `CarrierService.AcceptAssignmentAsync` / `DeclineAssignmentAsync` | EXTEND (reason code, truck optional) |
| Truck / driver | portal `AssignResourcesAsync`; v1 `PUT /truck` | EXTEND (one operation, two doors) |
| Update status | portal `AdvanceAsync`; v1 `POST /events` | EXTEND (one operation, two doors) |
| Send POD | `DocumentService.AddToJobAsync` + carrier ownership | EXTEND |
| Billing eligible jobs | `BillingCase` projection | EXTEND (paged in the database) |
| Create / edit / submit billing | `CarrierBillingService`, `BillingValidationService` | EXTEND |
| Validation and status | persisted Phase 5–7 projections | EXTEND |
| Original package | `OriginalDocumentService.SaveCarrierPackageAsync` | EXTEND |
| Authentication / isolation | `CarrierApiAuth` key-to-supplier binding | REUSE |
| Rate limiting / idempotency / versioning | v1 group, `carrier_api_requests`, `/v1` | REUSE |

## Implemented

**26 Sep (Codex, `fb696a1`)** — carrier-owned POD upload; paged Billing Eligible;
idempotent draft creation and update; idempotent billing-document upload and
submit; billing detail and latest validation reads; supplier-resolved service
entry points shared with the portal; byte-level SHA-256 upload fingerprints;
TMS audit source on API-originated billing rows.

**27 Sep (completion)**

- **The portal's operations through the API (BR-016).** `CarrierService`
  now has `AssignResourcesForAsync` and `AdvanceForAsync`; the portal's
  `AssignResourcesAsync` / `AdvanceAsync` resolve the person's supplier and
  call them, and the API calls them with the key's supplier. One ladder, one
  fleet-ownership check, one history row, one audit, and the same Billing
  Case at Delivery Complete. Only the channel differs (history and audit
  source `TMS`, reason "Carrier API").
- **Governance kept.** A key writes resources and status directly only when
  the department already trusts it for direct writes — its mark and
  `CarrierApi__AutoApply=on` (`CarrierApi.MayWriteDirectly`). Other keys get
  `403 not-trusted` there and keep the routes the department governs (truck
  into empty cells; status events approved by the job's owner). No key gained
  a power the department had not given it.
- **Accept without a truck**, as the portal has since Phase 2; the V1 body with
  a truck is read exactly as before. An accept or decline already made answers
  `replayed: true` and writes nothing (it used to write a second audit row).
- **Decline with a reason code and remark**, kept on the assignment with the
  key as responder; V1's `reason` is still read as the remark under `OTHER`.
- **New reads/writes:** `GET /fleet`, `PUT /assignments/{id}/resources`,
  `POST /assignments/{id}/status`, `GET /assignments/{id}/history`,
  `GET /billing/cases`, `GET /billing/invoices/{id}/status`,
  `PUT /billing/invoices/{id}/original-package`.
- **Upload controls at the door:** declared type on an allow-list (PDF, JPEG,
  PNG, WEBP, HEIC/HEIF, TIFF) and first bytes that agree; document `kind` of
  the requirement rules' shape (never a path).
- **Projection hygiene:** invoice answers are explicit copies without the
  LESCHACO reviewer's or receiver's name (the portal does not show them either),
  besides the storage coordinates Codex already removed.
- **Billing lists paged in the database** — the eligible list no longer reads a
  1,000-row window and filters it in memory.
- A truck, trailer or driver outside the carrier's register now has its own
  result (`not-owned`: `400` on the API, still `404` on the portal) instead of
  reading as "job not held".

## Defects found and fixed (production-relevant)

The end-to-end run found two hand-opened transactions outside the execution
strategy. The context is configured with `EnableRetryOnFailure`, and a retrying
strategy refuses such a transaction, so each call threw before doing anything:

1. **`BillingValidationService.SubmitAsync`** (Phase 5) — every billing submit,
   the **portal's and the API's**, answered `500`.
2. **`WorkflowService.ReassignSupplierAsync`** (Phase 2) — the My Jobs carrier
   reassignment answered `500`.

Both now run through `CreateExecutionStrategy().ExecuteAsync`, as the other
fifteen transactional sites in the codebase do. Both were re-run end to end.

The Billing Case creation audit row now carries the channel's source (it read
`web` when the API triggered it).

## Database changes

None. Phase 9 reuses `carrier_api_clients`, `carrier_api_requests`,
`supplier_requests`, `supplier_trucks`, `supplier_drivers`, `workflow_events`,
`shipment_milestones`, `billing_cases`, `billing_invoices`, links, validation
rows, documents, original packages and audit events. The AI checks' snapshot
comparison confirms the runtime model matches the last migration.

## API changes

Existing (unchanged contract unless noted): `GET /me`, `GET /assignments`,
`GET /assignments/{id}`, `POST …/accept` (truck now optional), `POST …/decline`
(`reasonCode`/`remark` added, `reason` still read), `PUT …/truck`,
`POST|GET …/events`, webhooks.

Phase 9: `POST /assignments/{id}/pod` · `GET /fleet` ·
`PUT /assignments/{id}/resources` · `POST /assignments/{id}/status` ·
`GET /assignments/{id}/history` · `GET /billing/eligible` · `GET /billing/cases` ·
`POST /billing/cases/{caseId}/draft` · `PUT /billing/invoices/{id}` ·
`POST /billing/invoices/{id}/documents` · `POST /billing/invoices/{id}/submit` ·
`PUT /billing/invoices/{id}/original-package` · `GET /billing/invoices/{id}` ·
`GET /billing/invoices/{id}/status` · `GET /billing/invoices/{id}/validation`.
Contract: `docs/integrations/carrier-tms/SCMOS_CARRIER_TMS_API_V1.md`.

## UI changes

None. Keys, the trust mark and webhooks stay on Integrations → Carrier API.

## Security review (spec §51)

- **Authentication** — the existing bearer key (SHA-256 at rest, revocable).
- **Tenant isolation / IDOR** — no body or query field selects a supplier
  (checked by reflection); every read and write filters by the key's supplier;
  another carrier's known job, case, invoice, validation, POD, history or
  package id is `404`, never `403` (verified end to end, below).
- **Mass assignment** — explicit request records; server-side state only.
- **File upload** — allow-listed types with byte sniffing, 32 MB, kind shape,
  private storage, no URL or object key in any answer. SCMOS has no malware
  scanning infrastructure; none was added.
- **Injection / XSS / CSRF** — EF parameterised queries; JSON only; bearer key,
  no cookies.
- **Rate limiting** — the existing 120/min per key, 20/min keyless.
- **Errors** — Problem Details with stable codes; no stack traces.
- **Audit** — every write through the door records `who = carrier-api:ck_…`,
  source `TMS`, with previous values; history rows carry `TMS`.

## Tests

- **End to end on LocalDB** (isolated database `scmos_p9`, two carriers, a
  trusted and an untrusted key for A, a trusted key for B; `CarrierApi__AutoApply=on`):
  **68 checks, 68 passed** — identity; offer isolation; accept without truck,
  idempotent replay (same key → stored answer, new key → `replayed`), missing
  key `400`; decline code/remark kept with the key as responder; fleet
  isolation; untrusted resources/status `403`; foreign truck and driver
  `400 not-owned`; the ladder (forward, replay, backwards `409`, unknown `400`);
  Delivery Complete → exactly one Billing Case, replay → the same case;
  history; eligible and filtered case lists; draft create/replay; draft edit;
  uploads refused for text, an EXE declared as PDF, and a path as kind; a real
  PDF reaching storage; submit → validation persisted (ownership pass, rate and
  tax rule not found → `BLOCKED`); validation/status/detail reads; original
  package before approval `409`; every cross-carrier probe by known id `404`;
  audit and history rows under the key with source `TMS`; reassignment
  (`200`, old superseded, new pending, superseded carrier `404`); an untrusted
  key's status event still queued for the owner (`202`).
- **Rule checks** — all 32 in the API workflow pass, including
  `--check-carrier-billing-phase9` (22 checks: supplier-free bodies, trust,
  ladder, upload sniffing, kind and reason-code shapes, status vocabulary).
- **AI checks** — 1,017 passed (includes the migration snapshot comparison).
- **Web** — 659 tests passed (carrier suites 39); TypeScript clean; ESLint clean
  on the changed test.
- **Builds** — API Debug and Release with warnings as errors.

## Known limitations

- **A Billing Case opens only when Delivery Complete is recorded through the
  portal or a trusted key.** A job an operator closes on the grid, or one whose
  status reaches `COMPLETED` any other way, gets no Billing Case, so its carrier
  cannot bill it through either door. Spec §16 asks for a consistent trigger.
  Opening cases for grid-closed jobs — and from which date, since history must
  not be backfilled — is a business decision (**BUSINESS RULE REQUIRES
  CONFIRMATION**); it is a Phase 4 change, not made here.
- **Two status ladders.** The owner-approved event path (`LineAuthority`) and the
  portal ladder (`CarrierOperations`) are separate rules for the same register
  column. They agree today; the drift risk is noted, not addressed.
- **Untrusted keys cannot reach Delivery Complete through the API** — by design
  of the governance above; their jobs close through the portal or the grid.
- **Payment information** to carriers remains TBD and is not exposed.
- **Billing webhooks** (returned, approved online, original received) are not
  sent yet; carriers poll `…/status`.
- A transient database failure in the middle of a strategy-wrapped transaction
  retries the whole unit without replay protection — the codebase-wide
  convention, not specific to Phase 9.

## Next phase

Phase 10 (AI assistance behind feature flags) depends on deterministic billing,
documents, validation and human approval — all in place. Before it, the
Billing Case trigger for grid-closed jobs should be decided, because the AI
billing assistance will read the same cases.
