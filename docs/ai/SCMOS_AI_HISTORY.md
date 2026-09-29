# AI history search (AI Agent Platform, step 15) — spec §47

Built 29 Sep 2026. The spec asks for a searchable history of what the AI did:
filter by date, agent, action, job, customer, carrier, user, result and
approval status; show why it acted, the evidence, the rule, the tool and the
result. SCMOS already recorded both halves, so this only makes them searchable.
Read only; no new table.

## ประวัติ AI (the agents' decisions, every status)

A new section in the AI Control Tower, for every account that sees the findings.
It opens on the latest decisions of **every** status: open, answered, superseded
and resolved.

| Filter | Matched against |
| --- | --- |
| ตั้งแต่ / ถึงวันที่ | when the decision was made, Bangkok days (at most a year) |
| Agent | the rule-first agents and the Booking Agent |
| ประเภท (the spec's "Action") | the decision's kind: delay risk, validation, carrier candidates, message draft, booking draft, not a booking |
| ผล (the spec's "Result") | OPEN / ACCEPTED / OVERRIDDEN / DISMISSED / SUPERSEDED / RESOLVED |
| งาน / ข้อความ | words in the summary, or the job key |
| ลูกค้า / ผู้ขนส่ง | the customer and the carrier **of the job the decision is about**, read from the register row |
| ผู้ตอบ | who answered it, or what they chose |

Opening a row shows why the agent concluded it (facts, rule results,
inferences), what it recommended, and what it rested on (evidence). A job opens
from there. A decision a model run made (a Booking draft) has
**ดูรอบการทำงาน**, which jumps to that run in AI Activity. The list keeps its own
scope: the team's for a role that sees the team, otherwise the person's own jobs.

`GET /api/ai/decisions` takes `from`, `to` (yyyy-MM-dd), `type`, `q`,
`customer`, `carrier` and `decidedBy` beside its existing filters. Bad dates,
a range that ends before it starts or runs past a year, and words over 80
characters or with control characters answer 400.

## AI Activity (the model runs), now searchable

A search form above the runs (ViewAudit, as before):

| Filter | Matched against |
| --- | --- |
| dates | the run's start, Bangkok days |
| Agent / เครื่องมือ | the audit's own vocabulary only (the tool is the spec's "Tool used") |
| ผล | how the run ended; **incomplete** is a run that never recorded its end |
| ผู้เรียก | the caller's id or part of it; the Booking mail pass is `system:agent-pass` |
| หลักฐาน | an evidence key its last step returned, matched whole (a job key, `mail:41`, `paste:1`) |

`GET /api/ai/audit` takes `agent`, `tool`, `result`, `user`, `from`, `to` and
`key`. An agent, tool or result outside the audit's vocabulary answers 400, and
a key cannot contain a quotation mark (it is matched inside the stored JSON).

"Approval status" is on each run's detail as before. The AI approvals queue
keeps its own section.

## Verification (29 Sep 2026, local only)

- `tests/Scmos.Ai.Checks`: 1,199 without a database, 1,379 with it. On a scratch
  database: decisions found by action, result (closed ones too), customer and
  carrier via the job, who answered, words and job key, and day. Runs found by
  tool, how they ended (incomplete), whole evidence key, user, agent and day; an
  unknown tool refused.
- API rule checks 35/35; web `npm test` 709; tsc; eslint.
- Live on a scratch database with 5 decisions and one audited mail-pass run:
  - The history listed all 5 with their results.
  - Customer "EVONIK" through the form found the one decision on that customer's job.
  - Opening the Booking draft and pressing **ดูรอบการทำงาน** opened its run in
    AI Activity (system:agent-pass · System, draft_booking · mail).
  - In the Activity search, key `mail:99` found nothing and `mail:41` found the run.
  - Scratch database dropped.
