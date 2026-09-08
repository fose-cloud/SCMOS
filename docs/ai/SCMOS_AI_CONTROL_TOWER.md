# Phase E — SCMOS AI Control Tower

Implemented locally on 2026-09-07. No deployment, production migration, AI flag change, permission grant or live OpenAI request was performed by Phase E.

## Entry and scope

- Sidebar: AI Control Tower. Direct authenticated entry: /ai-control-tower.
- Reuses SCMOSApp, Easy Auth and the existing /api/me capability response. Home keeps its existing landing preference.
- Legacy AI Assistant, tools and approvals remain intact. This page is not a second approval workflow.
- Carriers cannot enter the internal screen. Backend authorization remains authoritative on every API call.
- Operations AI is read-only. No job edits, price changes, assignments, approvals, external dispatch or arbitrary SQL tools are added.

## Sections and sources

| Section | Source | Meaning and limits |
| --- | --- | --- |
| Morning Brief | GET /api/dashboard/today | Existing ViewDashboard-authorized overview. Shows the returned plan date, including nearest-date fallback, notes and computation time. Four existing measures: planned jobs, in transit, status-delayed jobs, open CAR/PAR. CAR/PAR is all open cases, not restricted to the displayed plan date. |
| Priority Queue | GET /api/dashboard/briefing | Existing Dashboard / Monitor rules, not model-generated text. Findings keep their server urgency, count, explanation and reference date. Counts can overlap. |
| Risk Summary | Same briefing | Counts **topics** by Now / Soon / Watch, not distinct jobs, risk scores or predictions. Records findings remain visible in the queue. |
| Ask SCMOS AI | GET /api/ai/status; explicit POST /api/ai/chat | Operations-only request with fixed context.page=operations. Source evidence is assembled by the backend, not invented client-side. No automatic prompt on entry. |
| AI Activity | GET /api/ai/audit and /api/ai/audit/{runId} | Existing ViewAudit permission. Eight runs per page, older cursor and return-to-latest control, run metadata, ordered events and source references. |

The Dashboard overview and Operations answers do not necessarily share a data scope or time window. Dashboard APIs authorize system aggregates via ViewDashboard. The AI backend derives team/owner scope from the authenticated account; its risk_today query is overdue through the next two days. Labels explain this distinction. No OTD/MTD metric or independent KPI engine was added.

## Interaction and safeguards

- Prompt chips fill the input without submitting. Submit or Ctrl/Cmd+Enter sends one request; IME composition does not submit.
- Maximum 4,000 characters. Only message, agentId and context.page leave this form. No role, owner, model, key or tool override fields.
- In-flight submissions are guarded synchronously. No automatic POST retry on provider limits or failures.
- Users can stop waiting. Cancellation aborts the request and ignores late responses. It does not claim the backend or its audit cleanup never ran.
- GET requests have a 30-second client timeout. Chat allows 80 seconds for the configured server limit plus bounded audit cleanup.
- Prompt, response and audit selections live only in component memory. They are not written to localStorage, URLs or a shared page cache. Navigation/unmount and identity/role/owner/capability changes discard that state.
- New audit cursors or run selections hide prior request data immediately, even before the next effect runs.
- Response contracts reject malformed payloads. Errors use a local safe allowlist, not raw server text or HTML.
- Null/unavailable metrics are N/A; a verified zero remains 0. Failures never become an empty success.
- Disabled, configuration-invalid, missing-provider, missing-audit, unavailable-agent and mock states are explicit. Configured provider does not mean a live connectivity/quota test succeeded.
- Development mock has a distinct badge and no job evidence. Mock/preflight requests do not create durable execution audit rows.
- Evidence includes total versus returned count, truncation, reference window/time zone, unreadable-date/invalid-row counts and source timestamps.
- Evidence buttons open the existing job drawer using its source key. Priority topics navigate only to the server's known Monitor/My Job destinations. Readers without Monitor permission open My Job instead.
- The evidence table uses SCMOS's shared ZoomBox, including the common remembered zoom preference. This stores a display preference only, not evidence.
- Audit source links open **current** jobs subject to their usual authorization. They are not historical job snapshots. Incomplete audit is never presented as success.
- Existing legacy Assistant is available by an explicit navigation link; this page does not call its invoke or approval mutation APIs.

