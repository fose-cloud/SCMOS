using Microsoft.EntityFrameworkCore;
using Scmos.Api.Data;
using Scmos.Api.Rules;

namespace Scmos.Api.Ai.Communication;

/// <summary>
/// The tables behind the Communication Agent's read: the LINE ledger and
/// its rooms, the mail ledger and its job links, and the register's own
/// columns for labelling a job — read as the LINE and mail screens read
/// them, no wider.
/// </summary>
public sealed class CommunicationSource(ScmosDbContext db) : ICommunicationSource
{
    public async Task<IReadOnlyList<MessageJob>> FindJobsAsync(string query, int take, CancellationToken token)
    {
        var text = query.Trim();
        if (text.Length == 0) return [];
        var rows = await db.OperationJobs.AsNoTracking()
            .Where(job => job.Key == text || job.JobCode.Contains(text) || job.Container.Contains(text) || job.Customer.Contains(text))
            .OrderByDescending(job => job.WorkDate)
            .Take(take)
            .Select(job => new MessageJob(job.Key, job.JobCode, job.Customer, job.Trucker, job.OwnerId, job.Cat, job.WorkDate, job.Status))
            .ToListAsync(token);
        // The plan date is a string; sort it as a date once it is in hand.
        return rows.OrderByDescending(job => Formats.DateNumber(job.Date)).ToList();
    }

    public async Task<IReadOnlyList<MessageJob>> JobsAsync(IReadOnlyCollection<string> keys, CancellationToken token)
    {
        if (keys.Count == 0) return [];
        var wanted = keys.ToList();
        return await db.OperationJobs.AsNoTracking()
            .Where(job => wanted.Contains(job.Key))
            .Select(job => new MessageJob(job.Key, job.JobCode, job.Customer, job.Trucker, job.OwnerId, job.Cat, job.WorkDate, job.Status))
            .ToListAsync(token);
    }

    public async Task<IReadOnlyList<LineMessageRow>> LineAsync(DateTimeOffset since, IReadOnlyCollection<string>? jobKeys, int take, CancellationToken token)
    {
        var query = db.LineEvents.AsNoTracking()
            .Where(row => row.ReceivedAt >= since && (row.MessageType == "text" || row.MessageType == "image" || row.MessageType == CarrierEvent.MessageType));
        if (jobKeys is not null)
        {
            var wanted = jobKeys.ToList();
            query = query.Where(row => wanted.Contains(row.JobKey));
        }
        var rows = await query.OrderByDescending(row => row.ReceivedAt).Take(take).ToListAsync(token);
        var groupIds = rows.Select(row => row.LineGroupId).Where(id => id.Length > 0).Distinct().ToList();
        var names = groupIds.Count == 0 ? new Dictionary<string, string>() : await db.LineGroups.AsNoTracking()
            .Where(group => groupIds.Contains(group.LineGroupId))
            .ToDictionaryAsync(group => group.LineGroupId, group => group.GroupName, token);
        return rows.Select(row => new LineMessageRow(row, names.GetValueOrDefault(row.LineGroupId, ""))).ToList();
    }

    public async Task<IReadOnlyList<MailMessageRow>> MailAsync(IReadOnlyCollection<string> jobKeys, int take, CancellationToken token)
    {
        if (jobKeys.Count == 0) return [];
        var wanted = jobKeys.ToList();
        return await db.EmailJobLinks.AsNoTracking()
            .Where(link => wanted.Contains(link.JobKey))
            .Join(db.Emails.AsNoTracking(), link => link.EmailId, mail => mail.Id, (link, mail) => new { link, mail })
            .OrderByDescending(pair => pair.mail.SentAt)
            .Take(take)
            .Select(pair => new MailMessageRow(pair.mail.Id, pair.mail.Subject, pair.mail.FromName, pair.mail.SentAt, pair.mail.ProcessingStatus,
                pair.mail.HasAttachments, pair.link.JobKey, pair.link.Status, pair.link.Confidence, pair.link.MatchedOn))
            .ToListAsync(token);
    }
}
