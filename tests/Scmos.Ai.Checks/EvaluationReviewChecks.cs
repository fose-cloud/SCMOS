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
/// Annual Carrier Evaluation, Phases 8–9 (2 Oct 2026): department weighting, a score that no longer matches its evidence,
/// the decision kept apart from the score, what approval waits for, and finalizing into the Supplier Register — which
/// never changes a carrier's status on its own.
/// </summary>
static class EvaluationReviewChecks
{
    public static async Task RunAsync(Action<bool, string> check, bool sql)
    {
        // Phase 8: the department part, weighted. Operation counts twice EHSQ; a department weighted nought or silent is left out.
        var weights = new Dictionary<int, decimal> { [1] = 5, [2] = 5 };
        const int ops = 1, ehsq = 2, finance = 3, sales = 4;
        List<EvaluationScoring.Answered> answered =
        [
            new(1, ops, [new(1, 5), new(2, 5)]), new(2, ops, [new(1, 3), new(2, 3)]),
            new(3, ehsq, [new(1, 1), new(2, 1)]), new(4, finance, [new(1, 1), new(2, 1)]),
        ];
        var (lines, human) = EvaluationScoring.HumanScore(answered, weights,
            new Dictionary<int, decimal> { [ops] = 2, [ehsq] = 1, [finance] = 0, [sales] = 1 });
        check(lines.Single(line => line.DepartmentId == ops).Score == 80m && lines.Single(line => line.DepartmentId == ops).Responses == 2
            && Math.Round(human!.Value, 4) == 60m && lines.Single(line => line.DepartmentId == sales).Score is null,
            "aggregation: two Operation evaluators average to 80, weighted 2 against EHSQ's 20 at 1 — (160 + 20) ÷ 3 = 60; Finance at nought and silent Sales are left out");

        var at = new DateTimeOffset(2026, 10, 2, 9, 0, 0, TimeSpan.Zero);
        EvaluationReview.Freshness Fresh(DateTimeOffset? response = null, DateTimeOffset? manual = null, long? snapshot = 7, int version = 3) =>
            new(at, 7, 3, snapshot, version, response, manual);
        check(EvaluationReview.Stale(Fresh(at.AddHours(-1), at.AddHours(-2))).Count == 0
            && EvaluationReview.Stale(Fresh(response: at.AddMinutes(1))).Single().Contains("ผู้ประเมิน")
            && EvaluationReview.Stale(Fresh(manual: at.AddMinutes(1))).Single().Contains("ด้วยมือ")
            && EvaluationReview.Stale(Fresh(snapshot: 8)).Single().Contains("snapshot")
            && EvaluationReview.Stale(Fresh(version: 4)).Single().Contains("เกณฑ์")
            && EvaluationReview.Stale(Fresh(at.AddMinutes(1), at.AddMinutes(1), 8, 4)).Count == 4,
            "aggregation: a score is out of date once an answer, a manual score, a new snapshot or a rule change comes after it");
        check(EvaluationReview.Completion(3, 4) == 75m && EvaluationReview.Completion(1, 3) == 33.33m && EvaluationReview.Completion(0, 0) is null,
            "aggregation: completion is answers over the links still live, and nothing when nobody was asked");

        check(EvaluationReview.Decisions.Select(decision => decision.Code).Distinct().Count() == 6
            && EvaluationReview.Decisions.All(decision => decision.Label.Length > 0)
            && EvaluationReview.IsDecision(EvaluationReview.Inactive) && !EvaluationReview.IsDecision("pass") && !EvaluationReview.IsDecision("")
            && !EvaluationReview.NeedsNote(EvaluationReview.Continue, "") && !EvaluationReview.NeedsNote(EvaluationReview.Continue, EvaluationReview.Continue)
            && EvaluationReview.NeedsNote(EvaluationReview.Continue, EvaluationReview.Inactive)
            && EvaluationReview.NeedsNote(EvaluationReview.CorrectiveAction, "")
            && EvaluationReview.NeedsRegisterFollowUp(EvaluationReview.SuspendNewAllocation) && !EvaluationReview.NeedsRegisterFollowUp(EvaluationReview.CorrectiveAction)
            && EvaluationReview.NeedsActionPlan(EvaluationReview.ContinueWithPlan) && !EvaluationReview.NeedsActionPlan(EvaluationReview.Continue),
            "review: six decisions, none of them a score; anything but continuing — and any change — carries its reason");
        EvaluationReview.CarrierState State(string name, bool calculated = true, bool stale = false, string decision = EvaluationReview.Continue) =>
            new(name, calculated, stale, decision);
        var blocked = EvaluationReview.ApprovalProblems([State("A", calculated: false), State("B", stale: true), State("C", decision: ""), State("D")]);
        check(EvaluationReview.ApprovalProblems([]).Count == 1 && blocked.Count == 3 && blocked[0].Contains("A") && blocked[1].Contains("B")
            && blocked[2].Contains("C") && EvaluationReview.ApprovalProblems([State("A"), State("B", decision: EvaluationReview.Inactive)]).Count == 0,
            "review: approval waits for a score, a current one, and a decision on every carrier");

        if (sql) await SqlAsync(check);
    }

