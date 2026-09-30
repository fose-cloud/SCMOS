# The Subcontractor's own menu

v2.8.0, 29 Sep 2026. Asked for by the department lead: a Subcontractor
account gets a dashboard of its own company, and a menu of Workspace (NEW job,
My job, Postpone), Rate, Billing and KPI. The KPI must show only that
carrier's own figures.

## The menu

| Entry | What it shows | Reads |
| --- | --- | --- |
| Dashboard | The company, then cards: new jobs waiting, today's jobs, in progress, waiting for a truck, postponed/cancelled, on-time this month (and the score), then the billing cards | `/api/carrier`, `/api/carrier/kpi`, `/api/carrier-billing/*` |
| Workspace → NEW job | Jobs the carrier keyed in itself for Leschaco to confirm, then jobs Leschaco offered to accept or decline | `/api/carrier/job-requests`, `/api/carrier` |
| Workspace → My job | The schedule, as before (today, tomorrow, 7 days, calendar, waiting for a truck, in progress, done) | `/api/carrier` |
| Workspace → Postpone | Its jobs cancelled, or moved from the date first planned (the Workspace's own CANCEL/MOVED rule); a cancelled job is shown, not worked | `/api/carrier` |
| Rate | Its contracted lanes of the rate book, every vehicle on every fuel band, read only | `/api/carrier/rates` |
| Billing | Billing, as before | `/api/carrier-billing/*` |
| KPI | Its line of the contract scorecard and its on-time delivery, by month or year, with the monthly trend | `/api/carrier/kpi` |

Every `/api/carrier…` answer is cut to the account's company **on the server**.
The company comes from the account (`staff.supplier_id`), never from a request.
Rate: a lane is the carrier's when written against its supplier, or, written
against no supplier, under a name it trades by. A lane written against another
supplier stays that supplier's, whatever it is spelled. The quotes and the
surcharge list are not included. KPI: measured over the carrier's own jobs, and
only its own line of the scorecard is returned. No other carrier's score, no
ranking, no department figure.

## Jobs a carrier keys in

The carrier fills the add-job form's own fields for IMPORT, EXPORT or
DELIVERY, with the same essentials. The request waits in `carrier_job_requests`.
It never becomes a job by itself:

1. The bell tells the department (team view only, never a carrier's):
   **ผู้ขนส่งแจ้งงานใหม่**.
2. On the Operation Workspace, a panel lists what waits. **เปิดเป็นฟอร์มเพิ่มงาน**
   opens the ordinary add-job form, filled with the request's fields and the
   carrier as trucker.
3. Saving that form is the only way the job enters the register. After the
   save lands, the request is marked APPROVED with the job's key.
4. **ไม่รับ** refuses the request with a reason, which the carrier reads.
   The carrier can withdraw a request while it still waits.

Every step is in `audit_events` (entity `carrier-job-request`). A carrier may
have at most 50 requests waiting. Only someone who may add a job can settle
one; a carrier never can. Migration `CarrierJobRequests` is forward-only.

## The boundary this closed

Signing in as a carrier and calling the department's routes directly showed
that a Subcontractor account, which holds `ViewDashboard` for its own screens,
was let through:

- `/api/kpi`, `/api/kpi/measures`, `/api/kpi/excel`: every carrier's scorecard;
- `/api/suppliers`: every supplier with its score, tax id and compliance files;
- `/api/dashboard/*` and `/api/risk`: the department's figures;
- the bell: "on-time below target" over every carrier's jobs.

None of these is a carrier's screen, so `Rules/CarrierBoundary.cs` now answers
a carrier 403 on the first four. The bell answers a carrier with nothing. The
rate book (`/api/rates`) already refused carriers and still does.

Still open, not changed here: other routes guarded only by sign-in or by
`ViewDashboard` have not all been walked through as a carrier. The safer
design is an allow-list of the routes a carrier may call. That is the
suggested next step.

## Verified

- `tests/Scmos.Ai.Checks` (`CarrierPortalChecks`): two carriers in one database.
  Each sees only its own lanes, KPI, Postpone list and requests. The request
  flow covers approve, refuse, withdraw, stale revision and audit. The boundary
  covers which paths it refuses.
- Web: `tests/carrierPortal.test.mjs` (menu, reads, parsing, the request
  opened as the add-job form).
- Clicked through against a scratch database with the demo Subcontractor and
  Admin accounts: every menu entry; a request keyed, opened as the form, saved
  and approved, and the carrier seeing the job and the approval; a refusal; the
  carrier's bell empty; phone width without sideways scroll.
