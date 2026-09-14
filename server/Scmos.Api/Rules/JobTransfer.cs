using System.Text.Json.Nodes;

namespace Scmos.Api.Rules;

/// <summary>
/// Moving one person's jobs to another.
///
/// <para>
/// A delegation lets a colleague edit somebody's jobs for a while and leaves
/// the name on them. This changes the name. It is the other half of the same
/// arrangement — the one the grant itself points at when a leave runs past
/// ninety days: "ควรเปลี่ยนผู้รับผิดชอบงานแทน" — and it is what somebody who has
/// resigned needs, since no grant covers a person who is not coming back.
/// </para>
///
/// <para>
/// Which jobs move is decided here, from the job's own date and status, so it
/// can be checked without a database. <c>--check-job-transfer</c> runs it.
/// </para>
/// </summary>
public static class JobTransfer
{
    /// <summary>
    /// The days whose jobs move, inclusive at both ends, as the date numbers
    /// the register compares with. Zero on a side means open on that side, and
    /// open on both means every job the person holds — a resignation rather
    /// than a leave.
    /// </summary>
    public sealed record Period(int From, int To)
    {
        public bool Unbounded => From == 0 && To == 0;

        /// <summary>As the screen wrote it, for the audit row and the preview.</summary>
        public string Describe(string fromText, string toText) =>
            Unbounded ? "ทุกงาน"
            : From == 0 ? $"ถึง {toText}"
            : To == 0 ? $"ตั้งแต่ {fromText}"
            : $"{fromText} – {toText}";
    }

    /// <summary>
    /// The period, or why it cannot be read.
    ///
    /// Either side may be left blank. A date that is written but will not
    /// parse is refused rather than treated as blank: "15/9/2026" typed for a
    /// week's leave would otherwise move every job the person has ever had.
    /// </summary>
    public static (Period? Period, string? Problem) ReadPeriod(string? fromDate, string? toDate)
    {
        var fromText = Formats.Clean(fromDate);
        var toText = Formats.Clean(toDate);
        var from = fromText.Length == 0 ? 0 : Formats.DateNumber(fromText);
        var to = toText.Length == 0 ? 0 : Formats.DateNumber(toText);

        if (fromText.Length > 0 && from == 0) return (null, "วันเริ่มต้องเขียนเป็น วว/ดด/ปปปป");
        if (toText.Length > 0 && to == 0) return (null, "วันสิ้นสุดต้องเขียนเป็น วว/ดด/ปปปป");
        if (from > 0 && to > 0 && to < from) return (null, "วันสิ้นสุดต้องไม่อยู่ก่อนวันเริ่ม");
        return (new Period(from, to), null);
    }

    /// <summary>What happens to one of the person's jobs.</summary>
    public enum Fate
    {
        /// <summary>Goes to the other person.</summary>
        Moves,
        /// <summary>Dated before or after the leave. Not the other person's business.</summary>
        Outside,
        /// <summary>
        /// Has no readable date, so nothing can say whether it falls in the
        /// leave. Left where it is and counted, so the person moving the work
        /// knows to look at it — only an unbounded transfer takes it.
        /// </summary>
        Undated,
        /// <summary>
        /// Finished or cancelled. It stays with whoever did it: the register
        /// is also the record of who worked what, and a completed job handed
        /// to somebody who never touched it is a KPI credited to the wrong
        /// person. A single job can still be moved by hand from the grid.
        /// </summary>
        ClosedOut,
    }

    public static Fate FateOf(Period period, string? workDate, string? status)
    {
        if (Stays(status)) return Fate.ClosedOut;
        if (period.Unbounded) return Fate.Moves;

        var day = Formats.DateNumber(workDate);
        if (day == 0) return Fate.Undated;
        var inside = (period.From == 0 || day >= period.From)
            && (period.To == 0 || day <= period.To);
        return inside ? Fate.Moves : Fate.Outside;
    }

    /// <summary>
    /// Whether a job is done with — finished or cancelled — and so stays with
    /// the person who had it. Read through the status ladder so a row still
    /// carrying the old free-text status ("Completed") answers the same way
    /// as one carrying the code.
    /// </summary>
    public static bool Stays(string? status)
    {
        var text = Formats.Clean(status);
        if (text.Length == 0) return false;
        var code = JobStatus.IsControlled(text) ? text.ToUpperInvariant() : JobStatus.FromLegacy(text);
        return JobStatus.IsClosedOut(code);
    }

    /// <summary>
    /// The stored job with its owner changed and nothing else touched.
    ///
    /// The workspace's model rides along as JSON beside the columns, and the
    /// two are written together so they cannot drift — so the name and the id
    /// have to change in the JSON as well as in <c>owner</c> and
    /// <c>owner_id</c>, or the row would belong to one person and the job the
    /// workspace reads back would say another.
    /// </summary>
    public static string Reassigned(string data, string name, string id)
    {
        JsonObject? node;
        try { node = JsonNode.Parse(data)?.AsObject(); }
        catch (System.Text.Json.JsonException) { node = null; }
        if (node is null) return data;
        node["op"] = name;
        node["opId"] = id;
        return node.ToJsonString();
    }
}
