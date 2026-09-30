using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Scmos.Api.Auth;
using Scmos.Api.Data;
using Scmos.Api.Rules;
using Scmos.Api.Services;

/// <summary>
/// Capacity Planning's fleet tables (30 Sep 2026): a carrier registers its own heads, tails and drivers with
/// their papers; nobody registers a truck another carrier holds; nothing is registered without every paper; and
/// a carrier opens only its own trucks' and drivers' files. Two carriers in a throwaway LocalDB, files in memory.
/// </summary>
static class FleetChecks
{
    public static async Task RunAsync(Action<bool, string> check, bool sql)
    {
        check(FleetDocuments.Key("70-1234 กทม") == FleetDocuments.Key(" 70 1234กทม ") && FleetDocuments.Key("70-1234 กทม") == "701234กทม"
            && FleetDocuments.Key("3ขบ.00597/63") == "3ขบ0059763" && FleetDocuments.Tidy("  70-1234   กทม ") == "70-1234 กทม",
            "fleet: a plate or licence is one thing however it is spaced or punctuated, Thai letters kept");
        check(FleetDocuments.Truck.Select(need => need.Thai).SequenceEqual(["เล่มทะเบียนรถ", "ประกันรถ", "ประกันสินค้า"])
            && FleetDocuments.Driver.Single().Thai == "ใบขับขี่"
            && !FleetDocuments.Truck[0].Expires && FleetDocuments.Truck.Skip(1).All(need => need.Expires),
            "fleet: a truck carries its registration book and both insurances; a driver, the licence — the department's list");
        check(CarrierBoundary.Allows("GET", new PathString("/api/carrier/fleet"))
            && CarrierBoundary.Allows("POST", new PathString("/api/carrier/fleet/trucks"))
            && CarrierBoundary.Allows("POST", new PathString("/api/carrier/fleet/drivers/4/documents"))
            && !CarrierBoundary.Allows("GET", new PathString("/api/fleet")),
            "fleet: a carrier reaches its own fleet routes and never the department's every-carrier table");
        if (sql) await SqlAsync(check);
    }

    private sealed class MemoryFiles : IFileStore
    {
        public readonly Dictionary<string, byte[]> Blobs = new();
        public bool Configured => true;
        public async Task<string> PutAsync(string objectKey, Stream content, string contentType,
            IDictionary<string, string> metadata, CancellationToken token)
        {
            using var copy = new MemoryStream();
            await content.CopyToAsync(copy, token);
            Blobs[objectKey] = copy.ToArray();
            return "memory://" + objectKey;
        }
        public Task<Stream?> OpenAsync(string objectKey, CancellationToken token) =>
            Task.FromResult<Stream?>(Blobs.TryGetValue(objectKey, out var bytes) ? new MemoryStream(bytes) : null);
    }

