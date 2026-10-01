using Microsoft.EntityFrameworkCore;
using Scmos.Api.Data;
using Scmos.Api.Rules;
using Scmos.Api.Services;

/// <summary>
/// A rate lane's Job Rotation customer (1 Oct 2026): picked in Rate Management, or named by the lane's own
/// Customer or From text; and Billing's Rate price taken only from a lane of the job's own customer or of none.
/// One carrier's lanes in a throwaway LocalDB.
/// </summary>
static class RateCustomerChecks
{
    public static async Task RunAsync(Action<bool, string> check, bool sql)
    {
        var rotation = new Dictionary<string, string> { ["ALLNEX"] = "ALLNEX", ["SCG"] = "SCG" };
        check(LaneCustomer.Of(null, "Allnex ", "LCB", rotation) == ("ALLNEX", LaneCustomer.Matched)
            && LaneCustomer.Of(null, "LCB-PORT", " allnex", rotation) == ("ALLNEX", LaneCustomer.Matched)
            && LaneCustomer.Of(null, "ALLNEX (MAP TA PHUT)", "LCB", rotation) == ("", "")
            && LaneCustomer.Of("", "ALLNEX", "LCB", rotation) == ("", LaneCustomer.General)
            && LaneCustomer.Of("SCG", "ALLNEX", "LCB", rotation) == ("SCG", LaneCustomer.Picked),
            "rate customer: a picked link wins; until one is, a lane's Customer or From text names a rotation customer only exactly");
        check(LaneCustomer.Serves("", "BASF") && LaneCustomer.Serves("ALLNEX", " allnex ") && !LaneCustomer.Serves("ALLNEX", "SCG"),
            "rate customer: a lane of no customer serves any job; a customer's lane serves only that customer's");
        if (sql) await SqlAsync(check);
    }

    private static OperationJob Job(string key, string customer, string destination) => new()
    {
        Key = key, Cat = "IMPORT", Customer = customer, Trucker = "ALPHA", Status = "COMPLETED", WorkDate = "15/09/2026",
        Data = $$"""{"key":"{{key}}","cat":"IMPORT","date":"15/09/2026","customer":"{{customer}}","destination":"{{destination}}","type":"1X40'","diesel":"31"}""",
    };

