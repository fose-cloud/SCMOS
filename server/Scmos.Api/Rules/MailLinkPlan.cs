using Scmos.Api.Data;

namespace Scmos.Api.Rules;

/// <summary>
/// What to write when a message is matched against the register again.
///
/// <para>
/// A message is matched more than once. A claim is abandoned and the row goes
/// back in the queue; the catch-up finds mail the webhook already brought; the
/// extractor is improved and everything is re-read. Each of those has to leave
/// the same links as the first pass — <b>except where a person has since had an
/// opinion</b>.
/// </para>
///
/// <para>
/// That exception is the whole of this file. Somebody confirmed a link, or
/// rejected one, and re-running the rules must not quietly undo either. A
/// confirmation overwritten by a fresh SUGGESTED is a decision that silently
/// stopped counting; a rejection overwritten is worse, because the machine gets
/// to insist. So a row a person has touched is left exactly as it is, and only
/// the machine's own guesses are rewritten.
/// </para>
///
/// <para>
/// Pure, so <c>--check-links</c> can prove the re-run is a no-op with no
/// mailbox and no database — which is the property that cannot be checked by
/// looking at it.
/// </para>
/// </summary>
public static class MailLinkPlan
{
    /// <summary>A link already stored against this message.</summary>
    /// <param name="JobKey">The job it points at.</param>
    /// <param name="Status">SUGGESTED · CONFIRMED · REJECTED.</param>
    public record Existing(string JobKey, string Status);

    /// <summary>What to do about one job.</summary>
    public static class Do
    {
        /// <summary>No link yet. Write one.</summary>
        public const string Add = "ADD";

        /// <summary>The machine's own guess, restated with today's numbers.</summary>
        public const string Update = "UPDATE";

        /// <summary>A guess the rules no longer support. Take it away.</summary>
        public const string Remove = "REMOVE";

        /// <summary>A person has had an opinion. Do not touch it.</summary>
        public const string Keep = "KEEP";
    }

    /// <param name="JobKey">Which job.</param>
    /// <param name="Action">One of <see cref="Do"/>.</param>
    /// <param name="Match">The fresh match, where there is one.</param>
    public record Change(string JobKey, string Action, EmailMatching.JobMatch? Match);

    /// <summary>
    /// Whether a stored link is a person's doing rather than the machine's.
    ///
    /// Anything that is not the machine's own SUGGESTED counts, including a
    /// status nobody recognises: an unfamiliar value in that column is a reason
    /// to leave a row alone, not a reason to overwrite it.
    /// </summary>
    public static bool IsPersons(string? status) =>
        !string.Equals(status, MailLink.Suggested, StringComparison.Ordinal);

    /// <summary>
    /// The writes one re-match implies.
    ///
    /// <para>
    /// Matches below the offering threshold are not links at all — the
    /// specification says they are not shown — so they neither add a row nor
    /// keep one alive. A job that was suggested and no longer clears the bar has
    /// its suggestion withdrawn, because a stale guess left on screen is one
    /// somebody eventually confirms.
    /// </para>
    /// </summary>
    public static List<Change> For(
        IReadOnlyList<Existing>? existing,
        IReadOnlyList<EmailMatching.JobMatch>? matches)
    {
        var held = new Dictionary<string, Existing>(StringComparer.Ordinal);
        foreach (var one in existing ?? [])
            held[one.JobKey] = one;

        var changes = new List<Change>();
        // Two sets, not one. `visited` stops the same job being read twice out
        // of the match list; `written` is what this pass actually decided
        // about. Conflating them left a suggestion that had fallen below the
        // floor looking like something this pass had handled, so the sweep
        // stepped over it and the stale guess stayed on screen — where
        // somebody eventually confirms it.
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var written = new HashSet<string>(StringComparer.Ordinal);

        foreach (var match in matches ?? [])
        {
            if (!visited.Add(match.JobKey)) continue;

            // Below the floor it is not offered, so there is nothing to write —
            // and anything stored for it is left to the sweep, which withdraws
            // it.
            if (match.Decision == EmailMatching.Decision.Unmatched) continue;

            written.Add(match.JobKey);

            if (!held.TryGetValue(match.JobKey, out var already))
            {
                changes.Add(new(match.JobKey, Do.Add, match));
                continue;
            }

            changes.Add(IsPersons(already.Status)
                ? new(match.JobKey, Do.Keep, match)
                : new(match.JobKey, Do.Update, match));
        }

        // Everything stored that this pass did not write about.
        foreach (var (key, one) in held)
        {
            if (written.Contains(key)) continue;
            changes.Add(new(key, IsPersons(one.Status) ? Do.Keep : Do.Remove, null));
        }

        return changes;
    }

    /// <summary>
    /// Whether a person still has something to decide about this message.
    ///
    /// <para>
    /// True when anything is merely suggested, and true when two jobs both
    /// cleared the linking bar — two confident answers to one question is a
    /// question. False when one job was linked cleanly, and false when nothing
    /// reached the floor at all: an email about no job we hold is not a task, it
    /// is ordinary mail, and marking every one of them for review is how the
    /// flag stops meaning anything.
    /// </para>
    /// </summary>
    public static bool NeedsAPerson(IReadOnlyList<EmailMatching.JobMatch>? matches)
    {
        var offered = (matches ?? []).Where(one => one.Decision != EmailMatching.Decision.Unmatched).ToList();
        if (offered.Count == 0) return false;

        var linked = offered.Count(one => one.Decision == EmailMatching.Decision.Link);
        return linked != 1 || offered.Count != linked;
    }
}
