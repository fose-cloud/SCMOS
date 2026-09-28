using Microsoft.EntityFrameworkCore;
using Scmos.Api.Rules;
using Scmos.Api.Services;

namespace Scmos.Api.Data;

/// <summary>
/// Final cross-phase acceptance gate for Carrier Collaboration + Billing.
/// It composes the authoritative domain rules and inspects the EF model only;
/// no database, network, Production data, credential or external Finance system is used.
/// </summary>
public static class CarrierBillingFinalAcceptanceCheck
{
    public static int? Run(string[] args)
    {
        if (!args.Contains("--check-carrier-billing-acceptance")) return null;
        var failed = 0;
        void Check(bool condition, string message)
        {
            Console.WriteLine($"  {(condition ? "ok  " : "FAIL")} {message}");
            if (!condition) failed++;
        }

        // One continuous business path, composed from the same rules used by the APIs.
        var acceptance = CarrierAssignment.DecideAnswer(CarrierAssignment.Pending, CarrierAssignment.Confirmed);
        var acceptanceReplay = CarrierAssignment.DecideAnswer(CarrierAssignment.Confirmed, CarrierAssignment.Confirmed);
        Check(acceptance == AssignmentAnswerDecision.Apply
            && acceptanceReplay == AssignmentAnswerDecision.Replay,
            "a Carrier accepts once and an identical retry is idempotent");

        var delivery = CarrierOperations.Decide("DELIVERY", JobStatus.Delivered,
            CarrierOperations.DeliveryComplete);
        var deliveryReplay = CarrierOperations.Decide("DELIVERY", JobStatus.Completed,
            CarrierOperations.DeliveryComplete);
        Check(delivery.Decision == CarrierOperationDecision.Apply
            && delivery.TargetStatus == JobStatus.Completed
            && deliveryReplay.Decision == CarrierOperationDecision.Replay,
            "Delivery Complete closes the original Job once and safely replays");
        Check(BillingEligibility.IsEligible(delivery.TargetStatus, CarrierAssignment.Confirmed),
            "the completed confirmed Job becomes billing eligible");

        var sla = BusinessCalendar.Calculate(new DateOnly(2026, 9, 28), 4,
            BillingSlaStartDay.Day0, new Dictionary<DateOnly, string>());
        Check(sla.StartDate == new DateOnly(2026, 9, 28)
            && sla.DueDate == new DateOnly(2026, 10, 1),
            "the snapshotted SLA uses explicit working-day configuration");

        var validations = new[]
        {
            BillingValidationRules.Document(true, true),
            BillingValidationRules.Rate(1, 1_000m, 1_000m),
            BillingValidationRules.Charge(true),
            BillingValidationRules.Tax(70m, 70m),
            BillingValidationRules.Duplicate(false, false, false),
        };
        Check(validations.All(result => !result.Blocking)
            && validations.Select(result => result.Code).SequenceEqual([
                "DOCUMENT_PRESENT", "CONTRACT_RATE_MATCH", "ADDITIONAL_CHARGE_APPROVED",
                "TAX_MATCH", "DUPLICATE_CHECK_PASSED",
            ]), "documents, rate, approved charges, tax and duplicate checks all participate");
        Check(BillingReviewTransitions.CanAct(BillingInvoiceStatus.SubconReview)
            && BillingReviewTransitions.NextStatus(BillingReviewAction.ApproveOnline)
                == BillingInvoiceStatus.AwaitingOriginal,
            "Subcontract Management approval cannot skip Original document control");

        var beforeOriginal = BillingFinanceReadiness.Evaluate(true, false, false);
        var financeReady = BillingFinanceReadiness.Evaluate(true, true, false);
        Check(!beforeOriginal.Ready && financeReady.Ready
            && financeReady.InvoiceStatus == BillingInvoiceStatus.ReadyForFinance,
            "Finance readiness requires online approval, Original receipt and no blocker");

        var canonical = new CanonicalFinanceInvoice("scmos.finance.invoice.v1", 42, 7,
            "C7", "Carrier Seven", "INV-42", "2026-09-28", "THB", [],
            1_000m, [], 70m, 1_070m, DateTimeOffset.UnixEpoch,
            DateTimeOffset.UnixEpoch, "billing-review:42:cycle:1");
        var submitted = new InternalFinanceQueueAdapter().SubmitAsync(canonical,
            "acceptance-finance-42", CancellationToken.None).GetAwaiter().GetResult();
        Check(submitted.Ok && submitted.Status == FinanceStatus.Submitted
            && submitted.ExternalReference == "SCMOS-FIN-42",
            "the configured internal Finance boundary accepts the canonical snapshot offline");
        Check(FinanceReconciliation.CanApply(FinanceStatus.Submitted, FinanceStatus.Accepted)
            && FinanceReconciliation.CanApply(FinanceStatus.Accepted, FinanceStatus.Paid)
            && FinanceReconciliation.CanApply(FinanceStatus.Paid, FinanceStatus.Closed)
            && FinanceReconciliation.InvoiceStatus(FinanceStatus.Closed) == BillingInvoiceStatus.Closed,
            "Finance response, payment and close follow the reviewed reconciliation path");
        Check(CarrierBillingVisibility.PublicStatus(BillingCaseStatus.Closed)
                == BillingCaseStatus.ReadyForFinance,
            "private Finance and payment states remain hidden from Carrier projections");

        // Known-ID isolation is tested directly, not inferred from navigation or UI filtering.
        var carrierA = new CarrierTenant(17, "Carrier A",
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Carrier A", "A" });
        Check(CarrierTenantPolicy.OwnsSupplier(carrierA, 17)
            && !CarrierTenantPolicy.OwnsSupplier(carrierA, 18)
            && CarrierAssignment.BelongsTo(17, "Carrier A", 17, carrierA.Names)
            && !CarrierAssignment.BelongsTo(18, "Carrier B", 17, carrierA.Names),
            "Carrier A cannot use Carrier B's known supplier or assignment identity");

        // The model must retain a traversable audit chain without a second Job master.
        var options = new DbContextOptionsBuilder<ScmosDbContext>()
            .UseSqlServer("Server=(localdb)\\MSSQLLocalDB;Database=scmos-final-acceptance-model;Trusted_Connection=True")
            .Options;
        using var db = new ScmosDbContext(options);
        var billingCase = db.Model.FindEntityType(typeof(BillingCase));
        var link = db.Model.FindEntityType(typeof(BillingInvoiceJobLink));
        var validation = db.Model.FindEntityType(typeof(BillingValidationResult));
        var review = db.Model.FindEntityType(typeof(BillingReviewEvent));
        var original = db.Model.FindEntityType(typeof(OriginalDocumentPackage));
        var finance = db.Model.FindEntityType(typeof(BillingFinanceRecord));
        var outbox = db.Model.FindEntityType(typeof(IntegrationOutboxEvent));

        Check(billingCase?.GetForeignKeys().Any(key => key.PrincipalEntityType.ClrType == typeof(OperationJob)) == true
            && billingCase.GetForeignKeys().Any(key => key.PrincipalEntityType.ClrType == typeof(SupplierRequest)),
            "Billing Case traces to the original SCMOS Job and Carrier assignment");
        Check(link?.GetForeignKeys().Any(key => key.PrincipalEntityType.ClrType == typeof(BillingInvoice)) == true
            && link.GetForeignKeys().Any(key => key.PrincipalEntityType.ClrType == typeof(BillingCase)),
            "Invoice-to-Job traceability is an explicit relational link");
        Check(validation?.GetForeignKeys().Any(key => key.PrincipalEntityType.ClrType == typeof(BillingValidationRun)) == true
            && review?.GetForeignKeys().Any(key => key.PrincipalEntityType.ClrType == typeof(BillingInvoice)) == true,
            "validation evidence and human review remain linked to the Invoice");
        Check(original?.GetForeignKeys().Any(key => key.PrincipalEntityType.ClrType == typeof(BillingInvoice)) == true
            && original.FindProperty(nameof(OriginalDocumentPackage.ReceivedById)) is not null,
            "Original receipt retains Invoice identity and receiving actor");
        Check(finance?.GetForeignKeys().Any(key => key.PrincipalEntityType.ClrType == typeof(BillingInvoice)) == true
            && finance.FindProperty(nameof(BillingFinanceRecord.PaymentReference)) is not null
            && finance.FindProperty(nameof(BillingFinanceRecord.ReconciledBy)) is not null,
            "Finance, payment and reconciliation remain traceable to the Invoice");
        Check(outbox?.GetIndexes().Any(index => index.IsUnique
            && index.Properties.Select(property => property.Name)
                .SequenceEqual([nameof(IntegrationOutboxEvent.IdempotencyKey)])) == true
            && FinanceRetry.Next(1, DateTimeOffset.UnixEpoch) is not null
            && FinanceRetry.Next(6, DateTimeOffset.UnixEpoch) is null,
            "the Finance outbox is idempotent, retryable and bounded");

        Console.WriteLine(failed == 0
            ? "Carrier Billing final acceptance checks passed."
            : $"Carrier Billing final acceptance checks failed: {failed}");
        return failed == 0 ? 0 : 1;
    }
}
