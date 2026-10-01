using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Scmos.Api.Auth;
using Scmos.Api.Data;
using Scmos.Api.Rules;
using Scmos.Api.Services;

/// <summary>
/// Annual Carrier Evaluation, Phases 1–2 (1 Oct 2026): the department's baseline as defaults, the configuration rules
/// a campaign must meet before it opens, who may do what, and — on a throwaway LocalDB — a campaign set up, counted,
/// opened and then held still.
/// </summary>
static class AnnualEvaluationChecks
{
    public static async Task RunAsync(Action<bool, string> check, bool sql)
    {
        var kpis = AnnualEvaluationRules.Kpis;
        check(kpis.Sum(kpi => kpi.Weight) == AnnualEvaluationRules.DefaultSystemWeight
            && AnnualEvaluationRules.Questions.Sum(question => question.Weight) == AnnualEvaluationRules.DefaultHumanWeight
            && AnnualEvaluationRules.DefaultSystemWeight + AnnualEvaluationRules.DefaultHumanWeight == 100m
            && kpis.Select(kpi => kpi.Code).SequenceEqual(["otd", "safety", "claim", "billing", "pricing", "documents", "certification"])
            && kpis.Select(kpi => kpi.Weight).SequenceEqual([20m, 15m, 10m, 10m, 5m, 5m, 5m]),
            "annual evaluation: the department's baseline — 70 measured (20·15·10·10·5·5·5) and 30 from six questions");
        check(kpis.Single(kpi => kpi.Code == "otd").Bands.SequenceEqual([(100m, 100m), (96m, 75m), (91m, 50m), (85m, 25m)])
            && kpis.Where(kpi => kpi.Code != "otd").All(kpi => kpi.Bands.Length == 0)
            && kpis.Single(kpi => kpi.Code == "pricing").Method == AnnualEvaluationRules.Manual,
            "annual evaluation: only OTD has bands the department gave; the rest start empty rather than invented");
        check(AnnualEvaluationRules.Eligibility(0, 5) == AnnualEvaluationRules.NoActivity
            && AnnualEvaluationRules.Eligibility(4, 5) == AnnualEvaluationRules.LimitedData
            && AnnualEvaluationRules.Eligibility(5, 5) == AnnualEvaluationRules.Full
            && AnnualEvaluationRules.Eligibility(1, 0) == AnnualEvaluationRules.Full,
            "annual evaluation: full at the minimum, limited below it, no activity at none");
        check(AnnualEvaluationRules.CanMove(AnnualEvaluationRules.Draft, AnnualEvaluationRules.DataPreparation)
            && AnnualEvaluationRules.CanMove(AnnualEvaluationRules.Ready, AnnualEvaluationRules.Open)
            && !AnnualEvaluationRules.CanMove(AnnualEvaluationRules.Draft, AnnualEvaluationRules.Open)
            && !AnnualEvaluationRules.CanMove(AnnualEvaluationRules.Open, AnnualEvaluationRules.Draft)
            && !AnnualEvaluationRules.CanMove(AnnualEvaluationRules.Finalized, AnnualEvaluationRules.Open)
            && AnnualEvaluationRules.IsDecision(AnnualEvaluationRules.Approved) && AnnualEvaluationRules.IsDecision(AnnualEvaluationRules.Finalized)
            && !AnnualEvaluationRules.IsDecision(AnnualEvaluationRules.Open),
            "annual evaluation: a campaign moves one state at a time, never back past open; approving and finalizing are decisions");
        check(!AnnualEvaluationRules.Locked(AnnualEvaluationRules.Ready) && AnnualEvaluationRules.Statuses
                .SkipWhile(status => status != AnnualEvaluationRules.Open).All(AnnualEvaluationRules.Locked),
            "annual evaluation: the rules are held still from the moment the campaign opens");
        check(Roles.Can(Roles.Operation, Capability.ViewAnnualEvaluation) && !Roles.Can(Roles.Operation, Capability.ManageAnnualEvaluation)
            && Roles.Can(Roles.Supervisor, Capability.ManageAnnualEvaluation) && !Roles.Can(Roles.Supervisor, Capability.DecideAnnualEvaluation)
            && Roles.Can(Roles.Manager, Capability.DecideAnnualEvaluation) && Roles.Can(Roles.AssistantManager, Capability.DecideAnnualEvaluation)
            && Roles.Can(Roles.Admin, Capability.DecideAnnualEvaluation)
            && !Roles.Can(Roles.Subcontractor, Capability.ViewAnnualEvaluation) && !Roles.Can(Roles.Viewer, Capability.ViewAnnualEvaluation)
            && !CarrierBoundary.Allows("GET", new PathString("/api/annual-evaluations")),
            "annual evaluation: Operation reads, supervisors run a campaign, managers decide; a carrier reaches none of it");
        check(AnnualEvaluationRules.Slug(" Driver / Service Quality ") == "driver-service-quality" && AnnualEvaluationRules.Slug("  ") == "",
            "annual evaluation: a typed code becomes letters, digits and dashes");
        if (sql) await SqlAsync(check);
    }

