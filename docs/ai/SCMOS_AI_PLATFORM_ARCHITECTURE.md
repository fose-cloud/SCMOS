# SCMOS AI Multi-Agent Platform Architecture

Phase 0 proposal · 14 September 2026 · baseline `a62d35a` (v2.6.4).

Status: **design only, not implemented or enabled**. [Assessment](SCMOS_AI_PLATFORM_ASSESSMENT.md) contains actual-code evidence and limitations. [Implementation plan](SCMOS_AI_IMPLEMENTATION_PLAN.md) defines gates. This document supersedes neither historical release records nor existing business rules.

## 1. Architecture decisions

1. Extend the existing .NET modular application, AiGateway and AgentOrchestrator. No new backend, second gateway, duplicate SQL schema, or authentication system.
2. Retain the existing Operations read-only experience while adding one specialist at a time behind server-controlled readiness.
3. Keep the application in charge of user identity, scope, rules, calculations, tool dispatch, approval, audit and limits. The model may propose intent/tool arguments and write bounded explanatory prose; it cannot authorize itself.
4. Reuse domain services directly inside the API process where safe. Do not make loopback HTTP calls simply to match a logical diagram. Service calls still need the same capability/row/field checks as the API they replace.
5. Preserve unsupported/unknown/incomplete outcomes. A named menu, configured flag, seeded tool row or model response does not establish a connected data source.
6. Read and Recommend are the V1 action levels. Approval infrastructure does not grant business mutation tools. Payment approval, autonomous deployment, infrastructure modification, unrestricted SQL and permission changes remain unavailable.

## 2. Target execution flow

```text
SCMOSApp / existing AI Control Tower
    │ signed-in, same-origin request; no role/scope overrides
Next.js /api proxy → /api/ai/chat → AiGateway
    │
AgentOrchestrator
    ├─ authenticate + capability/scope + enablement + request budget
    ├─ IntentRouter → bounded plan or clarification/unsupported
    ├─ SemanticRegistry + BusinessRuleRegistry → verified definitions
    ├─ audit start → ToolExecutor / QueryPolicyGuard
    │       ├─ Data adapter → scoped KPI/report/rate reads
    │       ├─ Operations adapter → existing OperationsReadService
    │       ├─ Communication adapter → authorized stored mail/LINE
    │       ├─ Document adapter → authorized files + verified rate evidence
    │       ├─ Engineering adapter → allowlisted GitHub read client
    │       └─ SRE adapter → allowlisted telemetry/deployment read client
    │              ↓
    │       Existing SCMOS Services / approved external readers
    │              ↓
    │       Azure SQL / private Blob / authorized integrations
    ├─ validate typed results, rule basis, source references and completeness
    ├─ compose evidence-based response; audit completion before release
    └─ future proposal → existing approvals → human review (no V1 execution)
```

Authorization and audit wrap **each** step, not only the first request. Changing agent, tool or conversational context cannot widen access. A failed audit stops execution/release, while ordinary SCMOS screens remain available.

## 3. Existing versus proposed components

| Logical component | Existing implementation | Additive design |
| --- | --- | --- |
| Gateway | Services/AiGateway.cs | Keep entry point, gradually align legacy invoke/approval paths with typed policy |
| Orchestrator | Ai/AgentOrchestrator.cs | Agent-neutral dispatch; explicit bounded plan, deadline and failure policy |
| Provider | IAiProvider, OpenAiProvider, MockAiProvider | Preserve abstraction; add necessary multimodal/draft request contracts before adapting legacy callers |
| Agent registry | Eight existing descriptors; only Operations connected | Add new logical agents without renaming historical IDs; distinguish descriptor/enabled/connected/ready |
| Tool registry | Three Operations read handlers | Add reviewed descriptors with action level, source, version, limits and output contract |
| Permission guard | AiPermissionPolicy + Roles + AppUser | Introduce domain-specific scoped contexts, not a universal Team boolean for all data |
| Query guard | Strict input schemas + scoped Operations query | Extract common budget/argument policy; per-domain date/range/field/capability validation |
| Semantics/rules | Existing Rules and service calculations | Read-only, versioned descriptors pointing to actual code and declared inputs/denominator |
| Approval | approvals + AiGateway queue/decision/marker | Harden existing lifecycle before connecting any executor; do not create a duplicate queue |
| Audit | ai_audit_logs, SqlAiExecutionAudit, AiAuditReader | Backward-compatible multi-step event model plus safe intent/rule/plan metadata |
| UI | AiControlTower, aiControl.ts parsers, existing shell | Evolve Ask SCMOS in place; preserve current route/navigation and failures |
| Context | Current message and page only | Owner-bound, expiring structured filters; no transcript persistence by default |
| Observability | ILogger, optional App Insights, health | Correlation, per-step timings/failure classes and bounded usage, no secret/raw-prompt logs |

