using Microsoft.EntityFrameworkCore;
using Scmos.Api.Rules;

namespace Scmos.Api.Data;

public sealed record AiOwnerOption(string Id, string Name, string Role);
public interface IAiOwnerDirectory
{
    Task<string?> ValidateAsync(string? ownerId, string? fallbackId, CancellationToken token);
}

/// <summary>Real active staff identities, not role labels or model-invented names. No approval privilege is granted.</summary>
public sealed class AiOwnerDirectory(ScmosDbContext db) : IAiOwnerDirectory
{
    public static bool Eligible(string? role) => Roles.Find(role) is not null
        && Roles.Can(role, Capability.ApproveAi | Capability.EditAnyJob | Capability.AssignJobs);

    public async Task<IReadOnlyList<AiOwnerOption>> OptionsAsync(CancellationToken token)
    {
        var rows = await db.Staff.AsNoTracking().Where(s => s.Active && s.Email.Trim() != "").OrderBy(s => s.Name)
            .Select(s => new AiOwnerOption(s.Id, s.Name, s.Role)).ToListAsync(token);
        return rows.Where(s => Eligible(s.Role)).ToArray();
    }
    public async Task<string?> ValidateAsync(string? ownerId, string? fallbackId, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(ownerId) || string.IsNullOrWhiteSpace(fallbackId)) return "owner_required";
        if (ownerId == fallbackId) return "independent_fallback_required";
        // Explicit email references let a human nominate an existing account without guessing its Staff.Id.
        // Exact address matching only: no display-name, local-part, guest-claim or role fallback.
        var ownerEmail = EmailReference(ownerId);
        var fallbackEmail = EmailReference(fallbackId);
        var rows = await db.Staff.AsNoTracking().Where(s =>
                (ownerEmail == null && s.Id == ownerId) || (fallbackEmail == null && s.Id == fallbackId)
                || (ownerEmail != null && s.Email.Trim().ToLower() == ownerEmail)
                || (fallbackEmail != null && s.Email.Trim().ToLower() == fallbackEmail))
            .Select(s => new { s.Id, s.Active, s.Role, s.Email }).Take(3).ToListAsync(token);
        var primary = rows.Where(s => ownerEmail is null ? s.Id == ownerId
            : string.Equals(s.Email.Trim(), ownerEmail, StringComparison.OrdinalIgnoreCase)).ToArray();
        var fallback = rows.Where(s => fallbackEmail is null ? s.Id == fallbackId
            : string.Equals(s.Email.Trim(), fallbackEmail, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (rows.Count > 2 || primary.Length != 1 || fallback.Length != 1
            || rows.Any(s => !s.Active || !Eligible(s.Role) || string.IsNullOrWhiteSpace(s.Email)))
            return "owner_not_eligible";
        if (primary[0].Id == fallback[0].Id) return "independent_fallback_required";
        return rows.Select(s => s.Email.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Count() == 2
            ? null : "independent_fallback_required";
    }
    private static string? EmailReference(string reference) => reference.StartsWith("email:", StringComparison.Ordinal)
        ? reference[6..].Trim().ToLowerInvariant() : null;
}
