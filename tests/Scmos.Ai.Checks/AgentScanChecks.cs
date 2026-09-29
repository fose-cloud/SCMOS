using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Scmos.Api.Ai;
using Scmos.Api.Ai.Booking;
using Scmos.Api.Ai.Carrier;
using Scmos.Api.Ai.Communication;
using Scmos.Api.Ai.Otd;
using Scmos.Api.Ai.Validation;
using Scmos.Api.Auth;
using Scmos.Api.Data;
using Scmos.Api.Rules;
using Scmos.Api.Services;

/// <summary>
/// The rule-first agents (Agent Platform, 28 Sep 2026): the OTD Agent's and the
/// Validation Agent's judgements against a fixed clock — 10:00 in Bangkok on
/// 28 September — every result held to the decision contract, and with
/// <c>--write-local-db</c> the pass's lifecycle on a scratch database of its own.
/// </summary>
static class AgentScanChecks
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-09-28T03:00:00Z");
    private const string Today = "28/09/2026";
    private static readonly string Container40 = "1X40'";

    private static JsonObject Job(string key, params (string Field, string Value)[] set)
    {
        var job = new JsonObject
        {
            ["key"] = key, ["cat"] = "IMPORT", ["opId"] = "OP-S1", ["op"] = "Operator", ["date"] = Today, ["planTime"] = "",
            ["status"] = "SUPPLIER_CONFIRMED", ["trucker"] = "SHORE", ["licence"] = "70-1111", ["driver"] = "Somchai",
            ["contact"] = "081-2345678", ["customer"] = "BASF", ["type"] = Container40, ["container"] = "TEMU5246902",
            ["seal"] = "", ["arrDate"] = "", ["arrTime"] = "", ["jobCode"] = "J-" + key, ["destination"] = "LKB",
        };
        foreach (var (field, value) in set) job[field] = value;
        return job;
    }

    private static CachedJobRow Row(JsonObject job)
    {
        var raw = JsonDocument.Parse(job.ToJsonString()).RootElement.Clone();
        return new CachedJobRow(job["key"]!.GetValue<string>(), job["trucker"]!.GetValue<string>(), raw, JobRecord.From(raw));
    }

    private static AgentResult? Otd(JsonObject job, DateTimeOffset? at = null) => OtdAgent.Assess(Row(job), at ?? Now, 120, 60);
    private static AgentResult? Check(JsonObject job) => ValidationAgent.Assess(Row(job), Now);

    public static async Task RunAsync(Action<bool, string> check, bool sql)
    {
        var agents = new AgentRegistry();
        var known = agents.All.Select(agent => agent.Id).ToList();
        check(JobVehicleType.IsKnown(Container40), "scan: the fixture's vehicle type is on the department's list");
        var every = new List<AgentResult>();
        AgentResult? Keep(AgentResult? result) { if (result is not null) every.Add(result); return result; }

        /* ---------------- OTD ---------------- */
        var missed = Keep(Otd(Job("P1", ("planTime", "08:00"), ("status", "DISPATCHED"))));
        check(missed is { RiskLevel: OtdAgent.Critical } && missed.RuleReferences.Contains(OtdAgent.PastWindow) && missed.Summary.Contains("120"),
            "OTD: two hours past plan with no arrival is CRITICAL — past the on-time window");
        var inGrace = Keep(Otd(Job("P2", ("planTime", "09:45"), ("status", "DISPATCHED"))));
        check(inGrace is { RiskLevel: OtdAgent.High } && inGrace.RuleReferences.Contains(OtdAgent.PastPlan),
            "OTD: fifteen minutes past plan, inside the grace, is HIGH");
        var evonik = Keep(Otd(Job("P3", ("planTime", "08:00"), ("status", "DISPATCHED"), ("customer", "EVONIK THAILAND"), ("type", "1X20' TK"))));
        check(evonik is { RiskLevel: OtdAgent.High } && evonik.RuleResults.Any(rule => rule.Source == "CustomerTerms.EVONIK"),
            "OTD: the customer's own grace is the rule — Evonik tank, two hours past, is still inside its 180 minutes");
        var noTruck = Keep(Otd(Job("P4", ("planTime", "10:45"), ("licence", ""), ("driver", ""))));
        check(noTruck is { RiskLevel: OtdAgent.High } && noTruck.RuleReferences.Contains(OtdAgent.NoTruckNearPlan),
            "OTD: 45 minutes to plan with no truck or driver is HIGH");
        var notRunning = Keep(Otd(Job("P5", ("planTime", "11:30"))));
        check(notRunning is { RiskLevel: OtdAgent.Watch } && notRunning.RuleReferences.SequenceEqual([OtdAgent.NotDispatchedNearPlan]),
            "OTD: 90 minutes to plan and not yet running is WATCH");
        check(Otd(Job("P6", ("planTime", "11:30"), ("status", "DISPATCHED"))) is null, "OTD: running, with everything named, is nothing to report");
        check(Otd(Job("P7", ("planTime", "08:00"), ("arrDate", Today), ("arrTime", "08:10"))) is null, "OTD: an arrival recorded ends the question");
        check(Otd(Job("P8", ("planTime", "08:00"), ("status", "COMPLETED"))) is null && Otd(Job("P9", ("planTime", "08:00"), ("status", "CANCELLED"))) is null,
            "OTD: finished and cancelled work is not a risk");
        check(Otd(Job("P10", ("date", "25/09/2026"))) is null, "OTD: three days old with no arrival is a record gap, not today's risk");
        var yesterday = Keep(Otd(Job("P11", ("date", "27/09/2026"))));
        check(yesterday is { RiskLevel: OtdAgent.Critical } && yesterday.RuleReferences.SequenceEqual(["MonitorRules.Overdue"]),
            "OTD: yesterday with no plan time and no arrival is the monitor's Overdue — one rule, read in two places");
        var noCarrier = Keep(Otd(Job("P12", ("date", "29/09/2026"), ("trucker", ""), ("licence", ""), ("driver", ""))));
        check(noCarrier is { RiskLevel: OtdAgent.High } && noCarrier.RuleReferences.Contains("MonitorRules.NoCarrier"),
            "OTD: tomorrow with no carrier is HIGH, from the monitor");
        var tomorrowNoTruck = Keep(Otd(Job("P13", ("date", "29/09/2026"), ("licence", ""), ("driver", ""))));
        check(tomorrowNoTruck is { RiskLevel: OtdAgent.Watch } && tomorrowNoTruck.RuleReferences.SequenceEqual(["MonitorRules.NoTruck"]),
            "OTD: tomorrow with a carrier and no truck is WATCH");
        check(Otd(Job("P14", ("date", "03/10/2026"), ("trucker", ""))) is null, "OTD: past the monitor's window is not yet a risk");
        check(missed!.Facts.All(fact => fact.Source is not null) && missed.Inferences.Count > 0 && missed.Recommendations.Any(r => r.Text.Contains("SHORE")),
            "OTD: facts carry their sources; the inference and the recommendation are kept apart, naming the carrier");
        var later = Otd(Job("P1", ("planTime", "08:00"), ("status", "DISPATCHED")), Now.AddMinutes(10))!;
        check(later.Summary != missed.Summary && AiDecisionLog.FingerprintOf(later) == AiDecisionLog.FingerprintOf(missed),
            "OTD: the minutes move every pass, the finding does not — the fingerprint ignores the clock");

        /* ---------------- Validation ---------------- */
        check(Check(Job("V1", ("planTime", "14:00"))) is null, "validation: a readable job with its paperwork passes and writes nothing");
        var weight = Keep(Check(Job("V2", ("weight", "abc"))));
        check(weight is { Status: AgentResultRules.InsufficientInformation } && weight.RuleReferences.Contains("VALIDATE.WEIGHT")
            && ValidationAgent.OutcomeOf(weight.RuleReferences) == ValidationAgent.NeedsInformation,
            "validation: an unreadable weight is NEEDS_INFORMATION, by JobRules.Validate");
        var type = Keep(Check(Job("V3", ("type", "40 FOOT BOX"))));
        check(type is { Status: AgentResultRules.RequiresHumanReview } && type.RuleReferences.Contains("VEHICLE_TYPE_UNKNOWN")
            && ValidationAgent.OutcomeOf(type.RuleReferences) == ValidationAgent.Warning,
            "validation: a vehicle type off the list is a WARNING");
        var gate = Keep(Check(Job("V4", ("cat", "EXPORT"), ("date", "02/10/2026"), ("planTime", "17:00"), ("closingDate", "02/10/2026"), ("closingTime", "12:00"))));
        check(gate is not null && gate.RuleReferences.Contains("GATE_IN_RISK") && !gate.RuleReferences.Contains("GAP.SEAL"),
            "validation: an export truck due after the yard closes is raised; the seal is not chased four days out");
        var noBox = Keep(Check(Job("V5", ("date", "29/09/2026"), ("container", ""))));
        check(noBox is { Status: AgentResultRules.InsufficientInformation } && noBox.RuleReferences.Contains("GAP.CONTAINER"),
            "validation: tomorrow's job with no container number is NEEDS_INFORMATION");
        check(Check(Job("V6", ("date", "29/09/2026"), ("licence", ""), ("driver", ""))) is null,
            "validation: a missing truck is the OTD Agent's finding, not raised twice");
        check(Check(Job("V7", ("date", "15/10/2026"), ("weight", "abc"))) is null && Check(Job("V8", ("weight", "abc"), ("status", "COMPLETED"))) is null,
            "validation: beyond the coming week, and finished work, are not checked");
        check(every.All(result => AgentResultRules.Problems(result, known).Count == 0),
            "scan: every result both agents produced passes the decision contract");

        if (sql) await SqlAsync(check, agents);
    }

    private static async Task SqlAsync(Action<bool, string> check, AgentRegistry agents)
    {
        var database = "SCMOS_AI_SCAN_TEST_" + Guid.NewGuid().ToString("N");
        var connection = $"Server=(localdb)\\MSSQLLocalDB;Database={database};Integrated Security=true;TrustServerCertificate=true";
        var options = new DbContextOptionsBuilder<ScmosDbContext>().UseSqlServer(connection,
            s => { s.UseCompatibilityLevel(150); s.EnableRetryOnFailure(3); }).Options;
        await using var setup = new ScmosDbContext(options);
        await setup.Database.EnsureCreatedAsync();
        try
        {
            var clock = new MovableClock(Now);
            var ai = new AiOptions { Enabled = true, OtdAgentEnabled = true, ValidationAgentEnabled = true };
            OperationJob Stored(JsonObject job) => new()
            {
                Key = job["key"]!.GetValue<string>(), Cat = "IMPORT", Owner = "Operator", OwnerId = "OP-S1", WorkDate = Today,
                Customer = "BASF", Trucker = job["trucker"]!.GetValue<string>(), JobCode = job["jobCode"]!.GetValue<string>(),
                Status = job["status"]!.GetValue<string>(), Data = job.ToJsonString(), UpdatedBy = "test", UpdatedAt = Now,
            };
            setup.OperationJobs.AddRange(
                Stored(Job("S-LATE", ("planTime", "08:00"), ("status", "DISPATCHED"))),
                Stored(Job("S-TRUCK", ("planTime", "10:45"), ("licence", ""), ("driver", ""))),
                Stored(Job("S-WEIGHT", ("planTime", "15:00"), ("status", "DISPATCHED"), ("weight", "abc"))),
                Stored(Job("S-FINE", ("planTime", "15:00"), ("status", "DISPATCHED"))));
            await setup.SaveChangesAsync();

            async Task<IReadOnlyList<ScanSummary>> Pass(AiOptions? with = null)
            {
                await using var db = new ScmosDbContext(options);
                var audit = new AuditService(db, new HttpContextAccessor(), NullLogger<AuditService>.Instance);
                var governance = new AiGovernanceService(db, agents, audit, Options.Create(with ?? ai), clock, NullLogger<AiGovernanceService>.Instance);
                var cache = new MemoryCache(new MemoryCacheOptions());
                var register = new JobRegisterCache(db, cache, NullLogger<JobRegisterCache>.Instance);
                // A test that just edited a job drops the cached read, as the application's own writes do.
                register.Invalidate();
                var suppliers = new SupplierService(db, new KpiEngine(db, register, new CarrierDirectory(db, cache), cache,
                    Options.Create(new PreRunOptions())));
                var scanner = new AgentScanner(db, register, agents, governance, new AiDecisionLog(db, agents, audit, clock),
                    suppliers, Options.Create(with ?? ai), clock, NullLogger<AgentScanner>.Instance);
                return await scanner.ScanAsync(default);
            }
            async Task<List<AiDecision>> Decisions()
            {
                await using var db = new ScmosDbContext(options);
                return await db.AiDecisions.AsNoTracking().OrderBy(one => one.Id).ToListAsync();
            }
            async Task Edit(string key, params (string Field, string Value)[] set)
            {
                await using var db = new ScmosDbContext(options);
                var row = await db.OperationJobs.SingleAsync(one => one.Key == key);
                var job = JsonNode.Parse(row.Data)!.AsObject();
                foreach (var (field, value) in set) job[field] = value;
                row.Data = job.ToJsonString();
                await db.SaveChangesAsync();
            }

            var first = await Pass();
            var rows = await Decisions();
            check(first.Where(one => one.AgentId is not CarrierAgent.Id and not CommunicationAgent.Id and not BookingAgent.Id).All(one => one.Code == "ok")
                && first.Where(one => one.AgentId is CarrierAgent.Id or CommunicationAgent.Id or BookingAgent.Id).All(one => one.Code == "agent_disabled")
                && first.Single(one => one.AgentId == OtdAgent.Id).Created == 2
                && first.Single(one => one.AgentId == ValidationAgent.Id).Created == 1 && rows.Count == 3,
                "scan SQL: the first pass records the late job and the truckless one (OTD) and the unreadable weight (Validation)");
            check(rows.All(one => one.Status == AiDecisionLog.Open && one.Shadow && one.OwnerId == "OP-S1" && one.RunId == ""
                && one.Fingerprint.Length == 64 && one.Autonomy == 2), "scan SQL: in shadow, owned, fingerprinted, no model run claimed");

            clock.Advance(TimeSpan.FromMinutes(10));
            var again = await Pass();
            check(again.All(one => one.Created == 0 && one.Superseded == 0 && one.Resolved == 0) && (await Decisions()).Count == 3,
                "scan SQL: the same findings ten minutes later add nothing — the log is not a flood");

            await Edit("S-TRUCK", ("licence", "70-2222"), ("driver", "Somsak"));
            var changed = await Pass();
            rows = await Decisions();
            var truck = rows.Where(one => one.EntityId == "S-TRUCK").ToList();
            check(changed.Single(one => one.AgentId == OtdAgent.Id) is { Superseded: 1, Created: 1 }
                && truck.Count == 2 && truck[0].Status == AiDecisionLog.Superseded && truck[1].Status == AiDecisionLog.Open
                && truck[1].RiskLevel == OtdAgent.Watch,
                "scan SQL: a truck named near plan changes the finding — the old one superseded, the new one (not yet running) open");

            await Edit("S-LATE", ("arrDate", Today), ("arrTime", "09:10"));
            var resolved = await Pass();
            check(resolved.Single(one => one.AgentId == OtdAgent.Id).Resolved == 1
                && (await Decisions()).Single(one => one.EntityId == "S-LATE").Status == AiDecisionLog.Resolved,
                "scan SQL: an arrival recorded resolves the finding — kept, closed by the pass, not deleted");

            await using (var db = new ScmosDbContext(options))
            {
                var log = new AiDecisionLog(db, agents, new AuditService(db, new HttpContextAccessor(), NullLogger<AuditService>.Instance), clock);
                var weightId = (await db.AiDecisions.AsNoTracking().SingleAsync(one => one.EntityId == "S-WEIGHT")).Id;
                var owner = new AppUser("scan-op", "op@test.invalid", "Operator", Roles.Operation, "OP-S1", "test", true);
                check(await log.AnswerAsync(weightId, owner, "DISMISSED", "", "Weight is fixed in the workbook tomorrow", default) == "ok",
                    "scan SQL: the owner sets a finding aside");
            }
            await Pass();
            check((await Decisions()).Count(one => one.EntityId == "S-WEIGHT") == 1,
                "scan SQL: a finding a person already answered is not raised again while it is the same finding");

            await using (var db = new ScmosDbContext(options))
            {
                var service = new AiGovernanceService(db, agents, new AuditService(db, new HttpContextAccessor(), NullLogger<AuditService>.Instance),
                    Options.Create(ai), clock, NullLogger<AiGovernanceService>.Instance);
                var admin = new AppUser("scan-admin", "a@test.invalid", "Admin", Roles.Admin, "AD-S1", "test", true);
                check(await service.SetAsync(admin, OtdAgent.Id, 2, true, AgentGovernance.Paused, "drill", 0, default) == "ok", "scan SQL: the OTD Agent paused");
            }
            await Edit("S-FINE", ("status", "SUPPLIER_CONFIRMED"), ("planTime", "11:00"), ("licence", ""), ("driver", ""));
            var countBefore = (await Decisions()).Count;
            var paused = await Pass();
            check(paused.Single(one => one.AgentId == OtdAgent.Id).Code == "agent_paused" && (await Decisions()).Count == countBefore
                && (await Decisions()).Count(one => one.AgentId == OtdAgent.Id && one.Status == AiDecisionLog.Open) == 1,
                "scan SQL: a paused agent writes nothing and closes nothing");
            var off = await Pass(new AiOptions { Enabled = true, ValidationAgentEnabled = true });
            check(off.Single(one => one.AgentId == OtdAgent.Id).Code == "agent_disabled", "scan SQL: an agent whose flag is off does not run");
            var aiOff = await Pass(new AiOptions { OtdAgentEnabled = true, ValidationAgentEnabled = true });
            check(aiOff.All(one => one.Code == "agent_disabled"), "scan SQL: with AI off nothing runs — SCMOS without AI is unchanged");

            /* ---------------- the Carrier Agent and its shadow comparison ---------------- */
            var carrierOn = new AiOptions { Enabled = true, VendorAgentEnabled = true };
            await using (var db = new ScmosDbContext(options))
            {
                for (var i = 0; i < 6; i++)
                    db.OperationJobs.Add(Stored(Job($"H-SHORE-{i}", ("status", "COMPLETED"), ("date", "01/09/2026"), ("planTime", "08:00"),
                        ("arrDate", "01/09/2026"), ("arrTime", "07:55"))));
                for (var i = 0; i < 6; i++)
                    db.OperationJobs.Add(Stored(Job($"H-ACN-{i}", ("trucker", "ACN"), ("status", "COMPLETED"), ("date", "01/09/2026"),
                        ("planTime", "08:00"), ("arrDate", "01/09/2026"), ("arrTime", i < 3 ? "07:50" : "09:30"))));
                db.OperationJobs.Add(Stored(Job("S-OPEN", ("trucker", ""), ("status", JobStatus.WaitingSupplier), ("date", "29/09/2026"),
                    ("planTime", "09:00"), ("licence", ""), ("driver", ""))));
                var shore = new Supplier { Name = "SHORE LOGISTICS", Code = "SHR", Status = "approved", IsCarrier = true, CreatedAt = Now, UpdatedAt = Now };
                db.Suppliers.AddRange(shore, new Supplier { Name = "ACN", Code = "ACN", Status = "approved", IsCarrier = true, CreatedAt = Now, UpdatedAt = Now });
                await db.SaveChangesAsync();
                db.SupplierAliases.Add(new SupplierAlias { SupplierId = shore.Id, Alias = "SHORE", Source = "test", Confirmed = true });
                await db.SaveChangesAsync();
            }
            async Task Ask(string carrier, int rank, string? closeFirst = null)
            {
                await using var db = new ScmosDbContext(options);
                if (closeFirst is not null)
                {
                    var open = await db.SupplierRequests.SingleAsync(one => one.JobKey == "S-OPEN" && one.Outcome == CarrierAssignment.Pending);
                    open.Outcome = closeFirst;
                    open.RespondedAt = clock.GetUtcNow();
                }
                else db.SupplierRequests.Add(new SupplierRequest
                {
                    JobKey = "S-OPEN", Carrier = carrier, Rank = rank, Outcome = CarrierAssignment.Pending,
                    RequestedBy = "Operator", RequestedAt = clock.GetUtcNow().AddMinutes(1),
                });
                await db.SaveChangesAsync();
            }

            clock.Advance(TimeSpan.FromMinutes(15));
            var carrierFirst = await Pass(carrierOn);
            var raised = (await Decisions()).Where(one => one.AgentId == CarrierAgent.Id).ToList();
            check(carrierFirst.Single(one => one.AgentId == CarrierAgent.Id) is { Code: "ok", Created: 1 } && raised.Count == 1
                && raised[0] is { EntityId: "S-OPEN", Status: AiDecisionLog.Open, Shadow: true, OwnerId: "OP-S1", RiskLevel: "WATCH" }
                && raised[0].RuleReferences.Contains("\"Carrier.Ask:SHORE\"") && raised[0].RuleReferences.Contains("\"Carrier.Then1:ACN\""),
                "scan SQL: tomorrow's job without a carrier — ask SHORE, then ACN, from this register's own record; in shadow, owned");
            await using (var db = new ScmosDbContext(options))
            {
                var audit = new AuditService(db, new HttpContextAccessor(), NullLogger<AuditService>.Instance);
                var workflow = new WorkflowService(db, new JobRegisterCache(db, new MemoryCache(new MemoryCacheOptions()), NullLogger<JobRegisterCache>.Instance),
                    new CarrierWebhookQueue(db, NullLogger<CarrierWebhookQueue>.Instance), audit);
                var order = await workflow.PriorityForAsync("BASF", "IMPORT", default);
                check(order.Select(one => one.Carrier).Take(2).SequenceEqual(["SHORE", "ACN"]),
                    "scan SQL: the workflow's own PriorityForAsync, over the table, gives the order the agent read from the register — one rule");
            }
            check(carrierFirst.Where(one => one.AgentId != CarrierAgent.Id).All(one => one.Code == "agent_disabled"),
                "scan SQL: the Carrier Agent runs on its own flag (AI__VendorAgentEnabled), the others on theirs");

            await Ask("SHORE", 1);
            clock.Advance(TimeSpan.FromMinutes(15));
            var asked = await Pass(carrierOn);
            var first1 = (await Decisions()).Single(one => one.Id == raised[0].Id);
            check(asked.Single(one => one.AgentId == CarrierAgent.Id).Resolved == 1
                && first1 is { Status: AiDecisionLog.Resolved, HumanChoice: "SHORE", HumanMatches: true, DecidedBy: "system" }
                && first1.OverrideReason == "ถาม SHORE แล้ว (Operator)",
                "scan SQL: SHORE asked through the workflow — the decision resolves and records the person's choice, which matched");

            await Ask("", 0, closeFirst: CarrierAssignment.Rejected);
            clock.Advance(TimeSpan.FromMinutes(15));
            await Pass(carrierOn);
            var second = (await Decisions()).Where(one => one.AgentId == CarrierAgent.Id && one.Status == AiDecisionLog.Open).ToList();
            check(second.Count == 1 && second[0].RuleReferences.Contains("\"Carrier.Ask:ACN\"") && !second[0].RuleReferences.Contains("SHORE"),
                "scan SQL: SHORE declined — a new decision recommends ACN, and SHORE is not asked twice");

            await Ask("NEWCO", 2);
            clock.Advance(TimeSpan.FromMinutes(15));
            await Pass(carrierOn);
            var skipped = (await Decisions()).Single(one => one.Id == second[0].Id);
            check(skipped is { Status: AiDecisionLog.Resolved, HumanChoice: "NEWCO", HumanMatches: false },
                "scan SQL: the operator asked NEWCO instead — recorded as a choice that did not match, for the comparison");

            await using (var db = new ScmosDbContext(options))
            {
                var service = new AiGovernanceService(db, agents, new AuditService(db, new HttpContextAccessor(), NullLogger<AuditService>.Instance),
                    Options.Create(carrierOn), clock, NullLogger<AiGovernanceService>.Instance);
                var admin = new AppUser("scan-admin", "a@test.invalid", "Admin", Roles.Admin, "AD-S1", "test", true);
                var report = await service.ReportAsync(admin, default);
                var carrierView = report.Agents.Single(one => one.Id == CarrierAgent.Id);
                var validation = report.Agents.Single(one => one.Id == ValidationAgent.Id);
                check(report.Available && carrierView is { Compared30d: 2, Matched30d: 1, ShadowMode: true, Name: "Carrier Agent" }
                    && validation is { Compared30d: 0, Matched30d: 0 },
                    "scan SQL: the governance report counts the comparison — 1 of 2 matched; a dismissal compares nothing");
            }

            /* ---------------- the Communication Agent's drafts ---------------- */
            var chatOnly = await Pass(new AiOptions { Enabled = true, CommunicationAgentEnabled = true });
            check(chatOnly.Single(one => one.AgentId == CommunicationAgent.Id).Code == "agent_disabled"
                && !(await Decisions()).Any(one => one.AgentId == CommunicationAgent.Id),
                "scan SQL: the Communication Agent's chat flag alone does not start its drafts — they have their own switch");
            var commOn = new AiOptions { Enabled = true, CommunicationAgentEnabled = true, CommunicationDraftsEnabled = true };
            await using (var db = new ScmosDbContext(options))
            {
                db.OperationJobs.AddRange(
                    Stored(Job("S-POD", ("status", "COMPLETED"), ("date", "27/09/2026"), ("arrDate", "27/09/2026"), ("arrTime", "10:00"))),
                    Stored(Job("S-POD-HELD", ("status", "COMPLETED"), ("date", "27/09/2026"), ("arrDate", "27/09/2026"), ("arrTime", "10:00"))),
                    Stored(Job("S-WROTE", ("date", "29/09/2026"), ("licence", ""), ("driver", ""))));
                db.Documents.Add(new StoredDocument { JobKey = "S-POD-HELD", Folder = "POD", FileName = "pod.pdf", ObjectKey = "test/S-POD-HELD/POD/pod.pdf", UploadedBy = "test", UploadedAt = clock.GetUtcNow() });
                db.LineEvents.Add(new LineEvent { WebhookEventId = "scan-test-wrote", JobKey = "S-WROTE", ProcessingStatus = LineProcessing.NeedReview, ReceivedAt = clock.GetUtcNow() });
                await db.SaveChangesAsync();
            }
            clock.Advance(TimeSpan.FromMinutes(90));
            var drafted = await Pass(commOn);
            var drafts = (await Decisions()).Where(one => one.AgentId == CommunicationAgent.Id).OrderBy(one => one.EntityId).ToList();
            string TemplateOf(AiDecision row) => CommunicationDrafts.TemplateOf(JsonSerializer.Deserialize<string[]>(row.RuleReferences)!) ?? "";
            check(drafted.Single(one => one.AgentId == CommunicationAgent.Id) is { Code: "ok", Created: 3 }
                && drafts.Select(one => one.EntityId).SequenceEqual(["S-FINE", "S-OPEN", "S-POD"])
                && TemplateOf(drafts[0]) == CommunicationTemplates.TruckDetailReminder
                && TemplateOf(drafts[1]) == CommunicationTemplates.ConfirmationReminder && AiDecisionLog.View(drafts[1]).Findings.Recommendations.Single().Text.StartsWith("เรียน NEWCO รบกวนยืนยันรับงาน", StringComparison.Ordinal)
                && TemplateOf(drafts[2]) == CommunicationTemplates.PodReminder
                && drafts.All(one => one is { Status: AiDecisionLog.Open, DecisionType: CommunicationDrafts.DecisionType, OwnerId: "OP-S1", RunId: "" }),
                "scan SQL: three drafts — the truck for today's job, NEWCO's unanswered request, yesterday's POD; not the job with a POD on file, not the one whose carrier already wrote");

            await using (var db = new ScmosDbContext(options))
            {
                var log = new AiDecisionLog(db, agents, new AuditService(db, new HttpContextAccessor(), NullLogger<AuditService>.Instance), clock);
                var owner = new AppUser("scan-op", "op@test.invalid", "Operator", Roles.Operation, "OP-S1", "test", true);
                check(await log.AnswerAsync(drafts[1].Id, owner, "ACCEPTED", "LINE", "", default) == "ok",
                    "scan SQL: the owner sends the reminder on LINE and says so");
            }
            clock.Advance(TimeSpan.FromMinutes(15));
            var afterSent = await Pass(commOn);
            var sent = (await Decisions()).Single(one => one.Id == drafts[1].Id);
            check(sent is { Status: AiDecisionLog.Accepted, HumanChoice: "LINE", HumanMatches: true, DecidedBy: "op@test.invalid" }
                && afterSent.Single(one => one.AgentId == CommunicationAgent.Id).Created == 0
                && (await Decisions()).Count(one => one.AgentId == CommunicationAgent.Id && one.EntityId == "S-OPEN") == 1,
                "scan SQL: sent is recorded — who, on which channel — and the same reminder is not drafted again");

            await using (var db = new ScmosDbContext(options))
            {
                db.Documents.Add(new StoredDocument { JobKey = "S-POD", Folder = "POD", FileName = "pod.pdf", ObjectKey = "test/S-POD/POD/pod.pdf", UploadedBy = "test", UploadedAt = clock.GetUtcNow() });
                await db.SaveChangesAsync();
            }
            clock.Advance(TimeSpan.FromMinutes(15));
            var podIn = await Pass(commOn);
            check(podIn.Single(one => one.AgentId == CommunicationAgent.Id).Resolved == 1
                && (await Decisions()).Single(one => one.Id == drafts[2].Id).Status == AiDecisionLog.Resolved,
                "scan SQL: the POD arrives — the draft that asked for it is resolved, never sent");
        }
        finally
        {
            if (!database.StartsWith("SCMOS_AI_SCAN_TEST_", StringComparison.Ordinal)
                || setup.Database.GetDbConnection().DataSource != "(localdb)\\MSSQLLocalDB") throw new InvalidOperationException("Unsafe cleanup target");
            await setup.Database.EnsureDeletedAsync();
        }
    }
}
