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
    JobRecord? Record,
    /// <summary>When the row was last written — the column, which the JSON does not carry.</summary>
    DateTimeOffset UpdatedAt = default);

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
///
/// <para>
/// Since 22 Sep 2026 a reader may say it can take a stale answer
/// (<c>staleOk</c>): when a write has just dropped the cache, such a reader is
/// given the last snapshot built — never older than <see cref="MaxStale"/> —
/// at once, and the fresh read runs behind it, once, for everyone. That
/// morning every dashboard the department opened after 09:00 waited on the
/// same whole-register read, one after another, because each write had
/// dropped the one before. The readers that opt in are the ones that
/// summarise the register — the dashboard's cards, the KPI, the
/// notifications, the monitor, the reports — and the web app's opening load,
/// which corrects itself from <c>/api/jobs/changed</c> a few times a minute
/// anyway. The workspace's own page, and anything that writes from what it
/// read, still reads fresh.
/// </para>
/// </summary>
public sealed class JobRegisterCache(ScmosDbContext db, IMemoryCache cache,
    ILogger<JobRegisterCache> log, IServiceScopeFactory? scopes = null)
{
    private const string CacheKey = "operation-register-snapshot-v1";
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(5);
    /// <summary>The oldest snapshot a reader that can take a stale one is given; past this it waits for a fresh read like everyone else.</summary>
    public static readonly TimeSpan MaxStale = TimeSpan.FromMinutes(3);
    private static JobRegisterSnapshot? _last;
    private static DateTimeOffset _lastBuiltAt;
    private static int _refreshing;
    private static int _again;

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

    public Task<JobRegisterSnapshot> ReadAsync(CancellationToken token) => ReadAsync(token, staleOk: false);

    /// <param name="staleOk">
    /// Whether the last snapshot built — at most <see cref="MaxStale"/> old — will do while a fresh one is read behind it.
    /// A reader that summarises the register may say yes; one that writes from what it read, or shows a row somebody
    /// just edited, must not.
    /// </param>
    public async Task<JobRegisterSnapshot> ReadAsync(CancellationToken token, bool staleOk)
    {
        if (cache.TryGetValue(CacheKey, out JobRegisterSnapshot? found) && found is not null)
            return found;

        if (staleOk && Stale() is { } stale)
        {
            // Answer now with what was last read; read the register once, in the background, for the next reader.
            ScheduleRefresh();
            return stale;
        }

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
                // MaxSaveBatch limits one request, not the register. Capping this
                // query hid every row after 5,000 from Workspace and duplicate
                // detection while Staff still counted those rows directly in SQL.
                // A successful import must therefore remain visible here in full.
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
                valid.Add(new CachedJobRow(row.Key, row.Trucker, raw, JobRecord.From(raw), row.UpdatedAt));
                if (row.UpdatedAt > updatedAt) updatedAt = row.UpdatedAt;
            }

            json.Append("],\"count\":").Append(valid.Count).Append(",\"updatedAt\":");
            json.Append(JsonSerializer.Serialize(valid.Count == 0
                ? ""
                : updatedAt.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'")));
            json.Append('}');

            var snapshot = new JobRegisterSnapshot(valid, json.ToString(), valid.Count, updatedAt);
            // What the stale readers are given after the next write, dated by when it was built.
            _last = snapshot;
            _lastBuiltAt = DateTimeOffset.UtcNow;

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

    /// <summary>
    /// The snapshot as cached, or null — without reading. The SRE Agent
    /// (Phase 7) reports whether the next reader will pay the whole-register
    /// read; asking this method to load it would be that read.
    /// </summary>
    public JobRegisterSnapshot? Peek() => cache.TryGetValue(CacheKey, out JobRegisterSnapshot? found) ? found : null;

    /// <summary>The last snapshot built, when it is still young enough to hand a stale reader; null otherwise.</summary>
    public static JobRegisterSnapshot? Stale() =>
        _last is { } last && DateTimeOffset.UtcNow - _lastBuiltAt <= MaxStale ? last : null;

    /// <summary>How old the last snapshot built is, for the SRE Agent's health view; null when none was built since the process started.</summary>
    public static TimeSpan? StaleAge() => _last is null ? null : DateTimeOffset.UtcNow - _lastBuiltAt;

    /// <summary>Whether a background read is running now.</summary>
    public static bool Refreshing => Volatile.Read(ref _refreshing) == 1;

    /// <summary>
    /// One background read of the register, for everyone: a second request
    /// while it runs marks it to run once more, not once per request. A host
    /// without a scope factory (a rule check) reads on demand and never here.
    /// </summary>
    private void ScheduleRefresh()
    {
        if (scopes is null) return;
        if (Interlocked.CompareExchange(ref _refreshing, 1, 0) != 0) { Volatile.Write(ref _again, 1); return; }
        var factory = scopes;
        var logger = log;
        _ = Task.Run(async () =>
        {
            try
            {
                do
                {
                    Volatile.Write(ref _again, 0);
                    try
                    {
                        // A scope of its own: the request that asked has gone by the time this finishes.
                        using var scope = factory.CreateScope();
                        var register = scope.ServiceProvider.GetRequiredService<JobRegisterCache>();
                        await register.ReadAsync(CancellationToken.None, staleOk: false);
                    }
                    catch (Exception error)
                    {
                        logger.LogWarning(error, "The background register read failed; the next reader reads it in the foreground.");
                        break;
                    }
                } while (Volatile.Read(ref _again) == 1);
            }
            finally { Volatile.Write(ref _refreshing, 0); }
        });
    }
}
