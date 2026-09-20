# SCMOS AI platform — Phase 1E record: approval hardening

Implemented 20 September 2026 · released as v2.7.59 · the slice the [re-assessment](SCMOS_AI_PLATFORM_ASSESSMENT.md#re-assessment--20-september-2026-v2751) recommended first (security items S1 and S2), so that the queue every later agent lands its proposals in is governed before it carries anything that matters.

## PHASE COMPLETED — 1E

**What the queue now is.** One table, `approvals`, unchanged in purpose: a tool marked *Approval* in the catalogue writes nothing and parks the exact payload for a person. What changed is who may do what to a row, written once in `Ai/ApprovalPolicy.cs` and enforced the same way at every route:

| Act | Who | Conditions |
| --- | --- | --- |
| propose (`POST /api/ai/invoke`) | an internal account holding the capability the tool stands in for (`update_shipment`/`assign_supplier` → EditOwnJobs, `update_rate` → EditRates, `close_carpar` → CloseCarPar, `send_email` → ApproveAi) | summary ≤ 500 chars, ≤ 20 fields, identifier keys, plain-text values ≤ 500 chars, < 8 KB; a key naming an identity or a permission (`role`, `userId`, `sql`, `authorization`…) is refused |
| see (`GET /api/ai/approvals`) | approvers (ApproveAi) see all; anybody else their own; a carrier nothing (403) | rows past their expiry are marked `expired` on the way |
| decide (`POST /api/ai/approvals/{id}`) | an approver who is **not** the requester | pending, not stale; second factor first; the state change is conditional on `pending`, so two approvers have one effect |
| withdraw (`POST /api/ai/approvals/{id}/cancel`) | the requester or an approver | pending, not stale; a row not theirs to see answers 404 |
| record as applied (`POST /api/ai/approvals/{id}/applied`) | an approver | approved, not stale; the body's `hash` must equal the row's fingerprint (SHA-256 of the payload as stored) — a result typed by a client proves nothing about execution, so what is recorded is *that a person did the thing, and which thing* |

Every row now carries the requester's account id, the payload's fingerprint, the request's correlation id, an expiry (a week pending; a week after approval), and who recorded it applied and when. Rows from before 1E are matched by the signature stored then and answer to their payload's own fingerprint.

**Files created**

- `server/Scmos.Api/Ai/ApprovalPolicy.cs` — the rules, pure
- `server/Scmos.Api/Endpoints/AiApprovalEndpoints.cs` — the `/api/ai` matrix and queue routes, moved out of `SupplierEndpoints` where they had lived since the first AI release (so the checks can host them alone; nothing else changed in the move)
- `server/Scmos.Api/Data/Migrations/20260920123432_AiApprovalHardening.cs` (+ Designer) — six additive columns
- `tests/Scmos.Ai.Checks/ApprovalChecks.cs`
- this record

**Files modified**

- `server/Scmos.Api/Data/SupplierEntities.cs`, `ScmosDbContext.cs` — `Approval.RequesterId`, `PayloadHash`, `CorrelationId`, `ExpiresAt`, `AppliedBy`, `AppliedAt`
- `server/Scmos.Api/Services/AiGateway.cs` — `InvokeAsync` binds and fingerprints; `ApprovalsAsync(user, state)` is scoped and sweeps expiry; `DecideAsync`, `CancelAsync`, `MarkAppliedAsync(id, result, reviewedHash, user)` are conditional updates; `FindAsync`, `ExpireAsync`; `ApprovalView` carries `payloadHash`, `expiresAt`, `appliedBy/At`, `mine`, `actions`
- `server/Scmos.Api/Endpoints/SupplierEndpoints.cs` — the AI section removed; `Program.cs` — `app.MapAiApprovals()`
- `app/scmos/screens/Assistant.tsx` — the queue offers only the `actions` the API returned for the reader; withdraw; fingerprint and expiry under each row; `expired`/`cancelled` states; "บันทึกว่าทำแล้ว" quotes the fingerprint
- `tests/Scmos.Ai.Checks/Program.cs`, `OperationsChangeChecks.cs` — the new suite registered; the legacy-bypass check updated to the new signature

**Existing components reused** — `AiPermissions` (the catalogue; the permission levels are untouched), `AiPermissionPolicy.InternalUser`, `Capability`/`Roles`, `ApiResults.NeedsSecondFactor` (the same second-factor pattern as the Operations reviewed-change pilot and the supplier routes), `AuditService` (`approve`/`reject`/`apply` rows as before; a withdrawal is a `reject` row `pending → cancelled`), EF `ExecuteUpdate` for the conditional state changes (the pattern the Operations pilot's confirm uses).

**APIs added** — `POST /api/ai/approvals/{id}/cancel`. **Changed** — `GET /api/ai/approvals` is scoped and 403 for a carrier; `POST /api/ai/invoke` is 403 for a carrier and refuses a proposal the account could not make by hand; `POST /api/ai/approvals/{id}` asks for the second factor and answers 409 (not 403) when the row cannot be decided; `POST …/applied` requires `hash` and answers 409 without it.

**Tools / agents added** — none. No tool is connected to an executor; nothing in the queue is executed by SCMOS.

**Database changes** — `approvals`: `RequesterId` nvarchar(128) '' · `PayloadHash` nvarchar(64) '' · `CorrelationId` nvarchar(64) '' · `ExpiresAt` datetimeoffset null · `AppliedBy` nvarchar(120) '' · `AppliedAt` datetimeoffset null. **Migration status** — applied to LocalDB for the probe; applied to production by the v2.7.59 API release. `Down` drops only the six columns. No table renamed or dropped; no existing column changed.

**Security changes** — S1 closed (listing/creation scope; carrier and IDOR cases tested). S2 narrowed: "applied" is no longer a free marker — it needs an approver, the second factor, an approved unexpired row and the fingerprint the approver read; it is still, deliberately, not an executor. Self-approval refused. Concurrent decisions have one effect. Payload keys that name an identity or a permission are refused at the door.

**Permissions added** — none. The existing `ApproveAi` capability is what makes an approver; the tool-to-capability map reuses the capabilities the manual change already needs.

**Tests added** — 66 checks in `tests/Scmos.Ai.Checks` (`ApprovalChecks`): 53 pure (offline, CI) and 34 more on LocalDB under `--write-local-db` (conditional updates, scoped listing, race, expiry sweep, withdrawal, the HTTP guards incl. carrier 403, foreign-id 404, MFA refusal, hash-less 409, one audit row). Totals: 396 offline (was 343), 462 with `--write-local-db`. Unchanged suites pass: 579 Node tests, `--check-capability`, `--check-carrier-api` (142), `--check-line`; `-warnaserror` build; tsc; eslint.

**Build result** — green at the 1E commit.

**Verified on LocalDB, 20 Sep** — as Watsana (operator): `update_shipment` proposal → 200, `approvalId`; `update_rate` → 403 "เสนอได้เฉพาะผู้ที่มีสิทธิ์ทำการเปลี่ยนแปลงนี้เอง"; a payload with `role` → 403 naming the key. As Nattikorn (assistant manager): `update_rate` → 200. Uthai (another operator) lists 0; Watsana lists her own with `["cancel"]`; Titchanatorn (supervisor) lists both with `["approve","reject","cancel"]`; Nattikorn sees `["cancel"]` on his own and the full set on Watsana's. On the screen as Titchanatorn: approve → "อนุมัติแล้ว …", the row turns approved with the fingerprint and a new expiry, the only button left is "บันทึกว่าทำแล้ว"; that → applied, "ทำแล้วโดย SV-01".

**Known risks**

- The "applied" record is a person's statement; SCMOS does not verify that the change was made. That is the design (no executor in 1E); the audit row names who said so.
- A supervisor who is the only approver cannot approve their own proposals — by design; another supervisor or a manager must.
- The Assistant screen still shows a paragraph of explanatory prose about deletion (from the first AI release); it predates the no-prose rule and is not touched by this slice.
- The expiry sweep runs on listing, not on a clock: a stale row nobody lists stays `pending` in the table (and is refused on any act) until the next listing.

**Remaining tasks** — 1D (multi-step audit topology, correlation id from web proxy through tool to audit, bounded conversation context), then Phase 2 (the Data Agent's first read).

**Recommended next phase** — 1D.
