using System.Collections.Concurrent;
using Scmos.Api.Auth;

namespace Scmos.Api.Ai;

/// <summary>
/// What the assistant last did for one person — the tool, the view, the
/// limit, the day the answer was about — kept a few minutes so a follow-up
/// ("และงานล่าช้าล่ะ") can be read against it. Phase 1D's context pilot.
/// </summary>
/// <remarks>
/// Structured facts only: never the question, never the answer, never a
/// row. Bound to the account that asked; a different account, or the same
/// account after the window, finds nothing. In memory on this instance,
/// nowhere else, and gone at the next restart — a pilot, by design, with no
/// table behind it. Off unless <c>AI:ContextEnabled</c> is set.
/// </remarks>
public sealed record AiContextEntry(string AgentId, string Tool, string View, int Limit, string AsOfDate, DateTimeOffset At);

public sealed class AiContextService(TimeProvider clock)
{
    private readonly ConcurrentDictionary<string, AiContextEntry> _entries = new(StringComparer.Ordinal);

    /// <summary>The most accounts remembered at once; beyond it the stalest entries go first.</summary>
    public const int MaxEntries = 500;

    /// <summary>Remembers what a run just did for this person. Nothing is stored for an account without an id.</summary>
    public void Remember(AppUser user, AiContextEntry entry)
    {
        if (string.IsNullOrWhiteSpace(user.UserId)) return;
        if (_entries.Count >= MaxEntries) Trim();
        _entries[user.UserId] = entry;
    }

    /// <summary>
    /// What this person's last run did, when it was within <paramref name="window"/>
    /// and for the same agent; null otherwise. An expired entry is dropped on the way.
    /// </summary>
    public AiContextEntry? Recall(AppUser user, string agentId, TimeSpan window)
    {
        if (string.IsNullOrWhiteSpace(user.UserId) || !_entries.TryGetValue(user.UserId, out var entry)) return null;
        if (clock.GetUtcNow() - entry.At > window)
        {
            _entries.TryRemove(user.UserId, out _);
            return null;
        }
        return entry.AgentId == agentId ? entry : null;
    }

    public void Forget(AppUser user)
    {
        if (!string.IsNullOrWhiteSpace(user.UserId)) _entries.TryRemove(user.UserId, out _);
    }

    /// <summary>
    /// The one line the model is told about the previous question — the
    /// facts, not the words — so "the same but for delays" resolves to the
    /// same day and scope. The user's text stays untrusted data either way.
    /// </summary>
    public static string Hint(AiContextEntry entry) =>
        $"Context from this user's previous question a few minutes ago: tool {entry.Tool}, view {entry.View}, limit {entry.Limit}, as of {entry.AsOfDate}. "
        + "A follow-up that names no day or scope refers to the same day and scope; it still may only select one read-only tool.";

    private void Trim()
    {
        foreach (var stale in _entries.OrderBy(pair => pair.Value.At).Take(MaxEntries / 10).Select(pair => pair.Key).ToList())
            _entries.TryRemove(stale, out _);
    }
}
