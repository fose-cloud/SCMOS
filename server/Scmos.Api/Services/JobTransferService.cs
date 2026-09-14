using System.Data;
using Microsoft.EntityFrameworkCore;
using Scmos.Api.Auth;
using Scmos.Api.Data;
using Scmos.Api.Rules;

namespace Scmos.Api.Services;

/// <summary>
/// Moving one person's jobs to another, in the register.
///
/// Preview first, then apply — the same two steps as the supplier and CAR/PAR
/// imports, for the same reason: the number this is about to change is the
/// number the supervisor should see before it changes. Which jobs move is
/// <see cref="JobTransfer"/>'s decision; this only reads them, writes them,
/// and leaves an audit row per job saying who had it and who has it now.
/// </summary>
public class JobTransferService(ScmosDbContext db, JobRegisterCache register, AuditService audit)
{
    /// <summary>Somebody whose name is on at least one job.</summary>
    public record Holder(string Id, string Name, string Role, bool Active, int Jobs);

    /// <summary>Somebody the work may be given to — the same rule as a delegation.</summary>
    public record Receiver(string Id, string Name, string Role);

    /// <param name="Moving">The jobs that will change hands.</param>
    /// <param name="Undated">Left behind because nothing says which day they fall on.</param>
    /// <param name="ClosedOut">Left behind because they are finished or cancelled.</param>
    /// <param name="Keys">The moved jobs, so the workspace can change the same rows without reloading.</param>
    public record Outcome(bool Ok, string Message, string FromName, string ToName, string Period,
        int Held, int Moving, int Undated, int ClosedOut,
        IReadOnlyList<string> Sample, bool Applied, IReadOnlyList<string> Keys);

