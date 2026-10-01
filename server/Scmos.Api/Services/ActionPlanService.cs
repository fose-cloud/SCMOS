using Microsoft.EntityFrameworkCore;
using Scmos.Api.Auth;
using Scmos.Api.Data;
using Scmos.Api.Rules;

namespace Scmos.Api.Services;

public record ActionPlanRow(long Id, string Number, string Title, string DevelopmentType, string Category, string TargetType,
    string TargetName, string EmployeeId, int? SupplierId, string OwnerId, string OwnerName, string StartDate, string TargetDate,
    string ActualCompletionDate, int? Progress, string Status, bool Overdue, string Priority, int Year, int? Quarter, int? Month,
    DateTimeOffset UpdatedAt);

public record ActionPlanFilter(int? Year, int? Quarter, int? Month, string? DevelopmentType, string? Category, string? OwnerId,
    string? EmployeeId, int? SupplierId, string? Status, string? Priority, string? Query);

public record ActionPlanInput(
    string? Title, string? DevelopmentType, string? Category, string? Period, int? Year, int? Quarter, int? Month, string? Priority,
    string? Description, string? Objective, string? ExpectedOutcome, string? StartDate, string? TargetDate, string? OwnerId,
    string? TargetType, string? EmployeeId, string? Position, string? Team, string? Supervisor, int? SupplierId, string? TargetName,
    string? DevelopmentArea, string? CurrentLevel, string? TargetLevel, string? Gap, string? RootCause, string? Method, string? Coach,
    string? CarrierContact, string? EvaluationMethod, string? ReviewDate, string? Result, string? Metric, decimal? Baseline,
    decimal? TargetValue, decimal? ActualValue, IReadOnlyList<ActionItemInput>? Items);

public record ActionItemInput(string? Action, string? Description, string? OwnerId, string? SupportingPerson,
    string? SupportingDepartment, string? StartDate, string? TargetDate, string? ActualCompletionDate, string? Priority,
    string? Status, int? Progress, string? ExpectedResult, string? ActualResult, string? Remark, string? TrainingTitle,
    string? TrainingType, string? Trainer, string? TrainingProvider, string? TrainingDate, string? Participants,
    string? CertificateExpiry);

public record ActionPlanResult(bool Ok, string Message, int Status = StatusCodes.Status200OK, long? Id = null);

public record ActionPlanDetail(ActionPlan Plan, int? Progress, bool Overdue, bool CanEdit, bool CanReview,
    IReadOnlyList<ActionPlanItem> Items, IReadOnlyList<ActionPlanUpdate> Updates, IReadOnlyList<ActionPlanReview> Reviews,
    IReadOnlyList<ActionPlanScore> Scores, IReadOnlyList<ActionPlanReference> References, IReadOnlyList<DocumentView> Evidence,
    string SupplierName);

public record CountView(string Key, int Count);
public record MonthView(string Month, int Completed, int Due);

public record ActionPlanDashboard(int Total, int Active, int Completed, int Overdue, int DueThisMonth, int People, int Carrier,
    IReadOnlyList<CountView> ByStatus, IReadOnlyList<CountView> ByCategory, IReadOnlyList<CountView> ByPriority,
    IReadOnlyList<CountView> ByOwner, IReadOnlyList<MonthView> Monthly, int? PeopleProgress, int? CarrierProgress);

public record PersonView(string Id, string Name, string Role);

/// <summary>
/// Subcontract Management's Action Plan (1 Oct 2026), round one: plans, their steps and progress, the history,
/// evidence, review, subcontractor scores and references to other SCMOS records.
///
/// <para>
/// Who reads a plan is decided here, for every route: a subcontractor development plan is the department's
/// to read; a people development plan is read by those who review plans (supervisors upward), its owner and the
/// person it develops — nobody else, by any route, including its evidence.
/// </para>
/// </summary>
public class ActionPlanService(ScmosDbContext db, AuditService audit)
{
    private static int Today() => Formats.DateNumber(Formats.Now.ToString("dd/MM/yyyy"));
    private static string TodayText() => Formats.Now.ToString("dd/MM/yyyy");

    /// <summary>The rule for reading a plan — see the class remarks.</summary>
    public static bool CanSee(AppUser user, ActionPlan plan) =>
        !string.Equals(user.Role, Roles.Subcontractor, StringComparison.OrdinalIgnoreCase)
        && (plan.DevelopmentType != ActionPlanRules.People || user.Can(Capability.ReviewActionPlans)
            || (plan.OwnerId.Length > 0 && plan.OwnerId == user.OperatorId)
            || (plan.EmployeeId.Length > 0 && plan.EmployeeId == user.OperatorId));

    public static bool CanEdit(AppUser user, ActionPlan plan) => user.Can(Capability.EditActionPlans) && CanSee(user, plan);

    /* ------------------------------------------------------------------ reading */

    public async Task<IReadOnlyList<ActionPlanRow>> ListAsync(AppUser user, ActionPlanFilter filter, CancellationToken token)
    {
        var plans = await VisibleAsync(user, token);
        var progress = await ProgressAsync(plans.Select(plan => plan.Id).ToList(), token);
        var today = Today();
        var rows = plans.Select(plan => Row(plan, progress.GetValueOrDefault(plan.Id), today)).ToList();
        var words = (filter.Query ?? "").Trim().ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return rows.Where(row =>
                (filter.Year is null || row.Year == filter.Year)
                && (filter.Quarter is null || row.Quarter == filter.Quarter || QuarterOf(row.TargetDate) == filter.Quarter)
                && (filter.Month is null || row.Month == filter.Month || Formats.DateNumber(row.TargetDate) / 100 % 100 == filter.Month)
                && (string.IsNullOrWhiteSpace(filter.DevelopmentType) || row.DevelopmentType == filter.DevelopmentType)
                && (string.IsNullOrWhiteSpace(filter.Category) || row.Category == filter.Category)
                && (string.IsNullOrWhiteSpace(filter.OwnerId) || row.OwnerId == filter.OwnerId)
                && (string.IsNullOrWhiteSpace(filter.EmployeeId) || row.EmployeeId == filter.EmployeeId)
                && (filter.SupplierId is null || row.SupplierId == filter.SupplierId)
                && (string.IsNullOrWhiteSpace(filter.Status) || (filter.Status == "overdue" ? row.Overdue : row.Status == filter.Status))
                && (string.IsNullOrWhiteSpace(filter.Priority) || row.Priority == filter.Priority)
                && words.All(word => $"{row.Number} {row.Title} {row.TargetName} {row.OwnerName} {row.Category}".ToLowerInvariant().Contains(word)))
            .OrderBy(row => row.Overdue ? 0 : 1).ThenBy(row => Formats.DateNumber(row.TargetDate) is > 0 and var due ? due : int.MaxValue)
            .ThenByDescending(row => row.Id).ToList();
    }

