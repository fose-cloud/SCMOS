# SCMOS AI platform — Phase 4 record: the Communication Agent's first read

Implemented 21 September 2026 · released as v2.7.66 · as the [plan](SCMOS_AI_IMPLEMENTATION_PLAN.md) §2 scoped it: "reuse mail/LINE persistence, matching/review and mailbox/group policy; search, summary and extracted suggestions/drafts; real authorized source test; carrier/PII isolation; injection tests; no send/apply action; reuse existing tables; no second inbox".

## PHASE COMPLETED — 4

**What the specification's Communication Agent already was.** Extraction (plate, ETA, arrival, status, container, seal, delay reason), matching a message to a shipment, human review, confirmation, then the SCMOS update — the LINE integration has done all of that since 16 September, and the mail pipeline links a message to a job with a confidence for a person to confirm. Phase 4 does not build a second reader; it gives the AI platform a read over what those readers already wrote down.

**What it answers.** "ผู้ขนส่งแจ้งอะไรเกี่ยวกับตู้ TEMU5246902 บ้าง", "ข้อความที่รออนุมัติ", "ข้อความที่จับคู่งานไม่ได้", "ข้อความวันนี้": one tool, `query_messages`, four views —

| View | What it is |
| --- | --- |
| `job` (with `query`: a job number, container or customer) | every LINE message, TMS event and linked mail about that job, newest first, any date |
| `waiting` (`days`, default 7) | messages pinned to a job that await its owner's word — the drawer's queue, read |
| `unmatched` (`days`) | messages the rule understood but could pin to no job — the unresolved ones |
| `today` | today's messages by carrier room, the greetings left out but counted |

Each row carries the channel (LINE / TMS / mail), when, whose room (or which mailbox sender), which job, **what SCMOS's own parser read** — status, arrival stamp, ETA, plate, container, seal, delay and its category, whether it was a question — **what the system did with it** (นำเข้าแล้ว · รออนุมัติ · จับคู่ไม่ได้ · ไม่เกี่ยวกับงาน · ไม่สำเร็จ; for a mail: จับคู่แล้ว · เสนอจับคู่ · ปฏิเสธ, with the match and its confidence), and an excerpt. The answer counts waiting / applied / unmatched / ignored / mails, names the jobs it found (as buttons into the drawer), and says in its basis: nothing sent, nothing applied.

**PII and carrier isolation.** A message may carry a driver's name and phone. The excerpt has every phone number masked (`081xxxxxxx`) and the driver's name the parser read replaced by "[คนขับ]"; the row has no driver or contact field at all — the line the Operations evidence draws. The model never sees a message: it chooses the view and the job from the question, the server composes the rows, and the audit's evidence keys are the messages' ids (`line:123`, `tms:45`, `mail:7`). The agent needs `ViewMailbox` — the Communication Center's own capability, held from Operation User upward and deliberately not by a carrier's account, since one mailbox and one LINE ledger hold every carrier's correspondence. A restricted account (no `ViewTeam`) reads messages about its own jobs only; a message pinned to nothing is nobody's.

**No send, no apply, no draft.** The spec's "draft response" stays out of V1: a draft would be words a person might paste into a carrier's room, and the department turned LINE replies off on 20 September. Summaries are server-composed sentences over the counts and the newest row, not model prose.

**Files created**

- `server/Scmos.Api/Ai/Communication/MessagesReadService.cs` — `ICommunicationSource`, `MessageJob`, `LineMessageRow`, `MailMessageRow`, `MessageEvidence`, `MessagesAnswer`, the read, `StateOf`, `Excerpt` (phone mask, driver removal), `MessagesReadHandler`
- `server/Scmos.Api/Ai/Communication/CommunicationSource.cs` — the EF-backed source over `line_events`, `line_groups`, `emails`, `email_job_links`, `operation_jobs`
- `server/Scmos.Api/Ai/Communication/CommunicationAgent.cs` — `IAgentExecutor<CommunicationExecution>`, `Summarise`
- `tests/Scmos.Ai.Checks/CommunicationChecks.cs`
- this record

**Files modified**

