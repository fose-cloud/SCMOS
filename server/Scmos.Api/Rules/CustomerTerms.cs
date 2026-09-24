namespace Scmos.Api.Rules;

/// <summary>
/// The terms a customer's own agreement sets on the department's measures,
/// written once.
///
/// The on-time KPI has always been zero grace — a truck is late the minute it
/// is late — and that is still the department's rule. A customer's agreement
/// can say otherwise for that customer, and may apply only to one kind of job.
/// The term lives here, beside nothing else, and <see cref="JobRules.IsOnTime"/>
/// reads it, so the KPI engine, the monthly report, the Data Agent and the
/// dashboard all apply the one reading. The web's dashboard keeps its own copy
/// of the on-time count and reads the same table there
/// (<c>app/scmos/customerTerms.ts</c>); a test fails if the two tables differ.
///
/// A term is matched on the customer's name as the register spells it —
/// "LOTUS" and "LOTUS ASIA" are both Lotus — and never on a substring
/// elsewhere in the name, so a customer whose name merely contains the word
/// gets the department's rule.
/// </summary>
public static class CustomerTerms
{
    /// <param name="Customer">The name's first word(s), upper-case, as the register spells them.</param>
    /// <param name="GraceMinutes">Minutes after plan an arrival still counts on time.</param>
    /// <param name="Since">When the term took effect in SCMOS.</param>
    /// <param name="Scope">"all", or "tank" when the term applies only to Tank jobs.</param>
    public sealed record Term(string Customer, int GraceMinutes, string Since, string Scope, string Basis);

    public static readonly Term[] All =
    [
        new("LOTUS", 30, "21/09/2026", "all", "กำหนดโดยหัวหน้าแผนก 21/09/2026: รถถึงช้าไม่เกิน 30 นาทีไม่นับเป็นล่าช้าใน KPI และ Dashboard"),
        new("ALLNEX", 30, "24/09/2026", "all", "กำหนด 24/09/2026: รถถึงช้าไม่เกิน 30 นาทีไม่นับเป็นล่าช้าใน KPI และ Dashboard"),
        new("SYENSQO", 30, "24/09/2026", "all", "กำหนด 24/09/2026: รถถึงช้าไม่เกิน 30 นาทีไม่นับเป็นล่าช้าใน KPI และ Dashboard"),
        new("EVONIK (THAILAND) LTD.", 180, "24/09/2026", "tank", "กำหนด 24/09/2026 เฉพาะงาน Tank: รถถึงช้าไม่เกิน 180 นาทีไม่นับเป็นล่าช้าใน KPI และ Dashboard"),
    ];

    private static bool CustomerMatches(Term term, string name) =>
        name == term.Customer || name.StartsWith(term.Customer + " ", StringComparison.Ordinal);

    /// <summary>The term for a customer and job type, or null when the department's rule applies.</summary>
    public static Term? Of(string? customer, string? jobType = null)
    {
        var name = Formats.Clean(customer).ToUpperInvariant();
        if (name.Length == 0) return null;
        foreach (var term in All)
            if (CustomerMatches(term, name)
                && (term.Scope == "all" || term.Scope == "tank" && JobVehicleType.IsTank(jobType))) return term;
        return null;
    }

    /// <summary>Every registered term for a customer, including job-scoped terms.</summary>
    public static IEnumerable<Term> ForCustomer(string? customer)
    {
        var name = Formats.Clean(customer).ToUpperInvariant();
        return name.Length == 0 ? [] : All.Where(term => CustomerMatches(term, name));
    }

    /// <summary>Minutes after plan an arrival still counts on time — zero unless a matching term says otherwise.</summary>
    public static int GraceMinutes(string? customer, string? jobType = null) => Of(customer, jobType)?.GraceMinutes ?? 0;

    public static string Label(Term term) =>
        $"{term.Customer}{(term.Scope == "tank" ? " (เฉพาะงาน Tank)" : "")} นับตรงเวลาภายใน {term.GraceMinutes} นาที";

    public static string ContractLabel(Term term) =>
        $"{term.Customer}: on time within {term.GraceMinutes} minutes of plan"
        + (term.Scope == "tank" ? " for Tank jobs" : "") + $" (since {term.Since})";

    /// <summary>The terms in force, in one line for a figure's note — "LOTUS ภายใน 30 นาที".</summary>
    public static string Describe() =>
        string.Join(" · ", All.Select(Label));
}