    public async Task<ActionPlanDashboard> DashboardAsync(AppUser user, int? year, CancellationToken token)
    {
        var rows = await ListAsync(user, new ActionPlanFilter(year, null, null, null, null, null, null, null, null, null, null), token);
        var live = rows.Where(row => row.Status != ActionPlanRules.Cancelled).ToList();
        var now = Formats.Now;
        var thisMonth = now.Year * 100 + now.Month;
        static IReadOnlyList<CountView> Count(IEnumerable<string> keys) =>
            keys.GroupBy(key => key.Length == 0 ? "—" : key).Select(group => new CountView(group.Key, group.Count()))
                .OrderByDescending(view => view.Count).ToList();
        // Eight months back for what was completed, three ahead for what falls due.
        var monthly = Enumerable.Range(0, 12).Select(step => now.AddMonths(step - 8)).Select(month =>
        {
            var key = month.Year * 100 + month.Month;
            return new MonthView(month.ToString("MM/yyyy"),
                live.Count(row => row.Status == ActionPlanRules.Completed && Formats.DateNumber(row.ActualCompletionDate) / 100 == key),
                live.Count(row => Formats.DateNumber(row.TargetDate) / 100 == key));
        }).ToList();
        static int? Average(IEnumerable<int?> values)
        {
            var known = values.Where(value => value is not null).Select(value => value!.Value).ToList();
            return known.Count == 0 ? null : (int)Math.Round(known.Average(), MidpointRounding.AwayFromZero);
        }
        return new ActionPlanDashboard(live.Count,
            live.Count(row => row.Status is ActionPlanRules.Planned or ActionPlanRules.InProgress or ActionPlanRules.Waiting or ActionPlanRules.PendingReview),
            live.Count(row => row.Status == ActionPlanRules.Completed), live.Count(row => row.Overdue),
            live.Count(row => row.Status != ActionPlanRules.Completed && Formats.DateNumber(row.TargetDate) / 100 == thisMonth),
            live.Count(row => row.DevelopmentType == ActionPlanRules.People), live.Count(row => row.DevelopmentType == ActionPlanRules.Subcontractor),
            Count(live.Select(row => row.Overdue ? "overdue" : row.Status)), Count(live.Select(row => row.Category)),
            Count(live.Select(row => row.Priority)), Count(live.Select(row => row.OwnerName)), monthly,
            Average(live.Where(row => row.DevelopmentType == ActionPlanRules.People).Select(row => row.Progress)),
            Average(live.Where(row => row.DevelopmentType == ActionPlanRules.Subcontractor).Select(row => row.Progress)));
    }

    public async Task<ActionPlanDetail?> ReadAsync(AppUser user, long id, CancellationToken token)
    {
        var plan = await db.ActionPlans.AsNoTracking().FirstOrDefaultAsync(row => row.Id == id, token);
        if (plan is null || !CanSee(user, plan)) return null;
        var items = await db.ActionPlanItems.AsNoTracking().Where(row => row.PlanId == id).OrderBy(row => row.Sequence).ToListAsync(token);
        var updates = await db.ActionPlanUpdates.AsNoTracking().Where(row => row.PlanId == id).OrderByDescending(row => row.Id).ToListAsync(token);
        var reviews = await db.ActionPlanReviews.AsNoTracking().Where(row => row.PlanId == id).OrderByDescending(row => row.Id).ToListAsync(token);
        var scores = await db.ActionPlanScores.AsNoTracking().Where(row => row.PlanId == id).OrderBy(row => row.Dimension).ThenBy(row => row.Id).ToListAsync(token);
        var references = await db.ActionPlanReferences.AsNoTracking().Where(row => row.PlanId == id).OrderBy(row => row.Id).ToListAsync(token);
        var evidence = (await db.Documents.AsNoTracking().Where(row => row.ActionPlanId == id).OrderByDescending(row => row.Id).ToListAsync(token))
            .Select(DocumentService.Describe).ToList();
        var supplier = plan.SupplierId is { } supplierId
            ? await db.Suppliers.AsNoTracking().Where(row => row.Id == supplierId).Select(row => row.Name).FirstOrDefaultAsync(token) ?? ""
            : "";
        return new ActionPlanDetail(plan, ActionPlanRules.Progress(items.Select(item => (item.Status, item.Progress))),
            ActionPlanRules.Overdue(plan.Status, plan.TargetDate, Today()), CanEdit(user, plan), user.Can(Capability.ReviewActionPlans),
            items, updates, reviews, scores, references, evidence, supplier);
    }

    /// <summary>The plan's own audit rows, newest first — the Audit History tab.</summary>
    public async Task<IReadOnlyList<AuditEvent>?> HistoryAsync(AppUser user, long id, CancellationToken token)
    {
        var plan = await db.ActionPlans.AsNoTracking().FirstOrDefaultAsync(row => row.Id == id, token);
        if (plan is null || !CanSee(user, plan)) return null;
        var key = id.ToString();
        return await db.AuditEvents.AsNoTracking().Where(row => row.Entity == "action-plan" && row.EntityId == key)
            .OrderByDescending(row => row.Id).Take(500).ToListAsync(token);
    }

