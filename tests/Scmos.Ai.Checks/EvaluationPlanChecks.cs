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
/// Annual Carrier Evaluation, Phase 10 (2 Oct 2026): a carrier's improvement plan is an Action Plan — drafted from what
/// the evaluation found, created by the Action Plan module's own rules, and listed back on the campaign by its reference.
/// </summary>
static class EvaluationPlanChecks
{
    public static async Task RunAsync(Action<bool, string> check, bool sql)
    {
        EvaluationScoring.Band[] otd = [new(100, 100), new(96, 75), new(91, 50), new(85, 25)];
        check(EvaluationFindings.TargetOf(otd, AnnualEvaluationRules.Higher) == 100m
            && EvaluationFindings.TargetOf([new(0, 100), new(1, 60)], AnnualEvaluationRules.Lower) == 0m
            && EvaluationFindings.TargetOf([new(95, 100), new(97, 100), new(90, 50)], AnnualEvaluationRules.Higher) == 95m
            && EvaluationFindings.TargetOf([new(0, 100), new(0.5m, 100), new(2, 40)], AnnualEvaluationRules.Lower) == 0.5m
            && EvaluationFindings.TargetOf([], AnnualEvaluationRules.Higher) is null,
            "findings: a KPI's target is the easiest value that still scores in full; none without bands");

        List<EvaluationFindings.KpiBasis> kpis =
        [
            new("otd", "OTD", 20, AnnualEvaluationRules.Higher, AnnualEvaluationRules.Band, 92.5m, 50m, true, 100m),
            new("safety", "Safety", 15, AnnualEvaluationRules.Lower, AnnualEvaluationRules.Band, 0.8m, 60m, true, 0m),
            new("pricing", "Pricing", 5, AnnualEvaluationRules.Higher, AnnualEvaluationRules.Manual, null, 70m, true, null),
            new("billing", "Billing", 10, AnnualEvaluationRules.Higher, AnnualEvaluationRules.Band, 100m, 100m, true, 98m),
            new("documents", "Documents", 5, AnnualEvaluationRules.Higher, AnnualEvaluationRules.Band, null, null, false, 100m),
        ];
        List<EvaluationFindings.QuestionBasis> questions =
            [new("communication", "การสื่อสาร", 3.5m, 4), new("capacity", "ความพร้อมของรถ", 2m, 3), new("service", "บริการ", 4m, 2), new("satisfaction", "พึงพอใจ", null, 0)];
        var findings = EvaluationFindings.Of(kpis, questions);
        check(findings.Select(finding => finding.Code).SequenceEqual(["otd", "safety", "pricing", "capacity", "communication"])
            && findings[0].PointsLost == 10m && findings[1].PointsLost == 6m && findings[2].PointsLost == 1.5m
            && findings[0].Text.Contains("92.5") && findings[0].Text.Contains("≥ 100") && findings[1].Text.Contains("≤ 0")
            && findings[2].Text.Contains("70/100") && findings[3].Text.Contains("2/5") && findings[3].Text.Contains("3 คน"),
            "findings: KPIs below full marks by the points they cost, then questions under 'good' from the lowest — full marks, no figure and no ratings are not findings");

        var categories = ActionPlanRules.DefaultCategories[ActionPlanRules.Subcontractor];
        check(AnnualEvaluationRules.Kpis.All(kpi => categories.Contains(EvaluationFindings.CategoryOf(new("kpi", kpi.Code, kpi.Name, null, null, 50, 1, ""))))
            && EvaluationFindings.CategoryOf(findings[1]) == "Safety Improvement" && EvaluationFindings.CategoryOf(findings[3]) == "Carrier Performance Development"
            && EvaluationFindings.CategoryOf(null) == "Carrier Performance Development"
            && EvaluationFindings.PriorityOf(EvaluationReview.CorrectiveAction) == "high" && EvaluationFindings.PriorityOf(EvaluationReview.Inactive) == "critical"
            && EvaluationFindings.PriorityOf(EvaluationReview.ContinueWithPlan) == "medium" && ActionPlanRules.Priorities.Contains(EvaluationFindings.PriorityOf(""))
            && ActionPlanService.ReferenceKinds.Contains(EvaluationPlanService.ReferenceKind),
            "findings: the plan's category is one the Action Plan module already has, and its priority follows the decision");
        check(EvaluationFindings.ReferenceOf("AE-2026", 7) == "AE-2026:7" && EvaluationFindings.CarrierOf("AE-2026", "AE-2026:7") == 7
            && EvaluationFindings.CarrierOf("AE-2026", "AE-2026-2:7") is null && EvaluationFindings.CarrierOf("AE-2026", "AE-2026:x") is null
            && EvaluationFindings.CarrierOf("AE-2026", "AE-2026:-3") is null,
            "findings: a plan names the campaign and the carrier's row in it — another campaign of the same year is not this one");

        if (sql) await SqlAsync(check);
    }