    /// <summary>
    /// The people this screen can move work from and to.
    ///
    /// Holders are read off the register rather than the staff list, so a
    /// person who has left and been made inactive — the case a transfer
    /// exists for — is still offered, with the number of jobs their name is
    /// still on. Receivers are the same operations line a delegation may go
    /// to; the caller is included, since a supervisor covering somebody's
    /// week takes the work themselves as often as not.
    /// </summary>
    public async Task<(IReadOnlyList<Holder> Holders, IReadOnlyList<Receiver> Receivers)> PeopleAsync(
        CancellationToken token)
    {
        var counts = await db.OperationJobs.AsNoTracking()
            .Where(job => job.OwnerId != "")
            .GroupBy(job => job.OwnerId)
            .Select(group => new { Id = group.Key, Jobs = group.Count() })
            .ToListAsync(token);
        var staff = await db.Staff.AsNoTracking().ToListAsync(token);
        var people = staff.ToDictionary(person => person.Id, StringComparer.OrdinalIgnoreCase);

        var holders = counts
            .Select(count => people.TryGetValue(count.Id, out var person)
                ? new Holder(person.Id, person.Name, person.Role, person.Active, count.Jobs)
                // An id the directory has never heard of still holds jobs, and
                // the way to find out whose they were is to be able to see it.
                : new Holder(count.Id, count.Id, "", false, count.Jobs))
            .OrderBy(holder => holder.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var receivers = staff
            .Where(person => DelegationService.CanReceive(person, ""))
            .OrderBy(person => person.Name, StringComparer.OrdinalIgnoreCase)
            .Select(person => new Receiver(person.Id, person.Name, person.Role))
            .ToList();

        return (holders, receivers);
    }

    public async Task<Outcome> RunAsync(AppUser actor, string fromId, string toId,
        string fromDate, string toDate, string reason, bool apply, CancellationToken token)
    {
        static Outcome Refused(string why) => new(false, why, "", "", "", 0, 0, 0, 0, [], false, []);

        var from = fromId.Trim();
        var to = toId.Trim();
        if (from.Length == 0) return Refused("เลือกคนที่ลาก่อน");
        if (to.Length == 0) return Refused("เลือกคนที่จะรับงานก่อน");
        if (string.Equals(from, to, StringComparison.OrdinalIgnoreCase))
            return Refused("โยกงานให้ตัวเองไม่ได้");

        var (period, problem) = JobTransfer.ReadPeriod(fromDate, toDate);
        if (period is null) return Refused(problem!);

        // Nothing is written without a reason. The audit row is the only place
        // that will ever say why forty jobs changed hands on a Tuesday.
        if (apply && Formats.Clean(reason).Length < 4)
            return Refused("ใส่เหตุผลอย่างน้อย 4 ตัวอักษร เช่น ลาพักร้อน");

        var receiver = await db.Staff.AsNoTracking()
            .FirstOrDefaultAsync(person => person.Id == to, token);
        if (receiver is null || !DelegationService.CanReceive(receiver, from))
            return Refused("คนที่จะรับงานต้องเป็นบัญชีฝ่ายปฏิบัติการที่ยังใช้งานอยู่");

        // The person leaving need not be active — that is the point — and need
        // not even be in the directory: a row whose owner id nobody recognises
        // is exactly a row somebody needs to be able to take.
        var leaver = await db.Staff.AsNoTracking()
            .FirstOrDefaultAsync(person => person.Id == from, token);
        var fromName = leaver?.Name ?? from;
        var described = period.Describe(Formats.Clean(fromDate), Formats.Clean(toDate));

        return await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            // A failed transient attempt must not leave modified entities for the retry.
            db.ChangeTracker.Clear();
            await using var transaction = apply
                ? await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, token) : null;

            var held = apply
                ? await db.OperationJobs.Where(job => job.OwnerId == from).ToListAsync(token)
                : await db.OperationJobs.AsNoTracking().Where(job => job.OwnerId == from).ToListAsync(token);

            var moving = new List<OperationJob>();
            int undated = 0, closedOut = 0;
            foreach (var job in held)
            {
                switch (JobTransfer.FateOf(period, job.WorkDate, job.Status))
                {
                    case JobTransfer.Fate.Moves: moving.Add(job); break;
                    case JobTransfer.Fate.Undated: undated++; break;
                    case JobTransfer.Fate.ClosedOut: closedOut++; break;
                }
            }
            moving.Sort((a, b) => Formats.DateNumber(a.WorkDate).CompareTo(Formats.DateNumber(b.WorkDate)));

            var sample = moving.Take(5)
                .Select(job =>
                {
                    var line = string.Join(" · ", new[] { job.WorkDate, job.Customer, job.JobCode }
                        .Where(part => part.Length > 0));
                    // A row with none of the three is still a row; its key is
                    // the only name it has.
                    return line.Length > 0 ? line : job.Key;
                })
                .ToList();

            if (apply && moving.Count > 0)
            {
                var now = DateTimeOffset.UtcNow;
                var why = Formats.Clean(reason);
                foreach (var job in moving)
                {
                    var label = job.JobCode.Length > 0 && job.Customer.Length > 0
                        ? $"{job.JobCode} · {job.Customer}"
                        : job.JobCode.Length > 0 ? job.JobCode : job.Customer;
                    // One row per job, the same shape as a reassignment made from
                    // the grid, so the trail reads the same whichever way it
                    // was done — and so "who could have edited this" has one
                    // answer for every job that changed hands.
                    audit.Stage(actor, AuditActions.Assign, "job", job.Key, label,
                        "ผู้รับผิดชอบ", job.Owner, receiver.Name,
                        $"{why} — โยกงานของ {fromName} ({described})");

                    job.Owner = receiver.Name;
                    job.OwnerId = receiver.Id;
                    job.Data = JobTransfer.Reassigned(job.Data, receiver.Name, receiver.Id);
                    job.UpdatedBy = actor.Signature;
                    job.UpdatedAt = now;
                }
                await db.SaveChangesAsync(token);
                await transaction!.CommitAsync(token);
                register.Invalidate();
            }

            var message = moving.Count == 0
                ? $"ไม่มีงานของ {fromName} ที่จะย้าย ({described})"
                : apply
                    ? $"โยก {moving.Count} งานของ {fromName} ให้ {receiver.Name} แล้ว"
                    : $"พบ {moving.Count} งานของ {fromName} ที่จะย้ายให้ {receiver.Name}";

            return new Outcome(true, message, fromName, receiver.Name, described,
                held.Count, moving.Count, undated, closedOut, sample, apply && moving.Count > 0,
                apply ? moving.Select(job => job.Key).ToList() : []);
        });
    }
}
