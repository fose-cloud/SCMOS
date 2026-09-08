# SCMOS AI B–E release — 2026-09-08

## Approved scope

The user explicitly requested applying AiExecutionAudit / ai_audit_logs, then pushing and deploying the completed work before the next improvement phase. This supersedes the earlier QA-only scope. Release includes the already-tested foundation, read-only Operations agent, durable Audit and Control Tower UI; it does not enable live AI or write tools.

## Database — completed before push

- Target: scmos-sql-3959.database.windows.net / scmos, compatibility 170.
- Azure reported Online, seven-day short-term retention and earliestRestoreDate 2026-09-01T04:46:18.457864Z. Differential interval: 12 hours; locally redundant backup storage. These are service metadata, not a restore drill or a newly created backup.
- Rechecked local/live history: only 20260907092459_AiExecutionAudit was pending. No unrelated migration was applied.
- Executed the reviewed idempotent SQL inside its transaction, with XACT_ABORT, target/baseline/collision guards and bounded lock/command timeouts. Did not run the application's broader migrate/seeding command.
- SQL SHA-256: 3299D40FF41D297D8B0F770F1B15FEA97AEED6AA94DB809610859D726758DA05.
- Committed at 2026-09-08T04:52:21.5959775Z; schema verification at 04:52:22.0432891Z.
- Applied history: 37 / 37 local migrations; no pending migration.
- dbo.ai_audit_logs exists with 25 columns, identity primary key, ai_audit_logs_at_idx (created_at, id) and unique ai_audit_logs_run_sequence_idx (run_id, sequence). Receipt is present.
- No existing business table/row was changed, no sample audit run was inserted and no business rows were read.
- Temporary rule codex-audit-release-20260908-1150-c68d2a allowed only the previously approved 49.229.138.124. Cleanup at 04:52:27.7995545Z verified its absence and all four original firewall rules unchanged.
- Credentials remained in process memory/environment only and were not displayed or saved.

## Release validation

API Release warnings-as-errors passed; 196 AI no-SQL/fake-provider checks passed; EF model parity passed. Web lint had 0 errors / 3 pre-existing warnings, all 410 Node tests passed and the production build included /ai-control-tower. Earlier same-day isolated browser QA passed 15 scenario groups. No live provider calls were made.

No AI flag overrides or KeyVault URI were present in the inspected API App Settings; the packaged AiOptions defaults are off. Existing provider key presence was confirmed without revealing it. Keep feature enablement separate from deployment; verify effective status after deployment without executing real chat.

The earlier bfa44c9 API deployment finished successfully before this release began. The remote and local branch were aligned at that commit. No other running GitHub Actions deployment was reported immediately before migration.

## Deployment — completed and verified

Released commit: 7dce5cbd86610a83e94f60ee408ae9afe9e15223, pushed to azure-dotnet-migration. Tag codex-ai-b-e-20260908-7dce5cb pins that exact release. The 62-file commit excludes generated output, credentials and business data.

| Component | Production result |
| --- | --- |
| API | [Run 34188778474](https://github.com/fose-cloud/SCMOS/actions/runs/34188778474) succeeded. CI passed 196 AI checks, migration bundle reported no migrations pending, deploy/restart succeeded and health passed at 2026-09-08T05:04:26Z. Optional business cleanup steps were skipped. |
| Web | [Run 34189285832](https://github.com/fose-cloud/SCMOS/actions/runs/34189285832) succeeded in slot=production. Its marker check verified 7dce5cbd86610a83e94f60ee408ae9afe9e15223 at 05:09:24Z and smoke returned HTTP 200. |

### Resumed verification — 2026-09-08, 19:36 Asia/Bangkok

While the task was paused, subsequent user work advanced the branch and Production. On resumption, an unnecessary duplicate Web run (34226478843) was briefly dispatched for the pinned AI tag before the already-successful Web release was discovered. It was cancelled before deployment: both its build and deploy jobs ended cancelled, with no deployment steps executed. No older package replaced the user's newer release.

Current Web marker was read directly from the Production Kudu deployment-version file at 12:32:39Z and returned 10c847668fcac204ac6fc63ba005ee151ab7e987. [Web run 34199705805](https://github.com/fose-cloud/SCMOS/actions/runs/34199705805) for that commit succeeded. Git ancestry confirms it contains the AI release; the AI implementation and migration paths have no changes since 7dce5cb.

The later [API run 34198395458](https://github.com/fose-cloud/SCMOS/actions/runs/34198395458) for 9e0ef8e also succeeded. Current API Kudu deployment metadata reports status=4, active=true, completion 07:18:47Z; that metadata did not expose a commit SHA, so it is not claimed as independent API SHA proof.

Final smoke observations:

- API /health returned 200 at 12:33:55Z. An earlier 30-second request timed out; the repeat passed without restarting or changing settings.
- Anonymous API /api/ai/status and /api/ai/audit returned 401; no authenticated AI execution or provider request was made.
- Anonymous Web /ai-control-tower returned 200 with the Microsoft sign-in path and without the synthetic fixture identifiers. HTTP 200 here is the sign-in entry, not proof of an authenticated Control Tower session.
- At 12:35:38Z, the allowlisted AI flag overrides remained absent and KeyVault was not configured in API App Settings. The unchanged packaged defaults remain off; an authenticated runtime status/activation check is still next-phase work.
- No later SQL/firewall write, forced branch update, permission grant or AI enablement was performed during resumed verification. New user changes and the newer deployed versions were preserved.

This follow-up record is documentation-only. Its commit does not require redeploying application binaries or replacing the later user releases.

## Rollback / next phase

If application rollback is necessary, deploy the approved prior application artifact and keep AI disabled. Preserve ai_audit_logs and its migration receipt; Audit Down intentionally refuses to discard evidence. No database restore or rollback was performed here.

Real signed-in owner/team/carrier acceptance, provider/source integration, latency, retention policy and feature activation remain next-phase work. A healthy deployment or anonymous authorization check is not proof of successful live AI execution.
