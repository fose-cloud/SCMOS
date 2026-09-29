# AI Agent Platform — Foundation (Phase 0.1)

For the *SCMOS AI Agent Platform — Master Codex Implementation Specification*
(27 Sep 2026), after its Phase 0 assessment (`SCMOS_AI_REPOSITORY_ASSESSMENT.md`).
Built 27–28 Sep 2026 on the existing platform: no second registry, audit,
approval engine or broker. **With no settings saved, every agent behaves
exactly as before** — that is what the defaults are for.

## What was added

| Spec item | Where it lives | Notes |
| --- | --- | --- |
| Autonomy L0–L4 (§13) | `Ai/AgentGovernance.cs` (`AiAutonomy`), `ai_agent_settings.autonomy` | A setting can only lower what the agent's code was built for (`AgentDefinition.MaxAutonomy`): the Operations Agent L3 (its change pilot, confirmed by a supervisor), every other L2. |
| Shadow mode (§16) | `ai_agent_settings.shadow_mode` | The agent still decides and recommends; nothing it decides is executed. In shadow the Operations change pilot proposes nothing. |
| Agent status (§12) | `ai_agent_settings.status` | ACTIVE · PAUSED · MAINTENANCE · DISABLED set by an administrator; DEGRADED is the circuit breaker's and never stored. |
| `AI.Execution.Enabled` kill switch (§15) | the `platform` row of `ai_agent_settings` | Its autonomy is the ceiling for every agent: **L4** everything as configured · **L2** read and recommend only, every write refused · **L0** every agent stopped. Takes effect on the next request, no restart. |
| Circuit breaker (§54) | `AgentGovernance.Breaker`, read from `ai_audit_logs` | 3 platform failures in a row → DEGRADED (still answers); 5 → the agent rests for the cool-down (refuses `agent_circuit_open`); after it, the next run decides. A run the platform answered — even "please be clearer" — ends the count; busy/cancelled/not-connected neither count nor reset. |
| Agent health (§12) | `GET /api/ai/agents` | Runs, failures, failure rate, consecutive failures, last success/failure, average latency — last 24 h, from the audit. No new counter table. |
| Cost / usage (§57) | `AI__PriceList`, `AI__PriceCurrency` | Tokens were already audited; cost = tokens × configured price. A model without a price shows "ราคาไม่ครบ", never zero. |
| Prompt versioning (§17) | `ai_audit_logs.prompt_version` | Every run's start records `build:<commit>` — the prompts are written in code, so the commit names the exact text that answered. Recorded by the audit writer; no agent changed. |
| Decision log (§17, §23–25) | `ai_decisions`, `Ai/AgentResults.cs`, `Ai/AiDecisionLog.cs` | Facts, rule results, observations, inferences, recommendations and blocking issues kept apart. A fact or rule result without its source, a recommendation from nothing, or a BLOCKED without blocking issues is refused whole — never trimmed into shape. For shadow runs: the human's choice, match/override and the override's reason. |

**Not in this foundation** (the spec's later steps): the event scanner, the six
operational agents, *My AI Tasks*. No agent writes decisions yet; the OTD and
Validation agents will be the first.

## Where each gate is asked

One snapshot per run (`IAiGovernance.SnapshotAsync`), asked by:

- the orchestrator, before the provider is called or anything is read (`agent_paused`, `agent_maintenance`, `agent_disabled`, `ai_stopped`, `agent_circuit_open`, `governance_unavailable` — all 503);
- each step of a Management plan (a paused specialist is off for the plan too);
- the Operations change pilot — *execute with approval* (`execution_disabled`, `shadow_mode`);
- the Workspace document reader (the Document & Invoice Agent reading).

Governance that cannot be read refuses (fail closed), as the Operations switch
already does. The development mock and a configuration with AI off never read it.

## Operating it

AI Control Tower → **การกำกับ AI** (anyone who may read the AI audit sees it; an
Administrator changes it). Every change needs a reason, is audited
(`audit_events`, entity `ai-agent-settings`, one row per field) and carries the
revision it was read at — a stale one is refused (409), never overwritten.

Drill, when an agent must stop in Production: set its status to หยุดชั่วคราว
with a reason — immediate, no restart. To stop all writes: AI ทั้งระบบ →
อ่านและแนะนำเท่านั้น. To stop everything: หยุด AI ทั้งหมด. Until 29 Sep 2026 the
flags (`AI__…Enabled`) always won; since then an agent's **เปิด/ปิด** switch, once
stored, wins over its flag in either direction, and only `AI__Enabled` stays above
it ([SCMOS_AI_AGENT_SWITCH.md](SCMOS_AI_AGENT_SWITCH.md)).

## Configuration (all optional)

| Setting | Default | Meaning |
| --- | --- | --- |
| `AI__BreakerDegradedAfter` | 3 | failures in a row shown DEGRADED |
| `AI__BreakerPauseAfter` | 5 | failures in a row that rest the agent |
| `AI__BreakerCoolDownMinutes` | 10 | how long it rests |
| `AI__PriceList` | *(empty)* | `model=input/output; …` per million tokens, e.g. `gpt-4.1=2.00/8.00` — **CONFIGURATION_REQUIRED** for cost |
| `AI__PriceCurrency` | USD | the currency of those prices |

## API

`GET /api/ai/agents` · `PUT /api/ai/agents/{agentId}/settings` (and `platform`) ·
`GET /api/ai/decisions` · `POST /api/ai/decisions/{id}/outcome`. Writes need JSON,
the `X-SCMOS-AI-Control: 1` header, an Administrator (settings) with the second
factor, and a reason. `/api/ai/status` gained `executionEnabled`,
`governanceAvailable` and per agent `status`, `autonomy`, `shadow`, `code` —
appended; the first fields are unchanged.

## Database

Migration `AiAgentGovernance`: table `ai_agent_settings`, table `ai_decisions`
(check constraints on autonomy, status, confidence; status is a concurrency
token so a decision is answered once), column `ai_audit_logs.prompt_version`
(defaulted). Additive. **Rollback is forward-only** — like the audit's own
migration, `Down` throws rather than erase decisions and prompt versions; roll
back the application and switch the AI off instead. Older code runs against
the added table and column unchanged.

## Tests

- `tests/Scmos.Ai.Checks/GovernanceChecks.cs` — defaults reproduce today; autonomy
  cap; L0/L1; shadow; execution switch; platform stop; statuses; breaker
  thresholds, cool-down and neutral runs; prices; prompt version shape and
  placement; the decision contract; the orchestrator refusing **before** any
  provider call; the Control Tower status. With `--write-local-db`, on its own
  scratch LocalDB: Administrator-only saves, reasons, design cap, audit rows,
  revision conflicts, snapshot, execution switch and shadow gating the change
  pilot, breaker and cost from audit rows, decision record/list scope/answer once.
- `tests/aiGovernance.test.mjs` — strict parsing, levels on offer, labels, cost
  text, request body, panel wiring, Thai refusal texts.
- Verified in the browser on local servers (27–28 Sep): read-only view for an
  audit reader; an Administrator pausing the Data Agent and turning execution to
  read-and-recommend, each audited, reflected in `/api/ai/status`; stale revision 409.

## Next

Done 28 Sep: the OTD and Validation agents — `SCMOS_AI_OTD_VALIDATION_AGENTS.md`.
Then the Carrier Agent in shadow. The five confirmations in the assessment (§8)
are still open and do not block it.
