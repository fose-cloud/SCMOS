# SCMOS AI Agent Platform — Repository Assessment (Phase 0)

For the *SCMOS AI Agent Platform — Master Codex Implementation Specification*
(received 27 September 2026). Read against the repository at `65fd552`
(`azure-dotnet-migration`, v2.7.89 web / Carrier Billing Phase 9 API).
Evidence is named by file; anything not found is said to be not found.
No code was changed for this assessment.

**The short version.** The specification asks for a governed AI platform with
six operational agents. SCMOS already has the platform — registry, tool
registry, permission catalogue, audit, kill switches, approval queues, an AI
Control Tower, eleven agents behind their own flags, all read-only by
construction, all live since 22 September — and it already has the
deterministic engines four of the six agents would explain (validation,
OTD risk, sequential carrier assignment, pre-run confirmation). What it does
not have is **autonomy levels, shadow mode, a decision log, agent health with
a circuit breaker, prompt versions, cost, an event router, and a Customer
Requirement master**. Those are the gap. Nothing in the spec requires a
second registry, a second audit, a second approval engine or a message broker.

---

## 1. Existing architecture (spec §9)

| Area | What the repository has | Evidence |
| --- | --- | --- |
| Frontend | Next.js 16, TypeScript, one app shell with screen ids (not the App Router per screen); CSS modules; no shadcn/ui | `app/SCMOSApp.tsx`, `app/scmos/nav.ts`, `app/scmos/screens/*` |
| Backend | ASP.NET Core .NET 10 minimal APIs, one project; services + pure `Rules/*` checked by `--check-*` | `server/Scmos.Api` |
| Database | Azure SQL (Standard S2), EF Core migrations, `EnableRetryOnFailure` | `Data/ScmosDbContext.cs`, `Program.cs:44` |
| Background work | In-process `BackgroundService`s: reports, LINE worker/reminder/chase, carrier webhooks, corrections, register warm-up, mail, Graph subscriptions | `Program.cs` `AddHostedService` |
| Events | No event bus or broker. Work is triggered by schedulers scanning state and by the write paths themselves | as above |
| Authentication | Entra ID (Easy Auth), guest UPN handling, Development header in dev; SCMOS-issued keys for carriers | `Auth/UserAccessor.cs`, `Services/CarrierApiAuth.cs` |
| Authorization | Capability model per role (`Rules/Roles.cs`), roles: Administrator, Manager, Assistant Manager, Operation Supervisor, Subcontractor, Operation User, CS, Management, Viewer; second factor for sensitive writes | `Rules/Roles.cs`, `Rules/SignInStrength.cs` |
| Audit | `audit_events` (every register write, source SCMOS/LINE/TMS/…, revertible by supervisors); `ai_audit_logs` (append-only AI runs) | `Services/AuditService.cs`, `Data/AiAuditLog.cs` |
| Notifications | Bell (`Rules/Notifications.cs`), mail via Graph (`MailWorker`), LINE (inbound + one 16:00 summary), carrier webhooks | as named |
| Integrations | LINE, Outlook/Graph, Carrier TMS API v1, OpenAI | `docs/integrations/`, `Ai/Providers/OpenAiProvider.cs` |
| Testing | `--check-*` rule checks (32 in CI), `tests/Scmos.Ai.Checks` (1,017), web `node --test` (659) | `.github/workflows/api.yml` |
| Deployment | GitHub Actions → App Service (API on push to `server/**`, web by dispatch) | `.github/workflows/*.yml` |

## 2. The governance rules the spec names (§3)

