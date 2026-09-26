using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Scmos.Api.Auth;
using Scmos.Api.Data;
using Scmos.Api.Rules;

namespace Scmos.Api.Services;

public record BillingControlTowerItem(long CaseId, long? InvoiceId, string JobKey, string JobCode,
    string Customer, int SupplierId, string Supplier, string Status, string SlaState,
    DateTimeOffset DeliveryCompletedAt, DateTimeOffset? SubmittedAt, int? SubmissionLeadWorkingDays,
    int? InternalReviewMinutes, int? OriginalPendingDays, int ExceptionCount, int BlockingExceptionCount,
    bool FirstTimeRight, bool Returned, bool Overdue, string InvoiceNumber);

public record BillingControlTowerCarrier(int SupplierId, string Supplier,
    BillingControlTowerMetrics Metrics);

public record BillingControlTowerView(string From, string To, string Scope,
    BillingControlTowerMetrics Metrics, IReadOnlyList<BillingControlTowerCarrier> Carriers,
    IReadOnlyList<BillingControlTowerItem> Items, IReadOnlyDictionary<string, int> Exceptions);

public record BillingControlTowerResult(bool Ok, string Code, string Message,
    BillingControlTowerView? Dashboard = null);

/// <summary>
/// Phase 8 read model. Every query is bounded by delivery date and carrier
/// scope is resolved from the authenticated identity, never from the browser.
/// No financial or workflow state is changed while building the dashboard.
/// </summary>
public class CarrierBillingControlTowerService(ScmosDbContext db, CarrierTenantContext tenants)
{
    private static readonly TimeSpan Thailand = TimeSpan.FromHours(7);

