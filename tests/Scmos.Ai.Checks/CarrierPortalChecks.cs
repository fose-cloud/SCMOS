using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Scmos.Api.Auth;
using Scmos.Api.Data;
using Scmos.Api.Endpoints;
using Scmos.Api.Rules;
using Scmos.Api.Services;

/// <summary>
/// The Subcontractor's own screens (29 Sep 2026): Rate and KPI cut down to the
/// account's company on the server, and the Postpone list read by the
/// workspace's own rule. Two carriers in one database; each must see its own and
/// nothing of the other's.
/// </summary>
static class CarrierPortalChecks
{
    public static async Task RunAsync(Action<bool, string> check, bool sql)
    {
        check(WorkspaceTabs.WasMoved("10/09/2026", "15/09/2026") && !WorkspaceTabs.WasMoved("", "15/09/2026")
            && !WorkspaceTabs.WasMoved("15/09/2026", "15/09/2026"),
            "carrier portal: moved means a first date that is not today's plan date — the workspace's rule, from two fields");
        check(CarrierEndpoints.ValidPeriod("2026", "09", out var september) && september == new Period("2026", "09", "")
            && CarrierEndpoints.ValidPeriod("2026", "", out var whole) && whole.Month == ""
            && CarrierEndpoints.ValidPeriod(null, null, out var current) && current.Year.Length == 4 && current.Month.Length == 2
            && !CarrierEndpoints.ValidPeriod("26", "09", out _) && !CarrierEndpoints.ValidPeriod("2026", "13", out _)
            && !CarrierEndpoints.ValidPeriod("2026", "9", out _) && !CarrierEndpoints.ValidPeriod("2026 OR 1=1", "09", out _),
            "carrier portal: the KPI period is a year and a month (or the whole year), nothing else");
        check(new[] { "/api/kpi", "/api/kpi/measures", "/api/KPI/excel", "/api/suppliers", "/api/suppliers/3", "/api/dashboard/today", "/api/risk" }
                .All(path => CarrierBoundary.Refuses(new Microsoft.AspNetCore.Http.PathString(path)))
            && !new[] { "/api/carrier/kpi", "/api/carrier/rates", "/api/carrier-billing/cases", "/api/kpix", "/api/documents", "/api/me", "/health" }
                .Any(path => CarrierBoundary.Refuses(new Microsoft.AspNetCore.Http.PathString(path))),
            "carrier boundary: the department's KPI, Supplier Register, dashboard and risk refuse a carrier; its own routes do not");

        Dictionary<string, string> Import(params (string Field, string Value)[] set)
        {
            var fields = new Dictionary<string, string> { ["customer"] = "BASF", ["date"] = "02/10/2026", ["type"] = "1X40'", ["destination"] = "LKB" };
            foreach (var (field, value) in set) fields[field] = value;
            return fields;
        }
        check(CarrierJobRequestService.Problem("IMPORT", Import(), "") is null
            && CarrierJobRequestService.Problem("IMPORT", Import(("planTime", "08:30"), ("container", "TEMU5246902")), "call first") is null
            && CarrierJobRequestService.Problem("BULK", Import(), "") is not null
            && CarrierJobRequestService.Problem("IMPORT", Import(("plant", "X")), "") is not null
            && CarrierJobRequestService.Problem("IMPORT", Import(("date", "2026-10-02")), "") is not null
            && CarrierJobRequestService.Problem("IMPORT", Import(("planTime", "8.30am")), "") is not null
            && CarrierJobRequestService.Problem("IMPORT", Import(("destination", "")), "") is not null
            && CarrierJobRequestService.Problem("IMPORT", Import(("customer", new string('x', 121))), "") is not null
            && CarrierJobRequestService.Problem("IMPORT", Import(), new string('x', 501)) is not null,
            "carrier requests: the add-job form's own fields for the category, its essentials, dates and times as the form writes them");
        check(!CarrierJobRequestService.CanReview(new AppUser("s", "s@c.test", "S", Roles.Subcontractor, "SUB-A", "test", true))
            && CarrierJobRequestService.CanReview(new AppUser("o", "o@test.invalid", "O", Roles.Operation, "OP-C1", "test", true)),
            "carrier requests: a carrier never settles a request; someone in the department who may add a job does");
        if (sql) await SqlAsync(check);
    }

    private sealed class NoFiles : IFileStore
    {
        public bool Configured => false;
        public Task<string> PutAsync(string objectKey, Stream content, string contentType, IDictionary<string, string> metadata, CancellationToken token) =>
            throw new InvalidOperationException("no file store in checks");
        public Task<Stream?> OpenAsync(string objectKey, CancellationToken token) => Task.FromResult<Stream?>(null);
    }

