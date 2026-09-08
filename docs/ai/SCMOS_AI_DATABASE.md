# SCMOS AI database plan

## Current QA checkpoint — 2026-09-08

Release update: the user subsequently approved migration and deployment. AiExecutionAudit was applied on Production at 2026-09-08T04:52:21.5959775Z. Verification found ai_audit_logs with all 25 columns, primary key, two secondary indexes and the migration receipt; all 37 local migrations are applied and none remain pending. Temporary firewall access was removed and the four original rules preserved. See [release record](SCMOS_AI_RELEASE_20260908.md). The following preflight observations are historical.

Read this checkpoint before the historical implementation notes below. The latest successful metadata check at 2026-09-08T04:40:58.2671664Z (11:40:58 Asia/Bangkok) found 36 applied migrations out of 37 local migrations, latest applied 20260907093954_DieselPrices, only 20260907092459_AiExecutionAudit pending, no unknown applied IDs and no dbo.ai_audit_logs object. Database scmos compatibility is 170; the inspection identity has database VIEW DEFINITION. This supersedes the earlier SQL observations and the earlier Diesel ID in the historical notes.

Subsequent shared-worktree changes had removed Audit from the current EF snapshot. QA reproduced the model-drift failure, restored only the original generated Audit entity block, and added a default no-connection parity check. API build, all 196 default AI checks and EF has-pending-model-changes now pass. No applied migration/designer was changed and no SQL migration was run.

The earlier credential and firewall blockers were resolved by separate explicit user approvals. Added one temporary rule for only 49.229.138.124, queried only EF history and sys metadata, and removed that exact new rule in finally. At 2026-09-08T04:41:03.5053715Z cleanup verification confirmed the temporary rule absent and all four original firewall names/ranges unchanged. No credentials were displayed/saved, no business rows were queried, and no migration or database write was executed. Temporary access is closed. Audit migration/release authorization, backup/restore review and actual authenticated integration remain pending; see [pre-deployment QA](SCMOS_AI_PREDEPLOY_QA.md).

## Phase D migration proposal — 2026-09-07, before implementation

Read-only Azure SQL inspection confirmed database scmos, compatibility 170, latest applied migration 20260907082846_CustomerRateKind (EF 10.0.11). Existing ai_tools, approvals and audit_events match their relevant mapped columns. No ai_runs or ai_tool_calls exists. Source HEAD 919d802 also contains 20260907085025_LineIntegration, not yet applied on production: a blanket migrate would include that unrelated change. This task will NOT migrate, deploy, enable AI, or push to the deployment branch.

Chosen minimal equivalent to run/tool tables: one append-only ai_audit_logs table, with immutable run_started, tool_started, tool_completed and run_completed events. Current Phase C permits exactly one tool per run; sequence numbers 1–4 and a unique (run_id, sequence) index identify each event. Run and tool tracking are projections over these durable events, not independent mutable truth. A server-generated tool_call_id links the two tool events. Later multi-tool orchestration requires an explicit schema/contract extension.

Store server-resolved user ID, role, scope, agent, configured model, known action/status, low risk / not-required approval, safe structured view/limit summary (no search text), source table and up to 50 returned job keys, aggregate counts, token usage and UTC event time. Do not store prompts, raw tool arguments, model responses, hidden reasoning, contact data or credentials. Identity IDs and source keys are sensitive operational metadata: read via existing ViewAudit capability, internal known roles only. There is no new role grant or approval workflow. No conversations are needed for a single-turn API.

Indexes: unique run_id + sequence; created_at + id for time lookup (the primary key provides the bounded run-page cursor). String fields have bounded application validation. Source-reference JSON is capped at 6000 serialized characters; EF maps that to nvarchar(max), because SQL Server bounded nvarchar stops at 4000. No existing table/column/index changes, backfill, seeds or business-data write. Strict validation and a serializable transaction enforce event ordering, immutable run identity and exact idempotent replay. Audit uses an isolated DbContext so it never saves pending business changes. An acknowledged start must precede provider/tool execution; acknowledged completion must precede evidence release. Failed persistence means no result, never in-memory success. Incomplete histories remain visible as incomplete rather than being rewritten as success after a crash.

Retention: no automatic deletion, cleanup job or edit/delete API in Phase D. Retention duration and archival/deletion authorization remain a policy decision before long-term operations. Rollback is disable AI flags and deploy the previous application while retaining the table. Migration Down always throws, even for an empty table; it never silently drops evidence. Review generated SQL and all pending migrations against live history again before any release.

