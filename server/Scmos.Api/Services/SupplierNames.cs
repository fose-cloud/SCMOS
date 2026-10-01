using Microsoft.EntityFrameworkCore;
using Scmos.Api.Data;
using Scmos.Api.Rules;

namespace Scmos.Api.Services;

/// <summary>
/// Which subcontractor a written company name means (30 Sep 2026): by its name, code, legal name or any alias,
/// each reduced to <see cref="SupplierRegister.Key"/> — so "Wealthy Logistic Co., Ltd." finds the supplier whose
/// legal name it is, and "WEALTHY" the one registered under it.
///
/// <para>
/// The company pickers on screen offer the approved list (<c>carriers.ts</c>, as My job's carrier column does);
/// a name the API is sent is held to the same list here. A key two suppliers share means neither, rather than
/// whichever came first.
/// </para>
/// </summary>
public class SupplierNames(ScmosDbContext db)
{
    public sealed record Match(int Id, string Name);

    /// <summary>A lookup over the approved suppliers, read once for a request that resolves many names.</summary>
    public Task<Func<string?, Match?>> ApprovedAsync(CancellationToken token) => LookupAsync(true, token);

    /// <summary>
    /// The same over every supplier, approved or not (1 Oct 2026) — the audit plan names companies still
    /// being onboarded, whose audit is what approves them.
    /// </summary>
    public Task<Func<string?, Match?>> AnyAsync(CancellationToken token) => LookupAsync(false, token);

    private async Task<Func<string?, Match?>> LookupAsync(bool approvedOnly, CancellationToken token)
    {
        var suppliers = await db.Suppliers.AsNoTracking().Where(row => !approvedOnly || row.Status == "approved")
            .Select(row => new { row.Id, row.Name, row.Code, row.LegalName }).ToListAsync(token);
        var byId = suppliers.ToDictionary(row => row.Id);
        var ids = byId.Keys.ToList();
        var aliases = await db.SupplierAliases.AsNoTracking().Where(row => ids.Contains(row.SupplierId))
            .Select(row => new { row.SupplierId, row.Alias }).ToListAsync(token);

        var byKey = new Dictionary<string, Match?>(StringComparer.Ordinal);
        void Add(string text, Match match)
        {
            var key = SupplierRegister.Key(text);
            if (key.Length == 0) return;
            byKey[key] = byKey.TryGetValue(key, out var held) && held?.Id != match.Id ? null : match;
        }
        foreach (var row in suppliers)
        {
            var match = new Match(row.Id, row.Name);
            Add(row.Name, match); Add(row.Code, match); Add(row.LegalName, match);
        }
        foreach (var alias in aliases)
            Add(alias.Alias, new Match(alias.SupplierId, byId[alias.SupplierId].Name));

        return text => byKey.GetValueOrDefault(SupplierRegister.Key(text ?? ""));
    }

    /// <summary>Every key one supplier is written under — for its rows written before a column held its id.</summary>
    public async Task<HashSet<string>> KeysOfAsync(Supplier company, CancellationToken token)
    {
        var aliases = await db.SupplierAliases.AsNoTracking().Where(row => row.SupplierId == company.Id)
            .Select(row => row.Alias).ToListAsync(token);
        return new[] { company.Name, company.Code, company.LegalName }.Concat(aliases)
            .Select(SupplierRegister.Key).Where(key => key.Length > 0).ToHashSet(StringComparer.Ordinal);
    }
}
