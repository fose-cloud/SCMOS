using System.Text.Json.Serialization;
using Scmos.Api.Ai.Communication;
using Scmos.Api.Ai.Data;
using Scmos.Api.Ai.Operations;

namespace Scmos.Api.Ai;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AiChatRequest(string Message, string? AgentId = null, AiPageContext? Context = null);

// No role, user, SQL, model, system prompt or owner override is accepted from the browser.
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AiPageContext(string Page);

public enum AiRisk { Low, Medium, High, Restricted }
public sealed record AiUsage(int InputTokens, int OutputTokens);
public sealed record AiToolCall(string Id, string Name, string Arguments);
public sealed record AiProviderRequest(string Instructions, string Message, IReadOnlyList<AiToolDefinition> Tools);
public sealed record AiProviderResult(string Code, string? Text = null,
    IReadOnlyList<AiToolCall>? ToolCalls = null, AiUsage? Usage = null, bool Mock = false);

public interface IAiProvider
{
    bool Configured { get; }
    bool IsMock { get; }
    Task<AiProviderResult> CompleteAsync(AiProviderRequest request, CancellationToken token);
}

public sealed record AiChatResponse(string RunId, string Code, string Summary, string? AgentId = null,
    bool Mock = false, AiUsage? Usage = null, OperationsAnswer? Evidence = null,
    /// <summary>The API request's correlation id (1D) — the same one on every audit event of the run.</summary>
    string CorrelationId = "",
    /// <summary>Whether the previous question's facts were given to the model (1D context pilot).</summary>
    bool ContextUsed = false,
    /// <summary>The Data Agent's figure (Phase 2); null for every other agent.</summary>
    DataAnswer? Kpi = null,
    /// <summary>The Communication Agent's messages (Phase 4); null for every other agent.</summary>
    MessagesAnswer? Messages = null);
public sealed record AiChatOutcome(int Status, AiChatResponse Response);
public sealed record AiAgentStatus(string Id, string Name, bool Enabled, bool Connected);
public sealed record AiStatus(bool Enabled, bool ChatEnabled, bool ProviderConfigured, bool Mock,
    bool ConfigurationValid, bool LiveToolsReady, bool WriteToolsReady, IReadOnlyList<AiAgentStatus> Agents,
    bool AuditReady = false, OperationsControlView? OperationsControl = null);

public static class AiRequestValidator
{
    public const int MaxBodyBytes = 16 * 1024;
    public const int MaxMessageChars = 4000;

    public static bool Valid(AiChatRequest? request) => request is not null
        && !string.IsNullOrWhiteSpace(request.Message)
        && request.Message.Length <= MaxMessageChars
        && (request.AgentId is null || request.AgentId.Length is > 0 and <= 40)
        && (request.Context is null || request.Context.Page is { Length: > 0 and <= 40 });
}
