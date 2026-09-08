using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Scmos.Api.Ai;
using Scmos.Api.Ai.Operations;
using Scmos.Api.Auth;
using Scmos.Api.Data;
using Scmos.Api.Data.Migrations;
using Scmos.Api.Endpoints;
using Scmos.Api.Rules;
using Scmos.Api.Services;

static class AuditChecks
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-09-07T09:00:00Z");
    private static readonly AppUser Operator = new("audit-test-user", "test@example.invalid", "Test",
        Roles.Operation, "OP-A", "test", true);
    private static AiExecutionEvent Start(string? runId = null) => new(runId ?? Guid.NewGuid().ToString("N"),
        Operator.UserId, Operator.Role, "operations-agent", "run_started", null, "running", Now,
        Scope: new(false, "OP-A"), Model: "gpt-4.1");
    private static AiExecutionEvent Tool(AiExecutionEvent start) => start with
    {
        Event = "tool_started", At = Now.AddSeconds(1), Tool = "query_shipments",
        ToolCallId = Guid.NewGuid().ToString("N"), View = "risk_today", Limit = 2, Usage = new(10, 5),
    };
    private static AiExecutionEvent Finish(AiExecutionEvent tool) => tool with
    {
        Event = "tool_completed", Status = "succeeded", At = Now.AddSeconds(2),
        Total = 9, Returned = 2, SourceKeys = ["job-1", "job-2"],
    };
    private static void Refuses(Action action, Action<bool, string> check, string name)
    {
        try { action(); check(false, name); }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException) { check(true, name); }
    }
    private static async Task RefusesAsync(Func<Task> action, Action<bool, string> check, string name)
    {
        try { await action(); check(false, name); }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or SqlException) { check(true, name); }
    }

    public static async Task RunAsync(Action<bool, string> check, bool localDb, bool isolated)
    {
        var start = Start();
        var tool = Tool(start);
        var finish = Finish(tool);
        var end = finish with { Event = "run_completed", At = Now.AddSeconds(3) };
        var rows = new List<AiAuditLog>();
        foreach (var e in new[] { start, tool, finish, end })
        {
            var row = AiAuditRules.From(e);
            check(AiAuditRules.MayAppend(rows, row), "D: legal audit transition " + e.Event);
            rows.Add(row);
        }
        check(!AiAuditRules.MayAppend(rows, AiAuditRules.From(end)), "D: identical replay is idempotent");
        Refuses(() => AiAuditRules.MayAppend(rows, AiAuditRules.From(end with { Total = 10 })), check, "D: replay cannot rewrite totals");
        Refuses(() => AiAuditRules.MayAppend([], AiAuditRules.From(tool)), check, "D: orphan tool refused");
        Refuses(() => AiAuditRules.MayAppend([rows[0]], rows[2]), check, "D: completion without tool start refused");
        Refuses(() => AiAuditRules.MayAppend([rows[0], rows[1]], rows[3]), check, "D: run cannot skip tool completion");
        Refuses(() => AiAuditRules.MayAppend([rows[0]], AiAuditRules.From(tool with { UserId = "other" })), check, "D: immutable run identity");
        Refuses(() => AiAuditRules.MayAppend([rows[0]], AiAuditRules.From(tool with { Scope = new(true, null) })), check, "D: immutable read scope");
        Refuses(() => AiAuditRules.MayAppend([rows[0], rows[1]], AiAuditRules.From(finish with { ToolCallId = Guid.NewGuid().ToString("N") })), check, "D: immutable tool call");
        Refuses(() => AiAuditRules.MayAppend([rows[0]], AiAuditRules.From(tool with { At = Now.AddDays(-1) })), check, "D: reversed chronology refused");
        foreach (var bad in new[]
        {
            start with { RunId = "forged" }, start with { Model = "PRIVATE key\n" },
            start with { Scope = new(false, "") }, start with { Role = "unknown" },
            start with { AgentId = "billing-agent" }, start with { Status = "PRIVATE_PROVIDER_ERROR" },
            start with { UserId = new string('x', 161) }, start with { Usage = new(-1, 2) },
            tool with { Tool = "delete_shipment" }, tool with { ToolCallId = "PROVIDER_RAW_ID" },
            tool with { Limit = 51 }, tool with { View = "PRIVATE_SEARCH_TEXT" },
            finish with { Returned = 3 }, finish with { Total = 1 }, finish with { SourceKeys = ["same", "same"] },
            finish with { SourceKeys = ["job-1", new string('x', 81)] },
        }) Refuses(() => AiAuditRules.From(bad), check, "D: unsafe audit metadata rejected");
        var failed = start with { Event = "run_completed", Status = "provider_busy", At = Now.AddSeconds(1), Usage = new(1, 0) };
        check(AiAuditRules.MayAppend([rows[0]], AiAuditRules.From(failed)), "D: provider failure closes run without tool");
        var projected = AiAuditReader.Project(rows, Now.AddMinutes(5));
        check(projected.Status == "succeeded" && projected.SourceKeys.Length == 2
            && projected.Usage == new AiUsage(10, 5), "D: durable timeline projects evidence and usage");
        check(AiAuditReader.Project([rows[0]], Now.AddSeconds(10)).Status == "running", "D: active run remains running");
        check(AiAuditReader.Project([rows[0]], Now.AddMinutes(3)).Status == "incomplete", "D: abandoned run is incomplete, never guessed success");
        check(AiAuditReader.Allowed(Operator), "D: existing operator ViewAudit remains authorized");
        foreach (var role in new[] { Roles.Subcontractor, Roles.Viewer, Roles.Management, "unknown" })
            check(!AiAuditReader.Allowed(Operator with { Role = role }), "D: audit refuses role " + role);
        check(!AiAuditReader.Allowed(Operator with { Recognised = false }) && !AiAuditReader.Allowed(null),
            "D: unrecognized/anonymous audit refused");
        var migration = new AiExecutionAudit();
        check(migration.UpOperations.OfType<CreateTableOperation>().Single().Name == "ai_audit_logs"
            && migration.UpOperations.All(op => op is CreateTableOperation or CreateIndexOperation),
            "D: migration only adds one audit table and indexes");
        check(migration.UpOperations.OfType<CreateIndexOperation>().Any(i => i.IsUnique
            && i.Columns.SequenceEqual(["run_id", "sequence"])), "D: database enforces unique event sequence");
        check(migration.DownOperations.Count == 1 && migration.DownOperations[0] is SqlOperation sql && sql.Sql.Contains("THROW"),
            "D: rollback cannot drop audit evidence");
        var offline = new SqlAiExecutionAudit(new DbContextOptionsBuilder<ScmosDbContext>().Options);
        check(!offline.Ready && !await offline.CheckReadyAsync(default), "D: absent provider/schema never reports audit ready");
        // Metadata comparison only: no connection is opened and no migrations are applied.
        await using (var model = new ScmosDbContext(new DbContextOptionsBuilder<ScmosDbContext>()
            .UseSqlServer("Server=127.0.0.1,1;Database=offline-model-check;User Id=unused;Password=unused;Encrypt=True").Options))
            check(!model.Database.HasPendingModelChanges(), "D: merged migration snapshot matches the runtime model");
        if (localDb) await SqlAsync(check, migration, isolated);
    }

    private static async Task SqlAsync(Action<bool, string> check, AiExecutionAudit migration, bool isolated)
    {
        // No environment override, appsettings, Entra login, Azure credentials, or live provider.
        var database = "ScmosAiAuditTests_" + Guid.NewGuid().ToString("N");
        var instance = isolated ? "ScmosAiAuditCheck_20260907" : "MSSQLLocalDB";
        var localSource = "(localdb)\\" + instance;
        var connection = $"Server={localSource};Database={database};Integrated Security=true;TrustServerCertificate=true;Connect Timeout=10";
        var options = new DbContextOptionsBuilder<ScmosDbContext>().UseSqlServer(connection,
            sql => { sql.EnableRetryOnFailure(3); sql.UseCompatibilityLevel(150); }).Options;
        ScmosDbContext Open() => new(options);
        SqlAiExecutionAudit Sink() => new(options);
        var masterConnection = new SqlConnectionStringBuilder(connection) { InitialCatalog = "master" }.ConnectionString;
        await using var master = new SqlConnection(masterConnection);
        await master.OpenAsync();
        await using var create = master.CreateCommand();
        create.CommandText = $"CREATE DATABASE [{database}]";
        await create.ExecuteNonQueryAsync();
        try
        {
            var missing = Sink();
            check(!await missing.CheckReadyAsync(default), "D SQL: missing migration refuses readiness");
            var noSchemaSource = new OperationsFixtureSource([]);
            var noSchemaProvider = new OperationsFixtureProvider();
            var noSchemaAgent = new OperationsAgent(new ToolRegistry(new(noSchemaSource, new OperationsClock(Now))),
                missing, noSchemaProvider, new OperationsClock(Now));
            check((await noSchemaAgent.RunAsync(Guid.NewGuid().ToString("N"), new("today"), Operator,
                new AgentRegistry().Find("operations-agent")!, default)).Code == "audit_not_ready"
                && noSchemaProvider.Calls == 0 && noSchemaSource.Reads == 0, "D SQL: no schema blocks provider and source");
            await using (var setup = Open())
            {
                migration.ActiveProvider = "Microsoft.EntityFrameworkCore.SqlServer";
                var commands = setup.GetService<IMigrationsSqlGenerator>().Generate(migration.UpOperations, setup.Model);
                // Apply ONLY Phase D Up, not old data/seed migrations from other modules.
                foreach (var command in commands) await setup.Database.ExecuteSqlRawAsync(command.CommandText);
                await setup.Database.ExecuteSqlRawAsync("CREATE TABLE phase_d_sentinel (id int NOT NULL PRIMARY KEY); INSERT INTO phase_d_sentinel VALUES (42);");
            }
            check(await Sink().CheckReadyAsync(default), "D SQL: installed mapped audit shape is ready");
            var start = Start();
            var tool = Tool(start);
            var finish = Finish(tool);
            var end = finish with { Event = "run_completed", At = Now.AddSeconds(3) };
            foreach (var e in new[] { start, tool, finish, end }) await Sink().RecordAsync(e, default);
            await using (var verify = Open())
                check(await verify.AiAuditLogs.CountAsync() == 4, "D SQL: four events survive fresh contexts");
            await Task.WhenAll(Sink().RecordAsync(end, default), Sink().RecordAsync(end, default));
            await RefusesAsync(() => Sink().RecordAsync(end with { UserId = "other" }, default), check, "D SQL: conflicting retry refused");
            var concurrent = Start();
            await Task.WhenAll(Sink().RecordAsync(concurrent, default), Sink().RecordAsync(concurrent, default));
            await using (var verify = Open())
                check(await verify.AiAuditLogs.CountAsync() == 5, "D SQL: concurrent identical starts/completions persist once");
            await RefusesAsync(() => Sink().RecordAsync(Tool(Start()), default), check, "D SQL: orphan event refused atomically");
            await RefusesAsync(() => Sink().RecordAsync(Tool(concurrent) with { UserId = "wrong" }, default), check, "D SQL: changed actor refused atomically");
            await using (var pending = Open())
            {
                pending.OperationJobs.Add(new() { Key = "MUST_NOT_BE_SAVED" });
                await Sink().RecordAsync(Start(), default);
                check(pending.ChangeTracker.Entries().Single().State == EntityState.Added,
                    "D SQL: audit never saves business context changes (business table not even installed)");
            }
            var reader = new AiAuditReader(Sink(), new OperationsClock(Now.AddMinutes(4)));
            var page = await reader.PageAsync(Operator, null, 1, default);
            check(page.Runs.Length == 1 && page.NextBeforeId.HasValue, "D SQL: audit page bounded with continuation");
            var page2 = await reader.PageAsync(Operator, page.NextBeforeId, 1, default);
            check(page.Runs[0].RunId != page2.Runs[0].RunId, "D SQL: continuation does not repeat a run");
            var run = await reader.RunAsync(Operator, start.RunId, default);
            check(run?.Status == "succeeded" && run.ToolStatus == "succeeded" && run.Events.Length == 4
                && run.Scope == new AiReadScope(false, "OP-A"), "D SQL: run/tool identity scope and states survive reopen");
            check(await reader.RunAsync(Operator, Guid.NewGuid().ToString("N"), default) is null, "D SQL: unknown run is not fabricated");
            try { await reader.PageAsync(Operator with { Role = Roles.Subcontractor }, null, 1, default); check(false, "D SQL: reader permission"); }
            catch (UnauthorizedAccessException) { check(true, "D SQL: service independently enforces audit permission"); }
            await HttpAsync(check, options);
            var cancellationProvider = new AuditCancellationProvider();
            var cancellationSource = new OperationsFixtureSource([]);
            var cancellationAgent = new OperationsAgent(new ToolRegistry(new(cancellationSource, new OperationsClock(Now))),
                Sink(), cancellationProvider, new OperationsClock(Now));
            var cancelledRun = Guid.NewGuid().ToString("N");
            using (var cancellation = new CancellationTokenSource())
            {
                var pending = cancellationAgent.RunAsync(cancelledRun, new("today"), Operator,
                    new AgentRegistry().Find("operations-agent")!, cancellation.Token);
                await cancellationProvider.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
                cancellation.Cancel();
                try { await pending; check(false, "D SQL: cancellation"); }
                catch (OperationCanceledException) { check(true, "D SQL: caller cancellation propagates after cleanup"); }
            }
            var cancelled = await reader.RunAsync(Operator, cancelledRun, default);
            check(cancelled?.Status == "cancelled" && cancelled.Events.Length == 2 && cancellationSource.Reads == 0,
                "D SQL: cancellation terminal event persists with independent cleanup token");
            await using (var verify = Open())
            {
                var sentinel = await verify.Database.SqlQueryRaw<int>("SELECT id AS Value FROM phase_d_sentinel").SingleAsync();
                check(sentinel == 42, "D SQL: unrelated table unchanged");
                foreach (var command in verify.GetService<IMigrationsSqlGenerator>().Generate(migration.DownOperations, verify.Model))
                    await RefusesAsync(() => verify.Database.ExecuteSqlRawAsync(command.CommandText), check, "D SQL: downgrade refuses audit deletion");
                check(await verify.AiAuditLogs.AnyAsync(), "D SQL: audit evidence survives refused downgrade");
            }
        }
        finally
        {
            // Only delete the exact randomly named LocalDB database created above.
            var target = new SqlConnectionStringBuilder(connection);
            if (target.DataSource != localSource || target.InitialCatalog != database
                || !database.StartsWith("ScmosAiAuditTests_", StringComparison.Ordinal)
                || !Guid.TryParseExact(database["ScmosAiAuditTests_".Length..], "N", out _))
                throw new InvalidOperationException("Unexpected test database target.");
            SqlConnection.ClearAllPools();
            await using var cleanup = Open();
            await cleanup.Database.EnsureDeletedAsync();
            Console.WriteLine("Removed disposable LocalDB test database: " + database);
        }
    }

    private static async Task HttpAsync(Action<bool, string> check, DbContextOptions<ScmosDbContext> options)
    {
        var provider = new OperationsFixtureProvider();
        var source = new OperationsFixtureSource([]);
        var users = new TestUsers();
        var builder = WebApplication.CreateBuilder();
        builder.Configuration.Sources.Clear();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["AI:Enabled"] = "true", ["AI:ChatEnabled"] = "true", ["AI:OperationsAgentEnabled"] = "true",
        });
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddAiFoundation(builder.Configuration);
        builder.Services.AddSingleton(options);
        builder.Services.AddScoped(_ => new ScmosDbContext(options));
        builder.Services.AddSingleton<IUserAccessor>(users);
        builder.Services.AddSingleton<IAiProvider>(provider);
        builder.Services.AddSingleton<IOperationsSource>(source);
        builder.Services.AddSingleton<TimeProvider>(new OperationsClock(Now));
        builder.Services.Configure<OpenAiOptions>(_ => { });
        builder.Services.AddScoped<AiGateway>();
        await using var app = builder.Build();
        app.MapAiFoundation();
        await app.StartAsync();
        try
        {
            using var http = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
            check((int)(await http.GetAsync("/api/ai/audit")).StatusCode == 401, "D HTTP: audit authentication required");
            users.User = Operator with { Role = Roles.Subcontractor };
            check((int)(await http.GetAsync("/api/ai/audit")).StatusCode == 403, "D HTTP: carrier cannot read audit");
            users.User = Operator;
            check((int)(await http.GetAsync("/api/ai/audit?take=101")).StatusCode == 400, "D HTTP: bounded audit pagination");
            check((int)(await http.GetAsync("/api/ai/audit/not-a-run")).StatusCode == 400, "D HTTP: malformed run refused");
            var response = await http.PostAsync("/api/ai/chat",
                new StringContent("{\"message\":\"PRIVATE_USER_PROMPT งานเสี่ยงวันนี้\"}", Encoding.UTF8, "application/json"));
            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            check(response.IsSuccessStatusCode, "D HTTP: real SQL audit enables fake-provider/source Operations");
            var runId = body.RootElement.GetProperty("runId").GetString()!;
            var history = await http.GetAsync("/api/ai/audit/" + runId);
            var text = await history.Content.ReadAsStringAsync();
            using var trace = JsonDocument.Parse(text);
            check(history.IsSuccessStatusCode && history.Headers.CacheControl?.NoStore == true
                && trace.RootElement.GetProperty("events").GetArrayLength() == 4, "D HTTP: complete persisted trace is readable and private");
            check(!text.Contains("PRIVATE_") && !text.Contains("Arguments") && !text.Contains("MODEL_INVENTED")
                && !text.Contains("ApiKey") && trace.RootElement.GetProperty("total").GetInt32() == 0,
                "D HTTP: prompt/provider text not retained and genuine zero remains zero");
            await using var db = new ScmosDbContext(options);
            var raw = JsonSerializer.Serialize(await db.AiAuditLogs.AsNoTracking().Where(e => e.RunId == runId).ToListAsync());
            check(!raw.Contains("PRIVATE_") && !raw.Contains("MODEL_INVENTED"), "D SQL: no raw prompt/provider text in storage");
            provider.Selection = new("provider_busy");
            var failed = await http.PostAsync("/api/ai/chat", new StringContent("{\"message\":\"today\"}", Encoding.UTF8, "application/json"));
            using var failBody = JsonDocument.Parse(await failed.Content.ReadAsStringAsync());
            var failureTrace = await http.GetStringAsync("/api/ai/audit/" + failBody.RootElement.GetProperty("runId").GetString());
            check((int)failed.StatusCode == 429 && failureTrace.Contains("provider_busy"), "D HTTP: provider failure persists sanitized terminal state");
            provider.Reset();
            foreach (var failAt in new[] { "run_started", "tool_started", "tool_completed", "run_completed" })
            {
                // Fault injection only on this runner's isolated table, never on SCMOS/Azure.
                await db.Database.ExecuteSqlRawAsync(
                    $"ALTER TABLE dbo.ai_audit_logs WITH NOCHECK ADD CONSTRAINT audit_check_failure CHECK ([event] <> '{failAt}');");
                var calls = provider.Calls;
                var reads = source.Reads;
                var blocked = await http.PostAsync("/api/ai/chat",
                    new StringContent("{\"message\":\"today\"}", Encoding.UTF8, "application/json"));
                var blockedText = await blocked.Content.ReadAsStringAsync();
                using var blockedBody = JsonDocument.Parse(blockedText);
                check((int)blocked.StatusCode == 503 && blockedText.Contains("audit_not_ready")
                    && !blockedText.Contains("PRIVATE_") && blockedBody.RootElement.GetProperty("evidence").ValueKind == JsonValueKind.Null,
                    "D SQL: persistence failure withholds evidence at " + failAt);
                check(provider.Calls - calls == (failAt == "run_started" ? 0 : 1)
                    && source.Reads - reads == (failAt is "run_started" or "tool_started" ? 0 : 1),
                    "D SQL: failed audit gates provider/source at " + failAt);
                await db.Database.ExecuteSqlRawAsync("ALTER TABLE dbo.ai_audit_logs DROP CONSTRAINT audit_check_failure;");
            }
            users.User = Operator with { Role = Roles.Viewer };
            check((int)(await http.GetAsync("/api/ai/audit/" + runId)).StatusCode == 403, "D HTTP: guessed run ID does not bypass permission");
        }
        finally { await app.StopAsync(); }
    }
}

sealed class AuditCancellationProvider : IAiProvider
{
    public bool Configured => true;
    public bool IsMock => false;
    public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public async Task<AiProviderResult> CompleteAsync(AiProviderRequest request, CancellationToken token)
    {
        Started.TrySetResult();
        await Task.Delay(Timeout.Infinite, token);
        return new("unreachable");
    }
}
