using Microsoft.EntityFrameworkCore;
using Scmos.Api.Data;
using Scmos.Api.Rules;

namespace Scmos.Api.Services;

public record BandView(string Label, decimal Min, decimal Max, int Position);

public record LaneView(
    long Id, int? SupplierId, string Carrier, string Service, string Customer,
    string From, string To, string County, string Remark,
    Dictionary<string, int?[]> Prices,
    /// <summary>
    /// Where the row came from: <c>carrier</c> for a rate read off a carrier's
    /// own form, <c>quotation</c> for one keyed into the rate sheet and spread
    /// up the bands by the fuel clause.
    ///
    /// Appended rather than slotted in beside Carrier, because this is a
    /// positional record and everything that builds one does so by position.
    ///
    /// The screen shows it. A price a carrier signed and a price somebody typed
    /// this morning are both rates, but only one of them is a contract, and a
    /// table that mixed them without saying which is which would be the more
    /// convenient of the two and the less trustworthy.
    /// </summary>
    string Source = RateSources.Carrier,
    /// <summary>
    /// When this quoted lane was last moved into the contracted book, or null
    /// while it has not been.
    ///
    /// Only ever set on a <c>quotation</c> row. It is what lets the New
    /// Transport Rate tab show what is still waiting and what has already gone
    /// across, so pressing the button twice is not a guess.
    /// </summary>
    DateTime? PromotedAt = null,
    /// <summary>
    /// True when the quote has changed since it was moved.
    ///
    /// The contracted row is a copy taken at the moment of the move, on purpose
    /// — a rate somebody agreed to should not move under them because the sheet
    /// was edited afterwards. But the two drifting apart silently is how the
    /// book stops meaning anything, so the difference is shown and moving again
    /// is what resolves it.
    /// </summary>
    bool PromotedStale = false);

/// <summary>How a move went: what was written, and what could not be.</summary>
public record PromoteResult(int Lanes, int Prices, int Skipped, IReadOnlyList<string> Notes);

