using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Scmos.Api.Ai;
using Scmos.Api.Ai.Communication;
using Scmos.Api.Ai.Data;
using Scmos.Api.Ai.Documents;
using Scmos.Api.Ai.Engineering;
using Scmos.Api.Ai.Management;
using Scmos.Api.Ai.Operations;
using Scmos.Api.Ai.Sre;
using Scmos.Api.Auth;
using Scmos.Api.Rules;

/// <summary>
/// Phase 9 — who may ask what, written out in full, and the switches that
/// stop everybody.
///
/// <para>
/// Every role SCMOS has, against every agent and every read it owns, with
/// every adapter connected and the audit ready — the friendliest world the
/// runtime can be in, so a verdict of "allowed" here is a permission that
/// really exists. The expected matrix is a literal table below: a
/// capability added to a role, a tool moved to another agent or a gate
/// removed shows up as a diff in that table rather than as a quiet yes.
/// That is the bug this codebase has actually shipped — an operator seeing
/// the whole team's history because a role name was tested instead of a
/// capability — and the table is what makes it loud.
/// </para>
///
/// <para>
/// Then the kill switches, which are the rollback: AI off, chat off, the
/// agent's own flag off, the Operations emergency stop. Each answers 503
/// with the code the Control Tower shows, before any provider call or read.
/// </para>
/// </summary>
static class AccessMatrixCheck
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-09-22T05:00:00Z");

    /// <summary>One signed-in person per role — recognised, with an owner id, which is the most a role can have.</summary>
    private static AppUser Person(string role) =>
        new($"u-{role}", $"{role}@test.invalid".ToLowerInvariant(), role, role, "OP-A1", "test", true);

    public static async Task RunAsync(Action<bool, string> check)
    {
        var clock = new OperationsClock(Now);
        // Every adapter connected: the matrix must show what permission says, not what is plugged in.
        var tools = new ToolRegistry(
            operations: new OperationsReadService(new OperationsFixtureSource([]), clock),
            data: new DataReadService(new KpiFixture(), clock),
            messages: new MessagesReadService(new CommunicationFixture([], [], []), clock),
            documents: new DocumentsReadService(new DocumentFixture([], [], []), clock),
            engineering: new EngineeringReadService(new EngineeringFixtureSource([]), clock),
            source: new SourceReadService(new SourceTreeFixture(new Dictionary<string, string>())),
            platform: new PlatformReadService(new PlatformFixture(), new DeploymentFixture([]), clock));
        var agents = new AgentRegistry();
        var guard = new QueryPolicyGuard(tools);
        check(tools.All.All(tool => tool.Handler is not null), "9: every reviewed read is connected for this matrix — a refusal below is a permission, never a missing adapter");

        var lines = new List<string>();
        foreach (var role in Roles.All)
        {
            var user = Person(role.Name);
            foreach (var agent in agents.All)
            {
                var use = AiPermissionPolicy.CanUse(user, agent);
                var reads = agent.AllowedTools.Where(name => tools.Find(name) is not null).ToList();
                var verdicts = reads.Count == 0
                    ? "—"
                    : string.Join(" ", reads.Select(name => $"{name}={AiPermissionPolicy.AuthorizeTool(user, agent, name, tools, durableAuditReady: true)}"));
                lines.Add($"{role.Name} · {agent.Id} · use={(use ? "yes" : "no")} · {verdicts}");
            }
        }

        // The table. Ordered by Roles.All then AgentRegistry.All, so a new role or agent
        // appears here as an added line rather than shifting every line after it.
        string[] expected =
        [
            "Administrator · operations-agent · use=yes · query_shipments=allowed search_shipment=allowed query_delays=allowed query_followup=allowed",
            "Administrator · vendor-agent · use=yes · —",
            "Administrator · rate-agent · use=yes · —",
            "Administrator · data-agent · use=yes · query_kpi=allowed",
            "Administrator · incident-agent · use=yes · —",
            "Administrator · document-agent · use=yes · query_documents=allowed",
            "Administrator · compliance-agent · use=yes · —",
            "Administrator · management-agent · use=yes · —",
            "Administrator · communication-agent · use=yes · query_messages=allowed",
            "Administrator · engineering-agent · use=yes · query_repository=allowed read_source=allowed",
            "Administrator · sre-agent · use=yes · query_platform=allowed",

            "Manager · operations-agent · use=yes · query_shipments=allowed search_shipment=allowed query_delays=allowed query_followup=allowed",
            "Manager · vendor-agent · use=yes · —",
            "Manager · rate-agent · use=yes · —",
            "Manager · data-agent · use=yes · query_kpi=allowed",
            "Manager · incident-agent · use=yes · —",
            "Manager · document-agent · use=yes · query_documents=allowed",
            "Manager · compliance-agent · use=yes · —",
            "Manager · management-agent · use=yes · —",
            "Manager · communication-agent · use=yes · query_messages=allowed",
            "Manager · engineering-agent · use=no · query_repository=forbidden read_source=forbidden",
            "Manager · sre-agent · use=no · query_platform=forbidden",

            "Assistant Manager · operations-agent · use=yes · query_shipments=allowed search_shipment=allowed query_delays=allowed query_followup=allowed",
            "Assistant Manager · vendor-agent · use=yes · —",
            "Assistant Manager · rate-agent · use=yes · —",
            "Assistant Manager · data-agent · use=yes · query_kpi=allowed",
            "Assistant Manager · incident-agent · use=yes · —",
            "Assistant Manager · document-agent · use=yes · query_documents=allowed",
            "Assistant Manager · compliance-agent · use=yes · —",
            "Assistant Manager · management-agent · use=yes · —",
            "Assistant Manager · communication-agent · use=yes · query_messages=allowed",
            "Assistant Manager · engineering-agent · use=no · query_repository=forbidden read_source=forbidden",
            "Assistant Manager · sre-agent · use=no · query_platform=forbidden",

            "Operation Supervisor · operations-agent · use=yes · query_shipments=allowed search_shipment=allowed query_delays=allowed query_followup=allowed",
            "Operation Supervisor · vendor-agent · use=yes · —",
            "Operation Supervisor · rate-agent · use=yes · —",
            "Operation Supervisor · data-agent · use=yes · query_kpi=allowed",
            "Operation Supervisor · incident-agent · use=yes · —",
            "Operation Supervisor · document-agent · use=yes · query_documents=allowed",
            "Operation Supervisor · compliance-agent · use=yes · —",
            "Operation Supervisor · management-agent · use=yes · —",
            "Operation Supervisor · communication-agent · use=yes · query_messages=allowed",
            "Operation Supervisor · engineering-agent · use=no · query_repository=forbidden read_source=forbidden",
            "Operation Supervisor · sre-agent · use=no · query_platform=forbidden",

            // A carrier is refused everywhere, by InternalUser, before any capability is read.
            "Subcontractor · operations-agent · use=no · query_shipments=forbidden search_shipment=forbidden query_delays=forbidden query_followup=forbidden",
            "Subcontractor · vendor-agent · use=no · —",
            "Subcontractor · rate-agent · use=no · —",
            "Subcontractor · data-agent · use=no · query_kpi=forbidden",
            "Subcontractor · incident-agent · use=no · —",
            "Subcontractor · document-agent · use=no · query_documents=forbidden",
            "Subcontractor · compliance-agent · use=no · —",
            "Subcontractor · management-agent · use=no · —",
            "Subcontractor · communication-agent · use=no · query_messages=forbidden",
            "Subcontractor · engineering-agent · use=no · query_repository=forbidden read_source=forbidden",
            "Subcontractor · sre-agent · use=no · query_platform=forbidden",

            "Operation User · operations-agent · use=yes · query_shipments=allowed search_shipment=allowed query_delays=allowed query_followup=allowed",
            "Operation User · vendor-agent · use=no · —",
            "Operation User · rate-agent · use=yes · —",
            "Operation User · data-agent · use=yes · query_kpi=allowed",
            "Operation User · incident-agent · use=yes · —",
            "Operation User · document-agent · use=yes · query_documents=allowed",
            "Operation User · compliance-agent · use=yes · —",
            "Operation User · management-agent · use=yes · —",
            "Operation User · communication-agent · use=yes · query_messages=allowed",
            "Operation User · engineering-agent · use=no · query_repository=forbidden read_source=forbidden",
            "Operation User · sre-agent · use=no · query_platform=forbidden",

            // Customer service upload paperwork; they do not read the mailbox or the rates.
            "CS · operations-agent · use=yes · query_shipments=allowed search_shipment=allowed query_delays=allowed query_followup=allowed",
            "CS · vendor-agent · use=no · —",
            "CS · rate-agent · use=no · —",
            "CS · data-agent · use=yes · query_kpi=allowed",
            "CS · incident-agent · use=yes · —",
            "CS · document-agent · use=yes · query_documents=allowed",
            "CS · compliance-agent · use=no · —",
            "CS · management-agent · use=yes · —",
            "CS · communication-agent · use=no · query_messages=forbidden",
            "CS · engineering-agent · use=no · query_repository=forbidden read_source=forbidden",
            "CS · sre-agent · use=no · query_platform=forbidden",

            // The dashboard roles read figures and nothing else.
            "Management · operations-agent · use=yes · query_shipments=allowed search_shipment=allowed query_delays=allowed query_followup=allowed",
            "Management · vendor-agent · use=no · —",
            "Management · rate-agent · use=no · —",
            "Management · data-agent · use=yes · query_kpi=allowed",
            "Management · incident-agent · use=yes · —",
            "Management · document-agent · use=no · query_documents=forbidden",
            "Management · compliance-agent · use=no · —",
            "Management · management-agent · use=yes · —",
            "Management · communication-agent · use=no · query_messages=forbidden",
            "Management · engineering-agent · use=no · query_repository=forbidden read_source=forbidden",
            "Management · sre-agent · use=no · query_platform=forbidden",

            "Viewer · operations-agent · use=yes · query_shipments=allowed search_shipment=allowed query_delays=allowed query_followup=allowed",
            "Viewer · vendor-agent · use=no · —",
            "Viewer · rate-agent · use=no · —",
            "Viewer · data-agent · use=yes · query_kpi=allowed",
            "Viewer · incident-agent · use=yes · —",
            "Viewer · document-agent · use=no · query_documents=forbidden",
            "Viewer · compliance-agent · use=no · —",
            "Viewer · management-agent · use=yes · —",
            "Viewer · communication-agent · use=no · query_messages=forbidden",
            "Viewer · engineering-agent · use=no · query_repository=forbidden read_source=forbidden",
            "Viewer · sre-agent · use=no · query_platform=forbidden",
        ];
        var difference = lines.Except(expected).Concat(expected.Except(lines)).ToList();
        check(difference.Count == 0 && lines.Count == expected.Length,
            "9: the whole access matrix is the written table — " + (difference.Count == 0 ? $"{lines.Count} lines" : string.Join(" | ", difference.Take(4))));

        /* ---- the invariants the table is there to protect ---- */
        var carrier = Person(Roles.Subcontractor);
        check(agents.All.All(agent => !AiPermissionPolicy.CanUse(carrier, agent))
            && tools.All.All(tool => AiPermissionPolicy.AuthorizeTool(carrier, agents.Find(tool.AgentId)!, tool.Name, tools, true) == "forbidden"),
            "9: a carrier reaches no agent and no read, whatever capability a role map ever grants them");
        check(Roles.All.Where(role => role.Name != Roles.Admin)
            .All(role => !AiPermissionPolicy.CanUse(Person(role.Name), agents.Find(EngineeringAgent.Id)!)
                && !AiPermissionPolicy.CanUse(Person(role.Name), agents.Find(SreAgent.Id)!)),
            "9: the repository and the platform are the Administrator's alone");
        check(tools.All.All(tool => AiPermissionPolicy.AuthorizeTool(Person(Roles.Admin), agents.Find(tool.AgentId)!, tool.Name, tools, durableAuditReady: false) == "audit_not_ready"),
            "9: with the durable audit unavailable no read is allowed to anybody, not even an Administrator");
        check(tools.All.All(tool => agents.All.Where(agent => agent.Id != tool.AgentId)
                .All(agent => AiPermissionPolicy.AuthorizeTool(Person(Roles.Admin), agent, tool.Name, tools, true) == "forbidden")),
            "9: a read belongs to its own agent — no agent may run another's, however the model asks");
        var unrecognised = Person(Roles.Admin) with { Recognised = false };
        var anonymous = Person(Roles.Admin) with { UserId = "" };
        check(agents.All.All(agent => !AiPermissionPolicy.CanUse(unrecognised, agent) && !AiPermissionPolicy.CanUse(anonymous, agent)),
            "9: an account nobody put in the directory, and one with no id at all, reach nothing");
        // A role that may not see the team reads its own work, so it needs an owner id to read at all;
        // one without it has no scope, and no agent will read for them. (A role holding ViewTeam reads the
        // team's work and needs none — the scope says which, and this is that difference, on purpose.)
        var noScope = new AppUser("u-no-scope", "x@test.invalid", "x", Roles.Management, "", "test", true);
        var withScope = noScope with { OperatorId = "MG-01" };
        check(!agents.All.Any(agent => AiPermissionPolicy.CanUse(noScope, agent)) && AiPermissionPolicy.Scope(noScope) is null
            && AiPermissionPolicy.Scope(withScope) is { Team: false, OperatorId: "MG-01" }
            && AiPermissionPolicy.Scope(Person(Roles.Operation) with { OperatorId = "" }) is { Team: true },
            "9: an account that may not see the team has no read scope without an owner id; one that may reads the team's work");
        check(guard.Allowed(Person(Roles.Admin), agents.Find(SreAgent.Id)!, PlatformReadService.Tool, true)
            && !guard.Allowed(Person(Roles.Supervisor), agents.Find(SreAgent.Id)!, PlatformReadService.Tool, true)
            && !guard.Allowed(Person(Roles.Admin), agents.Find(SreAgent.Id)!, PlatformReadService.Tool, auditReady: false),
            "9: the dispatch guard says the same as the table, including the audit gate");

        /* ---- the kill switches: the rollback, before any provider call ---- */
        using var limiter = new AiRunLimiter();
        var provider = new ManagementFixtureProvider();
        var environment = new TestEnvironment();
        AgentOrchestrator Orchestrator(AiOptions options) => new(Options.Create(options), environment, provider, agents, limiter,
            NullLogger<AgentOrchestrator>.Instance, data: new DataAgent(tools, new OperationsTestAudit(), provider, clock));
        var admin = Person(Roles.Admin);
        // AiOptions is a settings class, not a record: each switch is built from the same "everything on".
        static AiOptions On(Action<AiOptions>? change = null)
        {
            var options = new AiOptions { Enabled = true, ChatEnabled = true, DataAgentEnabled = true, OperationsAgentEnabled = true };
            change?.Invoke(options);
            return options;
        }
        var calls = provider.Calls;

        var off = await Orchestrator(On(o => o.Enabled = false)).RunAsync(new("x", DataAgent.Id), admin, default);
        var chatOff = await Orchestrator(On(o => o.ChatEnabled = false)).RunAsync(new("x", DataAgent.Id), admin, default);
        var agentOff = await Orchestrator(On(o => o.DataAgentEnabled = false)).RunAsync(new("x", DataAgent.Id), admin, default);
        check(off is { Status: 503, Response.Code: "disabled" } && chatOff is { Status: 503, Response.Code: "disabled" }
            && agentOff is { Status: 503, Response.Code: "agent_disabled" } && provider.Calls == calls,
            "9: AI off, chat off and an agent's own flag off each stop the run at 503 before the provider is called — the rollback is a setting, not a deploy");
        var emergency = await Orchestrator(On(o => o.OperationsEmergencyDisabled = true)).RunAsync(new("x", "operations-agent"), admin, default);
        check(emergency is { Status: 503, Response.Code: "disabled" } && provider.Calls == calls,
            "9: the Operations emergency stop refuses that agent outright");
        // Production, not the checks' Development stand-in: mock mode there is a refusal.
        var production = new TestEnvironment { EnvironmentName = "Production" };
        var mockInProduction = await new AgentOrchestrator(Options.Create(On(o => o.MockMode = true)), production, provider, agents, limiter,
            NullLogger<AgentOrchestrator>.Instance, data: new DataAgent(tools, new OperationsTestAudit(), provider, clock))
            .RunAsync(new("x", DataAgent.Id), admin, default);
        check(mockInProduction is { Status: 503, Response.Code: "configuration_invalid" } && provider.Calls == calls,
            "9: mock mode outside Development is a refusal, never a pretend answer on production data");
        check(Orchestrator(On()).Status(admin).Agents.Count(a => a.Enabled) == 2
            && Orchestrator(On(o => o.Enabled = false)).Status(admin).Agents.All(a => !a.Enabled),
            "9: the status the Control Tower draws says the same as the gate the run passes");
    }
}
