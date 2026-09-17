namespace Scmos.Api.Rules;

/// <summary>
/// What a haulier's message goes into REMARK as.
///
/// <para>
/// "ALLNEX 260900760168 B5 ติดต่อแถวอยู่ลานดินค่ะ" — the truck is queuing at the
/// dirt yard. Not an arrival, not on the way with a time: nothing the status
/// ladder has a rung for, and until 17 Sep 2026 the room was told so ("ไม่พบ
/// สถานะ") and the message waited in the queue. The department's rule: say
/// nothing back, and put what was said into the job's REMARK with the date
/// and time the haulier sent it; a later update on the same job joins on
/// the end after a comma, so the cell reads as the day's log.
/// </para>
///
/// <para>Pure: the cell as it is and the message in, the cell as it will be out.</para>
/// </summary>
public static class LineRemark
{
    /// <summary>The row's outcome once its words are on the job.</summary>
    public const string Written = "remark-written";

    /// <summary>What separates one update from the next in the cell.</summary>
    public const string Separator = ", ";

    /// <summary>How much of one message the cell takes; a haulier's line is rarely longer.</summary>
    public const int MaxNote = 200;

    /// <summary>"17/09/2026 11:46 ALLNEX 260900760168 B5 ติดต่อแถวอยู่ลานดินค่ะ" — one update, as the cell writes it.</summary>
    public static string Note(string text, DateTimeOffset sentAt)
    {
        var said = LineParser.Normalise(text);
        if (said.Length > MaxNote) said = said[..MaxNote].TrimEnd() + "…";
        var here = sentAt.ToOffset(TimeSpan.FromHours(7));
        return $"{here:dd/MM/yyyy HH:mm} {said}";
    }

    /// <summary>
    /// The cell with this update on the end: the note alone when the cell is
    /// empty, the cell, a comma and the note otherwise. The same note twice
    /// — LINE redelivered, or the driver repeated — is written once.
    /// </summary>
    public static string Append(string? remark, string note)
    {
        var had = (remark ?? "").Trim();
        if (had.Length == 0) return note;
        if (had.Split(Separator).Any(one => one.Trim() == note)) return had;
        return had + Separator + note;
    }
}
