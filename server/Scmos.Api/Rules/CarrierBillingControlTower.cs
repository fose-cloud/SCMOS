namespace Scmos.Api.Rules;

public record BillingControlTowerFact(
    long CaseId,
    int SupplierId,
    DateOnly? SlaStartDate,
    DateOnly? SlaDueDate,
    DateOnly? SubmittedOn,
    DateOnly? OnlineApprovedOn,
    DateOnly? OriginalReceivedOn,
    string Status,
    string FirstValidationOutcome,
    string FirstDecisionAction,
    int ReviewDecisionCount,
    int ReturnCount,
    int ReviewDurationMinutes,
    int ReviewDurationSamples);

public record AssignmentControlTowerFact(string Outcome, bool TruckAssignmentPending);

public record BillingControlTowerMetrics(
    int BillingCases,
    int Submitted,
    int Within3WorkingDays,
    decimal? Within3WorkingDaysPercent,
    int Within4WorkingDays,
    decimal? Within4WorkingDaysPercent,
    int Overdue,
    int FirstTimeRight,
    int FirstTimeRightBase,
    decimal? FirstTimeRightPercent,
    int Returned,
    int ReviewDecisions,
    decimal? ReturnRatePercent,
    decimal? AverageSubmissionLeadWorkingDays,
    decimal? AverageInternalReviewMinutes,
    int OriginalPending,
    decimal? AverageOriginalPendingDays,
    int OldestOriginalPendingDays,
    int CarrierAccepted,
    int CarrierDecisions,
    decimal? CarrierAcceptancePercent,
    int CarrierAcceptancePending,
    int TruckAssignmentPending);

/// <summary>
/// Pure Phase 8 metric rules. The service supplies already tenant-scoped facts
/// and the reviewed business calendar; this type only performs deterministic
/// arithmetic, which keeps the dashboard, drill-down and tests on one rule.
/// </summary>
public static class CarrierBillingControlTower
{
    /// <summary>A carrier-supplied filter can only narrow to its own tenant.</summary>
    public static int? SupplierScope(bool isCarrier, int? tenantSupplierId, int? requestedSupplierId) =>
        isCarrier ? tenantSupplierId : requestedSupplierId;

    public static int? SubmissionLeadWorkingDays(BillingControlTowerFact fact,
        IReadOnlyDictionary<DateOnly, string> overrides)
    {
        if (fact.SlaStartDate is not { } start || fact.SubmittedOn is not { } submitted) return null;
        if (submitted < start) return 0;
        var count = 0;
        for (var date = start; date <= submitted; date = date.AddDays(1))
            if (BusinessCalendar.IsWorkingDay(date, overrides)) count++;
        return count;
    }

    public static BillingControlTowerMetrics Measure(
        IReadOnlyList<BillingControlTowerFact> billing,
        IReadOnlyList<AssignmentControlTowerFact> assignments,
        DateOnly today,
        IReadOnlyDictionary<DateOnly, string> overrides)
    {
        var leads = billing.Select(row => SubmissionLeadWorkingDays(row, overrides))
            .Where(value => value is not null).Select(value => value!.Value).ToList();
        var within3 = leads.Count(value => value <= 3);
        var within4 = leads.Count(value => value <= 4);
        var firstDecisions = billing.Where(row => row.FirstDecisionAction.Length > 0).ToList();
        var firstTimeRight = firstDecisions.Count(row =>
            row.FirstDecisionAction == BillingReviewAction.ApproveOnline
            && row.FirstValidationOutcome != BillingValidationCategory.Blocked);
        var reviewDecisions = billing.Sum(row => row.ReviewDecisionCount);
        var returned = billing.Sum(row => row.ReturnCount);
        var reviewSamples = billing.Sum(row => row.ReviewDurationSamples);
        var reviewMinutes = billing.Sum(row => row.ReviewDurationMinutes);
        var originalAges = billing.Where(row => row.Status == BillingInvoiceStatus.AwaitingOriginal
                && row.OnlineApprovedOn is not null)
            .Select(row => Math.Max(0, today.DayNumber - row.OnlineApprovedOn!.Value.DayNumber)).ToList();
        var decidedAssignments = assignments.Where(row => row.Outcome is
            CarrierAssignment.Confirmed or CarrierAssignment.Rejected).ToList();
        var accepted = decidedAssignments.Count(row => row.Outcome == CarrierAssignment.Confirmed);

        return new(
            billing.Count,
            leads.Count,
            within3,
            Percent(within3, leads.Count),
            within4,
            Percent(within4, leads.Count),
            billing.Count(row => row.SubmittedOn is null && row.SlaDueDate is { } due && due < today),
            firstTimeRight,
            firstDecisions.Count,
            Percent(firstTimeRight, firstDecisions.Count),
            returned,
            reviewDecisions,
            Percent(returned, reviewDecisions),
            leads.Count == 0 ? null : decimal.Round((decimal)leads.Average(), 2),
            reviewSamples == 0 ? null : decimal.Round((decimal)reviewMinutes / reviewSamples, 1),
            originalAges.Count,
            originalAges.Count == 0 ? null : decimal.Round((decimal)originalAges.Average(), 1),
            originalAges.Count == 0 ? 0 : originalAges.Max(),
            accepted,
            decidedAssignments.Count,
            Percent(accepted, decidedAssignments.Count),
            assignments.Count(row => row.Outcome == CarrierAssignment.Pending),
            assignments.Count(row => row.Outcome == CarrierAssignment.Confirmed && row.TruckAssignmentPending));
    }

    private static decimal? Percent(int numerator, int denominator) => denominator == 0
        ? null
        : decimal.Round(numerator * 100m / denominator, 1);
}
