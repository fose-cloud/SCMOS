using Microsoft.EntityFrameworkCore;
using Scmos.Api.Data;
using Scmos.Api.Rules;

namespace Scmos.Api.Services;

/// <param name="Customer">The name as the register spells it.</param>
/// <param name="Trips">How many trips it has, so a picker can lead with the busiest.</param>
/// <param name="Measurable">How many of those can be scored at all.</param>
public record ReportCustomer(string Customer, int Trips, int Measurable)
{
    public int Coverage => Trips == 0 ? 0 : (int)Math.Round(100.0 * Measurable / Trips);
}

/// <summary>What the Report Centre offers before anybody has chosen anything.</summary>
public record ReportChoices(IReadOnlyList<ReportCustomer> Customers, IReadOnlyList<string> Months);

/// <summary>
/// A customer's month, assembled from the register.
///
/// <para>
/// The arithmetic is <see cref="MonthlyReport"/>, which imports nothing and is
/// checked with <c>--check-report</c>. This is the part that cannot be pure:
/// reading the register, resolving carrier spellings to companies, and finding
/// the delay records for the jobs in scope. Nothing here decides whether a trip
/// was on time.
/// </para>
///
/// <para>
/// Not built on <see cref="KpiEngine"/>, though it answers a similar question.
/// The engine is scoped to a period and cached against one, and threading a
/// customer through it would change a screen that is already in production for
/// the sake of a new one. What the two share is the only thing that matters —
/// <see cref="JobRules"/> — so the report and the KPI screen cannot disagree
/// about lateness however differently they select their rows.
/// </para>
/// </summary>
public class MonthlyReportService(JobRegisterCache register, CarrierDirectory carriers, ScmosDbContext db)
{
    /// <summary>How many months of history the trend line carries.</summary>
    private const int TrendMonths = 6;

    /// <summary>
    /// The month a job belongs to, as "MM/yyyy".
    ///
    /// Read off the plan date rather than the arrival, because a trip planned
    /// for the 31st and delivered on the 1st belongs to the month it was
    /// promised in — which is the month the customer is asking about.
    /// </summary>
    private static string MonthOf(JobRecord job)
    {
        var date = (job.Date ?? "").Trim();
        // dd/MM/yyyy, which is how every date in this register is written.
        return date.Length >= 10 ? date[3..10] : "";
    }

    private async Task<List<MonthlyReport.Trip>> TripsAsync(CancellationToken token)
    {
        var snapshot = await register.ReadAsync(token);
        var directory = await carriers.ReadAsync(token);

        return snapshot.Rows
            .Where(row => row.Record is not null)
            .Select(row => new MonthlyReport.Trip(
                (row.Record!.Customer ?? "").Trim(),
                // The register knows that "DGT" and "DGT Cross Haul Co., Ltd."
                // are one company. Reporting them as two vendors would split a
                // carrier's month across two rows of the table.
                directory.Company(row.Trucker),
                row.Record))
            .ToList();
    }

    /// <summary>
    /// The customers and months worth offering, and how measurable each is.
    ///
    /// The coverage travels with the name on purpose. A picker that lists
    /// twenty customers alike invites somebody to generate a performance report
    /// for one whose trips carry no arrival times at all, and discover only
    /// from the finished PDF that there was nothing to report.
    /// </summary>
    public async Task<ReportChoices> ChoicesAsync(CancellationToken token)
    {
        var trips = await TripsAsync(token);

        var customers = trips
            .Where(one => one.Customer.Length > 0)
            .GroupBy(one => one.Customer, StringComparer.OrdinalIgnoreCase)
            .Select(group => new ReportCustomer(
                group.Key,
                group.Count(),
                group.Count(one => JobRules.IsMeasurable(one.Record))))
            .OrderByDescending(one => one.Trips)
            .ThenBy(one => one.Customer, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var months = trips
            .Select(one => MonthOf(one.Record))
            .Where(one => one.Length > 0)
            .Distinct()
            // Newest first, sorted as yyyy-MM rather than as the dd/MM/yyyy
            // text, which would put December before February.
            .OrderByDescending(one => one[3..] + one[..2], StringComparer.Ordinal)
            .ToList();

        return new ReportChoices(customers, months);
    }

    /// <summary>
    /// The report as it goes over the wire — the computed view plus the things
    /// the screen needs said rather than worked out.
    ///
    /// <para>
    /// One place, because the archive stores this document and the endpoint
    /// returns it, and the stored copy is later rendered by the very same
    /// screen. When the two were built separately the archived version was
    /// missing three fields the page reads, and the page crashed on opening a
    /// snapshot — quietly, and only for the archive.
    /// </para>
    /// </summary>
    public static object Document(MonthlyReportView report, string generatedBy, string generatedAt) =>
        new
        {
            report.Customer,
            report.Month,
            report.Summary,
            report.Target,
            report.Vendors,
            report.DelayReasons,
            report.Trend,
            MeetsTarget = MonthlyReport.MeetsTarget(report.Summary, report.Target),
            Confidence = MonthlyReport.Confidence(report.Summary),
            MonthlyReport.MinimumSample,
            GeneratedBy = generatedBy,
            GeneratedAt = generatedAt,
        };

    /// <summary>Bangkok, which is the clock a report is dated in.</summary>
    public static string Stamp() =>
        DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(7)).ToString("dd/MM/yyyy HH:mm");

