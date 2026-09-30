# The Subcontractor's own menu

v2.8.0, 29 Sep 2026; Capacity and the department-style Dashboard in v2.8.1, 30 Sep 2026; the route allow-list and
My job as the department's Workspace in v2.8.2, 30 Sep 2026. Asked for by the department lead: a Subcontractor
account gets a dashboard of its own company, and a menu of Workspace (NEW job,
My job, Postpone), Rate, Billing and KPI. The KPI must show only that
carrier's own figures.

## The menu

| Entry | What it shows | Reads |
| --- | --- | --- |
| Dashboard | The department's own Dashboard (Executive and Operational tabs, every card and panel), counted over this company's jobs and measures; the hero names the company, its tiles lead to the carrier's screens (the NEW job tile shows how many offers wait); no TODAY tab and no AI briefing, which are the department's day | `/api/carrier/dashboard/jobs`, `/api/carrier/dashboard/measures` |
| Workspace → NEW job | Jobs the carrier keyed in itself for Leschaco to confirm, then jobs Leschaco offered to accept or decline — every job keyed in SCMOS with this carrier on it | `/api/carrier/job-requests`, `/api/carrier` |
| Workspace → My job | The department's Operation Workspace grid (MY JOBS, PENDING, COMPLETED), over the carrier's own jobs; only the field cells can be edited | `/api/carrier/jobs` |
| Workspace → Postpone | Its jobs cancelled, or moved from the date first planned (the Workspace's own CANCEL/MOVED rule); a cancelled job is shown, not worked | `/api/carrier` |
| Capacity | Trucks free and already promised per day and vehicle, which it records itself (saying a day again corrects it); beside each, its own Leschaco jobs for that day — not the department's demand | `/api/carrier/capacity` |
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

## The Dashboard: the department's, for one company

The carrier's Dashboard is the same component the department uses (`Dashboard` →
`ControlTower`), in carrier mode:

- The jobs come from `/api/carrier/dashboard/jobs`: the carrier's own rows
  (as its portal counts them), cut to the fields the dashboard counts by
  (`CarrierService.DashboardFields`). The operators' remarks and edit history
  are not sent. The browser prepares them exactly as it prepares the register.
- The measures come from `/api/carrier/dashboard/measures`: the KPI engine
  under a TRUCKER scope of the carrier's names, **then cut again**.
  - The engine's scorecard adds any carrier an issue names without a job, so a
    scoped report can still carry another carrier. Supplier performance, which
    averages and lists the scorecard, is rebuilt from the carrier's own line,
    trend included.
  - The suppliers list and scorecard keep only the carrier.
  - The department's issue counts and "cases outside the filter" note are removed.

## My job: the department's Workspace, over its own jobs

My job is the grid the department works in: the same columns, tabs (MY JOBS,
PENDING, COMPLETED), search, saved views, Excel export and undo. What differs:

- The register comes from `/api/carrier/jobs`, and the refresh from
  `/api/carrier/jobs/since`. It holds the carrier's own jobs only, cut to the
  grid's fields (`CarrierRegisterService.Fields`). Remarks, edit history, CS and
  move and incident notes are not sent. MY JOBS lists every job of the company,
  not one operator's.
- Saving goes to `PUT /api/carrier/jobs`. The server takes only the field
  cells: licence, driver, contact, container, seal, arrival date and arrival time
  (`CarrierRegisterService.Editable`). The web's `CARRIER_EDITABLE` must match it,
  and a test pins that. Any other field sent is ignored, so customer, carrier and
  price stay as the department keyed them. A job that is not the carrier's
  refuses the whole save (403).
- A plate, phone, date or time is checked before anything is written. A bad
  value refuses the save. TMS only fills empty cells; here the carrier may
  overwrite its own field cells. Each change is in `audit_events` with the
  account and the reason **ผู้ขนส่งแก้ในตาราง My job**.
- Status: the dropdown offers only the carrier's own steps after the job's
  current status (dispatched, picked up, loading, in transit, delivered,
  container returned, completed). The server moves the job through the
  carrier's status step, as the schedule's buttons did. A completed or cancelled
  job is read only.
- A carrier cannot add, delete, bulk-set status or owner, postpone or cancel.
  The drawer also has no issue button for a carrier. All of these stay with the
  department.

Postpone still shows the schedule's own list.

## NEW job first, then My job — and back in SCMOS

Asked for on 30 Sep 2026: a job created in SCMOS for a carrier shows in that
carrier's NEW job, and moves to its My job only after it accepts.
`RegisterCarrierFollower` runs after every save of the department's register
(`PUT /api/jobs`). It reads what the save changed about each job's carrier and
status; `JobsRepository.LastChanges` is read in the same round trip, before
the write.

- A registered carrier named on an open job (keyed with it, or named later) is
  asked: a pending `supplier_requests` row, like the workflow's "ขอรถ". Its
  TMS webhook hears of it.
- Another carrier named instead: the open ask is closed (`superseded`,
  `REGISTER_CARRIER_CHANGED`) and the new carrier is asked. The name taken off:
  the ask is closed. The same company under another spelling asks nobody again.
- A job with no ask history, keyed before this, stays in the carrier's My job
  by name, as before. A closed job is never offered.
