using Microsoft.EntityFrameworkCore;
using Scmos.Api.Auth;
using Scmos.Api.Data;
using Scmos.Api.Rules;

namespace Scmos.Api.Services;

/// <summary>
/// Who sees a stored message (4 Oct 2026), asked by every place that shows one — the Communication Center, its detail,
/// a mail attachment's document — so the rule is written once.
///
/// <para>
/// A shared mailbox's mail is seen by everybody who reads the Communication Center, as before. A personal mailbox's mail
/// is seen by its owner, by Supervisor and above, and by whoever owns a job it is linked to (a link nobody rejected) —
/// the user's decision: an operator does not read a colleague's mailbox, but does see the mail about their own job.
/// </para>
/// </summary>
public static class MailVisibility
{
    /// <summary>Supervisor and above see every mailbox: the same right that settles a message's links.</summary>
    public static bool SeesAll(AppUser user) => user.Can(Capability.EditAnyJob);

    /// <summary>The messages this person may see.</summary>
    public static IQueryable<Email> VisibleTo(this IQueryable<Email> rows, ScmosDbContext db, AppUser user)
    {
        if (SeesAll(user)) return rows;
        var me = user.OperatorId;
        var shared = db.Mailboxes.Where(box => box.OwnerOperatorId == "").Select(box => box.Id);
        if (string.IsNullOrWhiteSpace(me)) return rows.Where(mail => shared.Contains(mail.MailboxId));
        var mine = db.Mailboxes.Where(box => box.OwnerOperatorId == me).Select(box => box.Id);
        var myJobs = db.OperationJobs.Where(job => job.OwnerId == me).Select(job => job.Key);
        var aboutMyJobs = db.EmailJobLinks.Where(link => link.Status != MailLink.Rejected && myJobs.Contains(link.JobKey))
            .Select(link => link.EmailId);
        return rows.Where(mail => shared.Contains(mail.MailboxId) || mine.Contains(mail.MailboxId) || aboutMyJobs.Contains(mail.Id));
    }

    /// <summary>
    /// Whether this person may attach a message to a job as its owner (5 Oct 2026, the user's decision): they edit their
    /// own jobs and read mail, the job is theirs, and the message is one they may see. Those who edit any job need none of
    /// this; rejecting a link, or linking somebody else's job, stays theirs.
    /// </summary>
    public static async Task<bool> MayAttachAsync(ScmosDbContext db, AppUser user, long emailId, string jobKey, CancellationToken token)
    {
        var me = user.OperatorId;
        if (string.IsNullOrWhiteSpace(me) || !user.Can(Capability.EditOwnJobs) || !user.Can(Capability.ViewMailbox)) return false;
        return await db.OperationJobs.AsNoTracking().AnyAsync(job => job.Key == jobKey && job.OwnerId == me, token)
            && await CanSeeAsync(db, user, emailId, token);
    }

    /// <summary>Whether this person may see one message.</summary>
    public static Task<bool> CanSeeAsync(ScmosDbContext db, AppUser user, long emailId, CancellationToken token) =>
        db.Emails.AsNoTracking().Where(mail => mail.Id == emailId).VisibleTo(db, user).AnyAsync(token);

    /// <summary>
    /// Whether a stored document is an attachment of a message this person may not see, not yet filed to a job. Once a
    /// link is confirmed the file is that job's paperwork (<see cref="MailFiling"/>) and is seen as the job's documents
    /// are; before, it follows its message. A document that came from no message is not this rule's to judge.
    /// </summary>
    public static async Task<bool> HidesDocumentAsync(ScmosDbContext db, AppUser user, StoredDocument document, CancellationToken token)
    {
        if (SeesAll(user) || document.JobKey.Length > 0) return false;
        var emails = await db.EmailAttachments.AsNoTracking().Where(file => file.StoredDocumentId == document.Id)
            .Select(file => file.EmailId).Distinct().ToListAsync(token);
        if (emails.Count == 0) return false;
        var visible = await db.Emails.AsNoTracking().Where(mail => emails.Contains(mail.Id)).VisibleTo(db, user).AnyAsync(token);
        return !visible;
    }

    /// <summary>The ids of unfiled attachments of mail this person may not see — for a list to leave out.</summary>
    public static IQueryable<long> HiddenDocuments(ScmosDbContext db, AppUser user)
    {
        var visible = db.Emails.VisibleTo(db, user).Select(mail => mail.Id);
        var unfiled = db.Documents.Where(one => one.JobKey == "").Select(one => one.Id);
        return db.EmailAttachments.Where(file => file.StoredDocumentId > 0 && !visible.Contains(file.EmailId)
                && unfiled.Contains(file.StoredDocumentId))
            .Select(file => file.StoredDocumentId);
    }
}
