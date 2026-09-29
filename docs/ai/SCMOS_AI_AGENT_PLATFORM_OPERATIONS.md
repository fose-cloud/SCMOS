# Running the AI Agent Platform — operations runbook

For whoever turns the agents on, watches them and stops them. Current to
v2.7.99 (29 Sep 2026). The spec's §63 asks for operations, events,
permissions, audit and deployment documents. This page is the operations and
events part; the rest are linked at the end.

## What is deployed and what is off

Everything below is in production, and every new agent is **off** until it is
switched on. Since v2.7.99 that is done in **AI Control Tower → การกำกับ AI →
เปิด/ปิด** (Administrator, second factor): at once, no restart, audited.
**เปิดทั้งหมด** switches on every agent and pass still off. The Portal flags
below still decide for an agent nobody has switched; a switch, once used, wins
over its flag until it is switched again. Changing a Portal setting restarts
the site for about 90 seconds, so do that outside working hours or warn people.

| Agent | What it does | Calls the model? | Setting(s) |
| --- | --- | --- | --- |
| OTD | Delay risk before on-time fails | no | `AI__OtdAgentEnabled` |
| Validation | The register's own checks on the coming week | no | `AI__ValidationAgentEnabled` |
| Carrier (`vendor-agent`) | Which carrier to ask first; compared with who people actually asked | no | `AI__VendorAgentEnabled` |
| Communication drafts | Template reminders to carriers for a person to send (SCMOS sends nothing) | no | `AI__CommunicationDraftsEnabled` (the agent's own flag is already on for chat) |
| Booking, pasted text | Booking text read into the add-job form | yes, one call per **อ่านข้อความ** | `AI__BookingAgentEnabled` |
| Booking, unplaced mail | Unplaced mail read into drafts | yes, one call per new message (at most 10 a pass) | `AI__BookingMailEnabled` as well; the Outlook mailbox must be connected |

All of them also need `AI__Enabled=true` (already on); off, it stops every
agent whatever its switch says. The chat agents also need `AI__ChatEnabled`.
Operations has its own switch (the **เปิด Operations AI** button, also in its
row). The Booking Agent uses the OpenAI key the document reader already uses.

## Suggested order

1. **OTD and Validation.** No model, no cost, findings only. Watch a few days.
2. **Carrier.** Its "ตรงกับคน 30 วัน" figure (below) builds up as people ask carriers.
3. **Communication drafts.** Reminder wording, checked by the job owners who send them.
4. **Booking, paste.** People choose to use it; each read is audited.
5. **Booking, mail.** Only once the mailbox is confirmed connected, and after
   deciding the model cost of reading unplaced mail is acceptable.

Before each one, open **AI Control Tower → การกำกับ AI** and check the agent's
row: status **ทำงาน**, level **L2 แนะนำ**, **Shadow** ticked. New agents
default to exactly that.

## What runs when (the platform's "events")

There is no message broker. Like the corrections queue, the LINE sweep and the
webhooks, the agents run as a **scheduled pass over current state**:

- Every `AI__AgentScanMinutes` (15; 0 turns it off), starting 4 minutes after
  the app starts. Each agent that is switched on asks governance first; one
  that is refused writes nothing.
- OTD, Validation and Carrier read the register. The Communication Agent reads
  the register plus pending carrier requests, POD files and LINE/TMS messages
  awaiting review. The Booking mail pass reads mail received within
  `AI__BookingMailHours` (48) that no job holds.
- A finding seen again is left alone. A changed one supersedes the old one, one
  no longer true is resolved, and one a person already answered is not raised
  again. Nothing is deleted.
- The Booking **paste** runs only when someone presses **อ่านข้อความ**.
- An administrator can run a pass immediately with
  `POST /api/ai/agents/{agentId}/scan` (needs the `X-SCMOS-AI-Control: 1` header).

## Where people see it

- **AI Control Tower → งานที่ AI ตรวจพบ:**
  - The summary cards.
  - **ของฉัน**: each person's own open items, most urgent first.
  - Per-agent filters.
- **The bell:** "AI พบงานเสี่ยง" when an unanswered finding is HIGH or
  CRITICAL. Clicking it opens the AI Control Tower. It clears when the finding
  is answered or its reason goes away.
- **ประวัติ AI:** every decision, any status, searchable.
- **AI Activity:** every model run, searchable.

Who may answer a finding: the job's owner, or a supervisor and above. Mail
drafts have no owner, so they go to supervisors and above.

## What to watch

- **การกำกับ AI → ตรงกับคน 30 วัน** per agent: how often what people did matched
  what the agent said. This is the figure to judge an agent on, before anyone
  considers more autonomy.
- **Agent ผิดปกติ** card, and the governance section's breaker column. Three
  failures in a row show DEGRADED; five pause the agent for
  `AI__BreakerCoolDownMinutes` (10). It resumes by itself.
- **ไม่เกี่ยว** answers on a kind of finding mean the rule is raising noise.
  Report the kind; the rule is changed, not the data.

## How to stop

| To stop | Do this | Takes effect |
| --- | --- | --- |
| One agent | การกำกับ AI → its **เปิด/ปิด** switch to ปิด, or its status **หยุดชั่วคราว** (Administrator, second factor; audited) | at once for questions, the next pass for findings |
| All AI actions, keep reading and recommending | Platform level → **อ่านและแนะนำเท่านั้น** (L2) | at once |
| All AI | Platform level → **หยุด AI ทั้งหมด** (L0) | at once |
| Every agent, from outside the app | `AI__Enabled=false` in the Portal | after the ~90 s restart |

SCMOS works exactly as before with every agent off. The agents write only to
`ai_decisions` and the AI audit, never to jobs.

## Raising autonomy

Not yet, and not from this page. Every agent of this platform is at most L2
(recommend) in code, and a setting cannot lift it above that. The one agent
built higher is the earlier Operations Agent's change pilot (L3, every change
approved by a supervisor). Anything beyond recommending needs
the department's open decisions first:
1. the carrier confirmation time limit, and whether an expired request may move on by itself;
2. whether reminders may be sent to carriers at all, and on which channel;
3. the Customer Requirement master (for the RequirementRisk agent);
4. model prices for the cost column;
5. the text of Rule −1 to 3 and AP-01 to 04.

