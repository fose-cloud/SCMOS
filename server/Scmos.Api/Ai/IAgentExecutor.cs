using Scmos.Api.Auth;

namespace Scmos.Api.Ai;

/// <summary>
/// Typed boundary for an already connected agent. Registration grants no permissions;
/// the orchestrator and implementation still enforce authorization and durable audit.
/// </summary>
public interface IAgentExecutor<TResult>
{
    string AgentId { get; }
    bool Connected { get; }
    bool AuditReady { get; }
    bool Ready { get; }
    Task<bool> CheckAuditReadyAsync(CancellationToken token);
    Task<TResult> RunAsync(string runId, AiChatRequest request, AppUser user,
        AgentDefinition agent, CancellationToken token);
}
