namespace Scmos.Api.Ai;

/// <summary>Metadata only; never prompts, raw arguments, PII, secrets or hidden reasoning.</summary>
public sealed record AiExecutionEvent(string RunId, string UserId, string Role, string AgentId,
    string Event, string? Tool, string Status, DateTimeOffset At, int? Total = null, int? Returned = null,
    AiReadScope? Scope = null, AiUsage? Usage = null, string? ToolCallId = null, string? Model = null,
    string? View = null, int? Limit = null, string[]? SourceKeys = null);
public interface IAiExecutionAudit
{
    bool Ready { get; }
    Task<bool> CheckReadyAsync(CancellationToken token) => Task.FromResult(Ready);
    Task RecordAsync(AiExecutionEvent entry, CancellationToken token);
}

/// <summary>Phase D supplies durable storage. There is no production in-memory fallback or bypass flag.</summary>
public sealed class UnavailableAiExecutionAudit : IAiExecutionAudit
{
    public bool Ready => false;
    public Task RecordAsync(AiExecutionEvent entry, CancellationToken token)
        => throw new InvalidOperationException("Durable AI audit is not connected.");
}
