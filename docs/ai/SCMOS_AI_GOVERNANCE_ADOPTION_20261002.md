# SCMOS AI Governance Constitution — adoption assessment

Date: 2 October 2026 (Asia/Bangkok). Reviewed source: `5c74422c`, Web version
`v2.8.15`, on `azure-dotnet-migration`. Working branch:
`codex/ai-governance-constitution`.

The [original Constitution v1.0](SCMOS_AI_GOVERNANCE_CONSTITUTION_V1.md) is now
preserved as SCMOS product policy, linked from the AI documentation and from
the root repository instructions. All 44 sections are retained. This change
adds documentation and development instructions only. It does not change live
agent behavior, grant permissions, set budgets, migrate a database or deploy.

This assessment comes from source inspection, not a signed-in Production test
or an infrastructure/security certification. Configuration defaults do not
prove effective Production settings. Existing controls are evidence of partial
coverage, not a declaration that SCMOS meets the whole Constitution.

## 1. Architectural facts and components to reuse

The existing boundaries are distributed across components; there is no file
named `PolicyGateway.cs` to replace. Reuse these components rather than create
another orchestrator, gateway, agent or database abstraction:

| Responsibility | Existing component and evidence |
| --- | --- |
| Identity, purpose, tools, capability, design ceiling and page routing | [AgentRegistry](../../server/Scmos.Api/Ai/AgentRegistry.cs), currently 14 entries |
| Caller and agent authorization | [AiPermissionPolicy](../../server/Scmos.Api/Ai/AiPermissionPolicy.cs): recognized internal user, agent capability, team/operator scope and tool ownership are checked separately |
| Connected tool contracts and output shapes | [ToolRegistry](../../server/Scmos.Api/Ai/ToolRegistry.cs), [QueryPolicyGuard](../../server/Scmos.Api/Ai/QueryPolicyGuard.cs): input schema, reviewed source/output metadata, matching agent and audit readiness |
| Chat run and deadline | [AgentOrchestrator](../../server/Scmos.Api/Ai/AgentOrchestrator.cs): checks identity, configuration, connection, governance, limiter and audit before live calls |
| Settings, design ceiling, shadow mode and breaker | [AgentGovernance](../../server/Scmos.Api/Ai/AgentGovernance.cs), [AiGovernanceService](../../server/Scmos.Api/Ai/AiGovernanceService.cs): unknown agent/unavailable governance can refuse; settings cannot raise the code's ceiling |
| Legacy tool catalogue and human approval queue | [AiGateway](../../server/Scmos.Api/Services/AiGateway.cs), [ApprovalPolicy](../../server/Scmos.Api/Ai/ApprovalPolicy.cs): requester/RBAC checks, exact payload hash, expiry and no self-approval in the queue |
| Dedicated Operations changes | [OperationsChangeService](../../server/Scmos.Api/Ai/Operations/OperationsChangeService.cs): a separate human confirmation flow, serializable transaction, current staff and stale job checks, reviewed fields only |
| Auditing | [SqlAiExecutionAudit](../../server/Scmos.Api/Ai/SqlAiExecutionAudit.cs), [AiAuditRules](../../server/Scmos.Api/Ai/AiAuditRules.cs): isolated context, required durable events, append/replay validation and prompt build reference |
| Rate/concurrency/tool bounds | [AiRunLimiter](../../server/Scmos.Api/Ai/AiRunLimiter.cs), [ToolExecutor](../../server/Scmos.Api/Ai/ToolExecutor.cs), [AiOptions](../../server/Scmos.Api/Ai/AiOptions.cs): shared per-instance limiter, per-run tool budget, timeout and output bounds |
| Scheduled runs and dedicated drafts | [AgentScanner](../../server/Scmos.Api/Ai/AgentScanner.cs), [BookingDraftService](../../server/Scmos.Api/Ai/Booking/BookingDraftService.cs), [BookingMailPass](../../server/Scmos.Api/Ai/Booking/BookingMailPass.cs), [ExtractionRun](../../server/Scmos.Api/Ai/Documents/ExtractionRun.cs) |
| Provider boundary | [OpenAiProvider](../../server/Scmos.Api/Ai/Providers/OpenAiProvider.cs): server-side settings, bounded inputs, strict tool output validation, no SDK retries and no parallel tool calls |
| Source/rule provenance | [SourceScopeRegistry](../../server/Scmos.Api/Ai/Semantic/SourceScopeRegistry.cs), [BusinessRuleRegistry](../../server/Scmos.Api/Ai/Semantic/BusinessRuleRegistry.cs), existing specialist answer contracts |

Models receive selected evidence and function declarations, not a SQL
connection or generic SQL tool. However, all agents run inside the API process;
some scheduled orchestration classes receive `ScmosDbContext` directly. These
facts do not prove per-agent credential/network/runtime isolation under
sections 3, 6, 17, 18 and 40. Preserve ordinary server-owned repositories and
audit storage; design any tighter boundary around existing services.

