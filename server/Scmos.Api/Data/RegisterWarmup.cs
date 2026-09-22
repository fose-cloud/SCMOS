using System.Diagnostics;

namespace Scmos.Api.Data;

/// <summary>
/// Builds the register snapshot once when the process starts, so the first
/// person through the door after a deploy does not pay for it.
///
/// <para>
/// Always On keeps App Service from unloading an idle site; it does nothing
/// about this. Every deploy restarts the process, the cache is empty, and
/// the first authenticated read of the day rebuilds a 2.6 MB snapshot —
/// measured at 75–85 seconds on 22 September 2026, on a warm S2 database,
/// four times in one afternoon. That cost is real and somebody was always
/// paying it; this makes the process pay it instead, five seconds after it
/// starts answering, while nobody is waiting.
/// </para>
///
/// <para>
/// One read, once, and then silence: the cache's own lifetime and
/// stale-while-revalidate (v2.7.77) carry it from there. A failure is a
/// warning and nothing else — the next reader builds the snapshot exactly
/// as it did before this file existed.
/// </para>
/// </summary>
public class RegisterWarmup(IServiceProvider services, IConfiguration configuration, ILogger<RegisterWarmup> log) : BackgroundService
{
    /// <summary>Set to false to start cold — the first reader then builds the snapshot, as before.</summary>
    public const string EnabledKey = "Register:WarmAtStartup";

    /// <summary>Long enough for the app to be answering health checks and short enough to be ready before people arrive.</summary>
    private static readonly TimeSpan After = TimeSpan.FromSeconds(5);

    protected override async Task ExecuteAsync(CancellationToken stopping)
    {
        if (!configuration.GetValue(EnabledKey, true)) return;
        try { await Task.Delay(After, stopping); }
        catch (OperationCanceledException) { return; }

        var watch = Stopwatch.StartNew();
        try
        {
            using var scope = services.CreateScope();
            var register = scope.ServiceProvider.GetRequiredService<JobRegisterCache>();
            var snapshot = await register.ReadAsync(stopping);
            log.LogInformation("Register warmed at startup: {Rows} rows in {ElapsedMs} ms — the first reader will not build it",
                snapshot.Count, watch.ElapsedMilliseconds);
        }
        catch (OperationCanceledException) when (stopping.IsCancellationRequested) { }
        catch (Exception problem)
        {
            log.LogWarning(problem, "Register warm-up failed after {ElapsedMs} ms; the first reader builds the snapshot as before",
                watch.ElapsedMilliseconds);
        }
    }
}
