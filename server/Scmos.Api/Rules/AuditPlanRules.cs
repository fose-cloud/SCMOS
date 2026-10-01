namespace Scmos.Api.Rules;

/// <summary>
/// The audit plan's vocabulary, as the workbook's legend writes it (1 Oct 2026): a date is a fixed schedule,
/// ○ a tentative one, ✓ done, X not yet done, → a project that continues, ⇢ an audit postponed to a later date.
/// "Not yet done" is not stored — it is a planned audit whose date has passed, read again every time.
/// </summary>
public static class AuditPlanRules
{
    public const string ReAudit = "re-audit";
    public const string New = "new";
    public static readonly string[] Kinds = [ReAudit, New];

    /// <summary>The section headings, in the sheet's order.</summary>
    public static readonly IReadOnlyDictionary<string, string> Sections = new Dictionary<string, string>
    {
        [ReAudit] = "RE-Audit EHS : Truck Sub-Contractor",
        [New] = "New Sub-Contractor Audit",
    };

    public const string Fixed = "fixed";
    public const string Tentative = "tentative";
    public static readonly string[] Schedules = [Fixed, Tentative];

    public const string Planned = "planned";
    public const string Done = "done";
    public const string Postponed = "postponed";
    public const string Continue = "continue";
    public const string Cancelled = "cancelled";
    public static readonly string[] Statuses = [Planned, Done, Postponed, Continue, Cancelled];

    /// <summary>A planned audit whose date has gone by without being done — the sheet's X.</summary>
    public static bool NotYetDone(string status, string auditDate, int today) =>
        status == Planned && Formats.DateNumber(auditDate) is > 0 and var due && due < today;

    /// <summary>The year an audit date falls in, or 0 when it is not a date.</summary>
    public static int YearOf(string date) => Formats.DateNumber(date) / 10000;
}