    public async Task<BillingControlTowerResult> BuildAsync(AppUser user, DateOnly from, DateOnly to,
        int? requestedSupplierId, CancellationToken token)
    {
        if (to < from || to.DayNumber - from.DayNumber > 366)
            return Fail("INVALID_PERIOD", "ช่วงวันที่ต้องไม่เกิน 366 วัน และวันสิ้นสุดต้องไม่ก่อนวันเริ่มต้น");

        int? supplierId = requestedSupplierId;
        string scope;
        if (CarrierTenantContext.IsCarrier(user))
        {
            var tenant = await tenants.ResolveAsync(user, token);
            if (tenant is null) return Fail("NO_CARRIER", "บัญชีนี้ไม่ได้ผูกกับบริษัทผู้รับเหมา");
            supplierId = CarrierBillingControlTower.SupplierScope(true, tenant.SupplierId, requestedSupplierId);
            scope = tenant.SupplierName;
        }
        else
        {
            if (!user.Can(Capability.ViewDashboard))
                return Fail("FORBIDDEN", "บัญชีนี้ไม่มีสิทธิ์ดู Control Tower");
            supplierId = CarrierBillingControlTower.SupplierScope(false, null, requestedSupplierId);
            scope = supplierId is null ? "ALL" : "SUPPLIER";
        }

        var fromAt = new DateTimeOffset(from.ToDateTime(TimeOnly.MinValue), Thailand).ToUniversalTime();
        var untilAt = new DateTimeOffset(to.AddDays(1).ToDateTime(TimeOnly.MinValue), Thailand).ToUniversalTime();
        var caseQuery = db.BillingCases.AsNoTracking().Where(row =>
            row.DeliveryCompletedAt >= fromAt && row.DeliveryCompletedAt < untilAt);
        if (supplierId is { } heldSupplier) caseQuery = caseQuery.Where(row => row.SupplierId == heldSupplier);
        var cases = await caseQuery.OrderByDescending(row => row.DeliveryCompletedAt).Take(5000).ToListAsync(token);

        var caseIds = cases.Select(row => row.Id).ToList();
        var jobKeys = cases.Select(row => row.JobKey).Distinct().ToList();
        var supplierIds = cases.Select(row => row.SupplierId).Distinct().ToList();
        var links = await db.BillingInvoiceJobLinks.AsNoTracking()
            .Where(row => caseIds.Contains(row.BillingCaseId)).ToListAsync(token);
        var invoiceIds = links.Select(row => row.InvoiceId).Distinct().ToList();
        var invoices = await db.BillingInvoices.AsNoTracking().Where(row => invoiceIds.Contains(row.Id))
            .ToDictionaryAsync(row => row.Id, token);
        var jobs = await db.OperationJobs.AsNoTracking().Where(row => jobKeys.Contains(row.Key))
            .ToDictionaryAsync(row => row.Key, token);
        var suppliers = await db.Suppliers.AsNoTracking().Where(row => supplierIds.Contains(row.Id))
            .ToDictionaryAsync(row => row.Id, row => row.Name, token);
        var runs = await db.BillingValidationRuns.AsNoTracking().Where(row => invoiceIds.Contains(row.InvoiceId))
            .OrderBy(row => row.Sequence).ToListAsync(token);
        var firstRuns = runs.GroupBy(row => row.InvoiceId).ToDictionary(group => group.Key, group => group.First());
        var latestRuns = runs.GroupBy(row => row.InvoiceId).ToDictionary(group => group.Key, group => group.Last());
        var latestRunIds = latestRuns.Values.Select(row => row.Id).ToList();
        var validationRows = await db.BillingValidationResults.AsNoTracking()
            .Where(row => latestRunIds.Contains(row.RunId)).ToListAsync(token);
        var validations = validationRows.GroupBy(row => row.InvoiceId).ToDictionary(group => group.Key, group => group.ToList());
        var reviewRows = await db.BillingReviewEvents.AsNoTracking().Where(row => invoiceIds.Contains(row.InvoiceId))
            .OrderBy(row => row.Id).ToListAsync(token);
        var reviews = reviewRows.GroupBy(row => row.InvoiceId).ToDictionary(group => group.Key, group => group.ToList());
        var originals = await db.OriginalDocumentPackages.AsNoTracking().Where(row => invoiceIds.Contains(row.InvoiceId))
            .ToDictionaryAsync(row => row.InvoiceId, token);

        var calendarFrom = cases.Where(row => row.SlaStartDate is not null).Select(row => row.SlaStartDate!.Value)
            .DefaultIfEmpty(from).Min();
        var today = DateOnly.FromDateTime(DateTimeOffset.UtcNow.ToOffset(Thailand).DateTime);
        var overrides = await db.BusinessCalendarDays.AsNoTracking()
            .Where(row => row.Date >= calendarFrom && row.Date <= (to > today ? to : today))
            .ToDictionaryAsync(row => row.Date, row => row.Kind, token);

        var facts = new List<BillingControlTowerFact>(cases.Count);
        var items = new List<BillingControlTowerItem>(cases.Count);
        foreach (var billingCase in cases)
        {
            var link = links.FirstOrDefault(row => row.BillingCaseId == billingCase.Id);
            var invoice = link is not null && invoices.TryGetValue(link.InvoiceId, out var heldInvoice) ? heldInvoice : null;
            var invoiceReviews = invoice is null ? [] : reviews.GetValueOrDefault(invoice.Id, []);
            var decisions = invoiceReviews.Where(IsDecision).ToList();
            var firstDecision = decisions.FirstOrDefault(row => row.Cycle == 1)?.Action ?? "";
            var firstValidation = invoice is not null && firstRuns.TryGetValue(invoice.Id, out var firstRun)
                ? firstRun.Outcome : "";
            var durations = ReviewDurations(invoiceReviews);
            var submittedOn = LocalDate(invoice?.SubmittedAt);
            var approvedOn = LocalDate(invoice?.OnlineApprovedAt);
            var originalReceivedOn = invoice is not null && originals.TryGetValue(invoice.Id, out var package)
                ? LocalDate(package.ReceivedAt) : null;
            var fact = new BillingControlTowerFact(billingCase.Id, billingCase.SupplierId,
                billingCase.SlaStartDate, billingCase.SlaDueDate, submittedOn, approvedOn,
                originalReceivedOn, invoice?.Status ?? billingCase.Status, firstValidation, firstDecision,
                decisions.Count, decisions.Count(row => row.Action == BillingReviewAction.ReturnToCarrier),
                durations.Sum(), durations.Count);
            facts.Add(fact);

            var latest = invoice is null ? [] : validations.GetValueOrDefault(invoice.Id, []);
            var lead = CarrierBillingControlTower.SubmissionLeadWorkingDays(fact, overrides);
            var pendingAge = fact.Status == BillingInvoiceStatus.AwaitingOriginal && approvedOn is { } approved
                ? Math.Max(0, today.DayNumber - approved.DayNumber) : (int?)null;
            jobs.TryGetValue(billingCase.JobKey, out var job);
            var overdue = submittedOn is null && billingCase.SlaDueDate is { } due && due < today;
            items.Add(new(billingCase.Id, invoice?.Id, billingCase.JobKey, job?.JobCode ?? billingCase.JobKey,
                job?.Customer ?? "", billingCase.SupplierId, suppliers.GetValueOrDefault(billingCase.SupplierId, ""),
                fact.Status, submittedOn is not null
                    ? (billingCase.SlaDueDate is { } submittedDue && submittedOn > submittedDue ? "SUBMITTED_LATE" : "SUBMITTED_ON_TIME")
                    : BillingSlaState.Of(today, billingCase.SlaDueDate),
                billingCase.DeliveryCompletedAt, invoice?.SubmittedAt, lead,
                durations.Count == 0 ? null : (int?)Math.Round(durations.Average()), pendingAge,
                latest.Count(row => row.Category is BillingValidationCategory.Exception or BillingValidationCategory.Blocked),
                latest.Count(row => row.Blocking), firstDecision == BillingReviewAction.ApproveOnline,
                decisions.Any(row => row.Action == BillingReviewAction.ReturnToCarrier), overdue,
                invoice?.InvoiceNumber ?? ""));
        }

        var assignmentQuery = db.SupplierRequests.AsNoTracking().Where(row =>
            row.RequestedAt >= fromAt && row.RequestedAt < untilAt);
        if (supplierId is { } assignmentSupplier) assignmentQuery = assignmentQuery.Where(row => row.SupplierId == assignmentSupplier);
        var assignments = await assignmentQuery.ToListAsync(token);
        var assignmentJobs = assignments.Select(row => row.JobKey).Distinct().ToList();
        var assignmentJobRows = await db.OperationJobs.AsNoTracking().Where(row => assignmentJobs.Contains(row.Key))
            .ToDictionaryAsync(row => row.Key, token);
        var assignmentFacts = assignments.Select(row => new AssignmentControlTowerFact(row.Outcome,
            row.Outcome == CarrierAssignment.Confirmed
            && (!assignmentJobRows.TryGetValue(row.JobKey, out var job) || TruckPending(job)))).ToList();

        var metrics = CarrierBillingControlTower.Measure(facts, assignmentFacts, today, overrides);
        var carriers = facts.GroupBy(row => row.SupplierId).Select(group =>
        {
            var carrierAssignments = assignments.Where(row => row.SupplierId == group.Key).Select(row =>
                new AssignmentControlTowerFact(row.Outcome,
                    row.Outcome == CarrierAssignment.Confirmed
                    && (!assignmentJobRows.TryGetValue(row.JobKey, out var job) || TruckPending(job)))).ToList();
            return new BillingControlTowerCarrier(group.Key, suppliers.GetValueOrDefault(group.Key, ""),
                CarrierBillingControlTower.Measure(group.ToList(), carrierAssignments, today, overrides));
        }).OrderByDescending(row => row.Metrics.BillingCases).ThenBy(row => row.Supplier).ToList();
        var exceptions = validationRows.Where(row => row.Category != BillingValidationCategory.Pass)
            .GroupBy(row => row.Code).OrderByDescending(group => group.Count())
            .ToDictionary(group => group.Key, group => group.Count());

        var displayScope = supplierId is null ? scope : suppliers.GetValueOrDefault(supplierId.Value, scope);
        return new(true, "OK", "", new(from.ToString("yyyy-MM-dd"), to.ToString("yyyy-MM-dd"),
            displayScope, metrics, carriers, items, exceptions));
    }

