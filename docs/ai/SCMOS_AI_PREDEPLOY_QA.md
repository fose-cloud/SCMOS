# SCMOS AI B–E — pre-deployment QA

Release update: after this QA checkpoint the user explicitly approved migration, push and deploy. The approved Audit migration was applied and its 25-column table/indexes/receipt verified on 2026-09-08; no migrations remain pending. See [release record](SCMOS_AI_RELEASE_20260908.md) for execution and deployment status. Earlier pending/QA-only statements below record the pre-approval state, not current migration status.

## Decision — 2026-09-08, Asia/Bangkok

Local QA is complete for this checkpoint. **Do not deploy or enable Production AI from this result alone.** Durable Audit migration, a test-environment decision, actual authenticated integration and a reviewed release candidate remain gates.

The user approved continuing QA/preparation. This work performed no push, workflow dispatch, deployment, production migration, flag change, permission grant, live OpenAI call or business-data mutation.

This is a shared, changing workspace, not a frozen release artifact. The resumed check started at aaf2b6f; bfa44c9 was subsequently observed. B–E files and the fixes below remain local working-tree changes. Unrelated place/LINE/OTD work was preserved. Passing tests do not identify what is already deployed, and deployed commit IDs were not verified here.

## Verified results

| Check | Result and scope |
| --- | --- |
| npm run lint | 0 errors; 3 existing warnings in layout.tsx, Chrome.tsx and CargoForm.tsx |
| npm run typecheck | Passed |
| npm test | 410 passed, including 16 Control Tower tests |
| npm run build | Passed; dynamic /ai-control-tower route is in the build |
| API Release build, UseAppHost=false, warnings-as-errors | Passed, 0 warnings/errors; user's running API was not stopped |
| AI default checks | 196 passed; fake provider, synthetic records, no SQL connection |
| Quotation regression | 21 passed in arithmetic-only mode, no SQL connection |
| EF has-pending-model-changes | Passed after snapshot repair; this compares local metadata, not live SQL history |
| Edge headless browser | 15 scenario groups passed; fresh context, loopback fixtures only |
| Visual QA | Desktop 1440×1000, tablet 768×1024, mobile 390×844 inspected; mobile Control Tower client/scroll widths both 342px |

The AI test project still emits the pre-existing EF1002 warning for its opt-in, validated LocalDB cleanup helper. The production API build is warning-free. This preflight did not rerun the prior Phase D SQL persistence suite: its historical 232-pass result must not be presented as a new SQL run.

### Browser coverage

- Page sections, unavailable N/A versus actual zero, chips that only fill the form.
- Ctrl+Enter sends one request; bounded evidence and truncation are shown.
- Audit selection, incomplete-run warnings, older cursor and return to latest.
- Source evidence opens the exact job even when the paged workspace result excludes it.
- Duplicate submission is blocked; cancellation ignores a deliberately late response.
- Disabled and missing-Audit states prevent submission.
- Accounts without capabilities do not request Dashboard/Audit data.
- Operator source navigation uses My Job when Monitor is unavailable.
- Empty results, sanitized upstream errors and clearly labeled mock responses.
- Mobile form submission and no horizontal page overflow.
- Carriers do not see the internal Control Tower; no JavaScript runtime exceptions.

Run tests/ai-control-preview.mjs, then tests/ai-control-browser.mjs with an existing Playwright installation (SCMOS_QA_PLAYWRIGHT may point to its playwright/test module). No dependency or lockfile was added. The suite is optional and is not automatically included in npm test/CI. Both app request routing and fixture endpoints prevent forwarding to Production.

Screenshots are reproducible local outputs under ignored outputs/ai-qa: desktop.png, desktop-activity.png, tablet.png, mobile.png and mobile-answer.png. They contain synthetic data and do not prove a signed-in Production session. The preview wrapper may show unchanged SCMOS branding/environment labels; the data and API are still fixtures.

## Issue found and repaired

The current EF snapshot had lost the AiAuditLog entity while runtime mapping and the pending Audit migration still contained it. The CLI correctly reported a pending model change, which can stop an EF migration release.

Reproduced the failure, added a default no-connection regression check, and restored the exact 145-line Audit entity metadata from its generated designer into ScmosDbContextModelSnapshot.cs. The check then passed.

No applied migration was rewritten. No migration ID was changed, no duplicate migration was generated and no migration was executed. The original Audit Up/Down and the applied Diesel migration/designer were preserved. Default AI CI now detects future snapshot/runtime drift.

## Azure evidence — separate timestamps

### Latest successful SQL check — 2026-09-08, 11:40:58 Asia/Bangkok

The user separately approved temporary firewall access for exactly 49.229.138.124, to be removed immediately after the read-only inspection. Created a new rule only after verifying that its unique name did not exist; start and end IP were both the approved address. Existing rules were not edited.

SQL metadata timestamp: 2026-09-08T04:40:58.2671664Z. Local source HEAD: bfa44c9 plus the documented working-tree changes.

| Metadata | Verified result |
| --- | --- |
| Target | scmos-sql-3959.database.windows.net / scmos |
| Compatibility | 170 |
| Applied / local migrations | 36 / 37 |
| Latest applied migration | 20260907093954_DieselPrices |
| Pending migration | Only 20260907092459_AiExecutionAudit |
| Applied IDs absent from local migration files | None |
| dbo.ai_audit_logs object collision | No matching object; table not yet present |
| Database VIEW DEFINITION | Available to the inspection identity |