## Implementation

- app/scmos/screens/AiControlTower.tsx and its scoped CSS module: responsive UI, private request state, read-only source links.
- app/scmos/aiControl.ts: typed contracts, runtime validation, availability rules, safe errors and request body construction.
- app/scmos/nav.ts and app/SCMOSApp.tsx: additive menu and screen integration. Control Tower does not request the full client-side jobs register.
- app/ai-control-tower/page.tsx: same platform sign-in boundary as Home.

OpenAI Docs guidance influenced the explicit limitations and access to original evidence, not the provider/model choice: [Safety best practices — human review and limitations](https://developers.openai.com/api/docs/guides/safety-best-practices).

## Verification and remaining gates

- 16 focused Node tests cover contract validation, unavailable versus zero, evidence limits, mock isolation, status prerequisites, strict request shape, safe navigation, Audit metadata, abort/no-retry behavior and sanitized failures.
- Full Node suite: 410 passed at shared HEAD c1ed268. The count grew while other workstreams added their own tests; Phase E adds 16.
- AI backend regression: 196 no-SQL/fake-provider checks passed in the 2026-09-08 preflight, including the new merged-snapshot parity guard. Existing EF1002 warning is in the Phase D isolated database test helper.
- Next production build passed and includes the dynamic /ai-control-tower route. HTTP fixture preview returned 200 with Morning Brief, Ask SCMOS AI and Activity rendered.
- Desktop automation initially failed to initialize on this host. The 2026-09-08 preflight used existing Playwright with installed Edge in a fresh headless context instead: 15 scenario groups passed against synthetic fixtures, and desktop/tablet/mobile screenshots were visually inspected. Real Entra authentication and live API/SQL/provider browser integration are still pending.
- No live provider call, production identity smoke test, production query benchmark or deployment was performed.

### Reproducible isolated preview

Run node tests/ai-control-preview.mjs from the repository root, then open http://127.0.0.1:4107/ai-control-tower.

The runner binds only loopback ports 4107 (Next) and 4108 (synthetic fixture API), overrides the proxy base/key in its own process, and never starts the .NET service, SQL, OpenAI or any production scheduler. Its small Next wrapper imports the real route, layout and API proxy. It has a separate app directory and ignored build output, so it does not stop or reuse the user's existing dev server. Static public assets are not copied into this QA wrapper.

Fixtures live only under tests; no test records are imported by the production UI. The local fixture rejects mutations other than synthetic chat, and rejects unsupported endpoints rather than forwarding them. Stop the runner with Ctrl+C after QA.

Fixture modes can be set with a local POST to http://127.0.0.1:4108/__fixture?mode=VALUE, then reload the UI. Allowed values: ready, disabled, audit-missing, no-access, empty, slow, error, mock, operator, carrier. GET /__fixture identifies the fixture and reports method/path counts only, not prompts, headers or secrets.

With the fixture runner active, run node tests/ai-control-browser.mjs. If Playwright is not a project dependency, set SCMOS_QA_PLAYWRIGHT to an existing installed playwright/test module. This optional suite is separate from npm test and was not added as an uninstalled CI dependency. It uses installed Edge, blocks all app requests outside the loopback fixture origin, and never reuses the user's signed-in browser profile.

The suite covers keyboard/chip submission, duplicate/cancel behavior, safe errors, mock/disabled gates, capability-dependent requests, Audit selection/pagination, source drawer navigation even when the job is absent from the current paged result, and mobile submission. Generated screenshots are under ignored outputs/ai-qa. Remaining checks need real authenticated integration, not more fixture success. See [current pre-deployment QA and release gates](SCMOS_AI_PREDEPLOY_QA.md).

## Release / rollback

Do not infer production readiness from the UI build. Review all shared-worktree commits and the pending migration chain before any push: the API workflow can deploy and migrate automatically. Phase D audit storage, existing AI flags, real source/provider smoke tests and signed-in browser QA remain release gates.

Phase E needs no new SQL migration. UI rollback is additive-file/menu removal while preserving B/C/D and unrelated work. Do not drop audit history or reset the shared branch.
