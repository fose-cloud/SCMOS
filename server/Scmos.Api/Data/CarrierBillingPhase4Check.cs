using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Scmos.Api.Rules;

namespace Scmos.Api.Data;

/// <summary>Phase 4 billing eligibility, SLA state and additive schema checks.</summary>
public static class CarrierBillingPhase4Check
{
    public static int? Run(string[] args)
    {
        if (!args.Contains("--check-carrier-billing-phase4")) return null;
        var failed = 0;

        failed += Check("a completed job with a confirmed carrier is billing eligible",
            BillingEligibility.IsEligible(JobStatus.Completed, CarrierAssignment.Confirmed), true);
        failed += Check("an unfinished job is not billing eligible",
            BillingEligibility.IsEligible(JobStatus.Delivered, CarrierAssignment.Confirmed), false);
        failed += Check("an unconfirmed carrier is not billing eligible",
            BillingEligibility.IsEligible(JobStatus.Completed, CarrierAssignment.Pending), false);
        failed += Check("missing due date is a visible configuration problem",
            BillingSlaState.Of(new(2026, 9, 25), null), BillingSlaState.ConfigurationMissing);
        failed += Check("future due date is on track",
            BillingSlaState.Of(new(2026, 9, 25), new(2026, 9, 28)), BillingSlaState.OnTrack);
        failed += Check("the day before due is due soon",
            BillingSlaState.Of(new(2026, 9, 25), new(2026, 9, 26)), BillingSlaState.DueSoon);
        failed += Check("the due date is due today",
            BillingSlaState.Of(new(2026, 9, 25), new(2026, 9, 25)), BillingSlaState.DueToday);
        failed += Check("a past due date is overdue",
            BillingSlaState.Of(new(2026, 9, 25), new(2026, 9, 24)), BillingSlaState.Overdue);
        failed += Check("hyphenated Delivery Complete is normalised",
            CarrierOperations.IsDeliveryComplete("delivery-complete"), true);

        var options = new DbContextOptionsBuilder<ScmosDbContext>()
            .UseSqlServer("Server=(localdb)\\MSSQLLocalDB;Database=scmos-phase4-model;Trusted_Connection=True")
            .Options;
        using var db = new ScmosDbContext(options);
        var billingCase = db.Model.FindEntityType(typeof(BillingCase));
        var invoice = db.Model.FindEntityType(typeof(BillingInvoice));
        var link = db.Model.FindEntityType(typeof(BillingInvoiceJobLink));
        var document = db.Model.FindEntityType(typeof(StoredDocument));

        failed += Check("Billing Case reuses one unique SCMOS JobKey",
            billingCase?.GetIndexes().Any(index => index.IsUnique
                && index.Properties.Select(property => property.Name)
                    .SequenceEqual([nameof(BillingCase.JobKey)])) == true, true);
        failed += Check("every Billing Case requires a confirmed assignment foreign key",
            billingCase?.FindProperty(nameof(BillingCase.AssignmentId))?.IsNullable == false, true);
        failed += Check("one Billing Case cannot be linked to duplicate invoice drafts",
            link?.GetIndexes().Any(index => index.IsUnique
                && index.Properties.Select(property => property.Name)
                    .SequenceEqual([nameof(BillingInvoiceJobLink.BillingCaseId)])) == true, true);
        failed += Check("invoice numbers are unique inside one carrier tenant",
            invoice?.GetIndexes().Any(index => index.IsUnique
                && index.Properties.Select(property => property.Name)
                    .SequenceEqual([nameof(BillingInvoice.SupplierId), nameof(BillingInvoice.InvoiceNumber)])) == true, true);
        failed += Check("billing documents link to the Billing Case",
            document?.FindProperty(nameof(StoredDocument.BillingCaseId)) is not null, true);
        failed += Check("billing documents link to the Invoice Draft",
            document?.FindProperty(nameof(StoredDocument.BillingInvoiceId)) is not null, true);

        Console.WriteLine(failed == 0
            ? "Carrier Billing Phase 4 checks passed."
            : $"{failed} Carrier Billing Phase 4 check(s) failed.");
        return failed == 0 ? 0 : 1;
    }

    private static int Check<T>(string why, T got, T want)
    {
        var ok = EqualityComparer<T>.Default.Equals(got, want);
        Console.WriteLine($"  {(ok ? "ok  " : "FAIL")} {why}");
        if (!ok) Console.WriteLine($"       got {got}; want {want}");
        return ok ? 0 : 1;
    }
}
