# AI foundation, Operations and durable audit checks

Default, offline (no SQL or OpenAI credentials):

```powershell
dotnet run --project tests/Scmos.Ai.Checks/Scmos.Ai.Checks.csproj -c Release -p:UseAppHost=false
```

196 assertions cover provider transport, gateway HTTP, scopes/rules, schemas, event transitions, replay conflicts, pagination authorization, incomplete-run projection, additive migration operations and current snapshot/runtime-model parity. CI runs this default path. It never starts the production API/scheduler or reads appsettings/secret files. SDK requests use an in-memory transport. Localhost tests have synthetic identities and source records. The snapshot comparison constructs SQL Server metadata without opening a connection.

## Real SQL persistence (explicit opt-in)

On Windows with a working default SQL Server LocalDB:

```powershell
dotnet run --project tests/Scmos.Ai.Checks/Scmos.Ai.Checks.csproj -c Release -p:UseAppHost=false -- --local-db
```

For an isolated SQL Server 2019 instance, first verify the instance does not already belong to another test/session, then create/start ScmosAiAuditCheck_20260907 with SqlLocalDB. Add --isolated:

```powershell
dotnet run --project tests/Scmos.Ai.Checks/Scmos.Ai.Checks.csproj -c Release -p:UseAppHost=false -- --local-db --isolated
```

This path has 233 assertions total after adding the snapshot guard. The preceding Phase D SQL run passed 232 assertions; the 2026-09-08 preflight reran only the 196-assertion default path, not SQL persistence. Connection targets are fixed LocalDB instances and a generated ScmosAiAuditTests_<GUID> database; there is no configurable Azure/production target. The runner creates the database, applies only Phase D Up via EF's SQL generator, tests reopening, duplicates/concurrency, permissions, source references, cancellation and SQL constraint fault injection at all four boundaries, then deletes only its exact validated test database in finally. It does not run earlier migrations, staff seeding or business-data updates. A sentinel table verifies unrelated state remains intact.

The default instance failed to start on the Phase D verification attempt, before any test database was created. The isolated 15.0.4382.1 instance worked. All completed/failed SQL test databases were removed in finally; the dedicated empty instance was also stopped/deleted after verification. Remove a dedicated instance only if you created it and verified it has no remaining user databases; never stop/remove the user's shared MSSQLLocalDB instance.

Scope of proof: actual SQL Server audit persistence and HTTP boundaries, fake OpenAI provider and synthetic Operations source. These checks do not prove live provider billing/quota, production latency, actual operation_jobs SQL results, Azure firewall configuration, or signed-in browser behavior. Production migration/deployment/enablement require a separate reviewed release. No UI is added before Phase E.
