using Microsoft.EntityFrameworkCore;
using Scmos.Api.Data;

namespace Scmos.Api.Services;

public record BandView(string Label, decimal Min, decimal Max, int Position);

public record LaneView(
    long Id, int? SupplierId, string Carrier, string Service, string Customer,
    string From, string To, string County, string Remark,
    Dictionary<string, int?[]> Prices);

public record RateBookView(
    IReadOnlyList<BandView> Bands,
    IReadOnlyList<LaneView> Lanes,
    IReadOnlyList<RateSurcharge> Surcharges,
    int PriceCount);

public record CarrierQuote(string Carrier, int? SupplierId, int Price, LaneView Lane);

/// <summary>
/// The rate book, read from Azure SQL.
///
/// It used to be a two-megabyte file the web app served from its public
/// directory, which was wrong twice over: eighteen carriers' negotiated prices
/// were reachable by anyone who guessed the URL, and the backend could not see
/// them — so carrier priority could not be ordered by what a carrier charges,
/// which is the ordering the process actually asks for.
/// </summary>
public class RateService(ScmosDbContext db)
{
    /// <summary>Which band a diesel price falls in, or -1 above every quoted band.</summary>
    public static int BandFor(IReadOnlyList<BandView> bands, decimal diesel)
    {
        foreach (var band in bands)
            if (diesel <= band.Max) return band.Position;
        return -1;
    }

    public async Task<RateBookView> ReadAsync(string? carrier, string? service, CancellationToken token)
    {
        var bands = await db.FuelBands.AsNoTracking().OrderBy(band => band.Position)
            .Select(band => new BandView(band.Label, band.MinPrice, band.MaxPrice, band.Position))
            .ToListAsync(token);

        var query = db.RateLanes.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(carrier) && carrier != "All")
            query = query.Where(lane => lane.Carrier == carrier);
        if (!string.IsNullOrWhiteSpace(service) && service != "All")
            query = query.Where(lane => lane.Service == service);

        var lanes = await query.ToListAsync(token);
        var laneIds = lanes.Select(lane => lane.Id).ToHashSet();

        var prices = await db.RatePrices.AsNoTracking()
            .Where(price => laneIds.Contains(price.LaneId))
            .ToListAsync(token);

        var width = bands.Count == 0 ? 0 : bands.Max(band => band.Position) + 1;
        var byLane = prices.GroupBy(price => price.LaneId)
            .ToDictionary(group => group.Key, group => group.ToList());

        var views = lanes.Select(lane =>
        {
            var table = new Dictionary<string, int?[]>();
            if (byLane.TryGetValue(lane.Id, out var rows))
            {
                foreach (var price in rows)
                {
                    if (!table.TryGetValue(price.Vehicle, out var row))
                    {
                        row = new int?[width];
                        table[price.Vehicle] = row;
                    }
                    if (price.BandPosition >= 0 && price.BandPosition < width)
                        row[price.BandPosition] = price.Price;
                }
            }
            return new LaneView(lane.Id, lane.SupplierId, lane.Carrier, lane.Service, lane.Customer,
                lane.FromPlace, lane.ToPlace, lane.County, lane.Remark, table);
        }).ToList();

        var surcharges = await db.RateSurcharges.AsNoTracking().ToListAsync(token);
        return new RateBookView(bands, views, surcharges, prices.Count);
    }

    /// <summary>
    /// The price for a lane and vehicle at a diesel price.
    ///
    /// A block that only quoted the lower bands still answers for a higher
    /// diesel price: the last band it did quote is the contracted rate.
    /// </summary>
    public static int? PriceAt(LaneView lane, string vehicle, IReadOnlyList<BandView> bands, decimal diesel)
    {
        if (!lane.Prices.TryGetValue(vehicle, out var row)) return null;
        var wanted = BandFor(bands, diesel);
        if (wanted < 0) return null;

        for (var position = Math.Min(wanted, row.Length - 1); position >= 0; position--)
            if (row[position] is not null) return row[position];
        return null;
    }

    /// <summary>
    /// Who quoted this journey, cheapest first.
    ///
    /// This is what the carrier priority has been missing. Matching is on the
    /// customer and the two end points; a carrier is only offered when they
    /// actually priced the vehicle the job needs.
    /// </summary>
    public async Task<IReadOnlyList<CarrierQuote>> QuotesForAsync(
        string customer, string destination, string vehicle, decimal diesel, CancellationToken token)
    {
        if (vehicle.Length == 0) return [];

        var book = await ReadAsync(null, null, token);
        var wanted = Tokens($"{customer} {destination}");
        if (wanted.Count == 0) return [];

        var best = new Dictionary<string, CarrierQuote>(StringComparer.OrdinalIgnoreCase);

        foreach (var lane in book.Lanes)
        {
            var price = PriceAt(lane, vehicle, book.Bands, diesel);
            if (price is null) continue;

            var score = Math.Max(
                Overlap(wanted, Tokens($"{lane.Customer} {lane.To}")),
                Overlap(wanted, Tokens($"{lane.Customer} {lane.From}")));
            if (score < 0.5) continue;

            if (!best.TryGetValue(lane.Carrier, out var held) || held.Price > price.Value)
                best[lane.Carrier] = new CarrierQuote(lane.Carrier, lane.SupplierId, price.Value, lane);
        }

        return best.Values.OrderBy(quote => quote.Price).ToList();
    }

    private static readonly string[] Stop =
        ["co", "ltd", "company", "limited", "thailand", "th", "inc", "plc", "จำกัด", "บริษัท", "มหาชน"];

    private static List<string> Tokens(string value) =>
        (value ?? "").ToLowerInvariant()
            .Split([' ', ',', '.', '/', '-', '(', ')', '\t', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Where(word => word.Length > 2 && !Stop.Contains(word))
            .ToList();

    private static double Overlap(List<string> a, List<string> b)
    {
        if (a.Count == 0 || b.Count == 0) return 0;
        var set = b.ToHashSet(StringComparer.Ordinal);
        return (double)a.Count(set.Contains) / Math.Min(a.Count, b.Count);
    }
}
