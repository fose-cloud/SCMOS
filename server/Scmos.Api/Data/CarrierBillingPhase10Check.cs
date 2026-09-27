using Scmos.Api.Ai;
using Scmos.Api.Ai.Documents;
using Scmos.Api.Services;

namespace Scmos.Api.Data;

/// <summary>Phase 10 contract checks. They use no database, network or secret.</summary>
public static class CarrierBillingPhase10Check
{
    public static int? Run(string[] args)
    {
        if (!args.Contains("--check-carrier-billing-phase10")) return null;
        var failed = 0;
        void Check(bool condition, string message)
        {
            Console.WriteLine($"  {(condition ? "ok  " : "FAIL")} {message}");
            if (!condition) failed++;
        }

        var defaults = new AiOptions();
        Check(!defaults.BillingAiEnabled && !defaults.DocumentAiEnabled,
            "billing and document AI are independently default-off");
        Check(BillingAiKinds.All.SequenceEqual([
                "DOCUMENT_CLASSIFICATION", "INVOICE_EXTRACTION", "POD_EXTRACTION",
                "BILLING_RISK", "VARIANCE_EXPLANATION"]),
            "only the five reviewed Phase 10 analysis kinds are accepted");
        Check(BillingAiKinds.All.Where(BillingAiKinds.IsDocument).Count() == 3,
            "only classification, invoice and POD modes can read a document");
        Check(AiAuditRules.KnownTools.Contains("analyze_billing")
            && AiAuditRules.BillingAiViews.SequenceEqual(["billing_risk", "variance"])
            && new[] { "classification", "invoice", "pod" }.All(AiAuditRules.ExtractViews.Contains),
            "every Phase 10 provider read has an approved audit vocabulary");
        Check(typeof(BillingAiAnalysis).GetProperty(nameof(BillingAiAnalysis.ResultJson)) is not null
            && typeof(BillingAiAnalysis).GetProperty(nameof(BillingAiAnalysis.Confidence)) is not null
            && typeof(BillingAiAnalysis).GetProperty(nameof(BillingAiAnalysis.EvidenceReference)) is not null
            && typeof(BillingAiAnalysis).GetProperty(nameof(BillingAiAnalysis.DecidedBy)) is not null,
            "result, confidence, evidence and human decision are durable");
        Check(ExtractionRun.ViewOf("INVOICE") == "invoice" && ExtractionRun.ViewOf("POD") == "pod"
            && ExtractionRun.ViewOf("CLASSIFICATION") == "classification",
            "document reads retain their exact Phase 10 audit view");

        Console.WriteLine(failed == 0 ? "Carrier Billing Phase 10 checks passed."
            : $"Carrier Billing Phase 10 checks failed: {failed}");
        return failed == 0 ? 0 : 1;
    }
}
