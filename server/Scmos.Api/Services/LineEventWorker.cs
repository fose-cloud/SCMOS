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
    /// <summary>A photo with no container in it: filed, not queued. A driver's selfie is not for review.</summary>
    public const string NoContainerInPhoto = "no-container-in-photo";

    /// <summary>The model offered numbers and none passed the check digit: a person looks at the photo.</summary>
    public const string ContainerCheckDigit = "container-check-digit";

    /// <summary>The photo could not be fetched or read at all; the note says why.</summary>
    public const string ImageFailed = "image-failed";

    /// <summary>The switch for the bot's replies in the room. On unless "off".</summary>
    public const string RepliesKey = "Line:Replies";

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

        if (row.MessageType == "image")
        {
            // Nothing is said back about a photo — asked to stop on 17 Sep
            // 2026, when four photos of one delivery drew five replies.
            await ProcessImageAsync(db, row, stopping);
            return;
        }

        var read = LineParser.Parse(row.RawText, row.ReceivedAt);
        await ProcessTextAsync(db, row, read, stopping);
        // The room hears back once the row is filed, whatever the filing was.
        if (row.ProcessingStatus != LineProcessing.Processing)
            await AnswerAsync(db, row, LineReply.ForMessage(read, row.ErrorCode, ResolvedTo(row, read)), stopping);
    }

    /// <summary>
    /// The row's reason when a message says nothing about any job — a
    /// greeting, an acknowledgement, a caption. Filed as ignored, never
    /// answered; the room is not to be argued with.
    /// </summary>
    public const string NotAboutAJob = "not-about-a-job";

    /// <summary>The status the message would set, as the matched job's ladder names it, for the acknowledgement.</summary>
    private static string ResolvedTo(LineEvent row, LineParser.Parsed read) =>
        read.Status is null ? "" : row.JobKey.Length > 0 && row.ErrorMessage.Length == 0 ? row.ParsedStatus : read.Status;

    private async Task ProcessTextAsync(ScmosDbContext db, LineEvent row, LineParser.Parsed read, CancellationToken stopping)
    {
        row.JobNumber = read.JobNumber ?? "";
        row.ParsedStatus = read.Status ?? "";
        row.Confidence = read.Confidence;
        row.MatchedRules = string.Join(", ", read.MatchedRules);
        row.Warnings = string.Join(", ", read.Warnings);
        row.ProcessedAt = DateTimeOffset.UtcNow;

        // A message the parser had doubts about, or one with nothing to find a
        // job by, is not a failure and not retried. Reading the same words
        // again will not make them clearer, so it waits for a person. A
        // message with a reference and no doubts goes on to be matched — a
        // plate alone is under the auto threshold, and is matched anyway,
        // because the match is what the reviewer needs to see. Nothing is
        // applied here either way.
        if (read.Warnings.Count > 0 || !read.HasReference)
        {
            // A question — "ถึงโรงงานที่โมงคะ" — is the room talking to itself,
            // and so is a message about no job at all — "สวัสดีครับ",
            // "รับทราบ" (17 Sep 2026). Filed where it can be found, not
            // queued for anybody, and not answered.
            var idle = read.Question || !read.AboutAJob;
            row.ProcessingStatus = idle ? LineProcessing.Ignored : LineProcessing.NeedReview;
            row.ErrorCode = read.Question ? (read.Warnings.Count > 0 ? read.Warnings[0] : "no-reference")
                : !read.AboutAJob ? NotAboutAJob
                : read.Warnings.Count > 0 ? read.Warnings[0] : "no-reference";
            row.ErrorMessage = "";
            await db.SaveChangesAsync(stopping);
            log.LogInformation("LINE message {Id} {Filed}: {Reason}", row.Id, idle ? "filed" : "needs review", row.ErrorCode);
            return;
        }

        // Understood. Now: is the room allowed to say it about that job, and
        // which job is it? The rule decides; this only stores the answer.
        var decision = await LineMatching.DecideAsync(
            db, row.LineGroupId, read, row.ReceivedAt, stopping);

        // One key when there is one job. For a number that covers several of the
        // speaker's own rows the column cannot hold the choice, so it stays
        // empty and the keys go in the message a person reads — unless the
        // message is about every row ("3 ตู้"), when the first key pins it to
        // a job and the note carries the rest, so each row shows it.
        row.JobKey = decision.Keys.Count == 1 || decision.Every ? decision.Keys[0] : "";
        row.ErrorMessage = LineAuthority.KeysNote(decision.Detail, decision.Keys);

        if (decision.Result == LineAuthority.Outcome.AlreadyThere)
        {
            // Nothing to do and nothing wrong. Vendors repeat themselves, and a
            // review queue that fills up with messages agreeing with the
            // register is one nobody reads.
            row.ProcessingStatus = LineProcessing.Ignored;
            row.ErrorCode = decision.Result;
            await db.SaveChangesAsync(stopping);
            log.LogInformation("LINE message {Id}: job {Key} is already {Status}",
                row.Id, row.JobKey, decision.To);
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
        // The status as the job's own ladder names it, for the room's acknowledgement.
        if (decision.Applies && decision.To.Length > 0) row.ParsedStatus = decision.To;
        await db.SaveChangesAsync(stopping);

        log.LogInformation(
            "LINE message {Id}: job {Job} -> {Outcome} (key {Key}, {From} to {To})",
            row.Id, row.JobNumber, row.ErrorCode, row.JobKey, decision.From, decision.To);
    }

    /// <summary>
    /// Says one line back into the room the message came from — only a room
    /// somebody has bound, only while replies are on, and never in a way
    /// that stops the filing: a reply that fails is logged and that is all.
    /// The message's own reply token is tried first (free); an expired one
    /// falls back to a push.
    /// </summary>
    private async Task AnswerAsync(ScmosDbContext db, LineEvent row, string? text, CancellationToken stopping)
    {
        if (text is null || row.LineGroupId.Length == 0) return;
        using var scope = services.CreateScope();
        var config = scope.ServiceProvider.GetRequiredService<IConfiguration>();
        if (string.Equals((config[RepliesKey] ?? "").Trim(), "off", StringComparison.OrdinalIgnoreCase)) return;
        var notifier = scope.ServiceProvider.GetRequiredService<ILineNotifier>();
        if (!notifier.Configured) return;

        var bound = await db.LineGroups.AsNoTracking()
            .AnyAsync(one => one.LineGroupId == row.LineGroupId && one.IsActive && one.GroupType == LineGroupType.Vendor && one.SupplierId > 0, stopping);
        if (!bound) return;

        try
        {
            var failure = await notifier.ReplyAsync(ReplyTokenOf(row.RawPayload), row.LineGroupId, [text], stopping);
            if (failure.Length > 0) log.LogWarning("LINE reply for message {Id} failed: {Why}", row.Id, failure);
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            log.LogWarning(error, "LINE reply for message {Id} threw", row.Id);
        }
    }

    /// <summary>The reply token LINE sent with the event, out of the payload kept on the row.</summary>
    private static string ReplyTokenOf(string payload)
    {
        try
        {
            using var json = System.Text.Json.JsonDocument.Parse(payload);
            return json.RootElement.TryGetProperty("replyToken", out var value) && value.ValueKind == System.Text.Json.JsonValueKind.String
                ? value.GetString() ?? "" : "";
        }
        catch (System.Text.Json.JsonException) { return ""; }
    }

    /// <summary>
    /// A photograph: read for its container number, then matched to the job
    /// waiting for one.
    ///
    /// The reading is kept on the row the first time it is made, so a retry
    /// after a database fault does not pay for the model twice, and so what
    /// the model said survives whatever the rule then decides.
    /// </summary>
    private async Task ProcessImageAsync(ScmosDbContext db, LineEvent row, CancellationToken stopping)
    {
        using var scope = services.CreateScope();
        var reader = scope.ServiceProvider.GetRequiredService<ILineImageReader>();

        if (row.ImageReading.Length == 0 && row.ImageNote.Length == 0)
        {
            var result = await reader.ReadAsync(row, stopping);
            row.ImageKey = result.ImageKey.Length > 0 ? result.ImageKey : row.ImageKey;

            // A number the check digit refused is kept if the register or the
            // haulier's own typed messages already carry it — see
            // LineImageReading.RejectedIn for the photo that taught this.
            var vouched = result.Failure.Length > 0
                ? []
                : await LineMatching.VouchedAsync(db, row.LineGroupId, result.Reading.Rejected, row.ReceivedAt, stopping);
            var accepted = result.Reading.Valid.Concat(vouched).ToList();

            row.ImageReading = string.Join(", ", accepted);
            row.ImageNote = result.Failure.Length > 0
                ? result.Failure
                : Note(result.Reading, vouched);
            row.MatchedRules = string.Join(", ",
                result.Reading.Valid.Select(one => $"container:{one}")
                    .Concat(vouched.Select(one => $"container-vouched:{one}")));
            row.Warnings = result.Reading.Rejected.Count > vouched.Count ? "container-check-digit" : "";
            row.ProcessedAt = DateTimeOffset.UtcNow;

            if (result.Failure.Length > 0)
            {
                row.ProcessingStatus = LineProcessing.NeedReview;
                row.ErrorCode = ImageFailed;
                row.ErrorMessage = result.Failure;
                await db.SaveChangesAsync(stopping);
                log.LogWarning("LINE photo {Id} could not be read: {Why}", row.Id, result.Failure);
                return;
            }
        }

        var numbers = row.ImageReading.Split(", ", StringSplitOptions.RemoveEmptyEntries);
        row.ProcessedAt = DateTimeOffset.UtcNow;

        if (numbers.Length == 0)
        {
            // A misread — the model saw a number and none passed the check
            // digit — is worth a person's look at the photo. A photo with no
            // number in it is not.
            var misread = row.Warnings.Contains(ContainerCheckDigit, StringComparison.Ordinal);
            row.ProcessingStatus = misread ? LineProcessing.NeedReview : LineProcessing.Ignored;
            row.ErrorCode = misread ? ContainerCheckDigit : NoContainerInPhoto;
            row.ErrorMessage = row.ImageNote;
            await db.SaveChangesAsync(stopping);
            log.LogInformation("LINE photo {Id}: {Outcome}", row.Id, row.ErrorCode);
            return;
        }

        if (numbers.Length > 1)
        {
            row.ProcessingStatus = LineProcessing.NeedReview;
            row.ErrorCode = "many-containers";
            row.ErrorMessage = $"อ่านได้ {numbers.Length} ตู้: {row.ImageReading}";
            await db.SaveChangesAsync(stopping);
            return;
        }

        var decision = await LineMatching.DecideContainerAsync(
            db, row.LineGroupId, numbers[0], row.ReceivedAt, stopping);
        row.JobKey = decision.Keys.Count == 1 ? decision.Keys[0] : "";
        row.ErrorMessage = decision.Keys.Count > 1
            ? $"{decision.Detail} ({string.Join(", ", decision.Keys)})"
            : decision.Detail;

        if (decision.Result == LineAuthority.Outcome.AlreadyThere)
        {
            // A haulier's photo of a box the job already carries — the door,
            // the seal — is the truck at the site, at the moment the photo
            // was sent ("เวลาบริษัทขนส่ง", 17 Sep 2026). Offered as the
            // arrival for the one job still waiting for it; a job already
            // there, or two jobs on the same box, leaves the photo filed.
            var waiting = await LineMatching.AwaitingArrivalAsync(db, decision.Keys, stopping);
            if (waiting.Count == 1)
            {
                var sent = LineParser.SentAt(row.ReceivedAt);
                row.JobKey = waiting[0].Key;
                row.ParsedStatus = LineAuthority.ResolveSite(waiting[0].Category, LineParser.SiteArrival);
                row.ProcessingStatus = LineProcessing.NeedReview;
                row.ErrorCode = "ready-to-apply";
                row.ErrorMessage = $"รูปตู้ {numbers[0]} จากผู้ขนส่ง = รถถึงหน้างาน {sent:HH:mm} (เวลาที่ส่งรูป)";
                row.MatchedRules = string.Join(", ", new[] { row.MatchedRules, $"photo-arrival:{sent:HH:mm}" }.Where(one => one.Length > 0));
                await db.SaveChangesAsync(stopping);
                log.LogInformation("LINE photo {Id}: {Container} on {Key} — arrival at {Sent}", row.Id, numbers[0], row.JobKey, sent);
                return;
            }
            row.ProcessingStatus = LineProcessing.Ignored;
            row.ErrorCode = decision.Result;
            await db.SaveChangesAsync(stopping);
            log.LogInformation("LINE photo {Id}: {Container} is already on {Key}", row.Id, numbers[0], row.JobKey);
            return;
        }

        // As for a text message: even a clean match waits for a person.
        row.ProcessingStatus = LineProcessing.NeedReview;
        row.ErrorCode = decision.Applies ? "ready-to-apply" : decision.Result;
        await db.SaveChangesAsync(stopping);
        log.LogInformation("LINE photo {Id}: {Container} -> {Outcome} (key {Key})",
            row.Id, numbers[0], row.ErrorCode, row.JobKey);
    }

    /// <summary>
    /// The row's note: the model's sentence, the numbers that failed the
    /// check but are known anyway, and the ones that failed and are not.
    /// </summary>
    private static string Note(LineImageReading.Reading reading, IReadOnlyList<string> vouched)
    {
        var note = reading.Note;
        if (vouched.Count > 0)
            note = $"{note} — {string.Join(", ", vouched)} check digit ไม่ผ่าน แต่ตรงกับที่ผู้ขนส่งพิมพ์/ทะเบียนงาน".Trim(' ', '—');
        var refused = reading.Rejected.Where(one => !vouched.Contains(one, StringComparer.Ordinal)).ToList();
        if (refused.Count > 0)
            note = $"{note} — เลขที่อ่านได้แต่ {LineImageReading.RejectedMark} {string.Join(", ", refused)}".Trim(' ', '—');
        return note.Length > 500 ? note[..500] : note;
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