    private static async Task SqlAsync(Action<bool, string> check)
    {
        var database = "SCMOS_EVALUATION_PLAN_TEST_" + Guid.NewGuid().ToString("N");
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
            await db.SaveChangesAsync();
            db.OperationJobs.Add(new OperationJob
            {
                Key = "P1", Trucker = "ALPHA TRANSPORT", WorkDate = "10/03/2026", Status = JobStatus.Completed, UpdatedAt = now,
                Data = JsonSerializer.Serialize(new { key = "P1", date = "10/03/2026", status = JobStatus.Completed, trucker = "ALPHA TRANSPORT", planTime = "08:00",
                    arrDate = "10/03/2026", arrTime = "07:50" }),
            });
            StaffMember Person(string id, string name, string role) => new()
            {
                Id = id, Email = id.ToLowerInvariant() + "@test.invalid", Name = name, Account = id.ToLowerInvariant(), Role = role, Active = true,
                CreatedBy = "test", CreatedAt = now, UpdatedBy = "test", UpdatedAt = now,
            };
            db.Staff.AddRange(Person("OP-1", "Owner One", Roles.Operation), Person("SV-1", "Supervisor", Roles.Supervisor));
            await db.SaveChangesAsync();

            AppUser User(string id, string role) => new(id.ToLowerInvariant(), id.ToLowerInvariant() + "@test.invalid", id, role, id, "test", true);
            var operation = User("OP-1", Roles.Operation);
            var supervisor = User("SV-1", Roles.Supervisor);
            var auditing = new AuditService(db, new HttpContextAccessor(), NullLogger<AuditService>.Instance);
            var directory = new CarrierDirectory(db, new MemoryCache(new MemoryCacheOptions()));
            var campaigns = new AnnualEvaluationService(db, auditing, directory);
            var snapshots = new EvaluationSnapshotService(db, auditing,
                new JobRegisterCache(db, new MemoryCache(new MemoryCacheOptions()), NullLogger<JobRegisterCache>.Instance), directory);
            var invitations = new EvaluationInvitationService(db, auditing);
            var external = new ExternalEvaluationService(db, auditing);
            var scoring = new EvaluationScoringService(db, auditing);
            var review = new EvaluationReviewService(db, auditing);
            var evaluationPlans = new EvaluationPlanService(db);
            var actionPlans = new ActionPlanService(db, auditing);
            await actionPlans.TypesAsync(default);

            var id = (int)(await campaigns.CreateAsync(supervisor, new CreateCampaignInput(2026, null, null), default)).Id!.Value;
            var other = (int)(await campaigns.CreateAsync(supervisor, new CreateCampaignInput(2026, "Re-run", null), default)).Id!.Value;
            await campaigns.ChangeCarriersAsync(supervisor, id, new CarrierChangeInput("add-eligible", null, null), default);
            var row = await db.EvaluationCarriers.AsNoTracking().SingleAsync(one => one.CampaignId == id);
            await campaigns.MoveAsync(supervisor, id, AnnualEvaluationRules.DataPreparation, null, default);
            await snapshots.GenerateAsync(supervisor, id, null, null, default);
            var ops = await db.EvaluationDepartments.SingleAsync(one => one.Code == "ops");
            await invitations.AddEvaluatorAsync(supervisor, id, new EvaluatorInput("Ops reviewer", "", ops.Id), default);
            var evaluatorId = (await db.EvaluationEvaluators.SingleAsync(one => one.CampaignId == id)).Id;
            var link = (await invitations.GenerateAsync(supervisor, id, new InvitationRequest([evaluatorId], null, null), default)).Links!.Single();
            var campaign = await db.EvaluationCampaigns.SingleAsync(one => one.Id == id);
            var today = DateOnly.FromDateTime(DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(7)).DateTime);
            campaign.Status = AnnualEvaluationRules.Open;
            campaign.OpenOn = today.AddDays(-1);
            campaign.DueOn = today.AddDays(5);
            campaign.MinimumSystemCoverage = 0;
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();

            await scoring.SetManualScoreAsync(supervisor, id, row.Id, "pricing", 60, "Rates well above the RFQ", default);
            var sheet = (await external.ReadAsync(link.Token, default)).View!;
            await external.SubmitAsync(link.Token, new ExternalSubmission(sheet.Questions.Select(question => question.Code switch
            {
                "capacity" => new EvaluationInvitations.Answer(question.Code, 2, false, "Short of trucks every month end"),
                "communication" => new EvaluationInvitations.Answer(question.Code, 3, false, ""),
                _ => new EvaluationInvitations.Answer(question.Code, 5, false, ""),
            }).ToList(), ""), default);
            await scoring.CalculateAsync(supervisor, id, null, null, default);

