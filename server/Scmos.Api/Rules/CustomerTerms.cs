namespace Scmos.Api.Rules;

/// <summary>
/// The terms a customer's own agreement sets on the department's measures,
/// written once.
///
/// The on-time KPI has always been zero grace — a truck is late the minute it
/// is late — and that is still the department's rule. A customer's agreement
/// can say otherwise for that customer alone: on 21 September 2026 the
/// department lead set Lotus's — an arrival up to thirty minutes after plan
/// is not counted late in the KPI or on the dashboard; later than that, it
/// is. The term lives here, beside nothing else, and <see cref="JobRules.IsOnTime"/>
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
    public sealed record Term(string Customer, int GraceMinutes, string Since, string Basis);

    public static readonly Term[] All =
    [
        new("LOTUS", 30, "21/09/2026", "กำหนดโดยหัวหน้าแผนก 21/09/2026: รถถึงช้าไม่เกิน 30 นาทีไม่นับเป็นล่าช้าใน KPI และ Dashboard"),
    ];

    /// <summary>The term for a customer, or null when the department's rule applies.</summary>
    public static Term? Of(string? customer)
    {
        var name = Formats.Clean(customer).ToUpperInvariant();
        if (name.Length == 0) return null;
        foreach (var term in All)
            if (name == term.Customer || name.StartsWith(term.Customer + " ", StringComparison.Ordinal)) return term;
        return null;
    }

    /// <summary>Minutes after plan an arrival still counts on time for this customer — zero unless a term says otherwise.</summary>
    public static int GraceMinutes(string? customer) => Of(customer)?.GraceMinutes ?? 0;

    /// <summary>The terms in force, in one line for a figure's note — "LOTUS ภายใน 30 นาที".</summary>
    public static string Describe() =>
        string.Join(" · ", All.Select(term => $"{term.Customer} นับตรงเวลาภายใน {term.GraceMinutes} นาที"));
}
