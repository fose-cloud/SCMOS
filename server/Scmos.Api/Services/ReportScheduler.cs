namespace Scmos.Api.Services;

/// <summary>
/// Archives last month's reports when the month turns over.
///
/// <para>
/// Inside this application rather than in a function app or a scheduler
/// service, because it needs no infrastructure that is not already running:
/// the API is on an App Service with Always On, so a hosted service here is
/// awake at midnight without anything being provisioned, paid for, or given
/// its own credentials to the database.
/// </para>
///
/// <para>
/// It is written to be interrupted. App Service recycles a container whenever
/// it likes, so the loop cannot assume it will still be alive an hour from now,
/// and "did I already do this" has to be a question about the database rather
/// than about a variable. That is what the unique index on (customer, month) is
/// for: taking a snapshot twice is refused by the schema, so the worst a
/// restart can cause is wasted work.
/// </para>
/// </summary>
public class ReportScheduler(IServiceProvider services, ILogger<ReportScheduler> log)
    : BackgroundService
{
    /// <summary>
    /// How often it looks at the clock.
    ///
    /// An hour, not a day. A daily timer started at 3pm fires at 3pm, so the
    /// first of the month would be nine hours old before anybody noticed — and
    /// after a restart it would be a different nine hours. Looking hourly and
    /// asking the database whether the work is done makes the schedule a
    /// property of the data rather than of when the process happened to boot.
    /// </summary>
    private static readonly TimeSpan Tick = TimeSpan.FromHours(1);

    /// <summary>
    /// The yard's clock, which is the one a month boundary means anything in.
    /// Midnight UTC on the 1st is seven in the morning here; the reverse would
    /// archive September on the last evening of it.
    /// </summary>
    private static readonly TimeSpan Thailand = TimeSpan.FromHours(7);

    protected override async Task ExecuteAsync(CancellationToken stopping)
    {
        // Nothing for the first few minutes. A container that restarts eleven
        // times in a morning should not run the sweep eleven times, and the work
        // is not urgent to the minute — the month has already ended.
        try
        {
            await Task.Delay(TimeSpan.FromMinutes(5), stopping);
        }
        catch (OperationCanceledException) { return; }

        while (!stopping.IsCancellationRequested)
        {
            try
            {
                await SweepAsync(stopping);
            }
            catch (OperationCanceledException) when (stopping.IsCancellationRequested)
            {
                return;
            }
            catch (Exception problem)
            {
                // Never fatal. A failed sweep is retried next hour; a scheduler
                // that takes the API down with it is a worse outcome than a
                // report archived late.
                log.LogError(problem, "Report archive sweep failed");
            }

            try
            {
                await Task.Delay(Tick, stopping);
            }
            catch (OperationCanceledException) { return; }
        }
    }

    private async Task SweepAsync(CancellationToken token)
    {
        var here = DateTimeOffset.UtcNow.ToOffset(Thailand);

        // Only in the first days of a month. Running the sweep on the twentieth
        // would archive a figure nobody asked for and stamp it "scheduler",
        // which is a claim about when it was taken.
        if (here.Day > 3) return;

        var month = ReportArchiveService.PreviousMonth(here);

        // Its own scope: this is a singleton and the services it needs are
        // scoped to a request everywhere else.
        using var scope = services.CreateScope();
        var archive = scope.ServiceProvider.GetRequiredService<ReportArchiveService>();

        var taken = await archive.TakeMonthAsync(month, "scheduler", token);
        if (taken > 0) log.LogInformation("Archived {Count} report(s) for {Month}", taken, month);
    }
}
