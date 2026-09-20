using Microsoft.EntityFrameworkCore;
using Scmos.Api.Auth;
using Scmos.Api.Data;
using Scmos.Api.Rules;

namespace Scmos.Api.Services;

/// <summary>
/// Who a Carrier API call is from: the key in its Authorization header,
/// looked up by hash, bound to one supplier. Null is the answer for every
/// request that is not a live key — no header, a malformed one, a hash the
/// register does not hold, a revoked row, a supplier that is gone — and the
/// caller is told only "no valid key", never which of those it was.
///
/// <para>
/// The supplier comes from the key's row. Nothing in the request — no
/// header, no query, no body — is read to decide whose work is being asked
/// for; that is the one rule the specification calls non-negotiable, and it
/// is the same rule <see cref="CarrierService.CompanyOfAsync"/> applies to a
/// person's account.
/// </para>
/// </summary>
public class CarrierApiAuth(ScmosDbContext db, ILogger<CarrierApiAuth> log)
{
    /// <summary>A key that was found: the credential's public name and the supplier it speaks for.</summary>
    /// <param name="AutoApplyMarked">Whether the department marked this key for auto-apply; effective only with the setting on — see <see cref="CarrierApi.AutoApplies"/>.</param>
    public record Principal(long ClientRowId, string ClientId, string ClientName, Supplier Company, bool AutoApplyMarked = false)
    {
        /// <summary>
        /// The credential as the register and the audit trail name it:
        /// <c>updated_by</c> and <c>who</c> read "carrier-api:ck_…", the role is
        /// the carrier's, and the source is its own — so "who set this plate"
        /// answers with the carrier's system, not with a person or with LINE.
        /// </summary>
        public AppUser AsUser() => new(
            UserId: $"carrier-api:{ClientId}", Email: "", DisplayName: $"{Company.Name} · {ClientName}",
            Role: Roles.Subcontractor, OperatorId: "", Source: "carrier-api", Recognised: true);
    }

    /// <summary>Where the principal is kept for the rest of the request, once the filter has resolved it.</summary>
    public const string ItemKey = "scmos.carrier-api.principal";

    /// <summary>How stale <c>last_seen_at</c> may be before a call refreshes it — one write every few minutes, not one per call.</summary>
    private static readonly TimeSpan SeenGrain = TimeSpan.FromMinutes(5);

    public async Task<Principal?> ResolveAsync(string? authorization, CancellationToken token)
    {
        var key = CarrierApi.KeyFrom(authorization);
        if (key is null) return null;
        var hash = CarrierApi.HashOf(key);

        var row = await db.CarrierApiClients.FirstOrDefaultAsync(one => one.KeyHash == hash, token);
        if (row is null || row.Status != CarrierApiClientStatus.Active || row.RevokedAt is not null)
        {
            // The client id is logged when there is one — a revoked key still
            // being presented is worth a line — the key never is.
            if (row is not null) log.LogWarning("Carrier API: revoked key presented for client {Client}", row.ClientId);
            return null;
        }

        var company = await db.Suppliers.AsNoTracking().FirstOrDefaultAsync(one => one.Id == row.SupplierId, token);
        if (company is null)
        {
            log.LogWarning("Carrier API: client {Client} names supplier {Supplier}, which does not exist", row.ClientId, row.SupplierId);
            return null;
        }

        var now = DateTimeOffset.UtcNow;
        if (row.LastSeenAt is null || now - row.LastSeenAt.Value > SeenGrain)
        {
            row.LastSeenAt = now;
            try { await db.SaveChangesAsync(token); }
            catch (DbUpdateException) { /* a stamp lost to a race is not worth failing the call */ }
        }
        return new Principal(row.Id, row.ClientId, row.Name, company, row.AutoApply);
    }
}
