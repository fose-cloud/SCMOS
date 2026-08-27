using Scmos.Api.Data;

namespace Scmos.Api.Rules;

/// <summary>
/// Which column of the customer's carrier scorecard an issue counts under.
///
/// The scorecard has five columns and the issue log has none of them. Somebody
/// filling the log chose a Thai category, a source and sometimes a grade, and a
/// rule in the scorecard turned that combination into one of the five — which
/// meant the person entering the data that decides a haulier's mark could not
/// see what their entry would count as, and the headings on the two screens had
/// nothing in common.
///
/// So the column is a field on the issue, offered in the form under the
/// customer's own headings. <see cref="Derive"/> is what the scorecard has
/// always worked out for itself, kept as the default for a new entry and as the
/// answer for every row logged before the field existed — nothing already
/// recorded changes its meaning.
/// </summary>
public static class ScorecardColumn
{
    public const string TransportMajor = "Transport Accident (Major)";
    public const string TransportMinor = "Transport Accident (Minor)";
    public const string LoadingAccident = "Loading Accident";
    public const string Complaint = "Complaint (Internal & External)";
    public const string Breakdown = "Truck break down / No customer complaint";

    /// <summary>Counted for the month, but not against any of the five columns.</summary>
    public const string NotCounted = "ไม่นับในคะแนน";

    /// <summary>In the order the customer's report lays them out.</summary>
    public static readonly string[] All =
        [TransportMajor, TransportMinor, LoadingAccident, Complaint, Breakdown, NotCounted];

    /// <summary>The category an accident is logged under in the issue register.</summary>
    public const string AccidentCategory = "ความปลอดภัย/อุบัติเหตุ";

    /// <summary>The category a lorry that would not run is logged under.</summary>
    public const string BreakdownCategory = "รถ/อุปกรณ์ไม่พร้อม";

    /// <summary>The category a damage or discrepancy report is logged under.</summary>
    public const string DamageCategory = "สินค้าชำรุด/สูญหาย";

    /// <summary>
    /// Where a complaint comes from, inside the company and outside it.
    ///
    /// The report column reads "Complaint (Internal &amp; external)", so both
    /// halves count: the customer complaining is external, and CS, shipping,
    /// billing or the warehouse raising it is internal. Customs, the depot and
    /// the forwarder are none of those — a customs hold is a fact about the
    /// shipment, not somebody complaining about the haulier — so they are left
    /// out.
    /// </summary>
    public static readonly IReadOnlySet<string> ComplaintSources =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { "Customer", "CS", "CS/Shipping", "Shipping", "Billing", "Warehouse" };

    /// <summary>
    /// What the register's own fields say this is, when nobody has said.
    ///
    /// This is the rule the scorecard applied silently. It is still applied —
    /// as the form's starting answer, and as the answer for the rows logged
    /// before the field existed — but it is visible now, and a person who knows
    /// better can say so.
    /// </summary>
    public static string Derive(string category, string source, string accidentGrade)
    {
        var kind = (category ?? "").Trim();

        if (string.Equals(kind, AccidentCategory, StringComparison.Ordinal))
        {
            var grade = (accidentGrade ?? "").Trim();
            if (grade.Equals("loading", StringComparison.OrdinalIgnoreCase)) return LoadingAccident;
            if (grade.Contains("minor", StringComparison.OrdinalIgnoreCase)) return TransportMinor;
            if (grade.Contains("major", StringComparison.OrdinalIgnoreCase)) return TransportMajor;

            // Reported as an accident with nobody saying which kind. Counted
            // and shown, kept out of the score: a major accident is weighted at
            // 35% against a minor one's 15%, and guessing would put a third of
            // somebody's mark on a guess.
            return "";
        }

        if (string.Equals(kind, BreakdownCategory, StringComparison.Ordinal)) return Breakdown;

        // An accident or a breakdown is never also a complaint — each has a
        // column of its own, and counting one event under two headings marks
        // the haulier down twice for it.
        return ComplaintSources.Contains((source ?? "").Trim()) ? Complaint : NotCounted;
    }

    /// <summary>
    /// The column this issue counts under: what somebody said, or what the
    /// register implies when nobody has.
    /// </summary>
    public static string Of(OperationalIssue issue)
    {
        var chosen = (issue.ScorecardColumn ?? "").Trim();
        if (chosen.Length > 0) return chosen;
        return Derive(issue.Category, issue.Source, issue.AccidentGrade);
    }

    /// <summary>An accident of any of the three kinds, graded or not.</summary>
    public static bool IsAccident(OperationalIssue issue)
    {
        var column = Of(issue);
        if (column is TransportMajor or TransportMinor or LoadingAccident) return true;

        // Logged as an accident and never graded: still an accident, still due
        // a report inside the accident window, still absent from the score.
        return string.Equals(issue.Category.Trim(), AccidentCategory, StringComparison.Ordinal);
    }

    /// <summary>Reported as an accident and never graded, so counted nowhere.</summary>
    public static bool IsUngradedAccident(OperationalIssue issue) =>
        IsAccident(issue) && Of(issue) is not (TransportMajor or TransportMinor or LoadingAccident);
}
