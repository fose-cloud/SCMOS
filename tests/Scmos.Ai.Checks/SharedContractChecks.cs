using System.Text.Json;
using Scmos.Api.Ai;
using Scmos.Api.Ai.Operations;

static class SharedContractChecks
{
    public static void Run(Action<bool, string> check)
    {
        var registry = new ToolRegistry();
        check(default(AiActionLevel) == AiActionLevel.Unspecified,
            "1A: unspecified action metadata is not a read permission");
        check(registry.All.Select(t => t.Name).SequenceEqual(
            ["query_shipments", "search_shipment", "query_delays"]), "1A: exactly the existing three tools");
        foreach (var tool in registry.All)
        {
            check(tool.Policy is { ActionLevel: AiActionLevel.Read, Version: "1", Source: "operation_jobs",
                    MaxEvidenceRows: 50, ScopePolicy: "server-resolved-team-or-operator", DeadlineSetting: "AI:TimeoutSeconds" }
                && tool.Policy.OutputType == typeof(OperationsAnswer), "1A: reviewed policy for " + tool.Name);
            check(tool.Risk == AiRisk.Low && tool.AuditPolicy == "required-before-and-after"
                && tool.AgentId == "operations-agent" && tool.Handler is null,
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
            ["runId", "code", "summary", "agentId", "mock", "usage", "evidence"]),
            "1A: public chat response envelope unchanged");
        check(Fields(new AiStatus(false, false, false, false, true, false, false, []),
            ["enabled", "chatEnabled", "providerConfigured", "mock", "configurationValid", "liveToolsReady",
                "writeToolsReady", "agents", "auditReady", "operationsControl"]),
            "1A: public status envelope unchanged");
        check(Fields(new AiAgentStatus("operations-agent", "Operations", true, true),
            ["id", "name", "enabled", "connected"]), "1A: agent status remains backward compatible");
    }
}
