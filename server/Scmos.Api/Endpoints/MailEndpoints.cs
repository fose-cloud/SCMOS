using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Scmos.Api.Auth;
using Scmos.Api.Data;
using Scmos.Api.Rules;
using Scmos.Api.Services;

namespace Scmos.Api.Endpoints;

/// <summary>
/// The Communication Center: what arrived, which job it is about, and settling
/// the ones the machine was not sure of.
///
/// <para>
/// Reading is <see cref="Capability.ViewMailbox"/>, which the operators hold.
/// <b>Deciding is <see cref="Capability.EditAnyJob"/></b>, because confirming
/// that a message belongs to a job changes what the register says about that
/// job rather than what somebody can see — the plan drew that line and this
/// keeps it.
/// </para>
///
/// <para>
/// The message's processing status is <i>derived</i> from its links after every
/// decision rather than nudged from wherever it was. Somebody confirms one of
/// two suggestions and the message is still waiting; they settle the second and
/// it is not; they reject the only link and it is waiting again. Deriving it is
/// the only way the badge, the list and the page agree.
/// </para>
/// </summary>
public static class MailEndpoints
{
    /// <summary>Which job, and what the person decided about it.</summary>
    /// <param name="JobKey">The job the link points at.</param>
    /// <param name="Status">CONFIRMED or REJECTED — see <see cref="MailReview.IsADecision"/>.</param>
    public record DecisionBody(string? JobKey, string? Status);

