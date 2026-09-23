# SCMOS AI platform — Phase 7 record: the SRE Agent's first read

Implemented 22 September 2026 · released as v2.7.75 · as the [plan](SCMOS_AI_IMPLEMENTATION_PLAN.md) §2 scoped it: "restricted telemetry/health/deployment reader with correlation and safe redaction; source-backed time window; no restart, rollback, secret or firewall action; no business DB changes".

## PHASE COMPLETED — 7

**What the specification's SRE Agent needs, and what exists.** The specification wants metrics, errors and deployment summaries with safe identifiers. SCMOS has no Application Insights and no Azure Monitor connector — the assessment named that an infrastructure decision, and the department has not taken it. What SCMOS does have is what the API knows about itself: its process, the time its database takes to answer, the register cache the whole first page depends on, its configuration, the ledgers its workers write to, its own failure statuses, and — on the same public GitHub host the Engineering Agent already reads — the repository's workflow runs, which are the deployments. Phase 7 reads those, measures what can be measured, and claims nothing else.

**What it answers.** "ระบบเป็นอย่างไรบ้างตอนนี้", "การ deploy ล่าสุดผ่านไหม", "วันนี้มีข้อผิดพลาดอะไรบ้าง": one tool, `query_platform`, three views —

| View | What it is |
| --- | --- |
| `health` | ten signals, each with a state (ok · warn · bad · unknown): the API process (uptime, runtime, environment, the App Service instance's short id — never the machine name); the database's answer to `SELECT 1` in milliseconds, capped at ten seconds — over five seconds is "waking or saturated", the 21 September finding; whether the register snapshot is cached (if not, the next reader pays the 24–62 s read) — read with `JobRegisterCache.Peek()`, never loaded for this; blob storage and the AI provider configured; when LINE, the carrier TMS, the mailbox, the register's edits and the AI runs were last seen, a day's silence marked |
| `deployments` (`limit` ≤ 50) | the repository's latest workflow runs as GitHub reports them: API / Web, status and conclusion as the state, branch, short SHA, the run's title (untrusted display), the actor's login, when |
| `errors` (`days` 1–30, default 1) | this platform's own failures counted by kind: AI runs that ended other than succeeded or clarification (by the audit's status word), LINE messages the worker could not process, mails it could not process, carrier webhooks failing or retired — each with the newest one's safe identifier (`run 21f4fb50`, `line:1830`, `mail:11`, `webhook:5`) |

Every answer's basis says it: no Application Insights or Azure Monitor connector exists; no metric is claimed that was not measured; safe identifiers only; nothing restarted, rolled back or changed.

**Safe redaction.** A row carries a label, a value, a state, a detail and a safe id. It never carries a secret, a connection string, a webhook's URL, a message's text, a mail's subject or sender, a person's data, or the machine's name. The web parser refuses a row with a URL or an e-mail address in its detail, or a `secret` field, so a future regression cannot reach the screen. The deployment title is GitHub's — untrusted display, never in the summary.

**No action.** There is no tool that restarts, scales, rolls back, or touches a setting, a secret, a firewall or a resource — none exists to be offered, so none can be selected; the schema refuses a `resource`, `action` or `command` argument. Administrator only (`AdministerData`), behind `AI:SreAgentEnabled` (default false).

