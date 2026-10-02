using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.Extensions.Options;
using Scmos.Api.Ai;
using Scmos.Api.Auth;
using Scmos.Api.Data;
using Scmos.Api.Data.Migrations;
using Scmos.Api.Rules;

static class PermissionSqlChecks
{
    public static async Task Run(Action<bool, string> check, bool local)
    {
        var migration = new AiPermissionAuthorizationAudit { ActiveProvider = "Microsoft.EntityFrameworkCore.SqlServer" };
        check(migration.UpOperations.OfType<CreateTableOperation>().Single().Name == "ai_authorization_logs"
            && migration.UpOperations.All(op => op is CreateTableOperation or CreateIndexOperation),
            "permission SQL: migration only adds authorization evidence and indexes");
        check(migration.DownOperations.Count == 1 && migration.DownOperations[0] is SqlOperation guard && guard.Sql.Contains("THROW"),
            "permission SQL: rollback cannot erase authorization/security evidence");
        if (!local) return;
        // Explicit opt-in. Hardcoded isolated LocalDB only; no environment overrides, appsettings or credentials.
        var database = "ScmosAiPolicyTests_" + Guid.NewGuid().ToString("N");
        var connection = $"Server=(localdb)\\ScmosAiAuditCheck_20260907;Database={database};Integrated Security=true;TrustServerCertificate=true;Connect Timeout=10";
        var options = new DbContextOptionsBuilder<ScmosDbContext>().UseSqlServer(connection,
            sql => { sql.EnableRetryOnFailure(5); sql.UseCompatibilityLevel(150); }).Options;
        var masterConnection = new SqlConnectionStringBuilder(connection) { InitialCatalog = "master" }.ConnectionString;
        await using var master = new SqlConnection(masterConnection);
        await master.OpenAsync();
        await using var create = master.CreateCommand();
        create.CommandText = $"CREATE DATABASE [{database}]";
        await create.ExecuteNonQueryAsync();
        try
        {
            await using var db = new ScmosDbContext(options);
            foreach (var command in db.GetService<IMigrationsSqlGenerator>().Generate(migration.UpOperations, db.Model))
                await db.Database.ExecuteSqlRawAsync(command.CommandText);
            // Only the two columns the revoke probe needs. No business tables or Production data.
            await db.Database.ExecuteSqlRawAsync("CREATE TABLE ai_tools ([Name] nvarchar(60) NOT NULL PRIMARY KEY, [Enabled] bit NOT NULL);");
            var budget = new AgentBudgetPolicy(800, 8, 8, 0, 20, 4, 100, 0.04m, 0.05m, 0.01m);
            var user = new AppUser("test-admin", "", "test", Roles.Admin, "OP-TEST", "test", true);
            var request = AiAuthorizationRequest.For(AgentIds.Operations, AiAction.BookingRead, "query_shipments", user, "policy-local-sql");
            var decision = new AiAuthorizationDecision(AiAuthorizationVerdict.Allow, "allowed", "test-policy-1", AiRisk.Low, AiPermissionLevel.Read, request.CorrelationId, false);
            var ai = Options.Create(new AiOptions { TimeoutSeconds = 60 });
            SqlAiExecutionAudit Sink() => new(options, ai);
            AiPolicyAuditEvent Event() => new(Guid.NewGuid().ToString("N"), DateTimeOffset.UtcNow, request, decision, false, 1);
            var first = Event();
            check(await Sink().RecordAuthorizationAsync(first, budget, default) is null, "permission SQL: committed authorization reserves cost before dispatch");
            check(await Sink().RecordAuthorizationAsync(first, budget, default) is null && await db.AiAuthorizationLogs.CountAsync() == 1,
                "permission SQL: identical audit retry persists once and reserves once");
            try
            {
                await Sink().RecordAuthorizationAsync(first with { Request = request with { ResourceId = "different-resource" } }, budget, default);
                check(false, "permission SQL: conflicting replay rejected");
            }
            catch (InvalidOperationException) { check(true, "permission SQL: conflicting replay rejected"); }
            var sql = Event() with { Request = request with { Action = AiAction.DirectProductionSql }, SecurityEvent = true,
                Decision = decision with { Decision = AiAuthorizationVerdict.Deny, ReasonCode = "absolute_forbidden", Permission = AiPermissionLevel.Forbidden } };
            await Sink().RecordAuthorizationAsync(sql, budget, default);
            check(await db.AiAuthorizationLogs.AnyAsync(x => x.Id == sql.Id && x.SecurityEvent && x.ReservedCost == 0 && x.Decision == "Deny"),
                "permission SQL: denied SQL attempt is durable security evidence without reserving cost");
            var attempts = Enumerable.Range(0, 8).Select(_ => Event()).ToArray();
            var answers = await Task.WhenAll(attempts.Select(entry => Sink().RecordAuthorizationAsync(entry, budget, default)));
            check(answers.Count(x => x is null) == 3 && answers.Count(x => x == "budget_exhausted") == 5
                && await db.AiAuthorizationLogs.SumAsync(x => x.ReservedCost) == 0.04m,
                "permission SQL: serializable concurrent reservation cannot exceed the daily budget");
            check(await db.AiAuthorizationLogs.CountAsync() == 10, "permission SQL: every allow/deny survives fresh contexts");
            await db.Database.ExecuteSqlRawAsync("INSERT INTO ai_tools ([Name], [Enabled]) VALUES ('query_shipments', 0);");
            var revoked = Event();
            check(await Sink().RecordAuthorizationAsync(revoked, budget, default) == "tool_disabled"
                && await db.AiAuthorizationLogs.AnyAsync(x => x.Id == revoked.Id && x.Decision == "Deny" && x.ReservedCost == 0),
                "permission SQL: stored tool revocation cannot be overridden by a manifest grant");
        }
        finally
        {
            SqlConnection.ClearAllPools();
            if (!database.StartsWith("ScmosAiPolicyTests_", StringComparison.Ordinal) || database.Length != 51)
                throw new InvalidOperationException("Unexpected scratch database identity.");
            // Remove only the freshly-created local test database, never SCMOS or Production evidence.
            await using var drop = master.CreateCommand();
            drop.CommandText = $"ALTER DATABASE [{database}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{database}];";
            await drop.ExecuteNonQueryAsync();
        }
    }
}
