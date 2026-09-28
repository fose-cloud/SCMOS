using Microsoft.Extensions.Options;
using Scmos.Api.Auth;
using Scmos.Api.Rules;
using Scmos.Api.Services;

namespace Scmos.Api.Data;

/// <summary>Phase 11 contract checks. No database, network, credentials or ERP is used.</summary>
public static class CarrierBillingPhase11Check
{
    public static int? Run(string[] args)
    {
        if (!args.Contains("--check-carrier-billing-phase11")) return null;
        var failed = 0;
        void Check(bool condition, string message)
        {
            Console.WriteLine($"  {(condition ? "ok  " : "FAIL")} {message}");
            if (!condition) failed++;
        }

        var defaults = new FinanceIntegrationOptions();
        Check(!defaults.Enabled && defaults.Adapter == "None" && defaults.Roles().Count == 0,
            "Finance integration and release roles default fail-closed");

        var ready = BillingFinanceReadiness.Evaluate(true, true, false);
        Check(ready.Ready && ready.InvoiceStatus == BillingInvoiceStatus.ReadyForFinance,
            "deterministic Finance readiness remains the release authority");
        Check(!BillingFinanceReadiness.Evaluate(false, true, false).Ready
            && !BillingFinanceReadiness.Evaluate(true, false, false).Ready
            && !BillingFinanceReadiness.Evaluate(true, true, true).Ready,
            "approval, Original and no blocking exception are all required");

        var firstRetry = FinanceRetry.Next(1, DateTimeOffset.UnixEpoch);
        Check(firstRetry == DateTimeOffset.UnixEpoch.AddMinutes(1)
            && FinanceRetry.Next(6, DateTimeOffset.UnixEpoch) is null,
            "retry is bounded and ends in an operator-visible dead letter");
        Check(FinanceReconciliation.CanApply(FinanceStatus.Submitted, FinanceStatus.Accepted)
            && FinanceReconciliation.CanApply(FinanceStatus.Accepted, FinanceStatus.Paid)
            && FinanceReconciliation.CanApply(FinanceStatus.Paid, FinanceStatus.Closed)
            && !FinanceReconciliation.CanApply(FinanceStatus.Submitted, FinanceStatus.Closed),
            "Finance reconciliation rejects skipped transitions");
        var carrierStatuses = CarrierBillingVisibility.StorageStatuses([BillingCaseStatus.ReadyForFinance]);
        Check(CarrierBillingVisibility.PublicStatus(BillingCaseStatus.Paid) == BillingCaseStatus.ReadyForFinance
            && carrierStatuses.Contains(BillingCaseStatus.ReadyForFinance)
            && carrierStatuses.Contains(BillingCaseStatus.FinanceProcessing)
            && carrierStatuses.Contains(BillingCaseStatus.FinanceRejected)
            && carrierStatuses.Contains(BillingCaseStatus.Paid)
            && carrierStatuses.Contains(BillingCaseStatus.Closed),
            "carrier status masking is consistent for rows and filtered paging");

        var canonical = typeof(CanonicalFinanceInvoice);
        Check(new[] { "CarrierName", "InvoiceNumber", "InvoiceDate", "Currency", "Jobs", "BaseCost",
                "ApprovedAdditionalCharges", "Tax", "GrandTotal", "OnlineApprovedAt",
                "OriginalReceivedAt", "ApprovalReference" }
            .All(name => canonical.GetProperty(name) is not null),
            "canonical payload carries the reviewed Finance fields without an ERP-specific DTO");
        Check(typeof(IntegrationOutboxEvent).GetProperty(nameof(IntegrationOutboxEvent.IdempotencyKey)) is not null
            && typeof(BillingFinanceRecord).GetProperty(nameof(BillingFinanceRecord.PayloadHash)) is not null
            && typeof(BillingFinanceRecord).GetProperty(nameof(BillingFinanceRecord.RowVersion)) is not null,
            "outbox idempotency, immutable snapshot hash and concurrency are durable");

        var mock = new MockFinanceAdapter();
        var sample = new CanonicalFinanceInvoice("scmos.finance.invoice.v1", 42, 7, "C7", "Carrier",
            "INV-42", "2026-09-28", "THB", [], 100m, [], 7m, 107m,
            DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, "billing-review:1:cycle:1");
        var result = mock.SubmitAsync(sample, "phase11-check", CancellationToken.None).GetAwaiter().GetResult();
        Check(result.Ok && result.Status == FinanceStatus.Accepted
            && result.ExternalReference == "MOCK-FIN-42", "mock adapter is deterministic and offline");
        var internalResult = new InternalFinanceQueueAdapter().SubmitAsync(sample,
            "phase11-internal-check", CancellationToken.None).GetAwaiter().GetResult();
        Check(internalResult.Ok && internalResult.Status == FinanceStatus.Submitted
            && internalResult.ExternalReference == "SCMOS-FIN-42"
            && internalResult.Code == "INTERNAL_QUEUE",
            "SCMOS internal Finance queue is deterministic and makes no external call");
        Check(!new UnconfiguredFinanceAdapter().Configured, "unknown Production Finance target stays unconfigured");

        var configured = Options.Create(new FinanceIntegrationOptions
            { ReleaseRoles = [Roles.Manager, Roles.AssistantManager] });
        var policy = new FinanceReleasePolicy(configured);
        var manager = new AppUser("manager", "manager@local", "Manager", Roles.Manager, "M-1", "test", true);
        var assistant = new AppUser("assistant", "assistant@local", "Assistant Manager",
            Roles.AssistantManager, "AM-1", "test", true);
        var supervisor = new AppUser("supervisor", "supervisor@local", "Supervisor",
            Roles.Supervisor, "SV-1", "test", true);
        var admin = new AppUser("admin", "admin@local", "Administrator",
            Roles.Admin, "AD-1", "test", true);
        var carrier = new AppUser("carrier", "carrier@local", "Carrier", Roles.Subcontractor, "C-1", "test", true);
        Check(policy.CanRelease(manager) && policy.CanRelease(assistant)
            && !policy.CanRelease(supervisor) && !policy.CanRelease(admin)
            && !policy.CanRelease(carrier),
            "only Manager and Assistant Manager may release Finance");

        Console.WriteLine(failed == 0 ? "Carrier Billing Phase 11 checks passed."
            : $"Carrier Billing Phase 11 checks failed: {failed}");
        return failed == 0 ? 0 : 1;
    }
}