## 2. Agent scope: fourteen, not six

Sections 3 and 35 mention six agents, while sections 1 and the document scope
cover all existing and future agents. Apply the requirements to all fourteen
registered entries. Keep the original wording and existing IDs; do not delete
or duplicate agents to force the roster to six. A registered descriptor is
not proof of a connected executor or Production enablement.

| Agent ID | Present role/path | Required capability in registry |
| --- | --- | --- |
| `operations-agent` | Shipment chat reads and dedicated reviewed change pilot | ViewDashboard |
| `vendor-agent` | Carrier recommendations in scheduled pass | ManageSuppliers |
| `rate-agent` | Rate descriptor; no live chat executor wired by orchestrator | ViewRates |
| `data-agent` | Existing KPI reports through chat adapter | ViewDashboard |
| `incident-agent` | Incident descriptor; no live chat executor wired | ViewDashboard |
| `document-agent` | Paperwork chat, dedicated extraction; billing AI also needs review | UploadDocuments |
| `compliance-agent` | Training descriptor; no live chat executor wired | ManageTraining |
| `management-agent` | Two fixed plans over specialist reads | ViewDashboard |
| `communication-agent` | Message reads and template reminder drafts | ViewMailbox |
| `engineering-agent` | Fixed GitHub metadata and bounded source reads | AdministerData |
| `otd-agent` | Rule-first scheduled risk recommendations | ViewDashboard |
| `validation-agent` | Rule-first scheduled register validation | ViewDashboard |
| `booking-agent` | Pasted booking and unplaced-mail drafts | EditOwnJobs |
| `sre-agent` | Read platform signals and deployment metadata | AdministerData |

Every manifest must record the actual reviewed tools, API paths and scopes of
that agent's entry points. Capability names in this table are user-side gates,
not an agent permission manifest. Human owner, fallback owner, monetary budgets
and infrastructure identity must come from authorized configuration; they are
not inferred from these capability names.

## 3. Action levels conflict with stored autonomy

The Constitution's action classification and existing enums describe different
things. Direct numeric conversion would reinterpret permissions already stored
in `AiAgentConfigs`. No enum values or settings have been changed here.

| Constitution action level | Meaning | Relation to existing code |
| --- | --- | --- |
| L0 | Read / observe | Usually existing `AiAutonomy.Observe` (numeric 1); `AiActionLevel.Read` also 1 |
| L1 | Analyze / recommend | Usually `AiAutonomy.Recommend` (numeric 2) |
| L2 | Prepare / draft | Dedicated draft paths; no separate autonomy enum value |
| L3 | Controlled, explicitly allowed execution | Do not equate to existing numeric 3, which means `ExecuteWithApproval`; existing autonomous ceiling is numeric 4 |
| L4 | Human approval required before high-impact execution | Must use exact approved payload; do not equate to existing numeric 4 (`RuleGovernedAutonomous`) |

Existing `AiActionLevel` additionally uses 0=Unspecified, 3=ApprovalRequired and
4=Restricted. Keep risk, action classification, autonomy, enabled/disabled state
and approval decision as separate fields. In particular Constitution L0 is a
read, whereas legacy autonomy L0 disables the agent. New UI/API wording and
persisted values require an explicit mapping and compatibility tests.

## 4. Coverage of all Constitution sections

Status: **partial** means source demonstrates a control but not the complete
requirement; **gap** means the required contract/enforcement is missing from
the reviewed paths; **unverified** means deployment evidence is needed.

