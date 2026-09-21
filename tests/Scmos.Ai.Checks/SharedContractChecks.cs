using System.Text.Json;
using Scmos.Api.Ai;
using Scmos.Api.Ai.Operations;

static class SharedContractChecks
{
    public static void Run(Action<bool, string> check)
    {
        SemanticBoundaryChecks.Run(check);
        var registry = new ToolRegistry();
        var rules = Scmos.Api.Ai.Semantic.BusinessRuleRegistry.All;
        check(rules.Count == 4 && rules.Select(r => r.Id).Distinct().Count() == rules.Count
            && rules.All(r => r.Version == (r.Id == "arrival.on_time" ? "2" : "1")
                && r.SourceMember.StartsWith("Rules/") && r.MissingData.Length > 0),
            "1C: versioned rule descriptors retain code provenance and missing-data meaning");
        check(Scmos.Api.Ai.Semantic.BusinessRuleRegistry.Resolve("arrival.on_time")?.ThresholdMinutes == 0
            && Scmos.Api.Ai.Semantic.BusinessRuleRegistry.Resolve("arrival.late_beyond")?.ThresholdMinutes == Scmos.Api.Rules.JobRules.LateMinutes,
            "1C: zero-grace KPI is distinct from default lateness tolerance");
        check(Scmos.Api.Ai.Semantic.BusinessRuleRegistry.Resolve("unknown") is null
            && Scmos.Api.Ai.Semantic.BusinessRuleRegistry.Resolve("arrival.on_time", "CUSTOMER-SLA") is null,
            "1C: unknown rules and unverified customer contracts have no fallback");
        var budget = new AiDispatchBudget();
        check(budget.TryConsume() && !budget.TryConsume() && !budget.TryConsume(),
            "1B: one call per request including failed attempts");
        var concurrentBudget = new AiDispatchBudget();
        var winners = 0;
        Parallel.For(0, 20, _ => { if (concurrentBudget.TryConsume()) Interlocked.Increment(ref winners); });
        check(winners == 1, "1B: concurrent dispatch cannot exceed budget");
        check(default(AiActionLevel) == AiActionLevel.Unspecified,
            "1A: unspecified action metadata is not a read permission");
        check(registry.All.Select(t => t.Name).SequenceEqual(
            ["query_shipments", "search_shipment", "query_delays", "query_kpi"]), "1A/2: exactly the existing three Operations tools, and the Data Agent's one (Phase 2)");
        foreach (var tool in registry.All)
        {
            var data = tool.Name == "query_kpi";
            check(tool.Policy is { ActionLevel: AiActionLevel.Read, Version: "1", Source: "operation_jobs",
                    MaxEvidenceRows: 50, ScopePolicy: "server-resolved-team-or-operator", DeadlineSetting: "AI:TimeoutSeconds" }
                && tool.Policy.OutputType == (data ? typeof(Scmos.Api.Ai.Data.DataAnswer) : typeof(OperationsAnswer)), "1A: reviewed policy for " + tool.Name);
            check(tool.Risk == AiRisk.Low && tool.AuditPolicy == "required-before-and-after"
                && tool.AgentId == (data ? "data-agent" : "operations-agent") && tool.Handler is null,
                "1A: metadata changes neither legacy risk/audit nor connectivity");
            using var schema = JsonDocument.Parse(tool.InputSchema.Json);
            var root = schema.RootElement;
            check(!root.GetProperty("additionalProperties").GetBoolean()
                && root.GetProperty("properties").GetProperty("limit").GetProperty("maximum").GetInt32() == 50,
                "1A: strict provider schema and row cap unchanged");
            using var json = JsonDocument.Parse(JsonSerializer.Serialize(tool));
            check(!json.RootElement.TryGetProperty("Policy", out _)
                && !json.RootElement.TryGetProperty("OutputType", out _)
                && !json.RootElement.TryGetProperty("ActionLevel", out _),
                "1A: internal policy does not expand serialized tool contracts");
        }
        check(registry.Find("query_rates") is null && registry.Find("execute_sql") is null,
            "1A: no additional specialist or SQL handler registered");
        var web = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        bool Fields<T>(T value, string[] expected)
        {
            using var json = JsonDocument.Parse(JsonSerializer.Serialize(value, web));
            return json.RootElement.EnumerateObject().Select(p => p.Name).SequenceEqual(expected);
        }
        check(Fields(new AiChatResponse("run", "ok", "summary"),
            ["runId", "code", "summary", "agentId", "mock", "usage", "evidence", "correlationId", "contextUsed", "kpi"]),
            "1A/1D/2: public chat response envelope is the 1A one, plus correlationId and contextUsed (1D) and kpi (2) appended");
        check(Fields(new AiStatus(false, false, false, false, true, false, false, []),
            ["enabled", "chatEnabled", "providerConfigured", "mock", "configurationValid", "liveToolsReady",
                "writeToolsReady", "agents", "auditReady", "operationsControl"]),
            "1A: public status envelope unchanged");
        check(Fields(new AiAgentStatus("operations-agent", "Operations", true, true),
            ["id", "name", "enabled", "connected"]), "1A: agent status remains backward compatible");
    }
}