Verification plan: offline schema/validation/authorization and HTTP checks by default; opt-in randomly named LocalDB-only database for actual SQL persistence, replay/concurrency, missing-schema behavior and migration SQL validation. No production credentials or provider requests in tests.

## Phase D implemented / release checkpoint

Migration 20260907092459_AiExecutionAudit creates only ai_audit_logs and its two indexes. SQL tests applied only this migration's Up to newly created LocalDB databases, never the shared migration chain. Default checks: 195 passed; with isolated SQL and HTTP/failure/cancellation checks: 232 passed. Generated migration operations and the current EF snapshot were validated; no pending model changes.

A final read-only production recheck found dbo.ai_audit_logs absent and latest applied migration 20260907085025_LineIntegration. Production therefore advanced during this task, independently of our work; we did not run that migration. Concurrent source work also added 20260907092645_DieselPrices and committed 0f6acb0. Its latest snapshot/designer includes the current shared model, including audit metadata: review/release the full migration chain carefully rather than assuming a snapshot or isolated commit is self-contained. Do not remove the audit mapping from the shared snapshot.

No production business rows were queried for D. Only schema metadata/applied history were read. No production migration, deployment, AI flag change, Git push or live OpenAI request was performed by this task. API read paths are /api/ai/audit and /api/ai/audit/{runId}; no UI or update/delete API. Readiness probes metadata shape but does not guarantee write permission; the mandatory committed start provides the execution gate.

## Historical B/C notes

Release checklist for the generated [Phase D SQL](migrations/20260907092459_AiExecutionAudit.sql):

1. Recheck target DB name, live EF history and table-name collision. The script assumes an existing EF history table and the reviewed LineIntegration baseline; it does not install older migrations.
2. Review/approve the complete release contents, snapshot and other pending migrations separately. The branch workflow automatically deploys/migrates on server pushes. Do not use a push as a test.
3. Keep AI flags off; follow the existing production backup/change-control process. Apply only the approved migration plan using approved tooling; this document is not authorization to execute it.
4. Check the table, unique index and migration receipt. Review SQL identity read/insert privileges without broadening business roles.
5. Enable only reviewed read-only flags after production/provider/source smoke tests and retention review. Audit failure must still block execution/results. Rollback by disabling AI, not dropping the log table.

The standalone SQL was generated/reviewed but not executed here. Local SQL tests used EF's migration Up operations on disposable databases. Production application of this script is still pending.

The earlier B/C notes below describe their historical state; Phase D implementation/results are recorded above and in the implementation log.

Phases B/C, 2026-09-07: **no database changes, migrations, seeds or production SQL access performed by these tasks**.

The inspected EF/Azure SQL design already includes `ai_tools`, `approvals` and `audit_events`. See [domain map](SCMOS_DOMAIN_MAP.md). Reuse existing approval concepts; do not create a competing approvals table casually. `MarkAppliedAsync` records a state and is not proof that an operational command executed.

Phase B state is code/configuration plus transient request objects. Run IDs are correlation identifiers only, not persisted run records. Chat messages, results and provider internals are not stored. The test host constructs a context without a SQL provider and never queries it.

Phase C adds JobsRepository.ReadAnalysisAsync: a read-only EF query against existing operation_jobs, filtered by the server-resolved OwnerId before materialization for restricted users. One authorized-scope read supplies complete counts; only bounded evidence is retained. Primary key and OwnerId come from mapped columns, not editable JSON fields. No new table/index or change to existing queries is needed. SQL generation and the parser/projection are tested without connecting; real SQL execution/latency has not been validated yet.

The new IAiExecutionAudit interface defines metadata-only run/tool events. Its production registration intentionally refuses readiness. In-memory success/failure audit implementations exist only in tests; they must not be deployed as a replacement for Phase D storage. No business read or live provider call can pass the public gateway's audit gate with the shipped registration.

Phase D proposal, not implemented: additive `ai_runs` / `ai_tool_calls` (or a reviewed equivalent), carrying caller, scope summary, agent, status, tool, safe argument/evidence summary, source references, usage and timestamps. Conversation/message tables are optional and should be introduced only if required by the UI and retention policy. Never persist hidden reasoning or provider credentials.

Before proposing migration code, verify live schema and applied EF history, review current concurrent migrations, settle retention/PII fields and strict audit-failure semantics, and document indexes and rollback. Prefer disabling AI while retaining audit evidence over dropping data. Existing `AuditService` best-effort behavior is not sufficient for proof of a successful AI tool run.

Do not enable business tools before durable audit is ready. Do not run the production API migration workflow as a test: the existing branch workflow may deploy and migrate on a server-code push. Other concurrent workstreams may have migrations in the shared tree; they are not Phase B changes or authorization to apply them.
