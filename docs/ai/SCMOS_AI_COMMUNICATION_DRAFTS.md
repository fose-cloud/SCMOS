# Communication Agent drafts (AI Agent Platform, step 12, internal only) — spec §30

Built 28 Sep 2026 on the rule-first pass (`SCMOS_AI_OTD_VALIDATION_AGENTS.md`).
The Phase 4 `communication-agent` (chat: reads what carriers said) gains the
platform spec's **template-first communication**. **No model, and nothing is
sent.** LINE has been inbound-only since v2.7.54 and whether reminders may go
out at all is the department's open decision (confirmation 4). So the agent
drafts, and the job's owner sends the message themselves and records it.

## When a draft is written

| Template | Raised when | Rule it reuses |
| --- | --- | --- |
| `CARRIER_CONFIRMATION_REMINDER` | a carrier request is still pending after `AI__CarrierReminderMinutes` (60), job planned from yesterday on | `supplier_requests` pending (AP-04: one per job) |
| `TRUCK_DETAIL_REMINDER` | carrier on the job, no plate or no driver, plan today to +2 days | the bell's `Notifications.MissingBookingData` + the monitor's window |
| `POD_REMINDER` | job done (arrival, else plan day) yesterday to `AI__PodReminderDays` (14) ago, carrier named, no file in the POD folder | the bell's *POD missing*, `DocumentChecklist` POD, `DocumentsReadService.DoneOn` |

**No draft** when the carrier has already written about the job (a LINE or TMS
message waiting for a person, `line_events` NEED_REVIEW), or for a cancelled
job. There is at most one draft per job at a time, in the order above.

The spec's other three examples are not written. `DELAY_INTERNAL_ALERT` and
`DOCUMENT_MISSING` are staff-facing, and the bell already raises both
(*Truck delay*, *POD missing*). The bell is computed from current state on
purpose and its alert list is fixed, so a second copy would be noise.
`BOOKING_REQUEST` belongs to the Booking Agent (next step).

## Templates (`Ai/Communication/CommunicationTemplates.cs`)

Fixed Thai text with `{variable}` holes. Only the template's approved variables
may fill it; anything else is refused as a programming error. A value becomes
one line, braces are removed and it is cut to 60 characters. A missing value
reads "—" so the gap shows in the draft. There is no AI wording layer.

## What is recorded (AP-02: attributable, traceable, auditable)

Each draft is a row in `ai_decisions` (`decision_type = communication_draft`).
No migration was needed.

| Spec field | Where |
| --- | --- |
| CommunicationId | `ai_decisions.id` |
| JobId | `entity_id` |
| Recipient | rule reference `Recipient:carrier:{KEY}` |
| Channel | `Channel:MANUAL` (a person sends it); the channel used is the answer's `human_choice` (LINE / โทรศัพท์ / อีเมล) |
| TemplateCode | `Template:{CODE}`; the message is the recommendation, sourced `template:{CODE}` |
| Trigger | `Trigger:supplier_request:{id}` / `Trigger:plan:{day}` / `Trigger:done:{day}`, plus the facts and rule result with their sources |
| CreatedByAgent | `agent_id = communication-agent` |
| ExecutionId | none: no model run (`run_id` empty), like the other rule-first agents |
| Timestamp | `created_at`; `decided_at` for the answer |
| Delivery status | OPEN = drafted, not sent · ACCEPTED = sent by the person (channel in `human_choice`) · OVERRIDDEN = sent something else (what and why) · DISMISSED = not sent · RESOLVED = no longer needed (the carrier answered, the truck was named, the POD arrived) |

Every answer goes through `AiDecisionLog.AnswerAsync`: the owner or a
supervisor, audited in `audit_events`.

**Duplicate protection.** The fingerprint is the template, carrier and trigger,
never the minutes waited. So the same draft is never written twice, a draft a
person answered is not raised again for the same trigger, and a new request is
a new trigger.

## Screen (AI Control Tower → งานที่ AI ตรวจพบ → ข้อความ)

The draft shows its facts and rule, then **ร่างข้อความ** with **คัดลอกข้อความ**.
If the browser refuses the clipboard (a policy or an embedded view), the text is
selected for Ctrl+C instead. The answers are **ส่งแล้ว** with the channel,
**ส่งข้อความอื่น** (what was sent and why), and **ไม่ส่ง**. A draft has no risk
level, so no risk badge is drawn.

## Switching it on

The agent's own flag (`AI__CommunicationAgentEnabled`) is already on for its chat
read, so the drafts have **their own switch**: `AI__CommunicationDraftsEnabled`
(default off). With `AI__Enabled`, both flags and the agent ACTIVE in the
governance section, the next pass (every `AI__AgentScanMinutes`) drafts.
`AI__CarrierReminderMinutes` (5–1440) and `AI__PodReminderDays` (1–60) are
configuration until the department sets the confirmation SLA.

Sending for real would need confirmation 4, a channel decision, the Execute
autonomy level (above the agent's L2 ceiling today) and shadow comparison first.
None of that is built.

## Verification (28 Sep 2026, local only)

- `tests/Scmos.Ai.Checks`: 1,149 without a database, 1,309 with
  `--write-local-db --local-db` (16 draft checks; on the scratch database:
  own switch, three drafts, sent recorded and not re-drafted, draft resolved
  when the POD arrives).
- API rule checks 35/35; web `npm test` 692; tsc; eslint.
- Live on a scratch LocalDB: scan → three drafts. In the Control Tower: filter
  **ข้อความ 3**, copy fell back to selecting the text in the embedded browser,
  **ส่งแล้ว** by โทรศัพท์ → ACCEPTED, choice recorded, `audit_events` row;
  rescan unchanged. Scratch database dropped.