    /// <summary>The department's people, for the employee, owner and coach pickers — never a carrier account.</summary>
    public async Task<IReadOnlyList<PersonView>> PeopleAsync(CancellationToken token) =>
        await db.Staff.AsNoTracking().Where(row => row.Active && row.Role != Roles.Subcontractor)
            .OrderBy(row => row.Name).Select(row => new PersonView(row.Id, row.Name, row.Role)).ToListAsync(token);

    /// <summary>The configurable plan types, filled with the department's defaults the first time.</summary>
    public async Task<IReadOnlyList<ActionPlanType>> TypesAsync(CancellationToken token)
    {
        if (!await db.ActionPlanTypes.AnyAsync(token))
        {
            foreach (var (kind, names) in ActionPlanRules.DefaultCategories)
                db.ActionPlanTypes.AddRange(names.Select((name, position) => new ActionPlanType { DevelopmentType = kind, Name = name, Position = position }));
            try { await db.SaveChangesAsync(token); }
            catch (DbUpdateException) { db.ChangeTracker.Clear(); }   // another request seeded them first
        }
        return await db.ActionPlanTypes.AsNoTracking().OrderBy(row => row.DevelopmentType).ThenBy(row => row.Position).ToListAsync(token);
    }

    public async Task<ActionPlanResult> SaveTypeAsync(AppUser user, string? developmentType, string? name, bool active, CancellationToken token)
    {
        var kind = (developmentType ?? "").Trim().ToLowerInvariant();
        var text = (name ?? "").Trim();
        if (!ActionPlanRules.DevelopmentTypes.Contains(kind) || text.Length is 0 or > 120) return Refused("ระบุประเภทการพัฒนาและชื่อประเภทแผน");
        var row = await db.ActionPlanTypes.FirstOrDefaultAsync(one => one.DevelopmentType == kind && one.Name == text, token);
        if (row is null)
        {
            row = new ActionPlanType { DevelopmentType = kind, Name = text,
                Position = (await db.ActionPlanTypes.Where(one => one.DevelopmentType == kind).MaxAsync(one => (int?)one.Position, token) ?? -1) + 1 };
            db.ActionPlanTypes.Add(row);
        }
        row.Active = active;
        await db.SaveChangesAsync(token);
        await audit.RecordAsync(user, AuditActions.Configure, "action-plan-type", row.Id.ToString(), text, "active", "", active ? "on" : "off", kind, token);
        return new ActionPlanResult(true, active ? $"เปิดใช้ประเภท {text} แล้ว" : $"ปิดประเภท {text} แล้ว", Id: row.Id);
    }

    /* ------------------------------------------------------------------ writing */

    public async Task<ActionPlanResult> CreateAsync(AppUser user, ActionPlanInput input, CancellationToken token)
    {
        if (!user.Can(Capability.EditActionPlans)) return Refused("บัญชีนี้ไม่มีสิทธิ์สร้าง Action Plan", StatusCodes.Status403Forbidden);
        var now = DateTimeOffset.UtcNow;
        var plan = new ActionPlan { CreatedBy = user.Signature, CreatedAt = now, UpdatedBy = user.Signature, UpdatedAt = now };
        var problem = await ApplyAsync(plan, input, user, token);
        if (problem is not null) return problem;
        var items = new List<ActionPlanItem>();
        foreach (var (item, index) in (input.Items ?? []).Select((item, index) => (item, index)))
        {
            var row = new ActionPlanItem { Sequence = index + 1, CreatedBy = user.Signature, CreatedAt = now, UpdatedBy = user.Signature, UpdatedAt = now };
            var itemProblem = await ApplyItemAsync(row, item, token);
            if (itemProblem is not null) return itemProblem with { Message = $"ขั้นที่ {index + 1}: {itemProblem.Message}" };
            items.Add(row);
        }
        if (!CanSee(user, plan)) return Refused("แผนพัฒนาพนักงานต้องมีเจ้าของแผนหรือพนักงานเป็นตัวท่านเอง — หรือให้หัวหน้าสร้าง");

        // The running number within the year: retried once if two plans are numbered in the same instant.
        for (var attempt = 0; ; attempt++)
        {
            var prefix = $"AP-SCM-{plan.Year}-";
            var last = await db.ActionPlans.Where(row => row.Number.StartsWith(prefix)).Select(row => row.Number).ToListAsync(token);
            plan.Number = ActionPlanRules.Number(plan.Year, last.Select(number => int.TryParse(number[prefix.Length..], out var n) ? n : 0).DefaultIfEmpty(0).Max() + 1);
            db.ActionPlans.Add(plan);
            try { await db.SaveChangesAsync(token); break; }
            catch (DbUpdateException) when (attempt == 0) { db.ChangeTracker.Clear(); plan.Id = 0; }
        }
        foreach (var row in items) row.PlanId = plan.Id;
        db.ActionPlanItems.AddRange(items);
        await db.SaveChangesAsync(token);
        await Record(user, plan.Id, AuditActions.Register, "plan", "", $"{plan.Number} · {plan.Title} · {items.Count} ขั้น", token);
        return new ActionPlanResult(true, $"สร้าง {plan.Number} แล้ว", Id: plan.Id);
    }

    public async Task<ActionPlanResult> UpdateAsync(AppUser user, long id, ActionPlanInput input, CancellationToken token)
    {
        var plan = await db.ActionPlans.FirstOrDefaultAsync(row => row.Id == id, token);
        if (plan is null || !CanSee(user, plan)) return NotFound();
        if (!CanEdit(user, plan)) return Refused("บัญชีนี้ไม่มีสิทธิ์แก้ไข Action Plan", StatusCodes.Status403Forbidden);
        if (plan.Status is ActionPlanRules.Completed or ActionPlanRules.Cancelled) return Refused("แผนนี้ปิดแล้ว — Reopen ก่อนแก้ไข");
        var before = new { plan.OwnerName, plan.TargetDate, plan.Title, plan.Priority };
        var problem = await ApplyAsync(plan, input with { Items = null }, user, token);
        if (problem is not null) return problem;
        if (!CanSee(user, plan)) return Refused("หลังแก้ไขแล้วท่านจะไม่เห็นแผนนี้ — ให้หัวหน้าเปลี่ยนเจ้าของแผน");
        plan.UpdatedBy = user.Signature;
        plan.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(token);
        if (before.OwnerName != plan.OwnerName) await Record(user, id, AuditActions.Assign, "owner", before.OwnerName, plan.OwnerName, token);
        if (before.TargetDate != plan.TargetDate) await Record(user, id, AuditActions.Update, "target date", before.TargetDate, plan.TargetDate, token);
        await Record(user, id, AuditActions.Update, "plan", $"{before.Title} · {before.Priority}", $"{plan.Title} · {plan.Priority}", token);
        return new ActionPlanResult(true, $"บันทึก {plan.Number} แล้ว", Id: id);
    }

