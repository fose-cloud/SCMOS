namespace Scmos.Api.Rules;

/// <summary>One line of a report, with the base every rate was measured over.</summary>
/// <param name="Trips">Every trip in the period, measurable or not.</param>
/// <param name="Measurable">How many carry the four fields an arrival is judged from.</param>
/// <param name="OnTime">Of those, how many arrived by their plan.</param>
/// <param name="Late">Of those, how many did not.</param>
/// <param name="Otd">
/// The percentage, or <b>null</b> when nothing could be measured.
///
/// Null and not zero, and the distinction is the reason this record exists. A
/// customer whose trips carry no arrival time has not achieved 0% — nobody
/// knows what they achieved, and a report that prints 0% has invented a finding
/// out of a gap in the paperwork.
/// </param>
public record ReportLine(string Name, int Trips, int Measurable, int OnTime, int Late, double? Otd)
{
    /// <summary>What share of the trips could be judged at all.</summary>
    public int Coverage => Trips == 0 ? 0 : (int)Math.Round(100.0 * Measurable / Trips);
}

/// <param name="Month">"07/2026", as a month is written on a report.</param>
public record ReportTrendPoint(string Month, double? Otd, int Measurable);

public record MonthlyReportView(
    string Customer,
    string Month,
    ReportLine Summary,
    double Target,
    IReadOnlyList<ReportLine> Vendors,
    IReadOnlyList<DelayReason> DelayReasons,
    IReadOnlyList<ReportTrendPoint> Trend);

/// <summary>
/// Why trips were late, and how many.
///
/// Named for what it is rather than reusing Services' <c>Counted</c>: Rules is
/// the layer underneath Services and must not reach up into it, and a second
/// record called <c>Counted</c> in a second namespace is the shape of thing
/// somebody later merges wrongly.
/// </summary>
public record DelayReason(string Label, int Value);

/// <summary>
/// A customer's month, read out of the register.
///
/// <para>
/// Pure, and it imports nothing but <see cref="JobRules"/>. That matters more
/// here than anywhere: on-time is the figure the whole report is about, and
/// this file must never form its own opinion of what "on time" means. It calls
/// <see cref="JobRules.IsOnTime"/> and <see cref="JobRules.IsMeasurable"/> —
/// the same two functions the KPI screen, the carrier scorecard and the
/// supervisor's monitor call. A second definition of lateness living in a
/// reporting module is precisely how a PDF ends up disagreeing with the screen
/// it was generated from.
/// </para>
///
/// <para>
/// What it adds is the population: one customer, one month, grouped by carrier.
/// That is a filter, not a rule.
/// </para>
/// </summary>
public static class MonthlyReport
{
    /// <summary>
    /// What the team reports against. 85% is the figure on the customer packs;
    /// it is stated on the report rather than compared silently, so a reader
    /// can disagree with the target instead of only with the score.
    /// </summary>
    public const double DefaultTarget = 85.0;

    /// <summary>
    /// Below this many measurable trips a percentage says more about the sample
    /// than about the carrier.
    ///
    /// The same reasoning the KPI screen already applies to a carrier score:
    /// one trip on time is not a hundred percent. The line is still shown —
    /// hiding a carrier because they only ran twice would quietly shrink the
    /// month — but the rate is withheld and the counts speak for themselves.
    /// </summary>
    public const int MinimumSample = 5;

    /// <summary>One job, as much of it as this report reads.</summary>
    public record Trip(string Customer, string Carrier, JobRecord Record);

    /// <summary>
    /// Everything the report says about one set of trips.
    ///
    /// <paramref name="withRate"/> is false for a carrier below the minimum
    /// sample: the counts are still true, the percentage would not be.
    /// </summary>
    public static ReportLine Line(string name, IReadOnlyList<Trip> trips, bool withRate = true)
    {
        var measurable = trips.Where(one => JobRules.IsMeasurable(one.Record)).ToList();
        var onTime = measurable.Count(one => JobRules.IsOnTime(one.Record));
        var enough = withRate && measurable.Count >= MinimumSample;

        return new ReportLine(
            name,
            trips.Count,
            measurable.Count,
            onTime,
            measurable.Count - onTime,
            // Null on an empty base whatever the caller asked for: there is no
            // percentage of nothing, and a caller that wanted one is wrong.
            measurable.Count == 0 || !enough
                ? null
                : Math.Round(100.0 * onTime / measurable.Count, 1));
    }

    /// <summary>
    /// The whole report for one customer and one month.
    ///
    /// The summary keeps its rate whatever the sample, because it is the figure
    /// the month is about and withholding it would leave the reader with no
    /// headline at all; the base is printed beside it so a thin month is
    /// visible rather than hidden.
    /// </summary>
    public static MonthlyReportView Build(
        string customer, string month, IReadOnlyList<Trip> trips,
        IReadOnlyList<DelayReason> delayReasons, IReadOnlyList<ReportTrendPoint> trend,
        double target = DefaultTarget) =>
        new(customer, month,
            Line(customer, trips, withRate: trips.Count > 0),
            target,
            trips
                .Where(one => one.Carrier.Trim().Length > 0)
                .GroupBy(one => one.Carrier.Trim(), StringComparer.OrdinalIgnoreCase)
                .Select(group => Line(group.Key, group.ToList()))
                // Busiest first. Ordering by the rate would put a carrier who
                // ran twice above one who ran ninety times, which is the order
                // nobody wants to read a vendor table in.
                .OrderByDescending(line => line.Trips)
                .ThenBy(line => line.Name, StringComparer.OrdinalIgnoreCase)
                .ToList(),
            delayReasons,
            trend);

    /// <summary>Whether the month met what was agreed. Null when it cannot be said.</summary>
    public static bool? MeetsTarget(ReportLine summary, double target) =>
        summary.Otd is null ? null : summary.Otd >= target;

    /// <summary>
    /// How far the report can be trusted, in one word for the reader.
    ///
    /// Printed on the report itself rather than left for somebody to work out
    /// from the base. A customer whose trips are two-thirds unmeasured is not
    /// a customer whose OTD is known, and the person receiving the PDF is
    /// exactly the person least able to tell.
    /// </summary>
    public static string Confidence(ReportLine summary) => summary switch
    {
        { Trips: 0 } => "ไม่มีเที่ยวในเดือนนี้",
        { Measurable: 0 } => "วัดไม่ได้เลย — ไม่มีเที่ยวใดบันทึกเวลาถึง",
        _ when summary.Coverage >= 90 => "ครบถ้วน",
        _ when summary.Coverage >= 60 => $"วัดได้ {summary.Coverage}% ของเที่ยว",
        _ => $"วัดได้เพียง {summary.Coverage}% ของเที่ยว — ตัวเลขนี้ยังไม่ควรใช้ตัดสิน",
    };
}
