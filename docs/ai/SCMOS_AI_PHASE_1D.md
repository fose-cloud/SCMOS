# SCMOS AI platform — Phase 1D record: audit topology, correlation, context pilot

Implemented 20 September 2026 · released as v2.7.60 · the slice the [re-assessment](SCMOS_AI_PLATFORM_ASSESSMENT.md#re-assessment--20-september-2026-v2751) put after 1E: the audit ready for more than one tool step before any agent is allowed more than one, a correlation id from the web proxy through the API to every audit row, and a bounded conversation-context pilot behind a switch. With it, Phase 1 (the shared foundation) is complete.

## PHASE COMPLETED — 1D

**The audit topology.** `ai_audit_logs` kept one tool per run: sequences 1–4, fixed. It now holds a run of up to eight steps — `run_started` at 1, step *k* at 2*k* / 2*k*+1, `run_completed` at 2·max(*N*,1)+2 — so a run with one step or none is still 1, 2, 3, 4 and every row written before today reads exactly as it did (`AiAuditRules.SequenceOf`). A step may not be skipped, repeated or completed under another number; a run may not complete over a started step; a completion that names its step count must name the right one, and one that does not (pre-1D) is a one-step run. Succeeded completions still carry the last step's evidence. The dispatch budget is unchanged: **one read per run** (`AiDispatchBudget.MaxToolCalls = 1`). The audit can now describe what Phase 8 will do; nothing is allowed to do it yet. The audited agents and tools are explicit lists (`KnownAgents`, `KnownTools`), extended per phase as an agent connects — the Data Agent in Phase 2.

**The correlation id.** The web proxy sets `x-correlation-id` on every call it forwards (a UUID, or the browser's when well-formed) and passes the API's back. `POST /api/ai/chat` resolves the header (else the server's trace id, else a fresh id — `AiAuditRules.CorrelationOf`), answers with it in the `X-Correlation-Id` header and the body (`correlationId`), and writes it on every audit event of the run (`ai_audit_logs.correlation_id`); the same resolver stamps `approvals.correlation_id` on a proposal (1E). The id is immutable within a run; a malformed one is dropped, never invented into the row. The Control Tower shows it on a run and on a reply.

**The context pilot.** `AiContextService` keeps, per account, what the last successful run did — agent, tool, view, limit, the day the answer was about — for `AI:ContextMinutes` (10) minutes, in memory on the instance, and gives the model one line of those facts on the next question so "และงานล่าช้าล่ะ" reads against the same day and scope. Structured facts only: never the question, the answer or a row. Bound to the account; another account finds nothing; the window expires it; a restart forgets it; no table. **Off by default** (`AI:ContextEnabled`); with it off nothing is remembered or used. The reply says `contextUsed` when it was.

**Files created**

- `server/Scmos.Api/Ai/AiContextService.cs`
- `server/Scmos.Api/Data/Migrations/20260920131444_AiAuditCorrelationSteps.cs` (+ Designer) — two additive columns
- this record

**Files modified**

- `server/Scmos.Api/Ai/AiAuditRules.cs` — steps, `KnownAgents`/`KnownTools`, `IsCorrelation`, `CorrelationOf`, `SequenceOf`, `StepsCompleted`; `MayAppend` by event name
- `server/Scmos.Api/Ai/IAiExecutionAudit.cs` — `AiExecutionEvent.CorrelationId`, `.Step`
- `server/Scmos.Api/Data/AiAuditLog.cs` — `CorrelationId`, `Step`
- `server/Scmos.Api/Ai/AiAuditReader.cs` — projection by event name; `CorrelationId`, `Steps`, per-event `Step`/`Tool`
- `server/Scmos.Api/Ai/Operations/OperationsAgent.cs` — names the step, carries the correlation id, reads/remembers context
- `server/Scmos.Api/Ai/IAgentExecutor.cs`, `AgentOrchestrator.cs`, `AiContracts.cs`, `Services/AiGateway.cs` — the correlation id through the boundary; `AiChatResponse.CorrelationId`, `.ContextUsed`
- `server/Scmos.Api/Ai/AiOptions.cs` — `ContextEnabled`, `ContextMinutes`; `AiServiceRegistration.cs` — the singleton
- `server/Scmos.Api/Endpoints/AiChatEndpoints.cs`, `AiApprovalEndpoints.cs` — the resolver, the header
- `app/api/[...path]/route.ts` — `x-correlation-id` forwarded in and out
- `app/scmos/aiControl.ts` — `MAX_AUDIT_EVENTS = 18`, the new optional fields parsed strictly; `screens/AiControlTower.tsx` — correlation and steps shown
- `tests/Scmos.Ai.Checks/AuditChecks.cs`, `OperationsChecks.cs`, `SharedContractChecks.cs`; `tests/aiControl.test.mjs`, `tests/fixtures/ai-control.mjs`

**Existing components reused** — the strict `SqlAiExecutionAudit` (serializable append, fingerprint replay) untouched; `AiPermissionPolicy`, `QueryPolicyGuard`, `ToolExecutor` untouched; the `TimeProvider` the agent already had; the Carrier API's correlation-id shape (letters, digits, `.-_:`, ≤ 64).

**APIs added** — none. **Changed** — `POST /api/ai/chat` accepts `X-Correlation-Id`, answers with it, and its body carries `correlationId` and `contextUsed` (appended; the 1A envelope check is updated to say so). `GET /api/ai/audit` and `/{runId}` carry `correlationId`, `steps`, and `step`/`tool` per event.

**Tools / agents added** — none.

**Database changes** — `ai_audit_logs`: `correlation_id` nvarchar(64) '' · `step` int null. **Migration status** — applied to LocalDB for the probe; applied to production by the v2.7.60 API release. `Down` drops only the two columns; the Phase D `THROW` on dropping the table stands.

**Security changes** — none loosened. The model sees, at most, one more sentence of server-composed facts (tool, view, limit, date) — never user text. A correlation id from outside is validated to shape before it is stored or echoed. The audit's `KnownAgents` list refuses a row from an agent that is not connected.

**Permissions added** — none.

**Tests added** — 33 checks in `tests/Scmos.Ai.Checks`: the sequence table, a legal two-step run, skipped/repeated/mismatched steps, completion over a started step, an uncounted completion of a two-step run, immutable and malformed correlation ids, the resolver, the projection, the migration's shape; the context pilot (first question bare, follow-up given the facts and never the words, another account isolated, window expiry, switch off, per agent), the correlation id and step on every audit event, a malformed id dropped. Offline total 429; with `--write-local-db --local-db` 543. Web: 18 Control Tower tests (older runs without the fields, a six-event run, the reply's new fields). Unchanged suites: 579 Node, `--check-capability`, `--check-carrier-api`, `--check-line`; `-warnaserror`; tsc; eslint.

**Build result** — green at the 1D commit.

**Verified on LocalDB, 20 Sep** — `POST /api/ai/chat` with `X-Correlation-Id: probe-1d-001` → the header back and `"correlationId":"probe-1d-001"` in the body (chat itself 503 disabled locally, as configured); with `X-Correlation-Id: bad value <x>` → the server's trace id (`0HN…:00000001`) instead; `POST /api/ai/invoke` with `X-Correlation-Id: probe-1d-approval` → `approvals.CorrelationId = probe-1d-approval`.

**Known risks**

- The context pilot is per instance: on a scaled-out API a follow-up may land on an instance that remembers nothing. That is the pilot's bound, stated; a table is the Phase 8 decision.
- The hint tells the model the previous *facts*; it does not tell the model the previous *answer*. A follow-up that needs the rows ("the first one of those") still gets a fresh read.
- `KnownAgents`/`KnownTools` are code lists; a new agent's rows are refused until it is added — deliberately loud.

**Remaining tasks** — Phase 1 is complete (1A–1E). Phase 2: the Data Agent's first read (monthly volume/OTD for an explicit rule and period, customer/trucker filters) over `KpiService` with the provenance block.

**Recommended next phase** — 2.
