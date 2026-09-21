# SCMOS AI platform — Phase 2 record: the Data Agent's first read

Implemented 20–21 September 2026 · released as v2.7.61 · the first agent after Operations, as the [plan](SCMOS_AI_IMPLEMENTATION_PLAN.md) §2 scoped it: "source-backed volume/OTD read for an explicit rule and bounded period; customer/trucker filters". The specification's *SCMOS Data Agent*.

## PHASE COMPLETED — 2

**What it answers.** "KPI เดือนที่แล้วของ L'OREAL", "จำนวนงานปีนี้แยกตามผู้ขนส่ง", "KPI 2026-09 ของ SANGJA": the department's own figure for one period — a year, a month or a day — narrowed, if the question says so, to one customer (by name, contained, case-insensitive) or one carrier (by company, through the directory's aliases, so SJ and SANGJA are one). The answer is the figure the KPI screen shows, computed by `KpiService` from the same register snapshot, and it carries what a figure needs before anyone repeats it:

- the period and the filters as applied, and the scope (a restricted account gets its own jobs' figure and the summary says so);
- total, the measured base (jobs with a plan time and an arrival time), on-time count and percent — never a percentage without its base; when the base is empty the summary says the KPI cannot be measured;
- not-assessable, undated, format-error and action-required counts, the category split, a per-carrier breakdown (up to 50, `truncated` when cut);
- the rule: `arrival.on_time` v2, `Rules/JobRules.cs:JobRules.IsOnTime`, its meaning and what missing data does to it (from `BusinessRuleRegistry`);
- `customerContract: "unknown"` — no verified customer contract is registered, and the answer says the department's zero-grace rule was applied instead of guessing an SLA;
- when the register was last changed (`sourceUpdatedAt`), when the read was made, and the basis text.

**How it works.** The same shape as Operations, on purpose: the model chooses the tool (`query_kpi`) and its arguments from the question — the period as `YYYY`, `YYYY-MM` or `YYYY-MM-DD`, a customer, a carrier, a carrier-row limit — with the instructions naming today, this month and last month in Asia/Bangkok; the server validates the arguments against the strict schema, refuses a period the rule cannot read (clarification), and `DataReadService` computes through `IKpiReports`; the audit surrounds the read (`data-agent`, `query_kpi`, view `kpi`, the carriers as the evidence keys, the correlation id); the dispatch budget is one read. Nothing the model writes becomes a number.

**Files created**

- `server/Scmos.Api/Ai/Data/DataReadService.cs` — `DataAnswer` (+ `DataRule`, `DataFilters`, `DataCarrier`), `ParsePeriod`, `Label`, `CleanFilter`, the read, `DataReadHandler`
- `server/Scmos.Api/Ai/Data/DataAgent.cs` — `IAgentExecutor<DataExecution>`, `Summarise`
- `tests/Scmos.Ai.Checks/DataChecks.cs`
- this record

**Files modified**

- `server/Scmos.Api/Services/KpiService.cs` — `IKpiReports`, `KpiFilter` (customer, trucker, owner), `BuildAsync(period, filter)`; `KpiReport.SourceUpdatedAt` (additive, default null); the parameterless overload unchanged
- `server/Scmos.Api/Ai/AgentRegistry.cs` — `kpi-agent` → **`data-agent`** ("Data Agent", tools `query_kpi`, pages `kpi`, `reports`); `AiOptions.KpiAgentEnabled` → `DataAgentEnabled` (never set anywhere; nothing was connected under the old name)
- `server/Scmos.Api/Ai/ToolRegistry.cs` — `query_kpi` with its schema and policy, bound when a connected `DataReadService` is given
- `server/Scmos.Api/Ai/QueryPolicyGuard.cs` — admits `DataAnswer` as a read output beside `OperationsAnswer`
- `server/Scmos.Api/Ai/AiAuditRules.cs` — `KnownAgents` + `data-agent`, `KnownTools` + `query_kpi`, view `kpi`
- `server/Scmos.Api/Ai/AgentOrchestrator.cs` — routes a connected Data Agent in live mode; status shows it; `AiChatResponse.Kpi`
- `server/Scmos.Api/Ai/AiContracts.cs`, `Endpoints/AiChatEndpoints.cs` — `kpi` on the envelope (appended)
- `server/Scmos.Api/Ai/AiServiceRegistration.cs`, `Program.cs` — `IKpiReports` beside `KpiService`; `DataReadService` (not connected in a host without a KPI service), `DataAgent`
- `server/Scmos.Api/Rules/AiPermissions.cs` — `query_kpi` in the catalogue (Allow, agent `kpi`); `Services/AiGateway.cs` — the matrix shows a catalogue tool the roster lacks; `Data/SupplierSeeder.cs` — the seeder adds missing roster rows and never touches existing ones
- `app/scmos/aiControl.ts` — `KpiAnswer`, strict parser (a Data Agent reply must carry `kpi` and no Operations evidence; an Operations reply must carry no `kpi`), `askBody(message, agent)`, `dataAvailability`
- `app/scmos/screens/AiControlTower.tsx` — an agent choice (Operations / ข้อมูล KPI) shown only when the status lists the Data Agent; KPI prompts; the `KpiCard` with the figure and its provenance, carriers in the shared scroll box
- `tests/Scmos.Ai.Checks/SharedContractChecks.cs`, `OperationsChecks.cs`, `AuditChecks.cs`, `Program.cs`; `tests/aiControl.test.mjs`, `tests/fixtures/ai-control.mjs`
- `docs/ai/SCMOS_AI_AGENTS.md`, `SCMOS_AI_ARCHITECTURE.md`, `SCMOS_AI_IMPLEMENTATION_PLAN.md`, `SCMOS_AI_PLATFORM_ASSESSMENT.md`

**Existing components reused** — `KpiService` (the figure; no second calculation anywhere), `CarrierDirectory.Company` (the carrier filter's aliases), `JobRules.InPeriod / IsMeasurable / IsOnTime` through the service, `BusinessRuleRegistry.Resolve` (the rule on the answer), `AiPermissionPolicy` / `QueryPolicyGuard` / `AiDispatchBudget` / `AiInputSchema` (1A–1B), the audit (1D topology, correlation id), `AgentOrchestrator`'s gates (sign-in, internal account, agent flag, audit ready, provider), the Control Tower's request/parse/render pattern.

**APIs added** — none. **Changed** — `POST /api/ai/chat` accepts `agentId: "data-agent"` (or `context.page: "kpi"`) and answers with `kpi` (appended to the envelope); `GET /api/ai/status` lists `data-agent` with `connected: true` where a KPI service is present; `GET /api/ai/tools` includes `query_kpi`.

**Tools added** — `query_kpi` (Read, `operation_jobs`, ≤ 50 carrier rows). **Agents added** — `data-agent` (the former `kpi-agent` descriptor, now connected).

**Database changes** — none. **Migration status** — none needed; the `ai_tools` roster row for `query_kpi` is written the next time `--seed-suppliers` runs, and the matrix shows it from the catalogue until then.

**Security changes** — none loosened. The Data Agent is off unless `AI:DataAgentEnabled` (and `AI:Enabled`, `AI:ChatEnabled`) — all absent on production. A carrier is refused at the orchestrator and again at the guard; a view-only account has no scope; a restricted account (Management) reads its own jobs' figure only — a scope the KPI screen itself does not impose, applied here because the AI's scope comes from the server identity (1B). The model sees one tool schema and the question; a customer name in the register never reaches the prompt; a rate, a contract, an SLA cannot be produced (the tool has none and the answer says `unknown`).

**Permissions added** — none. `ViewDashboard` is what the agent and the tool require, as the KPI screen does.

**Tests added** — 62 checks in `tests/Scmos.Ai.Checks` (`DataChecks`, 58 labelled "2:" plus 4 adjusted contract checks): the period table (valid and invalid), labels, filter cleaning, the read over a fixture source (period and filters delivered, the figure and its cut, the rule and the unknown contract, restricted scope, blank scope, bad arguments, wrong tool), the registry and schema (forged owner refused), the agent id and flag, the guard, the audit vocabulary (accepts `data-agent`/`query_kpi`/`kpi`, refuses mixed views and the old id), the agent's run (ok with provenance, summary wording, audit sequence and evidence keys, prompt hygiene, carrier/other-agent refused, clarification on no tool or unreadable period, invalid tool, restricted account, no audit, no service), and the orchestrator (status, the run carrying `kpi` not `evidence`, the KPI page route, the flag off, a carrier, Operations still not connected without its executor). Offline total 491 (was 429); with `--write-local-db --local-db` 605. Web: 20 Control Tower tests (the KPI reply accepted, ten malformed shapes refused, `askBody`, `dataAvailability`). Unchanged suites: 580 Node, `--check-capability`, `--check-carrier-api`, `--check-line`, `--check-scorecard`, `--check-report`; `-warnaserror`; tsc; eslint.

**Build result** — green at the Phase 2 commit.

**Verified on LocalDB, 21 Sep** — `GET /api/ai/status` as a supervisor lists `data-agent` `enabled: false, connected: true` (the flag is off; the KPI service is bound). A live question needs the provider, which the local host does not have; the run is proved by the fixture-provider checks, as Operations' was in Phase C.

**Known risks**

- The KPI screen shows the department's figure to every `ViewDashboard` holder; the Data Agent narrows a Management-role account to its own jobs (1B's scope rule). The two can differ for that role; the summary says "เฉพาะงานของ …" so the reader knows which figure they have.
- A customer filter is a contained match on the register's free-text customer name ("AKZO" matches "AKZO NOBEL"); a carrier filter is the directory's company. The answer states the filter as applied.
- The model chooses the period; "เดือนที่แล้ว" resolves against today in Bangkok, given in the prompt. A question with no period yields a clarification, not a guess.
- One read per run: a comparison ("this month versus last") is two questions today; Phase 8 is where a run takes two steps.

**Remaining tasks** — Phase 3 (the Operations extension: named missing-ETA/truck queries), then 4 (Communication over the mail/LINE ledgers), 5 (Document & Invoice), 6–7 (Engineering, SRE — connectors that need infrastructure decisions), 8 (collaboration), 9 (hardening). Each waits for the department's word, as the phases so far have.

**Recommended next phase** — 3.
