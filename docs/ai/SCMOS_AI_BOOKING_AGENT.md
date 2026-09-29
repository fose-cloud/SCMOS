# Booking Agent (AI Agent Platform, step 13) — spec §27

Built 28–29 Sep 2026. The department chose both sources (pasted text and
unplaced Outlook mail) and a model that proposes each field with verified
quotes. **The model proposes; SCMOS keeps a field only when the words it cites
are really in the text and really say that value.** Nothing is created: a
person saves the job through the existing add-job form, so the existing
booking path is untouched and there is no second booking system.

## What it reads

| Source | Where | Switch |
| --- | --- | --- |
| Text pasted into the add-job form (a booking request copied from mail or LINE) | **อ่านข้อความ** under the document reader in the add-job form | `AI__BookingAgentEnabled` |
| Mail the matcher left unplaced (no confirmed or suggested link), received within `AI__BookingMailHours` (48), processed | the scheduled pass, every `AI__AgentScanMinutes`; drafts under **Booking** in "งานที่ AI ตรวจพบ" | `AI__BookingMailEnabled` in addition |

Both need `AI__Enabled` and the OpenAI key the document reader already uses.
The mail pass reads at most `AI__BookingMailPerPass` (10) messages per pass,
oldest first. Each message is read once: a booking becomes an open draft, and
anything else is recorded as read (`not_booking`, closed), so it is never read
or paid for twice. A provider failure ends the pass and the message waits.

## The check (`Ai/Booking/BookingVerification.cs`)

The fields are the add-job form's own, less the carrier's side (who hauls it,
the truck and the driver), which a booking request does not carry.

| Kind | Kept when |
| --- | --- |
| every field | its quote appears verbatim in the text (spacing and case aside) |
| date, closing date | the quote names that very day: 29/09/2026, 29-9-69 (Buddhist year), 2026-09-29, 29 Sep 2026, 29 ก.ย. 69, or today / tomorrow / พรุ่งนี้ / มะรืน against the day received. A date with no year takes the received year only within a week before to four months after |
| time, closing time | the quote says it: 08:30, 8.30, 8am/2.30pm, 0800 น., 10 น., 8 โมงเช้า, บ่าย 2, 2 ทุ่ม, เที่ยง. A bare "5 โมง" is ambiguous and is not read |
| weight, kgs, pallet, zip | the number is in the quote, thousands separators aside. Units are never converted (20 ตัน is not 20000) |
| text (destination, plant, container, booking, …) | the value's letters, digits and Thai marks are in the quote's |
| customer | also a customer the register already holds (the form's own list), entered in the register's spelling |
| type | also lands on the department's vehicle list (`JobVehicleType.Canonical`), entered as the list spells it |

Anything else is **not accepted** and is shown with its reason: the quote isn't
in the text, the value isn't what the quote says, the customer isn't in the
register, or the type isn't on the list. The essential fields (import: customer,
date, type, destination; export: customer, date, type, plant; delivery: customer,
date, warehouse) make the draft COMPLETE or NEEDS_INFORMATION.

## Screens

- **Add-job form.** Paste, then **อ่านข้อความ**. Accepted fields fill the form and
  are marked AI, with a line each showing the value and its quote. Refused fields
  are listed with the reason, and missing essentials as "ยังไม่มี". The person
  checks and saves as always.
- **AI Control Tower → Booking.** A mail draft shows the mail (subject, sender,
  received), the draft's fields with their quotes, and what was refused.
  **เปิดฟอร์มเพิ่มงาน** opens the add-job form pre-filled. **Saving that job
  answers the draft** (ACCEPTED, `human_choice` = the new job's key). Otherwise
  the options are **สร้างงานแล้ว** or **ไม่ใช่ booking**. When a person links the
  mail to a job in the mail screen, the pass resolves the draft with that job.

Drafts are about a mail, not a job, so they have no owner. Accounts that see
the team (supervisors and above) see them.

## Governance and audit

- `booking-agent`: capability `EditOwnJobs` (who may add jobs), autonomy L2 at
  most, shadow by default, no chat tools or pages. Its read, `draft_booking`, is
  a dedicated endpoint like `extract_document`.
- Every read is a run in `ai_audit_logs` (tool `draft_booking`, view = the category
  for a paste or `mail`), committed before anything is released. The only evidence
  key is `paste:1` or `mail:{id}`; no text, value or quote is written to the audit.
- The mail pass has no person behind it. The audit accepts exactly one non-person
  identity, `system:agent-pass` / `System`, and only for this agent, this tool and
  the `mail` view. No account can hold the System role, and the id is refused
  under any other role.
- Mail drafts live in `ai_decisions` (`entity_type = email`, `decision_type =
  booking_draft` or `not_booking`). The draft names the model run that read it
  (`run_id`). Each accepted field is a fact sourced `email:{id}.text#{field}`, its
  quote an observation sourced `quote#{field}`, and the category the rule
  reference `Category:…`. No migration: `email` joined the contract's entity types.
- The text is untrusted data. The prompt says so, the answer is a strict JSON
  schema, and anything the text did not say cannot pass verification, so an
  instruction hidden in a mail can at most fill a field with words that are in
  the mail, for a person to review.

## Not built

- Attachments of unplaced mail (booking PDFs). The existing document reader in
  the form reads those on the person's click.
- The Customer Requirement master (confirmation 2), which the RequirementRisk
  agent needs, not this one.

## Verification (28–29 Sep 2026, local only)

- `tests/Scmos.Ai.Checks`: 1,196 without a database, 1,364 with
  `--write-local-db --local-db`. The new booking checks cover the verification
  cases (dates, times, numbers, names, customer, type), the reader's JSON, the
  audit's one system identity, the paste read under flag, governance, limiter
  and audit, and the mail pass on a scratch database (drafts, not-bookings read
  once, linked/old/unprocessed skipped, never re-read, resolved on link, per-pass
  cap, busy provider, no model).
- API rule checks 35/35; web `npm test` 700; tsc; eslint.
- Live on a scratch database, with no model key locally: the scan reports
  `not_configured`, the paste route answers "not configured", and a request
  without the control header is refused. A seeded mail draft showed under
  **Booking** with its fields, quotes and refusals, and with the
  **เปิดฟอร์มเพิ่มงาน / สร้างงานแล้ว / ไม่ใช่ booking** buttons.
  **เปิดฟอร์มเพิ่มงาน** opened the add-job form with date, customer, destination,
  time, type (1X40'HQ, on the list) and container filled and marked AI. Saving
  with a carrier created the job, and the draft was answered ACCEPTED with the
  new job's key, audited.
- Two faults found by that run, both fixed: (1) opened from the Control Tower,
  the form did not appear, because the register loads only on screens that need
  it, and an open add-job form now counts; (2) the draft was answered before the
  job's save had finished, and is now answered only after the save succeeds
  (confirmed live: job written 01:44:28, draft answered 01:45:06).
