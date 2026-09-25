using Microsoft.EntityFrameworkCore;
using Scmos.Api.Rules;

namespace Scmos.Api.Data;

public static class CarrierBillingPhase5Check
{
    public static int? Run(string[] args)
    {
        if (!args.Contains("--check-carrier-billing-phase5")) return null;
        var failed = 0;
        failed += Check("missing required document blocks", BillingValidationRules.Document(false, true).Code, "MISSING_REQUIRED_DOCUMENT");
        failed += Check("rate match passes", BillingValidationRules.Rate(1, 1000, 1000).Code, "CONTRACT_RATE_MATCH");
        failed += Check("rate mismatch is detected", BillingValidationRules.Rate(1, 1000, 1200).Code, "CONTRACT_RATE_MISMATCH");
        failed += Check("multiple applicable rates block", BillingValidationRules.Rate(2, null, 1000).Code, "MULTIPLE_RATE_MATCH");
        failed += Check("missing rate blocks", BillingValidationRules.Rate(0, null, 1000).Code, "CONTRACT_RATE_NOT_FOUND");
        failed += Check("approved charge passes", BillingValidationRules.Charge(true).Code, "ADDITIONAL_CHARGE_APPROVED");
        failed += Check("unapproved charge blocks", BillingValidationRules.Charge(false).Code, "UNAPPROVED_ADDITIONAL_CHARGE");
        failed += Check("tax match passes", BillingValidationRules.Tax(70, 70).Code, "TAX_MATCH");
        failed += Check("tax mismatch is detected", BillingValidationRules.Tax(70, 69).Code, "TAX_MISMATCH");
        failed += Check("duplicate invoice blocks", BillingValidationRules.Duplicate(true, false, false).Code, "DUPLICATE_INVOICE_NUMBER");
        failed += Check("job already billed blocks", BillingValidationRules.Duplicate(false, true, false).Code, "JOB_ALREADY_BILLED");

        var options = new DbContextOptionsBuilder<ScmosDbContext>().UseSqlServer("Server=(localdb)\\MSSQLLocalDB;Database=scmos-phase5-model;Trusted_Connection=True").Options;
        using var db = new ScmosDbContext(options);
        failed += Check("requirement rules are effective-dated", db.Model.FindEntityType(typeof(BillingRequirementRule))?.FindProperty(nameof(BillingRequirementRule.EffectiveFrom)) is not null, true);
        failed += Check("tax rules are effective-dated", db.Model.FindEntityType(typeof(BillingTaxRule))?.FindProperty(nameof(BillingTaxRule.EffectiveFrom)) is not null, true);
        failed += Check("individual validation results are persisted", db.Model.FindEntityType(typeof(BillingValidationResult)) is not null, true);
        Console.WriteLine(failed == 0 ? "Carrier Billing Phase 5 checks passed." : $"{failed} Carrier Billing Phase 5 check(s) failed.");
        return failed == 0 ? 0 : 1;
    }
    private static int Check<T>(string why, T got, T want) { var ok = EqualityComparer<T>.Default.Equals(got, want); Console.WriteLine($"  {(ok ? "ok  " : "FAIL")} {why}"); return ok ? 0 : 1; }
}