    private static async Task SqlAsync(Action<bool, string> check)
    {
        var database = "SCMOS_EVALUATION_REVIEW_TEST_" + Guid.NewGuid().ToString("N");
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
            OperationJob Job(string key, string trucker, string date) => new()
            {
                Key = key, Trucker = trucker, WorkDate = date, Status = JobStatus.Completed, UpdatedAt = now,
                Data = JsonSerializer.Serialize(new { key, date, status = JobStatus.Completed, trucker, planTime = "08:00", arrDate = date, arrTime = "07:50" }),
            };
            db.OperationJobs.AddRange(Job("R1", "ALPHA TRANSPORT", "10/03/2026"), Job("R2", "ALPHA TRANSPORT", "11/03/2026"), Job("R3", "BRAVO LOGISTICS", "12/03/2026"));
            // The screen this module replaced wrote "2026" for ALPHA; that row is history and stays as it is.
            db.SupplierEvaluations.Add(new SupplierEvaluation { SupplierId = alpha.Id, Period = "2026", TotalScore = 71, Grade = "B", Stage = "submitted", CreatedAt = now });
            await db.SaveChangesAsync();

            AppUser User(string id, string role) => new(id.ToLowerInvariant(), id.ToLowerInvariant() + "@test.invalid", id, role, id, "test", true);
            var operation = User("OP-1", Roles.Operation);
            var supervisor = User("SV-1", Roles.Supervisor);
            var manager = User("MG-1", Roles.Manager);
            var auditing = new AuditService(db, new HttpContextAccessor(), NullLogger<AuditService>.Instance);
            var directory = new CarrierDirectory(db, new MemoryCache(new MemoryCacheOptions()));
            var campaigns = new AnnualEvaluationService(db, auditing, directory);
            var snapshots = new EvaluationSnapshotService(db, auditing,
                new JobRegisterCache(db, new MemoryCache(new MemoryCacheOptions()), NullLogger<JobRegisterCache>.Instance), directory);
            var invitations = new EvaluationInvitationService(db, auditing);
            var external = new ExternalEvaluationService(db, auditing);
            var scoring = new EvaluationScoringService(db, auditing);
            var review = new EvaluationReviewService(db, auditing);

            var id = (int)(await campaigns.CreateAsync(supervisor, new CreateCampaignInput(2026, null, null), default)).Id!.Value;
            await campaigns.ChangeCarriersAsync(supervisor, id, new CarrierChangeInput("add-eligible", null, null), default);
            var alphaRow = await db.EvaluationCarriers.AsNoTracking().SingleAsync(row => row.CampaignId == id && row.SupplierId == alpha.Id);
            var bravoRow = await db.EvaluationCarriers.AsNoTracking().SingleAsync(row => row.CampaignId == id && row.SupplierId == bravo.Id);
            await campaigns.MoveAsync(supervisor, id, AnnualEvaluationRules.DataPreparation, null, default);
            await snapshots.GenerateAsync(supervisor, id, null, null, default);
            var opsDepartment = await db.EvaluationDepartments.SingleAsync(row => row.Code == "ops");
            await invitations.AddEvaluatorAsync(supervisor, id, new EvaluatorInput("Ops reviewer", "ops@test.invalid", opsDepartment.Id), default);
            var evaluatorId = (await db.EvaluationEvaluators.SingleAsync(row => row.CampaignId == id)).Id;
            var links = (await invitations.GenerateAsync(supervisor, id, new InvitationRequest([evaluatorId], null, null), default)).Links!;

            // Open for answers today. The opening rules are Phase 2's; here only what comes after them is under test.
            var campaign = await db.EvaluationCampaigns.SingleAsync(row => row.Id == id);
            var today = DateOnly.FromDateTime(DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(7)).DateTime);
            campaign.Status = AnnualEvaluationRules.Open;
            campaign.OpenOn = today.AddDays(-1);
            campaign.DueOn = today.AddDays(5);
            campaign.MinimumSystemCoverage = 0;
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();