- `server/Scmos.Api/Ai/AgentRegistry.cs` — the ninth specialist, `communication-agent` (pages `line`, `mail`, `communications`; capability `ViewMailbox`); `AiOptions.CommunicationAgentEnabled`
- `server/Scmos.Api/Ai/ToolRegistry.cs` — `query_messages` (view enum, optional `query`, optional `days` ≤ 30, `limit` ≤ 50), bound when a connected `MessagesReadService` is given
- `server/Scmos.Api/Ai/QueryPolicyGuard.cs`, `AiAuditRules.cs` (agent, tool, views — "today" is a view of two tools and the tool says which vocabulary), `AiContracts.cs` / `Endpoints/AiChatEndpoints.cs` (`messages` on the envelope, appended), `AgentOrchestrator.cs`, `AiServiceRegistration.cs`, `Program.cs` (`ICommunicationSource`), `Rules/AiPermissions.cs` (catalogue agent `communication`, tool `query_messages`)
- `app/scmos/aiControl.ts` — `MessagesAnswer`, strict parser, `askBody` (page `line`), `communicationAvailability`; `screens/AiControlTower.tsx` — the third door "ข้อความ LINE · อีเมล" shown only when the status lists the agent, prompts, the `MessagesCard`; `screens/Assistant.tsx` — the matrix's agent label
- `tests/Scmos.Ai.Checks/Program.cs` (nine specialists), `SharedContractChecks.cs`; `tests/aiControl.test.mjs`, `tests/fixtures/ai-control.mjs`

**Existing components reused** — `LineReadings.Of` (the parser's reading of a row, or a TMS payload's), `LineParser.Parsed`, `CarrierEvent.Payload`, `LineProcessing` states, `MailLink` states, the LINE rooms and mail links as stored; `AiPermissionPolicy`, `QueryPolicyGuard`, `AiDispatchBudget`, the audit (1D), the orchestrator's gates; the Control Tower's request/parse/render pattern.

**APIs added** — none. **Changed** — `POST /api/ai/chat` accepts `agentId: "communication-agent"` (or `context.page: "line"` / `"mail"`) and answers with `messages`; `GET /api/ai/status` lists `communication-agent`; `GET /api/ai/tools` includes `query_messages`.

**Tools added** — `query_messages`. **Agents added** — `communication-agent`.

**Database changes** — none. No second inbox, no new table; the ledgers are read as the LINE and mail screens read them.

**Security changes** — none loosened. Off unless `AI:CommunicationAgentEnabled` (absent on production). A carrier is refused at the orchestrator, the guard and the capability; a Management account (dashboard, no mailbox) is refused too. Phones masked, drivers removed, the model never given a message; an injected instruction inside a message is data in a row a person reads, never a prompt.

**Permissions added** — none. `ViewMailbox` already existed for the Communication Center.

**Tests added** — 47 checks in `tests/Scmos.Ai.Checks` (`CommunicationChecks`, 43 labelled "4:" plus four adjusted contract checks): the four views over a fixture ledger, the TMS row, the arrival/ETA/plate/container readings, the phone and driver redaction (in the row and anywhere in the answer), the linked mail, the counts and basis, the limit, restricted scope (own jobs only; a colleague's job finds nothing; unmatched excluded), invalid arguments, the schema (forged owner refused), the ninth agent's capability and pages, the flag, the guard for operator/supervisor/carrier, the audit vocabulary both ways, the agent's run (audit sequence and evidence keys, prompt hygiene, carrier refused, job view without a job → clarification, invalid tool, Management refused, operator reads), and the orchestrator (status, the run carrying `messages` only, the mail page route, a carrier refused and not even listed). Offline total 556 (was 509); with `--write-local-db --local-db` 670. Web: 20 Control Tower tests (the messages reply accepted, seven malformed shapes refused, `askBody`, `communicationAvailability`). Unchanged: 583 Node, `--check-capability`, `--check-line`; `-warnaserror`; tsc; eslint.

**Build result** — green at the Phase 4 commit.

**Verified on LocalDB, 21 Sep** — `GET /api/ai/status`: a supervisor and an operator see `communication-agent` `connected: true` (flag off); `GET /api/ai/tools` lists `query_messages` under `communication`. A live question needs the provider; the run is proved by the fixture-provider checks, as the other agents' were.

**Known risks**

- The driver-name removal relies on the parser having read a name; a name the parser did not recognise as one stays in the excerpt, as it does in the drawer the owner reads. Phones are masked by shape regardless.
- `unmatched` is "NEED_REVIEW with no job": a message the worker ignored as a non-job message is not unresolved, so it is counted under `ignored`, not listed.
- One read per run; "what did SANGJA say this week about all its jobs" is a `today`/`waiting` question narrowed by the room in the reader's eye, not a per-carrier filter — a follow-up if the department wants one.

**Remaining tasks** — Phase 5 (Document & Invoice over the existing extractor and rate readers), 6–7 (Engineering, SRE — need infrastructure decisions), 8 (collaboration), 9 (hardening).

**Recommended next phase** — 5.
