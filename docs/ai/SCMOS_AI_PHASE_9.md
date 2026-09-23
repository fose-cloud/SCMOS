# Phase 9 — production hardening

Status: the evidence-completeness increment below was written on 22 September 2026 and released with the second increment the same evening (v2.7.84). Every agent's flag is on in Production and every agent has answered an authenticated question there — the runs are in the table at the end.

## Evidence completeness

- The Management Agent checks that each specialist's `Truncated` flag agrees with `Total > Returned`, and does not choose one job when the search reports more than one total match.
- A one-job paperwork finding uses the document reader's full checklist standing, not just its first 50 display rows. Many held files can no longer hide a missing folder and make the summary say the checklist is complete. Unclear files and truncated display rows are named.
- In the late-paperwork plan, the intersection of two capped lists is labelled as a *minimum found in the rows read*, never as the full overlap. It remains a set overlap, not a cause of delay.
- The late-paperwork job list must match the document evidence rows whose identifiers go into the audit; a disagreement fails the run without releasing a partial finding.

The existing specialist reads, user scope, per-step authorization, audit sequence and result contracts remain unchanged. No database change, migration, write permission, new model call or secret is introduced.

Offline regression checks cover a job with more than 50 held files and missing checklist folders, plus a partial two-specialist intersection. Remaining Phase 9 gates include authenticated Production question/role checks, latency under working-hours load, audit reconciliation for real runs, and a reviewed rollback drill. The register-read redesign in `docs/REGISTER_READ_PLAN.md` follows Phase 9 rather than being folded into this patch.

---

# Phase 9, second increment — who may ask what, the switches that stop it, and what the audit holds

Implemented 22 September 2026 · released as v2.7.84.

## The access matrix (`tests/Scmos.Ai.Checks/AccessMatrixCheck.cs`)

