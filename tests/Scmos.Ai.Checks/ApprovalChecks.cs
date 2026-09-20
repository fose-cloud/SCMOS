using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Scmos.Api.Ai;
using Scmos.Api.Auth;
using Scmos.Api.Data;
using Scmos.Api.Endpoints;
using Scmos.Api.Rules;
using Scmos.Api.Services;

/// <summary>
/// Phase 1E — the assistant's approval queue: who may propose, see, decide,
/// withdraw and record; staleness; the fingerprint. The pure rules run
/// offline; the SQL section (--write-local-db) proves the conditional
/// state changes, the scoped listing and the HTTP guards on a scratch
/// LocalDB database that is deleted afterwards.
/// </summary>
static class ApprovalChecks
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-09-20T12:00:00Z");
    private static readonly AppUser Operator = new("appr-op", "op@test.invalid", "Operator", Roles.Operation, "OP-A1", "test", true);
    private static readonly AppUser OtherOperator = Operator with { UserId = "appr-op2", Email = "op2@test.invalid", OperatorId = "OP-A2" };
    private static readonly AppUser Supervisor = Operator with { UserId = "appr-sv", Email = "sv@test.invalid", Role = Roles.Supervisor, OperatorId = "SV-A1" };
    private static readonly AppUser OtherSupervisor = Supervisor with { UserId = "appr-sv2", Email = "sv2@test.invalid", OperatorId = "SV-A2" };
    private static readonly AppUser Manager = Supervisor with { UserId = "appr-mg", Email = "mg@test.invalid", Role = Roles.Manager, OperatorId = "AM-A1" };
    private static readonly AppUser Carrier = Operator with { UserId = "appr-cr", Email = "cr@test.invalid", Role = Roles.Subcontractor, OperatorId = "" };
    private static readonly AppUser Viewer = Operator with { UserId = "appr-vw", Email = "vw@test.invalid", Role = Roles.CustomerService };
    private static readonly Scmos.Api.Rules.AiToolDefinition UpdateShipment = AiPermissions.Find("update_shipment")!;
    private static readonly Scmos.Api.Rules.AiToolDefinition UpdateRate = AiPermissions.Find("update_rate")!;

    private static Approval Row(AppUser requester, string state = ApprovalPolicy.Pending, DateTimeOffset? expires = null, bool legacy = false)
    {
        var payload = "{\"jobKey\":\"J-1\",\"status\":\"DISPATCHED\"}";
        return new Approval
        {
            Id = 1, Tool = "update_shipment", Agent = AiPermissions.Operation, Summary = "test", Payload = payload,
            PayloadHash = legacy ? "" : ApprovalPolicy.Hash(payload), State = state,
            RequestedBy = requester.Signature, RequesterId = legacy ? "" : requester.UserId, RequestedAt = Now,
            ExpiresAt = expires ?? ApprovalPolicy.ExpiryOf(state, Now),
        };
    }

    public static async Task RunAsync(Action<bool, string> check, bool sql)
    {
        /* ---- who is an approver, who is a requester, who sees what ---- */
        check(!ApprovalPolicy.IsApprover(null) && !ApprovalPolicy.IsApprover(Carrier) && !ApprovalPolicy.IsApprover(Operator)
            && !ApprovalPolicy.IsApprover(Supervisor with { Recognised = false }), "approval: carrier, operator and unrecognised are not approvers");
        check(ApprovalPolicy.IsApprover(Supervisor) && ApprovalPolicy.IsApprover(Manager)
            && ApprovalPolicy.IsApprover(Manager with { Role = Roles.Admin }), "approval: supervisor and above approve");
        check(ApprovalPolicy.IsRequester(Operator, Row(Operator)) && !ApprovalPolicy.IsRequester(OtherOperator, Row(Operator)),
            "approval: the requester is known by account id");
        check(ApprovalPolicy.IsRequester(Operator, Row(Operator, legacy: true)) && !ApprovalPolicy.IsRequester(OtherOperator, Row(Operator, legacy: true)),
            "approval: a row from before 1E is matched by the signature stored then");
        check(ApprovalPolicy.CanSee(Supervisor, Row(Operator)) && ApprovalPolicy.CanSee(Operator, Row(Operator))
            && !ApprovalPolicy.CanSee(OtherOperator, Row(Operator)) && !ApprovalPolicy.CanSee(Carrier, Row(Carrier))
            && !ApprovalPolicy.CanSee(null, Row(Operator)), "approval: approvers see all, a requester their own, a carrier nothing");

        /* ---- who may propose what ---- */
        check(ApprovalPolicy.RequestProblem(Operator, UpdateShipment, "move the plan", new Dictionary<string, string> { ["jobKey"] = "J-1" }) is null,
            "approval: an operator may propose a change to a job");
        check(ApprovalPolicy.RequestProblem(Carrier, UpdateShipment, "x", null) == "not-internal", "approval: a carrier may not propose");
        check(ApprovalPolicy.RequestProblem(Supervisor with { Recognised = false }, UpdateShipment, "x", null) == "not-internal", "approval: an unrecognised account may not propose");
        check(ApprovalPolicy.RequestProblem(Viewer, UpdateShipment, "x", null) == "no-capability", "approval: a view-only account may not propose a job change");
        check(ApprovalPolicy.RequestProblem(Operator, UpdateRate, "x", null) == "no-capability"
            && ApprovalPolicy.RequestProblem(Manager, UpdateRate, "x", null) is null, "approval: a rate change needs the rate capability the person would need by hand");
        check(ApprovalPolicy.RequiredToRequest(AiPermissions.Find("send_email")!) == Capability.ApproveAi
            && ApprovalPolicy.RequiredToRequest(AiPermissions.Find("close_carpar")!) == Capability.CloseCarPar
            && ApprovalPolicy.RequiredToRequest(AiPermissions.Find("assign_supplier")!) == Capability.EditOwnJobs, "approval: every approval tool maps to the capability it stands in for");
        check(ApprovalPolicy.RequestProblem(Operator, UpdateShipment, new string('x', 501), null) == "summary"
            && ApprovalPolicy.RequestProblem(Operator, UpdateShipment, "a\u0007b", null) == "summary", "approval: the summary is bounded plain text");

        /* ---- the payload is arguments, bounded, plain, never an identity ---- */
        check(ApprovalPolicy.PayloadProblem(null) is null && ApprovalPolicy.PayloadProblem(new Dictionary<string, string>()) is null, "approval: an empty payload is fine");
        var many = Enumerable.Range(0, 21).ToDictionary(i => "f" + i, i => "v");
        check(ApprovalPolicy.PayloadProblem(many) == "too-many-fields", "approval: more than twenty fields refused");
        foreach (var key in new[] { "1abc", "a b", "a-b", new string('k', 41), "", "ก" })
            check(ApprovalPolicy.PayloadProblem(new Dictionary<string, string> { [key] = "v" })?.StartsWith("key:") == true, "approval: key refused: " + key);
        foreach (var key in new[] { "role", "Role", "userId", "sql", "authorization", "operator_id" })
            check(ApprovalPolicy.PayloadProblem(new Dictionary<string, string> { [key] = "Administrator" })?.StartsWith("reserved:") == true, "approval: reserved key refused: " + key);
        check(ApprovalPolicy.PayloadProblem(new Dictionary<string, string> { ["note"] = new string('x', 501) }) == "value:note"
            && ApprovalPolicy.PayloadProblem(new Dictionary<string, string> { ["note"] = "a\u0000b" }) == "value:note"
            && ApprovalPolicy.PayloadProblem(new Dictionary<string, string> { ["note"] = "line one\nline two" }) is null, "approval: values are bounded plain text; a newline is text");
        var big = Enumerable.Range(0, 17).ToDictionary(i => "field" + i, _ => new string('ก', 500));
        check(ApprovalPolicy.PayloadProblem(big) == "too-large", "approval: the whole payload is held under 8 KB");

        /* ---- the fingerprint ---- */
        var hash = ApprovalPolicy.Hash("{\"a\":1}");
        check(hash.Length == 64 && hash == hash.ToLowerInvariant() && hash == ApprovalPolicy.Hash("{\"a\":1}") && hash != ApprovalPolicy.Hash("{\"a\":2}"),
            "approval: the fingerprint is SHA-256, lower hex, of the payload as stored");
        check(ApprovalPolicy.ExpectedHash(Row(Operator, legacy: true)) == ApprovalPolicy.Hash(Row(Operator).Payload)
            && ApprovalPolicy.ExpectedHash(Row(Operator)) == Row(Operator).PayloadHash, "approval: a row from before 1E answers to its payload's own fingerprint");

        /* ---- staleness ---- */
        check(ApprovalPolicy.ExpiryOf(ApprovalPolicy.Pending, Now) == Now.AddDays(7) && ApprovalPolicy.ExpiryOf(ApprovalPolicy.Approved, Now) == Now.AddDays(7)
            && ApprovalPolicy.ExpiryOf(ApprovalPolicy.Applied, Now) is null && ApprovalPolicy.ExpiryOf(ApprovalPolicy.Rejected, Now) is null,
            "approval: pending and approved rows expire in a week; decided-to-the-end rows never");
        check(!ApprovalPolicy.IsExpired(Row(Operator), Now.AddDays(7).AddSeconds(-1)) && ApprovalPolicy.IsExpired(Row(Operator), Now.AddDays(7))
            && !ApprovalPolicy.IsExpired(Row(Operator, ApprovalPolicy.Rejected, Now.AddDays(-1)), Now), "approval: expiry is the instant, and only for live rows");

        /* ---- deciding ---- */
        check(ApprovalPolicy.DecideProblem(Operator, Row(OtherOperator), Now) == "approver", "approval: an operator may not decide");
        check(ApprovalPolicy.DecideProblem(Carrier, Row(Operator), Now) == "approver", "approval: a carrier may not decide");
        check(ApprovalPolicy.DecideProblem(Supervisor, Row(Supervisor), Now) == "self", "approval: a supervisor may not approve their own proposal");
        check(ApprovalPolicy.DecideProblem(Supervisor, Row(Supervisor, legacy: true), Now) == "self", "approval: self-approval is refused on a row from before 1E too");
        check(ApprovalPolicy.DecideProblem(OtherSupervisor, Row(Supervisor), Now) is null, "approval: another supervisor decides");
        check(ApprovalPolicy.DecideProblem(Supervisor, Row(Operator, ApprovalPolicy.Approved), Now) == "state"
            && ApprovalPolicy.DecideProblem(Supervisor, Row(Operator, ApprovalPolicy.Cancelled), Now) == "state", "approval: only a pending row is decided");
        check(ApprovalPolicy.DecideProblem(Supervisor, Row(Operator), Now.AddDays(8)) == "expired", "approval: a stale row is not decided");

        /* ---- withdrawing ---- */
        check(ApprovalPolicy.CancelProblem(Operator, Row(Operator), Now) is null && ApprovalPolicy.CancelProblem(Supervisor, Row(Operator), Now) is null,
            "approval: the requester or an approver withdraws");
        check(ApprovalPolicy.CancelProblem(OtherOperator, Row(Operator), Now) == "who" && ApprovalPolicy.CancelProblem(Carrier, Row(Carrier), Now) == "who"
            && ApprovalPolicy.CancelProblem(null, Row(Operator), Now) == "who", "approval: nobody else withdraws");
        check(ApprovalPolicy.CancelProblem(Operator, Row(Operator, ApprovalPolicy.Approved), Now) == "state"
            && ApprovalPolicy.CancelProblem(Operator, Row(Operator), Now.AddDays(8)) == "expired", "approval: only a live pending row is withdrawn");

        /* ---- recording as applied ---- */
        var approved = Row(Operator, ApprovalPolicy.Approved);
        check(ApprovalPolicy.ApplyProblem(Supervisor, approved, approved.PayloadHash, Now) is null
            && ApprovalPolicy.ApplyProblem(Supervisor, approved, approved.PayloadHash.ToUpperInvariant(), Now) is null, "approval: an approver records against the fingerprint read");
        check(ApprovalPolicy.ApplyProblem(Supervisor, approved, ApprovalPolicy.Hash("other"), Now) == "hash"
            && ApprovalPolicy.ApplyProblem(Supervisor, approved, "", Now) == "hash", "approval: a fingerprint that is not the row's records nothing");
        check(ApprovalPolicy.ApplyProblem(Operator, approved, approved.PayloadHash, Now) == "approver", "approval: an operator records nothing");
        check(ApprovalPolicy.ApplyProblem(Supervisor, Row(Operator), Row(Operator).PayloadHash, Now) == "state", "approval: a pending row is not applied");
        check(ApprovalPolicy.ApplyProblem(Supervisor, approved, approved.PayloadHash, Now.AddDays(8)) == "expired", "approval: a stale approval is not applied");
        var legacyApproved = Row(Operator, ApprovalPolicy.Approved, legacy: true);
        check(ApprovalPolicy.ApplyProblem(Supervisor, legacyApproved, ApprovalPolicy.Hash(legacyApproved.Payload), Now) is null, "approval: a row from before 1E is applied against its payload's fingerprint");

        /* ---- what the screen offers ---- */
        check(ApprovalPolicy.ActionsFor(Operator, Row(Operator), Now).SequenceEqual(["cancel"]), "approval: a requester may only withdraw");
        check(ApprovalPolicy.ActionsFor(OtherSupervisor, Row(Operator), Now).SequenceEqual(["approve", "reject", "cancel"]), "approval: an approver may decide or withdraw");
        check(ApprovalPolicy.ActionsFor(Supervisor, Row(Supervisor), Now).SequenceEqual(["cancel"]), "approval: a supervisor's own proposal offers them only withdrawal");
        check(ApprovalPolicy.ActionsFor(Supervisor, approved, Now).SequenceEqual(["apply"]) && ApprovalPolicy.ActionsFor(Operator, approved, Now).Count == 0,
            "approval: an approved row offers only recording, to an approver");
        check(ApprovalPolicy.ActionsFor(Carrier, Row(Carrier), Now).Count == 0 && ApprovalPolicy.ActionsFor(Supervisor, Row(Operator), Now.AddDays(8)).Count == 0,
            "approval: a carrier and a stale row offer nothing");

        if (!sql) return;
        await RunSqlAsync(check);
    }

    private static async Task RunSqlAsync(Action<bool, string> check)
    {
        // The connection is compiled test-only LocalDB. Never read settings or accept a server argument.
        var database = "SCMOS_AI_APPROVAL_TEST_" + Guid.NewGuid().ToString("N");
        var connection = $"Server=(localdb)\\MSSQLLocalDB;Database={database};Integrated Security=true;TrustServerCertificate=true";
        var options = new DbContextOptionsBuilder<ScmosDbContext>().UseSqlServer(connection,
            s => { s.UseCompatibilityLevel(150); s.EnableRetryOnFailure(3); }).Options;
        await using var setup = new ScmosDbContext(options);
        await setup.Database.EnsureCreatedAsync();
        try
        {
            AiGateway Gateway(ScmosDbContext db) => new(db);
            var fields = new Dictionary<string, string> { ["jobKey"] = "J-1", ["status"] = "DISPATCHED" };
            async Task<ToolOutcome> Invoke(AppUser by, Dictionary<string, string>? payload = null, string summary = "ย้ายแผน")
            {
                await using var db = new ScmosDbContext(options);
                return await Gateway(db).InvokeAsync("update_shipment", summary, payload ?? fields, by, null, default, "corr-1");
            }
            async Task<IReadOnlyList<ApprovalView>> List(AppUser by, string? state = null)
            {
                await using var db = new ScmosDbContext(options);
                return await Gateway(db).ApprovalsAsync(by, state, default);
            }
            async Task<ToolOutcome> Decide(long id, AppUser by, bool approve = true)
            {
                await using var db = new ScmosDbContext(options);
                return await Gateway(db).DecideAsync(id, approve, "reviewed", by, default);
            }
            async Task<ToolOutcome> Apply(long id, AppUser by, string hash)
            {
                await using var db = new ScmosDbContext(options);
                return await Gateway(db).MarkAppliedAsync(id, "done by hand", hash, by, default);
            }
            async Task<ToolOutcome> Cancel(long id, AppUser by)
            {
                await using var db = new ScmosDbContext(options);
                return await Gateway(db).CancelAsync(id, "withdrawn", by, default);
            }
            async Task<Approval> Read(long id)
            {
                await using var db = new ScmosDbContext(options);
                return await db.Approvals.AsNoTracking().SingleAsync(a => a.Id == id);
            }

            check(!(await Invoke(Carrier)).Ok && !(await Invoke(Viewer)).Ok, "approval SQL: a carrier's and a view-only proposal are refused before any row");
            check(!(await Invoke(Operator, new Dictionary<string, string> { ["role"] = "Administrator" })).Ok, "approval SQL: a payload naming a role is refused");
            check(await setup.Approvals.CountAsync() == 0, "approval SQL: refusals leave no row");

            var proposed = await Invoke(Operator);
            var row = await Read(proposed.ApprovalId!.Value);
            check(proposed.Ok && proposed.Kind == "needs-approval" && row.RequesterId == Operator.UserId && row.PayloadHash == ApprovalPolicy.Hash(row.Payload)
                && row.ExpiresAt == row.RequestedAt.AddDays(7) && row.CorrelationId == "corr-1" && row.State == "pending",
                "approval SQL: a proposal is bound to its requester, fingerprinted, dated and correlated");

            check((await List(Operator)).Count == 1 && (await List(OtherOperator)).Count == 0 && (await List(Supervisor)).Count == 1
                && (await List(Carrier)).Count == 0, "approval SQL: the requester and approvers see it; another operator and a carrier do not");
            check((await List(Operator)).Single().Actions.SequenceEqual(["cancel"]) && (await List(Supervisor)).Single().Actions.SequenceEqual(["approve", "reject", "cancel"])
                && (await List(Operator)).Single().Mine && !(await List(Supervisor)).Single().Mine, "approval SQL: each reader is offered only what the policy allows");

            check(!(await Decide(row.Id, Operator)).Ok, "approval SQL: the requester cannot decide");
            var own = await Invoke(Supervisor);
            check(!(await Decide(own.ApprovalId!.Value, Supervisor)).Ok && (await Read(own.ApprovalId.Value)).State == "pending", "approval SQL: a supervisor cannot approve their own proposal");
            check((await Decide(own.ApprovalId.Value, OtherSupervisor)).Ok && (await Read(own.ApprovalId.Value)).State == "approved", "approval SQL: another supervisor can");

            check(!(await Apply(row.Id, Supervisor, row.PayloadHash)).Ok, "approval SQL: a pending row is not applied");
            var decided = await Decide(row.Id, Supervisor);
            var afterDecision = await Read(row.Id);
            check(decided.Ok && afterDecision.State == "approved" && afterDecision.DecidedBy == Supervisor.Signature
                && afterDecision.ExpiresAt == afterDecision.DecidedAt!.Value.AddDays(7), "approval SQL: a decision is recorded with a fresh expiry");
            check(!(await Decide(row.Id, OtherSupervisor)).Ok, "approval SQL: a second decision has no effect");
            check(!(await Apply(row.Id, Supervisor, ApprovalPolicy.Hash("something else"))).Ok && (await Read(row.Id)).State == "approved",
                "approval SQL: applying against another fingerprint records nothing");
            check(!(await Apply(row.Id, Operator, row.PayloadHash)).Ok, "approval SQL: an operator records nothing");
            var applied = await Apply(row.Id, Supervisor, row.PayloadHash);
            var afterApply = await Read(row.Id);
            check(applied.Ok && afterApply.State == "applied" && afterApply.AppliedBy == Supervisor.Signature && afterApply.AppliedAt is not null
                && afterApply.ExpiresAt is null && afterApply.Result == "done by hand", "approval SQL: recorded as applied by the approver, against the fingerprint");
            check(!(await Apply(row.Id, Supervisor, row.PayloadHash)).Ok, "approval SQL: recording twice has no second effect");

            var race = await Invoke(Operator);
            var concurrent = await Task.WhenAll(Decide(race.ApprovalId!.Value, Supervisor), Decide(race.ApprovalId.Value, OtherSupervisor, false));
            var raced = await Read(race.ApprovalId.Value);
            check(concurrent.Count(r => r.Ok) == 1 && raced.State is "approved" or "rejected"
                && raced.DecidedBy == (raced.State == "approved" ? Supervisor.Signature : OtherSupervisor.Signature), "approval SQL: two approvers at once have one effect");

            var stale = await Invoke(Operator);
            await setup.Approvals.Where(a => a.Id == stale.ApprovalId).ExecuteUpdateAsync(s => s.SetProperty(a => a.ExpiresAt, DateTimeOffset.UtcNow.AddMinutes(-1)));
            check(!(await Decide(stale.ApprovalId!.Value, Supervisor)).Ok, "approval SQL: a stale proposal cannot be decided");
            var listed = await List(Supervisor);
            check(listed.Single(a => a.Id == stale.ApprovalId).State == "expired" && (await Read(stale.ApprovalId.Value)).ExpiresAt is null,
                "approval SQL: listing marks the stale row expired");

            var withdrawn = await Invoke(Operator);
            check(!(await Cancel(withdrawn.ApprovalId!.Value, OtherOperator)).Ok, "approval SQL: another operator cannot withdraw it — nor learn it exists");
            check((await Cancel(withdrawn.ApprovalId.Value, Operator)).Ok && (await Read(withdrawn.ApprovalId.Value)).State == "cancelled"
                && !(await Decide(withdrawn.ApprovalId.Value, Supervisor)).Ok, "approval SQL: the requester withdraws, and nothing decides it after");

            await RunHttpAsync(check, options, row.Id, (await Invoke(Operator)).ApprovalId!.Value);
        }
        finally
        {
            // Delete only this run's explicitly named local scratch database, never application data.
            if (!database.StartsWith("SCMOS_AI_APPROVAL_TEST_", StringComparison.Ordinal)
                || setup.Database.GetDbConnection().DataSource != "(localdb)\\MSSQLLocalDB") throw new InvalidOperationException("Unsafe cleanup target");
            await setup.Database.EnsureDeletedAsync();
        }
    }

    private static async Task RunHttpAsync(Action<bool, string> check, DbContextOptions<ScmosDbContext> options, long appliedId, long pendingId)
    {
        var users = new TestUsers { User = Carrier };
        var builder = WebApplication.CreateBuilder();
        builder.Configuration.Sources.Clear();
        builder.Configuration.AddInMemoryCollection();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddScoped(_ => new ScmosDbContext(options));
        builder.Services.AddScoped<AiGateway>();
        builder.Services.AddScoped<AuditService>();
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddSingleton<IUserAccessor>(users);
        await using var app = builder.Build();
        app.MapAiApprovals();
        await app.StartAsync();
        try
        {
            using var http = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
            async Task<HttpResponseMessage> Post(string path, object body)
            {
                using var message = new HttpRequestMessage(HttpMethod.Post, "/api/ai" + path)
                { Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json") };
                return await http.SendAsync(message);
            }
            check((int)(await http.GetAsync("/api/ai/approvals")).StatusCode == 403, "approval HTTP: a carrier cannot read the queue");
            check((int)(await Post("/invoke", new { tool = "update_shipment", summary = "x" })).StatusCode == 403, "approval HTTP: a carrier cannot propose");
            users.User = OtherOperator;
            var foreign = await http.GetAsync("/api/ai/approvals");
            check(foreign.IsSuccessStatusCode && foreign.Headers.CacheControl?.NoStore == true
                && (await foreign.Content.ReadFromJsonAsync<JsonElement>()).GetArrayLength() == 0, "approval HTTP: another operator's listing is private and empty");
            check((int)(await Post($"/approvals/{pendingId}", new { approved = true, note = "" })).StatusCode == 403, "approval HTTP: an operator cannot decide by direct POST");
            check((int)(await Post($"/approvals/{pendingId}/cancel", new { note = "" })).StatusCode == 404, "approval HTTP: a guessed id that is not theirs is not disclosed");
            users.User = Supervisor;
            users.Refusal = "second factor required";
            check((int)(await Post($"/approvals/{pendingId}", new { approved = true, note = "" })).StatusCode == 403
                && (int)(await Post($"/approvals/{appliedId}/applied", new { result = "x", hash = "" })).StatusCode == 403, "approval HTTP: the second factor is asked for before a decision or a record");
            users.Refusal = null;
            check((int)(await Post($"/approvals/{appliedId}/applied", new { result = "x", hash = "" })).StatusCode == 409, "approval HTTP: recording without the fingerprint is a conflict, not a success");
            check((int)(await Post($"/approvals/{pendingId}", new { approved = true, note = "" })).StatusCode == 200
                && (int)(await Post($"/approvals/{pendingId}", new { approved = true, note = "" })).StatusCode == 409, "approval HTTP: a decision succeeds once and is a conflict after");
            await using var db = new ScmosDbContext(options);
            check(await db.AuditEvents.CountAsync(a => a.Entity == "approval" && a.EntityId == pendingId.ToString() && a.Action == AuditActions.Approve) == 1,
                "approval HTTP: the decision is one audit row");
        }
        finally { await app.StopAsync(); }
    }
}
