# SCMOS AI Multi-Agent Implementation Plan

Phase 0 deliverable · 14 September 2026 · baseline `a62d35a` / v2.6.4.

Read with [Assessment](SCMOS_AI_PLATFORM_ASSESSMENT.md) and [Architecture](SCMOS_AI_PLATFORM_ARCHITECTURE.md). **No Phase 1 implementation starts in this delivery.** The specification's final stop instruction takes precedence over its earlier general roadmap language.

Update, 14 September 2026: following separate user approval, Phase 1A is now
implemented and verified locally. See [Phase 1A implementation record](SCMOS_AI_PHASE_1A.md).
The original Phase 0 scope above is historical; no push/deployment is included in 1A.

Following the user's separate confirmation of supervisory approval, a bounded
[Operations reviewed-change pilot](SCMOS_AI_OPERATIONS_WRITE_PILOT.md) was implemented
locally. It reuses approvals without migration; it is not completion of the general
multi-agent dispatch/approval roadmap and does not yet enable model-issued writes.

Update, 20 September 2026 (v2.7.51): 1A, 1B and 1C are implemented and deployed
with the ordinary releases; 1D and 1E are not started; no agent beyond Operations
is connected. The assessment's re-assessment section (20 Sep) recommends 1E, then
1D, then the Data Agent's first read. LINE (live 16 Sep) is the working reference
for Phase 4's rule-first / model-second shape.

Update, 20 September 2026 (v2.7.59): **1E is implemented and deployed** — see the
[Phase 1E record](SCMOS_AI_PHASE_1E.md). The six additive `approvals` columns §4
proposed were reviewed and applied by that release. 1D is next.

Update, 20 September 2026 (v2.7.60): **1D is implemented and deployed** — see the
[Phase 1D record](SCMOS_AI_PHASE_1D.md): multi-step audit topology (dispatch still
one read), correlation id proxy → API → audit, in-memory context pilot behind
`AI:ContextEnabled`. **Phase 1 is complete.** Phase 2 (Data Agent) is next.

Update, 21 September 2026 (v2.7.61): **Phase 2 is implemented and deployed** — see the
[Phase 2 record](SCMOS_AI_PHASE_2.md): the Data Agent's one read (`query_kpi`) over
`KpiService` with the provenance block, behind `AI:DataAgentEnabled` (off). Phase 3 is next.

Update, 21 September 2026 (v2.7.62): **Phase 3 is implemented and deployed** — see the
[Phase 3 record](SCMOS_AI_PHASE_3.md): `query_followup` with four views on the bell's and
the LINE chase's own rules (missing_truck, no_carrier, unreported, container_mismatch).
Phase 4 is next.

Update, 21 September 2026 (v2.7.66): **Phase 4 is implemented and deployed** — see the
[Phase 4 record](SCMOS_AI_PHASE_4.md): the Communication Agent's one read (`query_messages`)
over the LINE and mail ledgers as already read, phones masked and drivers removed, behind
`AI:CommunicationAgentEnabled` (off). Phase 5 is next.

Update, 22 September 2026 (v2.7.70): **Phase 5 is implemented and deployed** — see the
[Phase 5 record](SCMOS_AI_PHASE_5.md): the Document & Invoice Agent's one read (`query_documents`)
over the documents table and the register by the checklist, billing and compliance rules the screens
already apply, behind `AI:DocumentAgentEnabled` (off); the Workspace's document reader under the
shared limiter and audit (S4). No invoice ledger; insufficient evidence is stated, not filled.

Update, 21 September 2026: invoice **amount** comparison remains deferred
because no authoritative invoice ledger or approved-rate/terms data is available. Phase 6 has a
first Administrator-only, read-only GitHub metadata slice behind
`AI:EngineeringAgentEnabled`; see the [Phase 6 record](SCMOS_AI_PHASE_6.md).

Update, 22 September 2026 (v2.7.75): **Phase 7 is implemented and deployed** — see the
[Phase 7 record](SCMOS_AI_PHASE_7.md): the SRE Agent's one read (`query_platform`) over what the
platform knows about itself and the repository's workflow runs, behind `AI:SreAgentEnabled` (off);
no telemetry connector, and none claimed. Phases 8–9 remain.

## 1. What is already present

Gateway/orchestrator/provider abstraction, eight registry descriptors, three connected Operations read tools, strict input validation, per-instance limits, durable AI audit, admin-only Operations switch, read-only Control Tower, existing approval queue, Entra/RBAC, operational/KPI/rate/document services, Graph mail and LINE ingestion/review, Blob, CI/CD and offline checks. Do not rebuild these.

