using Scmos.Api.Rules;

namespace Scmos.Api.Data;

public static class CarrierBillingPhase8Check
{
    public static int? Run(string[] args)
    {
        if (!args.Contains("--check-carrier-billing-phase8")) return null;
        var failed = 0;
        void Check(bool condition, string message)
        {
            Console.WriteLine($"  {(condition ? "ok  " : "FAIL")} {message}");
            if (!condition) failed++;
        }

        var calendar = new Dictionary<DateOnly, string> { [new(2026, 9, 28)] = BusinessCalendarKind.PublicHoliday };
        BillingControlTowerFact Fact(long id, DateOnly? submitted, string status = BillingInvoiceStatus.SubconReview,
            string decision = "", DateOnly? approved = null, int reviewMinutes = 0, int reviewSamples = 0,
            int returns = 0, int decisions = 0, string validation = BillingValidationCategory.Pass) =>
                new(id, 1, new(2026, 9, 25), new(2026, 9, 30), submitted, approved, null,
                    status, validation, decision, decisions, returns, reviewMinutes, reviewSamples);
        var facts = new[] {
            Fact(1, new(2026, 9, 29), decision: BillingReviewAction.ApproveOnline,
                reviewMinutes: 90, reviewSamples: 1, decisions: 1),
            Fact(2, new(2026, 10, 1), decision: BillingReviewAction.ReturnToCarrier,
                reviewMinutes: 150, reviewSamples: 1, returns: 1, decisions: 1),
            Fact(3, null, BillingInvoiceStatus.AwaitingOriginal, approved: new(2026, 9, 27)),
        };
        var assignments = new[] {
            new AssignmentControlTowerFact(CarrierAssignment.Confirmed, false),
            new AssignmentControlTowerFact(CarrierAssignment.Confirmed, true),
            new AssignmentControlTowerFact(CarrierAssignment.Rejected, false),
            new AssignmentControlTowerFact(CarrierAssignment.Pending, false),
        };
        var measured = CarrierBillingControlTower.Measure(facts, assignments, new(2026, 10, 2), calendar);
        Check(CarrierBillingControlTower.SubmissionLeadWorkingDays(facts[0], calendar) == 2,
            "working-day lead skips the configured holiday and weekend");
        Check(measured.Within3WorkingDays == 1 && measured.Within3WorkingDaysPercent == 50m,
            "billing within three working days is measured over submitted invoices");
        Check(measured.Within4WorkingDays == 2 && measured.Within4WorkingDaysPercent == 100m,
            "billing within four working days is measured over submitted invoices");
        Check(measured.Overdue == 1, "an unsubmitted case past due is overdue");
        Check(measured.FirstTimeRight == 1 && measured.FirstTimeRightPercent == 50m,
            "first-time-right requires first-cycle online approval");
        var blockedFirst = CarrierBillingControlTower.Measure([
            Fact(4, new(2026, 9, 25), decision: BillingReviewAction.ApproveOnline,
                decisions: 1, validation: BillingValidationCategory.Blocked)], [], new(2026, 10, 2), calendar);
        Check(blockedFirst.FirstTimeRight == 0,
            "a first submission with blocking validation is not first-time-right after later approval");
        Check(measured.Returned == 1 && measured.ReturnRatePercent == 50m,
            "return rate uses human review decisions as its base");
        Check(measured.AverageInternalReviewMinutes == 120m,
            "internal review time averages completed review cycles");
        Check(measured.OriginalPending == 1 && measured.OldestOriginalPendingDays == 5,
            "original pending aging starts at online approval");
        Check(measured.CarrierAccepted == 2 && measured.CarrierAcceptancePercent == 66.7m,
            "carrier acceptance excludes pending and administrative outcomes");
        Check(measured.CarrierAcceptancePending == 1 && measured.TruckAssignmentPending == 1,
            "pending acceptance and accepted work without a truck remain visible");
        Check(CarrierBillingControlTower.SupplierScope(true, 17, 99) == 17,
            "carrier dashboard scope ignores another supplier id from the browser");
        Check(CarrierBillingControlTower.SupplierScope(false, null, 99) == 99,
            "internal dashboard may apply its requested supplier filter");

        Console.WriteLine(failed == 0 ? "Carrier Billing Phase 8 checks passed."
            : $"Carrier Billing Phase 8 checks failed: {failed}");
        return failed == 0 ? 0 : 1;
    }
}
