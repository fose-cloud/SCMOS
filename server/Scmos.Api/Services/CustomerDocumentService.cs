using Microsoft.EntityFrameworkCore;
using Scmos.Api.Data;

namespace Scmos.Api.Services;

/* -------------------------------------------------------------- what goes out */

public record CustomerBandView(string Label, decimal Min, decimal Max, int Position);

public record CustomerLaneView(
    long Id, string Carrier, string From, string To, string PostalCode,
    Dictionary<string, int?[]> Prices);

public record CustomerRateCardView(
    string Customer,
    IReadOnlyList<CustomerBandView> Bands,
    IReadOnlyList<CustomerLaneView> Lanes,
    IReadOnlyList<string> Carriers);

public record CargoTemplateView(string Customer, string SourceFile, IReadOnlyList<string> Columns);

/* --------------------------------------------------------------- what comes in */

public record CustomerBandInput(string Label, decimal Min, decimal Max);

public record CustomerLaneInput(
    string Carrier, string From, string To, string PostalCode,
    Dictionary<string, int?[]> Prices);

public record CustomerCardInput(
    string Customer, string Carrier,
    List<CustomerBandInput> Bands,
    List<CustomerLaneInput> Lanes);

public record CargoTemplateInput(string Customer, string SourceFile, List<string> Columns);

/// <summary>
/// A customer's own prices, and the shape of the receipt they sign for.
///
/// Kept apart from the subcontractor rate book throughout — see the note at the
/// top of CustomerDocumentEntities.cs for why that separation is the point
/// rather than an accident of storage.
/// </summary>
public class CustomerDocumentService(ScmosDbContext db)
{
    /* ------------------------------------------------------------- rates */

