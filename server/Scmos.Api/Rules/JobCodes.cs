using System.Text.RegularExpressions;

namespace Scmos.Api.Rules;

/// <summary>
/// What a SCMOS job number looks like, in one place.
///
/// <para>
/// Twelve digits, as the register writes them — 260600800773. Deliberately not
/// "any run of digits": the register also carries SAP orders, delivery notes and
/// SID numbers, and all of those are ten. A looser pattern would match the
/// delivery note and act on whatever job happened to hold that number.
/// </para>
///
/// <para>
/// The boundaries are written out rather than <c>\b</c>, so a longer run of
/// digits does not yield a twelve-digit match inside it. 2606008007731234 is not
/// a job number with four digits after it; it is not a job number.
/// </para>
///
/// <para>
/// Lifted out of <see cref="LineParser"/> when the mail extractor needed the
/// same rule. Two readings of "what is a job number" is precisely the shape of
/// bug this codebase keeps finding — one of them would be widened for a real
/// case and the other would go on refusing it, and nobody would connect the two.
/// </para>
/// </summary>
public static class JobCodes
{
    private static readonly Regex Pattern =
        new(@"(?<!\d)\d{12}(?!\d)", RegexOptions.Compiled);

    /// <summary>Every distinct job number in a piece of text, in the order they appear.</summary>
    public static string[] All(string? text) =>
        string.IsNullOrEmpty(text)
            ? []
            : [.. Pattern.Matches(text).Select(one => one.Value).Distinct(StringComparer.Ordinal)];

    /// <summary>
    /// The job number in a message, and how many were found.
    ///
    /// Null unless there is exactly one. Two numbers is not a message to guess
    /// at: somebody writing about two trips in one line is asking for both to be
    /// acted on, and neither the LINE worker nor the mail extractor does that —
    /// they queue for review, which is what a review queue is for.
    /// </summary>
    public static (string? Number, int Found) FindOne(string? text)
    {
        var matches = All(text);
        return (matches.Length == 1 ? matches[0] : null, matches.Length);
    }

    /// <summary>Whether a string is a job number and nothing else.</summary>
    public static bool IsJobCode(string? value) =>
        value is not null && value.Length == 12 && Pattern.IsMatch(value);
}
