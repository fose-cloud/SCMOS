using Scmos.Api.Endpoints;
using Scmos.Api.Rules;
using Scmos.Api.Services;

namespace Scmos.Api.Data;

/// <summary>Deterministic Phase 9 contract and security checks; no database or secret required.</summary>
public static class CarrierBillingPhase9Check
{
    public static int? Run(string[] args)
    {
        if (!args.Contains("--check-carrier-billing-phase9")) return null;
        var failed = 0;
        void Check(bool condition, string message)
        {
            Console.WriteLine($"  {(condition ? "ok  " : "FAIL")} {message}");
            if (!condition) failed++;
        }

        var tenant = new CarrierTenant(17, "Carrier A",
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Carrier A", "A" });
        Check(CarrierTenantPolicy.OwnsSupplier(tenant, 17)
            && !CarrierTenantPolicy.OwnsSupplier(tenant, 18),
            "a resolved carrier tenant cannot select another supplier");
        Check(typeof(CarrierBillingApiEndpoints.DraftBody).GetProperties()
                .All(property => property.Name != "SupplierId"),
            "the external billing body has no supplier selector");

        var key = CarrierApi.NewKey();
        Check(CarrierApi.IsKeyShaped(key) && CarrierApi.KeyFrom($"Bearer {key}") == key,
            "Phase 9 uses the existing versioned API-key door");
        Check(CarrierApi.AllowanceOf(CarrierApi.PartitionOf($"Bearer {key}", "127.0.0.1"))
                == CarrierApi.RequestsPerMinute,
            "the existing per-key rate limit applies");
        Check(CarrierApi.IdempotencyKeyOf("phase9-create-1") == "phase9-create-1"
            && CarrierApi.IdempotencyKeyOf("bad key") is null,
            "writes require the existing printable idempotency key");
        Check(CarrierApi.RequestHash("POST", "/api/carrier/v1/billing/cases/7/draft", "")
                != CarrierApi.RequestHash("POST", "/api/carrier/v1/billing/cases/8/draft", ""),
            "idempotency fingerprints include the resource path");
        Check(CarrierApi.ProblemOf(CarrierApi.NotFound).Status == 404
            && CarrierApi.ProblemOf(CarrierApi.Conflict).Status == 409
            && CarrierApi.ProblemOf(CarrierApi.RateLimited).Status == 429,
            "external refusals remain stable RFC 9457 statuses");
        Check(BillingEligibility.IsEligible(JobStatus.Completed, CarrierAssignment.Confirmed),
            "only a completed confirmed job is billing eligible");
        Check(DocumentService.MaxBytes == 32L * 1024 * 1024,
            "POD and billing documents reuse the controlled upload limit");

        // The portal's operations through the door: no supplier selector in any body.
        foreach (var body in new[] { typeof(CarrierOperationsApiEndpoints.ResourcesBody), typeof(CarrierOperationsApiEndpoints.StatusBody),
                     typeof(CarrierBillingApiEndpoints.OriginalPackageBody), typeof(CarrierApiEndpoints.DeclineBody) })
            Check(body.GetProperties().All(property => property.Name is not ("SupplierId" or "CarrierId" or "Supplier")),
                $"{body.Name} carries no supplier selector");
        Check(typeof(CarrierApiEndpoints.DeclineBody).GetProperty("Reason") is not null,
            "a V1 decline body (reason alone) is still read");

        // Direct writes follow the department's existing trust — both hands, never one.
        Check(!CarrierApi.MayWriteDirectly(null, true) && !CarrierApi.MayWriteDirectly("on", false)
            && !CarrierApi.MayWriteDirectly("yes", true) && CarrierApi.MayWriteDirectly("on", true),
            "resources and status write directly only for a marked key with CarrierApi__AutoApply=on");

        // The portal's ladder, reached through the API, up to Delivery Complete.
        Check(CarrierOperations.Types.Contains(CarrierOperations.DeliveryComplete)
            && CarrierOperations.Decide("IMPORT", JobStatus.Delivered, CarrierOperations.DeliveryComplete).Decision == CarrierOperationDecision.Apply
            && CarrierOperations.Decide("IMPORT", JobStatus.Delivered, CarrierOperations.DeliveryComplete).TargetStatus == JobStatus.Completed,
            "delivery_complete moves a delivered job to COMPLETED — the Billing Case trigger");
        Check(CarrierOperations.Decide("IMPORT", JobStatus.InTransit, CarrierOperations.PickedUp).Decision == CarrierOperationDecision.Refuse
            && CarrierOperations.Decide("IMPORT", JobStatus.Completed, CarrierOperations.Dispatched).Decision == CarrierOperationDecision.Refuse,
            "backwards and after-close moves are refused, as in the portal");

        // Uploads: the declared type must be allowed and the bytes must agree.
        static byte[] Bytes(params int[] values) => values.Select(value => (byte)value).ToArray();
        static byte[] Text(string value) => System.Text.Encoding.ASCII.GetBytes(value);
        Check(CarrierApi.UploadProblem("application/pdf", Text("%PDF-1.7\n")) is null
            && CarrierApi.UploadProblem("image/jpeg", Bytes(0xFF, 0xD8, 0xFF, 0xE0, 0, 0x10)) is null
            && CarrierApi.UploadProblem("image/png", Bytes(0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A)) is null
            && CarrierApi.UploadProblem("image/webp", [.. Text("RIFF"), 0, 0, 0, 0, .. Text("WEBPVP8 ")]) is null
            && CarrierApi.UploadProblem("image/heic", [0, 0, 0, 0x18, .. Text("ftypheic")]) is null
            && CarrierApi.UploadProblem("image/tiff", Bytes(0x49, 0x49, 0x2A, 0x00, 0x08, 0x00)) is null
            && CarrierApi.UploadProblem("application/pdf; charset=binary", Text("%PDF-1.4")) is null,
            "PDF, JPEG, PNG, WEBP, HEIC and TIFF are read by their bytes");
        Check(CarrierApi.UploadProblem("application/pdf", Bytes(0x4D, 0x5A, 0x90, 0x00)) is not null
            && CarrierApi.UploadProblem("image/png", Text("%PDF-1.4")) is not null
            && CarrierApi.UploadProblem("text/html", "<html>"u8) is not null
            && CarrierApi.UploadProblem("image/svg+xml", "<svg"u8) is not null
            && CarrierApi.UploadProblem("", "%PDF-1.4"u8) is not null
            && CarrierApi.UploadProblem("application/pdf", ReadOnlySpan<byte>.Empty) is not null,
            "a type off the list, a missing type, or bytes that disagree are refused");
        Check(CarrierApi.DocumentKind("", "invoice") == "invoice" && CarrierApi.DocumentKind(" Cargo_Receipt ", "x") == "cargo_receipt"
            && CarrierApi.DocumentKind("../etc", "x") is null && CarrierApi.DocumentKind(new string('a', 61), "x") is null,
            "a document kind is the rules' shape, never a path");
        Check(CarrierApi.ReasonCode("") == "OTHER" && CarrierApi.ReasonCode("no_truck") == "NO_TRUCK"
            && CarrierApi.ReasonCode("bad code") is null && CarrierApi.ReasonCode(new string('A', 41)) is null,
            "a decline's reason code is read, and OTHER when absent");
        Check(CarrierApi.BillingStatuses("")!.Count == 0
            && CarrierApi.BillingStatuses("waiting_carrier_submission, draft")!.SequenceEqual([BillingCaseStatus.WaitingCarrierSubmission, BillingCaseStatus.Draft])
            && CarrierApi.BillingStatuses("PAID") is null
            && CarrierApi.BillingCaseStatuses.Length == typeof(BillingCaseStatus).GetFields().Length,
            "billing lists filter on the case statuses there are, and every one of them");

        Console.WriteLine(failed == 0 ? "Carrier Billing Phase 9 checks passed."
            : $"Carrier Billing Phase 9 checks failed: {failed}");
        return failed == 0 ? 0 : 1;
    }
}
