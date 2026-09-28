# Carrier Agent (AI Agent Platform, step 11) — spec §29

Built 28 Sep 2026 on the foundation (`SCMOS_AI_AGENT_PLATFORM_FOUNDATION.md`)
and the scan of the OTD and Validation agents (`SCMOS_AI_OTD_VALIDATION_AGENTS.md`).
It is the registry's `vendor-agent` descriptor, never connected before, now
named **Carrier Agent** and connected to the rule-first pass, not to chat
(where it still has no executor). **Rule first, no model. Starts off (its own
flag, `AI__VendorAgentEnabled`), and in shadow when on.** It recommends which
carrier to ask; a person asks, through the workflow. It sends nothing and
writes nothing to a job.

## What it looks at

A job that is not finished, not cancelled, not domestic (DELIVERY), has **no
carrier**, **no request waiting or confirmed** (AP-04: one carrier holds a job
at a time — `supplier_requests_one_active_job_idx`), and is planned from
yesterday to **seven days** ahead.

## The order — the workflow's own

`WorkflowService.Rank` is the ranking `PriorityForAsync` has always used, pulled
out pure so both read it: a customer's jobs of the same category that name a
carrier; carriers with at least 5 measured runs first, best on-time rate first;
then the rest by how many of this customer's jobs they have carried (one run on
time is not a 100% record). The pass ranks from the same register read the other
agents use; a scratch-database check holds it equal to `PriorityForAsync` over
the table.

Carriers already asked for the job (any outcome: declined, expired, cancelled)
are removed. The next three are named in order: "ask X first · if declined or
no answer, ask Y · then Z". There is no price in the order: the rate book is not
yet in the database the API reads (as the workflow says).

## What the register bars, and what it only notes

| Case | Effect | Rule reference |
| --- | --- | --- |
| a spelling the Supplier Register cannot resolve (name, code, alias) | passed over — the workflow refuses a request for it | `Carrier.NotRegistered:{KEY}` |
| status **suspended** or **rejected** | passed over | `Carrier.Status:{KEY}:{status}` |
| status draft / pending-audit | named, noted | `Carrier.Note:{KEY}:status-…` |
| required papers missing or expired | named, noted | `Carrier.Note:{KEY}:compliance-…` |
| tank job, not ISO-tank capable; reefer (RF), not reefer capable; product says DG, not DG capable | named, noted | `Carrier.Note:{KEY}:no-tank` / `no-reefer` / `no-dg` |
| nobody left to recommend | REQUIRES_HUMAN_REVIEW; HIGH within the monitor's two days, else MEDIUM | `Carrier.NoEligible` |

Notes do **not** reorder: whether an unapproved status, missing papers or a
capability flag should stop a carrier being asked is a business rule not yet
confirmed. Until it is, the agent says so beside the name and leaves the order
the workflow's.

Risk: WATCH when the plan is within the monitor's two days, none further out.

## Shadow comparison

When a Carrier decision closes (the job left scope, or the finding changed), the
pass records what a person did: the first carrier **asked through the workflow
after the decision was raised** (`supplier_requests.requested_at`), or, with no
request, the carrier now on the job. `human_choice` is that carrier;
`human_matches` is whether it is the one the agent said to ask first; the note
names who asked. A decision nobody acted on records nothing. A person may also
answer a decision directly in the Control Tower (agreed / did otherwise /
dismissed), as for the other agents.

The governance section now shows, per agent, **ตรงกับคน 30 วัน**: decisions in
the last 30 days where a person's action can be set against the agent's, and how
many agreed (`compared30d` / `matched30d` from `ai_decisions.human_matches`).
This is the figure autonomy would be raised on (§16, §61, §66). A dismissal
compares nothing.

## Switching it on

1. Deploy (API + web). No migration.
2. In the Portal: `AI__VendorAgentEnabled=true` (with `AI__Enabled=true`,
   already on). Check the governance section first: the Carrier Agent row must
   show shadow on.
3. The next pass (every `AI__AgentScanMinutes`, 15) raises findings under the
   **ผู้ขนส่ง** filter in "งานที่ AI ตรวจพบ".

Turning it off: the flag, or its status in the governance section (PAUSED /
DISABLED). Refused, it writes and closes nothing.

## Open questions for the department

- Should missing/expired compliance papers, an unapproved status, or a capability
  mismatch (tank, reefer, DG) **remove** a carrier from the recommendation, or
  keep noting it? (Today: note only.)
- Carrier confirmation SLA and whether expiry may auto-advance (confirmation 3 of
  the platform spec) — the agent recommends the next carrier after an expiry but
  never asks on its own.

## Verification (28 Sep 2026, local only)

- `tests/Scmos.Ai.Checks`: 1,133 checks without a database, 1,288 with
  `--write-local-db --local-db` (15 Carrier scenarios; scanner lifecycle with
  real requests — matched, superseded after a refusal, not matched when the
  operator skipped the order; the governance counts; the workflow parity).
- API rule checks (34) and the incident import checks; web `npm test` 686; tsc; eslint.
- Live on a scratch LocalDB: scan → three findings (SHORE first on its 6/6
  record; suspended KERRY passed over; a customer nobody has run → a person
  chooses). Two requests through `POST /api/workflow/{job}/supplier-request`
  → rescan resolved both, one matched, one not; the Control Tower showed the
  **ผู้ขนส่ง** filter and **1/2 (50%)** for the Carrier Agent. Scratch database dropped.