    public static void MapMail(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/mail").WithTags("Mail");

        /*
         * The inbox. One page of messages, newest first, with the jobs each is
         * attached to — because a list that says only "linked" makes somebody
         * open every row to find out what it was linked to.
         */
        group.MapGet("/", async (HttpContext context, IUserAccessor users, ScmosDbContext db,
            // Nullable, both of them. A minimal API treats a non-nullable int
            // from the query string as required and answers 500 when it is
            // absent — so a caller that simply wants the first page, which is
            // every caller the screen makes, would have been refused.
            [FromQuery] string? view, [FromQuery] int? page, [FromQuery] int? per,
            [FromQuery] string? q, CancellationToken token) =>
        {
            var user = users.Current(context);
            if (user is null) return ApiResults.SignInRequired;
            if (!user.Can(Capability.ViewMailbox))
                return ApiResults.Error("บัญชีนี้ไม่มีสิทธิ์อ่านศูนย์รวมการติดต่อ", StatusCodes.Status403Forbidden);

            var wanted = MailReview.ViewOf(view);
            var size = Math.Clamp(per is > 0 ? per.Value : 50, 1, 200);
            var wantedPage = Math.Max(page ?? 1, 1);

            var rows = db.Emails.AsNoTracking().AsQueryable();

            // The status half of the view is a column, so it is a predicate the
            // database can answer. The linked half is a join, below.
            if (wanted == MailReview.View.Waiting)
                rows = rows.Where(one => one.ProcessingStatus == MailProcessing.NeedReview
                    || one.ProcessingStatus == MailProcessing.Failed);

            if (!string.IsNullOrWhiteSpace(q))
            {
                var text = q.Trim();
                rows = rows.Where(one => one.Subject.Contains(text)
                    || one.FromAddress.Contains(text)
                    || one.FromName.Contains(text));
            }

            // A message counts as linked when something is attached that nobody
            // has rejected. A rejected link is a record of a decision, not an
            // attachment, and counting it would put mail in the linked list that
            // somebody has explicitly said does not belong there.
            var attached = db.EmailJobLinks.AsNoTracking()
                .Where(one => one.Status != MailLink.Rejected)
                .Select(one => one.EmailId);

            if (wanted == MailReview.View.Linked) rows = rows.Where(one => attached.Contains(one.Id));
            if (wanted == MailReview.View.Unlinked) rows = rows.Where(one => !attached.Contains(one.Id));

            var total = await rows.CountAsync(token);
            var found = await rows
                .OrderByDescending(one => one.ReceivedAt)
                .ThenByDescending(one => one.Id)
                .Skip((wantedPage - 1) * size)
                .Take(size)
                .Select(one => new
                {
                    one.Id,
                    one.Subject,
                    one.FromAddress,
                    one.FromName,
                    one.ReceivedAt,
                    one.HasAttachments,
                    one.ProcessingStatus,
                    one.ErrorMessage,
                })
                .ToListAsync(token);

            // One query for the whole page's links rather than one per row.
            var ids = found.Select(one => one.Id).ToList();
            var links = await db.EmailJobLinks.AsNoTracking()
                .Where(one => ids.Contains(one.EmailId))
                .Select(one => new { one.EmailId, one.JobKey, one.Status, one.Confidence, one.MatchedOn })
                .ToListAsync(token);

            return Results.Json(new
            {
                total,
                page = wantedPage,
                pageCount = (int)Math.Ceiling(total / (double)size),
                view = wanted,
                waiting = await db.Emails.AsNoTracking().CountAsync(one =>
                    one.ProcessingStatus == MailProcessing.NeedReview
                    || one.ProcessingStatus == MailProcessing.Failed, token),
                messages = found.Select(one => new
                {
                    one.Id,
                    one.Subject,
                    one.FromAddress,
                    one.FromName,
                    one.ReceivedAt,
                    one.HasAttachments,
                    status = one.ProcessingStatus,
                    error = one.ErrorMessage,
                    links = links.Where(link => link.EmailId == one.Id)
                        .Select(link => new { link.JobKey, link.Status, link.Confidence, link.MatchedOn }),
                }),
            });
        });

        /*
         * One message, with everything the page shows about it.
         */
        group.MapGet("/{id:long}", async (long id, HttpContext context, IUserAccessor users,
            ScmosDbContext db, CancellationToken token) =>
        {
            var user = users.Current(context);
            if (user is null) return ApiResults.SignInRequired;
            if (!user.Can(Capability.ViewMailbox))
                return ApiResults.Error("บัญชีนี้ไม่มีสิทธิ์อ่านศูนย์รวมการติดต่อ", StatusCodes.Status403Forbidden);

            var message = await db.Emails.AsNoTracking().FirstOrDefaultAsync(one => one.Id == id, token);
            if (message is null) return ApiResults.Error("ไม่พบข้อความนี้", StatusCodes.Status404NotFound);

            var mailbox = await db.Mailboxes.AsNoTracking()
                .Where(one => one.Id == message.MailboxId)
                .Select(one => one.Address)
                .FirstOrDefaultAsync(token) ?? "";

            return Results.Json(new
            {
                message.Id,
                mailbox,
                message.Subject,
                message.FromAddress,
                message.FromName,
                message.SentAt,
                message.ReceivedAt,
                message.HasAttachments,
                status = message.ProcessingStatus,
                error = message.ErrorMessage,
                // The text is what the rules read; the HTML is what a person is
                // shown, and is empty until somebody opens the message — which
                // is this call. Both are sent so the page can prefer the HTML
                // and fall back rather than show an empty panel.
                bodyText = message.BodyText,
                bodyHtml = message.BodyHtml,
                participants = await db.EmailParticipants.AsNoTracking()
                    .Where(one => one.EmailId == id)
                    .Select(one => new { one.Kind, one.Address, one.DisplayName })
                    .ToListAsync(token),
                // What the extractor read, kept rather than recomputed, so a
                // link can be explained against the rules as they were.
                entities = await db.EmailEntities.AsNoTracking()
                    .Where(one => one.EmailId == id)
                    .Select(one => new { one.Kind, one.Value, one.InSubject, one.WellFormed })
                    .ToListAsync(token),
                links = await db.EmailJobLinks.AsNoTracking()
                    .Where(one => one.EmailId == id)
                    .OrderByDescending(one => one.Confidence)
                    .Select(one => new
                    {
                        one.JobKey, one.Status, one.Confidence,
                        one.MatchedOn, one.MatchedValue, one.ConfirmedBy, one.ConfirmedAt,
                    })
                    .ToListAsync(token),
                attachments = await db.EmailAttachments.AsNoTracking()
                    .Where(one => one.EmailId == id)
                    .Select(one => new { one.Id, one.FileName, one.ContentType, one.SizeBytes, one.StoredDocumentId })
                    .ToListAsync(token),
            });
        });

        /*
         * Settling one link. Confirm it, or reject it.
         */
        group.MapPost("/{id:long}/decide", async (long id, DecisionBody body, HttpContext context,
            IUserAccessor users, ScmosDbContext db, AuditService audit, CancellationToken token) =>
        {
            var user = users.Current(context);
            if (user is null) return ApiResults.SignInRequired;
            // Reading the mail is one permission; saying which job it belongs to
            // is another, because that is a change to the register.
            if (!user.Can(Capability.EditAnyJob))
                return ApiResults.Error("ยืนยันหรือปฏิเสธการจับคู่ได้เฉพาะผู้ที่แก้ไขงานของทีมได้",
                    StatusCodes.Status403Forbidden);

            var jobKey = (body?.JobKey ?? "").Trim();
            var status = (body?.Status ?? "").Trim().ToUpperInvariant();
            if (jobKey.Length == 0)
                return ApiResults.Error("ต้องระบุงาน", StatusCodes.Status400BadRequest);
            if (!MailReview.IsADecision(status))
                return ApiResults.Error("ตัดสินได้เฉพาะ ยืนยัน หรือ ปฏิเสธ", StatusCodes.Status400BadRequest);

            var message = await db.Emails.FirstOrDefaultAsync(one => one.Id == id, token);
            if (message is null) return ApiResults.Error("ไม่พบข้อความนี้", StatusCodes.Status404NotFound);

            var link = await db.EmailJobLinks
                .FirstOrDefaultAsync(one => one.EmailId == id && one.JobKey == jobKey, token);

            if (link is null)
            {
                // Linking by hand. Only ever as a confirmation — "reject" a link
                // that was never made is a row recording that somebody declined
                // to do something nobody suggested.
                if (status != MailLink.Confirmed)
                    return ApiResults.Error("ยังไม่มีการจับคู่กับงานนี้ให้ปฏิเสธ", StatusCodes.Status404NotFound);

                var known = await db.OperationJobs.AsNoTracking().AnyAsync(one => one.Key == jobKey, token);
                if (!known)
                    return ApiResults.Error("ไม่พบงานนี้ในระบบ", StatusCodes.Status404NotFound);

                link = new EmailJobLink
                {
                    EmailId = id,
                    JobKey = jobKey,
                    // No confidence, because nobody computed one. A hand-made
                    // link is somebody's word, and dressing it up as 1.0 would
                    // make it indistinguishable from an arithmetic nobody ran.
                    Confidence = 0,
                    MatchedOn = "PERSON",
                    MatchedValue = "",
                    CreatedAt = DateTimeOffset.UtcNow,
                };
                db.EmailJobLinks.Add(link);
            }

            link.Status = status;
            link.ConfirmedBy = user.Signature;
            link.ConfirmedAt = DateTimeOffset.UtcNow;

            /*
             * Every *other* link from the database, plus the one just decided.
             *
             * Not all of them from the database: this query runs before the
             * save, so the row being changed still reads with its old status
             * there. Reading them all left a settled message still carrying the
             * SUGGESTED it had a moment ago, so confirming a suggestion cleared
             * nothing and the message stayed in the waiting list — with a
             * confirmed link on it, which is the sort of thing that makes people
             * stop trusting a queue.
             */
            var statuses = await db.EmailJobLinks
                .Where(one => one.EmailId == id && one.JobKey != jobKey)
                .Select(one => one.Status)
                .ToListAsync(token);
            statuses.Add(status);

            message.ProcessingStatus = MailReview.StatusAfter(statuses, message.ProcessingStatus);
            await db.SaveChangesAsync(token);

            // Who said which message belongs to which job, and when. The link
            // itself records it, but the audit trail is where somebody looks
            // when a job's paperwork is questioned months later.
            await audit.RecordAsync(user,
                status == MailLink.Confirmed ? AuditActions.Approve : AuditActions.Reject,
                "email", id.ToString(), message.Subject, "job", "", jobKey, "", token);

            return Results.Json(new { ok = true, status = message.ProcessingStatus, jobKey, link = link.Status });
        });
    }
}
