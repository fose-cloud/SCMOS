# SCMOS AI architecture — Phases B/C/D

Updated 2026-09-07. Additive foundation and read-only Operations execution in the existing .NET API; not a new backend or a deployed Control Tower. See [discovery](SCMOS_CURRENT_ARCHITECTURE.md) and [implementation log](SCMOS_AI_IMPLEMENTATION_LOG.md).

## Implemented boundary

`existing Next proxy → /api/ai/chat → IUserAccessor → existing AiGateway → AgentOrchestrator → OperationsAgent → authorized tool → OperationsReadService → JobsRepository`

- `AiGateway` keeps its existing constructor, tools and approval behavior. New methods take the foundation runtime explicitly; legacy calls do not resolve new AI settings.
- `AgentRegistry` supplies eight specialists and deterministic master routing by explicit `agentId`, known `context.page`, or Operations when neither is supplied. No LLM router and no new role grants.
- `ToolRegistry` connects three reviewed read handlers through scoped DI. Existing `AiPermissions` remains the upper bound; its legacy Allow designation alone is not user authorization. Constructing a registry without a service leaves its handlers unavailable (used in foundation tests).
- `OpenAiProvider` adapts the already installed OpenAI .NET SDK 2.13.0, retaining the existing `OpenAI:Model` default (`gpt-4.1`). Typed tool proposals are validated but never executed in the provider.
- Development mock goes end-to-end through the gateway. It returns an explicit `[DEVELOPMENT MOCK]` label, no operational figures and no database reads/writes.
- The Operations agent uses one provider request to select one authorized tool. It validates that proposal again before dispatch, then composes totals, reasons and evidence on the server. Business records never go back to the model, and free-form model answers are not treated as facts.
- Phase D connects SqlAiExecutionAudit using isolated EF contexts. Non-mock chat probes the mapped audit table, then requires committed run/tool start and completion events. Missing schema, database failure or invalid metadata returns audit_not_ready without a business read or without releasing its result. Flags cannot bypass auditing. Other specialists still return not_connected. All AI flags still default off; no production migration or activation was performed.

## Configuration

Use the existing .NET configuration providers; Azure environment names use double underscores. No secret is added to repository files or the browser.

| .NET setting | Azure App Service setting | Default / meaning |
| --- | --- | --- |
| `OpenAI:ApiKey` | `OpenAI__ApiKey` | Reuse the existing API-side secret; never return it |
| `OpenAI:Model` | `OpenAI__Model` | Existing `gpt-4.1` default; extraction unchanged |
| `OpenAI:Endpoint` | `OpenAI__Endpoint` | Empty = SDK default; new adapter permits HTTPS only |
| `AI:Enabled` | `AI__Enabled` | false |
| `AI:ChatEnabled` | `AI__ChatEnabled` | false |
| `AI:MockMode` | `AI__MockMode` | false; refused outside Development |
| `AI:OperationsAgentEnabled` | `AI__OperationsAgentEnabled` | false |
| `AI:VendorAgentEnabled` | `AI__VendorAgentEnabled` | false |
| `AI:RateAgentEnabled` | `AI__RateAgentEnabled` | false |
| `AI:KpiAgentEnabled` | `AI__KpiAgentEnabled` | false |
| `AI:IncidentAgentEnabled` | `AI__IncidentAgentEnabled` | false |
| `AI:BillingAgentEnabled` | `AI__BillingAgentEnabled` | false |
| `AI:ComplianceAgentEnabled` | `AI__ComplianceAgentEnabled` | false |
| `AI:ManagementAgentEnabled` | `AI__ManagementAgentEnabled` | false |
| `AI:WriteToolsEnabled` | `AI__WriteToolsEnabled` | Reserved, false; setting true does not activate writes |
| `AI:TimeoutSeconds` | `AI__TimeoutSeconds` | 20; allowed 1–60 |
| `AI:MaxOutputTokens` | `AI__MaxOutputTokens` | 800; allowed 64–2000 |

The existing key was confirmed present in `scmos-api-3936` and the user authorized its reuse. Its value was not copied locally and validity/quota were **not** tested with a live request. No Azure setting was changed.

## API contract

All new routes follow existing server identity resolution and return `Cache-Control: no-store`.

- `GET /api/ai/status`: non-secret configuration booleans and caller-authorized specialist metadata. ProviderConfigured is configuration-only, not a live-provider test. Enabled, non-mock status probes the mapped audit shape with a five-second budget. Disabled/mock status never queries SQL. AuditReady means the read probe succeeded, not a guarantee of future writes; every run still commits its audit before execution. WriteToolsReady always remains false.
- `POST /api/ai/chat`: accepts only `message`, optional `agentId`, optional `context: { page }`. No client-provided identity, role, model, system prompt, SQL, owner filter, conversation history or tool list.
- `GET /api/ai/audit?take=25&beforeId=...`: internal recognized callers with existing ViewAudit, 1–100 runs per page, continuation ID. No exact global count or unbounded list. Existing ViewAudit includes Operation User; no role was broadened.
- `GET /api/ai/audit/{runId}`: the same permission guard, immutable run/tool timeline, source keys and usage; malformed ID 400, absent run 404, storage unavailable 503. No edit/delete route. Audit remains readable when AI execution flags are disabled.

