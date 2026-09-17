using System.Text.RegularExpressions;

namespace Scmos.Api.Rules;

/// <summary>
/// A message about several boxes, read as several messages.
///
/// <para>
/// The fifth real message, 17 Sep 2026, listed one booking and two boxes,
/// each on its own lines with its own status and its own truck:
/// </para>
/// <code>
/// SHPP
/// BKG.JJJCLCSHY603242
///
/// TXGU6125873 &gt;&gt;บรรจุเสร็จแล้วค่ะ
/// พขร.75-1724 นัฐพล ศรีจันทร์ 092-783-3892
///
/// TWCU8214443 &gt;&gt;บรรจุเสร็จแล้วค่ะ
/// พขร.72-2114 รังสรรค์ เรืองรัมย์ 092-572-5262
/// </code>
/// <para>
/// The parser refused it — two containers is not a message to guess at —
/// and the room was asked to send one box at a time. The department asked
/// for the opposite: read it in layers. This cuts such a message at every
/// line that names a box or a job number, carries the lines above the
/// first cut (the customer, the booking) into every part, and hands each
/// part to the parser as a message of its own — so each box gets its own
/// status, its own truck and its own decision, and the approval that
/// writes it is the same one every message gets.
/// </para>
///
/// <para>
/// Pure. What this cannot cut — two boxes on one line — is what the model
/// is asked to lay out (<c>LineMessageAnalyst</c>), in this same shape.
/// </para>
/// </summary>
public static class LineBlocks
{
    /// <summary>A line that starts a part: it names a box or a twelve-digit job number.</summary>
    private static readonly Regex Key = new(
        @"(?<![A-Za-z0-9])[A-Za-z]{3}[UuJjZz][\s-]?\d{7}(?!\d)|(?<!\d)\d{12}(?!\d)", RegexOptions.Compiled);

    /// <summary>
    /// The message cut into one part per box or job number, each with the
    /// header lines in front of it — or nothing, when the message has fewer
    /// than two keyed lines, or a line that names two keys (which is for the
    /// model to lay out, not for a rule to guess at).
    /// </summary>
    public static IReadOnlyList<string> Split(string? raw)
    {
        var lines = (raw ?? "").Replace("\r\n", "\n").Split('\n')
            .Select(line => line.Trim())
            .ToList();

        var header = new List<string>();
        var parts = new List<List<string>>();
        foreach (var line in lines)
        {
            if (line.Length == 0) continue;
            var keys = Key.Matches(line).Count;
            if (keys > 1) return [];
            if (keys == 1)
            {
                parts.Add([line]);
                continue;
            }
            if (parts.Count == 0) header.Add(line);
            else parts[^1].Add(line);
        }
        if (parts.Count < 2) return [];

        var head = string.Join(" ", header);
        return [.. parts.Select(part => (head.Length > 0 ? head + " " : "") + string.Join(" ", part))];
    }
}