## Permissions and audit

- Reading the findings needs the dashboard (`ViewDashboard`). Operation users
  see the team's findings but answer only their own jobs. Reading AI Activity
  needs `ViewAudit`. Changing governance needs an Administrator with a second factor.
- Every model run is in `ai_audit_logs`, committed before its result is
  released, holding ids and counts, never text. The Booking mail pass is the one
  run with no person behind it, as `system:agent-pass` / `System`, allowed only
  for that agent, tool and view. Every answer to a finding is also in the
  general audit (`audit_events`).
- Details: [SCMOS_AI_SECURITY.md](SCMOS_AI_SECURITY.md), [SCMOS_AI_TOOLS.md](SCMOS_AI_TOOLS.md),
  [SCMOS_AI_AGENT_PLATFORM_FOUNDATION.md](SCMOS_AI_AGENT_PLATFORM_FOUNDATION.md).

## Deployment

Deploying is the usual SCMOS release: the API deploys on push to
`azure-dotnet-migration` (migrations run then), and the web deploy is dispatched
by hand afterwards. Outside working hours unless asked. The platform added three
migrations: `AiAgentGovernance` and `AiDecisionLifecycle`, both forward-only, and
`AiAgentSwitch` (v2.7.99), which may be rolled back — the agents then follow their
flags again.

## Troubleshooting

| You see | It means |
| --- | --- |
| Nothing under งานที่ AI ตรวจพบ | the agents are off, or nothing is wrong |
| A scan answers `agent_disabled` | its switch is ปิด (or, with no switch stored, its flag is off), `AI__Enabled` is off, the pass's own switch (ร่างข้อความ, อ่านอีเมล) is ปิด, or its status is ปิด |
| `agent_paused` / `agent_maintenance` | its status in การกำกับ AI |
| `ai_stopped` | the platform level is หยุด AI ทั้งหมด |
| `agent_circuit_open` | it failed five times in a row and is resting for the cool-down; it resumes by itself |
| Booking says "not configured" | no OpenAI key on the site |
| Booking mail reads nothing | no unplaced mail within the window, or the mailbox is not connected |
| A finding keeps coming back | its reason is still true; answer it (it will not be raised again while the same) |
