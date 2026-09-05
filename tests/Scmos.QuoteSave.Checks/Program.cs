using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Scmos.Api.Auth;
using Scmos.Api.Data;
using Scmos.Api.Rules;
using Scmos.Api.Services;

var checks = 0;
void Check(bool value, string label)
{
    if (!value) throw new InvalidOperationException("FAIL: " + label);
    Console.WriteLine("PASS: " + label);
    checks++;
}
var card = new QuoteCardView([
    new(1, "4W", "4WH", 8, 1500, 1m, 300, 0),
    new(2, "6W", "6WH", 18, 2700, 1m, 500, 1),
    new(3, "4W RF", "4WH RF", 8, 1500, 1.5m, 300, 2),
    new(4, "20RF", "20' RF", 40, 4000, 1.5m, 500, 3),
], [new(1, "Waiting", QuoteBasis.PerHour, 250m, true, 0),
    new(2, "Fuel", QuoteBasis.Percent, 5m, true, 1),
    new(3, "Other", QuoteBasis.Percent, 10m, true, 2)], 10, "", "");
QuoteSaveBody Ask() => new(Guid.NewGuid().ToString(), "Local test origin", "Local test destination",
    "LOCAL QUOTE TEST", true, false, false, "", 119m, false, 10m,
    ["4W", "6W"], [], new() { ["4W"] = 2697, ["6W"] = 5326 });
var original = Ask();
var calculated = QuoteCalculation.Calculate(card, original);
Check(calculated.Error == "", "two truck types calculated together");
Check(calculated.Prices["4W"] == 2697 && calculated.Prices["6W"] == 5326, "round lines and margin before summing");
Check(QuoteCalculation.MatchesPreview(calculated, original.ExpectedTotals), "preview matches authoritative price");
Check(!QuoteCalculation.MatchesPreview(calculated, new() { ["4W"] = 1, ["6W"] = 5326 }), "changed or forged price refused");
Check(!QuoteCalculation.MatchesPreview(calculated, new() { ["4W"] = 2697 }), "partial preview refused");
var dg = QuoteCalculation.Calculate(card, original with { DangerousGoods = true });
Check(dg.Prices.ContainsKey("4W DG") && !dg.Prices.ContainsKey("4W"), "DG stays in the DG column");
var extra = QuoteCalculation.Calculate(card, original with { Options = [new(1, 2), new(2, 1), new(3, 1)] });
Check(extra.Prices["4W"] == 3735, "percent extras share the same pre-percent cost (2952 + 148 + 295 + 340)");
var reef = QuoteCalculation.Calculate(card, original with { Vehicles = ["4W RF"], DangerousGoods = true });
Check(reef.Prices["4W RF DG"] == 4376, "reefer uplift excludes DG surcharge");
Check(QuoteCalculation.Calculate(card, original with { Vehicles = ["4W", "20RF"], DangerousGoods = true }).Prices.Count == 0, "unsupported DG mapping refuses all, not a partial row");
foreach (var invalid in new[] { original with { Km = 0 }, original with { Km = 3001 },
    original with { MarginPercent = -1 }, original with { MarginPercent = 101 },
    original with { Vehicles = [] }, original with { Vehicles = ["not-a-truck"] },
    original with { Options = [new(99, 1)] }, original with { Options = [new(1, -1)] },
    original with { Options = [new(1, 1), new(1, 2)] } })
    Check(QuoteCalculation.Calculate(card, invalid).Error.Length > 0, "invalid selection/input refused");
Check(QuoteCalculation.DateAt(DateTimeOffset.Parse("2026-09-05T16:59:59Z")) == "05/09/2026", "Thai day before midnight");
Check(QuoteCalculation.DateAt(DateTimeOffset.Parse("2026-09-05T17:00:00Z")) == "06/09/2026", "Thai day after midnight, not UTC day");
Check(QuoteCalculation.DateAt(DateTimeOffset.Parse("2026-12-31T17:00:00Z")) == "01/01/2027", "Thai year boundary");

