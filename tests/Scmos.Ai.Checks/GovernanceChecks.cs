using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Scmos.Api.Ai;
using Scmos.Api.Ai.Operations;
using Scmos.Api.Ai.Providers;
using Scmos.Api.Auth;
using Scmos.Api.Data;
using Scmos.Api.Rules;
using Scmos.Api.Services;

/// <summary>
/// The Agent Platform foundation (27 Sep 2026): autonomy, shadow mode, status,
/// the execution kill switch, the circuit breaker, prompt versions, cost and the
/// decision log. The first block is pure; the second runs the orchestrator with
/// fixtures to show a refusal happens before any provider call; the third, with
/// <c>--write-local-db</c>, uses a scratch LocalDB database of its own.
/// </summary>
static class GovernanceChecks
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-09-27T05:00:00Z");
    private static readonly AppUser Admin = new("gov-admin", "admin@test.invalid", "Admin", Roles.Admin, "AD-G1", "test", true);
    private static readonly AppUser Operator = new("gov-op", "op@test.invalid", "Operator", Roles.Operation, "OP-G1", "test", true);
    private static readonly AppUser Restricted = Operator with { UserId = "gov-mgmt", Role = Roles.Management, OperatorId = "OP-G2" };
    private static readonly AppUser Supervisor = Operator with { UserId = "gov-sv", Role = Roles.Supervisor, OperatorId = "SV-G1" };

    private static AgentResult Result(string entity = "J1", string status = AgentResultRules.Completed) => new(
        "operations-agent", "otd_risk", "job", entity, status, "Pick-up at risk of running late",
        Facts: [new("Truck left the depot at 09:23", "line_events:41")],
        RuleResults: [new("Planned pick-up is 10:00", "job:J1.planTime")],
        Observations: [],
        Inferences: [new("The truck may reach the pick-up after 10:00")],
        Recommendations: [new("Check the ETA with the carrier")],
        BlockingIssues: [],
        RuleReferences: ["MonitorRules.RiskToday"],
        EvidenceReferences: ["line_events:41", "job:J1"],
        RiskLevel: "HIGH", Confidence: 0.8m, RequiresApproval: false);

    public static async Task RunAsync(Action<bool, string> check, bool sql)
    {
        var agents = new AgentRegistry();
        var operations = agents.Find("operations-agent")!;
        var data = agents.Find("data-agent")!;
        var platform = AgentGovernance.PlatformDefault;
        var healthy = AgentHealth.Unknown;

        /* ---------------- defaults reproduce the platform as it was ---------------- */
        foreach (var agent in agents.All)
        {
            var setting = AgentGovernance.Default(agent);
            check(AgentGovernance.Evaluate(agent, true, setting, platform, healthy, AgentNeed.Run).Allowed
                && AgentGovernance.Evaluate(agent, true, setting, platform, healthy, AgentNeed.Recommend).Allowed
                && !setting.ShadowMode && setting.Status == AgentGovernance.Active,
                $"foundation: with no settings rows {agent.Id} runs and recommends exactly as before");
            check(!AgentGovernance.Evaluate(agent, true, setting, platform, healthy, AgentNeed.ExecuteAutonomously).Allowed,
                $"foundation: {agent.Id} never executes on its own by default");
        }
        check(AgentGovernance.Evaluate(operations, true, AgentGovernance.Default(operations), platform, healthy, AgentNeed.ExecuteWithApproval).Allowed
            && agents.All.Where(agent => agent.Id != operations.Id).All(agent =>
                AgentGovernance.Evaluate(agent, true, AgentGovernance.Default(agent), platform, healthy, AgentNeed.ExecuteWithApproval).Code == "autonomy_insufficient"),
            "foundation: only the Operations Agent executes with approval by default — its change pilot, as before");
        check(AgentGovernance.ExecutionEnabled(platform), "foundation: the platform's execution switch starts on, as the pilot's own switches decided");

        /* ---------------- autonomy, shadow, status, the switches ---------------- */
        var lifted = AgentGovernance.Default(data) with { Autonomy = AiAutonomy.RuleGovernedAutonomous };
        check(AgentGovernance.Effective(data, lifted, platform) == AiAutonomy.Recommend,
            "autonomy: a setting never lifts an agent above what its code was built for");
        check(AgentGovernance.SettingProblem(data, false, 4, AgentGovernance.Active, "why") == "autonomy_above_design"
            && AgentGovernance.SettingProblem(operations, false, 3, AgentGovernance.Active, "why") is null,
            "autonomy: an administrator cannot store more than the design allows");
        var off = AgentGovernance.Default(data) with { Autonomy = AiAutonomy.Disabled };
        check(AgentGovernance.Evaluate(data, true, off, platform, healthy, AgentNeed.Run).Code == "agent_disabled", "autonomy: L0 runs nothing");
        var observe = AgentGovernance.Default(data) with { Autonomy = AiAutonomy.Observe };
        check(AgentGovernance.Evaluate(data, true, observe, platform, healthy, AgentNeed.Run).Allowed
            && AgentGovernance.Evaluate(data, true, observe, platform, healthy, AgentNeed.Recommend).Code == "autonomy_insufficient",
            "autonomy: L1 reads but does not recommend");
        var shadow = AgentGovernance.Default(operations) with { ShadowMode = true };
        check(AgentGovernance.Evaluate(operations, true, shadow, platform, healthy, AgentNeed.Recommend).Allowed
            && AgentGovernance.Evaluate(operations, true, shadow, platform, healthy, AgentNeed.ExecuteWithApproval).Code == "shadow_mode",
            "shadow: the agent still decides and recommends; nothing it decides is executed");
        var noExecution = platform with { Autonomy = AiAutonomy.Recommend };
        check(!AgentGovernance.ExecutionEnabled(noExecution)
            && agents.All.All(agent => AgentGovernance.Evaluate(agent, true, AgentGovernance.Default(agent), noExecution, healthy, AgentNeed.Run).Allowed)
            && AgentGovernance.Evaluate(operations, true, AgentGovernance.Default(operations), noExecution, healthy, AgentNeed.ExecuteWithApproval).Code == "execution_disabled",
            "kill switch: execution off leaves reading and recommending on and stops every write");
        var stopped = platform with { Autonomy = AiAutonomy.Disabled };
        check(agents.All.All(agent => AgentGovernance.Evaluate(agent, true, AgentGovernance.Default(agent), stopped, healthy, AgentNeed.Run).Code == "ai_stopped"),
            "kill switch: the platform at L0 stops every agent without a restart");
        foreach (var (status, code) in new[] { (AgentGovernance.Paused, "agent_paused"), (AgentGovernance.Maintenance, "agent_maintenance"), (AgentGovernance.Disabled, "agent_disabled") })
            check(AgentGovernance.Evaluate(data, true, AgentGovernance.Default(data) with { Status = status, Reason = "drill" }, platform, healthy, AgentNeed.Run) is { Allowed: false } refused
                && refused.Code == code && refused.Reason.Contains("drill"), $"status: {status} refuses with {code} and the administrator's reason");
        check(AgentGovernance.Evaluate(data, false, AgentGovernance.Default(data), platform, healthy, AgentNeed.Run).Code == "agent_disabled",
            "flags: an agent whose flag is off stays off whatever its settings say");
        check(AgentGovernance.SettingProblem(data, false, 2, AgentGovernance.Degraded, "why") == "invalid_status"
            && AgentGovernance.SettingProblem(data, false, 2, AgentGovernance.Paused, " ") == "reason_required"
            && AgentGovernance.SettingProblem(null, false, 2, AgentGovernance.Active, "why") == "unknown_agent",
            "settings: DEGRADED is the breaker's word, a change needs a reason, an agent must exist");

        /* ---------------- the circuit breaker ---------------- */
        (string, DateTimeOffset) Failed(int minutesAgo) => ("provider_unavailable", Now.AddMinutes(-minutesAgo));
        var coolDown = TimeSpan.FromMinutes(10);
        check(AgentGovernance.Breaker([Failed(1), Failed(2), Failed(3)], Now, 3, 5, coolDown) == (3, BreakerState.Degraded),
            "breaker: three platform failures in a row show DEGRADED");
        check(AgentGovernance.Breaker([Failed(1), Failed(2), Failed(3), Failed(4), Failed(5)], Now, 3, 5, coolDown) == (5, BreakerState.Open),
            "breaker: five in a row, recently, rest the agent");
        check(AgentGovernance.Breaker([Failed(20), Failed(21), Failed(22), Failed(23), Failed(24)], Now, 3, 5, coolDown) == (5, BreakerState.HalfOpen),
            "breaker: after the cool-down the next run is allowed to decide");
        check(AgentGovernance.Breaker([("succeeded", Now), Failed(1), Failed(2), Failed(3), Failed(4), Failed(5)], Now, 3, 5, coolDown) == (0, BreakerState.Closed)
            && AgentGovernance.Breaker([("clarification_required", Now), Failed(1), Failed(2), Failed(3)], Now, 3, 5, coolDown) == (0, BreakerState.Closed),
            "breaker: one answer — even a request to be clearer — closes it");
        check(AgentGovernance.Breaker([Failed(1), ("provider_busy", Now.AddMinutes(-2)), ("cancelled", Now.AddMinutes(-3)), ("not_connected", Now.AddMinutes(-4)), Failed(5), Failed(6)], Now, 3, 5, coolDown) == (3, BreakerState.Degraded),
            "breaker: busy, cancelled and not-connected runs neither count nor reset");
        var open = new AgentHealth(10, 5, 5, null, Now, null, BreakerState.Open);
        check(AgentGovernance.Evaluate(data, true, AgentGovernance.Default(data), platform, open, AgentNeed.Run).Code == "agent_circuit_open"
            && AgentGovernance.EffectiveStatus(AgentGovernance.Default(data), open) == AgentGovernance.Paused
            && AgentGovernance.EffectiveStatus(AgentGovernance.Default(data), open with { Breaker = BreakerState.Degraded }) == AgentGovernance.Degraded
            && AgentGovernance.Evaluate(data, true, AgentGovernance.Default(data), platform, open with { Breaker = BreakerState.Degraded }, AgentNeed.Run).Allowed,
            "breaker: open refuses and reads PAUSED; degraded still answers and reads DEGRADED");

        /* ---------------- cost, prompt version ---------------- */
        var prices = AgentGovernance.ParsePrices("gpt-4.1 = 2.00/8.00; broken; gpt-x=1/-1; mini=0.40/1.60");
        check(prices.Count == 2 && AgentGovernance.Cost("gpt-4.1", 1_000_000, 500_000, prices) == 6.0000m
            && AgentGovernance.Cost("GPT-4.1", 250_000, 0, prices) == 0.5000m && AgentGovernance.Cost("unknown", 1, 1, prices) is null,
            "cost: priced per million from configuration; a model without a price is unknown, never zero");
        check(AiAuditRules.IsPromptVersion(AiBuild.PromptVersion) && AiBuild.PromptVersion.StartsWith("build:", StringComparison.Ordinal),
            "prompt version: the build's commit, in the audit's shape");
        var start = new AiExecutionEvent(Guid.NewGuid().ToString("N"), "gov-op", Roles.Operation, "data-agent", "run_started", null,
            "running", Now, Scope: new(true, null), Model: "gpt-4.1", PromptVersion: "build:65fd552a1b2c");
        check(AiAuditRules.From(start).PromptVersion == "build:65fd552a1b2c", "prompt version: recorded on the run's start");
        check(Throws(() => AiAuditRules.From(start with { PromptVersion = "You are an SCMOS assistant" }))
            && Throws(() => AiAuditRules.From(start with { Event = "run_completed", Status = "failed", PromptVersion = "build:x" })),
            "prompt version: an identifier on the start only — never prompt text, never on another event");
        check(AiAuditRules.From(start with { PromptVersion = null }).PromptVersion == "", "prompt version: rows written without one stay valid");

        /* ---------------- the decision contract ---------------- */
        var known = agents.All.Select(agent => agent.Id).ToList();
        check(AgentResultRules.Problems(Result(), known).Count == 0, "decisions: a grounded result with facts, rule results, inference and recommendation passes");
        check(AgentResultRules.Problems(Result() with { Facts = [new("Truck left at 09:23")] }, known).Count > 0,
            "decisions: a fact without its source is refused — evidence before inference");
        check(AgentResultRules.Problems(Result() with { Facts = [], RuleResults = [] }, known).Count > 0,
            "decisions: a recommendation from nothing is refused");
        check(AgentResultRules.Problems(Result() with { Status = AgentResultRules.InsufficientInformation, Facts = [], RuleResults = [],
                Inferences = [], Recommendations = [], EvidenceReferences = [] }, known).Count == 0,
            "decisions: an agent may say it does not have enough to go on");
        check(AgentResultRules.Problems(Result() with { Status = AgentResultRules.Blocked }, known).Count > 0
            && AgentResultRules.Problems(Result() with { BlockingIssues = [new("Weight unknown")] }, known).Count > 0,
            "decisions: BLOCKED and its blocking issues come together or not at all");
        check(AgentResultRules.Problems(Result() with { Status = "DONE" }, known).Count > 0
            && AgentResultRules.Problems(Result() with { Confidence = 1.5m }, known).Count > 0
            && AgentResultRules.Problems(Result() with { AgentId = "booking-agent" }, known).Count > 0
            && AgentResultRules.Problems(Result() with { Summary = "bad\u0007bell" }, known).Count > 0
            && AgentResultRules.Problems(Result() with { Inferences = Enumerable.Range(0, 21).Select(i => new AgentFinding($"i{i}")).ToList() }, known).Count > 0
            && AgentResultRules.Problems(Result() with { RequiresApproval = true, Recommendations = [] }, known).Count > 0,
            "decisions: status, confidence, agent, text and size are checked; approval needs something to approve");

        /* ---------------- the orchestrator refuses before any provider call ---------------- */
        var development = new TestEnvironment();
        var live = new AiOptions { Enabled = true, ChatEnabled = true, DataAgentEnabled = true, OperationsAgentEnabled = true };
        async Task<(AiChatOutcome Outcome, int Calls, int Reads)> Run(GovernanceSnapshot snapshot, AiOptions? options = null)
        {
            var provider = new StubProvider((_, _) => throw new Exception("must never run"));
            var governance = new DefaultGovernance(snapshot);
            using var limiter = new AiRunLimiter();
            var runtime = new AgentOrchestrator(Options.Create(options ?? live), development, provider, agents, limiter,
                NullLogger<AgentOrchestrator>.Instance, governance: governance);
            var outcome = await runtime.RunAsync(new AiChatRequest("KPI this month", "data-agent"), Admin, default);
            return (outcome, provider.Calls, governance.Reads);
        }
        GovernanceSnapshot With(AgentSetting? platformSetting = null, AgentSetting? dataSetting = null, AgentHealth? dataHealth = null) =>
            new(agents, platformSetting ?? platform,
                dataSetting is null ? new Dictionary<string, AgentSetting>() : new() { [data.Id] = dataSetting },
                dataHealth is null ? new Dictionary<string, AgentHealth>() : new() { [data.Id] = dataHealth }, true);
        var baseline = await Run(With());
        check(baseline.Outcome.Response.Code == "not_connected" && baseline.Calls == 0 && baseline.Reads == 1,
            "orchestrator: with default governance a run passes the gate (and stops only at the missing adapter)");
        var paused = await Run(With(dataSetting: AgentGovernance.Default(data) with { Status = AgentGovernance.Paused, Reason = "drill" }));
        check(paused.Outcome.Status == 503 && paused.Outcome.Response.Code == "agent_paused" && paused.Calls == 0,
            "orchestrator: a paused agent is refused before the provider or a read");
        check((await Run(With(platformSetting: stopped))).Outcome.Response.Code == "ai_stopped", "orchestrator: the platform at L0 stops a run");
        check((await Run(With(dataHealth: open))).Outcome.Response.Code == "agent_circuit_open", "orchestrator: an open breaker refuses a run");
        check((await Run(GovernanceSnapshot.Unavailable(agents))).Outcome.Response.Code == "governance_unavailable",
            "orchestrator: governance that cannot be read refuses — fail closed");
        var mock = await Run(With(platformSetting: stopped), new AiOptions { Enabled = true, ChatEnabled = true, MockMode = true, DataAgentEnabled = true });
        check(mock.Reads == 0, "orchestrator: the development mock never reads governance, as it never reads SQL");
        var disabled = await Run(With(), new AiOptions());
        check(disabled.Outcome.Response.Code == "disabled" && disabled.Reads == 0,
            "orchestrator: with AI off in configuration nothing is read at all — SCMOS without AI is unchanged");
        using (var limiter = new AiRunLimiter())
        {
            var runtime = new AgentOrchestrator(Options.Create(live), development, new MockAiProvider(development), agents, limiter,
                NullLogger<AgentOrchestrator>.Instance, governance: new DefaultGovernance(With(noExecution,
                    AgentGovernance.Default(data) with { Status = AgentGovernance.Maintenance, Reason = "upgrade" })));
            var status = await runtime.StatusAsync(Admin, default);
            var dataView = status.Agents.Single(view => view.Id == data.Id);
            check(!status.ExecutionEnabled && status.GovernanceAvailable && !dataView.Enabled && dataView.Status == AgentGovernance.Maintenance
                && dataView.Code == "agent_maintenance" && dataView.Autonomy == 2,
                "status: the Control Tower is told the execution switch and each agent's governed state");
        }

        /* ---------------- who may govern ---------------- */
        check(AiGovernanceService.CanManage(Admin) && !AiGovernanceService.CanManage(Supervisor) && !AiGovernanceService.CanManage(Operator),
            "access: only an Administrator changes governance");
        check(AiGovernanceService.CanView(Admin) && !AiGovernanceService.CanView(Operator with { Role = Roles.Viewer })
            && !AiGovernanceService.CanView(Operator with { Role = Roles.Subcontractor }),
            "access: a viewer and a carrier never see governance");

        if (!sql) return;
        await SqlAsync(check, agents);
    }

    private static bool Throws(Action action)
    {
        try { action(); return false; }
        catch (ArgumentException) { return true; }
    }

    private static async Task SqlAsync(Action<bool, string> check, AgentRegistry agents)
    {
        // Compiled test-only LocalDB; its own scratch database, deleted at the end.
        var database = "SCMOS_AI_GOVERNANCE_TEST_" + Guid.NewGuid().ToString("N");
        var connection = $"Server=(localdb)\\MSSQLLocalDB;Database={database};Integrated Security=true;TrustServerCertificate=true";
        var options = new DbContextOptionsBuilder<ScmosDbContext>().UseSqlServer(connection,
            s => { s.UseCompatibilityLevel(150); s.EnableRetryOnFailure(3); }).Options;
        await using var setup = new ScmosDbContext(options);
        await setup.Database.EnsureCreatedAsync();
        try
        {
            var clock = new OperationsClock(Now);
            var ai = new AiOptions { PriceList = "gpt-4.1=2/8", OperationsWritesEnabled = true };
            AiGovernanceService Service(ScmosDbContext db) => new(db, agents,
                new AuditService(db, new HttpContextAccessor(), NullLogger<AuditService>.Instance), Options.Create(ai), clock,
                NullLogger<AiGovernanceService>.Instance);

            await using (var db = new ScmosDbContext(options))
            {
                var service = Service(db);
                check(await service.SetAsync(Operator, "data-agent", 1, false, AgentGovernance.Active, "why", 0, default) == "forbidden",
                    "SQL: only an Administrator saves a setting");
                check(await service.SetAsync(Admin, "data-agent", 1, false, AgentGovernance.Active, "", 0, default) == "reason_required"
                    && await service.SetAsync(Admin, "data-agent", 3, false, AgentGovernance.Active, "why", 0, default) == "autonomy_above_design"
                    && await service.SetAsync(Admin, "booking-agent", 1, false, AgentGovernance.Active, "why", 0, default) == "unknown_agent",
                    "SQL: a setting without a reason, above design, or for no agent is refused");
                check(await service.SetAsync(Admin, "data-agent", 1, true, AgentGovernance.Paused, "Drill: stop the Data Agent", 0, default) == "ok",
                    "SQL: an Administrator pauses an agent, lowers it to L1 and puts it in shadow");
            }
            await using (var db = new ScmosDbContext(options))
            {
                var row = await db.AiAgentConfigs.SingleAsync(one => one.AgentId == "data-agent");
                var audit = await db.AuditEvents.Where(one => one.Entity == "ai-agent-settings" && one.EntityId == "data-agent").ToListAsync();
                check(row.Revision == 1 && row.Autonomy == 1 && row.ShadowMode && row.Status == AgentGovernance.Paused && row.UpdatedBy.Length > 0
                    && audit.Count == 3 && audit.All(one => one.Reason == "Drill: stop the Data Agent"),
                    "SQL: the row is written with revision 1, and one audit row per field that changed, each with the reason");
                var service = Service(db);
                check(await service.SetAsync(Admin, "data-agent", 2, false, AgentGovernance.Active, "stale", 0, default) == "conflict",
                    "SQL: a change made against a stale read is a conflict, never an overwrite");
                check(await service.SetAsync(Admin, "data-agent", 1, true, AgentGovernance.Paused, "same", 1, default) == "ok"
                    && await db.AuditEvents.CountAsync(one => one.Entity == "ai-agent-settings") == 3,
                    "SQL: saving what is already there writes nothing and audits nothing");
                var snapshot = await service.SnapshotAsync(default);
                check(snapshot.Available && snapshot.Gate("data-agent", true, AgentNeed.Run).Code == "agent_paused"
                    && snapshot.Gate("sre-agent", true, AgentNeed.Run).Allowed,
                    "SQL: the snapshot carries one agent's pause without touching the others");
                check(await service.SetAsync(Admin, AgentGovernance.PlatformId, 2, false, null, "Execution off for the drill", 0, default) == "ok"
                    && !AgentGovernance.ExecutionEnabled((await service.SnapshotAsync(default)).Platform),
                    "SQL: the platform row is the execution switch");
            }

            // The Operations change pilot is gated by the same switch: execution off, nothing may be proposed or confirmed.
            await using (var db = new ScmosDbContext(options))
            {
                (await db.AiOperationsControls.SingleAsync(one => one.Id == 1)).Enabled = true;
                db.OperationJobs.Add(new OperationJob { Key = "GOV-1", Cat = "IMPORT", OwnerId = Operator.OperatorId, Owner = "Operator",
                    WorkDate = "27/09/2026", Status = "RECEIVED", UpdatedAt = Now, Data = "{}" });
                await db.SaveChangesAsync();
                var memory = new MemoryCache(new MemoryCacheOptions());
                OperationsChangeService Pilot(IAiGovernance? governance) => new(db,
                    new AuditService(db, new HttpContextAccessor(), NullLogger<AuditService>.Instance),
                    new JobRegisterCache(db, memory, NullLogger<JobRegisterCache>.Instance), clock, Options.Create(ai), governance);
                check((await Pilot(Service(db)).PreviewAsync("GOV-1", Operator, default))?.Enabled == false,
                    "SQL: with execution off the change pilot is not enabled");
                check(await Service(db).SetAsync(Admin, AgentGovernance.PlatformId, 4, false, null, "Execution back on", 1, default) == "ok"
                    && (await Pilot(Service(db)).PreviewAsync("GOV-1", Operator, default))?.Enabled == true,
                    "SQL: with execution back on the pilot is enabled again, as its own switches say");
                check(await Service(db).SetAsync(Admin, "operations-agent", 3, true, AgentGovernance.Active, "Shadow the pilot", 0, default) == "ok"
                    && (await Pilot(Service(db)).PreviewAsync("GOV-1", Operator, default))?.Enabled == false,
                    "SQL: the Operations Agent in shadow mode proposes and confirms nothing");
            }

            // The breaker and the usage come from the audit's own rows.
            await using (var db = new ScmosDbContext(options))
            {
                for (var i = 0; i < 5; i++)
                {
                    var run = Guid.NewGuid().ToString("N");
                    db.AiAuditLogs.Add(new AiAuditLog { RunId = run, Sequence = 1, UserId = "u", Role = Roles.Operation, AgentId = "sre-agent",
                        Event = "run_started", Status = "running", At = Now.AddMinutes(-3 - i).AddSeconds(-2), Model = "gpt-4.1", Fingerprint = "f" });
                    db.AiAuditLogs.Add(new AiAuditLog { RunId = run, Sequence = 4, UserId = "u", Role = Roles.Operation, AgentId = "sre-agent",
                        Event = "run_completed", Status = "provider_unavailable", At = Now.AddMinutes(-3 - i), Model = "gpt-4.1", Fingerprint = "f",
                        InputTokens = 1_000_000, OutputTokens = 0 });
                }
                await db.SaveChangesAsync();
                var service = Service(db);
                check((await service.SnapshotAsync(default)).Gate("sre-agent", true, AgentNeed.Run).Code == "agent_circuit_open",
                    "SQL: five failed runs in a row open the breaker, read from the audit");
                var report = await service.ReportAsync(Admin, default);
                var sre = report.Agents.Single(view => view.Id == "sre-agent");
                check(report.Available && sre.EffectiveStatus == AgentGovernance.Paused && sre.Runs24h == 5 && sre.Failures24h == 5
                    && sre.AverageMs == 2000 && sre.Usage.InputTokens24h == 5_000_000 && sre.Usage.Cost24h == 10m && report.PricesConfigured
                    && report.Agents.Single(view => view.Id == "data-agent").Status == AgentGovernance.Paused,
                    "SQL: the report shows health, latency, tokens and cost per agent");
            }

            // The decision log.
            await using (var db = new ScmosDbContext(options))
            {
                var log = new AiDecisionLog(db, agents, new AuditService(db, new HttpContextAccessor(), NullLogger<AuditService>.Instance), new OperationsClock(Now));
                var (refused, problems) = await log.RecordAsync(Result() with { Facts = [new("no source")] }, "", Operator.OperatorId, true, AiAutonomy.Recommend, default);
                check(refused is null && problems.Count > 0 && await db.AiDecisions.CountAsync() == 0, "SQL: an invalid result is refused whole; nothing is stored");
                var (mine, _) = await log.RecordAsync(Result("J1"), Guid.NewGuid().ToString("N"), Operator.OperatorId, true, AiAutonomy.Recommend, default);
                var (theirs, _) = await log.RecordAsync(Result("J2"), "", "OP-OTHER", true, AiAutonomy.Recommend, default);
                check(mine is > 0 && theirs is > 0, "SQL: a valid shadow decision is stored");
                var (teamItems, teamTotal) = await log.ListAsync(Operator, null, null, null, null, 1, 50, default);
                var (ownItems, ownTotal) = await log.ListAsync(Restricted with { OperatorId = Operator.OperatorId }, null, null, null, null, 1, 50, default);
                check(teamTotal == 2 && ownTotal == 1 && ownItems.Single().EntityId == "J1"
                    && teamItems.First().Findings.Facts.Single().Source == "line_events:41" && teamItems.First().PromptVersion.StartsWith("build:"),
                    "SQL: the list follows the AI's own scope, and the findings come back apart, with their sources");
                check(await log.AnswerAsync(mine!.Value, Operator, "OVERRIDDEN", "", "", default) == "override_needs_choice_and_reason",
                    "SQL: an override says what was chosen instead, and why");
                check(await log.AnswerAsync(theirs!.Value, Restricted with { OperatorId = Operator.OperatorId }, "ACCEPTED", "", "", default) == "not_found",
                    "SQL: a person who cannot see a decision cannot learn it exists");
                check(await log.AnswerAsync(mine.Value, Operator, "OVERRIDDEN", "Carrier B", "Carrier A has no DG driver today", default) == "ok"
                    && await log.AnswerAsync(mine.Value, Supervisor, "ACCEPTED", "", "", default) == "already_answered",
                    "SQL: the owner answers once; a second answer is refused");
                var answered = await db.AiDecisions.AsNoTracking().SingleAsync(one => one.Id == mine.Value);
                check(answered.Status == "OVERRIDDEN" && answered.HumanMatches == false && answered.HumanChoice == "Carrier B"
                    && await db.AuditEvents.AnyAsync(one => one.Entity == "ai-decision" && one.EntityId == mine.Value.ToString()),
                    "SQL: the shadow comparison is kept — the human's choice, the mismatch, the reason — and audited");
                check(await log.AnswerAsync(theirs.Value, Supervisor, "ACCEPTED", "", "", default) == "ok"
                    && (await db.AiDecisions.AsNoTracking().SingleAsync(one => one.Id == theirs.Value)).HumanMatches == true,
                    "SQL: a supervisor may answer any decision; agreement is recorded as a match");
            }
        }
        finally
        {
            // Delete only this run's explicitly named local scratch database, never application data.
            if (!database.StartsWith("SCMOS_AI_GOVERNANCE_TEST_", StringComparison.Ordinal)
                || setup.Database.GetDbConnection().DataSource != "(localdb)\\MSSQLLocalDB") throw new InvalidOperationException("Unsafe cleanup target");
            await setup.Database.EnsureDeletedAsync();
        }
    }
}