    /// <summary>A move along the workflow: plan it, start it, hold it, submit it for review, or cancel it.</summary>
    public async Task<ActionPlanResult> MoveAsync(AppUser user, long id, string? to, string? reason, CancellationToken token)
    {
        var plan = await db.ActionPlans.FirstOrDefaultAsync(row => row.Id == id, token);
        if (plan is null || !CanSee(user, plan)) return NotFound();
        if (!CanEdit(user, plan)) return Refused("บัญชีนี้ไม่มีสิทธิ์เปลี่ยนสถานะ", StatusCodes.Status403Forbidden);
        var wanted = (to ?? "").Trim().ToLowerInvariant();
        if (!ActionPlanRules.CanMove(plan.Status, wanted)) return Refused($"เปลี่ยนจาก {plan.Status} เป็น {wanted} ไม่ได้");
        if (wanted == ActionPlanRules.Cancelled && string.IsNullOrWhiteSpace(reason)) return Refused("ต้องระบุเหตุผลที่ยกเลิก");
        var before = plan.Status;
        plan.Status = wanted;
        if (wanted == ActionPlanRules.Cancelled) plan.CancelReason = Cut(reason!.Trim(), 1000);
        plan.UpdatedBy = user.Signature;
        plan.UpdatedAt = DateTimeOffset.UtcNow;
        if (wanted == ActionPlanRules.PendingReview)
            db.ActionPlanReviews.Add(new ActionPlanReview { PlanId = id, SubmittedBy = user.Signature, SubmittedAt = DateTimeOffset.UtcNow, Comment = Cut((reason ?? "").Trim(), 2000) });
        db.ActionPlanUpdates.Add(new ActionPlanUpdate { PlanId = id, Comment = Cut((reason ?? "").Trim(), 2000), StatusBefore = before, StatusAfter = wanted,
            CreatedBy = user.Signature, CreatedAt = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync(token);
        await Record(user, id, wanted == ActionPlanRules.PendingReview ? AuditActions.Update : AuditActions.StatusChange,
            wanted == ActionPlanRules.PendingReview ? "review submitted" : wanted == ActionPlanRules.Cancelled ? "cancelled" : "status", before, wanted, token, reason ?? "");
        return new ActionPlanResult(true, $"{plan.Number}: {wanted}", Id: id);
    }

    /// <summary>
    /// A reviewer's answer. Approved completes the plan; Need Improvement sends it back to In Progress; Reopen
    /// does the same, and reopens a plan already completed. No plan is completed any other way.
    /// </summary>
    public async Task<ActionPlanResult> ReviewAsync(AppUser user, long id, string? result, string? comment, CancellationToken token)
    {
        if (!user.Can(Capability.ReviewActionPlans)) return Refused("บัญชีนี้ไม่มีสิทธิ์ Review แผน", StatusCodes.Status403Forbidden);
        var plan = await db.ActionPlans.FirstOrDefaultAsync(row => row.Id == id, token);
        if (plan is null) return NotFound();
        var answer = (result ?? "").Trim().ToLowerInvariant();
        if (!ActionPlanRules.ReviewResults.Contains(answer)) return Refused("ผล Review ต้องเป็น Approved, Need Improvement หรือ Reopen");
        var reopening = plan.Status == ActionPlanRules.Completed && answer == ActionPlanRules.Reopen;
        if (plan.Status != ActionPlanRules.PendingReview && !reopening) return Refused("แผนนี้ไม่ได้รอ Review");
        var open = await db.ActionPlanReviews.Where(row => row.PlanId == id && row.Result == "").OrderByDescending(row => row.Id).FirstOrDefaultAsync(token);
        if (open is null)
        {
            open = new ActionPlanReview { PlanId = id, SubmittedBy = user.Signature, SubmittedAt = DateTimeOffset.UtcNow };
            db.ActionPlanReviews.Add(open);
        }
        open.Reviewer = user.Signature;
        open.ReviewedAt = DateTimeOffset.UtcNow;
        open.Result = answer;
        open.Comment = Cut((comment ?? "").Trim(), 2000);
        var before = plan.Status;
        plan.Status = answer == ActionPlanRules.Approved ? ActionPlanRules.Completed : ActionPlanRules.InProgress;
        if (answer == ActionPlanRules.Approved && plan.ActualCompletionDate.Length == 0) plan.ActualCompletionDate = TodayText();
        if (reopening) plan.ActualCompletionDate = "";
        plan.UpdatedBy = user.Signature;
        plan.UpdatedAt = DateTimeOffset.UtcNow;
        db.ActionPlanUpdates.Add(new ActionPlanUpdate { PlanId = id, Comment = $"Review: {answer}" + (open.Comment.Length > 0 ? $" — {open.Comment}" : ""),
            StatusBefore = before, StatusAfter = plan.Status, CreatedBy = user.Signature, CreatedAt = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync(token);
        await Record(user, id, answer == ActionPlanRules.Approved ? AuditActions.Approve : AuditActions.Reject,
            reopening ? "reopened" : answer == ActionPlanRules.Approved ? "completed" : "review result", before, plan.Status, token, open.Comment);
        return new ActionPlanResult(true, answer == ActionPlanRules.Approved ? $"{plan.Number} เสร็จสมบูรณ์" : $"{plan.Number} กลับไปดำเนินการต่อ", Id: id);
    }

    public async Task<ActionPlanResult> AddItemAsync(AppUser user, long id, ActionItemInput input, CancellationToken token)
    {
        var plan = await EditablePlanAsync(user, id, token);
        if (plan.Problem is not null) return plan.Problem;
        var now = DateTimeOffset.UtcNow;
        var item = new ActionPlanItem { PlanId = id, CreatedBy = user.Signature, CreatedAt = now, UpdatedBy = user.Signature, UpdatedAt = now,
            Sequence = (await db.ActionPlanItems.Where(row => row.PlanId == id).MaxAsync(row => (int?)row.Sequence, token) ?? 0) + 1 };
        var problem = await ApplyItemAsync(item, input, token);
        if (problem is not null) return problem;
        db.ActionPlanItems.Add(item);
        await db.SaveChangesAsync(token);
        await Record(user, id, AuditActions.Register, $"item {item.Sequence}", "", item.Action, token);
        return new ActionPlanResult(true, $"เพิ่มขั้นที่ {item.Sequence} แล้ว", Id: item.Id);
    }

    public async Task<ActionPlanResult> UpdateItemAsync(AppUser user, long id, long itemId, ActionItemInput input, CancellationToken token)
    {
        var plan = await EditablePlanAsync(user, id, token);
        if (plan.Problem is not null) return plan.Problem;
        var item = await db.ActionPlanItems.FirstOrDefaultAsync(row => row.Id == itemId && row.PlanId == id, token);
        if (item is null) return NotFound();
        var before = (item.Status, item.Progress, item.OwnerName, item.TargetDate);
        var progressBefore = await PlanProgressAsync(id, token);
        var problem = await ApplyItemAsync(item, input, token);
        if (problem is not null) return problem;
        item.UpdatedBy = user.Signature;
        item.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(token);
        var progressAfter = await PlanProgressAsync(id, token);
        if (before.Progress != item.Progress || before.Status != item.Status)
        {
            db.ActionPlanUpdates.Add(new ActionPlanUpdate { PlanId = id, ItemId = itemId, Comment = $"ขั้นที่ {item.Sequence}: {item.Action}",
                ProgressBefore = progressBefore, ProgressAfter = progressAfter, StatusBefore = before.Status, StatusAfter = item.Status,
                CreatedBy = user.Signature, CreatedAt = DateTimeOffset.UtcNow });
            await db.SaveChangesAsync(token);
            await Record(user, id, AuditActions.Update, $"item {item.Sequence} progress", $"{before.Status} {before.Progress}%", $"{item.Status} {item.Progress}%", token);
        }
        if (before.OwnerName != item.OwnerName) await Record(user, id, AuditActions.Assign, $"item {item.Sequence} owner", before.OwnerName, item.OwnerName, token);
        if (before.TargetDate != item.TargetDate) await Record(user, id, AuditActions.Update, $"item {item.Sequence} target date", before.TargetDate, item.TargetDate, token);
        return new ActionPlanResult(true, $"บันทึกขั้นที่ {item.Sequence} แล้ว", Id: itemId);
    }

    /// <summary>A step taken out of the plan — cancelled, kept, and out of the progress.</summary>
    public async Task<ActionPlanResult> RemoveItemAsync(AppUser user, long id, long itemId, CancellationToken token)
    {
        var plan = await EditablePlanAsync(user, id, token);
        if (plan.Problem is not null) return plan.Problem;
        var item = await db.ActionPlanItems.FirstOrDefaultAsync(row => row.Id == itemId && row.PlanId == id, token);
        if (item is null) return NotFound();
        var before = item.Status;
        item.Status = ActionPlanRules.Cancelled;
        item.UpdatedBy = user.Signature;
        item.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(token);
        await Record(user, id, AuditActions.Delete, $"item {item.Sequence} removed", before, ActionPlanRules.Cancelled, token, item.Action);
        return new ActionPlanResult(true, $"นำขั้นที่ {item.Sequence} ออกแล้ว", Id: itemId);
    }

    /// <summary>A follow-up: a comment, and — when given — a step's new progress.</summary>
    public async Task<ActionPlanResult> AddUpdateAsync(AppUser user, long id, string? comment, long? itemId, int? progress, CancellationToken token)
    {
        var plan = await EditablePlanAsync(user, id, token);
        if (plan.Problem is not null) return plan.Problem;
        var text = Cut((comment ?? "").Trim(), 2000);
        if (text.Length == 0 && progress is null) return Refused("เขียนความคืบหน้าหรือระบุ % ที่เปลี่ยน");
        var progressBefore = await PlanProgressAsync(id, token);
        ActionPlanItem? item = null;
        if (itemId is { } wanted)
        {
            item = await db.ActionPlanItems.FirstOrDefaultAsync(row => row.Id == wanted && row.PlanId == id, token);
            if (item is null) return NotFound();
            if (progress is { } value)
            {
                if (value is < 0 or > 100) return Refused("ความคืบหน้าต้องอยู่ระหว่าง 0–100");
                item.Progress = value;
                if (value == 100 && item.Status != ActionPlanRules.Completed) { item.Status = ActionPlanRules.Completed; if (item.ActualCompletionDate.Length == 0) item.ActualCompletionDate = TodayText(); }
                else if (value < 100 && item.Status is ActionPlanRules.Planned or ActionPlanRules.Completed) item.Status = ActionPlanRules.InProgress;
                item.UpdatedBy = user.Signature;
                item.UpdatedAt = DateTimeOffset.UtcNow;
            }
        }
        await db.SaveChangesAsync(token);
        var progressAfter = await PlanProgressAsync(id, token);
        db.ActionPlanUpdates.Add(new ActionPlanUpdate { PlanId = id, ItemId = item?.Id, Comment = text, ProgressBefore = progressBefore, ProgressAfter = progressAfter,
            CreatedBy = user.Signature, CreatedAt = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync(token);
        // The step's own figure in the field; the plan's progress before and after it.
        await Record(user, id, AuditActions.Update, item is null ? "progress note" : $"item {item.Sequence} → {item.Progress}%",
            progressBefore is null ? "" : $"{progressBefore}%", progressAfter is null ? "" : $"{progressAfter}%", token, text);
        return new ActionPlanResult(true, "บันทึกความคืบหน้าแล้ว", Id: id);
    }

    /// <summary>A subcontractor development score (1–5) on one dimension; the previous is the last one given when not stated.</summary>
    public async Task<ActionPlanResult> ScoreAsync(AppUser user, long id, string? dimension, int? previous, int? current, int? target, CancellationToken token)
    {
        var plan = await EditablePlanAsync(user, id, token);
        if (plan.Problem is not null) return plan.Problem;
        if (plan.Plan!.DevelopmentType != ActionPlanRules.Subcontractor) return Refused("คะแนนพัฒนาใช้กับแผนพัฒนาผู้ขนส่ง");
        var name = ActionPlanRules.ScoreDimensions.FirstOrDefault(one => one.Equals((dimension ?? "").Trim(), StringComparison.OrdinalIgnoreCase));
        if (name is null) return Refused("มิติที่ประเมินไม่ถูกต้อง");
        if (new[] { previous, current, target }.Any(score => score is < 1 or > 5)) return Refused("คะแนนต้องอยู่ระหว่าง 1–5");
        if (current is null && target is null) return Refused("ระบุคะแนนปัจจุบันหรือเป้าหมาย");
        previous ??= await db.ActionPlanScores.Where(row => row.SupplierId == plan.Plan.SupplierId && row.Dimension == name && row.CurrentScore != null)
            .OrderByDescending(row => row.Id).Select(row => row.CurrentScore).FirstOrDefaultAsync(token);
        db.ActionPlanScores.Add(new ActionPlanScore { PlanId = id, SupplierId = plan.Plan.SupplierId, Dimension = name, PreviousScore = previous,
            CurrentScore = current, TargetScore = target, AssessedBy = user.Signature, AssessedAt = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync(token);
        await Record(user, id, AuditActions.Update, $"score {name}", previous?.ToString() ?? "", $"{current} → {target}", token);
        return new ActionPlanResult(true, $"บันทึกคะแนน {name} แล้ว", Id: id);
    }

    public async Task<ActionPlanResult> AddReferenceAsync(AppUser user, long id, string? kind, string? refId, string? label, CancellationToken token)
    {
        var plan = await EditablePlanAsync(user, id, token);
        if (plan.Problem is not null) return plan.Problem;
        var what = (kind ?? "").Trim().ToLowerInvariant();
        string[] kinds = ["evaluation", "kpi", "incident", "carpar", "audit", "customer", "training", "risk", "project", "plan"];
        if (!kinds.Contains(what)) return Refused("ประเภทข้อมูลอ้างอิงไม่ถูกต้อง");
        if (string.IsNullOrWhiteSpace(refId) && string.IsNullOrWhiteSpace(label)) return Refused("ระบุเลขอ้างอิงหรือคำอธิบาย");
        var row = new ActionPlanReference { PlanId = id, Kind = what, RefId = Cut((refId ?? "").Trim(), 120), Label = Cut((label ?? "").Trim(), 300) };
        db.ActionPlanReferences.Add(row);
        await db.SaveChangesAsync(token);
        await Record(user, id, AuditActions.Register, "reference", "", $"{what} {row.RefId} {row.Label}".Trim(), token);
        return new ActionPlanResult(true, "เพิ่มข้อมูลอ้างอิงแล้ว", Id: row.Id);
    }

    public async Task<ActionPlanResult> RemoveReferenceAsync(AppUser user, long id, long referenceId, CancellationToken token)
    {
        var plan = await EditablePlanAsync(user, id, token);
        if (plan.Problem is not null) return plan.Problem;
        var row = await db.ActionPlanReferences.FirstOrDefaultAsync(one => one.Id == referenceId && one.PlanId == id, token);
        if (row is null) return NotFound();
        db.ActionPlanReferences.Remove(row);
        await db.SaveChangesAsync(token);
        await Record(user, id, AuditActions.Delete, "reference", $"{row.Kind} {row.RefId} {row.Label}".Trim(), "", token);
        return new ActionPlanResult(true, "นำข้อมูลอ้างอิงออกแล้ว", Id: id);
    }

    /// <summary>Records evidence uploaded on the plan in its own history — the file itself is the document route's.</summary>
    public Task RecordEvidenceAsync(AppUser user, long id, string fileName, CancellationToken token) =>
        Record(user, id, AuditActions.Upload, "evidence", "", fileName, token);

    /* ------------------------------------------------------------------ helpers */

    private static ActionPlanResult Refused(string message, int status = StatusCodes.Status400BadRequest) => new(false, message, status);
    private static ActionPlanResult NotFound() => new(false, "ไม่พบแผนนี้", StatusCodes.Status404NotFound);
    private static string Cut(string value, int length) => value.Length > length ? value[..length] : value;
    private static string Text(string? value, int length) => Cut((value ?? "").Trim(), length);
    private static int? QuarterOf(string date) => Formats.DateNumber(date) is > 0 and var number ? (number / 100 % 100 + 2) / 3 : null;

    private Task Record(AppUser user, long id, string action, string field, string before, string after, CancellationToken token, string reason = "") =>
        audit.RecordAsync(user, action, "action-plan", id.ToString(), field, field, Cut(before, 400), Cut(after, 400), Cut(reason, 400), token);

    private async Task<(ActionPlan? Plan, ActionPlanResult? Problem)> EditablePlanAsync(AppUser user, long id, CancellationToken token)
    {
        var plan = await db.ActionPlans.FirstOrDefaultAsync(row => row.Id == id, token);
        if (plan is null || !CanSee(user, plan)) return (null, NotFound());
        if (!CanEdit(user, plan)) return (null, Refused("บัญชีนี้ไม่มีสิทธิ์แก้ไข Action Plan", StatusCodes.Status403Forbidden));
        if (plan.Status is ActionPlanRules.Completed or ActionPlanRules.Cancelled) return (null, Refused("แผนนี้ปิดแล้ว"));
        return (plan, null);
    }

    private async Task<List<ActionPlan>> VisibleAsync(AppUser user, CancellationToken token)
    {
        if (string.Equals(user.Role, Roles.Subcontractor, StringComparison.OrdinalIgnoreCase)) return [];
        var me = user.OperatorId ?? "";
        var query = db.ActionPlans.AsNoTracking();
        if (!user.Can(Capability.ReviewActionPlans))
            query = query.Where(plan => plan.DevelopmentType != ActionPlanRules.People || (me != "" && (plan.OwnerId == me || plan.EmployeeId == me)));
        return await query.ToListAsync(token);
    }

    private async Task<Dictionary<long, int?>> ProgressAsync(List<long> ids, CancellationToken token) =>
        (await db.ActionPlanItems.AsNoTracking().Where(row => ids.Contains(row.PlanId)).Select(row => new { row.PlanId, row.Status, row.Progress }).ToListAsync(token))
        .GroupBy(row => row.PlanId).ToDictionary(group => group.Key, group => ActionPlanRules.Progress(group.Select(row => (row.Status, row.Progress))));

    private async Task<int?> PlanProgressAsync(long id, CancellationToken token) =>
        (await ProgressAsync([id], token)).GetValueOrDefault(id);

    private static ActionPlanRow Row(ActionPlan plan, int? progress, int today) => new(plan.Id, plan.Number, plan.Title, plan.DevelopmentType,
        plan.Category, plan.TargetType, plan.TargetType == "employee" ? plan.EmployeeName : plan.TargetName, plan.EmployeeId, plan.SupplierId,
        plan.OwnerId, plan.OwnerName, plan.StartDate, plan.TargetDate, plan.ActualCompletionDate, progress, plan.Status,
        ActionPlanRules.Overdue(plan.Status, plan.TargetDate, today), plan.Priority, plan.Year, plan.Quarter, plan.Month, plan.UpdatedAt);

    /// <summary>Every field from the form onto the plan, or why not; people and companies come from their registers.</summary>
    private async Task<ActionPlanResult?> ApplyAsync(ActionPlan plan, ActionPlanInput input, AppUser user, CancellationToken token)
    {
        var kind = Text(input.DevelopmentType, 20).ToLowerInvariant();
        if (!ActionPlanRules.DevelopmentTypes.Contains(kind)) return Refused("เลือก People Development หรือ Subcontractor Development");
        var title = Text(input.Title, 200);
        if (title.Length == 0) return Refused("ต้องระบุชื่อแผน");
        var target = Text(input.TargetType, 20).ToLowerInvariant();
        if (!ActionPlanRules.TargetTypes[kind].Contains(target)) return Refused("กลุ่มเป้าหมายไม่ตรงกับประเภทการพัฒนา");
        var period = Text(input.Period, 20).ToLowerInvariant();
        if (period.Length == 0) period = "annual";
        if (!ActionPlanRules.Periods.Contains(period)) return Refused("รอบแผนไม่ถูกต้อง");
        var priority = Text(input.Priority, 20).ToLowerInvariant();
        if (priority.Length == 0) priority = "medium";
        if (!ActionPlanRules.Priorities.Contains(priority)) return Refused("ระดับความสำคัญไม่ถูกต้อง");
        foreach (var (date, label) in new[] { (input.StartDate, "วันเริ่ม"), (input.TargetDate, "วันครบกำหนด"), (input.ReviewDate, "วันทบทวน") })
            if (Text(date, 20).Length > 0 && Formats.DateNumber(Text(date, 20)) == 0) return Refused($"{label}ต้องเป็นรูปแบบ DD/MM/YYYY");
        if (Formats.DateNumber(Text(input.TargetDate, 20)) == 0) return Refused("ต้องระบุวันครบกำหนด");
        if (Formats.DateNumber(Text(input.StartDate, 20)) is > 0 and var start && start > Formats.DateNumber(Text(input.TargetDate, 20)))
            return Refused("วันครบกำหนดต้องไม่ก่อนวันเริ่ม");
        var year = input.Year ?? Formats.DateNumber(Text(input.TargetDate, 20)) / 10000;
        if (year is < 2000 or > 2100) return Refused("ปีไม่ถูกต้อง");
        if (input.Quarter is < 1 or > 4 || input.Month is < 1 or > 12) return Refused("ไตรมาสหรือเดือนไม่ถูกต้อง");

        var category = Text(input.Category, 120);
        if (category.Length > 0 && !await db.ActionPlanTypes.AnyAsync(row => row.DevelopmentType == kind && row.Name == category && row.Active, token)
            && !ActionPlanRules.DefaultCategories[kind].Contains(category))
            return Refused("ประเภทแผนไม่อยู่ในรายการ");

        var people = await db.Staff.AsNoTracking().Where(row => row.Active).Select(row => new { row.Id, row.Name, row.Role }).ToListAsync(token);
        var ownerId = Text(input.OwnerId, 60);
        if (ownerId.Length == 0) ownerId = user.OperatorId ?? "";
        var owner = people.FirstOrDefault(row => row.Id == ownerId && row.Role != Roles.Subcontractor);
        if (owner is null) return Refused("เจ้าของแผนต้องเป็นพนักงานในระบบ");

        string employeeId = "", employeeName = "", targetName = Text(input.TargetName, 200);
        int? supplierId = null;
        if (target == "employee")
        {
            var employee = people.FirstOrDefault(row => row.Id == Text(input.EmployeeId, 60) && row.Role != Roles.Subcontractor);
            if (employee is null) return Refused("เลือกพนักงานจากรายชื่อในระบบ");
            employeeId = employee.Id;
            employeeName = employee.Name;
            targetName = employee.Name;
        }
        else if (kind == ActionPlanRules.Subcontractor)
        {
            var supplier = input.SupplierId is { } wanted ? await db.Suppliers.AsNoTracking().FirstOrDefaultAsync(row => row.Id == wanted, token) : null;
            if (supplier is null) return Refused("เลือกผู้ขนส่งจากทะเบียนผู้รับเหมา");
            supplierId = supplier.Id;
            targetName = supplier.Name;
        }
        else if (targetName.Length == 0) targetName = target == "department" ? "Subcontract Management" : "";
        if (targetName.Length == 0) return Refused("ระบุทีมที่จะพัฒนา");

        plan.Title = title;
        plan.DevelopmentType = kind;
        plan.Category = category;
        plan.Period = period;
        plan.Year = year;
        plan.Quarter = period == "quarterly" ? input.Quarter ?? QuarterOf(Text(input.TargetDate, 20)) : input.Quarter;
        plan.Month = period == "monthly" ? input.Month ?? Formats.DateNumber(Text(input.TargetDate, 20)) / 100 % 100 : input.Month;
        plan.Priority = priority;
        plan.Description = Text(input.Description, 2000);
        plan.Objective = Text(input.Objective, 2000);
        plan.ExpectedOutcome = Text(input.ExpectedOutcome, 2000);
        plan.StartDate = Text(input.StartDate, 20);
        plan.TargetDate = Text(input.TargetDate, 20);
        plan.OwnerId = owner.Id;
        plan.OwnerName = owner.Name;
        plan.TargetType = target;
        plan.EmployeeId = employeeId;
        plan.EmployeeName = employeeName;
        plan.Position = Text(input.Position, 200);
        plan.Team = Text(input.Team, 200);
        plan.Supervisor = Text(input.Supervisor, 200);
        plan.SupplierId = supplierId;
        plan.TargetName = targetName;
        plan.DevelopmentArea = Text(input.DevelopmentArea, 200);
        plan.CurrentLevel = Text(input.CurrentLevel, 200);
        plan.TargetLevel = Text(input.TargetLevel, 200);
        plan.Gap = Text(input.Gap, 1000);
        plan.RootCause = Text(input.RootCause, 1000);
        plan.Method = Text(input.Method, 200);
        plan.Coach = Text(input.Coach, 200);
        plan.CarrierContact = Text(input.CarrierContact, 200);
        plan.EvaluationMethod = Text(input.EvaluationMethod, 200);
        plan.ReviewDate = Text(input.ReviewDate, 20);
        plan.Result = Text(input.Result, 1000);
        plan.Metric = Text(input.Metric, 200);
        plan.Baseline = input.Baseline;
        plan.TargetValue = input.TargetValue;
        plan.ActualValue = input.ActualValue;
        return null;
    }

    private async Task<ActionPlanResult?> ApplyItemAsync(ActionPlanItem item, ActionItemInput input, CancellationToken token)
    {
        var action = Text(input.Action, 300);
        if (action.Length == 0) return Refused("ต้องระบุสิ่งที่จะทำ");
        foreach (var date in new[] { input.StartDate, input.TargetDate, input.ActualCompletionDate, input.TrainingDate, input.CertificateExpiry })
            if (Text(date, 20).Length > 0 && Formats.DateNumber(Text(date, 20)) == 0) return Refused("วันที่ต้องเป็นรูปแบบ DD/MM/YYYY");
        var status = Text(input.Status, 20).ToLowerInvariant();
        if (status.Length == 0) status = item.Status.Length > 0 ? item.Status : ActionPlanRules.Planned;
        if (!ActionPlanRules.ItemStatuses.Contains(status)) return Refused("สถานะขั้นตอนไม่ถูกต้อง");
        var priority = Text(input.Priority, 20).ToLowerInvariant();
        if (priority.Length == 0) priority = "medium";
        if (!ActionPlanRules.Priorities.Contains(priority)) return Refused("ระดับความสำคัญไม่ถูกต้อง");
        var progress = input.Progress ?? item.Progress;
        if (progress is < 0 or > 100) return Refused("ความคืบหน้าต้องอยู่ระหว่าง 0–100");
        var ownerId = Text(input.OwnerId, 60);
        var owner = ownerId.Length == 0 ? null
            : await db.Staff.AsNoTracking().Where(row => row.Id == ownerId && row.Active && row.Role != Roles.Subcontractor)
                .Select(row => new { row.Id, row.Name }).FirstOrDefaultAsync(token);
        if (ownerId.Length > 0 && owner is null) return Refused("ผู้รับผิดชอบต้องเป็นพนักงานในระบบ");
        var trainingType = Text(input.TrainingType, 200);
        if (trainingType.Length > 0 && !ActionPlanRules.TrainingTypes.Contains(trainingType)) return Refused("ประเภทการอบรมไม่ถูกต้อง");

        item.Action = action;
        item.Description = Text(input.Description, 2000);
        item.OwnerId = owner?.Id ?? "";
        item.OwnerName = owner?.Name ?? "";
        item.SupportingPerson = Text(input.SupportingPerson, 200);
        item.SupportingDepartment = Text(input.SupportingDepartment, 200);
        item.StartDate = Text(input.StartDate, 20);
        item.TargetDate = Text(input.TargetDate, 20);
        item.Priority = priority;
        item.Status = status;
        // A completed step is a hundred per cent, whatever was typed beside it.
        item.Progress = status == ActionPlanRules.Completed ? 100 : progress;
        item.ActualCompletionDate = status == ActionPlanRules.Completed
            ? (Text(input.ActualCompletionDate, 20) is { Length: > 0 } done ? done : item.ActualCompletionDate.Length > 0 ? item.ActualCompletionDate : TodayText())
            : Text(input.ActualCompletionDate, 20);
        item.ExpectedResult = Text(input.ExpectedResult, 1000);
        item.ActualResult = Text(input.ActualResult, 1000);
        item.Remark = Text(input.Remark, 1000);
        item.TrainingTitle = Text(input.TrainingTitle, 200);
        item.TrainingType = trainingType;
        item.Trainer = Text(input.Trainer, 200);
        item.TrainingProvider = Text(input.TrainingProvider, 200);
        item.TrainingDate = Text(input.TrainingDate, 20);
        item.Participants = Text(input.Participants, 1000);
        item.CertificateExpiry = Text(input.CertificateExpiry, 20);
        return null;
    }
}
