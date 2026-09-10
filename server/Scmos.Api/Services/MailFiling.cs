using Microsoft.EntityFrameworkCore;
using Scmos.Api.Data;
using Scmos.Api.Rules;

namespace Scmos.Api.Services;

/// <summary>
/// Putting a stored mail attachment on the job it turned out to be about.
///
/// <para>
/// The bytes are fetched within seconds of a message arriving, and at that
/// moment nobody has said which job it belongs to — the matcher may have
/// suggested one, and a suggestion is not an answer. So the file is written
/// into the mail tree, and this is what happens afterwards: when a link is
/// confirmed, the documents that came on that message gain the job's key and
/// appear in that job's paperwork list.
/// </para>
///
/// <para>
/// <b>The key in Blob does not move.</b> Only the row changes. Re-filing the
/// bytes would mean copying a blob, writing a second one, and having two
/// answers to where a file is — and the whole point of
/// <see cref="BlobPaths"/> is that the key alone explains the file. A key that
/// changes when somebody clicks a button explains nothing.
/// </para>
///
/// <para>
/// Called from both ends, because either can happen first: the worker calls it
/// after storing bytes, in case the link was already settled, and the decision
/// endpoint calls it after confirming a link, in case the bytes were already
/// stored. Neither knows which raced ahead, and the operation is written so it
/// does not matter.
/// </para>
/// </summary>
public static class MailFiling
{
    /// <summary>
    /// Attach this message's stored documents to the job somebody confirmed.
    ///
    /// <para>
    /// Only a confirmed link, and only when there is exactly one. Two confirmed
    /// jobs on one message is a legitimate state — a mail about a consolidation
    /// names several — and picking the first would file a customer's document
    /// under whichever job happened to have the lower id. Left unfiled, the
    /// document is still in the mail tree and still reachable from the message,
    /// which is worse for nobody; guessing would be worse for somebody.
    /// </para>
    /// </summary>
    /// <returns>How many document rows were given a job key.</returns>
    public static async Task<int> AttachToJobAsync(ScmosDbContext db, long emailId,
        CancellationToken token)
    {
        var confirmed = await db.EmailJobLinks.AsNoTracking()
            .Where(one => one.EmailId == emailId && one.Status == MailLink.Confirmed)
            .Select(one => one.JobKey)
            .Distinct()
            .Take(2)
            .ToListAsync(token);
        if (confirmed.Count != 1) return 0;

        var jobKey = confirmed[0];

        var documentIds = await db.EmailAttachments.AsNoTracking()
            .Where(one => one.EmailId == emailId && one.StoredDocumentId != 0)
            .Select(one => one.StoredDocumentId)
            .ToListAsync(token);
        if (documentIds.Count == 0) return 0;

        // Only rows that do not already say so, so calling this twice — which
        // both callers may — writes nothing the second time.
        var rows = await db.Documents
            .Where(one => documentIds.Contains(one.Id) && one.JobKey != jobKey)
            .ToListAsync(token);
        if (rows.Count == 0) return 0;

        var job = await db.OperationJobs.AsNoTracking()
            .FirstOrDefaultAsync(one => one.Key == jobKey, token);
        // A link confirmed against a job that has since gone. The document keeps
        // its own tree and says nothing it cannot support.
        if (job is null) return 0;

        foreach (var row in rows)
        {
            row.JobKey = job.Key;
            // The customer is filled in from the job rather than left blank,
            // because the documents screen reads it — but the year and the
            // reference are not touched: those describe the key this file was
            // actually written under, and it was not written under the job's.
            row.Customer = job.Customer;
        }

        await db.SaveChangesAsync(token);
        return rows.Count;
    }
}
