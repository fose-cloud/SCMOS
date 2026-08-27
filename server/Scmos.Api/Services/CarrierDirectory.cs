using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Scmos.Api.Data;

namespace Scmos.Api.Services;

/// <summary>
/// The company a spelling on a job means.
///
/// A job says "SJ", or "TTP", or "TATIYAPOL". The subcontractor register says
/// which company each of those is. Everything that groups or filters jobs by
/// haulier has to ask the same question, and until now each place answered it
/// for itself by upper-casing the raw text — so the scorecard, the carrier
/// load and the workspace filter each counted one company as two or three, and
/// each disagreed with the others about how many.
///
/// One reading, used everywhere. A spelling the register has never seen is
/// returned as itself rather than dropped or lumped in with the others: an
/// unregistered haulier is a gap to be seen, not a row to be hidden.
/// </summary>
public class CarrierDirectory(ScmosDbContext db, IMemoryCache cache)
{
    /// <summary>
    /// How long a reading stands.
    ///
    /// The same five minutes the job register uses, and for the same reason:
    /// these two are read together on every KPI build, and a directory that
    /// expired on a different clock would mean the pair of them disagreed for
    /// whatever gap was between.
    /// </summary>
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(5);

    private const string Key = "carrier-directory-v1";

    /// <param name="Stamp">
    /// Moves whenever the register does. Belongs in the cache key of anything
    /// computed from this — merging two hauliers changes every figure grouped
    /// by haulier, and a report cached against the jobs alone would go on
    /// showing them as two companies until a job happened to change.
    /// </param>
    public sealed record Lookup(
        IReadOnlyDictionary<string, string> BySpelling,
        IReadOnlyList<string> Companies,
        string Stamp)
    {
        /// <summary>Letters and digits only, upper case: "A.C.N" and "A C N" are one key.</summary>
        public static string Key(string value) =>
            new((value ?? "").ToUpperInvariant().Where(char.IsLetterOrDigit).ToArray());

        /// <summary>
        /// The company this spelling means, or the spelling itself when the
        /// register has never seen it.
        /// </summary>
        public string Company(string spelling)
        {
            var text = (spelling ?? "").Trim();
            if (text.Length == 0) return "";
            return BySpelling.TryGetValue(Key(text), out var name) ? name : text;
        }

        /// <summary>Whether the register can say which company this is.</summary>
        public bool Knows(string spelling) =>
            BySpelling.ContainsKey(Key(spelling ?? ""));

        /// <summary>
        /// Whether a job spelled one way should be shown under a filter set to
        /// another. Both sides go through the register, so choosing "Sangja
        /// Transport Co., Ltd." finds the jobs written SJ and SANGJA too.
        /// </summary>
        public bool Same(string spelling, string wanted) =>
            string.Equals(Company(spelling), Company(wanted), StringComparison.OrdinalIgnoreCase);
    }

    public async Task<Lookup> ReadAsync(CancellationToken token)
    {
        if (cache.TryGetValue(Key, out Lookup? ready) && ready is not null) return ready;

        var suppliers = await db.Suppliers.AsNoTracking()
            .Select(row => new { row.Id, row.Name, row.UpdatedAt })
            .ToListAsync(token);
        var aliases = await db.SupplierAliases.AsNoTracking()
            .Select(row => new { row.SupplierId, row.Alias })
            .ToListAsync(token);

        var nameOf = suppliers.ToDictionary(row => row.Id, row => row.Name);

        var bySpelling = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var row in suppliers) bySpelling[Lookup.Key(row.Name)] = row.Name;
        foreach (var alias in aliases)
            if (nameOf.TryGetValue(alias.SupplierId, out var name))
                bySpelling[Lookup.Key(alias.Alias)] = name;

        // Enough to move whenever a merge, a rename or an import does. A merge
        // touches the surviving row's UpdatedAt and removes aliases, so both
        // halves of this change.
        var newest = suppliers.Count == 0
            ? DateTimeOffset.MinValue
            : suppliers.Max(row => row.UpdatedAt);
        var stamp = $"{suppliers.Count}.{aliases.Count}.{newest.UtcTicks}";

        var lookup = new Lookup(
            bySpelling,
            suppliers.Select(row => row.Name).OrderBy(name => name, StringComparer.Ordinal).ToList(),
            stamp);

        cache.Set(Key, lookup, Lifetime);
        return lookup;
    }
}