## 4. Six-agent mapping

| Target agent | Reuse / current status | First safe increment | Not permitted |
| --- | --- | --- | --- |
| Data | Existing KPI/vendor/rate/management descriptors; KpiService/KpiEngine, MonthlyReportService, CarrierScorecard and rate readers | One verified KPI/volume query with customer/trucker/date/scope and measurable/excluded counts | Invoice facts without a source; changing rates or operational rows |
| Operations | Existing operations-agent and three live handlers | Preserve current behavior, add missing-ETA/assignment queries only with declared existing rule semantics | Automatic assignment, status changes, cancellation |
| Communication | Graph/mail and LINE stores/parsers/workers exist; no connected AI communication handler | Authorized search/thread summary and labeled extraction/draft | Sending messages or applying extracted facts automatically |
| Document & Invoice | DocumentExtractor, DocumentService, BlobFileStore and rate services | Safe document extraction/reference projection; discrepancy analysis where inputs/rate approval can be verified | Approving invoices/payment or inventing contract/fuel terms |
| Engineering | Repository/workflow exists, no runtime connector | Read-only allowlisted repository/commit/issue evidence; suggested patch as text | Merge/push/deploy or arbitrary shell execution |
| SRE | Health/ILogger/App Insights wiring; no runtime investigation connector | Restricted metrics/error/deployment summaries with safe identifiers | Restart, rollback, firewall/secrets/resource changes |

Existing `kpi-agent`, `vendor-agent`, `rate-agent`, `management-agent` may remain compatibility entry points mapped to Data capabilities. Do not expose all their capabilities as one broad grant. Incident/compliance adapters can later be delegated by Operations/Data with explicit domain permission. Existing billing-agent stays unavailable until evidence exists.

## 5. Tool and scope contract

Each executable tool must declare tool ID/version, agent allowlist, required capabilities, **action level separately from confidentiality risk**, strict input/output schema, source, rule references, deadline/row limits, audit policy and approval requirement. Unknown or unconnected tools are refused. Existing AiRisk.Low describes the current runtime gate; it should not be mistaken for a guarantee that operational data is low sensitivity.

Proposed trusted scopes (names illustrative): operational owner/team; analytics aggregate versus row evidence; rate source/customer/vendor visibility; mailbox/group allowlist; document owner/linked-job; repository/ref allowlist; telemetry resource/time window. Resolve them from current user and server policy, never from model-provided owner IDs, URLs or SQL.

QueryPolicyGuard does not parse arbitrary LLM SQL because no such tool is allowed. It validates typed inputs, date limits, output projections and budgets, then calls a known handler. Use server-side aggregation where possible. Keep Operations evidence cap at 50 and its current strict timeout; do not adopt 5,000 raw rows just because the specification suggests a maximum. If a scan budget is exceeded, refuse or explicitly return incomplete, never label sampled totals as complete.

## 6. Business semantic and rule registry

Start with code metadata that references current implementations; no rule editor/database is required for the first slice. A descriptor contains stable rule ID/version, source symbol, meaning, category/customer applicability if verified, input fields, calculation/denominator, exclusions, grace and effective interval when known. `unknown` is an explicit value, not a default permission to guess.

Initial entries must distinguish:

- Operational job key versus job code/container; active Import/Export versus Delivery/history.
- Zero-grace JobRules.IsOnTime KPI versus 30-minute LateBeyond and selectable VendorReport tolerance.
- Measurable timestamp evidence versus missing/malformed records.
- MonitorRules risk window versus WorkspaceTabs delay versus recorded categorized delays.
- Rate inquiry/calculated/promoted/customer buying/selling sources and fuel basis.
- Recorded CAR/PAR/issue facts versus proposed cause; uploaded invoice text versus verified invoice ledger.

Do not manufacture per-customer effective dates or claim a UI tolerance is a verified business contract. If the applicable rule cannot be verified, answer that explicitly. A business-approved rule change belongs in a separate versioned change with regression examples.

