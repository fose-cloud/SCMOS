using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Scmos.Api.Auth;
using Scmos.Api.Data;
using Scmos.Api.Rules;
using Scmos.Api.Services;

/// <summary>
/// Subcontract Management's Action Plan (1 Oct 2026): the rules that are worked out (number, progress, overdue,
/// workflow), who holds which right, and — on a throwaway LocalDB — that a people development plan is read only
/// by reviewers, its owner and its employee, by every route including its evidence.
/// </summary>
static class ActionPlanChecks
{
    public static async Task RunAsync(Action<bool, string> check, bool sql)
    {
        check(ActionPlanRules.Number(2026, 1) == "AP-SCM-2026-0001" && ActionPlanRules.Number(2027, 123) == "AP-SCM-2027-0123",
            "action plan: numbered AP-SCM-YYYY-XXXX");
        check(ActionPlanRules.Progress([("in-progress", 50), ("completed", 10), ("cancelled", 0)]) == 75
            && ActionPlanRules.Progress([]) is null && ActionPlanRules.Progress([("cancelled", 40)]) is null
            && ActionPlanRules.Progress([("planned", 0), ("planned", 33), ("planned", 34)]) == 22,
            "action plan: progress is the average of the steps not cancelled, a completed step counting 100");
        var today = Formats.DateNumber("01/10/2026");
        check(ActionPlanRules.Overdue("in-progress", "30/09/2026", today) && !ActionPlanRules.Overdue("in-progress", "01/10/2026", today)
            && !ActionPlanRules.Overdue("completed", "01/01/2026", today) && !ActionPlanRules.Overdue("cancelled", "01/01/2026", today)
            && !ActionPlanRules.Overdue("draft", "", today),
            "action plan: overdue is a past target date on a plan still open — worked out, never stored");
        check(ActionPlanRules.CanMove("draft", "planned") && !ActionPlanRules.CanMove("draft", "completed")
            && ActionPlanRules.CanMove("in-progress", "pending-review") && ActionPlanRules.CanMove("waiting", "in-progress")
            && !ActionPlanRules.CanMove("pending-review", "completed") && !ActionPlanRules.CanMove("completed", "in-progress"),
            "action plan: Draft → Planned → In Progress → Pending Review; Completed only by a review");
        check(Roles.Can(Roles.Operation, Capability.EditActionPlans) && !Roles.Can(Roles.Operation, Capability.ReviewActionPlans)
            && Roles.Can(Roles.Supervisor, Capability.ReviewActionPlans) && Roles.Can(Roles.Manager, Capability.ReviewActionPlans)
            && !Roles.Can(Roles.Subcontractor, Capability.EditActionPlans) && !Roles.Can(Roles.Viewer, Capability.EditActionPlans)
            && !CarrierBoundary.Allows("GET", new PathString("/api/action-plans")),
            "action plan: Operation works plans, supervisors upward review them, and a carrier reaches none");
        if (sql) await SqlAsync(check);
    }

    private static ActionPlanInput People(string title, string ownerId, string employeeId) => new(title, "people", "Training", "annual", 2026,
        null, null, "high", "", "Improve data analysis", "", "01/09/2026", "31/12/2026", ownerId, "employee", employeeId, "Operation Officer",
        "Trucking", "", null, "", "Data Analysis", "2 = Beginner", "4 = Advanced", "", "", "OJT", "", "", "", "", "", "", null, null, null,
        [new ActionItemInput("Power BI course", "", ownerId, "", "", "", "30/11/2026", "", "", "", 0, "", "", "", "Power BI", "System Training", "", "", "", "", "")]);

    private static ActionPlanInput Carrier(int supplierId, string ownerId) => new("Improve ALPHA OTD", "subcontractor", "Carrier Performance Development",
        "quarterly", 2026, null, null, "medium", "", "OTD 80% → 95%", "", "", "31/12/2026", ownerId, "carrier", "", "", "", "", supplierId, "",
        "OTD", "80%", "95%", "", "Late dispatch", "", "", "Khun Somchai", "", "", "", "OTD", 80, 95, null, []);

