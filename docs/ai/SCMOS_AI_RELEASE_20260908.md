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

## Deployment plan at this checkpoint

Push the reviewed AI commit to the existing azure-dotnet-migration branch, which starts the API workflow. Its migration bundle should find no pending migrations because the approved Audit SQL was already applied. Wait for API build, deploy, restart and health to succeed. Then dispatch the Web workflow on the same release commit with slot=production, verify its deployment-version marker and smoke checks.

Do not merge or force-push unrelated branches, dispatch optional cleanup jobs, change business permissions or turn AI flags on. Record final run IDs and verification results after both workflows finish.

## Rollback / next phase

If application rollback is necessary, deploy the approved prior application artifact and keep AI disabled. Preserve ai_audit_logs and its migration receipt; Audit Down intentionally refuses to discard evidence. No database restore or rollback was performed here.

Real signed-in owner/team/carrier acceptance, provider/source integration, latency, retention policy and feature activation remain next-phase work. A healthy deployment or anonymous authorization check is not proof of successful live AI execution.