Example request:

```json
{"message":"Show today's high-risk shipments.","context":{"page":"operations"}}
```

Response includes `runId`, `code`, `summary`, `agentId`, `mock`, `usage`, optional `evidence`, and `error` for non-success. New codes include audit_not_ready, clarification_required, invalid_tool, source_unavailable and provider_busy. Disabled/unauthorized/error responses never masquerade as an empty successful job list.

Limits: 16 KiB body including chunked requests, 4000 message characters, four in-flight runs and 20 starts/minute per API instance, no waiting queue. Each Operations run permits one tool call and at most 50 evidence rows. Provider adapter disables automatic retries, bounds output and timeout, and separates system/user messages. These instance-local limits need review before multi-instance live rollout.

## Phase C source and risk semantics

The repository read performs a single no-tracking, owner-filtered SQL query, streaming rows through the existing parser. Restricted reads require a nonblank server-resolved owner before SQL executes. The read service repeats the scope check, including malformed rows. Private driver/operator names become presence flags; raw JSON, contact fields and free-text notes are discarded. Evidence exposes source keys and bounded operational fields only.

- `today`: active My Job work scheduled on the current Thai date, excluding completed/cancelled work and DELIVERY.
- `risk_today`: existing `MonitorRules.Judge` reasons within an explicit window: overdue work through the next two days. No invented numeric risk score or new definition of HIGH. Far-future unassigned jobs are outside this query window even though the full monitor can flag them.
- `search`: active work by key/job code/container/customer across dates.
- `delays`: active work in `WorkspaceTabs.Delay`, not the risk queue and not an OTD calculation.

The run captures one Asia/Bangkok date (+07:00) for routing, filtering and response. MonitorRules still treats either arrival field as arrived, and NoTruck requires BOTH driver and plate missing. RiskService's broader/different calculation remains untouched.

Evidence includes full matched total, number returned, truncation marker, unreadable-date/malformed-row counts, reference date, source update/read timestamps and per-row reasons/suggested actions. Total is counted before capping examples. Source errors return unavailable, not zero. Rows retained in memory are bounded, but the source query still visits the authorized scope to calculate complete totals; no production latency benchmark is claimed.

## Test / rollout

```powershell
dotnet run --project tests/Scmos.Ai.Checks/Scmos.Ai.Checks.csproj -c Release -p:UseAppHost=false
```

This test uses an in-memory SDK transport and a temporary localhost-only host with fake identity and no SQL provider. It does not start SCMOS's production startup, migrations or scheduler. No credentials are needed. The existing API CI build job now runs this suite before publishing.

For a separate development host only, the four opt-in flags are `AI:Enabled`, `AI:ChatEnabled`, `AI:MockMode`, `AI:OperationsAgentEnabled`; set all true using existing local configuration conventions. Do not start the full API against Production merely to test mock mode; use the isolated runner above.

Phase D adds one append-only ai_audit_logs table: four ordered event types represent the current single-tool run. A unique run/sequence index, transactionally validated transitions and metadata fingerprint make exact retries idempotent and reject conflicting rewrites. Fingerprints are duplicate-detection checksums, not tamper-proof signatures. The application never edits/deletes audit rows; a database administrator can still alter a database, so no WORM/compliance certification is claimed.

Each write has a five-second budget and its own context/serializable transaction with the existing SQL retry strategy. There is no transaction held across a provider call or business read. Cancellation cleanup has a separate two-second token and is awaited. A run lacking a terminal event becomes an incomplete projection after two minutes; no background job fabricates a terminal outcome. A successful terminal event proves backend completion, not that the client received the HTTP response. Preflight rejections and development mocks do not start durable runs; legacy extraction/invoke are unchanged and are not covered by this new trail.

The opt-in --local-db --isolated check path applies only Phase D Up to a new randomly named LocalDB database, exercises actual SQL and HTTP persistence/failure/cancellation, and removes that database in finally. See tests/Scmos.Ai.Checks/README.md. Default tests still use no SQL/provider credentials. Next is Phase E Control Tower UI; production migration/release review and a live smoke test remain separate gates. No UI, conversation storage, high-risk writes or external dispatch was added in D.

## Phase E — Control Tower (local implementation)

The additive /ai-control-tower entry and sidebar screen now consume the existing Dashboard summary/briefing and Phase B–D status/chat/audit APIs. Dashboard data and Operations evidence retain their different authoritative scopes and time windows. The UI adds no tool handler, metric definition, SQL migration or write path. Prompt/evidence state is component-local, requests are cancellable, no chat POST is automatically retried, and unknown/failed data is unavailable rather than zero. Existing Assistant/approvals remain unchanged.

See [Control Tower source map, controls and QA](SCMOS_AI_CONTROL_TOWER.md). Build, contract checks and isolated headless browser/visual QA pass as of 2026-09-08. Real authenticated integration and production release gates remain pending; see [pre-deployment QA](SCMOS_AI_PREDEPLOY_QA.md). AI flags were not enabled and no live provider call was made.

Official reference: [OpenAI function calling](https://developers.openai.com/api/docs/guides/function-calling). The application owns tool execution; strict provider schemas are supplemented by server-side validation and authorization.
