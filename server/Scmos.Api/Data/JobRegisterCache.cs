using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Scmos.Api.Rules;

namespace Scmos.Api.Data;

/// <summary>One validated register row, in both its complete and rule-friendly forms.</summary>
public sealed record CachedJobRow(
    string Key,
    string Trucker,
    JsonElement Raw,
    JobRecord? Record);

/// <summary>A coherent read of the operation register.</summary>
public sealed record JobRegisterSnapshot(
    IReadOnlyList<CachedJobRow> Rows,
    string Json,
    int Count,
    DateTimeOffset UpdatedAt);

/// <summary>
/// Coalesces the expensive register read shared by the first-page services.
///
/// Dashboard, KPI, notifications, workspace paging and the full-register route
/// used to query and parse the same 2.6 MB JSON register independently. Several
/// of those requests start together when the web app opens, turning one read
/// into a burst of full-table reads. The cache is deliberately short lived and
/// every application write invalidates it, so it removes duplicate work without
/// becoming another source of operational truth.
/// </summary>
public sealed class JobRegisterCache(ScmosDbContext db, IMemoryCache cache,
    ILogger<JobRegisterCache> log)
{
    private const string CacheKey = "operation-register-snapshot-v1";
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Where a full-register read stops being cheap on this instance.
    ///
    /// Not a limit — nothing is dropped for crossing it. It is the point at
    /// which holding the whole register in memory is worth a line in the log,
    /// measured against the B1 the API runs on: about 760 bytes a job stored,
    /// so ten thousand jobs is roughly 8 MB of JSON plus the parsed copy beside
    /// it, twice over because callers want both forms.
    /// </summary>
    private const int LargeRegister = 10_000;
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static long _version;

    public async Task<JobRegisterSnapshot> ReadAsync(CancellationToken token)
    {
        if (cache.TryGetValue(CacheKey, out JobRegisterSnapshot? found) && found is not null)
            return found;

        await Gate.WaitAsync(token);
        try
        {
            if (cache.TryGetValue(CacheKey, out found) && found is not null)
                return found;

            var version = Volatile.Read(ref _version);
            var rows = await db.OperationJobs
                .AsNoTracking()
                .OrderBy(job => job.WorkDate == "" ? 1 : 0)
                .ThenBy(job => job.WorkDate)
                .ThenBy(job => job.Key)
                .Select(job => new { job.Key, job.Trucker, job.Data, job.UpdatedAt })
                .ToListAsync(token);

            // This read was capped at 5,000 rows, and it truncated in silence:
            // every screen fed from here — the dashboards, the KPI figures, the
            // export, the duplicate check — simply stopped counting past the
            // ceiling, and each of them still looked plausible. A total that is
            // really a floor is the one failure this codebase least wants,
            // because nothing about it looks wrong.
            //
            // So the cap is gone and the read is the whole register. What the cap
            // was protecting against is real, though: this parses every row into
            // memory and holds it for five minutes, on an instance with 1.75 GB
            // shared between two apps. The answer to that is for the remaining
            // callers to stop asking for the whole register — the paging endpoint
            // and ChangedAsync are what that looks like — and this warning is
            // what says the day has come, instead of a truncation nobody sees.
            if (rows.Count >= LargeRegister)
            {
                log.LogWarning(
                    "The register read returned {Rows} rows, past the {Threshold} this instance "
                    + "was sized for. It is complete and correct, but it is parsed in full and "
                    + "held for {Minutes} minutes. Callers that want a slice should use the "
                    + "paging endpoint rather than this.",
                    rows.Count, LargeRegister, Lifetime.TotalMinutes);
            }

            var valid = new List<CachedJobRow>(rows.Count);
            var json = new StringBuilder(rows.Count * 900 + 80);
            json.Append("{\"jobs\":[");

            DateTimeOffset updatedAt = default;
            foreach (var row in rows)
            {
                JsonElement raw;
                try
                {
                    using var document = JsonDocument.Parse(row.Data);
                    raw = document.RootElement.Clone();
                }
                catch (JsonException)
                {
                    // A malformed row must not prevent the rest of the register
                    // from opening. This is the same behaviour as the old loader.
                    continue;
                }

                if (valid.Count > 0) json.Append(',');
                json.Append(row.Data);
                valid.Add(new CachedJobRow(row.Key, row.Trucker, raw, JobRecord.From(raw)));
                if (row.UpdatedAt > updatedAt) updatedAt = row.UpdatedAt;
            }

            json.Append("],\"count\":").Append(valid.Count).Append(",\"updatedAt\":");
            json.Append(JsonSerializer.Serialize(valid.Count == 0
                ? ""
                : updatedAt.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'")));
            json.Append('}');

            var snapshot = new JobRegisterSnapshot(valid, json.ToString(), valid.Count, updatedAt);

            // If a write completed while the query was running, this answer is
            // valid for the request that started before it but must not be kept.
            if (version == Volatile.Read(ref _version))
            {
                cache.Set(CacheKey, snapshot, new MemoryCacheEntryOptions
                {
                    // All application write paths invalidate immediately. The
                    // bounded lifetime covers direct DBA changes while keeping
                    // navigation fast after a 20+ second cold register parse.
                    AbsoluteExpirationRelativeToNow = Lifetime,
                });
            }

            return snapshot;
        }
        finally
        {
            Gate.Release();
        }
    }

    public void Invalidate()
    {
        Interlocked.Increment(ref _version);
        cache.Remove(CacheKey);
    }
}