/// <summary>Where a rate row came from. Two words, spelled in one place.</summary>
public static class RateSources
{
    public const string Carrier = "carrier";
    public const string Quotation = "quotation";
}

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

    public Task<RateBookView> ReadAsync(string? carrier, string? service, CancellationToken token) =>
        ReadAsync(carrier, service, null, token);

    /// <summary>
    /// The rate book, optionally including what the rate sheet has been quoted.
    ///
    /// <paramref name="source"/> is "carrier", "quotation", or null for both.
    /// </summary>
    public async Task<RateBookView> ReadAsync(string? carrier, string? service, string? source,
        CancellationToken token)
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

        if (!Is(source, RateSources.Carrier))
        {
            var quoted = await QuotedLanesAsync(carrier, service, bands, width, token);
            views = Is(source, RateSources.Quotation) ? quoted : [.. views, .. quoted];
        }

        var surcharges = await db.RateSurcharges.AsNoTracking().ToListAsync(token);
        return new RateBookView(bands, views, surcharges, prices.Count);

        static bool Is(string? value, string what) =>
            string.Equals(value, what, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The rate sheet's own prices, as rate-book rows.
    ///
    /// The sheet holds one figure per vehicle — what the journey was quoted at,
    /// on one band — because that is the one number anybody is ever told. The
    /// rest of the row is the contract's fuel clause and follows from it, so it
    /// is worked out here rather than stored: a price and its six derivations
    /// written down separately are seven things that can disagree, and the day
    /// somebody corrects the quote is the day the other six stop matching it.
    ///
    /// The band is the inquiry's own, not the bottom rung. Of 3,005 lanes in
    /// the register, 1,806 were quoted at 30.00-32.99 — starting those at the
    /// bottom would put an extra 3% under three fifths of the book and print a
    /// price on a rung nobody quoted.
    /// </summary>
    private async Task<List<LaneView>> QuotedLanesAsync(string? carrier, string? service,
        IReadOnlyList<BandView> bands, int width, CancellationToken token)
    {
        // Which column of the band table each rung of the ladder is. Worked out
        // once here rather than per price: it is a property of the table, and
        // the table does not change while a request is being answered.
        var where = FuelLadder.PositionsIn([.. bands.Select(band => (band.Max, band.Position))]);

        var query = from lane in db.RateInquiryLanes.AsNoTracking()
                    join inquiry in db.RateInquiries.AsNoTracking() on lane.InquiryId equals inquiry.Id
                    select new { lane, inquiry };

        // The sheet's Subcon column holds a list — "SANGJA,SSL,PHURADA" — so a
        // carrier filter has to look inside it. Narrowed in SQL to rows that
        // mention the name at all, then split and compared whole, because
        // Contains on its own would let SSL match SSLOGISTICS.
        var wantCarrier = (carrier ?? "").Trim();
        var filtering = wantCarrier.Length > 0 && wantCarrier != "All";
        if (filtering) query = query.Where(row => row.lane.Carriers.Contains(wantCarrier));

        var wantService = (service ?? "").Trim();
        var narrowing = wantService.Length > 0 && wantService != "All";

        var rows = await query.ToListAsync(token);
        var lanes = new Dictionary<long, LaneView>();
        var rungOf = new Dictionary<long, int>();

        foreach (var row in rows)
        {
            if (filtering && !AnyOfFilter.IsAnyOfList(row.lane.Carriers, wantCarrier)) continue;

            // A lane quoted against a band that is not on the ladder is left out
            // rather than guessed onto the bottom rung. A wrong rung is a wrong
            // price at every band above it.
            var rung = FuelLadder.RungOf(row.inquiry.FuelBand);
            if (rung < 0) continue;

            var kind = row.lane.Fcl ? "FCL" : row.lane.Lcl ? "LCL" : "DOMESTIC";
            if (narrowing && !string.Equals(kind, wantService, StringComparison.OrdinalIgnoreCase))
                continue;

            rungOf[row.lane.Id] = rung;
            lanes[row.lane.Id] = new LaneView(
                row.lane.Id, null, row.lane.Carriers, kind, row.inquiry.Customer,
                row.lane.FromPlace, row.lane.ToPlace, row.lane.County, row.lane.Remark,
                [], RateSources.Quotation);
        }

        // One query for the prices, not one per lane.
        var ids = lanes.Keys.ToHashSet();
        var quoted = await db.RateInquiryPrices.AsNoTracking()
            .Where(price => ids.Contains(price.LaneId))
            .ToListAsync(token);

        foreach (var price in quoted)
        {
            if (!lanes.TryGetValue(price.LaneId, out var lane)) continue;
            lane.Prices[price.Vehicle] = FuelLadder.Expand(price.Price, rungOf[price.LaneId], where, width);
        }

        // Which of them are already in the contracted book, and whether what is
        // there still matches. One query, and only the bottom-rung price is
        // compared: the six above it are derived from that one, so if it agrees
        // they all do.
        var already = await db.RateLanes.AsNoTracking()
            .Where(lane => lane.FromInquiryLaneId != null && ids.Contains(lane.FromInquiryLaneId.Value))
            .Select(lane => new { lane.Id, From = lane.FromInquiryLaneId!.Value, lane.PromotedAt })
            .ToListAsync(token);

        var promotedIds = already.Select(one => one.Id).ToHashSet();
        var promotedPrices = await db.RatePrices.AsNoTracking()
            .Where(price => promotedIds.Contains(price.LaneId))
            .ToListAsync(token);
        var byPromoted = promotedPrices.GroupBy(price => price.LaneId)
            .ToDictionary(group => group.Key,
                group => group.ToDictionary(price => (price.Vehicle, price.BandPosition), price => price.Price));

        var result = new List<LaneView>(lanes.Count);
        foreach (var lane in lanes.Values)
        {
            // A lane with nothing priced is not a rate.
            if (lane.Prices.Count == 0) continue;

            var match = already.Find(one => one.From == lane.Id);
            if (match is null) { result.Add(lane); continue; }

            var stored = byPromoted.TryGetValue(match.Id, out var found) ? found : [];
            var stale = lane.Prices.Count != stored.Select(one => one.Key.Vehicle).Distinct().Count();
            if (!stale)
            {
                foreach (var (vehicle, row) in lane.Prices)
                {
                    for (var column = 0; column < row.Length && !stale; column++)
                    {
                        if (row[column] is null) continue;
                        stale = !stored.TryGetValue((vehicle, column), out var was) || was != row[column];
                    }
                    if (stale) break;
                }
            }

            result.Add(lane with { PromotedAt = match.PromotedAt, PromotedStale = stale });
        }

        return result;
    }

    /// <summary>
    /// Moves quoted lanes into the contracted rate book.
    ///
    /// Until this runs, a price typed into Rate Quotation lives only on the New
    /// Transport Rate tab: it can be read, compared and corrected, and it is
    /// not a rate anybody is working to. The move is what makes it one, which
    /// is why it is a button somebody presses rather than something that
    /// happens the moment a number is keyed.
    ///
    /// <para>The seven band prices are written out, not the one figure. The
    /// contracted book is a record of what was agreed at the moment it was
    /// agreed — deriving it live would mean an edit to the sheet silently
    /// rewriting a rate somebody had already quoted to a customer. Moving the
    /// same lane again overwrites its own row, so correcting a quote and
    /// pressing the button once more is the way to update it, and nothing is
    /// ever duplicated.</para>
    ///
    /// <para>Prices are replaced rather than merged. A vehicle dropped from the
    /// quotation has to disappear from the contracted row too; merging would
    /// leave it there for good, quoted by nobody.</para>
    /// </summary>
    public async Task<PromoteResult> PromoteAsync(IReadOnlyList<long> inquiryLaneIds, string who,
        CancellationToken token)
    {
        var notes = new List<string>();
        if (inquiryLaneIds.Count == 0) return new PromoteResult(0, 0, 0, notes);

        var bands = await db.FuelBands.AsNoTracking().OrderBy(band => band.Position)
            .Select(band => new BandView(band.Label, band.MinPrice, band.MaxPrice, band.Position))
            .ToListAsync(token);
        var width = bands.Count == 0 ? 0 : bands.Max(band => band.Position) + 1;
        var where = FuelLadder.PositionsIn([.. bands.Select(band => (band.Max, band.Position))]);

        var wanted = inquiryLaneIds.Distinct().ToList();
        var rows = await (from lane in db.RateInquiryLanes
                          join inquiry in db.RateInquiries on lane.InquiryId equals inquiry.Id
                          where wanted.Contains(lane.Id)
                          select new { lane, inquiry }).ToListAsync(token);

        var prices = await db.RateInquiryPrices.AsNoTracking()
            .Where(price => wanted.Contains(price.LaneId))
            .ToListAsync(token);
        var byLane = prices.GroupBy(price => price.LaneId)
            .ToDictionary(group => group.Key, group => group.ToList());

        var existing = await db.RateLanes
            .Where(lane => lane.FromInquiryLaneId != null && wanted.Contains(lane.FromInquiryLaneId.Value))
            .ToListAsync(token);
        var existingBy = existing.ToDictionary(lane => lane.FromInquiryLaneId!.Value);

        var now = DateTime.UtcNow;
        var movedLanes = 0;
        var movedPrices = 0;
        var skipped = 0;
        var manyCarriers = 0;

        foreach (var row in rows)
        {
            var rung = FuelLadder.RungOf(row.inquiry.FuelBand);
            if (rung < 0)
            {
                skipped++;
                if (notes.Count < 5)
                    notes.Add($"ใบที่ {row.inquiry.Number}: ช่วงราคาน้ำมัน \"{row.inquiry.FuelBand}\" ไม่อยู่ในบันได 7 ช่อง");
                continue;
            }
            if (!byLane.TryGetValue(row.lane.Id, out var quoted) || quoted.Count == 0)
            {
                skipped++;
                continue;
            }

            // Counted, not corrected. One price against several carriers is what
            // the register says, and picking one of them here would be this
            // service inventing which carrier agreed it.
            if (row.lane.Carriers.Contains(',')) manyCarriers++;

            if (!existingBy.TryGetValue(row.lane.Id, out var lane))
            {
                lane = new RateLane { FromInquiryLaneId = row.lane.Id };
                db.RateLanes.Add(lane);
                existingBy[row.lane.Id] = lane;
            }

            lane.Carrier = row.lane.Carriers;
            lane.Service = row.lane.Fcl ? "FCL" : row.lane.Lcl ? "LCL" : "DOMESTIC";
            lane.Customer = row.inquiry.Customer;
            lane.FromPlace = row.lane.FromPlace;
            lane.ToPlace = row.lane.ToPlace;
            lane.County = row.lane.County;
            lane.Remark = row.lane.Remark;
            lane.SourceFile = $"Rate Quotation · ใบที่ {row.inquiry.Number} · {row.inquiry.InquiredOn}";
            lane.PromotedAt = now;
            lane.PromotedBy = who;
            movedLanes++;

            // Saved before the prices, because a new lane has no id until it is.
            await db.SaveChangesAsync(token);

            var old = await db.RatePrices.Where(price => price.LaneId == lane.Id).ToListAsync(token);
            if (old.Count > 0) db.RatePrices.RemoveRange(old);

            foreach (var price in quoted)
            {
                var ladder = FuelLadder.Expand(price.Price, rung, where, width);
                for (var column = 0; column < ladder.Length; column++)
                {
                    if (ladder[column] is not int value) continue;
                    db.RatePrices.Add(new RatePrice
                    {
                        LaneId = lane.Id,
                        Vehicle = price.Vehicle,
                        BandPosition = column,
                        Price = value,
                    });
                    movedPrices++;
                }
            }
        }

        await db.SaveChangesAsync(token);

        if (manyCarriers > 0)
            notes.Add($"{manyCarriers} เส้นทางมีผู้ขนส่งมากกว่า 1 รายในช่องเดียว — ย้ายตามที่ทะเบียนบันทึกไว้");

        return new PromoteResult(movedLanes, movedPrices, skipped, notes);
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
