# OTD Agent and Validation Agent (AI Agent Platform, steps 8–10)

Built 28 Sep 2026 on the foundation (`SCMOS_AI_AGENT_PLATFORM_FOUNDATION.md`).
Both are **rule first with no model**: every figure is one SCMOS already
computes, every threshold one it already keeps (or configuration). What they
conclude goes to the decision log; people answer it in the AI Control Tower.
They start **off** (their own flags) and, when on, **in shadow**: they
recommend, a person acts. Nothing they conclude writes to a job.

## OTD Agent (`otd-agent`, `Ai/Otd/OtdAgent.cs`) — spec §31

Looks at a job while there is still something to do: not finished, not
cancelled, no arrival recorded, planned from **yesterday** to the monitor's
window ahead (`MonitorRules.SoonDays`, 2 days). Older jobs with no arrival are a
gap in the record, not today's risk.

| Level | Rule | Source of the threshold |
| --- | --- | --- |
| CRITICAL | past the on-time window (plan + grace) with no arrival | grace from `CustomerTerms` (30 min; EVONIK tank 180) |
| CRITICAL | the monitor's *Overdue* (plan day passed, no arrival) when there is no plan time | `MonitorRules.Judge` |
| HIGH | past plan, still inside the grace | `CustomerTerms` |
| HIGH | plan within `AI__OtdHighMinutes` (60) and no truck or driver | configuration |
| HIGH | the monitor's *Unassigned* or *NoCarrier* | `MonitorRules.Judge` |
| WATCH | plan within `AI__OtdWatchMinutes` (120) and the status not yet running | configuration + `JobRules.IsRunning` |
| WATCH | the monitor's *NoTruck* | `MonitorRules.Judge` |

The monitor's rule is called, not copied — one rule, read in two places.
There is no GPS or ETA feed: an arrival is what LINE, the TMS or the grid
recorded. Each result keeps the plan, status, carrier, truck, arrival and the
clock as facts with their sources; the window and what fired as rule results;
"may be late" as an inference; what to do as a recommendation.

## Validation Agent (`validation-agent`, `Ai/Validation/ValidationAgent.cs`) — spec §28

The register's own checks on the work of the coming week (yesterday to +7 days),
unfinished and not cancelled:

| Finding | Outcome | Rule |
| --- | --- | --- |
| a value present but unreadable (date, time, container, weight, phone, plate, a status off the ladder) | NEEDS_INFORMATION (error) / WARNING | `JobRules.Validate` |
| a vehicle type not on the department's list | WARNING | `JobVehicleType.IsKnown` |
| an export truck due after the yard closes | WARNING | `JobRules.GateInRisk` |
| no container, or on an export no seal, inside the monitor's window | NEEDS_INFORMATION | `JobRules.Gaps` |

PASS writes nothing. **BLOCKED is not used**: none of these rules blocks
continuation today, so none is claimed. A missing truck or driver is the OTD
Agent's finding, not raised twice.

## The pass and the log (`Ai/AgentScanner.cs`)

`AgentScanScheduler` runs both every `AI__AgentScanMinutes` (15; 0 = never),
four minutes after start. Each agent first asks governance for *Recommend*: its
flag and `AI__Enabled`, the platform switch, its autonomy, status and breaker.
Refused, it writes nothing and closes nothing. It reads the register fresh (a
background pass can wait; it does not take the screens' stale answer).

A finding is identified by a fingerprint of what fired — kind, outcome, risk,
rule codes — never the minutes, which move every pass. So:

- the same finding again → left as it is (no flood);
- a different finding for the job → the old one **SUPERSEDED**, the new one open;
- nothing left to say about the job → the open one **RESOLVED** (kept, not deleted);
- a finding a person already answered → not raised again while it is the same.

Decisions carry the job owner's id (who may answer besides a supervisor), the
agent's autonomy and shadow mode at the time, and the prompt version (the
build). No model run is claimed: `run_id` is empty.

Migration `AiDecisionLifecycle`: `ai_decisions.fingerprint` and the two system
statuses. Additive; rollback forward-only like the foundation's.

## Answering (AI Control Tower → งานที่ AI ตรวจพบ)

Everyone with the dashboard sees the open findings they may see (their own jobs,
or the team's when their role sees the team). The owner of the job or a
supervisor answers: **ถูกต้อง** (agreed — a match), **ทำอย่างอื่น** (what was
done instead and why — an override), **ไม่เกี่ยว** (dismissed). Each answer is
audited. These answers are the record the spec asks for before any autonomy is
raised (§16, §61).

## Switching them on

In the Azure Portal (API app settings): `AI__OtdAgentEnabled=true`,
`AI__ValidationAgentEnabled=true` (`AI__Enabled` is already on). Optional:
`AI__AgentScanMinutes`, `AI__OtdWatchMinutes`, `AI__OtdHighMinutes`. An
Administrator can run a pass at once: `POST /api/ai/agents/{otd-agent|validation-agent}/scan`
(control header). Pausing, shadow and the execution switch are on the การกำกับ AI section.

## Tests

`tests/Scmos.Ai.Checks/AgentScanChecks.cs` — every OTD level and exclusion
against a fixed clock (incl. Evonik's 180-minute tank grace and the fingerprint
ignoring the minutes), every validation finding and exclusion, every result held
to the decision contract; with `--write-local-db` the full lifecycle (create,
unchanged, supersede, resolve, answered-not-raised, paused, flag off, AI off).
`tests/aiFindings.test.mjs` — the screen refuses a fact without its source and
keeps the four kinds apart. Verified in the browser on local servers:
passes found exactly the seeded risks, an override and an accept were stored,
audited, and a rescan did not raise them again.

## Not yet

- No model explanation layer (spec: "add AI explanation after the core
  calculations are reliable") — the wording is rule templates.
- Findings are seen in the Control Tower; they are not pushed to the owner (bell,
  mail) yet.
- Carrier eligibility and supplier compliance are not in the Validation Agent;
  the Carrier Agent reads them (`SCMOS_AI_CARRIER_AGENT.md`). Driver training is
  read by no agent yet.