Not yet present as a complete platform: six connected agents, general intent planning, versioned rule resolution, multi-step audit, secure approval execution, conversation context, Engineering/SRE connectors, authoritative invoice ledger. Descriptors and setup instructions are not completed integrations.

## 2. Ordered delivery slices

| Phase | Concrete work | Acceptance gate | Database impact |
| --- | --- | --- | --- |
| 0 — discovery | Inventory, evidence-backed assessment, architecture, risks and this plan | Documents reviewed; scope/limits explicit | None |
| 1A — shared contracts | Introduce action level separate from risk; tool policy metadata; typed agent execution interface; compatibility tests for current Operations/status/response | Current 3 tools and switch remain unchanged; unknown tools/scopes refused; no new handler exposed | None expected |
| 1B — guarded dispatch | Extract reviewed ToolExecutor/QueryPolicyGuard; declared budgets; deterministic intent/clarification for connected tools only; per-agent readiness | Unauthorized or unsupported intents never select a broader agent; no browser-supplied role/SQL | None expected |
| 1C — semantic/rule metadata | Map current zero-grace KPI, 30-minute lateness, delay bucket and source definitions; version the descriptors | Definitions trace to current code; unverified customer contract returns unknown; no changed KPI behavior | None; code metadata initially |
| 1D — audit/context/control | Generalize execution audit/read projections, add redacted plan/rule/correlation metadata; bounded owner-bound context pilot; extend Control Tower parsers | Historical audit still readable; before/after persistence mandatory; replay/cancellation tested; no writes unlocked | Only if a reviewed event/context contract requires additive columns/table; decide first |
| 1E — approval hardening | Restrict existing queue listing/creation; typed proposal validation; design expiry/cancellation/stale state and actor/target checks; no business executor | Carrier/IDOR/self-approval/replay cases tested; approved/applied distinction truthful; no automatic mutation | Proposed additive approvals fields after schema review |
| 2 — Data Agent | First add source-backed volume/OTD read for an explicit rule and bounded period; customer/trucker filters; then comparisons/vendor breakdown | Match source metrics and denominators; invalid dates/exclusions visible; role-scoped SQL aggregates; no invoice invention | None by default; justified index only after measured query review |
| 3 — Operations extension | Preserve existing active Import/Export behavior; add precisely named missing-ETA/truck queries, evidence-driven recommendations | Match existing operational rules; category/history scope explicit; no status/assignment writes | None expected |
| 4 — Communication | Reuse mail/LINE persistence, matching/review and mailbox/group policy; search, summary and extracted suggestions/drafts | Real authorized source test; carrier/PII isolation; injection tests; no send/apply action | Reuse existing tables; no second inbox |
| 5 — Document & Invoice | Adapt DocumentExtractor and rate readers into common provider/audit controls; compare only verifiable evidence | Missing approval/fuel/effective terms reported as insufficient evidence; no invoice/payment approval | No invoice ledger for AI demo; separate domain decision if needed |
| 6 — Engineering | Allowlisted read-only repository/issues/commits/PR metadata connector and suggested fixes/tests | Untrusted issue content cannot execute commands, merge, push or deploy | No business DB changes expected |
| 7 — SRE | Restricted telemetry/health/deployment reader with correlation and safe redaction | Source-backed time window/correlation; no restart, rollback, secret or firewall action | No business DB changes expected |
| 8 — collaboration | Bounded multi-step orchestration only over accepted adapters; independent authorization per step | Audit every step; bounded calls/time/tokens; partial failures cannot imply causal certainty | Reuse versioned audit/context design |
| 9 — hardening | Performance/security/role E2E, schema compatibility, retention, monitoring, release/rollback rehearsal | Signed-off scoped pilot; deployment identity/health verified; no regression of normal SCMOS | Reviewed additive changes only |

**Phase 9 delivered 22 September 2026** (v2.7.84) — see [the record](SCMOS_AI_PHASE_9.md): the access matrix and the kill switches in the AI checks, `--report-ai-audit` for retention, the rollback drill written out, and every agent verified against Production by an authenticated question. What the phase measured rather than fixed: the whole-register read, which `docs/REGISTER_READ_PLAN.md` addresses next.

Implement in this order, not all agents simultaneously. Phase 1E enables governance infrastructure only. Financial approval/payment and autonomous production/infrastructure actions remain excluded from V1 even if a generic future approval framework exists.

## 3. First implementation package after review

Start with Phase 1A, not Data Agent features. Keep Operations as the regression baseline and preserve `operations-agent` IDs/current contracts. Add focused contracts/metadata and tests; defer multi-step execution until audit supports it. No provider change, credential replacement, production toggle, permission expansion, migration or deployment is required for this slice.

