using Scmos.Api.Data;

namespace Scmos.Api.Rules;

/// <summary>
/// What the Communication Center shows, and what changes when somebody decides.
///
/// <para>
/// The screen's questions, answered here rather than in the query or the
/// component: which messages are waiting on a person, what a message's state
/// becomes once its links are settled, and whether a decision may be made at
/// all. Each of those is a rule the inbox count and the detail page both depend
/// on, and two answers to any of them is a badge saying 5 over a list of 3.
/// </para>
/// </summary>
public static class MailReview
{
    /// <summary>What the inbox is asked to show.</summary>
    public static class View
    {
        /// <summary>Everything, newest first.</summary>
        public const string All = "ALL";

        /// <summary>Waiting on a person — a suggestion to settle, or a failure.</summary>
        public const string Waiting = "WAITING";

        /// <summary>Attached to a job, either automatically or by somebody.</summary>
        public const string Linked = "LINKED";

        /// <summary>Attached to nothing. Mail about no job the register holds.</summary>
        public const string Unlinked = "UNLINKED";
    }

    /// <summary>The views the screen offers, in the order it offers them.</summary>
    public static readonly string[] Views = [View.Waiting, View.Linked, View.Unlinked, View.All];

    /// <summary>A recognised view, or <see cref="View.All"/> for anything else.</summary>
    public static string ViewOf(string? asked)
    {
        var text = (asked ?? "").Trim().ToUpperInvariant();
        return Views.Contains(text) ? text : View.All;
    }

    /// <summary>
    /// What a message's processing status becomes once its links are what they
    /// are.
    ///
    /// <para>
    /// Recomputed from the links rather than nudged from wherever it was.
    /// Somebody confirms one of two suggestions and the message is still
    /// waiting; they settle the second and it is not; they reject a link the
    /// machine made and it goes back to waiting because nothing is attached any
    /// more. Deriving it is the only way those three end up agreeing.
    /// </para>
    ///
    /// <para>
    /// A message that failed to fetch is not touched. Its status is about the
    /// fetch, not about the linking, and rewriting it here would hide a message
    /// SCMOS never managed to read behind a tidy "processed".
    /// </para>
    /// </summary>
    /// <param name="statuses">The status of every link on the message.</param>
    /// <param name="current">Where the message is now.</param>
    public static string StatusAfter(IReadOnlyList<string>? statuses, string? current)
    {
        if (string.Equals(current, MailProcessing.Failed, StringComparison.Ordinal))
            return MailProcessing.Failed;

        var links = statuses ?? [];
        return links.Any(one => string.Equals(one, MailLink.Suggested, StringComparison.Ordinal))
            ? MailProcessing.NeedReview
            : MailProcessing.Processed;
    }

    /// <summary>
    /// Whether a link may be moved to this status by a person.
    ///
    /// <para>
    /// Confirmed and rejected, and nothing else. A person cannot put a link back
    /// to SUGGESTED: that is the machine's own word for "I guessed", and a
    /// person who has looked at a message has done something better than guess
    /// even when they decide it does not belong.
    /// </para>
    /// </summary>
    public static bool IsADecision(string? status) =>
        string.Equals(status, MailLink.Confirmed, StringComparison.Ordinal)
        || string.Equals(status, MailLink.Rejected, StringComparison.Ordinal);

    /// <summary>
    /// Whether a message still has something on it for a person.
    ///
    /// Used for the badge on the menu and for the WAITING view, from the same
    /// two facts, so the count and the list cannot disagree.
    /// </summary>
    public static bool IsWaiting(string? processingStatus) =>
        string.Equals(processingStatus, MailProcessing.NeedReview, StringComparison.Ordinal)
        || string.Equals(processingStatus, MailProcessing.Failed, StringComparison.Ordinal);
}
