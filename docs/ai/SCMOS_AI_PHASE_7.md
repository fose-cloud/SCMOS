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

**Verified on LocalDB, 22 Sep** — the checks' fixtures stand in for the platform; a live question needs the provider and the flag, as the other agents' did. Production verification follows the department's word on `AI__SreAgentEnabled`.

**Known risks** — the health view's ping is one query at one moment: a database that answered in 300 ms may take 40 s a minute later under a register read, and the view says so only if asked then. The `errors` view counts what SCMOS wrote down; an exception the API logged to App Service and nowhere else is not in it — that is the connector the department has not decided on. GitHub's unauthenticated rate limit (60 an hour per address) is shared with the Engineering Agent's reads.

**Remaining tasks** — Phase 8 (collaboration), 9 (hardening).

**Recommended next phase** — 9.