            await scoring.SetManualScoreAsync(supervisor, id, alphaRow.Id, "pricing", 80, "Rates in line with the RFQ", default);
            await scoring.SetManualScoreAsync(supervisor, id, bravoRow.Id, "pricing", 70, "Rates above the RFQ", default);
            var alphaLink = links.Single(link => link.Carrier == "ALPHA TRANSPORT");
            var sheet = (await external.ReadAsync(alphaLink.Token, default)).View!;
            await external.SubmitAsync(alphaLink.Token, new ExternalSubmission(
                sheet.Questions.Select(question => new EvaluationInvitations.Answer(question.Code, 4, false, "")).ToList(), ""), default);
            var calculated = await scoring.CalculateAsync(supervisor, id, null, null, default);
            var board = (await scoring.ResultsAsync(operation, id, default))!;
            var summary = (await review.SummaryAsync(operation, id, default))!;
            var alphaBoard = board.Single(row => row.SupplierId == alpha.Id);
            var opsLine = alphaBoard.Departments!.Single(line => line.DepartmentId == opsDepartment.Id);
            check(calculated.Ok && alphaBoard.Invited == 1 && alphaBoard.Responses == 1 && alphaBoard.Stale!.Count == 0
                && opsLine.Score == 80m && opsLine.Responses == 1 && opsLine.Invited == 1 && alphaBoard.HumanScore == 80m && alphaBoard.FinalScore is not null
                && board.Single(row => row.SupplierId == bravo.Id).FinalScore is null
                && summary.Carriers == 2 && summary.Evaluators == 1 && summary.Invited == 2 && summary.Responses == 1 && summary.Pending == 1
                && summary.Completion == 50m && summary.Calculated == 1 && summary.Insufficient == 1 && summary.NotCalculated == 0 && summary.Stale == 0
                && summary.ApprovalProblems.Count == 0,
                "board: each carrier's answers by department, its score and the campaign's completion — one of two links answered is 50%");

            await scoring.SetManualScoreAsync(supervisor, id, alphaRow.Id, "pricing", 85, "Second RFQ round came in", default);
            var stale = (await scoring.ResultsAsync(operation, id, default))!.Single(row => row.SupplierId == alpha.Id);
            var decideWhileOpen = await review.DecideAsync(manager, id, alphaRow.Id, EvaluationReview.Continue, "", default);
            check(stale.Stale!.Single().Contains("ด้วยมือ") && (await review.SummaryAsync(operation, id, default))!.Stale == 1
                && decideWhileOpen.Status == StatusCodes.Status409Conflict,
                "board: a manual score changed after the calculation marks the score out of date; nothing is decided while answers are still coming in");

            await campaigns.MoveAsync(supervisor, id, AnnualEvaluationRules.Closed, null, default);
            await campaigns.MoveAsync(supervisor, id, AnnualEvaluationRules.UnderReview, null, default);
            var blocked = await campaigns.MoveAsync(manager, id, AnnualEvaluationRules.Approved, null, default);
            var byOperation = await review.DecideAsync(operation, id, alphaRow.Id, EvaluationReview.Continue, "", default);
            var unknown = await review.DecideAsync(manager, id, alphaRow.Id, "pass", "", default);
            var inactiveNoNote = await review.DecideAsync(manager, id, bravoRow.Id, EvaluationReview.Inactive, "", default);
            var inactive = await review.DecideAsync(manager, id, bravoRow.Id, EvaluationReview.Inactive, "One job in the year and above-RFQ rates", default);
            var continued = await review.DecideAsync(supervisor, id, alphaRow.Id, EvaluationReview.Continue, "", default);
            var changedNoNote = await review.DecideAsync(manager, id, alphaRow.Id, EvaluationReview.CorrectiveAction, "", default);
            var changed = await review.DecideAsync(manager, id, alphaRow.Id, EvaluationReview.ContinueWithPlan, "POD compliance below target", default);
            var stillStale = await campaigns.MoveAsync(manager, id, AnnualEvaluationRules.Approved, null, default);
            var recalculated = await scoring.CalculateAsync(supervisor, id, [alphaRow.Id], "Second RFQ round assessed", default);
            var midReview = (await review.SummaryAsync(operation, id, default))!;
            var supervisorApprove = await campaigns.MoveAsync(supervisor, id, AnnualEvaluationRules.Approved, null, default);
            var approved = await campaigns.MoveAsync(manager, id, AnnualEvaluationRules.Approved, null, default);
            var decideAfterApproval = await review.DecideAsync(manager, id, alphaRow.Id, EvaluationReview.Continue, "changed my mind", default);
            check(blocked.Message.Contains("ต้องคำนวณใหม่") && blocked.Message.Contains("ยังไม่ได้ตัดสิน") && byOperation.Status == StatusCodes.Status403Forbidden
                && !unknown.Ok && !inactiveNoNote.Ok && inactive.Ok && continued.Ok && !changedNoNote.Ok && changed.Ok
                && !stillStale.Ok && stillStale.Message.Contains("ต้องคำนวณใหม่") && !stillStale.Message.Contains("ยังไม่ได้ตัดสิน")
                && recalculated.Ok && midReview.ApprovalProblems.Count == 0 && midReview.Decided == 2
                && midReview.FollowUps.Single().SupplierId == bravo.Id && midReview.FollowUps.Single().Decision == EvaluationReview.Inactive
                && !supervisorApprove.Ok && approved.Ok && decideAfterApproval.Status == StatusCodes.Status409Conflict,
                "review: a decision needs its reason; approval waits until every score is current and every carrier decided, and only a manager gives it");