Every role SCMOS has, against every agent and every read it owns, with every adapter connected and the durable audit ready — the friendliest world the runtime can be in, so an "allowed" in that table is a permission that really exists. The expected matrix is written out as a literal table of 99 lines: a capability added to a role, a tool moved to another agent, or a gate removed shows up as a diff in the table rather than as a quiet yes. That is the bug this codebase has actually shipped (an operator seeing the team's history because a role name was tested instead of a capability), and the table is what makes it loud.

The invariants the table protects, asserted separately so a failure names the rule rather than a line:

- a carrier reaches no agent and no read, whatever a role map ever grants them;
- the repository and the platform are the Administrator's alone;
- with the durable audit unavailable, no read is allowed to anybody — not even an Administrator;
- a read belongs to its own agent: no agent may run another's, however the model asks;
- an account nobody put in the directory, and one with no id, reach nothing;
- an account that may not see the team has no read scope without an owner id, and one that may reads the team's work;
- the dispatch guard says the same as the table, including the audit gate.

## The rollback is a setting, not a deploy

`AI:Enabled=false`, `AI:ChatEnabled=false`, an agent's own flag off, and `AI:OperationsEmergencyDisabled=true` each stop a run at 503 with the code the Control Tower shows (`disabled`, `agent_disabled`) **before the provider is called** — checked by counting the fixture provider's calls across each switch. Mock mode outside Development is `configuration_invalid`, never a pretend answer over production data. The status the Control Tower draws agrees with the gate the run passes.

The drill, in order, when an agent must be stopped in Production: turn its `AI__…AgentEnabled` off in the Portal (the App Service restarts, ~80 s, and the agent disappears from `/api/ai/status`); to stop all of it, `AI__ChatEnabled=false`; to stop the Operations pilot alone without a restart, the operations control switch in the Control Tower. Nothing needs a deploy or a rollback of code, and no data has to be put back — the platform writes nothing but its own audit.

## Retention (`--report-ai-audit`, `aiAuditReport` on the API workflow)

How many rows and runs the audit holds, how far back, by agent and by outcome, incomplete runs, tokens recorded, rows by month, and the size of the stored evidence identifiers. **It reads and prints; it never deletes an audit row** — a run that cannot be shown to have happened is the failure the platform is built to avoid, so what to keep and where the rest goes is the department's decision, taken on this report and carried out as its own reviewed change.

## The gates, and where each stands

| Gate | Where it stands |
| --- | --- |
| Security / role E2E | The matrix above, in CI with the AI checks (878 assertions). |
| Release / rollback rehearsal | The switches above, checked; the drill written out. |
| Schema compatibility | `AuditChecks`: the merged migration snapshot matches the runtime model, old rows and new rows both read, and a rollback cannot drop audit evidence. |
| Monitoring | The SRE Agent (Phase 7): health, deployments, the platform's own failures, and the last hour's requests from the in-process ring. |
| Retention | Measured by `--report-ai-audit`; the policy is the department's to set. |
| Performance under working-hours load | Measured, not simulated: on 22 September the requests view read p50 198 ms / p95 6.6 s over 354 requests, with `GET /api/notifications` at p95 16.9 s — the whole-register read, which `docs/REGISTER_READ_PLAN.md` addresses after this phase. The AI's own budget (`AI__TimeoutSeconds=60`) and the one-run limiter are unchanged. |
| Authenticated Production verification | Done, 22 September, every agent (below). |
| No regression of normal SCMOS | The rule checks, the web tests and the AI checks run on every push; the workspace, KPI, monitor and audit screens are untouched by this phase. |

## Verified in Production, 22 September 2026

Signed in as the department lead, each question asked through `/api/ai/chat` against the live API:

| Agent | Question | Answer |
| --- | --- | --- |
| SRE | ระบบเป็นอย่างไรบ้างตอนนี้ | 10 signals, 3.8 s, database 26 ms, two worth a look (TMS quiet, mail table empty) |
| SRE | คำขอ API ชั่วโมงล่าสุดช้าตรงไหน | 354 requests, 0 failed, 27 slow, p95 6.6 s, slowest `GET /api/notifications` |
| SRE | วันนี้มีข้อผิดพลาดอะไรบ้าง | zero in every kind |
| Document | งานไหนเอกสารยังไม่ครบ | 1,066 jobs, 50 shown, 4.2 s |
| Engineering | Commit ล่าสุดของ SCMOS | five, 1.5 s |
| Engineering | กฎ on-time ของ Lotus อยู่ตรงไหน | read `Rules/JobRules.cs` and pointed at `IsOnTime → CustomerTerms.GraceMinutes`, 3 reads, 10.6 s |
| Management | งานล่าช้างานไหนเอกสารยังไม่ครบ | two steps, 18 delayed, 1,066 short of paperwork, overlap named as a minimum |
| Management | สรุปงาน 260800810520 | three steps, 7.3 s, the job with its paperwork and its messages |

Two fixes came out of those runs and are in the same release: the job summary's search reaches completed jobs (server-set, never model-set), and a job is found by its ABS number and booking as the header search finds it.

**Remaining after Phase 9** — the register-read redesign (`docs/REGISTER_READ_PLAN.md`), which the requests view now measures; a retention policy when the department decides one; and Always On for the API App Service, so the first request after a restart is not 80 seconds.

---

# Phase 9, third increment — reading Phases 0 to 9 back

23 September 2026 · released as v2.7.85 · the department's word: "ตรวจเช็ค Phase 0 ถึง Phase 9 ว่าจุดความผิดพลาดตรงไหนหรือไม่ หากเจอแก้ไขให้หน่อย".

## What the reading found

**One live ambiguity.** Two agents claimed the page `compliance` — the Document & Invoice Agent, which has answered there since Phase 5 because compliance files near expiry are paperwork, and the never-connected Compliance Agent. `AgentRegistry.Resolve` takes the first match, so the router was deciding between them by array order and nobody would have known until somebody connected the second one and its own page did not reach it. The page now belongs to the agent that answers there; the Compliance Agent keeps `training`.

**One that would have bitten in a few months.** The correction pass read *every* LINE and TMS message of the last ninety-seven days, with its text, on every half-hourly run — to use the handful belonging to shipments that are actually late and unanswered. It now collects the jobs that will be asked first and reads the messages of those jobs only, five hundred keys to a query. Same proposals, on a table that no longer grows into the pass.

**One shape that would have read as a crash.** Two passes racing — two instances, or a hand-run beside the scheduler — end with the second failing the unique index that keeps one open proposal per cell. The pass now says so in one line and writes nothing, because the first pass wrote the same proposals.

**Four stale claims in the records themselves**: Phase 8 still said "not enabled or deployed", Phases 5 and 7 still said Production verification was waiting on the department, and the assessment still said Phases 8–9 had not started. Each is corrected where it stood, dated, without rewriting what was true when it was written.

## What now guards it

`tests/Scmos.Ai.Checks/PlatformVocabularyCheck.cs` — the platform's four lists against each other. A read exists in the tool registry, the agent registry, the permission catalogue and the audit vocabulary, and all four were written by hand; this codebase's oldest lesson is that a rule written twice drifts. It pins: every tool owned by exactly one agent and by the agent it names; every tool known to the audit and permitted by the catalogue; every audit tool name a real tool (except `extract_document`, the Workspace's own extractor, audited without ever being offered to a model); every agent with a flag of its own that turns on nothing else, found by name rather than by a second list; no two agents claiming a page, and every page routing to its agent; every audit view vocabulary identical to the read's own and offered by its schema; and the Management plans never appearing as reads.

1,016 assertions offline, 1,130 with LocalDB.

**Not found**: no permission that should not exist (the Phase 9 matrix is unchanged by this reading), no audit vocabulary gap, no view a read can answer and the audit would refuse, no tool in the registry that is a write, and no phase whose code disagrees with its record beyond the four sentences corrected above.