**Files created** — `server/Scmos.Api/Ai/Sre/PlatformReadService.cs` (`IPlatformSource`, `IDeploymentSource`, `PlatformSignal`, `PlatformAnswer`, the three views, `PlatformReadHandler`), `Ai/Sre/PlatformSource.cs` (`PlatformSource` — the timed ping, the cache peek, the ledgers' maxima, the failure counts, the process; `GitHubDeploymentSource` — GET-only, fixed repository), `Ai/Sre/SreAgent.cs`, `tests/Scmos.Ai.Checks/SreChecks.cs`, this record.

**Files modified** — `Data/JobRegisterCache.cs` (`Peek()`); `Ai/AgentRegistry.cs` (the eleventh specialist, `sre-agent`, pages `sre`, `health`, `system`), `Ai/AiOptions.cs` (`SreAgentEnabled`), `Ai/ToolRegistry.cs`, `Ai/QueryPolicyGuard.cs` (source `platform`), `Ai/AiAuditRules.cs` (agent, tool, views, source), `Ai/AiContracts.cs` / `Endpoints/AiChatEndpoints.cs` (`platform` on the envelope, appended), `Ai/AgentOrchestrator.cs`, `Ai/AiServiceRegistration.cs`, `Program.cs`, `Rules/AiPermissions.cs`; `app/scmos/aiControl.ts` (`PlatformAnswer`, strict parser, `sreAvailability`), `screens/AiControlTower.tsx` (the sixth door "ระบบ · SRE", prompts, `PlatformCard`), `agentReadiness.ts`; `tests/Scmos.Ai.Checks/SharedContractChecks.cs`, `Program.cs`, `CommunicationChecks.cs`, `DocumentChecks.cs` (eleven specialists); `tests/aiControl.test.mjs`, `tests/fixtures/ai-control.mjs`.

**Existing components reused** — `JobRegisterCache`, `IFileStore.Configured`, `OpenAiOptions`, `AiOptions`, the LINE / mail / audit / AI-audit / carrier-webhook tables as stored (counts and maxima only), the GitHub HTTP client configuration of Phase 6; `AiPermissionPolicy`, `QueryPolicyGuard`, `AiDispatchBudget`, the audit, the orchestrator's gates; the Control Tower's request/parse/render pattern.

**APIs added** — none. **Changed** — `POST /api/ai/chat` accepts `agentId: "sre-agent"` (or `context.page: "sre"` / `"health"` / `"system"`) and answers with `platform`; `GET /api/ai/status` lists `sre-agent` for an Administrator; `GET /api/ai/tools` includes `query_platform`.

**Tools added** — `query_platform`. **Agents added** — `sre-agent`.

**Database changes** — none. The read is counts and maxima over existing tables and one `SELECT 1`.

**Security changes** — none loosened. Off unless `AI:SreAgentEnabled`. A supervisor, an operator and a carrier are refused at the orchestrator, the guard and the capability; a non-administrator's status does not list the agent. The rows carry no secret, address or person's data by construction, and the web refuses one that does.

**Permissions added** — none. `AdministerData` already existed.

**Tests added** — 48 checks in `tests/Scmos.Ai.Checks` (`SreChecks`, labelled "7:"): the health view's ten signals and their order, uptime and instance, the states of a quick and a slow and a silent database, a cached and an empty register, missing storage and provider, a quiet worker and an empty ledger; the deployments view (states by conclusion, branch and SHA and actor, the untrusted title kept out of the summary, the limit passed through, the view without GitHub); the errors view (order, counts, safe ids, zero said plainly, the window); invalid arguments; no secret, URL or address in any row; the basis; the HTTP runs source (GET, fixed repository, a malformed SHA dropped, the cap); the registry (eleven specialists), the schema (resource/action refused), Administrator-only, the flag, the audit vocabulary both ways; the agent's run (audit sequence and keys, prompt hygiene, the three questions' summaries, another agent's tool refused, supervisor and carrier refused, audit failure withholds, not connected without the platform); the orchestrator (status, the reply carrying `platform` only, the health page route, a supervisor refused and not listed). Offline total 789 (was 741); with `--write-local-db --local-db` 903. Web: 25 Control Tower tests (the platform reply accepted, eight malformed shapes refused — a URL, an address or a secret in a row among them). Unchanged: 596 Node; `-warnaserror`; tsc; eslint.

**Build result** — green at the Phase 7 commit.

**Verified in Production, 22 Sep 2026** — `AI__SreAgentEnabled` on; health, requests and errors all answered (the readings are in [the Phase 9 record](SCMOS_AI_PHASE_9.md)). Before that, on LocalDB — the checks' fixtures stand in for the platform; a live question needs the provider and the flag, as the other agents' did. Production verification follows the department's word on `AI__SreAgentEnabled`.

**Known risks** — the health view's ping is one query at one moment: a database that answered in 300 ms may take 40 s a minute later under a register read, and the view says so only if asked then. The `errors` view counts what SCMOS wrote down; an exception the API logged to App Service and nowhere else is not in it — that is the connector the department has not decided on (the second read below answers part of this from inside the process). GitHub's unauthenticated rate limit (60 an hour per address) is shared with the Engineering Agent's reads.

**Remaining tasks** — Phase 8 (collaboration), 9 (hardening).

**Recommended next phase** — 9.

---

# Phase 7 record, second read: the requests the process remembers

Implemented 22 September 2026 · released as v2.7.78 · on the department's word "เริ่มทำ Phase 7 ต่อ" after the morning the dashboard would not load.

## PHASE COMPLETED — 7 (second read)

**What was missing.** That morning the department asked why the dashboard took minutes, and the answer had to be read off Portal charts and a log stream by hand: the API remembers nothing about its own requests, and the first read's `errors` view could only count what SCMOS had written to its ledgers. "What was slow at 09:41" and "what threw this morning" had no reader.

**What it is.** A ring of the last 5,000 API requests, in the process's memory — `RequestTelemetry` — filled by one middleware that sits inside the exception handler. Each sample is the route's *pattern* (`/api/jobs/{key}`, never the path with the key filled in), the method, the status, how long it took, the correlation id it came with (or the trace id), and the *type* of an exception it threw (never its message). No query string, no body, no user, no message text, no path. It is forgotten past its capacity and at every restart; it is not telemetry storage and does not try to be. Only `/api*` and `/health` are remembered — the site's other paths are not the API's business.

**What it answers.** A fourth view on the same tool, `query_platform` —

| View | What it is |
| --- | --- |
| `requests` | the last 60 minutes as the process remembers them: the volume with the failed (5xx), the refused (4xx) and the slow (over five seconds) counted; the nearest-rank p50 and p95 response times and the slowest; then each route pattern with its count, p95, failed and slow — the routes that threw first, then the slowest. An hour with nothing remembered is `unknown`, not fine. |
| `errors` (extended) | after the ledgers' counts, what the API itself threw as far back as the process remembers, within the days asked: one row per exception type and route pattern with the newest one's correlation id, and one row for the total that names how far back the memory reaches and how many requests it holds. |

The basis now says so: the last few thousand requests the process remembers, forgotten at restart; no path, query, body, secret, address, message text or person's data.

**Why inside the exception handler.** Ahead of it, the handler would answer the caller first and the middleware would only see a 500 with no type. Inside it, the middleware records the type on the way out and rethrows; the handler answers as it always did. The check proves the type is recorded and the exception still leaves the middleware; the local host proves the pipeline still answers `/health` 200, a routed call 401 and an unrouted one 404 through it.

**Files created** — `server/Scmos.Api/Ai/Sre/RequestTelemetry.cs` (`RequestSample`, `RequestTelemetry` — the ring, `Since`, `StartedAt`, `Recorded`; `RequestTelemetryMiddleware` — `RouteOf` names an unrouted call by its first two segments only).

**Files modified** — `Program.cs` (the singleton; the middleware after `UseExceptionHandler`); `Ai/Sre/PlatformReadService.cs` (`IPlatformSource.Requests` / `TelemetrySince`, the `requests` view, the API-exception rows in `errors`, `RequestMinutes`, the basis); `Ai/Sre/PlatformSource.cs` (reads the ring); `Ai/Sre/SreAgent.cs` (instructions, the summary of an hour); `Ai/ToolRegistry.cs` (description; the schema's choices follow `Views`); `Ai/AiAuditRules.cs` (`PlatformViews` += `requests`); web `aiControl.ts` (the parser's views), `AiControlTower.tsx` (the card's label; a prompt "คำขอ API ชั่วโมงล่าสุดช้าตรงไหน").

**Existing components reused** — `AiAuditRules.IsCorrelation` (the header's shape), the audit's 80-character key (a row id is capped to fit it), `TimeProvider`.

**APIs added** — none. **Changed** — `POST /api/ai/chat` may answer with `platform.view: "requests"`.

**Tools added** — none. **Agents added** — none. **Database changes** — none. **Permissions added** — none.

**Security changes** — none loosened. The ring holds nothing a path, a query string or an exception message could carry, by construction: the middleware stores the pattern and the type and nothing else, and a check serialises a sample made from a request whose path names a job and whose query string names a token and finds neither.

**Tests added** — 19 checks in `SreChecks` ("7:"): the four views in the schema and the audit; the `requests` view's rows, order, states, the hour's window, the nearest-rank percentiles, a 404 that is neither failed nor slow, the quiet hour; the `errors` view's exception rows and the quiet case; no `?` or job key in any row; the ring's capacity, order and `Since`; the middleware on a bare `HttpContext` — a routed request by its pattern, a thrown one as a 500 with the type and the trace id, an unrouted one by two segments, `/health` remembered and `/` not, a malformed correlation header not stored. 808 offline, 922 with LocalDB.

**Build result** — green (`-warnaserror` on the API; the checks project as CI builds it).

**Verified on LocalDB, 22 Sep** — the checks; the host started in Development with the middleware in its pipeline and answered `/health`, `/api/jobs`, `/api/nothing/here` and `/api/kpi/measures` as before. The mock provider never selects a tool, so the live view waits, as the first read did, for the flag and the provider in production.

**Known risks** — one process, one memory: on two App Service instances each answers for what it saw. A restart empties it — the first question after a deploy is answered "nothing remembered since {restart}", which is the truth. 5,000 samples is the busiest hour and a half of a working morning; a quieter day reaches back further. The p95 of a route seen once is that one request.

**Remaining tasks** — Phase 8 (collaboration), 9 (hardening); then the register-read plan (`docs/REGISTER_READ_PLAN.md`), which this view will measure.

**Recommended next phase** — 9.
