# SCMOS AI platform — Phase 3 record: the Operations extension

Implemented 21 September 2026 · released as v2.7.62 · as the [plan](SCMOS_AI_IMPLEMENTATION_PLAN.md) §2 scoped it: "preserve existing active Import/Export behavior; add precisely named missing-ETA/truck queries, evidence-driven recommendations; match existing operational rules; category/history scope explicit; no status/assignment writes".

## PHASE COMPLETED — 3

**What it answers.** "งานไหนยังไม่มีรถหรือคนขับ", "งานไหนยังไม่มีผู้ขนส่ง", "งานวันนี้ที่เลยเวลาแล้วยังเงียบ", "เลขตู้ไหนไม่ตรงมาตรฐาน": one new read tool, `query_followup`, with four precisely named views, each the department's **existing** rule and nothing new:

| View | The rule it is | Window |
| --- | --- | --- |
| `missing_truck` | the bell's `Notifications.MissingBookingData` — a carrier named, plate or driver blank — on the Monitor's horizon, not yet arrived | overdue through the next 2 days |
| `no_carrier` | the bell's `Notifications.NeedsCarrier` on the same horizon, not yet arrived | overdue through the next 2 days |
| `unreported` | the LINE chase's `LineChase.Reported` negated: today's job, plan time passed by the read's own clock, status still before DISPATCHED and no arrival cell | scheduled today, plan time passed |
| `container_mismatch` | the bell's `Notifications.ContainerWillNotMatch` (4 letters + 7 digits) | all active dates |

Every row is the same `OperationEvidence` as the other views (job, customer, carrier, date, plan time, status, has-owner/driver/plate/arrival flags — never a driver's name, a phone, a note) with an explanation that says exactly what is missing or how long past the plan time ("เลยเวลาแผน 08:00 มา 60 นาที …"), and a suggested follow-up ("ขอทะเบียนรถและชื่อคนขับจากผู้ขนส่ง", "ติดต่อผู้ขนส่ง หรือส่งต่อรายถัดไปตามลำดับ", "ติดตามผู้ขนส่ง — ถามสถานะรถและเวลาถึง", "ตรวจเลขตู้กับ booking ก่อนรถถึงหน้าท่า"). The answer's `window` names the scope; the Control Tower shows it in words. No risk score is invented; the Monitor's flag rides along where it exists.

**What "ETA" means here.** SCMOS holds no ETA cell; a driver's "ประมาณ 10.00" in LINE is shown to the owner and never written. So "missing ETA" is, in the register's own terms, *the plan time has passed and the register is silent* — the same test the LINE chase used to decide whether to ask. That is the `unreported` view.

**Files modified** (no file created but the checks' additions and this record)

- `server/Scmos.Api/Rules/Notifications.cs`, `Rules/LineChase.cs` — the three bell rules and `Reported` gain cell-level overloads; the record versions delegate, so each rule is still written once
- `server/Scmos.Api/Ai/Operations/OperationsReadService.cs` — `FollowUpTool`, `FollowUpViews`, the four views, their explanations and follow-ups, the `unreported` clock; `Window` per view
- `server/Scmos.Api/Ai/ToolRegistry.cs` — `query_followup` (schema `view` enum + `limit`, ≤ 50 rows, same policy as the other Operations reads)
- `server/Scmos.Api/Ai/AgentRegistry.cs` — the Operations agent may use it; `Rules/AiPermissions.cs` — catalogue entry (Allow)
- `server/Scmos.Api/Ai/AiAuditRules.cs` — `query_followup` and its four views in the vocabulary, exclusive to it
- `server/Scmos.Api/Ai/Operations/OperationsAgent.cs` — the model is told what each view is for; the answer's label per view
- `app/scmos/screens/AiControlTower.tsx` — three prompts; `app/scmos/aiControl.ts` — `WINDOW_LABEL`
- `tests/Scmos.Ai.Checks/OperationsChecks.cs` (fixtures gain `planTime` and `container`; ten follow-up rows), `SharedContractChecks.cs`

**Existing components reused** — `OperationsReadService` (the same scan, scope checks, evidence shape, redaction and sort), `Notifications` (the bell's three rules), `LineChase.Reported` (the chase's rule), `MonitorRules.SoonDays` (the horizon), `Formats.Moment` / `Formats.Zone` (the plan moment in Bangkok), `QueryPolicyGuard`, `ToolExecutor`, the audit (the tool is one more name in the vocabulary), the Control Tower's evidence card unchanged.

**APIs added** — none. `POST /api/ai/chat` to the Operations agent may now select `query_followup`; the reply shape is unchanged.

**Tools added** — `query_followup`. **Agents added** — none.

**Database changes** — none. **Migration status** — none needed (the `ai_tools` roster row comes with the next `--seed-suppliers`; the matrix shows the catalogue entry meanwhile).

**Security changes** — none. The same scope (server identity), the same capability (`ViewDashboard`), the same redaction, the same one-read budget, the same audit. Nothing writes a status or an assignment.

**Permissions added** — none.

**Tests added** — 14 checks labelled "3:" in `OperationsChecks` (each view's set against the fixtures, including what each excludes: arrived, far, done, cancelled, left, due later, tomorrow, no plan time; the wording; the schema's four views; a foreign view refused; the guard for Operations only; the audit vocabulary both ways; redaction) plus four adjusted contract checks. Offline total 509 (was 491); with `--write-local-db --local-db` 623. Unchanged: 580 Node, `--check-capability`, `--check-line`, `--check-monitor`; `-warnaserror`; tsc; eslint.

**Build result** — green at the Phase 3 commit.

**Known risks**

- `unreported` judges by the plan time at the moment of the read; a job whose plan time was keyed wrongly is "unreported" from the wrong minute. The row shows the plan time so the reader can tell.
- `missing_truck` and `no_carrier` share the bell's horizon (2 days) rather than the whole register — a carrier missing on a job three weeks out is not a follow-up yet, as the bell has always said.
- The model chooses the view; a question that fits none yields a clarification, as before.

**Remaining tasks** — Phase 4 (Communication over the mail/LINE ledgers), 5 (Document & Invoice), 6–7 (Engineering, SRE — need infrastructure decisions), 8 (collaboration), 9 (hardening).

**Recommended next phase** — 4.
