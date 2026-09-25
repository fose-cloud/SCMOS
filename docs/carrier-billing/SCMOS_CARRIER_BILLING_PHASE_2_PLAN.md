# SCMOS Carrier Collaboration + Billing — Phase 2 implementation plan

## Inspection result

Phase 2 extends the existing transportation workflow; it does not add another job or carrier model.

| Requirement | Existing component | Decision |
| --- | --- | --- |
| Job source | `OperationJob` / My Jobs register | REUSE |
| Carrier master and tenant | `Supplier`, `SupplierAlias`, `CarrierTenantContext` | REUSE |
| Assignment and history | `SupplierRequest` | EXTEND |
| Assignment rules | `CarrierAssignment`, `WorkflowService` | EXTEND |
| Carrier offers and accepted work | `CarrierService`, Carrier Portal, Carrier TMS API | EXTEND |
| Audit | `WorkflowEvent`, `AuditService` | EXTEND |
| Notifications | `CarrierWebhookQueue` | REUSE |
| Schedule source | accepted assignment + `OperationJob` projection | REUSE; no schedule table |

## Files and modules

- Extend `SupplierRequest` and its EF mapping with stable supplier identity, responder, structured rejection reason, remark, and the preceding assignment link.
- Extend `CarrierAssignment` with the complete active/closed state vocabulary and idempotent transition rules.
- Extend `WorkflowService` and workflow endpoints with explicit reassignment and complete assignment history.
- Extend `CarrierService` and carrier endpoints so an offer is answered by assignment id, stale/superseded offers cannot be accepted, and repeated identical accept/reject calls succeed without repeating side effects.
- Update Carrier Portal and the My Jobs workflow panel to show pending acceptance, assignment history, reassignment, and accepted schedule availability.
- Add an additive EF migration and Phase 2 automated checks.

## Schema change

The existing `supplier_requests` table remains the assignment table and history. Add nullable/backward-compatible columns:

- `supplier_id` — stable tenant identity for new assignments; historical rows retain alias-aware fallback.
- `responded_by` — authenticated human/API identity that answered.
- `reason_code` and `remark` — structured rejection/reassignment evidence.
- `previous_request_id` — prior assignment in a reassignment chain.

Add a filtered unique index on `job_key` for active outcomes (`pending`, `confirmed`) so concurrent requests cannot create two active assignments. No historical rows are deleted or rewritten.

## State mapping

The existing wire values remain backward compatible:

- `pending` = `PENDING_ACCEPTANCE`
- `confirmed` = `ACCEPTED`
- `rejected` = `REJECTED`
- `cancelled` = `CANCELLED`
- `expired` (and legacy `no-response`) = `EXPIRED`
- `superseded` = `SUPERSEDED`

## Security and integrity

- New assignments resolve to the existing Supplier master when possible and persist `supplier_id`.
- Carrier reads and writes are scoped to the authenticated supplier id; alias fallback is only for pre-Phase-2 historical rows.
- Accept/reject requires the offered assignment id, so a carrier cannot act on a later assignment by replaying an old job-key request.
- Reassignment closes the previous active record as `superseded`; it is never deleted.
- The accepted-work/schedule projection requires the current confirmed assignment, preventing a superseded carrier from retaining access.
- Database uniqueness and domain checks both enforce one active assignment per job.

## Regression risk

- Existing Carrier TMS endpoints keep their request contract and idempotency ledger.
- Existing workflow response and assignment endpoints remain available.
- Historical assignments without `supplier_id` continue to resolve through registered aliases.
- Carrier Schedule is a projection of `OperationJob` plus the current assignment, never a second source of truth.

## Phase completion report

### Implemented

- My Jobs creates one pending Carrier assignment against the original `OperationJob`.
- Carrier Portal and Carrier TMS remain the only identities allowed to accept or reject; LESCHACO can cancel, expire, or reassign but cannot answer on a Carrier's behalf.
- Accept/reject is assignment-id aware, idempotent, concurrency protected, audited, and rejects superseded assignments.
- Reassignment preserves the former assignment as `superseded`, links the next sequence, clears the former Carrier's truck/driver access from My Jobs, and sends the next offer through the existing webhook queue.
- Accepted work is projected automatically into the Carrier Schedule list; no schedule/job copy is created.
- My Jobs shows full assignment history including sequence, outcome, responder, reason code, remark, response time, and previous-assignment link.

### Reused

`OperationJob`, `Supplier`, `SupplierAlias`, `CarrierTenantContext`, `SupplierRequest`, `WorkflowEvent`, `AuditService`, `CarrierWebhookQueue`, `CarrierService`, Carrier Portal and Carrier TMS API.

### Created

- `CarrierCollaborationCheck` and `--check-carrier-collaboration` CI gate.
- `tests/carrierCollaboration.test.mjs` wiring/regression suite.
- EF migration `20260925031857_CarrierCollaborationPhase2`.

### Database changes

`supplier_requests` gains nullable `supplier_id` and `previous_request_id`, structured response evidence, responder identity, and SQL `rowversion`. A filtered unique index enforces one `pending`/`confirmed` row per Job. The migration preserves historical rows and stops with an explicit preflight error rather than guessing if legacy active duplicates exist. `Down` removes only Phase 2 additions.

### API changes

- `POST /api/workflow/{jobKey}/reassign-carrier`
- Carrier accept/decline bodies accept `requestId`; repeat answers return `replayed`.
- Carrier responses use 404 for a different/nonexistent offer and 409 for closed/conflicting state.
- Workflow response accepts only operator cancellation/expiry; Carrier accept/reject remains under authenticated Carrier routes.

### UI changes

- Carrier Portal sends the exact assignment id and exposes accepted assignments as `ตารางงาน`.
- Accept no longer requires truck/driver data; missing vehicle assignment is visibly marked for Phase 3.
- My Jobs workflow shows superseded/expired history and a controlled reassignment action.

### Security

New assignments require an existing Supplier/alias and store its stable tenant id. Stable identity is authoritative; alias fallback is limited to historical rows with a null supplier id. Current confirmed assignment, rather than a stale `trucker` string, grants schedule access. Known cross-tenant assignment ids return 404.

### Tests completed

- .NET Release build with warnings as errors: passed.
- Phase 2 domain/model checks: 19 passed.
- Phase 2 API/UI wiring checks: 8 passed.
- Carrier TMS API regression checks: passed.
- Phase 1 authorization/calendar regression checks: passed.
- Web tests: 632 passed, including the 8 new Phase 2 tests.
- ESLint: 0 errors (3 pre-existing warnings).
- TypeScript: passed.
- EF pending-model check: no changes after the migration.
- Idempotent SQL migration script generation: passed.

### Known limitations / next phase

- This Phase 2 branch has not been pushed, migrated, or deployed to Production.
- Phase 3 will add schedule filters (Today/Tomorrow/Week/Calendar), Carrier-owned truck/driver assignment, operational state transitions, POD, and Delivery Complete.
- Historical assignments intentionally remain nullable for `supplier_id`; alias-aware fallback preserves backward compatibility until a separately reviewed data reconciliation is approved.
