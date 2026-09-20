# Carrier TMS API — Phase 5 record

Implemented 20 September 2026 · released as v2.7.57 (API run 35507144419, web run 35507159422) · scope as set in the [assessment](SCMOS_CARRIER_TMS_ASSESSMENT.md) §20: auto-apply for named carriers behind a flag. The last phase of V1.

## PHASE COMPLETED — Phase 5

**Files created**

- `server/Scmos.Api/Data/Migrations/20260920110721_CarrierApiAutoApply.cs` (+ Designer) — one additive column
- this record

**Files modified**

- `server/Scmos.Api/Rules/CarrierApi.cs` — `AutoApplyKey`, `AutoApplyOn` (only "on"), `AutoApplies` (the setting on **and** the key marked)
- `server/Scmos.Api/Data/CarrierApiEntities.cs` (+ snapshot) — `CarrierApiClient.AutoApply`
- `server/Scmos.Api/Services/CarrierApiAuth.cs` — the principal carries the mark
- `server/Scmos.Api/Endpoints/CarrierApiEndpoints.cs` — the events route writes at once for a trusted key (`AutoApplyAsync`), `/me` reports `autoApply`, `POST /api/carrier-api/clients/{id}/auto-apply` for the department, the listing carries the mark and the switch
- `server/Scmos.Api/Endpoints/LineReviewEndpoints.cs` — `ArrivalWrite` made internal, so the auto-apply writes the arrival exactly as the approval does
- `server/Scmos.Api/Data/CarrierApiCheck.cs` — 3 checks added (137 total)
- `app/scmos/screens/CarrierApiClients.tsx` — the "เขียนทันที / รออนุมัติ" toggle per key, the switch's state in the header
- `docs/integrations/carrier-tms/SCMOS_CARRIER_TMS_API_V1.md`, `docs/integrations/SETUP.md`

**Existing components reused** — `LineAuthority.Decide / Move` (the same judgement as a queued event), `LineReviewEndpoints.ArrivalWrite` (the same empty-cell write as the owner's approval), `JobsRepository.PatchAsync`, `AuditService`, the `line_events` row as the record of what was applied, the Phase 4 `event.decided` webhook (`decidedBy: "auto"`), the `ManageSuppliers` guard and second-factor pattern for the department's mark.

**APIs added**

| Route | Who | What |
| --- | --- | --- |
| `POST /api/carrier-api/clients/{id}/auto-apply` `{ enabled, reason }` | `ManageSuppliers` | marks or unmarks a key; audited (`carrier-api-client`, field `auto_apply`) |

`POST /api/carrier/v1/assignments/{jobKey}/events` now answers `200 applied` with `written` for a trusted key; `GET /me` carries `autoApply`.

**Tools / agents added** — none.

**Database changes** — `carrier_api_clients.auto_apply` (bit, default 0). **Migration status** — applied to LocalDB for the probe; applied to production by the v2.7.57 API release. `Down` drops only the column.

**Security changes** — machine writes to the job's status and arrival without a person, gated twice: the department's mark on the specific key (audited) and an administrator's setting on the API (`CarrierApi__AutoApply=on`; off unless exactly "on", the `Line__Replies` shape). Neither alone does anything. The ladder still refuses a step back, a closed or held job; the arrival still goes only into empty cells; every write is audited under the key's own name, source `TMS`, reason "TMS auto-apply". Accept, decline, the truck and a note were already immediate and are unchanged.

**Permissions added** — none.

**Tests added** — 3 checks in `--check-carrier-api` (137 total). Unchanged suites pass: `--check-line`, `--check-capability`, 343 AI checks, 578 Node tests; build with warnings as errors; tsc; eslint.

**Build result** — green at 29e4513 (v2.7.57).

**Verified end to end (LocalDB, 20 Sep)** — with `CarrierApi__AutoApply=on`: an unmarked key's `dispatched` → 202 queued (dismissed); the department marks the key (200, audited; marking again says so); `/me` → `autoApply: true`; `dispatched` → 200 `applied`, `written: {status: DISPATCHED}`, the job DISPATCHED; `dispatched` again → 200 already-there; `arrived` 10:20 → 200 applied, `written: {status: DELIVERED, arrDate, arrTime}`, the job DELIVERED with the stamp; `in_transit` → 409 backwards; the events list reads applied / already-there / backwards; nothing waits in the drawer; unmarked → the next event queues. With the setting off: a marked key's `/me` → `autoApply: false` and its event queues. Audit rows: `carrier-api:ck_…`, source TMS, reason "TMS auto-apply (ck_…)".

**Known risks**

- Trust is per key, not per event type: a marked key's `delivered` without a prior `arrived` writes DELIVERED with no arrival stamp (as a LINE "ลงเสร็จ" would); the owner keys the time. A per-type mark is a follow-up if wanted.
- The setting is one for the whole API; the department's marks are the fine control. The setting is the kill switch.
- An auto-applied status is not undone by SCMOS; the grid is where a person corrects it, with the audit trail showing the key that wrote it.

**Remaining tasks** — none in V1. Follow-ups noted across the phases: the bell/badge wording for TMS rows; a resolved-address check for webhooks at send time; auto-disabling a webhook that stays dead; an Entra client-credentials option for carriers with a tenant; a per-event-type auto-apply mark.

**Recommended next step** — production use: issue a key from the screen for the first carrier, have its TMS read `/me` and `/assignments`, register a webhook and check a `ping`'s signature, queue one event and approve it; only then mark the key and switch auto-apply on.
