using Microsoft.EntityFrameworkCore;
using Scmos.Api.Data;
using Scmos.Api.Rules;

namespace Scmos.Api.Services;

/// <summary>
/// Fetches the messages the webhook only heard about, and catches up on the
/// ones it never heard about at all.
///
/// <para>
/// Two jobs, and the second is the one that is easy to leave out. The webhook
/// writes down that a message arrived; this fills it in. But Graph also sends
/// <c>missed</c> — it gave up on delivering something and cannot say what — and
/// there is nothing at the webhook that can recover from that, because nothing
/// there knows which message was lost. <b>The only recovery is to read the
/// mailbox forward again</b>, from where we last got to, which is what the
/// catch-up below does on a slower clock. Without it, <c>missed</c> is a log
/// line and the mail it stands for is simply never seen.
/// </para>
///
/// <para>
/// The claim-one-row shape is the LINE worker's, including the part that was
/// got wrong there first: <b>a stale claim is measured from the claim, never
/// from arrival</b>. A backlog is full of rows that arrived long ago and are
/// being worked on right now, and testing arrival hands those to a second
/// instance while the first still holds them — undoing the claim the guard
/// exists to make.
/// </para>
///
/// <para>
/// A configuration fault stops the pass instead of failing rows. Missing
/// consent is true of every message in the queue equally, and a worker that
/// keeps going spends a thousand messages' retry budget on it — turning a five
/// minute grant into a morning of re-queueing.
/// </para>
/// </summary>
public class MailWorker(IServiceProvider services, ILogger<MailWorker> log) : BackgroundService
{
    /// <summary>The queue is drained often; a notification should not sit long.</summary>
    private static readonly TimeSpan Tick = TimeSpan.FromSeconds(15);

    /// <summary>
    /// The catch-up runs on a much slower clock. It is the safety net under the
    /// webhook, not the way mail normally arrives, and reading a mailbox
    /// forward every fifteen seconds would be paying for the net constantly.
    /// </summary>
    private static readonly TimeSpan CatchUpEvery = TimeSpan.FromMinutes(15);

    /// <summary>How many messages one pass will fill in.</summary>
    private const int Batch = 20;

    /// <summary>
    /// How many pages one catch-up will read per mailbox.
    ///
    /// Bounded so a first connection, or a long outage, cannot turn one pass
    /// into an hour inside Graph. Whatever is left is read on the next pass —
    /// the high-water mark only moves over what was actually stored.
    /// </summary>
    private const int PagesPerCatchUp = 5;

    private DateTimeOffset _lastCatchUp = DateTimeOffset.MinValue;

    protected override async Task ExecuteAsync(CancellationToken stopping)
    {
        // Nothing for the first minute. A container that restarts repeatedly
        // should not talk to Graph on every boot.
        try
        {
            await Task.Delay(TimeSpan.FromMinutes(1), stopping);
        }
        catch (OperationCanceledException) { return; }

        while (!stopping.IsCancellationRequested)
        {
            try
            {
                if (DateTimeOffset.UtcNow - _lastCatchUp >= CatchUpEvery)
                {
                    _lastCatchUp = DateTimeOffset.UtcNow;
                    await CatchUpAsync(stopping);
                }

                await DrainAsync(stopping);
            }
            catch (OperationCanceledException) when (stopping.IsCancellationRequested)
            {
                return;
            }
            catch (Exception problem)
            {
                // Never fatal. A worker that takes the API down with it is a
                // worse outcome than mail processed a tick late.
                log.LogError(problem, "Mail worker pass failed");
            }

            try
            {
                await Task.Delay(Tick, stopping);
            }
            catch (OperationCanceledException) { return; }
        }
    }

    /// <summary>Fill in the messages the webhook wrote down.</summary>
    private async Task DrainAsync(CancellationToken stopping)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ScmosDbContext>();
        var reader = scope.ServiceProvider.GetRequiredService<GraphMailReader>();
        // The same scope, so the linker writes through the context this pass
        // already holds rather than opening a second one beside it.
        var linker = scope.ServiceProvider.GetRequiredService<MailLinker>();

        // Anything claimed and then dropped goes back first. The test is the
        // claim time — see the note on this class.
        var stale = DateTimeOffset.UtcNow - MailQueue.Abandoned;
        var recovered = await db.Emails
            .Where(one => one.ProcessingStatus == MailProcessing.Processing
                && (one.ProcessedAt == null || one.ProcessedAt < stale))
            .ExecuteUpdateAsync(set => set
                .SetProperty(one => one.ProcessingStatus, MailProcessing.Received), stopping);
        if (recovered > 0)
            log.LogWarning("Mail worker returned {Count} abandoned message(s) to the queue", recovered);

