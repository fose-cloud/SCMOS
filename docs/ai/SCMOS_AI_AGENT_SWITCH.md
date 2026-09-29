# The on/off switch in the AI Control Tower

v2.7.99, 29 Sep 2026. Asked for by the department lead so the agents could be
turned on without the Azure Portal: "ทำเป็นปุ่มเปิด/ปิด ใน AI control tower".

## What it does

**AI Control Tower → การกำกับ AI** has a **เปิด/ปิด** column. An Administrator
(with the second factor, as for every governance change) clicks it and
confirms; the change applies at once, with no restart. **เปิดทั้งหมด** at the
top switches on every agent and pass still off, Operations included when its
own switch is ready.

| Row | The switch | Where it was before |
| --- | --- | --- |
| OTD, Validation, Carrier | the agent's scheduled pass | `AI__OtdAgentEnabled`, `AI__ValidationAgentEnabled`, `AI__VendorAgentEnabled` |
| Data, Document & Invoice, Management, Engineering, SRE | the agent in chat | `AI__DataAgentEnabled` etc. |
| Communication | the agent in chat; **ร่างข้อความ** is its drafts pass | `AI__CommunicationAgentEnabled`; `AI__CommunicationDraftsEnabled` |
| Booking | the pasted-text read; **อ่านอีเมล** is its pass over unplaced mail | `AI__BookingAgentEnabled`; `AI__BookingMailEnabled` |
| Operations | its own switch since 8 Sep (`ai_operations_control`), with its readiness checks | unchanged |
| Rate, Incident, Compliance | none: nothing is built behind them yet | — |

A pass runs only while its agent is on as well. Turning on an agent that calls
the model (the chat agents, Booking, Operations) says so in the confirmation,
because each question or message read is paid for.

## How it decides

- Two nullable columns on `ai_agent_settings`, `enabled` and `pass_enabled`
  (migration `AiAgentSwitch`). Null follows the Portal flag, as before; true or
  false is an administrator's choice and wins over the flag in either direction
  until switched again. A row made only for the switch starts from the agent's
  defaults, so its level, shadow and status are unchanged.
- One rule, `AgentGovernance.SwitchedOn(setting, flag)`, is applied inside the
  governance gate every caller already uses, so the chat, the Management plans,
  the document reader, the Booking read and the scheduled pass all honour it.
- `AI__Enabled` stays above every switch (and `AI__ChatEnabled` above the chat
  agents), so the Portal can still stop all AI from outside the app. A switch
  does not pass the rest of governance: a paused agent, a stopped platform or an
  open breaker still refuses.
- `PUT /api/ai/agents/{agentId}/switch` with `{ target: "agent" | "pass", on,
  revision }`: Administrator, second factor, the `X-SCMOS-AI-Control` header, the
  revision read (a stale one is 409). One `audit_events` row per switch, in the
  same save, from "off (configuration)" or the earlier choice to the new one.

## Verified

- 1,204 checks without SQL and 1,395 with LocalDB (`tests/Scmos.Ai.Checks`),
  including: switched on runs an agent whose flag is off and off stops one whose
  flag is on, in the gate, the chat status, a chat run and the scheduled pass;
  `AI__Enabled` off still stops it; a drafts pass waits for its own switch; stale
  revision, supervisor, Operations and agents with nothing behind them refused;
  the audit row; the report.
- Web: 711 tests; the switches, **เปิดทั้งหมด** (10 switches in one go) and
  switching off, clicked through against a scratch LocalDB with every Portal
  flag off.
