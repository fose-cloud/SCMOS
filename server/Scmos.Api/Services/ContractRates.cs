using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Scmos.Api.Data;
using Scmos.Api.Rules;

namespace Scmos.Api.Services;

/// <summary>
/// A job's contract rate from its carrier's Rate, and how it was found.
/// </summary>
/// <param name="Amount">The price for one trip; null when none could be worked out — see <paramref name="Reason"/>.</param>
/// <param name="Matches">How many lanes fit equally well at different prices (more than one is a question, not a rate).</param>
/// <param name="Diesel">The diesel figure the band was chosen at.</param>
/// <param name="DieselFrom">"job" when keyed on the job, "month" when the month's average, "" when neither.</param>
/// <param name="Lane">The lane priced against: customer · from → to.</param>
/// <param name="Reason">Why there is no amount: no-vehicle, no-diesel, no-lane, not-quoted, many; empty when priced.</param>
public record ContractRateQuote(decimal? Amount, int Matches, string Vehicle, string Band, decimal? Diesel,
    string DieselFrom, string DieselMonth, bool DieselClosed, string Lane, long? LaneId, long? PriceId,
    string Version, string Source, string Reason);

/// <summary>
/// What a job costs by its carrier's Rate (30 Sep 2026: "Billing ... ให้แสดงว่างานนั้นๆ ราคาเท่าไหร่"). The rate
/// book's own rules, as Booking prices a job (<see cref="RateMatch"/>): the job's container wording onto a
/// vehicle; the carrier's lanes that fit the job's customer and destination at Booking's bar, the best-fitting
/// kept (a tie broken by the job's IMPORT/EXPORT in the lane's names); the band each lane quotes for the diesel
/// figure — the job's own, else the average of the month it ran in (<see cref="DieselMonth"/>). The lanes are
/// the carrier's Rate screen's: written against it, or against no supplier under a name it trades by.
///
/// <para>
/// A Billing Case shows it and the invoice form fills 1.1 from it; the invoice check tests a claim against the
/// same figure (<see cref="BillingValidationService"/>), so what the carrier is shown is what it is held to.
/// </para>
/// </summary>
public static class ContractRates
{
    private static readonly TimeSpan Thailand = TimeSpan.FromHours(7);

    public static async Task<Dictionary<string, ContractRateQuote>> QuoteAsync(ScmosDbContext db, Supplier supplier,
        IReadOnlyCollection<OperationJob> jobs, CancellationToken token)
    {
        var quotes = new Dictionary<string, ContractRateQuote>(StringComparer.Ordinal);
        if (jobs.Count == 0) return quotes;

        var bands = await db.FuelBands.AsNoTracking().OrderBy(row => row.Position).ToListAsync(token);
        var changes = (await db.DieselPrices.AsNoTracking().Select(row => new { row.EffectiveDate, row.Price }).ToListAsync(token))
            .Select(row => (row.EffectiveDate, (double)row.Price)).ToList();
        var aliases = await db.SupplierAliases.AsNoTracking().Where(row => row.SupplierId == supplier.Id)
            .Select(row => row.Alias).ToListAsync(token);
        var names = new HashSet<string>(aliases.Append(supplier.Name).Append(supplier.Code).Select(Key).Where(key => key.Length > 0));
        var lanes = (await db.RateLanes.AsNoTracking().Where(row => row.SupplierId == supplier.Id || row.SupplierId == null).ToListAsync(token))
            .Where(row => row.SupplierId == supplier.Id
                || row.Carrier.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Any(one => names.Contains(Key(one))))
            .ToList();
        var laneIds = lanes.Select(row => row.Id).ToList();
        var prices = laneIds.Count == 0 ? []
            : await db.RatePrices.AsNoTracking().Where(row => laneIds.Contains(row.LaneId)).ToListAsync(token);
        var byLane = prices.GroupBy(row => row.LaneId).ToDictionary(group => group.Key, group => group.ToList());
        var today = DateTimeOffset.UtcNow.ToOffset(Thailand).ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);