    private static async Task SqlAsync(Action<bool, string> check)
    {
        var database = "SCMOS_ACTION_PLAN_TEST_" + Guid.NewGuid().ToString("N");
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
            StaffMember Person(string id, string name, string role, int? supplier = null) => new()
            {
                Id = id, Email = id.ToLowerInvariant() + "@test.invalid", Name = name, Account = id.ToLowerInvariant(), Role = role, Active = true,
                SupplierId = supplier, CreatedBy = "test", CreatedAt = now, UpdatedBy = "test", UpdatedAt = now,
            };
            db.Staff.AddRange(Person("OP-1", "Owner One", Roles.Operation), Person("OP-2", "Employee Two", Roles.Operation),
                Person("OP-3", "Other Three", Roles.Operation), Person("SUP-1", "Supervisor", Roles.Supervisor), Person("SUB-1", "Carrier", Roles.Subcontractor, alpha.Id));
            await db.SaveChangesAsync();
            AppUser User(string id, string role) => new(id.ToLowerInvariant(), id.ToLowerInvariant() + "@test.invalid", id, role, id, "test", true);
            var owner = User("OP-1", Roles.Operation);
            var employee = User("OP-2", Roles.Operation);
            var other = User("OP-3", Roles.Operation);
            var supervisor = User("SUP-1", Roles.Supervisor);
            var carrier = User("SUB-1", Roles.Subcontractor);
            var service = new ActionPlanService(db, new AuditService(db, new HttpContextAccessor(), NullLogger<AuditService>.Instance));
            await service.TypesAsync(default);

            var first = await service.CreateAsync(owner, People("Develop Employee Two", "OP-1", "OP-2"), default);
            var hidden = await service.CreateAsync(other, People("Someone else's", "OP-1", "OP-2"), default);
            var second = await service.CreateAsync(owner, Carrier(alpha.Id, "OP-1"), default);
            var refusedCarrier = await service.CreateAsync(carrier, Carrier(alpha.Id, "OP-1"), default);
            var plans = await db.ActionPlans.AsNoTracking().OrderBy(row => row.Id).ToListAsync();
            check(first.Ok && second.Ok && !hidden.Ok && !refusedCarrier.Ok && plans.Count == 2
                && plans[0].Number == "AP-SCM-2026-0001" && plans[1].Number == "AP-SCM-2026-0002"
                && plans[0].EmployeeName == "Employee Two" && plans[0].OwnerName == "Owner One" && plans[1].TargetName == "ALPHA TRANSPORT"
                && await db.ActionPlanItems.CountAsync(row => row.PlanId == first.Id) == 1,
                "action plan: created with its steps and the next number; people and companies come from their registers; nobody creates a plan they could not read");

            var filter = new ActionPlanFilter(null, null, null, null, null, null, null, null, null, null, null);
            var seenByOwner = await service.ListAsync(owner, filter, default);
            var seenByEmployee = await service.ListAsync(employee, filter, default);
            var seenByOther = await service.ListAsync(other, filter, default);
            var seenBySupervisor = await service.ListAsync(supervisor, filter, default);
            check(seenByOwner.Count == 2 && seenByEmployee.Count == 2 && seenBySupervisor.Count == 2
                && seenByOther.Count == 1 && seenByOther[0].DevelopmentType == "subcontractor"
                && await service.ReadAsync(other, first.Id!.Value, default) is null
                && await service.HistoryAsync(other, first.Id.Value, default) is null
                && (await service.ListAsync(carrier, filter, default)).Count == 0,
                "action plan: a people plan is read by reviewers, its owner and its employee only; a subcontractor plan by the department; a carrier by nobody");

            var planId = first.Id.Value;
            var itemId = (await db.ActionPlanItems.AsNoTracking().FirstAsync(row => row.PlanId == planId)).Id;
            var added = await service.AddItemAsync(owner, planId, new ActionItemInput("Coaching with supervisor", "", "OP-1", "", "", "", "15/12/2026", "", "", "", 20,
                "", "", "", "", "", "", "", "", "", ""), default);
            var progressed = await service.AddUpdateAsync(owner, planId, "Course finished", itemId, 100, default);
            var notByOther = await service.AddUpdateAsync(other, planId, "x", null, null, default);
            var detail = await service.ReadAsync(employee, planId, default);
            check(added.Ok && progressed.Ok && !notByOther.Ok && detail!.Progress == 60
                && detail.Items.First(row => row.Id == itemId).Status == "completed" && detail.Updates.Any(row => row.ProgressAfter == 60),
                "action plan: a step at 100% is completed; the plan's progress follows its steps; the history keeps the change");

            var planned = await service.MoveAsync(owner, planId, "planned", "", default);
            var skip = await service.MoveAsync(owner, planId, "completed", "", default);
            await service.MoveAsync(owner, planId, "in-progress", "", default);
            var submitted = await service.MoveAsync(owner, planId, "pending-review", "Please review", default);
            var selfReview = await service.ReviewAsync(owner, planId, "approved", "", default);
            var improve = await service.ReviewAsync(supervisor, planId, "need-improvement", "Add evidence", default);
            var afterImprove = (await db.ActionPlans.AsNoTracking().FirstAsync(row => row.Id == planId)).Status;
            await service.MoveAsync(owner, planId, "pending-review", "", default);
            var approved = await service.ReviewAsync(supervisor, planId, "approved", "Good", default);
            var closed = await db.ActionPlans.AsNoTracking().FirstAsync(row => row.Id == planId);
            var editClosed = await service.AddItemAsync(owner, planId, new ActionItemInput("late", "", "", "", "", "", "", "", "", "", 0, "", "", "", "", "", "", "", "", "", ""), default);
            var reopened = await service.ReviewAsync(supervisor, planId, "reopen", "Not sustained", default);
            check(planned.Ok && !skip.Ok && submitted.Ok && !selfReview.Ok && improve.Ok && afterImprove == "in-progress"
                && approved.Ok && closed.Status == "completed" && closed.ActualCompletionDate.Length == 10 && !editClosed.Ok
                && reopened.Ok && (await db.ActionPlans.AsNoTracking().FirstAsync(row => row.Id == planId)).Status == "in-progress"
                && await db.ActionPlanReviews.CountAsync(row => row.PlanId == planId) == 3,
                "action plan: completed only by a reviewer's approval; need improvement and reopen send it back; a closed plan takes no changes");

            var carrierPlan = second.Id!.Value;
            var noScoreOnPeople = await service.ScoreAsync(owner, planId, "OTD", null, 3, 5, default);
            var scoreOne = await service.ScoreAsync(owner, carrierPlan, "otd", null, 2, 5, default);
            var scoreTwo = await service.ScoreAsync(owner, carrierPlan, "OTD", null, 4, 5, default);
            var badScore = await service.ScoreAsync(owner, carrierPlan, "OTD", null, 6, null, default);
            var scores = await db.ActionPlanScores.AsNoTracking().Where(row => row.PlanId == carrierPlan).OrderBy(row => row.Id).ToListAsync();
            check(!noScoreOnPeople.Ok && scoreOne.Ok && scoreTwo.Ok && !badScore.Ok && scores.Count == 2
                && scores[0].Dimension == "OTD" && scores[0].PreviousScore is null && scores[1].PreviousScore == 2 && scores[1].SupplierId == alpha.Id,
                "action plan: a subcontractor's scores are 1–5 per dimension, each keeping the one before it");

            db.Documents.Add(new StoredDocument { Scope = "action-plan", ActionPlanId = planId, Folder = "ActionPlan", Kind = "certificate",
                FileName = "certificate.pdf", ObjectKey = "SCMOS/ActionPlan/2026/AP-SCM-2026-0001/certificate.pdf", UploadedBy = "OP-1", UploadedAt = now });
            await db.SaveChangesAsync();
            var evidence = await db.Documents.AsNoTracking().FirstAsync(row => row.ActionPlanId == planId);
            var documents = new DocumentService(db, new NoFiles());
            var access = new CarrierDocumentAccess(db, new CarrierTenantContext(db, NullLogger<CarrierTenantContext>.Instance), documents);
            check(await access.CanReadAsync(employee, evidence, default) && await access.CanReadAsync(supervisor, evidence, default)
                && !await access.CanReadAsync(other, evidence, default) && !await access.CanReadAsync(carrier, evidence, default)
                && (await documents.ListAsync(null, null, null, null, null, default)).All(row => row.Id != evidence.Id),
                "action plan: evidence is read by whoever may read the plan, and never listed in the Document Centre");

            check(await db.AuditEvents.CountAsync(row => row.Entity == "action-plan" && row.EntityId == planId.ToString()) >= 10,
                "action plan: every change is in the plan's audit history");
        }
        finally
        {
            await db.Database.EnsureDeletedAsync();
        }
    }

    private sealed class NoFiles : IFileStore
    {
        public bool Configured => false;
        public Task<string> PutAsync(string objectKey, Stream content, string contentType, IDictionary<string, string> metadata, CancellationToken token) =>
            throw new InvalidOperationException("no file store in checks");
        public Task<Stream?> OpenAsync(string objectKey, CancellationToken token) => Task.FromResult<Stream?>(null);
    }
}
