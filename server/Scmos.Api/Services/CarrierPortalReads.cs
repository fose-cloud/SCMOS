using System.Text.RegularExpressions;
using Scmos.Api.Auth;
using Scmos.Api.Rules;

namespace Scmos.Api.Services;

/// <param name="Lanes">The carrier's own contracted lanes, each vehicle's price on each fuel band.</param>
public record CarrierRatesView(int SupplierId, string SupplierName, IReadOnlyList<BandView> Bands, IReadOnlyList<LaneView> Lanes);

/// <param name="OnTime">On-time delivery over this carrier's own jobs, with its trend.</param>
/// <param name="Score">This carrier's line of the contract scorecard, or null with no shipment in the period.</param>
public record CarrierKpiView(int SupplierId, string SupplierName, string Year, string Month,
    Measure? OnTime, CarrierScore? Score);

/// <summary>
/// What the Subcontractor's own Rate and KPI screens read (29 Sep 2026).
///
/// A carrier works for a different company, so both answers are cut down to that
/// company on the server, never on the screen: its lanes of the rate book, and its
/// line of the scorecard over its own jobs — no other carrier's score, no ranking,
/// no company-wide figure. The company is the account's, through
/// <see cref="CarrierService.CompanyOfAsync"/>; a request cannot name one. Null
/// for an account that is not a carrier's.
/// </summary>
public class CarrierPortalReads(CarrierService carriers, RateService rates, KpiEngine kpi, CarrierDirectory directory)
{
    public async Task<CarrierRatesView?> RatesAsync(AppUser user, CancellationToken token)
    {
        var company = await carriers.CompanyOfAsync(user, token);
        if (company is null) return null;
        var names = await carriers.NamesOfAsync(company, token);
        var book = await rates.ReadForSupplierAsync(company.Id, names, token);
        return new CarrierRatesView(company.Id, company.Name, book.Bands, book.Lanes);
    }

    public async Task<CarrierKpiView?> KpiAsync(AppUser user, Period period, CancellationToken token)
    {
        var company = await carriers.CompanyOfAsync(user, token);
        if (company is null) return null;
        var names = await carriers.NamesOfAsync(company, token);
        var lookup = await directory.ReadAsync(token);

        // The engine's own TRUCKER scope, set to every name this carrier trades under, so every figure
        // below is measured over its jobs alone — then only its own line is taken from what comes back.
        var report = await kpi.BuildWithTrendAsync(period, new KpiScope([], names.ToList()), token);
        var score = (report.Scorecard ?? [])
            .Where(line => names.Any(name => lookup.Same(line.Carrier, name)))
            .OrderByDescending(line => line.Shipments)
            .FirstOrDefault();
        var onTime = report.Measures.FirstOrDefault(measure => measure.Id == nameof(MeasureId.OnTimeDelivery));
        return new CarrierKpiView(company.Id, company.Name, period.Year, period.Month, onTime, score);
    }

    /// <summary>
    /// The department dashboard's measures, for the carrier's own Dashboard (30 Sep 2026): the same report
    /// the department's reads, over this carrier's jobs — and then cut again where the engine counts wider
    /// than its scope. Supplier performance averages every carrier on the scorecard, and an issue that names
    /// a haulier without a job can put another carrier there, so it is rebuilt from this carrier's line
    /// alone, trend included; the suppliers list keeps only this carrier; the issue counts and the
    /// "cases outside the filter" note are the department's, so they go.
    /// </summary>
    public async Task<KpiEngineReport?> DashboardMeasuresAsync(AppUser user, Period period, bool trend, CancellationToken token)
    {
        var company = await carriers.CompanyOfAsync(user, token);
        if (company is null) return null;
        var names = await carriers.NamesOfAsync(company, token);
        var lookup = await directory.ReadAsync(token);
        var scope = new KpiScope([], names.ToList());
        bool Mine(string carrier) => names.Any(name => lookup.Same(carrier, name));
        IReadOnlyList<CarrierScore> Own(KpiEngineReport one) => (one.Scorecard ?? []).Where(line => Mine(line.Carrier)).ToList();

        var report = trend ? await kpi.BuildWithTrendAsync(period, scope, token) : await kpi.BuildAsync(period, scope, token);
        var own = Own(report);
        var measures = new List<Measure>();
        foreach (var measure in report.Measures)
        {
            if (measure.Id == nameof(MeasureId.SupplierPerformance))
            {
                var mine = KpiEngine.SupplierPerformanceOf(own);
                List<TrendPoint>? points = null;
                if (measure.Trend is { } months)
                {
                    points = [];
                    foreach (var point in months)
                    {
                        var parts = point.Period.Split('-');
                        var line = parts.Length == 2 ? Own(await kpi.BuildAsync(new Period(parts[0], parts[1], ""), scope, token)).FirstOrDefault() : null;
                        points.Add(new TrendPoint(point.Period, line?.Weighted is { } weighted ? Math.Round(weighted, 1) : null, line?.Shipments ?? 0));
                    }
                }
                measures.Add(mine with { Target = measure.Target, MeetsTarget = mine.Value is { } value && measure.Target is { } target ? value >= target : null, Trend = points });
                continue;
            }
            measures.Add(measure with { Note = Outside.Replace(measure.Note, "") });
        }
        return report with
        {
            Measures = measures,
            Suppliers = report.Suppliers.Where(line => Mine(line.Carrier)).ToList(),
            Scorecard = own,
            UnattributedIssues = 0,
            IssuesInPeriod = 0,
        };
    }

    /// <summary>A count of the department's cases outside the scope, which the engine appends to two notes.</summary>
    private static readonly Regex Outside = new(@" · ไม่นับเคสที่ไม่ได้ผูกกับงานในขอบเขต \d+ เคส", RegexOptions.Compiled);

    public async Task<CarrierService.CarrierDashboardJobs?> DashboardJobsAsync(AppUser user, CancellationToken token)
    {
        var company = await carriers.CompanyOfAsync(user, token);
        return company is null ? null : await carriers.DashboardJobsAsync(company, token);
    }
}