Only EF migration history and sys metadata were read. No job/Audit business rows were queried and no migration, INSERT, UPDATE, DELETE or schema change was executed. The credential was kept in memory, never printed or saved; the child-process password environment was restored in finally. This check did not retest provider configuration, effective AI flags or actual application write permission.

Cleanup verified at 2026-09-08T04:41:03.5053715Z: rule codex-schema-20260908-0439-8c7a1d was absent, the firewall again contained four rules, and all original names/address ranges matched the pre-check snapshot exactly. Temporary access is closed. No other rule was removed.

The schema/history checkpoint is now complete; the earlier credential/firewall blockers below are historical. Audit migration, reviewed release contents, backup/restore readiness and authenticated integration remain separate gates, not authorized by this inspection.

### Resource metadata rechecked on 2026-09-08

- scmos-web-3936 and scmos-api-3936 reported Running in rg-scmos.
- The Web deployment-slot query returned an empty list. A staging option in a workflow does not mean a staging slot exists.
- This check read resource metadata only, not App Settings or SQL credentials.

### Historical SQL/config check: 2026-09-07T15:01:05.3788119Z

- Exact database: scmos on scmos-sql-3959.database.windows.net; compatibility 170.
- 36 applied migrations; latest 20260907093954_DieselPrices.
- Only locally pending migration: 20260907092459_AiExecutionAudit.
- No unknown applied migration compared with local files.
- dbo.ai_audit_logs did not exist.
- Existing OpenAI__ApiKey was present; its value was not printed or copied.
- No AI__* / AI:* App Settings overrides were returned. This is **not** proof of effective runtime flags; package configuration/other providers were not inspected.

Only migration history, table metadata and database metadata were queried, not business rows.

The initial SQL/config refresh on 2026-09-08 was rejected by the execution approval system because it would read and use the Production SQL credential from Azure App Settings. It did not execute and no bypass was attempted. The user subsequently approved that use and then the temporary network access recorded above. Provider configuration facts in this historical section were not refreshed by the schema-only check.

### Approved read-only retry — 2026-09-08, 11:36 Asia/Bangkok

The user explicitly approved reading and using the Azure App Settings SQL credential only for read-only schema/migration checks, without exposing the password or changing data. That credential-use gate is now resolved.

The retry retrieved only the approved connection setting, validated the exact server/database, and supplied the password through a process environment variable, restored in finally. No secret was printed or written to a file. The SQL server refused the connection because observed client IP 49.229.138.124 was not permitted by its firewall; the metadata queries did not run.

A separate resource-only check at 2026-09-08T04:36:41.1513595Z found publicNetworkAccess=Enabled, four server firewall rules, and zero ranges matching the observed client IP. No firewall rule or network setting was added, removed or changed. No alternate access route was attempted. No business rows, fresh SQL schema or migration history were read on this retry.

At that attempt the blocker was approved network access, not credential-use permission. Separate approval was then obtained and used for the successful, cleaned-up inspection above. No broad range or existing rule was changed.

## Release gates and proposed sequence

1. Freeze/review the exact B–E candidate with all shared changes. Rerun local checks if source changes. Do not treat the current dirty workspace as a reviewed release.
2. Select an isolated integration environment. No existing Web staging slot was found; creating resources/slots or an isolated database needs a separate approved plan. Do not silently use Production as staging.
3. Credential use and temporary single-IP access were approved; schema/history inspection completed at 11:40:58 on 2026-09-08 and the temporary rule is removed. Confirm backup/restore arrangements and review the Audit SQL before proposing execution. Revalidate target/history against any intervening changes at release time; this one-time inspection is not migration approval.
4. Apply only an approved migration/release plan with AI initially disabled. Confirm table/index/receipt and actual application identity read/insert permissions. Local model parity alone does not prove migration or write readiness.
5. Test real Entra identities through the Web proxy, API, scoped operation_jobs source, durable Audit and approved provider configuration. Include restricted owner, supervisor/team and carrier; unavailable provider/Audit; cancellation; and original job links. Bound request count/cost and measure latency. Do not publish fixture counts as production performance.
6. Review retention/archival ownership, monitoring and rollback. Activate only approved read-only Operations behavior after acceptance; write/approval tools remain later work.

### Workflow warning

At this checkpoint, API pushes to azure-dotnet-migration affecting server files or api.yml can deploy and apply all pending migrations. Manual API dispatch defaults migrate=true and can still deploy when using a read/report option. Do not dispatch it merely to inspect state.

Web pushes to main deploy Production. Manual Web dispatch offers staging/production, but the staging slot was absent. Neither workflow was invoked by this QA.

Rollback keeps AI disabled and returns the application to an approved earlier build while preserving durable Audit rows. Audit migration Down deliberately refuses to drop evidence. Do not reset the shared branch or remove unrelated migration history.

## Guidance used

The OpenAI Docs and API-key safety workflow influenced test isolation and secret handling, not model selection: keep keys server-side and separate development/testing from live traffic. No provider/key change was made. [OpenAI production best practices](https://developers.openai.com/api/docs/guides/production-best-practices).
