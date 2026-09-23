# Phase 8 — bounded specialist collaboration

Status: implemented and checked locally on 22 September 2026. Not enabled or deployed by that change —
**deployed the same day at `b7fdd1c`, `AI__ManagementAgentEnabled` set by the department that afternoon, and both plans answered
authenticated questions in Production that evening** (the runs are listed in [the Phase 9 record](SCMOS_AI_PHASE_9.md)).

The Management Agent selects exactly one server-defined read-only plan. It cannot supply a tool sequence, reorder steps, or write data. The first increment has two plans:

| Plan | Fixed reads | Result |
| --- | --- | --- |
| `summarise_job` | Operations `search_shipment` → Documents `query_documents/job` → Communication `query_messages/job` | One unambiguous job, its paperwork, and its linked messages. Zero or multiple jobs require clarification before later reads. |
| `summarise_late_paperwork` | Operations `query_delays` → Documents `query_documents/missing` | Counts and keyed intersection of delayed jobs and jobs short of paperwork. The overlap is **not** a causal explanation. |

Every plan is offered only if all its existing specialist adapters are connected, enabled, and permitted for the caller. Each step repeats its own specialist/tool/schema authorization immediately before reading and inherits the caller's existing team/owner scope. A run has at most three reads, one provider selection, the existing request timeout and in-flight limit, and a strict evidence cap. Every attempted read is recorded under one run and correlation ID with a numbered `tool_started`/`tool_completed` pair; the durable audit must accept the final completion before any composed result is released. Failure withholds all partial answers. The model sees plan descriptions and the question, not the source rows; findings are assembled by server code.

The one-job plan rejects document or message results that include another job, even when a specialist's search matches a similar key. The UI shows the plan trail and specialist cards and does not mistake a document/message audit ID for a job link. `AI:ManagementAgentEnabled` remains false by default; the global AI gate, provider, durable audit, and every step's specialist flag must also be ready. Turning on the flag does not add a write permission.

Verification: `dotnet run --project tests/Scmos.Ai.Checks/Scmos.Ai.Checks.csproj --no-restore`, `node --test tests/aiControl.test.mjs`, `dotnet build server/Scmos.Api/Scmos.Api.csproj -c Release --no-restore`, and `npm run build`. These are offline checks; no Azure SQL, live provider call, migration, or production data change is part of this increment.