    public async Task<MonthlyReportView> BuildAsync(string customer, string month, CancellationToken token)
    {
        var all = await TripsAsync(token);
        var wanted = all
            .Where(one => string.Equals(one.Customer, customer.Trim(), StringComparison.OrdinalIgnoreCase))
            .ToList();
        var inMonth = wanted.Where(one => MonthOf(one.Record) == month).ToList();

        return MonthlyReport.Build(customer.Trim(), month, inMonth,
            await ReasonsAsync(inMonth, token),
            Trend(wanted, month));
    }

    /// <summary>
    /// Why the late ones were late, most common first.
    ///
    /// From the delay register rather than guessed from the gap: a trip that
    /// arrived two hours late has a reason somebody typed, and the categories
    /// are the ones <see cref="DelayReasons"/> already names. When nobody
    /// recorded anything the list is simply empty, and the report leaves the
    /// section out rather than printing a heading over nothing.
    /// </summary>
    private async Task<List<DelayReason>> ReasonsAsync(
        IReadOnlyList<MonthlyReport.Trip> trips, CancellationToken token)
    {
        if (trips.Count == 0) return [];

        // The register keys jobs by the same key the delay records point at.
        var keys = trips.Select(one => one.Record.Key).Where(one => one.Length > 0).ToHashSet();
        if (keys.Count == 0) return [];

        var records = await db.DelayRecords.AsNoTracking()
            .Where(one => keys.Contains(one.JobKey))
            .Select(one => one.Category)
            .ToListAsync(token);

        return records
            .GroupBy(one => (one ?? "").Trim().Length == 0 ? "Other" : one!.Trim())
            .Select(group => new DelayReason(
                Enum.TryParse<DelayCategory>(group.Key, ignoreCase: true, out var known)
                    ? DelayReasons.Thai(known)
                    : group.Key,
                group.Count()))
            .OrderByDescending(one => one.Value)
            .ThenBy(one => one.Label, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// The months up to and including the one reported on, oldest first.
    ///
    /// Never past it: a report on July that drew a line through August would be
    /// a graph of the future, and it is the kind of thing nobody notices until
    /// a customer does.
    /// </summary>
    private static List<ReportTrendPoint> Trend(IReadOnlyList<MonthlyReport.Trip> customerTrips, string month)
    {
        var key = (string one) => one.Length >= 7 ? one[3..] + one[..2] : "";
        var upTo = key(month);

        return customerTrips
            .GroupBy(one => MonthOf(one.Record))
            .Where(group => group.Key.Length > 0
                && (upTo.Length == 0 || string.CompareOrdinal(key(group.Key), upTo) <= 0))
            .OrderBy(group => key(group.Key), StringComparer.Ordinal)
            .TakeLast(TrendMonths)
            .Select(group =>
            {
                // Through the same rule the headline uses, so a point on the
                // line and the figure above it are the same measurement.
                var line = MonthlyReport.Line(group.Key, group.ToList());
                return new ReportTrendPoint(group.Key, line.Otd, line.Measurable);
            })
            .ToList();
    }
}
