# Carrier TMS API — Phase 2 record

Implemented 20 September 2026 · released as v2.7.53 (API run 35502123054, web run 35502139896) · scope as set in the [assessment](SCMOS_CARRIER_TMS_ASSESSMENT.md) §20: accept / decline / truck with an idempotency ledger. Writes, through the portal's own service; no status events yet.

## PHASE COMPLETED — Phase 2

**Files created**

- `server/Scmos.Api/Data/Migrations/20260920091226_CarrierApiRequests.cs` (+ Designer) — the idempotency ledger, one additive table
- this record

**Files modified**

- `server/Scmos.Api/Rules/CarrierApi.cs` — `IdempotencyKeyOf`, `RequestHash`, `IsPlate` (the parser's rule), `ReadTruck` + `TruckInput`, the `idempotency-key-reused` (422) and `in-progress` (409) refusals
- `server/Scmos.Api/Services/CarrierService.cs` — `Result` gains `Code`, `Conflicts`, `Written`, `Skipped`, `Previous`; `AcceptForAsync` / `DeclineForAsync` extracted from the portal's `AcceptAsync` / `DeclineAsync` (which now call them); `UpdateTruckForAsync`; the empty-cell `Offer` rule
- `server/Scmos.Api/Endpoints/CarrierApiEndpoints.cs` — `POST …/accept`, `POST …/decline`, `PUT …/truck`; `IdempotentAsync`, `RefusedWrite` (404 / 409 by whether the job is in the carrier's lists), problem bodies stored and replayed
- `server/Scmos.Api/Services/CarrierApiAuth.cs` — `Principal.AsUser()` (the credential as `updated_by` / audit `who`)
- `server/Scmos.Api/Data/CarrierApiEntities.cs`, `ScmosDbContext.cs` (+ snapshot) — `CarrierApiRequest`
- `server/Scmos.Api/Data/LineEntities.cs` — `EventSource.CarrierApi = "TMS"`
- `server/Scmos.Api/Data/JobsRepository.cs` — `SnapshotAsync` now carries `contact` and `seal` (see the fix below)
- `server/Scmos.Api/Data/CarrierApiCheck.cs` — 26 checks added (idempotency key, request hash, the truck's fields)
- `docs/integrations/carrier-tms/SCMOS_CARRIER_TMS_API_V1.md`, `docs/integrations/SETUP.md`

**Existing components reused** — `CarrierService` (the acceptance, the refusal, the alias-aware carrier match, the cancel-the-others rule), `JobsRepository.PatchAsync / SnapshotAsync`, `JobStatus.SupplierConfirmed`, `Formats.Clean`, `LineParser.FindPlates / PlateKey / FindPhone` (the same reading LINE gives a haulier's message), `ContainerNumbers`, `AuditService`, the Phase 1 filter, rate limit and problem shape.

**APIs added**

| Route | What | Rules |
| --- | --- | --- |
| `POST /api/carrier/v1/assignments/{jobKey}/accept` | truck (required) + box/seal (empty cells) | `AcceptForAsync`; 404 not this carrier's; 409 already accepted / not offered / conflict |
| `POST /api/carrier/v1/assignments/{jobKey}/decline` | reason | `DeclineForAsync`; 404 / 409 as above |
| `PUT /api/carrier/v1/assignments/{jobKey}/truck` | any of the five cells | `UpdateTruckForAsync`; empty cells only; 409 conflicts named, nothing written; closed job refused |

All three: `Idempotency-Key` required (400 without), replay with `Idempotent-Replayed: true`, 422 on reuse, 409 in flight.

**Tools / agents added** — none.

**Database changes** — `carrier_api_requests` (id, client_row_id, idempotency_key, request_hash, response_status, response_content_type, response_body, created_at, completed_at; unique (client_row_id, idempotency_key)). Existing tables untouched. **Migration status** — applied to LocalDB for the probe; applied to production by the v2.7.53 API release. `Down` drops only the new table.

**Security changes** — machine writes now exist and are (a) scoped by the key's supplier, (b) limited to what the portal already allows a carrier, (c) idempotent, (d) audited under the credential's own name with source `TMS` and each cell's previous value. No capability added or changed. Values are validated to the register's standard before anything is looked up.

**Permissions added** — none.

**Tests added** — 26 checks in `--check-carrier-api` (86 total). Unchanged suites pass: `--check-line`, `--check-capability`, 343 AI checks, 578 Node tests; build with warnings as errors.

**Build result** — green at 80c9a79 (v2.7.53).

**Verified end to end (LocalDB, 20 Sep)** — two SANGJA offers created through the workflow; accept without a key → 400; bad fields → 400 with three named problems; accept with box and seal → 200, seven cells written, `SUPPLIER_CONFIRMED`, the correlation id echoed; the same key → replayed, identical body; the same key with another body → 422; a fresh key on the now-accepted job → 409 `group: accepted`; THREETRANS's key on SANGJA's offer → 404; the assignment read back with the truck, box and seal; decline → 200, again → 404, on an accepted job → 409; truck on a held job: first write, then skipped, then a different contact → 409 with the current value; empty body → 400; the other carrier → 404. Audit rows: `who = carrier-api:ck_…`, `source = TMS`, previous values recorded.

**Fix found on the way** — `JobsRepository.SnapshotAsync` never carried `contact` or `seal`, so any "write only into an empty cell" test on those two read the cell as empty, and the portal's audit "before" for contact had always been "(ว่าง)". Both cells are in the snapshot now; the jobs PUT's audited-field list is unchanged.

**Known risks**

- A TMS that does not send an `Idempotency-Key` cannot write (by design); the error names the header.
- Replayed answers carry the correlation id of the *first* request in their body (the header carries the current one).
- The `carrier_api_requests` sweep runs per write per client; a client that never writes again keeps at most its last month of rows.
- The acceptance cancels other carriers' pending requests exactly as the portal does — a TMS accepting late (after the operator moved on) is refused with 409 `not-offered`, not silently applied.

**Remaining tasks** — Phase 3 (status events queued for the owner's approval; REMARK fallback), Phase 4 (webhooks), Phase 5 (auto-apply behind a flag).

**Recommended next phase** — Phase 3, once a carrier has accepted a real offer through the API on production.
