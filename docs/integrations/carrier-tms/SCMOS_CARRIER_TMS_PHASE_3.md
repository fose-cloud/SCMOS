# Carrier TMS API — Phase 3 record

Implemented 20 September 2026 · released as v2.7.55 (API run 35504032089, web run 35504050620) · scope as set in the [assessment](SCMOS_CARRIER_TMS_ASSESSMENT.md) §20: status events queued for the job owner's approval, REMARK fallback. No auto-apply.

## PHASE COMPLETED — Phase 3

**Files created**

- `server/Scmos.Api/Rules/CarrierEvent.cs` — the pure rules: the eight types, what each becomes on the ladder (`arrived` = the site rung, settled by category), the clock's bounds, the payload a row keeps and the reading it gives back, the queue's line, the states a TMS is told
- `server/Scmos.Api/Services/LineReadings.cs` — one reading and one judgement for every queued row, LINE or TMS
- this record

**Files modified**

- `server/Scmos.Api/Services/LineMatching.cs` — `DecideForJobAsync` (the key's supplier as the speaker, the one job by key, the same `LineAuthority.Decide`) and `SpeakerForSupplierAsync`
- `server/Scmos.Api/Endpoints/CarrierApiEndpoints.cs` — `POST …/events`, `GET …/events`
- `server/Scmos.Api/Endpoints/LineReviewEndpoints.cs` — the list, the drawer's pending feed, the review card and the approval read through `LineReadings`; the pending feed includes `tms` rows; audit source per row
- `server/Scmos.Api/Data/CarrierApiCheck.cs` — 21 checks added (107 total)
- `docs/integrations/carrier-tms/SCMOS_CARRIER_TMS_API_V1.md`, `docs/integrations/SETUP.md`

**Existing components reused** — the whole LINE approval path: `line_events` as the queue (message type `tms`, the event in `raw_payload` as the webhook keeps a LINE event), `LineAuthority.Decide / Move / ResolveSite / AwaitingArrival`, `LineMatching.Candidate / ToCandidates`, the owner check (`MayActOnAsync`, delegations), the claim NEED_REVIEW → PROCESSING, `ArrivalWrite` (empty cells only), `LineRemark.Note / Append`, `AuditService`, the job drawer's "LINE n" badge and the LINE screen's queue — which show TMS rows with the supplier and credential where the room's name would be. No new table.

**APIs added**

| Route | What | Rules |
| --- | --- | --- |
| `POST /api/carrier/v1/assignments/{jobKey}/events` | a status or a note, `at`, `remark`; `Idempotency-Key` | 202 queued / 200 already-there / 200 remark-written / 404 not this carrier's / 409 backwards, closed, held, not on ladder (kept) / 400 |
| `GET /api/carrier/v1/assignments/{jobKey}/events` | this carrier's events on the job with their state | 404 when the job is not this carrier's |

**Tools / agents added** — none.

**Database changes** — none. TMS events are `line_events` rows (`message_type = 'tms'`, `line_group_id = ''`, `line_user_id = 'carrier-api:ck_…'`, `raw_payload` = the event). **Migration status** — no migration.

**Security changes** — a TMS can move a job exactly as far as its LINE room can, and only through the owner's approval; the supplier is the key's, re-judged at approval from the stored payload, never from a body. A note is the one immediate write, into REMARK only, audited under the credential's name.

**Permissions added** — none. Approval rides on `EditOwnJobs` (own or covered jobs) / `EditAnyJob`, as for LINE.

**Tests added** — 21 checks in `--check-carrier-api`. Unchanged suites pass: `--check-line`, `--check-capability`, `--check-notifications`, 343 AI checks, 578 Node tests; build with warnings as errors.

**Build result** — green at 5d439e7 (v2.7.55).

**Verified end to end (LocalDB, 20 Sep)** — on the job SANGJA accepted in the Phase 2 probe (IMPORT, SUPPLIER_CONFIRMED): a bad type, a future clock and a note without a remark → 400; `dispatched` → 202 queued (from SUPPLIER_CONFIRMED to DISPATCHED), replayed with the same key; THREETRANS's key → 404; the drawer's feed lists the TMS row on the job with "SANGJA · SANGJA phase-3 probe", `to: DISPATCHED`, `ready: true`; the review card judges it `ok`; a supervisor approves through the LINE route → the job is DISPATCHED and the event reads `applied`; `dispatched` again → 200 already-there; `picked_up` queued and approved; `arrived` 10:20 → queued with the arrival, approved → DELIVERED with ARRIVAL 20/09/2026 10:20; `dispatched` now → 409 `backwards`, kept; a `note` → REMARK "20/09/2026 11:00 รอคิวลง". Audit rows: status changes by SV-01 with source TMS, the remark by `carrier-api:ck_…`.

**Known risks**

- ~~The bell's alert still reads "ข้อความ LINE รอการอนุมัติ" and counts TMS rows in it; the drawer badge says "LINE n". Both are the same queue; the wording is a follow-up, not a fault.~~ Done in v2.7.58: the bell, the row badge and the drawer name the door — LINE, TMS, or both.
- The LINE chase's "answered" hold read only `text` rows; with the chase off (v2.7.54) it does not matter, and a TMS row on a job is an answer too if it is ever turned back on.
- A TMS that reports `delivered` on an import without a prior `arrived` gets DELIVERED without an arrival stamp — as a LINE "ลงเสร็จ" does; the arrival cells stay empty for the owner to key. Documented.
- Events older than 7 days or ahead by more than 10 minutes are refused; a TMS with a wrong clock sees 400 with the reason.

**Remaining tasks** — Phase 4 (webhooks: offer, cancellation, event decided; delivery ledger, retries, HMAC signature), Phase 5 (auto-apply for named carriers behind a flag), the bell/badge wording.

**Recommended next phase** — Phase 4, after one carrier's TMS has queued and had approved a real event on production.
