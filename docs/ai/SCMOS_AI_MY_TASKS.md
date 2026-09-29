# My AI Tasks and the Control Tower cards (AI Agent Platform, step 14) — spec §43, §44

Built 29 Sep 2026. The agents' findings were one list ordered by risk. It is now
a person's own queue, with summary cards above it, so nobody has to open every
normal job to find the few that need them (§44's goal). Read only; no new table.

## Cards (`GET /api/ai/tasks`, `Ai/AiTasks.cs`)

Counted on the server, in the decision list's own scope: the team's for a role
that sees the team, otherwise the person's own jobs. A card therefore never
counts what its list would not show.

| Card | Counts |
| --- | --- |
| งานที่ AI ดูอยู่ | open jobs in the agents' window (planned yesterday to +7 days) |
| รายการที่ AI พบ | open decisions |
| รอฉันตอบ | open decisions this person may answer (the job's owner, or any open one for a supervisor or above) |
| เสี่ยงสูง | HIGH or CRITICAL |
| รออนุมัติ | AI proposals pending an approver: for approvers only, nobody else decides them |
| ผู้ขนส่งต้องตาม | the Carrier Agent's "no eligible carrier", plus the Communication Agent's confirmation reminders |
| ข้อมูลไม่ครบ | INSUFFICIENT_INFORMATION (Validation's gaps, Booking drafts missing essentials) |
| Agent ผิดปกติ | agents whose circuit breaker is not closed |

The spec's "Carrier Timeout" is covered by ผู้ขนส่งต้องตาม: the confirmation
reminder is raised exactly when a request has waited past `AI__CarrierReminderMinutes`.

## ของฉัน (My AI Tasks)

The panel now opens on **ของฉัน**: only the decisions this person may answer,
in the spec's sections, most urgent first. Each decision is in exactly one
section: **AI ถูกบล็อก**, then **เสี่ยงสูง**, then **ผู้ขนส่งต้องตาม**, then
**ต้องการข้อมูล**, then **รอฉันตัดสินใจ**. A finding that is both high risk and a
carrier escalation (a HIGH "no eligible carrier") sits under เสี่ยงสูง, while the
ผู้ขนส่งต้องตาม card still counts it. Cards count by kind; sections place each item
once. "Needs my approval" is the approvals queue further down the Control Tower,
counted on its card.

**ทั้งหมด** and the per-agent filters show everything the person may see, as before.

## Who may answer

Each decision in `GET /api/ai/decisions` now carries `canAnswer`: the server's own
`AiDecisionLog.CanAnswer` for the reader. A decision they may not answer is shown
with no buttons. Before this, an Operation user (who sees the team through the
base read grant) got buttons on colleagues' jobs, and the server refused them.

The section rule is written twice: `AiTasksService.SectionOf` on the server,
`sectionOf` in `app/scmos/aiTasks.ts` on the screen. Both are checked against the
same cases (AgentScanChecks "tasks:", `tests/aiTasks.test.mjs`) so they cannot
drift.

## Verification (29 Sep 2026, local only)

- `tests/Scmos.Ai.Checks`: 1,197 without a database, 1,369 with it (the team's,
  an operator's, a colleague's and a dashboard-only account's counts; `canAnswer`
  per reader); API rule checks 35/35; web `npm test` 705; tsc; eslint.
- Live on a scratch database, as an Operation user owning four of five findings.
  Cards: 3 / 5 / 4 / 2 / 0 / 1 / 1 / 0. ของฉัน 4 in เสี่ยงสูง 2, ต้องการข้อมูล 1,
  รอฉันตัดสินใจ 1. Under ทั้งหมด the colleague's job had no buttons. Dismissing
  one refreshed the cards (5→4, 4→3, ข้อมูลไม่ครบ 1→0). Scratch database dropped.
