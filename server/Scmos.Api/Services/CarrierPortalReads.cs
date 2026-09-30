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
}