    private static bool IsDecision(BillingReviewEvent row) => row.Action is
        BillingReviewAction.ApproveOnline or BillingReviewAction.ReturnToCarrier or BillingReviewAction.RaiseDispute;

    private static List<int> ReviewDurations(IReadOnlyList<BillingReviewEvent> events)
    {
        var output = new List<int>();
        foreach (var cycle in events.GroupBy(row => row.Cycle))
        {
            var submitted = cycle.FirstOrDefault(row => row.Action is BillingReviewAction.Submitted or BillingReviewAction.Resubmitted);
            var decided = cycle.FirstOrDefault(IsDecision);
            if (submitted is not null && decided is not null && decided.At >= submitted.At)
                output.Add((int)Math.Round((decided.At - submitted.At).TotalMinutes));
        }
        return output;
    }

    private static DateOnly? LocalDate(DateTimeOffset? value) => value is null
        ? null : DateOnly.FromDateTime(value.Value.ToOffset(Thailand).DateTime);

    private static bool TruckPending(OperationJob job)
    {
        if (job.Status is JobStatus.Completed or JobStatus.Cancelled) return false;
        if (job.Status == JobStatus.SupplierConfirmed) return true;
        try
        {
            var node = JsonNode.Parse(job.Data)?.AsObject();
            return string.IsNullOrWhiteSpace(node?["licence"]?.ToString());
        }
        catch (JsonException) { return true; }
    }

    private static BillingControlTowerResult Fail(string code, string message) => new(false, code, message);
}
