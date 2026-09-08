using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Scmos.Api.Ai;
using Scmos.Api.Auth;
using Scmos.Api.Data;
using Scmos.Api.Data.Migrations;
using Scmos.Api.Rules;
using Scmos.Api.Services;

sealed class TestOperationsControl(OperationsControlState state) : IOperationsControl
{
    public Task<OperationsControlState> ReadAsync(CancellationToken token) => Task.FromResult(state);
}

static class OperationsControlChecks
{
    private static readonly AppUser Admin = new("control-admin", "admin@example.invalid", "Test", Roles.Admin, "", "test", true);
    public static async Task RunAsync(Action<bool, string> check)
    {
        check(OperationsControlService.CanManage(Admin), "control: administrator allowed");
        foreach (var role in new[] { Roles.Operation, Roles.Supervisor, Roles.Manager, Roles.Management, Roles.Viewer, Roles.Subcontractor, "unknown" })
            check(!OperationsControlService.CanManage(Admin with { Role = role }), "control: reject " + role);
        check(!OperationsControlService.CanManage(null) && !OperationsControlService.CanManage(Admin with { Recognised = false }), "control: identity required");
        var migration = new OperationsAiControl();
        check(migration.UpOperations.Count == 2 && migration.UpOperations[0] is CreateTableOperation { Name: "ai_operations_control" }
            && migration.UpOperations[1] is InsertDataOperation seed && Equals(seed.Values[0, 1], false), "control: migration only creates and seeds disabled switch");
        foreach (var state in new[] { new OperationsControlState(true, false, 0, false), new(false, true, 0, false), new(true, true, 1, true) })
        {
            check(!OperationsControlService.Effective(state), "control: off, missing and emergency states fail closed");
            using var limiter = new AiRunLimiter();
            var provider = new StubProvider((_, _) => throw new Exception("must not call provider"));
            var runtime = new AgentOrchestrator(Options.Create(new AiOptions { Enabled = true, ChatEnabled = true, OperationsAgentEnabled = true }),
                new TestEnvironment(), provider, new AgentRegistry(), limiter, NullLogger<AgentOrchestrator>.Instance,
                control: new TestOperationsControl(state));
            check((await runtime.RunAsync(new("today"), Admin, default)).Response.Code == "disabled" && provider.Calls == 0,
                "control: durable disabled state overrides old flags before provider");
            var status = await runtime.StatusAsync(Admin, default);
            check(status.Agents.Single(a => a.Id == "operations-agent").Enabled == false && !status.WriteToolsReady,
                "control: status agrees with runtime and never grants writes");
        }
        check(OperationsControlService.Effective(new(true, true, 1, false)), "control: enabled state recognized");
    }

    // Called ONLY inside AuditChecks' newly created LocalDB test database.
    public static async Task SqlAsync(DbContextOptions<ScmosDbContext> options, Action<bool, string> check)
    {
        await using (var setup = new ScmosDbContext(options))
        {
            foreach (var migration in new Migration[] { new AuditTrail(), new AuditSignInMethod(), new OperationsAiControl() })
            {
                migration.ActiveProvider = "Microsoft.EntityFrameworkCore.SqlServer";
                foreach (var command in setup.GetService<IMigrationsSqlGenerator>().Generate(migration.UpOperations, setup.Model))
                    await setup.Database.ExecuteSqlRawAsync(command.CommandText);
            }
        }
        OperationsControlService Service(ScmosDbContext db, bool emergency = false) => new(db,
            new AuditService(db, new HttpContextAccessor(), NullLogger<AuditService>.Instance),
            Options.Create(new AiOptions { OperationsEmergencyDisabled = emergency }), NullLogger<OperationsControlService>.Instance);
        async Task<OperationsControlState> Read(bool emergency = false)
        {
            await using var db = new ScmosDbContext(options);
            return await Service(db, emergency).ReadAsync(default);
        }
        async Task<string> Set(bool enabled, int revision, AppUser? user = null)
        {
            await using var db = new ScmosDbContext(options);
            return await Service(db).SetAsync(user ?? Admin, enabled, revision, default);
        }
        check(await Read() is { Available: true, Enabled: false, Revision: 0 }, "control SQL: starts disabled");
        check(await Set(true, 0, Admin with { Role = Roles.Operation }) == "forbidden", "control SQL: operator cannot change state");
        check(await Set(true, 0) == "ok" && await Read() is { Enabled: true, Revision: 1 }, "control SQL: enabled state survives new context");
        check(await Set(false, 0) == "control_conflict" && (await Read()).Enabled, "control SQL: stale browser cannot overwrite state");
        check(!OperationsControlService.Effective(await Read(true)), "control SQL: server emergency stop wins");
        check(await Set(false, 1) == "ok" && await Read() is { Enabled: false, Revision: 2 }, "control SQL: disable persists");
        await using (var db = new ScmosDbContext(options))
        {
            var history = await db.AuditEvents.OrderBy(a => a.Id).ToListAsync();
            check(history.Count == 2 && history[0].OldValue == "false" && history[0].NewValue == "true"
                && history[1].NewValue == "false" && history.All(a => a.Who == Admin.Signature && a.Entity == "ai-control"), "control SQL: actor and before/after audit persisted");
            await db.Database.ExecuteSqlRawAsync("ALTER TABLE audit_events WITH NOCHECK ADD CONSTRAINT control_test_failure CHECK (new_value <> 'true');");
        }
        check(await Set(true, 2) == "control_unavailable" && await Read() is { Enabled: false, Revision: 2 }, "control SQL: audit failure rolls back switch atomically");
        await using (var db = new ScmosDbContext(options))
            await db.Database.ExecuteSqlRawAsync("ALTER TABLE audit_events DROP CONSTRAINT control_test_failure;");
        var race = await Task.WhenAll(Set(true, 2), Set(true, 2));
        check(race.Count(code => code == "ok") == 1 && race.Count(code => code == "control_conflict") == 1, "control SQL: concurrent changes do not silently overwrite");
        check(await Set(false, 3) == "ok", "control SQL: leave test switch off");
    }
}