### Existing files likely to change, gated by slice

All backend paths below are relative to `server/Scmos.Api/`.

| Slice | Existing files to extend | Constraint |
| --- | --- | --- |
| 1A–1B | Ai/AiContracts.cs, ToolRegistry.cs, AgentRegistry.cs, AgentOrchestrator.cs, AiPermissionPolicy.cs, AiOptions.cs, AiServiceRegistration.cs | Backward-compatible existing Operations calls; current limits retained |
| 1C | Read adapters referencing Rules/JobRules.cs, MonitorRules.cs, WorkspaceTabs.cs, KpiMeasures.cs | Do not change formulas while merely describing rules |
| 1D | Ai/IAiExecutionAudit.cs, AiAuditRules.cs, AiAuditReader.cs, SqlAiExecutionAudit.cs; Endpoints/AiChatEndpoints.cs, AiAuditEndpoints.cs | Old events/clients supported; schema change separately reviewed |
| 1E | Services/AiGateway.cs, Endpoints/SupplierEndpoints.cs AI approval section; Data/SupplierEntities.cs and ScmosDbContext.cs only if fields approved | No claim that MarkApplied executes a command; no parallel approval table |
| 2 | Services/KpiService.cs, KpiEngine.cs, MonthlyReportService.cs and Data/JobsRepository analysis projection as necessary | Scoped source reads; aggregate permission not raw detail permission |
| 4–5 | Existing mail/LINE/document endpoints and adapters; Services/DocumentExtractor.cs, ReportWriterService.cs | Preserve current UI contracts; unify safety incrementally, not a wholesale rewrite |
| UI | app/scmos/screens/AiControlTower.tsx, aiControl.ts, aiControlRequest.ts, app/SCMOSApp.tsx/nav.ts only as needed | Keep current route, design language, read-only errors and user context isolation |
| Verification | tests/Scmos.Ai.Checks/*, tests/aiControl.test.mjs; existing domain tests | Add real behavior tests, not source-text assertions alone |

### Proposed new files (not yet created)

- `server/Scmos.Api/Ai/AiActionLevel.cs`, `IAgentExecutor.cs`, `ToolExecutor.cs`, `QueryPolicyGuard.cs`: focused contracts and dispatch policy, implemented only if existing types cannot cover them cleanly.
- `server/Scmos.Api/Ai/IntentRouter.cs`: bounded route proposal; connected-agent allowlist and clarification, not a model with arbitrary tools.
- `server/Scmos.Api/Ai/Semantic/SemanticRegistry.cs`, `Rules/BusinessRuleRegistry.cs`: verified metadata adapters inside the Ai tree, not duplicated business calculations.
- `server/Scmos.Api/Ai/Context/AiContextService.cs`: expiring structured follow-up context with trusted identity/scope, no raw transcript storage by default.
- Subsequent `Ai/Data/DataAgent.cs`, `DataReadService.cs` and equivalent scoped Communication/Document/Engineering/SRE adapters only in their phases.
- `docs/ai/SCMOS_AI_AGENT_CATALOG.md`, `SCMOS_AI_TOOL_CATALOG.md`, `SCMOS_AI_BUSINESS_RULES.md`, `SCMOS_AI_APPROVAL_MODEL.md`, `SCMOS_AI_DATA_ACCESS_POLICY.md`, `SCMOS_AI_TEST_PLAN.md`. Update existing `SCMOS_AI_SECURITY.md` when implementation changes policy; retain historical logs/release records.

Names are proposals based on the current repository, not statements that files/services already exist. Recheck the worktree before each phase; preserve concurrent changes.

## 4. Proposed persistence review

| Existing table / candidate | Current limitation | Proposal and rollback |
| --- | --- | --- |
| approvals | Free-form payload/signature/state; no typed target concurrency/expiry/idempotent execution contract | Add only approved nullable/versioned fields with compatibility handling; stable requester ID, target/version, payload schema/hash, expires_at, correlation/execution identity, concurrency token. Keep old rows readable but ineligible for new execution without explicit validation. Roll back application/flags, not evidence |
| ai_audit_logs | Fixed Operations tool vocabulary and four event sequences | First determine whether code/event-version changes suffice; add step/parent/rule metadata only where needed. Never replace table or delete historical events. Unique sequencing and exact replay remain enforced |
| AI context (new store only if persistent sessions required) | No AI sessions; mailbox conversations are unrelated | Prefer no-DB short-lived pilot. If persistence required, propose owner/scope/expiry/version/structured filters and retention first; do not store raw prompts by default |
| Business rules (new store only if editable/effective-dated rules approved) | Current rules are code/UI choices, not verified customer contracts | Start code descriptors; future rules need authoritative business input and provenance. No invented effective dates or blanket 30-minute rule |
| operation_jobs indexes | Full permitted scans/JSON parsing can be expensive | Measure and explain exact query first; no arbitrary index/mass date migration. Never cap source rows silently and report false totals |

Required before any migration: read-only deployed schema/history comparison, exact Up SQL/table list, current-data preservation, compatibility with old app and CI migration bundle, backup verification, roll-forward/application rollback plan, isolated test. **No migration is scheduled or authorized by this plan alone.**

## 5. Risk-driven acceptance tests

1. Identity: anonymous/unrecognized/carrier rejected; Operation follows existing ViewTeam; restricted user without owner refused; role refresh invalidates context.
2. Data: customer/trucker/period/category filters intersect permitted scope; zero versus null; no-date/impossible-date/leap-day/cross-midnight; historical/current dates explicitly stated; duplicates counted at defined grain.
3. Rules: zero-grace KPI versus 30-minute threshold versus selected tolerance; missing customer contract never guessed; measurable/excluded denominators match source service.
4. Tools: unknown name/extra JSON properties/SQL/forged scope refused; lower-level handler revalidates context; budgets enforced before costly reads; source completeness never hidden behind evidence cap.
5. Prompt security: malicious customer names, notes, document/email/LINE/GitHub text cannot alter instructions, authorize a different tool or leak rates/secrets.
6. Audit: unavailable schema fails closed; write failures at every boundary; old/new events; multiple tool steps; duplicate/conflicting replay; cancellation and incomplete runs; safe metadata only.
7. Approval: list/create/decide/apply scope; guessed IDs; self-approval policy; stale target, expiry/cancel, changed permissions, retries and atomic audit; client result text cannot prove execution.
8. UI: existing Operations switch behavior, no button for Operation User, no new global agent until ready; loading/error/empty/unknown states; no automatic request replay or model-generated navigation.
9. Performance: compare p50/p95 API/tool/SQL durations under defined scoped fixture volumes and concurrent users; existing SCMOS screen baseline must not regress. Thresholds require measured baseline and owner agreement, not guessed latency claims.
10. Integrations: approved mailbox/group/resource only; invalid webhook signature, duplicates, stale claims/retries, out-of-scope source references and document ownership; no external send or production action in tests.

## 6. Baseline verification and limits

| Check | Observed result |
| --- | --- |
| `npm test` | 485 passed, 0 failed on unchanged baseline during Phase 0 |
| `npm run build` | Passed; Next 16.3.1 production build with TypeScript check |
| `dotnet build server/Scmos.Api -c Release --no-restore` | Passed, 0 warnings/errors |
| `dotnet run --project tests/Scmos.Ai.Checks/Scmos.Ai.Checks.csproj -c Release -p:UseAppHost=false` | 229 passed, 14 September; offline SDK transport and loopback fixtures; no production/provider calls |
| `npm run lint` | Exit 0; three existing warnings: layout custom font, Chrome img, CargoForm img |
| AI test compilation warning | Existing EF1002 at AuditChecks.cs:288 (optional SQL test code), not a production query finding |
| Remote branch and latest deploy jobs | Local/remote `a62d35a`; latest Web/API jobs success, checked 14 September |
| Live SQL schema/real-account UI/provider/Graph/LINE/telemetry | Not re-tested in Phase 0; not claimed verified by local builds |

No new tests were needed for documentation-only changes. Subsequent runtime slices must rerun backend/frontend build, lint/typecheck, Node tests, AI tests and affected domain checks; SQL persistence tests use isolated non-production databases. Do not invoke production-host command paths that read secrets/start workers merely to test a pure rule.

## 7. Release and handoff

Phase 0 does not push/deploy. Worktree originally contained modified next-env.d.ts; it remains outside these deliverables. Implementation should start on a reviewed `codex/` branch, using the existing CI/CD with explicit production migration review. Current API auto-deploy/migrate trigger on the deployment branch is a material release side effect, not an ordinary documentation push.

Do not assume API staging exists: current API workflow deploys production; Web has a staging option. Any environment/infrastructure change is a separately authorized action. Preserve existing Operations switch state. Recovery disables new agent flags or rolls back code without deleting audits/business data.

**Phase 0 stop point reached:** deliver assessment, architecture, implementation plan, risk analysis and proposed file/database changes for review. Recommended next work is Phase 1A shared-contract compatibility, followed by policy/audit hardening; not activation of six agents or AI write permissions.