        foreach (var job in jobs)
            quotes[job.Key] = Quote(job, bands, changes, lanes, byLane, today);
        return quotes;
    }

    private static ContractRateQuote Quote(OperationJob job, List<FuelBand> bands, List<(string, double)> changes,
        List<RateLane> lanes, Dictionary<long, List<RatePrice>> byLane, string today)
    {
        var facts = Facts(job);
        var vehicle = RateMatch.VehicleForType(facts.Type);
        if (vehicle.Length == 0) return Empty("", null, "", "", false, "no-vehicle");

        var month = DieselMonth.ForJob(changes, facts.Date, today);
        var diesel = facts.Diesel ?? month?.Average;
        var from = facts.Diesel is not null ? "job" : month is not null ? "month" : "";
        var monthName = facts.Diesel is not null ? "" : month?.Month ?? DieselMonth.MonthOf(facts.Date);
        var closed = month?.Closed ?? false;
        if (diesel is null) return Empty(vehicle, null, "", monthName, false, "no-diesel");

        var fitting = new List<(RateLane Lane, double Score, int Band, RatePrice Price)>();
        var anyLane = false;
        foreach (var lane in lanes)
        {
            var score = RateMatch.Score(job.Customer, facts.Destination, lane.Customer, lane.FromPlace, lane.ToPlace);
            if (score < RateMatch.Fits) continue;
            anyLane = true;
            var quoted = byLane.GetValueOrDefault(lane.Id, []).Where(one => Same(one.Vehicle, vehicle)).ToList();
            var row = bands.Select(band => (band.MaxPrice, quoted.FirstOrDefault(one => one.BandPosition == band.Position))).ToList();
            var at = RateMatch.BandIn(row.Select(cell => (cell.MaxPrice, cell.Item2 is null ? (decimal?)null : cell.Item2.Price)).ToList(), diesel.Value);
            if (at >= 0) fitting.Add((lane, score, at, row[at].Item2!));
        }
        if (!anyLane) return Empty(vehicle, diesel, from, monthName, closed, "no-lane");
        if (fitting.Count == 0) return Empty(vehicle, diesel, from, monthName, closed, "not-quoted");

        var best = fitting.Max(one => one.Score);
        var top = fitting.Where(one => one.Score == best).ToList();
        if (top.Select(one => one.Price.Price).Distinct().Count() > 1)
        {
            // Two lanes fit as well as each other: the job's own IMPORT/EXPORT in a lane's names settles it.
            var kind = job.Cat.Trim().ToUpperInvariant();
            var named = top.Where(one => $"{one.Lane.Customer} {one.Lane.FromPlace} {one.Lane.ToPlace}".ToUpperInvariant().Contains(kind)).ToList();
            if (kind.Length > 0 && named.Count > 0 && named.Select(one => one.Price.Price).Distinct().Count() == 1) top = named;
        }
        if (top.Select(one => one.Price.Price).Distinct().Count() > 1)
            return Empty(vehicle, diesel, from, monthName, closed, "many") with { Matches = top.Count };

        var chosen = top[0];
        var band = bands[chosen.Band];
        return new(chosen.Price.Price, 1, vehicle, band.Label, diesel, from, monthName, closed,
            $"{chosen.Lane.Customer} · {chosen.Lane.FromPlace} → {chosen.Lane.ToPlace}", chosen.Lane.Id, chosen.Price.Id,
            chosen.Lane.SourceFile, chosen.Lane.PromotedAt?.ToString("O", CultureInfo.InvariantCulture) ?? chosen.Lane.SourceFile, "");
    }

    private static ContractRateQuote Empty(string vehicle, decimal? diesel, string from, string month, bool closed, string reason) =>
        new(null, 0, vehicle, "", diesel, from, month, closed, "", null, null, "", "", reason);

    private record JobFacts(string Date, string Destination, string Type, decimal? Diesel);

    private static JobFacts Facts(OperationJob job)
    {
        try
        {
            using var json = JsonDocument.Parse(job.Data);
            var root = json.RootElement;
            string Get(string name) => !root.TryGetProperty(name, out var value) ? ""
                : value.ValueKind == JsonValueKind.String ? (value.GetString() ?? "").Trim()
                : value.ValueKind == JsonValueKind.Number ? value.GetRawText() : "";
            decimal? diesel = decimal.TryParse(Get("diesel"), NumberStyles.Number, CultureInfo.InvariantCulture, out var keyed) && keyed > 0
                ? keyed : null;
            var destination = Get("destination");
            return new(Get("date"), destination.Length > 0 ? destination : Get("plant"), Get("type"), diesel);
        }
        catch (JsonException) { return new("", "", "", null); }
    }

    private static bool Same(string? a, string? b) => Key(a) == Key(b);
    private static string Key(string? value) => new((value ?? "").Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());
}
