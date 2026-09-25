using Microsoft.EntityFrameworkCore;
using Scmos.Api.Auth;
using Scmos.Api.Data;
using Scmos.Api.Rules;

namespace Scmos.Api.Services;

public record CarrierTenant(int SupplierId, string SupplierName, IReadOnlySet<string> Names);

public static class CarrierTenantPolicy
{
    public static bool OwnsSupplier(CarrierTenant? tenant, int supplierId) =>
        tenant is not null && tenant.SupplierId == supplierId;

    public static bool OwnsJob(CarrierTenant? tenant, string? carrier) =>
        tenant is not null && !string.IsNullOrWhiteSpace(carrier)
        && tenant.Names.Contains(carrier.Trim());
}

/// <summary>
/// Resolves the carrier boundary from server-owned identity data. A caller can
/// never choose its supplier id in a request. Human accounts use Staff.SupplierId;
/// machine clients continue to use CarrierApiClient.SupplierId.
/// </summary>
public class CarrierTenantContext(ScmosDbContext db, ILogger<CarrierTenantContext> log)
{
    public static bool IsCarrier(AppUser user) =>
        string.Equals(user.Role, Roles.Subcontractor, StringComparison.OrdinalIgnoreCase);

    public async Task<CarrierTenant?> ResolveAsync(AppUser user, CancellationToken token)
    {
        if (!IsCarrier(user)) return null;

        var person = await db.Staff.AsNoTracking()
            .FirstOrDefaultAsync(row => row.Id == user.OperatorId && row.Active, token);
        if (person?.SupplierId is not { } supplierId)
        {
            log.LogWarning("Carrier account {OperatorId} has no active supplier membership.", user.OperatorId);
            return null;
        }

        var supplier = await db.Suppliers.AsNoTracking()
            .FirstOrDefaultAsync(row => row.Id == supplierId && row.IsCarrier, token);
        if (supplier is null)
        {
            log.LogWarning("Carrier account {OperatorId} points to unavailable supplier {SupplierId}.",
                user.OperatorId, supplierId);
            return null;
        }

        var names = await db.SupplierAliases.AsNoTracking()
            .Where(row => row.SupplierId == supplierId)
            .Select(row => row.Alias)
            .ToListAsync(token);
        names.Add(supplier.Name);
        names.Add(supplier.Code);

        return new CarrierTenant(supplier.Id, supplier.Name,
            names.Where(name => !string.IsNullOrWhiteSpace(name))
                .Select(name => name.Trim())
                .ToHashSet(StringComparer.OrdinalIgnoreCase));
    }
}

/// <summary>
/// One authorization policy for every generic document route. Internal users
/// retain the existing capability checks; a carrier is additionally confined
/// to its supplier record, its drivers and jobs whose current carrier is one of
/// its registered names.
/// </summary>
public class CarrierDocumentAccess(ScmosDbContext db, CarrierTenantContext tenants, DocumentService documents)
{
    public async Task<IReadOnlyList<DocumentView>> ListAsync(AppUser user, string? jobKey,
        int? supplierId, long? caseId, long? issueId, string? folder, CancellationToken token)
    {
        if (!CarrierTenantContext.IsCarrier(user))
            return await documents.ListAsync(jobKey, supplierId, caseId, issueId, folder, token);

        var tenant = await tenants.ResolveAsync(user, token);
        return tenant is null
            ? []
            : await documents.ListForCarrierAsync(tenant, jobKey, supplierId, caseId, issueId, folder, token);
    }

    public async Task<bool> CanUseSupplierAsync(AppUser user, int supplierId, CancellationToken token)
    {
        if (!CarrierTenantContext.IsCarrier(user)) return true;
        var tenant = await tenants.ResolveAsync(user, token);
        return CarrierTenantPolicy.OwnsSupplier(tenant, supplierId);
    }

    public async Task<bool> CanUseJobAsync(AppUser user, string jobKey, CancellationToken token)
    {
        if (!CarrierTenantContext.IsCarrier(user)) return true;
        var tenant = await tenants.ResolveAsync(user, token);
        if (tenant is null || string.IsNullOrWhiteSpace(jobKey)) return false;

        var assignments = await db.SupplierRequests.AsNoTracking()
            .Where(row => row.JobKey == jobKey).ToListAsync(token);
        if (assignments.Count > 0)
            return assignments.Any(row => row.Outcome == CarrierAssignment.Confirmed
                && CarrierAssignment.BelongsTo(row.SupplierId, row.Carrier,
                    tenant.SupplierId, tenant.Names));

        // Alias fallback is only for historical jobs created before assignment
        // rows existed. Once history exists, the stable SupplierId decides.
        var carrier = await db.OperationJobs.AsNoTracking()
            .Where(row => row.Key == jobKey)
            .Select(row => row.Trucker)
            .FirstOrDefaultAsync(token);
        return CarrierTenantPolicy.OwnsJob(tenant, carrier);
    }

    public async Task<bool> CanUseCaseAsync(AppUser user, long caseId, CancellationToken token)
    {
        if (!CarrierTenantContext.IsCarrier(user)) return true;
        var jobKey = await db.IncidentCases.AsNoTracking()
            .Where(row => row.Id == caseId).Select(row => row.JobKey).FirstOrDefaultAsync(token);
        return jobKey is not null && await CanUseJobAsync(user, jobKey, token);
    }

    public async Task<bool> CanUseIssueAsync(AppUser user, long issueId, CancellationToken token)
    {
        if (!CarrierTenantContext.IsCarrier(user)) return true;
        var jobKey = await db.OperationalIssues.AsNoTracking()
            .Where(row => row.Id == issueId).Select(row => row.JobKey).FirstOrDefaultAsync(token);
        return jobKey is not null && await CanUseJobAsync(user, jobKey, token);
    }

    public async Task<bool> CanReadAsync(AppUser user, StoredDocument document, CancellationToken token)
    {
        if (!CarrierTenantContext.IsCarrier(user)) return true;
        if (document.SupplierId is { } supplierId)
            return await CanUseSupplierAsync(user, supplierId, token);
        if (!string.IsNullOrWhiteSpace(document.JobKey))
            return await CanUseJobAsync(user, document.JobKey, token);
        if (document.DriverId is { } driverId)
        {
            var tenant = await tenants.ResolveAsync(user, token);
            if (tenant is null) return false;
            return await db.Drivers.AsNoTracking()
                .AnyAsync(row => row.Id == driverId && row.SupplierId == tenant.SupplierId, token);
        }
        return false;
    }

}