- Back in SCMOS:
  - A job asked about reads **WAITING_SUPPLIER** when it was not that far yet.
  - The carrier's accept moves it to **SUPPLIER_CONFIRMED**. A job the
    department already moved past that keeps its status.
  - A refusal raises the bell's **ผู้ขนส่งไม่รับงาน** until another carrier is
    named.
  - A job the carrier keyed itself (a carrier job request) is confirmed when
    Leschaco approves it, and is not asked again.
- The job's owner (or a delegate, or anyone who may edit any job) may accept
  for the carrier when it said yes outside SCMOS. The button is
  **รับงานแทน {carrier}** in the job's drawer (`GET /api/jobs/{key}/carrier-ask`,
  `POST /api/jobs/{key}/carrier-ask/accept`).
  - It goes through the carrier's own acceptance, so the job moves to the
    carrier's My job and reads SUPPLIER_CONFIRMED.
  - The ask is marked `ACCEPTED_BY_OWNER`. Acceptance measures leave it out,
    like a binding, since it is not the carrier's answer in SCMOS.
  - Setting the status in the grid is not an acceptance: the job waits in the
    carrier's NEW job until the carrier or the owner accepts it.

## Every COMPLETED job has a Billing Case

The department decided on 30 Sep 2026: a job closed COMPLETED opens its
carrier's Billing Case, whoever closed it. That covers all such jobs,
including those closed before, and the due date runs from the real delivery.

- On save, `PUT /api/jobs` opens the case for a job it moves to COMPLETED.
  The carrier's own My job opens it through its status step.
- `BillingCaseSweep` runs every `Billing:SweepMinutes` (30, 0 = off; the first
  run is 4 minutes after start). It catches every other road to COMPLETED (LINE,
  TMS, corrections, imports). Its first runs bill the jobs closed before.
- A case needs a confirmed assignment. When the carrier holds the job without
  one (keyed before asks existed, or closed before it answered),
  `CarrierBillingService` binds it once. The binding is confirmed, with reason
  `REGISTER_BINDING` and no answer time. The KPI engine and the billing control
  tower leave these rows out, since they are not the carrier's answer.
- Due date: the job's arrival date and time (Thai time), otherwise its plan
  date and time, never later than now. Old jobs therefore open already overdue,
  as decided.
- A job with a Billing Case is never deleted. Deleting it by key is refused
  (409, "ยกเลิกงานแทน"). Clearing the register or a person's jobs keeps it and
  says how many were kept.

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
- `/api/capacity` (v2.8.1): every carrier's fleet; its POST took the supplier
  from the request under `EditOwnJobs`, which a carrier holds, so a carrier
  could have written another's capacity;
- the bell: "on-time below target" over every carrier's jobs.

None of these is a carrier's screen, so `Rules/CarrierBoundary.cs` now answers
a carrier 403 on the first five. The bell answers a carrier with nothing. The
rate book (`/api/rates`) already refused carriers and still does.

**Since 30 Sep 2026 this is an allow-list.** A carrier's account reaches only:
- `/api/carrier…`: its own routes, and the TMS API;
- `GET /api/me`, the bell (`/api/notifications`, empty for a carrier), and
  `GET /api/vehicle-types`;
- its jobs' documents: list, upload, and open a file, each checked by
  `CarrierDocumentAccess`;
- the billing routes its Billing screen uses.

Every other `/api` route answers it 403, with nothing to list. That includes
the LINE queue and the corrections queue, which answered a carrier before.
A new carrier screen that needs another route adds it to
`CarrierBoundary.Allowed`, with the reason.

## Verified

- `tests/Scmos.Ai.Checks` (`CarrierPortalChecks`): two carriers in one database.
  Each sees only its own lanes, KPI, Postpone list and requests. The request
  flow covers approve, refuse, withdraw, stale revision and audit. The boundary
  covers which paths it refuses.
  My job covers: the read and the delta cut to its own rows, another carrier's
  job refused, only field cells landing, the audit, and a bad plate, phone,
  status or closed job refused. It also covers a status moved along the ladder.
  The register checks cover:
  - a keyed job in NEW job and not My job, and WAITING_SUPPLIER;
  - accept to My job and SUPPLIER_CONFIRMED, and a later status kept;
  - a re-spelling and a legacy job untouched;
  - a carrier swapped and one removed;
  - a refusal on the bell until the next carrier;
  - a carrier's own request confirmed on approval;
  - a status set in the grid not accepting, and the owner accepting for the carrier;
  - a case on COMPLETED dated by arrival;
  - a legacy job completed in the carrier's My job, bound and billed;
  - the sweep paged and run once;
  - billed jobs kept from delete and clear.
- Web: `tests/carrierPortal.test.mjs` (menu, reads, parsing, the request
  opened as the add-job form, My job's editable cells and status choices against
  the server's).
- Clicked through against a scratch database with the demo Subcontractor and
  Admin accounts: every menu entry; a request keyed, opened as the form, saved
  and approved, and the carrier seeing the job and the approval; a refusal; the
  carrier's bell empty; phone width without sideways scroll. My job: the grid
  listing the carrier's five jobs; only the field cells and status opening; a
  plate and driver saved with the audit; the status list offering only the
  carrier's steps; DISPATCHED saved as a carrier status event.
