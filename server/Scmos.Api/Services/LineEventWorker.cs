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
/// Nothing here writes to a job yet. This pass proves the pipeline: claim,
/// parse, record what was understood, and file the message as processed or as
/// needing review. Updating the register comes next, and it is deliberately not
/// here — a worker that both drains a queue for the first time and mutates the
/// operational record is two things to debug at once.
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

        /*
         * Understood, and nothing has been done with it yet.
         *
         * The next step resolves the group to a supplier, the number to a job,
         * checks that the one may speak for the other, and writes the status
         * through the transport domain. Until that exists, an understood
         * message is filed for review rather than marked processed — telling
         * the screen a job was updated when none was would be worse than saying
         * nothing.
         */
        row.ProcessingStatus = LineProcessing.NeedReview;
        row.ErrorCode = "awaiting-job-matching";
        row.ErrorMessage = "อ่านข้อความได้แล้ว รอขั้นตอนจับคู่งานและอัปเดตสถานะ";
        await db.SaveChangesAsync(stopping);
        log.LogInformation("LINE message {Id} parsed: job {Job}, status {Status}, confidence {Confidence}",
            id, row.JobNumber, row.ParsedStatus, row.Confidence);
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
