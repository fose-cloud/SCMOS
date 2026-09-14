# SCMOS AI Platform Assessment

Phase 0 · reviewed 14 September 2026 (Asia/Bangkok) · repository baseline `a62d35a4d105cd46ee5ddb0deabdffe052441689`, release v2.6.4.

## Scope and evidence

This is a repository architecture assessment, not certification of live Azure configuration, data quality, or end-to-end permissions. The attached SCMOS AI Multi-Agent Platform V1 specification ends with an explicit **Phase 0 only, then stop** instruction. No runtime code, permissions, feature switches, migrations, infrastructure, or production business data are changed by this assessment.

The tracked-file inventory contains 563 files. Discovery covered the frontend, endpoint families, services, rules, EF model/migration inventory, AI runtime, integrations, tests, deployment workflows, and existing documentation. Critical execution/security paths were read directly; this is not a claim that every line of every asset/generated migration designer was manually audited. Searches found no repository AGENTS.md. Secrets and operational workbook exports were not read. Source links below are relative to this document.

Remote `azure-dotnet-migration` was checked on 14 September and matches local HEAD. Latest [API deployment](https://github.com/fose-cloud/SCMOS/actions/runs/34554712836) and [Web deployment](https://github.com/fose-cloud/SCMOS/actions/runs/34554715687) both report success for this SHA. This does not prove that Azure configuration or data have remained unchanged since deployment. No fresh authenticated production session or live SQL schema inspection was performed in this phase.

`next-env.d.ts` was already dirty when discovery began; Next build also generates this file. It is not part of the proposed feature changes and was not staged or committed. Earlier documents dated 7 September are historical: their statements that Outlook/LINE and runtime audit do not exist are no longer current.

## Current System Architecture

SCMOS is an existing modular application, not a new agent application to scaffold. The web and API deploy separately. The database is the operational source of truth; AI must reuse the domain services rather than create its own jobs, customers, rates, or reporting facts.

```text
Browser → Entra/App Service login → Next.js SCMOS shell
        → same-origin /api/* proxy → .NET Minimal APIs
        → capability/scope guards → domain services/rules → EF Core → Azure SQL
                                                        → private Blob / Graph
AI chat → existing AiGateway → AgentOrchestrator → OperationsAgent
        → approved read handler → JobsRepository → source-linked answer
        → ai_audit_logs before/after execution
```

## Frontend Architecture

Evidence: [package.json](../../package.json), [SCMOSApp](../../app/SCMOSApp.tsx), [API client](../../app/scmos/api.ts), [store](../../app/scmos/store.ts), [page cache](../../app/scmos/pageCache.ts), [navigation](../../app/scmos/nav.ts).

- Next.js 16.3.1, React 19.2.6, TypeScript 5.9.3; standalone deployment. Most screens are selected inside SCMOSApp, not one App Router route per module. `/ai-control-tower` already exists.
- Existing component/CSS conventions, Tailwind 4 utilities, local Card/cn helpers, Recharts, SheetJS, and pptxgenjs. Do not introduce a replacement UI framework or assume a complete shadcn component library already exists.
- Same-origin `apiFetch` publishes fetching state. Workspace has paged API reads, full-register consumers, save queue, Excel parsing, selection/paste/navigation/undo helpers, and periodic refresh.
- Page cache uses version/operator-keyed sessionStorage; UI preferences use localStorage. AI prompts/evidence use transient component state, not these caches.
- KPI/report calculations still exist on both TS and C# sides. New AI metrics must not use browser-calculated numbers as their authoritative source.

## Backend Architecture

Evidence: [Program](../../server/Scmos.Api/Program.cs), [project](../../server/Scmos.Api/Scmos.Api.csproj), [Endpoints](../../server/Scmos.Api/Endpoints), [Services](../../server/Scmos.Api/Services), [Rules](../../server/Scmos.Api/Rules).

.NET 10 Web API, Minimal API endpoint modules, DI with scoped services/DbContext, singleton caches/providers, typed records, async/CancellationToken, pure Rules classes. No separate controller layer or Dapper dependency was found. Many endpoints call services, while some also query EF directly; introducing AI adapters must not assume all authorization resides inside services.

Program registers SQL retry policy (5 retries, up to 10-second retry delay), default SQL command timeout 120 seconds, gzip, ProblemDetails/exception handler, optional CORS, optional Application Insights, and `/health` with DB health check. Hosted services include monthly report archive, conditional LINE processing, mailbox processing and Graph subscription renewal. Never start the production host simply to run discovery, because hosted services can process stored work.

## Database Architecture

Evidence: [ScmosDbContext](../../server/Scmos.Api/Data/ScmosDbContext.cs), [Entities](../../server/Scmos.Api/Data/Entities.cs), [Migrations](../../server/Scmos.Api/Data/Migrations).

EF Core SQL Server 10.0.11; 41 migration source files excluding designers/snapshot. Latest migration in source is `20260910023928_SupplierAslBslList`; preceding changes include mail attachment bytes and Communication Center. These are source facts, not a fresh production migration-history query.

`operation_jobs` holds a stable key, denormalized searchable columns and a full `nvarchar(max)` JSON payload. Existing indexes include owner/work_date, owner_id/work_date, cat/status. Dates are often DD/MM/YYYY strings; not all jobs carry valid dates. SQL ownership filtering exists in AI analysis reads, but date/search/category filtering and aggregation currently happen after streaming JSON into the API. No general global EF row-security query filter was found. App-scoped filtering is therefore a required part of every new adapter.

No repository-managed stored-procedure/view abstraction or Dapper usage was found. Explicit SQL exists, including the rate write lock; this is not an arbitrary SQL interface for agents. Untracked/live database objects remain unverified.

## Authentication

Evidence: [UserAccessor](../../server/Scmos.Api/Auth/UserAccessor.cs), [AppUser](../../server/Scmos.Api/Auth/AppUser.cs), [web auth](../../app/auth.ts), [proxy](../../app/api/%5B...path%5D/route.ts).

Reuse Microsoft Entra through App Service Easy Auth. Web forwards platform identity through a server-only shared proxy key; API uses constant-time key comparison. `Platform`, `Proxy`, and development-only identity modes exist. Staff directory and role-map recognition are separate from signing in; unknown users are refused by default. `/api/me` supplies role and owner identity to the UI. Do not create a second login system or accept user/role/owner overrides from AI requests.

## Authorization

Evidence: [Roles](../../server/Scmos.Api/Rules/Roles.cs), [AI permission policy](../../server/Scmos.Api/Ai/AiPermissionPolicy.cs), [AI policy catalogue](../../server/Scmos.Api/Rules/AiPermissions.cs).

Nine existing roles use capability flags. Operation User already has team visibility; the label “Own Workspace” is not evidence of own-row-only reads. AI follows actual ViewTeam, otherwise a nonempty authenticated OperatorId, and rejects carrier/unknown scopes. Commercial reads require ViewRates; mailbox reads require ViewMailbox; AI audit uses ViewAudit. Operation User's supplier-details editing and training editing must be preserved, not confused with supplier approval/deletion or AI approval.

`IUserAccessor.Refuses` checks the configured MFA/sign-in-strength policy; it is **not** a substitute for `user.Can(capability)`. New handlers must check both where applicable. Management dashboard access does not automatically authorize raw job or rate access. Existing service calls must be wrapped in reviewed authorization and output projections.

## Existing Business Domains

The following is a source-backed routing map. Paths under Endpoints and Services refer to `server/Scmos.Api/`; UI lives under `app/scmos/screens/`. Proposed agents are not yet connected to these services unless explicitly identified below.

| Domain / UI | Existing API family and reuse point | Existing tables / facts |
| --- | --- | --- |
| My Job / ImportWorkbook / Postpone | `/api/jobs`, `/page`, `/changed`; JobsRepository, WorkspaceService | operation_jobs, job_delegations, audit_events |
| Booking / workflow | `/api/workflow`; WorkflowService | workflow_events, supplier_requests; no second shipment master |
| Shipment monitor / PreRun / Verification | `/api/shipment`, `/api/monitor`, `/api/risk`, `/api/pre-run`, `/api/verification`; MonitoringService, MonitorService, RiskService | shipment_milestones, delay_records, pre_run_checks |
| Supplier Register / Vendor | `/api/suppliers`; SupplierService, CarrierDirectory | suppliers, supplier_aliases, supplier_contacts, supplier_trucks, supplier_drivers, supplier_evaluations |
| Capacity / Carrier Portal | `/api/capacity`, `/api/carrier`, `/api/vehicle-types`; CapacityService, CarrierService | supplier_capacity, vehicle_types, supplier_requests |
| Job Rotation / customers | `/api/rotation`; RotationService | rotation_assignments; staff and suppliers options |
| Rate Sheet / Rates / quotations | `/api/rates`, `/api/rate-inquiries`, `/api/quote-card`, `/api/journeys`; RateService, RateInquiryService, QuoteSheetService, QuoteCardService | rate_lanes, rate_prices, rate_surcharges, fuel_bands, rate_inquiries, rate_inquiry_lanes, rate_inquiry_prices, quote_vehicle_rates, quote_extras, quote_settings, journey_distances |
| Fuel | `/api/diesel`; FuelLadder, QuoteCalculation | diesel_prices, fuel_bands; effective-date/band basis must be retained |
| L'OREAL / Chemours / Cargo Receipt | `/api/customer-rates`, `/api/cargo-forms`; CustomerDocumentService; job-backed report UI | customer_rate_bands, customer_rate_lanes, customer_rate_prices, cargo_form_templates, operation_jobs |
| KPI / performance / dashboard | `/api/kpi`, `/measures`, `/excel`, `/api/dashboard/today`, `/briefing`; KpiService, KpiEngine, CarrierScorecard, DashboardService | existing jobs/delays/incidents/issues, not new AI facts |
| Report Centre / archives | `/api/reports`; MonthlyReportService, ReportArchiveService, ReportScheduler, ReportWriterService | report_archive; legacy report_uploads, operation_uploads, operation_entries via `/api/uploads`, `/api/operations` |
| Incident & CAR/PAR / issues | `/api/incidents`, `/api/issues`; IncidentService, IncidentImporter, OperationalIssueService | incident_cases, operational_issues, document/audit links |
| Customer Training Control | `/api/training`; TrainingService, TrainingRules | drivers, training_courses, customer_training_requirements, driver_training, customer_training_records |
| Document Center | `/api/documents`, `/api/ai-extract`; DocumentService, BlobFileStore, DocumentExtractor | documents + private Blob objects |
| Mail / Outlook | `/api/mail`, `/api/integrations/graph`, Graph webhook routes; GraphAuth, GraphMailReader, MailWorker, MailLinker, MailFiling | mailboxes, emails, email_participants, email_attachments, email_entities, email_job_links, graph_subscriptions |
| LINE review | `/api/integrations/line`; LineEventWorker, LineMatching, LineAuthority | line_groups, line_users, line_events |
| Identity / administration | `/api/me`, `/api/staff`, `/api/delegations`, `/api/roles`; StaffService, DelegationService | staff, job_delegations |
| Audit / AI controls | `/api/audit`, `/api/ai/*`; AuditService, AiGateway, AgentOrchestrator, SqlAiExecutionAudit | audit_events, ai_tools, approvals, ai_audit_logs, ai_operations_control |

## Existing Data Model

Use stable database IDs/operation key for joins, not job-code or container uniqueness assumptions. One business reference can have multiple job rows. `OperationJob.Data` is interpreted through JobRecord/WorkspaceTabs projections. `drivers` and `supplier_drivers` are different existing entities; do not merge them as part of AI work. Customer names are drawn from rotation/jobs/rate records; no standalone customer master entity was found. Email ConversationId is a Graph conversation identifier, not an AI chat session.

## Shipment Architecture

JobRules, JobStatus, WorkspaceTabs, Formats and MonitorRules define actual categorization, completion, no-date behavior and operational risk. Import/Export/Delivery have different field/status requirements. FCL/LCL and DG are existing operational/quotation fields, not independently managed AI domains. Reuse Excel column/category normalization and backend save validation; a generic prompt must not redefine those values.

Operations AI currently reads active Import/Export jobs only, not completed history or all Delivery jobs. `today`, `risk_today`, `search`, and `delays` have different windows. Its results cannot answer monthly OTD comparisons merely by enabling another flag.

## Vendor / Subcontractor Architecture

SupplierService manages master/details; CarrierDirectory resolves spelling aliases; carrier portal uses its own scope. Rotation uses supplier options for FCL/LCL. A read adapter must project allowed details, not expose contacts/licences/commercial terms indiscriminately. Existing Operation edit rights are not authorization for AI master-data writes.

## Rate Architecture

Buying/selling customer rates, inquiry prices, calculated quote rows and promoted rate-book entries are distinct sources. Preserve source, vehicle, route, customer/carrier, DG, fuel band and currency/basis. Reuse RateService reads and QuoteCalculation/FuelLadder arithmetic; never call promotion or save-to-sheet from a read tool. A stored quote is not proof of an approved effective contract. Missing approval/effective-period evidence must produce INSUFFICIENT EVIDENCE, not an invented agreed rate.

## Billing Architecture

No authoritative invoice/payment entity, BillingService, or invoice approval endpoint was found in the mapped model/service inventory. KpiEngine.Billing explicitly returns unmeasurable; its prose mentions a deadline but there is no invoice evidence to test it against. Billing menu/document receipt is not an invoice ledger. Invoice comparison can later review uploaded evidence against verified rate sources; automated invoice approval/payment is prohibited. Creating a billing system is a separate business scope decision.

## OTD / Performance Architecture

Evidence: [JobRules](../../server/Scmos.Api/Rules/JobRules.cs), [KpiMeasures](../../server/Scmos.Api/Rules/KpiMeasures.cs), [KpiEngine](../../server/Scmos.Api/Services/KpiEngine.cs), [VendorReport UI](../../app/scmos/screens/VendorReport.tsx), [MonitorRules](../../server/Scmos.Api/Rules/MonitorRules.cs).

| Existing calculation | Actual rule | AI consequence |
| --- | --- | --- |
| JobRules.IsOnTime / KPI | Plan date+plan time versus arrival date+arrival time; no grace | Label as this exact KPI, not universal customer OTD |
| JobRules.LateBeyond / monitor time comparison | More than 30 minutes by default | Not the inverse of zero-grace OTD |
| VendorReport UI | User-selectable 0/15/30/60 minutes; defaults to 30 | A selected tolerance is not proof of a contractual customer rule |
| MonitorRules.Judge | Excludes done/cancelled/unreadable plan dates and recorded arrival; overdue/unassigned/no carrier/no truck; near-term 2 days | Reuse priorities; do not invent risk weights or infer late from missing date |
| Workspace delay bucket | Held/delayed status and recorded reason semantics | Not identical to measured arrival lateness or delay_records |

There are six MeasureId members despite comments/catalogue text saying eight. IsMeasurable uses DateNumber checks; DateNumber is not strict calendar validation, while MinutesLate uses Moment and can return null. Impossible calendar dates are therefore a required regression test before publishing an AI KPI. No versioned, customer-effective BusinessRuleRegistry was found. Preserve current calculations and explicitly resolve/label their basis before adding customer-specific rules.

## Audit Architecture

Two existing purposes must remain distinct: `audit_events` records business edits; `ai_audit_logs` records strict execution metadata. AuditService.RecordAsync/RecordManyAsync are best-effort, while Stage participates in the caller's SaveChanges transaction. AI audit creates a separate context with 5-second timeout, serializable append, unique run/sequence, fingerprint/idempotency checks, and refuses result release when required persistence fails.

Current AiAuditRules accepts only operations-agent and the three read tools; fixed sequence 1–4 models exactly one tool per run. AiAuditReader also assumes one tool. This is a major extension point for multi-step/cross-agent execution, not a reason to create a second audit store. Fingerprints are not signed tamper-proof storage. Existing migration Down refuses destructive audit rollback.

No raw prompt, raw provider response, hidden reasoning, or unrestricted tool parameters are stored in this audit. The new brief's request to capture Question/ExecutionPlan must be interpreted as safe redacted intent, approved plan steps and bounded metadata, not hidden chain-of-thought or a silent change to prompt-retention policy. Permission/validation denials before OperationsAgent starts are not all durable execution events today.

## Existing AI Components

Evidence: [AiGateway](../../server/Scmos.Api/Services/AiGateway.cs), [AI directory](../../server/Scmos.Api/Ai), [Control Tower](../../app/scmos/screens/AiControlTower.tsx).

- One public gateway entry for chat, existing orchestrator, agent registry, typed input schemas, strict tool proposal validation, IAiProvider/OpenAiProvider/MockAiProvider, limiter and durable audit.
- Registry lists eight specialist descriptors, but only Operations has connected live handlers: query_shipments, search_shipment, query_delays. Other descriptors/flags are not implemented agents.
- Routing is agentId/page/default selection, not natural-language multi-agent intent routing. One model-selected read, no recursive loop; facts are composed by server code, and raw job records are not sent back to the model.
- Max 4,000 message characters / 16 KiB body; evidence max 50 rows; default request timeout 20 seconds; instance-local 20 starts/minute and four in flight. Existing stricter limits should not be raised to the brief's generic 500/5,000-row examples.
- Admin-only durable Operations switch, revision conflict check, atomic business audit, server emergency stop. WriteToolsReady remains false even if a configuration property is true. Do not alter existing switch state during discovery.
- DocumentExtractor and ReportWriterService call ChatClient directly outside the new orchestrator/audit path. They reuse provider configuration, but not all common safety/telemetry controls; incorporate through compatibility adapters later, not a breaking replacement now.
- No AI conversational session/context store, multi-agent planner, generalized query policy guard, or runtime Engineering/SRE connector was found.

## Existing Integrations

Outlook is no longer merely a placeholder: GraphAuth uses DefaultAzureCredential with an approved-mailbox list; webhook subscriptions, renewal, bounded worker queues, extraction/matching and human link review exist. Mailbox RBAC/Exchange application access must be verified live before widening use. MailEndpoints reads are capability-level, not per-owner shipment filtering.

LINE has signature-verified ingress, unique message IDs, group/supplier mapping, bounded claim/retry worker, parser and human review/apply path. Worker processing is not unattended job modification. However its review-list endpoint checks sign-in rather than an explicit internal/carrier scope and returns RawText; this is a source-level authorization concern to resolve before exposing it to a Communication Agent.

Graph and LINE presence in code does not prove current credentials, subscriptions, mailbox/group setup, or successful live delivery. No Teams ingestion/sending, Power BI embedding/client, or runtime GitHub/Azure Monitor investigation connector was found. ABS/CCS still have placeholder integration definitions. Do not present these as connected tools.

## Azure Architecture

App Service Web/API, Azure SQL, managed-identity-capable private Blob storage, optional Key Vault and Application Insights are wired in source. `infra/setup-storage.sh` and `infra/setup-slot.sh` are provisioning helpers, not proof of current cloud state. No Azure resources/settings/firewall rules were changed or read for secrets in this phase. Live SKU, access restrictions, retention, role assignments, telemetry ingestion, backups and schema drift remain verification items.

## GitHub / CI-CD Architecture

Evidence: [API workflow](../../.github/workflows/api.yml), [Web workflow](../../.github/workflows/web.yml).

API changes pushed to azure-dotnet-migration trigger build/checks and Production deploy including migration bundle application. Web auto-deploys main; manual workflow can select staging/production. Deploy jobs use Azure OIDC, environment targeting, serialized deployment groups, restarts and smoke checks. Web verifies package SHA and running release version; API smoke checks health, not a returned commit identity. API manual dispatch has no slot input despite older setup comments implying one. Environment approval/protected-branch settings were not inspected.

Do not push implementation work to the deployment branch casually. Future work should use a reviewed `codex/` branch; no push/deploy is performed in Phase 0.

## Logging / Monitoring

ILogger/ProblemDetails, DB health and conditional Application Insights already exist. Orchestrator logs run ID/agent/elapsed; AI audit stores timestamps/token usage. No uniform custom ActivitySource/Meter, per-tool latency contract, or end-to-end correlation propagation across proxy/model/Graph/SQL was found. The proxy currently does not forward Retry-After among response headers, so downstream 429 retry guidance is lost. This is an extension proposal, not a Phase 0 fix.

## Reusable Components

Reuse SCMOS shell, apiFetch, Control Tower parsers/status/evidence UI, local design primitives/Recharts, existing gateway/orchestrator/provider interfaces, registries, Roles/AppUser, Formats, JobRules, MonitorRules, KPI/rate services, Blob/document APIs, mail/LINE ingestion, approvals and both audit purposes. Reuse existing check projects and Node test runner.

## Architecture Gaps

Priority gaps: versioned semantic/rule descriptors, natural-language intent resolution, agent-neutral response/evidence envelope, typed executor policy, generalized audit sequencing, secure approval lifecycle, bounded conversation context, granular integration scopes, and per-agent readiness. There is no need to introduce microservices, a separate Python agent stack, vector DB, queue infrastructure or a second gateway merely to satisfy the logical diagram.

## Security Risks

| ID / priority | Evidence and limitation | Required treatment before expansion |
| --- | --- | --- |
| S1 / high | SupplierEndpoints `/ai/approvals` lists payloads for any recognized user; queue creation is sign-in gated, decision is supervisor-gated | Restrict listing/creation by actor/target/action scope; add carrier/role/IDOR tests before using the queue for sensitive proposals |
| S2 / high | MarkApplied changes approval state/result only; no actual reviewed executor; decision and business audit use separate saves | Do not equate applied marker with business execution; introduce reauthorization, payload binding, concurrency and atomic/idempotent execution before any writes |
| S3 / high | Several legacy read endpoints/services have sign-in-only or aggregate guards (KPI, document metadata, LINE review) | Build narrow scoped projections; do not hand arbitrary service APIs directly to the model. Exploitability needs role/session testing; this phase did not demonstrate a live leak |
| S4 / high | DocumentExtractor/ReportWriter bypass shared run limits/audit; extraction may return provider exception message | Sanitize legacy error path, integrate shared controls and model-call auditing before exposing them as general agent tools |
| S5 / medium | Shared Graph managed identity may serve identity administration and mail; actual live grants unverified | Verify least privilege and mailbox restriction; prohibit model-selected URLs, credentials and permission changes |
| S6 / medium | AI audit fields can reveal actor/source keys; append-only is application policy | Preserve ViewAudit and define redaction/retention; do not promise database-level immutable evidence without verifying controls |

## Data Risks

- Multiple date/measurement implementations; Bangkok-aware Formats versus DateTime.Now/DateTimeOffset.Now in some dashboard/workspace/KPI paths. Cross-midnight results can depend on host timezone. Do not infer live failure without measurement.
- Operation JSON versus denormalized columns, malformed/no-date rows, multi-row job codes/containers, aliases and unmatched source links. Quantify with read-only scoped checks later; no current counts are asserted here.
- OTD scope/denominator/grace differences and no verified customer-effective rule table. A UI tolerance is not contractual evidence.
- Empty billing/evidence must be UNKNOWN/NOT ASSESSABLE, not zero or success.
- Query output cap is not a source-scan cap: AI reads the whole permitted register; WorkspaceService pages after full cached register materialization. Cache is instance-local (5 minutes), and cross-instance invalidation needs review. New analytics must aggregate/filter at the server with truthful completeness metadata, never silently truncate totals.

## AI Integration Risks

The new six agents differ from the existing eight descriptors. Preserve persisted IDs/categories and map new logical agent responsibilities explicitly. A broad “data agent” cannot inherit every domain's permissions. Input validation/prompt warnings help but do not replace field/row authorization or output validation. Model-generated causal explanations must cite supported evidence and distinguish hypotheses. Multi-step loops need budget and cancellation limits; audit must support their actual event topology before enablement.

## Recommended Multi-Agent Architecture

See [Architecture](SCMOS_AI_PLATFORM_ARCHITECTURE.md). Incrementally extend the existing .NET runtime with reviewed adapters, not a parallel agent backend. Maintain read/recommend only until a separately accepted approval-execution design is implemented and tested.

## Existing Components To Reuse

AiGateway; AgentOrchestrator; IAiProvider; AgentRegistry/ToolRegistry; AiPermissionPolicy; SqlAiExecutionAudit; approvals/audit tables; SCMOS services/rules and identity; Control Tower and same-origin proxy. Source-level source-of-truth remains Azure SQL and approved integration stores.

## Components To Extend

Audit vocabulary/sequencing/read projections, agent dispatch and result contract, tool metadata/policy, scope-aware domain adapters, safe provider reuse for document/report calls, admin readiness UI, existing approval lifecycle and centralized rule provenance.

## New Components Required

Proposed, not created in Phase 0: bounded IntentRouter, agent-neutral ToolExecutor/QueryPolicyGuard, SemanticRegistry, BusinessRuleRegistry metadata adapters, scoped conversation-context service, per-domain agent adapters and safe external read connectors. Prefer named focused types under the existing Ai directory, registered through AiServiceRegistration.

## Database Changes Required

**None for Phase 0. None proven mandatory for the first read-only foundation slice.** Existing tables can support registry metadata in code and one-tool pilot execution. General approval execution likely needs additive fields on approvals (actor ID, target/version, expiry, payload fingerprint/schema version, correlation/execution identity, concurrency token); multi-step audit needs a compatible event representation/version and possibly additive correlation/step/rule metadata. Persistent conversations or administrator-edited effective rules need a separately reviewed persistence proposal, not speculative DDL now.

## Database Changes NOT Required

No duplicate shipment/customer/vendor/rate/billing/audit/gateway tables. No data copy to an AI warehouse, no renaming existing tables, no destructive cleanup, no blanket date conversion, no invoice table solely to make a demo answer work. Do not reuse Graph conversation_id as AI chat state.

## Files To Modify

Only documentation is authored in this phase. Future candidates: Ai/AgentOrchestrator.cs, Ai/AgentRegistry.cs, Ai/ToolRegistry.cs, Ai/AiContracts.cs, Ai/AiPermissionPolicy.cs, Ai/AiAuditRules.cs, Ai/AiAuditReader.cs, Ai/AiOptions.cs, Ai/AiServiceRegistration.cs; Services/AiGateway.cs; existing AI/approval endpoints; Control Tower and aiControl.ts parsers. Domain services only where scoped reads/rule extraction are justified. See the implementation plan for sequencing.

## New Files Proposed

This phase creates SCMOS_AI_PLATFORM_ASSESSMENT.md, SCMOS_AI_PLATFORM_ARCHITECTURE.md and SCMOS_AI_IMPLEMENTATION_PLAN.md in docs/ai. Future runtime files and remaining security/catalogue/rule/approval/data-policy/test documents are proposed in the plan. Existing similarly named historical documents are retained rather than silently overwritten.

## Migration Requirements

No migration created, generated, applied or rolled back. Before any future migration: compare deployed schema/history read-only, list exact table/column changes, review additive SQL and runtime/snapshot parity, verify backups and compatibility, and define roll-forward/application rollback while retaining audit records. Existing AI audit migration explicitly prevents destructive Down.

## Testing Strategy

Current baseline on the unchanged source: Node tests 485/485; frontend production build passes; API Release build passes with 0 warnings/errors. Resumed checks on 14 September: AI offline suite 229/229; lint exit 0 with three existing warnings (layout custom font, Chrome img, CargoForm img). AI test build emitted existing EF1002 warning at AuditChecks.cs:288 in the opt-in SQL test code; default run used no live SQL/provider. These checks are not production penetration/load/E2E tests.

Expand tests for routing, strict schemas, cross-role/carrier/owner scope, customer/trucker/date filters, impossible dates/Bangkok midnight, unknown rules, KPI denominator parity, bounded source reads, cancellation, injection, redaction, stale context, audit failure/multiple steps, approval IDOR/expiry/concurrency/replay, and UI loading/errors. Runtime-model parity is covered offline; live migration history remains unverified.

## Discovery checklist coverage

| Specification items | Evidence reviewed / outcome |
| --- | --- |
| 1–7 solution/frontend/backend/APIs/controllers/services | server/Scmos.slnx, project/package manifests, Program, all endpoint-family inventory, SCMOSApp/screens; Minimal APIs, not MVC controllers |
| 8–17 repositories/EF/Dapper/context/entities/DTOs/migrations/SQL/views | Data inventory, ScmosDbContext, entities/records, JobsRepository.Analysis, migration inventory, RateWriteLock; no Dapper or managed view/SP layer found |
| 18–22 identity/RBAC/Entra/roles | auth.ts, proxy, UserAccessor/AppUser, Roles, AiPermissionPolicy; no live Entra policy verification |
| 23–36 shipment/booking/trucking/customer/vendor/truck/driver/container/import/export/FCL/LCL/DG | domain table above, JobRecord/JobRules/WorkspaceTabs, rotation/supplier/training models, Excel/category helpers; existing domain structure preserved |
| 37–44 rate/fuel/billing/invoice/OTD/delay/performance/CAR-PAR | rate/quote/fuel services/rules and entities; KPI/monitor/report implementations; billing source absent; IncidentImporter exists |
| 45–50 audit/logging/error/Azure/Monitor/App Insights | both audit paths, Program, Ai provider, workflows; telemetry wiring exists, actual Azure telemetry not tested |
| 51–57 GitHub/AI/dashboard/Power BI/Outlook/LINE/notifications | workflows and remote status, Ai runtime/UI, dashboard/notification endpoints, Graph/mail/LINE code; Power BI/Teams runtime not found |
| 58–62 files/Blob/testing/conventions/DI | BlobFileStore, DocumentExtractor/DocumentService, tests, Program and AiServiceRegistration |
| 63–70 fetching/charts/dates/Thailand/flags/config/secrets/health | apiFetch/store/pageCache, Recharts manifest, Formats and host-clock callers, AiOptions/control service, Program configuration paths, `/health`; secret values not accessed |

## Phase report

Phase 0 repository assessment and design delivered. Files created: three documents listed above. Runtime files/APIs/tools/agents/security grants added: **none**. Components reused in the design: existing runtime, rules/services, identity and audit. Database changes/migration: **none**. No push/deploy or AI toggle changes. Known risks: S1–S6 and data/performance gaps above. Remaining work: review this plan, verify deployment-specific scope/schema when needed, then begin the first gated Phase 1 slice; do not enable all agents simultaneously.
