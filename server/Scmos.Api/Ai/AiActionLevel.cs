namespace Scmos.Api.Ai;

/// <summary>What an action does, not its data sensitivity or permission to execute.</summary>
public enum AiActionLevel
{
    Unspecified = 0,
    Read = 1,
    Recommend = 2,
    ApprovalRequired = 3,
    Restricted = 4,
}

/// <summary>
/// Reviewed server-side metadata, not an authorization grant or approval mechanism.
/// MaxEvidenceRows limits returned evidence, never source rows used for totals.
/// </summary>
public sealed record AiToolPolicy(AiActionLevel ActionLevel, string Version, string Source,
    Type OutputType, int MaxEvidenceRows)
{
    public string ScopePolicy => "server-resolved-team-or-operator";
    // The existing orchestrator owns the deadline; no second timeout is introduced.
    public string DeadlineSetting => $"{AiOptions.Section}:{nameof(AiOptions.TimeoutSeconds)}";
}