## 7. Response and conversational context

Evolve the current AiChatResponse additively (or version explicitly) so existing Operations UI remains valid. Proposed envelope: request/run ID, agent, code, summary, typed metrics/table/chart, applied filters, rule descriptors, sources, assessed/excluded counts, warnings, recommendations, suggested questions, actions and audit reference. Chart data is a projection of verified aggregates; do not duplicate calculation logic in React.

Each metric carries unit, numerator/denominator where meaningful, period/timezone, evidence source and completeness. Null is unknown; zero is a measured zero. Distinguish FACT, CALCULATED, SUGGESTED and UNKNOWN. Sanitize presentation; no raw model HTML or arbitrary navigation URLs.

Context pilot should retain only structured customer/trucker/date/metric selections within a session. Server-issued context ID is bound to user, authorization revision/scope and expiry. Recheck access for each follow-up; discard incompatible context on user/role change. Do not store prompt/evidence in general page caches, reuse Graph ConversationId, or replay old evidence as current. Persistence is a later decision with retention and an additive schema proposal.

## 8. Approval model: design gate, not enablement

Existing pending/approved/rejected/applied states are a starting point, not a safe business executor. Required future states include explicit cancellation/expiry and execution outcome; human approval is never synonymous with successful application.

Before future execution: immutable normalized payload with hash/schema version; source evidence and target version; requester stable ID; approver action/target permission; expiry; self-approval policy; compare original state; reauthorize at execution; idempotency key; exactly-once logical outcome under retries; atomic business mutation/audit where possible. External sends need an outbox/receipt reconciliation design before they are allowed. Rejected/expired/conflicted proposals do not execute. Do not let client-supplied result text mark a business operation successful.

Reuse and extend `approvals` only after inspecting live schema. Existing approvals have no typed target version, stable requester identity contract, expiry execution guard or idempotent executor. These are explicit blockers for write enablement; no action adapter will be wired in the read-only pilot.

## 9. Audit compatibility and observability

Retain `ai_audit_logs` and existing event IDs. Current four fixed sequence values and Operations-only validation cannot record arbitrary step graphs. Introduce a versioned event vocabulary/monotonic sequence plus stable step/parent correlation, preserving the historical reader. Test old rows, new one-step rows and multi-step rows before deployment. Decide whether existing columns can hold the first increment; do not generate a migration before the contract is settled.

Record sanitized intent, approved plan summaries, safe parameters, rules/source IDs, counts, timings, status, usage and approvals. Do not store hidden reasoning. Keep raw prompts/responses disabled unless a separately approved retention/redaction design requires them. Record permission denials safely without leaking the forbidden resource in an error or creating unauthenticated log amplification.

Use run ID across UI/API/tool events; add ActivitySource/ILogger correlation with bounded dimensions. Avoid user prompt/customer names as metric labels. Preserve current provider error sanitization and extend it to legacy document/report callers. Restore safe Retry-After forwarding where reviewed. Instance-local rate limiting needs a scaling/cost policy before multi-instance expansion.

## 10. Persistence and deployment decisions

Phase 0: documentation only. First Phase 1 slice: interfaces/metadata/tests and backward-compatible read-only dispatch, no mandatory database change. Later candidates: additive approval fields, versioned audit step metadata, expiring conversation store, effective-rule store if editable configuration is approved. No invoice/customer/shipment duplicates.

Use the existing CI/CD process and a non-deploying `codex/` branch for implementation review. API pushes to the existing deployment branch apply migration bundles automatically, so do not merge until SQL compatibility has been reviewed. Do not assume API staging exists from the Web slot selector. Roll back code/flags while retaining audit/data; no destructive down migration. Existing Operations enabled state is preserved unless a release owner explicitly changes it.

## 11. Unresolved decisions before later phases

- Which verified customer KPI rules/effective dates are authoritative? What is the intended metric when the user says OTD without a qualifier?
- Which roles get aggregate-only data versus detailed rate/mail/document evidence? Existing capabilities are the ceiling, not a reason to grant every new tool automatically.
- Which approved mailbox/LINE groups/repositories/telemetry resources are in scope? What retention/redaction applies?
- Is persistent follow-up context needed, or is short-lived structured context enough for V1?
- What is the required approval separation-of-duties and target-concurrency policy? No write enablement until decided and tested.

These do not block delivery of Phase 0. They are phase-specific gates, not assumptions to hide inside implementation.
