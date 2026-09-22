using System.Diagnostics;

namespace Scmos.Api.Data;

/// <summary>
/// The register cache's stale-while-revalidate, with <c>--check-register-cache</c>,
/// on a development database (LocalDB): a reader that can take a stale
/// snapshot is answered at once after a write, the fresh read runs behind it
/// once, and a strict reader still waits for the fresh one. Reads only; the
/// only write is <see cref="JobRegisterCache.Invalidate"/>, which drops a
/// cache entry and nothing in the database. Not in the CI loop — it needs a
/// register to read.
/// </summary>
public static class RegisterCacheCheck
{
    public static async Task<int> RunAsync(WebApplication app)
    {
        var failed = 0;
        Console.WriteLine("The register cache: a stale answer at once, a fresh one behind it.");
        Console.WriteLine();
        using var scope = app.Services.CreateScope();
        var register = scope.ServiceProvider.GetRequiredService<JobRegisterCache>();

        var watch = Stopwatch.StartNew();
        var first = await register.ReadAsync(default);
        var cold = watch.ElapsedMilliseconds;
        Console.WriteLine($"cold read: {first.Count} rows in {cold} ms");
        failed += Say("a fresh snapshot is cached after the read", register.Peek() is not null, true);
        failed += Say("and kept as the last one built", JobRegisterCache.Stale()?.Count == first.Count, true);

        register.Invalidate();
        failed += Say("a write drops the cached snapshot", register.Peek() is null, true);
        failed += Say("but not the last one built", JobRegisterCache.Stale() is not null, true);

        watch.Restart();
        var stale = await register.ReadAsync(default, staleOk: true);
        var quick = watch.ElapsedMilliseconds;
        Console.WriteLine($"stale-ok read after the write: {quick} ms");
        failed += Say("a reader that can take a stale answer is answered at once", quick < Math.Max(500, cold / 4), true);
        failed += Say("with the snapshot built before the write", ReferenceEquals(stale, first), true);
        failed += Say("and a fresh read starts behind it", JobRegisterCache.Refreshing || register.Peek() is not null, true);

        var waited = Stopwatch.StartNew();
        while (JobRegisterCache.Refreshing && waited.Elapsed < TimeSpan.FromSeconds(120)) await Task.Delay(100);
        Console.WriteLine($"background read finished in {waited.ElapsedMilliseconds} ms");
        failed += Say("the background read finishes and is cached", register.Peek() is not null, true);
        var fresh = await register.ReadAsync(default, staleOk: true);
        failed += Say("the next stale-ok reader gets the fresh snapshot", !ReferenceEquals(fresh, first) && fresh.Count == first.Count, true);

        register.Invalidate();
        watch.Restart();
        var strict = await register.ReadAsync(default);
        Console.WriteLine($"strict read after a write: {watch.ElapsedMilliseconds} ms");
        failed += Say("a strict reader waits for a fresh snapshot", !ReferenceEquals(strict, fresh) && register.Peek() is not null, true);

        failed += Say("a stale answer is never older than three minutes", JobRegisterCache.MaxStale, TimeSpan.FromMinutes(3));
        var age = JobRegisterCache.StaleAge();
        failed += Say("the SRE view can say how old the last snapshot is", age is not null && age < TimeSpan.FromMinutes(1), true);

        Console.WriteLine();
        Console.WriteLine(failed == 0 ? "The register cache answers at once and reads once." : $"{failed} check(s) failed.");
        return failed == 0 ? 0 : 1;
    }

    private static int Say<T>(string why, T got, T want)
    {
        var ok = EqualityComparer<T>.Default.Equals(got, want);
        Console.WriteLine($"{(ok ? "ok  " : "FAIL")}  {why,-62} {(ok ? "" : $"got {got}  want {want}")}");
        return ok ? 0 : 1;
    }
}
