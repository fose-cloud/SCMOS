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
            // arrival.on_time is version 4: registered grace can depend on both customer and job type.
            && rules.All(r => r.Version == (r.Id == "arrival.on_time" ? "4" : "1")
                && r.SourceMember.StartsWith("Rules/") && r.MissingData.Length > 0),
            "1C: versioned rule descriptors retain code provenance and missing-data meaning");
        check(Scmos.Api.Ai.Semantic.BusinessRuleRegistry.Resolve("arrival.on_time")?.ThresholdMinutes == 0
            && Scmos.Api.Ai.Semantic.BusinessRuleRegistry.Resolve("arrival.late_beyond")?.ThresholdMinutes == Scmos.Api.Rules.JobRules.LateMinutes,
            "1C: zero-grace KPI is distinct from default lateness tolerance");
        check(Scmos.Api.Ai.Semantic.BusinessRuleRegistry.Resolve("unknown") is null
            && Scmos.Api.Ai.Semantic.BusinessRuleRegistry.Resolve("arrival.on_time", "CUSTOMER-SLA") is null
            && Scmos.Api.Ai.Semantic.BusinessRuleRegistry.Resolve("arrival.late_beyond", "LOTUS ASIA") is null,
            "1C: unknown rules and unverified customer contracts have no fallback");
        // A registered term (Rules/CustomerTerms.cs) resolves for that customer alone, carrying its grace.
        var lotus = Scmos.Api.Ai.Semantic.BusinessRuleRegistry.Resolve("arrival.on_time", "LOTUS ASIA");
        check(lotus is { ThresholdMinutes: 30, Version: "4" } && lotus.Meaning.Contains("LOTUS") && lotus.SourceMember == "Rules/JobRules.cs:JobRules.IsOnTime"
            && Scmos.Api.Rules.CustomerTerms.GraceMinutes("lotus") == 30 && Scmos.Api.Rules.CustomerTerms.GraceMinutes("L'OREAL") == 0,
            "1C: a customer's registered term resolves for that customer with its grace, and the department's rule for everyone else");
        var evonikTank = Scmos.Api.Ai.Semantic.BusinessRuleRegistry.Resolve("arrival.on_time", "EVONIK", "1X20' TK");
        check(evonikTank is { ThresholdMinutes: 180, Version: "4" } && evonikTank.Meaning.Contains("Tank-job")
            && Scmos.Api.Ai.Semantic.BusinessRuleRegistry.Resolve("arrival.on_time", "EVONIK", "1X20'") is null,
            "1C: a job-scoped term resolves only for the matching vehicle type");
        var budget = new AiDispatchBudget();
        check(budget.TryConsume() && !budget.TryConsume() && !budget.TryConsume() && budget.Allowed == 1,
            "1B: one call per request including failed attempts");
        var wider = new AiDispatchBudget(Scmos.Api.Ai.Engineering.EngineeringAgent.MaxSourceSteps);
        check(wider.Allowed == 4 && wider.TryConsume() && wider.TryConsume() && wider.TryConsume() && wider.TryConsume() && !wider.TryConsume(),
            "6: the Engineering Agent's source read names its own bound, four, and the fifth is refused");
        var concurrentBudget = new AiDispatchBudget();
        var winners = 0;
        Parallel.For(0, 20, _ => { if (concurrentBudget.TryConsume()) Interlocked.Increment(ref winners); });
        check(winners == 1, "1B: concurrent dispatch cannot exceed budget");
        check(default(AiActionLevel) == AiActionLevel.Unspecified,
            "1A: unspecified action metadata is not a read permission");
        check(registry.All.Select(t => t.Name).SequenceEqual(
            ["query_shipments", "search_shipment", "query_delays", "query_followup", "query_kpi", "query_messages", "query_documents", "query_repository", "read_source", "query_platform"]), "1A/2/3/4/5/6/7: reviewed read tools only");
        foreach (var tool in registry.All)
        {
            var data = tool.Name == "query_kpi";
            var messages = tool.Name == "query_messages";
            var documents = tool.Name == "query_documents";
            var engineering = tool.Name == "query_repository";
            var sourceRead = tool.Name == "read_source";
            var platform = tool.Name == "query_platform";
            check(tool.Policy is { ActionLevel: AiActionLevel.Read, Version: "1",
                    ScopePolicy: "server-resolved-team-or-operator", DeadlineSetting: "AI:TimeoutSeconds" }
                && tool.Policy.Source == (engineering || sourceRead ? "github_public_repo" : platform ? "platform" : documents ? "documents" : "operation_jobs")
                && tool.Policy.MaxEvidenceRows == (engineering ? 20 : 50)
                && tool.Policy.OutputType == (engineering ? typeof(Scmos.Api.Ai.Engineering.EngineeringAnswer)
                    : sourceRead ? typeof(Scmos.Api.Ai.Engineering.SourceStep)
                    : platform ? typeof(Scmos.Api.Ai.Sre.PlatformAnswer)
                    : documents ? typeof(Scmos.Api.Ai.Documents.DocumentsAnswer)
                    : data ? typeof(Scmos.Api.Ai.Data.DataAnswer) : messages ? typeof(Scmos.Api.Ai.Communication.MessagesAnswer)
                    : typeof(OperationsAnswer)), "1A/5: reviewed policy for " + tool.Name);
            check(tool.Risk == AiRisk.Low && tool.AuditPolicy == "required-before-and-after"
                && tool.AgentId == (engineering || sourceRead ? "engineering-agent" : platform ? "sre-agent" : documents ? "document-agent" : data ? "data-agent" : messages ? "communication-agent" : "operations-agent") && tool.Handler is null,
                "1A: metadata changes neither legacy risk/audit nor connectivity");
            using var schema = JsonDocument.Parse(tool.InputSchema.Json);
            var root = schema.RootElement;
            check(!root.GetProperty("additionalProperties").GetBoolean()
                && root.GetProperty("properties").GetProperty("limit").GetProperty("maximum").GetInt32() == (engineering ? 20 : 50),
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
            ["runId", "code", "summary", "agentId", "mock", "usage", "evidence", "correlationId", "contextUsed", "kpi", "messages", "documents", "engineering", "source", "platform", "collaboration"]),
            "1A/1D/2/4/5/6/8: public chat response envelope remains append-only");
        check(Fields(new AiStatus(false, false, false, false, true, false, false, []),
            ["enabled", "chatEnabled", "providerConfigured", "mock", "configurationValid", "liveToolsReady",
                "writeToolsReady", "agents", "auditReady", "operationsControl"]),
            "1A: public status envelope unchanged");
        check(Fields(new AiAgentStatus("operations-agent", "Operations", true, true),
            ["id", "name", "enabled", "connected"]), "1A: agent status remains backward compatible");
    }
}