    private static IFormFile Pdf(string name)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes("%PDF-1.4 " + name);
        return new FormFile(new MemoryStream(bytes), 0, bytes.Length, "file", name)
            { Headers = new HeaderDictionary(), ContentType = "application/pdf" };
    }

    private static List<PaperInput> TruckPapers(string motorExpiry, string cargoExpiry, bool withCargo = true) =>
    [
        new("truck-registration-book", Pdf("book.pdf"), ""),
        new("truck-motor-insurance", Pdf("motor.pdf"), motorExpiry),
        new("truck-cargo-insurance", withCargo ? Pdf("cargo.pdf") : null, cargoExpiry),
    ];

    private static async Task SqlAsync(Action<bool, string> check)
    {
        var database = "SCMOS_FLEET_TEST_" + Guid.NewGuid().ToString("N");
        var connection = $"Server=(localdb)\\MSSQLLocalDB;Database={database};Integrated Security=true;TrustServerCertificate=true";
        var options = new DbContextOptionsBuilder<ScmosDbContext>().UseSqlServer(connection,
            s => { s.UseCompatibilityLevel(150); s.EnableRetryOnFailure(3); }).Options;
        await using var db = new ScmosDbContext(options);
        await db.Database.EnsureCreatedAsync();
        try
        {
            var now = DateTimeOffset.UtcNow;
            var alpha = new Supplier { Name = "ALPHA TRANSPORT", Code = "ALP", Status = "approved", IsCarrier = true, CreatedAt = now, UpdatedAt = now };
            var bravo = new Supplier { Name = "BRAVO LOGISTICS", Code = "BRV", Status = "approved", IsCarrier = true, CreatedAt = now, UpdatedAt = now };
            db.Suppliers.AddRange(alpha, bravo);
            await db.SaveChangesAsync();
            StaffMember Person(string id, Supplier company) => new()
            {
                Id = id, Email = id.ToLowerInvariant() + "@carrier.test", Name = company.Name + " dispatcher", Account = id.ToLowerInvariant(),
                Role = Roles.Subcontractor, Active = true, SupplierId = company.Id, CreatedBy = "test", CreatedAt = now, UpdatedBy = "test", UpdatedAt = now,
            };
            db.Staff.AddRange(Person("SUB-A", alpha), Person("SUB-B", bravo));
            await db.SaveChangesAsync();
            var alphaUser = new AppUser("a", "sub-a@carrier.test", "Alpha", Roles.Subcontractor, "SUB-A", "test", true);
            var bravoUser = new AppUser("b", "sub-b@carrier.test", "Bravo", Roles.Subcontractor, "SUB-B", "test", true);
            var staffUser = new AppUser("o", "o@test.invalid", "O", Roles.Operation, "OP-C1", "test", true);

            var files = new MemoryFiles();
            var documents = new DocumentService(db, files);
            var fleet = new FleetService(db, documents, new AuditService(db, new HttpContextAccessor(), NullLogger<AuditService>.Instance));
            var access = new CarrierDocumentAccess(db, new CarrierTenantContext(db, NullLogger<CarrierTenantContext>.Instance), documents);
            string Days(int days) => Formats.Now.AddDays(days).ToString("dd/MM/yyyy");

            var head = await fleet.AddTruckAsync(alphaUser, alpha, new TruckInput("70-1234 กทม", "head", "40f", true),
                TruckPapers(Days(200), ""), default);
            var missing = await fleet.AddTruckAsync(alphaUser, alpha, new TruckInput("70-5555", "head", "40F", false),
                TruckPapers(Days(200), "", withCargo: false), default);
            check(head.Ok && head.Id is > 0 && !missing.Ok && missing.Message.Contains("ประกันสินค้า")
                && await db.SupplierTrucks.CountAsync() == 1 && files.Blobs.Count == 3
                && files.Blobs.Keys.All(key => key.StartsWith("SCMOS/Supplier/ALP/Fleet/Truck/70-1234-กทม/")),
                "fleet: a truck is registered with all three papers filed under its company and plate — without one, nothing is written");

            var again = await fleet.AddTruckAsync(alphaUser, alpha, new TruckInput(" 70 1234กทม", "tail", "TRAILER", false), TruckPapers("", ""), default);
            var theirs = await fleet.AddTruckAsync(bravoUser, bravo, new TruckInput("70-1234 กทม", "head", "40F", false), TruckPapers("", ""), default);
            var odd = await fleet.AddTruckAsync(alphaUser, alpha, new TruckInput("70-7777", "cab", "40F", false), TruckPapers("", ""), default);
            var badDate = await fleet.AddTruckAsync(alphaUser, alpha, new TruckInput("70-7777", "head", "40F", false), TruckPapers("2026-12-31", ""), default);
            check(!again.Ok && again.Status == 409 && again.Message.Contains("ของบริษัทแล้ว")
                && !theirs.Ok && theirs.Status == 409 && theirs.Message.Contains("ผู้ขนส่งรายอื่น")
                && !odd.Ok && !badDate.Ok && badDate.Message.Contains("DD/MM/YYYY") && await db.SupplierTrucks.CountAsync() == 1,
                "fleet: one active row per plate anywhere, a head or a tail only, and dates the register can read");

            var tail = await fleet.AddTruckAsync(alphaUser, alpha, new TruckInput("71-0001", "tail", "TRAILER", false),
                TruckPapers(Days(15), Days(-2)), default);
            var driver = await fleet.AddDriverAsync(alphaUser, alpha, new DriverInput("สมชาย ใจดี", "081-2345678", "3ขบ.00597/63"),
                [new PaperInput("driver-licence", Pdf("licence.jpg"), Days(400))], default);
            var noLicence = await fleet.AddDriverAsync(alphaUser, alpha, new DriverInput("สมศักดิ์", "", "1ขบ.00018/65"),
                [new PaperInput("driver-licence", null, "")], default);
            var sameLicence = await fleet.AddDriverAsync(bravoUser, bravo, new DriverInput("อีกคน", "", "3ขบ 00597 63"),
                [new PaperInput("driver-licence", Pdf("l.pdf"), "")], default);
            var alphaView = await fleet.ReadAsync(alpha.Id, default);
            var headRow = alphaView.Trucks.Single(row => row.Id == head.Id);
            var tailRow = alphaView.Trucks.Single(row => row.Id == tail.Id);
            var driverRow = alphaView.Drivers.Single();
            check(tail.Ok && driver.Ok && !noLicence.Ok && !sameLicence.Ok && sameLicence.Status == 409
                && alphaView.Trucks.Select(row => row.Kind).SequenceEqual(["head", "tail"])
                && headRow.Plate == "70-1234 กทม" && headRow.VehicleType == "40F" && headRow.DgCapable
                && headRow.Papers.Select(paper => paper.State).SequenceEqual(["valid", "valid", "no-expiry"]) && headRow.State == "no-expiry"
                && tailRow.Papers[1].State == "expiring" && tailRow.Papers[2].State == "expired" && tailRow.State == "expired"
                && driverRow.LicenceExpiry == Days(400) && driverRow.State == "valid" && driverRow.Papers.Single().File!.FileName == "licence.jpg",
                "fleet: each paper reads as the compliance file does — held, expiring, expired, or held with no date — and the row as its worst");

            var renewed = await fleet.ReplacePaperAsync(alphaUser, alpha, true, head.Id!.Value,
                new PaperInput("truck-cargo-insurance", Pdf("cargo-2027.pdf"), Days(300)), default);
            var foreign = await fleet.ReplacePaperAsync(bravoUser, bravo, true, head.Id.Value,
                new PaperInput("truck-cargo-insurance", Pdf("x.pdf"), ""), default);
            var unknown = await fleet.ReplacePaperAsync(alphaUser, alpha, true, head.Id.Value,
                new PaperInput("driver-licence", Pdf("x.pdf"), ""), default);
            var renewedRow = (await fleet.ReadAsync(alpha.Id, default)).Trucks.Single(row => row.Id == head.Id);
            check(renewed.Ok && !foreign.Ok && foreign.Status == 404 && !unknown.Ok
                && renewedRow.Papers[2].State == "valid" && renewedRow.Papers[2].Versions == 2
                && renewedRow.Papers[2].File!.FileName == "cargo-2027.pdf" && renewedRow.State == "valid"
                && await db.Documents.CountAsync(row => row.TruckId == head.Id) == 4,
                "fleet: a renewed paper is a newer file beside the old one, and only the carrier that holds the truck may add it");

            var headDocument = await db.Documents.AsNoTracking().FirstAsync(row => row.TruckId == head.Id);
            var licenceDocument = await db.Documents.AsNoTracking().FirstAsync(row => row.FleetDriverId == driver.Id);
            check(await access.CanReadAsync(alphaUser, headDocument, default) && await access.CanReadAsync(alphaUser, licenceDocument, default)
                && !await access.CanReadAsync(bravoUser, headDocument, default) && !await access.CanReadAsync(bravoUser, licenceDocument, default)
                && await access.CanReadAsync(staffUser, headDocument, default)
                && headDocument.SupplierId is null && headDocument.Scope == "fleet"
                && !await db.Documents.AnyAsync(row => row.SupplierId != null),
                "fleet: a carrier opens its own trucks' and drivers' papers only, and none of them joins the supplier's compliance file");

            var retired = await fleet.SetActiveAsync(alphaUser, alpha, true, head.Id.Value, false, default);
            var moved = await fleet.AddTruckAsync(bravoUser, bravo, new TruckInput("70-1234 กทม", "head", "40F", false), TruckPapers("", ""), default);
            var back = await fleet.SetActiveAsync(alphaUser, alpha, true, head.Id.Value, true, default);
            var reRegister = await fleet.AddTruckAsync(alphaUser, alpha, new TruckInput("71-0001", "tail", "TRAILER", false), TruckPapers("", ""), default);
            var department = await fleet.ReadAsync(null, default);
            check(retired.Ok && moved.Ok && !back.Ok && back.Status == 409 && !reRegister.Ok
                && department.Trucks.Count == 3 && department.Trucks.Select(row => row.Supplier).Distinct().Count() == 2
                && department.Trucks.Last().Status == "inactive" && department.Drivers.Count == 1
                && await db.AuditEvents.CountAsync(row => row.Entity == "fleet-truck") == 5
                && await db.AuditEvents.CountAsync(row => row.Entity == "fleet-driver") == 1,
                "fleet: a sold truck is retired, not deleted, and may then be registered by its new carrier; the department reads every carrier's; every change is audited");
        }
        finally
        {
            await db.Database.EnsureDeletedAsync();
        }
    }
}