    private static JsonObject Job(string key, string trucker, params (string Field, string Value)[] set)
    {
        var job = new JsonObject
        {
            ["key"] = key, ["cat"] = "IMPORT", ["opId"] = "OP-C1", ["op"] = "Operator", ["date"] = "15/09/2026", ["planTime"] = "08:00",
            ["status"] = "COMPLETED", ["trucker"] = trucker, ["licence"] = "70-1111", ["driver"] = "Somchai", ["contact"] = "081-2345678",
            ["customer"] = "BASF", ["type"] = "1X40'", ["container"] = "TEMU5246902", ["arrDate"] = "15/09/2026", ["arrTime"] = "07:50",
            ["jobCode"] = "J-" + key, ["destination"] = "LKB", ["origDate"] = "",
        };
        foreach (var (field, value) in set) job[field] = value;
        return job;
    }

    private static async Task SqlAsync(Action<bool, string> check)
    {
        var database = "SCMOS_CARRIER_PORTAL_TEST_" + Guid.NewGuid().ToString("N");
        var connection = $"Server=(localdb)\\MSSQLLocalDB;Database={database};Integrated Security=true;TrustServerCertificate=true";
        var options = new DbContextOptionsBuilder<ScmosDbContext>().UseSqlServer(connection,
            s => { s.UseCompatibilityLevel(150); s.EnableRetryOnFailure(3); }).Options;
        await using var setup = new ScmosDbContext(options);
        await setup.Database.EnsureCreatedAsync();
        try
        {
            var now = new DateTimeOffset(2026, 9, 29, 3, 0, 0, TimeSpan.Zero);
            var alpha = new Supplier { Name = "ALPHA TRANSPORT", Code = "ALP", Status = "approved", IsCarrier = true, CreatedAt = now, UpdatedAt = now };
            var bravo = new Supplier { Name = "BRAVO LOGISTICS", Code = "BRV", Status = "approved", IsCarrier = true, CreatedAt = now, UpdatedAt = now };
            setup.Suppliers.AddRange(alpha, bravo);
            await setup.SaveChangesAsync();
            setup.SupplierAliases.Add(new SupplierAlias { SupplierId = alpha.Id, Alias = "ALPHA", Source = "test", Confirmed = true });
            StaffMember Person(string id, Supplier company) => new()
            {
                Id = id, Email = id.ToLowerInvariant() + "@carrier.test", Name = company.Name + " dispatcher", Account = id.ToLowerInvariant(),
                Role = Roles.Subcontractor, Active = true, SupplierId = company.Id, CreatedBy = "test", CreatedAt = now, UpdatedBy = "test", UpdatedAt = now,
            };
            setup.Staff.AddRange(Person("SUB-A", alpha), Person("SUB-B", bravo));
            setup.FuelBands.AddRange(new FuelBand { Label = "30.00-32.99", MinPrice = 30m, MaxPrice = 32.99m, Position = 0 },
                new FuelBand { Label = "33.00-35.99", MinPrice = 33m, MaxPrice = 35.99m, Position = 1 });
            RateLane Lane(int? supplier, string carrier, string customer, string to) => new()
                { SupplierId = supplier, Carrier = carrier, Service = "FCL", Customer = customer, FromPlace = "LCB", ToPlace = to, SourceFile = "test" };
            var lanes = new[]
            {
                Lane(alpha.Id, "ALPHA TRANSPORT", "BASF", "RAYONG-A1"),
                Lane(null, "ALPHA", "SCG", "SARABURI-A2"),
                Lane(bravo.Id, "BRAVO LOGISTICS", "BASF", "RAYONG-B1"),
                Lane(null, "BRAVO LOGISTICS", "SCG", "CHONBURI-B2"),
                // Written against Bravo under Alpha's spelling: the supplier it was written against decides.
                Lane(bravo.Id, "ALPHA", "LOTUS", "AYUTTHAYA-B3"),
            };
            setup.RateLanes.AddRange(lanes);
            await setup.SaveChangesAsync();
            foreach (var lane in lanes)
                setup.RatePrices.AddRange(new RatePrice { LaneId = lane.Id, Vehicle = "1X40'", BandPosition = 0, Price = 5000 + (int)lane.Id },
                    new RatePrice { LaneId = lane.Id, Vehicle = "1X40'", BandPosition = 1, Price = 5100 + (int)lane.Id });

            OperationJob Stored(JsonObject job) => new()
            {
                Key = job["key"]!.GetValue<string>(), Cat = "IMPORT", Owner = "Operator", OwnerId = "OP-C1", WorkDate = "15/09/2026",
                Customer = "BASF", Trucker = job["trucker"]!.GetValue<string>(), JobCode = job["jobCode"]!.GetValue<string>(),
                Status = job["status"]!.GetValue<string>(), Data = job.ToJsonString(), UpdatedBy = "test", UpdatedAt = now,
            };
            setup.OperationJobs.AddRange(
                Stored(Job("A-ON-1", "ALPHA")), Stored(Job("A-ON-2", "ALPHA TRANSPORT")),
                Stored(Job("A-LATE", "ALPHA", ("arrTime", "09:30"))),
                Stored(Job("A-MOVED", "ALPHA", ("status", "SUPPLIER_CONFIRMED"), ("origDate", "10/09/2026"), ("arrDate", ""), ("arrTime", ""))),
                Stored(Job("A-CANCEL", "ALPHA", ("status", "CANCELLED"), ("arrDate", ""), ("arrTime", ""))),
                Stored(Job("B-LATE-1", "BRAVO LOGISTICS", ("arrTime", "10:00"))),
                Stored(Job("B-LATE-2", "BRAVO LOGISTICS", ("arrTime", "10:00"))),
                Stored(Job("B-LATE-3", "BRV", ("arrTime", "10:00"), ("origDate", "01/09/2026"))));
            await setup.SaveChangesAsync();

            await using var db = new ScmosDbContext(options);
            var audit = new AuditService(db, new HttpContextAccessor(), NullLogger<AuditService>.Instance);
            var cache = new MemoryCache(new MemoryCacheOptions());
            var register = new JobRegisterCache(db, cache, NullLogger<JobRegisterCache>.Instance);
            var tenants = new CarrierTenantContext(db, NullLogger<CarrierTenantContext>.Instance);
            var billing = new CarrierBillingService(db, new BusinessCalendarService(db), tenants, new DocumentService(db, new NoFiles()),
                audit, new BillingValidationService(db, audit));
            var carriers = new CarrierService(db, new JobsRepository(db, register), register, tenants, audit, billing,
                NullLogger<CarrierService>.Instance, new CarrierWebhookQueue(db, NullLogger<CarrierWebhookQueue>.Instance));
            var directory = new CarrierDirectory(db, cache);
            var reads = new CarrierPortalReads(carriers, new RateService(db),
                new KpiEngine(db, register, directory, cache, Options.Create(new PreRunOptions())), directory);

            AppUser Carrier(string id) => new("user-" + id, id.ToLowerInvariant() + "@carrier.test", id, Roles.Subcontractor, id, "test", true);
            var userA = Carrier("SUB-A");
            var userB = Carrier("SUB-B");
            var admin = new AppUser("admin", "admin@test.invalid", "Admin", Roles.Admin, "AD-01", "test", true);

            var ratesA = await reads.RatesAsync(userA, default);
            var ratesB = await reads.RatesAsync(userB, default);
            check(ratesA is { SupplierName: "ALPHA TRANSPORT" } && ratesA.Lanes.Select(one => one.To).Order().SequenceEqual(["RAYONG-A1", "SARABURI-A2"])
                && ratesA.Lanes.All(one => one.Prices["1X40'"].Length == 2 && one.Prices["1X40'"][1] == 5100 + one.Id) && ratesA.Bands.Count == 2,
                "carrier portal SQL: Alpha's rates are its own lanes — by supplier, or by its alias where no supplier was written — with every band");
            check(ratesB is not null && ratesB.Lanes.Select(one => one.To).Order().SequenceEqual(["AYUTTHAYA-B3", "CHONBURI-B2", "RAYONG-B1"])
                && !JsonSerializer.Serialize(ratesA).Contains("BRAVO") && !JsonSerializer.Serialize(ratesA).Contains("AYUTTHAYA"),
                "carrier portal SQL: Bravo's lane spelled like Alpha stays Bravo's; nothing of Bravo's reaches Alpha");
            check(await reads.RatesAsync(admin, default) is null && await reads.KpiAsync(admin, new Period("2026", "09", ""), default) is null,
                "carrier portal SQL: an account that is not a carrier's gets no company's rates or KPI");

            // The register's last snapshot is shared by the process, and the checks before this one read
            // other databases; a fresh read of this one first, as the application's single database always is.
            await register.ReadAsync(default);
            var kpiA = await reads.KpiAsync(userA, new Period("2026", "09", ""), default);
            var kpiB = await reads.KpiAsync(userB, new Period("2026", "09", ""), default);
            var textA = JsonSerializer.Serialize(kpiA);
            check(kpiA is { SupplierName: "ALPHA TRANSPORT", OnTime: { Base: 3 } onTimeA, Score: { } scoreA }
                && Math.Abs((onTimeA.Value ?? 0) - 66.7) < 0.1 && scoreA.Carrier == "ALPHA TRANSPORT" && scoreA.Shipments >= 3,
                "carrier portal SQL: Alpha's KPI is measured over its own jobs — two of three on time, its own scorecard line");
            check(kpiB is { OnTime: { Base: 3, Value: 0 }, Score.Carrier: "BRAVO LOGISTICS" }
                && !textA.Contains("BRAVO") && !textA.Contains("B-LATE") && !JsonSerializer.Serialize(kpiB).Contains("ALPHA"),
                "carrier portal SQL: Bravo's late jobs are only in Bravo's KPI; neither carrier's answer names the other");

            var portal = await carriers.ReadAsync(userA, default);
            var postponed = portal!.Accepted.Where(job => job.Postponed).Select(job => job.Key).Order().ToList();
            check(postponed.SequenceEqual(["A-CANCEL", "A-MOVED"])
                && portal.Accepted.Single(job => job.Key == "A-MOVED").OrigDate == "10/09/2026"
                && portal.Accepted.All(job => job.Key.StartsWith("A-")),
                "carrier portal SQL: Postpone holds Alpha's cancelled job and the one moved from 10/09 — and only Alpha's jobs");

            /* ---------------- jobs a carrier keys in, for the department to confirm ---------------- */
            var requests = new CarrierJobRequestService(db, carriers, audit, TimeProvider.System);
            var staff = new AppUser("op", "op@test.invalid", "Operator", Roles.Operation, "OP-C1", "test", true);
            var fields = new Dictionary<string, string> { ["customer"] = "BASF", ["date"] = "02/10/2026", ["type"] = "1X40'", ["destination"] = "LKB", ["planTime"] = "" };
            var created = await requests.CreateAsync(userA, "import", fields, "call before loading", default);
            var mineA = await requests.MineAsync(userA, default);
            var mineB = await requests.MineAsync(userB, default);
            var waiting = await requests.ListAsync(null, default);
            check(created is { Ok: true, Id: { } } && mineA!.Count == 1 && mineA[0] is { Status: CarrierJobRequest.Pending, Category: "IMPORT", SupplierName: "ALPHA TRANSPORT" }
                && !mineA[0].Fields.ContainsKey("planTime") && mineB!.Count == 0 && waiting.Count == 1 && waiting[0].Id == created.Id
                && await requests.MineAsync(admin, default) is null && (await requests.CreateAsync(admin, "IMPORT", fields, "", default)).Code == "no_company",
                "carrier requests SQL: Alpha's request is Alpha's — kept without its blanks, waiting in the department's list, invisible to Bravo");
            var id = created.Id!.Value;
            check((await requests.WithdrawAsync(userB, id, default)).Code == "not_found"
                && (await requests.ApproveAsync(userA, id, "A-ON-1", 0, default)).Code == "forbidden"
                && (await requests.ApproveAsync(staff, id, "NO-SUCH-JOB", 0, default)).Code == "no_job"
                && (await requests.ApproveAsync(staff, id, "A-ON-1", 7, default)).Code == "conflict",
                "carrier requests SQL: Bravo cannot withdraw it, Alpha cannot approve it, no job not yet saved, no stale read");
            db.ChangeTracker.Clear();
            check((await requests.ApproveAsync(staff, id, "A-ON-1", 0, default)) is { Ok: true }
                && (await requests.MineAsync(userA, default))![0] is { Status: CarrierJobRequest.Approved, JobKey: "A-ON-1", DecidedBy: "op@test.invalid" }
                && (await requests.WithdrawAsync(userA, id, default)).Code == "closed" && (await requests.ListAsync(null, default)).Count == 0,
                "carrier requests SQL: approved with the job it became; closed to a late withdrawal; gone from what waits");
            var second = await requests.CreateAsync(userA, "EXPORT", new Dictionary<string, string>
                { ["customer"] = "SCG", ["date"] = "03/10/2026", ["type"] = "1X20'", ["plant"] = "Map Ta Phut" }, "", default);
            check((await requests.RejectAsync(staff, second.Id!.Value, " ", 0, default)).Code == "reason_required"
                && (await requests.RejectAsync(staff, second.Id!.Value, "No truck slot that day", 0, default)).Ok
                && (await requests.MineAsync(userA, default))!.Single(one => one.Id == second.Id) is { Status: CarrierJobRequest.Rejected, DecisionNote: "No truck slot that day" },
                "carrier requests SQL: refused only with a reason, and the carrier reads the reason");
            var trail = await db.AuditEvents.AsNoTracking().Where(one => one.Entity == "carrier-job-request").OrderBy(one => one.Id).ToListAsync();
            check(trail.Select(one => one.NewValue).SequenceEqual([CarrierJobRequest.Pending, CarrierJobRequest.Approved, CarrierJobRequest.Pending, CarrierJobRequest.Rejected])
                && trail[1].Reason.Contains("A-ON-1"),
                "carrier requests SQL: every step is in the audit — keyed, approved with its job, keyed, refused");
        }
        finally
        {
            await setup.Database.EnsureDeletedAsync();
        }
    }
}