| Name | Found? | What the repository does |
| --- | --- | --- |
| Rule -1, Rule 0, Rule 1, Rule 2, Rule 3 | **Not found** in code, docs, README or tests (there is no `AGENTS.md`) | **BUSINESS RULE REQUIRES CONFIRMATION** — their text is needed before any agent cites them |
| AP-01 Process-centric | Not found by name | Behaviour exists: `Rules/Workflow.cs` gates and holds; `JobStatus` ladders by category |
| AP-02 Controlled communication | Not found by name | Behaviour exists: LINE inbound-only since v2.7.54, every outbound path named (`docs/…`, memory of the department's decisions) |
| AP-04 Sequential carrier assignment | Not found by name | **Enforced at the database**: `supplier_requests_one_active_job_idx`, unique on `job_key` where outcome is pending/confirmed (`20260925031857_CarrierCollaborationPhase2`). Offers are ranked; reassignment supersedes. A confirmation **timeout is recorded by a person** (Expired); there is no configured confirmation SLA that advances automatically |

## 3. Reuse / extend / missing (spec §9 table)

| Spec requirement | Existing SCMOS component | Status |
| --- | --- | --- |
| AI Control Tower (orchestration) | `Ai/AgentOrchestrator.cs`, `/api/ai/chat`, `/api/ai/status`; screen `AiControlTower.tsx` (with the approvals section since v2.7.86) | **EXISTS** |
| Agent registry | `Ai/AgentRegistry.cs` — 11 agents in code, each with pages, tools and its own flag | **PARTIAL** — no autonomy level, shadow mode or status beyond on/off; not editable at runtime |
| Execution engine | `IAgentExecutor`, `ToolExecutor`, `AiRunLimiter`, timeouts | **EXISTS** |
| Tool registry + authorization | `ToolRegistry`, `ai_tools` catalogue (`Rules/AiPermissions.cs`), `QueryPolicyGuard`, `AiPermissionPolicy`; every registry tool is a low-risk read (checked by `PlatformVocabularyCheck`) | **EXISTS** |
| Permission = user + role + capability + record scope | `AiReadScope` (own / team), capability checks, carriers refused by `InternalUser`; 99-line access matrix in CI | **EXISTS** |
| Audit (execution, correlation, tokens) | `ai_audit_logs` — run id, sequence, agent, user, tools, evidence ids, tokens, correlation; append-only; `--report-ai-audit` never deletes | **EXISTS** |
| AiDecision / AiActionAudit | Writes are audited in `audit_events`; proposals in `job_corrections`, `approvals`, `line_events` | **PARTIAL** — no per-run structured decision record (facts / inference / recommendation) |
| Approval workflow | `approvals` + `ApprovalPolicy` (7-day TTL, requester/approver rules); `job_corrections` (owner approves); LINE/TMS queue; operations-changes pilot | **EXISTS** (three queues) — a user-facing "My AI Tasks" view over them is missing |
| Feature flags | `AI__Enabled`, `AI__ChatEnabled`, `AI__<Agent>AgentEnabled` ×11, `WriteToolsEnabled`, `OperationsWritesEnabled` | **EXISTS** — named differently from the spec; map, do not duplicate |
| Global kill switch `AI.Execution.Enabled` | `AI__Enabled`/`ChatEnabled` stop everything (503 before the provider); `ai_operations_control` is a runtime DB switch for the Operations write pilot | **PARTIAL** — no single runtime switch that stops *writes* while leaving reads and recommendations on |
| Shadow mode | — | **MISSING** |
| Autonomy levels L0–L4 | Write tools are off; everything live is L1–L2 in effect | **MISSING** as a setting |
| Agent health / circuit breaker | SRE Agent reads platform health; `/api/ai/status` per agent; `AiRunLimiter` | **PARTIAL** — no consecutive-failure tracking, no DEGRADED/PAUSED state |
| Cost / usage | Tokens per run in the audit | **PARTIAL** — no estimated cost (needs a price per model: **CONFIGURATION_REQUIRED**) |
| Prompt versioning | Instructions live in code; git is their history | **MISSING** as a recorded version per run |
| Event router / event contract | — (schedulers scan state; no domain events) | **MISSING** — see §6 for the recommended shape |
| Structured output validation | Management plans assemble typed results; model answers are re-validated where they are used (e.g. the customer-name reading re-checks every name against the rotation) | **PARTIAL** |
| Model gateway | `IAiProvider` (OpenAI, Mock) | **EXISTS** |
| Prompt-injection guard | External text passed as data; tools read-only; no tool can write | **EXISTS** by construction |
| Sales isolation (§5) | There is **no Sales role or Sales workspace** in the repository | **N/A today** — becomes real with the RFQ specification; the AI's user-scoped reads already refuse what a role cannot see |

## 4. The six core agents against what exists

| Agent | Deterministic engine already in SCMOS | Status | What the agent would add |
| --- | --- | --- | --- |
| RequirementRisk | Pieces only: `CustomerTrainingRequirements` (training per customer), `BillingRequirementRules` (billing documents), `DocumentChecklist`, `CustomerTerms` (Lotus OTD grace, in code), DG in `VehicleTypes`, `SupplierCompliance` | **MISSING master** — there is no Customer Requirement master (equipment, axle, safety, food-grade …). The spec forbids hard-coding them (§26) | Needs the master first: data the department keys, effective-dated. **BUSINESS RULE REQUIRES CONFIRMATION** for its content |
| Booking | Jobs are the bookings (`operation_jobs`, statuses DRAFT/RECEIVED/VALIDATING…); workbook import; Outlook mail read deterministically (`Rules/EmailExtraction.cs`, `MailQueue`, `EmailMatching`) | **PARTIAL** | Draft-first extraction from mail the matcher cannot place, with source and confidence per field — as proposals, never a job |
| Validation | `Rules/JobRules.cs` (issues with severity; KPI exclusion), `Workflow.cs` gates, `JobDateInputGuard`, `CorrectionRules`, `PreRun` | **EXISTS (rules)** | An explainer and a **validation history** (today issues are computed on read, not stored) |
| Carrier | Sequential offers (AP-04 index), scorecard (`SupplierEvaluations`, `ScorecardColumn`), compliance, training refusal at assignment, capacities, Carrier TMS API | **PARTIAL** | Eligibility filter from those rules, ranked candidates with reasons, **shadow mode** (AI suggested vs human chose). No autonomous sending |
| Communication | Communication Agent (reads LINE and mail); outbound: 16:00 LINE summary, Graph mail, webhooks, bell; LINE reminder/chase templates exist but are **off by the department's decision** (v2.7.54) | **PARTIAL** | Templates + duplicate guard + attribution; **internal notifications only** until the department re-opens outbound |
| OTD | `MonitorRules` risk list, `ProblemRules`, `KpiMeasures`, `CustomerTerms`, `DelayReasonRule`, Operations Agent `query_delays`/`query_followup` | **PARTIAL** | Rule-first projected-delay (plan vs arrival/last event) and an explanation that keeps fact / rule result / inference apart. There is no GPS/ETA feed; arrival comes from LINE, TMS and the grid |

## 5. Gap classification (spec §9)

- **EXISTS** — orchestration, tool registry and authorization, user-scoped reads, audit, model gateway, kill switches (all-or-nothing), approval queues, Control Tower screen, eleven agents.
- **PARTIAL** — registry (no autonomy/shadow/status), decision record, execution-only kill switch, health, usage cost, structured output, Booking/Carrier/Communication/OTD engines.
- **MISSING** — shadow mode and its decision log, autonomy levels, agent health with circuit breaker, prompt versions, event router, Customer Requirement master, validation history, My AI Tasks view, AI Admin view.
- **CONFLICT** — (1) the spec's agent codes (`BOOKING`, `CARRIER`, …) and flag names (`AI.Booking.Enabled`) differ from the live ids (`operations-agent`, `AI__OperationsAgentEnabled`); **resolution: keep the live ids and flags, map the spec's names in the docs**. (2) The spec's "AI Control Tower cards" (Jobs Monitored, AI Handling…) assume agents that run on events; today agents answer questions. (3) Outbound LINE was switched off by the department; the Communication Agent's A2 reminders would need that decision reversed — **BUSINESS RULE REQUIRES CONFIRMATION**.

## 6. Recommended shape (additive, no new infrastructure)

- **Settings, not code, for autonomy and shadow.** One table `ai_agent_settings` (agent id, autonomy L0–L4, shadow, status, revision, updated by/at) read at run time beside the existing flags. The flag stays the master off switch; the setting can only narrow it. Defaults: L2, shadow on. Admin-only screen, second factor, audited — the pattern `ai_operations_control` already uses.
- **`AI:ExecutionEnabled`** as a runtime switch in that same row family: when off, no agent may reach a write path or an outbound channel; reads and recommendations continue. SCMOS without AI is unaffected either way (the AI is not in any business path today — this stays true).
- **A decision log, not a second audit.** `ai_decisions` keyed by the audit's run id: entity, decision type, facts / rule results / inferences / recommendation (JSON, schema-checked), risk, confidence, rule references, and for shadow runs the human's actual choice and whether it matched. The audit keeps what happened; the decision log keeps what the agent concluded.
- **Health from the audit, not a new counter.** Consecutive failures and failure rate per agent are already in `ai_audit_logs`; a circuit breaker reads them and sets the setting's status to DEGRADED/PAUSED, alerting through the bell.
- **Prompt version = hash recorded on `run_started`.** The text stays in code (reviewed in PRs); the run records which version answered.
- **Events without a broker.** A scheduled scanner in the existing `BackgroundService` style emits typed internal events (the spec's contract, §19) from state changes it can see — offers past their response window, jobs entering tomorrow's plan without a truck, arrivals later than plan — and routes them to agents in shadow. This matches how LINE, corrections and webhooks already work on one App Service instance.

## 7. Recommended order (adapted from spec §65)

1. **Foundation** — settings table, execution switch, decision log, health/circuit breaker, prompt hash, usage cost (price table as configuration). Tests: autonomy, shadow, kill switch, AI-disabled regression.
2. **OTD Agent** and **Validation Agent** first — both sit on engines that exist, write nothing, and give the Control Tower real "Need attention" numbers.
3. **Carrier Agent in shadow** — candidates and reasons logged against the human's choice; AP-04 already enforced by the index.
4. **Communication Agent, internal only** — templates, duplicate guard, bell/mail to staff.
5. **Booking Agent, draft-first** — from mail the deterministic matcher could not place.
6. **RequirementRisk Agent** — after the Customer Requirement master exists and holds the department's data.
7. **My AI Tasks** (one view over the existing approval queues + decisions needing a person) and **AI Admin** (autonomy, shadow, status, execution switch).

## 8. Confirmations needed before the affected work

1. The text of **Rule -1, Rule 0, Rule 1, Rule 2, Rule 3** and **AP-01 … AP-04** — none is in the repository.
2. **Customer Requirement master** — who keys it, and its first content (equipment, axle, safety, food-grade, documents per customer).
3. **Carrier confirmation SLA** — minutes per customer or per job type, and whether an expired offer may move to the next carrier automatically (A2) or only by a person (today).
4. **Outbound carrier reminders** — LINE outbound was switched off on 20 September; may the Communication Agent send reminders at all, and on which channel.
5. **Model prices** for cost tracking (THB or USD per 1M tokens) — configuration, not code.

Steps 1, 2 (OTD, Validation) and the shadow half of 3 can proceed without these answers.
