# SCMOS Carrier Operations — Phase 3 Completion Report

## Outcome

Phase 3 adds the carrier-side operating workflow after an assignment has been accepted: truck and driver assignment, controlled status progression, immutable operational history, POD upload, and idempotent Delivery Complete.

## Existing Components Reused

- `OperationJob` remains the current operational source of truth.
- `SupplierTruck` and `SupplierDriver` remain the carrier fleet masters.
- `ShipmentMilestone` remains the operational milestone projection.
- `WorkflowEvent` remains the append-only reassignment and status history.
- `StoredDocument` and the existing blob storage service remain the POD store.
- The existing `JobStatus` category ladder remains the canonical job status vocabulary.
- `CarrierTenantContext` and the confirmed `SupplierRequest` assignment remain the tenant boundary.

No parallel carrier job, vehicle, driver, status, history, or document tables were introduced.

## Carrier Workflow

The portal now supports these carrier actions for confirmed work:

1. View the accepted-job schedule by Today, Tomorrow, next 7 days, selected calendar date, Truck Unassigned, Active, or Completed.
2. Select an active registered truck and driver, with an optional registered trailer.
3. Reassign resources while the job remains open; the current assignment is updated and the previous assignment remains visible in `WorkflowEvent` history.
4. Move through the controlled operational sequence: Dispatched, Picked Up, Loading, In Transit, Delivered, Container Returned when applicable, and Delivery Complete.
5. Upload and open multiple POD documents using the existing document service.
6. Review operational and reassignment history from the job card.

Invalid backward transitions and category-incompatible transitions are rejected. Repeating the current state is treated as an idempotent replay. Delivery Complete maps to the existing completed/closed state and repeated submissions do not create duplicate history.

## API Additions

- `PUT /api/carrier/{jobKey}/resources`
  - accepts registered `truckId`, optional `trailerId`, and registered `driverId`
  - verifies that every selected resource is active and belongs to the signed-in carrier
- `POST /api/carrier/{jobKey}/status`
  - accepts the operational event type, optional event time, and optional remark
  - validates the transition against the job category ladder

POD continues to use the existing `/api/documents` upload and download paths with folder `POD`, so document storage and authorization stay centralized.

## Tenant and Authorization Controls

- The browser never submits or selects a supplier identity.
- Confirmed `SupplierRequest.SupplierId` is authoritative for current assignments.
- Alias matching is retained only for historical jobs that do not have assignment history.
- A stale or reassigned carrier cannot update resources, advance status, list POD, or access POD by a known document ID.
- Fleet ownership is validated on the server, not inferred from the portal payload.
- Every mutation is staged with the existing audit service and committed with the operational change.

## Database and Migration Decision

Phase 3 requires no schema migration. All requirements are represented by existing normalized entities and append-only history. The release check must therefore report no pending EF Core model changes.

## Automated Verification

- Carrier operations domain check covers the full sequence, invalid transitions, container-return applicability, backward-transition rejection, and Delivery Complete idempotency.
- Carrier collaboration regression checks remain enabled.
- Node tests cover fleet ownership, reassignment history, milestone linkage, tenant isolation, API payload boundaries, and portal actions.
- The API workflow now runs the carrier operations rule check on every deployment.

Final local results:

- Carrier operations rule checks: 13 passed.
- Carrier collaboration regression checks: 19 passed.
- Carrier API and Carrier Billing foundation regression checks: passed.
- Web tests: 639 passed, 0 failed.
- TypeScript typecheck: passed.
- Next.js production build: passed.
- API Release build with warnings treated as errors: passed.
- ESLint: 0 errors; 3 pre-existing warnings outside the Phase 3 files.
- EF Core pending model check: no model changes since the last migration.

## Deliberate Boundaries

- SCMOS currently has no separate trailer master; an optional trailer is represented by a second active `SupplierTruck`. A duplicate trailer table was not created.
- The external TMS carrier API keeps its existing operator-approval event pipeline. The signed-in portal path applies the same existing SCMOS status vocabulary and category ladder directly for an accepted carrier assignment.
- Billing-case creation after Delivery Complete belongs to Phase 4 and is not introduced in this phase.