    private static async Task SqlAsync(Action<bool, string> check)
    {
        var database = "SCMOS_ANNUAL_EVALUATION_TEST_" + Guid.NewGuid().ToString("N");
        var connection = $"Server=(localdb)\\MSSQLLocalDB;Database={database};Integrated Security=true;TrustServerCertificate=true";
        var options = new DbContextOptionsBuilder<ScmosDbContext>().UseSqlServer(connection,
            s => { s.UseCompatibilityLevel(150); s.EnableRetryOnFailure(3); }).Options;
        await using var db = new ScmosDbContext(options);
        await db.Database.EnsureCreatedAsync();
        try
        {
            var now = DateTimeOffset.UtcNow;
            Supplier Carrier(string name, string code, string status = "approved", bool carrier = true) =>
                new() { Name = name, Code = code, Status = status, IsCarrier = carrier, CreatedAt = now, UpdatedAt = now };
            var alpha = Carrier("ALPHA TRANSPORT", "ALP");
            var bravo = Carrier("BRAVO LOGISTICS", "BRV");
            var draft = Carrier("CHARLIE", "CHL", status: "draft");
            var agent = Carrier("DELTA FREIGHT AGENT", "DLT", carrier: false);
            db.Suppliers.AddRange(alpha, bravo, draft, agent);
            await db.SaveChangesAsync();
            db.SupplierAliases.Add(new SupplierAlias { SupplierId = alpha.Id, Alias = "ALP", Source = "manual", Confirmed = true });
            // The job as the workspace stores it, so the register snapshot (Phase 3) reads the same jobs the count does.
            // J2 arrives two hours after plan with no reason recorded.
            OperationJob Job(string key, string trucker, string date, string status) =>
                new()
                {
                    Key = key, Trucker = trucker, WorkDate = date, Status = status, UpdatedAt = now,
                    Data = JsonSerializer.Serialize(new { key, date, status, trucker, planTime = "08:00", arrDate = date, arrTime = key == "J2" ? "10:00" : "07:55" }),
                };
            db.OperationJobs.AddRange(
                Job("J1", "ALPHA TRANSPORT", "15/03/2026", JobStatus.Completed), Job("J2", "ALP", "20/04/2026", JobStatus.Completed),
                Job("J3", "alpha transport", "01/05/2026", JobStatus.Completed), Job("J4", "ALPHA TRANSPORT", "02/05/2026", JobStatus.Completed),
                Job("J5", "ALPHA TRANSPORT", "03/05/2026", JobStatus.Completed), Job("J6", "ALPHA TRANSPORT", "04/05/2026", ""),
                Job("J7", "ALPHA TRANSPORT", "05/05/2026", JobStatus.Cancelled), Job("J8", "ALPHA TRANSPORT", "31/12/2025", JobStatus.Completed),
                Job("J9", "BRAVO LOGISTICS", "10/06/2026", JobStatus.Completed));
            await db.SaveChangesAsync();

            AppUser User(string id, string role) => new(id.ToLowerInvariant(), id.ToLowerInvariant() + "@test.invalid", id, role, id, "test", true);
            var operation = User("OP-1", Roles.Operation);
            var supervisor = User("SV-1", Roles.Supervisor);
            var manager = User("MG-1", Roles.Manager);
            var auditing = new AuditService(db, new HttpContextAccessor(), NullLogger<AuditService>.Instance);
            var directory = new CarrierDirectory(db, new MemoryCache(new MemoryCacheOptions()));
            var service = new AnnualEvaluationService(db, auditing, directory);
            var snapshots = new EvaluationSnapshotService(db, auditing,
                new JobRegisterCache(db, new MemoryCache(new MemoryCacheOptions()), NullLogger<JobRegisterCache>.Instance), directory);
            decimal? Figure(SnapshotView view, string code) => view.Metrics.Single(metric => metric.Code == code).Value;
            var scoring = new EvaluationScoringService(db, auditing);

            var refused = await service.CreateAsync(operation, new CreateCampaignInput(2026, null, null), default);
            var first = await service.CreateAsync(supervisor, new CreateCampaignInput(2026, null, null), default);
            var again = await service.CreateAsync(supervisor, new CreateCampaignInput(2026, "Re-run", null), default);
            var id = (int)first.Id!.Value;
            var view = (await service.ReadAsync(operation, id, default))!;
            var drivers = view.Questions.Single(question => question.Code == "service");
            var asked = drivers.Departments.Where(row => row.Enabled).Select(row => view.Departments.Single(one => one.DepartmentId == row.DepartmentId).Code).Order();
            check(!refused.Ok && first.Ok && again.Ok && view.Campaign.Code == "AE-2026"
                && (await db.EvaluationCampaigns.FindAsync((int)again.Id!.Value))!.Code == "AE-2026-2"
                && view.Campaign.PeriodStart == new DateOnly(2026, 1, 1) && view.Campaign.PeriodEnd == new DateOnly(2026, 12, 31)
                && view.Kpis.Count == 7 && view.Questions.Count == 6 && view.Departments.Count == 7 && view.Departments.All(row => row.Enabled && row.Weight == 1m)
                && asked.SequenceEqual(["cs", "ehsq", "ops"]) && !view.CanManage,
                "annual evaluation: a campaign starts from the baseline — seven KPIs, six questions, seven departments, drivers asked of Ops, CS and EHSQ only");
            check(view.Problems.Any(problem => problem.Contains("Score band")) && view.Problems.Any(problem => problem.Contains("วันเปิด"))
                && view.Problems.Any(problem => problem.StartsWith("KPI Safety")) && !view.Problems.Any(problem => problem.StartsWith("KPI OTD"))
                && view.Problems.Any(problem => problem.Contains("ผู้ขนส่ง")),
                "annual evaluation: a new campaign says what is missing before it may open — dates, bands nobody gave, score bands, carriers");

            var badCode = await service.SaveKpisAsync(supervisor, id, [new KpiInput("speed", 70, true, "band", "higher", null, 0, [])], default);
            var kpiInput = view.Kpis.Select(kpi => new KpiInput(kpi.Code, kpi.Code == "otd" ? 19.99m : kpi.Weight, kpi.Enabled, kpi.Method, kpi.Direction, null,
                kpi.FallbackScore, kpi.Bands.Count > 0 ? kpi.Bands : kpi.Method == "band" ? [new KpiBandInput(kpi.Direction == "lower" ? 0 : 100, 100), new KpiBandInput(kpi.Direction == "lower" ? 2 : 90, 50)] : [])).ToList();
            await service.SaveKpisAsync(supervisor, id, kpiInput, default);
            var offByCents = (await service.ReadAsync(operation, id, default))!.Problems;
            await service.SaveKpisAsync(supervisor, id, kpiInput.Select(kpi => kpi with { Weight = kpi.Code == "otd" ? 20m : kpi.Weight }).ToList(), default);
            await service.SaveScoreBandsAsync(supervisor, id, [new ScoreBandInput("excellent", "Excellent", 90), new ScoreBandInput("needs-review", "Management review", 0)], default);
            await service.UpdateAsync(supervisor, id, new CampaignInput(null, null, null, "2026-11-01", "2026-11-30", null, null, null, null, null), default);
            var add = await service.ChangeCarriersAsync(supervisor, id, new CarrierChangeInput("add-eligible", null, null), default);
            var addAgent = await service.ChangeCarriersAsync(supervisor, id, new CarrierChangeInput("add", [agent.Id], null), default);
            var excludeNoReason = await service.ChangeCarriersAsync(supervisor, id, new CarrierChangeInput("exclude", [bravo.Id], null), default);
            var rows = (await service.CarriersAsync(operation, id, default))!;
            var alphaRow = rows.Single(row => row.SupplierId == alpha.Id);
            var bravoRow = rows.Single(row => row.SupplierId == bravo.Id);
            check(!badCode.Ok && offByCents.Any(problem => problem.Contains("69.99")) && add.Ok && rows.Count == 2
                && rows.All(row => row.SupplierId != draft.Id) && !addAgent.Ok && !excludeNoReason.Ok
                && alphaRow.TotalJobs == 6 && alphaRow.CompletedJobs == 5 && alphaRow.Eligibility == AnnualEvaluationRules.Full
                && bravoRow.CompletedJobs == 1 && bravoRow.Eligibility == AnnualEvaluationRules.LimitedData,
                "annual evaluation: weights are compared exactly; approved carriers are added and counted in the period through their spellings, cancelled jobs left out");

            // Phase 3: the evidence. Without it the campaign may not open; with it, each carrier's figures are its own.
            var withoutEvidence = (await service.ReadAsync(operation, id, default))!.Problems;
            var notAllowed = await snapshots.GenerateAsync(operation, id, null, null, default);
            var taken = await snapshots.GenerateAsync(supervisor, id, null, null, default);
            var alphaEvidence = (await snapshots.ReadAsync(operation, id, alphaRow.Id, null, default))!;
            var bravoEvidence = (await snapshots.ReadAsync(operation, id, bravoRow.Id, null, default))!;
            check(withoutEvidence.Any(problem => problem.Contains("snapshot")) && !notAllowed.Ok && taken.Ok
                && alphaEvidence.Snapshot!.Version == 1 && alphaEvidence.Snapshot.Current
                && Figure(alphaEvidence, "total-jobs") == 6 && Figure(alphaEvidence, "completed-jobs") == 5
                && Figure(alphaEvidence, "late-unclassified") == 1 && Figure(alphaEvidence, "carrier-otd") == 100m
                && Figure(alphaEvidence, "operational-otd") == Math.Round(500m / 6, 4)
                && alphaEvidence.Metrics.Single(metric => metric.Code == "late-unclassified").Sources.SequenceEqual(["J2"])
                && Figure(bravoEvidence, "total-jobs") == 1
                && bravoEvidence.Metrics.Single(metric => metric.Code == "carrier-otd").Status == AnnualEvaluationRules.InsufficientData
                && alphaEvidence.Metrics.Single(metric => metric.Code == "pricing").Status == AnnualEvaluationRules.NotAvailable,
                "annual evaluation: a snapshot holds each carrier's own figures, from the register's jobs — a late job with no reason is shown, not charged");

            var noReady = await service.MoveAsync(supervisor, id, AnnualEvaluationRules.Open, null, default);
            var problems = (await service.ReadAsync(operation, id, default))!.Problems;
            await service.MoveAsync(supervisor, id, AnnualEvaluationRules.DataPreparation, null, default);
            var ready = await service.MoveAsync(supervisor, id, AnnualEvaluationRules.Ready, null, default);
            var opened = await service.MoveAsync(supervisor, id, AnnualEvaluationRules.Open, null, default);
            var lockedKpis = await service.SaveKpisAsync(supervisor, id, kpiInput, default);
            var lockedUpdate = await service.UpdateAsync(supervisor, id, new CampaignInput("x", null, null, null, null, null, null, null, null, null), default);
            var lockedCarriers = await service.ChangeCarriersAsync(supervisor, id, new CarrierChangeInput("exclude", [bravo.Id], "late"), default);
            var lockedNoReason = await snapshots.GenerateAsync(supervisor, id, null, null, default);
            var corrected = await snapshots.GenerateAsync(supervisor, id, [alphaRow.Id], "POD uploaded late, re-read", default);
            var versions = (await snapshots.ReadAsync(operation, id, alphaRow.Id, null, default))!.Versions;
            var firstVersion = (await snapshots.ReadAsync(operation, id, alphaRow.Id, 1, default))!;
            check(!lockedNoReason.Ok && corrected.Ok && versions.Count == 2 && versions.Single(one => one.Current).Version == 2
                && versions.Single(one => one.Version == 2).Reason == "POD uploaded late, re-read"
                && firstVersion.Metrics.Count == alphaEvidence.Metrics.Count && Figure(firstVersion, "total-jobs") == 6
                && (await snapshots.ReadAsync(operation, id, bravoRow.Id, null, default))!.Versions.Count == 1,
                "annual evaluation: once open, new evidence needs a reason and is a new version; the old one stays exactly as it was");

            // Phase 4: scoring. Pricing is assessed by hand; one Operation evaluator answers for ALPHA, N/A on drivers.
            var manualByOperation = await scoring.SetManualScoreAsync(operation, id, alphaRow.Id, "pricing", 80, "Rates in line with RFQ", default);
            var manualNoNote = await scoring.SetManualScoreAsync(supervisor, id, alphaRow.Id, "pricing", 80, "", default);
            var notManual = await scoring.SetManualScoreAsync(supervisor, id, alphaRow.Id, "otd", 80, "not a manual KPI", default);
            var manualOk = await scoring.SetManualScoreAsync(supervisor, id, alphaRow.Id, "pricing", 80, "Rates in line with RFQ round", default);
            var opsDepartment = await db.EvaluationDepartments.FirstAsync(row => row.Code == "ops");
            var questions = await db.EvaluationQuestions.Where(row => row.CampaignId == id).ToListAsync();
            var evaluator = new EvaluationEvaluator { CampaignId = id, Name = "Ops reviewer", Email = "ops@test.invalid", DepartmentId = opsDepartment.Id, CreatedAt = now };
            db.EvaluationEvaluators.Add(evaluator);
            await db.SaveChangesAsync();
            var invitation = new EvaluationInvitation
            {
                CampaignId = id, EvaluatorId = evaluator.Id, EvaluationCarrierId = alphaRow.Id, TokenHash = new string('a', 64),
                Status = AnnualEvaluationRules.InvitationSubmitted, ExpiresAt = now.AddDays(30), CreatedAt = now,
            };
            db.EvaluationInvitations.Add(invitation);
            await db.SaveChangesAsync();
            var response = new EvaluationResponse
            {
                InvitationId = invitation.Id, CampaignId = id, EvaluationCarrierId = alphaRow.Id, EvaluatorId = evaluator.Id,
                DepartmentId = opsDepartment.Id, SubmittedAt = now,
            };
            db.EvaluationResponses.Add(response);
            await db.SaveChangesAsync();
            db.EvaluationAnswers.AddRange(questions.Select(question => new EvaluationAnswer
                { ResponseId = response.Id, QuestionId = question.Id, Rating = question.Code == "service" ? null : 4 }));
            await db.SaveChangesAsync();

            var scored = await scoring.CalculateAsync(supervisor, id, null, null, default);
            var alphaScore = (await scoring.ResultAsync(operation, id, alphaRow.Id, null, default))!;
            var bravoScore = (await scoring.ResultAsync(operation, id, bravoRow.Id, null, default))!;
            // ALPHA: OTD 100×20, safety 100×15, claims 100×10, pricing 80×5, POD 0×5, documents 0×5 over 60 of 70 (billing has no
            // invoices); departments 80 (rating 4, drivers N/A).
            // Held to the four places it is stored at, and the final score worked from the held parts: 81.6667 × 0.7 + 80 × 0.3.
            var system = Math.Round(4900m / 60m, 4, MidpointRounding.AwayFromZero);
            check(!manualByOperation.Ok && !manualNoNote.Ok && !notManual.Ok && manualOk.Ok && scored.Ok
                && alphaScore.Row.SystemScore == 81.6667m && system == 81.6667m && alphaScore.Row.SystemWeightAvailable == 60
                && alphaScore.Row.HumanScore == 80m
                && alphaScore.Row.FinalScore == Math.Round(system * 70m / 100m + 80m * 30m / 100m, 4, MidpointRounding.AwayFromZero)
                && alphaScore.Row.FinalScore == 81.1667m && alphaScore.Row.Band == "needs-review"
                && alphaScore.Row.Status == EvaluationScoring.Calculated
                && bravoScore.Row.FinalScore is null && bravoScore.Row.Status == EvaluationScoring.Incomplete
                && alphaScore.Detail.GetProperty("kpis").EnumerateArray().Any(kpi => kpi.GetProperty("code").GetString() == "billing"
                    && !kpi.GetProperty("counted").GetBoolean())
                && await db.EvaluationDepartmentScores.CountAsync(row => row.ResultId == db.EvaluationResults.First(one => one.EvaluationCarrierId == alphaRow.Id && one.Current).Id) == 7,
                "annual evaluation: a carrier is scored from its snapshot, the manual pricing and its departments — the breakdown kept; one with too little evidence has no final score");

            var backNoReason = await service.MoveAsync(supervisor, id, AnnualEvaluationRules.Closed, null, default);
            var recalculateNoReason = await scoring.CalculateAsync(supervisor, id, null, null, default);
            var recalculated = await scoring.CalculateAsync(supervisor, id, [alphaRow.Id], "Late Ops response added", default);
            var alphaAgain = (await scoring.ResultAsync(operation, id, alphaRow.Id, null, default))!;
            var alphaFirst = (await scoring.ResultAsync(operation, id, alphaRow.Id, 1, default))!;
            var board = (await scoring.ResultsAsync(operation, id, default))!;
            check(!recalculateNoReason.Ok && recalculated.Ok && alphaAgain.Row.Version == 2 && alphaAgain.Versions.SequenceEqual([2, 1])
                && alphaAgain.Row.Reason == "Late Ops response added" && alphaFirst.Row.FinalScore == alphaScore.Row.FinalScore
                && board.Count == 2 && board[0].SupplierId == alpha.Id,
                "annual evaluation: after the campaign closes a recalculation needs a reason and is a new version; the first stays as it was");
            var reopenNoReason = await service.MoveAsync(supervisor, id, AnnualEvaluationRules.Open, null, default);
            await service.MoveAsync(supervisor, id, AnnualEvaluationRules.UnderReview, null, default);
            var supervisorApprove = await service.MoveAsync(supervisor, id, AnnualEvaluationRules.Approved, null, default);
            var managerApprove = await service.MoveAsync(manager, id, AnnualEvaluationRules.Approved, null, default);
            var campaign = await db.EvaluationCampaigns.AsNoTracking().FirstAsync(row => row.Id == id);
            var afterApproval = await snapshots.GenerateAsync(supervisor, id, null, "late correction", default);
            var scoreAfterApproval = await scoring.CalculateAsync(supervisor, id, null, "late correction", default);
            var manualAfterApproval = await scoring.SetManualScoreAsync(supervisor, id, alphaRow.Id, "pricing", 90, "late correction", default);
            check(!afterApproval.Ok && !scoreAfterApproval.Ok && !manualAfterApproval.Ok,
                "annual evaluation: an approved campaign takes no new evidence, scores or assessments");
            check(!noReady.Ok && problems.Count == 0 && ready.Ok && opened.Ok && campaign.LockedAt is not null && campaign.LockedBy.Length > 0
                && !lockedKpis.Ok && lockedKpis.Status == StatusCodes.Status409Conflict && !lockedUpdate.Ok && !lockedCarriers.Ok
                && backNoReason.Ok && !reopenNoReason.Ok && !supervisorApprove.Ok && managerApprove.Ok && campaign.Status == AnnualEvaluationRules.Approved,
                "annual evaluation: a valid campaign opens through ready and is then locked; reopening needs a reason; only a manager approves");
            check(await db.AuditEvents.CountAsync(row => row.Entity == "annual-evaluation" && row.EntityId == id.ToString()) >= 10,
                "annual evaluation: every change to a campaign is in the audit trail");
        }
        finally
        {
            await db.Database.EnsureDeletedAsync();
        }
    }
}
