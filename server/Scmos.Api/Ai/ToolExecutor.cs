using System.Text.Json;
using Scmos.Api.Auth;
using Scmos.Api.Ai.Operations;

namespace Scmos.Api.Ai;

/// <summary>One request-owned budget; not a singleton and never reset after a failed call.</summary>
public sealed class AiDispatchBudget(int maxToolCalls = AiDispatchBudget.MaxToolCalls)
{
    /// <summary>One read per run for every agent but the Engineering Agent's source read, which names its own bound (Phase 6, second increment).</summary>
    public const int MaxToolCalls = 1;
    private int attempts;
    public int Allowed => maxToolCalls;
    public bool TryConsume() => Interlocked.Increment(ref attempts) <= maxToolCalls;
}

/// <summary>Only connected, validated reads. The caller retains durable before/after audit lifecycle.</summary>
public sealed class ToolExecutor(QueryPolicyGuard guard)
{
    public async Task<OperationsAnswer> ReadAsync(AiToolCall call, AppUser user, AgentDefinition agent,
        bool auditReady, AiDispatchBudget budget, string runId, DateTimeOffset now, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var tool = guard.Resolve(user, agent, call, auditReady)
            ?? throw new InvalidOperationException("Unauthorized tool.");
        var scope = AiPermissionPolicy.Scope(user) ?? throw new InvalidOperationException("Missing scope.");
        if (!budget.TryConsume()) throw new InvalidOperationException("Tool budget exhausted.");
        using var json = JsonDocument.Parse(call.Arguments);
        var result = await tool.Handler!.ReadAsync(json.RootElement, new(runId, user.UserId, scope, now), token);
        var evidence = result.Deserialize<OperationsAnswer>();
        if (evidence is null || evidence.Rows is null || evidence.Returned != evidence.Rows.Count
            || evidence.Returned < 0 || evidence.Total < evidence.Returned
            || evidence.Returned > json.RootElement.GetProperty("limit").GetInt32())
            throw new InvalidOperationException("Invalid read output.");
        return evidence;
    }
}
