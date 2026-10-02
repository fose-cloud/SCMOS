using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Scmos.Api.Ai;
using Scmos.Api.Ai.Operations;
using Scmos.Api.Ai.Communication;
using Scmos.Api.Ai.Data;
using Scmos.Api.Ai.Documents;
using Scmos.Api.Ai.Engineering;
using Scmos.Api.Ai.Management;
using Scmos.Api.Ai.Sre;
using Scmos.Api.Auth;
using Scmos.Api.Data;
using Scmos.Api.Rules;
using Scmos.Api.Services;

static class PermissionEnforcementChecks
{
    public static string Json()
    {
        using var stream = typeof(AiPolicyCatalog).Assembly.GetManifestResourceStream("Scmos.Api.Ai.Policy.permission-matrix.json")!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
    public static JsonObject ReviewedFixture()
    {
        var json = JsonNode.Parse(Json())!.AsObject();
        json["approvalReference"] = "offline-test-only";
        foreach (var agent in json["agents"]!.AsArray())
        {
            agent!["humanOwner"] = "test-owner";
            agent["fallbackOwner"] = "test-fallback";
            agent["runtimeIsolationApproval"] = "offline-test-no-network";
            // Explicit test-only grants preserve old component tests; the Production candidate is unchanged.
            if (agent["agentId"]!.GetValue<string>() == AgentIds.Management)
            {
                var permissions = agent["permissions"]!.AsObject();
                permissions["BookingRead"] = "Read";
                permissions["DelayDetect"] = "Analyze";
                permissions["DocumentRead"] = "Read";
                permissions["CommunicationRead"] = "Read";
                foreach (var tool in new[] { "search_shipment", "query_delays", "query_documents", "query_messages" })
                    agent["allowedTools"]!.AsArray().Add(tool);
            }
            if (agent["agentId"]!.GetValue<string>() is AgentIds.Engineering or AgentIds.Sre)
                agent["networkAllowList"]!.AsArray().Add("api.github.com");
            agent["budget"] = JsonSerializer.SerializeToNode(new AgentBudgetPolicy(800, 8, 8, 0, 20, 4, 20, 1, 10, 0.01m),
                new JsonSerializerOptions(JsonSerializerDefaults.Web));
        }
        return json;
    }

    public static async Task Run(Action<bool, string> check)
    {
        var current = AiPolicyCatalog.Current;
        check(current.Valid && current.Manifests.Count == 14 && current.Manifests.ContainsKey(AgentIds.DocumentInvoice),
            "permission: fourteen manifests preserve existing immutable IDs");
        check(current.Manifests.Keys.All(id => current.Readiness(id) is not null), "permission: unapproved/missing operational configuration cannot start an agent");
        // An independent expectation table, not generated from the runtime grants.
        var expected = new Dictionary<string, Dictionary<AiAction, AiPermissionLevel>>
        {
            [AgentIds.Operations] = new() { [AiAction.BookingRead] = AiPermissionLevel.Read, [AiAction.DelayDetect] = AiPermissionLevel.Analyze },
            [AgentIds.Carrier] = new() { [AiAction.CarrierRecommend] = AiPermissionLevel.Draft },
            [AgentIds.Data] = new() { [AiAction.KpiGenerate] = AiPermissionLevel.Analyze },
            [AgentIds.DocumentInvoice] = new() { [AiAction.DocumentRead] = AiPermissionLevel.Read, [AiAction.DocumentExtract] = AiPermissionLevel.Analyze, [AiAction.BillingAnalyze] = AiPermissionLevel.Analyze },
            [AgentIds.Management] = new() { [AiAction.ManagementAnalyze] = AiPermissionLevel.Analyze },
            [AgentIds.Communication] = new() { [AiAction.CommunicationRead] = AiPermissionLevel.Read, [AiAction.CommunicationDraft] = AiPermissionLevel.Draft },
            [AgentIds.Engineering] = new() { [AiAction.CodeRead] = AiPermissionLevel.Read },
            [AgentIds.Otd] = new() { [AiAction.OtdCalculate] = AiPermissionLevel.Analyze },
            [AgentIds.Validation] = new() { [AiAction.ValidationAnalyze] = AiPermissionLevel.Analyze },
            [AgentIds.Booking] = new() { [AiAction.BookingCreateDraft] = AiPermissionLevel.Draft },
            [AgentIds.Sre] = new() { [AiAction.SystemHealthRead] = AiPermissionLevel.Read },
        };
        var rows = 0;
        foreach (var agent in new AgentRegistry().All)
        {
            foreach (var action in Enum.GetValues<AiAction>())
            {
                var want = expected.GetValueOrDefault(agent.Id)?.GetValueOrDefault(action, AiPermissionLevel.Forbidden) ?? AiPermissionLevel.Forbidden;
                if (current.Permission(agent.Id, action) != want) throw new InvalidOperationException($"Matrix mismatch: {agent.Id} x {action}");
                rows++;
            }
        }
        check(rows == 14 * Enum.GetValues<AiAction>().Length, $"permission: every candidate matrix row checked independently ({rows} rows)");
        check(AiPermissionPrecedence.Restrict(AiPermissionLevel.Read, AiPermissionLevel.Execute) == AiPermissionLevel.Read
            && AiPermissionPrecedence.Restrict(AiPermissionLevel.Execute, AiPermissionLevel.HumanApprovalRequired) == AiPermissionLevel.HumanApprovalRequired
            && AiPermissionPrecedence.Restrict(AiPermissionLevel.HumanApprovalRequired, AiPermissionLevel.Forbidden) == AiPermissionLevel.Forbidden
            && AiPermissionPrecedence.Restrict((AiPermissionLevel)999, AiPermissionLevel.Read) == AiPermissionLevel.Forbidden,
            "permission: restriction precedence cannot elevate a read or remove approval/forbidden");

        var fixture = ReviewedFixture();
        var catalog = AiPolicyCatalog.Parse(fixture.ToJsonString());
        check(catalog.Valid && catalog.Readiness(AgentIds.Operations) is null, "permission: reviewed offline fixture is ready without changing Production candidate");
        var user = new AppUser("test-admin", "", "Test", Roles.Admin, "OP-TEST", "test", true);
        var request = AiAuthorizationRequest.For(AgentIds.Operations, AiAction.BookingRead, "query_shipments", user, "test-permission");
        string Reason(AiAuthorizationRequest value, AiPolicyCatalog? policy = null) => AiGateway.Evaluate(value, policy ?? catalog).ReasonCode;
        check(Reason(request) == "allowed", "permission: explicit read + authenticated RBAC + scope passes pure grant checks");
        check(Reason(request with { AgentId = "unknown" }) == "unknown_agent", "permission: unknown agent denied");
        check(Reason(request with { Action = (AiAction)999 }) == "unknown_action", "permission: unknown action denied");
        check(Reason(request, AiPolicyCatalog.Parse("{}")) == "manifest_invalid", "permission: missing manifest denied");
        check(Reason(request with { ToolId = "unknown_tool" }) == "unknown_tool", "permission: unknown tool denied");
        check(Reason(request with { InputValid = false }) == "invalid_tool_input", "permission: invalid model arguments denied centrally");
        check(Reason(request with { ToolId = "search_shipment", AgentId = AgentIds.Booking, Action = AiAction.BookingCreateDraft }) == "tool_forbidden", "permission: wrong tool/action binding denied");
        check(Reason(request with { RequestedDataScope = null }) == "missing_data_scope", "permission: missing scope denied");
        check(Reason(request with { ApiScope = "admin.sql" }) == "api_scope_forbidden", "permission: ungranted API scope denied");
        check(Reason(request with { User = user with { Role = Roles.Subcontractor } }) == "user_forbidden", "permission: carrier cannot inherit internal scope");
        check(Reason(request with { User = user with { Recognised = false } }) == "user_forbidden", "permission: forged/unrecognised identity denied");
        check(Reason(request with { RiskLevel = (AiRisk)999 }) == "risk_forbidden", "permission: unknown risk denied");
        check(Reason(request with { AgentVersion = "new-unreviewed" }) == "agent_version_mismatch", "permission: version change grants no permission");
        check(Reason(request with { NetworkDestination = "attacker.invalid" }) == "network_forbidden", "permission: unexpected network denied");
        foreach (var approval in new[] { "expired", "wrong-approver", "wrong-resource", "reused" })
            check(Reason(request with { ApprovalId = approval }) == "approval_not_consumable", "permission: arbitrary " + approval + " approval is not execution authority");
        foreach (var action in new[] { AiAction.DirectProductionSql, AiAction.UserPermissionModify, AiAction.AiPolicyModify, AiAction.AuditModify, AiAction.AuditDelete })
            check(Reason(request with { Action = action }) == "absolute_forbidden", "permission: absolute prohibition " + action);
        check(Reason(request with { AgentId = AgentIds.Data, Action = AiAction.KpiGenerate, ToolId = "query_kpi", OriginAgentId = AgentIds.Operations,
            OriginalAction = AiAction.BookingRead, DelegationChain = [AgentIds.Operations, AgentIds.Data] }) == "privilege_chaining", "permission: specialist cannot launder origin permission");
        check(Reason(request with { DelegationChain = [AgentIds.Operations, AgentIds.Data, AgentIds.Operations] }) == "privilege_chaining", "permission: cyclic/unverifiable chain denied");
        var delegatedFixture = ReviewedFixture();
        delegatedFixture["agents"]!.AsArray().First(a => a!["agentId"]!.GetValue<string>() == AgentIds.Management)!["permissions"]!["BookingRead"] = "HumanApprovalRequired";
        var delegated = AiGateway.Evaluate(request with
        {
            ToolId = "search_shipment", OriginAgentId = AgentIds.Management, OriginalAction = AiAction.ManagementAnalyze,
            DelegationChain = [AgentIds.Management, AgentIds.Operations]
        }, AiPolicyCatalog.Parse(delegatedFixture.ToJsonString()));
        check(delegated.Decision == AiAuthorizationVerdict.HumanApprovalRequired && delegated.Permission == AiPermissionLevel.HumanApprovalRequired,
            "permission: delegated target cannot remove the initiating agent's human-approval restriction");
        check(Reason(request with { SystemPass = true }) == "invalid_identity", "permission: caller cannot mix human and system identities");
        check(Reason(request, current) == "policy_review_required", "permission: Production candidate remains fail-closed");

        foreach (var corrupt in new[] { "{bad", Json().Replace("\"failClosed\": true", "\"failClosed\": false"),
            Json().Replace("\"Low\"", "\"UnknownRisk\""), Json().Replace("\"Read\"", "999"),
            Json().Replace("\"policyVersion\":", "\"PolicyVersion\": \"override\", \"policyVersion\":"),
            Json().Replace("\"query_shipments\": \"BookingRead\"", "\"execute_sql\": \"BookingRead\""),
            Json().Replace("\"BookingRead\": \"Read\"", "\"DirectProductionSql\": \"Read\"") })
            check(!AiPolicyCatalog.Parse(corrupt).Valid, "permission: malformed, duplicate or forbidden policy rejected");
        check(!AiPolicyCatalog.Parse(Json().Replace("\"BookingRead\":", "\"0\":")).Valid,
            "permission: numeric enum dictionary aliases cannot define an action grant");
        check(!AiPolicyCatalog.ValidBudget(new(800, 8, 8, 0, 20, 4, 20, 1, 10, 0.0000001m)),
            "permission: cost reservation cannot round down to zero in SQL precision");

        using var db = new ScmosDbContext(new DbContextOptionsBuilder<ScmosDbContext>().Options);
        var audit = new PolicyFixtureAudit();
        var options = Options.Create(new AiOptions { Enabled = true, OperationsAgentEnabled = true });
        var gateway = new AiGateway(db, catalog, audit, new PolicyFixtureGovernance(), options);
        var allowed = await gateway.AuthorizeAsync(request, default);
        check(allowed.Allowed && allowed.AuditId.Length == 32 && audit.Entries.Count == 1, "permission: allow requires committed authorization evidence before dispatch");
        var denied = await gateway.AuthorizeAsync(request with { Action = AiAction.DirectProductionSql }, default);
        check(!denied.Allowed && audit.Entries[^1].SecurityEvent && audit.Entries.Count == 2, "permission: denied SQL attempt creates durable security evidence through audit service");
        audit.Fail = true;
        check((await gateway.AuthorizeAsync(request, default)).ReasonCode == "audit_unavailable", "permission: audit failure refuses otherwise allowed read");
        audit.Fail = false; audit.Refusal = "budget_exhausted";
        check((await gateway.AuthorizeAsync(request, default)).ReasonCode == "budget_exhausted", "permission: exhausted reservation refuses dispatch");
        audit.Refusal = null;
        options.Value.DisabledTools = ["query_shipments"];
        check((await gateway.AuthorizeAsync(request, default)).ReasonCode == "kill_switch", "permission: tool kill beats a reviewed grant");
        options.Value.DisabledTools = []; options.Value.Enabled = false;
        check(!(await gateway.AuthorizeAsync(request, default)).Allowed, "permission: global kill beats a reviewed grant");
        options.Value.Enabled = true; options.Value.DisabledAgentGroups = ["operations"];
        check((await gateway.AuthorizeAsync(request, default)).ReasonCode == "kill_switch", "permission: group kill beats a reviewed grant");
        options.Value.DisabledAgentGroups = [];
        check(!new AiOptions { DisabledTools = ["misspelled_tool"] }.Valid && !new AiOptions { DisabledAgentGroups = null! }.Valid,
            "permission: malformed revocation configuration fails closed");

        var source = new OperationsFixtureSource([]);
        var time = new OperationsClock(DateTimeOffset.UtcNow);
        var registry = new ToolRegistry(operations: new OperationsReadService(source, time), policyGateway: gateway);
        var unknown = await registry.AuthorizeCallAsync(AgentIds.Operations, new("call", "execute_sql", "{}"), user, "test-permission", default);
        check(unknown.ReasonCode == "unknown_tool" && audit.Entries[^1].SecurityEvent && source.Reads == 0,
            "permission: unknown model tool reaches centralized denial/security audit without a data read");
        var invalid = await registry.AuthorizeCallAsync(AgentIds.Operations, new("call", "query_shipments", "{}"), user, "test-permission", default);
        check(invalid.ReasonCode == "invalid_tool_input" && audit.Entries[^1].SecurityEvent && audit.Entries[^1].Request.UserId == user.UserId,
            "permission: malformed model call retains human attribution in denial/security audit");
        var beforeBatch = audit.Entries.Count;
        await registry.AuditRejectedCallsAsync(AgentIds.Operations,
            [new("a", "query_shipments", "{\"view\":\"today\",\"limit\":1}"), new("b", "unregistered", "{}")], user, "test-permission", default);
        check(audit.Entries.Count == beforeBatch + 2 && audit.Entries.Skip(beforeBatch).All(e => !e.Decision.Allowed && e.SecurityEvent),
            "permission: every call in a rejected model batch is denied and audited before execution");
        var secret = "sk-proj-offline-test-not-a-real-key";
        var safe = SqlAiExecutionAudit.AuthorizationRow(audit.Entries[^1] with
        {
            Request = request with { AgentId = secret, ToolId = secret, ResourceId = secret }
        });
        check(!safe.Metadata.Contains(secret) && safe.AgentId.StartsWith("hash:"),
            "permission: injected identifiers are hashed, not copied into security evidence");
        await DirectEntryChecks(check, user, time);
        check((await new AiGateway(db, catalog).AuthorizeAsync(request, default)).ReasonCode == "audit_unavailable", "permission: no nullable audit/gateway fail-open");
        var row = SqlAiExecutionAudit.AuthorizationRow(audit.Entries[1]);
        check(row.Metadata.Contains("DirectProductionSql") && row.PolicyVersion == catalog.Version && row.SecurityEvent
            && !row.Metadata.Contains("Test\"") && row.Fingerprint.Length == 64, "permission: audit preserves version/action/chain without prompt or human profile");
        var payload = new OperationsChangePayload(1, "shipment-1", "fingerprint", [], [], "test", user.UserId, user.OperatorId, DateTimeOffset.UtcNow.AddMinutes(30));
        check(!OperationsChangePolicy.IndependentApprover(user, payload)
            && !OperationsChangePolicy.IndependentApprover(user with { UserId = "alias" }, payload)
            && OperationsChangePolicy.IndependentApprover(user with { UserId = "another", OperatorId = "OP-OTHER" }, payload),
            "permission: Operations requires a different authorized human, not a second identity for the same operator");
        check(!OperationsChangePolicy.IndependentApprover(user with { Recognised = false, UserId = "another", OperatorId = "OP-OTHER" }, payload),
            "permission: unrecognised approver denied");
    }

    private static async Task DirectEntryChecks(Action<bool, string> check, AppUser user, TimeProvider time)
    {
        var source = new OperationsFixtureSource([]);
        // Connected application services with no gateway: even direct Agent.RunAsync must stop before the provider.
        var tools = new ToolRegistry(operations: new OperationsReadService(source, time),
            data: new DataReadService(new KpiFixture(), time), messages: new MessagesReadService(new CommunicationFixture([], [], []), time),
            documents: new DocumentsReadService(new DocumentFixture([], [], []), time),
            engineering: new EngineeringReadService(new EngineeringFixtureSource([]), time),
            platform: new PlatformReadService(new PlatformFixture(), new DeploymentFixture([]), time));
        var provider = new OperationsFixtureProvider();
        var audit = new OperationsTestAudit();
        var agents = new AgentRegistry();
        var ask = new AiChatRequest("test");
        var options = Options.Create(new AiOptions());
        var calls = new Dictionary<string, Func<Task<string>>>
        {
            [AgentIds.Operations] = async () => (await new OperationsAgent(tools, audit, provider, time).RunAsync("direct", ask, user, agents.Find(AgentIds.Operations)!, default)).Code,
            [AgentIds.Data] = async () => (await new DataAgent(tools, audit, provider, time).RunAsync("direct", ask, user, agents.Find(AgentIds.Data)!, default)).Code,
            [AgentIds.Communication] = async () => (await new CommunicationAgent(tools, audit, provider, time).RunAsync("direct", ask, user, agents.Find(AgentIds.Communication)!, default)).Code,
            [AgentIds.DocumentInvoice] = async () => (await new DocumentAgent(tools, audit, provider, time).RunAsync("direct", ask, user, agents.Find(AgentIds.DocumentInvoice)!, default)).Code,
            [AgentIds.Engineering] = async () => (await new EngineeringAgent(tools, audit, provider, time).RunAsync("direct", ask, user, agents.Find(AgentIds.Engineering)!, default)).Code,
            [AgentIds.Sre] = async () => (await new SreAgent(tools, audit, provider, time).RunAsync("direct", ask, user, agents.Find(AgentIds.Sre)!, default)).Code,
            [AgentIds.Management] = async () => (await new ManagementAgent(tools, agents, audit, provider, time, options).RunAsync("direct", ask, user, agents.Find(AgentIds.Management)!, default)).Code
        };
        foreach (var (id, run) in calls)
            check(await run() == "policy_gateway_unavailable" && provider.Calls == 0 && source.Reads == 0,
                "permission: direct " + id + " cannot bypass run authorization");
        using var input = JsonDocument.Parse("{\"view\":\"today\",\"limit\":1}");
        try
        {
            await tools.Find("query_shipments")!.Handler!.ReadAsync(input.RootElement,
                new("direct", user.UserId, AiPermissionPolicy.Scope(user)!, User: user), default);
            check(false, "permission: handler cannot bypass missing gateway");
        }
        catch (InvalidOperationException) { check(source.Reads == 0, "permission: direct handler refuses missing gateway before data access"); }
    }
}

sealed class PolicyFixtureGovernance : IAiGovernance
{
    public Task<GovernanceSnapshot> SnapshotAsync(CancellationToken token) => Task.FromResult(GovernanceSnapshot.Defaults(new AgentRegistry()));
}
sealed class PolicyFixtureAudit : IAiPolicyAudit
{
    public List<AiPolicyAuditEvent> Entries { get; } = [];
    public bool Fail { get; set; }
    public string? Refusal { get; set; }
    public Task<string?> RecordAuthorizationAsync(AiPolicyAuditEvent entry, AgentBudgetPolicy? budget, CancellationToken token)
    {
        if (Fail) throw new InvalidOperationException("offline audit failure");
        Entries.Add(entry);
        return Task.FromResult(Refusal);
    }
}

// For old component regression fixtures only: no real provider, SQL, Azure settings or network.
sealed class OfflineReviewedPolicyGateway : IAiPolicyGateway
{
    public static readonly OfflineReviewedPolicyGateway Instance = new();
    private readonly AiPolicyCatalog catalog = AiPolicyCatalog.Parse(PermissionEnforcementChecks.ReviewedFixture().ToJsonString());
    public Task<AiAuthorizationDecision> AuthorizeAsync(AiAuthorizationRequest request, CancellationToken token)
        => Task.FromResult(AiGateway.Evaluate(request, catalog));
}
