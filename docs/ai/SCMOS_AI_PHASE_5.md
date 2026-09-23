# SCMOS AI platform — Phase 5 record: the Document & Invoice Agent's first read

Implemented 21–22 September 2026 · released as v2.7.70 · as the [plan](SCMOS_AI_IMPLEMENTATION_PLAN.md) §2 scoped it: "adapt DocumentExtractor and rate readers into common provider/audit controls; compare only verifiable evidence; missing approval/fuel/effective terms reported as insufficient evidence; no invoice/payment approval; no invoice ledger for AI demo".

## PHASE COMPLETED — 5

**What the specification's Document & Invoice Agent already was.** The department already keeps its paperwork in SCMOS: the documents table (a file per job folder — Booking, ECard, POD, Images, Invoice, CARPAR — and per supplier and driver with an expiry), the verification screen's checklist (`DocumentChecklist` — which folders a job's category needs, which of them stop a truck, and from when), the compliance screen's expiry watch (`DocumentService.ExpiringWithinDays`), and the billing KPI's rule — invoices within four days of completion — which `KpiEngine.Billing` has reported as *not yet measurable* because there is no invoice table. Phase 5 does not build an invoice ledger; it gives the platform a read over what those screens already hold, judged by the rules they already apply.

**What it answers.** "เอกสารของตู้ TEMU5246902 ครบหรือยัง", "งานไหนเอกสารยังไม่ครบ", "ใบแจ้งหนี้ผู้ขนส่งของงานที่เสร็จแล้ว", "เอกสารผู้ขนส่งหรือคนขับที่ใกล้หมดอายุ": one tool, `query_documents`, four views —

| View | What it is |
| --- | --- |
| `job` (with `query`: a job number, container or customer) | the files filed for that job, and one row for each required folder still empty — the blocking ones (booking/DO, E-Card) marked — with the checklist's own reason for wanting it |
| `missing` (`days`, default 14, plus two days ahead — the bell's horizon) | jobs short of required paperwork, blocking first, then the most incomplete; the cancelled owe nothing; the complete are counted, not listed |
| `invoice` (`days`, default 14) | done jobs and their carrier's invoice: filed inside the rule's four days, filed late, not yet filed but inside the four days, or overdue — counted from the arrival the register recorded, else the plan date, and said which |
| `expiring` | suppliers' and drivers' compliance files expiring within the 60-day watch or already expired, the expired first, whose they are and how many days |

Every answer carries the rule and its figures (`DocumentChecklist v1 · invoice within 4 days · expiry watch 60 days`), the counts, and a basis that says what was not done: no file opened, no model reads one, no amount compared, nothing approved. The four days are now written once — `DocumentChecklist.InvoiceDays` — and read by the checklist's reason, the KPI's "not yet measurable" line and this agent.

**Insufficient evidence, stated.** An invoice's date is the day its file was filed, not a ledger date; the register holds no invoice amount and no rate is keyed to a job, so no amount is compared and no discrepancy is invented — the basis says so on every `invoice` answer. Approval of an invoice or a payment is not a tool and not a status.

**A rule put back.** `DocumentChecklist.ExpectedNow` asked a running job for its POD, photos and invoice by reading the status through `JobStatus.FromLegacy`, which turns a code it does not recognise — every controlled code — into DRAFT. Once the register moved to controlled codes, no running job was asked for its POD until it was done; the rule had not changed, its reading had. It now reads `JobRules.IsRunning`, which knows both vocabularies. The verification screen inherits the fix.

**The Workspace's document reader under the platform's controls (S4).** `DocumentExtractor` — the add-job form's reader of booking confirmations and box-door photos — called the model on its own with no run limit and no audit, and could return the provider's own error text. `ExtractionRun` now surrounds the same call: the shared `AiRunLimiter` (four in flight, twenty a minute), an internal scope the audit can record (a carrier's account is refused — the add-job form was never theirs), and a run in `ai_audit_logs` — agent `document-agent`, tool `extract_document`, the category as its view, one key per file with its name reduced to safe characters — committed before the fields are released, as every agent's read is. The provider's error text stays in the log. The answer's shape and the route are the ones they were.

**Files created**

- `server/Scmos.Api/Ai/Documents/DocumentsReadService.cs` — `IDocumentSource`, `DocumentJob`, `DocumentEvidence`, `DocumentsAnswer`, the four views, `Standing` (the checklist applied to one job), `DoneOn`, `DocumentsReadHandler`
- `server/Scmos.Api/Ai/Documents/DocumentSource.cs` — the EF-backed source: the register through `JobsRepository.ReadAnalysisAsync` (ownership in SQL, private fields projected away), `documents` for the jobs named and for every file with an expiry
- `server/Scmos.Api/Ai/Documents/DocumentAgent.cs` — `IAgentExecutor<DocumentExecution>`, `Summarise`
- `server/Scmos.Api/Ai/Documents/ExtractionRun.cs` — the extractor's limiter, scope and audit
- `tests/Scmos.Ai.Checks/DocumentChecks.cs`
- this record

**Files modified**

- `server/Scmos.Api/Rules/DocumentChecklist.cs` — `InvoiceDays`, `ExpectedNow` through `JobRules.IsRunning`; `Rules/KpiMeasures.cs`, `Services/KpiEngine.cs` — the four days read from the constant
- `server/Scmos.Api/Ai/AgentRegistry.cs` — the former `billing-agent` descriptor (never connected) is `document-agent`, "Document & Invoice Agent", pages `documents`, `verification`, `compliance`, `billing`, capability `UploadDocuments` — the Document Center's own; `AiOptions.DocumentAgentEnabled` (was `BillingAgentEnabled`, never set)
- `server/Scmos.Api/Ai/ToolRegistry.cs` — `query_documents` (view enum, optional `query`, optional `days` ≤ 90, `limit` ≤ 50), bound when a connected `DocumentsReadService` is given, policy source `documents`
- `server/Scmos.Api/Ai/QueryPolicyGuard.cs` — the reviewed sources are `operation_jobs` and `documents`; `AiAuditRules.cs` (agent, tools `query_documents` and `extract_document`, their views — "job" is a view of two tools), `AiContracts.cs` / `Endpoints/AiChatEndpoints.cs` (`documents` on the envelope, appended), `AgentOrchestrator.cs`, `AiServiceRegistration.cs`, `Program.cs` (`IDocumentSource`), `Rules/AiPermissions.cs` (catalogue tools `query_documents`, `extract_document` under `document`)
- `server/Scmos.Api/Endpoints/AiExtractEndpoints.cs` — through `ExtractionRun`, with the correlation id; `Services/DocumentExtractor.cs` — the provider's error text no longer reaches the screen
- `app/scmos/aiControl.ts` — `DocumentsAnswer`, strict parser, `askBody` (page `documents`), `documentAvailability`; `screens/AiControlTower.tsx` — the fourth door "เอกสาร · ใบแจ้งหนี้" shown only when the status lists the agent, prompts, the `DocumentsCard`
- `tests/Scmos.Ai.Checks/Program.cs`, `SharedContractChecks.cs` (tool list, policy source, envelope), `AuditChecks.cs` (the not-connected example is `vendor-agent`); `tests/aiControl.test.mjs`, `tests/fixtures/ai-control.mjs`

**Existing components reused** — `DocumentChecklist`, `VerificationService.UnclearMark`, `DocumentService.ExpiringWithinDays`, `JobRules.IsDone`/`IsRunning`, `WorkspaceTabs.IsCancelled`, `JobsRepository.ReadAnalysisAsync` and its projection, `Formats`; `AiPermissionPolicy`, `QueryPolicyGuard`, `AiDispatchBudget`, `AiRunLimiter`, the audit (1D), the orchestrator's gates; the Control Tower's request/parse/render pattern.

**APIs added** — none. **Changed** — `POST /api/ai/chat` accepts `agentId: "document-agent"` (or `context.page: "documents"` / `"verification"` / `"compliance"` / `"billing"`) and answers with `documents`; `GET /api/ai/status` lists `document-agent` (no longer `billing-agent`); `GET /api/ai/tools` includes `query_documents` and `extract_document`; `POST /api/ai-extract` answers 403 to a carrier's account, 429 when the shared limiter is full, 503 when the audit cannot record the read, and sets `X-Correlation-Id`.

**Tools added** — `query_documents`; `extract_document` names the existing reader in the audit. **Agents added** — `document-agent` (in the billing descriptor's place).

**Database changes** — none. No invoice table; the ledgers are read as the verification and compliance screens read them.

**Security changes** — none loosened. Off unless `AI:DocumentAgentEnabled` (absent on production). A carrier is refused at the orchestrator, the guard, the capability and — new — at the document reader. A dashboard-only account (Management) has no `UploadDocuments` and is refused. A restricted reader sees their own jobs' paperwork only. A job's driver, plate, phone and notes never reach the evidence (the analysis projection leaves them behind); a file's note appears only as the "unreadable" mark a person left. The model never sees a file, a file name or a job: it chooses the view and the job from the question; the server composes the rows; the audit's keys are `doc:123`, `job:KEY`, `job:KEY:Folder`. The extractor's audit holds no field value and no raw file name.

**Permissions added** — none. `UploadDocuments` already existed for the Document Center.

**Tests added** — 72 checks in `tests/Scmos.Ai.Checks` (`DocumentChecks`, labelled "5:", plus the adjusted contract checks): the four views over a fixture register and documents table, the unreadable mark, the blocking folders of a job not yet on the road, the window and the horizon, the ordering, the limit, the invoice rule from arrival and from plan date with the days counted, the caveat in the basis, the rule fix (a running job owes its POD under a controlled code and under the old text), the four days written once, the expiry ordering and ownership, restricted scope, invalid arguments, what never reaches the evidence, the registry (nine specialists, `billing-agent` gone), the schema (forged owner refused), the guard for supervisor / operator / customer service / carrier / dashboard-only, the audit vocabulary both ways including the extractor's, the agent's run (audit sequence and evidence keys, prompt hygiene, carrier refused, job view without a job → clarification, invalid tool, an operator's invoice question), the orchestrator (status, the run carrying `documents` only, the documents page route, a carrier refused and not listed), and the document reader (fields unchanged, the four events the strict sink accepts and appends in order, no value in the audit, carrier refused, audit not ready → nothing read, run unrecordable → model not called, fields withheld when the completion cannot be recorded, a busy provider recorded as such, the shared limiter's fifth read refused, an unconfigured reader unchanged, the category fallback and key sanitising, the error text gone from the source). Offline total 629 (was 557); with `--write-local-db --local-db` 743. Web: 21 Control Tower tests (the documents reply accepted, nine malformed shapes refused, `askBody`, `documentAvailability`). Unchanged: 585 Node, `--check-capability`, `--check-audit-revert`; `-warnaserror`; tsc; eslint.

**Build result** — green at the Phase 5 commit.

**Verified in Production, 22 Sep 2026** — `AI__DocumentAgentEnabled` on; "งานไหนเอกสารยังไม่ครบ" answered with 1,066 jobs in 4.2 s. Before that, on LocalDB — the checks' fixtures stand in for the tables; a live question needs the provider and the flag, as the other agents' did. Production verification follows the department's word on `AI__DocumentAgentEnabled`.

**Known risks**

- `missing` and `invoice` read the register's analysis projection whole for the scope, as the Operations reads do; in working hours that is the register's cost, not the agent's (see the Phase 4 record's production note).
- An invoice filed under a different folder (a scan dropped into `Other`) is not an invoice to this read, exactly as it is not one to the verification screen.
- The document reader's audit adds one to seven seconds per write in working hours to a call that already takes ten to twenty at the provider; a saturated database can now refuse the read (503) where before it would have released fields nobody recorded.

**Remaining tasks** — Phase 6–7 (Engineering, SRE — need infrastructure decisions), 8 (collaboration), 9 (hardening).

**Recommended next phase** — 9 (hardening) before 6–7, unless the department decides the connectors.
