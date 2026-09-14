# Operations reviewed-change pilot

Local implementation, 14 September 2026. **Not deployed or enabled on Production.**

The user confirmed that Supervisor and higher approve, while Operation Users can
still receive assignments. This increment provides the human review/write boundary;
it accepts a bounded explicit command syntax, not general natural-language inference.
The existing Operations model and its three tools remain read-only.

## Explicit chat-to-draft commands

Example: `เสนอแก้งาน KEY; วันที่ 15/09/2026; เวลา 09:30; เหตุผล ลูกค้าขอเลื่อน`.
Replace KEY with the authoritative job key, not a potentially duplicated job code.
The alias `/แก้งาน` is also supported. Optional fields are วันที่ (dd/MM/yyyy),
เวลา (HH:mm), สถานะ (existing status code), ผู้รับผิดชอบ (active staff ID).
At least one changed field and a reason are required. Duplicate/unknown fields,
multiple jobs, invalid dates/times, disallowed statuses and unavailable assignees
are refused; relative dates and names are not guessed.

POST `/api/ai/operations-changes/interpret` is scoped and read-only: it returns a
validated snapshot/draft, does not call the model, create approvals, or edit jobs.
The UI fills the separate panel; the user must still create the proposal and a
Supervisor-or-higher must explicitly confirm it. Drafting can work while writes
are disabled, but proposing/applying still requires both write gates.

## Implemented

- Control Tower has a separate reviewed-change panel. Read a job by its authoritative
  key, choose changed values and a reason, then create a proposal. The job is untouched.
- Operation Users can propose changes to their own jobs; supervisory roles can propose
  on the team. Delegation-based proposal access is not implemented in this pilot.
- Supervisor, Assistant Manager, Manager and Administrator can review the exact old/new
  values, enter a decision note, and confirm/apply or reject. MFA policy is checked.
  The same supervisor may propose and confirm; this is explicit human confirmation,
  not a separate-person/four-eyes approval rule.
- Both requester and approver are rechecked against active staff records before writes.
  An active internal staff member with EditOwnJobs (including Operation User) may receive
  an assignment; a carrier/inactive/read-only staff member may not.
- All writes require the new default-off `AI:OperationsWritesEnabled` setting **and**
  the durable Operations control enabled. Emergency stop wins. The old reserved
  `WriteToolsEnabled` flag grants nothing. Rejection remains possible while writes are off.
- Proposals expire after 30 minutes. A server-generated fingerprint binds the exact
  job snapshot. Changes committed after proposing cause a stale refusal, not overwrite.
- Confirmation uses one SQL serializable transaction for the job, approval state and
  staged audit events. Audit failure rolls everything back. Repeated/concurrent
  confirmation of the same proposal has at most one effect. No model rerun at approval.
- JSON fields not being edited are preserved, indexed date/status/owner fields are
  synchronized, original date is retained, and the register cache is invalidated after commit.
- Legacy approve/mark-applied endpoints refuse the new versioned proposal agent and
  do not expose its payloads in the old queue. Other legacy approval behavior is unchanged.

## Fields and boundaries

Allowed fields: `date` (DATE, dd/MM/yyyy Gregorian), `planTime` (PLAN LOADING TIME,
HH:mm), controlled `status`, and `opId` (directory-selected assignee). This does not
edit actual arrival/pickup times, drivers, rates, documents or permissions.

Only active Import/Export jobs are in this pilot. Completed/cancelled jobs cannot
be edited. COMPLETED, CANCELLED and BILLING_PENDING targets use the existing workflow.
When a job has workflow events, status changes must also go through that workflow;
date/time/assignment proposals remain possible. No workflow gates are bypassed.

The fingerprint protects this confirmation against prior concurrent commits. It does
not add global optimistic concurrency to the existing My Job whole-row save endpoint.
The queue shows up to the newest 100 eligible proposals; no long-term queue pagination yet.

## Attention list

Manual refresh in Control Tower reads scoped active Import/Export jobs through the
existing Operations source. Window: backlog through today + 2 days, with no recorded
ARRIVAL. It lists existing Monitor risks and separately flags either missing driver
name or missing plate. The original Monitor rule (both missing for NoTruck) is unchanged.
Counts scan the complete permitted result, retaining at most 50 examples. Undated and
malformed rows are counted explicitly. No invented score, external message, background
scheduler, browser push or changes to the global Alerts menu are included.

## Storage and migration

No schema change is necessary for this increment: it reuses `approvals.Payload` for a
server-created version 1 contract (snapshot, fields, requester identity and expiry),
the existing approval state/decision columns, `operation_jobs`, and `audit_events`.
No duplicate table, EF migration, Azure schema change or production migration was run.
`ai_audit_logs` continues to describe read-only model executions; human-confirmed edits
are in the ordinary audit trail with source `ai` and the approval ID.

## Verification

- `dotnet run --project tests/Scmos.Ai.Checks/Scmos.Ai.Checks.csproj -c Release -p:UseAppHost=false -- --write-local-db`:
  **319 checks passed**, including command parsing/read-only drafts and actual SQL commit/rollback, concurrent confirmation,
  replay, stale/expired proposals, role restrictions, legacy bypass refusal and HTTP
  MFA/CSRF/body-size/scope checks. No production connection or live model call.
- The test creates a uniquely named `SCMOS_AI_WRITE_TEST_*` database only on
  `(localdb)\MSSQLLocalDB`, and removes that exact scratch database afterward.
- `npm test`: **490 passed**, including explicit draft validation and existing Operations/UI regressions.
- API Release build and Next production build passed. No real-account browser UAT yet.
- Existing EF1002 warning in `AuditChecks.cs:288` is unrelated to this pilot.
- Lint of the changed UI/proxy files passed. Repository-wide `npm run lint` fails
  with 37 errors in existing `work/SCMOS Dashboard UI Design/*.js` files, outside
  this task; those files were not changed. `git diff --check` passed.

## Before production enablement

Review these pilot limitations; test the new panel with real Operator/Supervisor
accounts in a suitable test environment; check deployed schema compatibility and
release diff; then deploy and explicitly enable the separate write setting. No live
business record was modified as a test. General natural-language proposal inference
is not included; this is not an autonomous write executor.