| Sections | Status | Evidence and remaining work |
| --- | --- | --- |
| 1–2, 36–38, 41–44 | Partial | Constitution and repository instructions adopted. Existing architecture and approved deployment workflows remain; runtime governance change review and all section 38 test categories still need a release gate. |
| 3, 35 | Gap | Registry has IDs, names, purpose, capability and tools; there is no complete versioned manifest with owner, API/read/write scope, budget and startup validity gate for every agent. |
| 4–5, 7, 28 | Partial | Chat tool ownership/RBAC/schema/audit checks deny unknown access. Dedicated and legacy paths require the same manifest gate; some constructors explicitly allow absent optional governance, and stored settings need invalid-value testing. |
| 6, 17–18, 40 | Partial / unverified | Model has no generic DB tool. Server provider reuses server credentials; API services have DB access and some scheduled classes receive the context. Per-agent identities, credential broker, isolated runtime and network deny-by-default are not proven. Provider endpoint validation permits configured HTTPS rather than enforcing an approved-host catalogue. |
| 8, 19 | Gap | Constitutional L0–L4 taxonomy differs from both existing enums; approved action/risk mapping is needed. Existing recommendation/draft behavior does not authorize new business writes. |
| 9, 22–23 | Partial | Destructive tools are absent/denied; durable audit is append-checked. Pre-run refusals in `AgentOrchestrator` can return before any audit event; there is no uniform deny + audit + security-event pipeline for all listed forbidden actions. No WORM/DB permission isolation is proven. |
| 10, 20–21 | Partial | Main queue prevents self-approval, binds payload hash and expires it. Operations confirmation has a separate payload with before/after and expiry but no comparison of approver identity to payload requester; high-risk separation must be applied there too. |
| 11–12, 39 | Gap | Management uses fixed plans and checks each target's user permission/enablement. It has no caller/target permission-intersection envelope with expiry: Management's own tool list names plans, while the target's tools are invoked under that target. User authorization alone does not meet the new no-transitive-agent-permission rule. |
| 13–14 | Partial | Provider labels excerpts as untrusted, validates tool arguments and owns no arbitrary executor; Booking verifies cited source values; communication drafts are template-based. All external-document/mail/LINE/billing paths need injection tests and uniform `AI DRAFT` labeling plus commitment approval checks. |
| 15–16, 32–34 | Partial | Team/operator read scope and source/rule answer metadata exist; context memory is optional, transient and separate. Specific shipment/customer/document/date/financial scope minimization and provenance must be manifest-bound at every entry point. |
| 24 | Gap | Shared limiter: 20 starts/minute, 4 concurrent per API instance; configured timeout default 20 seconds and output default 800 tokens. Tool budgets exist. No complete per-agent input/job/API/token quotas, distributed daily limits or daily/monthly monetary reservation/enforcement. Price display is not budget enforcement. |
| 25–27 | Partial | Provider SDK retry count is zero; tool attempts are bounded; Operations applied-state replay and stale fingerprint protect confirmed writes. Proposals themselves lack a general idempotency-key contract; final-state verification must be distinguished from successful HTTP/transaction responses for each execution path. |
| 29–31 | Partial | AI is optional; settings, global/Operations stops, shadow mode and failure breaker exist. Tool/integration/group stops must be checked uniformly. Repeated security denials, anomalous usage/cost and privilege escalation are not a complete persisted `AGENT SUSPENDED` + Security Alert flow. |

## 5. Concrete implementation sequence

The source issues above are not fixed by placing the Constitution in a prompt.
Use the existing components and implement reviewable increments:

1. **Manifest and policy foundation.** Add immutable versioned manifests to the
   existing registry for every agent and dedicated path. Specify exact tools,
   API/read/write scope, action/risk classification, approval rules, owner and
   limits. Missing/invalid/unsupported fields block the affected agent. Extend
   existing governance/read/legacy gateways to evaluate manifests. Require
   explicit reviewed grants for Management plans and the intersection of caller
   delegation scope and target scope. Add the missing Operations requester
   check. Persisted enum meanings remain stable. Test default-deny, permission
   intersection, stale approvals, self-approval and fail-closed services.
2. **Audit, security and verification.** Extend existing audit events and sinks
   with manifest/policy version, explicit risk/verdict, approval/execution link
   and safe security-event references. Cover denied requests and scheduled
   paths. Reuse existing approval rows/transactions for replay protection;
   verify final business state through approved services. Review any additive
   schema changes, audit-failure behavior and operator alert destination.
3. **Budgets and emergency controls.** Enforce per-agent request/job/day/month
   budgets and API/tool/runtime/concurrency caps before provider or execution
   cost is incurred. Use atomic reservations across API instances, count
   attempted calls and retain usage on failures. Missing required price/budget
   blocks billable work. Apply existing kill switches to tools/integrations/
   groups, and introduce reviewed suspension/recovery for security anomalies.
4. **Infrastructure isolation and acceptance.** Establish approved network
   destinations, scoped identity/credential access and sandbox/runtime
   separation. No agent receives Production SQL credentials. Confirm human
   owners and budgets, then run all section 38 categories and manual-mode
   regression tests before using the existing Production deployment process.

The Constitution establishes constraints, not grants for new writes,
credentials, integrations or elevated scope. Owners/fallback owners and
monetary limits remain configuration decisions. Do not fabricate values or
silently mark an incomplete manifest runnable. Infrastructure changes require
the concrete architecture/release review specified by sections 2 and 36.

## 6. Verification of this adoption change

- Original text must match the attached document after line-ending
  normalization; verify that all sections 1–44 occur once and in order.
- Verify every Markdown target introduced here and the root instruction links.
- Check registry inventory against this table and run `git diff --check`.
- No runtime code, model/schema, prompts, permissions, workflow or live AI
  configuration has been changed by this documentation increment. No provider
  key or Production business data was read as part of this assessment.

Runtime implementation and its tests remain the sequence in section 5; this
adoption change is not a report that those controls have shipped.