// Opt-in integration tests. The connection cannot be overridden with a real
// server: this runner only creates a randomly named, disposable LocalDB database.
if (args.Contains("--local-db"))
{
    var database = "ScmosQuoteSaveTests_" + Guid.NewGuid().ToString("N");
    var instance = args.Contains("--isolated") ? "ScmosQuoteCheck_20260905" : "MSSQLLocalDB";
    var connection = $"Server=(localdb)\\{instance};Database={database};Integrated Security=true;TrustServerCertificate=true";
    ScmosDbContext Open() => new(new DbContextOptionsBuilder<ScmosDbContext>()
        .UseSqlServer(connection, sql => sql.EnableRetryOnFailure()).Options);
    var user = new AppUser("quote-test-user", "quote-test@example.invalid", "Quote test", "Administrator", "OP-TEST", "test", true);
    async Task<QuoteSaveOutcome> Save(QuoteSaveBody body)
    {
        await using var db = Open();
        var audit = new AuditService(db, new HttpContextAccessor(), NullLogger<AuditService>.Instance);
        return await new QuoteSheetService(db, new QuoteCardService(db), new RateInquiryService(db), audit)
            .SaveAsync(user, body, CancellationToken.None);
    }
    await using var setup = Open();
    try
    {
        await setup.Database.EnsureCreatedAsync();
        // Seed the real editable card, then derive the preview from those rows.
        var actualCard = await new QuoteCardService(setup).ReadAsync(CancellationToken.None);
        var request = Ask() with { Fcl = false, Domestic = true };
        request = request with { ExpectedTotals = QuoteCalculation.Calculate(actualCard, request).Totals };
        var saved = await Save(request);
        Check(saved.Status == 200 && saved.Receipt?.Number == 1, "save returns first automatic NO");
        var row = await setup.RateInquiries.AsNoTracking().SingleAsync();
        var lane = await setup.RateInquiryLanes.AsNoTracking().SingleAsync();
        Check(row.InquiredOn == QuoteCalculation.DateAt(DateTimeOffset.UtcNow) && row.Requestor == user.DisplayName, "DATE and requestor saved from server/user");
        Check(lane.Domestic && !lane.Fcl && !lane.Lcl, "Domestic-only load persists");
        Check(await setup.RateInquiryPrices.CountAsync() == 2, "all selected price columns persist");
        var replay = await Save(request);
        Check(replay.Status == 200 && replay.Replayed && replay.Receipt?.Id == saved.Receipt?.Id, "retry returns same saved receipt");
        Check(await setup.RateInquiries.CountAsync() == 1, "retry adds no duplicate row");
        Check((await Save(request with { Km = 120 })).Status == 409, "same request token with different content rejected");
        Check((await Save(request with { RequestId = Guid.NewGuid().ToString(), ExpectedTotals = new() { ["4W"] = 1 } })).Status == 409, "server rejects stale preview before any insertion");
        Check(await setup.RateInquiries.CountAsync() == 1, "refused save leaves no partial inquiry");
        var simultaneous = request with { RequestId = Guid.NewGuid().ToString() };
        var repeated = await Task.WhenAll(Save(simultaneous), Save(simultaneous));
        Check(repeated.All(one => one.Status == 200) && repeated.Select(one => one.Receipt!.Id).Distinct().Count() == 1, "concurrent identical requests save once");
        var different = await Task.WhenAll(Save(request with { RequestId = Guid.NewGuid().ToString() }),
            Save(request with { RequestId = Guid.NewGuid().ToString() }));
        Check(different.All(one => one.Status == 200) && different.Select(one => one.Receipt!.Number).Distinct().Count() == 2, "concurrent new requests get distinct automatic NO");
        Check(await setup.RateInquiries.CountAsync() == 4 && await setup.RateInquiryPrices.CountAsync() == 8, "four whole inquiries, eight prices, no partial rows");
        Check(await setup.AuditEvents.CountAsync() == 4, "each new save has exactly one audit receipt");
        var farther = request with { FromPlace = "Second origin", ToPlace = "Second destination", Km = 200 };
        var routeSet = request with { RequestId = Guid.NewGuid().ToString(), Routes = [
            new(request.FromPlace, request.ToPlace, request.Km, request.ExpectedTotals),
            new(farther.FromPlace, farther.ToPlace, farther.Km, QuoteCalculation.Calculate(actualCard, farther).Totals),
        ] };
        var multiple = await Save(routeSet);
        Check(multiple.Status == 200 && multiple.Receipt?.RouteCount == 2 && multiple.Receipt.Count == 4, "two routes times two trucks saved in one request");
        var routeLanes = await setup.RateInquiryLanes.AsNoTracking().Where(one => one.InquiryId == multiple.Receipt!.Id).OrderBy(one => one.Id).ToListAsync();
        Check(routeLanes.Count == 2 && routeLanes[0].FromPlace == request.FromPlace && routeLanes[1].FromPlace == farther.FromPlace, "distinct route endpoints persist as separate rows");
        var laneIds = routeLanes.Select(one => one.Id).ToList();
        var storedPrices = await setup.RateInquiryPrices.AsNoTracking().Where(one => laneIds.Contains(one.LaneId)).ToListAsync();
        Check(storedPrices.Single(one => one.LaneId == routeLanes[0].Id && one.Vehicle == "4W").Price == 2697 &&
            storedPrices.Single(one => one.LaneId == routeLanes[1].Id && one.Vehicle == "4W").Price == 3410, "each route price uses its own distance");
        var sheetPage = await new RateInquiryService(setup).SheetAsync(new("", "", "", "", "", "", "", "", 1, 50), CancellationToken.None);
        var displayed = sheetPage.Rows.Where(one => one.InquiryId == multiple.Receipt!.Id).ToList();
        Check(displayed.Count == 2 && displayed.Select(one => one.No).Distinct().Count() == 1 && displayed.Select(one => one.Date).Distinct().Count() == 1, "Rate Sheet reads both rows with shared DATE and NO");
        var badSet = routeSet with { RequestId = Guid.NewGuid().ToString(), Routes = [routeSet.Routes![0], routeSet.Routes[1] with { Km = 0 }] };
        Check((await Save(badSet)).Status == 400 && await setup.RateInquiries.CountAsync() == 5, "invalid second route saves no first route either");
        Check((await Save(routeSet)).Replayed && await setup.RateInquiryLanes.CountAsync() == 6, "multi-route retry cannot duplicate any route");
        Check((await Save(request with { Routes = [] })).Status == 400, "empty route set refused");
    }
    finally
    {
        // Only the exact database this runner created can be removed.
        if (setup.Database.GetDbConnection().Database != database || !database.StartsWith("ScmosQuoteSaveTests_"))
            throw new InvalidOperationException("Unexpected test database target");
        await setup.Database.EnsureDeletedAsync();
        Console.WriteLine("Disposable local test database removed: " + database);
    }
}
Console.WriteLine($"{checks} quotation checks passed.");
