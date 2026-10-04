using System.Text.Json;

namespace Scmos.Api.Ai;

/// <summary>Production handler decorator: identity, origin and scope are rechecked immediately before each read.</summary>
public sealed class PolicyReadHandler(AiToolDefinition tool, IAiPolicyGateway? gateway) : IAiReadToolHandler
{
    public async Task<JsonElement> ReadAsync(JsonElement arguments, AiToolContext context, CancellationToken token)
    {
        var action = AiPolicyCatalog.Current.Tools.GetValueOrDefault(tool.Name, (AiAction)(-1));
        var origin = context.OriginAgentId ?? tool.AgentId;
        var request = AiAuthorizationRequest.For(tool.AgentId, action, tool.Name, context.User, context.CorrelationId) with
        {
            RequestedDataScope = context.Scope, OriginAgentId = origin,
            OriginalAction = origin == tool.AgentId ? action : AiPolicyEntry.RunContract(origin).Action,
            DelegationChain = origin == tool.AgentId ? [origin] : [origin, tool.AgentId],
            NetworkDestination = tool.Name is "query_repository" or "read_source" ? "api.github.com" : null
        };
        // A context cannot borrow another user's identity or bypass the existing schema validation.
        if (context.User?.UserId != context.UserId)
            request = request with { User = null };
        request = request with { InputValid = tool.InputSchema.Valid(arguments.GetRawText()) };
        var authorization = await AiPolicyEntry.AuthorizeAsync(gateway, request, token);
        if (!authorization.Allowed) throw new InvalidOperationException("AI authorization denied: " + authorization.ReasonCode);
        return await tool.Handler!.ReadAsync(arguments, context, token);
    }
}
