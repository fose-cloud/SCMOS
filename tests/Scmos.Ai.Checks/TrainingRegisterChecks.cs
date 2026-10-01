using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Scmos.Api.Auth;
using Scmos.Api.Data;
using Scmos.Api.Endpoints;
using Scmos.Api.Rules;
using Scmos.Api.Services;

/// <summary>
/// Customer Training Control's company column (30 Sep 2026): chosen from the supplier register — name, code,
/// legal name or alias — and linked by id, so a Subcontractor's own Training Control finds its rows; a carrier
/// reads and writes its own company's rows only. Three suppliers in a throwaway LocalDB.
/// </summary>
static class TrainingRegisterChecks
{
    public static async Task RunAsync(Action<bool, string> check, bool sql)
    {
        check(CarrierBoundary.Allows("GET", new PathString("/api/carrier/training/register"))
            && CarrierBoundary.Allows("PUT", new PathString("/api/carrier/training/register/12"))
            && CarrierBoundary.Allows("POST", new PathString("/api/carrier/training/register/import"))
            && !CarrierBoundary.Allows("GET", new PathString("/api/training/register"))
            && !CarrierBoundary.Allows("POST", new PathString("/api/training/register/import")),
            "training: a carrier reaches its own register routes and never the department's whole register");

        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "tests", "fixtures", "supplier-keys.json"))) root = root.Parent;
        check(root is not null, "supplier keys: the shared fixture is found");
        if (root is not null)
        {
            using var fixture = JsonDocument.Parse(File.ReadAllText(Path.Combine(root.FullName, "tests", "fixtures", "supplier-keys.json")));
            check(fixture.RootElement.GetProperty("keys").EnumerateArray()
                    .All(one => SupplierRegister.Key(one.GetProperty("input").GetString()!) == one.GetProperty("key").GetString()),
                "supplier keys: the API reduces a company name to the key the screens do (supplier-keys.json) — Thai letters kept");
        }
        if (sql) await SqlAsync(check);
    }

    private static int Status(IResult result) => (result as IStatusCodeHttpResult)?.StatusCode ?? 200;

    private static TrainingEndpoints.RegisterBody Row(string company, string first, string licence, string course = "ALLNEX") =>
        new("1", course, first, "ทดสอบ", company, licence, "3", "21/11/2025", "20/11/2026");

    private static async Task<List<JsonElement>> RowsAsync(Supplier? carrier, ScmosDbContext db, SupplierNames names)
    {
        var read = await TrainingEndpoints.ReadRegisterAsync(carrier, db, names, default);
        return JsonDocument.Parse(JsonSerializer.Serialize(read, new JsonSerializerOptions(JsonSerializerDefaults.Web)))
            .RootElement.GetProperty("rows").EnumerateArray().ToList();
    }

    private static async Task SqlAsync(Action<bool, string> check)
    {
        var database = "SCMOS_TRAINING_TEST_" + Guid.NewGuid().ToString("N");
        var connection = $"Server=(localdb)\\MSSQLLocalDB;Database={database};Integrated Security=true;TrustServerCertificate=true";
        var options = new DbContextOptionsBuilder<ScmosDbContext>().UseSqlServer(connection,
            s => { s.UseCompatibilityLevel(150); s.EnableRetryOnFailure(3); }).Options;
        await using var db = new ScmosDbContext(options);
        await db.Database.EnsureCreatedAsync();
        try
        {
            var now = DateTimeOffset.UtcNow;
            Supplier Company(string name, string code, string legal, string status = "approved") => new()
                { Name = name, Code = code, LegalName = legal, Status = status, IsCarrier = true, CreatedAt = now, UpdatedAt = now };
            var alpha = Company("ALPHA TRANSPORT", "ALP", "Alpha Transport Co., Ltd.");
            // Bravo's legal name reduces to the same key as one of Alpha's aliases: that spelling means neither.
            var bravo = Company("BRAVO LOGISTICS", "BRV", "Shared");
            var charlie = Company("CHARLIE CARGO", "CHC", "Charlie Cargo Co., Ltd.", status: "draft");
            db.Suppliers.AddRange(alpha, bravo, charlie);
            await db.SaveChangesAsync();
            db.SupplierAliases.AddRange(
                new SupplierAlias { SupplierId = alpha.Id, Alias = "ALPHA", Source = "test", Confirmed = true },
                new SupplierAlias { SupplierId = alpha.Id, Alias = "SHARED", Source = "test", Confirmed = true });
            // Written before the column held an id: one under Alpha's alias, one under nobody the register knows.
            db.CustomerTrainingRecords.AddRange(
                new CustomerTrainingRecord { SequenceNo = "1", CourseCustomer = "ALLNEX", FirstName = "เก่า", LastName = "อัลฟา", Company = "Alpha",
                    DriverLicenseNo = "OLD-A", EffectiveDate = "21/11/2025", ExpiryDate = "20/11/2026", CreatedAt = now, UpdatedAt = now },
                new CustomerTrainingRecord { SequenceNo = "2", CourseCustomer = "ALLNEX", FirstName = "เก่า", LastName = "ไม่รู้จัก", Company = "Wealthy Old Name",
                    DriverLicenseNo = "OLD-W", EffectiveDate = "21/11/2025", ExpiryDate = "20/11/2026", CreatedAt = now, UpdatedAt = now });
            await db.SaveChangesAsync();

            var names = new SupplierNames(db);
            var resolve = await names.ApprovedAsync(default);
            check(resolve("alpha transport co., ltd.")?.Id == alpha.Id && resolve(" ALP ")?.Id == alpha.Id && resolve("Alpha")?.Id == alpha.Id
                && resolve("BRAVO LOGISTICS")?.Name == "BRAVO LOGISTICS" && resolve("Charlie Cargo Co., Ltd.") is null
                && resolve("SHARED") is null && resolve("") is null && resolve("Nobody") is null,
                "training: a company is an approved supplier by name, code, legal name or alias — a spelling two share means neither");

            var audit = new AuditService(db, new HttpContextAccessor(), NullLogger<AuditService>.Instance);
            var staff = new AppUser("o", "o@test.invalid", "O", Roles.Operation, "OP-C1", "test", true);
            var viewer = new AppUser("v", "v@test.invalid", "V", Roles.Viewer, "V-1", "test", true);
            var carrierUser = new AppUser("s", "s@carrier.test", "S", Roles.Subcontractor, "SUB-B", "test", true);

            var legal = await TrainingEndpoints.SaveRegisterAsync(null, Row("Alpha Transport Co., Ltd.", "สมชาย", "L-1"), staff, null, db, audit, names, default);
            var unknown = await TrainingEndpoints.SaveRegisterAsync(null, Row("Nobody Co", "สมศักดิ์", "L-2"), staff, null, db, audit, names, default);
            var blank = await TrainingEndpoints.SaveRegisterAsync(null, Row("", "สมศรี", "L-3"), staff, null, db, audit, names, default);
            var draft = await TrainingEndpoints.SaveRegisterAsync(null, Row("CHARLIE CARGO", "สมใจ", "L-4"), staff, null, db, audit, names, default);
            var noRight = await TrainingEndpoints.SaveRegisterAsync(null, Row("ALPHA", "สมปอง", "L-5"), viewer, null, db, audit, names, default);
            var saved = await db.CustomerTrainingRecords.AsNoTracking().SingleAsync(row => row.DriverLicenseNo == "L-1");
            check(Status(legal) == 200 && saved.Company == "ALPHA TRANSPORT" && saved.SupplierId == alpha.Id
                && Status(unknown) == 400 && Status(blank) == 400 && Status(draft) == 400 && Status(noRight) == 403
                && await db.CustomerTrainingRecords.CountAsync() == 3,
                "training: the department's row names an approved supplier, written as the register names it and linked by id — nothing else is saved");

            var old = await db.CustomerTrainingRecords.AsNoTracking().SingleAsync(row => row.DriverLicenseNo == "OLD-W");
            var keepOld = await TrainingEndpoints.SaveRegisterAsync(old.Id,
                Row("Wealthy Old Name", "เก่าแก้แล้ว", "OLD-W") with { LastName = "ไม่รู้จัก", SequenceNo = "2" }, staff, null, db, audit, names, default);
            var moveOld = await TrainingEndpoints.SaveRegisterAsync(old.Id, Row("Another Unknown", "เก่า", "OLD-W"), staff, null, db, audit, names, default);
            var fixOld = await TrainingEndpoints.SaveRegisterAsync(old.Id, Row("BRAVO LOGISTICS", "เก่า", "OLD-W"), staff, null, db, audit, names, default);
            var fixed_ = await db.CustomerTrainingRecords.AsNoTracking().SingleAsync(row => row.Id == old.Id);
            check(Status(keepOld) == 200 && Status(moveOld) == 400 && Status(fixOld) == 200
                && fixed_.Company == "BRAVO LOGISTICS" && fixed_.SupplierId == bravo.Id,
                "training: a row off the register keeps its company while other cells change, may not move to another unknown one, and links once one is picked");

            var carrierWrite = await TrainingEndpoints.SaveRegisterAsync(null, Row("ALPHA TRANSPORT", "บราโว่", "L-B1"), carrierUser, bravo, db, audit, names, default);
            var written = await db.CustomerTrainingRecords.AsNoTracking().SingleAsync(row => row.DriverLicenseNo == "L-B1");
            var alphaRows = await RowsAsync(alpha, db, names);
            var bravoRows = await RowsAsync(bravo, db, names);
            var foreign = await TrainingEndpoints.SaveRegisterAsync(saved.Id, Row("BRAVO LOGISTICS", "ยึด", "L-1"), carrierUser, bravo, db, audit, names, default);
            var own = await TrainingEndpoints.SaveRegisterAsync(written.Id, Row("", "บราโว่แก้", "L-B1"), carrierUser, bravo, db, audit, names, default);
            check(Status(carrierWrite) == 200 && written.Company == "BRAVO LOGISTICS" && written.SupplierId == bravo.Id
                && alphaRows.Select(row => row.GetProperty("driverLicenseNo").GetString()).Order().SequenceEqual(["L-1", "OLD-A"])
                && bravoRows.Select(row => row.GetProperty("driverLicenseNo").GetString()).Order().SequenceEqual(["L-B1", "OLD-W"])
                && Status(foreign) == 404 && Status(own) == 200
                && (await db.CustomerTrainingRecords.AsNoTracking().SingleAsync(row => row.Id == saved.Id)).FirstName == "สมชาย",
                "training: a carrier's rows are its own company's, older ones found by its names; what it writes is its own whatever the body says, and another's row is not there");

            var imported = await TrainingEndpoints.ImportRegisterAsync(new TrainingEndpoints.RegisterImportBody(
                [Row("ALPHA", "นำเข้า1", "I-1"), Row("Nobody Co", "นำเข้า2", "I-2"), Row("", "นำเข้า3", "I-3")]), staff, null, db, audit, names, default);
            var carrierImport = await TrainingEndpoints.ImportRegisterAsync(new TrainingEndpoints.RegisterImportBody(
                [Row("ALPHA TRANSPORT", "นำเข้าบราโว่", "I-4")]), carrierUser, bravo, db, audit, names, default);
            var i1 = await db.CustomerTrainingRecords.AsNoTracking().SingleAsync(row => row.DriverLicenseNo == "I-1");
            var i4 = await db.CustomerTrainingRecords.AsNoTracking().SingleAsync(row => row.DriverLicenseNo == "I-4");
            check(Status(imported) == 200 && Status(carrierImport) == 200
                && i1.Company == "ALPHA TRANSPORT" && i1.SupplierId == alpha.Id
                && !await db.CustomerTrainingRecords.AnyAsync(row => row.DriverLicenseNo == "I-2" || row.DriverLicenseNo == "I-3")
                && i4.Company == "BRAVO LOGISTICS" && i4.SupplierId == bravo.Id
                && await db.AuditEvents.CountAsync(row => row.Entity == "customer-training-register") == 7,
                "training: an import takes the rows whose company the register knows and reports the rest; a carrier's import is its own company's; every write is audited");
        }
        finally
        {
            await db.Database.EnsureDeletedAsync();
        }
    }
}
