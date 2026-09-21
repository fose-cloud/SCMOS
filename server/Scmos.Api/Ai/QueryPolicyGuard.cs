using Scmos.Api.Auth;
using Scmos.Api.Ai.Communication;
using Scmos.Api.Ai.Documents;
using Scmos.Api.Ai.Data;
using Scmos.Api.Ai.Operations;

namespace Scmos.Api.Ai;

/// <summary>Read dispatch boundary. Identity and scope come only from the server.</summary>
public sealed class QueryPolicyGuard(ToolRegistry registry)
{
    /// <summary>The answer shapes a connected read may return — the Operations evidence, and since Phase 2 the Data Agent's figure.</summary>
    private static readonly Type[] Outputs = [typeof(OperationsAnswer), typeof(DataAnswer), typeof(MessagesAnswer), typeof(DocumentsAnswer)];
    /// <summary>The tables a reviewed read may stand on: the register, and since Phase 5 the documents table beside it.</summary>
    private static readonly string[] Sources = ["operation_jobs", "documents"];

    public bool Allowed(AppUser user, AgentDefinition agent, string name, bool auditReady)
        => AiPermissionPolicy.AuthorizeTool(user, agent, name, registry, auditReady) == "allowed"
        && registry.Find(name)?.Policy is { ActionLevel: AiActionLevel.Read, Version: "1",
            MaxEvidenceRows: ToolRegistry.OperationsEvidenceLimit } policy
        && Sources.Contains(policy.Source, StringComparer.Ordinal) && Outputs.Contains(policy.OutputType);

    public AiToolDefinition? Resolve(AppUser user, AgentDefinition agent, AiToolCall call, bool auditReady)
    {
        if (!Allowed(user, agent, call.Name, auditReady) || string.IsNullOrWhiteSpace(call.Id)
            || call.Id.Length > 200) return null;
        var tool = registry.Find(call.Name);
        return tool?.InputSchema.Valid(call.Arguments) == true ? tool : null;
    }
}
