using Scmos.Api.Auth;
using Scmos.Api.Ai.Operations;

namespace Scmos.Api.Ai;

/// <summary>Read dispatch boundary. Identity and scope come only from the server.</summary>
public sealed class QueryPolicyGuard(ToolRegistry registry)
{
    public bool Allowed(AppUser user, AgentDefinition agent, string name, bool auditReady)
        => AiPermissionPolicy.AuthorizeTool(user, agent, name, registry, auditReady) == "allowed"
        && registry.Find(name)?.Policy is { ActionLevel: AiActionLevel.Read, Version: "1",
            Source: "operation_jobs", MaxEvidenceRows: ToolRegistry.OperationsEvidenceLimit } policy
        && policy.OutputType == typeof(OperationsAnswer);

    public AiToolDefinition? Resolve(AppUser user, AgentDefinition agent, AiToolCall call, bool auditReady)
    {
        if (!Allowed(user, agent, call.Name, auditReady) || string.IsNullOrWhiteSpace(call.Id)
            || call.Id.Length > 200) return null;
        var tool = registry.Find(call.Name);
        return tool?.InputSchema.Valid(call.Arguments) == true ? tool : null;
    }
}
