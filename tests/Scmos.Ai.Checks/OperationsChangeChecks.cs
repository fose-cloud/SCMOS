using System.Text.Json;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Scmos.Api.Endpoints;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Scmos.Api.Ai;
using Scmos.Api.Ai.Operations;
using Scmos.Api.Auth;
using Scmos.Api.Data;
using Scmos.Api.Rules;
using Scmos.Api.Services;

static class OperationsChangeChecks
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-09-14T03:00:00Z");
    private static readonly AppUser Operator = new("change-op", "op@test.invalid", "Operator", Roles.Operation, "OP-W1", "test", true);
    private static readonly AppUser Supervisor = Operator with { UserId = "change-sv", Email = "sv@test.invalid", Role = Roles.Supervisor, OperatorId = "SV-W1" };
    private static OperationJob Job(string key) => new() { Key = key, Cat = "IMPORT", OwnerId = Operator.OperatorId,
        Owner = "Operator", WorkDate = "14/09/2026", Status = "RECEIVED", UpdatedAt = Now,
        Data = "{\"key\":\"ignored\",\"planTime\":\"08:00\",\"remark\":\"retain me\",\"extra\":{\"a\":1}}" };
    private static StaffMember Assignee => new() { Id = "OP-W2", Name = "Second Operator", Role = Roles.Operation, Active = true };
    private static OperationsChangeRequest Request(OperationJob job) => new(job.Key, OperationsChangePolicy.Fingerprint(job),
        new() { ["date"] = "15/09/2026", ["planTime"] = "09:30", ["status"] = "WAITING_SUPPLIER", ["opId"] = "OP-W2" }, "Customer confirmed new plan");

    public static async Task RunAsync(Action<bool, string> check, bool sql)
    {
        var command = OperationsChangeCommand.Parse("เสนอแก้งาน apply; วันที่ 15/09/2026; เวลา 09:30; สถานะ WAITING_SUPPLIER; ผู้รับผิดชอบ OP-W2; เหตุผล Confirmed");
        check(command?.Key == "apply" && command.Changes.Count == 4 && command.Reason == "Confirmed", "command: explicit four-field draft");
        check(OperationsChangeCommand.Parse("/แก้งาน apply; เวลา 09:30; เหตุผล Confirmed")?.Changes["planTime"] == "09:30", "command: slash alias");
        foreach (var invalid in new[] { "", "แก้งานทั้งหมด", "เสนอแก้งาน a b; เวลา 09:30; เหตุผล test",
            "เสนอแก้งาน a; เวลา 09:30", "เสนอแก้งาน a; เวลา 09:30; เวลา 10:00; เหตุผล test",
            "เสนอแก้งาน a; sql UPDATE; เหตุผล test", "เสนอแก้งาน a; เวลา 09:30; เหตุผล test; เหตุผล again",
            new string('x', 2001) })
            check(OperationsChangeCommand.Parse(invalid) is null, "command: ambiguous/unsupported syntax rejected");
        check(!OperationsChangePolicy.CanApprove(Operator) && OperationsChangePolicy.CanRequest(Operator), "write: operator proposes, cannot approve");
        foreach (var role in Roles.All)
            check(OperationsChangePolicy.CanApprove(Supervisor with { Role = role.Name })
                == new[] { Roles.Admin, Roles.Manager, Roles.AssistantManager, Roles.Supervisor }.Contains(role.Name), "write: approval role " + role.Name);
        check(!OperationsChangePolicy.CanRequest(null) && !OperationsChangePolicy.CanRequest(Operator with { Role = Roles.Subcontractor })
            && !OperationsChangePolicy.CanApprove(Supervisor with { Recognised = false }), "write: anonymous/carrier/unrecognized denied");
        var job = Job("pure");
        var request = Request(job);
        check(OperationsChangePolicy.Validate(request, job, Assignee) == "ok", "write: Operation User is a valid assignee");
        check(OperationsChangePolicy.Validate(request, job, withNotActive()) == "invalid_assignee", "write: inactive assignee denied");
        StaffMember withNotActive() => new() { Id = "OP-W2", Name = "Inactive", Role = Roles.Operation, Active = false };
        foreach (var pair in new[] { ("date", "31/02/2026"), ("date", "14/09/2569"), ("planTime", "24:01"),
            ("status", "LOADING"), ("status", "COMPLETED"), ("status", "CANCELLED"), ("owner", "forged"), ("sql", "UPDATE jobs") })
            check(OperationsChangePolicy.Validate(request with { Changes = new() { [pair.Item1] = pair.Item2 } }, job, Assignee) != "ok", "write: refuse " + pair.Item1 + "=" + pair.Item2);
        check(OperationsChangePolicy.Validate(request with { Reason = "" }, job, Assignee) != "ok", "write: reason required");
        check(OperationsChangePolicy.Validate(request with { Version = "old" }, job, Assignee) != "ok", "write: stale snapshot refused");
        OperationsChangePolicy.Stage(job, request.Changes, Assignee, Supervisor.Signature, Now.AddMinutes(1));
        using (var json = JsonDocument.Parse(job.Data))
            check(job.OwnerId == "OP-W2" && job.Owner == "Second Operator" && job.WorkDate == "15/09/2026"
                && json.RootElement.GetProperty("origDate").GetString() == "14/09/2026"
                && json.RootElement.GetProperty("extra").GetProperty("a").GetInt32() == 1
                && json.RootElement.GetProperty("remark").GetString() == "retain me", "write: indexed values synchronized, unrelated JSON and original date preserved");
        check(!new AiOptions().OperationsWritesEnabled, "write: new write gate defaults off");
        var attentionRows = new[] { ("missing-driver", "", "PLATE"), ("missing-plate", "Driver", ""), ("complete", "Driver", "PLATE") }
            .Select(x => JobsRepository.AnalysisRow(x.Item1, "OP-W1", JsonSerializer.Serialize(new { key = x.Item1,
                cat = "IMPORT", date = "14/09/2026", op = "Operator", opId = "OP-W1", trucker = "Carrier",
                driver = x.Item2, licence = x.Item3, status = "RECEIVED" }), Now)).ToArray();
        var attention = new OperationsAttentionService(new OperationsFixtureSource(attentionRows), new OperationsClock(Now));
        var attentionResult = await attention.ReadAsync(Operator, default);
        check(attentionResult.Total == 2 && attentionResult.Rows.All(r => r.Risk is null),
            "attention: missing either name or plate alerts without changing Monitor risk");
        check((await attention.ReadAsync(Operator with { Role = Roles.Management, OperatorId = "OTHER" }, default)).Total == 0,
            "attention: restricted owner scope is preserved");
        if (!sql) return;
        // The connection is compiled test-only LocalDB. Never read settings or accept a server argument.
        var database = "SCMOS_AI_WRITE_TEST_" + Guid.NewGuid().ToString("N");
        var connection = $"Server=(localdb)\\MSSQLLocalDB;Database={database};Integrated Security=true;TrustServerCertificate=true";
        var options = new DbContextOptionsBuilder<ScmosDbContext>().UseSqlServer(connection,
            s => { s.UseCompatibilityLevel(150); s.EnableRetryOnFailure(3); }).Options;
        await using var setup = new ScmosDbContext(options);
        await setup.Database.EnsureCreatedAsync();
        try
        {
            setup.Staff.AddRange(new StaffMember { Id = Operator.OperatorId, Name = "Operator", Email = Operator.Email, Role = Operator.Role },
                new StaffMember { Id = Supervisor.OperatorId, Name = "Supervisor", Email = Supervisor.Email, Role = Supervisor.Role }, Assignee);
            (await setup.AiOperationsControls.SingleAsync(c => c.Id == 1)).Enabled = true;
            setup.OperationJobs.AddRange(Job("apply"), Job("stale"), Job("audit-fail"), Job("expired"), Job("race"));
            await setup.SaveChangesAsync();
            using var memory = new MemoryCache(new MemoryCacheOptions());
            OperationsChangeService Service(ScmosDbContext db, bool enabled = true, DateTimeOffset? at = null)
                => new(db, new AuditService(db, new HttpContextAccessor(), NullLogger<AuditService>.Instance),
                    new JobRegisterCache(db, memory, NullLogger<JobRegisterCache>.Instance), new OperationsClock(at ?? Now),
                    Options.Create(new AiOptions { OperationsWritesEnabled = enabled }));
            async Task<OperationsChangeResult> Propose(string key)
            {
                await using var db = new ScmosDbContext(options);
                return await Service(db).ProposeAsync(Request(Job(key)), Operator, default);
            }
            async Task<OperationsChangeResult> Confirm(long id, string key, AppUser? by = null, DateTimeOffset? at = null)
            {
                await using var db = new ScmosDbContext(options);
                return await Service(db, at: at).ConfirmAsync(id, OperationsChangePolicy.Fingerprint(Job(key)), true, "Reviewed exact changes", by ?? Supervisor, default);
            }
            await using (var off = new ScmosDbContext(options))
                check((await Service(off, false).ProposeAsync(Request(Job("apply")), Operator, default)).Code == "write_disabled", "write SQL: disabled gate blocks proposal");
            await using (var draftDb = new ScmosDbContext(options))
            {
                var beforeApprovals = await draftDb.Approvals.CountAsync();
                var beforeAudits = await draftDb.AuditEvents.CountAsync();
                foreach (var message in new[] { "เลื่อนงานพรุ่งนี้", "เสนอแก้งาน apply; เวลา 09:30",
                    "เสนอแก้งาน apply; วันที่ พรุ่งนี้; เหตุผล test",
                    "เสนอแก้งาน apply; เวลา 09:30; เวลา 10:00; เหตุผล test" })
                {
                    var clarification = JsonSerializer.SerializeToElement(await Service(draftDb).InterpretAsync(message, Operator, default));
                    check(clarification.GetProperty("code").GetString() == "clarification_required"
                        && clarification.GetProperty("questions").GetArrayLength() > 0, "clarification SQL: asks before creating proposal");
                }
                var draft = JsonSerializer.SerializeToElement(await Service(draftDb, false).InterpretAsync(
                    "เสนอแก้งาน apply; เวลา 09:30; เหตุผล Confirmed", Operator, default),
                    new JsonSerializerOptions(JsonSerializerDefaults.Web));
                check(draft.GetProperty("code").GetString() == "draft" && !draft.GetProperty("preview").GetProperty("enabled").GetBoolean(),
                    "command SQL: read-only draft available with writes disabled");
                check(await draftDb.Approvals.CountAsync() == beforeApprovals && await draftDb.AuditEvents.CountAsync() == beforeAudits
                    && (await draftDb.OperationJobs.AsNoTracking().SingleAsync(j => j.Key == "apply")).WorkDate == "14/09/2026",
                    "command SQL: interpreting creates no proposal, audit or job change");
                foreach (var invalid in new[] { "วันที่ พรุ่งนี้", "เวลา 25:00", "ผู้รับผิดชอบ MISSING", "สถานะ COMPLETED" })
                {
                    var result = JsonSerializer.SerializeToElement(await Service(draftDb).InterpretAsync(
                        "เสนอแก้งาน apply; " + invalid + "; เหตุผล test", Operator, default));
                    check(result.GetProperty("code").GetString() != "draft", "command SQL: field validation " + invalid);
                }
                var forbidden = JsonSerializer.SerializeToElement(await Service(draftDb).InterpretAsync(
                    "เสนอแก้งาน apply; เวลา 09:30; เหตุผล test", Operator with { OperatorId = "OTHER" }, default));
                check(forbidden.GetProperty("code").GetString() == "forbidden", "command SQL: foreign job denied");
            }
            var pending = await Propose("apply");
            check(pending.Code == "pending", "write SQL: persisted proposal");
            check((await setup.OperationJobs.AsNoTracking().SingleAsync(j => j.Key == "apply")).OwnerId == Operator.OperatorId, "write SQL: proposing never edits job");
            check((await Confirm(pending.Id!.Value, "apply", Operator)).Code == "forbidden", "write SQL: direct operator confirmation denied");
            check((await Confirm(pending.Id.Value, "apply")).Code == "applied", "write SQL: supervisor confirmation applies");
            check((await Confirm(pending.Id.Value, "apply")).Code == "already_applied", "write SQL: replay has no second effect");
            check((await setup.OperationJobs.AsNoTracking().SingleAsync(j => j.Key == "apply")).OwnerId == "OP-W2"
                && await setup.AuditEvents.CountAsync(a => a.EntityId == "apply" && a.Action == "edit") == 4, "write SQL: exact changes and audit persisted once");
            var stale = await Propose("stale");
            await setup.OperationJobs.Where(j => j.Key == "stale").ExecuteUpdateAsync(s => s.SetProperty(j => j.UpdatedBy, "other").SetProperty(j => j.UpdatedAt, Now.AddSeconds(1)));
            check((await Confirm(stale.Id!.Value, "stale")).Code == "stale", "write SQL: another edit prevents overwrite");
            var expired = await Propose("expired");
            check((await Confirm(expired.Id!.Value, "expired", at: Now.AddMinutes(31))).Code == "expired", "write SQL: expired proposal cannot execute");
            var race = await Propose("race");
            var concurrent = await Task.WhenAll(Confirm(race.Id!.Value, "race"), Confirm(race.Id.Value, "race"));
            check(concurrent.Count(r => r.Code == "applied") == 1 && concurrent.All(r => r.Code is "applied" or "already_applied"), "write SQL: concurrent confirmation has one effect");
            var failed = await Propose("audit-fail");
            await setup.Database.ExecuteSqlRawAsync("ALTER TABLE audit_events ADD CONSTRAINT write_test_fail CHECK (entity_id <> 'audit-fail' OR action <> 'edit')");
            check((await Confirm(failed.Id!.Value, "audit-fail")).Code == "write_unavailable", "write SQL: audit failure refuses write");
            check((await setup.OperationJobs.AsNoTracking().SingleAsync(j => j.Key == "audit-fail")).OwnerId == Operator.OperatorId
                && (await setup.Approvals.AsNoTracking().SingleAsync(a => a.Id == failed.Id)).State == "pending", "write SQL: audit failure rolls back job and approval");
            await using var legacyDb = new ScmosDbContext(options);
            var legacy = new AiGateway(legacyDb);
            check(!(await legacy.DecideAsync(failed.Id.Value, true, "bypass", Supervisor, default)).Ok
                && !(await legacy.MarkAppliedAsync(failed.Id.Value, "fake success", "", Supervisor, default)).Ok, "write SQL: legacy approval routes cannot bypass typed confirmation");
            var users = new TestUsers { User = Operator };
            var builder = WebApplication.CreateBuilder();
            builder.Configuration.Sources.Clear();
            builder.Configuration.AddInMemoryCollection();
            builder.Logging.ClearProviders();
            builder.WebHost.UseUrls("http://127.0.0.1:0");
            builder.Services.AddAiFoundation(builder.Configuration);
            builder.Services.AddScoped(_ => new ScmosDbContext(options));
            builder.Services.AddSingleton<IUserAccessor>(users);
            builder.Services.AddSingleton<IOperationsSource>(new OperationsFixtureSource(attentionRows));
            builder.Services.AddSingleton<TimeProvider>(new OperationsClock(Now));
            await using var app = builder.Build();
            app.MapOperationsChanges();
            await app.StartAsync();
            try
            {
                using var http = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
                async Task<HttpResponseMessage> Post(string path, string body, bool header = true)
                {
                    using var message = new HttpRequestMessage(HttpMethod.Post, "/api/ai/operations-changes" + path)
                    { Content = new StringContent(body, Encoding.UTF8, "application/json") };
                    if (header) message.Headers.Add("X-SCMOS-AI-Control", "1");
                    return await http.SendAsync(message);
                }
                check((int)(await Post("/1/confirm", "{}")).StatusCode == 403, "write HTTP: operator cannot confirm even by direct POST");
                check((int)(await Post("/interpret", "{\"message\":\"test\"}", false)).StatusCode == 400,
                    "command HTTP: same-origin header required");
                var draftResponse = await Post("/interpret", JsonSerializer.Serialize(new { message = "เสนอแก้งาน audit-fail; เวลา 09:30; เหตุผล test" }));
                using var draftJson = JsonDocument.Parse(await draftResponse.Content.ReadAsStringAsync());
                check(draftResponse.IsSuccessStatusCode && draftResponse.Headers.CacheControl?.NoStore == true
                    && draftJson.RootElement.GetProperty("code").GetString() == "draft", "command HTTP: scoped no-store draft response");
                users.User = Supervisor;
                users.Refusal = "second factor required";
                check((int)(await Post("/1/confirm", "{}")).StatusCode == 403, "write HTTP: supervisor MFA refusal enforced");
                users.Refusal = null;
                check((int)(await Post("", "{}", false)).StatusCode == 400, "write HTTP: CSRF header required");
                foreach (var invalid in new[] { "null", new string('x', 8193), "{\"sql\":\"update jobs\"}", "{\"key\":\"apply\",\"role\":\"Administrator\"}" })
                    check((int)(await Post("", invalid)).StatusCode == 400, "write HTTP: malformed/oversized/forged request refused");
                users.User = Operator;
                var previewResponse = await http.GetAsync("/api/ai/operations-changes/preview?key=audit-fail");
                using var previewJson = JsonDocument.Parse(await previewResponse.Content.ReadAsStringAsync());
                check(previewResponse.IsSuccessStatusCode && previewResponse.Headers.CacheControl?.NoStore == true
                    && !previewJson.RootElement.GetProperty("enabled").GetBoolean(), "write HTTP: scoped preview is private and default off");
                users.User = Operator with { OperatorId = "OTHER" };
                check((int)(await http.GetAsync("/api/ai/operations-changes/preview?key=audit-fail")).StatusCode == 404, "write HTTP: guessed foreign job is not disclosed");
                users.User = Operator with { Role = Roles.Subcontractor };
                check((int)(await http.GetAsync("/api/ai/operations-changes")).StatusCode == 403
                    && (int)(await http.GetAsync("/api/ai/operations-changes/attention")).StatusCode == 403, "write HTTP: carrier cannot read proposals or team alerts");
            }
            finally { await app.StopAsync(); }
        }
        finally
        {
            // Delete only this run's explicitly named local scratch database, never application data.
            if (!database.StartsWith("SCMOS_AI_WRITE_TEST_", StringComparison.Ordinal)
                || setup.Database.GetDbConnection().DataSource != "(localdb)\\MSSQLLocalDB") throw new InvalidOperationException("Unsafe cleanup target");
            await setup.Database.EnsureDeletedAsync();
        }
    }
}
