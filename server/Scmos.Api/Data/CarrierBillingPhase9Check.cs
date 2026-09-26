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

        Console.WriteLine(failed == 0 ? "Carrier Billing Phase 9 checks passed."
            : $"Carrier Billing Phase 9 checks failed: {failed}");
        return failed == 0 ? 0 : 1;
    }
}