            var draft = (await evaluationPlans.DraftAsync(operation, id, row.Id, default))!;
            var codes = draft.Findings.Select(finding => finding.Code).ToList();
            check(codes.Contains("pricing") && codes.IndexOf("capacity") >= 0 && codes.IndexOf("capacity") < codes.IndexOf("communication")
                && !codes.Contains("satisfaction") && draft.Findings.Where(finding => finding.Kind == "kpi").All(finding => finding.Score < 100)
                && draft.SupplierId == alpha.Id && draft.DevelopmentType == ActionPlanRules.Subcontractor && draft.TargetType == "carrier"
                && draft.Gap.Contains("Pricing Competitiveness") && draft.Gap.Contains("ความพร้อมของรถ") && !draft.Gap.Contains('\n')
                && draft.Items.Count is > 0 and <= EvaluationFindings.MaxSteps
                && draft.References.Single().Kind == "evaluation" && draft.References.Single().RefId == $"AE-2026:{row.Id}"
                && await evaluationPlans.DraftAsync(operation, id, row.Id + 999, default) is null,
                "plan draft: the carrier's findings — a manual KPI below full marks, the questions its evaluators rated under 'good' — with a reference back");

            await campaigns.MoveAsync(supervisor, id, AnnualEvaluationRules.Closed, null, default);
            await campaigns.MoveAsync(supervisor, id, AnnualEvaluationRules.UnderReview, null, default);
            await review.DecideAsync(supervisor, id, row.Id, EvaluationReview.ContinueWithPlan, "Capacity at month end", default);
            var waiting = (await review.SummaryAsync(operation, id, default))!;
            var decided = (await evaluationPlans.DraftAsync(operation, id, row.Id, default))!;

            ActionPlanInput Input(PlanDraft from, IReadOnlyList<ActionReferenceInput>? references = null) => new(from.Title, from.DevelopmentType, from.Category,
                "annual", 2026, null, null, from.Priority, "", from.Objective, "", "", "31/12/2026", "OP-1", from.TargetType, "", "", "", "", from.SupplierId, "",
                from.DevelopmentArea, from.CurrentLevel, from.TargetLevel, from.Gap, "", "", "", "", "", "", "", from.Metric, from.Baseline, from.TargetValue, null,
                from.Items.Select(step => new ActionItemInput(step.Action, "", "OP-1", "", "", "", "30/11/2026", "", "", "", 0, step.ExpectedResult, "", "", "", "", "",
                    "", "", "", "")).ToList(), references ?? from.References);
            var created = await actionPlans.CreateAsync(operation, Input(decided), default);
            var elsewhere = await actionPlans.CreateAsync(operation, Input(decided, [new ActionReferenceInput("evaluation", $"AE-2026-2:{row.Id}", "other campaign")]), default);
            var listed = (await evaluationPlans.PlansAsync(operation, id, default))!;
            var listedOther = (await evaluationPlans.PlansAsync(operation, other, default))!;
            var covered = (await review.SummaryAsync(operation, id, default))!;
            check(waiting.PlanFollowUps.Single().EvaluationCarrierId == row.Id && waiting.Plans == 0
                && decided.Objective.StartsWith(EvaluationReview.LabelOf(EvaluationReview.ContinueWithPlan)) && decided.Priority == "medium"
                && created.Ok && elsewhere.Ok && listed.Count == 1 && listed[0].PlanId == created.Id && listed[0].EvaluationCarrierId == row.Id
                && listed[0].Status == ActionPlanRules.Draft && !listed[0].Overdue && listed[0].Progress == 0 && listedOther.Count == 0
                && covered.PlanFollowUps.Count == 0 && covered.Plans == 1,
                "plan: the draft is accepted by the Action Plan module as it is; the campaign lists the plan by its reference — one naming another campaign's row is not counted — and stops waiting for one");

            var cancelled = await actionPlans.MoveAsync(operation, created.Id!.Value, ActionPlanRules.Cancelled, "Opened twice by mistake", default);
            var afterCancel = (await review.SummaryAsync(operation, id, default))!;
            var stillListed = (await evaluationPlans.PlansAsync(operation, id, default))!;
            check(cancelled.Ok && afterCancel.Plans == 0 && afterCancel.PlanFollowUps.Single().EvaluationCarrierId == row.Id
                && stillListed.Single().Status == ActionPlanRules.Cancelled,
                "plan: a cancelled plan is still listed, but the decision waits for a live one again");
        }
        finally
        {
            await db.Database.EnsureDeletedAsync();
        }
    }
}
