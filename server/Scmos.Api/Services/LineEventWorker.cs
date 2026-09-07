using Microsoft.EntityFrameworkCore;
using Scmos.Api.Data;
using Scmos.Api.Rules;

namespace Scmos.Api.Services;

/// <summary>
/// Reads the LINE messages the webhook stored, and decides what to do with each.
///
/// <para>
/// Inside this application rather than in a function app behind a queue, for
/// the reason ReportScheduler gives: the API is on an App Service with Always
/// On, so a hosted service here is awake without anything being provisioned,
/// paid for, or given its own credentials to the database. The plan writes down
/// what this costs — a table poll is not a queue — and the claim below is what
/// makes it safe anyway.
/// </para>
///
/// <para>
/// <b>The claim is the important part.</b> If App Service scales out, two
/// instances run this loop against one table. Reading a batch and then
/// processing it would have both instances process the same rows. So a row is
/// taken with a conditional update — RECEIVED to PROCESSING, only where it is
/// still RECEIVED — and only the instance whose update affected a row goes on
/// to do the work. The database decides, not the timing.
/// </para>
///
/// <para>
/// Nothing here writes to a job. It claims, parses, works out which job the
/// message means and whether the room that sent it may speak for that job, and
/// files the answer. Applying the answer is a separate step on purpose: changing
/// an operational record needs approval, and a message in a chat room is not
/// one, so even a decision of "ok" is filed as ready-to-apply for an operator.
/// </para>
/// </summary>
public class LineEventWorker(IServiceProvider services, ILogger<LineEventWorker> log)
    : BackgroundService
{
    /// <summary>
    /// How often it looks for work.
    ///
    /// Ten seconds. A vendor writing "ถึงลูกค้าแล้ว" expects the confirmation
    /// while they are still looking at the group, and a poll is cheap: one
    /// indexed query returning nothing, which is what it does nearly always.
    /// </summary>
    private static readonly TimeSpan Tick = TimeSpan.FromSeconds(10);

    /// <summary>How many to take in one pass, so a backlog cannot hold the loop.</summary>
    private const int Batch = 20;

    /// <summary>
    /// How many times a row is retried before it is left alone.
    ///
    /// For technical failures only — a database that was briefly unreachable.
    /// A message that cannot be understood is not retried at all: it goes to
    /// NEED_REVIEW the first time, because reading it again will not change
    /// what it says.
    /// </summary>
    private const int MaxRetries = 3;

    protected override async Task ExecuteAsync(CancellationToken stopping)
    {
        while (!stopping.IsCancellationRequested)
        {
            try
            {
                await DrainAsync(stopping);
            }
            catch (OperationCanceledException) when (stopping.IsCancellationRequested)
            {
                break;
            }
            catch (Exception error)
            {
                // The loop outlives one bad pass. A worker that died on the
                // first transient fault would stop processing LINE messages
                // until somebody noticed the container had gone quiet.
                log.LogError(error, "LINE worker pass failed");
            }

            try
            {
                await Task.Delay(Tick, stopping);
            }
            catch (OperationCanceledException) { break; }
        }
    }

    /// <summary>
    /// How long a row may sit claimed before it is assumed abandoned.
    ///
    /// App Service recycles a container whenever it likes, and a claim taken a
    /// moment before that leaves a row in PROCESSING with nobody working on it.
    /// Without this the message is lost silently — the worst outcome available,
    /// because the vendor believes they reported it. Five minutes is far longer
    /// than a pass takes and far shorter than anybody would wait.
    /// </summary>
    private static readonly TimeSpan Abandoned = TimeSpan.FromMinutes(5);

    private async Task DrainAsync(CancellationToken stopping)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ScmosDbContext>();

        // Anything claimed and then dropped goes back in the queue first. The
        // test is the claim time, not the arrival time: a backlog is full of
        // rows that arrived long ago and are being worked on right now, and
        // testing arrival would hand those to a second instance while the first
        // still held them — undoing the claim this worker exists to make.
        var stale = DateTimeOffset.UtcNow - Abandoned;
        var recovered = await db.LineEvents
            .Where(one => one.ProcessingStatus == LineProcessing.Processing
                && (one.ProcessedAt == null || one.ProcessedAt < stale))
            .ExecuteUpdateAsync(set => set
                .SetProperty(one => one.ProcessingStatus, LineProcessing.Received), stopping);
        if (recovered > 0)
            log.LogWarning("LINE worker returned {Count} abandoned message(s) to the queue", recovered);

        var waiting = await db.LineEvents.AsNoTracking()
            .Where(one => one.ProcessingStatus == LineProcessing.Received)
            .OrderBy(one => one.ReceivedAt)
            .Take(Batch)
            .Select(one => one.Id)
            .ToListAsync(stopping);

        foreach (var id in waiting)
        {
            if (stopping.IsCancellationRequested) return;

            // The claim. Only the instance whose update affects a row owns it.
            var now = DateTimeOffset.UtcNow;
            var claimed = await db.LineEvents
                .Where(one => one.Id == id && one.ProcessingStatus == LineProcessing.Received)
                .ExecuteUpdateAsync(set => set
                    .SetProperty(one => one.ProcessingStatus, LineProcessing.Processing)
                    // Stamped with the claim so the recovery above can tell a row
                    // being worked on from one whose worker is gone.
                    .SetProperty(one => one.ProcessedAt, now), stopping);
            if (claimed == 0) continue;

            try
            {
                await ProcessAsync(db, id, stopping);
            }
            catch (Exception error)
            {
                await FailAsync(db, id, error, stopping);
            }
        }
    }

    private async Task ProcessAsync(ScmosDbContext db, long id, CancellationToken stopping)
    {
        var row = await db.LineEvents.FirstOrDefaultAsync(one => one.Id == id, stopping);
        if (row is null) return;

        var read = LineParser.Parse(row.RawText, row.ReceivedAt);

        row.JobNumber = read.JobNumber ?? "";
        row.ParsedStatus = read.Status ?? "";
        row.Confidence = read.Confidence;
        row.MatchedRules = string.Join(", ", read.MatchedRules);
        row.Warnings = string.Join(", ", read.Warnings);
        row.ProcessedAt = DateTimeOffset.UtcNow;

        if (!read.CanAutoProcess)
        {
            // Not a failure and not retried. Reading the same words again will
            // not make them clearer, so it waits for a person.
            row.ProcessingStatus = LineProcessing.NeedReview;
            row.ErrorCode = read.Warnings.Count > 0 ? read.Warnings[0] : "low-confidence";
            row.ErrorMessage = "";
            await db.SaveChangesAsync(stopping);
            log.LogInformation("LINE message {Id} needs review: {Reason}", id, row.ErrorCode);
            return;
        }

        // Understood. Now: is the room allowed to say it about that job, and
        // which job is it? The rule decides; this only stores the answer.
        var decision = await LineMatching.DecideAsync(
            db, row.LineGroupId, row.JobNumber, row.ParsedStatus, stopping);

        // One key when there is one job. For a number that covers several of the
        // speaker's own rows the column cannot hold the choice, so it stays
        // empty and the keys go in the message a person reads.
        row.JobKey = decision.Keys.Count == 1 ? decision.Keys[0] : "";
        row.ErrorMessage = decision.Keys.Count > 1
            ? $"{decision.Detail} ({string.Join(", ", decision.Keys)})"
            : decision.Detail;

        if (decision.Result == LineAuthority.Outcome.AlreadyThere)
        {
            // Nothing to do and nothing wrong. Vendors repeat themselves, and a
            // review queue that fills up with messages agreeing with the
            // register is one nobody reads.
            row.ProcessingStatus = LineProcessing.Ignored;
            row.ErrorCode = decision.Result;
            await db.SaveChangesAsync(stopping);
            log.LogInformation("LINE message {Id}: job {Key} is already {Status}",
                id, row.JobKey, decision.To);
            return;
        }

        /*
         * Everything else waits for a person, including a decision of "ok".
         *
         * Not because the match is in doubt — it is the one case where it is
         * not — but because changing an operational record needs approval, and
         * a message in a chat room is not one. So an allowed update is filed as
         * ready-to-apply and an operator applies it. The step that does the
         * applying is next, and it hangs off that approval rather than off this
         * loop.
         */
        row.ProcessingStatus = LineProcessing.NeedReview;
        row.ErrorCode = decision.Applies ? "ready-to-apply" : decision.Result;
        await db.SaveChangesAsync(stopping);

        log.LogInformation(
            "LINE message {Id}: job {Job} -> {Outcome} (key {Key}, {From} to {To})",
            id, row.JobNumber, row.ErrorCode, row.JobKey, decision.From, decision.To);
    }

    /// <summary>
    /// A technical failure: retried a few times, then left where somebody can
    /// see it.
    ///
    /// Distinct from a message that could not be understood, which is never
    /// retried. Mixing the two is how a queue ends up spinning on a message it
    /// will never process while a genuine outage goes unnoticed.
    /// </summary>
    private async Task FailAsync(ScmosDbContext db, long id, Exception error, CancellationToken stopping)
    {
        try
        {
            db.ChangeTracker.Clear();
            var row = await db.LineEvents.FirstOrDefaultAsync(one => one.Id == id, stopping);
            if (row is null) return;

            row.RetryCount += 1;
            row.ErrorCode = "processing-failed";
            // The type and message, not the stack. This column is read on a
            // screen, and a stack trace there is noise around the one line that
            // says what went wrong.
            row.ErrorMessage = $"{error.GetType().Name}: {error.Message}";
            row.ProcessingStatus = row.RetryCount >= MaxRetries
                ? LineProcessing.Failed
                : LineProcessing.Received;
            await db.SaveChangesAsync(stopping);

            log.LogError(error, "LINE message {Id} failed, attempt {Attempt}", id, row.RetryCount);
        }
        catch (Exception second)
        {
            // The database is the thing that is broken. Leaving the row in
            // PROCESSING is survivable — see the note on stuck rows in the
            // review screen — and shouting into the log is all that is left.
            log.LogError(second, "LINE message {Id} could not even be marked failed", id);
        }
    }
}