        var waiting = await db.Emails.AsNoTracking()
            .Where(one => one.ProcessingStatus == MailProcessing.Received)
            .OrderBy(one => one.ReceivedAt)
            .Take(Batch)
            .Select(one => one.Id)
            .ToListAsync(stopping);

        foreach (var id in waiting)
        {
            if (stopping.IsCancellationRequested) return;

            // The claim. Only the instance whose update affects a row owns it,
            // and the stamp is what lets the recovery above tell a row being
            // worked on from one whose worker is gone.
            var now = DateTimeOffset.UtcNow;
            var claimed = await db.Emails
                .Where(one => one.Id == id && one.ProcessingStatus == MailProcessing.Received)
                .ExecuteUpdateAsync(set => set
                    .SetProperty(one => one.ProcessingStatus, MailProcessing.Processing)
                    .SetProperty(one => one.ProcessedAt, now), stopping);
            if (claimed == 0) continue;

            if (!await FillAsync(db, reader, linker, id, stopping)) return;
        }
    }

    /// <summary>
    /// Fetch one message and write what came back. False means stop the pass.
    /// </summary>
    private async Task<bool> FillAsync(ScmosDbContext db, GraphMailReader reader, MailLinker linker,
        long id, CancellationToken stopping)
    {
        var row = await db.Emails.FirstOrDefaultAsync(one => one.Id == id, stopping);
        if (row is null) return true;

        var mailbox = await db.Mailboxes.AsNoTracking()
            .FirstOrDefaultAsync(one => one.Id == row.MailboxId, stopping);
        if (mailbox is null)
        {
            // A message whose mailbox row has gone. Nothing to fetch it from,
            // and no amount of retrying will produce one.
            row.ProcessingStatus = MailProcessing.Failed;
            row.ErrorCode = "no_mailbox";
            row.ErrorMessage = "ไม่พบตู้จดหมายของข้อความนี้แล้ว";
            row.ProcessedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(stopping);
            return true;
        }

        var fetched = await reader.MessageAsync(mailbox.Address, row.GraphMessageId, stopping);
        var decision = MailQueue.Decide(fetched.Finding.Code, fetched.Ok, row.RetryCount);

        switch (decision)
        {
            case MailQueue.Next.Pause:
                // Put this row back untouched — it did nothing wrong — and stop.
                row.ProcessingStatus = MailProcessing.Received;
                await db.SaveChangesAsync(stopping);
                log.LogWarning("Mail worker paused: {Why}", fetched.Finding.Message);
                return false;

            case MailQueue.Next.Retry:
                row.ProcessingStatus = MailProcessing.Received;
                row.RetryCount += 1;
                row.ErrorCode = fetched.Finding.Code;
                row.ErrorMessage = Fit(fetched.Finding.Message);
                await db.SaveChangesAsync(stopping);
                return true;

            case MailQueue.Next.GiveUp:
                row.ProcessingStatus = MailProcessing.Failed;
                row.RetryCount += 1;
                row.ErrorCode = fetched.Finding.Code;
                row.ErrorMessage = Fit(fetched.Finding.Message);
                row.ProcessedAt = DateTimeOffset.UtcNow;
                await db.SaveChangesAsync(stopping);
                log.LogError("Mail message {Id} failed after {Attempts} attempts: {Why}",
                    id, row.RetryCount, fetched.Finding.Message);
                return true;

            case MailQueue.Next.Gone:
                // The queue is finished with it. PROCESSED here means "nothing
                // further to do", not "we hold its contents" — the error code
                // is what says which, and a deleted message is not a fault
                // anybody should be shown a red count for.
                row.ProcessingStatus = MailProcessing.Processed;
                row.ErrorCode = "message_gone";
                row.ErrorMessage = "ข้อความถูกลบหรือย้ายไปแล้วก่อนที่ระบบจะอ่านได้";
                row.ProcessedAt = DateTimeOffset.UtcNow;
                await db.SaveChangesAsync(stopping);
                return true;
        }

        // Ok, but nothing came back that could be read. Treated as a retry
        // rather than a success: the alternative is a stored message with no
        // sender and no body that looks like a real one.
        if (fetched.Value is not { } message)
        {
            row.ProcessingStatus = row.RetryCount + 1 >= MailQueue.MaxRetries
                ? MailProcessing.Failed : MailProcessing.Received;
            row.RetryCount += 1;
            row.ErrorCode = "unreadable";
            row.ErrorMessage = "Microsoft Graph ตอบสำเร็จ แต่อ่านเนื้อหาข้อความไม่ได้";
            await db.SaveChangesAsync(stopping);
            return true;
        }

        await StoreAsync(db, reader, linker, row, mailbox, message, stopping);
        return true;
    }

    /// <summary>
    /// Write the message, everybody on it, and what it had attached.
    ///
    /// <para>
    /// The stub the webhook wrote is updated rather than replaced, so the id
    /// that made it unique keeps meaning what it meant. Participants and
    /// attachments are replaced wholesale, because a second pass over the same
    /// message must leave one set and not two — the row can be filled in twice
    /// after an abandoned claim, and that has to be a no-op rather than a
    /// duplicate.
    /// </para>
    /// </summary>
    private async Task StoreAsync(ScmosDbContext db, GraphMailReader reader, MailLinker linker,
        Email row, Mailbox mailbox, GraphMessages.Read message, CancellationToken stopping)
    {
        row.ConversationId = message.Message.ConversationId;
        row.InternetMessageId = message.Message.InternetMessageId;
        row.Subject = message.Message.Subject;
        row.FromAddress = message.Message.FromAddress;
        row.FromName = message.Message.FromName;
        row.BodyText = message.Message.BodyText;
        row.BodyHtml = message.Message.BodyHtml;
        row.SentAt = message.Message.SentAt;
        // The real arrival time now replaces the moment the notification
        // reached us, which is what the webhook had to use.
        if (message.Message.ReceivedAt != default) row.ReceivedAt = message.Message.ReceivedAt;
        row.HasAttachments = message.Message.HasAttachments;
        row.ErrorCode = "";
        row.ErrorMessage = "";

        var oldParticipants = await db.EmailParticipants
            .Where(one => one.EmailId == row.Id).ToListAsync(stopping);
        db.EmailParticipants.RemoveRange(oldParticipants);
        foreach (var who in message.Participants)
        {
            who.EmailId = row.Id;
            db.EmailParticipants.Add(who);
        }

        if (row.HasAttachments)
        {
            var attachments = await reader.AttachmentsAsync(mailbox.Address, row.GraphMessageId, stopping);
            if (attachments.Ok && attachments.Value is { } files)
            {
                var old = await db.EmailAttachments
                    .Where(one => one.EmailId == row.Id).ToListAsync(stopping);
                db.EmailAttachments.RemoveRange(old);
                foreach (var file in files)
                {
                    file.EmailId = row.Id;
                    file.CreatedAt = DateTimeOffset.UtcNow;
                    db.EmailAttachments.Add(file);
                }
            }
            else
            {
                // The message is worth keeping even when its attachment list
                // could not be read. Said out loud, because the alternative is
                // a message that quietly looks like it had no files.
                log.LogWarning("Stored message {Id} but could not list its attachments: {Why}",
                    row.Id, attachments.Finding.Message);
            }
        }

        row.ProcessingStatus = MailProcessing.Processed;
        row.ProcessedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(stopping);

        await LinkAsync(db, linker, row, stopping);
    }

    /// <summary>
    /// Read what the message names and attach it to the job it is about.
    ///
    /// <para>
    /// After the message is saved, not with it. A message SCMOS holds is worth
    /// having even when the matching fails — the identifiers can be read again,
    /// and a person can link it by hand — so a fault here leaves a stored
    /// message rather than rolling one back.
    /// </para>
    ///
    /// <para>
    /// NEED_REVIEW means somebody has a decision to make: a suggestion to
    /// confirm, or two jobs that both cleared the bar. Mail about no job we hold
    /// stays PROCESSED, because marking every unmatched message for review is
    /// how the flag stops meaning anything.
    /// </para>
    /// </summary>
    private async Task LinkAsync(ScmosDbContext db, MailLinker linker, Email row,
        CancellationToken stopping)
    {
        try
        {
            var result = await linker.LinkAsync(row, stopping);
            if (result.NeedsAPerson)
            {
                row.ProcessingStatus = MailProcessing.NeedReview;
                await db.SaveChangesAsync(stopping);
            }
            log.LogInformation(
                "Message {Id}: {Found} identifier(s), {Linked} linked, {Suggested} suggested",
                row.Id, result.Found, result.Linked, result.Suggested);
        }
        catch (OperationCanceledException) when (stopping.IsCancellationRequested) { throw; }
        catch (Exception problem)
        {
            // The message is stored either way. Matching it again is what the
            // next pass over an abandoned claim does, and doing it by hand is
            // what the Communication Center is for.
            log.LogError(problem, "Stored message {Id} but could not match it to a job", row.Id);
        }
    }

    /// <summary>
    /// Read each mailbox forward from where we last got to.
    ///
    /// <para>
    /// This is what recovers a <c>missed</c> lifecycle event, and what covers
    /// every gap the webhook cannot know about: a subscription that lapsed
    /// before the renewal loop caught it, an App Service that was down when a
    /// notification was delivered, a notification Graph simply never sent.
    /// </para>
    ///
    /// <para>
    /// The high-water mark moves only over messages that were actually written
    /// down, so a pass that stops half way through costs a repeat rather than a
    /// gap. Repeats are free — the unique key stores each message once.
    /// </para>
    /// </summary>
    private async Task CatchUpAsync(CancellationToken stopping)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ScmosDbContext>();
        var reader = scope.ServiceProvider.GetRequiredService<GraphMailReader>();
        var graph = scope.ServiceProvider.GetRequiredService<GraphAuth>();

        var mailboxes = await db.Mailboxes.Where(one => one.IsActive).ToListAsync(stopping);

        foreach (var mailbox in mailboxes)
        {
            if (stopping.IsCancellationRequested) return;
            if (!graph.Approves(mailbox.Address)) continue;

            var now = DateTimeOffset.UtcNow;
            var from = MailQueue.ReadFrom(mailbox.LastSyncedAt, now);
            var highWater = mailbox.LastSyncedAt;
            var found = 0;

            var page = await reader.PageAsync(mailbox.Address, from, stopping);
            for (var read = 0; read < PagesPerCatchUp; read++)
            {
                if (!page.Ok || page.Value is not { } current)
                {
                    if (MailQueue.Halts(page.Finding.Code, page.Ok))
                        log.LogWarning("Catch-up paused for {Mailbox}: {Why}",
                            mailbox.Address, page.Finding.Message);
                    break;
                }

                foreach (var message in current.Messages)
                {
                    if (await db.Emails.AsNoTracking().AnyAsync(one =>
                            one.MailboxId == mailbox.Id
                            && one.GraphMessageId == message.Message.GraphMessageId, stopping))
                    {
                        // Already known. Still moves the mark: this message is
                        // accounted for, and re-reading it forever would make
                        // the mark stick at the first message we ever saw.
                        if (message.Message.ReceivedAt > (highWater ?? DateTimeOffset.MinValue))
                            highWater = message.Message.ReceivedAt;
                        continue;
                    }

                    // A stub, exactly as the webhook writes one. The drain fills
                    // it in, so there is one path that stores a message and not
                    // two that have to agree.
                    db.Emails.Add(new Email
                    {
                        MailboxId = mailbox.Id,
                        GraphMessageId = message.Message.GraphMessageId,
                        ReceivedAt = message.Message.ReceivedAt,
                        CreatedAt = now,
                        ProcessingStatus = MailProcessing.Received,
                    });
                    found++;

                    if (message.Message.ReceivedAt > (highWater ?? DateTimeOffset.MinValue))
                        highWater = message.Message.ReceivedAt;
                }

                if (found > 0)
                {
                    try
                    {
                        await db.SaveChangesAsync(stopping);
                    }
                    catch (DbUpdateException)
                    {
                        // The webhook stored the same message while this page
                        // was being read. The index settled it; losing is
                        // correct and is not worth an error.
                        db.ChangeTracker.Clear();
                        log.LogInformation("Catch-up lost a race with the webhook on {Mailbox}",
                            mailbox.Address);
                        break;
                    }
                }

                if (current.NextLink.Length == 0) break;
                page = await reader.NextAsync(current.NextLink, stopping);
            }

            if (highWater is { } mark && mark != mailbox.LastSyncedAt)
            {
                mailbox.LastSyncedAt = mark;
                mailbox.UpdatedAt = now;
                await db.SaveChangesAsync(stopping);
            }

            if (found > 0)
                log.LogInformation("Catch-up found {Count} message(s) in {Mailbox} the webhook had not",
                    found, mailbox.Address);
        }
    }

    /// <summary>Cut to the column, so a long Graph complaint cannot fail the save.</summary>
    private static string Fit(string message) => message.Length <= 500 ? message : message[..500];
}
