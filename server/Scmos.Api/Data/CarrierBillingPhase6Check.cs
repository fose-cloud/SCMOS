using Microsoft.EntityFrameworkCore;
using Scmos.Api.Auth;
using Scmos.Api.Rules;

namespace Scmos.Api.Data;

public static class CarrierBillingPhase6Check
{
    public static int? Run(string[] args)
    {
        if (!args.Contains("--check-carrier-billing-phase6")) return null;
        var failed = 0;
        var operation = new AppUser("op", "op@local", "Operation", Roles.Operation, "OP-01", "test", true);
        var carrier = new AppUser("carrier", "carrier@local", "Carrier", Roles.Subcontractor, "SUB-01", "test", true);
        failed += Check("operation may review billing", operation.Can(Capability.ReviewBilling), true);
        failed += Check("carrier may not review billing", carrier.Can(Capability.ReviewBilling), false);
        failed += Check("review queue may be approved or returned", BillingReviewTransitions.CanAct(BillingInvoiceStatus.SubconReview), true);
        failed += Check("draft may not bypass validation", BillingReviewTransitions.CanAct(BillingInvoiceStatus.Draft), false);
        failed += Check("online approval waits for the original", BillingReviewTransitions.NextStatus(BillingReviewAction.ApproveOnline), BillingInvoiceStatus.AwaitingOriginal);
        failed += Check("return sends the invoice back to its carrier", BillingReviewTransitions.NextStatus(BillingReviewAction.ReturnToCarrier), BillingInvoiceStatus.Returned);
        failed += Check("dispute enters its own visible state", BillingReviewTransitions.NextStatus(BillingReviewAction.RaiseDispute), BillingInvoiceStatus.Disputed);
        failed += Check("returned invoice may be resubmitted", BillingReviewTransitions.CanResubmit(BillingInvoiceStatus.Returned), true);
        failed += Check("disputed invoice may be resubmitted", BillingReviewTransitions.CanResubmit(BillingInvoiceStatus.Disputed), true);
        failed += Check("approved invoice may not be resubmitted", BillingReviewTransitions.CanResubmit(BillingInvoiceStatus.AwaitingOriginal), false);
        failed += Check("structured return reason is accepted", BillingReturnReason.Problem("WRONG_RATE", "") is null, true);
        failed += Check("OTHER requires a remark", BillingReturnReason.Problem("OTHER", "") is not null, true);
        failed += Check("unknown return reason is rejected", BillingReturnReason.Problem("TYPO", "detail") is not null, true);
        var first = new DateTimeOffset(2026, 9, 1, 8, 0, 0, TimeSpan.Zero);
        var second = new DateTimeOffset(2026, 9, 2, 8, 0, 0, TimeSpan.Zero);
        failed += Check("resubmit does not reset the original SLA clock", BillingReviewTransitions.PreserveFirstSubmitted(first, second), first);

        var options = new DbContextOptionsBuilder<ScmosDbContext>()
            .UseSqlServer("Server=(localdb)\\MSSQLLocalDB;Database=scmos-phase6-model;Trusted_Connection=True").Options;
        using var db = new ScmosDbContext(options);
        var review = db.Model.FindEntityType(typeof(BillingReviewEvent));
        failed += Check("review decisions have an append-only audit entity", review is not null, true);
        failed += Check("review audit records actor", review?.FindProperty(nameof(BillingReviewEvent.ActorId)) is not null, true);
        failed += Check("review audit records transition", review?.FindProperty(nameof(BillingReviewEvent.FromStatus)) is not null
            && review.FindProperty(nameof(BillingReviewEvent.ToStatus)) is not null, true);
        failed += Check("review audit is linked to its invoice", review?.GetForeignKeys().Any(x => x.Properties.Any(p => p.Name == nameof(BillingReviewEvent.InvoiceId))) == true, true);
        Console.WriteLine(failed == 0 ? "Carrier Billing Phase 6 checks passed." : $"{failed} Carrier Billing Phase 6 check(s) failed.");
        return failed == 0 ? 0 : 1;
    }

    private static int Check<T>(string why, T got, T want)
    {
        var ok = EqualityComparer<T>.Default.Equals(got, want);
        Console.WriteLine($"  {(ok ? "ok  " : "FAIL")} {why}");
        return ok ? 0 : 1;
    }
}