    public async Task<CustomerRateCardView> ReadCardAsync(string customer, CancellationToken token)
    {
        var bands = await db.CustomerRateBands.AsNoTracking()
            .Where(band => band.Customer == customer)
            .OrderBy(band => band.Position)
            .Select(band => new CustomerBandView(band.Label, band.MinPrice, band.MaxPrice, band.Position))
            .ToListAsync(token);

        var lanes = await db.CustomerRateLanes.AsNoTracking()
            .Where(lane => lane.Customer == customer)
            .ToListAsync(token);

        var laneIds = lanes.Select(lane => lane.Id).ToHashSet();
        var prices = await db.CustomerRatePrices.AsNoTracking()
            .Where(price => laneIds.Contains(price.LaneId))
            .ToListAsync(token);

        // One row per band, however many bands this customer's clause has. The
        // width comes from the stored positions rather than from the count, so a
        // card whose bands were saved with a gap still lines its prices up.
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
            return new CustomerLaneView(lane.Id, lane.Carrier, lane.FromPlace, lane.ToPlace,
                lane.PostalCode, table);
        }).ToList();

        var carriers = views.Select(lane => lane.Carrier).Distinct().OrderBy(name => name).ToList();
        return new CustomerRateCardView(customer, bands, views, carriers);
    }

    /// <summary>
    /// Replaces one haulier's part of one customer's card, and nothing else.
    ///
    /// Not the whole card: more than one company runs this work, their cards
    /// arrive separately, and the seeder next door taught this lesson the
    /// expensive way — it clears every rate in the book before loading, so
    /// loading one carrier's file through it would wipe the other seventeen.
    /// Saving THAI KOT here leaves SSL exactly where it was.
    ///
    /// The bands are the customer's clause and are replaced whole, because they
    /// are what the lane prices index against: keeping old bands beside new ones
    /// would leave prices pointing at positions that have moved.
    /// </summary>
    public async Task<int> SaveCardAsync(CustomerCardInput input, CancellationToken token)
    {
        var customer = input.Customer.Trim();
        var carrier = input.Carrier.Trim();
        if (customer.Length == 0 || carrier.Length == 0) return 0;

        // All of it or none of it, and through the execution strategy.
        //
        // The strategy part is not decoration: this context is configured with
        // EnableRetryOnFailure, and a retrying strategy refuses a transaction
        // somebody opened by hand — it cannot replay half a unit of work it did
        // not start. Opening one directly threw, which is the 500 this endpoint
        // answered with the first time anybody pressed save. RotationService
        // and JobsRepository both already do it this way; I wrote a third
        // version of a rule the codebase had twice, and got it wrong.
        //
        // The transaction itself matters because the old bands are deleted
        // before the new lanes are written. A failure in between leaves a card
        // whose prices index into bands that no longer exist — every figure on
        // it pointing at the wrong step of the fuel clause, with nothing on
        // screen to say so. A half-written rate card is worse than none.
        var saved = 0;
        var strategy = db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var work = await db.Database.BeginTransactionAsync(token);

            var doomed = await db.CustomerRateLanes
                .Where(lane => lane.Customer == customer && lane.Carrier == carrier)
                .Select(lane => lane.Id)
                .ToListAsync(token);

            if (doomed.Count > 0)
            {
                await db.CustomerRatePrices.Where(price => doomed.Contains(price.LaneId))
                    .ExecuteDeleteAsync(token);
                await db.CustomerRateLanes.Where(lane => doomed.Contains(lane.Id))
                    .ExecuteDeleteAsync(token);
            }

            await db.CustomerRateBands.Where(band => band.Customer == customer)
                .ExecuteDeleteAsync(token);

            for (var position = 0; position < input.Bands.Count; position++)
            {
                var band = input.Bands[position];
                db.CustomerRateBands.Add(new CustomerRateBand
                {
                    Customer = customer,
                    Label = band.Label,
                    MinPrice = band.Min,
                    MaxPrice = band.Max,
                    Position = position,
                });
            }

            // Every lane in one write, then every price in one more.
            //
            // This used to save inside the loop, twice per lane, which is a
            // round trip to Azure SQL each time: three hundred and eighteen of
            // them for a card of a hundred and fifty-nine lanes. EF hands back
            // the generated ids after one SaveChanges, so the prices can be
            // attached straight afterwards.
            var rows = input.Lanes.Select(lane => new CustomerRateLane
            {
                Customer = customer,
                Carrier = carrier,
                FromPlace = lane.From,
                ToPlace = lane.To,
                PostalCode = lane.PostalCode,
            }).ToList();
            db.CustomerRateLanes.AddRange(rows);
            await db.SaveChangesAsync(token);

            // Reset inside the body: a retry runs all of this again, and a
            // counter that kept its previous total would report twice what it
            // wrote.
            saved = 0;
            for (var index = 0; index < rows.Count; index++)
            {
                foreach (var (vehicle, prices) in input.Lanes[index].Prices)
                {
                    for (var position = 0; position < prices.Length; position++)
                    {
                        if (prices[position] is not { } price) continue;
                        db.CustomerRatePrices.Add(new CustomerRatePrice
                        {
                            LaneId = rows[index].Id,
                            Vehicle = vehicle,
                            BandPosition = position,
                            Price = price,
                        });
                        saved++;
                    }
                }
            }
            await db.SaveChangesAsync(token);
            await work.CommitAsync(token);
        });

        return saved;
    }

    public async Task<List<string>> CustomersWithCardsAsync(CancellationToken token) =>
        await db.CustomerRateLanes.AsNoTracking()
            .Select(lane => lane.Customer).Distinct().OrderBy(name => name)
            .ToListAsync(token);

    /* ------------------------------------------------------- cargo forms */

    public async Task<List<CargoTemplateView>> ReadTemplatesAsync(CancellationToken token)
    {
        var rows = await db.CargoFormTemplates.AsNoTracking()
            .OrderBy(template => template.Customer)
            .ToListAsync(token);

        return rows.Select(template => new CargoTemplateView(
            template.Customer, template.SourceFile,
            // Stored tab separated because that is what the columns are: an
            // ordered row of headings. A blank heading is a real column on some
            // of these forms, so empties are kept rather than filtered away.
            template.Columns.Split('\t').ToList())).ToList();
    }

    /// <summary>
    /// Replaces the whole set of form templates.
    ///
    /// Whole, unlike the rates: these arrive as a folder, and a customer whose
    /// file has been withdrawn should stop being offered. Uploading the folder
    /// is the act of saying "this is the set", which is not what saving one
    /// haulier's price card says.
    /// </summary>
    public async Task<int> SaveTemplatesAsync(List<CargoTemplateInput> input, CancellationToken token)
    {
        var clean = input
            .Where(template => template.Customer.Trim().Length > 0 && template.Columns.Count > 0)
            .GroupBy(template => template.Customer.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();

        if (clean.Count == 0) return 0;

        // Same shape as the rate save, and the same two reasons: the old set is
        // cleared before the new one lands, so a failure in between would leave
        // no forms at all; and this context retries on failure, which means a
        // hand-opened transaction is refused rather than replayed.
        var strategy = db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var work = await db.Database.BeginTransactionAsync(token);
            await db.CargoFormTemplates.ExecuteDeleteAsync(token);
            db.CargoFormTemplates.AddRange(clean.Select(template => new CargoFormTemplate
            {
                Customer = template.Customer.Trim(),
                SourceFile = template.SourceFile,
                Columns = string.Join('\t', template.Columns),
            }));
            await db.SaveChangesAsync(token);
            await work.CommitAsync(token);
        });
        return clean.Count;
    }
}