            var alphaResult = (await scoring.ResultAsync(operation, id, alphaRow.Id, null, default))!.Row;
            var finalized = await campaigns.MoveAsync(manager, id, AnnualEvaluationRules.Finalized, null, default);
            db.ChangeTracker.Clear();
            var published = await db.SupplierEvaluations.AsNoTracking().Where(row => row.Period == "AE-2026").ToListAsync();
            var alphaPublished = published.Single(row => row.SupplierId == alpha.Id);
            var bravoPublished = published.Single(row => row.SupplierId == bravo.Id);
            var alphaNow = await db.Suppliers.AsNoTracking().SingleAsync(row => row.Id == alpha.Id);
            var bravoNow = await db.Suppliers.AsNoTracking().SingleAsync(row => row.Id == bravo.Id);
            var oldRow = await db.SupplierEvaluations.AsNoTracking().SingleAsync(row => row.SupplierId == alpha.Id && row.Period == "2026");
            check(finalized.Ok && published.Count == 2 && published.All(row => row.Source == SupplierEvaluation.CampaignSource && row.Stage == "approved")
                && alphaPublished.FinalPercent == alphaResult.FinalScore
                && alphaPublished.TotalScore == (int)Math.Round(alphaResult.FinalScore!.Value, 0, MidpointRounding.AwayFromZero)
                && alphaPublished.Note.StartsWith(EvaluationReview.LabelOf(EvaluationReview.ContinueWithPlan)) && alphaPublished.Note.Contains("POD compliance")
                && alphaNow.LastScore == alphaPublished.TotalScore && alphaNow.LastEvaluatedPeriod == "AE-2026"
                && bravoPublished.FinalPercent is null && bravoPublished.TotalScore is null && bravoPublished.Result == "ข้อมูลไม่พอ"
                && bravoNow.LastScore is null && bravoNow.Status == "approved"
                && oldRow.TotalScore == 71 && oldRow.Grade == "B" && oldRow.Source == SupplierEvaluation.ScmosSource
                && (await review.SummaryAsync(operation, id, default))!.Published == 2,
                "finalize: each carrier's result goes into its evaluation history — the old screen's row kept — and an 'inactive' decision does not retire the carrier by itself");

            var calculateAfter = await scoring.CalculateAsync(supervisor, id, null, "late correction", default);
            var decideAfter = await review.DecideAsync(manager, id, alphaRow.Id, EvaluationReview.Continue, "late correction", default);
            var audits = await db.AuditEvents.AsNoTracking().Where(row => row.Entity == "annual-evaluation" && row.EntityId == id.ToString()).ToListAsync();
            check(!calculateAfter.Ok && !decideAfter.Ok
                && audits.Count(row => row.Field == $"decision:{alphaRow.Id}") == 2 && audits.Any(row => row.Field == $"decision:{bravoRow.Id}" && row.Reason.Contains("RFQ"))
                && audits.Any(row => row.Field == "published") && audits.Any(row => row.Field == "status" && row.NewValue == AnnualEvaluationRules.Finalized),
                "finalize: a finalized campaign takes no new scores or decisions; every decision, the publishing and the move are in the audit trail");
        }
        finally
        {
            await db.Database.EnsureDeletedAsync();
        }
    }
}