    private static async Task SqlAsync(Action<bool, string> check)
    {
        var database = "SCMOS_RATE_CUSTOMER_TEST_" + Guid.NewGuid().ToString("N");
        var connection = $"Server=(localdb)\\MSSQLLocalDB;Database={database};Integrated Security=true;TrustServerCertificate=true";
        var options = new DbContextOptionsBuilder<ScmosDbContext>().UseSqlServer(connection,
            s => { s.UseCompatibilityLevel(150); s.EnableRetryOnFailure(3); }).Options;
        await using var db = new ScmosDbContext(options);
        await db.Database.EnsureCreatedAsync();
        try
        {
            var now = DateTimeOffset.UtcNow;
            var alpha = new Supplier { Name = "ALPHA TRANSPORT", Code = "ALP", Status = "approved", IsCarrier = true, CreatedAt = now, UpdatedAt = now };
            db.Suppliers.Add(alpha);
            db.FuelBands.AddRange(new FuelBand { Label = "30.00-32.99", MinPrice = 30m, MaxPrice = 32.99m, Position = 0 },
                new FuelBand { Label = "33.00-35.99", MinPrice = 33m, MaxPrice = 35.99m, Position = 1 });
            db.RotationAssignments.AddRange(new RotationAssignment { Customer = "ALLNEX", Sheet = "test", UpdatedAt = now },
                new RotationAssignment { Customer = "SCG", Sheet = "test", UpdatedAt = now },
                new RotationAssignment { Customer = "BASF", Sheet = "test", UpdatedAt = now });
            await db.SaveChangesAsync();
            RateLane Lane(string customer, string to) => new()
                { SupplierId = alpha.Id, Carrier = "ALPHA TRANSPORT", Service = "FCL", Customer = customer, FromPlace = "LCB", ToPlace = to, SourceFile = "test" };
            var allnex = Lane("ALLNEX", "RAYONG");
            var scg = Lane("SCG", "RAYONG");
            var anySaraburi = Lane("—", "SARABURI");
            var anyRayong = Lane("—", "RAYONG");
            db.RateLanes.AddRange(allnex, scg, anySaraburi, anyRayong);
            await db.SaveChangesAsync();
            db.RatePrices.AddRange(new RatePrice { LaneId = allnex.Id, Vehicle = "40F", BandPosition = 0, Price = 5000 },
                new RatePrice { LaneId = scg.Id, Vehicle = "40F", BandPosition = 0, Price = 6000 },
                new RatePrice { LaneId = anySaraburi.Id, Vehicle = "40F", BandPosition = 0, Price = 7000 },
                new RatePrice { LaneId = anyRayong.Id, Vehicle = "40F", BandPosition = 0, Price = 4500 });
            await db.SaveChangesAsync();

            var quotes = await ContractRates.QuoteAsync(db, alpha, [Job("J-ALLNEX", "ALLNEX", "RAYONG"), Job("J-SCG", "SCG", "RAYONG"),
                Job("J-BASF", "BASF", "RAYONG"), Job("J-SCG-SBR", "SCG", "SARABURI")], default);
            check(quotes["J-ALLNEX"].Amount == 5000 && quotes["J-ALLNEX"].Lane.StartsWith("ALLNEX ·") && quotes["J-SCG"].Amount == 6000
                && quotes["J-BASF"].Amount == 4500 && quotes["J-SCG-SBR"].Amount == 7000,
                "rate customer: Billing prices a job from its own customer's lane first, else a lane of none — never another customer's, and never a customer's lane on another road");

            var service = new RateService(db);
            var unknown = await service.SetLaneCustomerAsync(scg.Id, "Nobody Co", default);
            var picked = await service.SetLaneCustomerAsync(anyRayong.Id, " basf ", default);
            var stored = await db.RateLanes.AsNoTracking().SingleAsync(row => row.Id == anyRayong.Id);
            var closed = await ContractRates.QuoteAsync(db, alpha, [Job("J-NEWCO", "NEWCO", "RAYONG"), Job("J-BASF", "BASF", "RAYONG")], default);
            var general = await service.SetLaneCustomerAsync(scg.Id, "", default);
            var opened = await ContractRates.QuoteAsync(db, alpha, [Job("J-NEWCO", "NEWCO", "RAYONG")], default);
            var reset = await service.SetLaneCustomerAsync(scg.Id, null, default);
            var book = await service.ReadAsync(null, null, "carrier", default);
            var scgView = book.Lanes.Single(row => row.Id == scg.Id);
            var basfView = book.Lanes.Single(row => row.Id == anyRayong.Id);
            check(!unknown.Ok && picked.Ok && stored.RotationCustomer == "BASF"
                && closed["J-NEWCO"].Amount is null && closed["J-NEWCO"].Reason == "no-lane" && closed["J-BASF"].Amount == 4500
                && general.Ok && general.After == "(ทุกลูกค้า)" && opened["J-NEWCO"].Amount == 6000
                && reset.Ok && reset.After == "(ตามชื่อในเส้นทาง)"
                && scgView.RotationCustomer == "SCG" && scgView.CustomerLink == LaneCustomer.Matched
                && basfView.RotationCustomer == "BASF" && basfView.CustomerLink == LaneCustomer.Picked,
                "rate customer: a link is picked from Job Rotation only, written as the rotation spells it; a customer's link closes a lane to others, any customer's opens it, and none goes back to the lane's own text");

            var carriers = await service.QuotesForAsync("NEWCO", "RAYONG", "40F", 31m, default);
            check(carriers.Count == 0, "rate customer: the carrier price list for a customer leaves out lanes agreed for another");
        }
        finally
        {
            await db.Database.EnsureDeletedAsync();
        }
    }
}
