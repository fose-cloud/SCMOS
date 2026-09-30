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
        bool Allows(string request)
        {
            var parts = request.Split(' ', 2);
            return CarrierBoundary.Allows(parts[0], new Microsoft.AspNetCore.Http.PathString(parts[1]));
        }
        var carrierOwn = new[]
        {
            "GET /api/carrier", "POST /api/carrier/J-1/accept", "PUT /api/carrier/J-1/resources", "GET /api/carrier/kpi",
            "GET /api/carrier/dashboard/measures", "POST /api/carrier/capacity", "POST /api/carrier/job-requests/4/withdraw",
            "GET /api/carrier/v1/assignments", "GET /api/me", "HEAD /api/me", "GET /api/notifications", "GET /api/notifications/kinds",
            "GET /api/vehicle-types", "GET /api/documents", "POST /api/documents", "GET /api/documents/51/content",
            "GET /api/carrier-billing/cases", "GET /api/carrier-billing/control-tower/", "POST /api/carrier-billing/cases/3/draft",
            "PUT /api/carrier-billing/invoices/7", "POST /api/carrier-billing/invoices/7/submit", "POST /api/carrier-billing/invoices/7/charges",
            "POST /api/carrier-billing/invoices/7/documents", "PUT /api/carrier-billing/invoices/7/original-package", "GET /health",
        };
        var department = new[]
        {
            "GET /api/kpi/measures", "GET /api/KPI/excel", "GET /api/suppliers", "GET /api/dashboard/today", "GET /api/risk", "GET /api/capacity",
            "POST /api/capacity", "GET /api/jobs", "GET /api/jobs/page", "PUT /api/jobs", "GET /api/integrations/line/events/pending",
            "GET /api/corrections/pending", "GET /api/ai/status", "GET /api/carrier-job-requests", "GET /api/carrier-api/clients",
            "POST /api/carrier-billing/review/7", "GET /api/carrier-billing/finance/records", "PUT /api/carrier-billing/foundation/calendar/2026-10-01",
            "POST /api/documents/retention/5", "GET /api/documents/51/content/x", "GET /api/rates", "DELETE /api/me", "POST /api/notifications",
            "GET /api/carrierx", "GET /api/audit", "GET /api/staff",
        };
        check(carrierOwn.All(Allows) && !department.Any(Allows),
            "carrier boundary: an allow-list — its own routes and the few shared ones its screens need; every department route refused by default");

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
            // Band 2 is priced on Bravo's lanes only, band 3 on nobody's: a carrier is sent the bands its own lanes price.
            setup.FuelBands.AddRange(new FuelBand { Label = "30.00-32.99", MinPrice = 30m, MaxPrice = 32.99m, Position = 0 },
                new FuelBand { Label = "33.00-35.99", MinPrice = 33m, MaxPrice = 35.99m, Position = 1 },
                new FuelBand { Label = "36.00-38.99", MinPrice = 36m, MaxPrice = 38.99m, Position = 2 },
                new FuelBand { Label = "39.00-41.99", MinPrice = 39m, MaxPrice = 41.99m, Position = 3 });
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
            {
                setup.RatePrices.AddRange(new RatePrice { LaneId = lane.Id, Vehicle = "1X40'", BandPosition = 0, Price = 5000 + (int)lane.Id },
                    new RatePrice { LaneId = lane.Id, Vehicle = "1X40'", BandPosition = 1, Price = 5100 + (int)lane.Id });
                if (lane.SupplierId == bravo.Id)
                    setup.RatePrices.Add(new RatePrice { LaneId = lane.Id, Vehicle = "1X40'", BandPosition = 2, Price = 5200 + (int)lane.Id });
            }

            OperationJob Stored(JsonObject job) => new()
            {
                Key = job["key"]!.GetValue<string>(), Cat = "IMPORT", Owner = "Operator", OwnerId = "OP-C1", WorkDate = "15/09/2026",
                Customer = "BASF", Trucker = job["trucker"]!.GetValue<string>(), JobCode = job["jobCode"]!.GetValue<string>(),
                Status = job["status"]!.GetValue<string>(), Data = job.ToJsonString(), UpdatedBy = "test", UpdatedAt = now,
            };
            setup.OperationJobs.AddRange(
                Stored(Job("A-ON-1", "ALPHA")), Stored(Job("A-ON-2", "ALPHA TRANSPORT")),
                Stored(Job("A-LATE", "ALPHA", ("arrTime", "09:30"), ("remark", "internal note, not for the carrier"))),
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
                && ratesA.Lanes.All(one => one.Prices["1X40'"].Length == 2 && one.Prices["1X40'"][1] == 5100 + one.Id)
                && ratesA.Bands.Select(one => one.Position).SequenceEqual([0, 1]),
                "carrier portal SQL: Alpha's rates are its own lanes — by supplier, or by its alias where no supplier was written — on the bands they price, not Bravo's");
            check(ratesB is not null && ratesB.Bands.Select(one => one.Label).SequenceEqual(["30.00-32.99", "33.00-35.99", "36.00-38.99"])
                && ratesB.Lanes.Where(one => one.SupplierId == bravo.Id).All(one => one.Prices["1X40'"].Length == 3 && one.Prices["1X40'"][2] == 5200 + one.Id),
                "carrier portal SQL: a band no lane of the carrier prices is not sent, and the rows keep each band's position");
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

            /* ---------------- the carrier's own Dashboard ---------------- */
            // An issue that names Bravo and no job: the scorecard puts Bravo on the sheet even under Alpha's scope.
            db.OperationalIssues.Add(new OperationalIssue { Code = "OI-T1", FoundOn = "16/09/2026", Reporter = "BRAVO LOGISTICS",
                Detail = "late report", Category = "การรายงาน", Severity = "Low" });
            await db.SaveChangesAsync();
            await register.ReadAsync(default);
            var alphaCompany = (await carriers.CompanyOfAsync(userA, default))!;
            var namesA = await carriers.NamesOfAsync(alphaCompany, default);
            var dashJobs = await carriers.DashboardJobsAsync(alphaCompany, default);
            check(dashJobs.SupplierName == "ALPHA TRANSPORT" && dashJobs.Jobs.Select(job => job["key"]).Order()
                    .SequenceEqual(["A-CANCEL", "A-LATE", "A-MOVED", "A-ON-1", "A-ON-2"])
                && dashJobs.Jobs.All(job => !job.ContainsKey("remark") && !job.ContainsKey("hist") && job.Keys.All(CarrierService.DashboardFields.Contains))
                && dashJobs.Jobs.Single(job => job["key"] == "A-LATE")["arrTime"] == "09:30",
                "carrier dashboard SQL: its own jobs only, cut to the dashboard's fields — no operator's remark, no history");
            var rawScope = await new KpiEngine(db, register, directory, cache, Options.Create(new PreRunOptions()))
                .BuildAsync(new Period("2026", "09", ""), new KpiScope([], namesA.ToList()), default);
            var dashA = (await reads.DashboardMeasuresAsync(userA, new Period("2026", "09", ""), true, default))!;
            var perf = dashA.Measures.Single(measure => measure.Id == nameof(MeasureId.SupplierPerformance));
            var dashText = JsonSerializer.Serialize(dashA);
            check((rawScope.Scorecard ?? []).Any(line => line.Carrier == "BRAVO LOGISTICS")
                && !dashText.Contains("BRAVO") && dashA.Scorecard!.All(line => line.Carrier == "ALPHA TRANSPORT")
                && dashA.Suppliers.All(line => line.Carrier == "ALPHA TRANSPORT") && perf.Breakdown.All(entry => entry.Label == "ALPHA TRANSPORT")
                && dashA is { UnattributedIssues: 0, IssuesInPeriod: 0 }
                && dashA.Measures.Single(measure => measure.Id == nameof(MeasureId.OnTimeDelivery)) is { Base: 3 },
                "carrier dashboard SQL: the engine's scoped scorecard still names Bravo off an issue; the carrier's answer does not, anywhere");
            check(await reads.DashboardMeasuresAsync(admin, new Period("2026", "09", ""), false, default) is null
                && await reads.DashboardJobsAsync(admin, default) is null,
                "carrier dashboard SQL: an account that is not a carrier's gets no company's dashboard");

            /* ---------------- the carrier's own Capacity ---------------- */
            var capacity = new CapacityService(db);
            var namesB = await carriers.NamesOfAsync((await carriers.CompanyOfAsync(userB, default))!, default);
            check((await capacity.ReportAsync(alpha.Id, "15/09/2026", "40f", 5, 2, "sub-a", default)).Ok
                && (await capacity.ReportAsync(bravo.Id, "15/09/2026", "40F", 9, 9, "sub-b", default)).Ok
                && (await capacity.ReportAsync(alpha.Id, "15/09/2026", "40F", 4, 1, "sub-a", default)).Ok
                && !(await capacity.ReportAsync(alpha.Id, "2026-09-15", "40F", 1, 0, "sub-a", default)).Ok,
                "carrier capacity SQL: reported per day and vehicle; saying it again corrects it; the register's date form only");
            var capA = await capacity.ReadForSupplierAsync(alpha.Id, alpha.Name, namesA, "14/09/2026", 3, default);
            var capB = await capacity.ReadForSupplierAsync(bravo.Id, bravo.Name, namesB, "14/09/2026", 3, default);
            var fortyA = capA.Cells.Single(cell => cell.Date == "15/09/2026" && cell.VehicleType == "40F");
            check(fortyA is { Reported: true, Available: 4, Committed: 1, Spare: 3 } && fortyA.Jobs == 4
                && capA.Cells.Count == 1 && capA.Dates.SequenceEqual(["14/09/2026", "15/09/2026", "16/09/2026"])
                && capB.Cells.Single(cell => cell.Date == "15/09/2026") is { Available: 9, Jobs: 3 }
                && !JsonSerializer.Serialize(capA).Contains("BRAVO") && !JsonSerializer.Serialize(capB).Contains("ALPHA"),
                "carrier capacity SQL: each carrier reads its own fleet beside its own jobs (not cancelled) — never the other's fleet or the department's demand");

            /* ---------------- the carrier's My job: the department's workspace over its own register ---------------- */
            var grid = new CarrierRegisterService(db, carriers, new JobsRepository(db, register), audit);
            JsonElement Sent(object job) => JsonSerializer.SerializeToElement(job);
            async Task<Dictionary<string, string>> Row(string key) =>
                JsonSerializer.Deserialize<Dictionary<string, JsonElement>>((await db.OperationJobs.AsNoTracking().SingleAsync(one => one.Key == key)).Data)!
                    .Where(pair => pair.Value.ValueKind == JsonValueKind.String).ToDictionary(pair => pair.Key, pair => pair.Value.GetString() ?? "");
            var readA = (await grid.ReadAsync(userA, default))!;
            var readB = (await grid.ReadAsync(userB, default))!;
            check(readA.Jobs.Select(job => job["key"]).Order().SequenceEqual(["A-CANCEL", "A-LATE", "A-MOVED", "A-ON-1", "A-ON-2"])
                && readB.Jobs.All(job => job["key"].StartsWith("B-"))
                && readA.Jobs.All(job => !job.ContainsKey("remark") && !job.ContainsKey("hist") && job.Keys.All(CarrierRegisterService.Fields.Contains))
                && await grid.ReadAsync(admin, default) is null,
                "carrier my job SQL: the register it reads is its own jobs, cut to the grid's fields — no remark, no history");
            var stampBefore = DateTimeOffset.UtcNow.AddSeconds(-1);
            var foreign = await grid.SaveAsync(userA, [Sent(new { key = "B-LATE-1", licence = "70-9999" })], default);
            var mixed = await grid.SaveAsync(userA, [Sent(new
            {
                key = "A-MOVED", customer = "HACKED", trucker = "BRAVO LOGISTICS", cost = "1",
                licence = "70-1234", contact = "081-2345678", arrDate = "02/10/2026", arrTime = "09:15",
            })], default);
            var moved = await Row("A-MOVED");
            check(foreign is { Ok: false, Status: 403 } && (await Row("B-LATE-1"))["licence"] == "70-1111"
                && mixed is { Ok: true, Saved: 1 } && moved["customer"] == "BASF" && moved["trucker"] == "ALPHA" && moved.GetValueOrDefault("cost", "") == ""
                && moved["licence"] == "70-1234" && moved["contact"] == "081-2345678" && moved["arrTime"] == "09:15",
                "carrier my job SQL: another carrier's job refuses the save; on its own, only the field cells land — customer, carrier and price stay");
            var gridTrail = await db.AuditEvents.AsNoTracking().Where(one => one.Entity == "job" && one.EntityId == "A-MOVED").ToListAsync();
            check(gridTrail.Any(one => one.NewValue == "70-1234" && one.Reason == "ผู้ขนส่งแก้ในตาราง My job" && one.Who == "sub-a@carrier.test")
                && gridTrail.Any(one => one.NewValue == "09:15"),
                "carrier my job SQL: each cell it changes is in the audit, with who and why");
            check((await grid.SaveAsync(userA, [Sent(new { key = "A-MOVED", licence = "not a plate" })], default)) is { Ok: false, Status: 400 }
                && (await grid.SaveAsync(userA, [Sent(new { key = "A-MOVED", contact = "12345" })], default)) is { Ok: false, Status: 400 }
                && (await grid.SaveAsync(userA, [Sent(new { key = "A-MOVED", status = "RECEIVED" })], default)) is { Ok: false, Status: 400 }
                && (await grid.SaveAsync(userA, [Sent(new { key = "A-ON-1", licence = "70-5555" })], default)) is { Ok: false, Status: 400 }
                && (await Row("A-ON-1"))["licence"] == "70-1111",
                "carrier my job SQL: a malformed plate or phone, a status off its ladder, and a closed job are refused, nothing written");
            var dispatched = await grid.SaveAsync(userA, [Sent(new { key = "A-MOVED", status = "DISPATCHED" })], default);
            check(dispatched is { Ok: true } && (await Row("A-MOVED"))["status"] == JobStatus.Dispatched,
                "carrier my job SQL: a status along the carrier's ladder goes through its own status step");
            var delta = (await grid.ChangedAsync(userA, stampBefore, default))!;
            check(delta.Count == 5 && delta.Jobs.Any(job => job["key"] == "A-MOVED") && delta.Jobs.All(job => job["key"].StartsWith("A-")),
                "carrier my job SQL: what changed since a stamp is its own rows only, with its own count");

            /* ---------------- jobs a carrier keys in, for the department to confirm ---------------- */
            var requests = new CarrierJobRequestService(db, carriers, new JobsRepository(db, register), audit, TimeProvider.System);
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

            /* ---------------- the register asks the carrier, and bills what closes (30 Sep 2026) ---------------- */
            var registerJobs = new JobsRepository(db, register);
            var follower = new RegisterCarrierFollower(db, registerJobs, billing,
                new CarrierWebhookQueue(db, NullLogger<CarrierWebhookQueue>.Instance), audit, NullLogger<RegisterCarrierFollower>.Instance);
            async Task<RegisterFollowResult> Keyed(params JsonObject[] rows)
            {
                await registerJobs.SaveAsync(rows.Select(row => JsonSerializer.SerializeToElement(row)).ToList(), staff.Signature, default);
                var followed = await follower.FollowAsync(registerJobs.LastChanges, staff, default);
                await register.ReadAsync(default);
                return followed;
            }
            // The portal and the bell read the register as it was a moment ago while it is read again behind
            // them (stale-while-revalidate); a check reads it afresh first.
            async Task<CarrierService.Portal> PortalOf(AppUser user)
            {
                await register.ReadAsync(default);
                return (await carriers.ReadAsync(user, default))!;
            }

            var fresh = await Keyed(Job("N-1", "ALPHA", ("status", "RECEIVED"), ("arrDate", ""), ("arrTime", "")));
            var offeredA = await PortalOf(userA);
            check(fresh is { Offered: 1, Withdrawn: 0, Billed: 0 }
                && offeredA.Offered.Any(job => job.Key == "N-1") && offeredA.Accepted.All(job => job.Key != "N-1")
                && (await grid.ReadAsync(userA, default))!.Jobs.All(job => job["key"] != "N-1")
                && (await Row("N-1"))["status"] == JobStatus.WaitingSupplier,
                "register SQL: a job keyed with a carrier is that carrier's NEW job, not its My job, and reads WAITING_SUPPLIER in SCMOS");
            var yes = await carriers.AcceptAssignmentAsync(alpha, "N-1", null, "", "", "", "", "", userA.Signature, default);
            check(yes.Ok && (await PortalOf(userA)).Accepted.Any(job => job.Key == "N-1")
                && (await grid.ReadAsync(userA, default))!.Jobs.Any(job => job["key"] == "N-1")
                && (await Row("N-1"))["status"] == JobStatus.SupplierConfirmed,
                "register SQL: accepted, it moves to the carrier's My job, and SCMOS reads SUPPLIER_CONFIRMED");

            var ahead = await Keyed(Job("N-2", "ALPHA", ("status", "DISPATCHED")));
            var aheadYes = await carriers.AcceptAssignmentAsync(alpha, "N-2", null, "", "", "", "", "", userA.Signature, default);
            check(ahead.Offered == 1 && aheadYes.Ok && (await Row("N-2"))["status"] == JobStatus.Dispatched,
                "register SQL: a job the department already moved on keeps its status when asked and when accepted");
            var respelled = await Keyed(Job("N-1", "ALPHA TRANSPORT", ("status", JobStatus.SupplierConfirmed), ("arrDate", ""), ("arrTime", "")));
            var untouched = await Keyed(Job("A-ON-1", "ALPHA", ("remark", "edited by the department")));
            check(respelled is { Offered: 0, Withdrawn: 0 } && (await PortalOf(userA)).Accepted.Any(job => job.Key == "N-1")
                && untouched is { Offered: 0, Withdrawn: 0, Billed: 0 } && (await PortalOf(userA)).Accepted.Any(job => job.Key == "A-ON-1"),
                "register SQL: the same company spelled another way asks nobody again, and a job it held by name stays its own");

            await Keyed(Job("N-3", "ALPHA", ("status", "RECEIVED")));
            var swapped = await Keyed(Job("N-3", "BRAVO LOGISTICS", ("status", JobStatus.WaitingSupplier)));
            var asks = await db.SupplierRequests.AsNoTracking().Where(row => row.JobKey == "N-3").OrderBy(row => row.Id).ToListAsync();
            check(swapped is { Offered: 1, Withdrawn: 1 } && (await PortalOf(userA)).Offered.All(job => job.Key != "N-3")
                && (await PortalOf(userB)).Offered.Any(job => job.Key == "N-3") && asks.Count == 2
                && asks[0] is { Outcome: CarrierAssignment.Superseded, ReasonCode: CarrierAssignment.RegisterChanged }
                && asks[1] is { Outcome: CarrierAssignment.Pending, Rank: 2 },
                "register SQL: another carrier named closes the first ask and asks the second");
            var takenOff = await Keyed(Job("N-3", "", ("status", JobStatus.WaitingSupplier)));
            check(takenOff is { Offered: 0, Withdrawn: 1 } && (await PortalOf(userB)).Offered.All(job => job.Key != "N-3"),
                "register SQL: the carrier taken off the job, its ask is closed");

            await Keyed(Job("N-4", "BRAVO LOGISTICS", ("status", "RECEIVED")));
            var bellCache = new MemoryCache(new MemoryCacheOptions());
            var bell = new NotificationService(db, new KpiEngine(db, register, new CarrierDirectory(db, bellCache), bellCache,
                Options.Create(new PreRunOptions())), register, new DelegationService(db));
            async Task<bool> Declined()
            {
                await register.ReadAsync(default);
                return (await bell.BuildAsync(null, default)).Alerts
                    .Any(alert => alert.Kind == nameof(AlertKind.CarrierDeclined) && alert.TargetId == "N-4");
            }
            var quiet = await Declined();
            var no = await carriers.DeclineAssignmentAsync(bravo, "N-4", null, "NO_TRUCK", "", userB.Signature, default);
            var rung = await Declined();
            await Keyed(Job("N-4", "ALPHA", ("status", JobStatus.WaitingSupplier)));
            check(!quiet && no.Ok && rung && !await Declined() && (await PortalOf(userA)).Offered.Any(job => job.Key == "N-4"),
                "register SQL: a carrier's no rings the department's bell until another carrier is named, and that one is asked");

            var ownAsk = await requests.CreateAsync(userA, "import", fields, "", default);
            await Keyed(Job("N-5", "ALPHA TRANSPORT", ("status", "RECEIVED")));
            db.ChangeTracker.Clear();
            var approved = await requests.ApproveAsync(staff, ownAsk.Id!.Value, "N-5", 0, default);
            var own = await db.SupplierRequests.AsNoTracking().SingleAsync(row => row.JobKey == "N-5");
            check(approved.Ok && own is { Outcome: CarrierAssignment.Confirmed, ReasonCode: CarrierAssignment.CarrierRequested, RespondedBy: "sub-a@carrier.test" }
                && (await Row("N-5"))["status"] == JobStatus.SupplierConfirmed && (await PortalOf(userA)).Accepted.Any(job => job.Key == "N-5"),
                "register SQL: a job the carrier keyed itself, saved and approved, is in its My job without asking it again");

            var closedOut = await Keyed(Job("N-1", "ALPHA TRANSPORT", ("status", JobStatus.Completed), ("arrDate", "28/09/2026"), ("arrTime", "10:00")));
            var caseN1 = await db.BillingCases.AsNoTracking().SingleOrDefaultAsync(row => row.JobKey == "N-1");
            check(closedOut.Billed == 1 && caseN1 is { } && caseN1.SupplierId == alpha.Id
                && caseN1.DeliveryCompletedAt == new DateTimeOffset(2026, 9, 28, 3, 0, 0, TimeSpan.Zero)
                && (await billing.ListForCarrierAsync(alpha.Id, default)).Items.Any(item => item.JobKey == "N-1")
                && (await billing.ListForCarrierAsync(bravo.Id, default)).Items.All(item => item.JobKey != "N-1"),
                "register SQL: closed COMPLETED in SCMOS, the carrier's Billing Case opens, due from the real delivery");

            var finished = await grid.SaveAsync(userA, [Sent(new { key = "A-MOVED", status = "COMPLETED" })], default);
            var movedCase = await db.BillingCases.AsNoTracking().SingleOrDefaultAsync(row => row.JobKey == "A-MOVED");
            var binding = movedCase is null ? null : await db.SupplierRequests.AsNoTracking().SingleOrDefaultAsync(row => row.Id == movedCase.AssignmentId);
            check(finished.Ok && movedCase is { } && binding is { Outcome: CarrierAssignment.Confirmed, ReasonCode: CarrierAssignment.RegisterBinding },
                "register SQL: a job the carrier held by name, completed in its own My job, is bound to it and billed");

            var scheduler = new AppUser("scheduler", "", "SCMOS", "System", "", "system", Recognised: true);
            var swept = await follower.SweepAsync(scheduler, default, page: 2);
            var billedNow = await db.BillingCases.AsNoTracking().Select(row => row.JobKey).ToListAsync();
            var bindings = await db.SupplierRequests.AsNoTracking().CountAsync(row => row.ReasonCode == CarrierAssignment.RegisterBinding);
            check(swept == 6 && billedNow.Order().SequenceEqual(["A-LATE", "A-MOVED", "A-ON-1", "A-ON-2", "B-LATE-1", "B-LATE-2", "B-LATE-3", "N-1"])
                && bindings == 7 && await follower.SweepAsync(scheduler, default) == 0,
                "register SQL: the sweep bills every COMPLETED job closed before, a page at a time, once — the cancelled one not");

            var billedKeys = await registerJobs.BilledAsync(["A-ON-1", "N-2", "N-4"], default);
            var jobsBefore = await db.OperationJobs.CountAsync();
            var removed = await registerJobs.ClearAsync(default);
            check(billedKeys.SequenceEqual(["A-ON-1"]) && removed == jobsBefore - billedNow.Count
                && await db.OperationJobs.CountAsync() == billedNow.Count,
                "register SQL: a job with a Billing Case is never deleted — named by key, and kept when the register is cleared");
        }
        finally
        {
            await setup.Database.EnsureDeletedAsync();
        }
    }
}
